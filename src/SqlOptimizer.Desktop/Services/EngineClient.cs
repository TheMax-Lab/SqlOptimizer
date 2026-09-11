using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace SqlOptimizer.Desktop.Services;

using SqlOptimizer.Desktop.Protocol;

/// <summary>
/// Client of the local .NET 8 engine host. Owns the engine process and the
/// stdin/stdout JSON protocol: startup (hello handshake + configure),
/// correlated requests with timeouts, best-effort cancellation, engine-loss
/// detection with automatic restart on the next operation, and a bounded
/// stderr log for diagnostics. The UI never touches the process streams
/// directly.
/// </summary>
public sealed class EngineClient : IDisposable
{
    private const int MaxLogLines = 500;
    private const int HelloTimeoutMs = 20000;

    private readonly object _writeLock = new object();
    private readonly object _logGate = new object();
    private readonly Queue<string> _log = new Queue<string>();
    private readonly ConcurrentDictionary<long, TaskCompletionSource<EngineResponse>> _pending =
        new ConcurrentDictionary<long, TaskCompletionSource<EngineResponse>>();
    private readonly Func<DesktopConfiguration> _configurationFactory;
    private readonly Func<(string FileName, string Arguments)?> _engineLocator;

    // Serializes the ensure state machine: concurrent callers share the single
    // in-flight startup instead of starting/stopping additional engine processes.
    private readonly object _ensureLock = new object();

    // Guards the _process/_stdin pair so a stop and a start never interleave them.
    private readonly object _lifecycleLock = new object();

    // Processes with a stop in flight: each process is stopped and disposed exactly once.
    private readonly ConcurrentDictionary<Process, byte> _stoppingProcesses =
        new ConcurrentDictionary<Process, byte>();

    // The single in-flight ensure/startup, shared by all concurrent callers.
    private Task? _ensureTask;

    private Process? _process;
    private StreamWriter? _stdin;
    private long _nextId;
    private long _activeRequestId;
    private volatile bool _configured;
    private volatile bool _disposed;

    /// <summary>Creates the client. The engine is started lazily on first use.</summary>
    /// <param name="configurationFactory">Factory for the current desktop configuration.</param>
    /// <param name="engineLocator">Function that resolves the engine process start.</param>
    public EngineClient(Func<DesktopConfiguration> configurationFactory, Func<(string FileName, string Arguments)?> engineLocator)
    {
        _configurationFactory = configurationFactory ?? throw new ArgumentNullException(nameof(configurationFactory));
        _engineLocator = engineLocator ?? throw new ArgumentNullException(nameof(engineLocator));
    }

    /// <summary>Raised (on a background thread) when the engine process ends unexpectedly.</summary>
    public event EventHandler<string>? EngineLost;

    /// <summary>Raised (on a background thread) when the engine readiness changes.</summary>
    public event EventHandler<string>? StatusChanged;

    /// <summary>True while the engine process is alive.</summary>
    public bool IsRunning
    {
        get
        {
            var p = _process;
            if (p is null)
            {
                return false;
            }

            try
            {
                return !p.HasExited;
            }
            catch (Win32Exception)
            {
                // Handle released by a concurrent stop: treat as not running.
                return false;
            }
        }
    }

    /// <summary>Pid of the engine process (null when stopped).</summary>
    public int? Pid
    {
        get
        {
            var p = _process;
            if (p is null)
            {
                return null;
            }

            try
            {
                return p.HasExited ? null : p.Id;
            }
            catch (Win32Exception)
            {
                // Handle released by a concurrent stop: treat as not running.
                return null;
            }
        }
    }

    /// <summary>Correlation id of the request currently in flight (null when none).</summary>
    public long? ActiveRequestId
    {
        get
        {
            var id = Interlocked.Read(ref _activeRequestId);
            return id == 0 ? null : id;
        }
    }

    /// <summary>A bounded, most-recent snapshot of the engine stderr log.</summary>
    public IReadOnlyList<string> GetEngineLog()
    {
        lock (_logGate)
        {
            return _log.ToArray();
        }
    }

    /// <summary>
    /// Ensures a configured engine is running: starts the process when needed,
    /// waits for the hello line and sends the configure operation. Concurrent
    /// callers share the single in-flight startup; they never start or stop
    /// additional engine processes.
    /// </summary>
    public Task EnsureEngineAsync()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(EngineClient));
        }

        // Fast path: a healthy, configured engine is already running.
        if (IsRunning && _configured)
        {
            return Task.CompletedTask;
        }

        lock (_ensureLock)
        {
            if (IsRunning && _configured)
            {
                return Task.CompletedTask;
            }

            // Share the in-flight startup instead of running a second one.
            _ensureTask ??= RunEnsureCoreAsync();
            return _ensureTask;
        }
    }

    /// <summary>Stops any dead engine, starts a fresh one and configures it.</summary>
    private async Task RunEnsureCoreAsync()
    {
        try
        {
            // A dead or never-configured engine must be replaced cleanly.
            await StopEngineAsync();
            await StartEngineAsync();

            var configureResponse = await ConfigureEngineAsync();
            if (!configureResponse.Ok)
            {
                await StopEngineAsync();
                throw new EngineNotAvailableException(
                    "The engine failed to start: " +
                    (configureResponse.Error?.Message ?? "the engine rejected the configuration."));
            }

            _configured = true;
        }
        finally
        {
            lock (_ensureLock)
            {
                _ensureTask = null;
            }
        }
    }

    /// <summary>Starts the engine process and waits for the hello line.</summary>
    private async Task StartEngineAsync()
    {
        var engine = _engineLocator();
        if (engine is null)
        {
            throw new EngineNotAvailableException(
                "The local engine host (SqlOptimizer.Desktop.Engine) could not be found. " +
                "Build the solution first, or set 'Desktop.EnginePath' in App.config to the engine executable.");
        }

        var psi = new ProcessStartInfo
        {
            FileName = engine.Value.FileName,
            Arguments = engine.Value.Arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        var process = new Process { StartInfo = psi };
        if (!process.Start())
        {
            throw new EngineNotAvailableException("The engine process could not be started.");
        }

        // Per-start handshake: only the stdout of THIS process can complete it,
        // so a hello from another engine instance can never satisfy this start.
        var helloTcs = new TaskCompletionSource<EngineResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_lifecycleLock)
        {
            _process = process;
            _stdin = process.StandardInput;
        }
        _configured = false;
        LogLine($"engine started (pid {process.Id})");
        RaiseStatus($"Engine starting (pid {process.Id})...");

        // Read pumps (background threads; the process exit ends both loops).
        _ = Task.Run(() => PumpLines(process.StandardOutput, process, isStdout: true, helloTcs: helloTcs));
        _ = Task.Run(() => PumpLines(process.StandardError, process, isStdout: false, helloTcs: helloTcs));

        try
        {
            var helloTask = helloTcs.Task;
            var completed = await Task.WhenAny(helloTask, Task.Delay(HelloTimeoutMs));
            if (completed != helloTask)
            {
                await StopEngineAsync();
                throw new EngineNotAvailableException("The engine did not respond during startup.");
            }

            var hello = await helloTask;
            if (hello.Type != "hello")
            {
                await StopEngineAsync();
                throw new EngineNotAvailableException("The engine did not complete its startup handshake.");
            }

            LogLine("engine handshake complete");
            RaiseStatus($"Engine running (pid {process.Id})");
        }
        catch (EngineNotAvailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await StopEngineAsync();
            throw new EngineNotAvailableException("The engine failed to start: " + ex.Message);
        }
    }

    /// <summary>
    /// Sends the configure operation with the current desktop configuration.
    /// The engine is guaranteed to be running (just started by the ensure
    /// path), so the operation is sent directly without re-entering
    /// EnsureEngineAsync, which would otherwise restart the not-yet-configured
    /// engine.
    /// </summary>
    private Task<EngineResponse> ConfigureEngineAsync()
    {
        var configuration = _configurationFactory();
        var payload = new
        {
            database = new
            {
                connectionString = configuration.Database.ConnectionString,
                enabled = configuration.Database.Enabled,
                commandTimeoutSeconds = configuration.Database.CommandTimeoutSeconds,
                connectionTimeoutSeconds = configuration.Database.ConnectionTimeoutSeconds,
                maxRowsForComparison = configuration.Database.MaxRowsForComparison,
                maxResultCells = configuration.Database.MaxResultCells,
                applicationName = configuration.Database.ApplicationName,
                metadataCacheTtlSeconds = configuration.Database.MetadataCacheTtlSeconds,
                metadataCacheMaxEntries = configuration.Database.MetadataCacheMaxEntries
            },
            llm = new
            {
                provider = configuration.Llm.Provider,
                model = configuration.Llm.Model,
                apiKey = configuration.Llm.ApiKey,
                endpoint = configuration.Llm.Endpoint,
                temperature = configuration.Llm.Temperature,
                timeoutSeconds = configuration.Llm.TimeoutSeconds,
                maxPromptChars = configuration.Llm.MaxPromptChars,
                maxCompletionTokens = configuration.Llm.MaxCompletionTokens
            },
            pipeline = new
            {
                maxSqlLength = 200000,
                maxCandidates = 3,
                enableRuntimeValidation = false,
                logSql = false
            }
        };

        return SendRequestAsync(Operations.Configure, payload, TimeSpan.FromSeconds(30));
    }

    /// <summary>Reads lines from one process stream until it closes.</summary>
    private void PumpLines(StreamReader reader, Process process, bool isStdout, TaskCompletionSource<EngineResponse> helloTcs)
    {
        string? line;
        try
        {
            while ((line = reader.ReadLine()) != null)
            {
                if (isStdout)
                {
                    DispatchProtocolLine(line, helloTcs);
                }
                else if (!string.IsNullOrWhiteSpace(line))
                {
                    LogLine(line);
                }
            }
        }
        catch (ObjectDisposedException)
        {
            // Stream closed during shutdown: expected.
        }
        catch (IOException)
        {
            // Stream broken during shutdown: expected.
        }

        if (isStdout && !_disposed)
        {
            // Wait (bounded) only for THIS process to end; a released or already
            // exited handle is not an error.
            try
            {
                if (!process.HasExited)
                {
                    process.WaitForExit(5000);
                }
            }
            catch (Win32Exception)
            {
                // Handle already released by a graceful stop: the process is gone.
            }
            catch (InvalidOperationException)
            {
                // Process already exited.
            }

            // Report the loss only while this process is still the current one;
            // a graceful stop has already replaced it.
            if (!_disposed && ReferenceEquals(_process, process))
            {
                FailPending(EngineErrors.EngineLost, "The engine process terminated unexpectedly.");
                RaiseStatus("Engine stopped unexpectedly.");
                EngineLost?.Invoke(this, "The engine process terminated unexpectedly.");
            }
        }
    }


    /// <summary>
    /// Sends one correlated request to the engine and waits for its response,
    /// applying the given timeout (the engine-side timeout plus client slack).
    /// </summary>
    /// <param name="operation">Operation name.</param>
    /// <param name="payload">Operation payload (serialized as-is).</param>
    /// <param name="timeout">Operation timeout (sent to the engine).</param>
    /// <param name="cancellationToken">Optional UI cancellation token.</param>
    public async Task<EngineResponse> RequestAsync(string operation, object? payload, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        await EnsureEngineAsync();
        return await SendRequestAsync(operation, payload, timeout, cancellationToken);
    }

    /// <summary>
    /// Sends one correlated request to a running, configured engine and waits
    /// for its response, applying the given timeout (the engine-side timeout
    /// plus client slack).
    /// </summary>
    /// <param name="operation">Operation name.</param>
    /// <param name="payload">Operation payload (serialized as-is).</param>
    /// <param name="timeout">Operation timeout (sent to the engine).</param>
    /// <param name="cancellationToken">Optional UI cancellation token.</param>
    private async Task<EngineResponse> SendRequestAsync(string operation, object? payload, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<EngineResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        Interlocked.Exchange(ref _activeRequestId, id);

        try
        {
            var line = JsonSerializer.Serialize(
                new
                {
                    v = 1,
                    id,
                    op = operation,
                    timeoutMs = (int)timeout.TotalMilliseconds,
                    payload
                },
                ClientJson.Options);
            WriteLine(line);

            // Client-side deadline: engine timeout + generous slack (the engine
            // itself times the operation and answers with REQUEST_TIMEOUT).
            var deadline = Task.Delay(timeout.Add(TimeSpan.FromSeconds(15)), cancellationToken);
            var winner = await Task.WhenAny(tcs.Task, deadline);
            if (winner == tcs.Task)
            {
                return await tcs.Task;
            }

            _pending.TryRemove(id, out _);
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            // Best-effort remote cancel, then surface the local timeout.
            try
            {
                var cancelLine = JsonSerializer.Serialize(new { v = 1, id = Interlocked.Increment(ref _nextId), op = Operations.Cancel, timeoutMs = 5000, payload = new { requestId = id } }, ClientJson.Options);
                WriteLine(cancelLine);
            }
            catch (ObjectDisposedException)
            {
                // Engine already gone: nothing to cancel.
            }

            throw new EngineRequestException(EngineErrors.RequestTimeout, "The operation timed out. The engine may still finish the work in the background.");
        }
        finally
        {
            Interlocked.CompareExchange(ref _activeRequestId, 0, id);
            _pending.TryRemove(id, out _);
        }
    }

    /// <summary>Dispatches one protocol line from the engine stdout.</summary>
    private void DispatchProtocolLine(string line, TaskCompletionSource<EngineResponse> helloTcs)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        EngineResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<EngineResponse>(line, ClientJson.Options);
        }
        catch (JsonException)
        {
            LogLine("protocol: non-JSON line ignored: " + Truncate(line, 200));
            return;
        }

        if (response is null)
        {
            return;
        }

        if (string.Equals(response.Type, "hello", StringComparison.Ordinal))
        {
            // Only the handshake of the start that owns this pump is completed.
            helloTcs.TrySetResult(response);
            return;
        }

        if (response.Id is { } id && _pending.TryRemove(id, out var tcs))
        {
            tcs.TrySetResult(response);
        }
        // Unknown correlation ids (for example after a restart) are dropped.
    }

    /// <summary>Completes all pending requests with an error (engine lost/shutdown).</summary>
    private void FailPending(string code, string message)
    {
        foreach (var pair in _pending)
        {
            if (_pending.TryRemove(pair.Key, out var tcs))
            {
                tcs.TrySetException(new EngineRequestException(code, message));
            }
        }
    }

    /// <summary>Writes one line to the engine stdin (serialized access).</summary>
    private void WriteLine(string line)
    {
        lock (_writeLock)
        {
            var stdin = _stdin ?? throw new InvalidOperationException("The engine is not running.");
            stdin.WriteLine(line);
            stdin.Flush();
        }
    }

    /// <summary>Cancels an in-flight request by its correlation id (best effort).</summary>
    /// <param name="requestId">Correlation id of the request.</param>
    public async Task CancelRequestAsync(long requestId)
    {
        if (!IsRunning)
        {
            return;
        }

        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<EngineResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        WriteLine(JsonSerializer.Serialize(new { v = 1, id, op = Operations.Cancel, timeoutMs = 5000, payload = new { requestId } }, ClientJson.Options));
        await Task.WhenAny(tcs.Task, Task.Delay(6000));
    }

    /// <summary>
    /// Best-effort cancel of the request currently in flight (if any), using
    /// the real engine correlation id tracked by <see cref="RequestAsync"/>.
    /// No-op when no request is in flight.
    /// </summary>
    public Task CancelActiveRequestAsync()
    {
        var id = ActiveRequestId;
        if (id is null)
        {
            return Task.CompletedTask;
        }

        return CancelRequestAsync(id.Value);
    }


    /// <summary>
    /// Stops the engine: graceful shutdown request, bounded wait, then kill.
    /// Re-entrancy safe: each process is stopped and disposed exactly once,
    /// and an already exited or already released handle can never escape as
    /// an error. Pending requests are failed so no caller hangs.
    /// </summary>
    public Task StopEngineAsync()
    {
        Process? process;
        StreamWriter? stdin;
        lock (_lifecycleLock)
        {
            process = _process;
            stdin = _stdin;
        }

        _configured = false;
        FailPending(EngineErrors.EngineLost, "The engine was stopped.");

        if (process is null)
        {
            return Task.CompletedTask;
        }

        // Claim the process so a concurrent stop finds it claimed and leaves;
        // only the claimer waits on it and disposes it.
        if (!_stoppingProcesses.TryAdd(process, 0))
        {
            return Task.CompletedTask;
        }

        try
        {
            // Graceful shutdown on this process's own stdin (best effort).
            try
            {
                if (stdin is not null)
                {
                    lock (_writeLock)
                    {
                        stdin.WriteLine(JsonSerializer.Serialize(new { v = 1, id = Interlocked.Increment(ref _nextId), op = Operations.Shutdown, timeoutMs = 5000 }, ClientJson.Options));
                        stdin.Flush();
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                // Already gone.
            }
            catch (IOException)
            {
                // Broken pipe: already gone.
            }

            try
            {
                if (!process.HasExited)
                {
                    if (!process.WaitForExit(5000))
                    {
                        process.Kill();
                        process.WaitForExit(2000);
                        LogLine("engine killed after shutdown grace period");
                    }
                    else
                    {
                        LogLine("engine stopped (exit code " + process.ExitCode + ")");
                    }
                }
            }
            catch (InvalidOperationException)
            {
                // Process already exited.
            }
            catch (Win32Exception)
            {
                // Handle already invalid: the engine is gone, which counts as a successful stop.
            }
        }
        finally
        {
            // Clear the shared state only if it still refers to THIS process, so a
            // stop that finishes late can never wipe a newer engine's streams.
            lock (_lifecycleLock)
            {
                if (ReferenceEquals(_process, process))
                {
                    _process = null;
                    _stdin = null;
                }
            }
            _stoppingProcesses.TryRemove(process, out _);
            process.Dispose();
            RaiseStatus("Engine stopped.");
        }

        return Task.CompletedTask;
    }

    /// <summary>Appends a line to the bounded engine log.</summary>
    private void LogLine(string message)
    {
        lock (_logGate)
        {
            _log.Enqueue(DateTime.Now.ToString("HH:mm:ss.fff") + " " + message);
            while (_log.Count > MaxLogLines)
            {
                _log.Dequeue();
            }
        }
    }

    /// <summary>Truncates a string for log display.</summary>
    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value.Substring(0, max) + "...";

    /// <summary>Raises the status event on a background thread.</summary>
    private void RaiseStatus(string status) => StatusChanged?.Invoke(this, status);

    /// <summary>Releases the process (graceful, bounded) and all client state.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            StopEngineAsync().GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // Best effort on dispose: never block the UI shutdown on the engine.
        }
    }

    /// <summary>Raised when the engine cannot be started or located.</summary>
    public sealed class EngineNotAvailableException : Exception
    {
        /// <summary>Creates the exception.</summary>
        /// <param name="message">Safe user readable message.</param>
        public EngineNotAvailableException(string message)
            : base(message)
        {
        }
    }

    /// <summary>Raised when a request fails (protocol error, timeout, engine lost).</summary>
    public sealed class EngineRequestException : Exception
    {
        /// <summary>Creates the exception.</summary>
        /// <param name="code">Stable machine readable error code.</param>
        /// <param name="message">Safe user readable message.</param>
        public EngineRequestException(string code, string message)
            : base(message)
        {
            Code = code;
        }

        /// <summary>Stable machine readable error code.</summary>
        public string Code { get; }
    }
}


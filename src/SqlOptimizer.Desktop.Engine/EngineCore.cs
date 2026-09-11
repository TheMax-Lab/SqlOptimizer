using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SqlOptimizer.Application.Abstractions;
using SqlOptimizer.Application.DTOs;
using SqlOptimizer.Domain.Common;
using SqlOptimizer.Desktop.Engine.Protocol;
using SqlOptimizer.Desktop.Engine.Support;

namespace SqlOptimizer.Desktop.Engine;

/// <summary>
/// Core of the desktop engine host (partial): the line-delimited JSON
/// request loop over stdin/stdout. Concurrency is bounded, every request
/// carries a correlation id and a timeout, in-flight requests can be
/// canceled by id, and shutdown drains in-flight work before exiting.
/// stdout carries only protocol lines; diagnostics go to stderr.
/// </summary>
public sealed partial class EngineCore
{
    private readonly object _stdoutLock = new();
    private readonly StreamWriter _stdout;
    private readonly SemaphoreSlim _pipelineGate = new(ProtocolLimits.MaxConcurrentRequests, ProtocolLimits.MaxConcurrentRequests);
    private readonly SemaphoreSlim _bootstrapGate = new(1, 1);
    private readonly ConcurrentDictionary<long, CancellationTokenSource> _active = new();
    private readonly ConcurrentDictionary<long, Task> _activeTasks = new();

    private ServiceProvider? _provider;
    private EngineConfiguration? _configuration;
    private EngineDatabaseProbe? _probe;
    private volatile bool _shutdownRequested;

    /// <summary>Creates the engine core. stdout becomes the protocol channel.</summary>
    public EngineCore()
    {
        _stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false))
        {
            AutoFlush = true
        };
    }

    /// <summary>True once the client requested a graceful shutdown.</summary>
    public bool ShutdownRequested => _shutdownRequested;

    /// <summary>Sends the hello line with engine identity and capabilities.</summary>
    public Task SendHelloAsync()
    {
        var hello = new EngineHello(
            1,
            "hello",
            new EngineInfo("SqlOptimizer.Desktop.Engine", EngineVersion, RuntimeInformation.FrameworkDescription),
            new EngineCapabilities(
                "SqlServer",
                [
                    OperationNames.Configure,
                    OperationNames.Ping,
                    OperationNames.Health,
                    OperationNames.Analyze,
                    OperationNames.Optimize,
                    OperationNames.Validate,
                    OperationNames.Cancel,
                    OperationNames.Shutdown
                ]));
        return WriteLineAsync(JsonSerializer.Serialize(hello, EngineJson.Options));
    }

    /// <summary>
    /// Dispatches one protocol line. Pipeline operations run concurrently
    /// (bounded); control operations run inline. Exceptions never escape:
    /// each request always gets exactly one response line.
    /// </summary>
    /// <param name="line">A non-empty protocol line.</param>
    public async Task HandleLineAsync(string line)
    {
        EngineRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<EngineRequest>(line, EngineJson.Options);
        }
        catch (JsonException)
        {
            await WriteErrorAsync(0, EngineErrors.ProtocolError, "The request line is not valid JSON.");
            return;
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Op))
        {
            await WriteErrorAsync(0, EngineErrors.ProtocolError, "The request is missing the operation name.");
            return;
        }

        var op = request.Op.Trim();
        switch (op)
        {
            case OperationNames.Configure:
                Track(request.Id, HandleConfigureAsync(request));
                break;

            case OperationNames.Ping:
                await WriteOkAsync(request.Id, new PingPayload(true));
                break;

            case OperationNames.Health:
                Track(request.Id, HandleHealthAsync(request));
                break;

            case OperationNames.Analyze:
            case OperationNames.Optimize:
            case OperationNames.Validate:
                Track(request.Id, RunPipelineAsync(request, op));
                break;

            case OperationNames.Cancel:
                Track(request.Id, HandleCancelAsync(request));
                break;

            case OperationNames.Shutdown:
                await WriteOkAsync(request.Id, new ShutdownResultPayload(true));
                _shutdownRequested = true;
                break;

            default:
                await WriteErrorAsync(request.Id, EngineErrors.ProtocolError, $"Unknown operation '{op}'.");
                break;
        }
    }

    /// <summary>
    /// Graceful shutdown: drains in-flight operations (with a grace period),
    /// disposes the service provider and closes the protocol channel.
    /// </summary>
    public async Task ShutdownAsync()
    {
        if (!_activeTasks.IsEmpty)
        {
            try
            {
                await Task.WhenAll(_activeTasks.Values).WaitAsync(ProtocolLimits.ShutdownGrace);
            }
            catch (TimeoutException)
            {
                foreach (var cts in _active.Values)
                {
                    cts.Cancel();
                }
            }
        }

        _provider?.Dispose();
        _provider = null;
        await _stdout.DisposeAsync();
    }

    /// <summary>Tracks a fire-and-forget request task (removal + fault logging).</summary>
    private void Track(long id, Task task)
    {
        _activeTasks[id] = task;
        _ = task.ContinueWith(
            t =>
            {
                _activeTasks.TryRemove(id, out _);
                if (t.Exception is not null)
                {
                    LogException(t.Exception.GetBaseException());
                }
            },
            TaskContinuationOptions.OnlyOnFaulted);
    }
}


using Microsoft.Extensions.Logging;

namespace SqlOptimizer.Desktop.Engine.Support;

/// <summary>
/// Minimal <see cref="ILogger{TCategoryName}"/> implementation for the
/// desktop engine host: existing infrastructure services require a logger
/// (the ASP.NET host normally supplies one). Output goes to stderr only, so
/// stdout stays a clean protocol channel. Diagnostic lines never echo SQL
/// text (the pipeline logs SQL only when LogSql is explicitly enabled) and
/// any configured secret (connection string, API key) is redacted before
/// writing.
/// </summary>
public sealed class StderrLogger<T> : ILogger<T>
{
    private static readonly object Gate = new();

    /// <inheritdoc />
    public IDisposable BeginScope<TState>(TState state) where TState : notnull
    {
        return NullScope.Instance;
    }

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = SecretRedactor.Redact(formatter(state, exception));
        var suffix = exception is null ? string.Empty : $" ({exception.GetType().Name}: {SecretRedactor.Redact(exception.Message)})";

        lock (Gate)
        {
            Console.Error.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{logLevel.ToString().ToUpperInvariant()}] {message}{suffix}");
        }
    }

    /// <summary>Do-not-dispose scope handle (scopes are not tracked).</summary>
    private sealed class NullScope : IDisposable
    {
        /// <summary>Shared instance.</summary>
        public static readonly NullScope Instance = new();

        /// <inheritdoc />
        public void Dispose()
        {
        }
    }
}

using System.Text.Json;
using SqlOptimizer.Desktop.Engine.Protocol;
using SqlOptimizer.Desktop.Engine.Support;

namespace SqlOptimizer.Desktop.Engine;

/// <summary>
/// Control operations of the engine core (partial): configure, health and
/// cancel. Configuration is the single point where the existing pipeline is
/// composed (see <see cref="EngineBootstrap"/>); in-flight operations are
/// drained before a re-configure.
/// </summary>
public sealed partial class EngineCore
{
    /// <summary>
    /// Handles the <c>configure</c> operation: maps the payload onto the
    /// existing option records, (re)builds the service provider and registers
    /// the secrets for redaction.
    /// </summary>
    private async Task HandleConfigureAsync(EngineRequest request)
    {
        await _bootstrapGate.WaitAsync();
        try
        {
            ConfigurePayload? payload = null;
            if (request.Payload is { } element)
            {
                try
                {
                    payload = element.Deserialize<ConfigurePayload>(EngineJson.Options);
                }
                catch (JsonException)
                {
                    await WriteErrorAsync(request.Id, EngineErrors.ProtocolError, "The configure payload is not valid.");
                    return;
                }
            }

            if (!_activeTasks.IsEmpty)
            {
                try
                {
                    await Task.WhenAll(_activeTasks.Values).WaitAsync(ProtocolLimits.ConfigureDrainGrace);
                }
                catch (TimeoutException)
                {
                    Log("Re-configure: in-flight operations did not finish in time; canceling them.");
                    foreach (var cts in _active.Values)
                    {
                        cts.Cancel();
                    }
                }
            }

            _provider?.Dispose();
            var configuration = EngineConfiguration.FromPayload(payload);
            SecretRedactor.SetSecrets(configuration.Secrets.ToArray());
            _provider = EngineBootstrap.Build(configuration);
            _configuration = configuration;
            _probe = new EngineDatabaseProbe(configuration.Database);

            Log("Engine configured " +
                $"(database: {(configuration.Database.Enabled ? "enabled" : "disabled")}, " +
                $"LLM: {LlmSummary(configuration.Llm)}).");
            await WriteOkAsync(request.Id, new ConfiguredPayload(true));
        }
        catch (Exception ex)
        {
            Log($"Configure failed: {ex.Message}");
            await WriteErrorAsync(request.Id, EngineErrors.Unknown, "The engine could not be configured.");
        }
        finally
        {
            _bootstrapGate.Release();
        }
    }

    /// <summary>Handles the <c>health</c> operation (database ping + readiness state).</summary>
    private async Task HandleHealthAsync(EngineRequest request)
    {
        var configuration = _configuration;
        var probe = _probe;
        if (_provider is null || configuration is null || probe is null)
        {
            await WriteErrorAsync(request.Id, EngineErrors.NotConfigured, NotConfiguredMessage);
            return;
        }

        using var cts = new CancellationTokenSource(ResolveTimeout(request));
        try
        {
            var configured = probe.IsConfigured;
            var reachable = configured && await probe.PingAsync(cts.Token);
            var payload = new HealthResponsePayload(
                !configured || reachable ? "Healthy" : "Degraded",
                DateTimeOffset.UtcNow,
                new HealthDatabasePayload(configured, reachable, configured ? configuration.Database.MetadataCacheTtlSeconds : null),
                new HealthLlmPayload(configuration.Llm.Provider, IsLlmConfigured(configuration.Llm), configuration.Llm.Model));
            await WriteOkAsync(request.Id, payload);
        }
        catch (OperationCanceledException)
        {
            await WriteErrorAsync(request.Id, EngineErrors.RequestTimeout, "The health check timed out.");
        }
        catch (Exception)
        {
            await WriteErrorAsync(request.Id, EngineErrors.Unknown, "The health check failed.");
        }
    }

    /// <summary>Handles the <c>cancel</c> operation (best-effort cancel by request id).</summary>
    private async Task HandleCancelAsync(EngineRequest request)
    {
        var cancelled = false;
        if (ReadPayload<CancelPayload>(request) is { } payload && _active.TryGetValue(payload.RequestId, out var cts))
        {
            cts.Cancel();
            cancelled = true;
        }

        await WriteOkAsync(request.Id, new CancelResultPayload(cancelled));
    }
}

using Microsoft.Extensions.DependencyInjection;
using SqlOptimizer.Application.Abstractions;
using SqlOptimizer.Application.DTOs;
using SqlOptimizer.Domain.Common;
using SqlOptimizer.Domain.Optimization;
using SqlOptimizer.Desktop.Engine.Protocol;

namespace SqlOptimizer.Desktop.Engine;

/// <summary>
/// Pipeline operations of the engine core (partial): analyze, optimize and
/// validate. Each operation runs through the existing scoped services with a
/// per-request timeout and external cancellation; exactly one response is
/// always produced for each request.
/// </summary>
public sealed partial class EngineCore
{
    /// <summary>Runs one pipeline operation (analyze/optimize/validate).</summary>
    private async Task RunPipelineAsync(EngineRequest request, string op)
    {
        if (_provider is null)
        {
            await WriteErrorAsync(request.Id, EngineErrors.NotConfigured, NotConfiguredMessage);
            return;
        }

        if (!await _pipelineGate.WaitAsync(TimeSpan.FromSeconds(30)))
        {
            await WriteErrorAsync(request.Id, EngineErrors.EngineBusy, "The engine is busy; please try again shortly.");
            return;
        }

        var timeout = ResolveTimeout(request);
        var timeoutCts = new CancellationTokenSource(timeout);
        var externalCts = new CancellationTokenSource();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, externalCts.Token);
        _active[request.Id] = externalCts;

        try
        {
            using var scope = _provider.CreateScope();
            var services = scope.ServiceProvider;
            object payload;

            switch (op)
            {
                case OperationNames.Analyze:
                {
                    var p = ReadPayload<AnalyzePayload>(request);
                    if (p is null)
                    {
                        await WriteErrorAsync(request.Id, EngineErrors.ProtocolError, "The analyze payload is missing or invalid.");
                        return;
                    }

                    var analyzer = services.GetRequiredService<ISqlAnalyzer>();
                    var analysis = await analyzer.AnalyzeAsync(
                        new SqlAnalysisRequest
                        {
                            Sql = p.Sql,
                            Dialect = p.Dialect ?? SqlDialect.SqlServer,
                            IncludeAst = p.IncludeAst
                        },
                        linkedCts.Token);

                    payload = new AnalyzeResponsePayload(
                        analysis.Sql,
                        analysis.Dialect,
                        analysis.ComplexityScore,
                        analysis.PerformanceScore,
                        analysis.Findings,
                        analysis.Statistics,
                        p.IncludeAst ? analysis.Ast : null);
                    break;
                }
                case OperationNames.Optimize:
                {
                    var p = ReadPayload<OptimizePayload>(request);
                    if (p is null)
                    {
                        await WriteErrorAsync(request.Id, EngineErrors.ProtocolError, "The optimize payload is missing or invalid.");
                        return;
                    }

                    var optimizer = services.GetRequiredService<ISqlOptimizer>();
                    var result = await optimizer.OptimizeAsync(
                        new SqlOptimizationRequest
                        {
                            Sql = p.Sql,
                            Dialect = p.Dialect ?? SqlDialect.SqlServer,
                            Options = p.Options is { } o
                                ? new OptimizationOptions
                                {
                                    UseLlm = o.UseLlm,
                                    GenerateIndexes = o.GenerateIndexes,
                                    ValidateSemantics = o.ValidateSemantics,
                                    GeneratePrompt = o.GeneratePrompt,
                                    MaxCandidates = Math.Clamp(o.MaxCandidates, 1, 10),
                                    Strategy = o.Strategy ?? OptimizationStrategy.Balanced
                                }
                                : new OptimizationOptions()
                        },
                        linkedCts.Token);

                    // The full result (analysis, plan, candidates, indexes,
                    // prompt, top-candidate validation, limitations) mirrors
                    // exactly what the HTTP API returns.
                    payload = result;
                    break;
                }

                case OperationNames.Validate:
                {
                    var p = ReadPayload<ValidatePayload>(request);
                    if (p is null)
                    {
                        await WriteErrorAsync(request.Id, EngineErrors.ProtocolError, "The validate payload is missing or invalid.");
                        return;
                    }

                    var validator = services.GetRequiredService<ISqlValidator>();
                    var result = await validator.ValidateAsync(
                        new SqlValidationRequest(
                            p.OriginalSql ?? string.Empty,
                            p.CandidateSql ?? string.Empty,
                            p.CompareResults,
                            p.MaxRowsForComparison is { } rows && rows > 0 ? Math.Clamp(rows, 1, 100_000) : 1_000,
                            p.Dialect ?? SqlDialect.SqlServer,
                            null),
                        linkedCts.Token);

                    payload = result;
                    break;
                }

                default:
                    await WriteErrorAsync(request.Id, EngineErrors.ProtocolError, $"Operation '{op}' cannot be executed here.");
                    return;
            }

            Log($"{op} completed (id={request.Id}).");
            await WriteOkAsync(request.Id, payload);
        }
        catch (OperationCanceledException) when (linkedCts.IsCancellationRequested)
        {
            if (externalCts.IsCancellationRequested)
            {
                await WriteErrorAsync(request.Id, EngineErrors.Canceled, "The operation was canceled by the client.");
            }
            else
            {
                await WriteErrorAsync(request.Id, EngineErrors.RequestTimeout, $"The operation timed out after {timeout.TotalSeconds:0} seconds.");
            }
        }
        catch (Exception ex)
        {
            var (code, message) = MapException(ex);
            Log($"{op} failed (id={request.Id}): {code}");
            LogException(ex);
            await WriteErrorAsync(request.Id, code, message);
        }
        finally
        {
            _active.TryRemove(request.Id, out _);
            timeoutCts.Dispose();
            externalCts.Dispose();
            _pipelineGate.Release();
        }
    }
}


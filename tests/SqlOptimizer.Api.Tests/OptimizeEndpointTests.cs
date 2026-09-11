using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SqlOptimizer.Api.Tests.TestSupport;
using SqlOptimizer.Application.Abstractions;
using SqlOptimizer.Application.DTOs;
using SqlOptimizer.Application.Options;
using SqlOptimizer.Application.Services;
using SqlOptimizer.Application.Services.Validation;
using SqlOptimizer.Domain.Common;
using SqlOptimizer.Domain.Parsing;
using Xunit;

namespace SqlOptimizer.Api.Tests;

/// <summary>
/// M7 offline tests for POST /api/v1/optimize: the endpoint must delegate to
/// the Application optimizer (no business logic in the API layer) and return
/// the full optimization result with per-candidate validation statuses.
/// Inconclusive candidates are never reinterpreted as success.
/// </summary>
public sealed class OptimizeEndpointTests
{
    private const string TestSql = "SELECT * FROM dbo.Customers ORDER BY Id";

    [Fact]
    public async Task ValidSql_Returns200WithCandidatesRankingAndValidation()
    {
        await using var factory = ApiFactory.Create();
        using var client = factory.CreateDefaultClient();

        // Schema is supplied so the deterministic generator can emit the
        // star-expansion candidate (its only safe-by-construction rewrite).
        var response = await client.PostAsJsonAsync("/api/v1/optimize", new
        {
            sql = TestSql,
            options = new { useLlm = false, generatePrompt = false, validateSemantics = true },
            schema = new
            {
                tables = new object[]
                {
                    new
                    {
                        schema = "dbo",
                        name = "Customers",
                        estimatedRowCount = 4,
                        columns = new object[]
                        {
                            new { name = "Id", dataType = "INT", nullable = false, primaryKey = true },
                            new { name = "Name", dataType = "NVARCHAR(100)", nullable = false, primaryKey = false },
                            new { name = "City", dataType = "NVARCHAR(50)", nullable = true, primaryKey = false }
                        },
                        indexes = new object[0]
                    }
                }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        root.GetProperty("analysis").GetProperty("sql").GetString().Should().Be(TestSql);
        root.GetProperty("optimizationPlan").ValueKind.Should().Be(JsonValueKind.Object);
        root.GetProperty("candidates").ValueKind.Should().Be(JsonValueKind.Array);
        root.GetProperty("candidates").GetArrayLength().Should().BeGreaterThan(0, "the deterministic pipeline produces at least one candidate for SELECT *");

        // Ranking is 1-based and every candidate carries its own validation.
        var first = root.GetProperty("candidates").EnumerateArray().First();
        first.GetProperty("rank").GetInt32().Should().Be(1);
        first.GetProperty("validation").ValueKind.Should().Be(JsonValueKind.Object);
        first.GetProperty("validation").GetProperty("status").GetString().Should()
            .BeOneOf("Passed", "Failed", "Inconclusive", "NotExecuted", "NotRequested");
    }

    [Fact]
    public async Task UnparseableSql_Returns400WithSqlParseError()
    {
        await using var factory = ApiFactory.Create();
        using var client = factory.CreateDefaultClient();

        var response = await client.PostAsJsonAsync("/api/v1/optimize", new { sql = "SELEC 1 FRO" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("code").GetString().Should().Be("SQL_PARSE_ERROR");
    }

    [Fact]
    public async Task SqlExceedingMaxLength_Returns400WithSqlTooLong_AndDoesNotInvokeOptimizer()
    {
        var captured = new DelegatingSqlOptimizer?[1];
        await using var factory = WithOptimizerProbe(captured, new Dictionary<string, string>
        {
            ["Api__MaxSqlLengthChars"] = "64"
        });
        using var client = factory.CreateDefaultClient();

        var response = await client.PostAsJsonAsync("/api/v1/optimize", new { sql = new string('x', 100) });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("code").GetString().Should().Be("SQL_TOO_LONG");

        (captured[0]?.CallCount ?? 0).Should().Be(0, "over-limit input must be rejected before the Application layer");
    }

    [Fact]
    public async Task RequestIsMappedToTheApplicationOptimizer()
    {
        var captured = new DelegatingSqlOptimizer?[1];
        await using var factory = WithOptimizerProbe(captured);
        using var client = factory.CreateDefaultClient();

        var response = await client.PostAsJsonAsync("/api/v1/optimize", new
        {
            sql = TestSql,
            options = new { useLlm = false, generatePrompt = false, validateSemantics = true, maxCandidates = 2 }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        captured[0].Should().NotBeNull("the endpoint must delegate to the Application optimizer");
        var probe = captured[0]!;
        probe.CallCount.Should().Be(1, "the endpoint must delegate to the Application optimizer exactly once");
        probe.LastRequest.Should().NotBeNull();
        var request = probe.LastRequest!;
        request.Sql.Should().Be(TestSql);
        request.Options.UseLlm.Should().BeFalse();
        request.Options.ValidateSemantics.Should().BeTrue();
        request.Options.MaxCandidates.Should().Be(2);
    }

    // ------------------------------------------------------------------
    // M11: input validation, explicit validation/LLM control, failure
    // mapping, and cancellation propagation for POST /api/v1/optimize.
    // ------------------------------------------------------------------

    [Fact]
    public async Task EmptySql_Returns400WithSqlInvalidInput_AndDoesNotInvokeOptimizer()
    {
        var captured = new DelegatingSqlOptimizer?[1];
        await using var factory = WithOptimizerProbe(captured);
        using var client = factory.CreateDefaultClient();

        var response = await client.PostAsJsonAsync("/api/v1/optimize", new { sql = "   " });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("code").GetString().Should().Be("SQL_INVALID_INPUT");

        (captured[0]?.CallCount ?? 0).Should().Be(0, "empty SQL must be rejected before the Application layer");
    }

    [Fact]
    public async Task MissingRequestBody_Returns400()
    {
        await using var factory = ApiFactory.Create();
        using var client = factory.CreateDefaultClient();

        var response = await client.PostAsync(
            "/api/v1/optimize",
            new StringContent(string.Empty, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "a missing JSON body is a client error, not a server error");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("code").GetString().Should().Be("INVALID_REQUEST_BODY");
    }

    [Fact]
    public async Task InvalidEnumValue_Returns400()
    {
        await using var factory = ApiFactory.Create();
        using var client = factory.CreateDefaultClient();

        var response = await client.PostAsJsonAsync("/api/v1/optimize", new
        {
            sql = TestSql,
            options = new { useLlm = false, strategy = "NotARealStrategy" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "unknown enum values must be rejected as a client error");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("code").GetString().Should().Be("INVALID_REQUEST_BODY");
    }

    [Fact]
    public async Task ValidateSemanticsFalse_ForwardsCompareResultsFalse_AndNeverClaimsRuntimeEquivalence()
    {
        var captured = new DelegatingSqlValidator?[1];
        await using var factory = WithValidatorProbe(captured);
        using var client = factory.CreateDefaultClient();

        // The schema lets the deterministic generator produce a candidate so
        // the mandatory validation stage actually runs for this request.
        var response = await client.PostAsJsonAsync("/api/v1/optimize", new
        {
            sql = TestSql,
            options = new { useLlm = false, generatePrompt = false, validateSemantics = false },
            schema = new
            {
                tables = new object[]
                {
                    new
                    {
                        schema = "dbo",
                        name = "Customers",
                        estimatedRowCount = 4,
                        columns = new object[]
                        {
                            new { name = "Id", dataType = "INT", nullable = false, primaryKey = true },
                            new { name = "Name", dataType = "NVARCHAR(100)", nullable = false, primaryKey = false },
                            new { name = "City", dataType = "NVARCHAR(50)", nullable = true, primaryKey = false }
                        },
                        indexes = new object[0]
                    }
                }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var probe = captured[0];
        probe.Should().NotBeNull("every candidate must pass the static validation stage");
        probe!.CallCount.Should().BeGreaterThan(0);
        probe.LastRequest!.CompareResults.Should().BeFalse(
            "ValidateSemantics=false must never request a runtime (executing) comparison");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        foreach (var candidate in doc.RootElement.GetProperty("candidates").EnumerateArray())
        {
            var validation = candidate.GetProperty("validation");
            validation.GetProperty("semanticallyEquivalent").GetBoolean().Should().BeFalse(
                "without a runtime comparison, semantic equivalence is never claimed");
            validation.GetProperty("improvementPercentage").ValueKind.Should()
                .Be(JsonValueKind.Null, "a performance claim requires proven equivalence");
        }
    }

    [Fact]
    public async Task UseLlmFalse_NeverInvokesLlmClient_AndStillReturnsDeterministicResult()
    {
        var captured = new RecordingLlmClient?[1];
        await using var factory = ApiFactory.Create(configure: builder => builder.ConfigureTestServices(services =>
            // Last registration wins: the probe stands in for the single
            // ILlmClient resolved by the LLM candidate generator.
            services.AddSingleton<ILlmClient>(captured[0] ??= new RecordingLlmClient())));
        using var client = factory.CreateDefaultClient();

        var response = await client.PostAsJsonAsync("/api/v1/optimize", new
        {
            sql = TestSql,
            options = new { useLlm = false, generatePrompt = false, validateSemantics = false }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the deterministic pipeline must work without any LLM");
        captured[0]!.CallCount.Should().Be(0, "UseLlm=false must never reach the LLM client");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("analysis").GetProperty("sql").GetString().Should().Be(TestSql);
    }

    [Fact]
    public async Task OptimizerFailure_Returns500InternalErrorWithoutLeakingDetails()
    {
        const string FakeConnection = "Server=SecretHost;Database=SecretDb;User Id=sa;Password=SuperSecret123";
        await using var factory = WithFailingOptimizer(
            new InvalidOperationException($"could not continue: connection '{FakeConnection}' is broken"));
        using var client = factory.CreateDefaultClient();

        var response = await client.PostAsJsonAsync("/api/v1/optimize", new { sql = TestSql });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("code").GetString().Should().Be("INTERNAL_ERROR");
        doc.RootElement.GetProperty("status").GetInt32().Should().Be(500);

        body.Should().NotContain("SecretHost");
        body.Should().NotContain("SuperSecret123");
        body.Should().NotContain("InvalidOperationException", "exception types are server-side details");
        body.Should().NotContain("at SqlOptimizer", "stack frames are server-side details");
    }

    [Fact]
    public async Task DatabaseFailure_Returns503WithDatabaseError()
    {
        await using var factory = WithFailingOptimizer(
            new SqlDatabaseException("the database server refused the connection"));
        using var client = factory.CreateDefaultClient();

        var response = await client.PostAsJsonAsync("/api/v1/optimize", new { sql = TestSql });

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("code").GetString().Should().Be("DATABASE_ERROR");
        body.Should().NotContain("refused the connection", "raw provider details must not be echoed");
    }

    [Fact]
    public async Task ClientCancellation_IsPropagatedToTheApplication_AndReportedAs408()
    {
        var probe = new CancellingSqlOptimizer();
        await using var factory = ApiFactory.Create(configure: builder => builder.ConfigureTestServices(services =>
            services.AddScoped<ISqlOptimizer>(_ => probe)));
        using var client = factory.CreateDefaultClient();

        using var cts = new CancellationTokenSource();
        var requestTask = client.PostAsJsonAsync("/api/v1/optimize", new { sql = TestSql }, cts.Token);

        // Synchronize: wait until the server-side handler has started before
        // cancelling, so the assertion is deterministic.
        await probe.WaitForStartAsync(TimeSpan.FromSeconds(10));
        cts.Cancel();

        HttpResponseMessage? response = null;
        OperationCanceledException? clientCancellation = null;
        try
        {
            response = await requestTask;
        }
        catch (OperationCanceledException oce)
        {
            clientCancellation = oce;
        }

        probe.ObservedCancellation.Should().BeTrue(
            "the HTTP cancellation token must reach the Application layer through context.RequestAborted");

        if (response is not null)
        {
            // The server observed the cancellation and completed the exchange
            // with the documented 408 problem.
            response.StatusCode.Should().Be(HttpStatusCode.RequestTimeout);
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            doc.RootElement.GetProperty("code").GetString().Should().Be("REQUEST_CANCELLED");
        }
        else
        {
            // The client aborted before the 408 could be delivered: the
            // cancellation was still applied end to end.
            clientCancellation.Should().NotBeNull();
        }
    }

    private static WebApplicationFactory<Program> WithFailingOptimizer(Exception exception) =>
        ApiFactory.Create(configure: builder => builder.ConfigureTestServices(services =>
            services.AddScoped<ISqlOptimizer>(_ => new ThrowingSqlOptimizer(exception))));

    private static WebApplicationFactory<Program> WithValidatorProbe(DelegatingSqlValidator?[] captured) =>
        ApiFactory.Create(configure: builder => builder.ConfigureTestServices(services =>
            // Same capture pattern as WithOptimizerProbe: the first scoped
            // instance is the one the pipeline used for the request.
            services.AddScoped<ISqlValidator>(sp => captured[0] ??= new DelegatingSqlValidator(
                new SqlValidator(
                    sp.GetRequiredService<ISqlParser>(),
                    sp.GetRequiredService<SqlOptimizerOptions>(),
                    sp.GetRequiredService<IDatabaseValidationProvider>(),
                    sp.GetService<IDatabaseMetadataProvider>())))));

    /// <summary>
    /// Optimizer double that blocks on the cancellation token: used to prove
    /// the HTTP token propagates into the Application layer and is observed.
    /// </summary>
    private sealed class CancellingSqlOptimizer : ISqlOptimizer
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>True once the token received by the pipeline was cancelled.</summary>
        public bool ObservedCancellation { get; private set; }

        /// <summary>Completes when the server-side handler has started.</summary>
        public Task WaitForStartAsync(TimeSpan timeout) => _started.Task.WaitAsync(timeout);

        /// <inheritdoc />
        public async Task<SqlOptimizationResult> OptimizeAsync(
            SqlOptimizationRequest request,
            CancellationToken cancellationToken = default)
        {
            _started.TrySetResult();
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                ObservedCancellation = true;
                throw;
            }

            throw new InvalidOperationException("The cancellation double must not complete the pipeline.");
        }
    }

    private static WebApplicationFactory<Program> WithOptimizerProbe(
        DelegatingSqlOptimizer?[] captured,
        IReadOnlyDictionary<string, string>? settings = null) =>
        ApiFactory.Create(settings, configure: builder => builder.ConfigureTestServices(services =>
            // The probe is scoped like the real service, but the first instance
            // created (by the request scope) is captured so the test can observe
            // the exact instance the endpoint used: resolving a fresh scope
            // would create a new instance with an empty counter.
            services.AddScoped<ISqlOptimizer>(sp => captured[0] ??= new DelegatingSqlOptimizer(
                new SqlOptimizationService(
                    sp.GetRequiredService<ISqlAnalyzer>(),
                    sp.GetRequiredService<IOptimizationPlanBuilder>(),
                    sp.GetRequiredService<IIndexAdvisor>(),
                    sp.GetRequiredService<IEnumerable<ISqlOptimizationCandidateGenerator>>(),
                    sp.GetRequiredService<ISqlValidator>(),
                    sp.GetRequiredService<CandidateRanker>(),
                    sp.GetRequiredService<IPromptGenerator>(),
                    sp.GetRequiredService<SqlOptimizerOptions>())))));

    [Fact]
    public async Task MalformedJsonBody_Returns400WithInvalidRequestBodyCode()
    {
        // M11 regression: a malformed JSON body is a request error (400
        // INVALID_REQUEST_BODY), never a server error (500), and the
        // response must not echo the JSON parser diagnostics.
        await using var factory = ApiFactory.Create();
        using var client = factory.CreateDefaultClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/optimize");
        request.Content = new StringContent("{ this is not valid json", System.Text.Encoding.UTF8, "application/json");
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.GetProperty("code").GetString().Should().Be("INVALID_REQUEST_BODY");
        root.GetProperty("requestId").GetString().Should().NotBeNullOrEmpty();
    }

}

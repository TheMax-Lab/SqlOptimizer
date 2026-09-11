using SqlOptimizer.Application.Abstractions;
using SqlOptimizer.Application.DTOs;
using SqlOptimizer.Application.Options;
using SqlOptimizer.Domain.AST;
using SqlOptimizer.Domain.Common;
using SqlOptimizer.Domain.Metadata;
using SqlOptimizer.Domain.Parsing;

namespace SqlOptimizer.Application.Services.Validation;

/// <summary>
/// Concrete <see cref="ISqlValidator"/>: candidate SQL → validation result,
/// in deterministic stages:
/// <list type="number">
/// <item>Stage A — syntax: the candidate must parse with the dialect parser.</item>
/// <item>Stage B — structure: the candidate must preserve the structural
/// contract of the original (tables, parameters, projection, DISTINCT, set
/// operations, joins, grouping, ordering).</item>
/// <item>Stage C — semantic risk: transformations that may change NULL
/// behavior, cardinality, ordering, aggregation, collation or three-valued
/// logic are flagged as risks.</item>
/// <item>Stage D — optional runtime comparison, only when the request asks
/// for it, the server switch allows it, and a database validation provider
/// is available.</item>
/// </list>
/// Status mapping for Stage D: a comparison that ran and proved equality is
/// the only path to <c>SemanticallyEquivalent</c>; a comparison that ran but
/// produced no evidence (for example an execution failure) is
/// <c>Inconclusive</c>; a requested comparison that could not be attempted at
/// all (disabled server-side, no provider, dialect mismatch) is
/// <c>NotExecuted</c>, never <c>Passed</c>. The validator never claims
/// semantic equivalence from parsing or from AST comparison, and
/// <c>ImprovementPercentage</c> is reported only when semantic equivalence
/// has been proven by the runtime comparison; every other status leaves it
/// unavailable.
/// </summary>
public sealed class SqlValidator : ISqlValidator
{
    /// <summary>Confidence assigned to a parse-failure conclusion.</summary>
    private const double ConfidenceParseFailure = 0.99;

    /// <summary>Confidence assigned to a structural (provable difference) failure.</summary>
    private const double ConfidenceStructuralFailure = 0.90;

    /// <summary>Confidence assigned to a runtime result mismatch.</summary>
    private const double ConfidenceRuntimeMismatch = 0.95;

    /// <summary>Confidence assigned to an inconclusive conclusion (equivalence undecidable).</summary>
    private const double ConfidenceInconclusive = 0.40;

    /// <summary>Confidence assigned to a structurally passed candidate (no runtime evidence).</summary>
    private const double ConfidencePassedStatic = 0.85;

    /// <summary>Confidence assigned to a passed candidate with runtime result equality.</summary>
    private const double ConfidencePassedRuntime = 0.95;

    /// <summary>
    /// Confidence assigned to a not-executed conclusion: high, because "the
    /// requested runtime comparison did not run" is a deterministic fact, not
    /// an inference about equivalence.
    /// </summary>
    private const double ConfidenceNotExecuted = 0.95;

    /// <summary>Safe limit on the distinct tables fetched from a live metadata source per validation.</summary>
    private const int MaxTablesForLiveMetadata = 16;

    private readonly ISqlParser _parser;
    private readonly SqlOptimizerOptions _options;
    private readonly IDatabaseValidationProvider _validationProvider;
    private readonly IDatabaseMetadataProvider? _metadataProvider;

    /// <summary>
    /// Creates a new validator.
    /// </summary>
    /// <param name="parser">The SQL parser (one dialect per implementation).</param>
    /// <param name="options">Global SqlOptimizer options (limits, runtime validation switch).</param>
    /// <param name="validationProvider">
    /// Database-backed result comparison provider; use
    /// <see cref="UnavailableDatabaseValidationProvider"/> when no database is configured.
    /// </param>
    /// <param name="metadataProvider">
    /// Optional live schema metadata source; when available and the request
    /// carries no (or incomplete) schema, the referenced tables are fetched
    /// so metadata-backed proofs (star expansion, COUNT(*) vs COUNT(column))
    /// can be applied. When null (the default) validation stays fully static.
    /// </param>
    public SqlValidator(
        ISqlParser parser,
        SqlOptimizerOptions options,
        IDatabaseValidationProvider validationProvider,
        IDatabaseMetadataProvider? metadataProvider = null)
    {
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _validationProvider = validationProvider ?? throw new ArgumentNullException(nameof(validationProvider));
        _metadataProvider = metadataProvider;
    }

    /// <inheritdoc />
    public async Task<ValidationResult> ValidateAsync(
        SqlValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.OriginalSql))
        {
            throw new SqlInvalidInputException("The original SQL text must not be empty.");
        }

        if (request.OriginalSql.Length > _options.MaxSqlLength)
        {
            throw new SqlInvalidInputException(
                $"The original SQL text is {request.OriginalSql.Length} characters long; the maximum is {_options.MaxSqlLength}.");
        }

        if (request.Dialect != _parser.Dialect)
        {
            throw new SqlUnsupportedDialectException(request.Dialect);
        }

        // Stage A — syntax. The candidate is untrusted input: a parse failure
        // is a validation outcome (Failed), not an exception.
        if (string.IsNullOrWhiteSpace(request.CandidateSql))
        {
            return new ValidationResult
            {
                Status = ValidationStatus.Failed,
                SyntaxValid = false,
                Errors = ["The candidate SQL is empty."],
                ValidationConfidence = ConfidenceParseFailure
            };
        }

        SelectStatement candidateAst;
        try
        {
            candidateAst = _parser.Parse(request.CandidateSql).Root;
        }
        catch (SqlParseException ex)
        {
            return new ValidationResult
            {
                Status = ValidationStatus.Failed,
                SyntaxValid = false,
                Errors = [ex.Message],
                ValidationConfidence = ConfidenceParseFailure
            };
        }
        catch (SqlInvalidInputException ex)
        {
            return new ValidationResult
            {
                Status = ValidationStatus.Failed,
                SyntaxValid = false,
                Errors = [ex.Message],
                ValidationConfidence = ConfidenceParseFailure
            };
        }

        // The original is trusted pipeline input: if it does not parse, that
        // is a caller error, not a candidate problem. The API layer maps this
        // to the SQL_PARSE_ERROR problem code.
        SelectStatement originalAst;
        try
        {
            originalAst = _parser.Parse(request.OriginalSql).Root;
        }
        catch (SqlParseException ex)
        {
            throw new SqlParseException($"The original SQL could not be parsed: {ex.Message}", ex);
        }

        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        // Stage B — structure, Stage C — semantic risk. The schema used for
        // the metadata-backed proofs is the request schema, enriched with
        // live metadata when a metadata provider is configured.
        var originalSnapshot = QueryStructureSnapshot.Build(originalAst);
        var candidateSnapshot = QueryStructureSnapshot.Build(candidateAst);

        var evidence = new List<string>();
        var limitations = new List<string>();
        var schema = await EnrichSchemaWithLiveMetadataAsync(
            originalSnapshot, candidateSnapshot, request.Schema, evidence, limitations, cancellationToken);

        var structure = SqlStructureComparer.Compare(originalSnapshot, candidateSnapshot, schema);
        var risks = structure.Risks
            .Concat(SqlSemanticRiskAnalyzer.Analyze(originalSnapshot, candidateSnapshot))
            .Distinct()
            .ToList();

        evidence.AddRange(structure.VerifiedFacts);
        bool? runtimeEqual = null;
        TimeSpan? originalDuration = null;
        TimeSpan? candidateDuration = null;
        bool runtimeAttempted = false;

        // Stage D — optional runtime comparison (explicitly requested,
        // enabled server-side, and backed by an available provider).
        if (request.CompareResults)
        {
            var runtime = await RunRuntimeComparisonAsync(
                request, evidence, limitations, cancellationToken);

            runtimeAttempted = runtime.Attempted;
            runtimeEqual = runtime.Equal;
            originalDuration = runtime.Original;
            candidateDuration = runtime.Candidate;
        }

        var differences = structure.ProvableDifferences
            .Concat(structure.UnverifiedDifferences)
            .ToList();

        // Failed: a provable structural difference or a proven runtime mismatch.
        // No ImprovementPercentage here: a performance claim requires proven
        // semantic equivalence, which by definition is absent on this path.
        if (structure.ProvableDifferences.Count > 0 || runtimeEqual == false)
        {
            return new ValidationResult
            {
                Status = ValidationStatus.Failed,
                SyntaxValid = true,
                SemanticallyEquivalent = false,
                OriginalExecutionTime = originalDuration,
                OptimizedExecutionTime = candidateDuration,
                Differences = differences,
                SemanticRisks = risks,
                ValidationConfidence = runtimeEqual == false ? ConfidenceRuntimeMismatch : ConfidenceStructuralFailure,
                Evidence = evidence,
                Limitations = limitations,
                AffectedObjects = structure.AffectedObjects
            };
        }

        // NotExecuted: runtime comparison was requested but could not be
        // attempted at all (disabled server-side, no provider, dialect
        // mismatch). Static findings are preserved in the result, but the
        // status never upgrades to a success.
        if (request.CompareResults && !runtimeAttempted)
        {
            return new ValidationResult
            {
                Status = ValidationStatus.NotExecuted,
                SyntaxValid = true,
                SemanticallyEquivalent = false,
                Differences = differences,
                SemanticRisks = risks,
                ValidationConfidence = ConfidenceNotExecuted,
                Evidence = evidence,
                Limitations = limitations,
                AffectedObjects = structure.AffectedObjects
            };
        }

        // Inconclusive (runtime): the comparison was attempted but produced no
        // equality evidence (for example an execution or connection failure, or
        // a truncated comparison). A missing result is "no evidence", never a
        // pass.
        if (request.CompareResults && runtimeAttempted && runtimeEqual is null)
        {
            return new ValidationResult
            {
                Status = ValidationStatus.Inconclusive,
                SyntaxValid = true,
                SemanticallyEquivalent = false,
                OriginalExecutionTime = originalDuration,
                OptimizedExecutionTime = candidateDuration,
                Differences = differences,
                SemanticRisks = risks,
                ValidationConfidence = ConfidenceInconclusive,
                Evidence = evidence,
                Limitations = limitations,
                AffectedObjects = structure.AffectedObjects
            };
        }

        // Inconclusive: something changed in a way the AST (and, when it ran,
        // the runtime comparison) cannot decide.
        if (structure.UnverifiedDifferences.Count > 0 || risks.Any(r => !r.Provable))
        {
            if (runtimeEqual == true)
            {
                limitations.Add("Runtime results were equal on the compared data, but static analysis found semantic risks that are data-dependent; a broader comparison or schema proof is required.");
            }
            else
            {
                limitations.Add("Semantic equivalence could not be established from static analysis; database validation (result comparison or plan inspection) is required.");
            }

            // No ImprovementPercentage: semantic equivalence was not established.
            return new ValidationResult
            {
                Status = ValidationStatus.Inconclusive,
                SyntaxValid = true,
                SemanticallyEquivalent = false,
                OriginalExecutionTime = originalDuration,
                OptimizedExecutionTime = candidateDuration,
                Differences = differences,
                SemanticRisks = risks,
                ValidationConfidence = ConfidenceInconclusive,
                Evidence = evidence,
                Limitations = limitations,
                AffectedObjects = structure.AffectedObjects
            };
        }

        // Passed: no provable and no unverified differences, no open risks.
        if (runtimeEqual != true)
        {
            limitations.Add("Equivalence was established structurally only; a runtime result comparison would provide stronger evidence.");
        }

        return new ValidationResult
        {
            Status = ValidationStatus.Passed,
            SyntaxValid = true,
            SemanticallyEquivalent = runtimeEqual == true,
            OriginalExecutionTime = originalDuration,
            OptimizedExecutionTime = candidateDuration,
            ImprovementPercentage = ImprovementPercentage(originalDuration, candidateDuration),
            Differences = differences,
            SemanticRisks = risks,
            ValidationConfidence = runtimeEqual == true ? ConfidencePassedRuntime : ConfidencePassedStatic,
            Evidence = evidence,
            Limitations = limitations,
            AffectedObjects = structure.AffectedObjects
        };
    }

    /// <summary>
    /// Outcome of the optional runtime comparison stage.
    /// </summary>
    /// <param name="Attempted">True when the validation provider was actually invoked.</param>
    /// <param name="Equal">Equality answer of the comparison; null when none was produced.</param>
    /// <param name="Original">Original execution duration when measured.</param>
    /// <param name="Candidate">Candidate execution duration when measured.</param>
    private sealed record RuntimeComparisonOutcome(bool Attempted, bool? Equal, TimeSpan? Original, TimeSpan? Candidate);

    /// <summary>
    /// Runs the optional runtime result comparison, recording evidence and
    /// limitations. Reports whether the comparison was attempted at all, the
    /// equality answer (null when none was produced) and the measured
    /// durations.
    /// </summary>
    private async Task<RuntimeComparisonOutcome> RunRuntimeComparisonAsync(
        SqlValidationRequest request,
        List<string> evidence,
        List<string> limitations,
        CancellationToken cancellationToken)
    {
        if (!_options.EnableRuntimeValidation)
        {
            limitations.Add("Runtime result comparison was requested but is disabled on the server (EnableRuntimeValidation=false); no database result comparison was performed.");
            return new(false, null, null, null);
        }

        if (!_validationProvider.IsAvailable)
        {
            limitations.Add("Runtime result comparison was requested but no database validation provider is available; only static structural validation was performed.");
            return new(false, null, null, null);
        }

        if (_validationProvider.Dialect != request.Dialect)
        {
            limitations.Add(
                $"Runtime result comparison was requested but the validation provider dialect ({_validationProvider.Dialect}) does not match the request dialect ({request.Dialect}).");
            return new(false, null, null, null);
        }

        var comparison = await _validationProvider.CompareResultsAsync(
            request.OriginalSql,
            request.CandidateSql,
            Math.Max(1, request.MaxRowsForComparison),
            cancellationToken);

        if (comparison is null)
        {
            limitations.Add("Runtime result comparison was requested but could not be executed by the validation provider.");
            return new(true, null, null, null);
        }

        evidence.Add(comparison.ResultsEqual
            ? $"Database result comparison: results equal ({comparison.OriginalRows} vs {comparison.CandidateRows} rows{(comparison.Truncated ? ", truncated at the comparison cap" : string.Empty)})."
            : $"Database result comparison: results differ ({comparison.OriginalRows} vs {comparison.CandidateRows} rows).");

        foreach (var note in comparison.Notes)
        {
            evidence.Add(note);
        }

        bool? equal;
        if (comparison.Truncated)
        {
            // Truncation means the compared rows are all the evidence there
            // is. A mismatch inside them is proven, but equality of the full
            // result sets is not; equivalence must never be claimed from a
            // truncated comparison.
            equal = comparison.ResultsEqual ? null : false;
            limitations.Add(
                "The result comparison hit the comparison cap and was truncated; equivalence of the full result sets cannot be established from the compared rows.");
        }
        else
        {
            equal = comparison.ResultsEqual;
        }

        return new(true, equal, comparison.OriginalDuration, comparison.CandidateDuration);
    }

    /// <summary>
    /// Enriches the request schema with live schema metadata when a metadata
    /// provider is available, so the metadata-backed structural proofs (for
    /// example star expansion and COUNT(*) vs COUNT(column)) can be applied.
    /// Request-provided metadata always wins; live metadata only fills the
    /// gaps. Any failure degrades to the request schema (conservative) and is
    /// recorded as a limitation, never as an error.
    /// </summary>
    private async Task<DatabaseSchema?> EnrichSchemaWithLiveMetadataAsync(
        QueryStructureSnapshot original,
        QueryStructureSnapshot candidate,
        DatabaseSchema? requestSchema,
        List<string> evidence,
        List<string> limitations,
        CancellationToken cancellationToken)
    {
        var provider = _metadataProvider;
        if (provider is not { IsAvailable: true })
        {
            return requestSchema;
        }

        var tableNames = original.Tables
            .Concat(candidate.Tables)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (tableNames.Count == 0)
        {
            return requestSchema;
        }

        var missing = tableNames.Where(name => !SchemaContainsTable(requestSchema, name)).ToList();
        if (missing.Count == 0)
        {
            return requestSchema;
        }

        if (missing.Count > MaxTablesForLiveMetadata)
        {
            limitations.Add(
                $"Live schema metadata was not fetched: {missing.Count} distinct tables are referenced, above the safe limit of {MaxTablesForLiveMetadata}.");
            return requestSchema;
        }

        DatabaseSchema? live;
        try
        {
            live = await provider.GetTablesSchemaAsync(missing, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            limitations.Add(
                $"Live schema metadata could not be read ({ex.Message}); metadata-backed proofs were not applied.");
            return requestSchema;
        }

        if (live is not { Tables: { Count: > 0 } })
        {
            limitations.Add(
                "Live schema metadata was requested but no metadata could be read; metadata-backed proofs were not applied.");
            return requestSchema;
        }

        var mergedTables = new List<DatabaseTable>(live.Tables.Count + (requestSchema?.Tables.Count ?? 0));
        if (requestSchema is not null)
        {
            mergedTables.AddRange(requestSchema.Tables);
        }

        foreach (var table in live.Tables)
        {
            if (mergedTables.All(existing => !IsSameTable(existing, table)))
            {
                mergedTables.Add(table);
            }
        }

        evidence.Add(
            $"Live schema metadata was used for table(s): {string.Join(", ", live.Tables.Select(t => $"{t.Schema}.{t.Name}"))}.");
        return new DatabaseSchema(mergedTables);
    }

    /// <summary>True when the schema already contains a table with the given (possibly qualified) name.</summary>
    private static bool SchemaContainsTable(DatabaseSchema? schema, string name)
    {
        if (schema is null)
        {
            return false;
        }

        var parts = name.Split('.', 2);
        return parts.Length == 2
            ? schema.FindTable(new TableReference(parts[0], parts[1], null)) is not null
            : schema.FindTable(parts[0]) is not null;
    }

    /// <summary>Same-table check tolerant of missing schema qualifiers.</summary>
    private static bool IsSameTable(DatabaseTable a, DatabaseTable b) =>
        string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase)
        && (string.IsNullOrEmpty(a.Schema)
            || string.IsNullOrEmpty(b.Schema)
            || string.Equals(a.Schema, b.Schema, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Computes the improvement percentage when both durations are available
    /// and the candidate is faster; null otherwise.
    /// </summary>
    private static double? ImprovementPercentage(TimeSpan? original, TimeSpan? candidate)
    {
        if (original is not { } o || candidate is not { } c || o <= TimeSpan.Zero)
        {
            return null;
        }

        var percentage = (o - c).TotalMilliseconds / o.TotalMilliseconds * 100d;
        return percentage > 0 ? Math.Round(percentage, 2) : null;
    }
}

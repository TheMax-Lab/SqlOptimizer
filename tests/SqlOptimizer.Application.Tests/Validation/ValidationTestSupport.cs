using SqlOptimizer.Application.Abstractions;
using SqlOptimizer.Application.DTOs;
using SqlOptimizer.Application.Options;
using SqlOptimizer.Application.Services.Validation;
using SqlOptimizer.Domain.Common;
using SqlOptimizer.Domain.Metadata;
using SqlOptimizer.Infrastructure.Parsing;

namespace SqlOptimizer.Application.Tests.Validation;

/// <summary>
/// Shared factory for validation tests: production parser, production
/// options and a configurable database validation provider. No mocks of the
/// parser or the validator; only the (external) database provider is
/// replaceable, as it would be in production DI.
/// </summary>
internal static class ValidationTestSupport
{
    /// <summary>Creates a validator with the production parser and the given options/provider.</summary>
    internal static SqlValidator CreateValidator(
        SqlOptimizerOptions? options = null,
        IDatabaseValidationProvider? provider = null) => new(
            new SqlServerSqlParser(),
            options ?? new SqlOptimizerOptions(),
            provider ?? new UnavailableDatabaseValidationProvider());

    /// <summary>Validates the candidate against the original query.</summary>
    internal static Task<ValidationResult> ValidateAsync(
        SqlValidator validator,
        string original,
        string candidate,
        bool compareResults = false,
        int maxRows = 1000,
        DatabaseSchema? schema = null) =>
        validator.ValidateAsync(new SqlValidationRequest(
            original, candidate, compareResults, maxRows, SqlDialect.SqlServer, schema));

    /// <summary>Schema used by the tests: Customers (Id PK, Name, City) and Orders.</summary>
    internal static DatabaseSchema TestSchema() => new(new[]
    {
        new DatabaseTable("dbo", "Customers", 10_000, new[]
        {
            new DatabaseColumn("Id", "INT", Nullable: false, PrimaryKey: true),
            new DatabaseColumn("Name", "NVARCHAR(100)", Nullable: true, PrimaryKey: false),
            new DatabaseColumn("City", "NVARCHAR(50)", Nullable: true, PrimaryKey: false)
        }, Array.Empty<DatabaseIndex>()),
        new DatabaseTable("dbo", "Orders", 50_000, new[]
        {
            new DatabaseColumn("OrderId", "INT", Nullable: false, PrimaryKey: true),
            new DatabaseColumn("CustomerId", "INT", Nullable: false, PrimaryKey: false),
            new DatabaseColumn("Total", "DECIMAL(18,2)", Nullable: true, PrimaryKey: false)
        }, Array.Empty<DatabaseIndex>())
    });

    /// <summary>Creates a fixed-comparison provider (test double for the external database).</summary>
    internal static FixedComparisonProvider CreateFixedProvider(DatabaseComparisonResult result) => new(result);

    /// <summary>
    /// Test double for the external database validation provider: returns a
    /// canned comparison result (or null for "no evidence") and records the
    /// invocations. It represents an external system boundary, not the
    /// validator under test.
    /// </summary>
    internal sealed class FixedComparisonProvider : IDatabaseValidationProvider
    {
        private readonly DatabaseComparisonResult? _result;
        private readonly SqlDialect _dialect;

        public FixedComparisonProvider(DatabaseComparisonResult? result, SqlDialect dialect = SqlDialect.SqlServer)
        {
            _result = result;
            _dialect = dialect;
        }

        public SqlDialect Dialect => _dialect;

        public bool IsAvailable => true;

        public int CompareCalls { get; private set; }

        public int? LastMaxRows { get; private set; }

        public string? LastOriginal { get; private set; }

        public string? LastCandidate { get; private set; }

        public Task<DatabaseComparisonResult?> CompareResultsAsync(
            string originalSql,
            string candidateSql,
            int maxRowsForComparison,
            CancellationToken cancellationToken = default)
        {
            CompareCalls++;
            LastMaxRows = maxRowsForComparison;
            LastOriginal = originalSql;
            LastCandidate = candidateSql;
            return Task.FromResult<DatabaseComparisonResult?>(_result);
        }
    }
}

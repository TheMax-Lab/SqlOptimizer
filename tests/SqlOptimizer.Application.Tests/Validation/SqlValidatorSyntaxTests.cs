using FluentAssertions;
using SqlOptimizer.Application.DTOs;
using SqlOptimizer.Domain.Common;
using Xunit;

namespace SqlOptimizer.Application.Tests.Validation;

/// <summary>
/// Stage A of validation: the candidate must parse with the SQL Server
/// parser. Invalid candidate SQL is a validation outcome (Failed), never an
/// exception; caller errors (bad dialect, empty original) are exceptions.
/// </summary>
public class SqlValidatorSyntaxTests
{
    [Fact]
    public async Task ValidSql_SameQuery_Passes()
    {
        var validator = ValidationTestSupport.CreateValidator();

        var result = await ValidationTestSupport.ValidateAsync(
            validator, "SELECT Id FROM Customers", "SELECT Id FROM Customers");

        result.Status.Should().Be(ValidationStatus.Passed);
        result.SyntaxValid.Should().BeTrue();
        result.SemanticallyEquivalent.Should().BeFalse();
        result.Errors.Should().BeEmpty();
        result.ValidationConfidence.Should().BeGreaterThan(0.8);
    }

    [Fact]
    public async Task InvalidCandidateSql_Fails_WithSyntaxError()
    {
        var validator = ValidationTestSupport.CreateValidator();

        var result = await ValidationTestSupport.ValidateAsync(
            validator, "SELECT Id FROM Customers", "SELECT FROM WHERE");

        result.Status.Should().Be(ValidationStatus.Failed);
        result.SyntaxValid.Should().BeFalse();
        result.SemanticallyEquivalent.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task EmptyCandidateSql_Fails()
    {
        var validator = ValidationTestSupport.CreateValidator();

        var result = await ValidationTestSupport.ValidateAsync(
            validator, "SELECT Id FROM Customers", "   ");

        result.Status.Should().Be(ValidationStatus.Failed);
        result.SyntaxValid.Should().BeFalse();
        result.Errors.Should().ContainSingle();
    }

    [Fact]
    public async Task NonSelectCandidate_Fails()
    {
        var validator = ValidationTestSupport.CreateValidator();

        var result = await ValidationTestSupport.ValidateAsync(
            validator, "SELECT Id FROM Customers", "DROP TABLE Customers");

        result.Status.Should().Be(ValidationStatus.Failed);
        result.SyntaxValid.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task UnsupportedDialect_Throws()
    {
        var validator = ValidationTestSupport.CreateValidator();

        Func<Task> act = () => validator.ValidateAsync(new SqlValidationRequest(
            "SELECT 1", "SELECT 1", CompareResults: false, Dialect: SqlDialect.PostgreSql));

        await act.Should().ThrowAsync<SqlUnsupportedDialectException>();
    }

    [Fact]
    public async Task NullRequest_Throws_WithArgumentNullException()
    {
        var validator = ValidationTestSupport.CreateValidator();

        Func<Task> act = () => validator.ValidateAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task EmptyOriginalSql_Throws_WithSqlInvalidInputException()
    {
        var validator = ValidationTestSupport.CreateValidator();

        Func<Task> act = () => ValidationTestSupport.ValidateAsync(validator, "", "SELECT 1");

        await act.Should().ThrowAsync<SqlInvalidInputException>();
    }

    [Fact]
    public async Task CancelledToken_Throws_WithOperationCanceledException()
    {
        var validator = ValidationTestSupport.CreateValidator();

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Func<Task> act = () => validator.ValidateAsync(
            new SqlValidationRequest("SELECT 1", "SELECT 1", false), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}

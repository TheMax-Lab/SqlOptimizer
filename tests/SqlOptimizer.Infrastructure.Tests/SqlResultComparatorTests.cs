using FluentAssertions;
using SqlOptimizer.Infrastructure.Validation;
using Xunit;

namespace SqlOptimizer.Infrastructure.Tests;

/// <summary>
/// Deterministic result-set comparison: column metadata, row counts, NULL
/// semantics, duplicate preservation and the ordered/unordered strategies.
/// All tests are offline (pure data, no database).
/// </summary>
public class SqlResultComparatorTests
{
    private static readonly SqlResultComparator.ResultColumn[] SingleInt =
    [
        new SqlResultComparator.ResultColumn("Id", "int")
    ];

    private static SqlResultComparator.ComparisonOutcome CompareUnordered(
        object?[][] a, object?[][] b, int cap = 1000) =>
        SqlResultComparator.Compare(SingleInt, a, SingleInt, b, positional: false, cap);

    private static SqlResultComparator.ComparisonOutcome ComparePositional(
        object?[][] a, object?[][] b, int cap = 1000) =>
        SqlResultComparator.Compare(SingleInt, a, SingleInt, b, positional: true, cap);

    [Fact]
    public void IdenticalOrderedSets_AreEqual_Positional()
    {
        var outcome = ComparePositional([[1], [2], [3]], [[1], [2], [3]]);

        outcome.ResultsEqual.Should().BeTrue();
        outcome.Truncated.Should().BeFalse();
        outcome.Notes.Should().BeEmpty();
    }

    [Fact]
    public void SameRowsDifferentOrder_AreEqual_Unordered()
    {
        var outcome = CompareUnordered([[3], [1], [2]], [[1], [2], [3]]);

        outcome.ResultsEqual.Should().BeTrue();
    }

    [Fact]
    public void SameRowsDifferentOrder_AreNotEqual_Positional()
    {
        var outcome = ComparePositional([[1], [2]], [[2], [1]]);

        outcome.ResultsEqual.Should().BeFalse();
        outcome.Notes.Should().Contain(n => n.Contains("values differ"));
    }

    [Fact]
    public void Null_IsDistinctFromOrdinaryValues()
    {
        var withNull = CompareUnordered([[null], [1]], [[1], [2]]);
        var nullVsNull = CompareUnordered([[null], [1]], [[1], [null]]);

        withNull.ResultsEqual.Should().BeFalse();
        nullVsNull.ResultsEqual.Should().BeTrue();
    }

    [Fact]
    public void DuplicateRows_ArePreservedInMultiset()
    {
        var twoDuplicates = CompareUnordered([[1], [1], [2]], [[1], [2]]);
        var sameDuplicates = CompareUnordered([[1], [1], [2]], [[2], [1], [1]]);

        twoDuplicates.ResultsEqual.Should().BeFalse();
        twoDuplicates.Notes.Should().Contain(n => n.Contains("time(s)"));
        sameDuplicates.ResultsEqual.Should().BeTrue();
    }

    [Fact]
    public void DifferentRowCounts_AreNotEqual()
    {
        var outcome = CompareUnordered([[1], [2]], [[1]]);

        outcome.ResultsEqual.Should().BeFalse();
        outcome.Notes.Should().Contain(n => n.Contains("Row count differs"));
    }

    [Fact]
    public void DifferentColumnCounts_AreNotEqual()
    {
        var outcome = SqlResultComparator.Compare(
            SingleInt, [[1]],
            [new SqlResultComparator.ResultColumn("Id", "int"), new SqlResultComparator.ResultColumn("Name", "nvarchar(100)")],
            [[1, "a"]],
            positional: false,
            cap: 1000);

        outcome.ResultsEqual.Should().BeFalse();
        outcome.Notes.Should().Contain(n => n.Contains("Column count differs"));
    }

    [Fact]
    public void DifferentColumnNames_AreNotEqual()
    {
        var outcome = SqlResultComparator.Compare(
            SingleInt, [[1]],
            [new SqlResultComparator.ResultColumn("ID", "int")],
            [[1]],
            positional: false,
            cap: 1000);

        // Case-insensitive: only a real name difference fails.
        outcome.ResultsEqual.Should().BeTrue();

        var renamed = SqlResultComparator.Compare(
            SingleInt, [[1]],
            [new SqlResultComparator.ResultColumn("Other", "int")],
            [[1]],
            positional: false,
            cap: 1000);

        renamed.ResultsEqual.Should().BeFalse();
        renamed.Notes.Should().Contain(n => n.Contains("name differs"));
    }

    [Fact]
    public void DifferentDataTypes_AreNotEqual()
    {
        var outcome = SqlResultComparator.Compare(
            SingleInt, [[1]],
            [new SqlResultComparator.ResultColumn("Id", "bigint")],
            [[1L]],
            positional: false,
            cap: 1000);

        outcome.ResultsEqual.Should().BeFalse();
        outcome.Notes.Should().Contain(n => n.Contains("data type differs"));
    }

    [Fact]
    public void Truncation_IsReported()
    {
        var outcome = CompareUnordered([[1], [2], [3]], [[1], [2], [3]], cap: 2);

        outcome.Truncated.Should().BeTrue();
    }

    [Fact]
    public void BinaryCells_CompareByContent()
    {
        var equal = CompareUnordered([[new byte[] { 1, 2, 3 }]], [[new byte[] { 1, 2, 3 }]]);
        var different = CompareUnordered([[new byte[] { 1, 2, 3 }]], [[new byte[] { 1, 2, 4 }]]);

        equal.ResultsEqual.Should().BeTrue();
        different.ResultsEqual.Should().BeFalse();
    }

    [Fact]
    public void PaddedStrings_CompareExactly()
    {
        // NCHAR-style padding is part of the value: padded vs unpadded differs.
        var paddedVsPadded = CompareUnordered([[ "abc  " ]], [["abc  "]]);
        var paddedVsUnpadded = CompareUnordered([["abc  "]], [["abc"]]);

        paddedVsPadded.ResultsEqual.Should().BeTrue();
        paddedVsUnpadded.ResultsEqual.Should().BeFalse();
    }

    [Fact]
    public void MultiCellRows_NeverCollideAcrossCells()
    {
        SqlResultComparator.ResultColumn[] twoColumns =
        [
            new SqlResultComparator.ResultColumn("A", "nvarchar(10)"),
            new SqlResultComparator.ResultColumn("B", "nvarchar(10)")
        ];

        var split = SqlResultComparator.Compare(twoColumns, [["a", "bc"]], twoColumns, [["ab", "c"]], positional: false, cap: 100);

        split.ResultsEqual.Should().BeFalse();
    }

    [Fact]
    public void DecimalValues_CompareExactly()
    {
        var equal = CompareUnordered([[1.10m], [2.20m]], [[2.20m], [1.10m]]);
        var different = CompareUnordered([[1.10m], [2.20m]], [[2.20m], [1.20m]]);

        equal.ResultsEqual.Should().BeTrue();
        different.ResultsEqual.Should().BeFalse();
    }

    [Fact]
    public void DateTimeValues_CompareExactly()
    {
        var a = new DateTime(2024, 1, 2, 3, 4, 5);
        var equal = CompareUnordered([[a], [a.AddSeconds(1)]], [[a.AddSeconds(1)], [a]]);
        var different = CompareUnordered([[a]], [[a.AddTicks(1)]]);

        equal.ResultsEqual.Should().BeTrue();
        different.ResultsEqual.Should().BeFalse();
    }

    [Fact]
    public void FloatingPointValues_CompareExactly()
    {
        var equal = CompareUnordered([[1.5d], [2.5d]], [[2.5d], [1.5d]]);
        var different = CompareUnordered([[1.5d]], [[1.5000000001d]]);

        equal.ResultsEqual.Should().BeTrue();
        different.ResultsEqual.Should().BeFalse();
    }

    [Fact]
    public void SameTextualFormDifferentClrType_AreNotEqual()
    {
        // 1 (int) and 1 (long) render identically but are distinct values:
        // the canonical key includes the CLR type, so they must not collide
        // in the multiset comparison.
        var outcome = CompareUnordered([[1]], [[1L]]);

        outcome.ResultsEqual.Should().BeFalse();
    }

    [Fact]
    public void EmptyResultSets_AreEqual()
    {
        var outcome = CompareUnordered([], []);

        outcome.ResultsEqual.Should().BeTrue();
        outcome.Notes.Should().BeEmpty();
    }

    [Fact]
    public void OneRowVersusZeroRows_AreNotEqual()
    {
        var outcome = CompareUnordered([[1]], []);

        outcome.ResultsEqual.Should().BeFalse();
        outcome.Notes.Should().Contain(n => n.Contains("Row count differs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SpecMultiplicityCase_DifferingDuplicateDistribution_IsNotEqual()
    {
        // A = [row1, row1, row2], B = [row1, row2, row2]: same row count and
        // same distinct rows, but different duplicate multiplicity.
        var outcome = CompareUnordered([[1], [1], [2]], [[1], [2], [2]]);

        outcome.ResultsEqual.Should().BeFalse();
        outcome.Notes.Should().Contain(n => n.Contains("time(s)", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DateTimeOffsetValues_CompareExactly()
    {
        var a = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);
        // The same instant expressed with a different (non-zero) offset.
        var sameInstantDifferentOffset = new DateTimeOffset(2024, 1, 2, 4, 4, 5, TimeSpan.FromHours(1));

        var equal = CompareUnordered([[a], [a.AddSeconds(1)]], [[a.AddSeconds(1)], [a]]);
        var differentOffset = CompareUnordered([[a]], [[sameInstantDifferentOffset]]);
        var differentInstant = CompareUnordered([[a]], [[a.AddTicks(1)]]);

        equal.ResultsEqual.Should().BeTrue();
        differentOffset.ResultsEqual.Should().BeTrue(
            "the same instant expressed with a different offset is the same DateTimeOffset value");
        differentInstant.ResultsEqual.Should().BeFalse();
    }

    [Fact]
    public void ByteScalars_CompareExactly()
    {
        var equal = CompareUnordered([[new byte[] { 1 }]], [[new byte[] { 1 }]]);
        var different = CompareUnordered([[new byte[] { 1 }]], [[new byte[] { 2 }]]);
        var typeMismatch = CompareUnordered([[new byte[] { 1 }]], [[(short)1]]);

        equal.ResultsEqual.Should().BeTrue();
        different.ResultsEqual.Should().BeFalse();
        typeMismatch.ResultsEqual.Should().BeFalse(
            "a byte array and a scalar are distinct value kinds even if they render the same");
    }

    [Fact]
    public void StringComparison_IsCaseSensitive()
    {
        var equalCase = CompareUnordered([["Rome"], ["rome"]], [["rome"], ["Rome"]]);
        var differentCase = CompareUnordered([["Rome"]], [["rome"]]);

        equalCase.ResultsEqual.Should().BeTrue("same multiset of values in any order");
        differentCase.ResultsEqual.Should().BeFalse(
            "string values are compared exactly; case is part of the value");
    }

    [Fact]
    public void Positional_MaterializedColumnCountMismatch_IsReported()
    {
        SqlResultComparator.ResultColumn[] twoColumns =
        [
            new SqlResultComparator.ResultColumn("A", "int"),
            new SqlResultComparator.ResultColumn("B", "int")
        ];

        var outcome = SqlResultComparator.Compare(
            twoColumns, [[1, 2]],
            twoColumns, [[1]],
            positional: true,
            cap: 10);

        outcome.ResultsEqual.Should().BeFalse();
        outcome.Notes.Should().Contain(n => n.Contains("column count differs", StringComparison.OrdinalIgnoreCase));
    }
}

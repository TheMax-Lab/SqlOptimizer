using System.Globalization;
using System.Text;

namespace SqlOptimizer.Infrastructure.Validation;

/// <summary>
/// Pure, deterministic result-set comparison used by the database validation
/// provider. It compares column metadata (count, names, data types), row
/// counts and cell values, treating NULL as a value distinct from any
/// ordinary value and preserving duplicate rows. When the queries carry no
/// explicit ordering, rows are compared as a multiset (deterministic row
/// canonicalization, counts preserved) instead of by row position; when
/// ordering is explicit, rows are compared positionally. The comparator
/// never invents values, never mutates its input and reports every
/// mismatch it finds as an explicit note.
/// </summary>
public static class SqlResultComparator
{
    /// <summary>Maximum characters of a cell value shown in comparison notes.</summary>
    private const int MaxNoteValueLength = 60;

    /// <summary>Column metadata of one compared result set.</summary>
    /// <param name="Name">Column name as reported by the database.</param>
    /// <param name="DataType">Data type as reported by the database.</param>
    public sealed record ResultColumn(string Name, string DataType);

    /// <summary>Outcome of comparing two result sets.</summary>
    /// <param name="ResultsEqual">True when every compared aspect matched.</param>
    /// <param name="Truncated">True when either input already exceeded the comparison cap.</param>
    /// <param name="Notes">Explicit mismatch notes (empty when equal).</param>
    public sealed record ComparisonOutcome(bool ResultsEqual, bool Truncated, IReadOnlyList<string> Notes);

    /// <summary>
    /// Compares two materialized result sets.
    /// </summary>
    /// <param name="originalColumns">Original column metadata (in order).</param>
    /// <param name="originalRows">Original rows (each row a cell array), at most cap + 1 rows.</param>
    /// <param name="candidateColumns">Candidate column metadata (in order).</param>
    /// <param name="candidateRows">Candidate rows, at most cap + 1 rows.</param>
    /// <param name="positional">True when both queries carry an explicit ORDER BY (compare by row position).</param>
    /// <param name="cap">The comparison row cap.</param>
    public static ComparisonOutcome Compare(
        IReadOnlyList<ResultColumn> originalColumns,
        object?[][] originalRows,
        IReadOnlyList<ResultColumn> candidateColumns,
        object?[][] candidateRows,
        bool positional,
        int cap)
    {
        ArgumentNullException.ThrowIfNull(originalColumns);
        ArgumentNullException.ThrowIfNull(originalRows);
        ArgumentNullException.ThrowIfNull(candidateColumns);
        ArgumentNullException.ThrowIfNull(candidateRows);

        var truncated = originalRows.Length > cap || candidateRows.Length > cap;
        var notes = new List<string>();

        if (originalColumns.Count != candidateColumns.Count)
        {
            notes.Add($"Column count differs (original: {originalColumns.Count}, candidate: {candidateColumns.Count}).");
            return new ComparisonOutcome(false, truncated, notes);
        }

        for (var i = 0; i < originalColumns.Count; i++)
        {
            if (!string.Equals(originalColumns[i].Name, candidateColumns[i].Name, StringComparison.OrdinalIgnoreCase))
            {
                notes.Add($"Column {i + 1} name differs ('{originalColumns[i].Name}' vs '{candidateColumns[i].Name}').");
            }

            if (!string.Equals(originalColumns[i].DataType, candidateColumns[i].DataType, StringComparison.OrdinalIgnoreCase))
            {
                notes.Add($"Column {i + 1} data type differs ('{originalColumns[i].DataType}' vs '{candidateColumns[i].DataType}').");
            }
        }

        if (notes.Count > 0)
        {
            return new ComparisonOutcome(false, truncated, notes);
        }

        if (originalRows.Length != candidateRows.Length)
        {
            notes.Add($"Row count differs (original: {originalRows.Length}, candidate: {candidateRows.Length}{(truncated ? ", results truncated at the comparison cap" : string.Empty)}).");

            // Positional comparison is decided by the row count; multiset
            // comparison continues so that the differing rows are reported
            // explicitly as well.
            if (positional)
            {
                return new ComparisonOutcome(false, truncated, notes);
            }
        }

        var equal = positional
            ? RowsEqualPositional(originalRows, candidateRows, notes)
            : RowMultisetsEqual(originalRows, candidateRows, notes);

        return new ComparisonOutcome(equal, truncated, notes);
    }

    /// <summary>Positional, cell-by-cell comparison (order is part of the contract).</summary>
    private static bool RowsEqualPositional(object?[][] a, object?[][] b, List<string> notes)
    {
        for (var row = 0; row < a.Length; row++)
        {
            if (a[row].Length != b[row].Length)
            {
                notes.Add($"Row {row + 1}: materialized column count differs.");
                return false;
            }

            for (var cell = 0; cell < a[row].Length; cell++)
            {
                if (!CellsEqual(a[row][cell], b[row][cell]))
                {
                    notes.Add($"Row {row + 1}, column {cell + 1}: values differ ({RenderCell(a[row][cell])} vs {RenderCell(b[row][cell])}).");
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Multiset comparison: rows are canonicalized deterministically and their
    /// counts compared. Duplicate rows and NULL cells are preserved; no value
    /// is reordered or coerced inside a row.
    /// </summary>
    private static bool RowMultisetsEqual(object?[][] a, object?[][] b, List<string> notes)
    {
        var countsA = BuildRowMultiset(a);
        var countsB = BuildRowMultiset(b);

        foreach (var (key, count) in countsA)
        {
            if (!countsB.TryGetValue(key, out var otherCount) || otherCount != count)
            {
                notes.Add($"Row multiset differs: a row present {count} time(s) in the original is present {otherCount} time(s) in the candidate.");
                return false;
            }
        }

        if (countsA.Count != countsB.Count)
        {
            notes.Add("Row multiset differs: the candidate contains rows not present in the original.");
            return false;
        }

        return true;
    }

    /// <summary>Counts how often each canonical row occurs.</summary>
    private static Dictionary<string, int> BuildRowMultiset(object?[][] rows)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var key = RowKey(row);
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        return counts;
    }

    /// <summary>
    /// Deterministic canonical key of a row: each cell is encoded with a
    /// type tag (NULL / binary / scalar plus CLR type name) so distinct
    /// values never collide, including values that render identically
    /// (for example 1 as int and 1 as long).
    /// </summary>
    private static string RowKey(object?[] row)
    {
        var builder = new StringBuilder(row.Length * 24);
        foreach (var cell in row)
        {
            builder.Append(' ');
            if (cell is null)
            {
                builder.Append('\u0001');
            }
            else if (cell is byte[] bytes)
            {
                builder.Append('\u0002').Append(Convert.ToHexString(bytes));
            }
            else
            {
                // Some scalar types lose precision with their default string
                // form (for example DateTime renders without sub-second ticks),
                // so they use a round-trip format: distinct values must never
                // render identically. DateTimeOffset is normalized to UTC so
                // the same instant compares equal regardless of offset,
                // matching SQL Server datetimeoffset semantics (and the
                // positional path, which uses DateTimeOffset.Equals).
                var rendered = cell switch
                {
                    DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
                    DateTimeOffset dateTimeOffset => dateTimeOffset.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                    double d => d.ToString("R", CultureInfo.InvariantCulture),
                    float f => f.ToString("R", CultureInfo.InvariantCulture),
                    _ => Convert.ToString(cell, CultureInfo.InvariantCulture)
                };


                builder.Append('\u0003').Append(cell.GetType().FullName).Append('|')
                    .Append(rendered).Append('\u0000');
            }
        }

        return builder.ToString();
    }

    /// <summary>NULL-safe cell equality; binary cells compare by content.</summary>
    private static bool CellsEqual(object? a, object? b)
    {
        if (a is null)
        {
            return b is null;
        }

        if (b is null)
        {
            return false;
        }

        return a is byte[] aBytes && b is byte[] bBytes
            ? aBytes.AsSpan().SequenceEqual(bBytes)
            : a.Equals(b);
    }

    /// <summary>Renders a cell for notes, bounded in length (values may be sensitive).</summary>
    private static string RenderCell(object? cell)
    {
        if (cell is null)
        {
            return "NULL";
        }

        if (cell is byte[] bytes)
        {
            return $"binary({bytes.Length} bytes)";
        }

        var text = Convert.ToString(cell, CultureInfo.InvariantCulture) ?? string.Empty;
        return text.Length <= MaxNoteValueLength ? text : $"{text[..MaxNoteValueLength]}…";
    }
}
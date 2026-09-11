using SqlOptimizer.Application.DTOs;
using SqlOptimizer.Domain.AST;
using SqlOptimizer.Domain.Metadata;

namespace SqlOptimizer.Application.Services.Validation;

/// <summary>
/// Stage B of validation: structural comparison of two parsed statements via
/// their <see cref="QueryStructureSnapshot"/>s. It classifies every observed
/// difference as either provable (the result contract or structure
/// demonstrably changed) or unverified (the AST alone cannot decide the
/// semantic effect). Identical normalized structure is reported as verified
/// structural facts only; it is never claimed as semantic equivalence.
/// </summary>
public static class SqlStructureComparer
{
    /// <summary>
    /// Compares the original and candidate statements structurally.
    /// </summary>
    /// <param name="original">Snapshot of the original statement.</param>
    /// <param name="candidate">Snapshot of the candidate statement.</param>
    /// <param name="schema">Schema metadata when available (enables metadata-backed proofs).</param>
    public static StructureComparison Compare(
        QueryStructureSnapshot original,
        QueryStructureSnapshot candidate,
        DatabaseSchema? schema = null)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(candidate);

        var provable = new List<string>();
        var unverified = new List<string>();
        var verified = new List<string>();
        var risks = new List<SemanticRisk>();

        if (original.Fingerprint == candidate.Fingerprint)
        {
            verified.Add("Statements are structurally identical after canonicalization (projection, FROM, WHERE, GROUP BY, HAVING, ORDER BY, set operations, CTEs).");
            return BuildComparison(provable, unverified, verified, risks, original, candidate);
        }

        CompareNameSets(
            original.Tables, candidate.Tables,
            names => $"Referenced tables differ: missing [{string.Join(", ", names)}].",
            names => $"Referenced tables differ: added [{string.Join(", ", names)}].",
            names => $"Referenced tables are identical: {string.Join(", ", names)}.",
            provable, verified, risks,
            new SemanticRisk(SemanticRiskType.StatementStructure,
                "The set of referenced tables changed; the candidate reads a different data source.",
                Provable: true));

        CompareNameSets(
            original.Parameters, candidate.Parameters,
            names => $"Parameters differ: missing [{string.Join(", ", names)}].",
            names => $"Parameters differ: added [{string.Join(", ", names)}].",
            names => $"Parameter usage is identical: {string.Join(", ", names)}.",
            provable, verified, risks,
            new SemanticRisk(SemanticRiskType.ParameterUsage,
                "Parameter usage changed; the candidate cannot be invoked with the same parameter values.",
                Provable: true));

        if (original.Distinct != candidate.Distinct)
        {
            provable.Add($"DISTINCT flag differs (original: {(original.Distinct ? "DISTINCT" : "none")}, candidate: {(candidate.Distinct ? "DISTINCT" : "none")}); row de-duplication changes.");
            risks.Add(new SemanticRisk(SemanticRiskType.DuplicateElimination,
                candidate.Distinct ? "DISTINCT was added; duplicate rows will now be eliminated." : "DISTINCT was removed; duplicate rows are no longer eliminated.",
                Provable: true));
        }
        else
        {
            verified.Add($"DISTINCT flag identical ({(original.Distinct ? "DISTINCT" : "none")}).");
        }

        if (!SameList(original.SetOperators, candidate.SetOperators))
        {
            provable.Add($"Set operators differ (original: [{JoinOrNone(original.SetOperators)}], candidate: [{JoinOrNone(candidate.SetOperators)}]).");
            risks.Add(new SemanticRisk(SemanticRiskType.DuplicateElimination,
                "The set operation chain changed (for example UNION vs UNION ALL); duplicate elimination behavior changes.",
                Provable: true));
        }
        else if (original.SetOperators.Count > 0)
        {
            verified.Add($"Set operators identical: {string.Join(", ", original.SetOperators)}.");
        }

        CompareProjection(original, candidate, schema, provable, unverified, verified, risks);
        ComparePredicate(
            original.Where, candidate.Where, "WHERE",
            unverified, verified, risks,
            new SemanticRisk(SemanticRiskType.PredicateLogic,
                "The WHERE predicate changed in a way that cannot be proven equivalent; SQL three-valued logic makes NULL handling data-dependent.",
                Provable: false));

        ComparePredicate(
            original.Having, candidate.Having, "HAVING",
            unverified, verified, risks,
            new SemanticRisk(SemanticRiskType.AggregationSemantics,
                "The HAVING predicate changed; whether aggregation results change is data-dependent.",
                Provable: false));

        if (!SameList(original.GroupBy, candidate.GroupBy))
        {
            provable.Add($"GROUP BY differs (original: [{JoinOrNone(original.GroupBy)}], candidate: [{JoinOrNone(candidate.GroupBy)}]).");
            risks.Add(new SemanticRisk(SemanticRiskType.AggregationSemantics,
                "The grouping changed; aggregate results are computed over different groups.",
                Provable: true));
        }
        else if (original.GroupBy.Count > 0)
        {
            verified.Add($"GROUP BY identical: {string.Join(", ", original.GroupBy)}.");
        }

        if (!SameList(original.OrderBy, candidate.OrderBy))
        {
            provable.Add($"ORDER BY differs (original: [{JoinOrNone(original.OrderBy, "; ")}], candidate: [{JoinOrNone(candidate.OrderBy, "; ")}]); ordering is externally observable.");
            risks.Add(new SemanticRisk(SemanticRiskType.Ordering,
                "Result ordering changed; consumers may rely on the original ordering.",
                Provable: true));
        }
        else if (original.OrderBy.Count > 0)
        {
            verified.Add($"ORDER BY identical: {string.Join("; ", original.OrderBy)}.");
        }

        CompareFromStructure(original, candidate, provable, unverified, verified, risks);

        if (original.SubqueryCount != candidate.SubqueryCount)
        {
            unverified.Add($"Subquery count differs (original: {original.SubqueryCount}, candidate: {candidate.SubqueryCount}); a subquery-to-join (or join-to-subquery) rewrite cannot be validated from the AST alone.");
            risks.Add(new SemanticRisk(SemanticRiskType.JoinCardinality,
                "Subqueries and joins changed; join cardinality can change when the inner side contains duplicates.",
                Provable: false));
        }

        if (!SameList(original.Casts, candidate.Casts))
        {
            var originalTry = original.Casts.Count(c => c.StartsWith("TRY_CONVERT", StringComparison.Ordinal));
            var candidateTry = candidate.Casts.Count(c => c.StartsWith("TRY_CONVERT", StringComparison.Ordinal));

            if (originalTry != candidateTry)
            {
                provable.Add($"TRY_CONVERT usage differs (original: {originalTry}, candidate: {candidateTry}); TRY_CONVERT returns NULL on conversion failure while CAST/CONVERT raise an error.");
                risks.Add(new SemanticRisk(SemanticRiskType.NullBehavior,
                    "TRY_CONVERT and CAST/CONVERT differ on conversion failure: NULL vs error.",
                    Provable: true));
            }
            else
            {
                unverified.Add($"Data conversion expressions differ (original: [{JoinOrNone(original.Casts, "; ")}], candidate: [{JoinOrNone(candidate.Casts, "; ")}]); conversion behavior is not proven equivalent.");
                risks.Add(new SemanticRisk(SemanticRiskType.ImplicitConversion,
                    "CAST/CONVERT expressions changed; type conversion and truncation behavior may differ.",
                    Provable: false));
            }
        }
        else if (original.Casts.Count > 0)
        {
            verified.Add($"Data conversions identical: {string.Join("; ", original.Casts)}.");
        }

        return BuildComparison(provable, unverified, verified, risks, original, candidate);
    }

    /// <summary>Compares the SELECT lists item by item, with metadata-backed proofs for star expansion and COUNT equivalence.</summary>
    private static void CompareProjection(
        QueryStructureSnapshot original,
        QueryStructureSnapshot candidate,
        DatabaseSchema? schema,
        List<string> provable,
        List<string> unverified,
        List<string> verified,
        List<SemanticRisk> risks)
    {
        // A single SELECT * (or t.*) in the original, expanded to explicit
        // columns in the candidate, is proven only against schema metadata.
        if (original.Projection.Count == 1
            && original.Projection[0].IsStar
            && candidate.Projection.All(p => !p.IsStar))
        {
            var star = original.Projection[0];

            if (schema is not null
                && original.SingleBaseTable is not null
                && FindSingleBaseTable(schema, original.SingleBaseTable) is { } table
                && (star.StarTableAlias is null || EqualsIgnoreCase(star.StarTableAlias, original.SingleBaseTableQualifier)))
            {
                if (candidate.Projection.Count != table.Columns.Count)
                {
                    provable.Add($"Projection column count differs: the star expansion has {candidate.Projection.Count} column(s) but table {original.SingleBaseTable} has {table.Columns.Count}.");
                    risks.Add(new SemanticRisk(SemanticRiskType.Projection,
                        "The star expansion does not list every column of the table.",
                        Provable: true));
                    return;
                }

                var allMatch = true;
                for (var i = 0; i < candidate.Projection.Count; i++)
                {
                    if (TryProveStarExpansion(original, star, candidate.Projection[i], i, schema))
                    {
                        continue;
                    }

                    allMatch = false;
                    provable.Add($"Projection item {i + 1} does not match column {table.Columns[i].Name} of table {original.SingleBaseTable}.");
                }

                if (allMatch)
                {
                    verified.Add($"Projection star expansion matches every column of table {original.SingleBaseTable} in order (proven from schema metadata).");
                }
                else
                {
                    risks.Add(new SemanticRisk(SemanticRiskType.Projection,
                        "The star expansion does not reproduce the table's column list.",
                        Provable: true));
                }

                return;
            }

            unverified.Add("The original uses a star projection and the candidate uses explicit columns; without schema metadata covering the table, the expansion cannot be proven equivalent.");
            risks.Add(new SemanticRisk(SemanticRiskType.Projection,
                "SELECT * was expanded to explicit columns; the expansion is proven only when schema metadata lists the exact column set in order.",
                Provable: false));
            return;
        }


        if (original.Projection.Count != candidate.Projection.Count)
        {
            provable.Add($"Projection column count differs (original: {original.Projection.Count}, candidate: {candidate.Projection.Count}).");
            risks.Add(new SemanticRisk(SemanticRiskType.Projection,
                "The number of projected columns changed; the result shape differs.",
                Provable: true));
            return;
        }

        var allIdentical = true;

        for (var i = 0; i < original.Projection.Count; i++)
        {
            var a = original.Projection[i];
            var b = candidate.Projection[i];

            if (!a.IsStar && !b.IsStar
                && a.NormalizedExpression == b.NormalizedExpression
                && EqualsIgnoreCase(EffectiveAlias(a), EffectiveAlias(b)))
            {
                continue;
            }

            if (a.IsStar && !b.IsStar)
            {
                allIdentical = false;
                unverified.Add($"Projection item {i + 1}: the original uses a star projection ({(a.StarTableAlias is null ? "*" : $"{a.StarTableAlias}.*")}) while the candidate uses an explicit expression; star expansion cannot be proven in this position.");
                risks.Add(new SemanticRisk(SemanticRiskType.Projection,
                    "A star projection was replaced by an explicit expression; equivalence is unproven.",
                    Provable: false));
                continue;
            }

            if (!a.IsStar && b.IsStar)
            {
                allIdentical = false;
                provable.Add($"Projection item {i + 1} was collapsed into a star projection; the explicit column contract changes.");
                risks.Add(new SemanticRisk(SemanticRiskType.Projection,
                    "An explicit column was replaced by a star projection; the result columns follow the table definition.",
                    Provable: true));
            }

            if (!a.IsStar && !b.IsStar && a.NormalizedExpression != b.NormalizedExpression)
            {
                allIdentical = false;

                var countResolution = TryResolveCountEquivalence(a.Expression, b.Expression, schema);
                if (countResolution is not null)
                {
                    if (countResolution == true)
                    {
                        verified.Add($"Projection item {i + 1}: COUNT(*) and COUNT(column) are equivalent because the column is NOT NULL (schema proven).");
                    }
                    else
                    {
                        provable.Add($"Projection item {i + 1}: COUNT(*) and COUNT(column) differ because the column is nullable; NULL rows are excluded from COUNT(column).");
                        risks.Add(new SemanticRisk(SemanticRiskType.AggregationSemantics,
                            "COUNT(*) and COUNT(column) differ when the column is nullable.",
                            Provable: true));
                    }
                }
                else
                {
                    unverified.Add($"Projection item {i + 1} expression differs: '{a.NormalizedExpression}' vs '{b.NormalizedExpression}'.");
                    var isAggregate = a.Expression is AggregateExpression || b.Expression is AggregateExpression;
                    risks.Add(new SemanticRisk(
                        isAggregate ? SemanticRiskType.AggregationSemantics : SemanticRiskType.Projection,
                        isAggregate
                            ? "An aggregate expression changed; aggregation semantics may differ."
                            : "A projected expression changed; the projected value may differ.",
                        Provable: false));
                }
            }

            if (!a.IsStar && !b.IsStar && !EqualsIgnoreCase(EffectiveAlias(a), EffectiveAlias(b)))
            {
                allIdentical = false;
                provable.Add($"Projection item {i + 1} alias differs ('{EffectiveAlias(a) ?? "<none>"}' vs '{EffectiveAlias(b) ?? "<none>"}'); result column names are externally observable.");
                risks.Add(new SemanticRisk(SemanticRiskType.Projection,
                    "A projection alias changed; consumers reading the result by column name may break.",
                    Provable: true));
            }
        }

        if (allIdentical)
        {
            verified.Add($"Projection expressions and aliases are identical ({original.Projection.Count} item(s)).");
        }
    }

    /// <summary>Compares the FROM structure: join count, join types, then predicates (outer-join aware).</summary>
    private static void CompareFromStructure(
        QueryStructureSnapshot original,
        QueryStructureSnapshot candidate,
        List<string> provable,
        List<string> unverified,
        List<string> verified,
        List<SemanticRisk> risks)
    {
        if (original.FromTree == candidate.FromTree)
        {
            if (original.JoinCount > 0)
            {
                verified.Add($"Join structure identical ({original.JoinCount} join(s)).");
            }

            return;
        }

        if (original.JoinCount != candidate.JoinCount)
        {
            if (original.SubqueryCount != candidate.SubqueryCount)
            {
                unverified.Add($"Join count differs together with the subquery count (joins {original.JoinCount} → {candidate.JoinCount}); this looks like a subquery-to-join (or join-to-subquery) rewrite whose cardinality effect cannot be decided from the AST.");
                risks.Add(new SemanticRisk(SemanticRiskType.JoinCardinality,
                    "A subquery/join transformation changed the join structure; row multiplication depends on duplicates on the inner side.",
                    Provable: false));
            }
            else
            {
                provable.Add($"Join count differs (original: {original.JoinCount}, candidate: {candidate.JoinCount}); row multiplication potential changes.");
                risks.Add(new SemanticRisk(SemanticRiskType.JoinCardinality,
                    "The number of joins changed; the candidate multiplies rows differently.",
                    Provable: true));
            }

            return;
        }

        var typesDiffer = original.JoinTypes
            .Select((type, i) => (type, Candidate: candidate.JoinTypes[i]))
            .Any(t => !EqualsIgnoreCase(t.type, t.Candidate));

        if (typesDiffer)
        {
            for (var i = 0; i < original.JoinTypes.Count; i++)
            {
                if (EqualsIgnoreCase(original.JoinTypes[i], candidate.JoinTypes[i]))
                {
                    continue;
                }

                provable.Add($"Join type differs at position {i + 1} ({original.JoinTypes[i]} vs {candidate.JoinTypes[i]}).");
                risks.Add(new SemanticRisk(
                    original.JoinTypes[i] != "Inner" || candidate.JoinTypes[i] != "Inner"
                        ? SemanticRiskType.OuterJoinSemantics
                        : SemanticRiskType.JoinCardinality,
                    "The join type changed; outer joins keep unmatched rows while inner joins drop them.",
                    Provable: true));
            }

            return;
        }

        if (original.HasOuterJoin || candidate.HasOuterJoin)
        {
            provable.Add("An outer join's FROM structure changed (join predicate or source); predicates on the nullable side of a LEFT/RIGHT/FULL join behave differently depending on where they are placed.");
            risks.Add(new SemanticRisk(SemanticRiskType.OuterJoinSemantics,
                "An outer join's predicate structure changed; moving a filter between ON and WHERE changes which NULL-extended rows survive.",
                Provable: true));
            return;
        }

        unverified.Add("The join predicate or FROM source structure changed; predicate rearrangements may or may not preserve filtering behavior under NULLs.");
        risks.Add(new SemanticRisk(SemanticRiskType.JoinCardinality,
            "Join predicates changed; NULL handling in join conditions can change the matched row set.",
            Provable: false));
    }

    /// <summary>Compares a canonical predicate (WHERE/HAVING) for presence and text equality.</summary>
    private static void ComparePredicate(
        string? original,
        string? candidate,
        string label,
        List<string> unverified,
        List<string> verified,
        List<SemanticRisk> risks,
        SemanticRisk changedRisk)
    {
        if (original is null && candidate is null)
        {
            return;
        }

        if (original is null || candidate is null)
        {
            unverified.Add($"{label} predicate presence differs (original: {(original is null ? "none" : "present")}, candidate: {(candidate is null ? "none" : "present")}).");
            risks.Add(changedRisk);
            return;
        }

        if (original != candidate)
        {
            unverified.Add($"{label} predicate differs after canonicalization: '{original}' vs '{candidate}'.");
            risks.Add(changedRisk);
            return;
        }

        verified.Add($"{label} predicate identical after canonicalization: {original}.");
    }

    /// <summary>Compares two deduplicated name sets (tables, parameters) case-insensitively.</summary>
    private static void CompareNameSets(
        IReadOnlyList<string> original,
        IReadOnlyList<string> candidate,
        Func<IReadOnlyList<string>, string> missingText,
        Func<IReadOnlyList<string>, string> addedText,
        Func<IReadOnlyList<string>, string> identicalText,
        List<string> provable,
        List<string> verified,
        List<SemanticRisk> risks,
        SemanticRisk provableRisk)
    {
        var missing = original.Except(candidate, StringComparer.OrdinalIgnoreCase).ToList();
        var added = candidate.Except(original, StringComparer.OrdinalIgnoreCase).ToList();

        if (missing.Count == 0 && added.Count == 0)
        {
            if (original.Count > 0)
            {
                verified.Add(identicalText(original));
            }

            return;
        }

        if (missing.Count > 0)
        {
            provable.Add(missingText(missing));
        }

        if (added.Count > 0)
        {
            provable.Add(addedText(added));
        }

        risks.Add(provableRisk);
    }

    /// <summary>
    /// Resolves the single base table by its <see cref="TableReference.FullName"/>
    /// (a bare name or a schema-qualified "schema.name"). The schema stores
    /// tables by bare name and verifies the schema qualifier when one is
    /// present, so a qualified FROM such as "dbo.Customers" must be split into
    /// a <see cref="TableReference"/> before lookup; the bare-name overload of
    /// <see cref="DatabaseSchema.FindTable(string)"/> would not match it.
    /// </summary>
    private static DatabaseTable? FindSingleBaseTable(DatabaseSchema schema, string singleBaseTable)
    {
        var dot = singleBaseTable.IndexOf('.');
        return dot < 0
            ? schema.FindTable(singleBaseTable)
            : schema.FindTable(new TableReference(singleBaseTable[..dot], singleBaseTable[(dot + 1)..], null));
    }

    /// <summary>
    /// Proves one position of a star expansion against schema metadata: the
    /// candidate item must be the i-th column of the single base table
    /// (name and, when present, a matching qualifier/alias).
    /// </summary>
    private static bool TryProveStarExpansion(
        QueryStructureSnapshot original,
        ProjectionStructure starItem,
        ProjectionStructure explicitItem,
        int index,
        DatabaseSchema schema)
    {
        if (original.SingleBaseTable is null)
        {
            return false;
        }

        var table = FindSingleBaseTable(schema, original.SingleBaseTable);
        if (table is null)
        {
            return false;
        }

        var expected = table.Columns.ElementAtOrDefault(index);
        if (expected is null)
        {
            return false;
        }

        if (starItem.StarTableAlias is not null
            && !EqualsIgnoreCase(starItem.StarTableAlias, original.SingleBaseTableQualifier))
        {
            return false;
        }

        if (explicitItem.Expression is not ColumnExpression column)
        {
            return false;
        }

        if (column.TableAlias is not null
            && !EqualsIgnoreCase(column.TableAlias, original.SingleBaseTableQualifier))
        {
            return false;
        }

        if (!EqualsIgnoreCase(column.Name, expected.Name))
        {
            return false;
        }

        return explicitItem.Alias is null || EqualsIgnoreCase(explicitItem.Alias, expected.Name);
    }

    /// <summary>
    /// Resolves COUNT(*) vs COUNT(column) equivalence using schema nullability.
    /// Returns true when proven equivalent (column NOT NULL), false when
    /// proven different (column nullable), null when undecidable (no schema,
    /// unknown or ambiguous column).
    /// </summary>
    private static bool? TryResolveCountEquivalence(
        SqlExpression? a,
        SqlExpression? b,
        DatabaseSchema? schema)
    {
        if (a is not AggregateExpression aggA || b is not AggregateExpression aggB)
        {
            return null;
        }

        if (!IsCount(aggA) || !IsCount(aggB) || aggA.CountStar == aggB.CountStar)
        {
            return null;
        }

        var countColumn = aggA.CountStar ? aggB : aggA;
        if (countColumn.Arguments.Count != 1 || countColumn.Arguments[0] is not ColumnExpression column)
        {
            return null;
        }

        if (schema is null)
        {
            return null;
        }

        var dbColumn = ResolveColumn(schema, column);
        return dbColumn is null ? null : !dbColumn.Nullable;
    }

    /// <summary>True for a plain COUNT(*) / COUNT(x) (no DISTINCT, no window).</summary>
    private static bool IsCount(AggregateExpression aggregate) =>
        string.Equals(aggregate.FunctionName, "COUNT", StringComparison.OrdinalIgnoreCase)
        && !aggregate.Distinct
        && aggregate.Window is null;

    /// <summary>
    /// Resolves a column reference against the schema. Qualified references
    /// match by table name (aliases are not in the schema metadata);
    /// unqualified references resolve only when exactly one table has the column.
    /// </summary>
    private static DatabaseColumn? ResolveColumn(DatabaseSchema schema, ColumnExpression column)
    {
        if (!string.IsNullOrEmpty(column.TableAlias))
        {
            return schema.FindTable(column.TableAlias)?
                .Columns.FirstOrDefault(c => EqualsIgnoreCase(c.Name, column.Name));
        }

        var matches = schema.Tables
            .Select(t => t.Columns.FirstOrDefault(c => EqualsIgnoreCase(c.Name, column.Name)))
            .Where(c => c is not null)
            .ToList();

        return matches.Count == 1 ? matches[0]! : null;
    }

    /// <summary>Joins items with a separator, or &lt;none&gt; when the list is empty.</summary>
    private static string JoinOrNone(IReadOnlyList<string> items, string separator = ", ") =>
        items.Count == 0 ? "<none>" : string.Join(separator, items);

    /// <summary>Order-sensitive list equality on canonical (already normalized) text.</summary>
    private static bool SameList(IReadOnlyList<string> a, IReadOnlyList<string> b) =>
        a.Count == b.Count
        && !a.Select((value, i) => (value, i)).Any(p => !p.value.Equals(b[p.i], StringComparison.Ordinal));

    /// <summary>
    /// The effective output column name of a projection item: the explicit
    /// alias when present, otherwise the column name for a bare column
    /// reference (its implicit name), otherwise null.
    /// </summary>
    private static string? EffectiveAlias(ProjectionStructure item) =>
        item.Alias ?? (item.IsStar || item.Expression is not ColumnExpression column ? null : column.Name);

    /// <summary>Case-insensitive string comparison (null-safe).</summary>
    private static bool EqualsIgnoreCase(string? a, string? b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Assembles the comparison result, deduplicating risks and affected objects.</summary>
    private static StructureComparison BuildComparison(
        List<string> provable,
        List<string> unverified,
        List<string> verified,
        List<SemanticRisk> risks,
        QueryStructureSnapshot original,
        QueryStructureSnapshot candidate) => new()
        {
            ProvableDifferences = provable,
            UnverifiedDifferences = unverified,
            VerifiedFacts = verified,
            Risks = risks.Distinct().ToList(),
            AffectedObjects = original.Tables
                .Concat(candidate.Tables)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
}

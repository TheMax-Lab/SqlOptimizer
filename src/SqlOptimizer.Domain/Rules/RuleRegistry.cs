using SqlOptimizer.Domain.Analysis;

namespace SqlOptimizer.Domain.Rules;

/// <summary>
/// Immutable registry of all available optimization rules. Duplicate rule
/// identifiers are rejected at construction time.
/// </summary>
public sealed class RuleRegistry
{
    private readonly IReadOnlyDictionary<string, ISqlOptimizationRule> _rulesById;

    /// <summary>
    /// Creates a registry from the given rules.
    /// </summary>
    /// <param name="rules">The rules to register.</param>
    /// <exception cref="ArgumentException">Two rules share the same identifier.</exception>
    public RuleRegistry(IEnumerable<ISqlOptimizationRule> rules)
    {
        var list = (rules ?? Array.Empty<ISqlOptimizationRule>()).ToList();

        var byId = new Dictionary<string, ISqlOptimizationRule>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in list)
        {
            if (rule is null)
            {
                continue;
            }

            if (!byId.TryAdd(rule.Id, rule))
            {
                throw new ArgumentException($"Duplicate rule identifier '{rule.Id}'.", nameof(rules));
            }
        }

        Rules = list.Where(r => r is not null).ToList();
        _rulesById = byId;
    }

    /// <summary>All registered rules.</summary>
    public IReadOnlyList<ISqlOptimizationRule> Rules { get; }

    /// <summary>
    /// Finds a rule by identifier (case insensitive), or null.
    /// </summary>
    /// <param name="id">Rule identifier.</param>
    public ISqlOptimizationRule? FindById(string id) =>
        _rulesById.TryGetValue(id, out var rule) ? rule : null;

    /// <summary>
    /// Executes every rule that can analyze the context and aggregates the
    /// findings. Rules run in a deterministic order (sorted by rule
    /// identifier, ordinal); rules that cannot apply to the context are
    /// skipped. Rules must return an empty sequence (never throw) when they
    /// detect no issues, so the pipeline never aborts on a single rule.
    /// </summary>
    /// <param name="context">The shared analysis context (parsed once).</param>
    public IReadOnlyList<SqlFinding> Analyze(SqlAnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var findings = new List<SqlFinding>();

        foreach (var rule in Rules.OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            if (!rule.CanAnalyze(context))
            {
                continue;
            }

            findings.AddRange(rule.Analyze(context));
        }

        return findings;
    }
}

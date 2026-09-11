using System.Text.Json.Serialization;

namespace SqlOptimizer.Domain.AST;

/// <summary>
/// Base type for FROM clause sources: base tables, derived tables (subqueries)
/// and joins. Joins form a tree of <see cref="JoinSource"/> nodes.
/// </summary>
[JsonPolymorphic]
[JsonDerivedType(typeof(TableReference))]
[JsonDerivedType(typeof(SubquerySource))]
[JsonDerivedType(typeof(JoinSource))]
public abstract record FromSource : SqlNode;

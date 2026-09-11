using System.Text.Json;
using System.Text.Json.Serialization;

namespace SqlOptimizer.Domain.AST;

/// <summary>
/// Base type for every node in the parser independent SQL abstract syntax tree.
/// The AST is produced once per query and shared by all analysis rules.
/// </summary>
/// <remarks>
/// <see cref="JsonUnknownDerivedTypeHandling.FallBackToNearestAncestor"/> is
/// required because the intermediate abstract bases
/// (<see cref="SqlExpression"/>, <see cref="FromSource"/>) are registered as
/// derived types: System.Text.Json (9.0) must be told how to serialize a
/// runtime type that is only reachable through such an intermediate base.
/// </remarks>
[JsonPolymorphic(UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FallBackToNearestAncestor)]
[JsonDerivedType(typeof(SelectStatement))]
[JsonDerivedType(typeof(SelectItem))]
[JsonDerivedType(typeof(FromClause))]
[JsonDerivedType(typeof(OrderByItem))]
[JsonDerivedType(typeof(SetOperation))]
[JsonDerivedType(typeof(CommonTableExpression))]
[JsonDerivedType(typeof(WindowFunction))]
[JsonDerivedType(typeof(CaseWhenClause))]
[JsonDerivedType(typeof(SqlExpression), "expression")]
[JsonDerivedType(typeof(FromSource), "fromSource")]
public abstract record SqlNode
{
    /// <summary>
    /// Deterministic child nodes of this node, used by walkers and visitors.
    /// Ignored during JSON serialization; the typed properties already carry
    /// the same information.
    /// </summary>
    [JsonIgnore]
    public abstract IEnumerable<SqlNode> Children { get; }
}

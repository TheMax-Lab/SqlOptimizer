using System.Text.Json.Serialization;

namespace SqlOptimizer.Domain.AST;

/// <summary>
/// Base type for all SQL scalar/boolean expressions in the AST.
/// </summary>
[JsonPolymorphic]
[JsonDerivedType(typeof(ColumnExpression))]
[JsonDerivedType(typeof(LiteralExpression))]
[JsonDerivedType(typeof(ParameterExpression))]
[JsonDerivedType(typeof(FunctionExpression))]
[JsonDerivedType(typeof(AggregateExpression))]
[JsonDerivedType(typeof(BinaryExpression))]
[JsonDerivedType(typeof(UnaryExpression))]
[JsonDerivedType(typeof(InExpression))]
[JsonDerivedType(typeof(ExistsExpression))]
[JsonDerivedType(typeof(LikeExpression))]
[JsonDerivedType(typeof(CaseExpression))]
[JsonDerivedType(typeof(SubqueryExpression))]
[JsonDerivedType(typeof(CastExpression))]
public abstract record SqlExpression : SqlNode;

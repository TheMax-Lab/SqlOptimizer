namespace SqlOptimizer.Domain.AST.Visitors;

/// <summary>
/// Visitor over the SQL AST. The base implementation traverses all children;
/// rules derive from this type and override only the node kinds they care
/// about. All traversal logic lives here so rules never re-implement walks.
/// </summary>
public abstract class SqlAstVisitor
{
    /// <summary>
    /// Dispatches a node to its specific visit method.
    /// </summary>
    /// <param name="node">The node to visit.</param>
    public virtual void Visit(SqlNode? node)
    {
        if (node is null)
        {
            return;
        }

        switch (node)
        {
            case SelectStatement selectStatement:
                VisitSelectStatement(selectStatement);
                break;
            case SelectItem selectItem:
                VisitSelectItem(selectItem);
                break;
            case FromClause fromClause:
                VisitFromClause(fromClause);
                break;
            case TableReference tableReference:
                VisitTableReference(tableReference);
                break;
            case SubquerySource subquerySource:
                VisitSubquerySource(subquerySource);
                break;
            case JoinSource joinSource:
                VisitJoinSource(joinSource);
                break;
            case OrderByItem orderByItem:
                VisitOrderByItem(orderByItem);
                break;
            case SetOperation setOperation:
                VisitSetOperation(setOperation);
                break;
            case CommonTableExpression cte:
                VisitCommonTableExpression(cte);
                break;
            case WindowFunction windowFunction:
                VisitWindowFunction(windowFunction);
                break;
            case CaseWhenClause caseWhenClause:
                VisitCaseWhenClause(caseWhenClause);
                break;
            case ColumnExpression columnExpression:
                VisitColumnExpression(columnExpression);
                break;
            case LiteralExpression literalExpression:
                VisitLiteralExpression(literalExpression);
                break;
            case ParameterExpression parameterExpression:
                VisitParameterExpression(parameterExpression);
                break;
            case FunctionExpression functionExpression:
                VisitFunctionExpression(functionExpression);
                break;
            case AggregateExpression aggregateExpression:
                VisitAggregateExpression(aggregateExpression);
                break;
            case BinaryExpression binaryExpression:
                VisitBinaryExpression(binaryExpression);
                break;
            case UnaryExpression unaryExpression:
                VisitUnaryExpression(unaryExpression);
                break;
            case InExpression inExpression:
                VisitInExpression(inExpression);
                break;
            case ExistsExpression existsExpression:
                VisitExistsExpression(existsExpression);
                break;
            case LikeExpression likeExpression:
                VisitLikeExpression(likeExpression);
                break;
            case CaseExpression caseExpression:
                VisitCaseExpression(caseExpression);
                break;
            case SubqueryExpression subqueryExpression:
                VisitSubqueryExpression(subqueryExpression);
                break;
            case CastExpression castExpression:
                VisitCastExpression(castExpression);
                break;
            default:
                VisitSqlNode(node);
                break;
        }
    }

    /// <summary>Fallback visit: traverses children only.</summary>
    /// <param name="node">The node.</param>
    protected virtual void VisitSqlNode(SqlNode node) => VisitChildren(node);

    /// <summary>Traverses the children of the given node.</summary>
    /// <param name="node">The node whose children are visited.</param>
    protected virtual void VisitChildren(SqlNode node)
    {
        foreach (var child in node.Children)
        {
            Visit(child);
        }
    }

    /// <summary>Visits a <see cref="SelectStatement"/>.</summary>
    public virtual void VisitSelectStatement(SelectStatement node) => VisitChildren(node);

    /// <summary>Visits a <see cref="SelectItem"/>.</summary>
    public virtual void VisitSelectItem(SelectItem node) => VisitChildren(node);

    /// <summary>Visits a <see cref="FromClause"/>.</summary>
    public virtual void VisitFromClause(FromClause node) => VisitChildren(node);

    /// <summary>Visits a <see cref="TableReference"/>.</summary>
    public virtual void VisitTableReference(TableReference node) => VisitChildren(node);

    /// <summary>Visits a <see cref="SubquerySource"/>.</summary>
    public virtual void VisitSubquerySource(SubquerySource node) => VisitChildren(node);

    /// <summary>Visits a <see cref="JoinSource"/>.</summary>
    public virtual void VisitJoinSource(JoinSource node) => VisitChildren(node);

    /// <summary>Visits an <see cref="OrderByItem"/>.</summary>
    public virtual void VisitOrderByItem(OrderByItem node) => VisitChildren(node);

    /// <summary>Visits a <see cref="SetOperation"/>.</summary>
    public virtual void VisitSetOperation(SetOperation node) => VisitChildren(node);

    /// <summary>Visits a <see cref="CommonTableExpression"/>.</summary>
    public virtual void VisitCommonTableExpression(CommonTableExpression node) => VisitChildren(node);

    /// <summary>Visits a <see cref="WindowFunction"/> (OVER clause).</summary>
    public virtual void VisitWindowFunction(WindowFunction node) => VisitChildren(node);

    /// <summary>Visits a <see cref="CaseWhenClause"/>.</summary>
    public virtual void VisitCaseWhenClause(CaseWhenClause node) => VisitChildren(node);

    /// <summary>Visits a <see cref="ColumnExpression"/>.</summary>
    public virtual void VisitColumnExpression(ColumnExpression node) => VisitChildren(node);

    /// <summary>Visits a <see cref="LiteralExpression"/>.</summary>
    public virtual void VisitLiteralExpression(LiteralExpression node) => VisitChildren(node);

    /// <summary>Visits a <see cref="ParameterExpression"/>.</summary>
    public virtual void VisitParameterExpression(ParameterExpression node) => VisitChildren(node);

    /// <summary>Visits a <see cref="FunctionExpression"/>.</summary>
    public virtual void VisitFunctionExpression(FunctionExpression node) => VisitChildren(node);

    /// <summary>Visits an <see cref="AggregateExpression"/>.</summary>
    public virtual void VisitAggregateExpression(AggregateExpression node) => VisitChildren(node);

    /// <summary>Visits a <see cref="BinaryExpression"/>.</summary>
    public virtual void VisitBinaryExpression(BinaryExpression node) => VisitChildren(node);

    /// <summary>Visits a <see cref="UnaryExpression"/>.</summary>
    public virtual void VisitUnaryExpression(UnaryExpression node) => VisitChildren(node);

    /// <summary>Visits an <see cref="InExpression"/>.</summary>
    public virtual void VisitInExpression(InExpression node) => VisitChildren(node);

    /// <summary>Visits an <see cref="ExistsExpression"/>.</summary>
    public virtual void VisitExistsExpression(ExistsExpression node) => VisitChildren(node);

    /// <summary>Visits a <see cref="LikeExpression"/>.</summary>
    public virtual void VisitLikeExpression(LikeExpression node) => VisitChildren(node);

    /// <summary>Visits a <see cref="CaseExpression"/>.</summary>
    public virtual void VisitCaseExpression(CaseExpression node) => VisitChildren(node);

    /// <summary>Visits a <see cref="SubqueryExpression"/>.</summary>
    public virtual void VisitSubqueryExpression(SubqueryExpression node) => VisitChildren(node);

    /// <summary>Visits a <see cref="CastExpression"/>.</summary>
    public virtual void VisitCastExpression(CastExpression node) => VisitChildren(node);
}

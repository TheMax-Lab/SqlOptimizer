using Microsoft.Extensions.DependencyInjection;
using SqlOptimizer.Domain.Analysis;
using SqlOptimizer.Domain.Rules;
using SqlOptimizer.Rules.Scoring;

namespace SqlOptimizer.Rules;

/// <summary>
/// Registers the deterministic rule engine (all 20 rules, the rule registry
/// and the score engine) for dependency injection. Rules are stateless and
/// deterministic, so they are safe to share as singletons.
/// </summary>
public static class SqlOptimizerRulesServiceCollectionExtensions
{
    /// <summary>
    /// Registers every deterministic optimization rule, the
    /// <see cref="RuleRegistry"/> and the <see cref="IScoreEngine"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddSqlOptimizerRules(this IServiceCollection services)
    {
        services.AddSingleton<ISqlOptimizationRule, SelectStarRule>();
        services.AddSingleton<ISqlOptimizationRule, FunctionOnColumnRule>();
        services.AddSingleton<ISqlOptimizationRule, NonSargablePredicateRule>();
        services.AddSingleton<ISqlOptimizationRule, LeadingWildcardLikeRule>();
        services.AddSingleton<ISqlOptimizationRule, ImplicitConversionRule>();
        services.AddSingleton<ISqlOptimizationRule, OrPredicateRule>();
        services.AddSingleton<ISqlOptimizationRule, CorrelatedSubqueryRule>();
        services.AddSingleton<ISqlOptimizationRule, NotInNullableRule>();
        services.AddSingleton<ISqlOptimizationRule, DistinctRule>();
        services.AddSingleton<ISqlOptimizationRule, UnionRule>();
        services.AddSingleton<ISqlOptimizationRule, RedundantOrderByRule>();
        services.AddSingleton<ISqlOptimizationRule, CartesianJoinRule>();
        services.AddSingleton<ISqlOptimizationRule, LeftJoinFilterRule>();
        services.AddSingleton<ISqlOptimizationRule, ExcessiveSubqueryRule>();
        services.AddSingleton<ISqlOptimizationRule, LargeInListRule>();
        services.AddSingleton<ISqlOptimizationRule, UnnecessaryCastRule>();
        services.AddSingleton<ISqlOptimizationRule, DuplicateExpressionRule>();
        services.AddSingleton<ISqlOptimizationRule, PotentialJoinExplosionRule>();
        services.AddSingleton<ISqlOptimizationRule, MissingJoinPredicateRule>();
        services.AddSingleton<ISqlOptimizationRule, ExcessiveFunctionUsageRule>();

        services.AddSingleton<RuleRegistry>();
        services.AddSingleton<IScoreEngine, DeterministicScoreEngine>();

        return services;
    }
}
namespace SqlOptimizer.Domain.Analysis;

/// <summary>Category of a finding.</summary>
public enum FindingCategory
{
    /// <summary>General performance.</summary>
    Performance,

    /// <summary>Predicate sargability (index seekability).</summary>
    Sargability,

    /// <summary>Join related issues.</summary>
    Join,

    /// <summary>Index related issues.</summary>
    Index,

    /// <summary>Row count / cardinality issues.</summary>
    Cardinality,

    /// <summary>Maintainability concerns.</summary>
    Maintainability,

    /// <summary>Potential correctness issues.</summary>
    Correctness
}

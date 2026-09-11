using System.Xml;
using SqlOptimizer.Domain.Common;

namespace SqlOptimizer.Domain.Metadata;

/// <summary>
/// Parses SQL Server execution plan XML into an <see cref="ExecutionPlan"/>.
/// Only SQL Server plan documents are supported; this is the dialect the MVP
/// implements. The parser is tolerant: unknown operators are skipped, a
/// document with no recognizable operators yields an empty operator list.
/// </summary>
public static class ExecutionPlanParser
{
    private const string ShowPlanNamespace = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

    /// <summary>
    /// Parses a SQL Server execution plan XML document.
    /// </summary>
    /// <param name="rawPlan">Plan XML text.</param>
    /// <exception cref="SqlParseException">The XML is not a valid plan document.</exception>
    public static ExecutionPlan Parse(string rawPlan)
    {
        if (string.IsNullOrWhiteSpace(rawPlan))
        {
            throw new SqlParseException("Execution plan is empty.");
        }

        XmlDocument document;
        try
        {
            document = new XmlDocument();
            document.LoadXml(rawPlan);
        }
        catch (XmlException ex)
        {
            throw new SqlParseException("Execution plan is not valid XML.", ex);
        }

        var operators = new List<PlanOperator>();

        // local-name() avoids namespace strictness across plan versions.
        var elements = document.SelectNodes(
            "//*[local-name()='RelOp']",
            new XmlNamespaceManager(document.NameTable));

        if (elements is null)
        {
            return new ExecutionPlan(rawPlan, operators);
        }

        foreach (XmlNode? element in elements)
        {
            if (element is not XmlElement relOp)
            {
                continue;
            }

            // Real SQL Server showplan XML names the operator attribute
            // "PhysicalOp" (for example PhysicalOp="Index Scan"); the parser
            // was originally written against a synthetic "PhysicalOpName"
            // attribute that never appears in a real plan, so live plans
            // parsed to zero operators. Read the real attribute first and keep
            // "PhysicalOpName" as a fallback for the synthetic offline
            // fixtures used by the unit tests.
            var operatorType = relOp.GetAttribute("PhysicalOp");
            if (string.IsNullOrEmpty(operatorType))
            {
                operatorType = relOp.GetAttribute("PhysicalOpName");
            }
            if (string.IsNullOrEmpty(operatorType))
            {
                continue;
            }

            operators.Add(new PlanOperator(
                OperatorType: operatorType,
                EstimatedCost: ParseEstimatedCost(relOp),
                EstimatedRows: ParseLong(relOp.GetAttribute("EstimateRows")),
                ActualRows: ParseLong(relOp.GetAttribute("ActualRows")),
                ObjectName: GetObjectName(relOp)));
        }

        return new ExecutionPlan(rawPlan, operators);
    }

    /// <summary>
    /// Extracts the object name of an operator when present (table/index).
    /// </summary>
    /// <param name="relOp">The RelOp element.</param>
    private static string? GetObjectName(XmlElement relOp)
    {
        var objectElement = relOp.SelectSingleNode(".//*[local-name()='Object']");
        if (objectElement is not XmlElement objectElementElement)
        {
            return null;
        }

        var schema = objectElementElement.GetAttribute("Schema");
        var table = objectElementElement.GetAttribute("Table");
        var index = objectElementElement.GetAttribute("Index");
        var name = objectElementElement.GetAttribute("Name");

        var qualified = string.IsNullOrEmpty(table) ? name : (string.IsNullOrEmpty(schema) ? table : $"{schema}.{table}");
        if (string.IsNullOrEmpty(qualified))
        {
            return null;
        }

        return string.IsNullOrEmpty(index) ? qualified : $"{qualified} ({index})";
    }

    /// <summary>
    /// Reads the estimated cost of an operator. SQL Server plans report the
    /// cost as <c>EstimateCPU</c> and <c>EstimateIO</c> attributes (there is no
    /// single <c>EstimateCost</c> attribute on real plans); when an explicit
    /// <c>EstimateCost</c> attribute is present it takes precedence. Returns 0
    /// when no cost information is available.
    /// </summary>
    /// <param name="relOp">The RelOp element.</param>
    private static double ParseEstimatedCost(XmlElement relOp)
    {
        if (TryParseDouble(relOp.GetAttribute("EstimateCost"), out var explicitCost))
        {
            return explicitCost;
        }

        TryParseDouble(relOp.GetAttribute("EstimateCPU"), out var cpu);
        TryParseDouble(relOp.GetAttribute("EstimateIO"), out var io);
        return cpu + io;
    }

    /// <summary>Parses a double attribute value, reporting success.</summary>
    /// <param name="value">Raw attribute value.</param>
    /// <param name="result">The parsed value when <c>true</c> is returned.</param>
    private static bool TryParseDouble(string value, out double result) =>
        double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out result);

    /// <summary>Parses a long attribute value, or null when absent/invalid.</summary>
    /// <param name="value">Raw attribute value.</param>
    private static long? ParseLong(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var asDouble))
        {
            return (long)asDouble;
        }

        return long.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var asLong)
            ? asLong
            : null;
    }
}

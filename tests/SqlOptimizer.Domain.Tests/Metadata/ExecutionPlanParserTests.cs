using FluentAssertions;
using SqlOptimizer.Domain.Common;
using SqlOptimizer.Domain.Metadata;
using Xunit;

namespace SqlOptimizer.Domain.Tests.Metadata;

/// <summary>
/// SQL Server execution plan XML parsing: operators in document order,
/// cost/rows/object extraction, namespace tolerance, and controlled errors
/// for empty or invalid XML (the parser must never crash with uncontrolled
/// exceptions).
/// </summary>
public class ExecutionPlanParserTests
{
    /// <summary>A realistic ShowPlanXML document (with the showplan namespace).</summary>
    private const string NamespacedPlan = """
        <?xml version="1.0" encoding="utf-8"?>
        <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan" Version="1.6" Build="16.0.4105.1">
          <BatchSequence>
            <Batch StatementsExecutionOrder="PlanFirst" CompilerVersion="1601031529">
              <Statement>
                <SimpleOperator>
                  <RelOp NodeId="0" PhysicalOpName="Compute Scalar" EstimateCPU="0.000125" EstimateIO="0.000125" EstimateRows="4" EstimateRereads="0" />
                  <RelOp NodeId="1" PhysicalOpName="Clustered Index Scan" EstimateCPU="0.000126" EstimateIO="0.000125" EstimateRows="4" EstimateRereads="0">
                    <OutputList>
                      <OutputColumn Name="Id" ColumnReference="2:0" />
                    </OutputList>
                    <Object Database="[TestDb]" Schema="[dbo]" Table="[Customers]" Index="[PK_Customers]" IndexKind="Clustered" />
                  </RelOp>
                </SimpleOperator>
              </Statement>
            </Batch>
          </BatchSequence>
        </ShowPlanXML>
        """;

    [Fact]
    public void ValidPlan_ParsesOperatorsInDocumentOrder()
    {
        var plan = ExecutionPlanParser.Parse(NamespacedPlan);

        plan.RawPlan.Should().Be(NamespacedPlan);
        plan.Operators.Should().HaveCount(2);

        var first = plan.Operators[0];
        first.OperatorType.Should().Be("Compute Scalar");
        first.EstimatedCost.Should().BeApproximately(0.00025d, 1e-10);
        first.EstimatedRows.Should().Be(4);
        first.ActualRows.Should().BeNull();
        first.ObjectName.Should().BeNull();

        var second = plan.Operators[1];
        second.OperatorType.Should().Be("Clustered Index Scan");
        second.EstimatedRows.Should().Be(4);
        second.ObjectName.Should().Be("[dbo].[Customers] ([PK_Customers])");
    }

    [Fact]
    public void ExplicitEstimateCostAttribute_TakesPrecedenceOverCpuAndIo()
    {
        const string plan = """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan">
              <RelOp PhysicalOpName="Hash Match" EstimateCost="0.5" EstimateCPU="0.1" EstimateIO="0.2" />
            </ShowPlanXML>
            """;

        var parsed = ExecutionPlanParser.Parse(plan);

        parsed.Operators.Single().EstimatedCost.Should().Be(0.5d);
    }

    [Fact]
    public void CostWithoutAttributes_IsZero()
    {
        const string plan = """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan">
              <RelOp PhysicalOpName="Table Scan" EstimateRows="10" />
            </ShowPlanXML>
            """;

        var parsed = ExecutionPlanParser.Parse(plan);

        parsed.Operators.Single().EstimatedCost.Should().Be(0d);
        parsed.Operators.Single().EstimatedRows.Should().Be(10);
    }

    [Fact]
    public void ActualRows_AreExtractedWhenPresent()
    {
        const string plan = """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan">
              <RelOp PhysicalOpName="Index Scan" EstimateRows="100" ActualRows="42" />
            </ShowPlanXML>
            """;

        var parsed = ExecutionPlanParser.Parse(plan);

        var op = parsed.Operators.Single();
        op.ActualRows.Should().Be(42);
        op.EstimatedRows.Should().Be(100);
    }

    [Fact]
    public void ObjectWithoutIndex_SchemaQualifiedTable()
    {
        const string plan = """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan">
              <RelOp PhysicalOpName="Table Scan">
                <Object Schema="[dbo]" Table="[Orders]" />
              </RelOp>
            </ShowPlanXML>
            """;

        var parsed = ExecutionPlanParser.Parse(plan);

        parsed.Operators.Single().ObjectName.Should().Be("[dbo].[Orders]");
    }

    [Fact]
    public void ObjectWithoutSchema_UnqualifiedTable()
    {
        const string plan = """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan">
              <RelOp PhysicalOpName="Table Scan">
                <Object Table="Orders" />
              </RelOp>
            </ShowPlanXML>
            """;

        var parsed = ExecutionPlanParser.Parse(plan);

        parsed.Operators.Single().ObjectName.Should().Be("Orders");
    }

    [Fact]
    public void PlanWithoutNamespace_StillParses()
    {
        const string plan = """
            <ShowPlanXML>
              <RelOp PhysicalOpName="Index Scan" EstimateCPU="0.25" EstimateIO="0.5" EstimateRows="7" />
            </ShowPlanXML>
            """;

        var parsed = ExecutionPlanParser.Parse(plan);

        parsed.Operators.Should().ContainSingle()
            .Which.Should().Be(new PlanOperator("Index Scan", 0.75d, 7L, null, null));
    }

    [Fact]
    public void ValidXmlWithoutOperators_YieldsEmptyOperatorList()
    {
        const string plan = """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan">
              <BatchSequence><Batch><Statement /></Batch></BatchSequence>
            </ShowPlanXML>
            """;

        var parsed = ExecutionPlanParser.Parse(plan);

        parsed.Operators.Should().BeEmpty();
        parsed.RawPlan.Should().Be(plan);
    }

    [Fact]
    public void OperatorsWithoutPhysicalOpName_AreSkipped()
    {
        const string plan = """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan">
              <RelOp EstimateRows="99" />
              <RelOp PhysicalOpName="Table Scan" EstimateRows="5" />
            </ShowPlanXML>
            """;

        var parsed = ExecutionPlanParser.Parse(plan);

        parsed.Operators.Should().ContainSingle().Which.OperatorType.Should().Be("Table Scan");
    }

    [Fact]
    public void InvalidNumericAttributes_DegradeGracefully()
    {
        const string plan = """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan">
              <RelOp PhysicalOpName="Table Scan" EstimateCost="not-a-number" EstimateRows="abc" />
            </ShowPlanXML>
            """;

        var parsed = ExecutionPlanParser.Parse(plan);

        var op = parsed.Operators.Single();
        op.EstimatedCost.Should().Be(0d);
        op.EstimatedRows.Should().BeNull();
    }

    [Fact]
    public void InvalidXml_ThrowsSqlParseException()
    {
        Action act = () => ExecutionPlanParser.Parse("<ShowPlanXML><unclosed></ShowPlanXML>");

        act.Should().Throw<SqlParseException>()
            .WithMessage("*not valid XML*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void EmptyOrBlankInput_ThrowsSqlParseException(string? input)
    {
        Action act = () => ExecutionPlanParser.Parse(input!);

        act.Should().Throw<SqlParseException>()
            .WithMessage("*empty*");
    }
}
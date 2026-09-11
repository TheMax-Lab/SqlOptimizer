using FluentAssertions;
using SqlOptimizer.Application.Services;
using Xunit;

namespace SqlOptimizer.Application.Tests.Optimization;

/// <summary>
/// Defensive parsing of untrusted LLM output: valid multi-candidate and
/// legacy responses parse; malformed JSON, missing/empty SQL, invalid
/// confidence, invalid objects and excessive counts are rejected — never
/// converted into candidates, never thrown.
/// </summary>
public class LlmResponseParserTests
{
    private readonly LlmResponseParser _parser = new();

    [Fact]
    public void ValidSingleCandidate_Parses()
    {
        var result = _parser.Parse(
            """
            {"candidates":[{"sql":"SELECT Id FROM Customers","explanation":"narrower","expectedImpact":"I/O","confidence":0.8}]}
            """);

        result.Should().NotBeNull();
        result!.Candidates.Should().HaveCount(1);
        result.Candidates[0].Sql.Should().Be("SELECT Id FROM Customers");
        result.Candidates[0].Explanation.Should().Be("narrower");
        result.Candidates[0].ExpectedImpact.Should().Be("I/O");
        result.Candidates[0].Confidence.Should().Be(0.8);
        result.Rejections.Should().BeEmpty();
    }

    [Fact]
    public void ValidMultipleCandidates_Parses()
    {
        var result = _parser.Parse(
            """
            {"candidates":[
              {"sql":"SELECT Id FROM Customers","confidence":0.9},
              {"sql":"SELECT Id FROM Customers WHERE Id > 0","confidence":0.7}
            ]}
            """);

        result.Should().NotBeNull();
        result!.Candidates.Should().HaveCount(2);
    }

    [Fact]
    public void MarkdownFencedJson_Parses()
    {
        var result = _parser.Parse(
            "Here is the result:\n```json\n{\"candidates\":[{\"sql\":\"SELECT 1\",\"confidence\":1}]}\n```\nDone.");

        result.Should().NotBeNull();
        result!.Candidates.Should().HaveCount(1);
    }

    [Fact]
    public void LegacySingleShape_ParsesAsOneCandidate()
    {
        var result = _parser.Parse(
            """
            {"optimizedSql":"SELECT Id FROM Customers","confidence":0.6}
            """);

        result.Should().NotBeNull();
        result!.Candidates.Should().HaveCount(1);
        result.Candidates[0].Sql.Should().Be("SELECT Id FROM Customers");
    }

    [Fact]
    public void MissingConfidence_DefaultsToHalf()
    {
        var result = _parser.Parse("""{"candidates":[{"sql":"SELECT 1"}]}""");

        result.Should().NotBeNull();
        result!.Candidates[0].Confidence.Should().Be(0.5);
    }

    [Fact]
    public void MalformedJson_ReturnsNull()
    {
        _parser.Parse("{not valid json").Should().BeNull();
        _parser.Parse("no json at all").Should().BeNull();
        _parser.Parse("").Should().BeNull();
    }

    [Fact]
    public void MissingSql_RejectsEntry_ReturnsNullWhenNothingUsable()
    {
        _parser.Parse("""{"candidates":[{"explanation":"no sql here","confidence":0.5}]}""").Should().BeNull();
    }

    [Fact]
    public void EmptySql_RejectsEntry()
    {
        var result = _parser.Parse(
            """
            {"candidates":[{"sql":"   "},{"sql":"SELECT 1","confidence":0.5}]}
            """);

        result.Should().NotBeNull();
        result!.Candidates.Should().HaveCount(1);
        result.Rejections.Should().ContainSingle().Which.Should().Contain("empty SQL");
    }

    [Fact]
    public void InvalidConfidence_RejectsEntry()
    {
        _parser.Parse("""{"candidates":[{"sql":"SELECT 1","confidence":1.5}]}""").Should().BeNull();
        _parser.Parse("""{"candidates":[{"sql":"SELECT 1","confidence":"high"}]}""").Should().BeNull();
        _parser.Parse("""{"candidates":[{"sql":"SELECT 1","confidence":-0.1}]}""").Should().BeNull();
    }

    [Fact]
    public void NonObjectEntry_IsRejected()
    {
        var result = _parser.Parse(
            """
            {"candidates":["not an object",{"sql":"SELECT 1","confidence":0.5}]}
            """);

        result.Should().NotBeNull();
        result!.Candidates.Should().HaveCount(1);
        result.Rejections.Should().ContainSingle().Which.Should().Contain("not a JSON object");
    }

    [Fact]
    public void ExcessiveCandidateCount_TruncatesAndRecordsRejection()
    {
        var items = string.Join(",", Enumerable.Range(1, 5).Select(i => $"{{\"sql\":\"SELECT {i}\",\"confidence\":0.5}}"));
        var result = _parser.Parse($"{{\"candidates\":[{items}]}}", maxCandidates: 2);

        result.Should().NotBeNull();
        result!.Candidates.Should().HaveCount(2);
        result.Rejections.Should().HaveCount(3);
        result.Rejections.Should().OnlyContain(r => r.Contains("maximum of 2"));
    }

    [Fact]
    public void EmptyCandidateList_ReturnsNull()
    {
        _parser.Parse("""{"candidates":[]}""").Should().BeNull();
    }
}

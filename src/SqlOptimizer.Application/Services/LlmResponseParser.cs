using System.Globalization;
using System.Text.Json;
using SqlOptimizer.Application.DTOs;

namespace SqlOptimizer.Application.Services;

/// <summary>
/// Parses and validates raw LLM output into an <see cref="LlmOptimizationResponse"/>.
/// The LLM output is untrusted and never trusted: malformed JSON, missing or
/// empty SQL, invalid confidence values, oversized payloads and excessive
/// candidate counts are rejected defensively. Invalid entries are discarded
/// with a recorded reason (never converted into candidates), and when no
/// usable candidate remains the parser returns <c>null</c> (never an
/// exception), so a bad response can not crash the pipeline.
/// </summary>
public sealed class LlmResponseParser
{
    /// <summary>Default cap on the number of candidates accepted from one response.</summary>
    public const int DefaultMaxCandidates = 10;

    /// <summary>Maximum length of a single candidate SQL text.</summary>
    public const int MaxCandidateSqlLength = 200_000;

    /// <summary>Maximum length of free-text fields (explanation, impact, notes).</summary>
    private const int MaxTextFieldLength = 4_000;

    /// <summary>Confidence assigned when the model did not report one.</summary>
    private const double DefaultConfidence = 0.5;

    /// <summary>
    /// Parses the raw LLM completion text.
    /// </summary>
    /// <param name="content">Raw completion text (untrusted).</param>
    /// <param name="maxCandidates">Maximum number of candidates to accept.</param>
    /// <returns>The parsed response, or null when no usable candidate is present.</returns>
    public LlmOptimizationResponse? Parse(string content, int maxCandidates = DefaultMaxCandidates)
    {
        if (string.IsNullOrWhiteSpace(content) || maxCandidates < 1)
        {
            return null;
        }

        var json = ExtractJson(content);
        if (json is null)
        {
            return null;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var rejections = new List<string>();
            var candidates = new List<LlmCandidate>();

            if (TryGetProperty(root, "candidates", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var item in list.EnumerateArray())
                {
                    index++;
                    if (candidates.Count >= maxCandidates)
                    {
                        rejections.Add($"Candidate {index} discarded: the response exceeds the maximum of {maxCandidates} candidates.");
                        continue;
                    }

                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        rejections.Add($"Candidate {index} discarded: entry is not a JSON object.");
                        continue;
                    }

                    TryReadCandidate(item, index, legacy: false, rejections, candidates);
                }
            }
            else if (TryGetProperty(root, "optimizedSql", out var legacySql) &&
                     legacySql.ValueKind == JsonValueKind.String)
            {
                // Legacy single-candidate shape, accepted for compatibility.
                TryReadCandidate(root, 1, legacy: true, rejections, candidates);
            }

            return candidates.Count == 0
                ? null
                : new LlmOptimizationResponse(candidates) { Rejections = rejections };
        }
    }

    /// <summary>
    /// Validates and converts one JSON entry into a candidate, or records a
    /// rejection reason. Never throws on malformed entries.
    /// </summary>
    private static void TryReadCandidate(
        JsonElement item,
        int index,
        bool legacy,
        List<string> rejections,
        List<LlmCandidate> candidates)
    {
        var sqlName = legacy ? "optimizedSql" : "sql";
        if (!TryGetProperty(item, sqlName, out var sqlElement) || sqlElement.ValueKind != JsonValueKind.String)
        {
            rejections.Add($"Candidate {index} discarded: missing or non-string '{sqlName}'.");
            return;
        }

        var sql = sqlElement.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(sql))
        {
            rejections.Add($"Candidate {index} discarded: empty SQL.");
            return;
        }

        if (sql.Length > MaxCandidateSqlLength)
        {
            rejections.Add($"Candidate {index} discarded: SQL longer than {MaxCandidateSqlLength} characters.");
            return;
        }

        if (!TryReadConfidence(item, out var confidence))
        {
            rejections.Add($"Candidate {index} discarded: missing or invalid confidence value (must be a number between 0 and 1).");
            return;
        }

        candidates.Add(new LlmCandidate(
            sql,
            ReadText(item, "explanation"),
            ReadText(item, "expectedImpact"),
            confidence,
            ReadStringArray(item, "warnings"),
            ReadStringArray(item, "assumptions")));
    }

    /// <summary>
    /// Reads the confidence value. A missing value defaults to
    /// <see cref="DefaultConfidence"/>; an unparseable value or a value
    /// outside 0-1 is invalid and rejected.
    /// </summary>
    private static bool TryReadConfidence(JsonElement item, out double confidence)
    {
        if (!TryGetProperty(item, "confidence", out var element))
        {
            confidence = DefaultConfidence;
            return true;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Number when element.TryGetDouble(out var number) && number is >= 0 and <= 1:
                confidence = number;
                return true;

            case JsonValueKind.String when double.TryParse(
                element.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed) && parsed is >= 0 and <= 1:
                confidence = parsed;
                return true;

            default:
                confidence = 0;
                return false;
        }
    }

    /// <summary>
    /// Reads an optional single string property (case insensitive), trimmed
    /// and capped; null when absent or not a string.
    /// </summary>
    private static string? ReadText(JsonElement item, string name)
    {
        if (!TryGetProperty(item, name, out var element) || element.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = element.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Length > MaxTextFieldLength ? value[..MaxTextFieldLength] : value;
    }

    /// <summary>
    /// Extracts the outermost JSON object from the response text, tolerating
    /// markdown code fences or explanatory prose around it.
    /// </summary>
    /// <param name="content">Raw completion text.</param>
    private static string? ExtractJson(string content)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');

        if (start < 0 || end <= start)
        {
            return null;
        }

        return content[start..(end + 1)];
    }

    /// <summary>
    /// Reads a property by name case-insensitively.
    /// </summary>
    private static bool TryGetProperty(JsonElement root, string name, out JsonElement element)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                element = property.Value;
                return true;
            }
        }

        element = default;
        return false;
    }

    /// <summary>
    /// Reads a string array property (case insensitive), ignoring invalid entries.
    /// </summary>
    private static IReadOnlyList<string> ReadStringArray(JsonElement root, string name)
    {
        if (!TryGetProperty(root, name, out var element) || element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var values = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind is JsonValueKind.String or JsonValueKind.Number)
            {
                var value = item.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    values.Add(value.Trim());
                }
            }
        }

        return values;
    }
}

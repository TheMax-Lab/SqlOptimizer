namespace SqlOptimizer.Desktop.Engine.Support;

/// <summary>
/// Redacts configured secrets (connection string, LLM API key) from any text
/// before it is written to stderr or returned to the client. The connection
/// string and API key are the only values the host treats as secret; they are
/// replaced with a fixed marker so a leak can never surface in logs or error
/// messages.
/// </summary>
public static class SecretRedactor
{
    private static readonly object Gate = new();

    private static string[] _secrets = Array.Empty<string>();

    /// <summary>
    /// Registers the secret values to redact. Empty or whitespace values are
    /// ignored. Call once after configuration is known.
    /// </summary>
    /// <param name="secrets">Secret values (connection string, API key, ...).</param>
    public static void SetSecrets(params string?[] secrets)
    {
        var meaningful = (secrets ?? Array.Empty<string?>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!)
            .Distinct()
            .ToArray();

        lock (Gate)
        {
            _secrets = meaningful;
        }
    }

    /// <summary>
    /// Replaces every registered secret in <paramref name="text"/> with a
    /// fixed marker. Returns the input unchanged when nothing is registered
    /// or the text is null.
    /// </summary>
    /// <param name="text">Text to scrub (may be null).</param>
    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        string[] secrets;
        lock (Gate)
        {
            secrets = _secrets;
        }

        var result = text;
        foreach (var secret in secrets)
        {
            if (!string.IsNullOrEmpty(secret))
            {
                result = result.Replace(secret, "[redacted]", StringComparison.Ordinal);
            }
        }

        return result;
    }
}

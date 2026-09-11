using System.Collections.Specialized;
using System.Configuration;
using System.Globalization;

namespace SqlOptimizer.Desktop.Services;

/// <summary>
/// Reads the desktop configuration from App.config (the "Database" and
/// "Llm" sections mirror the HTTP API's configuration section names, plus
/// desktop-specific appSettings). Secrets (connection string, API key) are
/// only forwarded to the local engine process and never displayed or logged.
/// </summary>
public sealed class DesktopConfiguration
{
    /// <summary>Loads the configuration from the application config file.</summary>
    public static DesktopConfiguration Load()
    {
        var app = ConfigurationManager.AppSettings;
        return new DesktopConfiguration
        {
            EnginePath = app["Desktop.EnginePath"] ?? string.Empty,
            DefaultOperationTimeoutSeconds = ReadInt(app["Desktop.DefaultOperationTimeoutSeconds"], 300, 10, 600),
            Database = ReadSection("Database"),
            Llm = ReadSection("Llm")
        };
    }

    /// <summary>Optional absolute path override for the engine executable.</summary>
    public string EnginePath { get; init; } = string.Empty;

    /// <summary>Default operation timeout in seconds sent to the engine.</summary>
    public int DefaultOperationTimeoutSeconds { get; init; } = 300;

    /// <summary>Database section values.</summary>
    public SectionValues Database { get; init; } = new SectionValues();

    /// <summary>LLM section values.</summary>
    public SectionValues Llm { get; init; } = new SectionValues();

    /// <summary>Reads a name/value configuration section (missing keys stay empty).</summary>
    private static SectionValues ReadSection(string name)
    {
        var section = ConfigurationManager.GetSection(name) as NameValueCollection;
        return new SectionValues
        {
            ConnectionString = section?["ConnectionString"] ?? string.Empty,
            Enabled = ReadBool(section?["Enabled"]),
            Model = section?["Model"] ?? string.Empty,
            ApiKey = section?["ApiKey"] ?? string.Empty,
            Endpoint = section?["Endpoint"] ?? string.Empty,
            Provider = section?["Provider"] ?? string.Empty,
            Temperature = ReadDouble(section?["Temperature"], 0.1),
            CommandTimeoutSeconds = ReadInt(section?["CommandTimeoutSeconds"], 30, 1, 3600),
            ConnectionTimeoutSeconds = ReadInt(section?["ConnectionTimeoutSeconds"], 15, 1, 300),
            MaxRowsForComparison = ReadInt(section?["MaxRowsForComparison"], 1000, 1, 100000),
            MaxResultCells = ReadInt(section?["MaxResultCells"], 20000, 1, 1000000),
            ApplicationName = string.IsNullOrWhiteSpace(section?["ApplicationName"])
                ? "SqlOptimizer.Desktop"
                : section!["ApplicationName"]!,
            MetadataCacheTtlSeconds = ReadInt(section?["MetadataCacheTtlSeconds"], 300, 1, 86400),
            MetadataCacheMaxEntries = ReadInt(section?["MetadataCacheMaxEntries"], 256, 1, 10000),
            TimeoutSeconds = ReadInt(section?["TimeoutSeconds"], 120, 1, 3600),
            MaxPromptChars = ReadInt(section?["MaxPromptChars"], 60000, 1000, 2000000),
            MaxCompletionTokens = ReadInt(section?["MaxCompletionTokens"], 2000, 1, 100000)
        };
    }


    /// <summary>Plain values of one configuration section.</summary>
    public sealed class SectionValues
    {
        /// <summary>SQL Server connection string (secret).</summary>
        public string ConnectionString { get; init; } = string.Empty;

        /// <summary>Master switch for the database providers.</summary>
        public bool Enabled { get; init; }

        /// <summary>LLM provider name: "OpenAI", "Mock" or empty (disabled).</summary>
        public string Provider { get; init; } = string.Empty;

        /// <summary>LLM model identifier.</summary>
        public string Model { get; init; } = string.Empty;

        /// <summary>LLM API key (secret).</summary>
        public string ApiKey { get; init; } = string.Empty;

        /// <summary>OpenAI-compatible endpoint base URL.</summary>
        public string Endpoint { get; init; } = string.Empty;

        /// <summary>Sampling temperature.</summary>
        public double Temperature { get; init; } = 0.1;

        /// <summary>Database command timeout in seconds.</summary>
        public int CommandTimeoutSeconds { get; init; } = 30;

        /// <summary>Database connection timeout in seconds.</summary>
        public int ConnectionTimeoutSeconds { get; init; } = 15;

        /// <summary>Hard cap on rows compared per query.</summary>
        public int MaxRowsForComparison { get; init; } = 1000;

        /// <summary>Safety cap on result-set cells.</summary>
        public int MaxResultCells { get; init; } = 20000;

        /// <summary>Application name reported to SQL Server.</summary>
        public string ApplicationName { get; init; } = "SqlOptimizer.Desktop";

        /// <summary>Live schema metadata cache TTL in seconds.</summary>
        public int MetadataCacheTtlSeconds { get; init; } = 300;

        /// <summary>Live schema metadata cache size cap.</summary>
        public int MetadataCacheMaxEntries { get; init; } = 256;

        /// <summary>LLM request timeout in seconds.</summary>
        public int TimeoutSeconds { get; init; } = 120;

        /// <summary>Maximum combined prompt length accepted.</summary>
        public int MaxPromptChars { get; init; } = 60000;

        /// <summary>Default maximum completion tokens requested.</summary>
        public int MaxCompletionTokens { get; init; } = 2000;
    }

    /// <summary>Reads a bool from a config string (default when absent/invalid).</summary>
    private static bool ReadBool(string? value) =>
        bool.TryParse(value, out var result) ? result : false;

    /// <summary>Reads a double from a config string (default when absent/invalid).</summary>
    private static double ReadDouble(string? value, double fallback) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : fallback;

    /// <summary>Reads a clamped int from a config string (default when absent/invalid).</summary>
    private static int ReadInt(string? value, int fallback, int min, int max) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? Math.Max(min, Math.Min(max, result))
            : fallback;
}

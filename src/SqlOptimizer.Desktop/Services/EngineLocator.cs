using System.IO;

namespace SqlOptimizer.Desktop.Services;

/// <summary>
/// Locates the local .NET 8 engine host executable. Resolution order:
/// 1) the optional "Desktop.EnginePath" appSetting (absolute path),
/// 2) candidate relative locations around the application output folder
///    (the standard solution build layout). The engine must be built first
///    (it is a normal project of the same solution).
/// </summary>
public static class EngineLocator
{
    private const string EngineFileName = "SqlOptimizer.Desktop.Engine.exe";
    private const string EngineDllName = "SqlOptimizer.Desktop.Engine.dll";

    /// <summary>
    /// Resolves the engine process start (file name + arguments). Returns
    /// null when the engine cannot be located.
    /// </summary>
    /// <param name="configuredPath">Optional absolute path from App.config.</param>
    public static (string FileName, string Arguments)? Locate(string configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            if (File.Exists(configuredPath))
            {
                return (configuredPath, string.Empty);
            }

            var dir = Path.GetDirectoryName(configuredPath);
            var file = Path.GetFileName(configuredPath);
            if (!string.IsNullOrWhiteSpace(dir) && File.Exists(Path.Combine(dir, EngineDllName)))
            {
                var dotnet = DotNetCli();
                if (dotnet is not null)
                {
                    return (dotnet, EngineDllName);
                }
            }

            return null;
        }

        var baseDir = AppContext.BaseDirectory;
        foreach (var candidate in Candidates(baseDir))
        {
            if (File.Exists(candidate))
            {
                return (candidate, string.Empty);
            }

            var dll = Path.Combine(
                Path.GetDirectoryName(candidate) ?? string.Empty,
                EngineDllName);
            if (File.Exists(dll))
            {
                var dotnet = DotNetCli();
                if (dotnet != null)
                {
                    return (dotnet, dll);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Candidate engine locations relative to the application output folder,
    /// covering the standard "dotnet build"/Visual Studio solution layout in
    /// both Debug and Release configurations.
    /// </summary>
    private static IEnumerable<string> Candidates(string baseDir)
    {
        foreach (var configuration in new[] { "Debug", "Release" })
        {
            yield return Path.Combine(baseDir, "SqlOptimizer.Desktop.Engine", EngineFileName);
            yield return Path.Combine(baseDir, "..", "SqlOptimizer.Desktop.Engine", EngineFileName);
            yield return Path.Combine(baseDir, "..", "..", "SqlOptimizer.Desktop.Engine", "bin", configuration, "net8.0", EngineFileName);
            yield return Path.Combine(baseDir, "..", "..", "..", "SqlOptimizer.Desktop.Engine", "bin", configuration, "net8.0", EngineFileName);
            yield return Path.Combine(baseDir, "..", "..", "..", "..", "SqlOptimizer.Desktop.Engine", "bin", configuration, "net8.0", EngineFileName);
        }
    }

    /// <summary>
    /// Finds the dotnet CLI on PATH (used to start the engine DLL when no
    /// native apphost executable is present). Returns null when not found.
    /// </summary>
    private static string? DotNetCli()
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathVar.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate;
            try
            {
                candidate = Path.Combine(dir.Trim(), "dotnet.exe");
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}

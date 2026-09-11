using System.Text;
using SqlOptimizer.Desktop.Engine.Protocol;

namespace SqlOptimizer.Desktop.Engine;

/// <summary>
/// Entry point of the desktop engine host. It runs a line-delimited JSON
/// request loop over stdin/stdout and executes the existing
/// Application/Rules/Infrastructure pipeline in-process. There is no HTTP,
/// no web server and no network endpoint: the only channel is the process
/// stdin/stdout pair owned by the desktop client. Diagnostics go to stderr.
/// </summary>
public static class Program
{
    /// <summary>
    /// Runs the engine until stdin closes or a graceful <c>shutdown</c> is
    /// received. Returns 0 on clean exit.
    /// </summary>
    public static async Task<int> Main()
    {
        try
        {
            Console.InputEncoding = Encoding.UTF8;
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (IOException)
        {
            // Non-interactive host without a usable console: keep defaults.
        }

        var core = new EngineCore();
        await core.SendHelloAsync();

        string? line;
        while ((line = await Console.In.ReadLineAsync()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.Length > ProtocolLimits.MaxLineChars)
            {
                await core.WriteProtocolErrorAsync(0, "The request line exceeds the maximum length.");
                continue;
            }

            await core.HandleLineAsync(line);

            if (core.ShutdownRequested)
            {
                break;
            }
        }

        await core.ShutdownAsync();
        return 0;
    }
}

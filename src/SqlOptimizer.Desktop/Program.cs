using System.Windows.Forms;

namespace SqlOptimizer.Desktop;

/// <summary>
/// Entry point of the SqlOptimizer desktop application. All analysis,
/// optimization and validation work runs in the local .NET 8 engine host
/// process, reached exclusively through the stdin/stdout JSON protocol.
/// </summary>
internal static class Program
{
    /// <summary>The main entry point for the application.</summary>
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Never crash without a safe message; never show stack traces.
        Application.ThreadException += (_, args) =>
            MessageBox.Show(
                "The application encountered an unexpected error: " + args.Exception.Message,
                "SqlOptimizer Desktop",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);

        Application.Run(new Forms.MainForm());
    }
}

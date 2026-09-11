using System.Windows.Forms;
using SqlOptimizer.Desktop.Services;

namespace SqlOptimizer.Desktop.Forms;

/// <summary>
/// Read-only viewer for the engine host's stderr diagnostics. Refreshes on
/// demand and periodically while open. The log never contains secrets (the
/// engine redacts connection strings and API keys before writing).
/// </summary>
public sealed class LogForm : Form
{
    private readonly EngineClient _engine;
    private readonly TextBox _textBox;
    private readonly System.Windows.Forms.Timer _timer;

    /// <summary>Creates the log viewer.</summary>
    /// <param name="engine">The engine client whose log is displayed.</param>
    public LogForm(EngineClient engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        Text = "Engine log";
        ClientSize = new Size(760, 420);
        StartPosition = FormStartPosition.CenterParent;

        _textBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9F)
        };

        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36, FlowDirection = FlowDirection.RightToLeft };
        var refresh = new Button { Text = "Refresh", Width = 90 };
        refresh.Click += (_, _) => RefreshView();
        var close = new Button { Text = "Close", Width = 90 };
        close.Click += (_, _) => Close();
        btnPanel.Controls.Add(refresh);
        btnPanel.Controls.Add(close);

        Controls.Add(_textBox);
        Controls.Add(btnPanel);
        RefreshView();

        _timer = new System.Windows.Forms.Timer { Interval = 2000 };
        _timer.Tick += (_, _) => RefreshView();
        _timer.Start();
    }

    /// <summary>Refreshes the displayed log content.</summary>
    private void RefreshView()
    {
        var lines = _engine.GetEngineLog();
        var text = lines.Count == 0
            ? "(no engine diagnostics yet)"
            : string.Join(Environment.NewLine, lines);
        if (_textBox.Text != text)
        {
            _textBox.Text = text;
            _textBox.SelectionStart = _textBox.TextLength;
            _textBox.ScrollToCaret();
        }
    }

    /// <summary>Stops the refresh timer when the form closes.</summary>
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        _timer.Dispose();
        base.OnFormClosed(e);
    }
}

using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using SqlOptimizer.Desktop.Protocol;
using SqlOptimizer.Desktop.Services;

namespace SqlOptimizer.Desktop.Forms;

/// <summary>
/// Main window of the SqlOptimizer desktop application. Every analysis,
/// optimization and validation operation runs in the local .NET 8 engine
/// host process through the stdin/stdout JSON protocol (no HTTP). The UI
/// stays responsive: operations run asynchronously, buttons are disabled
/// while in flight, and errors are shown as safe messages without stack
/// traces or secrets.
/// </summary>
public partial class MainForm : Form
{
    private readonly EngineClient _engine;
    private readonly DesktopConfiguration _configuration;
    private CancellationTokenSource? _operationCts;
    private List<JsonElement> _findings = new List<JsonElement>();
    private List<JsonElement> _candidates = new List<JsonElement>();
    private volatile bool _closing;

    /// <summary>Creates the main form and wires the engine client.</summary>
    public MainForm()
    {
        InitializeComponent();
        _configuration = DesktopConfiguration.Load();
        _engine = new EngineClient(
            () => _configuration,
            () => EngineLocator.Locate(_configuration.EnginePath));
        _engine.EngineLost += OnEngineLost;
        _engine.StatusChanged += OnEngineStatus;
        LoadSampleSql();
        _ = InitializeAsync();
    }

    /// <summary>Loads a representative sample query into the editor.</summary>
    private void LoadSampleSql()
    {
        txtSql.Text =
            "SELECT o.OrderId, o.Total, c.Name\r\n" +
            "FROM Sales.Orders o\r\n" +
            "INNER JOIN Sales.Customers c ON c.CustomerId = o.CustomerId\r\n" +
            "WHERE o.OrderDate > '2024-01-01'\r\n" +
            "  AND LOWER(c.Name) LIKE '%smith%'\r\n" +
            "  AND o.Status IN (1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21)\r\n" +
            "ORDER BY o.OrderDate DESC";
    }

    /// <summary>Starts the engine and refreshes the status strip (background).</summary>
    private async Task InitializeAsync()
    {
        try
        {
            await _engine.EnsureEngineAsync();
            await RefreshHealthAsync();
        }
        catch (Exception ex)
        {
            SetStatus("Engine not ready: " + SafeMessage(ex));
        }
    }

    /// <summary>Runs the health operation and updates the status strip.</summary>
    private async Task RefreshHealthAsync()
    {
        try
        {
            var response = await _engine.RequestAsync(Operations.Health, null, TimeSpan.FromSeconds(20));
            if (!response.Ok || response.Payload is not { } payload)
            {
                SetStatus(response.Error?.Message ?? "Health check failed.");
                return;
            }

            var database = payload.GetProperty("database").GetProperty("configured").GetBoolean()
                ? (payload.GetProperty("database").GetProperty("reachable").GetBoolean()
                    ? "Database: reachable"
                    : "Database: configured (not reachable)")
                : "Database: not configured";
            var llm = payload.GetProperty("llm").GetProperty("configured").GetBoolean()
                ? "LLM: " + (payload.GetProperty("llm").GetProperty("provider").GetString() ?? "")
                : "LLM: disabled";

            if (IsHandleCreated && !IsDisposed)
            {
                BeginInvoke(new Action(() =>
                {
                    dbLabel.Text = database;
                    llmLabel.Text = llm;
                    var overall = payload.GetProperty("status").GetString();
                    SetStatus(overall == "Healthy" ? "Ready." : "Ready (degraded).");
                }));
            }
        }
        catch (Exception ex)
        {
            if (IsHandleCreated && !IsDisposed)
            {
                BeginInvoke(new Action(() => SetStatus("Status unavailable: " + SafeMessage(ex))));
            }
        }
    }


    /// <summary>Runs one engine operation with busy state management.</summary>
    /// <param name="operation">Operation name.</param>
    /// <param name="payload">Operation payload.</param>
    /// <param name="timeout">Operation timeout.</param>
    /// <param name="render">Renderer for the response payload.</param>
    private async Task RunOperationAsync(string operation, object payload, TimeSpan timeout, Action<JsonElement> render)
    {
        SetBusy(true, operation);
        _operationCts = new CancellationTokenSource();
        try
        {
            var response = await _engine.RequestAsync(operation, payload, timeout, _operationCts.Token);
            if (!response.Ok)
            {
                ShowEngineError(response.Error?.Code ?? EngineErrors.Unknown, response.Error?.Message ?? "The operation failed.");
                return;
            }

            if (response.Payload is { } element)
            {
                render(element);
                SetStatus(operation + " completed.");
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus("Operation canceled.");
        }
        catch (EngineClient.EngineRequestException ex)
        {
            ShowEngineError(ex.Code, ex.Message);
        }
        catch (EngineClient.EngineNotAvailableException ex)
        {
            ShowEngineError(EngineErrors.EngineLost, ex.Message);
        }
        catch (Exception ex)
        {
            ShowEngineError(EngineErrors.Unknown, SafeMessage(ex));
        }
        finally
        {
            _operationCts?.Dispose();
            _operationCts = null;
            SetBusy(false, operation);
        }
    }

    /// <summary>Enables/disables the operation buttons while an operation runs.</summary>
    private void SetBusy(bool busy, string operation)
    {
        btnAnalyze.Enabled = !busy;
        btnOptimize.Enabled = !busy;
        btnValidate.Enabled = !busy;
        btnValidateTab.Enabled = !busy;
        btnUseTopCandidate.Enabled = !busy;
        btnStop.Enabled = busy;
        engineRestartItem.Enabled = !busy;
        if (IsHandleCreated && !IsDisposed && busy)
        {
            BeginInvoke(new Action(() => SetStatus(operation + " running...")));
        }
    }

    /// <summary>Sets the main status label (thread safe).</summary>
    private void SetStatus(string message)
    {
        if (IsHandleCreated && !IsDisposed)
        {
            BeginInvoke(new Action(() => statusLabel.Text = message));
        }
    }

    /// <summary>Shows a safe engine error (friendly title + message, no stack traces).</summary>
    private void ShowEngineError(string code, string message)
    {
        var title = FriendlyTitle(code);
        if (IsHandleCreated && !IsDisposed)
        {
            BeginInvoke(new Action(() =>
            {
                SetStatus(title + ".");
                MessageBox.Show(this, title + ": " + message, "SqlOptimizer Desktop",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }));
        }
    }

    /// <summary>Maps a stable error code to a friendly title.</summary>
    private static string FriendlyTitle(string code)
    {
        switch (code)
        {
            case EngineErrors.SqlInvalidInput: return "Invalid SQL input";
            case EngineErrors.SqlParseError: return "SQL parse error";
            case EngineErrors.SqlUnsupportedDialect: return "Unsupported dialect";
            case EngineErrors.SqlValidation: return "Validation error";
            case EngineErrors.SqlDatabase: return "Database error";
            case EngineErrors.SqlSafety: return "Safety violation";
            case EngineErrors.SqlAnalysis: return "Analysis error";
            case EngineErrors.LlmNotConfigured: return "LLM not configured";
            case EngineErrors.LlmInvalidResponse: return "LLM response rejected";
            case EngineErrors.LlmError: return "LLM error";
            case EngineErrors.RequestTimeout: return "Operation timed out";
            case EngineErrors.Canceled: return "Operation canceled";
            case EngineErrors.EngineBusy: return "Engine busy";
            case EngineErrors.NotConfigured: return "Engine not ready";
            case EngineErrors.EngineLost: return "Engine process lost";
            case EngineErrors.ProtocolError: return "Protocol error";
            default: return "Engine error";
        }
    }

    /// <summary>Sanitizes an exception message for display (message only, never stack).</summary>
    private static string SafeMessage(Exception ex) => ex.Message;


    /// <summary>Runs the analyze operation on the current SQL text.</summary>
    private async void btnAnalyze_Click(object sender, EventArgs e)
    {
        var sql = txtSql.Text;
        if (string.IsNullOrWhiteSpace(sql))
        {
            MessageBox.Show(this, "Please enter a SELECT statement in the SQL editor first.",
                "SqlOptimizer Desktop", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        await RunOperationAsync(
            Operations.Analyze,
            new { sql, dialect = "SqlServer", includeAst = chkIncludeAst.Checked },
            TimeSpan.FromSeconds(60),
            payload => RenderAnalysis(payload));
    }

    /// <summary>Runs the optimize operation on the current SQL text.</summary>
    private async void btnOptimize_Click(object sender, EventArgs e)
    {
        var sql = txtSql.Text;
        if (string.IsNullOrWhiteSpace(sql))
        {
            MessageBox.Show(this, "Please enter a SELECT statement in the SQL editor first.",
                "SqlOptimizer Desktop", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        await RunOperationAsync(
            Operations.Optimize,
            new
            {
                sql,
                dialect = "SqlServer",
                options = new
                {
                    useLlm = chkUseLlm.Checked,
                    generateIndexes = true,
                    validateSemantics = false,
                    generatePrompt = chkGeneratePrompt.Checked,
                    maxCandidates = 3,
                    strategy = (string)cmbStrategy.SelectedItem
                }
            },
            TimeSpan.FromSeconds(300),
            payload => RenderOptimization(payload));
    }

    /// <summary>Runs the validate operation on the original/candidate pair.</summary>
    private async void btnValidate_Click(object sender, EventArgs e)
    {
        var original = txtSql.Text;
        var candidate = txtCandidate.Text;
        if (string.IsNullOrWhiteSpace(original) || string.IsNullOrWhiteSpace(candidate))
        {
            MessageBox.Show(this, "Provide both the original SQL (editor) and the candidate SQL before validating.",
                "SqlOptimizer Desktop", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        await RunOperationAsync(
            Operations.Validate,
            new { originalSql = original, candidateSql = candidate, compareResults = chkCompareResults.Checked, maxRowsForComparison = 1000, dialect = "SqlServer" },
            TimeSpan.FromSeconds(120),
            payload => RenderValidation(payload));
    }

    /// <summary>Cancels the in-flight operation (best effort, via the protocol).</summary>
    private async void btnStop_Click(object sender, EventArgs e)
    {
        var cts = _operationCts;
        if (cts is null)
        {
            return;
        }

        btnStop.Enabled = false;
        cts.Cancel();
        try
        {
            await _engine.CancelActiveRequestAsync();
        }
        catch (Exception ex)
        {
            SetStatus("Cancel request failed: " + SafeMessage(ex));
        }
    }


    /// <summary>Loads the sample query.</summary>
    private void btnSample_Click(object sender, EventArgs e)
    {
        LoadSampleSql();
    }

    /// <summary>Copies the top optimized candidate into the candidate editor.</summary>
    private void btnUseTopCandidate_Click(object sender, EventArgs e)
    {
        if (dgvCandidates.Rows.Count == 0)
        {
            MessageBox.Show(this, "No candidates available. Run Optimize first.",
                "SqlOptimizer Desktop", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var sql = dgvCandidates.Rows[0].Tag as string;
        if (!string.IsNullOrEmpty(sql))
        {
            txtCandidate.Text = sql;
            SetStatus("Top candidate copied to the candidate editor.");
        }
    }

    /// <summary>Restarts the engine (stop + start + configure + health).</summary>
    private async void engineRestartItem_Click(object sender, EventArgs e)
    {
        SetStatus("Restarting engine...");
        try
        {
            await _engine.StopEngineAsync();
            await _engine.EnsureEngineAsync();
            await RefreshHealthAsync();
            SetStatus("Engine restarted.");
        }
        catch (Exception ex)
        {
            SetStatus("Engine restart failed: " + SafeMessage(ex));
        }
    }

    /// <summary>Refreshes the health/status strip.</summary>
    private async void engineHealthItem_Click(object sender, EventArgs e)
    {
        SetStatus("Checking status...");
        await RefreshHealthAsync();
    }

    /// <summary>Opens the engine log viewer.</summary>
    private void engineLogItem_Click(object sender, EventArgs e)
    {
        using var form = new LogForm(_engine);
        form.Show(this);
    }

    /// <summary>Shows the about dialog.</summary>
    private void helpAboutItem_Click(object sender, EventArgs e)
    {
        MessageBox.Show(this,
            "SqlOptimizer Desktop\r\n" +
            "Local SQL analysis, optimization and validation.\r\n" +
            "\r\n" +
            "All operations run in the local SqlOptimizer.Desktop.Engine process " +
            "over a stdin/stdout JSON protocol (no HTTP, no network).\r\n" +
            "\r\n" +
            "Findings, recommendations, candidates and index suggestions are " +
            "advisory: validation results (when available) are the only semantic " +
            "statement produced by the pipeline.",
            "About SqlOptimizer Desktop", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>Exits the application (graceful engine shutdown).</summary>
    private void fileExitItem_Click(object sender, EventArgs e)
    {
        Close();
    }

    /// <summary>Handles engine loss (background thread).</summary>
    private void OnEngineLost(object sender, string reason)
    {
        try
        {
            if (IsHandleCreated && !IsDisposed)
            {
                BeginInvoke(new Action(() =>
                {
                    engineLabel.Text = "Engine: stopped";
                    SetStatus("Engine stopped. The next operation will restart it.");
                }));
            }
        }
        catch (ObjectDisposedException)
        {
            // Form already closing: nothing to update.
        }
        catch (InvalidOperationException)
        {
            // Handle already destroyed: nothing to update.
        }
    }

    /// <summary>Handles engine status changes (background thread).</summary>
    private void OnEngineStatus(object sender, string status)
    {
        try
        {
            if (IsHandleCreated && !IsDisposed)
            {
                BeginInvoke(new Action(() =>
                {
                    var pid = _engine.Pid;
                    engineLabel.Text = pid is int p ? "Engine: running (pid " + p + ")" : "Engine: stopped";
                }));
            }
        }
        catch (ObjectDisposedException)
        {
            // Form already closing: nothing to update.
        }
        catch (InvalidOperationException)
        {
            // Handle already destroyed: nothing to update.
        }
    }

    /// <summary>Closes the form and stops the engine gracefully.</summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_closing)
        {
            _closing = true;
            try
            {
                // Bounded graceful shutdown so the UI never hangs on exit.
                var task = _engine.StopEngineAsync();
                if (!task.Wait(TimeSpan.FromSeconds(3)))
                {
                    _engine.Dispose();
                }
            }
            catch (Exception)
            {
                // Never block shutdown on the engine.
            }
        }

        base.OnFormClosing(e);
    }


    /// <summary>Renders an analyze response (scores, statistics, findings, AST).</summary>
    private void RenderAnalysis(JsonElement payload)
    {
        lblComplexityValue.Text = GetInt(payload, "complexityScore").ToString();
        lblPerformanceValue.Text = GetInt(payload, "performanceScore").ToString();
        lblTopCandidateValue.Text = "-";

        BindStatistics(payload.TryGetProperty("statistics", out var statistics) ? statistics : default);
        BindFindings(payload.TryGetProperty("findings", out var findings) ? findings : default);

        if (payload.TryGetProperty("ast", out var ast) && ast.ValueKind == JsonValueKind.Object)
        {
            var json = ast.GetRawText();
            txtAst.Text = json.Length > 30000 ? json.Substring(0, 30000) + "\r\n... [truncated]" : json;
        }
        else
        {
            txtAst.Text = "(AST not requested - enable 'Include AST' to view the parsed statement tree.)";
        }

        lstLimitations.Items.Clear();
    }

    /// <summary>Binds the statistics object to the statistics grid.</summary>
    private void BindStatistics(JsonElement statistics)
    {
        var rows = new List<object[]>();
        if (statistics.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in statistics.EnumerateObject())
            {
                rows.Add(new object[] { Pascalize(property.Name), property.Value.ToString() });
            }
        }

        dgvStatistics.Rows.Clear();
        foreach (var row in rows)
        {
            dgvStatistics.Rows.Add(row);
        }
    }

    /// <summary>Binds the findings array to the findings grid.</summary>
    private void BindFindings(JsonElement findings)
    {
        _findings = new List<JsonElement>();
        dgvFindings.Rows.Clear();
        if (findings.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var finding in findings.EnumerateArray())
        {
            _findings.Add(finding.Clone());
            dgvFindings.Rows.Add(
                GetStr(finding, "ruleId"),
                GetStr(finding, "severity"),
                GetStr(finding, "category"),
                GetDouble(finding, "confidence").ToString("0.00"),
                GetStr(finding, "message"));
        }
    }


    /// <summary>Renders an optimize response (analysis, plan, candidates, indexes, prompt, limitations).</summary>
    private void RenderOptimization(JsonElement payload)
    {
        if (payload.TryGetProperty("analysis", out var analysis))
        {
            RenderAnalysis(analysis);
        }

        if (payload.TryGetProperty("optimizationPlan", out var plan))
        {
            lblPlanBenefit.Text = "Estimated total benefit: " + GetInt(plan, "estimatedTotalBenefit").ToString() + " / 100";
            dgvPlan.Rows.Clear();
            if (plan.TryGetProperty("actions", out var actions) && actions.ValueKind == JsonValueKind.Array)
            {
                foreach (var action in actions.EnumerateArray())
                {
                    dgvPlan.Rows.Add(
                        GetStr(action, "actionType"),
                        GetStr(action, "description"),
                        GetStr(action, "risk"),
                        GetDouble(action, "confidence").ToString("0.00"),
                        GetStr(action, "relatedRuleId"));
                }
            }
        }

        _candidates = new List<JsonElement>();
        dgvCandidates.Rows.Clear();
        if (payload.TryGetProperty("candidates", out var candidates) && candidates.ValueKind == JsonValueKind.Array)
        {
            foreach (var candidate in candidates.EnumerateArray())
            {
                _candidates.Add(candidate.Clone());
                var row = dgvCandidates.Rows.Add(
                    GetInt(candidate, "rank").ToString(),
                    GetStr(candidate, "candidateId"),
                    GetStr(candidate, "source"),
                    GetStr(candidate, "status"),
                    GetDouble(candidate, "confidence").ToString("0.00"),
                    GetStringArray(candidate, "ruleIds"));
                dgvCandidates.Rows[row].Tag = GetStr(candidate, "candidateSql");
            }
        }

        dgvIndexes.Rows.Clear();
        if (payload.TryGetProperty("indexes", out var indexes) && indexes.ValueKind == JsonValueKind.Array)
        {
            foreach (var index in indexes.EnumerateArray())
            {
                dgvIndexes.Rows.Add(
                    GetStr(index, "table"),
                    GetStringArray(index, "keyColumns"),
                    GetStringArray(index, "includedColumns"),
                    GetInt(index, "estimatedBenefit").ToString(),
                    GetDouble(index, "confidence").ToString("0.00"));
            }
        }

        if (payload.TryGetProperty("prompt", out var prompt) && prompt.ValueKind == JsonValueKind.Object)
        {
            var text = "System prompt:\r\n" + GetStr(prompt, "systemPrompt") +
                       "\r\n\r\nUser prompt:\r\n" + GetStr(prompt, "userPrompt");
            txtPrompt.Text = text.Length > 30000 ? text.Substring(0, 30000) + "\r\n... [truncated]" : text;
        }
        else
        {
            txtPrompt.Text = "(Prompt generation was not requested.)";
        }

        var limitations = new List<string>();
        if (payload.TryGetProperty("limitations", out var limit) && limit.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in limit.EnumerateArray())
            {
                limitations.Add(item.ToString());
            }
        }

        lstOptLimitations.Items.Clear();
        lstOptLimitations.Items.AddRange(limitations.ToArray());
        lstLimitations.Items.Clear();
        lstLimitations.Items.AddRange(limitations.ToArray());

        if (payload.TryGetProperty("validation", out var validation) && validation.ValueKind == JsonValueKind.Object)
        {
            var status = GetStr(validation, "status");
            lblTopCandidateValue.Text = status;
            lblTopCandidateValue.ForeColor = ValidationColor(status);
        }
    }


    /// <summary>Renders a validate response (status, proof details, evidence, limitations).</summary>
    private void RenderValidation(JsonElement payload)
    {
        var status = GetStr(payload, "status");
        lblValStatus.Text = "Status: " + status;
        lblValStatus.ForeColor = ValidationColor(status);
        lblValSyntax.Text = "Syntax: " + (GetBool(payload, "syntaxValid") ? "valid" : "invalid");
        var equivalent = GetBool(payload, "semanticallyEquivalent");
        lblValEquivalent.Text = "Semantically equivalent: " + (equivalent ? "yes (proven)" : "not proven");
        lblValEquivalent.ForeColor = equivalent ? Color.Green : Color.DimGray;
        lblValConfidence.Text = "Validation confidence: " + GetDouble(payload, "validationConfidence").ToString("0.00");
        lblValExecution.Text = "Result comparison: " +
            (status == "NotExecuted"
                ? "not executed (runtime comparison unavailable or disabled)"
                : "executed (database-backed)");

        FillList(lstDifferences, payload, "differences");
        FillList(lstErrors, payload, "errors");
        FillList(lstEvidence, payload, "evidence");
        FillList(lstValLimitations, payload, "limitations");
    }

    /// <summary>Colors for validation status values.</summary>
    private static Color ValidationColor(string status)
    {
        switch (status)
        {
            case "Passed":
            case "Equivalent": return Color.Green;
            case "Failed":
            case "Different": return Color.Firebrick;
            case "Inconclusive": return Color.OrangeRed;
            default: return Color.DimGray;
        }
    }

    /// <summary>Shows the selected finding detail (explanation, recommendations, fragment).</summary>
    private void dgvFindings_SelectionChanged(object sender, EventArgs e)
    {
        var index = dgvFindings.CurrentRow?.Index;
        if (index is null || index < 0 || index >= _findings.Count)
        {
            rtbFindingDetail.Text = string.Empty;
            return;
        }

        var finding = _findings[index.Value];
        var builder = new StringBuilder();
        builder.AppendLine("Rule: ").AppendLine(GetStr(finding, "ruleId") + " (" + GetStr(finding, "severity") + ", " + GetStr(finding, "category") + ")");
        builder.AppendLine("Confidence: ").AppendLine(GetDouble(finding, "confidence").ToString("0.00"));
        builder.AppendLine("Fragment: ").AppendLine(GetStr(finding, "sqlFragment"));
        builder.AppendLine().AppendLine("Explanation:").AppendLine(GetStr(finding, "explanation")).AppendLine();
        builder.AppendLine("Recommendations:");
        if (finding.TryGetProperty("recommendations", out var recommendations) && recommendations.ValueKind == JsonValueKind.Array)
        {
            foreach (var recommendation in recommendations.EnumerateArray())
            {
                builder.AppendLine("- ").AppendLine(recommendation.ToString());
            }
        }

        if (finding.TryGetProperty("impact", out var impact) && impact.ValueKind == JsonValueKind.Object)
        {
            builder.AppendLine("Impact: performance ").Append(GetInt(impact, "performance").ToString())
                .Append(", readability ").Append(GetInt(impact, "readability").ToString())
                .Append(", maintainability ").Append(GetInt(impact, "maintainability").ToString())
                .Append(", risk ").AppendLine(GetInt(impact, "risk").ToString());
        }

        rtbFindingDetail.Text = builder.ToString();
    }


    /// <summary>Shows the selected candidate SQL and explanation.</summary>
    private void dgvCandidates_SelectionChanged(object sender, EventArgs e)
    {
        var index = dgvCandidates.CurrentRow?.Index;
        if (index is null || index < 0 || index >= _candidates.Count)
        {
            rtbCandidateSql.Text = string.Empty;
            return;
        }

        var candidate = _candidates[index.Value];
        var builder = new StringBuilder();
        builder.AppendLine("Candidate: ").AppendLine(GetStr(candidate, "candidateId") +
            " (source: " + GetStr(candidate, "source") + ", status: " + GetStr(candidate, "status") + ")");
        builder.AppendLine("Explanation: ").AppendLine(GetStr(candidate, "explanation")).AppendLine();
        if (candidate.TryGetProperty("warnings", out var warnings) && warnings.ValueKind == JsonValueKind.Array && warnings.GetArrayLength() > 0)
        {
            builder.AppendLine("Warnings:");
            foreach (var warning in warnings.EnumerateArray())
            {
                builder.AppendLine("- ").AppendLine(warning.ToString());
            }
        }

        builder.AppendLine("Candidate SQL:").AppendLine();
        builder.AppendLine(GetStr(candidate, "candidateSql"));
        rtbCandidateSql.Text = builder.ToString();
    }

    /// <summary>Fills a list box with the string array found under a payload property.</summary>
    private static void FillList(ListBox list, JsonElement payload, string property)
    {
        list.Items.Clear();
        if (payload.TryGetProperty(property, out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in array.EnumerateArray())
            {
                list.Items.Add(item.ToString());
            }
        }
    }

    /// <summary>Reads a string property (empty when absent).</summary>
    private static string GetStr(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    /// <summary>Reads a double property (0 when absent).</summary>
    private static double GetDouble(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : 0d;

    /// <summary>Reads an int property (0 when absent).</summary>
    private static int GetInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : 0;

    /// <summary>Reads a bool property (false when absent).</summary>
    private static bool GetBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    /// <summary>Joins a string array property with ", " (empty when absent).</summary>
    private static string GetStringArray(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        foreach (var item in array.EnumerateArray())
        {
            parts.Add(item.ToString());
        }

        return string.Join(", ", parts);
    }

    /// <summary>Converts a camelCase property name to PascalCase for display.</summary>
    private static string Pascalize(string name) =>
        string.IsNullOrEmpty(name) ? name : char.ToUpperInvariant(name[0]) + name.Substring(1);
}

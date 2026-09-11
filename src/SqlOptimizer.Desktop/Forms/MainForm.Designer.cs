namespace SqlOptimizer.Desktop.Forms;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null;

    private MenuStrip menuStrip1;
    private ToolStripMenuItem fileMenu;
    private ToolStripMenuItem fileExitItem;
    private ToolStripMenuItem engineMenu;
    private ToolStripMenuItem engineRestartItem;
    private ToolStripMenuItem engineHealthItem;
    private ToolStripMenuItem engineLogItem;
    private ToolStripMenuItem helpMenu;
    private ToolStripMenuItem helpAboutItem;
    private StatusStrip statusStrip1;
    private ToolStripStatusLabel statusLabel;
    private ToolStripStatusLabel dbLabel;
    private ToolStripStatusLabel llmLabel;
    private ToolStripStatusLabel engineLabel;
    private SplitContainer splitMain;
    private Label labelSql;
    private TextBox txtSql;
    private GroupBox grpOptions;
    private CheckBox chkUseLlm;
    private CheckBox chkGeneratePrompt;
    private CheckBox chkIncludeAst;
    private Label lblStrategy;
    private ComboBox cmbStrategy;
    private FlowLayoutPanel btnPanel;
    private Button btnAnalyze;
    private Button btnOptimize;
    private Button btnValidate;
    private Button btnStop;
    private Button btnSample;
    private TabControl tabResults;
    private TabPage tabSummary;
    private TabPage tabFindings;
    private TabPage tabOptimize;
    private TabPage tabValidate;
    private TabPage tabAst;
    private FlowLayoutPanel summaryTop;
    private Label lblComplexityCaption;
    private Label lblComplexityValue;
    private Label lblPerformanceCaption;
    private Label lblPerformanceValue;
    private Label lblTopCandidateCaption;
    private Label lblTopCandidateValue;
    private GroupBox grpLimitations;
    private ListBox lstLimitations;
    private DataGridView dgvStatistics;
    private DataGridViewTextBoxColumn statNameCol;
    private DataGridViewTextBoxColumn statValueCol;
    private SplitContainer splitFindings;
    private DataGridView dgvFindings;
    private DataGridViewTextBoxColumn findingRuleCol;
    private DataGridViewTextBoxColumn findingSeverityCol;
    private DataGridViewTextBoxColumn findingCategoryCol;
    private DataGridViewTextBoxColumn findingConfidenceCol;
    private DataGridViewTextBoxColumn findingMessageCol;
    private RichTextBox rtbFindingDetail;
    private SplitContainer splitOpt;
    private TabControl tabOptInner;
    private TabPage tabPlan;
    private TabPage tabCandidates;
    private TabPage tabIndexes;
    private Label lblPlanBenefit;
    private DataGridView dgvPlan;
    private DataGridViewTextBoxColumn planTypeCol;
    private DataGridViewTextBoxColumn planDescCol;
    private DataGridViewTextBoxColumn planRiskCol;
    private DataGridViewTextBoxColumn planConfidenceCol;
    private DataGridViewTextBoxColumn planRuleCol;
    private SplitContainer splitCandidates;
    private DataGridView dgvCandidates;
    private DataGridViewTextBoxColumn candRankCol;
    private DataGridViewTextBoxColumn candIdCol;
    private DataGridViewTextBoxColumn candSourceCol;
    private DataGridViewTextBoxColumn candStatusCol;
    private DataGridViewTextBoxColumn candConfidenceCol;
    private DataGridViewTextBoxColumn candRuleIdsCol;
    private RichTextBox rtbCandidateSql;
    private DataGridView dgvIndexes;
    private DataGridViewTextBoxColumn idxTableCol;
    private DataGridViewTextBoxColumn idxKeyCol;
    private DataGridViewTextBoxColumn idxIncludedCol;
    private DataGridViewTextBoxColumn idxBenefitCol;
    private DataGridViewTextBoxColumn idxConfidenceCol;
    private TextBox txtPrompt;
    private ListBox lstOptLimitations;
    private Label lblCandidateCaption;
    private TextBox txtCandidate;
    private FlowLayoutPanel validateButtons;
    private Button btnUseTopCandidate;
    private CheckBox chkCompareResults;
    private Button btnValidateTab;
    private SplitContainer splitVal;
    private FlowLayoutPanel valSummary;
    private Label lblValStatus;
    private Label lblValSyntax;
    private Label lblValEquivalent;
    private Label lblValConfidence;
    private Label lblValExecution;
    private TabControl tabValDetails;
    private TabPage tabValDifferences;
    private TabPage tabValErrors;
    private TabPage tabValEvidence;
    private TabPage tabValLimitations;
    private ListBox lstDifferences;
    private ListBox lstErrors;
    private ListBox lstEvidence;
    private ListBox lstValLimitations;
    private TextBox txtAst;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }


    private void InitializeComponent()
    {
        this.components = new System.ComponentModel.Container();
        this.SuspendLayout();

        // menuStrip1
        this.menuStrip1 = new MenuStrip();
        this.fileMenu = new ToolStripMenuItem();
        this.fileExitItem = new ToolStripMenuItem();
        this.engineMenu = new ToolStripMenuItem();
        this.engineRestartItem = new ToolStripMenuItem();
        this.engineHealthItem = new ToolStripMenuItem();
        this.engineLogItem = new ToolStripMenuItem();
        this.helpMenu = new ToolStripMenuItem();
        this.helpAboutItem = new ToolStripMenuItem();
        this.fileMenu.Text = "&File";
        this.fileMenu.DropDownItems.AddRange(new ToolStripItem[] { this.fileExitItem });
        this.fileExitItem.Text = "&Exit";
        this.fileExitItem.Click += this.fileExitItem_Click;
        this.engineMenu.Text = "&Engine";
        this.engineMenu.DropDownItems.AddRange(new ToolStripItem[] { this.engineRestartItem, this.engineHealthItem, this.engineLogItem });
        this.engineRestartItem.Text = "&Restart engine";
        this.engineRestartItem.Click += this.engineRestartItem_Click;
        this.engineHealthItem.Text = "Update &status";
        this.engineHealthItem.Click += this.engineHealthItem_Click;
        this.engineLogItem.Text = "Engine &log...";
        this.engineLogItem.Click += this.engineLogItem_Click;
        this.helpMenu.Text = "&Help";
        this.helpMenu.DropDownItems.AddRange(new ToolStripItem[] { this.helpAboutItem });
        this.helpAboutItem.Text = "&About";
        this.helpAboutItem.Click += this.helpAboutItem_Click;
        this.menuStrip1.Items.AddRange(new ToolStripItem[] { this.fileMenu, this.engineMenu, this.helpMenu });

        // statusStrip1
        this.statusStrip1 = new StatusStrip();
        this.statusLabel = new ToolStripStatusLabel("Ready.");
        this.statusLabel.Spring = true;
        this.statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        this.dbLabel = new ToolStripStatusLabel("Database: ...");
        this.dbLabel.BorderSides = ToolStripStatusLabelBorderSides.Left;
        this.llmLabel = new ToolStripStatusLabel("LLM: ...");
        this.llmLabel.BorderSides = ToolStripStatusLabelBorderSides.Left;
        this.engineLabel = new ToolStripStatusLabel("Engine: ...");
        this.engineLabel.BorderSides = ToolStripStatusLabelBorderSides.Left;
        this.statusStrip1.Items.AddRange(new ToolStripStatusLabel[] { this.statusLabel, this.dbLabel, this.llmLabel, this.engineLabel });


        // splitMain (top: SQL editor, bottom: results)
        this.splitMain = new SplitContainer();
        this.splitMain.Dock = DockStyle.Fill;
        this.splitMain.Orientation = Orientation.Horizontal;
        this.splitMain.Size = new Size(1100, 760);
        this.splitMain.SplitterDistance = 340;
        this.splitMain.Panel1MinSize = 180;
        this.splitMain.Panel2MinSize = 200;
        this.splitMain.Panel1.SuspendLayout();
        this.splitMain.Panel2.SuspendLayout();
        this.splitMain.SuspendLayout();

        this.labelSql = new Label();
        this.labelSql.Text = "SQL (T-SQL, SELECT statements only)";
        this.labelSql.Dock = DockStyle.Top;
        this.labelSql.Height = 18;

        this.btnPanel = new FlowLayoutPanel();
        this.btnPanel.Dock = DockStyle.Bottom;
        this.btnPanel.Height = 40;
        this.btnPanel.SuspendLayout();
        this.btnAnalyze = new Button();
        this.btnAnalyze.Text = "&Analyze";
        this.btnAnalyze.Width = 96;
        this.btnAnalyze.Click += this.btnAnalyze_Click;
        this.btnOptimize = new Button();
        this.btnOptimize.Text = "&Optimize";
        this.btnOptimize.Width = 96;
        this.btnOptimize.Click += this.btnOptimize_Click;
        this.btnValidate = new Button();
        this.btnValidate.Text = "&Validate";
        this.btnValidate.Width = 96;
        this.btnValidate.Click += this.btnValidate_Click;
        this.btnStop = new Button();
        this.btnStop.Text = "St&op";
        this.btnStop.Width = 72;
        this.btnStop.Enabled = false;
        this.btnStop.Click += this.btnStop_Click;
        this.btnSample = new Button();
        this.btnSample.Text = "Load &sample";
        this.btnSample.Width = 100;
        this.btnSample.Click += this.btnSample_Click;
        this.btnPanel.Controls.AddRange(new Control[] { this.btnAnalyze, this.btnOptimize, this.btnValidate, this.btnStop, this.btnSample });
        this.btnPanel.ResumeLayout(true);

        this.grpOptions = new GroupBox();
        this.grpOptions.Text = "Options";
        this.grpOptions.Dock = DockStyle.Bottom;
        this.grpOptions.Height = 48;
        this.grpOptions.SuspendLayout();
        this.chkUseLlm = new CheckBox();
        this.chkUseLlm.Text = "Use LLM";
        this.chkUseLlm.AutoSize = true;
        this.chkUseLlm.Location = new Point(12, 20);
        this.chkGeneratePrompt = new CheckBox();
        this.chkGeneratePrompt.Text = "Generate prompt";
        this.chkGeneratePrompt.AutoSize = true;
        this.chkGeneratePrompt.Checked = true;
        this.chkGeneratePrompt.Location = new Point(100, 20);
        this.chkIncludeAst = new CheckBox();
        this.chkIncludeAst.Text = "Include AST";
        this.chkIncludeAst.AutoSize = true;
        this.chkIncludeAst.Location = new Point(230, 20);
        this.lblStrategy = new Label();
        this.lblStrategy.Text = "Strategy:";
        this.lblStrategy.AutoSize = true;
        this.lblStrategy.Location = new Point(340, 23);
        this.cmbStrategy = new ComboBox();
        this.cmbStrategy.DropDownStyle = ComboBoxStyle.DropDownList;
        this.cmbStrategy.Items.AddRange(new object[] { "Conservative", "Balanced", "Aggressive" });
        this.cmbStrategy.SelectedIndex = 1;
        this.cmbStrategy.Location = new Point(400, 20);
        this.grpOptions.Controls.AddRange(new Control[] { this.chkUseLlm, this.chkGeneratePrompt, this.chkIncludeAst, this.lblStrategy, this.cmbStrategy });
        this.grpOptions.ResumeLayout(true);

        this.txtSql = new TextBox();
        this.txtSql.Multiline = true;
        this.txtSql.ScrollBars = ScrollBars.Both;
        this.txtSql.WordWrap = false;
        this.txtSql.AcceptsReturn = true;
        this.txtSql.AcceptsTab = true;
        this.txtSql.Font = new Font("Consolas", 10F);
        this.txtSql.Dock = DockStyle.Fill;


        // tabResults
        this.tabResults = new TabControl();
        this.tabResults.Dock = DockStyle.Fill;
        this.tabResults.SuspendLayout();
        this.tabSummary = new TabPage("Summary");
        this.tabFindings = new TabPage("Findings");
        this.tabOptimize = new TabPage("Optimize");
        this.tabValidate = new TabPage("Validate");
        this.tabAst = new TabPage("AST");
        this.tabResults.TabPages.AddRange(new TabPage[] { this.tabSummary, this.tabFindings, this.tabOptimize, this.tabValidate, this.tabAst });
        this.splitMain.Panel2.Controls.Add(this.tabResults);
        this.splitMain.Panel2.ResumeLayout(true);
        this.splitMain.ResumeLayout(true);

        // tabSummary
        this.summaryTop = new FlowLayoutPanel();
        this.summaryTop.Dock = DockStyle.Top;
        this.summaryTop.Height = 86;
        this.summaryTop.SuspendLayout();
        this.lblComplexityCaption = new Label();
        this.lblComplexityCaption.Text = "Complexity score";
        this.lblComplexityCaption.AutoSize = true;
        this.lblComplexityCaption.Margin = new Padding(18, 8, 0, 0);
        this.lblComplexityValue = new Label();
        this.lblComplexityValue.Text = "-";
        this.lblComplexityValue.Font = new Font(this.Font.FontFamily, 18F, FontStyle.Bold);
        this.lblComplexityValue.AutoSize = true;
        this.lblComplexityValue.Margin = new Padding(18, 0, 0, 0);
        this.lblPerformanceCaption = new Label();
        this.lblPerformanceCaption.Text = "Performance risk";
        this.lblPerformanceCaption.AutoSize = true;
        this.lblPerformanceCaption.Margin = new Padding(18, 8, 0, 0);
        this.lblPerformanceValue = new Label();
        this.lblPerformanceValue.Text = "-";
        this.lblPerformanceValue.Font = new Font(this.Font.FontFamily, 18F, FontStyle.Bold);
        this.lblPerformanceValue.AutoSize = true;
        this.lblPerformanceValue.Margin = new Padding(18, 0, 0, 0);
        this.lblTopCandidateCaption = new Label();
        this.lblTopCandidateCaption.Text = "Top candidate validation";
        this.lblTopCandidateCaption.AutoSize = true;
        this.lblTopCandidateCaption.Margin = new Padding(18, 8, 0, 0);
        this.lblTopCandidateValue = new Label();
        this.lblTopCandidateValue.Text = "-";
        this.lblTopCandidateValue.AutoSize = true;
        this.lblTopCandidateValue.Margin = new Padding(18, 0, 0, 0);
        this.summaryTop.Controls.AddRange(new Control[] {
            this.lblComplexityCaption, this.lblComplexityValue,
            this.lblPerformanceCaption, this.lblPerformanceValue,
            this.lblTopCandidateCaption, this.lblTopCandidateValue });
        this.summaryTop.ResumeLayout(true);

        this.grpLimitations = new GroupBox();
        this.grpLimitations.Text = "Pipeline limitations (what was not proven)";
        this.grpLimitations.Dock = DockStyle.Bottom;
        this.grpLimitations.Height = 110;
        this.grpLimitations.SuspendLayout();
        this.lstLimitations = new ListBox();
        this.lstLimitations.Dock = DockStyle.Fill;
        this.grpLimitations.Controls.Add(this.lstLimitations);
        this.grpLimitations.ResumeLayout(true);

        this.dgvStatistics = new DataGridView();
        this.dgvStatistics.Dock = DockStyle.Fill;
        this.dgvStatistics.ReadOnly = true;
        this.dgvStatistics.AllowUserToAddRows = false;
        this.dgvStatistics.RowHeadersVisible = false;
        this.statNameCol = new DataGridViewTextBoxColumn();
        this.statNameCol.HeaderText = "Statistic";
        this.statNameCol.Width = 260;
        this.statValueCol = new DataGridViewTextBoxColumn();
        this.statValueCol.HeaderText = "Value";
        this.statValueCol.Width = 320;
        this.dgvStatistics.Columns.AddRange(new DataGridViewColumn[] { this.statNameCol, this.statValueCol });

        this.tabSummary.Controls.Add(this.dgvStatistics);
        this.tabSummary.Controls.Add(this.grpLimitations);
        this.tabSummary.Controls.Add(this.summaryTop);

        this.splitMain.Panel1.Controls.Add(this.txtSql);
        this.splitMain.Panel1.Controls.Add(this.btnPanel);
        this.splitMain.Panel1.Controls.Add(this.grpOptions);
        this.splitMain.Panel1.Controls.Add(this.labelSql);
        this.splitMain.Panel1.ResumeLayout(true);


        // tabFindings
        this.splitFindings = new SplitContainer();
        this.splitFindings.Dock = DockStyle.Fill;
        this.splitFindings.Orientation = Orientation.Horizontal;
        this.splitFindings.Size = new Size(1000, 620);
        this.splitFindings.SplitterDistance = 320;
        this.splitFindings.Panel1MinSize = 160;
        this.splitFindings.Panel2MinSize = 110;
        this.splitFindings.Panel1.SuspendLayout();
        this.splitFindings.Panel2.SuspendLayout();
        this.splitFindings.SuspendLayout();
        this.dgvFindings = new DataGridView();
        this.dgvFindings.Dock = DockStyle.Fill;
        this.dgvFindings.ReadOnly = true;
        this.dgvFindings.AllowUserToAddRows = false;
        this.dgvFindings.RowHeadersVisible = false;
        this.dgvFindings.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        this.dgvFindings.MultiSelect = false;
        this.findingRuleCol = new DataGridViewTextBoxColumn();
        this.findingRuleCol.HeaderText = "Rule";
        this.findingRuleCol.Width = 70;
        this.findingSeverityCol = new DataGridViewTextBoxColumn();
        this.findingSeverityCol.HeaderText = "Severity";
        this.findingSeverityCol.Width = 80;
        this.findingCategoryCol = new DataGridViewTextBoxColumn();
        this.findingCategoryCol.HeaderText = "Category";
        this.findingCategoryCol.Width = 130;
        this.findingConfidenceCol = new DataGridViewTextBoxColumn();
        this.findingConfidenceCol.HeaderText = "Confidence";
        this.findingConfidenceCol.Width = 80;
        this.findingMessageCol = new DataGridViewTextBoxColumn();
        this.findingMessageCol.HeaderText = "Message";
        this.findingMessageCol.Width = 520;
        this.dgvFindings.Columns.AddRange(new DataGridViewColumn[] {
            this.findingRuleCol, this.findingSeverityCol, this.findingCategoryCol, this.findingConfidenceCol, this.findingMessageCol });
        this.dgvFindings.SelectionChanged += this.dgvFindings_SelectionChanged;
        this.rtbFindingDetail = new RichTextBox();
        this.rtbFindingDetail.ReadOnly = true;
        this.rtbFindingDetail.Dock = DockStyle.Fill;
        this.splitFindings.Panel1.Controls.Add(this.dgvFindings);
        this.splitFindings.Panel2.Controls.Add(this.rtbFindingDetail);
        this.splitFindings.Panel1.ResumeLayout(true);
        this.splitFindings.Panel2.ResumeLayout(true);
        this.splitFindings.ResumeLayout(true);
        this.tabFindings.Controls.Add(this.splitFindings);


        // tabOptimize
        this.splitOpt = new SplitContainer();
        this.splitOpt.Dock = DockStyle.Fill;
        this.splitOpt.Orientation = Orientation.Horizontal;
        this.splitOpt.Size = new Size(1000, 660);
        this.splitOpt.SplitterDistance = 300;
        this.splitOpt.Panel1MinSize = 180;
        this.splitOpt.Panel2MinSize = 150;
        this.splitOpt.Panel1.SuspendLayout();
        this.splitOpt.Panel2.SuspendLayout();
        this.splitOpt.SuspendLayout();
        this.tabOptInner = new TabControl();
        this.tabOptInner.Dock = DockStyle.Fill;
        this.tabOptInner.SuspendLayout();
        this.tabPlan = new TabPage("Plan");
        this.tabCandidates = new TabPage("Candidates");
        this.tabIndexes = new TabPage("Indexes");
        this.tabOptInner.TabPages.AddRange(new TabPage[] { this.tabPlan, this.tabCandidates, this.tabIndexes });

        this.lblPlanBenefit = new Label();
        this.lblPlanBenefit.Text = "Estimated total benefit: -";
        this.lblPlanBenefit.Dock = DockStyle.Top;
        this.lblPlanBenefit.Height = 22;
        this.lblPlanBenefit.Padding = new Padding(4, 4, 0, 0);
        this.dgvPlan = new DataGridView();
        this.dgvPlan.Dock = DockStyle.Fill;
        this.dgvPlan.ReadOnly = true;
        this.dgvPlan.AllowUserToAddRows = false;
        this.dgvPlan.RowHeadersVisible = false;
        this.planTypeCol = new DataGridViewTextBoxColumn();
        this.planTypeCol.HeaderText = "Action";
        this.planTypeCol.Width = 200;
        this.planDescCol = new DataGridViewTextBoxColumn();
        this.planDescCol.HeaderText = "Description";
        this.planDescCol.Width = 380;
        this.planRiskCol = new DataGridViewTextBoxColumn();
        this.planRiskCol.HeaderText = "Risk";
        this.planRiskCol.Width = 70;
        this.planConfidenceCol = new DataGridViewTextBoxColumn();
        this.planConfidenceCol.HeaderText = "Confidence";
        this.planConfidenceCol.Width = 80;
        this.planRuleCol = new DataGridViewTextBoxColumn();
        this.planRuleCol.HeaderText = "Rule";
        this.planRuleCol.Width = 70;
        this.dgvPlan.Columns.AddRange(new DataGridViewColumn[] {
            this.planTypeCol, this.planDescCol, this.planRiskCol, this.planConfidenceCol, this.planRuleCol });
        this.tabPlan.Controls.Add(this.dgvPlan);
        this.tabPlan.Controls.Add(this.lblPlanBenefit);

        this.splitCandidates = new SplitContainer();
        this.splitCandidates.Dock = DockStyle.Fill;
        this.splitCandidates.Size = new Size(940, 400);
        this.splitCandidates.SplitterDistance = 480;
        this.splitCandidates.Panel1MinSize = 300;
        this.splitCandidates.Panel2MinSize = 120;
        this.splitCandidates.Panel1.SuspendLayout();
        this.splitCandidates.Panel2.SuspendLayout();
        this.splitCandidates.SuspendLayout();
        this.dgvCandidates = new DataGridView();
        this.dgvCandidates.Dock = DockStyle.Fill;
        this.dgvCandidates.ReadOnly = true;
        this.dgvCandidates.AllowUserToAddRows = false;
        this.dgvCandidates.RowHeadersVisible = false;
        this.dgvCandidates.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        this.dgvCandidates.MultiSelect = false;
        this.candRankCol = new DataGridViewTextBoxColumn();
        this.candRankCol.HeaderText = "Rank";
        this.candRankCol.Width = 46;
        this.candIdCol = new DataGridViewTextBoxColumn();
        this.candIdCol.HeaderText = "Id";
        this.candIdCol.Width = 90;
        this.candSourceCol = new DataGridViewTextBoxColumn();
        this.candSourceCol.HeaderText = "Source";
        this.candSourceCol.Width = 70;
        this.candStatusCol = new DataGridViewTextBoxColumn();
        this.candStatusCol.HeaderText = "Status";
        this.candStatusCol.Width = 90;
        this.candConfidenceCol = new DataGridViewTextBoxColumn();
        this.candConfidenceCol.HeaderText = "Confidence";
        this.candConfidenceCol.Width = 80;
        this.candRuleIdsCol = new DataGridViewTextBoxColumn();
        this.candRuleIdsCol.HeaderText = "Rules";
        this.candRuleIdsCol.Width = 130;
        this.dgvCandidates.Columns.AddRange(new DataGridViewColumn[] {
            this.candRankCol, this.candIdCol, this.candSourceCol, this.candStatusCol, this.candConfidenceCol, this.candRuleIdsCol });
        this.dgvCandidates.SelectionChanged += this.dgvCandidates_SelectionChanged;
        this.rtbCandidateSql = new RichTextBox();
        this.rtbCandidateSql.ReadOnly = true;
        this.rtbCandidateSql.Dock = DockStyle.Fill;
        this.rtbCandidateSql.Font = new Font("Consolas", 9.5F);
        this.splitCandidates.Panel1.Controls.Add(this.dgvCandidates);
        this.splitCandidates.Panel2.Controls.Add(this.rtbCandidateSql);
        this.splitCandidates.Panel1.ResumeLayout(true);
        this.splitCandidates.Panel2.ResumeLayout(true);
        this.splitCandidates.ResumeLayout(true);
        this.tabCandidates.Controls.Add(this.splitCandidates);


        this.dgvIndexes = new DataGridView();
        this.dgvIndexes.Dock = DockStyle.Fill;
        this.dgvIndexes.ReadOnly = true;
        this.dgvIndexes.AllowUserToAddRows = false;
        this.dgvIndexes.RowHeadersVisible = false;
        this.idxTableCol = new DataGridViewTextBoxColumn();
        this.idxTableCol.HeaderText = "Table";
        this.idxTableCol.Width = 220;
        this.idxKeyCol = new DataGridViewTextBoxColumn();
        this.idxKeyCol.HeaderText = "Key columns";
        this.idxKeyCol.Width = 220;
        this.idxIncludedCol = new DataGridViewTextBoxColumn();
        this.idxIncludedCol.HeaderText = "Included columns";
        this.idxIncludedCol.Width = 180;
        this.idxBenefitCol = new DataGridViewTextBoxColumn();
        this.idxBenefitCol.HeaderText = "Benefit";
        this.idxBenefitCol.Width = 60;
        this.idxConfidenceCol = new DataGridViewTextBoxColumn();
        this.idxConfidenceCol.HeaderText = "Confidence";
        this.idxConfidenceCol.Width = 80;
        this.dgvIndexes.Columns.AddRange(new DataGridViewColumn[] {
            this.idxTableCol, this.idxKeyCol, this.idxIncludedCol, this.idxBenefitCol, this.idxConfidenceCol });
        this.tabIndexes.Controls.Add(this.dgvIndexes);

        this.tabOptInner.ResumeLayout(true);
        this.splitOpt.Panel1.Controls.Add(this.tabOptInner);

        this.txtPrompt = new TextBox();
        this.txtPrompt.Multiline = true;
        this.txtPrompt.ReadOnly = true;
        this.txtPrompt.ScrollBars = ScrollBars.Both;
        this.txtPrompt.Dock = DockStyle.Fill;
        this.lstOptLimitations = new ListBox();
        this.lstOptLimitations.Dock = DockStyle.Fill;
        this.splitOpt.Panel2.Controls.Add(this.lstOptLimitations);
        this.splitOpt.Panel2.Controls.Add(this.txtPrompt);
        this.splitOpt.Panel1.ResumeLayout(true);
        this.splitOpt.Panel2.ResumeLayout(true);
        this.splitOpt.ResumeLayout(true);
        this.tabOptimize.Controls.Add(this.splitOpt);


        // tabValidate
        this.lblCandidateCaption = new Label();
        this.lblCandidateCaption.Text = "Candidate (optimized) SQL - use the top candidate from Optimize or paste a rewritten query:";
        this.lblCandidateCaption.Dock = DockStyle.Top;
        this.lblCandidateCaption.Height = 18;
        this.txtCandidate = new TextBox();
        this.txtCandidate.Multiline = true;
        this.txtCandidate.ScrollBars = ScrollBars.Both;
        this.txtCandidate.WordWrap = false;
        this.txtCandidate.AcceptsReturn = true;
        this.txtCandidate.AcceptsTab = true;
        this.txtCandidate.Font = new Font("Consolas", 9.5F);
        this.txtCandidate.Dock = DockStyle.Top;
        this.txtCandidate.Height = 96;
        this.validateButtons = new FlowLayoutPanel();
        this.validateButtons.Dock = DockStyle.Top;
        this.validateButtons.Height = 34;
        this.validateButtons.SuspendLayout();
        this.btnUseTopCandidate = new Button();
        this.btnUseTopCandidate.Text = "Use top candidate";
        this.btnUseTopCandidate.Width = 120;
        this.btnUseTopCandidate.Click += this.btnUseTopCandidate_Click;
        this.chkCompareResults = new CheckBox();
        this.chkCompareResults.Text = "Compare results (requires a reachable database)";
        this.chkCompareResults.AutoSize = true;
        this.chkCompareResults.Padding = new Padding(12, 4, 0, 0);
        this.btnValidateTab = new Button();
        this.btnValidateTab.Text = "Validate";
        this.btnValidateTab.Width = 96;
        this.btnValidateTab.Click += this.btnValidate_Click;
        this.validateButtons.Controls.AddRange(new Control[] { this.btnUseTopCandidate, this.chkCompareResults, this.btnValidateTab });
        this.validateButtons.ResumeLayout(true);

        this.splitVal = new SplitContainer();
        this.splitVal.Dock = DockStyle.Fill;
        this.splitVal.Size = new Size(860, 400);
        this.splitVal.SplitterDistance = 330;
        this.splitVal.Panel1MinSize = 240;
        this.splitVal.Panel2MinSize = 260;
        this.splitVal.Panel1.SuspendLayout();
        this.splitVal.Panel2.SuspendLayout();
        this.splitVal.SuspendLayout();
        this.valSummary = new FlowLayoutPanel();
        this.valSummary.Dock = DockStyle.Fill;
        this.valSummary.WrapContents = false;
        this.valSummary.SuspendLayout();
        this.lblValStatus = new Label();
        this.lblValStatus.Text = "Status: -";
        this.lblValStatus.Font = new Font(this.Font.FontFamily, 12F, FontStyle.Bold);
        this.lblValStatus.AutoSize = true;
        this.lblValStatus.Margin = new Padding(10, 10, 0, 0);
        this.lblValSyntax = new Label();
        this.lblValSyntax.Text = "Syntax: -";
        this.lblValSyntax.AutoSize = true;
        this.lblValSyntax.Margin = new Padding(10, 8, 0, 0);
        this.lblValEquivalent = new Label();
        this.lblValEquivalent.Text = "Semantically equivalent: -";
        this.lblValEquivalent.AutoSize = true;
        this.lblValEquivalent.Margin = new Padding(10, 8, 0, 0);
        this.lblValConfidence = new Label();
        this.lblValConfidence.Text = "Validation confidence: -";
        this.lblValConfidence.AutoSize = true;
        this.lblValConfidence.Margin = new Padding(10, 8, 0, 0);
        this.lblValExecution = new Label();
        this.lblValExecution.Text = "Result comparison: -";
        this.lblValExecution.AutoSize = true;
        this.lblValExecution.Margin = new Padding(10, 8, 0, 0);
        this.valSummary.Controls.AddRange(new Control[] {
            this.lblValStatus, this.lblValSyntax, this.lblValEquivalent, this.lblValConfidence, this.lblValExecution });
        this.valSummary.ResumeLayout(true);
        this.splitVal.Panel1.Controls.Add(this.valSummary);
        this.splitVal.Panel1.ResumeLayout(true);


        this.tabValDetails = new TabControl();
        this.tabValDetails.Dock = DockStyle.Fill;
        this.tabValDetails.SuspendLayout();
        this.tabValDifferences = new TabPage("Differences");
        this.lstDifferences = new ListBox();
        this.lstDifferences.Dock = DockStyle.Fill;
        this.tabValDifferences.Controls.Add(this.lstDifferences);
        this.tabValErrors = new TabPage("Errors / risks");
        this.lstErrors = new ListBox();
        this.lstErrors.Dock = DockStyle.Fill;
        this.tabValErrors.Controls.Add(this.lstErrors);
        this.tabValEvidence = new TabPage("Evidence");
        this.lstEvidence = new ListBox();
        this.lstEvidence.Dock = DockStyle.Fill;
        this.tabValEvidence.Controls.Add(this.lstEvidence);
        this.tabValLimitations = new TabPage("Limitations");
        this.lstValLimitations = new ListBox();
        this.lstValLimitations.Dock = DockStyle.Fill;
        this.tabValLimitations.Controls.Add(this.lstValLimitations);
        this.tabValDetails.TabPages.AddRange(new TabPage[] {
            this.tabValDifferences, this.tabValErrors, this.tabValEvidence, this.tabValLimitations });
        this.tabValDetails.ResumeLayout(true);
        this.splitVal.Panel2.Controls.Add(this.tabValDetails);
        this.splitVal.Panel2.ResumeLayout(true);
        this.splitVal.ResumeLayout(true);
        this.tabValidate.Controls.Add(this.splitVal);
        this.tabValidate.Controls.Add(this.validateButtons);
        this.tabValidate.Controls.Add(this.txtCandidate);
        this.tabValidate.Controls.Add(this.lblCandidateCaption);

        // tabAst
        this.txtAst = new TextBox();
        this.txtAst.Multiline = true;
        this.txtAst.ReadOnly = true;
        this.txtAst.ScrollBars = ScrollBars.Both;
        this.txtAst.WordWrap = false;
        this.txtAst.Font = new Font("Consolas", 9F);
        this.txtAst.Dock = DockStyle.Fill;
        this.tabAst.Controls.Add(this.txtAst);

        // MainForm
        this.menuStrip1.ResumeLayout(true);
        this.statusStrip1.ResumeLayout(true);
        this.tabResults.ResumeLayout(true);
        this.tabSummary.ResumeLayout(true);
        this.summaryTop.ResumeLayout(true);
        this.grpLimitations.ResumeLayout(true);
        this.tabFindings.ResumeLayout(true);
        this.tabOptimize.ResumeLayout(true);
        this.tabOptInner.ResumeLayout(true);
        this.tabPlan.ResumeLayout(true);
        this.tabCandidates.ResumeLayout(true);
        this.tabIndexes.ResumeLayout(true);
        this.tabValidate.ResumeLayout(true);
        this.validateButtons.ResumeLayout(true);
        this.valSummary.ResumeLayout(true);
        this.tabValDetails.ResumeLayout(true);
        this.tabValDifferences.ResumeLayout(true);
        this.tabValErrors.ResumeLayout(true);
        this.tabValEvidence.ResumeLayout(true);
        this.tabValLimitations.ResumeLayout(true);
        this.tabAst.ResumeLayout(true);
        this.ClientSize = new Size(1180, 820);
        this.AutoScaleMode = AutoScaleMode.Font;
        this.Text = "SqlOptimizer Desktop";
        this.Controls.Add(this.splitMain);
        this.Controls.Add(this.statusStrip1);
        this.Controls.Add(this.menuStrip1);
        this.MainMenuStrip = this.menuStrip1;
        this.ResumeLayout(true);
    }
}

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using McTools.Xrm.Connection;
using Microsoft.Xrm.Sdk;
using XrmToolBox.Extensibility;

namespace DeploymentDoctor
{
    /// <summary>
    /// Main connection = Target (where the change doesn't show). Second connection = Dev (source), optional.
    /// </summary>
    public class DeploymentDoctorControl : MultipleConnectionsPluginControlBase
    {
        private const string AdditionalOrganizationAction = "AdditionalOrganization";

        private ToolStripLabel lblTarget;
        private ToolStripLabel lblDev;
        private ToolStripButton btnConnectDev;
        private ToolStripButton btnDisconnectDev;
        private ToolStripDropDownButton ddExport;

        private ComboBox cboSolution;
        private CheckBox chkOnlyInSolution;
        private ComboBox cboKind;
        private System.Windows.Forms.Label lblGroup;
        private ComboBox cboGroup;
        private System.Windows.Forms.Label lblComponent;
        private ComboBox cboComponent;
        private Button btnDiagnose;

        private System.Windows.Forms.Label lblVerdict;
        private DataGridView gridFindings;
        private DataGridView gridLayers;
        private TextBox txtDiff, txtTargetContent, txtDevContent, txtLayerContent;
        private System.Windows.Forms.Label lblStatus;

        private ConnectionDetail targetDetail;
        private List<SolutionItem> solutions = new List<SolutionItem>();
        private List<TableItem> tables = new List<TableItem>();
        private readonly Dictionary<ComponentKind, List<ComponentItem>> itemsByKind = new Dictionary<ComponentKind, List<ComponentItem>>();
        private HashSet<Guid> solutionIds;          // null = no solution filter
        private Diagnosis lastDiagnosis;
        private ComponentItem lastItem;
        private string lastSolution;            // what the last diagnosis ran against, for reports
        private ConnectionDetail lastDevDetail;
        private DateTime lastRunOn;
        private bool exportXml, exportUrls;         // HTML export choices, remembered for this session only

        private const string NoSolution = "(none - check the component only)";

        private ComponentHandler CurrentHandler { get { return Handlers.For(CurrentKind); } }
        private ComponentKind CurrentKind
        {
            get { var k = cboKind.SelectedItem as KindOption; return k == null ? ComponentKind.Form : k.Kind; }
        }

        private class KindOption
        {
            public ComponentKind Kind;
            public string Text;
            public override string ToString() { return Text; }
        }

        public DeploymentDoctorControl()
        {
            BuildUi();
        }

        private ConnectionDetail DevDetail { get { return AdditionalConnectionDetails.LastOrDefault(); } }
        private IOrganizationService DevService { get { var d = DevDetail; return d == null ? null : d.ServiceClient; } }

        // ---------------------------------------------------------------- UI

        private void BuildUi()
        {
            var tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
            var btnReload = new ToolStripButton("Reload lists");
            lblTarget = new ToolStripLabel("Target: (not connected)") { ForeColor = Color.DarkRed };
            btnConnectDev = new ToolStripButton("Connect Dev (source)...");
            btnDisconnectDev = new ToolStripButton("Disconnect Dev") { Enabled = false };
            lblDev = new ToolStripLabel("Dev: (optional, not connected)") { ForeColor = Color.DimGray };
            ddExport = new ToolStripDropDownButton("Export report") { Enabled = false };
            ddExport.DropDownItems.Add("HTML report (to email or attach to a ticket)...", null, (s, e) => ExportFile("html"));
            ddExport.DropDownItems.Add("CSV (opens in Excel)...", null, (s, e) => ExportFile("csv"));
            ddExport.DropDownItems.Add("Copy as text (for Teams / chat)", null, (s, e) => CopyReport());
            var btnAbout = new ToolStripButton("About") { Alignment = ToolStripItemAlignment.Right };
            tools.Items.AddRange(new ToolStripItem[]
            {
                btnReload, new ToolStripSeparator(), lblTarget, new ToolStripSeparator(),
                btnConnectDev, btnDisconnectDev, lblDev, new ToolStripSeparator(), ddExport, btnAbout
            });
            btnReload.Click += (s, e) => ExecuteMethod(LoadLists);
            btnConnectDev.Click += (s, e) => AddAdditionalOrganization();
            btnDisconnectDev.Click += (s, e) => { var d = DevDetail; if (d != null) RemoveAdditionalOrganization(d); };
            btnAbout.Click += (s, e) => ShowAbout();

            var pick = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6, 4, 6, 2), WrapContents = true };
            cboSolution = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 340, DropDownWidth = 520 };
            chkOnlyInSolution = new CheckBox { Text = "Only items in this solution", AutoSize = true, Checked = true, Margin = new Padding(3, 6, 12, 0) };
            cboKind = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
            cboKind.Items.AddRange(new object[]
            {
                new KindOption { Kind = ComponentKind.Form, Text = "Form" },
                new KindOption { Kind = ComponentKind.View, Text = "View" },
                new KindOption { Kind = ComponentKind.WebResource, Text = "Web resource" },
                new KindOption { Kind = ComponentKind.Process, Text = "Process / flow" }
            });
            cboKind.SelectedIndex = 0;
            lblGroup = Caption("Table:");
            cboGroup = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220, DropDownWidth = 360 };
            lblComponent = Caption("Form:");
            // Typing filters the list (web resources and flows can be many)
            cboComponent = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDown, Width = 300, DropDownWidth = 480,
                AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems
            };
            btnDiagnose = new Button { Text = "Diagnose", AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
            pick.Controls.AddRange(new Control[]
            {
                Caption("Solution you deployed:"), cboSolution, chkOnlyInSolution,
                Caption("Type:"), cboKind, lblGroup, cboGroup, lblComponent, cboComponent, btnDiagnose
            });
            cboSolution.SelectedIndexChanged += (s, e) => SolutionChanged();
            chkOnlyInSolution.CheckedChanged += (s, e) => SolutionChanged();
            cboKind.SelectedIndexChanged += (s, e) => KindChanged();
            cboGroup.SelectedIndexChanged += (s, e) => FillComponents();
            btnDiagnose.Click += (s, e) => ExecuteMethod(Diagnose);

            lblVerdict = new System.Windows.Forms.Label
            {
                Dock = DockStyle.Top, Height = 64, Padding = new Padding(8, 6, 8, 4),
                Font = new Font(Font.FontFamily, 10f, FontStyle.Bold),
                Text = "Pick the solution you deployed and the form, view, web resource or process that doesn't show your change, then click Diagnose."
            };

            gridFindings = NewGrid();
            gridFindings.AutoGenerateColumns = false;
            gridFindings.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            gridFindings.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Severity", HeaderText = "Result", Width = 70 });
            gridFindings.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Check", HeaderText = "Check", Width = 170 });
            gridFindings.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Result", HeaderText = "What was found", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 60, DefaultCellStyle = { WrapMode = DataGridViewTriState.True } });
            gridFindings.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "HowToFix", HeaderText = "How to fix", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 40, DefaultCellStyle = { WrapMode = DataGridViewTriState.True } });
            gridFindings.Columns.Add(new DataGridViewButtonColumn { Name = "Fix", DataPropertyName = "FixButton", HeaderText = "Fix", Width = 170 });
            gridFindings.DataBindingComplete += (s, e) => FormatFindings();
            gridFindings.CellContentClick += FindingsCellClick;

            gridLayers = NewGrid();
            gridLayers.AutoGenerateColumns = true;
            gridLayers.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
            gridLayers.DataBindingComplete += (s, e) => FormatLayers();

            txtDiff = NewText(); txtTargetContent = NewText(); txtDevContent = NewText(); txtLayerContent = NewText();

            var tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(Page("Solution layers (Target, top first)", gridLayers));
            tabs.TabPages.Add(Page("Differences: Dev vs Target", txtDiff));
            tabs.TabPages.Add(Page("Target content", txtTargetContent));
            tabs.TabPages.Add(Page("Dev content", txtDevContent));
            tabs.TabPages.Add(Page("Your solution's layer content", txtLayerContent));

            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 300 };
            split.Panel1.Controls.Add(gridFindings);
            split.Panel2.Controls.Add(tabs);

            lblStatus = new System.Windows.Forms.Label { Dock = DockStyle.Bottom, Height = 22, Padding = new Padding(6, 3, 0, 0), Text = "Connect to the TARGET environment (where the change doesn't show) to begin.  |  Deployment Doctor - created by Ankit Rana" };

            Controls.Add(split);
            Controls.Add(lblVerdict);
            Controls.Add(pick);
            Controls.Add(tools);
            Controls.Add(lblStatus);
            Dock = DockStyle.Fill;
        }

        private static System.Windows.Forms.Label Caption(string text)
        {
            return new System.Windows.Forms.Label { Text = text, AutoSize = true, Margin = new Padding(3, 7, 0, 0) };
        }

        private static DataGridView NewGrid()
        {
            return new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BackgroundColor = SystemColors.Window
            };
        }

        private static TextBox NewText()
        {
            return new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 9f), BackColor = SystemColors.Window };
        }

        private static TabPage Page(string title, Control content)
        {
            var p = new TabPage(title);
            p.Controls.Add(content);
            return p;
        }

        private void FormatFindings()
        {
            foreach (DataGridViewRow r in gridFindings.Rows)
            {
                var f = r.DataBoundItem as Finding;
                if (f == null) continue;
                r.DefaultCellStyle.BackColor = SeverityColor(f.Severity);
                // Rows without an automatic fix get a plain empty cell instead of a button
                if (f.Action == null) r.Cells["Fix"] = new DataGridViewTextBoxCell { Value = "" };
            }
        }

        private void FormatLayers()
        {
            foreach (DataGridViewColumn c in gridLayers.Columns)
                if (c.ValueType == typeof(DateTime?)) c.DefaultCellStyle.Format = "yyyy-MM-dd HH:mm";
            foreach (DataGridViewRow r in gridLayers.Rows)
            {
                var l = r.DataBoundItem as LayerRow;
                if (l == null) continue;
                r.DefaultCellStyle.BackColor =
                    l.Note == "Unmanaged (Active)" ? Color.MistyRose :
                    l.Note == "Your solution" ? Color.Honeydew :
                    l.Note == "Above yours" || (l.Note ?? "").StartsWith("Staged") ? Color.LightYellow :
                    SystemColors.Window;
            }
        }

        private static Color SeverityColor(Severity s)
        {
            switch (s)
            {
                case Severity.Problem: return Color.MistyRose;
                case Severity.Warning: return Color.LightYellow;
                case Severity.Ok: return Color.Honeydew;
                default: return SystemColors.Window;
            }
        }

        // ------------------------------------------------------------ Lists

        private void LoadLists()
        {
            itemsByKind.Clear();
            var kind = CurrentKind;
            WorkAsync(new WorkAsyncInfo
            {
                Message = "Loading solutions, tables and " + CurrentHandler.Label.ToLowerInvariant() + "s from Target...",
                Work = (w, a) =>
                {
                    a.Result = Tuple.Create(DoctorService.LoadSolutions(Service), DoctorService.LoadTables(Service), Handlers.For(kind).LoadAll(Service));
                },
                PostWorkCallBack = a =>
                {
                    if (a.Error != null) { MessageBox.Show("Could not load lists:\r\n" + a.Error.Message, "Error"); return; }
                    var t = (Tuple<List<SolutionItem>, List<TableItem>, List<ComponentItem>>)a.Result;
                    solutions = t.Item1; tables = t.Item2; itemsByKind[kind] = t.Item3;

                    cboSolution.BeginUpdate();
                    cboSolution.Items.Clear();
                    cboSolution.Items.Add(NoSolution);
                    // Your own managed solutions are the usual suspects, so list them first
                    foreach (var s in solutions.Where(x => !x.IsMicrosoft && x.UniqueName != "Default" && x.UniqueName != "Active")
                                               .OrderByDescending(x => x.IsManaged).ThenBy(x => x.FriendlyName))
                        cboSolution.Items.Add(s);
                    cboSolution.EndUpdate();
                    cboSolution.SelectedIndex = cboSolution.Items.Count > 1 ? 1 : 0;   // triggers SolutionChanged
                    SetStatus(string.Format("{0} solutions loaded.", cboSolution.Items.Count - 1));
                }
            });
        }

        private void KindChanged()
        {
            var h = CurrentHandler;
            lblGroup.Text = h.GroupLabel;
            lblComponent.Text = h.Label + ":";
            if (Service == null) return;

            if (itemsByKind.ContainsKey(h.Kind)) { SolutionChanged(); return; }

            var kind = h.Kind;
            WorkAsync(new WorkAsyncInfo
            {
                Message = "Loading " + h.Label.ToLowerInvariant() + "s from Target...",
                Work = (w, a) => { a.Result = Handlers.For(kind).LoadAll(Service); },
                PostWorkCallBack = a =>
                {
                    if (a.Error != null) { MessageBox.Show("Could not load " + h.Label.ToLowerInvariant() + "s:\r\n" + a.Error.Message, "Error"); return; }
                    itemsByKind[kind] = (List<ComponentItem>)a.Result;
                    SolutionChanged();
                }
            });
        }

        private List<ComponentItem> CurrentItems()
        {
            List<ComponentItem> items;
            return itemsByKind.TryGetValue(CurrentKind, out items) ? items : new List<ComponentItem>();
        }

        private void SolutionChanged()
        {
            var sol = cboSolution.SelectedItem as SolutionItem;
            if (sol == null || !chkOnlyInSolution.Checked || Service == null)
            {
                solutionIds = null;
                FillGroups();
                return;
            }

            var h = CurrentHandler;
            var items = CurrentItems();
            WorkAsync(new WorkAsyncInfo
            {
                Message = "Reading " + h.Label.ToLowerInvariant() + "s in " + sol.UniqueName + "...",
                Work = (w, a) => { a.Result = DoctorService.IdsInSolution(Service, sol.Id, h, items, tables); },
                PostWorkCallBack = a =>
                {
                    if (a.Error != null) { MessageBox.Show(a.Error.Message, "Error"); solutionIds = null; }
                    else solutionIds = (HashSet<Guid>)a.Result;
                    FillGroups();
                }
            });
        }

        private IEnumerable<ComponentItem> CandidateItems()
        {
            var items = CurrentItems();
            return solutionIds == null ? items : items.Where(i => solutionIds.Contains(i.Id));
        }

        private string GroupText(string key)
        {
            if (CurrentKind == ComponentKind.WebResource) return key;
            if (key == ProcessHandler.NoTable) return "(no table)";
            var t = tables.FirstOrDefault(x => x.LogicalName == key);
            return t == null ? key : t.ToString();
        }

        private void FillGroups()
        {
            var previous = (cboGroup.SelectedItem as GroupOption)?.Key;
            cboGroup.BeginUpdate();
            cboGroup.Items.Clear();
            foreach (var key in CandidateItems().Select(i => i.Group).Distinct()
                                 .Select(k => new GroupOption { Key = k, Text = GroupText(k) }).OrderBy(g => g.Text))
                cboGroup.Items.Add(key);
            cboGroup.EndUpdate();

            var keep = cboGroup.Items.OfType<GroupOption>().FirstOrDefault(g => g.Key == previous);
            if (keep != null) cboGroup.SelectedItem = keep;
            else if (cboGroup.Items.Count > 0) cboGroup.SelectedIndex = 0;
            else FillComponents();

            var what = CurrentHandler.Label.ToLowerInvariant() + "s";
            if (solutionIds != null && solutionIds.Count == 0)
                SetStatus("This solution contains no " + what + " in Target. Untick 'Only items in this solution' to pick any.");
            else
                SetStatus(CandidateItems().Count() + " " + what + (solutionIds != null ? " in this solution." : " in Target."));
        }

        private void FillComponents()
        {
            var group = (cboGroup.SelectedItem as GroupOption)?.Key;
            cboComponent.BeginUpdate();
            cboComponent.Items.Clear();
            cboComponent.Text = "";
            foreach (var i in CandidateItems().Where(i => i.Group == group).OrderBy(i => i.SubType).ThenBy(i => i.Name))
                cboComponent.Items.Add(i);
            cboComponent.EndUpdate();
            if (cboComponent.Items.Count > 0) cboComponent.SelectedIndex = 0;
        }

        /// <summary>Selected item, also when the user typed the text instead of picking it.</summary>
        private ComponentItem SelectedComponent()
        {
            var item = cboComponent.SelectedItem as ComponentItem;
            if (item != null) return item;
            var text = cboComponent.Text;
            return cboComponent.Items.OfType<ComponentItem>().FirstOrDefault(i => i.ToString() == text || i.Name == text);
        }

        // -------------------------------------------------------- Diagnose

        private void Diagnose()
        {
            var item = SelectedComponent();
            if (item == null) { MessageBox.Show("Pick a " + CurrentHandler.Label.ToLowerInvariant() + " first."); return; }
            var sol = cboSolution.SelectedItem as SolutionItem;
            var dev = DevService;
            var devDetail = DevDetail;

            if (dev != null && targetDetail != null && DevDetail != null &&
                string.Equals(targetDetail.WebApplicationUrl, DevDetail.WebApplicationUrl, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("The Dev connection points to the same environment as Target. Connect Dev to your source environment.", "Deployment Doctor");
                return;
            }

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Diagnosing '" + item.Name + "'...",
                Work = (w, a) => { a.Result = DoctorService.Diagnose(Service, dev, sol, item); },
                PostWorkCallBack = a =>
                {
                    if (a.Error != null) { MessageBox.Show(a.Error.Message, "Error"); return; }
                    lastDiagnosis = (Diagnosis)a.Result;
                    lastItem = item;
                    lastSolution = sol == null ? NoSolution : sol.ToString();
                    lastDevDetail = dev == null ? null : devDetail;
                    lastRunOn = DateTime.Now;
                    ShowDiagnosis(lastDiagnosis);
                }
            });
        }

        private void ShowDiagnosis(Diagnosis d)
        {
            var worst = d.Findings.Select(x => x.Severity).DefaultIfEmpty(Severity.Ok).Min();
            lblVerdict.BackColor = SeverityColor(worst);
            lblVerdict.Text = d.Verdict + Environment.NewLine + "Next step: " + d.NextStep;

            gridFindings.DataSource = d.Findings.ToList();
            gridLayers.DataSource = d.Layers.ToList();

            txtDiff.Text = d.DevContent == null
                ? "Connect Dev (source) to compare Dev with Target."
                : ReportBuilder.DifferencesText(d);
            txtTargetContent.Text = d.TargetContent ?? "";
            txtDevContent.Text = d.DevContent ?? "(no Dev connection, or not found in Dev)";
            txtLayerContent.Text = d.LayerContent ?? "(layer content not available)";

            ddExport.Enabled = true;
            SetStatus(string.Format("{0} problem(s), {1} warning(s).",
                d.Findings.Count(x => x.Severity == Severity.Problem), d.Findings.Count(x => x.Severity == Severity.Warning)));
        }

        private void FindingsCellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || gridFindings.Columns[e.ColumnIndex].Name != "Fix") return;
            var f = gridFindings.Rows[e.RowIndex].DataBoundItem as Finding;
            if (f == null || f.Action == null) return;

            var detail = f.Action.OnDev ? DevDetail : targetDetail;
            var svc = f.Action.OnDev ? DevService : Service;
            if (svc == null) { MessageBox.Show("Not connected to " + (f.Action.OnDev ? "Dev" : "Target") + "."); return; }

            var action = f.Action;
            var envName = action.OnDev ? "DEV" : "TARGET";

            // First find out who the change would run as, then explain everything before anything changes
            WorkAsync(new WorkAsyncInfo
            {
                Message = "Checking which user the " + envName + " connection uses...",
                Work = (w, a) => { a.Result = DoctorService.GetCaller(svc); },
                PostWorkCallBack = a =>
                {
                    var caller = a.Error == null ? (CallerInfo)a.Result : null;
                    using (var dlg = new FixConfirmDialog(action, envName,
                        detail == null ? "?" : detail.ConnectionName, detail == null ? null : detail.WebApplicationUrl, caller))
                    {
                        if (dlg.ShowDialog(this) != DialogResult.OK) { SetStatus(action.Label + " cancelled. Nothing was changed."); return; }
                    }
                    RunFix(action, svc);
                }
            });
        }

        private void RunFix(FixAction action, IOrganizationService svc)
        {
            WorkAsync(new WorkAsyncInfo
            {
                Message = action.Label + "...",
                Work = (w, a) => action.Run(svc),
                PostWorkCallBack = a =>
                {
                    if (a.Error != null) { MessageBox.Show(action.Label + " failed:\r\n" + a.Error.Message, "Error"); return; }
                    MessageBox.Show(action.Label + " done. Running the diagnosis again.", "Deployment Doctor");
                    Diagnose();
                }
            });
        }

        /// <summary>Context of the last diagnosis, for the report header.</summary>
        private ReportContext CurrentContext()
        {
            return new ReportContext
            {
                KindLabel = lastItem == null ? "" : Handlers.For(lastItem.Kind).Label,
                ComponentName = lastItem == null ? "" : lastItem.Name,
                Table = lastItem == null ? "" : (lastItem.Group == ProcessHandler.NoTable ? "(no table)" : lastItem.Group),
                GroupLabel = lastItem == null ? "Table" : Handlers.For(lastItem.Kind).GroupLabel.TrimEnd(':'),
                SubType = lastItem == null ? "" : lastItem.SubType,
                ComponentId = lastItem == null ? Guid.Empty : lastItem.Id,
                Solution = lastSolution,
                TargetName = targetDetail == null ? "" : targetDetail.ConnectionName,
                TargetUrl = targetDetail == null ? null : targetDetail.WebApplicationUrl,
                DevName = lastDevDetail == null ? null : lastDevDetail.ConnectionName,
                DevUrl = lastDevDetail == null ? null : lastDevDetail.WebApplicationUrl,
                Generated = lastRunOn,
                ToolVersion = typeof(DeploymentDoctorControl).Assembly.GetName().Version.ToString(3)
            };
        }

        private void CopyReport()
        {
            if (lastDiagnosis == null) return;
            Clipboard.SetText(ReportBuilder.Text(lastDiagnosis, CurrentContext()));
            SetStatus("Report copied to the clipboard.");
        }

        private void ExportFile(string format)
        {
            if (lastDiagnosis == null) return;
            var ctx = CurrentContext();

            // HTML is the only format with full XML and URLs: ask what may go into the shared file
            if (format == "html")
            {
                using (var opts = new ExportOptionsDialog(exportXml, exportUrls))
                {
                    if (opts.ShowDialog(this) != DialogResult.OK) return;
                    exportXml = opts.IncludeXml;
                    exportUrls = opts.IncludeUrls;
                }
                ctx.IncludeXml = exportXml;
                ctx.IncludeUrls = exportUrls;
            }
            var name = SafeFileName(string.Format("DeploymentDoctor_{0}_{1}_{2:yyyyMMdd-HHmm}", ctx.KindLabel, ctx.ComponentName, ctx.Generated));

            using (var dlg = new SaveFileDialog
            {
                FileName = name + "." + format,
                Filter = format == "html" ? "HTML report (*.html)|*.html" : "CSV (*.csv)|*.csv"
            })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                var content = format == "html" ? ReportBuilder.Html(lastDiagnosis, ctx) : ReportBuilder.Csv(lastDiagnosis, ctx);
                try
                {
                    File.WriteAllText(dlg.FileName, content, new UTF8Encoding(true));
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not save the report:\r\n" + ex.Message, "Error");
                    return;
                }
                SetStatus("Report saved: " + dlg.FileName);

                if (MessageBox.Show("Report saved.\r\n\r\n" + dlg.FileName + "\r\n\r\nOpen it now?", "Deployment Doctor",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                {
                    try { System.Diagnostics.Process.Start(dlg.FileName); }
                    catch (Exception ex) { MessageBox.Show("Could not open the file:\r\n" + ex.Message, "Error"); }
                }
            }
        }

        private static string SafeFileName(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Replace(' ', '_');
        }

        private void ShowAbout()
        {
            var version = typeof(DeploymentDoctorControl).Assembly.GetName().Version;
            MessageBox.Show(
                "Deployment Doctor  v" + version.ToString(3) + "\r\n" +
                "Created by Ankit Rana\r\n\r\n" +
                "Explains why a change deployed with a managed solution does not show in the target environment. " +
                "Works for forms, views, web resources and processes (classic workflows, business rules, actions, cloud flows).\r\n\r\n" +
                "Connect the main connection to the TARGET (UAT/Prod), and optionally a second connection to DEV (source). " +
                "It checks: publish state and solution membership in Dev, solution membership and version in Target, " +
                "solution layers (unmanaged layer, other managed solutions above yours, staged upgrades), " +
                "the content in Dev vs Target vs your solution's layer, plus type checks: form state, apps, security roles and order; " +
                "view state and default view; who uses a web resource; flow on/off, connection references and duplicate copies.\r\n\r\n" +
                "The tool only reads, except when you click a Fix button and confirm. Each fix tells you exactly what it changes and where.",
                "About - Deployment Doctor", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void SetStatus(string text)
        {
            lblStatus.Text = text + "  |  Deployment Doctor - created by Ankit Rana";
        }

        // --------------------------------------------------------- Connections

        public override void UpdateConnection(IOrganizationService newService, ConnectionDetail detail, string actionName, object parameter)
        {
            if (actionName == AdditionalOrganizationAction)
            {
                // Dev connection: the base adds it to AdditionalConnectionDetails -> ConnectionDetailsUpdated
                base.UpdateConnection(newService, detail, actionName, parameter);
                return;
            }

            targetDetail = detail;
            solutions = new List<SolutionItem>(); tables = new List<TableItem>(); itemsByKind.Clear();
            solutionIds = null; lastDiagnosis = null;
            cboSolution.Items.Clear(); cboGroup.Items.Clear(); cboComponent.Items.Clear(); cboComponent.Text = "";
            gridFindings.DataSource = null; gridLayers.DataSource = null;
            ddExport.Enabled = false;
            lblTarget.Text = "Target: " + (detail == null ? "(not connected)" : detail.ConnectionName);
            lblTarget.ForeColor = detail == null ? Color.DarkRed : Color.DarkGreen;

            base.UpdateConnection(newService, detail, actionName, parameter);

            if (newService != null && string.IsNullOrEmpty(actionName))
                LoadLists();
        }

        protected override void ConnectionDetailsUpdated(NotifyCollectionChangedEventArgs e)
        {
            // Keep a single Dev connection: the newest one wins
            if (e.Action == NotifyCollectionChangedAction.Add && AdditionalConnectionDetails.Count > 1)
            {
                foreach (var old in AdditionalConnectionDetails.Take(AdditionalConnectionDetails.Count - 1).ToList())
                    RemoveAdditionalOrganization(old);
                return;
            }

            var dev = DevDetail;
            lblDev.Text = dev == null ? "Dev: (optional, not connected)" : "Dev: " + dev.ConnectionName;
            lblDev.ForeColor = dev == null ? Color.DimGray : Color.DarkGreen;
            btnDisconnectDev.Enabled = dev != null;
            btnConnectDev.Text = dev == null ? "Connect Dev (source)..." : "Change Dev...";
        }
    }
}

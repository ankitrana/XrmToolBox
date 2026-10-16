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
    /// Left: three steps to pick what to check. Right: the answer, then the evidence.
    /// </summary>
    public class DeploymentDoctorControl : MultipleConnectionsPluginControlBase
    {
        private const string AdditionalOrganizationAction = "AdditionalOrganization";

        private ToolStripLabel lblTarget;
        private ToolStripDropDownButton ddDev;
        private ToolStripMenuItem miConnectDev;
        private ToolStripMenuItem miDisconnectDev;
        private ToolStripDropDownButton ddExport;
        private ToolStripButton btnExpand;
        private bool allOpen;

        private PickerPanel picker;
        private ResultsView results;
        private CompareView compareView;
        private ToolStripButton btnModeOne, btnModeAll;
        private bool compareMode;
        private CompareResult lastCompare;
        private string lastCompareDev, lastCompareTarget;
        private System.Windows.Forms.Label lblStatus;

        private ConnectionDetail targetDetail;
        private List<SolutionItem> solutions = new List<SolutionItem>();
        private List<TableItem> tables = new List<TableItem>();
        private readonly Dictionary<ComponentKind, List<ComponentItem>> itemsByKind = new Dictionary<ComponentKind, List<ComponentItem>>();
        private HashSet<Guid> solutionIds;          // null = no solution filter
        private Diagnosis lastDiagnosis;
        private ComponentItem lastItem;
        private SolutionItem lastSolItem;
        private string lastSolution;            // what the last diagnosis ran against, for reports
        private ConnectionDetail lastDevDetail;
        private DateTime lastRunOn;
        private bool exportXml, exportUrls;         // HTML export choices, remembered for this session only

        private const string NoSolution = "(no solution - check the item only)";

        private ComponentHandler CurrentHandler { get { return Handlers.For(CurrentKind); } }
        private ComponentKind CurrentKind { get { return picker.Kind; } }

        public DeploymentDoctorControl()
        {
            BuildUi();
        }

        private ConnectionDetail DevDetail { get { return AdditionalConnectionDetails.LastOrDefault(); } }
        private IOrganizationService DevService { get { var d = DevDetail; return d == null ? null : d.ServiceClient; } }

        // ---------------------------------------------------------------- UI

        private void BuildUi()
        {
            // Toolbar: only connections and a few actions
            var tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
            lblTarget = new ToolStripLabel("\u25CF Target: not connected") { ForeColor = Color.DarkRed, Font = new Font(Font, FontStyle.Bold) };
            ddDev = new ToolStripDropDownButton("\u25CF Dev: optional") { ForeColor = Color.DimGray, ToolTipText = "Connect a second environment (the source) to compare it with Target" };
            miConnectDev = new ToolStripMenuItem("Connect Dev (source)...");
            miDisconnectDev = new ToolStripMenuItem("Disconnect Dev") { Enabled = false };
            ddDev.DropDownItems.Add(miConnectDev);
            ddDev.DropDownItems.Add(miDisconnectDev);
            miConnectDev.Click += (s, e) => AddAdditionalOrganization();
            miDisconnectDev.Click += (s, e) => { var d = DevDetail; if (d != null) RemoveAdditionalOrganization(d); };

            var btnAbout = new ToolStripButton("About") { Alignment = ToolStripItemAlignment.Right };
            ddExport = new ToolStripDropDownButton("Export report") { Enabled = false, Alignment = ToolStripItemAlignment.Right };
            ddExport.DropDownItems.Add("HTML report (to email or attach to a ticket)...", null, (s, e) => ExportFile("html"));
            ddExport.DropDownItems.Add("CSV (opens in Excel)...", null, (s, e) => ExportFile("csv"));
            ddExport.DropDownItems.Add("Copy as text (for Teams / chat)", null, (s, e) => CopyReport());
            var btnReload = new ToolStripButton("Refresh lists") { Alignment = ToolStripItemAlignment.Right };
            btnExpand = new ToolStripButton("Show all details") { Enabled = false, ToolTipText = "Open every section: passed checks, evidence and notes" };
            btnExpand.Click += (s, e) =>
            {
                allOpen = !allOpen;
                results.SetAllExpanded(allOpen);
                btnExpand.Text = allOpen ? "Hide details" : "Show all details";
            };
            btnModeOne = new ToolStripButton("Check one item") { Checked = true, ToolTipText = "Find out why one component did not deploy as expected" };
            btnModeAll = new ToolStripButton("Compare whole solution") { ToolTipText = "Compare every component of a solution between Dev and Target" };
            btnModeOne.Click += (s, e) => SetMode(false);
            btnModeAll.Click += (s, e) => SetMode(true);
            tools.Items.AddRange(new ToolStripItem[] { lblTarget, new ToolStripSeparator(), ddDev, new ToolStripSeparator(), btnModeOne, btnModeAll, new ToolStripSeparator(), btnExpand, btnAbout, ddExport, btnReload });
            btnReload.Click += (s, e) => ExecuteMethod(LoadLists);
            btnAbout.Click += (s, e) => ShowAbout();

            picker = new PickerPanel();
            picker.Solution.SelectedIndexChanged += (s, e) => SolutionChanged();
            picker.OnlyInSolution.CheckedChanged += (s, e) => SolutionChanged();
            picker.ShowSystemSolutions.CheckedChanged += (s, e) => FillSolutionList();
            picker.KindChanged += (s, e) => KindChanged();
            picker.Group.SelectedIndexChanged += (s, e) => FillComponents();
            picker.Component.SelectedIndexChanged += (s, e) => UpdateDiagnoseEnabled();
            picker.Component.TextChanged += (s, e) => UpdateDiagnoseEnabled();
            picker.Diagnose.Click += (s, e) => { if (compareMode) ExecuteMethod(RunCompare); else ExecuteMethod(Diagnose); };

            results = new ResultsView { Dock = DockStyle.Fill };
            compareView = new CompareView { Visible = false };
            compareView.DiagnoseRequested += row => DiagnoseFromCompare(row);

            lblStatus = new System.Windows.Forms.Label { Dock = DockStyle.Bottom, Height = 22, Padding = new Padding(6, 3, 0, 0), Text = "Connect to the TARGET environment (where the change doesn't show) to begin.  |  Deployment Doctor - created by Ankit Rana" };

            Controls.Add(results);
            Controls.Add(compareView);
            Controls.Add(picker);
            Controls.Add(tools);
            Controls.Add(lblStatus);
            Dock = DockStyle.Fill;
        }

        private void UpdateDiagnoseEnabled()
        {
            if (compareMode)
            {
                bool hasDev = DevService != null;
                picker.Diagnose.Enabled = hasDev && picker.Solution.SelectedItem is SolutionItem;
                picker.CompareHint.Text = hasDev
                    ? "Compares every component of the solution picked below in Dev with Target."
                    : "Connect Dev first (toolbar, Dev menu): it is the environment the solution came from.";
                return;
            }
            picker.Diagnose.Enabled = SelectedComponent() != null;
        }

        /// <summary>Switch between "Check one item" and "Compare whole solution".</summary>
        private void SetMode(bool compare)
        {
            compareMode = compare;
            btnModeOne.Checked = !compare;
            btnModeAll.Checked = compare;
            picker.SetCompareMode(compare);
            results.Visible = !compare;
            compareView.Visible = compare;
            btnExpand.Visible = !compare;
            ddExport.Enabled = compare ? lastCompare != null : lastDiagnosis != null;
            UpdateDiagnoseEnabled();
            if (compare) SetStatus(lastCompare == null ? "Pick a solution and click Compare." : "Compared " + lastCompare.SolutionName + ": " + lastCompare.NotMatching + " of " + lastCompare.Rows.Count + " components need attention.");
            else SolutionChanged();
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

                    FillSolutionList();
                }
            });
        }

        /// <summary>Fill the solution dropdown from the solutions installed in Target (Microsoft and system ones only on request).</summary>
        private void FillSolutionList()
        {
            var cbo = picker.Solution;
            var previous = cbo.SelectedItem as SolutionItem;
            var all = picker.ShowSystemSolutions.Checked;

            cbo.BeginUpdate();
            cbo.Items.Clear();
            cbo.Items.Add(NoSolution);
            // Managed solutions first: they are the ones deployed from another environment
            foreach (var s in solutions.Where(x => all || (!x.IsMicrosoft && x.UniqueName != "Default" && x.UniqueName != "Active"))
                                       .OrderByDescending(x => x.IsManaged).ThenBy(x => x.FriendlyName))
                cbo.Items.Add(s);
            cbo.EndUpdate();

            var keep = previous == null ? null : cbo.Items.OfType<SolutionItem>().FirstOrDefault(s => s.Id == previous.Id);
            if (keep != null) cbo.SelectedItem = keep;
            else cbo.SelectedIndex = cbo.Items.Count > 1 ? 1 : 0;   // triggers SolutionChanged
            SetStatus(string.Format("{0} solutions installed in Target{1}.", cbo.Items.Count - 1, all ? "" : " (Microsoft and system solutions hidden)"));
        }

        private void KindChanged()
        {
            var h = CurrentHandler;
            picker.GroupLabel.Text = h.GroupLabel.TrimEnd(':');
            picker.ComponentLabel.Text = h.Label;
            picker.ShowGroups(h.HasGroups);
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
            if (compareMode) { UpdateDiagnoseEnabled(); return; }
            var sol = picker.Solution.SelectedItem as SolutionItem;
            if (sol == null || !picker.OnlyInSolution.Checked || Service == null)
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
            if (key == ProcessHandler.NoTable) return CurrentKind == ComponentKind.Ribbon ? "(application ribbon)" : "(no table)";
            var t = tables.FirstOrDefault(x => x.LogicalName == key);
            return t == null ? key : t.ToString();
        }

        private void FillGroups()
        {
            var cbo = picker.Group;
            var previous = (cbo.SelectedItem as GroupOption)?.Key;
            cbo.BeginUpdate();
            cbo.Items.Clear();
            foreach (var key in CandidateItems().Select(i => i.Group).Distinct()
                                 .Select(k => new GroupOption { Key = k, Text = GroupText(k) }).OrderBy(g => g.Text))
                cbo.Items.Add(key);
            cbo.EndUpdate();

            var keep = cbo.Items.OfType<GroupOption>().FirstOrDefault(g => g.Key == previous);
            if (keep != null) cbo.SelectedItem = keep;
            else if (cbo.Items.Count > 0) cbo.SelectedIndex = 0;
            else FillComponents();

            var what = CurrentHandler.Label.ToLowerInvariant() + "s";
            if (solutionIds != null && solutionIds.Count == 0)
                SetStatus("This solution contains no " + what + " in Target. Untick 'Only items in this solution' to pick any.");
            else
                SetStatus(CandidateItems().Count() + " " + what + (solutionIds != null ? " in this solution." : " in Target."));
        }

        private void FillComponents()
        {
            var cbo = picker.Component;
            var group = (picker.Group.SelectedItem as GroupOption)?.Key;
            cbo.BeginUpdate();
            cbo.Items.Clear();
            cbo.Text = "";
            foreach (var i in CandidateItems().Where(i => i.Group == group).OrderBy(i => i.SubType).ThenBy(i => i.Name))
                cbo.Items.Add(i);
            cbo.EndUpdate();
            if (cbo.Items.Count > 0) cbo.SelectedIndex = 0;
            UpdateDiagnoseEnabled();
        }

        /// <summary>Selected item, also when the user typed the text instead of picking it.</summary>
        private ComponentItem SelectedComponent()
        {
            var cbo = picker.Component;
            var item = cbo.SelectedItem as ComponentItem;
            if (item != null) return item;
            var text = cbo.Text;
            if (string.IsNullOrEmpty(text)) return null;
            return cbo.Items.OfType<ComponentItem>().FirstOrDefault(i => i.ToString() == text || i.Name == text);
        }

        // -------------------------------------------------------- Compare whole solution

        private void RunCompare()
        {
            var sol = picker.Solution.SelectedItem as SolutionItem;
            if (sol == null) { MessageBox.Show("Pick a solution first."); return; }
            var dev = DevService;
            if (dev == null) { MessageBox.Show("Connect Dev first (toolbar, Dev menu). It is the environment the solution was exported from.", "Deployment Doctor"); return; }
            if (targetDetail != null && DevDetail != null &&
                string.Equals(targetDetail.WebApplicationUrl, DevDetail.WebApplicationUrl, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("The Dev connection points to the same environment as Target. Connect Dev to your source environment.", "Deployment Doctor");
                return;
            }

            var devName = DevDetail == null ? "Dev" : DevDetail.ConnectionName;
            var targetName = targetDetail == null ? "Target" : targetDetail.ConnectionName;
            WorkAsync(new WorkAsyncInfo
            {
                Message = "Comparing " + sol.FriendlyName + " between Dev and Target...",
                Work = (w, a) => { a.Result = SolutionCompare.Run(dev, Service, sol, m => w.ReportProgress(0, m)); },
                ProgressChanged = e => { var m = e.UserState as string; if (m != null) SetWorkingMessage(m); },
                PostWorkCallBack = a =>
                {
                    if (a.Error != null) { MessageBox.Show(a.Error.Message, "Deployment Doctor"); return; }
                    lastCompare = (CompareResult)a.Result;
                    lastCompareDev = devName;
                    lastCompareTarget = targetName;
                    compareView.ShowResult(lastCompare, devName, targetName);
                    ddExport.Enabled = true;
                    SetStatus("Compared " + lastCompare.SolutionName + ": " + lastCompare.NotMatching + " of " + lastCompare.Rows.Count + " components need attention.");
                }
            });
        }

        // -------------------------------------------------------- Diagnose
        private void Diagnose()
        {
            var item = SelectedComponent();
            if (item == null) { MessageBox.Show("Pick a " + CurrentHandler.Label.ToLowerInvariant() + " first."); return; }
            DiagnoseItem(item, picker.Solution.SelectedItem as SolutionItem);
        }

        /// <summary>From the solution compare: diagnose one component, shown on the "Check one item" screen.</summary>
        private void DiagnoseFromCompare(CompareRow row)
        {
            SetMode(false);
            DiagnoseItem(row.Item, picker.Solution.SelectedItem as SolutionItem);
        }

        private void DiagnoseItem(ComponentItem item, SolutionItem sol)
        {
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
                    lastSolItem = sol;
                    lastSolution = sol == null ? NoSolution : sol.ToString();
                    lastDevDetail = dev == null ? null : devDetail;
                    lastRunOn = DateTime.Now;
                    ShowDiagnosis(lastDiagnosis);
                }
            });
        }

        private void ShowDiagnosis(Diagnosis d)
        {
            results.Show(d, OnFixRequested);
            ddExport.Enabled = true;
            allOpen = false;
            btnExpand.Enabled = true;
            btnExpand.Text = "Show all details";
            SetStatus(string.Format("{0} problem(s), {1} warning(s).",
                d.Findings.Count(x => x.Severity == Severity.Problem), d.Findings.Count(x => x.Severity == Severity.Warning)));
        }

        /// <summary>A Fix button was clicked: explain the change first, and only run it after the user agrees.</summary>
        private void OnFixRequested(Finding f)
        {
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
                    if (lastItem != null) DiagnoseItem(lastItem, lastSolItem); else Diagnose();
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
                Table = lastItem == null ? "" : (lastItem.Group == ProcessHandler.NoTable ? (lastItem.Kind == ComponentKind.Ribbon ? "(application ribbon)" : "(no table)") : lastItem.Group == SiteMapHandler.Everything ? "-" : lastItem.Group),
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
            if (compareMode)
            {
                if (lastCompare == null) return;
                Clipboard.SetText(CompareReport.Text(lastCompare, lastCompareDev, lastCompareTarget));
                SetStatus("Compare result copied to the clipboard.");
                return;
            }
            if (lastDiagnosis == null) return;
            Clipboard.SetText(ReportBuilder.Text(lastDiagnosis, CurrentContext()));
            SetStatus("Report copied to the clipboard.");
        }

        private void ExportFile(string format)
        {
            if (compareMode) { ExportCompare(format); return; }
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

        private void ExportCompare(string format)
        {
            if (lastCompare == null) return;
            var version = typeof(DeploymentDoctorControl).Assembly.GetName().Version.ToString(3);
            var name = SafeFileName(string.Format("SolutionCompare_{0}_{1:yyyyMMdd-HHmm}", lastCompare.SolutionName, lastCompare.RanOn));
            using (var dlg = new SaveFileDialog
            {
                FileName = name + "." + format,
                Filter = format == "html" ? "HTML report (*.html)|*.html" : "CSV (*.csv)|*.csv"
            })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                var content = format == "html"
                    ? CompareReport.Html(lastCompare, lastCompareDev, lastCompareTarget, version)
                    : CompareReport.Csv(lastCompare, lastCompareDev, lastCompareTarget);
                try { File.WriteAllText(dlg.FileName, content, new UTF8Encoding(true)); }
                catch (Exception ex) { MessageBox.Show("Could not save the report:\r\n" + ex.Message, "Error"); return; }
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
                "Works for forms, views, site maps, ribbons, web resources, processes (classic workflows, business rules, actions, cloud flows), environment variables, connection references, plug-in steps and plug-in assemblies.\r\n\r\n" +
                "Connect the main connection to the TARGET (UAT/Prod), and optionally a second connection to DEV (source). " +
                "It checks: publish state and solution membership in Dev, solution membership and version in Target, " +
                "solution layers (unmanaged layer, other managed solutions above yours, staged upgrades), " +
                "the content in Dev vs Target vs your solution's layer, plus type checks: form health (is the form really corrupted, or only broken references?), same-name forms, form state, apps, security roles and order; " +
                "view state and default view; who uses a web resource; flow on/off, connection references and duplicate copies; environment variable values; whether a connection reference has a connection; whether a plug-in step is enabled and which assembly version it runs.\r\n\r\n" +
                "Compare whole solution: pick a solution and compare every component between Dev and Target (content, on/off state, unpublished changes, items removed in Dev), then open any component in the doctor. Not compared yet: tables, columns, choices, security roles, model-driven apps and dashboards.\r\n\r\n" +
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
            solutionIds = null; lastDiagnosis = null; lastCompare = null; lastItem = null; lastSolItem = null;
            compareView.ShowEmpty();
            picker.Solution.Items.Clear(); picker.Group.Items.Clear(); picker.Component.Items.Clear(); picker.Component.Text = "";
            results.ShowEmpty();
            ddExport.Enabled = false;
            btnExpand.Enabled = false;
            UpdateDiagnoseEnabled();
            lblTarget.Text = "\u25CF Target: " + (detail == null ? "not connected" : detail.ConnectionName);
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
            ddDev.Text = dev == null ? "\u25CF Dev: optional" : "\u25CF Dev: " + dev.ConnectionName;
            ddDev.ForeColor = dev == null ? Color.DimGray : Color.DarkGreen;
            miDisconnectDev.Enabled = dev != null;
            miConnectDev.Text = dev == null ? "Connect Dev (source)..." : "Change Dev...";
            UpdateDiagnoseEnabled();
        }
    }
}

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using McTools.Xrm.Connection;
using Microsoft.Xrm.Sdk;
using XrmToolBox.Extensibility;

namespace UnmanagedLayerFinder
{
    public class UnmanagedLayerFinderControl : PluginControlBase
    {
        private ToolStripButton btnLoadSolutions;
        private ToolStripComboBox cboSolutions;
        private ToolStripButton btnLoadComponents;
        private ToolStripButton btnCheck;
        private ToolStripButton btnExport;
        private CheckBox chkAudit;
        private CheckBox chkOnlyUnmanaged;
        private ComboBox cboComponentType;
        private const string AllTypes = "All types";
        private CheckBox chkSelectAll;
        private DataGridView grid;
        private System.Windows.Forms.Label lblSummary;

        // Solution filters
        private ToolStripComboBox cboSolutionType;
        private ToolStripDropDownButton ddPublishers;
        private CheckBox chkExcludeMicrosoft;

        private const string TypeUnmanaged = "Unmanaged";
        private const string TypeManaged = "Managed";
        private const string TypeBoth = "Both";

        private List<SolutionItem> allSolutions = new List<SolutionItem>();
        private List<ComponentRow> allRows = new List<ComponentRow>();

        public UnmanagedLayerFinderControl()
        {
            BuildUi();
        }

        // ---------------------------------------------------------------- UI

        private void BuildUi()
        {
            var tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
            btnLoadSolutions = new ToolStripButton("Load solutions");
            cboSolutions = new ToolStripComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AutoSize = false, Width = 350, DropDownWidth = 500 };
            btnLoadComponents = new ToolStripButton("Load components");
            btnCheck = new ToolStripButton("Check layers (selected)");
            btnExport = new ToolStripButton("Export CSV");
            var btnAbout = new ToolStripButton("About") { Alignment = ToolStripItemAlignment.Right };
            btnAbout.Click += (s, e) => ShowAbout();
            tools.Items.AddRange(new ToolStripItem[]
            {
                btnLoadSolutions, new ToolStripSeparator(), cboSolutions, btnLoadComponents,
                new ToolStripSeparator(), btnCheck, new ToolStripSeparator(), btnExport, btnAbout
            });

            btnLoadSolutions.Click += (s, e) => ExecuteMethod(LoadSolutions);
            btnLoadComponents.Click += (s, e) => ExecuteMethod(LoadComponents);
            btnCheck.Click += (s, e) => ExecuteMethod(CheckLayers);
            btnExport.Click += (s, e) => ExportCsv();

            // Second row: solution filters (applied locally, no extra Dataverse calls)
            var filters = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
            cboSolutionType = new ToolStripComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AutoSize = false, Width = 110 };
            cboSolutionType.Items.AddRange(new object[] { TypeUnmanaged, TypeManaged, TypeBoth });
            cboSolutionType.SelectedItem = TypeBoth;
            cboSolutionType.SelectedIndexChanged += (s, e) => ApplySolutionFilter();

            ddPublishers = new ToolStripDropDownButton("Publishers (All)") { Enabled = false };
            // Keep the menu open while ticking several publishers
            ddPublishers.DropDown.Closing += (s, e) =>
            {
                if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked) e.Cancel = true;
            };

            chkExcludeMicrosoft = new CheckBox { Text = "Exclude Microsoft solutions", AutoSize = true, BackColor = System.Drawing.Color.Transparent };
            chkExcludeMicrosoft.CheckedChanged += (s, e) => { BuildPublisherMenu(); ApplySolutionFilter(); };

            filters.Items.AddRange(new ToolStripItem[]
            {
                new ToolStripLabel("Solution type:"), cboSolutionType, new ToolStripSeparator(),
                new ToolStripLabel("Publisher:"), ddPublishers, new ToolStripSeparator(),
                new ToolStripControlHost(chkExcludeMicrosoft)
            });

            var options = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6, 4, 6, 0) };
            chkSelectAll = new CheckBox { Text = "Select all", AutoSize = true };
            chkOnlyUnmanaged = new CheckBox { Text = "Show only unmanaged", AutoSize = true };
            chkAudit = new CheckBox { Text = "Also look up audit log (needs auditing enabled at change time)", AutoSize = true };
            var lblType = new System.Windows.Forms.Label { Text = "Component type:", AutoSize = true, Margin = new Padding(3, 6, 0, 0) };
            cboComponentType = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
            cboComponentType.Items.Add(AllTypes);
            cboComponentType.SelectedIndex = 0;
            options.Controls.AddRange(new Control[] { lblType, cboComponentType, chkSelectAll, chkOnlyUnmanaged, chkAudit });
            chkSelectAll.CheckedChanged += (s, e) => SetAllSelected(chkSelectAll.Checked);
            chkOnlyUnmanaged.CheckedChanged += (s, e) => BindGrid();
            cboComponentType.SelectedIndexChanged += (s, e) => BindGrid();

            grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AutoGenerateColumns = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
                BackgroundColor = System.Drawing.SystemColors.Window
            };
            grid.DataBindingComplete += (s, e) => FormatGrid();
            grid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };

            lblSummary = new System.Windows.Forms.Label { Dock = DockStyle.Bottom, Height = 22, Padding = new Padding(6, 3, 0, 0), Text = "Connect to an organization to begin.  |  Unmanaged Layer Finder - created by Ankit Rana" };

            Controls.Add(grid);
            Controls.Add(options);
            Controls.Add(filters);   // docked under the main toolbar
            Controls.Add(tools);
            Controls.Add(lblSummary);
            Dock = DockStyle.Fill;
        }

        private void FormatGrid()
        {
            foreach (DataGridViewColumn c in grid.Columns)
            {
                c.ReadOnly = c.Name != "Selected";
                if (c.ValueType == typeof(DateTime?) || c.ValueType == typeof(DateTime))
                    c.DefaultCellStyle.Format = "yyyy-MM-dd HH:mm";
            }
            foreach (DataGridViewRow r in grid.Rows)
            {
                var row = r.DataBoundItem as ComponentRow;
                if (row == null) continue;
                r.DefaultCellStyle.BackColor =
                    row.Status == "Unmanaged" ? System.Drawing.Color.MistyRose :
                    row.Status == "Error" ? System.Drawing.Color.LightYellow :
                    System.Drawing.SystemColors.Window;
            }
        }

        /// <summary>Selected component type, or null for all types.</summary>
        private string SelectedType
        {
            get
            {
                var item = cboComponentType.SelectedItem as TypeOption;
                return item == null ? null : item.Type;
            }
        }

        private class TypeOption
        {
            public string Type;
            public string Text;
            public override string ToString() { return Text; }
        }

        /// <summary>Fill the component type filter from the loaded rows, with total and unmanaged counts.</summary>
        private void BuildTypeFilter()
        {
            var previous = SelectedType;
            cboComponentType.BeginUpdate();
            cboComponentType.Items.Clear();
            cboComponentType.Items.Add(AllTypes);
            foreach (var g in allRows.GroupBy(r => r.Type).OrderBy(g => g.Key))
            {
                int unmanaged = g.Count(r => r.Status == "Unmanaged");
                cboComponentType.Items.Add(new TypeOption
                {
                    Type = g.Key,
                    Text = unmanaged > 0
                        ? string.Format("{0} ({1}, {2} unmanaged)", g.Key, g.Count(), unmanaged)
                        : string.Format("{0} ({1})", g.Key, g.Count())
                });
            }
            var keep = cboComponentType.Items.OfType<TypeOption>().FirstOrDefault(o => o.Type == previous);
            if (keep != null) cboComponentType.SelectedItem = keep;
            else cboComponentType.SelectedIndex = 0;
            cboComponentType.EndUpdate();
        }

        private List<ComponentRow> VisibleRows()
        {
            IEnumerable<ComponentRow> src = allRows;
            var type = SelectedType;
            if (type != null) src = src.Where(r => r.Type == type);
            if (chkOnlyUnmanaged.Checked) src = src.Where(r => r.Status == "Unmanaged");
            return src.ToList();
        }

        private void BindGrid()
        {
            var visible = VisibleRows();
            grid.DataSource = new BindingList<ComponentRow>(visible);

            int unmanaged = allRows.Count(r => r.Status == "Unmanaged");
            int checkedCount = allRows.Count(r => !string.IsNullOrEmpty(r.Status) && r.Status != "Not supported");
            var type = SelectedType;
            var scope = type == null
                ? ""
                : string.Format(" | showing {0}: {1} rows, {2} unmanaged", type, visible.Count,
                    allRows.Count(r => r.Type == type && r.Status == "Unmanaged"));
            lblSummary.Text = string.Format("{0} components | {1} checked | {2} with unmanaged layer{3}",
                allRows.Count, checkedCount, unmanaged, scope);
        }

        private void ShowAbout()
        {
            var version = typeof(UnmanagedLayerFinderControl).Assembly.GetName().Version;
            MessageBox.Show(
                "Unmanaged Layer Finder  v" + version.ToString(3) + "\r\n" +
                "Created by Ankit Rana\r\n\r\n" +
                "Finds unmanaged (Active) layers on the components of a solution.\r\n" +
                "For each one it shows the layer stack, when the unmanaged layer was written, " +
                "what changed, and who last modified it (from the component record or the audit log).\r\n\r\n" +
                "Notes:\r\n" +
                "- Layer data comes from the msdyn_componentlayer table.\r\n" +
                "- 'Modified by' is the last modifier of the component record; metadata components " +
                "(tables, columns) have none, so use the audit option, which needs auditing enabled when the change was made.\r\n" +
                "- This tool is read-only. It does not change your environment.",
                "About - Unmanaged Layer Finder", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void SetAllSelected(bool value)
        {
            // Only the rows currently shown, so you can e.g. filter to Forms and check just those
            foreach (var r in VisibleRows().Where(x => x.Status != "Not supported")) r.Selected = value;
            BindGrid();
        }

        // ------------------------------------------------------------ Actions

        private void LoadSolutions()
        {
            WorkAsync(new WorkAsyncInfo
            {
                Message = "Loading solutions...",
                Work = (w, a) => { a.Result = LayerService.LoadSolutions(Service); },
                PostWorkCallBack = a =>
                {
                    if (a.Error != null) { MessageBox.Show("Could not load solutions:\r\n" + a.Error.Message, "Error"); return; }
                    allSolutions = (List<SolutionItem>)a.Result;
                    BuildPublisherMenu();
                    ApplySolutionFilter();
                }
            });
        }

        // ---------------------------------------------------- Solution filters

        private static string PublisherKey(SolutionItem s)
        {
            return string.IsNullOrEmpty(s.PublisherName) ? "(no publisher)" : s.PublisherName;
        }

        /// <summary>Rebuild the publisher checklist from the loaded solutions, keeping previous ticks.</summary>
        private void BuildPublisherMenu()
        {
            var previouslyUnticked = new HashSet<string>(ddPublishers.DropDownItems.OfType<ToolStripMenuItem>()
                .Where(i => i.Tag is string && !i.Checked).Select(i => (string)i.Tag));

            ddPublishers.DropDownItems.Clear();

            var publishers = allSolutions
                .Where(s => !(chkExcludeMicrosoft.Checked && s.IsMicrosoft))
                .GroupBy(PublisherKey)
                .OrderBy(g => g.Key)
                .ToList();

            var selectAll = new ToolStripMenuItem("Select all");
            selectAll.Click += (s, e) => SetAllPublishers(true);
            var clearAll = new ToolStripMenuItem("Clear all");
            clearAll.Click += (s, e) => SetAllPublishers(false);
            ddPublishers.DropDownItems.Add(selectAll);
            ddPublishers.DropDownItems.Add(clearAll);
            ddPublishers.DropDownItems.Add(new ToolStripSeparator());

            foreach (var g in publishers)
            {
                var item = new ToolStripMenuItem(string.Format("{0} ({1})", g.Key, g.Count()))
                {
                    Tag = g.Key,
                    CheckOnClick = true,
                    Checked = !previouslyUnticked.Contains(g.Key)
                };
                item.CheckedChanged += (s, e) => ApplySolutionFilter();
                ddPublishers.DropDownItems.Add(item);
            }

            ddPublishers.Enabled = publishers.Count > 0;
        }

        private void SetAllPublishers(bool value)
        {
            foreach (var item in ddPublishers.DropDownItems.OfType<ToolStripMenuItem>().Where(i => i.Tag is string))
                item.Checked = value;
            ApplySolutionFilter();
        }

        /// <summary>Refill the solution dropdown using the type, publisher and Microsoft filters.</summary>
        private void ApplySolutionFilter()
        {
            if (cboSolutions == null) return;

            var publisherItems = ddPublishers.DropDownItems.OfType<ToolStripMenuItem>().Where(i => i.Tag is string).ToList();
            var tickedPublishers = new HashSet<string>(publisherItems.Where(i => i.Checked).Select(i => (string)i.Tag));
            string type = cboSolutionType.SelectedItem as string ?? TypeBoth;

            var filtered = allSolutions.Where(s =>
                (type == TypeBoth || (type == TypeManaged) == s.IsManaged) &&
                !(chkExcludeMicrosoft.Checked && s.IsMicrosoft) &&
                tickedPublishers.Contains(PublisherKey(s))).ToList();

            var previous = cboSolutions.SelectedItem as SolutionItem;
            cboSolutions.Items.Clear();
            foreach (var s in filtered) cboSolutions.Items.Add(s);
            var keep = previous == null ? null : filtered.FirstOrDefault(s => s.Id == previous.Id);
            if (keep != null) cboSolutions.SelectedItem = keep;
            else if (filtered.Count > 0) cboSolutions.SelectedIndex = 0;

            ddPublishers.Text = tickedPublishers.Count == publisherItems.Count
                ? "Publishers (All)"
                : string.Format("Publishers ({0} of {1})", tickedPublishers.Count, publisherItems.Count);

            if (allSolutions.Count > 0)
                lblSummary.Text = string.Format("{0} of {1} solutions shown. Pick one and click 'Load components'.  |  Created by Ankit Rana",
                    filtered.Count, allSolutions.Count);
        }

        private void LoadComponents()
        {
            var sol = cboSolutions.SelectedItem as SolutionItem;
            if (sol == null) { MessageBox.Show("Load and pick a solution first."); return; }

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Loading components of " + sol.Display + "...",
                Work = (w, a) => { a.Result = LayerService.LoadComponents(Service, sol.Id); },
                PostWorkCallBack = a =>
                {
                    if (a.Error != null) { MessageBox.Show(a.Error.Message, "Error"); return; }
                    allRows = (List<ComponentRow>)a.Result;
                    chkSelectAll.Checked = false;
                    BuildTypeFilter();
                    BindGrid();
                }
            });
        }

        private void CheckLayers()
        {
            var type = SelectedType;
            var targets = allRows.Where(r => r.Selected && (type == null || r.Type == type)).ToList();
            if (targets.Count == 0)
            {
                MessageBox.Show(type == null ? "Tick at least one component." : "Tick at least one " + type + " component.");
                return;
            }
            bool audit = chkAudit.Checked;

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Checking layers...",
                Work = (w, a) =>
                {
                    for (int i = 0; i < targets.Count; i++)
                    {
                        if (w.CancellationPending) { a.Cancel = true; return; }
                        w.ReportProgress(i * 100 / targets.Count,
                            string.Format("Checking {0}/{1}: {2}", i + 1, targets.Count, targets[i].Name));
                        LayerService.CheckComponent(Service, targets[i], audit);
                    }
                },
                ProgressChanged = e => SetWorkingMessage(e.UserState as string),
                PostWorkCallBack = a =>
                {
                    if (a.Error != null) MessageBox.Show(a.Error.Message, "Error");
                    BuildTypeFilter();   // refresh "(n, x unmanaged)" counts
                    BindGrid();
                }
            });
        }

        private void ExportCsv()
        {
            var rows = (grid.DataSource as BindingList<ComponentRow>)?.ToList() ?? allRows;
            if (rows.Count == 0) { MessageBox.Show("Nothing to export."); return; }

            using (var dlg = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "UnmanagedLayers.csv" })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;

                var sb = new StringBuilder();
                sb.AppendLine("Type,Name,Details,ComponentId,Status,Layers,LayerStack,UnmanagedLayerTime,ChangedAttributes,ModifiedBy,ModifiedOn,AuditUser,AuditTime,Notes");
                foreach (var r in rows)
                {
                    sb.AppendLine(string.Join(",", new[]
                    {
                        Csv(r.Type), Csv(r.Name), Csv(r.Details), Csv(r.ComponentId.ToString()), Csv(r.Status), Csv(r.Layers.ToString()),
                        Csv(r.LayerStack), Csv(Fmt(r.UnmanagedLayerTime)), Csv(r.ChangedAttributes), Csv(r.ModifiedBy),
                        Csv(Fmt(r.ModifiedOn)), Csv(r.AuditUser), Csv(Fmt(r.AuditTime)), Csv(r.Notes)
                    }));
                }
                File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
            }
        }

        private static string Fmt(DateTime? d) { return d.HasValue ? d.Value.ToString("yyyy-MM-dd HH:mm:ss") : ""; }
        private static string Csv(string s)
        {
            if (s == null) return "";
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        // --------------------------------------------------------- Connection

        public override void UpdateConnection(IOrganizationService newService, ConnectionDetail detail, string actionName, object parameter)
        {
            // Reset first: base.UpdateConnection may run a pending action (e.g. LoadSolutions) that refills the list
            cboSolutions.Items.Clear();
            allSolutions = new List<SolutionItem>();
            ddPublishers.DropDownItems.Clear();
            ddPublishers.Enabled = false;
            allRows = new List<ComponentRow>();
            BuildTypeFilter();
            BindGrid();
            lblSummary.Text = "Connected to " + (detail == null ? "organization" : detail.ConnectionName) + ". Loading solutions...  |  Created by Ankit Rana";

            base.UpdateConnection(newService, detail, actionName, parameter);

            // Auto-load solutions on connect, unless the connect was triggered by a button that will load them itself
            if (newService != null && string.IsNullOrEmpty(actionName))
                LoadSolutions();
        }
    }
}

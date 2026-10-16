using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace DeploymentDoctor
{
    /// <summary>
    /// The right-hand side of "Compare whole solution": a summary line, one row per component
    /// (red = not the same, orange = needs a look, green = same) and the full details of the selected row.
    /// </summary>
    internal class CompareView : Panel
    {
        private readonly System.Windows.Forms.Label emptyText;
        private readonly Panel body;
        private readonly System.Windows.Forms.Label title;
        private readonly System.Windows.Forms.Label summary;
        private readonly System.Windows.Forms.Label note;
        private readonly CheckBox hideSame;
        private readonly DataGridView grid;
        private readonly TextBox details;
        private readonly Button diagnose;
        private CompareResult result;

        public event Action<CompareRow> DiagnoseRequested;

        public CompareView()
        {
            BackColor = Look.Surface;
            Dock = DockStyle.Fill;

            emptyText = new System.Windows.Forms.Label
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(30, 28, 30, 0),
                ForeColor = SystemColors.ControlText,
                Font = Look.Body
            };
            ShowEmptyText();

            body = new Panel { Dock = DockStyle.Fill, Visible = false, BackColor = Look.Surface };

            var head = new Panel { Dock = DockStyle.Top, Height = 104, BackColor = Color.White, Padding = new Padding(16, 10, 16, 6) };
            head.Paint += (s, e) => { using (var p = new Pen(Look.Border)) e.Graphics.DrawLine(p, 0, head.Height - 1, head.Width, head.Height - 1); };
            title = new System.Windows.Forms.Label { Dock = DockStyle.Top, Height = 28, Font = Look.Title, AutoEllipsis = true };
            summary = new System.Windows.Forms.Label { Dock = DockStyle.Top, Height = 24, Font = Look.Bold, AutoEllipsis = true };
            note = new System.Windows.Forms.Label { Dock = DockStyle.Top, Height = 22, ForeColor = Look.Dim, AutoEllipsis = true };
            hideSame = new CheckBox { Text = "Hide components that match", AutoSize = true, Dock = DockStyle.Top };
            hideSame.CheckedChanged += (s, e) => Fill();
            head.Controls.Add(hideSame); head.Controls.Add(note); head.Controls.Add(summary); head.Controls.Add(title);

            grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                GridColor = Look.Border,
                EnableHeadersVisualStyles = false,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 28,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None
            };
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(236, 240, 244);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = SystemColors.ControlText;
            grid.ColumnHeadersDefaultCellStyle.Font = Look.Bold;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(222, 236, 249);
            grid.DefaultCellStyle.SelectionForeColor = SystemColors.ControlText;
            grid.RowTemplate.Height = 26;
            AddColumn("Status", 140);
            AddColumn("Type", 110);
            AddColumn("Component", 200);
            AddColumn("Table", 100);
            AddColumn("Dev", 62);
            AddColumn("Target", 62);
            AddColumn("What differs", 400);
            grid.Columns[6].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            grid.Columns[6].MinimumWidth = 220;
            grid.SelectionChanged += (s, e) => ShowDetails();

            var lower = new Panel { Dock = DockStyle.Bottom, Height = 230, BackColor = Color.White, Padding = new Padding(16, 8, 16, 8) };
            lower.Paint += (s, e) => { using (var p = new Pen(Look.Border)) e.Graphics.DrawLine(p, 0, 0, lower.Width, 0); };
            diagnose = Look.PrimaryButton("Diagnose this component", 190);
            diagnose.Dock = DockStyle.Top;
            diagnose.Enabled = false;
            diagnose.Click += (s, e) =>
            {
                var row = Selected();
                var h = DiagnoseRequested;
                if (row != null && h != null) h(row);
            };
            details = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None,
                BackColor = Color.White,
                Font = Look.Body
            };
            lower.Controls.Add(details);
            var gap = new Panel { Dock = DockStyle.Top, Height = 6 };
            lower.Controls.Add(gap);
            lower.Controls.Add(diagnose);

            body.Controls.Add(grid);
            body.Controls.Add(lower);
            body.Controls.Add(head);
            Controls.Add(body);
            Controls.Add(emptyText);
        }

        private void AddColumn(string name, int width)
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = name, Width = width, SortMode = DataGridViewColumnSortMode.NotSortable });
        }

        private void ShowEmptyText()
        {
            emptyText.Text =
                "Did everything in the solution arrive in Target as it is in Dev?\r\n\r\n" +
                "1.  Connect Dev (toolbar, Dev menu): it is the environment the solution came from.\r\n" +
                "2.  Pick the solution on the left.\r\n" +
                "3.  Click Compare.\r\n\r\n" +
                "Every form, view, site map, ribbon, process / flow, environment variable, connection reference, plug-in step, plug-in assembly and web resource of the solution is compared between Dev and Target. " +
                "Components that are not the same are shown in red, ones that need a look in orange (for example a flow that is on in Dev but off in Target, or changes that were never published in Dev). " +
                "Click a row to see exactly what is different.\r\n\r\n" +
                "Not compared yet: tables, columns, choices, security roles, model-driven apps and dashboards (they are only counted in the header). " +
                "Only published content is compared. Dev must be connected.\r\n\r\n" +
                "Nothing is changed. It only reads from your environments.";
        }

        public void ShowEmpty()
        {
            result = null;
            body.Visible = false;
            emptyText.Visible = true;
        }

        public void ShowResult(CompareResult r, string devName, string targetName)
        {
            result = r;
            emptyText.Visible = false;
            body.Visible = true;

            title.Text = "Dev → Target: " + r.SolutionName;
            var parts = new List<string>();
            Func<CompareStatus, string, string> add = (s, label) => { var n = r.Count(s); return n == 0 ? null : n + " " + label; };
            parts.Add(r.Count(CompareStatus.Same) + " same");
            foreach (var p in new[]
            {
                add(CompareStatus.Different, "different"),
                add(CompareStatus.MissingInTarget, "missing in Target"),
                add(CompareStatus.StateDiffers, "state differs"),
                add(CompareStatus.NotPublishedInDev, "not published in Dev"),
                add(CompareStatus.OnlyInTarget, "only in Target"),
                add(CompareStatus.CouldNotCheck, "could not be checked")
            }) if (p != null) parts.Add(p);
            summary.Text = r.NotMatching == 0
                ? "✔  All " + r.Rows.Count + " components are the same in Dev and Target"
                : r.NotMatching + " of " + r.Rows.Count + " components need attention  (" + string.Join(", ", parts) + ")";
            summary.ForeColor = r.NotMatching == 0 ? Look.Ok : r.Count(CompareStatus.Different) + r.Count(CompareStatus.MissingInTarget) > 0 ? Look.Problem : Look.Warning;

            var versionText = "Solution version: Dev " + r.DevVersion + ", Target " + r.TargetVersion + (r.DevVersion != r.TargetVersion ? "  (different: the latest version may not be deployed)" : "");
            note.Text = versionText + (r.NotCompared.Count > 0 ? "   |   Not compared yet: " + string.Join(", ", r.NotCompared.Take(6)) + (r.NotCompared.Count > 6 ? " ..." : "") : "");
            Fill();
        }

        private void Fill()
        {
            grid.Rows.Clear();
            if (result == null) return;
            foreach (var r in result.Rows.Where(x => !hideSame.Checked || x.Status != CompareStatus.Same))
            {
                int i = grid.Rows.Add(Look.GlyphFor(r.Severity) + "  " + r.StatusText, r.TypeName, r.Item.Name, r.TableText, r.DevState ?? "", r.TargetState ?? "", r.Summary);
                grid.Rows[i].Tag = r;
                grid.Rows[i].Cells[0].Style.ForeColor = Look.For(r.Severity);
                grid.Rows[i].Cells[0].Style.SelectionForeColor = Look.For(r.Severity);
                grid.Rows[i].Cells[0].Style.Font = Look.Bold;
            }
            if (grid.Rows.Count > 0) { grid.ClearSelection(); grid.Rows[0].Selected = true; }
            ShowDetails();
        }

        private CompareRow Selected()
        {
            return grid.SelectedRows.Count == 0 ? null : grid.SelectedRows[0].Tag as CompareRow;
        }

        private void ShowDetails()
        {
            var r = Selected();
            if (r == null) { details.Text = result != null && grid.Rows.Count == 0 ? "Every component matches." : ""; diagnose.Enabled = false; return; }
            var lines = new List<string>
            {
                r.TypeName + ": " + r.Item.Name + (string.IsNullOrEmpty(r.Item.SubType) ? "" : "  [" + r.Item.SubType + "]"),
                (string.IsNullOrEmpty(r.TableText) ? "" : "Table: " + r.TableText + "    ") + "Status: " + r.StatusText,
                ""
            };
            lines.AddRange(r.Lines);
            details.Text = string.Join("\r\n", lines);
            diagnose.Enabled = true;
        }
    }
}

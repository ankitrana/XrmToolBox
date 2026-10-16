using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace DeploymentDoctor
{
    /// <summary>A one-line answer for an evidence section: is there an issue or not, and what it means.</summary>
    internal class EvidenceStatus
    {
        public Severity Severity;
        public string Short;          // shown in the section title
        public string Explanation;    // shown at the top of the section

        public static EvidenceStatus Layers(Diagnosis d)
        {
            Func<string, bool> has = note => d.Layers.Any(l => l.Note == note);
            if (d.Layers.Count == 0)
                return new EvidenceStatus { Severity = Severity.Info, Short = "no layer information", Explanation = "No solution layers were returned for this item." };
            if (has("Unmanaged (Active)"))
                return new EvidenceStatus
                {
                    Severity = Severity.Problem,
                    Short = "an unmanaged layer is on top",
                    Explanation = "An unmanaged layer sits on top, so users see the version that was edited directly in this environment, not what you deployed. " +
                                  "Move the change to Dev if you want to keep it, then remove the unmanaged layer."
                };
            if (has("Above yours"))
                return new EvidenceStatus
                {
                    Severity = Severity.Warning,
                    Short = "other solutions are above yours",
                    Explanation = "Other managed solutions are installed above yours. The top layer wins for anything it changes, so their version is what users get."
                };
            if (has("Staged upgrade (not applied)"))
                return new EvidenceStatus
                {
                    Severity = Severity.Warning,
                    Short = "a staged upgrade is waiting",
                    Explanation = "The import was staged but the upgrade was never applied. In Solutions open your solution and choose Apply Upgrade."
                };
            if (has("Your solution"))
                return new EvidenceStatus
                {
                    Severity = Severity.Ok,
                    Short = "no issue, your solution is the top managed layer",
                    Explanation = "Your solution is the top managed layer and nothing is edited on top of it, so users get what you deployed."
                };
            return new EvidenceStatus { Severity = Severity.Info, Short = "your solution has no layer here", Explanation = "Your solution has no layer on this item in this environment." };
        }

        public static EvidenceStatus Differences(Diagnosis d, int rows)
        {
            if (d.DevContent == null)
                return new EvidenceStatus { Severity = Severity.Info, Short = "Dev not compared", Explanation = "Connect Dev (source) from the toolbar to compare it with Target." };
            if (d.Differences.Count == 0)
                return new EvidenceStatus
                {
                    Severity = Severity.Ok,
                    Short = "no issue, Dev and Target are identical",
                    Explanation = "Dev and Target have the same content, so nothing was lost in the deployment."
                };
            return new EvidenceStatus
            {
                Severity = Severity.Problem,
                Short = rows + " difference" + (rows == 1 ? "" : "s") + " between Dev and Target",
                Explanation = "Dev is where you built the change and Target is where it should appear. These items are not the same:"
            };
        }
    }

    /// <summary>The solution layers as a stack of labelled cards, top layer first.</summary>
    internal class LayersBody : StackItem
    {
        private class Card { public Rectangle Bounds; public Color Color; public string Glyph; }

        private readonly Diagnosis d;
        private readonly EvidenceStatus status;
        private readonly List<TextBlock> blocks = new List<TextBlock>();
        private readonly List<Card> cards = new List<Card>();

        public LayersBody(Diagnosis diagnosis)
        {
            d = diagnosis;
            status = EvidenceStatus.Layers(d);
        }

        private static string LayerTag(LayerRow l, bool belowYours, out Severity sev)
        {
            switch (l.Note)
            {
                case "Unmanaged (Active)": sev = Severity.Problem; return "UNMANAGED · edited directly in this environment";
                case "Your solution": sev = Severity.Ok; return "YOUR SOLUTION";
                case "Above yours": sev = Severity.Warning; return "ABOVE YOURS · its version wins for what it changes";
                case "Staged upgrade (not applied)": sev = Severity.Warning; return "STAGED UPGRADE · imported but not applied yet";
                default:
                    sev = Severity.Info;
                    return belowYours ? "BELOW YOURS · overridden by the layers above" : "OTHER LAYER";
            }
        }

        public override void ApplyWidth(int w)
        {
            Width = w;
            blocks.Clear();
            cards.Clear();
            int y = 0;

            var intro = Block(blocks, status.Explanation, Look.Body, SystemColors.ControlText, 2, y, w - 4);
            y += intro.Bounds.Height + 6;
            var how = Block(blocks, "Layers are stacked like pages: the top one is what users see. Top layer first:", Look.Body, Look.Dim, 2, y, w - 4);
            y += how.Bounds.Height + 10;

            bool belowYours = false;
            foreach (var l in d.Layers)
            {
                Severity sev;
                var tag = LayerTag(l, belowYours, out sev);
                var color = Look.For(sev);
                const int left = 40, right = 14;
                int tw = Math.Max(120, w - left - right);
                int top = y;
                int cy = top + 10;

                var name = Block(blocks, l.Solution, Look.Bold, SystemColors.WindowText, left, cy, tw);
                cy += name.Bounds.Height + 2;
                var label = Block(blocks, tag, Look.Bold, color, left, cy, tw);
                cy += label.Bounds.Height + 3;

                var parts = new List<string>();
                if (!string.IsNullOrEmpty(l.Publisher)) parts.Add("Publisher: " + l.Publisher);
                if (l.Written.HasValue) parts.Add("written " + l.Written.Value.ToString("yyyy-MM-dd HH:mm"));
                if (parts.Count > 0)
                {
                    var det = Block(blocks, string.Join("  ·  ", parts), Look.Body, Look.Dim, left, cy, tw);
                    cy += det.Bounds.Height + 3;
                }
                if (l.Note == "Unmanaged (Active)" && !string.IsNullOrEmpty(l.Changes))
                {
                    var ch = Block(blocks, "Changed: " + TextDiff.Shorten(l.Changes, 300), Look.Body, SystemColors.ControlText, left, cy, tw);
                    cy += ch.Bounds.Height + 3;
                }

                cards.Add(new Card { Bounds = new Rectangle(0, top, w, cy - top + 7), Color = color, Glyph = Look.GlyphFor(sev) });
                y = cy + 7 + 8;
                if (l.Note == "Your solution") belowYours = true;
            }
            Height = Math.Max(y, 24);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            foreach (var c in cards)
            {
                g.FillRectangle(SystemBrushes.Window, c.Bounds);
                using (var p = new Pen(Look.Border)) g.DrawRectangle(p, c.Bounds.X, c.Bounds.Y, c.Bounds.Width - 1, c.Bounds.Height - 1);
                using (var b = new SolidBrush(c.Color)) g.FillRectangle(b, c.Bounds.X, c.Bounds.Y, 4, c.Bounds.Height);
                TextRenderer.DrawText(g, c.Glyph, Look.Glyph, new Point(c.Bounds.X + 12, c.Bounds.Y + 8), c.Color, TextFormatFlags.NoPadding);
            }
            foreach (var t in blocks) t.Draw(g);
        }
    }

    /// <summary>One line of the Dev vs Target table.</summary>
    internal class DiffRow
    {
        public string Result { get; set; }
        public string What { get; set; }
        public string Item { get; set; }
        public string InDev { get; set; }
        public string InTarget { get; set; }

        private static readonly Regex OnlyIn = new Regex(@"^Only in (?<side>Dev|Target): (?<rest>.*)$", RegexOptions.Singleline);
        private static readonly Regex ChangedMulti = new Regex(@"^Changed: (?<key>[^\r\n]*)\r?\n\s+Dev: (?<a>[^\r\n]*)\r?\n\s+Target: (?<b>[^\r\n]*)\s*$");
        private static readonly Regex ChangedLine = new Regex(@"^Changed: (?<key>[^(\r\n]*?)\s*(?:\((?<info>.*)\))?\s*$", RegexOptions.Singleline);
        private static readonly Regex Detail = new Regex(@"^(?<item>.*?)(?:\s{2}\((?<detail>.*)\))?$", RegexOptions.Singleline);
        private static readonly Regex Key = new Regex(@"^(?<type>.*?)\s*'(?<item>.*)'\s*$", RegexOptions.Singleline);
        private static readonly Regex Line = new Regex(@"^line (?<n>\d+): (?<text>.*)$", RegexOptions.Singleline);

        private const string Missing = "Missing in Target";
        private const string Extra = "Only in Target";
        private const string Different = "Different";

        public static List<DiffRow> Parse(IEnumerable<string> lines)
        {
            var rows = new List<DiffRow>();
            foreach (var s in lines)
            {
                var m = OnlyIn.Match(s);
                if (m.Success)
                {
                    var d = Detail.Match(m.Groups["rest"].Value);
                    var item = d.Groups["item"].Value;
                    var detail = d.Groups["detail"].Success ? d.Groups["detail"].Value : "present";
                    var fromDev = m.Groups["side"].Value == "Dev";
                    var row = Split(item);
                    row.Result = fromDev ? Missing : Extra;
                    var ln = Line.Match(item);
                    if (ln.Success) { row.What = "Script line"; row.Item = "line " + ln.Groups["n"].Value; detail = ln.Groups["text"].Value; }
                    row.InDev = fromDev ? detail : "—";
                    row.InTarget = fromDev ? "—" : detail;
                    rows.Add(row);
                    continue;
                }

                var c = ChangedMulti.Match(s);
                if (c.Success)
                {
                    var row = Split(c.Groups["key"].Value);
                    row.Result = Different;
                    row.InDev = c.Groups["a"].Value;
                    row.InTarget = c.Groups["b"].Value;
                    rows.Add(row);
                    continue;
                }

                var one = ChangedLine.Match(s);
                if (one.Success)
                {
                    rows.Add(new DiffRow { Result = Different, What = one.Groups["key"].Value, Item = one.Groups["info"].Value, InDev = "", InTarget = "" });
                    continue;
                }
                rows.Add(new DiffRow { Result = Different, What = s, Item = "", InDev = "", InTarget = "" });
            }
            return rows.OrderBy(r => r.Result == Missing ? 0 : r.Result == Different ? 1 : 2).ToList();
        }

        /// <summary>"Field 'name'" -> What "Field", Item "name"; "Field order in 'x'" -> "Field order", "x".</summary>
        private static DiffRow Split(string key)
        {
            var k = Key.Match(key);
            if (k.Success)
            {
                var type = k.Groups["type"].Value.Trim();
                if (type.EndsWith(" in", StringComparison.Ordinal)) type = type.Substring(0, type.Length - 3);
                return new DiffRow { What = type, Item = k.Groups["item"].Value };
            }
            var colon = key.IndexOf(": ", StringComparison.Ordinal);
            if (colon > 0) return new DiffRow { What = key.Substring(0, colon), Item = key.Substring(colon + 2) };
            return new DiffRow { What = key, Item = "" };
        }
    }

    /// <summary>What is different between Dev and Target, as a table with a clear result per row.</summary>
    internal class DiffBody : StackItem
    {
        private readonly EvidenceStatus status;
        private readonly List<DiffRow> rows;
        private readonly List<TextBlock> blocks = new List<TextBlock>();
        private readonly DataGridView grid;

        public static List<DiffRow> RowsFor(Diagnosis d) { return d.DevContent == null ? new List<DiffRow>() : DiffRow.Parse(d.Differences); }

        public DiffBody(Diagnosis d)
        {
            rows = RowsFor(d);
            status = EvidenceStatus.Differences(d, rows.Count);

            grid = new DataGridView
            {
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BackgroundColor = SystemColors.Window,
                BorderStyle = BorderStyle.FixedSingle,
                AutoGenerateColumns = false,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
                Visible = rows.Count > 0
            };
            grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            grid.DefaultCellStyle.Padding = new Padding(2, 3, 2, 3);
            grid.Columns.Add(Col("Result", "Result", 17));
            grid.Columns.Add(Col("What", "What", 13));
            grid.Columns.Add(Col("Item", "Item", 20));
            grid.Columns.Add(Col("InDev", "In Dev", 25));
            grid.Columns.Add(Col("InTarget", "In Target", 25));
            grid.DataSource = rows;
            grid.DataBindingComplete += (s, e) => grid.ClearSelection();
            grid.CellFormatting += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex != 0 || e.RowIndex >= rows.Count) return;
                var r = rows[e.RowIndex].Result;
                e.CellStyle.ForeColor = r == "Only in Target" ? Look.Warning : Look.Problem;
                e.CellStyle.Font = Look.Bold;
            };
            Controls.Add(grid);
        }

        private static DataGridViewTextBoxColumn Col(string prop, string header, float weight)
        {
            return new DataGridViewTextBoxColumn { DataPropertyName = prop, HeaderText = header, FillWeight = weight, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill };
        }

        public override void ApplyWidth(int w)
        {
            Width = w;
            blocks.Clear();
            var intro = Block(blocks, status.Explanation, Look.Body, SystemColors.ControlText, 2, 0, w - 4);
            int y = intro.Bounds.Height + 8;
            if (rows.Count > 0)
            {
                grid.Location = new Point(0, y);
                grid.Size = new Size(w, Math.Min(360, 36 + rows.Count * 46));
                y += grid.Height;
            }
            Height = y + 2;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            foreach (var t in blocks) t.Draw(e.Graphics);
        }
    }

    /// <summary>
    /// Developer view: the original evidence tabs (layers grid, differences as text, and the raw content of
    /// Target, Dev and your solution's layer).
    /// </summary>
    internal class DeveloperView : StackItem
    {
        private readonly DataGridView layers = NewGrid();
        private readonly TextBox diff = NewText();
        private readonly TextBox target = NewText();
        private readonly TextBox dev = NewText();
        private readonly TextBox layer = NewText();

        public DeveloperView()
        {
            var tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(Page("Solution layers (Target, top first)", layers));
            tabs.TabPages.Add(Page("Differences: Dev vs Target", diff));
            tabs.TabPages.Add(Page("Target content", target));
            tabs.TabPages.Add(Page("Dev content", dev));
            tabs.TabPages.Add(Page("Your solution's layer content", layer));
            Controls.Add(tabs);
            layers.AutoGenerateColumns = true;
            layers.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
            layers.DataBindingComplete += (s, e) => FormatLayers();
        }

        public override void ApplyWidth(int w)
        {
            Width = w;
            Height = 330;
        }

        public void Fill(Diagnosis d)
        {
            layers.DataSource = d.Layers.ToList();
            diff.Text = d.DevContent == null ? "Connect Dev (source) to compare Dev with Target." : ReportBuilder.DifferencesText(d);
            target.Text = d.TargetContent ?? "";
            dev.Text = d.DevContent ?? "(no Dev connection, or not found in Dev)";
            layer.Text = d.LayerContent ?? "(layer content not available)";
        }

        private void FormatLayers()
        {
            foreach (DataGridViewColumn c in layers.Columns)
                if (c.ValueType == typeof(DateTime?)) c.DefaultCellStyle.Format = "yyyy-MM-dd HH:mm";
            foreach (DataGridViewRow r in layers.Rows)
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
    }

    /// <summary>User view: the layers and the Dev vs Target differences in plain words, each with an "issue or no issue" heading.</summary>
    internal class UserEvidence : StackItem
    {
        private readonly LayersBody layers;
        private readonly DiffBody diff;
        private readonly EvidenceStatus layerStatus;
        private readonly EvidenceStatus diffStatus;
        private readonly List<TextBlock> blocks = new List<TextBlock>();

        public UserEvidence(Diagnosis d)
        {
            layers = new LayersBody(d);
            diff = new DiffBody(d);
            layerStatus = EvidenceStatus.Layers(d);
            diffStatus = EvidenceStatus.Differences(d, DiffBody.RowsFor(d).Count);
            Controls.Add(layers);
            Controls.Add(diff);
        }

        public override void ApplyWidth(int w)
        {
            Width = w;
            blocks.Clear();
            int y = 0;

            var h1 = Block(blocks, "Solution layers  \u2014  " + layerStatus.Short, Look.Bold, Look.For(layerStatus.Severity), 0, y, w);
            y += h1.Bounds.Height + 6;
            layers.ApplyWidth(w);
            layers.Location = new Point(0, y);
            y += layers.Height + 18;

            var h2 = Block(blocks, "Dev vs Target  \u2014  " + diffStatus.Short, Look.Bold, Look.For(diffStatus.Severity), 0, y, w);
            y += h2.Bounds.Height + 6;
            diff.ApplyWidth(w);
            diff.Location = new Point(0, y);
            y += diff.Height;

            Height = y + 4;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            foreach (var t in blocks) t.Draw(e.Graphics);
        }
    }

    /// <summary>The evidence area: a switch between the User view and the Developer view, and the chosen view below it.</summary>
    internal class EvidencePanel : StackItem
    {
        private readonly RadioButton userButton;
        private readonly RadioButton devButton;
        private readonly UserEvidence user;
        private readonly DeveloperView developer;
        private readonly List<TextBlock> blocks = new List<TextBlock>();

        public event EventHandler ViewChanged;
        public bool DeveloperMode { get { return devButton.Checked; } }

        public EvidencePanel(Diagnosis d, DeveloperView developerView, bool startInDeveloperView)
        {
            developer = developerView;
            developer.Fill(d);
            user = new UserEvidence(d);

            userButton = Toggle("User view", !startInDeveloperView);
            devButton = Toggle("Developer view", startInDeveloperView);
            Controls.Add(userButton);
            Controls.Add(devButton);
            Controls.Add(user);
            Controls.Add(developer);

            EventHandler changed = (s, e) =>
            {
                if (!((RadioButton)s).Checked) return;
                var h = ViewChanged;
                if (h != null) h(this, EventArgs.Empty);
            };
            userButton.CheckedChanged += changed;
            devButton.CheckedChanged += changed;
        }

        private static RadioButton Toggle(string text, bool on)
        {
            var rb = new RadioButton
            {
                Text = text,
                Appearance = Appearance.Button,
                TextAlign = ContentAlignment.MiddleCenter,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                Size = new Size(116, 28),
                Checked = on,
                Cursor = Cursors.Hand
            };
            rb.FlatAppearance.BorderColor = Look.Border;
            rb.FlatAppearance.CheckedBackColor = Color.FromArgb(222, 236, 249);
            rb.FlatAppearance.MouseOverBackColor = Color.FromArgb(240, 246, 252);
            Action style = () => { rb.ForeColor = rb.Checked ? Look.Accent : SystemColors.ControlText; rb.Font = new Font(SystemFonts.MessageBoxFont, rb.Checked ? FontStyle.Bold : FontStyle.Regular); };
            rb.CheckedChanged += (s, e) => style();
            style();
            return rb;
        }

        public override void ApplyWidth(int w)
        {
            Width = w;
            blocks.Clear();
            bool devMode = devButton.Checked;

            Block(blocks, "Show the evidence as:", Look.Body, Look.Dim, 0, 6, 150);
            userButton.Location = new Point(150, 0);
            devButton.Location = new Point(150 + userButton.Width + 6, 0);

            int y = 36;
            var hint = Block(blocks, devMode
                ? "The raw tables and text: layers grid, differences as text, and the XML, script or flow definition of Target, Dev and your layer."
                : "A plain-words summary: what each layer means, and what is different between Dev and Target.", Look.Body, Look.Dim, 0, y, w);
            y += hint.Bounds.Height + 10;

            user.Visible = !devMode;
            developer.Visible = devMode;
            if (devMode)
            {
                developer.ApplyWidth(w);
                developer.Location = new Point(0, y);
                y += developer.Height;
            }
            else
            {
                user.ApplyWidth(w);
                user.Location = new Point(0, y);
                y += user.Height;
            }
            Height = y + 2;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            foreach (var t in blocks) t.Draw(e.Graphics);
        }
    }
}
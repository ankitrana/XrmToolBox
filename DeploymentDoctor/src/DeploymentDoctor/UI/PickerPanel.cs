using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace DeploymentDoctor
{
    /// <summary>
    /// The left column: three short steps (solution, what changed, which one) and the Diagnose button.
    /// It only holds the controls; the screen that owns it fills the lists and reacts to changes.
    /// </summary>
    internal class PickerPanel : Panel
    {
        public readonly ComboBox Solution = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, DropDownWidth = 520 };
        public readonly CheckBox OnlyInSolution = new CheckBox { Text = "Only items in this solution", AutoSize = true, Checked = true };
        public readonly CheckBox ShowSystemSolutions = new CheckBox { Text = "Also show Microsoft and system solutions", AutoSize = true };
        public readonly System.Windows.Forms.Label GroupLabel = Small("Table");
        public readonly ComboBox Group = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, DropDownWidth = 400 };
        public readonly System.Windows.Forms.Label ComponentLabel = Small("Form");
        // Typing filters the list (web resources and flows can be many)
        public readonly ComboBox Component = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDown,
            DropDownWidth = 520,
            AutoCompleteMode = AutoCompleteMode.SuggestAppend,
            AutoCompleteSource = AutoCompleteSource.ListItems
        };
        public readonly Button Diagnose;
        /// <summary>Shown in "Compare whole solution" mode: what the comparison needs.</summary>
        public readonly System.Windows.Forms.Label CompareHint = new System.Windows.Forms.Label { AutoSize = true, MaximumSize = new Size(270, 0), ForeColor = Look.Dim, Visible = false };

        private System.Windows.Forms.Label step2, step3;
        private System.Windows.Forms.Label singleHint;

        /// <summary>The component type list: one row per type, sorted by category, category shown dimmed at the right.</summary>
        public readonly ComboBox ComponentType = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 22,
            MaxDropDownItems = 14,
            DropDownWidth = 340
        };

        private static readonly string[] CategoryOrder = { "Screens and navigation", "Logic and automation", "Settings and connections", "Code and resources" };
        public event EventHandler KindChanged;

        public PickerPanel()
        {
            Width = 320;
            Dock = DockStyle.Left;
            BackColor = Color.White;
            AutoScroll = true;
            Padding = new Padding(18, 16, 18, 16);

            var rows = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 1,
                Padding = new Padding(0)
            };
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            foreach (var h in Handlers.All.OrderBy(x => CategoryRank(x.Category)))
                ComponentType.Items.Add(h);
            ComponentType.DrawItem += DrawType;
            ComponentType.SelectedIndexChanged += (s, e) => { var h = KindChanged; if (h != null) h(this, EventArgs.Empty); };
            ComponentType.SelectedIndex = 0;
            Diagnose = new Button
            {
                Text = "Diagnose",
                Dock = DockStyle.Fill,
                Height = 40,
                FlatStyle = FlatStyle.Flat,
                BackColor = Look.Accent,
                ForeColor = Color.White,
                Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 11f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false,
                Enabled = false
            };
            Diagnose.FlatAppearance.BorderSize = 0;
            Diagnose.EnabledChanged += (s, e) => Diagnose.BackColor = Diagnose.Enabled ? Look.Accent : Color.FromArgb(160, 175, 195);

            Solution.Dock = DockStyle.Fill;
            Group.Dock = DockStyle.Fill;
            ComponentType.Dock = DockStyle.Fill;
            Component.Dock = DockStyle.Fill;

            Add(rows, Step("1   Solution"), 0, 0);
            Add(rows, Solution, 0, 2);
            Add(rows, Hint("Solutions installed in Target"), 0, 2);
            Add(rows, ShowSystemSolutions, 0, 2);
            Add(rows, CompareHint, 6, 0);
            Add(rows, step2 = Step("2   Component type"), 18, 4);
            Add(rows, ComponentType, 0, 0);
            Add(rows, step3 = Step("3   Component"), 18, 4);
            Add(rows, GroupLabel, 0, 2);
            Add(rows, Group, 0, 6);
            Add(rows, ComponentLabel, 0, 2);
            Add(rows, Component, 0, 4);
            Add(rows, OnlyInSolution, 0, 0);            Add(rows, Diagnose, 22, 0);
            Add(rows, singleHint = Hint("Nothing is changed by Diagnose. It only reads from your environments."), 12, 0);

            Controls.Add(rows);
        }

        /// <summary>"Compare whole solution" needs only the solution: the component steps are hidden.</summary>
        public void SetCompareMode(bool compare)
        {
            foreach (Control c in new Control[] { step2, ComponentType, step3, GroupLabel, Group, ComponentLabel, Component, OnlyInSolution })
                c.Visible = !compare;
            if (!compare) ShowGroups(((ComponentHandler)ComponentType.SelectedItem).HasGroups);
            CompareHint.Visible = compare;
            singleHint.Text = compare ? "Nothing is changed by Compare. It only reads from your environments." : "Nothing is changed by Diagnose. It only reads from your environments.";
            Diagnose.Text = compare ? "Compare with Dev" : "Diagnose";
        }

        public ComponentKind Kind
        {
            get
            {
                var h = ComponentType.SelectedItem as ComponentHandler;
                return h == null ? ComponentKind.Form : h.Kind;
            }
        }

        /// <summary>Hide the table/prefix box for types that have no such grouping (site map).</summary>
        public void ShowGroups(bool visible)
        {
            GroupLabel.Visible = visible;
            Group.Visible = visible;
        }

        private static int CategoryRank(string category)
        {
            var i = Array.IndexOf(CategoryOrder, category);
            return i < 0 ? CategoryOrder.Length : i;
        }

        private void DrawType(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index < 0) return;
            var h = (ComponentHandler)ComponentType.Items[e.Index];
            var selected = (e.State & DrawItemState.Selected) != 0;
            var fore = selected ? SystemColors.HighlightText : SystemColors.ControlText;
            var dim = selected ? SystemColors.HighlightText : Look.Dim;
            var inEdit = (e.State & DrawItemState.ComboBoxEdit) != 0;

            var name = h.DisplayName;
            TextRenderer.DrawText(e.Graphics, name, e.Font, new Point(e.Bounds.Left + 2, e.Bounds.Top + 2), fore);
            var x = e.Bounds.Left + 2 + TextRenderer.MeasureText(e.Graphics, name, e.Font, new Size(int.MaxValue, 100), TextFormatFlags.NoPadding).Width;
            if (h.IsBeta)
                TextRenderer.DrawText(e.Graphics, "  beta", e.Font, new Point(x, e.Bounds.Top + 2), selected ? SystemColors.HighlightText : Look.Accent);
            if (!inEdit)
                TextRenderer.DrawText(e.Graphics, h.Category, e.Font, e.Bounds, dim,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            e.DrawFocusRectangle();
        }
        private static void Add(TableLayoutPanel rows, Control c, int top, int bottom)
        {
            c.Margin = new Padding(0, top, 0, bottom);
            rows.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rows.Controls.Add(c, 0, rows.RowStyles.Count - 1);
        }

        private static System.Windows.Forms.Label Step(string text)
        {
            return new System.Windows.Forms.Label { Text = text, AutoSize = true, Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold), ForeColor = Look.Accent };
        }

        private static System.Windows.Forms.Label Small(string text)
        {
            return new System.Windows.Forms.Label { Text = text, AutoSize = true, ForeColor = Look.Dim };
        }

        private static System.Windows.Forms.Label Hint(string text)
        {
            return new System.Windows.Forms.Label { Text = text, AutoSize = true, MaximumSize = new Size(270, 0), ForeColor = Look.Dim };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var p = new Pen(Look.Border)) e.Graphics.DrawLine(p, Width - 1, 0, Width - 1, Height);
        }
    }
}

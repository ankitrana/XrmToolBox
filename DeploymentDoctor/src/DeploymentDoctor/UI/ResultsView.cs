using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace DeploymentDoctor
{
    /// <summary>One block of the results column. It lays itself out for a given width and reports its height.</summary>
    internal abstract class StackItem : Panel
    {
        protected StackItem()
        {
            DoubleBuffered = true;
            BackColor = Look.Surface;
        }

        public abstract void ApplyWidth(int width);

        /// <summary>Right-click > "Copy text": the cards are drawn, not selectable, so this keeps copying possible.</summary>
        protected void EnableCopy(Func<string> text)
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Copy text", null, (s, e) =>
            {
                try { Clipboard.SetText(text()); } catch (System.Runtime.InteropServices.ExternalException) { }
            });
            ContextMenuStrip = menu;
        }

        protected static TextBlock Block(List<TextBlock> blocks, string text, Font font, Color color, int x, int y, int width)
        {
            var b = new TextBlock { Text = text, Font = font, Color = color, Bounds = new Rectangle(x, y, width, Look.TextHeight(text, font, width)) };
            blocks.Add(b);
            return b;
        }
    }

    /// <summary>The answer: what is most likely wrong, what to do next, and the main fix button.</summary>
    internal class VerdictItem : StackItem
    {
        private readonly Diagnosis d;
        private readonly List<TextBlock> blocks = new List<TextBlock>();
        private readonly Button fixButton;

        public VerdictItem(Diagnosis diagnosis, Action<Finding> onFix)
        {
            d = diagnosis;
            EnableCopy(() => d.VerdictTitle + "\r\n" + d.VerdictDetail + "\r\nNext step: " + d.NextStep);
            var f = d.VerdictFinding;
            if (f != null && f.Action != null && onFix != null)
            {
                fixButton = Look.PrimaryButton(f.Action.Label, 170);
                fixButton.Click += (s, e) => onFix(f);
                Controls.Add(fixButton);
            }
        }

        public override void ApplyWidth(int w)
        {
            Width = w;
            blocks.Clear();
            const int left = 66, right = 18;
            int tw = Math.Max(120, w - left - right);
            int y = 16;

            var title = Block(blocks, d.VerdictTitle, Look.Title, SystemColors.WindowText, left, y, tw);
            y += title.Bounds.Height + 4;
            if (!string.IsNullOrEmpty(d.VerdictDetail))
            {
                var detail = Block(blocks, d.VerdictDetail, Look.Body, SystemColors.WindowText, left, y, tw);
                y += detail.Bounds.Height + 12;
            }
            var nextStep = d.NextStep;
            if (fixButton != null && !string.IsNullOrEmpty(nextStep))
            {
                var cut = nextStep.LastIndexOf(" (or click '", StringComparison.Ordinal);
                if (cut > 0) nextStep = nextStep.Substring(0, cut);
            }
            if (!string.IsNullOrEmpty(nextStep))
            {
                var label = Block(blocks, "NEXT STEP", Look.Bold, Look.For(d.VerdictSeverity), left, y, tw);
                y += label.Bounds.Height + 2;
                var next = Block(blocks, nextStep, Look.Body, SystemColors.WindowText, left, y, tw);
                y += next.Bounds.Height + 12;
            }
            if (d.VerdictFinding != null && !string.IsNullOrEmpty(d.VerdictFinding.Details))
            {
                var dl = Block(blocks, d.VerdictFinding.DetailsTitle.ToUpperInvariant(), Look.Bold, Look.Dim, left, y, tw);
                y += dl.Bounds.Height + 2;
                var dt = Block(blocks, d.VerdictFinding.Details, Look.Body, SystemColors.WindowText, left, y, tw);
                y += dt.Bounds.Height + 12;
            }
            if (fixButton != null)
            {
                fixButton.Location = new Point(left, y);
                y += fixButton.Height + 12;
            }
            Height = y + 4;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            var color = Look.For(d.VerdictSeverity);
            g.FillRectangle(SystemBrushes.Window, ClientRectangle);
            using (var p = new Pen(Look.Border)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            using (var b = new SolidBrush(color)) g.FillRectangle(b, 0, 0, 6, Height);
            TextRenderer.DrawText(g, Look.GlyphFor(d.VerdictSeverity), Look.BigGlyph, new Point(20, 12), color, TextFormatFlags.NoPadding);
            foreach (var t in blocks) t.Draw(g);
        }
    }

    /// <summary>One finding: a card for problems and warnings, a one-line row for passed checks.</summary>
    internal class FindingCard : StackItem
    {
        private readonly Finding finding;
        private readonly bool compact;
        private readonly Button fixButton;
        private readonly List<TextBlock> blocks = new List<TextBlock>();

        public FindingCard(Finding f, Action<Finding> onFix, bool compact)
        {
            finding = f;
            this.compact = compact;
            EnableCopy(() => f.Check + "\r\n" + f.Result + (string.IsNullOrEmpty(f.Details) ? "" : "\r\n\r\n" + f.DetailsTitle + ":\r\n" + f.Details) + (string.IsNullOrEmpty(f.HowToFix) ? "" : "\r\nHow to fix: " + f.HowToFix));
            if (!compact && f.Action != null && onFix != null)
            {
                fixButton = Look.PrimaryButton(f.Action.Label);
                fixButton.Click += (s, e) => onFix(finding);
                Controls.Add(fixButton);
            }
        }

        public override void ApplyWidth(int w)
        {
            Width = w;
            blocks.Clear();

            if (compact)
            {
                // Check name on the left, what was found on the right
                const int checkWidth = 190;
                int resultX = 28 + checkWidth + 8;
                int rw = Math.Max(120, w - resultX - 8);
                var c = Block(blocks, finding.Check, Look.Bold, SystemColors.ControlText, 28, 5, checkWidth);
                var r = Block(blocks, finding.Result, Look.Body, SystemColors.ControlText, resultX, 5, rw);
                int h = Math.Max(c.Bounds.Height, r.Bounds.Height);
                if (finding.Severity == Severity.Info && !string.IsNullOrEmpty(finding.HowToFix))
                {
                    var advice = Block(blocks, finding.HowToFix, Look.Body, Look.Dim, resultX, 5 + h + 2, rw);
                    h += advice.Bounds.Height + 2;
                }
                Height = h + 11;
                Invalidate();
                return;
            }

            const int left = 46, right = 16;
            int tw = Math.Max(120, w - left - right);
            int y = 12;
            var title = Block(blocks, finding.Check, Look.Bold, SystemColors.WindowText, left, y, tw);
            y += title.Bounds.Height + 3;
            var body = Block(blocks, finding.Result, Look.Body, SystemColors.WindowText, left, y, tw);
            y += body.Bounds.Height;

            if (!string.IsNullOrEmpty(finding.Details))
            {
                y += 8;
                var dl = Block(blocks, finding.DetailsTitle.ToUpperInvariant(), Look.Bold, Look.Dim, left, y, tw);
                y += dl.Bounds.Height + 1;
                var dt = Block(blocks, finding.Details, Look.Body, SystemColors.WindowText, left, y, tw);
                y += dt.Bounds.Height;
            }

            var showFix = (finding.Severity == Severity.Problem || finding.Severity == Severity.Warning) && !string.IsNullOrEmpty(finding.HowToFix);
            if (showFix)
            {
                y += 8;
                var label = Block(blocks, "HOW TO FIX", Look.Bold, Look.For(finding.Severity), left, y, tw);
                y += label.Bounds.Height + 1;
                var how = Block(blocks, finding.HowToFix, Look.Body, SystemColors.WindowText, left, y, tw);
                y += how.Bounds.Height;
            }
            if (fixButton != null)
            {
                y += 10;
                fixButton.Location = new Point(left, y);
                y += fixButton.Height;
            }
            Height = y + 12;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            var color = Look.For(finding.Severity);
            if (compact)
            {
                TextRenderer.DrawText(g, Look.GlyphFor(finding.Severity), Look.Body, new Point(6, 5), color, TextFormatFlags.NoPadding);
            }
            else
            {
                g.FillRectangle(SystemBrushes.Window, ClientRectangle);
                using (var p = new Pen(Look.Border)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
                using (var b = new SolidBrush(color)) g.FillRectangle(b, 0, 0, 4, Height);
                TextRenderer.DrawText(g, Look.GlyphFor(finding.Severity), Look.Glyph, new Point(14, 10), color, TextFormatFlags.NoPadding);
            }
            foreach (var t in blocks) t.Draw(g);
        }
    }

    /// <summary>A titled group that can be opened and closed, e.g. "Passed checks (11)".</summary>
    internal class SectionItem : StackItem
    {
        private const int HeaderHeight = 32;
        private readonly string title;
        private readonly Color color;
        private readonly List<StackItem> kids;

        public bool Expanded { get; private set; }
        public void SetExpanded(bool expanded) { Expanded = expanded; }
        public event EventHandler Toggled;

        public SectionItem(string title, Color color, bool expanded, IEnumerable<StackItem> children)
        {
            this.title = title;
            this.color = color;
            Expanded = expanded;
            kids = children.ToList();
            foreach (var k in kids) { Controls.Add(k); k.Visible = expanded; }
        }

        public override void ApplyWidth(int w)
        {
            Width = w;
            int y = HeaderHeight;
            foreach (var k in kids)
            {
                k.Visible = Expanded;
                if (!Expanded) continue;
                k.ApplyWidth(w);
                k.Location = new Point(0, y);
                y += k.Height + 8;
            }
            Height = Expanded ? Math.Max(HeaderHeight, y) : HeaderHeight;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Y > HeaderHeight) return;
            Expanded = !Expanded;
            var h = Toggled;
            if (h != null) h(this, EventArgs.Empty);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor = e.Y <= HeaderHeight ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            TextRenderer.DrawText(g, Expanded ? "▾" : "▸", Look.Glyph, new Point(2, 4), color, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, title, Look.Bold, new Rectangle(24, 8, Math.Max(40, Width - 30), 20), color, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            using (var p = new Pen(Look.Border)) g.DrawLine(p, 0, HeaderHeight - 2, Width, HeaderHeight - 2);
        }
    }

    /// <summary>The first thing seen before a diagnosis: what the tool can do and how to start.</summary>
    internal class EmptyItem : StackItem
    {
        private readonly List<TextBlock> blocks = new List<TextBlock>();

        private static readonly string[][] Checks =
        {
            new[] { "Form", "Dev vs Target changes, corruption check, who can open it, which app shows it" },
            new[] { "View", "Columns, filters and sort order, default view, which apps include it" },
            new[] { "Site map", "Areas, groups and sub areas, corruption check, missing tables, which apps use it" },
            new[] { "Ribbon", "Buttons, commands and the scripts they run, missing scripts, undefined commands" },
            new[] { "Process / flow", "Workflow, business rule, action or cloud flow: steps, turned on or off, connection references" },
            new[] { "Environment variable", "Default value, and whether Target has any value for it" },
            new[] { "Connection reference", "Whether it has a connection in Target, and which connector" },
            new[] { "Plug-in step", "Enabled or disabled, stage, filtering columns, assembly version" },
            new[] { "Plug-in assembly", "Version and the DLL itself" },
            new[] { "Web resource", "Changed lines of a script, page or style, and which forms use it" }
        };

        private static readonly string[] Gives =
        {
            "The most likely cause and what to do next, in plain words.",
            "Solution layers: unmanaged changes in Target (what changed and when it was last changed), other solutions above yours, pending upgrades.",
            "Whether the change was published in Dev and is in the solution you deployed.",
            "Fix buttons for the common causes. You always confirm first and nothing changes without it.",
            "A report you can share: HTML, CSV or text."
        };

        public override void ApplyWidth(int w)
        {
            Width = w;
            blocks.Clear();
            int tw = Math.Max(160, w - 60);
            int y = 28;
            var t = Block(blocks, "Why doesn't my change show in the target environment?", Look.Title, SystemColors.WindowText, 30, y, tw);
            y += t.Bounds.Height + 4;
            var sub = Block(blocks, "Deployment Doctor finds out why something you deployed with a managed solution does not show up, and helps you fix it.", Look.Body, Look.Dim, 30, y, tw);
            y += sub.Bounds.Height + 20;

            y = Heading(y, "WHAT IT CAN CHECK", tw);
            int nameW = 170;
            bool twoColumns = tw > 520;
            foreach (var c in Checks)
            {
                if (twoColumns)
                {
                    var n = Block(blocks, c[0], Look.Bold, SystemColors.ControlText, 30, y, nameW);
                    var d = Block(blocks, c[1], Look.Body, SystemColors.ControlText, 30 + nameW + 10, y, tw - nameW - 10);
                    y += Math.Max(n.Bounds.Height, d.Bounds.Height) + 6;
                }
                else
                {
                    var n = Block(blocks, c[0], Look.Bold, SystemColors.ControlText, 30, y, tw);
                    var d = Block(blocks, c[1], Look.Body, SystemColors.ControlText, 30, y + n.Bounds.Height, tw);
                    y += n.Bounds.Height + d.Bounds.Height + 6;
                }
            }
            y += 12;

            y = Heading(y, "WHAT YOU GET", tw);
            foreach (var line in Gives)
            {
                var b = Block(blocks, "\u2022  " + line, Look.Body, SystemColors.ControlText, 30, y, tw);
                y += b.Bounds.Height + 4;
            }
            y += 14;

            y = Heading(y, "HOW TO START", tw);
            foreach (var line in new[]
            {
                "1.  Pick the solution that contains your change (the list comes from Target).",
                "2.  Choose the component type, then the component.",
                "3.  Click Diagnose."
            })
            {
                var b = Block(blocks, line, Look.Body, SystemColors.ControlText, 30, y, tw);
                y += b.Bounds.Height + 4;
            }
            y += 14;

            y = Heading(y, "OR COMPARE A WHOLE SOLUTION", tw);
            foreach (var line in new[]
            {
                "Click \"Compare whole solution\" in the toolbar to check everything you deployed in one go.",
                "Every component is compared between Dev and Target: red = different or missing, orange = state differs (for example a flow on in Dev but off in Target) or never published in Dev, green = same.",
                "Click any row for the full list of differences, then \"Diagnose this component\" for the cause and fix."
            })
            {
                var b = Block(blocks, "\u2022  " + line, Look.Body, SystemColors.ControlText, 30, y, tw);
                y += b.Bounds.Height + 4;
            }
            y += 14;

            y = Heading(y, "WHAT IT DOES NOT DO (YET)", tw);
            foreach (var line in new[]
            {
                "It does not change anything by itself. Only a Fix button changes something, and only after you confirm.",
                "It does not compare tables, columns, choices, security roles, model-driven apps or dashboards yet (Compare whole solution only counts them).",
                "It cannot tell for sure who made an unmanaged change or when it was first added: Dataverse keeps only the time of the last change.",
                "It compares the published content, not unpublished work in progress (it tells you when Dev has unpublished changes).",
                "It cannot remove the unmanaged layer of a ribbon, environment variable, connection reference or plug-in (it explains the manual way).",
                "Comparing with Dev needs a second connection to Dev (toolbar, Dev menu)."
            })
            {
                var b = Block(blocks, "\u2022  " + line, Look.Body, SystemColors.ControlText, 30, y, tw);
                y += b.Bounds.Height + 4;
            }
            y += 6;
            var tip = Block(blocks, "Tip: connect Dev as a second connection (toolbar, Dev menu) to get the most out of every check.", Look.Body, Look.Dim, 30, y, tw);
            y += tip.Bounds.Height;
            Height = y + 30;
            Invalidate();
        }

        private int Heading(int y, string text, int tw)
        {
            var h = Block(blocks, text, Look.Bold, Look.Accent, 30, y, tw);
            return y + h.Bounds.Height + 6;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            foreach (var t in blocks) t.Draw(e.Graphics);
        }
    }
    /// <summary>
    /// The right-hand side: the verdict first, then problems, warnings and form health as cards;
    /// passed checks and evidence stay closed until you open them.
    /// </summary>
    internal class ResultsView : Panel
    {
        private const int Gutter = 16;
        private readonly List<StackItem> items = new List<StackItem>();
        private readonly DeveloperView developerView = new DeveloperView();
        private bool developerMode;           // User view or Developer view; kept between diagnoses

        public ResultsView()
        {
            AutoScroll = true;
            BackColor = Look.Surface;
            DoubleBuffered = true;
            ShowEmpty();
        }

        public void ShowEmpty()
        {
            Clear();
            Add(new EmptyItem());
            Relayout();
        }

        public void Show(Diagnosis d, Action<Finding> onFix)
        {
            Clear();
            Add(new VerdictItem(d, onFix));

            var vf = d.VerdictFinding;
            Func<Finding, bool> isHealth = f => f.IsHealth;
            var rest = d.Findings.Where(f => f != vf).ToList();
            var health = rest.Where(isHealth).ToList();
            var others = rest.Where(f => !isHealth(f)).ToList();
            var problems = others.Where(f => f.Severity == Severity.Problem).ToList();
            var warnings = others.Where(f => f.Severity == Severity.Warning).ToList();
            var passed = others.Where(f => f.Severity == Severity.Ok || f.Severity == Severity.Info).ToList();

            Func<IEnumerable<Finding>, IEnumerable<StackItem>> cards = fs => fs.Select(f => (StackItem)new FindingCard(f, onFix, false)).ToList();

            if (health.Count == 1)
                Add(new FindingCard(health[0], onFix, false));
            else if (health.Count > 1)
                Add(new SectionItem("Health details", health.Any(f => f.Severity == Severity.Problem) ? Look.Problem : Look.Info, true, cards(health)));
            if (problems.Count > 0)
                Add(new SectionItem((vf != null ? "Other problems" : "Problems") + " (" + problems.Count + ")", Look.Problem, true, cards(problems)));
            if (warnings.Count > 0)
                Add(new SectionItem("Warnings (" + warnings.Count + ")", Look.Warning, true, cards(warnings)));
            if (passed.Count > 0)
                Add(new SectionItem("Passed checks and notes (" + passed.Count + ")", Look.Ok, false,
                    passed.Select(f => (StackItem)new FindingCard(f, onFix, true)).ToList()));

            // Evidence: a User view (plain words, "issue or no issue" per part) and a Developer view (the raw tabs)
            var layers = EvidenceStatus.Layers(d);
            var diffs = EvidenceStatus.Differences(d, DiffBody.RowsFor(d).Count);
            var flagged = new[] { layers, diffs }.Where(s => s.Severity == Severity.Problem || s.Severity == Severity.Warning).ToList();
            var worst = new[] { layers.Severity, diffs.Severity }.Min();
            var title = flagged.Count > 0
                ? "Evidence  \u2014  " + string.Join("; ", flagged.Select(s => s.Short))
                : worst == Severity.Ok ? "Evidence  \u2014  no issue in the solution layers or in Dev vs Target" : "Evidence  \u2014  solution layers and Dev vs Target";
            var evidence = new EvidencePanel(d, developerView, developerMode);
            evidence.ViewChanged += (s, e) => { developerMode = evidence.DeveloperMode; Relayout(); };
            Add(new SectionItem(title, Look.For(worst), flagged.Count > 0, new StackItem[] { evidence }));
            AutoScrollPosition = new Point(0, 0);
            Relayout();
        }

        /// <summary>Open or close every section (passed checks, evidence...).</summary>
        public void SetAllExpanded(bool expanded)
        {
            foreach (var s in items.OfType<SectionItem>()) s.SetExpanded(expanded);
            Relayout();
        }

        private void Add(StackItem item)
        {
            items.Add(item);
            Controls.Add(item);
            var section = item as SectionItem;
            if (section != null) section.Toggled += (s, e) => Relayout();
        }

        private void Clear()
        {
            // The developer-view tabs are reused: take them out before the old sections are thrown away
            if (developerView.Parent != null) developerView.Parent.Controls.Remove(developerView);
            foreach (var item in items) { Controls.Remove(item); item.Dispose(); }
            items.Clear();
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            Relayout();
        }

        private void Relayout()
        {
            if (items.Count == 0) return;
            SuspendLayout();

            // Always leave room for the vertical scrollbar, so the content never needs a horizontal one
            int w = Math.Max(320, Width - Gutter * 2 - SystemInformation.VerticalScrollBarWidth - 2);
            int total = Stack(w);
            AutoScrollMinSize = new Size(0, total);

            var offset = AutoScrollPosition;
            int y = Gutter;
            foreach (var it in items)
            {
                it.Location = new Point(Gutter + offset.X, y + offset.Y);
                y += it.Height + 12;
            }
            ResumeLayout();
            Invalidate(true);
        }

        private int Stack(int width)
        {
            int y = Gutter;
            foreach (var it in items) { it.ApplyWidth(width); y += it.Height + 12; }
            return y + 4;
        }
    }
}

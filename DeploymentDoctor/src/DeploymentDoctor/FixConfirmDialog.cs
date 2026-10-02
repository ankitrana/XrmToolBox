using System.Drawing;
using System.Windows.Forms;

namespace DeploymentDoctor
{
    /// <summary>
    /// Shown before any Fix runs: where, as whom, what changes, whether it can be undone, advice,
    /// and how to do it by hand instead. "Make the change" stays disabled until the box is ticked.
    /// </summary>
    internal class FixConfirmDialog : Form
    {
        private const int TextWidth = 600;

        public FixConfirmDialog(FixAction action, string environment, string connectionName, string url, CallerInfo caller)
        {
            Text = "Before you continue: " + action.Label;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(14);

            var layout = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };

            layout.Controls.Add(new System.Windows.Forms.Label
            {
                Text = "This will change the " + environment + " environment",
                AutoSize = true,
                Font = new Font(Font.FontFamily, 11f, FontStyle.Bold),
                ForeColor = environment == "TARGET" ? Color.DarkRed : Color.DarkGreen,
                Margin = new Padding(0, 0, 0, 8)
            });

            Section(layout, "Environment", connectionName + (string.IsNullOrEmpty(url) ? "" : "\r\n" + url));
            Section(layout, "Changes are made as", caller == null
                ? "(could not read the user of this connection)"
                : caller + "\r\nThis account is shown as 'Modified by' and in the audit log. Dataverse refuses the change if it lacks the permission.",
                caller != null && caller.IsApplicationUser ? Color.DarkOrange : SystemColors.ControlText);
            Section(layout, "What it will do", action.WhatItDoes);
            Section(layout, "Can it be undone?", action.Undo ?? "-",
                action.Undo != null && action.Undo.StartsWith("No") ? Color.DarkRed : SystemColors.ControlText);
            if (!string.IsNullOrEmpty(action.Advice)) Section(layout, "Before you do this", action.Advice);
            if (!string.IsNullOrEmpty(action.ManualSteps)) Section(layout, "Prefer to do it by hand?", action.ManualSteps);

            var understand = new CheckBox
            {
                Text = "I have read this and want to make this change in " + environment + (caller == null ? "" : " as " + (caller.FullName ?? "this user")),
                AutoSize = true,
                MaximumSize = new Size(TextWidth, 0),
                Margin = new Padding(0, 6, 0, 6)
            };
            layout.Controls.Add(understand);

            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Width = TextWidth, Margin = new Padding(0, 6, 0, 0) };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            var run = new Button { Text = "Make the change", DialogResult = DialogResult.OK, AutoSize = true, Enabled = false };
            var copy = new Button { Text = "Copy manual steps", AutoSize = true, Enabled = !string.IsNullOrEmpty(action.ManualSteps) };
            copy.Click += (s, e) =>
            {
                Clipboard.SetText(action.Label + "\r\n" + connectionName + "\r\n\r\n" + action.ManualSteps);
                copy.Text = "Copied";
            };
            understand.CheckedChanged += (s, e) => run.Enabled = understand.Checked;
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(run);
            buttons.Controls.Add(copy);
            layout.Controls.Add(buttons);

            // Enter / Esc both cancel: making a change always needs the tick and a click
            AcceptButton = cancel;
            CancelButton = cancel;
            Controls.Add(layout);
        }

        private static void Section(FlowLayoutPanel layout, string title, string body, Color? color = null)
        {
            layout.Controls.Add(new System.Windows.Forms.Label
            {
                Text = title,
                AutoSize = true,
                Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
                Margin = new Padding(0, 4, 0, 0)
            });
            layout.Controls.Add(new System.Windows.Forms.Label
            {
                Text = body,
                AutoSize = true,
                MaximumSize = new Size(TextWidth, 0),
                ForeColor = color ?? SystemColors.ControlText,
                Margin = new Padding(12, 2, 0, 6)
            });
        }
    }
}

using System.Drawing;
using System.Windows.Forms;

namespace DeploymentDoctor
{
    /// <summary>
    /// Asked before saving an HTML report: what may go into a file that will be shared.
    /// Both options are off by default; the diagnosis, differences and fix steps are always included.
    /// </summary>
    internal class ExportOptionsDialog : Form
    {
        private readonly CheckBox chkXml;
        private readonly CheckBox chkUrls;

        public bool IncludeXml { get { return chkXml.Checked; } }
        public bool IncludeUrls { get { return chkUrls.Checked; } }

        public ExportOptionsDialog(bool includeXml, bool includeUrls)
        {
            Text = "Export report - what to include";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(12);

            var layout = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                WrapContents = false,
                Dock = DockStyle.Fill
            };

            layout.Controls.Add(new System.Windows.Forms.Label
            {
                AutoSize = true,
                MaximumSize = new Size(440, 0),
                Text = "Always included: the verdict, next steps, all checks, solution layers and the list of differences.\r\n" +
                       "The report never contains record data or credentials.",
                Margin = new Padding(0, 0, 0, 10)
            });

            chkXml = new CheckBox { Text = "Include full content (form/view XML, web resource file, flow or workflow definition)", AutoSize = true, Checked = includeXml };
            layout.Controls.Add(chkXml);
            layout.Controls.Add(Hint("Can contain iframe URLs, parameters, script code, flow inputs or keys. Leave off when sharing outside your team."));

            chkUrls = new CheckBox { Text = "Include environment URLs", AutoSize = true, Checked = includeUrls };
            layout.Controls.Add(chkUrls);
            layout.Controls.Add(Hint("Connection names are always shown. URLs reveal the organization address."));

            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(0, 10, 0, 0) };
            var ok = new Button { Text = "Export", DialogResult = DialogResult.OK, AutoSize = true };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            layout.Controls.Add(buttons);

            AcceptButton = ok;
            CancelButton = cancel;
            Controls.Add(layout);
        }

        private static System.Windows.Forms.Label Hint(string text)
        {
            return new System.Windows.Forms.Label
            {
                Text = text,
                AutoSize = true,
                MaximumSize = new Size(440, 0),
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(20, 0, 0, 10)
            };
        }
    }
}

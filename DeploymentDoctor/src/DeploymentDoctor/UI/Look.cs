using System;
using System.Drawing;
using System.Windows.Forms;

namespace DeploymentDoctor
{
    /// <summary>Colours, fonts and text helpers shared by the screen.</summary>
    internal static class Look
    {
        public static readonly Color Problem = Color.FromArgb(198, 40, 40);
        public static readonly Color Warning = Color.FromArgb(191, 120, 0);
        public static readonly Color Ok = Color.FromArgb(46, 125, 50);
        public static readonly Color Info = Color.FromArgb(96, 110, 125);
        public static readonly Color Accent = Color.FromArgb(0, 103, 192);
        public static readonly Color Surface = Color.FromArgb(243, 245, 247);
        public static readonly Color Border = Color.FromArgb(218, 222, 228);
        public static readonly Color Dim = Color.FromArgb(105, 115, 128);

        private static Font body, bold, title, glyph, bigGlyph;
        public static Font Body { get { return body ?? (body = SystemFonts.MessageBoxFont); } }
        public static Font Bold { get { return bold ?? (bold = new Font(Body, FontStyle.Bold)); } }
        public static Font Title { get { return title ?? (title = new Font(Body.FontFamily, 14f, FontStyle.Bold)); } }
        public static Font Glyph { get { return glyph ?? (glyph = new Font("Segoe UI Symbol", 13f)); } }
        public static Font BigGlyph { get { return bigGlyph ?? (bigGlyph = new Font("Segoe UI Symbol", 22f)); } }

        public const TextFormatFlags Wrap =
            TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl | TextFormatFlags.Left | TextFormatFlags.Top;

        public static Color For(Severity s)
        {
            switch (s)
            {
                case Severity.Problem: return Problem;
                case Severity.Warning: return Warning;
                case Severity.Ok: return Ok;
                default: return Info;
            }
        }

        public static string GlyphFor(Severity s)
        {
            switch (s)
            {
                case Severity.Problem: return "✖";   // heavy x
                case Severity.Warning: return "⚠";   // warning sign
                case Severity.Ok: return "✔";        // check mark
                default: return "ℹ";                 // information
            }
        }

        public static int TextHeight(string text, Font font, int width)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            return TextRenderer.MeasureText(text, font, new Size(Math.Max(20, width), 0), Wrap).Height;
        }

        /// <summary>A flat, filled button for the one main action of a card or panel.</summary>
        public static Button PrimaryButton(string text, int minWidth = 150)
        {
            var b = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = Accent,
                ForeColor = Color.White,
                Font = Bold,
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };
            b.FlatAppearance.BorderSize = 0;
            var w = TextRenderer.MeasureText(text, Bold).Width + 36;
            b.Size = new Size(Math.Max(minWidth, w), 32);
            return b;
        }
    }

    /// <summary>A piece of text with its font, colour and place, drawn by the card that owns it.</summary>
    internal class TextBlock
    {
        public string Text;
        public Font Font;
        public Color Color;
        public Rectangle Bounds;

        public void Draw(Graphics g)
        {
            TextRenderer.DrawText(g, Text, Font, Bounds, Color, Look.Wrap);
        }
    }
}

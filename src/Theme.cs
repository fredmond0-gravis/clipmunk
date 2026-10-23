using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Clipmunk
{
    /// <summary>Dark, minimal palette shared by every window.</summary>
    internal static class Theme
    {
        public static readonly Color Window = Color.FromArgb(24, 25, 28);
        public static readonly Color Chrome = Color.FromArgb(32, 33, 37);
        public static readonly Color Canvas = Color.FromArgb(16, 17, 19);
        public static readonly Color Card = Color.FromArgb(38, 40, 45);
        public static readonly Color Hover = Color.FromArgb(48, 50, 57);
        public static readonly Color Pressed = Color.FromArgb(60, 63, 72);
        public static readonly Color Border = Color.FromArgb(52, 54, 60);
        public static readonly Color Text = Color.FromArgb(232, 233, 236);
        public static readonly Color SubText = Color.FromArgb(150, 154, 164);
        public static readonly Color Ink = Color.FromArgb(226, 228, 233);
        public static readonly Color Accent = Color.FromArgb(255, 138, 0);
        public static readonly Color AccentSoft = Color.FromArgb(70, 255, 138, 0);

        private static float scale = -1f;

        /// <summary>System DPI scale (1.0 = 96 dpi).</summary>
        public static float Scale
        {
            get
            {
                if (scale < 0)
                {
                    try
                    {
                        using (var g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;
                    }
                    catch (Exception)
                    {
                        scale = 1f;
                    }
                }
                return scale;
            }
        }

        public static int S(float v) { return (int)Math.Round(v * Scale); }

        public static Font UiFont(float pt) { return new Font("Segoe UI", pt, FontStyle.Regular); }
        public static Font UiFont(float pt, FontStyle style) { return new Font("Segoe UI", pt, style); }

        public static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRounded(Graphics g, Color c, RectangleF r, float radius)
        {
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = RoundedRect(r, radius))
            using (var b = new SolidBrush(c))
                g.FillPath(b, path);
            g.SmoothingMode = old;
        }

        public static void Apply(ToolStrip strip)
        {
            strip.Renderer = new DarkRenderer();
            strip.BackColor = Chrome;
            strip.ForeColor = Text;
            strip.GripStyle = ToolStripGripStyle.Hidden;
        }

        public static void Apply(ToolStripDropDown menu)
        {
            menu.Renderer = new DarkRenderer();
            menu.BackColor = Card;
            menu.ForeColor = Text;
        }
    }

    internal sealed class DarkColors : ProfessionalColorTable
    {
        public override Color ToolStripGradientBegin { get { return Theme.Chrome; } }
        public override Color ToolStripGradientMiddle { get { return Theme.Chrome; } }
        public override Color ToolStripGradientEnd { get { return Theme.Chrome; } }
        public override Color ToolStripBorder { get { return Theme.Chrome; } }
        public override Color ToolStripDropDownBackground { get { return Theme.Card; } }
        public override Color MenuBorder { get { return Theme.Border; } }
        public override Color MenuItemBorder { get { return Theme.Hover; } }
        public override Color MenuItemSelected { get { return Theme.Hover; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.Hover; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.Hover; } }
        public override Color MenuItemPressedGradientBegin { get { return Theme.Card; } }
        public override Color MenuItemPressedGradientEnd { get { return Theme.Card; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Card; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Card; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Card; } }
        public override Color SeparatorDark { get { return Theme.Border; } }
        public override Color SeparatorLight { get { return Theme.Border; } }
        public override Color StatusStripGradientBegin { get { return Theme.Chrome; } }
        public override Color StatusStripGradientEnd { get { return Theme.Chrome; } }
        public override Color CheckBackground { get { return Theme.Pressed; } }
        public override Color CheckSelectedBackground { get { return Theme.Pressed; } }
        public override Color CheckPressedBackground { get { return Theme.Pressed; } }
    }

    /// <summary>Flat dark renderer with rounded hover/checked pills.</summary>
    internal sealed class DarkRenderer : ToolStripProfessionalRenderer
    {
        public DarkRenderer() : base(new DarkColors())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            if (e.ToolStrip is ToolStripDropDown)
            {
                using (var p = new Pen(Theme.Border))
                    e.Graphics.DrawRectangle(p, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
            }
        }

        private static void Pill(Graphics g, ToolStripItem item, bool isChecked)
        {
            Color c;
            if (item.Pressed) c = Theme.Pressed;
            else if (isChecked) c = Theme.Pressed;
            else if (item.Selected && item.Enabled) c = Theme.Hover;
            else return;
            var r = new RectangleF(1, 1, item.Width - 2, item.Height - 2);
            Theme.FillRounded(g, c, r, Theme.S(6));
            if (isChecked)
            {
                using (var p = new Pen(Theme.AccentSoft, 1f))
                using (var path = Theme.RoundedRect(new RectangleF(1.5f, 1.5f, item.Width - 3, item.Height - 3), Theme.S(6)))
                {
                    var old = g.SmoothingMode;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.DrawPath(p, path);
                    g.SmoothingMode = old;
                }
            }
        }

        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
        {
            var b = e.Item as ToolStripButton;
            if (b != null && b.Tag as string == "accent")
            {
                Color c = b.Pressed ? Color.FromArgb(220, 118, 0) : b.Selected ? Color.FromArgb(255, 155, 40) : Theme.Accent;
                Theme.FillRounded(e.Graphics, c, new RectangleF(2, 3, b.Width - 4, b.Height - 6), Theme.S(6));
                return;
            }
            Pill(e.Graphics, e.Item, b != null && b.Checked);
        }

        protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e)
        {
            Pill(e.Graphics, e.Item, false);
        }

        protected override void OnRenderSplitButtonBackground(ToolStripItemRenderEventArgs e)
        {
            Pill(e.Graphics, e.Item, false);
            var sb = (ToolStripSplitButton)e.Item;
            DrawArrow(new ToolStripArrowRenderEventArgs(e.Graphics, sb, sb.DropDownButtonBounds, Theme.SubText, ArrowDirection.Down));
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.ToolStrip is ToolStripDropDown)
            {
                if (e.Item.Selected && e.Item.Enabled)
                    Theme.FillRounded(e.Graphics, Theme.Hover, new RectangleF(4, 1, e.Item.Width - 8, e.Item.Height - 2), Theme.S(5));
                return;
            }
            base.OnRenderMenuItemBackground(e);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            var b = e.Item as ToolStripButton;
            if (b != null && b.Tag as string == "accent") e.TextColor = Color.FromArgb(25, 18, 8);
            else e.TextColor = e.Item.Enabled ? (e.Item.ForeColor == SystemColors.ControlText ? Theme.Text : e.Item.ForeColor) : Color.FromArgb(95, 98, 106);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.SubText;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using (var p = new Pen(Theme.Border))
            {
                if (e.Vertical)
                {
                    int x = e.Item.Width / 2;
                    e.Graphics.DrawLine(p, x, 6, x, e.Item.Height - 6);
                }
                else
                {
                    int y = e.Item.Height / 2;
                    e.Graphics.DrawLine(p, 8, y, e.Item.Width - 8, y);
                }
            }
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var r = e.ImageRectangle;
            r.Inflate(2, 2);
            Theme.FillRounded(e.Graphics, Theme.Pressed, r, 4);
            using (var p = new Pen(Theme.Accent, 2f))
            {
                var old = e.Graphics.SmoothingMode;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.DrawLines(p, new[]
                {
                    new PointF(r.X + r.Width * 0.25f, r.Y + r.Height * 0.52f),
                    new PointF(r.X + r.Width * 0.43f, r.Y + r.Height * 0.70f),
                    new PointF(r.X + r.Width * 0.76f, r.Y + r.Height * 0.32f)
                });
                e.Graphics.SmoothingMode = old;
            }
        }

        protected override void OnRenderStatusStripSizingGrip(ToolStripRenderEventArgs e)
        {
        }
    }
}

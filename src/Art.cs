using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace Clipmunk
{
    /// <summary>All artwork is drawn in code so it stays crisp at every DPI.</summary>
    internal static class Art
    {
        // ---------------------------------------------------------------- app icon

        public static Icon AppIcon()
        {
            using (var s = typeof(Art).Assembly.GetManifestResourceStream("Clipmunk.ico"))
            {
                if (s != null) return new Icon(s);
            }
            using (var b = DrawAppIcon(32)) return Icon.FromHandle(b.GetHicon());
        }

        public static Icon AppIcon(int size)
        {
            using (var s = typeof(Art).Assembly.GetManifestResourceStream("Clipmunk.ico"))
            {
                if (s != null) return new Icon(s, size, size);
            }
            using (var b = DrawAppIcon(size)) return Icon.FromHandle(b.GetHicon());
        }

        public static Bitmap DrawAppIcon(int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.ScaleTransform(size / 64f, size / 64f);
                DrawChipmunk(g, size >= 24);
            }
            return bmp;
        }

        /// <summary>Chipmunk face on a 64x64 grid.</summary>
        public static void DrawChipmunk(Graphics g, bool detail)
        {
            var fur = Color.FromArgb(214, 128, 58);
            var dark = Color.FromArgb(86, 46, 20);
            var cream = Color.FromArgb(252, 233, 204);

            using (var b = new SolidBrush(dark))
            {
                g.FillEllipse(b, 4, 4, 20, 20);
                g.FillEllipse(b, 40, 4, 20, 20);
            }
            using (var b = new SolidBrush(Color.FromArgb(240, 166, 128)))
            {
                g.FillEllipse(b, 9, 9, 10, 10);
                g.FillEllipse(b, 45, 9, 10, 10);
            }
            using (var b = new SolidBrush(fur)) g.FillEllipse(b, 5, 11, 54, 50);
            using (var p = new Pen(dark, 2.5f)) g.DrawEllipse(p, 5, 11, 54, 50);
            using (var b = new SolidBrush(dark))
            using (var path = Theme.RoundedRect(new RectangleF(29, 12, 6, 16), 3))
                g.FillPath(b, path);
            using (var b = new SolidBrush(cream))
            {
                g.FillEllipse(b, 9, 33, 24, 24);
                g.FillEllipse(b, 31, 33, 24, 24);
            }
            using (var b = new SolidBrush(Color.FromArgb(28, 20, 16)))
            {
                g.FillEllipse(b, 16, 24, 9, 10.5f);
                g.FillEllipse(b, 39, 24, 9, 10.5f);
            }
            using (var b = new SolidBrush(Color.White))
            {
                g.FillEllipse(b, 19.2f, 25.8f, 3.4f, 3.4f);
                g.FillEllipse(b, 42.2f, 25.8f, 3.4f, 3.4f);
            }
            using (var b = new SolidBrush(Color.FromArgb(58, 30, 16))) g.FillEllipse(b, 27.5f, 36, 9, 6.5f);
            if (detail)
            {
                using (var b = new SolidBrush(Color.White))
                using (var p = new Pen(dark, 1.3f))
                {
                    var r = new RectangleF(28.5f, 45, 7, 7.5f);
                    g.FillRectangle(b, r);
                    g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
                    g.DrawLine(p, 32, 45, 32, 52.5f);
                }
            }
        }

        public static void WriteIco(string path)
        {
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 256 };
            var images = new List<byte[]>();
            foreach (int s in sizes)
            {
                using (var b = DrawAppIcon(s)) images.Add(s >= 256 ? PngBytes(b) : DibBytes(b));
            }
            using (var fs = File.Create(path))
            using (var w = new BinaryWriter(fs))
            {
                w.Write((short)0);
                w.Write((short)1);
                w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    int s = sizes[i];
                    w.Write((byte)(s >= 256 ? 0 : s));
                    w.Write((byte)(s >= 256 ? 0 : s));
                    w.Write((byte)0);
                    w.Write((byte)0);
                    w.Write((short)1);
                    w.Write((short)32);
                    w.Write(images[i].Length);
                    w.Write(offset);
                    offset += images[i].Length;
                }
                foreach (var data in images) w.Write(data);
            }
        }

        private static byte[] PngBytes(Bitmap b)
        {
            using (var ms = new MemoryStream())
            {
                b.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }

        private static byte[] DibBytes(Bitmap b)
        {
            int w = b.Width, h = b.Height;
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(40); bw.Write(w); bw.Write(h * 2);
                bw.Write((short)1); bw.Write((short)32);
                bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
                for (int y = h - 1; y >= 0; y--)
                {
                    for (int x = 0; x < w; x++)
                    {
                        Color c = b.GetPixel(x, y);
                        bw.Write(c.B); bw.Write(c.G); bw.Write(c.R); bw.Write(c.A);
                    }
                }
                bw.Write(new byte[((w + 31) / 32) * 4 * h]);
                bw.Flush();
                return ms.ToArray();
            }
        }

        // ---------------------------------------------------------------- toolbar glyphs

        public static Bitmap Glyph(string name, int size)
        {
            return Glyph(name, size, Theme.Ink);
        }

        public static Bitmap Glyph(string name, int size, Color ink)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.ScaleTransform(size / 20f, size / 20f);
                DrawGlyph(g, name, ink);
            }
            return bmp;
        }

        private static PointF P(float x, float y) { return new PointF(x, y); }

        public static void ArrowHead(Graphics g, Brush b, PointF from, PointF tip, float len, float half)
        {
            float dx = tip.X - from.X, dy = tip.Y - from.Y;
            float d = (float)Math.Sqrt(dx * dx + dy * dy);
            if (d < 0.001f) return;
            float ux = dx / d, uy = dy / d;
            var bc = new PointF(tip.X - ux * len, tip.Y - uy * len);
            g.FillPolygon(b, new[] { tip, new PointF(bc.X - uy * half, bc.Y + ux * half), new PointF(bc.X + uy * half, bc.Y - ux * half) });
        }

        private static void DrawGlyph(Graphics g, string name, Color inkColor)
        {
            using (var ink = new Pen(inkColor, 1.6f))
            using (var ink2 = new Pen(inkColor, 2f))
            using (var inkBrush = new SolidBrush(inkColor))
            {
                ink.StartCap = ink.EndCap = LineCap.Round;
                ink.LineJoin = LineJoin.Round;
                ink2.StartCap = ink2.EndCap = LineCap.Round;
                ink2.LineJoin = LineJoin.Round;
                switch (name)
                {
                    case "select":
                    {
                        var pts = new[] { P(5, 2), P(5, 16.5f), P(8.6f, 13.2f), P(11, 18.4f), P(13.6f, 17.2f), P(11.3f, 12.2f), P(15.8f, 12.2f) };
                        g.FillPolygon(inkBrush, pts);
                        using (var p = new Pen(inkColor, 1.2f) { LineJoin = LineJoin.Round }) g.DrawPolygon(p, pts);
                        break;
                    }
                    case "pen":
                    {
                        var st = g.Save();
                        g.TranslateTransform(10, 10);
                        g.RotateTransform(-45);
                        using (var p = new Pen(inkColor, 1.5f) { LineJoin = LineJoin.Round })
                        {
                            g.DrawRectangle(p, -4.5f, -2.5f, 13.5f, 5f);
                            g.DrawLine(p, 6, -2.5f, 6, 2.5f);
                            g.DrawPolygon(p, new[] { P(-4.5f, -2.5f), P(-9f, 0), P(-4.5f, 2.5f) });
                        }
                        g.FillPolygon(inkBrush, new[] { P(-7.3f, -0.95f), P(-9f, 0), P(-7.3f, 0.95f) });
                        g.Restore(st);
                        break;
                    }
                    case "highlighter":
                    {
                        using (var b = new SolidBrush(Color.FromArgb(230, 255, 214, 10))) g.FillRectangle(b, 2, 15.5f, 16, 3);
                        var st = g.Save();
                        g.TranslateTransform(11.5f, 7.5f);
                        g.RotateTransform(-45);
                        using (var p = new Pen(inkColor, 1.5f) { LineJoin = LineJoin.Round })
                        {
                            g.DrawRectangle(p, -3f, -3f, 10, 6f);
                            g.DrawPolygon(p, new[] { P(-3, -3f), P(-6.5f, -1.5f), P(-6.5f, 1.5f), P(-3, 3f) });
                        }
                        g.Restore(st);
                        break;
                    }
                    case "arrow":
                        g.DrawLine(ink2, 4, 16, 12.5f, 7.5f);
                        ArrowHead(g, inkBrush, P(4, 16), P(16.8f, 3.2f), 6.5f, 3.8f);
                        break;
                    case "line":
                        g.DrawLine(ink2, 4, 16, 16, 4);
                        break;
                    case "rect":
                        using (var path = Theme.RoundedRect(new RectangleF(2.8f, 4.8f, 14.4f, 10.4f), 1.5f)) g.DrawPath(ink, path);
                        break;
                    case "ellipse":
                        g.DrawEllipse(ink, 2.5f, 4.5f, 15, 11);
                        break;
                    case "text":
                        using (var path = CenteredText("T", 17, P(10, 10.2f), true)) g.FillPath(inkBrush, path);
                        break;
                    case "step":
                        using (var b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, 2, 2, 16, 16);
                        using (var path = CenteredText("1", 10.5f, P(10, 10), true)) g.FillPath(Brushes.White, path);
                        break;
                    case "sticker":
                        Stickers.Draw(g, StickerKind.Smile, new RectangleF(1.5f, 1.5f, 17, 17));
                        break;
                    case "blur":
                    {
                        int[] shades = { 150, 90, 190, 110, 225, 135, 185, 80, 160 };
                        float cell = 16f / 3f;
                        for (int i = 0; i < 9; i++)
                        {
                            int v = shades[i];
                            using (var b = new SolidBrush(Color.FromArgb(v, v, v + 6)))
                                g.FillRectangle(b, 2 + (i % 3) * cell, 2 + (i / 3) * cell, cell + 0.3f, cell + 0.3f);
                        }
                        break;
                    }
                    case "crop":
                        g.DrawLines(ink, new[] { P(5, 1.5f), P(5, 15), P(18.5f, 15) });
                        g.DrawLines(ink, new[] { P(1.5f, 5), P(15, 5), P(15, 18.5f) });
                        break;
                    case "capture":
                    {
                        float a = 2, b = 18, l = 5;
                        g.DrawLines(ink, new[] { P(a, a + l), P(a, a), P(a + l, a) });
                        g.DrawLines(ink, new[] { P(b - l, a), P(b, a), P(b, a + l) });
                        g.DrawLines(ink, new[] { P(b, b - l), P(b, b), P(b - l, b) });
                        g.DrawLines(ink, new[] { P(a + l, b), P(a, b), P(a, b - l) });
                        using (var p = new Pen(Theme.Accent, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        {
                            g.DrawLine(p, 10, 6.5f, 10, 13.5f);
                            g.DrawLine(p, 6.5f, 10, 13.5f, 10);
                        }
                        break;
                    }
                    case "undo":
                    case "redo":
                    {
                        var st = g.Save();
                        if (name == "redo")
                        {
                            g.TranslateTransform(20, 0);
                            g.ScaleTransform(-1, 1);
                        }
                        g.DrawBezier(ink2, P(15.5f, 16), P(16.5f, 8), P(12, 5.5f), P(7, 6.5f));
                        ArrowHead(g, inkBrush, P(12, 5.5f), P(3.4f, 7.3f), 5.8f, 3.8f);
                        g.Restore(st);
                        break;
                    }
                    case "copy":
                        using (var p = new Pen(inkColor, 1.5f))
                        {
                            using (var path = Theme.RoundedRect(new RectangleF(7, 2, 10, 12.5f), 2f)) g.DrawPath(p, path);
                            using (var path = Theme.RoundedRect(new RectangleF(3, 5.5f, 10, 12.5f), 2f))
                            {
                                using (var b = new SolidBrush(Theme.Chrome)) g.FillPath(b, path);
                                g.DrawPath(p, path);
                            }
                        }
                        break;
                    case "save":
                        using (var p = new Pen(inkColor, 1.5f) { LineJoin = LineJoin.Round })
                        {
                            g.DrawLine(p, 10, 2.5f, 10, 12);
                            g.DrawLines(p, new[] { P(6, 8.5f), P(10, 12.5f), P(14, 8.5f) });
                            g.DrawLines(p, new[] { P(3, 13), P(3, 17), P(17, 17), P(17, 13) });
                        }
                        break;
                    case "library":
                        using (var p = new Pen(inkColor, 1.5f))
                        {
                            for (int i = 0; i < 4; i++)
                            {
                                float x = 2.5f + (i % 2) * 8, y = 2.5f + (i / 2) * 8;
                                using (var path = Theme.RoundedRect(new RectangleF(x, y, 7, 7), 1.6f)) g.DrawPath(p, path);
                            }
                        }
                        break;
                    case "folder":
                        using (var p = new Pen(inkColor, 1.5f) { LineJoin = LineJoin.Round })
                            g.DrawPolygon(p, new[] { P(2.5f, 4.5f), P(8, 4.5f), P(10, 6.5f), P(17.5f, 6.5f), P(17.5f, 16), P(2.5f, 16) });
                        break;
                    case "bold":
                        using (var path = CenteredText("B", 14, P(10, 10.2f), true)) g.FillPath(inkBrush, path);
                        break;
                    case "search":
                        g.DrawEllipse(ink, 3, 3, 10, 10);
                        g.DrawLine(ink2, 11.8f, 11.8f, 16.5f, 16.5f);
                        break;
                    case "drive":
                    {
                        // Google-Drive-like triangle, simplified
                        using (var b1 = new SolidBrush(Color.FromArgb(52, 168, 83)))
                        using (var b2 = new SolidBrush(Color.FromArgb(66, 133, 244)))
                        using (var b3 = new SolidBrush(Color.FromArgb(251, 188, 4)))
                        {
                            g.FillPolygon(b3, new[] { P(7, 3), P(13, 3), P(19, 13.5f), P(13, 13.5f) });
                            g.FillPolygon(b1, new[] { P(7, 3), P(10, 8.25f), P(4, 18.5f), P(1, 13.5f) });
                            g.FillPolygon(b2, new[] { P(4, 18.5f), P(7, 13.5f), P(19, 13.5f), P(16, 18.5f) });
                        }
                        break;
                    }
                }
            }
        }

        public static GraphicsPath CenteredText(string text, float emSize, PointF center, bool bold)
        {
            var path = new GraphicsPath();
            FontFamily ff;
            try { ff = new FontFamily("Segoe UI"); }
            catch (ArgumentException) { ff = new FontFamily(System.Drawing.Text.GenericFontFamilies.SansSerif); }
            using (ff)
                path.AddString(text, ff, (int)(bold ? FontStyle.Bold : FontStyle.Regular), emSize, PointF.Empty, StringFormat.GenericTypographic);
            var b = path.GetBounds();
            using (var m = new Matrix())
            {
                m.Translate(center.X - (b.X + b.Width / 2), center.Y - (b.Y + b.Height / 2));
                path.Transform(m);
            }
            return path;
        }

        // ---------------------------------------------------------------- swatches

        public static Bitmap Swatch(Color c, int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float pad = size * 0.14f;
                var r = new RectangleF(pad, pad, size - 2 * pad, size - 2 * pad);
                if (c.A == 0)
                {
                    using (var p = new Pen(Theme.SubText, Math.Max(1f, size / 14f))) g.DrawEllipse(p, r);
                    using (var p = new Pen(Color.FromArgb(235, 80, 80), Math.Max(1.2f, size / 12f)))
                        g.DrawLine(p, r.Left + r.Width * 0.2f, r.Bottom - r.Height * 0.2f, r.Right - r.Width * 0.2f, r.Top + r.Height * 0.2f);
                }
                else
                {
                    if (c.A < 255)
                    {
                        using (var clip = new GraphicsPath())
                        {
                            clip.AddEllipse(r);
                            g.SetClip(clip);
                            float cs = Math.Max(2, size / 6f);
                            for (float y = r.Top; y < r.Bottom; y += cs)
                                for (float x = r.Left; x < r.Right; x += cs)
                                    g.FillRectangle(((int)((x - r.Left) / cs) + (int)((y - r.Top) / cs)) % 2 == 0 ? Brushes.White : Brushes.LightGray, x, y, cs, cs);
                            g.ResetClip();
                        }
                    }
                    using (var b = new SolidBrush(c)) g.FillEllipse(b, r);
                    using (var p = new Pen(Color.FromArgb(70, 255, 255, 255), 1f)) g.DrawEllipse(p, r);
                }
            }
            return bmp;
        }

        /// <summary>Hollow ring swatch for "outline color".</summary>
        public static Bitmap Ring(Color c, int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float w = size * 0.2f;
                float pad = size * 0.14f + w / 2;
                using (var p = new Pen(c, w)) g.DrawEllipse(p, pad, pad, size - 2 * pad, size - 2 * pad);
            }
            return bmp;
        }

        public static Bitmap WidthGlyph(float width, int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float w = Math.Max(1f, Math.Min(size * 0.55f, width * size / 28f + 1));
                using (var p = new Pen(Theme.Ink, w) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(p, size * 0.18f, size / 2f, size * 0.82f, size / 2f);
            }
            return bmp;
        }

        public static Bitmap StickerImage(StickerKind k, int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                Stickers.Draw(g, k, new RectangleF(size * 0.06f, size * 0.06f, size * 0.88f, size * 0.88f));
            }
            return bmp;
        }

        // ---------------------------------------------------------------- self-test sheet

        public static void WritePreviewSheet(string path)
        {
            string[] glyphs = { "select", "pen", "highlighter", "arrow", "line", "rect", "ellipse", "text", "step", "sticker", "blur", "crop", "capture", "undo", "redo", "copy", "save", "library", "folder", "bold", "search", "drive" };
            using (var bmp = new Bitmap(1200, 560))
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Theme.Chrome);
                g.FillRectangle(Brushes.White, 0, 400, 1200, 160);
                int x = 10;
                foreach (int s in new[] { 16, 20, 24, 32, 48, 64, 128, 256 })
                {
                    using (var ic = DrawAppIcon(s)) g.DrawImage(ic, x, 10, s, s);
                    x += s + 12;
                }
                x = 10;
                foreach (var name in glyphs)
                {
                    using (var a = Glyph(name, 20)) g.DrawImage(a, x, 280, 20, 20);
                    using (var a = Glyph(name, 40)) g.DrawImage(a, x, 310, 40, 40);
                    x += 50;
                }
                x = 10;
                foreach (StickerKind k in Enum.GetValues(typeof(StickerKind)))
                {
                    using (var a = StickerImage(k, 40)) g.DrawImage(a, x, 410, 40, 40);
                    using (var a = StickerImage(k, 96)) g.DrawImage(a, x, 455, 96, 96);
                    x += 110;
                }
                bmp.Save(path, ImageFormat.Png);
            }
        }
    }
}

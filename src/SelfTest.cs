using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Clipmunk
{
    /// <summary>
    /// Hidden "--selftest DIR" mode: renders the overlay, editor and library to PNGs and checks that
    /// annotations survive a save/reopen round trip. Never touches the user's real settings or library.
    /// </summary>
    internal static class SelfTest
    {
        private static readonly StringBuilder log = new StringBuilder();

        public static int Run(string dir)
        {
            int failures = 0;
            Directory.CreateDirectory(dir);
            string lib = Path.Combine(dir, "library");
            if (Directory.Exists(lib)) Directory.Delete(lib, true);
            Directory.CreateDirectory(lib);
            Settings.ReadOnly = true;
            Settings.Current.LibraryFolder = lib;
            Settings.Current.CaptureMode = CaptureMode.Rectangle;

            Step("art", () => Art.WritePreviewSheet(Path.Combine(dir, "art.png")), ref failures);
            Step("overlay", () => RenderOverlay(Path.Combine(dir, "overlay.png")), ref failures);
            Step("freeform", () => Freeform(Path.Combine(dir, "freeform.png")), ref failures);
            Step("empty step", () => EmptyStep(), ref failures);
            Step("roundtrip", () => RoundTrip(), ref failures);
            Step("editor", () => RenderEditor(Path.Combine(dir, "editor.png"), Path.Combine(dir, "flat.png")), ref failures);
            Step("library", () => RenderLibrary(Path.Combine(dir, "library.png")), ref failures);

            log.AppendLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
            File.WriteAllText(Path.Combine(dir, "results.txt"), log.ToString());
            return failures;
        }

        private static void Step(string name, Action a, ref int failures)
        {
            try
            {
                a();
                log.AppendLine("ok    " + name);
            }
            catch (Exception ex)
            {
                failures++;
                log.AppendLine("FAIL  " + name + ": " + ex);
            }
        }

        private static void Check(bool cond, string what)
        {
            if (!cond) throw new Exception("check failed: " + what);
        }

        private static Bitmap FakeDesktop(int w, int h)
        {
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppRgb);
            using (var g = Graphics.FromImage(bmp))
            {
                using (var bg = new LinearGradientBrush(new Rectangle(0, 0, w, h), Color.FromArgb(40, 70, 120), Color.FromArgb(150, 90, 140), 35f)) g.FillRectangle(bg, 0, 0, w, h);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                DrawWindow(g, new Rectangle(w / 10, h / 8, w / 2, h / 2), "Quarterly report — Excel", Color.FromArgb(33, 115, 70));
                DrawWindow(g, new Rectangle(w / 2, h / 3, w * 2 / 5, h / 2), "Inbox — Outlook", Color.FromArgb(0, 120, 212));
            }
            return bmp;
        }

        private static void DrawWindow(Graphics g, Rectangle r, string title, Color accent)
        {
            g.FillRectangle(Brushes.White, r);
            using (var b = new SolidBrush(accent)) g.FillRectangle(b, r.X, r.Y, r.Width, 34);
            using (var f = new Font("Segoe UI", 11f, GraphicsUnit.Pixel)) g.DrawString(title, f, Brushes.White, r.X + 10, r.Y + 9);
            using (var p = new Pen(Color.FromArgb(220, 220, 220)))
            using (var f = new Font("Segoe UI", 12f, GraphicsUnit.Pixel))
            {
                for (int y = r.Y + 50, i = 0; y < r.Bottom - 20; y += 26, i++)
                {
                    g.DrawLine(p, r.X + 10, y + 20, r.Right - 10, y + 20);
                    g.DrawString("Row " + (i + 1) + "   alpha  1,284   beta  9.31%   gamma  $4,120", f, Brushes.DimGray, r.X + 14, y + 2);
                }
            }
        }

        private static void RenderOverlay(string path)
        {
            var vs = Screen.PrimaryScreen.Bounds;
            using (var shot = FakeDesktop(vs.Width, vs.Height))
            using (var ov = new CaptureOverlay(shot, vs, new List<Rectangle>()))
            using (var target = new Bitmap(vs.Width, vs.Height))
            {
                var sel = new Rectangle(vs.Width / 4, vs.Height / 4, vs.Width / 3, vs.Height / 3);
                ov.RenderPreview(target, new Point(sel.Right - 1, sel.Bottom - 1), sel);
                target.Save(path, ImageFormat.Png);
            }
        }

        private static void Freeform(string path)
        {
            var vs = Screen.PrimaryScreen.Bounds;
            using (var shot = FakeDesktop(vs.Width, vs.Height))
            using (var ov = new CaptureOverlay(shot, vs, new List<Rectangle>()))
            {
                var tri = new List<Point> { new Point(200, 100), new Point(500, 400), new Point(100, 400) };
                using (var cut = ov.FreeformForTest(tri))
                {
                    Check(cut != null, "freeform result");
                    Check(cut.PixelFormat == PixelFormat.Format32bppArgb, "freeform has alpha");
                    Check(cut.Width == 401 && cut.Height == 301, "freeform size " + cut.Width + "x" + cut.Height);
                    Check(cut.GetPixel(2, 2).A == 0, "outside is transparent");
                    Check(cut.GetPixel(200, 250).A == 255, "inside is opaque");
                    Check(cut.GetPixel(200, 250).ToArgb() == shot.GetPixel(300, 350).ToArgb(), "inside matches the screen");
                    cut.Save(path, ImageFormat.Png);
                }
            }
        }

        /// <summary>A step is 0×0 right after the click that creates it; drawing it must not throw.</summary>
        private static void EmptyStep()
        {
            using (var bmp = new Bitmap(100, 100))
            using (var g = Graphics.FromImage(bmp))
                new StepShape { Number = 1, L = 50, T = 50, R = 50, B = 50 }.Draw(g, bmp);
        }

        private static List<Shape> SampleShapes(int w, int h)
        {
            var shapes = new List<Shape>();
            shapes.Add(new RectShape { Stroke = Color.FromArgb(255, 59, 48), Width = 4, L = w * 0.12f, T = h * 0.2f, R = w * 0.45f, B = h * 0.45f });
            shapes.Add(new LineShape { Arrow = true, Stroke = Color.FromArgb(255, 59, 48), Width = 6, P1 = new PointF(w * 0.7f, h * 0.15f), P2 = new PointF(w * 0.47f, h * 0.3f) });
            shapes.Add(new TextShape { Text = "Check this total", X = w * 0.55f, Y = h * 0.08f, FontSize = 28, Stroke = Color.White, Fill = Color.FromArgb(255, 59, 48) });
            shapes.Add(new EllipseShape { Stroke = Color.FromArgb(10, 132, 255), Width = 4, L = w * 0.6f, T = h * 0.55f, R = w * 0.85f, B = h * 0.75f });
            shapes.Add(new BlurShape { Width = 4, L = w * 0.15f, T = h * 0.6f, R = w * 0.45f, B = h * 0.72f });
            var pen = new PenShape { Stroke = Color.FromArgb(52, 199, 89), Width = 5 };
            for (int i = 0; i <= 40; i++) pen.Points.Add(new PointF(w * 0.55f + i * 6, h * 0.85f + (float)Math.Sin(i / 4.0) * 18));
            shapes.Add(pen);
            var hl = new PenShape { Highlighter = true, Stroke = Color.FromArgb(255, 214, 10), Width = 5 };
            hl.Points.Add(new PointF(w * 0.12f, h * 0.53f));
            hl.Points.Add(new PointF(w * 0.45f, h * 0.53f));
            shapes.Add(hl);
            shapes.Add(new StepShape { Number = 1, Stroke = Color.FromArgb(255, 149, 0), L = w * 0.08f, T = h * 0.12f, R = w * 0.08f + 40, B = h * 0.12f + 40 });
            shapes.Add(new StepShape { Number = 2, Stroke = Color.FromArgb(255, 149, 0), L = w * 0.5f, T = h * 0.5f, R = w * 0.5f + 40, B = h * 0.5f + 40 });
            shapes.Add(new StickerShape { Sticker = StickerKind.Check, L = w * 0.88f, T = h * 0.4f, R = w * 0.88f + 64, B = h * 0.4f + 64 });
            shapes.Add(new StickerShape { Sticker = StickerKind.Warning, L = w * 0.03f, T = h * 0.75f, R = w * 0.03f + 64, B = h * 0.75f + 64 });
            return shapes;
        }

        private static void RoundTrip()
        {
            using (var img = FakeDesktop(900, 560))
            {
                var bmp = img.Clone(new Rectangle(0, 0, 900, 560), PixelFormat.Format24bppRgb);
                var doc = CaptureDocument.CreateNew(bmp);
                doc.Shapes = SampleShapes(900, 560);
                ((TextShape)doc.Shapes[2]).Text = "Line one\r\nline two with tab\tand ünïcødé 🐿";
                doc.Save(bmp);
                var again = CaptureDocument.Open(doc.ImagePath);
                Check(again.Shapes.Count == doc.Shapes.Count, "shape count " + again.Shapes.Count + " vs " + doc.Shapes.Count);
                Check(((TextShape)again.Shapes[2]).Text == ((TextShape)doc.Shapes[2]).Text, "text survives");
                Check(((PenShape)again.Shapes[5]).Points.Count == 41, "pen points survive");
                Check(again.Base.Width == 900 && again.Base.Height == 560, "base size");
                log.AppendLine("      saved " + doc.ImagePath);
            }
        }

        private static void RenderEditor(string path, string flatPath)
        {
            using (var img = FakeDesktop(900, 560))
            {
                var bmp = img.Clone(new Rectangle(0, 0, 900, 560), PixelFormat.Format24bppRgb);
                var doc = CaptureDocument.CreateNew(bmp);
                doc.Shapes = SampleShapes(900, 560);
                using (var ed = new EditorForm(doc))
                {
                    ed.ShowInTaskbar = false;
                    ed.Show();
                    ed.Location = new Point(-ed.Width - 4000, 0);
                    Pump(300);
                    using (var shot = new Bitmap(ed.Width, ed.Height))
                    {
                        ed.DrawToBitmap(shot, new Rectangle(0, 0, ed.Width, ed.Height));
                        shot.Save(path, ImageFormat.Png);
                    }
                    using (var flat = ed.FlattenForTest()) flat.Save(flatPath, ImageFormat.Png);
                    ed.Close();
                }
            }
        }

        private static void RenderLibrary(string path)
        {
            using (var img = FakeDesktop(1200, 700))
            {
                for (int i = 0; i < 3; i++)
                {
                    var bmp = img.Clone(new Rectangle(i * 150, i * 60, 700 - i * 120, 420 - i * 50), PixelFormat.Format24bppRgb);
                    CaptureDocument.CreateNew(bmp);
                    System.Threading.Thread.Sleep(1100); // distinct timestamps in file names
                }
            }
            using (var lib = new LibraryForm())
            {
                lib.ShowInTaskbar = false;
                lib.Show();
                lib.Location = new Point(-lib.Width - 4000, 0);
                Pump(2000);
                using (var shot = new Bitmap(lib.Width, lib.Height))
                {
                    lib.DrawToBitmap(shot, new Rectangle(0, 0, lib.Width, lib.Height));
                    shot.Save(path, ImageFormat.Png);
                }
                lib.Close();
            }
        }

        private static void Pump(int ms)
        {
            var until = DateTime.Now.AddMilliseconds(ms);
            while (DateTime.Now < until)
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(15);
            }
        }
    }
}

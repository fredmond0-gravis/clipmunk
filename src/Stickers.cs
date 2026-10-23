using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Clipmunk
{
    internal enum StickerKind { Check, Cross, Warning, Info, Question, Star, Heart, Smile, Cursor, Pin, Fire, ThumbsUp }

    /// <summary>Vector stickers drawn on a 100x100 design grid.</summary>
    internal static class Stickers
    {
        public static string Name(StickerKind k)
        {
            switch (k)
            {
                case StickerKind.Check: return "Check";
                case StickerKind.Cross: return "Cross";
                case StickerKind.Warning: return "Warning";
                case StickerKind.Info: return "Info";
                case StickerKind.Question: return "Question";
                case StickerKind.Star: return "Star";
                case StickerKind.Heart: return "Heart";
                case StickerKind.Smile: return "Smile";
                case StickerKind.Cursor: return "Mouse pointer";
                case StickerKind.Pin: return "Pin";
                case StickerKind.Fire: return "Fire";
                case StickerKind.ThumbsUp: return "Thumbs up";
            }
            return k.ToString();
        }

        private static PointF P(float x, float y) { return new PointF(x, y); }

        public static void Draw(Graphics g, StickerKind k, RectangleF r)
        {
            if (r.Width < 1 || r.Height < 1) return;
            var state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(r.X, r.Y);
            g.ScaleTransform(r.Width / 100f, r.Height / 100f);
            switch (k)
            {
                case StickerKind.Check:
                    Badge(g, Color.FromArgb(46, 170, 82));
                    using (var p = WhitePen(11)) g.DrawLines(p, new[] { P(29, 52), P(44, 66), P(72, 36) });
                    break;
                case StickerKind.Cross:
                    Badge(g, Color.FromArgb(232, 62, 62));
                    using (var p = WhitePen(11))
                    {
                        g.DrawLine(p, 35, 35, 65, 65);
                        g.DrawLine(p, 65, 35, 35, 65);
                    }
                    break;
                case StickerKind.Info:
                    Badge(g, Color.FromArgb(40, 132, 238));
                    g.FillEllipse(Brushes.White, 43.5f, 20, 13, 13);
                    using (var path = Theme.RoundedRect(new RectangleF(44, 39, 12, 39), 6)) g.FillPath(Brushes.White, path);
                    break;
                case StickerKind.Question:
                    Badge(g, Color.FromArgb(132, 92, 230));
                    using (var path = Art.CenteredText("?", 62, P(50, 51), true)) g.FillPath(Brushes.White, path);
                    break;
                case StickerKind.Warning:
                {
                    using (var path = new GraphicsPath())
                    {
                        path.AddPolygon(new[] { P(50, 10), P(93, 86), P(7, 86) });
                        Shadow(g, path);
                        using (var p = new Pen(Color.White, 12) { LineJoin = LineJoin.Round }) g.DrawPath(p, path);
                        using (var b = new SolidBrush(Color.FromArgb(255, 196, 0))) g.FillPath(b, path);
                        using (var p = new Pen(Color.FromArgb(255, 196, 0), 6) { LineJoin = LineJoin.Round }) g.DrawPath(p, path);
                    }
                    using (var b = new SolidBrush(Color.FromArgb(33, 33, 33)))
                    {
                        using (var path = Theme.RoundedRect(new RectangleF(44.5f, 34, 11, 30), 5.5f)) g.FillPath(b, path);
                        g.FillEllipse(b, 44, 69, 12, 12);
                    }
                    break;
                }
                case StickerKind.Star:
                {
                    var pts = new PointF[10];
                    for (int i = 0; i < 10; i++)
                    {
                        double a = -Math.PI / 2 + i * Math.PI / 5;
                        float rad = i % 2 == 0 ? 47 : 20;
                        pts[i] = P(50 + (float)Math.Cos(a) * rad, 54 + (float)Math.Sin(a) * rad);
                    }
                    Outlined(g, pts, Color.FromArgb(255, 193, 7));
                    break;
                }
                case StickerKind.Heart:
                    using (var path = new GraphicsPath())
                    {
                        path.AddBezier(50, 90, 20, 70, 6, 52, 6, 33);
                        path.AddBezier(6, 33, 6, 17, 17, 8, 30, 8);
                        path.AddBezier(30, 8, 40, 8, 47, 14, 50, 21);
                        path.AddBezier(50, 21, 53, 14, 60, 8, 70, 8);
                        path.AddBezier(70, 8, 83, 8, 94, 17, 94, 33);
                        path.AddBezier(94, 33, 94, 52, 80, 70, 50, 90);
                        path.CloseFigure();
                        OutlinedPath(g, path, Color.FromArgb(236, 64, 90));
                    }
                    break;
                case StickerKind.Smile:
                    Badge(g, Color.FromArgb(255, 200, 40));
                    using (var b = new SolidBrush(Color.FromArgb(70, 44, 20)))
                    {
                        g.FillEllipse(b, 32, 31, 10, 15);
                        g.FillEllipse(b, 58, 31, 10, 15);
                    }
                    using (var p = new Pen(Color.FromArgb(70, 44, 20), 7) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        g.DrawArc(p, 27, 28, 46, 44, 25, 130);
                    break;
                case StickerKind.Cursor:
                {
                    var pts = new[] { P(24, 4), P(24, 84), P(42, 67), P(54, 94), P(67, 88), P(55, 62), P(80, 62) };
                    using (var path = new GraphicsPath())
                    {
                        path.AddPolygon(pts);
                        Shadow(g, path);
                        g.FillPath(Brushes.White, path);
                        using (var p = new Pen(Color.Black, 5) { LineJoin = LineJoin.Round }) g.DrawPath(p, path);
                    }
                    break;
                }
                case StickerKind.Pin:
                    using (var path = new GraphicsPath())
                    {
                        path.AddArc(20, 4, 60, 60, 135, 270);
                        path.AddLine(71.2f, 55.2f, 50, 96);
                        path.CloseFigure();
                        OutlinedPath(g, path, Color.FromArgb(232, 62, 62));
                    }
                    g.FillEllipse(Brushes.White, 37, 21, 26, 26);
                    break;
                case StickerKind.Fire:
                    using (var path = new GraphicsPath())
                    {
                        path.AddBezier(50, 96, 24, 96, 12, 78, 14, 60);
                        path.AddBezier(14, 60, 16, 42, 30, 34, 32, 18);
                        path.AddBezier(32, 18, 44, 26, 48, 38, 46, 48);
                        path.AddBezier(46, 48, 56, 40, 60, 22, 56, 4);
                        path.AddBezier(56, 4, 78, 18, 90, 42, 86, 64);
                        path.AddBezier(86, 64, 84, 84, 70, 96, 50, 96);
                        path.CloseFigure();
                        OutlinedPath(g, path, Color.FromArgb(255, 112, 30));
                    }
                    using (var path = new GraphicsPath())
                    {
                        path.AddBezier(50, 90, 38, 90, 32, 80, 34, 70);
                        path.AddBezier(34, 70, 36, 60, 46, 56, 48, 46);
                        path.AddBezier(48, 46, 60, 56, 68, 66, 64, 76);
                        path.AddBezier(64, 76, 62, 86, 56, 90, 50, 90);
                        path.CloseFigure();
                        using (var b = new SolidBrush(Color.FromArgb(255, 214, 64))) g.FillPath(b, path);
                    }
                    break;
                case StickerKind.ThumbsUp:
                    Badge(g, Color.FromArgb(40, 132, 238));
                    using (var path = new GraphicsPath())
                    {
                        // thumb + fist
                        path.AddLine(38, 47, 50, 24);
                        path.AddBezier(50, 24, 54, 18, 62, 22, 60, 30);
                        path.AddLine(60, 30, 57, 42);
                        path.AddLine(57, 42, 71, 42);
                        path.AddBezier(71, 42, 78, 42, 79, 50, 75, 53);
                        path.AddBezier(75, 53, 78, 57, 76, 63, 72, 64);
                        path.AddBezier(72, 64, 75, 68, 72, 74, 67, 74);
                        path.AddLine(67, 74, 38, 74);
                        path.CloseFigure();
                        g.FillPath(Brushes.White, path);
                    }
                    using (var path = Theme.RoundedRect(new RectangleF(25, 46, 10, 29), 3)) g.FillPath(Brushes.White, path);
                    break;
            }
            g.Restore(state);
        }

        private static Pen WhitePen(float w)
        {
            return new Pen(Color.White, w) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        }

        private static void Shadow(Graphics g, GraphicsPath path)
        {
            var st = g.Save();
            g.TranslateTransform(1.5f, 3.5f);
            using (var b = new SolidBrush(Color.FromArgb(60, 0, 0, 0))) g.FillPath(b, path);
            g.Restore(st);
        }

        /// <summary>Round badge with a white rim and soft shadow.</summary>
        private static void Badge(Graphics g, Color fill)
        {
            using (var b = new SolidBrush(Color.FromArgb(60, 0, 0, 0))) g.FillEllipse(b, 4, 6.5f, 93, 93);
            g.FillEllipse(Brushes.White, 2.5f, 2.5f, 93, 93);
            using (var b = new SolidBrush(fill)) g.FillEllipse(b, 8, 8, 82, 82);
        }

        private static void Outlined(Graphics g, PointF[] pts, Color fill)
        {
            using (var path = new GraphicsPath())
            {
                path.AddPolygon(pts);
                OutlinedPath(g, path, fill);
            }
        }

        private static void OutlinedPath(Graphics g, GraphicsPath path, Color fill)
        {
            Shadow(g, path);
            using (var p = new Pen(Color.White, 9) { LineJoin = LineJoin.Round }) g.DrawPath(p, path);
            using (var b = new SolidBrush(fill)) g.FillPath(b, path);
        }
    }
}

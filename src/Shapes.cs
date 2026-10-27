using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Windows.Forms;

namespace Clipmunk
{
    /// <summary>An editable annotation. Coordinates are in image pixels.</summary>
    internal abstract class Shape
    {
        public Color Stroke = Color.Red;
        public Color Fill = Color.Transparent;
        public float Width = 4f;

        public abstract string Kind { get; }
        public abstract void Draw(Graphics g, Bitmap baseImage);
        public abstract RectangleF Bounds { get; }
        public abstract bool HitTest(PointF p, float tolerance);
        public abstract void Offset(float dx, float dy);
        public abstract Shape Clone();
        public virtual PointF[] Handles { get { return new PointF[0]; } }
        public virtual void MoveHandle(int index, PointF p, bool constrain) { }
        public virtual Cursor HandleCursor(int index) { return Cursors.SizeAll; }
        public virtual void Normalize() { }
        public virtual bool IsDegenerate { get { return false; } }

        // Which style fields this shape responds to.
        public virtual bool UsesStroke { get { return true; } }
        public virtual bool UsesFill { get { return false; } }
        public virtual bool UsesWidth { get { return false; } }

        protected T CopyStyle<T>(T s) where T : Shape
        {
            s.Stroke = Stroke;
            s.Fill = Fill;
            s.Width = Width;
            return s;
        }

        // ---- persistence: one tab-separated line per shape
        public virtual void Write(List<string> f)
        {
            f.Add(Hex(Stroke));
            f.Add(Hex(Fill));
            f.Add(Num(Width));
        }

        public virtual void Read(string[] f, ref int i)
        {
            Stroke = ParseColor(f[i++]);
            Fill = ParseColor(f[i++]);
            Width = ParseNum(f[i++]);
        }

        public static string Hex(Color c) { return c.ToArgb().ToString("X8"); }
        public static string Num(float v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }
        public static Color ParseColor(string s) { return Color.FromArgb(int.Parse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture)); }
        public static float ParseNum(string s) { return float.Parse(s, CultureInfo.InvariantCulture); }

        // ---- geometry helpers
        public static float DistanceToSegment(PointF p, PointF a, PointF b)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y;
            float len2 = dx * dx + dy * dy;
            float t = len2 == 0 ? 0 : ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2;
            t = Math.Max(0f, Math.Min(1f, t));
            float x = a.X + t * dx - p.X, y = a.Y + t * dy - p.Y;
            return (float)Math.Sqrt(x * x + y * y);
        }

        public static PointF ConstrainAspect(PointF anchor, PointF p, float aspect)
        {
            float w = p.X - anchor.X, h = p.Y - anchor.Y;
            float sw = w < 0 ? -1 : 1, sh = h < 0 ? -1 : 1;
            float aw = Math.Abs(w), ah = Math.Abs(h);
            if (aw / aspect > ah) ah = aw / aspect;
            else aw = ah * aspect;
            return new PointF(anchor.X + sw * aw, anchor.Y + sh * ah);
        }

        public static PointF SnapAngle(PointF anchor, PointF p)
        {
            double dx = p.X - anchor.X, dy = p.Y - anchor.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            double step = Math.PI / 4;
            double a = Math.Round(Math.Atan2(dy, dx) / step) * step;
            return new PointF(anchor.X + (float)(Math.Cos(a) * len), anchor.Y + (float)(Math.Sin(a) * len));
        }

        public static Color ContrastText(Color c)
        {
            double lum = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
            return lum > 0.62 ? Color.FromArgb(28, 28, 30) : Color.White;
        }

        public static bool Same(Color a, Color b) { return a.ToArgb() == b.ToArgb(); }
    }

    // ------------------------------------------------------------------ lines & arrows

    internal sealed class LineShape : Shape
    {
        public PointF P1, P2;
        public bool Arrow;

        public override string Kind { get { return Arrow ? "arrow" : "line"; } }
        public override bool UsesWidth { get { return true; } }
        private float HeadLength { get { return Math.Max(Width * 3.4f, 12f); } }

        public override void Draw(Graphics g, Bitmap baseImage)
        {
            float w = Math.Max(Width, 1f);
            float dx = P2.X - P1.X, dy = P2.Y - P1.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            using (var pen = new Pen(Stroke, w))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                if (!Arrow || len < 0.5f)
                {
                    g.DrawLine(pen, P1, P2);
                    return;
                }
                float head = Math.Min(HeadLength, len);
                float half = Math.Max(head * 0.48f, w * 1.1f);
                float ux = dx / len, uy = dy / len;
                var bc = new PointF(P2.X - ux * head, P2.Y - uy * head);
                var notch = new PointF(bc.X + ux * head * 0.22f, bc.Y + uy * head * 0.22f);
                pen.EndCap = LineCap.Flat;
                if (len > head) g.DrawLine(pen, P1, new PointF(notch.X + ux * 0.5f, notch.Y + uy * 0.5f));
                var pts = new[]
                {
                    P2,
                    new PointF(bc.X - uy * half, bc.Y + ux * half),
                    notch,
                    new PointF(bc.X + uy * half, bc.Y - ux * half)
                };
                using (var b = new SolidBrush(Stroke)) g.FillPolygon(b, pts);
            }
        }

        public override RectangleF Bounds
        {
            get
            {
                var r = RectangleF.FromLTRB(Math.Min(P1.X, P2.X), Math.Min(P1.Y, P2.Y), Math.Max(P1.X, P2.X), Math.Max(P1.Y, P2.Y));
                float i = Arrow ? Math.Max(Width, HeadLength / 2) : Width / 2;
                r.Inflate(i, i);
                return r;
            }
        }

        public override bool HitTest(PointF p, float tol) { return DistanceToSegment(p, P1, P2) <= tol + Math.Max(Width, 2) / 2 + (Arrow ? 2 : 0); }
        public override void Offset(float dx, float dy) { P1 = new PointF(P1.X + dx, P1.Y + dy); P2 = new PointF(P2.X + dx, P2.Y + dy); }
        public override PointF[] Handles { get { return new[] { P1, P2 }; } }

        public override void MoveHandle(int index, PointF p, bool constrain)
        {
            var anchor = index == 0 ? P2 : P1;
            if (constrain) p = SnapAngle(anchor, p);
            if (index == 0) P1 = p; else P2 = p;
        }

        public override bool IsDegenerate
        {
            get
            {
                float dx = P2.X - P1.X, dy = P2.Y - P1.Y;
                return dx * dx + dy * dy < 9;
            }
        }

        public override Shape Clone() { return CopyStyle(new LineShape { P1 = P1, P2 = P2, Arrow = Arrow }); }

        public override void Write(List<string> f)
        {
            base.Write(f);
            f.Add(Num(P1.X)); f.Add(Num(P1.Y)); f.Add(Num(P2.X)); f.Add(Num(P2.Y));
        }

        public override void Read(string[] f, ref int i)
        {
            base.Read(f, ref i);
            P1 = new PointF(ParseNum(f[i++]), ParseNum(f[i++]));
            P2 = new PointF(ParseNum(f[i++]), ParseNum(f[i++]));
        }
    }

    // ------------------------------------------------------------------ freehand

    internal sealed class PenShape : Shape
    {
        public readonly List<PointF> Points = new List<PointF>();
        public bool Highlighter;

        public override string Kind { get { return Highlighter ? "highlighter" : "pen"; } }
        public override bool UsesWidth { get { return true; } }
        private float EffectiveWidth { get { return Highlighter ? Math.Max(Width * 3.5f, 12f) : Math.Max(Width, 1f); } }

        public override void Draw(Graphics g, Bitmap baseImage)
        {
            if (Points.Count == 0) return;
            float w = EffectiveWidth;
            var color = Highlighter ? Color.FromArgb(125, Stroke) : Stroke;
            if (Points.Count == 1)
            {
                using (var b = new SolidBrush(color)) g.FillEllipse(b, Points[0].X - w / 2, Points[0].Y - w / 2, w, w);
                return;
            }
            using (var pen = new Pen(color, w))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                g.DrawLines(pen, Points.ToArray());
            }
        }

        public override RectangleF Bounds
        {
            get
            {
                if (Points.Count == 0) return RectangleF.Empty;
                float l = float.MaxValue, t = float.MaxValue, r = float.MinValue, b = float.MinValue;
                foreach (var p in Points)
                {
                    l = Math.Min(l, p.X); t = Math.Min(t, p.Y);
                    r = Math.Max(r, p.X); b = Math.Max(b, p.Y);
                }
                var rect = RectangleF.FromLTRB(l, t, r, b);
                rect.Inflate(EffectiveWidth / 2, EffectiveWidth / 2);
                return rect;
            }
        }

        public override bool HitTest(PointF p, float tol)
        {
            float lim = tol + EffectiveWidth / 2;
            if (Points.Count == 1) return DistanceToSegment(p, Points[0], Points[0]) <= lim;
            for (int i = 1; i < Points.Count; i++)
                if (DistanceToSegment(p, Points[i - 1], Points[i]) <= lim) return true;
            return false;
        }

        public override void Offset(float dx, float dy)
        {
            for (int i = 0; i < Points.Count; i++) Points[i] = new PointF(Points[i].X + dx, Points[i].Y + dy);
        }

        public override Shape Clone()
        {
            var s = CopyStyle(new PenShape { Highlighter = Highlighter });
            s.Points.AddRange(Points);
            return s;
        }

        public override void Write(List<string> f)
        {
            base.Write(f);
            var parts = new List<string>();
            foreach (var p in Points) parts.Add(Num(p.X) + "," + Num(p.Y));
            f.Add(string.Join(";", parts.ToArray()));
        }

        public override void Read(string[] f, ref int i)
        {
            base.Read(f, ref i);
            foreach (var pair in f[i++].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var xy = pair.Split(',');
                Points.Add(new PointF(ParseNum(xy[0]), ParseNum(xy[1])));
            }
        }
    }

    // ------------------------------------------------------------------ rectangular shapes

    internal abstract class BoxShape : Shape
    {
        public float L, T, R, B;
        private static readonly int[] CornerIds = { 0, 2, 4, 6 };

        public virtual bool LockAspect { get { return false; } }
        public virtual float Aspect { get { return 1f; } }

        public RectangleF Rect
        {
            get { return RectangleF.FromLTRB(Math.Min(L, R), Math.Min(T, B), Math.Max(L, R), Math.Max(T, B)); }
        }

        public void SetRect(RectangleF r) { L = r.Left; T = r.Top; R = r.Right; B = r.Bottom; }

        public override RectangleF Bounds { get { return Rect; } }
        public override void Offset(float dx, float dy) { L += dx; R += dx; T += dy; B += dy; }
        public override void Normalize() { SetRect(Rect); }

        public override bool IsDegenerate
        {
            get
            {
                var r = Rect;
                return r.Width < 3 || r.Height < 3;
            }
        }

        public override bool HitTest(PointF p, float tol)
        {
            var r = Rect;
            r.Inflate(tol, tol);
            return r.Contains(p);
        }

        // Handle ids: 0 TL, 1 T, 2 TR, 3 R, 4 BR, 5 B, 6 BL, 7 L
        private int HandleId(int index) { return LockAspect ? CornerIds[index] : index; }

        public override PointF[] Handles
        {
            get
            {
                var r = Rect;
                float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
                var all = new[]
                {
                    new PointF(r.Left, r.Top), new PointF(cx, r.Top), new PointF(r.Right, r.Top), new PointF(r.Right, cy),
                    new PointF(r.Right, r.Bottom), new PointF(cx, r.Bottom), new PointF(r.Left, r.Bottom), new PointF(r.Left, cy)
                };
                if (!LockAspect) return all;
                return new[] { all[0], all[2], all[4], all[6] };
            }
        }

        public override Cursor HandleCursor(int index)
        {
            switch (HandleId(index))
            {
                case 0: case 4: return Cursors.SizeNWSE;
                case 2: case 6: return Cursors.SizeNESW;
                case 1: case 5: return Cursors.SizeNS;
                default: return Cursors.SizeWE;
            }
        }

        public override void MoveHandle(int index, PointF p, bool constrain)
        {
            int id = HandleId(index);
            if (id % 2 == 0 && (constrain || LockAspect))
            {
                float ax = (id == 0 || id == 6) ? R : L;
                float ay = (id == 0 || id == 2) ? B : T;
                p = ConstrainAspect(new PointF(ax, ay), p, Aspect);
            }
            switch (id)
            {
                case 0: L = p.X; T = p.Y; break;
                case 1: T = p.Y; break;
                case 2: R = p.X; T = p.Y; break;
                case 3: R = p.X; break;
                case 4: R = p.X; B = p.Y; break;
                case 5: B = p.Y; break;
                case 6: L = p.X; B = p.Y; break;
                case 7: L = p.X; break;
            }
        }

        protected TBox CopyBox<TBox>(TBox s) where TBox : BoxShape
        {
            CopyStyle(s);
            s.L = L; s.T = T; s.R = R; s.B = B;
            return s;
        }

        public override void Write(List<string> f)
        {
            base.Write(f);
            f.Add(Num(L)); f.Add(Num(T)); f.Add(Num(R)); f.Add(Num(B));
        }

        public override void Read(string[] f, ref int i)
        {
            base.Read(f, ref i);
            L = ParseNum(f[i++]); T = ParseNum(f[i++]); R = ParseNum(f[i++]); B = ParseNum(f[i++]);
        }
    }

    internal sealed class RectShape : BoxShape
    {
        public override string Kind { get { return "rect"; } }
        public override bool UsesFill { get { return true; } }
        public override bool UsesWidth { get { return true; } }

        private float Radius { get { return Math.Max(2f, Width * 1.2f); } }

        public override void Draw(Graphics g, Bitmap baseImage)
        {
            var r = Rect;
            using (var path = Theme.RoundedRect(r, Radius))
            {
                if (Fill.A > 0) using (var b = new SolidBrush(Fill)) g.FillPath(b, path);
                if (Width > 0) using (var pen = new Pen(Stroke, Width)) g.DrawPath(pen, path);
            }
        }

        public override RectangleF Bounds
        {
            get
            {
                var r = Rect;
                r.Inflate(Width / 2, Width / 2);
                return r;
            }
        }

        public override bool HitTest(PointF p, float tol)
        {
            if (Fill.A > 0) return base.HitTest(p, tol);
            float band = tol + Math.Max(Width, 2) / 2;
            var outer = Rect;
            outer.Inflate(band, band);
            var inner = Rect;
            inner.Inflate(-band, -band);
            return outer.Contains(p) && !(inner.Width > 0 && inner.Height > 0 && inner.Contains(p));
        }

        public override Shape Clone() { return CopyBox(new RectShape()); }
    }

    internal sealed class EllipseShape : BoxShape
    {
        public override string Kind { get { return "ellipse"; } }
        public override bool UsesFill { get { return true; } }
        public override bool UsesWidth { get { return true; } }

        public override void Draw(Graphics g, Bitmap baseImage)
        {
            var r = Rect;
            if (Fill.A > 0) using (var b = new SolidBrush(Fill)) g.FillEllipse(b, r);
            if (Width > 0) using (var pen = new Pen(Stroke, Width)) g.DrawEllipse(pen, r);
        }

        public override RectangleF Bounds
        {
            get
            {
                var r = Rect;
                r.Inflate(Width / 2, Width / 2);
                return r;
            }
        }

        public override bool HitTest(PointF p, float tol)
        {
            var r = Rect;
            float rx = r.Width / 2, ry = r.Height / 2;
            if (rx < 0.5f || ry < 0.5f) return false;
            float nx = (p.X - (r.X + rx)) / rx, ny = (p.Y - (r.Y + ry)) / ry;
            double d = Math.Sqrt(nx * nx + ny * ny);
            if (Fill.A > 0 && d <= 1) return true;
            double band = (tol + Math.Max(Width, 2) / 2) / Math.Min(rx, ry);
            return Math.Abs(d - 1) <= band;
        }

        public override Shape Clone() { return CopyBox(new EllipseShape()); }
    }

    /// <summary>Pixelates whatever part of the screenshot it covers.</summary>
    internal sealed class BlurShape : BoxShape
    {
        private Bitmap cache;
        private Rectangle cacheRect;
        private int cacheBlock;
        private Bitmap cacheSource;

        public override string Kind { get { return "blur"; } }
        public override bool UsesStroke { get { return false; } }
        public override bool UsesWidth { get { return true; } }

        public override void Draw(Graphics g, Bitmap baseImage)
        {
            if (baseImage == null) return;
            var r = Rectangle.Round(Rect);
            r.Intersect(new Rectangle(0, 0, baseImage.Width, baseImage.Height));
            if (r.Width < 1 || r.Height < 1) return;
            int block = (int)Math.Max(5, Math.Round(Width * 2.5f));
            int sw = Math.Max(1, (int)Math.Ceiling(r.Width / (float)block));
            int sh = Math.Max(1, (int)Math.Ceiling(r.Height / (float)block));
            if (cache == null || cacheRect != r || cacheBlock != block || cacheSource != baseImage)
            {
                if (cache != null) cache.Dispose();
                cache = new Bitmap(sw, sh, PixelFormat.Format32bppArgb);
                using (var cg = Graphics.FromImage(cache))
                using (var ia = new ImageAttributes())
                {
                    ia.SetWrapMode(WrapMode.TileFlipXY);
                    cg.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    cg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    cg.DrawImage(baseImage, new Rectangle(0, 0, sw, sh), r.X, r.Y, r.Width, r.Height, GraphicsUnit.Pixel, ia);
                }
                cacheRect = r;
                cacheBlock = block;
                cacheSource = baseImage;
            }
            var st = g.Save();
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            using (var ia = new ImageAttributes())
            {
                ia.SetWrapMode(WrapMode.TileFlipXY);
                g.DrawImage(cache, r, 0, 0, sw, sh, GraphicsUnit.Pixel, ia);
            }
            g.Restore(st);
        }

        public override Shape Clone() { return CopyBox(new BlurShape()); }
    }

    internal sealed class StickerShape : BoxShape
    {
        public StickerKind Sticker;

        public override string Kind { get { return "sticker"; } }
        public override bool UsesStroke { get { return false; } }
        public override bool LockAspect { get { return true; } }

        public override void Draw(Graphics g, Bitmap baseImage) { Stickers.Draw(g, Sticker, Rect); }
        public override Shape Clone() { var s = CopyBox(new StickerShape()); s.Sticker = Sticker; return s; }

        public override void Write(List<string> f) { base.Write(f); f.Add(Sticker.ToString()); }

        public override void Read(string[] f, ref int i)
        {
            base.Read(f, ref i);
            StickerKind k;
            if (Enum.TryParse(f[i++], out k)) Sticker = k;
        }
    }

    /// <summary>Numbered step badge (1, 2, 3...).</summary>
    internal sealed class StepShape : BoxShape
    {
        public int Number = 1;

        public override string Kind { get { return "step"; } }
        public override bool LockAspect { get { return true; } }

        public override void Draw(Graphics g, Bitmap baseImage)
        {
            var r = Rect;
            float d = Math.Min(r.Width, r.Height);
            if (d < 2f) return; // just created (0×0 until the drag ends); GDI+ can't lay out a 0 px number
            var c = new RectangleF(r.X + (r.Width - d) / 2, r.Y + (r.Height - d) / 2, d, d);
            using (var sh = new SolidBrush(Color.FromArgb(70, 0, 0, 0))) g.FillEllipse(sh, c.X + d * 0.02f, c.Y + d * 0.06f, d, d);
            using (var b = new SolidBrush(Stroke)) g.FillEllipse(b, c);
            using (var p = new Pen(Color.White, Math.Max(1.5f, d * 0.07f)))
            {
                var ri = c;
                ri.Inflate(-d * 0.035f, -d * 0.035f);
                g.DrawEllipse(p, ri);
            }
            string s = Number.ToString(CultureInfo.InvariantCulture);
            float em = d * (s.Length > 1 ? 0.46f : 0.56f);
            using (var path = Art.CenteredText(s, em, new PointF(c.X + d / 2, c.Y + d / 2), true))
            using (var tb = new SolidBrush(ContrastText(Stroke)))
                g.FillPath(tb, path);
        }

        public override bool HitTest(PointF p, float tol)
        {
            var r = Rect;
            float rad = Math.Min(r.Width, r.Height) / 2 + tol;
            float dx = p.X - (r.X + r.Width / 2), dy = p.Y - (r.Y + r.Height / 2);
            return dx * dx + dy * dy <= rad * rad;
        }

        public override Shape Clone() { var s = CopyBox(new StepShape()); s.Number = Number; return s; }
        public override void Write(List<string> f) { base.Write(f); f.Add(Number.ToString(CultureInfo.InvariantCulture)); }
        public override void Read(string[] f, ref int i) { base.Read(f, ref i); Number = int.Parse(f[i++], CultureInfo.InvariantCulture); }
    }

    /// <summary>Pasted or dropped image.</summary>
    internal sealed class ImageShape : BoxShape
    {
        public Bitmap Image;

        public override string Kind { get { return "image"; } }
        public override bool UsesStroke { get { return false; } }
        public override bool LockAspect { get { return true; } }
        public override float Aspect { get { return Image == null ? 1f : Image.Width / (float)Image.Height; } }

        public override void Draw(Graphics g, Bitmap baseImage)
        {
            if (Image == null) return;
            var r = Rect;
            var st = g.Save();
            bool exact = Math.Abs(r.Width - Image.Width) < 0.5f && Math.Abs(r.Height - Image.Height) < 0.5f;
            g.InterpolationMode = exact ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(Image, r);
            g.Restore(st);
        }

        public override Shape Clone() { var s = CopyBox(new ImageShape()); s.Image = Image; return s; }
    }

    // ------------------------------------------------------------------ text

    internal sealed class TextShape : Shape
    {
        public const string FontName = "Segoe UI";
        public float X, Y;
        public string Text = "";
        public float FontSize = 24f;
        public bool Bold = true;

        private static Bitmap measureBitmap;
        private static Graphics measureGraphics;
        public static readonly StringFormat Format = CreateFormat();

        private static StringFormat CreateFormat()
        {
            var f = (StringFormat)StringFormat.GenericTypographic.Clone();
            f.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
            f.Trimming = StringTrimming.None;
            return f;
        }

        public override string Kind { get { return "text"; } }
        public override bool UsesFill { get { return true; } }

        public Font CreateFont() { return new Font(FontName, Math.Max(4f, FontSize), Bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel); }
        private float Pad { get { return Fill.A > 0 ? FontSize * 0.32f : 0f; } }

        public SizeF MeasureText()
        {
            if (measureGraphics == null)
            {
                measureBitmap = new Bitmap(1, 1);
                measureGraphics = Graphics.FromImage(measureBitmap);
                measureGraphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            }
            string t = string.IsNullOrEmpty(Text) ? " " : Text;
            if (t.EndsWith("\n")) t += " ";
            using (var f = CreateFont()) return measureGraphics.MeasureString(t, f, PointF.Empty, Format);
        }

        public override RectangleF Bounds
        {
            get
            {
                var s = MeasureText();
                float p = Pad;
                return new RectangleF(X - p, Y - p, s.Width + 2 * p, s.Height + 2 * p);
            }
        }

        public override void Draw(Graphics g, Bitmap baseImage)
        {
            if (string.IsNullOrEmpty(Text)) return;
            if (Fill.A > 0)
            {
                using (var path = Theme.RoundedRect(Bounds, FontSize * 0.22f))
                using (var br = new SolidBrush(Fill))
                    g.FillPath(br, path);
            }
            using (var f = CreateFont())
            using (var br = new SolidBrush(Stroke))
                g.DrawString(Text, f, br, new PointF(X, Y), Format);
        }

        public override bool HitTest(PointF p, float tol)
        {
            var r = Bounds;
            r.Inflate(tol, tol);
            return r.Contains(p);
        }

        public override void Offset(float dx, float dy) { X += dx; Y += dy; }

        public override Shape Clone()
        {
            return CopyStyle(new TextShape { X = X, Y = Y, Text = Text, FontSize = FontSize, Bold = Bold });
        }

        public override void Write(List<string> f)
        {
            base.Write(f);
            f.Add(Num(X)); f.Add(Num(Y)); f.Add(Num(FontSize)); f.Add(Bold ? "1" : "0");
            f.Add(Uri.EscapeDataString(Text ?? ""));
        }

        public override void Read(string[] f, ref int i)
        {
            base.Read(f, ref i);
            X = ParseNum(f[i++]); Y = ParseNum(f[i++]); FontSize = ParseNum(f[i++]); Bold = f[i++] == "1";
            Text = Uri.UnescapeDataString(f[i++]);
        }
    }

    internal static class ShapeFactory
    {
        public static Shape Create(string kind)
        {
            switch (kind)
            {
                case "arrow": return new LineShape { Arrow = true };
                case "line": return new LineShape();
                case "pen": return new PenShape();
                case "highlighter": return new PenShape { Highlighter = true };
                case "rect": return new RectShape();
                case "ellipse": return new EllipseShape();
                case "blur": return new BlurShape();
                case "sticker": return new StickerShape();
                case "step": return new StepShape();
                case "image": return new ImageShape();
                case "text": return new TextShape();
            }
            return null;
        }
    }
}

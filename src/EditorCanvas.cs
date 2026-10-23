using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

namespace Clipmunk
{
    internal enum Tool { Select, Pen, Highlighter, Arrow, Line, Rectangle, Ellipse, Text, Step, Sticker, Blur, Crop }
    internal enum StyleField { Stroke, Fill, Width, FontSize, Bold, Sticker }

    internal sealed class Style
    {
        public Color Stroke;
        public Color Fill;
        public float Width;
        public float FontSize;
        public bool Bold;
        public StickerKind Sticker;
    }

    /// <summary>Scrolling viewport that keeps the canvas centred when it is smaller than the window.</summary>
    internal sealed class CanvasHost : Panel
    {
        public EditorCanvas Canvas;

        public CanvasHost()
        {
            AutoScroll = true;
            DoubleBuffered = true;
            BackColor = Theme.Canvas;
        }

        protected override Point ScrollToControl(Control activeControl) { return AutoScrollPosition; }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            CenterCanvas();
        }

        public void CenterCanvas()
        {
            if (Canvas == null) return;
            var cs = ClientSize;
            var sz = Canvas.Size;
            var sp = AutoScrollPosition;
            int x = sz.Width <= cs.Width ? (cs.Width - sz.Width) / 2 : sp.X;
            int y = sz.Height <= cs.Height ? (cs.Height - sz.Height) / 2 : sp.Y;
            if (Canvas.Left != x || Canvas.Top != y) Canvas.Location = new Point(x, y);
        }
    }

    internal sealed class EditorCanvas : Control
    {
        private sealed class Snapshot
        {
            public Bitmap Base;
            public List<Shape> Shapes;
        }

        private enum DragMode { None, Create, Move, Handle, Crop, Pan }

        public readonly Style Style;
        private readonly CanvasHost host;
        private readonly int pad;
        private readonly float ui;
        private Bitmap baseImage;
        private List<Shape> shapes;
        private Shape selected;
        private Tool tool;
        private float zoom = 1f;
        private Bitmap zoomCache;
        private float zoomCacheZoom;
        private Bitmap zoomCacheSource;

        private DragMode drag;
        private Shape creating;
        private PointF dragStart, lastPoint;
        private int handleIndex;
        private bool dragChanged;
        private Rectangle cropRect;
        private Point panMouse, panScroll;

        private Snapshot pending;
        private readonly Stack<Snapshot> undo = new Stack<Snapshot>();
        private readonly Stack<Snapshot> redo = new Stack<Snapshot>();

        private TextBox editBox;
        private TextShape editing;
        private bool editingIsNew, editDirty;
        private string editOriginal;

        private readonly ContextMenuStrip shapeMenu;

        public event EventHandler Changed;
        public event EventHandler SelectionChanged;
        public event EventHandler ZoomChanged;
        public event EventHandler ToolChanged;

        public EditorCanvas(CanvasHost host, Bitmap image, List<Shape> initialShapes, Style style)
        {
            this.host = host;
            host.Canvas = this;
            Style = style;
            ui = Theme.Scale;
            pad = (int)(28 * ui);
            baseImage = image;
            shapes = initialShapes ?? new List<Shape>();
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.Canvas;
            TabStop = true;
            AllowDrop = true;
            UpdateCanvasSize();

            shapeMenu = new ContextMenuStrip();
            Theme.Apply(shapeMenu);
            shapeMenu.Items.Add("Edit text", null, (s, e) => { var t = selected as TextShape; if (t != null) BeginTextEdit(t, false); });
            shapeMenu.Items.Add("Duplicate\tCtrl+D", null, (s, e) => DuplicateSelected());
            shapeMenu.Items.Add("Bring to front", null, (s, e) => Reorder(true));
            shapeMenu.Items.Add("Send to back", null, (s, e) => Reorder(false));
            shapeMenu.Items.Add(new ToolStripSeparator());
            shapeMenu.Items.Add("Delete\tDel", null, (s, e) => DeleteSelected());
            shapeMenu.Opening += (s, e) => shapeMenu.Items[0].Visible = selected is TextShape;
        }

        // ------------------------------------------------------------------ public surface

        public Bitmap BaseImage { get { return baseImage; } }
        public List<Shape> Shapes { get { return shapes; } }
        public Shape Selected { get { return selected; } }
        public float Zoom { get { return zoom; } }
        public bool CanUndo { get { return undo.Count > 0; } }
        public bool CanRedo { get { return redo.Count > 0; } }
        public bool IsEditingText { get { return editing != null; } }

        public Tool Tool
        {
            get { return tool; }
            set
            {
                CommitTextEdit();
                if (tool == value) return;
                tool = value;
                Cursor = CursorForTool();
                var h = ToolChanged;
                if (h != null) h(this, EventArgs.Empty);
                Invalidate();
            }
        }

        private PointF ToImage(Point p) { return new PointF((p.X - pad) / zoom, (p.Y - pad) / zoom); }
        private PointF ToScreen(PointF p) { return new PointF(p.X * zoom + pad, p.Y * zoom + pad); }

        private RectangleF ToScreen(RectangleF r)
        {
            return new RectangleF(r.X * zoom + pad, r.Y * zoom + pad, r.Width * zoom, r.Height * zoom);
        }

        private void UpdateCanvasSize()
        {
            Size = new Size((int)Math.Ceiling(baseImage.Width * zoom) + 2 * pad, (int)Math.Ceiling(baseImage.Height * zoom) + 2 * pad);
        }

        // ------------------------------------------------------------------ undo

        private Snapshot TakeSnapshot()
        {
            return new Snapshot { Base = baseImage, Shapes = shapes.Select(s => s.Clone()).ToList() };
        }

        private void BeginChange() { pending = TakeSnapshot(); }

        private void CommitChange()
        {
            if (pending == null) return;
            undo.Push(pending);
            redo.Clear();
            pending = null;
            OnChanged();
        }

        private void CancelChange() { pending = null; }

        private void OnChanged()
        {
            var h = Changed;
            if (h != null) h(this, EventArgs.Empty);
        }

        public void Undo()
        {
            CommitTextEdit();
            if (undo.Count == 0) return;
            redo.Push(new Snapshot { Base = baseImage, Shapes = shapes });
            Restore(undo.Pop());
        }

        public void Redo()
        {
            CommitTextEdit();
            if (redo.Count == 0) return;
            undo.Push(new Snapshot { Base = baseImage, Shapes = shapes });
            Restore(redo.Pop());
        }

        private void Restore(Snapshot s)
        {
            bool sizeChanged = s.Base != baseImage;
            baseImage = s.Base;
            shapes = s.Shapes;
            Select(null);
            if (sizeChanged)
            {
                UpdateCanvasSize();
                host.CenterCanvas();
            }
            Invalidate();
            OnChanged();
        }

        // ------------------------------------------------------------------ selection & style

        public void Select(Shape s)
        {
            selected = s;
            if (s != null) SyncStyleFrom(s);
            var h = SelectionChanged;
            if (h != null) h(this, EventArgs.Empty);
            Invalidate();
        }

        private void SyncStyleFrom(Shape s)
        {
            if (s.UsesStroke) Style.Stroke = s.Stroke;
            if (s.UsesFill) Style.Fill = s.Fill;
            if (s.UsesWidth) Style.Width = s.Width;
            var t = s as TextShape;
            if (t != null)
            {
                Style.FontSize = t.FontSize;
                Style.Bold = t.Bold;
            }
            var st = s as StickerShape;
            if (st != null) Style.Sticker = st.Sticker;
        }

        /// <summary>Push a toolbar change onto the selected shape (or the text being typed).</summary>
        public void ApplyStyle(StyleField field)
        {
            if (editing != null)
            {
                if (ApplyField(editing, field)) editDirty = true;
                StyleEditBox();
                Invalidate();
                return;
            }
            if (selected == null) return;
            var snap = TakeSnapshot();
            if (ApplyField(selected, field))
            {
                undo.Push(snap);
                redo.Clear();
                OnChanged();
                Invalidate();
            }
        }

        private bool ApplyField(Shape s, StyleField f)
        {
            switch (f)
            {
                case StyleField.Stroke:
                    if (!s.UsesStroke || Shape.Same(s.Stroke, Style.Stroke)) return false;
                    s.Stroke = Style.Stroke;
                    return true;
                case StyleField.Fill:
                    if (!s.UsesFill || Shape.Same(s.Fill, Style.Fill)) return false;
                    s.Fill = Style.Fill;
                    return true;
                case StyleField.Width:
                    if (!s.UsesWidth || s.Width == Style.Width) return false;
                    s.Width = Style.Width;
                    return true;
                case StyleField.FontSize:
                {
                    var t = s as TextShape;
                    if (t != null)
                    {
                        if (t.FontSize == Style.FontSize) return false;
                        t.FontSize = Style.FontSize;
                        return true;
                    }
                    var step = s as StepShape;
                    if (step != null)
                    {
                        var r = step.Rect;
                        float d = StepDiameter;
                        if (Math.Abs(r.Width - d) < 0.5f) return false;
                        float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
                        step.SetRect(new RectangleF(cx - d / 2, cy - d / 2, d, d));
                        return true;
                    }
                    return false;
                }
                case StyleField.Bold:
                {
                    var t = s as TextShape;
                    if (t == null || t.Bold == Style.Bold) return false;
                    t.Bold = Style.Bold;
                    return true;
                }
                case StyleField.Sticker:
                {
                    var st = s as StickerShape;
                    if (st == null || st.Sticker == Style.Sticker) return false;
                    st.Sticker = Style.Sticker;
                    return true;
                }
            }
            return false;
        }

        private float StepDiameter { get { return Math.Max(18f, Style.FontSize * 1.5f); } }

        private T Styled<T>(T s) where T : Shape
        {
            s.Stroke = Style.Stroke;
            s.Fill = Style.Fill;
            s.Width = Style.Width;
            return s;
        }

        // ------------------------------------------------------------------ commands

        public bool DeleteSelected()
        {
            if (selected == null) return false;
            BeginChange();
            shapes.Remove(selected);
            CommitChange();
            Select(null);
            return true;
        }

        public void DuplicateSelected()
        {
            if (selected == null) return;
            BeginChange();
            var c = selected.Clone();
            float off = 14 / zoom;
            c.Offset(off, off);
            shapes.Add(c);
            CommitChange();
            Select(c);
        }

        private void Reorder(bool front)
        {
            if (selected == null) return;
            BeginChange();
            shapes.Remove(selected);
            if (front) shapes.Add(selected);
            else shapes.Insert(0, selected);
            CommitChange();
            Invalidate();
        }

        public bool Nudge(int dx, int dy)
        {
            if (selected == null) return false;
            BeginChange();
            selected.Offset(dx, dy);
            CommitChange();
            Invalidate();
            return true;
        }

        public void Escape()
        {
            if (drag != DragMode.None)
            {
                if (drag == DragMode.Create && creating != null) shapes.Remove(creating);
                creating = null;
                drag = DragMode.None;
                pending = null;
                Capture = false;
                Invalidate();
            }
            else if (selected != null) Select(null);
            else Tool = Tool.Select;
        }

        public void AddImage(Bitmap img)
        {
            CommitTextEdit();
            BeginChange();
            float w = img.Width, h = img.Height;
            float max = Math.Min(baseImage.Width, baseImage.Height) * 0.8f;
            if (w > max || h > max)
            {
                float k = max / Math.Max(w, h);
                w *= k;
                h *= k;
            }
            var c = VisibleImageCenter();
            var s = new ImageShape { Image = img };
            s.SetRect(new RectangleF((float)Math.Round(c.X - w / 2), (float)Math.Round(c.Y - h / 2), w, h));
            shapes.Add(s);
            CommitChange();
            Tool = Tool.Select;
            Select(s);
        }

        public void AddText(string text)
        {
            CommitTextEdit();
            BeginChange();
            var c = VisibleImageCenter();
            var t = Styled(new TextShape { Text = text, FontSize = Style.FontSize, Bold = Style.Bold });
            var sz = t.MeasureText();
            t.X = c.X - sz.Width / 2;
            t.Y = c.Y - sz.Height / 2;
            shapes.Add(t);
            CommitChange();
            Select(t);
        }

        private PointF VisibleImageCenter()
        {
            var vis = new Point(host.ClientSize.Width / 2 - Left, host.ClientSize.Height / 2 - Top);
            var p = ToImage(vis);
            return new PointF(Math.Max(0, Math.Min(baseImage.Width, p.X)), Math.Max(0, Math.Min(baseImage.Height, p.Y)));
        }

        private int NextStepNumber()
        {
            int n = 0;
            foreach (var s in shapes)
            {
                var st = s as StepShape;
                if (st != null) n = Math.Max(n, st.Number);
            }
            return n + 1;
        }

        private void ApplyCrop(Rectangle r)
        {
            BeginChange();
            var nb = baseImage.Clone(r, baseImage.PixelFormat);
            nb.SetResolution(96, 96);
            foreach (var s in shapes) s.Offset(-r.X, -r.Y);
            baseImage = nb;
            CommitChange();
            Select(null);
            UpdateCanvasSize();
            host.PerformLayout();
            host.CenterCanvas();
            Tool = Tool.Select;
            Invalidate();
        }

        /// <summary>Screenshot with all annotations burned in.</summary>
        public Bitmap Flatten()
        {
            var fmt = Image.IsAlphaPixelFormat(baseImage.PixelFormat) ? PixelFormat.Format32bppArgb : PixelFormat.Format24bppRgb;
            var bmp = new Bitmap(baseImage.Width, baseImage.Height, fmt);
            bmp.SetResolution(96, 96);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(baseImage, new Rectangle(0, 0, baseImage.Width, baseImage.Height));
                g.CompositingMode = CompositingMode.SourceOver;
                PrepareShapeGraphics(g);
                foreach (var s in shapes) s.Draw(g, baseImage);
            }
            return bmp;
        }

        private static void PrepareShapeGraphics(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.CompositingQuality = CompositingQuality.HighQuality;
        }

        // ------------------------------------------------------------------ zoom

        public void SetZoom(float z, Point? anchor)
        {
            z = Math.Max(0.05f, Math.Min(16f, z));
            if (Math.Abs(z - zoom) < 0.0001f) return;
            CommitTextEdit();
            Point a = anchor ?? new Point(host.ClientSize.Width / 2 - Left, host.ClientSize.Height / 2 - Top);
            PointF imgPt = ToImage(a);
            var inHost = new Point(a.X + Left, a.Y + Top);
            zoom = z;
            UpdateCanvasSize();
            host.PerformLayout();
            var np = ToScreen(imgPt);
            host.AutoScrollPosition = new Point((int)(np.X - inHost.X), (int)(np.Y - inHost.Y));
            host.CenterCanvas();
            Invalidate();
            var h = ZoomChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        public float FitZoom()
        {
            float aw = host.ClientSize.Width - 2 * pad, ah = host.ClientSize.Height - 2 * pad;
            if (aw < 10 || ah < 10) return 1f;
            return Math.Min(1f, Math.Min(aw / baseImage.Width, ah / baseImage.Height));
        }

        public void ZoomToFit() { SetZoom(FitZoom(), null); }

        private static readonly float[] ZoomSteps = { 0.1f, 0.25f, 0.33f, 0.5f, 0.67f, 0.75f, 1f, 1.5f, 2f, 3f, 4f, 6f, 8f, 12f, 16f };

        public void ZoomStep(int dir, Point? anchor)
        {
            float next = zoom;
            if (dir > 0)
            {
                foreach (var s in ZoomSteps) if (s > zoom + 0.001f) { next = s; break; }
            }
            else
            {
                for (int i = ZoomSteps.Length - 1; i >= 0; i--) if (ZoomSteps[i] < zoom - 0.001f) { next = ZoomSteps[i]; break; }
            }
            SetZoom(next, anchor);
        }

        // ------------------------------------------------------------------ painting

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            int w = baseImage.Width, h = baseImage.Height;
            var img = new RectangleF(pad, pad, w * zoom, h * zoom);

            // soft drop shadow
            g.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 1; i <= 6; i++)
            {
                using (var p = new Pen(Color.FromArgb(18 - i * 2, 0, 0, 0), 2))
                    g.DrawRectangle(p, img.X - i, img.Y - i + 2, img.Width + 2 * i, img.Height + 2 * i);
            }
            g.SmoothingMode = SmoothingMode.None;

            DrawBase(g, e.ClipRectangle, img);

            var state = g.Save();
            g.TranslateTransform(pad, pad);
            g.ScaleTransform(zoom, zoom);
            g.SetClip(new RectangleF(0, 0, w, h));
            PrepareShapeGraphics(g);
            foreach (var s in shapes)
                if (s != editing) s.Draw(g, baseImage);
            g.Restore(state);

            if (drag == DragMode.Crop && cropRect.Width > 0 && cropRect.Height > 0) DrawCrop(g, img);
            if (selected != null && editing == null && drag != DragMode.Create) DrawSelection(g);
        }

        private void DrawBase(Graphics g, Rectangle clip, RectangleF img)
        {
            if (zoom >= 1f)
            {
                var vis = RectangleF.Intersect(clip, img);
                if (vis.Width <= 0 || vis.Height <= 0) return;
                int x0 = Math.Max(0, (int)Math.Floor((vis.Left - pad) / zoom));
                int y0 = Math.Max(0, (int)Math.Floor((vis.Top - pad) / zoom));
                int x1 = Math.Min(baseImage.Width, (int)Math.Ceiling((vis.Right - pad) / zoom));
                int y1 = Math.Min(baseImage.Height, (int)Math.Ceiling((vis.Bottom - pad) / zoom));
                if (x1 <= x0 || y1 <= y0) return;
                var src = Rectangle.FromLTRB(x0, y0, x1, y1);
                var dst = new RectangleF(pad + x0 * zoom, pad + y0 * zoom, src.Width * zoom, src.Height * zoom);
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(baseImage, dst, src, GraphicsUnit.Pixel);
                g.PixelOffsetMode = PixelOffsetMode.Default;
            }
            else
            {
                if (zoomCache == null || zoomCacheZoom != zoom || zoomCacheSource != baseImage)
                {
                    if (zoomCache != null) zoomCache.Dispose();
                    int cw = Math.Max(1, (int)Math.Round(baseImage.Width * zoom)), ch = Math.Max(1, (int)Math.Round(baseImage.Height * zoom));
                    zoomCache = new Bitmap(cw, ch, PixelFormat.Format32bppPArgb);
                    using (var cg = Graphics.FromImage(zoomCache))
                    using (var ia = new ImageAttributes())
                    {
                        ia.SetWrapMode(WrapMode.TileFlipXY);
                        cg.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        cg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        cg.DrawImage(baseImage, new Rectangle(0, 0, cw, ch), 0, 0, baseImage.Width, baseImage.Height, GraphicsUnit.Pixel, ia);
                    }
                    zoomCacheZoom = zoom;
                    zoomCacheSource = baseImage;
                }
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.DrawImage(zoomCache, new RectangleF(img.X, img.Y, zoomCache.Width, zoomCache.Height));
            }
        }

        private void DrawCrop(Graphics g, RectangleF img)
        {
            var cr = ToScreen(new RectangleF(cropRect.X, cropRect.Y, cropRect.Width, cropRect.Height));
            using (var region = new Region(img))
            using (var b = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
            {
                region.Exclude(cr);
                g.FillRegion(b, region);
            }
            using (var p = new Pen(Color.White, 1) { DashStyle = DashStyle.Dash }) g.DrawRectangle(p, cr.X, cr.Y, cr.Width, cr.Height);
            string label = cropRect.Width + " × " + cropRect.Height;
            using (var f = Theme.UiFont(9f))
            {
                var sz = TextRenderer.MeasureText(label, f);
                var lr = new RectangleF(cr.X, cr.Y - sz.Height - 8 * ui, sz.Width + 14 * ui, sz.Height + 4 * ui);
                if (lr.Y < 0) lr.Y = cr.Y + 6 * ui;
                Theme.FillRounded(g, Color.FromArgb(230, 20, 21, 24), lr, lr.Height / 2);
                TextRenderer.DrawText(g, label, f, Rectangle.Round(lr), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        private void DrawSelection(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.None;
            if (!(selected is LineShape))
            {
                var b = ToScreen(selected.Bounds);
                b.Inflate(3, 3);
                using (var p = new Pen(Color.FromArgb(160, 255, 255, 255), 1)) g.DrawRectangle(p, b.X, b.Y, b.Width, b.Height);
                using (var p = new Pen(Theme.Accent, 1) { DashStyle = DashStyle.Dash }) g.DrawRectangle(p, b.X, b.Y, b.Width, b.Height);
            }
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float r = 5f * ui;
            foreach (var hp in selected.Handles)
            {
                var sp = ToScreen(hp);
                var hr = new RectangleF(sp.X - r, sp.Y - r, 2 * r, 2 * r);
                g.FillEllipse(Brushes.White, hr);
                using (var p = new Pen(Theme.Accent, 2f)) g.DrawEllipse(p, hr);
            }
        }

        // ------------------------------------------------------------------ mouse

        private Shape HitShape(PointF p)
        {
            float tol = 4f / zoom;
            for (int i = shapes.Count - 1; i >= 0; i--)
                if (shapes[i].HitTest(p, tol)) return shapes[i];
            return null;
        }

        private int HitHandle(Point screen)
        {
            if (selected == null) return -1;
            var hs = selected.Handles;
            float r = 7f * ui;
            for (int i = 0; i < hs.Length; i++)
            {
                var sp = ToScreen(hs[i]);
                if (Math.Abs(sp.X - screen.X) <= r && Math.Abs(sp.Y - screen.Y) <= r) return i;
            }
            return -1;
        }

        private bool ClickMovesExisting
        {
            get { return tool != Tool.Pen && tool != Tool.Highlighter && tool != Tool.Crop; }
        }

        private Cursor CursorForTool()
        {
            switch (tool)
            {
                case Tool.Select: return Cursors.Default;
                case Tool.Text: return Cursors.IBeam;
                default: return Cursors.Cross;
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            var p = ToImage(e.Location);

            if (e.Button == MouseButtons.Middle)
            {
                drag = DragMode.Pan;
                panMouse = Cursor.Position;
                panScroll = new Point(-host.AutoScrollPosition.X, -host.AutoScrollPosition.Y);
                Cursor = Cursors.SizeAll;
                return;
            }
            if (e.Button == MouseButtons.Right)
            {
                CommitTextEdit();
                var hit = HitShape(p);
                if (hit != null)
                {
                    Select(hit);
                    shapeMenu.Show(this, e.Location);
                }
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            if (editing != null)
            {
                CommitTextEdit();
                return;
            }

            if (e.Clicks == 2)
            {
                var t = HitShape(p) as TextShape;
                if (t != null)
                {
                    BeginTextEdit(t, false);
                    return;
                }
            }

            int hIndex = HitHandle(e.Location);
            if (hIndex >= 0)
            {
                BeginChange();
                selected.Normalize();
                drag = DragMode.Handle;
                handleIndex = hIndex;
                dragChanged = false;
                Capture = true;
                return;
            }

            var under = HitShape(p);
            if (under != null && (tool == Tool.Select || ClickMovesExisting))
            {
                if (tool == Tool.Text && under is TextShape)
                {
                    BeginTextEdit((TextShape)under, false);
                    return;
                }
                Select(under);
                BeginChange();
                drag = DragMode.Move;
                lastPoint = p;
                dragChanged = false;
                Capture = true;
                return;
            }

            if (tool == Tool.Select)
            {
                Select(null);
                return;
            }

            dragStart = p;
            Capture = true;
            switch (tool)
            {
                case Tool.Crop:
                    drag = DragMode.Crop;
                    cropRect = Rectangle.Empty;
                    return;
                case Tool.Text:
                {
                    var t = Styled(new TextShape { X = p.X, Y = p.Y, FontSize = Style.FontSize, Bold = Style.Bold });
                    using (var f = t.CreateFont()) t.Y -= f.Height * 0.5f;
                    Capture = false;
                    BeginTextEdit(t, true);
                    return;
                }
            }

            BeginChange();
            creating = NewShape(p);
            shapes.Add(creating);
            drag = DragMode.Create;
            Select(null);
            Invalidate();
        }

        private Shape NewShape(PointF p)
        {
            switch (tool)
            {
                case Tool.Pen:
                case Tool.Highlighter:
                {
                    var ps = Styled(new PenShape { Highlighter = tool == Tool.Highlighter });
                    ps.Points.Add(p);
                    return ps;
                }
                case Tool.Arrow:
                case Tool.Line:
                    return Styled(new LineShape { P1 = p, P2 = p, Arrow = tool == Tool.Arrow });
                case Tool.Rectangle:
                    return Styled(new RectShape { L = p.X, T = p.Y, R = p.X, B = p.Y });
                case Tool.Ellipse:
                    return Styled(new EllipseShape { L = p.X, T = p.Y, R = p.X, B = p.Y });
                case Tool.Blur:
                    return Styled(new BlurShape { L = p.X, T = p.Y, R = p.X, B = p.Y });
                case Tool.Sticker:
                    return Styled(new StickerShape { Sticker = Style.Sticker, L = p.X, T = p.Y, R = p.X, B = p.Y });
                default:
                    return Styled(new StepShape { Number = NextStepNumber(), L = p.X, T = p.Y, R = p.X, B = p.Y });
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var p = ToImage(e.Location);
            bool shift = (ModifierKeys & Keys.Shift) != 0;

            switch (drag)
            {
                case DragMode.None:
                {
                    int hIndex = HitHandle(e.Location);
                    if (hIndex >= 0) Cursor = selected.HandleCursor(hIndex);
                    else if ((tool == Tool.Select || ClickMovesExisting) && HitShape(p) != null) Cursor = Cursors.SizeAll;
                    else Cursor = CursorForTool();
                    return;
                }
                case DragMode.Pan:
                {
                    var c = Cursor.Position;
                    host.AutoScrollPosition = new Point(panScroll.X - (c.X - panMouse.X), panScroll.Y - (c.Y - panMouse.Y));
                    host.CenterCanvas();
                    return;
                }
                case DragMode.Move:
                {
                    float dx = p.X - lastPoint.X, dy = p.Y - lastPoint.Y;
                    if (dx != 0 || dy != 0)
                    {
                        selected.Offset(dx, dy);
                        lastPoint = p;
                        dragChanged = true;
                    }
                    break;
                }
                case DragMode.Handle:
                    selected.MoveHandle(handleIndex, p, shift);
                    dragChanged = true;
                    break;
                case DragMode.Crop:
                {
                    var r = Rectangle.FromLTRB(
                        (int)Math.Round(Math.Min(dragStart.X, p.X)), (int)Math.Round(Math.Min(dragStart.Y, p.Y)),
                        (int)Math.Round(Math.Max(dragStart.X, p.X)), (int)Math.Round(Math.Max(dragStart.Y, p.Y)));
                    r.Intersect(new Rectangle(0, 0, baseImage.Width, baseImage.Height));
                    cropRect = r;
                    break;
                }
                case DragMode.Create:
                {
                    var ps = creating as PenShape;
                    var ls = creating as LineShape;
                    var bs = creating as BoxShape;
                    if (ps != null)
                    {
                        var last = ps.Points[ps.Points.Count - 1];
                        float ddx = p.X - last.X, ddy = p.Y - last.Y;
                        if (ddx * ddx + ddy * ddy >= 1.5f / (zoom * zoom)) ps.Points.Add(p);
                    }
                    else if (ls != null)
                    {
                        ls.P2 = shift ? Shape.SnapAngle(ls.P1, p) : p;
                    }
                    else if (bs != null)
                    {
                        var q = (shift || bs.LockAspect) ? Shape.ConstrainAspect(dragStart, p, bs.Aspect) : p;
                        bs.L = dragStart.X;
                        bs.T = dragStart.Y;
                        bs.R = q.X;
                        bs.B = q.Y;
                    }
                    break;
                }
            }
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            var mode = drag;
            drag = DragMode.None;
            Capture = false;
            switch (mode)
            {
                case DragMode.Pan:
                    Cursor = CursorForTool();
                    break;
                case DragMode.Move:
                case DragMode.Handle:
                    if (selected != null) selected.Normalize();
                    if (dragChanged) CommitChange();
                    else CancelChange();
                    break;
                case DragMode.Crop:
                    if (cropRect.Width >= 2 && cropRect.Height >= 2) ApplyCrop(cropRect);
                    cropRect = Rectangle.Empty;
                    break;
                case DragMode.Create:
                {
                    var c = creating;
                    creating = null;
                    c.Normalize();
                    var box = c as BoxShape;
                    if ((c is StickerShape || c is StepShape) && c.IsDegenerate)
                    {
                        float d = c is StepShape ? StepDiameter : (float)Math.Round(64 * ui);
                        box.SetRect(new RectangleF(dragStart.X - d / 2, dragStart.Y - d / 2, d, d));
                    }
                    if (c.IsDegenerate)
                    {
                        shapes.Remove(c);
                        CancelChange();
                    }
                    else
                    {
                        CommitChange();
                        if (!(c is PenShape)) Select(c);
                    }
                    break;
                }
            }
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            var he = e as HandledMouseEventArgs;
            if ((ModifierKeys & Keys.Control) != 0)
            {
                ZoomStep(e.Delta > 0 ? 1 : -1, e.Location);
                if (he != null) he.Handled = true;
                return;
            }
            if ((ModifierKeys & Keys.Shift) != 0)
            {
                host.AutoScrollPosition = new Point(-host.AutoScrollPosition.X - e.Delta, -host.AutoScrollPosition.Y);
                host.CenterCanvas();
                if (he != null) he.Handled = true;
                return;
            }
            base.OnMouseWheel(e);
        }

        protected override void OnDragEnter(DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Bitmap) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            try
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null)
                {
                    foreach (var f in files)
                        if (Library.IsImage(f)) { AddImage(ImageIO.Load(f)); return; }
                }
                var bmp = e.Data.GetData(DataFormats.Bitmap) as Bitmap;
                if (bmp != null) AddImage(new Bitmap(bmp));
            }
            catch (Exception)
            {
            }
        }

        // ------------------------------------------------------------------ inline text editing

        private void BeginTextEdit(TextShape t, bool isNew)
        {
            CommitTextEdit();
            BeginChange();
            if (isNew) shapes.Add(t);
            editing = t;
            editingIsNew = isNew;
            editDirty = false;
            editOriginal = t.Text;
            selected = null;
            SyncStyleFrom(t);
            var h = SelectionChanged;
            if (h != null) h(this, EventArgs.Empty);

            editBox = new TextBox
            {
                Multiline = true,
                AcceptsReturn = true,
                AcceptsTab = false,
                WordWrap = false,
                BorderStyle = BorderStyle.None,
                ScrollBars = ScrollBars.None,
                Text = t.Text
            };
            Controls.Add(editBox);
            Native.SendMessage(editBox.Handle, Native.EM_SETMARGINS, (IntPtr)3, IntPtr.Zero);
            StyleEditBox();
            editBox.TextChanged += (s, e) => LayoutEditBox();
            editBox.KeyDown += EditBoxKeyDown;
            editBox.LostFocus += (s, e) =>
            {
                if (!IsDisposed) BeginInvoke((Action)CommitTextEdit);
            };
            editBox.Focus();
            if (isNew) editBox.SelectionStart = editBox.TextLength;
            else editBox.SelectAll();
            Invalidate();
        }

        private void StyleEditBox()
        {
            if (editBox == null || editing == null) return;
            var old = editBox.Font;
            editBox.Font = new Font(TextShape.FontName, Math.Max(4f, editing.FontSize * zoom), editing.Bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
            if (old != null && old != Control.DefaultFont) old.Dispose();
            editBox.ForeColor = Color.FromArgb(255, editing.Stroke);
            if (editing.Fill.A > 0) editBox.BackColor = Color.FromArgb(255, editing.Fill);
            else editBox.BackColor = Shape.ContrastText(editing.Stroke) == Color.White ? Color.White : Color.FromArgb(40, 42, 48);
            LayoutEditBox();
        }

        private void LayoutEditBox()
        {
            if (editBox == null || editing == null) return;
            string t = editBox.Text.Length == 0 ? "W" : editBox.Text;
            if (t.EndsWith("\n")) t += "W";
            var sz = TextRenderer.MeasureText(t + "W", editBox.Font, Size.Empty, TextFormatFlags.NoPadding);
            var loc = ToScreen(new PointF(editing.X, editing.Y));
            editBox.Bounds = new Rectangle((int)Math.Round(loc.X), (int)Math.Round(loc.Y), sz.Width + 4, sz.Height + 4);
        }

        private void EditBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape || (e.KeyCode == Keys.Enter && e.Control))
            {
                e.SuppressKeyPress = true;
                BeginInvoke((Action)CommitTextEdit);
                BeginInvoke((Action)(() => Focus()));
            }
        }

        public void CommitTextEdit()
        {
            if (editing == null) return;
            var t = editing;
            var box = editBox;
            editing = null;
            editBox = null;
            t.Text = box.Text.Replace("\r\n", "\n").Replace("\n", "\r\n");
            Controls.Remove(box);
            box.Dispose();

            if (string.IsNullOrWhiteSpace(t.Text))
            {
                shapes.Remove(t);
                if (editingIsNew) CancelChange();
                else CommitChange();
                Select(null);
            }
            else
            {
                if (editingIsNew || editDirty || t.Text != editOriginal) CommitChange();
                else CancelChange();
                Select(t);
            }
            Invalidate();
        }
    }
}

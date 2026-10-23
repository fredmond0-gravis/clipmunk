using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Clipmunk
{
    /// <summary>
    /// Low-level keyboard hook that swallows the Print Screen key. It runs on its own thread so a busy
    /// UI thread can never make Windows drop the hook. Alt+PrtScn and Win+PrtScn pass through.
    /// </summary>
    internal sealed class PrintScreenHook : IDisposable
    {
        private readonly Action onPress;
        private Native.LowLevelKeyboardProc proc;
        private IntPtr hook;
        private Thread thread;
        private uint threadId;
        private bool keyDown;

        public PrintScreenHook(Action onPress)
        {
            this.onPress = onPress;
        }

        public bool Start()
        {
            var ready = new ManualResetEvent(false);
            thread = new Thread(() =>
            {
                proc = HookProc;
                hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, proc, Native.GetModuleHandle(null), 0);
                threadId = Native.GetCurrentThreadId();
                ready.Set();
                if (hook == IntPtr.Zero) return;
                Native.MSG msg;
                while (Native.GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
                {
                }
                Native.UnhookWindowsHookEx(hook);
            });
            thread.IsBackground = true;
            thread.Name = "Clipmunk keyboard hook";
            thread.Start();
            ready.WaitOne();
            return hook != IntPtr.Zero;
        }

        private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var k = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
                if (Program.Debug) Program.Log("key vk=0x" + k.vkCode.ToString("X2") + " msg=0x" + wParam.ToInt32().ToString("X") + " flags=0x" + k.flags.ToString("X"));
                if (k.vkCode == Native.VK_SNAPSHOT)
                {
                    bool alt = (Native.GetAsyncKeyState(Native.VK_MENU) & 0x8000) != 0;
                    bool win = (Native.GetAsyncKeyState(Native.VK_LWIN) & 0x8000) != 0 || (Native.GetAsyncKeyState(Native.VK_RWIN) & 0x8000) != 0;
                    if (!alt && !win)
                    {
                        int msg = wParam.ToInt32();
                        bool down = msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN;
                        if (down)
                        {
                            if (!keyDown) onPress();
                            keyDown = true;
                        }
                        else
                        {
                            if (!keyDown) onPress(); // some keyboards only report key-up
                            keyDown = false;
                        }
                        return (IntPtr)1;
                    }
                }
            }
            return Native.CallNextHookEx(hook, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (threadId != 0) Native.PostThreadMessage(threadId, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }
    }

    internal static class ScreenCapture
    {
        public static Bitmap CaptureVirtualScreen(out Rectangle vs)
        {
            vs = SystemInformation.VirtualScreen;
            var bmp = new Bitmap(vs.Width, vs.Height, PixelFormat.Format32bppRgb);
            using (var g = Graphics.FromImage(bmp))
                g.CopyFromScreen(vs.Left, vs.Top, 0, 0, vs.Size, CopyPixelOperation.SourceCopy);
            return bmp;
        }

        /// <summary>Visible top-level window rectangles (screen coords), topmost first.</summary>
        public static List<Rectangle> VisibleWindows()
        {
            var list = new List<Rectangle>();
            var cls = new StringBuilder(256);
            Native.EnumWindows((h, l) =>
            {
                if (!Native.IsWindowVisible(h) || Native.IsIconic(h)) return true;
                int ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
                if ((ex & Native.WS_EX_TRANSPARENT) != 0) return true;
                int cloaked;
                if (Native.DwmGetWindowAttribute(h, Native.DWMWA_CLOAKED, out cloaked, 4) == 0 && cloaked != 0) return true;
                cls.Length = 0;
                Native.GetClassName(h, cls, cls.Capacity);
                string c = cls.ToString();
                if (c == "Progman" || c == "WorkerW") return true;
                Native.RECT r;
                if (Native.DwmGetWindowAttribute(h, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf(typeof(Native.RECT))) != 0)
                    Native.GetWindowRect(h, out r);
                var rect = r.ToRectangle();
                if (rect.Width >= 8 && rect.Height >= 8) list.Add(rect);
                return true;
            }, IntPtr.Zero);
            return list;
        }
    }

    /// <summary>
    /// Full-screen frozen overlay. Drag to select, click to grab the window under the cursor, arrows nudge
    /// one pixel, Esc/right-click cancels. A magnifier shows the exact pixel under the crosshair.
    /// </summary>
    internal sealed class CaptureOverlay : Form
    {
        private readonly Bitmap shot;
        private readonly Rectangle vs;
        private readonly List<Rectangle> windows = new List<Rectangle>();
        private IntPtr origDC, origBmp, origOld, dimDC, dimBmp, dimOld;
        private BufferedGraphicsContext bufferContext;
        private BufferedGraphics buffer;
        private Graphics bufferTarget;
        private Rectangle[] paintRects;
        private List<Rectangle> lastOverlay = new List<Rectangle>();
        private Point cursor;
        private bool dragging, moved;
        private Point dragStart;
        private Rectangle selection, hover;
        private readonly Font labelFont, smallFont;

        public Bitmap Result { get; private set; }

        public CaptureOverlay(Bitmap shot, Rectangle virtualScreen, List<Rectangle> windowRects)
        {
            this.shot = shot;
            vs = virtualScreen;
            var client = new Rectangle(Point.Empty, vs.Size);
            foreach (var r in windowRects)
            {
                var c = r;
                c.Offset(-vs.X, -vs.Y);
                c.Intersect(client);
                if (!c.IsEmpty) windows.Add(c);
            }

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            Bounds = vs;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            Cursor = Cursors.Cross;
            Text = "Clipmunk capture";
            BackColor = Color.Black;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque, true);

            float s = Native.DpiScaleAt(Cursor.Position);
            labelFont = new Font("Segoe UI Semibold", 12.5f * s, FontStyle.Regular, GraphicsUnit.Pixel);
            smallFont = new Font("Segoe UI", 11.5f * s, FontStyle.Regular, GraphicsUnit.Pixel);

            PrepareSurfaces();
            cursor = new Point(Cursor.Position.X - vs.X, Cursor.Position.Y - vs.Y);
            hover = FindHover(cursor);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        private void PrepareSurfaces()
        {
            IntPtr screen = Native.GetDC(IntPtr.Zero);
            origDC = Native.CreateCompatibleDC(screen);
            origBmp = shot.GetHbitmap();
            origOld = Native.SelectObject(origDC, origBmp);
            dimDC = Native.CreateCompatibleDC(screen);
            dimBmp = shot.GetHbitmap();
            dimOld = Native.SelectObject(dimDC, dimBmp);
            Native.ReleaseDC(IntPtr.Zero, screen);
            using (var g = Graphics.FromHdc(dimDC))
            using (var b = new SolidBrush(Color.FromArgb(125, 8, 9, 12)))
                g.FillRectangle(b, 0, 0, vs.Width, vs.Height);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            bufferContext = new BufferedGraphicsContext();
            bufferContext.MaximumBuffer = new Size(vs.Width + 1, vs.Height + 1);
            bufferTarget = CreateGraphics();
            buffer = bufferContext.Allocate(bufferTarget, new Rectangle(Point.Empty, vs.Size));
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Bounds = vs;
            if (Program.Debug) Program.Log("overlay shown at " + Bounds);
            Native.ForceForeground(Handle);
            Activate();
            UpdateOverlay();
        }

        // ------------------------------------------------------------------ input

        private Rectangle ActiveRect { get { return dragging && moved ? selection : hover; } }

        private Rectangle MonitorAt(Point clientPt)
        {
            var b = Screen.FromPoint(new Point(clientPt.X + vs.X, clientPt.Y + vs.Y)).Bounds;
            b.Offset(-vs.X, -vs.Y);
            return b;
        }

        private Rectangle FindHover(Point p)
        {
            foreach (var w in windows)
                if (w.Contains(p)) return w;
            return MonitorAt(p);
        }

        private Rectangle FromCorners(Point a, Point b)
        {
            var r = Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X) + 1, Math.Max(a.Y, b.Y) + 1);
            r.Intersect(new Rectangle(Point.Empty, vs.Size));
            return r;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                dragging = true;
                moved = false;
                dragStart = e.Location;
                selection = Rectangle.Empty;
            }
            else if (e.Button == MouseButtons.Right)
            {
                if (dragging)
                {
                    dragging = false;
                    moved = false;
                    UpdateOverlay();
                }
                else
                {
                    DialogResult = DialogResult.Cancel;
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            cursor = e.Location;
            if (dragging)
            {
                if (!moved && (Math.Abs(e.X - dragStart.X) > 3 || Math.Abs(e.Y - dragStart.Y) > 3)) moved = true;
                if (moved) selection = FromCorners(dragStart, cursor);
            }
            else
            {
                hover = FindHover(cursor);
            }
            UpdateOverlay();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !dragging) return;
            dragging = false;
            Finish(moved ? selection : hover);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            int step = e.Shift ? 10 : 1;
            switch (e.KeyCode)
            {
                case Keys.Escape:
                    DialogResult = DialogResult.Cancel;
                    break;
                case Keys.Enter:
                    Finish(ActiveRect);
                    break;
                case Keys.Left: Nudge(-step, 0); break;
                case Keys.Right: Nudge(step, 0); break;
                case Keys.Up: Nudge(0, -step); break;
                case Keys.Down: Nudge(0, step); break;
            }
        }

        private static void Nudge(int dx, int dy)
        {
            var p = Cursor.Position;
            Cursor.Position = new Point(p.X + dx, p.Y + dy);
        }

        private void Finish(Rectangle r)
        {
            if (r.Width < 1 || r.Height < 1)
            {
                DialogResult = DialogResult.Cancel;
                return;
            }
            Result = shot.Clone(r, PixelFormat.Format24bppRgb);
            Result.SetResolution(96, 96);
            DialogResult = DialogResult.OK;
        }

        // ------------------------------------------------------------------ painting

        private void UpdateOverlay()
        {
            var now = OverlayRects();
            foreach (var r in lastOverlay) Invalidate(r);
            foreach (var r in now) Invalidate(r);
            lastOverlay = now;
        }

        private float UiScale { get { return Native.DpiScaleAt(new Point(cursor.X + vs.X, cursor.Y + vs.Y)); } }

        private List<Rectangle> OverlayRects()
        {
            var l = new List<Rectangle>();
            l.Add(new Rectangle(cursor.X - 1, 0, 3, vs.Height));
            l.Add(new Rectangle(0, cursor.Y - 1, vs.Width, 3));
            var a = ActiveRect;
            if (!a.IsEmpty)
            {
                var b = a;
                b.Inflate(4, 4);
                l.Add(b);
                var lr = LabelRect(a);
                lr.Inflate(2, 2);
                l.Add(lr);
            }
            var m = MagnifierBounds();
            m.Inflate(4, 4);
            l.Add(m);
            var h = HintRect();
            h.Inflate(2, 2);
            l.Add(h);
            return l;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_PAINT && IsHandleCreated)
            {
                IntPtr rgn = Native.CreateRectRgn(0, 0, 0, 0);
                if (Native.GetUpdateRgn(Handle, rgn, false) > 1)
                {
                    using (var region = Region.FromHrgn(rgn))
                    using (var mx = new Matrix())
                        paintRects = Array.ConvertAll(region.GetRegionScans(mx), new Converter<RectangleF, Rectangle>(Rectangle.Round));
                }
                Native.DeleteObject(rgn);
            }
            base.WndProc(ref m);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var rects = paintRects ?? new[] { e.ClipRectangle };
            paintRects = null;
            RenderFrame(buffer.Graphics, rects);
            buffer.Render(e.Graphics);
        }

        private void RenderFrame(Graphics g, Rectangle[] rects)
        {
            var active = ActiveRect;
            IntPtr hdc = g.GetHdc();
            try
            {
                foreach (var r in rects)
                {
                    Native.BitBlt(hdc, r.X, r.Y, r.Width, r.Height, dimDC, r.X, r.Y, Native.SRCCOPY);
                    if (!active.IsEmpty)
                    {
                        var a = Rectangle.Intersect(r, active);
                        if (!a.IsEmpty) Native.BitBlt(hdc, a.X, a.Y, a.Width, a.Height, origDC, a.X, a.Y, Native.SRCCOPY);
                    }
                }
            }
            finally
            {
                g.ReleaseHdc(hdc);
            }
            using (var clip = new Region())
            {
                clip.MakeEmpty();
                foreach (var r in rects) clip.Union(r);
                g.Clip = clip;
                DrawOverlays(g);
                g.ResetClip();
            }
        }

        private void DrawOverlays(Graphics g)
        {
            float s = UiScale;
            var active = ActiveRect;
            g.SmoothingMode = SmoothingMode.None;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            // crosshair, with a gap around the cursor so the pixel itself stays visible
            int gap = (int)(10 * s);
            using (var b = new SolidBrush(Color.FromArgb(150, Theme.Accent)))
            {
                g.FillRectangle(b, cursor.X, 0, 1, Math.Max(0, cursor.Y - gap));
                g.FillRectangle(b, cursor.X, cursor.Y + gap + 1, 1, Math.Max(0, vs.Height - cursor.Y - gap));
                g.FillRectangle(b, 0, cursor.Y, Math.Max(0, cursor.X - gap), 1);
                g.FillRectangle(b, cursor.X + gap + 1, cursor.Y, Math.Max(0, vs.Width - cursor.X - gap), 1);
            }

            if (!active.IsEmpty)
            {
                using (var b = new SolidBrush(Theme.Accent))
                {
                    g.FillRectangle(b, active.X - 2, active.Y - 2, active.Width + 4, 2);
                    g.FillRectangle(b, active.X - 2, active.Bottom, active.Width + 4, 2);
                    g.FillRectangle(b, active.X - 2, active.Y, 2, active.Height);
                    g.FillRectangle(b, active.Right, active.Y, 2, active.Height);
                }
                var lr = LabelRect(active);
                Pill(g, lr, Color.FromArgb(235, 20, 21, 24));
                TextRenderer.DrawText(g, active.Width + " × " + active.Height, labelFont, lr, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            DrawMagnifier(g, s);

            var hr = HintRect();
            Pill(g, hr, Color.FromArgb(225, 20, 21, 24));
            TextRenderer.DrawText(g, HintText, smallFont, hr, Color.FromArgb(225, 228, 233),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        private const string HintText = "Drag to select   ·   Click a window   ·   Arrows nudge 1px   ·   Enter to grab   ·   Esc to cancel";

        private static void Pill(Graphics g, Rectangle r, Color c)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.RoundedRect(new RectangleF(r.X, r.Y, r.Width, r.Height), r.Height / 2f))
            using (var b = new SolidBrush(c))
                g.FillPath(b, path);
            g.SmoothingMode = SmoothingMode.None;
        }

        private Rectangle LabelRect(Rectangle a)
        {
            float s = UiScale;
            var sz = TextRenderer.MeasureText(a.Width + " × " + a.Height, labelFont);
            int w = sz.Width + (int)(18 * s), h = (int)(24 * s);
            int x = a.X, y = a.Y - h - (int)(8 * s);
            var mon = MonitorAt(new Point(a.X + 1, a.Y + 1));
            if (y < mon.Top) y = a.Y + (int)(8 * s);
            if (x + w > mon.Right) x = mon.Right - w;
            if (x < mon.Left) x = mon.Left;
            return new Rectangle(x, y, w, h);
        }

        private Rectangle HintRect()
        {
            float s = UiScale;
            var mon = MonitorAt(cursor);
            var sz = TextRenderer.MeasureText(HintText, smallFont);
            int w = sz.Width + (int)(32 * s), h = (int)(30 * s);
            return new Rectangle(mon.X + (mon.Width - w) / 2, mon.Y + (int)(20 * s), w, h);
        }

        private const int Cells = 15;

        private void LayoutMagnifier(out Rectangle box, out Rectangle info, out int cell)
        {
            float s = UiScale;
            cell = Math.Max(6, (int)Math.Round(8 * s));
            int size = Cells * cell;
            int infoH = (int)(46 * s);
            int gap = (int)(4 * s);
            var mon = MonitorAt(cursor);
            int off = (int)(24 * s);
            int x = cursor.X + off, y = cursor.Y + off;
            if (x + size > mon.Right) x = cursor.X - off - size;
            if (y + size + gap + infoH > mon.Bottom) y = cursor.Y - off - size - gap - infoH;
            box = new Rectangle(x, y, size, size);
            info = new Rectangle(x, y + size + gap, size, infoH);
        }

        private Rectangle MagnifierBounds()
        {
            Rectangle box, info;
            int cell;
            LayoutMagnifier(out box, out info, out cell);
            return Rectangle.Union(box, info);
        }

        private void DrawMagnifier(Graphics g, float s)
        {
            Rectangle box, info;
            int cell;
            LayoutMagnifier(out box, out info, out cell);
            float radius = 10 * s;

            var oldClip = g.Clip;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var shadow = Theme.RoundedRect(new RectangleF(box.X - 1, box.Y + 2, box.Width + 2, box.Height + 2), radius))
            using (var sb = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
                g.FillPath(sb, shadow);
            using (var path = Theme.RoundedRect(new RectangleF(box.X, box.Y, box.Width, box.Height), radius))
            {
                g.SetClip(path, CombineMode.Intersect);
                g.FillRectangle(Brushes.Black, box);
                var src = new Rectangle(cursor.X - Cells / 2, cursor.Y - Cells / 2, Cells, Cells);
                var inter = Rectangle.Intersect(src, new Rectangle(0, 0, shot.Width, shot.Height));
                if (!inter.IsEmpty)
                {
                    var dst = new Rectangle(box.X + (inter.X - src.X) * cell, box.Y + (inter.Y - src.Y) * cell, inter.Width * cell, inter.Height * cell);
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(shot, dst, inter, GraphicsUnit.Pixel);
                    g.PixelOffsetMode = PixelOffsetMode.Default;
                }
                g.SmoothingMode = SmoothingMode.None;
                using (var gp = new Pen(Color.FromArgb(36, 255, 255, 255)))
                {
                    for (int i = 1; i < Cells; i++)
                    {
                        g.DrawLine(gp, box.X + i * cell, box.Y, box.X + i * cell, box.Bottom);
                        g.DrawLine(gp, box.X, box.Y + i * cell, box.Right, box.Y + i * cell);
                    }
                }
                int c0 = Cells / 2 * cell;
                using (var band = new SolidBrush(Color.FromArgb(40, Theme.Accent)))
                {
                    g.FillRectangle(band, box.X, box.Y + c0, box.Width, cell);
                    g.FillRectangle(band, box.X + c0, box.Y, cell, box.Height);
                }
                var cr = new Rectangle(box.X + c0, box.Y + c0, cell, cell);
                g.DrawRectangle(Pens.Black, cr.X - 1, cr.Y - 1, cr.Width + 1, cr.Height + 1);
                g.DrawRectangle(Pens.White, cr.X, cr.Y, cr.Width - 1, cr.Height - 1);
                g.Clip = oldClip;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var bp = new Pen(Color.FromArgb(200, Theme.Accent), 1.5f)) g.DrawPath(bp, path);
            }

            // info card
            using (var path = Theme.RoundedRect(new RectangleF(info.X, info.Y, info.Width, info.Height), radius))
            using (var b = new SolidBrush(Color.FromArgb(240, 22, 23, 27)))
                g.FillPath(b, path);
            g.SmoothingMode = SmoothingMode.None;

            Color px = Color.Black;
            if (cursor.X >= 0 && cursor.Y >= 0 && cursor.X < shot.Width && cursor.Y < shot.Height) px = shot.GetPixel(cursor.X, cursor.Y);
            int pad = (int)(10 * s);
            int line = info.Height / 2;
            var xy = new Rectangle(info.X + pad, info.Y + (int)(3 * s), info.Width - 2 * pad, line);
            string pos = (cursor.X + vs.X) + ", " + (cursor.Y + vs.Y);
            if (dragging && moved) pos = selection.Width + " × " + selection.Height;
            TextRenderer.DrawText(g, pos, smallFont, xy, Color.White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            int sw = (int)(12 * s);
            var swatch = new Rectangle(info.X + pad, info.Y + line + (line - sw) / 2 - (int)(2 * s), sw, sw);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(Color.FromArgb(255, px))) g.FillEllipse(b, swatch);
            using (var p = new Pen(Color.FromArgb(120, 255, 255, 255))) g.DrawEllipse(p, swatch);
            g.SmoothingMode = SmoothingMode.None;
            var hex = new Rectangle(swatch.Right + (int)(6 * s), info.Y + line - (int)(2 * s), info.Width, line);
            TextRenderer.DrawText(g, string.Format("#{0:X2}{1:X2}{2:X2}", px.R, px.G, px.B), smallFont, hex,
                Theme.SubText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        /// <summary>Renders one frame to a bitmap (used by the self-test).</summary>
        public void RenderPreview(Bitmap target, Point at, Rectangle sel)
        {
            cursor = at;
            if (!sel.IsEmpty)
            {
                dragging = true;
                moved = true;
                selection = sel;
            }
            else
            {
                hover = FindHover(at);
            }
            using (var g = Graphics.FromImage(target)) RenderFrame(g, new[] { new Rectangle(Point.Empty, vs.Size) });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (buffer != null) buffer.Dispose();
                if (bufferContext != null) bufferContext.Dispose();
                if (bufferTarget != null) bufferTarget.Dispose();
                labelFont.Dispose();
                smallFont.Dispose();
            }
            if (origDC != IntPtr.Zero)
            {
                Native.SelectObject(origDC, origOld);
                Native.DeleteObject(origBmp);
                Native.DeleteDC(origDC);
                origDC = IntPtr.Zero;
            }
            if (dimDC != IntPtr.Zero)
            {
                Native.SelectObject(dimDC, dimOld);
                Native.DeleteObject(dimBmp);
                Native.DeleteDC(dimDC);
                dimDC = IntPtr.Zero;
            }
            base.Dispose(disposing);
        }
    }
}

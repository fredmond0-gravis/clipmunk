using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace Clipmunk
{
    internal sealed class LibraryItem
    {
        public string Path;
        public DateTime Time;
        public DateTime Modified;
        public Bitmap Thumb;
        public Size ImageSize;
        public bool Editable;
        public Rectangle Bounds;
    }

    internal sealed class LibraryForm : Form
    {
        private readonly ThumbGrid grid;
        private readonly ToolStrip header;
        private readonly ToolStripTextBox search;
        private readonly StatusStrip status;
        private readonly ToolStripStatusLabel countLabel, folderLabel, messageLabel;
        private readonly System.Windows.Forms.Timer refreshTimer, messageTimer;
        private FileSystemWatcher watcher;
        private readonly Dictionary<string, LibraryItem> cache = new Dictionary<string, LibraryItem>(StringComparer.OrdinalIgnoreCase);

        public LibraryForm()
        {
            Text = "Clipmunk Library";
            Icon = Art.AppIcon();
            BackColor = Theme.Window;
            ForeColor = Theme.Text;
            Font = Theme.UiFont(9f);
            KeyPreview = true;
            int icon = Theme.S(20);
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            int w = Math.Min(Theme.S(1180), (int)(wa.Width * 0.85)), h = Math.Min(Theme.S(780), (int)(wa.Height * 0.85));
            StartPosition = FormStartPosition.Manual;
            Bounds = new Rectangle(wa.X + (wa.Width - w) / 2, wa.Y + (wa.Height - h) / 2, w, h);
            MinimumSize = new Size(Theme.S(560), Theme.S(380));

            header = new ToolStrip { Dock = DockStyle.Top, ImageScalingSize = new Size(icon, icon), Padding = new Padding(Theme.S(14), Theme.S(10), Theme.S(14), Theme.S(10)) };
            Theme.Apply(header);
            var title = new ToolStripLabel("Library") { Font = Theme.UiFont(14f, FontStyle.Bold), ForeColor = Theme.Text, Image = Art.DrawAppIcon(Theme.S(28)), ImageScaling = ToolStripItemImageScaling.None, Margin = new Padding(0, 0, Theme.S(12), 0) };
            header.Items.Add(title);

            var capture = new ToolStripButton("  Capture  ", Art.Glyph("capture", icon, Color.FromArgb(25, 18, 8)), (s, e) => TrayContext.StartCapture(null))
            {
                Alignment = ToolStripItemAlignment.Right, Tag = "accent", Font = Theme.UiFont(9f, FontStyle.Bold), ToolTipText = "New capture (Print Screen)", Margin = new Padding(Theme.S(6), 0, 0, 0)
            };
            var folderMenu = new ToolStripDropDownButton(Art.Glyph("folder", icon)) { Alignment = ToolStripItemAlignment.Right, ToolTipText = "Library folder", ShowDropDownArrow = false };
            Theme.Apply(folderMenu.DropDown);
            folderMenu.DropDownItems.Add("Open folder in Explorer", null, (s, e) => OpenFolder());
            folderMenu.DropDownItems.Add("Change library folder…", null, (s, e) => TrayContext.ChooseLibraryFolder(this));
            if (GoogleDrive.MyDrive != null)
                folderMenu.DropDownItems.Add("Use Google Drive (" + Path.Combine(GoogleDrive.MyDrive, "Clipmunk") + ")", Art.Glyph("drive", icon), (s, e) => Library.Folder = Path.Combine(GoogleDrive.MyDrive, "Clipmunk"));
            search = new ToolStripTextBox { Alignment = ToolStripItemAlignment.Right, BorderStyle = BorderStyle.None, BackColor = Theme.Card, ForeColor = Theme.Text, AutoSize = false, Width = Theme.S(200), ToolTipText = "Search by name or date (e.g. 2026-10 or Oct)" };
            search.TextChanged += (s, e) => ApplyFilter();
            var searchIcon = new ToolStripLabel(Art.Glyph("search", icon, Theme.SubText)) { Alignment = ToolStripItemAlignment.Right };
            header.Items.Add(capture);
            header.Items.Add(folderMenu);
            header.Items.Add(search);
            header.Items.Add(searchIcon);

            grid = new ThumbGrid { Dock = DockStyle.Fill };
            grid.OpenRequested += item => EditorForm.OpenDocument(item.Path);
            grid.DeleteRequested += DeleteItems;
            grid.CopyRequested += CopyItem;
            grid.RevealRequested += item => Reveal(item.Path);

            status = new StatusStrip { SizingGrip = false, Padding = new Padding(Theme.S(14), 2, Theme.S(14), 2) };
            Theme.Apply(status);
            countLabel = new ToolStripStatusLabel { ForeColor = Theme.Text };
            folderLabel = new ToolStripStatusLabel { ForeColor = Theme.SubText, IsLink = true, LinkColor = Theme.SubText, ActiveLinkColor = Theme.Accent, LinkBehavior = LinkBehavior.HoverUnderline, Margin = new Padding(Theme.S(10), 0, 0, 0) };
            folderLabel.Click += (s, e) => OpenFolder();
            messageLabel = new ToolStripStatusLabel { Spring = true, ForeColor = Theme.Accent, TextAlign = ContentAlignment.MiddleRight };
            status.Items.AddRange(new ToolStripItem[] { countLabel, folderLabel, messageLabel });

            Controls.Add(grid);
            Controls.Add(header);
            Controls.Add(status);

            refreshTimer = new System.Windows.Forms.Timer { Interval = 600 };
            refreshTimer.Tick += (s, e) => { refreshTimer.Stop(); Reload(); };
            messageTimer = new System.Windows.Forms.Timer { Interval = 2600 };
            messageTimer.Tick += (s, e) => { messageTimer.Stop(); messageLabel.Text = ""; };

            Library.FolderChanged += OnFolderChanged;
            WatchFolder();
            Reload();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.UseDarkTitleBar(Handle, Theme.Chrome);
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            ScheduleReload();
        }

        private void OnFolderChanged(object sender, EventArgs e)
        {
            if (IsDisposed) return;
            cache.Clear();
            WatchFolder();
            Reload();
        }

        private void WatchFolder()
        {
            if (watcher != null)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
                watcher = null;
            }
            string folder = Library.Folder;
            bool drive = GoogleDrive.Contains(folder);
            folderLabel.Text = (drive ? "☁ Google Drive  ·  " : "") + folder;
            folderLabel.ToolTipText = drive ? "Synced by Google Drive — your screenshots show up on every computer signed in to Drive." : "Click to open in Explorer";
            try
            {
                Directory.CreateDirectory(folder);
                watcher = new FileSystemWatcher(folder) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
                FileSystemEventHandler h = (s, e) => ScheduleReloadFromAnyThread();
                watcher.Created += h;
                watcher.Changed += h;
                watcher.Deleted += h;
                watcher.Renamed += (s, e) => ScheduleReloadFromAnyThread();
                watcher.EnableRaisingEvents = true;
            }
            catch (Exception)
            {
                // Network/virtual drives may not support change notifications; we still refresh on focus.
            }
        }

        private void ScheduleReloadFromAnyThread()
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke((Action)ScheduleReload); }
            catch (InvalidOperationException) { }
        }

        private void ScheduleReload()
        {
            refreshTimer.Stop();
            refreshTimer.Start();
        }

        private List<LibraryItem> all = new List<LibraryItem>();

        public void Reload()
        {
            string folder = Library.Folder;
            var items = new List<LibraryItem>();
            try
            {
                if (Directory.Exists(folder))
                {
                    string side = Path.Combine(folder, Library.SidecarFolderName);
                    foreach (var f in Directory.GetFiles(folder))
                    {
                        if (!Library.IsImage(f)) continue;
                        DateTime mod;
                        try { mod = File.GetLastWriteTimeUtc(f); }
                        catch (Exception) { continue; }
                        LibraryItem item;
                        if (!cache.TryGetValue(f, out item) || item.Modified != mod)
                        {
                            if (item != null && item.Thumb != null) item.Thumb.Dispose();
                            item = new LibraryItem { Path = f, Modified = mod, Time = Library.CaptureTime(f) };
                            cache[f] = item;
                        }
                        item.Editable = File.Exists(Path.Combine(side, Path.GetFileName(f) + ".cmk"));
                        items.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                ShowMessage("Can't read folder: " + ex.Message);
            }
            var present = new HashSet<string>(items.Select(i => i.Path), StringComparer.OrdinalIgnoreCase);
            foreach (var key in cache.Keys.Where(k => !present.Contains(k)).ToList())
            {
                if (cache[key].Thumb != null) cache[key].Thumb.Dispose();
                cache.Remove(key);
            }
            all = items.OrderByDescending(i => i.Time).ToList();
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            string q = search.Text.Trim();
            var list = all;
            if (q.Length > 0)
            {
                list = all.Where(i =>
                    Path.GetFileName(i.Path).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    i.Time.ToString("yyyy-MM-dd dddd MMMM", CultureInfo.CurrentCulture).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            }
            grid.SetItems(list);
            countLabel.Text = all.Count == 0 ? "No screenshots yet" :
                (q.Length > 0 ? list.Count + " of " : "") + all.Count + (all.Count == 1 ? " screenshot" : " screenshots");
        }

        private void ShowMessage(string text)
        {
            messageLabel.Text = text;
            messageTimer.Stop();
            messageTimer.Start();
        }

        private void OpenFolder()
        {
            try
            {
                Directory.CreateDirectory(Library.Folder);
                Process.Start("explorer.exe", "\"" + Library.Folder + "\"");
            }
            catch (Exception)
            {
            }
        }

        private static void Reveal(string path)
        {
            try { Process.Start("explorer.exe", "/select,\"" + path + "\""); }
            catch (Exception) { }
        }

        private void CopyItem(LibraryItem item)
        {
            try
            {
                using (var bmp = ImageIO.Load(item.Path)) ClipboardHelper.CopyFile(item.Path, bmp);
                ShowMessage("Copied " + Path.GetFileNameWithoutExtension(item.Path));
            }
            catch (Exception ex)
            {
                ShowMessage("Couldn't copy: " + ex.Message);
            }
        }

        private void DeleteItems(List<LibraryItem> items)
        {
            if (items.Count == 0) return;
            foreach (var item in items)
            {
                try
                {
                    EditorForm.CloseFor(item.Path);
                    Library.Recycle(item.Path);
                }
                catch (Exception ex)
                {
                    ShowMessage("Couldn't delete: " + ex.Message);
                }
            }
            ShowMessage(items.Count == 1 ? "Moved to Recycle Bin" : items.Count + " screenshots moved to Recycle Bin");
            Reload();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.F))
            {
                search.Focus();
                return true;
            }
            if (keyData == Keys.Escape && search.Focused && search.Text.Length > 0)
            {
                search.Text = "";
                grid.Focus();
                return true;
            }
            if (keyData == Keys.F5)
            {
                Reload();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Library.FolderChanged -= OnFolderChanged;
            if (watcher != null) watcher.Dispose();
            refreshTimer.Dispose();
            messageTimer.Dispose();
            grid.StopLoading();
            foreach (var item in cache.Values) if (item.Thumb != null) item.Thumb.Dispose();
            cache.Clear();
            base.OnFormClosed(e);
        }
    }

    /// <summary>Scrolling grid of screenshot cards grouped by day; thumbnails decode on a worker thread.</summary>
    internal sealed class ThumbGrid : ScrollableControl
    {
        private sealed class Group
        {
            public string Title;
            public int Y;
        }

        private List<LibraryItem> items = new List<LibraryItem>();
        private readonly List<Group> groups = new List<Group>();
        private readonly HashSet<LibraryItem> selection = new HashSet<LibraryItem>();
        private LibraryItem hover, anchor;
        private Point mouseDown;
        private LibraryItem pressed;
        private readonly Font titleFont, captionFont, smallFont, emptyFont;
        private readonly ContextMenuStrip menu;
        private readonly Queue<LibraryItem> loadQueue = new Queue<LibraryItem>();
        private readonly HashSet<LibraryItem> queued = new HashSet<LibraryItem>();
        private readonly object sync = new object();
        private Thread worker;
        private volatile bool stopping;
        private readonly AutoResetEvent wake = new AutoResetEvent(false);

        public event Action<LibraryItem> OpenRequested;
        public event Action<List<LibraryItem>> DeleteRequested;
        public event Action<LibraryItem> CopyRequested;
        public event Action<LibraryItem> RevealRequested;

        public ThumbGrid()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.Window;
            AutoScroll = true;
            TabStop = true;
            titleFont = Theme.UiFont(10.5f, FontStyle.Bold);
            captionFont = Theme.UiFont(9f, FontStyle.Bold);
            smallFont = Theme.UiFont(8.25f);
            emptyFont = Theme.UiFont(11f);

            menu = new ContextMenuStrip();
            Theme.Apply(menu);
            menu.Items.Add("Open in editor", null, (s, e) => { if (anchor != null && OpenRequested != null) OpenRequested(anchor); });
            menu.Items.Add("Copy\tCtrl+C", null, (s, e) => { if (anchor != null && CopyRequested != null) CopyRequested(anchor); });
            menu.Items.Add("Show in Explorer", null, (s, e) => { if (anchor != null && RevealRequested != null) RevealRequested(anchor); });
            menu.Items.Add("Open with default app", null, (s, e) => { if (anchor != null) try { Process.Start(anchor.Path); } catch (Exception) { } });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Delete\tDel", null, (s, e) => RequestDelete());
        }

        private int Margin_ { get { return Theme.S(18); } }
        private int Gap { get { return Theme.S(14); } }
        private int HeaderH { get { return Theme.S(38); } }
        private int CaptionH { get { return Theme.S(40); } }

        public void SetItems(List<LibraryItem> list)
        {
            items = list;
            selection.RemoveWhere(i => !list.Contains(i));
            if (hover != null && !list.Contains(hover)) hover = null;
            DoLayout();
            Invalidate();
            // Decode every thumbnail in the background (newest first) so scrolling is instant.
            var missing = list.Where(i => i.Thumb == null).ToList();
            if (missing.Count > 0) RequestThumbs(missing);
        }

        private static string DayTitle(DateTime t)
        {
            var today = DateTime.Today;
            if (t.Date == today) return "Today";
            if (t.Date == today.AddDays(-1)) return "Yesterday";
            if (t.Date > today.AddDays(-7)) return t.ToString("dddd", CultureInfo.CurrentCulture);
            if (t.Year == today.Year) return t.ToString("dddd, MMMM d", CultureInfo.CurrentCulture);
            return t.ToString("MMMM d, yyyy", CultureInfo.CurrentCulture);
        }

        private void DoLayout()
        {
            groups.Clear();
            int avail = ClientSize.Width - 2 * Margin_;
            int minCard = Theme.S(230);
            int cols = Math.Max(1, (avail + Gap) / (minCard + Gap));
            int cardW = Math.Max(Theme.S(120), (avail - (cols - 1) * Gap) / cols);
            int thumbH = (int)(cardW * 0.62f);
            int cardH = thumbH + CaptionH;
            int y = Theme.S(6);
            int col = 0;
            DateTime day = DateTime.MinValue;
            foreach (var item in items)
            {
                if (item.Time.Date != day || groups.Count == 0)
                {
                    if (col != 0) y += cardH + Gap;
                    col = 0;
                    day = item.Time.Date;
                    groups.Add(new Group { Title = DayTitle(item.Time), Y = y });
                    y += HeaderH;
                }
                item.Bounds = new Rectangle(Margin_ + col * (cardW + Gap), y, cardW, cardH);
                col++;
                if (col == cols)
                {
                    col = 0;
                    y += cardH + Gap;
                }
            }
            if (col != 0) y += cardH + Gap;
            AutoScrollMinSize = new Size(0, y + Margin_);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            DoLayout();
            Invalidate();
        }

        protected override void OnScroll(ScrollEventArgs se)
        {
            base.OnScroll(se);
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Invalidate();
        }

        private Rectangle ToView(Rectangle r)
        {
            r.Offset(AutoScrollPosition);
            return r;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            if (items.Count == 0)
            {
                DrawEmpty(g);
                return;
            }
            int oy = AutoScrollPosition.Y;
            foreach (var grp in groups)
            {
                var r = new Rectangle(Margin_, grp.Y + oy, ClientSize.Width - 2 * Margin_, HeaderH);
                if (r.Bottom < 0 || r.Top > ClientSize.Height) continue;
                TextRenderer.DrawText(g, grp.Title, titleFont, r, Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            var view = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);
            var wanted = new List<LibraryItem>();
            foreach (var item in items)
            {
                var r = ToView(item.Bounds);
                if (!r.IntersectsWith(view)) continue;
                DrawCard(g, item, r);
                if (item.Thumb == null) wanted.Add(item);
            }
            if (wanted.Count > 0) RequestThumbs(wanted);
        }

        private void DrawEmpty(Graphics g)
        {
            int s = Theme.S(96);
            int cx = ClientSize.Width / 2, cy = ClientSize.Height / 2 - Theme.S(40);
            using (var ic = Art.DrawAppIcon(s)) g.DrawImage(ic, cx - s / 2, cy - s / 2, s, s);
            var r = new Rectangle(0, cy + s / 2 + Theme.S(16), ClientSize.Width, Theme.S(30));
            TextRenderer.DrawText(g, "No screenshots here yet", emptyFont, r, Theme.Text, TextFormatFlags.HorizontalCenter);
            r.Offset(0, Theme.S(28));
            TextRenderer.DrawText(g, "Press Print Screen to grab one — it lands here automatically.", Font, r, Theme.SubText, TextFormatFlags.HorizontalCenter);
        }

        private void DrawCard(Graphics g, LibraryItem item, Rectangle r)
        {
            bool sel = selection.Contains(item);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float radius = Theme.S(10);
            Theme.FillRounded(g, item == hover ? Theme.Hover : Theme.Card, r, radius);

            int inset = Theme.S(6);
            var thumbArea = new Rectangle(r.X + inset, r.Y + inset, r.Width - 2 * inset, r.Height - CaptionH - inset);
            Theme.FillRounded(g, Color.FromArgb(20, 21, 24), thumbArea, radius - 3);
            if (item.Thumb != null)
            {
                float k = Math.Min(thumbArea.Width / (float)item.Thumb.Width, thumbArea.Height / (float)item.Thumb.Height);
                k = Math.Min(k, 1f * Theme.Scale * 2);
                int tw = (int)(item.Thumb.Width * k), th = (int)(item.Thumb.Height * k);
                var tr = new Rectangle(thumbArea.X + (thumbArea.Width - tw) / 2, thumbArea.Y + (thumbArea.Height - th) / 2, tw, th);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                using (var clip = Theme.RoundedRect(thumbArea, radius - 3))
                {
                    var old = g.Clip;
                    g.SetClip(clip, CombineMode.Intersect);
                    g.DrawImage(item.Thumb, tr);
                    g.Clip = old;
                }
            }
            else
            {
                using (var b = new SolidBrush(Color.FromArgb(40, 255, 255, 255)))
                    g.FillEllipse(b, thumbArea.X + thumbArea.Width / 2 - Theme.S(4), thumbArea.Y + thumbArea.Height / 2 - Theme.S(4), Theme.S(8), Theme.S(8));
            }

            var cap = new Rectangle(r.X + Theme.S(12), r.Bottom - CaptionH, r.Width - Theme.S(24), CaptionH);
            string time = item.Time.ToString("t", CultureInfo.CurrentCulture);
            TextRenderer.DrawText(g, time, captionFont, cap, Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            string info = item.ImageSize.IsEmpty ? "" : item.ImageSize.Width + " × " + item.ImageSize.Height;
            if (item.Editable) info = "✎  " + info;
            TextRenderer.DrawText(g, info, smallFont, cap, Theme.SubText, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            if (sel)
            {
                using (var p = new Pen(Theme.Accent, Theme.S(2)))
                using (var path = Theme.RoundedRect(new RectangleF(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2), radius))
                    g.DrawPath(p, path);
            }
        }

        // ------------------------------------------------------------------ thumbnails

        private void RequestThumbs(List<LibraryItem> wanted)
        {
            lock (sync)
            {
                foreach (var it in wanted)
                {
                    if (queued.Contains(it)) continue;
                    queued.Add(it);
                    loadQueue.Enqueue(it);
                }
            }
            if (worker == null)
            {
                worker = new Thread(WorkerLoop) { IsBackground = true, Name = "Clipmunk thumbnails", Priority = ThreadPriority.BelowNormal };
                worker.Start();
            }
            wake.Set();
        }

        private void WorkerLoop()
        {
            while (!stopping)
            {
                LibraryItem next = null;
                lock (sync)
                {
                    if (loadQueue.Count > 0) next = loadQueue.Dequeue();
                }
                if (next == null)
                {
                    wake.WaitOne(1000);
                    continue;
                }
                Bitmap thumb = null;
                Size size = Size.Empty;
                try
                {
                    var bytes = File.ReadAllBytes(next.Path);
                    using (var ms = new MemoryStream(bytes))
                    using (var img = Image.FromStream(ms, false, false))
                    {
                        size = img.Size;
                        int max = Theme.S(480);
                        float k = Math.Min(1f, Math.Min(max / (float)img.Width, max / (float)img.Height));
                        int tw = Math.Max(1, (int)(img.Width * k)), th = Math.Max(1, (int)(img.Height * k));
                        thumb = new Bitmap(tw, th, PixelFormat.Format32bppPArgb);
                        using (var g = Graphics.FromImage(thumb))
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            g.DrawImage(img, new Rectangle(0, 0, tw, th));
                        }
                    }
                }
                catch (Exception)
                {
                    thumb = null;
                }
                var item = next;
                var t = thumb;
                var sz = size;
                if (stopping || IsDisposed)
                {
                    if (t != null) t.Dispose();
                    return;
                }
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        lock (sync) queued.Remove(item);
                        if (t == null) t = new Bitmap(1, 1);
                        if (item.Thumb != null) item.Thumb.Dispose();
                        item.Thumb = t;
                        item.ImageSize = sz;
                        Invalidate(ToView(item.Bounds));
                    }));
                }
                catch (InvalidOperationException)
                {
                    return;
                }
            }
        }

        public void StopLoading()
        {
            stopping = true;
            wake.Set();
        }

        // ------------------------------------------------------------------ input

        private LibraryItem HitItem(Point p)
        {
            p.Offset(-AutoScrollPosition.X, -AutoScrollPosition.Y);
            foreach (var item in items) if (item.Bounds.Contains(p)) return item;
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var h = HitItem(e.Location);
            if (h != hover)
            {
                if (hover != null) Invalidate(ToView(hover.Bounds));
                hover = h;
                if (hover != null) Invalidate(ToView(hover.Bounds));
                Cursor = hover != null ? Cursors.Hand : Cursors.Default;
            }
            if (pressed != null && e.Button == MouseButtons.Left &&
                (Math.Abs(e.X - mouseDown.X) > SystemInformation.DragSize.Width || Math.Abs(e.Y - mouseDown.Y) > SystemInformation.DragSize.Height))
            {
                var files = new System.Collections.Specialized.StringCollection();
                foreach (var it in selection.Count > 0 ? selection.ToList() : new List<LibraryItem> { pressed }) files.Add(it.Path);
                var data = new DataObject();
                data.SetFileDropList(files);
                pressed = null;
                DoDragDrop(data, DragDropEffects.Copy);
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hover != null) Invalidate(ToView(hover.Bounds));
            hover = null;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            var hit = HitItem(e.Location);
            mouseDown = e.Location;
            pressed = e.Button == MouseButtons.Left ? hit : null;
            if (hit == null)
            {
                if ((ModifierKeys & Keys.Control) == 0) selection.Clear();
                Invalidate();
                return;
            }
            if (e.Button == MouseButtons.Left && e.Clicks == 2)
            {
                pressed = null;
                if (OpenRequested != null) OpenRequested(hit);
                return;
            }
            if ((ModifierKeys & Keys.Control) != 0)
            {
                if (!selection.Remove(hit)) selection.Add(hit);
            }
            else if ((ModifierKeys & Keys.Shift) != 0 && anchor != null)
            {
                int a = items.IndexOf(anchor), b = items.IndexOf(hit);
                selection.Clear();
                for (int i = Math.Min(a, b); i <= Math.Max(a, b); i++) selection.Add(items[i]);
            }
            else if (!selection.Contains(hit) || e.Button == MouseButtons.Left)
            {
                selection.Clear();
                selection.Add(hit);
            }
            anchor = hit;
            Invalidate();
            if (e.Button == MouseButtons.Right) menu.Show(this, e.Location);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            pressed = null;
        }

        private void RequestDelete()
        {
            var list = selection.ToList();
            if (list.Count == 0 && anchor != null) list.Add(anchor);
            if (DeleteRequested != null) DeleteRequested(list);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Left:
                case Keys.Right:
                case Keys.Up:
                case Keys.Down:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (items.Count == 0) return;
            if (e.KeyCode == Keys.Delete) { RequestDelete(); return; }
            if (e.KeyCode == Keys.Enter && anchor != null) { if (OpenRequested != null) OpenRequested(anchor); return; }
            if (e.Control && e.KeyCode == Keys.C && anchor != null) { if (CopyRequested != null) CopyRequested(anchor); return; }
            if (e.Control && e.KeyCode == Keys.A) { selection.UnionWith(items); Invalidate(); return; }

            int idx = anchor == null ? -1 : items.IndexOf(anchor);
            int next = idx;
            switch (e.KeyCode)
            {
                case Keys.Left: next = Math.Max(0, idx - 1); break;
                case Keys.Right: next = Math.Min(items.Count - 1, idx + 1); break;
                case Keys.Up:
                case Keys.Down:
                {
                    if (idx < 0) { next = 0; break; }
                    var cur = items[idx].Bounds;
                    int dir = e.KeyCode == Keys.Down ? 1 : -1;
                    LibraryItem best = null;
                    int bestScore = int.MaxValue;
                    foreach (var it in items)
                    {
                        int dy = (it.Bounds.Y - cur.Y) * dir;
                        if (dy <= 0) continue;
                        int score = dy * 4 + Math.Abs(it.Bounds.X - cur.X);
                        if (score < bestScore) { bestScore = score; best = it; }
                    }
                    if (best != null) next = items.IndexOf(best);
                    break;
                }
                default:
                    return;
            }
            if (next < 0) next = 0;
            anchor = items[next];
            selection.Clear();
            selection.Add(anchor);
            EnsureVisible(anchor);
            Invalidate();
            e.Handled = true;
        }

        private void EnsureVisible(LibraryItem item)
        {
            var r = ToView(item.Bounds);
            int y = -AutoScrollPosition.Y;
            if (r.Top < 0) AutoScrollPosition = new Point(0, y + r.Top - Gap);
            else if (r.Bottom > ClientSize.Height) AutoScrollPosition = new Point(0, y + r.Bottom - ClientSize.Height + Gap);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopLoading();
                titleFont.Dispose();
                captionFont.Dispose();
                smallFont.Dispose();
                emptyFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

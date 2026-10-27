using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace Clipmunk
{
    internal sealed class EditorForm : Form
    {
        private static readonly Dictionary<string, EditorForm> openEditors = new Dictionary<string, EditorForm>(StringComparer.OrdinalIgnoreCase);

        public static readonly Color[] Palette =
        {
            Color.FromArgb(255, 59, 48), Color.FromArgb(255, 149, 0), Color.FromArgb(255, 214, 10), Color.FromArgb(52, 199, 89),
            Color.FromArgb(0, 199, 190), Color.FromArgb(10, 132, 255), Color.FromArgb(94, 92, 230), Color.FromArgb(255, 55, 135),
            Color.FromArgb(255, 255, 255), Color.FromArgb(142, 142, 147), Color.FromArgb(58, 58, 60), Color.FromArgb(0, 0, 0)
        };

        private static readonly float[] Widths = { 0, 1, 2, 3, 4, 6, 8, 12, 16, 24 };
        private static readonly float[] FontSizes = { 12, 14, 16, 18, 20, 24, 28, 32, 40, 48, 64, 80, 96 };

        private readonly CaptureDocument doc;
        private readonly CanvasHost host;
        private readonly EditorCanvas canvas;
        private readonly ToolStrip toolBar, styleBar;
        private readonly StatusStrip status;
        private readonly ToolStripStatusLabel locationLabel, messageLabel, sizeLabel;
        private readonly ToolStripDropDownButton outlineButton, fillButton, widthButton, fontButton, stickerButton, zoomButton;
        private readonly ToolStripButton boldButton, undoButton, redoButton, rulerButton;
        private readonly ToolStripMenuItem autoCopyItem;
        private readonly List<ToolStripButton> quickSwatches = new List<ToolStripButton>();
        private readonly Dictionary<Tool, ToolStripButton> toolButtons = new Dictionary<Tool, ToolStripButton>();
        private readonly Timer saveTimer, copyTimer, messageTimer;
        private readonly int icon;
        private bool dirty;

        public static void OpenDocument(string path)
        {
            EditorForm existing;
            if (openEditors.TryGetValue(path, out existing) && !existing.IsDisposed)
            {
                if (existing.WindowState == FormWindowState.Minimized) existing.WindowState = FormWindowState.Normal;
                existing.Activate();
                return;
            }
            var ed = new EditorForm(CaptureDocument.Open(path));
            ed.Show();
            ed.Activate();
        }

        public static void CloseFor(string path)
        {
            EditorForm existing;
            if (openEditors.TryGetValue(path, out existing) && !existing.IsDisposed)
            {
                existing.dirty = false;
                existing.saveTimer.Stop();
                existing.Close();
            }
        }

        public EditorForm(CaptureDocument document)
        {
            doc = document;
            openEditors[doc.ImagePath] = this;
            icon = Theme.S(20);
            var settings = Settings.Current;

            Text = "Clipmunk";
            Icon = Art.AppIcon();
            BackColor = Theme.Window;
            ForeColor = Theme.Text;
            Font = Theme.UiFont(9f);
            KeyPreview = true;
            StartPosition = FormStartPosition.Manual;

            var style = new Style
            {
                Stroke = settings.Stroke, Fill = settings.Fill, Width = settings.Width,
                FontSize = settings.FontSize, Bold = settings.Bold, Sticker = settings.Sticker
            };

            host = new CanvasHost { Dock = DockStyle.Fill };
            canvas = new EditorCanvas(host, doc.Base, doc.Shapes, style);
            host.Controls.Add(canvas);

            // ---------------- tool bar
            toolBar = new ToolStrip { Dock = DockStyle.Top, ImageScalingSize = new Size(icon, icon), Padding = new Padding(Theme.S(8), Theme.S(6), Theme.S(8), Theme.S(4)), AutoSize = true };
            Theme.Apply(toolBar);
            toolBar.Items.Add(GlyphButton("capture", "New capture (Ctrl+N)", (s, e) => NewCapture()));
            toolBar.Items.Add(new ToolStripSeparator());
            AddTool(Tool.Select, "select", "Select & move (V)");
            AddTool(Tool.Arrow, "arrow", "Arrow (A)");
            AddTool(Tool.Line, "line", "Line (L)");
            AddTool(Tool.Rectangle, "rect", "Rectangle (R)");
            AddTool(Tool.Ellipse, "ellipse", "Ellipse (E)");
            AddTool(Tool.Pen, "pen", "Pen (P)");
            AddTool(Tool.Highlighter, "highlighter", "Highlighter (H)");
            AddTool(Tool.Text, "text", "Text (T)");
            AddTool(Tool.Step, "step", "Step number (N)");
            AddTool(Tool.Sticker, "sticker", "Sticker (K)");
            AddTool(Tool.Blur, "blur", "Pixelate / redact (B)");
            AddTool(Tool.Crop, "crop", "Crop (C)");
            AddTool(Tool.Rotate, "rotate", "Rotate image (O) — drag to turn, Shift snaps to 45°");
            rulerButton = GlyphButton("ruler", "Ruler (Ctrl+R) — drag to move, ↻ or wheel to turn; lines drawn along an edge snap to it", (s, e) => ToggleRuler());
            toolBar.Items.Add(rulerButton);
            toolBar.Items.Add(new ToolStripSeparator());
            undoButton = GlyphButton("undo", "Undo (Ctrl+Z)", (s, e) => canvas.Undo());
            redoButton = GlyphButton("redo", "Redo (Ctrl+Y)", (s, e) => canvas.Redo());
            toolBar.Items.Add(undoButton);
            toolBar.Items.Add(redoButton);

            var copyButton = new ToolStripButton("    Copy    ", null, (s, e) => CopyToClipboard(true))
            {
                Alignment = ToolStripItemAlignment.Right,
                Tag = "accent",
                ToolTipText = "Copy image to clipboard (Ctrl+C)",
                Font = Theme.UiFont(9f, FontStyle.Bold),
                Margin = new Padding(Theme.S(4), 0, 0, 0)
            };
            var moreButton = new ToolStripDropDownButton(Art.Glyph("save", icon)) { Alignment = ToolStripItemAlignment.Right, ToolTipText = "Export & options", ShowDropDownArrow = false };
            Theme.Apply(moreButton.DropDown);
            moreButton.DropDownItems.Add("Export as…\tCtrl+Shift+S", null, (s, e) => ExportAs());
            moreButton.DropDownItems.Add("Show in folder", null, (s, e) => ShowInFolder());
            moreButton.DropDownItems.Add("Copy file (for attaching)", null, (s, e) => CopyFile());
            moreButton.DropDownItems.Add(new ToolStripSeparator());
            autoCopyItem = new ToolStripMenuItem("Auto-copy edits to clipboard") { CheckOnClick = true, Checked = settings.AutoCopy };
            autoCopyItem.CheckedChanged += (s, e) => { Settings.Current.AutoCopy = autoCopyItem.Checked; Settings.Current.Save(); };
            moreButton.DropDownItems.Add(autoCopyItem);
            var libraryButton = GlyphButton("library", "Library (Ctrl+L)", (s, e) => TrayContext.ShowLibrary());
            libraryButton.Alignment = ToolStripItemAlignment.Right;
            toolBar.Items.Add(copyButton);
            toolBar.Items.Add(BuildAskClaudeButton());
            toolBar.Items.Add(moreButton);
            toolBar.Items.Add(libraryButton);

            // ---------------- style bar
            styleBar = new ToolStrip { Dock = DockStyle.Top, ImageScalingSize = new Size(icon, icon), Padding = new Padding(Theme.S(8), Theme.S(2), Theme.S(8), Theme.S(6)), AutoSize = true };
            Theme.Apply(styleBar);
            outlineButton = new ToolStripDropDownButton { ToolTipText = "Color", DisplayStyle = ToolStripItemDisplayStyle.Image };
            outlineButton.DropDown = new PalettePopup(false, c => { canvas.Style.Stroke = c; ApplyStyleField(StyleField.Stroke); });
            styleBar.Items.Add(outlineButton);
            for (int i = 0; i < 8; i++)
            {
                var c = Palette[i];
                var b = new ToolStripButton(Art.Swatch(c, icon)) { ToolTipText = "Left-click: color · Right-click: fill", Margin = new Padding(0) };
                b.MouseUp += (s, e) =>
                {
                    if (e.Button == MouseButtons.Right)
                    {
                        canvas.Style.Fill = Color.FromArgb(canvas.Style.Fill.A > 0 && canvas.Style.Fill.A < 255 ? canvas.Style.Fill.A : 255, c);
                        ApplyStyleField(StyleField.Fill);
                    }
                    else if (e.Button == MouseButtons.Left)
                    {
                        canvas.Style.Stroke = c;
                        ApplyStyleField(StyleField.Stroke);
                    }
                };
                b.Tag = c;
                quickSwatches.Add(b);
                styleBar.Items.Add(b);
            }
            styleBar.Items.Add(new ToolStripSeparator());
            fillButton = new ToolStripDropDownButton("Fill") { ToolTipText = "Fill for shapes and text background", TextImageRelation = TextImageRelation.ImageBeforeText };
            fillButton.DropDown = new PalettePopup(true, c => { canvas.Style.Fill = c; ApplyStyleField(StyleField.Fill); });
            styleBar.Items.Add(fillButton);

            widthButton = new ToolStripDropDownButton { ToolTipText = "Line width / blur strength", TextImageRelation = TextImageRelation.ImageBeforeText };
            Theme.Apply(widthButton.DropDown);
            foreach (var w in Widths)
            {
                float wv = w;
                widthButton.DropDownItems.Add(new ToolStripMenuItem(w == 0 ? "No outline" : w + " px", Art.WidthGlyph(w, icon), (s, e) => { canvas.Style.Width = wv; ApplyStyleField(StyleField.Width); }));
            }
            styleBar.Items.Add(widthButton);

            fontButton = new ToolStripDropDownButton { ToolTipText = "Text size (also sizes step numbers)" };
            Theme.Apply(fontButton.DropDown);
            foreach (var f in FontSizes)
            {
                float fv = f;
                fontButton.DropDownItems.Add(new ToolStripMenuItem(f + " px", null, (s, e) => { canvas.Style.FontSize = fv; ApplyStyleField(StyleField.FontSize); }));
            }
            styleBar.Items.Add(fontButton);
            boldButton = new ToolStripButton(Art.Glyph("bold", icon)) { ToolTipText = "Bold text" };
            boldButton.Click += (s, e) => { canvas.Style.Bold = !canvas.Style.Bold; ApplyStyleField(StyleField.Bold); };
            styleBar.Items.Add(boldButton);
            styleBar.Items.Add(new ToolStripSeparator());

            stickerButton = new ToolStripDropDownButton("Sticker") { ToolTipText = "Sticker", TextImageRelation = TextImageRelation.ImageBeforeText };
            Theme.Apply(stickerButton.DropDown);
            foreach (StickerKind k in Enum.GetValues(typeof(StickerKind)))
            {
                var kind = k;
                stickerButton.DropDownItems.Add(new ToolStripMenuItem(Stickers.Name(k), Art.StickerImage(k, Theme.S(24)), (s, e) =>
                {
                    canvas.Style.Sticker = kind;
                    SetTool(Tool.Sticker);
                    ApplyStyleField(StyleField.Sticker);
                }));
            }
            ((ToolStripDropDownMenu)stickerButton.DropDown).ImageScalingSize = new Size(Theme.S(24), Theme.S(24));
            styleBar.Items.Add(stickerButton);

            zoomButton = new ToolStripDropDownButton("100%") { Alignment = ToolStripItemAlignment.Right, ToolTipText = "Zoom (Ctrl+wheel)" };
            Theme.Apply(zoomButton.DropDown);
            zoomButton.DropDownItems.Add("Fit to window\tCtrl+0", null, (s, e) => canvas.ZoomToFit());
            foreach (var z in new[] { 0.25f, 0.5f, 1f, 2f, 4f, 8f })
            {
                float zv = z;
                zoomButton.DropDownItems.Add((z * 100).ToString(CultureInfo.InvariantCulture) + "%" + (z == 1f ? "\tCtrl+1" : ""), null, (s, e) => canvas.SetZoom(zv, null));
            }
            styleBar.Items.Add(zoomButton);

            // ---------------- status bar
            status = new StatusStrip { SizingGrip = false, Padding = new Padding(Theme.S(10), 2, Theme.S(10), 2) };
            Theme.Apply(status);
            locationLabel = new ToolStripStatusLabel { ForeColor = Theme.SubText, IsLink = false };
            locationLabel.Click += (s, e) => ShowInFolder();
            messageLabel = new ToolStripStatusLabel { Spring = true, ForeColor = Theme.Accent, TextAlign = ContentAlignment.MiddleRight };
            sizeLabel = new ToolStripStatusLabel { ForeColor = Theme.SubText };
            status.Items.AddRange(new ToolStripItem[] { locationLabel, messageLabel, sizeLabel });

            Controls.Add(host);
            Controls.Add(styleBar);
            Controls.Add(toolBar);
            Controls.Add(status);

            saveTimer = new Timer { Interval = 700 };
            saveTimer.Tick += (s, e) => { saveTimer.Stop(); SaveNow(); };
            copyTimer = new Timer { Interval = 450 };
            copyTimer.Tick += (s, e) => { copyTimer.Stop(); if (canvas.IsEditingText) copyTimer.Start(); else CopyToClipboard(false); };
            messageTimer = new Timer { Interval = 2600 };
            messageTimer.Tick += (s, e) => { messageTimer.Stop(); messageLabel.Text = ""; };

            canvas.Changed += (s, e) =>
            {
                dirty = true;
                UpdateChrome();
                saveTimer.Stop();
                saveTimer.Start();
                if (Settings.Current.AutoCopy)
                {
                    copyTimer.Stop();
                    copyTimer.Start();
                }
            };
            canvas.SelectionChanged += (s, e) => RefreshStyleUi();
            canvas.ZoomChanged += (s, e) => zoomButton.Text = Math.Round(canvas.Zoom * 100) + "%";
            canvas.ToolChanged += (s, e) =>
            {
                foreach (var kv in toolButtons) kv.Value.Checked = kv.Key == canvas.Tool;
                Settings.Current.LastTool = canvas.Tool;
            };

            canvas.Tool = settings.LastTool == Tool.Crop || settings.LastTool == Tool.Rotate ? Tool.Arrow : settings.LastTool;
            foreach (var kv in toolButtons) kv.Value.Checked = kv.Key == canvas.Tool;
            RefreshStyleUi();
            UpdateChrome();
            SizeToImage();
        }

        private ToolStripButton GlyphButton(string glyph, string tip, EventHandler click)
        {
            return new ToolStripButton(null, Art.Glyph(glyph, icon), click)
            {
                DisplayStyle = ToolStripItemDisplayStyle.Image,
                ToolTipText = tip,
                Padding = new Padding(Theme.S(3)),
                Margin = new Padding(1, 0, 1, 0)
            };
        }

        private void AddTool(Tool t, string glyph, string tip)
        {
            var b = GlyphButton(glyph, tip, (s, e) => SetTool(t));
            toolButtons[t] = b;
            toolBar.Items.Add(b);
        }

        private void SetTool(Tool t)
        {
            canvas.Tool = t;
            canvas.Focus();
        }

        private void ToggleRuler()
        {
            canvas.RulerVisible = !canvas.RulerVisible;
            rulerButton.Checked = canvas.RulerVisible;
            canvas.Focus();
        }

        private void SizeToImage()
        {
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            int chromeH = toolBar.PreferredSize.Height + styleBar.PreferredSize.Height + status.PreferredSize.Height + Theme.S(48);
            int minW = Math.Max(toolBar.PreferredSize.Width, styleBar.PreferredSize.Width) + Theme.S(24);
            int w = Math.Max(minW, Math.Min(doc.Base.Width + Theme.S(56) + 16, (int)(wa.Width * 0.9)));
            int h = Math.Max(Theme.S(520), Math.Min(doc.Base.Height + Theme.S(56) + chromeH, (int)(wa.Height * 0.9)));
            Bounds = new Rectangle(wa.X + (wa.Width - w) / 2, wa.Y + (wa.Height - h) / 2, w, h);
            MinimumSize = new Size(Theme.S(640), Theme.S(420));
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.UseDarkTitleBar(Handle, Theme.Chrome);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            host.PerformLayout();
            float fit = canvas.FitZoom();
            if (fit < 1f) canvas.SetZoom(fit, null);
            host.CenterCanvas();
            canvas.Focus();
        }

        // ------------------------------------------------------------------ UI sync

        private void ApplyStyleField(StyleField f)
        {
            var s = Settings.Current;
            var st = canvas.Style;
            s.Stroke = st.Stroke; s.Fill = st.Fill; s.Width = st.Width; s.FontSize = st.FontSize; s.Bold = st.Bold; s.Sticker = st.Sticker;
            canvas.ApplyStyle(f);
            RefreshStyleUi();
            if (!canvas.IsEditingText) canvas.Focus();
        }

        private static void SwapImage(ToolStripItem item, Image img)
        {
            var old = item.Image;
            item.Image = img;
            if (old != null) old.Dispose();
        }

        private void RefreshStyleUi()
        {
            var st = canvas.Style;
            SwapImage(outlineButton, Art.Ring(st.Stroke, icon));
            SwapImage(fillButton, Art.Swatch(st.Fill, icon));
            SwapImage(widthButton, Art.WidthGlyph(st.Width, icon));
            widthButton.Text = st.Width == 0 ? "None" : st.Width + "";
            fontButton.Text = "Aa  " + st.FontSize;
            boldButton.Checked = st.Bold;
            SwapImage(stickerButton, Art.StickerImage(st.Sticker, icon));
            foreach (var b in quickSwatches) b.Checked = Shape.Same((Color)b.Tag, st.Stroke);
        }

        private void UpdateChrome()
        {
            Text = Path.GetFileNameWithoutExtension(doc.ImagePath) + " — Clipmunk";
            undoButton.Enabled = canvas.CanUndo;
            redoButton.Enabled = canvas.CanRedo;
            sizeLabel.Text = canvas.BaseImage.Width + " × " + canvas.BaseImage.Height;
            string folder = Path.GetDirectoryName(doc.ImagePath);
            locationLabel.Text = (GoogleDrive.Contains(folder) ? "☁  Google Drive · " : "") + folder;
            locationLabel.ToolTipText = "Click to open the folder";
        }

        private void ShowMessage(string text)
        {
            messageLabel.Text = text;
            messageTimer.Stop();
            messageTimer.Start();
        }

        // ------------------------------------------------------------------ actions

        public Bitmap FlattenForTest() { return canvas.Flatten(); }

        private void SaveNow()
        {
            if (!dirty) return;
            try
            {
                using (var flat = canvas.Flatten())
                {
                    doc.Base = canvas.BaseImage;
                    doc.Shapes = canvas.Shapes;
                    doc.Save(flat);
                }
                dirty = false;
            }
            catch (Exception ex)
            {
                ShowMessage("Couldn't save: " + ex.Message);
            }
        }

        private void CopyToClipboard(bool announce)
        {
            if (announce) canvas.CommitTextEdit();
            using (var flat = canvas.Flatten())
            {
                bool ok = ClipboardHelper.CopyImage(flat);
                ShowMessage(ok ? (announce ? "Copied to clipboard" : "Clipboard updated") : "Clipboard is busy — try again");
            }
        }

        private ToolStripSplitButton BuildAskClaudeButton()
        {
            var button = new ToolStripSplitButton(" Ask Claude", Art.Glyph("claude", icon), (s, e) => AskClaude())
            {
                Alignment = ToolStripItemAlignment.Right,
                ToolTipText = "Start a new Claude chat with this screenshot (Ctrl+Shift+C)",
                Font = Theme.UiFont(9f, FontStyle.Bold),
                Margin = new Padding(Theme.S(4), 0, 0, 0)
            };
            Theme.Apply(button.DropDown);
            var appItem = new ToolStripMenuItem("Open in the Claude app") { Enabled = ClaudeHandoff.AppInstalled };
            var webItem = new ToolStripMenuItem("Open claude.ai in the browser");
            var pasteItem = new ToolStripMenuItem("Paste the screenshot automatically") { CheckOnClick = true, Checked = Settings.Current.ClaudeAutoPaste };
            Action sync = () =>
            {
                bool app = ClaudeHandoff.EffectiveTarget == ClaudeHandoff.AppTarget;
                appItem.Checked = app;
                webItem.Checked = !app;
            };
            appItem.Click += (s, e) => { Settings.Current.ClaudeTarget = ClaudeHandoff.AppTarget; Settings.Current.Save(); sync(); };
            webItem.Click += (s, e) => { Settings.Current.ClaudeTarget = ClaudeHandoff.BrowserTarget; Settings.Current.Save(); sync(); };
            pasteItem.CheckedChanged += (s, e) => { Settings.Current.ClaudeAutoPaste = pasteItem.Checked; Settings.Current.Save(); };
            button.DropDownItems.AddRange(new ToolStripItem[] { appItem, webItem, new ToolStripSeparator(), pasteItem });
            sync();
            return button;
        }

        private void AskClaude()
        {
            canvas.CommitTextEdit();
            SaveNow();
            using (var flat = canvas.Flatten()) ShowMessage(ClaudeHandoff.Send(flat, this));
        }

        private void CopyFile()
        {
            canvas.CommitTextEdit();
            SaveNow();
            using (var flat = canvas.Flatten()) ClipboardHelper.CopyFile(doc.ImagePath, flat);
            ShowMessage("File copied — paste it into an email or chat");
        }

        private void ExportAs()
        {
            canvas.CommitTextEdit();
            using (var dlg = new SaveFileDialog())
            {
                dlg.Filter = "PNG image (*.png)|*.png|JPEG image (*.jpg)|*.jpg;*.jpeg|Bitmap (*.bmp)|*.bmp|GIF image (*.gif)|*.gif";
                dlg.FileName = Path.GetFileName(doc.ImagePath);
                dlg.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                dlg.AddExtension = true;
                dlg.DefaultExt = "png";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                using (var flat = canvas.Flatten()) ImageIO.Save(flat, dlg.FileName);
                ShowMessage("Exported " + Path.GetFileName(dlg.FileName));
            }
        }

        private void ShowInFolder()
        {
            SaveNow();
            try { Process.Start("explorer.exe", "/select,\"" + doc.ImagePath + "\""); }
            catch (Exception) { }
        }

        private void NewCapture()
        {
            TrayContext.StartCapture(this);
        }

        private void Paste()
        {
            var bmp = ClipboardHelper.GetImage();
            if (bmp != null)
            {
                canvas.AddImage(bmp);
                return;
            }
            if (Clipboard.ContainsText()) canvas.AddText(Clipboard.GetText());
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (canvas.IsEditingText) return base.ProcessCmdKey(ref msg, keyData);
            switch (keyData)
            {
                case Keys.Control | Keys.Z: canvas.Undo(); return true;
                case Keys.Control | Keys.Y:
                case Keys.Control | Keys.Shift | Keys.Z: canvas.Redo(); return true;
                case Keys.Control | Keys.C: CopyToClipboard(true); return true;
                case Keys.Control | Keys.Shift | Keys.C: AskClaude(); return true;
                case Keys.Control | Keys.V: Paste(); return true;
                case Keys.Control | Keys.S: SaveNow(); ShowMessage("Saved to library"); return true;
                case Keys.Control | Keys.Shift | Keys.S: ExportAs(); return true;
                case Keys.Control | Keys.D: canvas.DuplicateSelected(); return true;
                case Keys.Control | Keys.R: ToggleRuler(); return true;
                case Keys.Control | Keys.N: NewCapture(); return true;
                case Keys.Control | Keys.L: TrayContext.ShowLibrary(); return true;
                case Keys.Control | Keys.W: Close(); return true;
                case Keys.Control | Keys.D0:
                case Keys.Control | Keys.NumPad0: canvas.ZoomToFit(); return true;
                case Keys.Control | Keys.D1:
                case Keys.Control | Keys.NumPad1: canvas.SetZoom(1f, null); return true;
                case Keys.Control | Keys.Oemplus:
                case Keys.Control | Keys.Add: canvas.ZoomStep(1, null); return true;
                case Keys.Control | Keys.OemMinus:
                case Keys.Control | Keys.Subtract: canvas.ZoomStep(-1, null); return true;
                case Keys.Delete:
                case Keys.Back: if (canvas.DeleteSelected()) return true; break;
                case Keys.Escape: canvas.Escape(); return true;
                case Keys.Left: if (canvas.Nudge(-1, 0)) return true; break;
                case Keys.Right: if (canvas.Nudge(1, 0)) return true; break;
                case Keys.Up: if (canvas.Nudge(0, -1)) return true; break;
                case Keys.Down: if (canvas.Nudge(0, 1)) return true; break;
                case Keys.Shift | Keys.Left: if (canvas.Nudge(-10, 0)) return true; break;
                case Keys.Shift | Keys.Right: if (canvas.Nudge(10, 0)) return true; break;
                case Keys.Shift | Keys.Up: if (canvas.Nudge(0, -10)) return true; break;
                case Keys.Shift | Keys.Down: if (canvas.Nudge(0, 10)) return true; break;
                case Keys.V: SetTool(Tool.Select); return true;
                case Keys.A: SetTool(Tool.Arrow); return true;
                case Keys.L: SetTool(Tool.Line); return true;
                case Keys.R: SetTool(Tool.Rectangle); return true;
                case Keys.E: SetTool(Tool.Ellipse); return true;
                case Keys.P: SetTool(Tool.Pen); return true;
                case Keys.H: SetTool(Tool.Highlighter); return true;
                case Keys.T: SetTool(Tool.Text); return true;
                case Keys.N: SetTool(Tool.Step); return true;
                case Keys.K: SetTool(Tool.Sticker); return true;
                case Keys.B: SetTool(Tool.Blur); return true;
                case Keys.C: SetTool(Tool.Crop); return true;
                case Keys.O: SetTool(Tool.Rotate); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            canvas.CommitTextEdit();
            saveTimer.Stop();
            copyTimer.Stop();
            SaveNow();
            Settings.Current.Save();
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            EditorForm current;
            if (openEditors.TryGetValue(doc.ImagePath, out current) && current == this) openEditors.Remove(doc.ImagePath);
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                saveTimer.Dispose();
                copyTimer.Dispose();
                messageTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>Drop-down grid of colour dots.</summary>
    internal sealed class PalettePopup : ToolStripDropDown
    {
        public PalettePopup(bool allowNone, Action<Color> picked)
        {
            Theme.Apply(this);
            Padding = new Padding(Theme.S(6));
            var grid = new SwatchGrid(allowNone);
            grid.Picked += c =>
            {
                Close();
                picked(c);
            };
            grid.CustomRequested += () =>
            {
                Close();
                using (var dlg = new ColorDialog { FullOpen = true })
                    if (dlg.ShowDialog() == DialogResult.OK) picked(dlg.Color);
            };
            Items.Add(new ToolStripControlHost(grid) { Margin = Padding.Empty, Padding = Padding.Empty, AutoSize = false, Size = grid.Size });
        }
    }

    internal sealed class SwatchGrid : Control
    {
        private const int Columns = 6;
        private readonly bool allowNone;
        private readonly int cell;
        private readonly List<Color> colors = new List<Color>();
        private int hover = -1;
        private bool translucent;

        public event Action<Color> Picked;
        public event Action CustomRequested;

        public SwatchGrid(bool allowNone)
        {
            this.allowNone = allowNone;
            cell = Theme.S(30);
            if (allowNone) colors.Add(Color.Transparent);
            colors.AddRange(EditorForm.Palette);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            BackColor = Theme.Card;
            int rows = (colors.Count + Columns - 1) / Columns;
            Size = new Size(Columns * cell, rows * cell + Theme.S(allowNone ? 64 : 34));
            Font = Theme.UiFont(9f);
        }

        private Rectangle CellRect(int i) { return new Rectangle((i % Columns) * cell, (i / Columns) * cell, cell, cell); }
        private int Rows { get { return (colors.Count + Columns - 1) / Columns; } }
        private Rectangle CustomRect { get { return new Rectangle(Theme.S(4), Rows * cell + Theme.S(4), Width - Theme.S(8), Theme.S(26)); } }
        private Rectangle TranslucentRect { get { return new Rectangle(Theme.S(4), Rows * cell + Theme.S(34), Width - Theme.S(8), Theme.S(26)); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 0; i < colors.Count; i++)
            {
                var r = CellRect(i);
                if (i == hover) Theme.FillRounded(g, Theme.Hover, r, Theme.S(6));
                using (var img = Art.Swatch(colors[i], cell - Theme.S(4))) g.DrawImage(img, r.X + Theme.S(2), r.Y + Theme.S(2));
            }
            DrawTextRow(g, CustomRect, "Custom color…", hover == -2);
            if (allowNone) DrawTextRow(g, TranslucentRect, (translucent ? "✓  " : "     ") + "Semi-transparent fill", hover == -3);
        }

        private void DrawTextRow(Graphics g, Rectangle r, string text, bool hot)
        {
            if (hot) Theme.FillRounded(g, Theme.Hover, r, Theme.S(5));
            TextRenderer.DrawText(g, text, Font, new Rectangle(r.X + Theme.S(8), r.Y, r.Width, r.Height), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        }

        private int HitIndex(Point p)
        {
            for (int i = 0; i < colors.Count; i++) if (CellRect(i).Contains(p)) return i;
            if (CustomRect.Contains(p)) return -2;
            if (allowNone && TranslucentRect.Contains(p)) return -3;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = HitIndex(e.Location);
            if (h != hover)
            {
                hover = h;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = -1;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            int h = HitIndex(e.Location);
            if (h >= 0)
            {
                var c = colors[h];
                if (translucent && c.A > 0) c = Color.FromArgb(110, c);
                if (Picked != null) Picked(c);
            }
            else if (h == -2)
            {
                if (CustomRequested != null) CustomRequested();
            }
            else if (h == -3)
            {
                translucent = !translucent;
                Invalidate();
            }
        }
    }
}

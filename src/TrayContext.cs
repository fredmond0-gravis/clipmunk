using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Clipmunk
{
    internal sealed class TrayContext : ApplicationContext
    {
        public const string ActivateEventName = @"Local\Clipmunk.Activate";
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        private static TrayContext instance;
        private readonly NotifyIcon tray;
        private readonly Control invoker;
        private readonly PrintScreenHook hook;
        private readonly EventWaitHandle activateEvent;
        private readonly RegisteredWaitHandle activateWait;
        private readonly EventWaitHandle captureEvent;
        private readonly RegisteredWaitHandle captureWait;
        private ToolStripMenuItem startupItem, openEditorItem, winShiftSItem;
        private LibraryForm library;
        private bool capturing;

        public TrayContext()
        {
            instance = this;
            invoker = new Control();
            invoker.CreateControl();
            IntPtr unused = invoker.Handle;

            tray = new NotifyIcon
            {
                Icon = Art.AppIcon(SystemInformation.SmallIconSize.Width),
                Text = "Clipmunk — Print Screen or Win+Shift+S to capture",
                ContextMenuStrip = BuildMenu(),
                Visible = true
            };
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowLibrary(); };
            tray.BalloonTipClicked += (s, e) => ShowLibrary();

            hook = new PrintScreenHook(() => invoker.BeginInvoke((Action)(() => StartCapture(null))));
            bool hooked = hook.Start();

            activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
            activateWait = ThreadPool.RegisterWaitForSingleObject(activateEvent,
                (st, timedOut) => invoker.BeginInvoke((Action)(() => Balloon("Clipmunk is already running", "Press Print Screen or Win+Shift+S to capture, or click the tray icon for your library."))),
                null, -1, false);
            captureEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.CaptureEventName);
            captureWait = ThreadPool.RegisterWaitForSingleObject(captureEvent,
                (st, timedOut) => invoker.BeginInvoke((Action)(() => StartCapture(null))), null, -1, false);
            if (Program.Debug) Program.Log("started; hook installed=" + hooked);

            if (IsStartupEnabled()) SetStartup(true); // keep the path current if the exe moved

            bool first = Settings.IsFirstRun;
            string folder = Library.Folder; // creates the default on first run
            Settings.Current.Save();
            if (!hooked)
                Balloon("Clipmunk is running", "Couldn't take over the Print Screen key or Win+Shift+S — use the tray icon to capture.");
            else if (first)
                Balloon("Clipmunk is ready 🐿", "Press Print Screen or Win+Shift+S to capture. Screenshots save to " + folder + (GoogleDrive.Contains(folder) ? " (synced by Google Drive)." : "."));
            else
                Balloon("Clipmunk is running", "Press Print Screen or Win+Shift+S to capture.");
        }

        private ContextMenuStrip BuildMenu()
        {
            var m = new ContextMenuStrip();
            Theme.Apply(m);
            m.ImageScalingSize = new Size(Theme.S(18), Theme.S(18));
            var cap = new ToolStripMenuItem("Capture\tPrtSc / Win+Shift+S",Art.Glyph("capture", Theme.S(18)), (s, e) => StartCapture(null));
            cap.Font = new Font(cap.Font, FontStyle.Bold);
            m.Items.Add(cap);
            m.Items.Add("Capture in 3 seconds", null, (s, e) => StartDelayed(3000));
            m.Items.Add("Library", Art.Glyph("library", Theme.S(18)), (s, e) => ShowLibrary());
            m.Items.Add("Edit image from clipboard", null, (s, e) => EditClipboard());
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Change library folder…", Art.Glyph("folder", Theme.S(18)), (s, e) => ChooseLibraryFolder(null));
            openEditorItem = new ToolStripMenuItem("Open editor after capture") { CheckOnClick = true, Checked = Settings.Current.OpenEditorAfterCapture };
            openEditorItem.CheckedChanged += (s, e) => { Settings.Current.OpenEditorAfterCapture = openEditorItem.Checked; Settings.Current.Save(); };
            m.Items.Add(openEditorItem);
            winShiftSItem = new ToolStripMenuItem("Win+Shift+S opens Clipmunk") { CheckOnClick = true, Checked = Settings.Current.WinShiftS };
            winShiftSItem.CheckedChanged += (s, e) => { Settings.Current.WinShiftS = winShiftSItem.Checked; Settings.Current.Save(); };
            m.Items.Add(winShiftSItem);
            startupItem = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true, Checked = IsStartupEnabled() };
            startupItem.CheckedChanged += (s, e) => SetStartup(startupItem.Checked);
            m.Items.Add(startupItem);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("About Clipmunk", null, (s, e) => ShowAbout());
            m.Items.Add("Quit", null, (s, e) => Quit());
            return m;
        }

        private void Balloon(string title, string text)
        {
            tray.ShowBalloonTip(4000, title, text, ToolTipIcon.None);
        }

        // ------------------------------------------------------------------ capture

        /// <summary>Starts a capture; if called from an editor, that editor hides until the grab is done.</summary>
        public static void StartCapture(Form hideDuring)
        {
            if (instance == null) return;
            if (hideDuring != null)
            {
                hideDuring.Hide();
                instance.Delay(300, () => instance.DoCapture(hideDuring));
            }
            else
            {
                instance.DoCapture(null);
            }
        }

        /// <summary>Capture as soon as the message loop is running (used by "--capture" on first launch).</summary>
        public static void QueueCapture()
        {
            if (instance != null) instance.invoker.BeginInvoke((Action)(() => StartCapture(null)));
        }

        private void StartDelayed(int ms)
        {
            Delay(ms, () => DoCapture(null));
        }

        private void Delay(int ms, Action action)
        {
            var t = new System.Windows.Forms.Timer { Interval = ms };
            t.Tick += (s, e) =>
            {
                t.Stop();
                t.Dispose();
                action();
            };
            t.Start();
        }

        private void DoCapture(Form restoreAfter)
        {
            if (Program.Debug) Program.Log("capture requested (busy=" + capturing + ")");
            if (capturing) return;
            capturing = true;
            Bitmap result = null;
            try
            {
                Rectangle vs;
                var windows = ScreenCapture.VisibleWindows();
                using (var shot = ScreenCapture.CaptureVirtualScreen(out vs))
                using (var overlay = new CaptureOverlay(shot, vs, windows))
                {
                    if (Program.Debug) Program.Log("overlay ready " + vs + ", " + windows.Count + " windows");
                    var r = overlay.ShowDialog();
                    if (Program.Debug) Program.Log("overlay closed: " + r);
                    if (r == DialogResult.OK) result = overlay.Result;
                }
            }
            catch (Exception ex)
            {
                if (Program.Debug) Program.Log("capture failed: " + ex);
                Balloon("Capture failed", ex.Message);
            }
            finally
            {
                capturing = false;
                if (restoreAfter != null && !restoreAfter.IsDisposed) restoreAfter.Show();
                GC.Collect();
            }
            if (result == null) return;

            ClipboardHelper.CopyImage(result);
            CaptureDocument doc = null;
            try
            {
                doc = CaptureDocument.CreateNew(result);
            }
            catch (Exception ex)
            {
                Balloon("Couldn't save to your library", ex.Message + "\nThe screenshot is still on your clipboard.");
                doc = new CaptureDocument { ImagePath = Path.Combine(Path.GetTempPath(), Path.GetFileName(Library.NewCapturePath(Path.GetTempPath()))), Base = result };
            }

            if (Settings.Current.OpenEditorAfterCapture)
            {
                var ed = new EditorForm(doc);
                ed.Show();
                ed.Activate();
            }
            else
            {
                Balloon("Copied & saved", Path.GetFileName(doc.ImagePath));
            }
        }

        private void EditClipboard()
        {
            var bmp = ClipboardHelper.GetImage();
            if (bmp == null)
            {
                Balloon("Nothing to edit", "There's no image on the clipboard.");
                return;
            }
            try
            {
                var ed = new EditorForm(CaptureDocument.CreateNew(bmp));
                ed.Show();
                ed.Activate();
            }
            catch (Exception ex)
            {
                Balloon("Couldn't save to your library", ex.Message);
            }
        }

        // ------------------------------------------------------------------ library

        public static void ShowLibrary()
        {
            if (instance == null) return;
            var lib = instance.library;
            if (lib == null || lib.IsDisposed)
            {
                lib = new LibraryForm();
                instance.library = lib;
                lib.Show();
            }
            else
            {
                if (lib.WindowState == FormWindowState.Minimized) lib.WindowState = FormWindowState.Normal;
                lib.Reload();
            }
            lib.Activate();
            Native.ForceForeground(lib.Handle);
        }

        public static void ChooseLibraryFolder(IWin32Window owner)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Choose where Clipmunk keeps your screenshots. Pick a folder inside Google Drive to see them on every computer.";
                dlg.ShowNewFolderButton = true;
                dlg.SelectedPath = Library.Folder;
                if (dlg.ShowDialog(owner) == DialogResult.OK) Library.Folder = dlg.SelectedPath;
            }
        }

        // ------------------------------------------------------------------ misc

        private static bool IsStartupEnabled()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue("Clipmunk") != null;
        }

        private static void SetStartup(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) k.SetValue("Clipmunk", "\"" + Application.ExecutablePath + "\"");
                    else k.DeleteValue("Clipmunk", false);
                }
            }
            catch (Exception)
            {
            }
        }

        private void ShowAbout()
        {
            MessageBox.Show(
                "Clipmunk " + Program.Version + "\n\n" +
                "Print Screen or Win+Shift+S — capture (drag an area or click a window)\n" +
                "In the capture: 1–4 switch rectangle / freeform / window / full screen, arrow keys nudge 1px, Shift+arrows 10px, Enter grabs, Esc cancels\n\n" +
                "Every capture is copied to the clipboard and saved to:\n" + Library.Folder,
                "About Clipmunk", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void Quit()
        {
            foreach (Form f in Application.OpenForms.Cast<Form>().ToArray()) f.Close();
            ExitThread();
        }

        protected override void ExitThreadCore()
        {
            hook.Dispose();
            activateWait.Unregister(null);
            captureWait.Unregister(null);
            tray.Visible = false;
            tray.Dispose();
            Settings.Current.Save();
            base.ExitThreadCore();
        }
    }
}

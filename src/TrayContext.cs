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
        private ToolStripMenuItem startupItem, openEditorItem, winShiftSItem, updateItem, autoUpdateItem;
        private LibraryForm library;
        private bool capturing;
        private Action balloonAction;
        private System.Windows.Forms.Timer updateTimer;
        private UpdateInfo availableUpdate;
        private Version notifiedVersion;
        private bool updating;

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
            tray.BalloonTipClicked += (s, e) =>
            {
                var action = balloonAction;
                balloonAction = null;
                if (action != null) action();
                else ShowLibrary();
            };

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
            // Updates: a quiet check shortly after start, then every 6 hours.
            updateTimer = new System.Windows.Forms.Timer { Interval = Program.UpdateNow ? 500 : 20000 };
            updateTimer.Tick += (s, e) =>
            {
                updateTimer.Interval = 6 * 60 * 60 * 1000;
                if (Program.UpdateNow) CheckForUpdates(false, true);
                else if (Settings.Current.AutoUpdate && !Updater.IsDevBuild) CheckForUpdates(false, false);
            };
            updateTimer.Start();

            if (Program.JustUpdated)
                Balloon("Clipmunk updated to " + Program.Version, "You're on the latest version. Press Print Screen to capture.");
            else if (!hooked)
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
            updateItem = new ToolStripMenuItem("Update available", null, (s, e) => { if (availableUpdate != null) InstallUpdate(availableUpdate); })
            {
                Visible = false,
                ForeColor = Theme.Accent
            };
            updateItem.Font = new Font(updateItem.Font, FontStyle.Bold);
            m.Items.Add(updateItem);
            var cap =new ToolStripMenuItem("Capture\tPrtSc / Win+Shift+S",Art.Glyph("capture", Theme.S(18)), (s, e) => StartCapture(null));
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
            autoUpdateItem = new ToolStripMenuItem("Check for updates automatically") { CheckOnClick = true, Checked = Settings.Current.AutoUpdate };
            autoUpdateItem.CheckedChanged += (s, e) => { Settings.Current.AutoUpdate = autoUpdateItem.Checked; Settings.Current.Save(); };
            m.Items.Add(autoUpdateItem);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Check for updates…", null, (s, e) => CheckForUpdates(true, false));
            m.Items.Add("About Clipmunk", null, (s, e) => ShowAbout());
            m.Items.Add("Quit", null, (s, e) => Quit());
            return m;
        }

        private void Balloon(string title, string text)
        {
            Balloon(title, text, null);
        }

        /// <summary>Shows a notification; clicking it runs <paramref name="onClick"/> (default: open the library).</summary>
        private void Balloon(string title, string text, Action onClick)
        {
            balloonAction = onClick;
            tray.ShowBalloonTip(4000, title, text, ToolTipIcon.None);
        }

        // ------------------------------------------------------------------ updates

        /// <summary>
        /// Checks GitHub in the background. Automatic checks only notify (once per version); a manual check
        /// asks before installing; "--update-now" installs without asking.
        /// </summary>
        private void CheckForUpdates(bool manual, bool installWithoutAsking)
        {
            if (updating) return;
            if (manual && Updater.IsDevBuild)
            {
                Balloon("Development build", "This copy runs from a source checkout — pull and run build.cmd to update it.");
                return;
            }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                UpdateInfo found = null;
                string error = null;
                try { found = Updater.Check(); }
                catch (Exception ex) { error = ex.Message; }
                invoker.BeginInvoke((Action)(() => OnUpdateChecked(found, error, manual, installWithoutAsking)));
            });
        }

        private void OnUpdateChecked(UpdateInfo found, string error, bool manual, bool installWithoutAsking)
        {
            if (error != null)
            {
                if (manual) Balloon("Couldn't check for updates", error + "\nClick to open the releases page.", OpenReleasesPage);
                return;
            }
            availableUpdate = found;
            updateItem.Visible = found != null;
            if (found == null)
            {
                if (manual) Balloon("Clipmunk is up to date", "You have the latest version (" + Program.Version + ").");
                return;
            }
            string v = found.Version.ToString(3);
            updateItem.Text = "Update to Clipmunk " + v;
            if (installWithoutAsking)
            {
                InstallUpdate(found);
            }
            else if (manual)
            {
                var answer = MessageBox.Show("Clipmunk " + v + " is available (you have " + Program.Version + ").\n\nUpdate now? Clipmunk will restart; open editors are saved first.",
                    "Clipmunk update", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (answer == DialogResult.Yes) InstallUpdate(found);
            }
            else if (notifiedVersion != found.Version)
            {
                notifiedVersion = found.Version;
                Balloon("Clipmunk " + v + " is available", "Click here to update — it only takes a few seconds.", () => InstallUpdate(found));
            }
        }

        private void InstallUpdate(UpdateInfo u)
        {
            if (updating) return;
            updating = true;
            Balloon("Updating Clipmunk…", "Downloading version " + u.Version.ToString(3) + ".");
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string error = Updater.Apply(u);
                invoker.BeginInvoke((Action)(() =>
                {
                    if (error == null)
                    {
                        Quit(); // the new copy starts as soon as this one has exited
                        return;
                    }
                    updating = false;
                    Balloon("Update failed", error + "\nClick to open the download page.", OpenReleasesPage);
                }));
            });
        }

        private static void OpenReleasesPage()
        {
            try { Process.Start(Updater.ReleasesPage); }
            catch (Exception) { }
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
            updateTimer.Stop();
            activateWait.Unregister(null);
            captureWait.Unregister(null);
            tray.Visible = false;
            tray.Dispose();
            Settings.Current.Save();
            base.ExitThreadCore();
        }
    }
}

using System;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Clipmunk
{
    /// <summary>
    /// "Ask Claude": puts the screenshot on the clipboard, opens a new Claude chat (desktop app if it is
    /// installed, otherwise claude.ai in the browser) and pastes the image into the composer once
    /// that window is in front.
    /// </summary>
    internal static class ClaudeHandoff
    {
        public const string AppTarget = "app";
        public const string BrowserTarget = "browser";
        private const string AppUrl = "claude://claude.ai/new";
        private const string WebUrl = "https://claude.ai/new";

        public static bool AppInstalled
        {
            get
            {
                try
                {
                    using (var k = Registry.ClassesRoot.OpenSubKey(@"claude\shell\open\command"))
                        return k != null && k.GetValue(null) != null;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>The configured target, falling back to the browser when the app isn't installed.</summary>
        public static string EffectiveTarget
        {
            get
            {
                string t = Settings.Current.ClaudeTarget;
                if (t == BrowserTarget) return BrowserTarget;
                return AppInstalled ? AppTarget : BrowserTarget;
            }
        }

        /// <summary>Returns a short status message for the editor.</summary>
        public static string Send(Bitmap image, Form source)
        {
            if (!ClipboardHelper.CopyImage(image)) return "Clipboard is busy — try again";

            bool app = EffectiveTarget == AppTarget;
            bool coldStart = app && !AppWindowExists();
            try
            {
                Process.Start(app ? AppUrl : WebUrl);
            }
            catch (Exception ex)
            {
                return "Couldn't open Claude: " + ex.Message;
            }

            if (!Settings.Current.ClaudeAutoPaste) return "Opening Claude — press Ctrl+V to add the screenshot";
            WaitAndPaste(source == null ? IntPtr.Zero : source.Handle, app, coldStart);
            return "Opening Claude and pasting your screenshot…";
        }

        /// <summary>
        /// Polls until a Claude window is in front, gives the new chat a moment to focus its composer,
        /// then sends Ctrl+V — but only if Claude is still the foreground window.
        /// </summary>
        private static void WaitAndPaste(IntPtr ownWindow, bool app, bool coldStart)
        {
            var started = DateTime.Now;
            DateTime? seenAt = null;
            int settleMs = app ? (coldStart ? 4500 : 1200) : 3000;
            var timer = new Timer { Interval = 150 };
            timer.Tick += (s, e) =>
            {
                if ((DateTime.Now - started).TotalSeconds > 20)
                {
                    timer.Stop();
                    timer.Dispose();
                    return;
                }
                IntPtr fg = Native.GetForegroundWindow();
                bool isClaude = fg != ownWindow && IsClaudeWindow(fg, app);
                if (!isClaude)
                {
                    seenAt = null;
                    return;
                }
                if (seenAt == null) seenAt = DateTime.Now;
                if ((DateTime.Now - seenAt.Value).TotalMilliseconds < settleMs) return;
                timer.Stop();
                timer.Dispose();
                Native.SendCtrlV();
            };
            timer.Start();
        }

        private static bool IsClaudeWindow(IntPtr hwnd, bool app)
        {
            if (hwnd == IntPtr.Zero) return false;
            if (app)
            {
                uint pid;
                Native.GetWindowThreadProcessId(hwnd, out pid);
                try
                {
                    using (var p = Process.GetProcessById((int)pid))
                        return string.Equals(p.ProcessName, "claude", StringComparison.OrdinalIgnoreCase);
                }
                catch (Exception)
                {
                    return false;
                }
            }
            var sb = new StringBuilder(512);
            Native.GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString().IndexOf("Claude", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool AppWindowExists()
        {
            foreach (var p in Process.GetProcessesByName("claude"))
            {
                using (p)
                {
                    try
                    {
                        if (p.MainWindowHandle != IntPtr.Zero) return true;
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            return false;
        }
    }
}

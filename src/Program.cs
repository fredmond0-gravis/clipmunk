using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Clipmunk")]
[assembly: AssemblyDescription("Clipmunk screenshot & annotation tool")]
[assembly: AssemblyProduct("Clipmunk")]
[assembly: AssemblyVersion("1.3.1.0")]
[assembly: AssemblyFileVersion("1.3.1.0")]

namespace Clipmunk
{
    internal static class Program
    {
        public const string Version = "1.3.1";
        public const string CaptureEventName = @"Local\Clipmunk.Capture";

        /// <summary>This process was started by the updater.</summary>
        public static bool JustUpdated;

        /// <summary>"--update-now": check GitHub and install any newer release without asking.</summary>
        public static bool UpdateNow;

        /// <summary>Set CLIPMUNK_DEBUG=1 to log to %APPDATA%\Clipmunk\debug.log.</summary>
        public static readonly bool Debug = Environment.GetEnvironmentVariable("CLIPMUNK_DEBUG") == "1";

        public static void Log(string text)
        {
            try
            {
                Directory.CreateDirectory(Settings.DataFolder);
                File.AppendAllText(Path.Combine(Settings.DataFolder, "debug.log"), DateTime.Now.ToString("HH:mm:ss.fff") + "  " + text + Environment.NewLine);
            }
            catch (Exception)
            {
            }
        }

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--write-icon")
            {
                Art.WriteIco(args[1]);
                return 0;
            }
            if (args.Length == 2 && args[0] == "--selftest")
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                return SelfTest.Run(args[1]);
            }

            bool captureNow = args.Length == 1 && args[0] == "--capture";
            UpdateNow = args.Length == 1 && args[0] == "--update-now";
            int oldPid;
            if (args.Length == 2 && args[0] == "--after-update" && int.TryParse(args[1], out oldPid))
            {
                // Started by the updater: let the old copy exit (and release the mutex) first.
                Updater.FinishUpdate(oldPid);
                JustUpdated = true;
            }
            else
            {
                Updater.CleanupOld();
            }

            bool createdNew;
            using (var mutex = new Mutex(true, @"Local\Clipmunk.SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    // Already running: "--capture" starts a capture there, otherwise just say hello.
                    try
                    {
                        using (var ev = EventWaitHandle.OpenExisting(captureNow ? CaptureEventName : TrayContext.ActivateEventName)) ev.Set();
                    }
                    catch (Exception)
                    {
                    }
                    return 0;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (s, e) => ReportError(e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => ReportError(e.ExceptionObject as Exception);

                using (var context = new TrayContext())
                {
                    if (captureNow) TrayContext.QueueCapture();
                    Application.Run(context);
                }
            }
            return 0;
        }

        public static void ReportError(Exception ex)
        {
            if (ex == null) return;
            string log = Path.Combine(Settings.DataFolder, "error.log");
            try
            {
                Directory.CreateDirectory(Settings.DataFolder);
                File.AppendAllText(log, DateTime.Now.ToString("s") + "  " + ex + Environment.NewLine + Environment.NewLine);
            }
            catch (Exception)
            {
            }
            MessageBox.Show("Something went wrong:\n\n" + ex.Message + "\n\nDetails were written to " + log, "Clipmunk",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Clipmunk
{
    internal sealed class UpdateInfo
    {
        public Version Version;
        public string Tag;
        public string ZipUrl;
        public string Sha256;
        public string PageUrl;
    }

    /// <summary>
    /// Self-update from GitHub Releases. Checks the latest release, downloads Clipmunk.zip, verifies its
    /// SHA-256 (from GitHub's asset digest) and the exe's version, then swaps the running exe:
    /// Windows lets us rename a running exe, so the old one becomes Clipmunk.exe.old, the new one takes
    /// its place, and the new process starts once this one has exited.
    /// </summary>
    internal static class Updater
    {
        public const string Repo = "fredmond0-gravis/clipmunk";
        private const string AssetName = "Clipmunk.zip";

        /// <summary>CLIPMUNK_UPDATE_FEED overrides the release URL (testing, staging, or a mirror).</summary>
        private static string FeedUrl
        {
            get
            {
                string env = Environment.GetEnvironmentVariable("CLIPMUNK_UPDATE_FEED");
                return string.IsNullOrEmpty(env) ? "https://api.github.com/repos/" + Repo + "/releases/latest" : env;
            }
        }

        public static string ReleasesPage { get { return "https://github.com/" + Repo + "/releases/latest"; } }

        public static Version CurrentVersion { get { return Normalize(typeof(Updater).Assembly.GetName().Version); } }

        /// <summary>True when running from a source checkout (dist\ next to build.cmd); those update by rebuilding.</summary>
        public static bool IsDevBuild
        {
            get
            {
                string dir = Path.GetDirectoryName(Application.ExecutablePath);
                return File.Exists(Path.Combine(Path.Combine(dir, ".."), "build.cmd"));
            }
        }

        private static Version Normalize(Version v)
        {
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));
        }

        public static bool TryParseTag(string tag, out Version v)
        {
            v = null;
            if (string.IsNullOrEmpty(tag)) return false;
            string s = tag.Trim().TrimStart('v', 'V');
            int dash = s.IndexOfAny(new[] { '-', '+' });
            if (dash >= 0) s = s.Substring(0, dash);
            Version parsed;
            if (!Version.TryParse(s.IndexOf('.') < 0 ? s + ".0" : s, out parsed)) return false;
            v = Normalize(parsed);
            return true;
        }

        private static WebClient Client(bool api)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var wc = new WebClient();
            wc.Headers[HttpRequestHeader.UserAgent] = "Clipmunk/" + Program.Version;
            if (api) wc.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
            return wc;
        }

        /// <summary>Returns the newer release, or null if this copy is up to date. Throws on network errors.</summary>
        public static UpdateInfo Check()
        {
            string json;
            using (var wc = Client(true))
            {
                wc.Encoding = System.Text.Encoding.UTF8;
                json = wc.DownloadString(FeedUrl);
            }
            return Parse(json, CurrentVersion);
        }

        public static UpdateInfo Parse(string json, Version current)
        {
            var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            if (root == null) return null;
            if (IsTrue(root, "draft") || IsTrue(root, "prerelease")) return null;
            string tag = root.ContainsKey("tag_name") ? root["tag_name"] as string : null;
            Version v;
            if (!TryParseTag(tag, out v) || v <= current) return null;

            var info = new UpdateInfo
            {
                Version = v,
                Tag = tag,
                PageUrl = root.ContainsKey("html_url") ? root["html_url"] as string : ReleasesPage
            };
            var assets = root.ContainsKey("assets") ? root["assets"] as IEnumerable : null;
            if (assets != null)
            {
                foreach (var a in assets)
                {
                    var asset = a as Dictionary<string, object>;
                    if (asset == null || !string.Equals(asset["name"] as string, AssetName, StringComparison.OrdinalIgnoreCase)) continue;
                    info.ZipUrl = asset["browser_download_url"] as string;
                    string digest = asset.ContainsKey("digest") ? asset["digest"] as string : null;
                    if (digest != null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) info.Sha256 = digest.Substring(7).ToLowerInvariant();
                }
            }
            return info.ZipUrl == null ? null : info;
        }

        private static bool IsTrue(Dictionary<string, object> d, string key)
        {
            object o;
            return d.TryGetValue(key, out o) && o is bool && (bool)o;
        }

        /// <summary>
        /// Downloads and installs the update next to the running exe and starts the new copy, which waits
        /// for this process to exit. Returns null on success (the caller should then quit), or an error message.
        /// </summary>
        public static string Apply(UpdateInfo u)
        {
            string exe = Application.ExecutablePath;
            string old = exe + ".old";
            string work = Path.Combine(Path.GetTempPath(), "Clipmunk-update");
            try
            {
                if (Directory.Exists(work)) Directory.Delete(work, true);
                Directory.CreateDirectory(work);
                string zip = Path.Combine(work, AssetName);
                using (var wc = Client(false)) wc.DownloadFile(u.ZipUrl, zip);

                if (u.Sha256 != null)
                {
                    string actual;
                    using (var sha = SHA256.Create())
                    using (var fs = File.OpenRead(zip))
                        actual = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
                    if (actual != u.Sha256) return "The download was corrupted (checksum mismatch). Nothing was changed.";
                }

                string fresh = Path.Combine(work, "Clipmunk.exe");
                using (var z = ZipFile.OpenRead(zip))
                {
                    ZipArchiveEntry entry = null;
                    foreach (var e in z.Entries)
                        if (string.Equals(e.Name, "Clipmunk.exe", StringComparison.OrdinalIgnoreCase)) entry = e;
                    if (entry == null) return "The release doesn't contain Clipmunk.exe. Nothing was changed.";
                    entry.ExtractToFile(fresh, true);
                }

                Version got;
                if (!TryParseTag(FileVersionInfo.GetVersionInfo(fresh).FileVersion, out got) || got != u.Version)
                    return "The downloaded file isn't version " + u.Version.ToString(3) + ". Nothing was changed.";

                if (File.Exists(old)) File.Delete(old);
                File.Move(exe, old);
                try
                {
                    File.Copy(fresh, exe, true);
                }
                catch (Exception)
                {
                    File.Move(old, exe);
                    throw;
                }
                Process.Start(exe, "--after-update " + Process.GetCurrentProcess().Id);
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return "Clipmunk's folder isn't writable (" + Path.GetDirectoryName(exe) + "). Download the new version from GitHub instead.";
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        /// <summary>Called on startup after an update: wait for the old process, then remove its exe.</summary>
        public static void FinishUpdate(int oldPid)
        {
            try
            {
                using (var p = Process.GetProcessById(oldPid)) p.WaitForExit(15000);
            }
            catch (Exception)
            {
                // already gone
            }
            CleanupOld();
        }

        public static void CleanupOld()
        {
            string old = Application.ExecutablePath + ".old";
            for (int i = 0; i < 10 && File.Exists(old); i++)
            {
                try
                {
                    File.Delete(old);
                }
                catch (Exception)
                {
                    System.Threading.Thread.Sleep(300);
                }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Clipmunk
{
    internal sealed class Settings
    {
        public Color Stroke = Color.FromArgb(255, 59, 48);
        public Color Fill = Color.Transparent;
        public float Width = 4f;
        public float FontSize = 24f;
        public bool Bold = true;
        public bool AutoCopy = true;
        public StickerKind Sticker = StickerKind.Check;
        public Tool LastTool = Tool.Arrow;
        public string LibraryFolder = "";
        public bool OpenEditorAfterCapture = true;
        public string ClaudeTarget = "";   // "app", "browser", or "" = app when installed
        public bool ClaudeAutoPaste = true;

        private static Settings current;
        public static Settings Current
        {
            get
            {
                if (current == null) current = Load();
                return current;
            }
        }

        public static string DataFolder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Clipmunk"); }
        }

        private static string FilePath { get { return Path.Combine(DataFolder, "settings.ini"); } }
        public static bool IsFirstRun { get { return !File.Exists(FilePath); } }

        private static Settings Load()
        {
            var s = new Settings();
            if (Theme.Scale >= 1.4f)
            {
                s.Width = 6f;
                s.FontSize = 32f;
            }
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    int i = line.IndexOf('=');
                    if (i <= 0) continue;
                    string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                    try
                    {
                        switch (k)
                        {
                            case "Stroke": s.Stroke = Shape.ParseColor(v); break;
                            case "Fill": s.Fill = Shape.ParseColor(v); break;
                            case "Width": s.Width = Shape.ParseNum(v); break;
                            case "FontSize": s.FontSize = Shape.ParseNum(v); break;
                            case "Bold": s.Bold = v == "1"; break;
                            case "AutoCopy": s.AutoCopy = v == "1"; break;
                            case "OpenEditor": s.OpenEditorAfterCapture = v == "1"; break;
                            case "LibraryFolder": s.LibraryFolder = v; break;
                            case "ClaudeTarget": s.ClaudeTarget = v; break;
                            case "ClaudeAutoPaste": s.ClaudeAutoPaste = v == "1"; break;
                            case "Sticker": { StickerKind sk; if (Enum.TryParse(v, out sk)) s.Sticker = sk; break; }
                            case "LastTool": { Tool t; if (Enum.TryParse(v, out t)) s.LastTool = t; break; }
                        }
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            catch (Exception)
            {
            }
            return s;
        }

        /// <summary>Set by the self-test so it never overwrites the user's settings.</summary>
        public static bool ReadOnly;

        public void Save()
        {
            if (ReadOnly) return;
            try
            {
                Directory.CreateDirectory(DataFolder);
                File.WriteAllLines(FilePath, new[]
                {
                    "Stroke=" + Shape.Hex(Stroke),
                    "Fill=" + Shape.Hex(Fill),
                    "Width=" + Shape.Num(Width),
                    "FontSize=" + Shape.Num(FontSize),
                    "Bold=" + (Bold ? "1" : "0"),
                    "AutoCopy=" + (AutoCopy ? "1" : "0"),
                    "OpenEditor=" + (OpenEditorAfterCapture ? "1" : "0"),
                    "Sticker=" + Sticker,
                    "LastTool=" + LastTool,
                    "LibraryFolder=" + LibraryFolder,
                    "ClaudeTarget=" + ClaudeTarget,
                    "ClaudeAutoPaste=" + (ClaudeAutoPaste ? "1" : "0")
                });
            }
            catch (Exception)
            {
            }
        }
    }

    /// <summary>Finds the Google Drive for desktop "My Drive" folder, if installed.</summary>
    internal static class GoogleDrive
    {
        private static string cached;
        private static bool searched;

        public static string MyDrive
        {
            get
            {
                if (!searched)
                {
                    searched = true;
                    cached = Find();
                }
                return cached;
            }
        }

        private static string Find()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var candidates = new List<string>
            {
                Path.Combine(home, "My Drive"),
                Path.Combine(home, "Google Drive", "My Drive"),
                Path.Combine(home, "Google Drive")
            };
            try
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    if (d.DriveType == DriveType.CDRom || d.DriveType == DriveType.Removable) continue;
                    candidates.Insert(0, Path.Combine(d.RootDirectory.FullName, "My Drive"));
                }
            }
            catch (Exception)
            {
            }
            foreach (var c in candidates)
            {
                try
                {
                    if (Directory.Exists(c)) return c;
                }
                catch (Exception)
                {
                }
            }
            return null;
        }

        public static bool Contains(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string root = MyDrive;
            if (root != null)
            {
                if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return true;
                // Drive for desktop mounts a whole letter (G:\My Drive, G:\Shared drives\...).
                string driveRoot = Path.GetPathRoot(root);
                if (string.Equals(Path.Combine(driveRoot, "My Drive"), root, StringComparison.OrdinalIgnoreCase) &&
                    path.StartsWith(driveRoot, StringComparison.OrdinalIgnoreCase)) return true;
            }
            try
            {
                var di = new DriveInfo(Path.GetPathRoot(path));
                return di.IsReady && (di.VolumeLabel.IndexOf("Google", StringComparison.OrdinalIgnoreCase) >= 0 || di.DriveFormat.IndexOf("Google", StringComparison.OrdinalIgnoreCase) >= 0);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    internal static class Library
    {
        public const string SidecarFolderName = ".clipmunk";
        public static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };

        public static string DefaultFolder
        {
            get
            {
                string drive = GoogleDrive.MyDrive;
                if (drive != null) return Path.Combine(drive, "Clipmunk");
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Clipmunk");
            }
        }

        public static string Folder
        {
            get
            {
                var s = Settings.Current;
                if (string.IsNullOrEmpty(s.LibraryFolder))
                {
                    s.LibraryFolder = DefaultFolder;
                    s.Save();
                }
                return s.LibraryFolder;
            }
            set
            {
                Settings.Current.LibraryFolder = value;
                Settings.Current.Save();
                var h = FolderChanged;
                if (h != null) h(null, EventArgs.Empty);
            }
        }

        public static event EventHandler FolderChanged;

        public static bool IsImage(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return Array.IndexOf(ImageExtensions, ext) >= 0;
        }

        public static string NewCapturePath()
        {
            return NewCapturePath(Folder);
        }

        public static string NewCapturePath(string folder)
        {
            Directory.CreateDirectory(folder);
            string stem = "Clipmunk " + DateTime.Now.ToString("yyyy-MM-dd 'at' HH.mm.ss", CultureInfo.InvariantCulture);
            string path = Path.Combine(folder, stem + ".png");
            for (int i = 2; File.Exists(path); i++) path = Path.Combine(folder, stem + " (" + i + ").png");
            return path;
        }

        /// <summary>Capture time from our file name, falling back to the file's timestamp.</summary>
        public static DateTime CaptureTime(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (name.StartsWith("Clipmunk ") && name.Length >= 31)
            {
                DateTime t;
                if (DateTime.TryParseExact(name.Substring(9, 22), "yyyy-MM-dd 'at' HH.mm.ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) return t;
            }
            try { return File.GetLastWriteTime(path); }
            catch (Exception) { return DateTime.MinValue; }
        }

        public static string SidecarDir(string imagePath) { return Path.Combine(Path.GetDirectoryName(imagePath), SidecarFolderName); }

        public static IEnumerable<string> SidecarFiles(string imagePath)
        {
            string dir = SidecarDir(imagePath);
            if (!Directory.Exists(dir)) yield break;
            string name = Path.GetFileName(imagePath);
            foreach (var f in Directory.GetFiles(dir, name + ".*")) yield return f;
        }

        public static void Recycle(string imagePath)
        {
            foreach (var f in new List<string>(SidecarFiles(imagePath))) RecycleFile(f);
            RecycleFile(imagePath);
        }

        private static void RecycleFile(string path)
        {
            if (!File.Exists(path)) return;
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        }
    }

    /// <summary>
    /// A screenshot in the library. The flattened image is what everyone sees; the original pixels and
    /// the annotation list live in a hidden ".clipmunk" folder next to it so edits stay editable.
    /// </summary>
    internal sealed class CaptureDocument
    {
        public string ImagePath;
        public Bitmap Base;
        public List<Shape> Shapes = new List<Shape>();
        private Bitmap persistedBase;

        private string OrigPath { get { return Path.Combine(Library.SidecarDir(ImagePath), Path.GetFileName(ImagePath) + ".orig.png"); } }
        private string ProjectPath { get { return Path.Combine(Library.SidecarDir(ImagePath), Path.GetFileName(ImagePath) + ".cmk"); } }

        public static CaptureDocument CreateNew(Bitmap capture)
        {
            var doc = new CaptureDocument { ImagePath = Library.NewCapturePath(), Base = capture };
            doc.Save(capture);
            return doc;
        }

        public static CaptureDocument Open(string path)
        {
            var doc = new CaptureDocument { ImagePath = path };
            try
            {
                if (File.Exists(doc.ProjectPath) && File.Exists(doc.OrigPath))
                {
                    doc.Base = ImageIO.Load(doc.OrigPath);
                    doc.ReadProject();
                    doc.persistedBase = doc.Base;
                    return doc;
                }
            }
            catch (Exception)
            {
                doc.Shapes.Clear();
            }
            doc.Base = ImageIO.Load(path);
            return doc;
        }

        public void Save(Bitmap flattened)
        {
            string dir = Library.SidecarDir(ImagePath);
            var di = Directory.CreateDirectory(dir);
            try
            {
                if ((di.Attributes & FileAttributes.Hidden) == 0) di.Attributes |= FileAttributes.Hidden;
            }
            catch (Exception)
            {
            }
            if (Base != persistedBase || !File.Exists(OrigPath))
            {
                ImageIO.Save(Base, OrigPath);
                persistedBase = Base;
            }
            WriteProject();
            ImageIO.Save(flattened, ImagePath);
        }

        private void WriteProject()
        {
            var sb = new StringBuilder();
            sb.AppendLine("clipmunk\t1\t" + Base.Width + "\t" + Base.Height);
            int imageIndex = 0;
            string prefix = Path.GetFileName(ImagePath);
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in Shapes)
            {
                var f = new List<string> { s.Kind };
                s.Write(f);
                var img = s as ImageShape;
                if (img != null)
                {
                    string file = prefix + ".img" + (imageIndex++) + ".png";
                    keep.Add(file);
                    ImageIO.Save(img.Image, Path.Combine(Library.SidecarDir(ImagePath), file));
                    f.Add(file);
                }
                sb.AppendLine(string.Join("\t", f.ToArray()));
            }
            File.WriteAllText(ProjectPath, sb.ToString(), Encoding.UTF8);

            // Remove stale pasted-image files from earlier saves.
            foreach (var old in Directory.GetFiles(Library.SidecarDir(ImagePath), prefix + ".img*.png"))
                if (!keep.Contains(Path.GetFileName(old))) TryDelete(old);
        }

        private void ReadProject()
        {
            var lines = File.ReadAllLines(ProjectPath, Encoding.UTF8);
            for (int n = 1; n < lines.Length; n++)
            {
                if (lines[n].Length == 0) continue;
                try
                {
                    var f = lines[n].Split('\t');
                    var s = ShapeFactory.Create(f[0]);
                    if (s == null) continue;
                    int i = 1;
                    s.Read(f, ref i);
                    var img = s as ImageShape;
                    if (img != null)
                    {
                        img.Image = ImageIO.Load(Path.Combine(Library.SidecarDir(ImagePath), f[i]));
                    }
                    Shapes.Add(s);
                }
                catch (Exception)
                {
                }
            }
        }

        private static void TryDelete(string path)
        {
            try { File.Delete(path); }
            catch (Exception) { }
        }
    }

    internal static class ImageIO
    {
        /// <summary>Loads an image without keeping the file locked.</summary>
        public static Bitmap Load(string path)
        {
            var bytes = File.ReadAllBytes(path);
            using (var ms = new MemoryStream(bytes))
            using (var img = Image.FromStream(ms))
            {
                var bmp = new Bitmap(img.Width, img.Height, Image.IsAlphaPixelFormat(img.PixelFormat) ? PixelFormat.Format32bppArgb : PixelFormat.Format24bppRgb);
                bmp.SetResolution(96, 96);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                    g.DrawImage(img, new Rectangle(0, 0, img.Width, img.Height));
                }
                return bmp;
            }
        }

        public static void Save(Image img, string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            using (var ms = new MemoryStream())
            {
                if (ext == ".jpg" || ext == ".jpeg")
                {
                    var codec = Array.Find(ImageCodecInfo.GetImageEncoders(), c => c.FormatID == ImageFormat.Jpeg.Guid);
                    using (var ep = new EncoderParameters(1))
                    {
                        ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 92L);
                        img.Save(ms, codec, ep);
                    }
                }
                else if (ext == ".bmp") img.Save(ms, ImageFormat.Bmp);
                else if (ext == ".gif") img.Save(ms, ImageFormat.Gif);
                else img.Save(ms, ImageFormat.Png);
                File.WriteAllBytes(path, ms.ToArray());
            }
        }
    }

    internal static class ClipboardHelper
    {
        public static bool CopyImage(Bitmap bmp)
        {
            try
            {
                var data = new DataObject();
                data.SetData(DataFormats.Bitmap, true, bmp);
                var png = new MemoryStream();
                bmp.Save(png, ImageFormat.Png);
                data.SetData("PNG", false, png);
                Clipboard.SetDataObject(data, true, 10, 100);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static void CopyFile(string path, Bitmap bmp)
        {
            try
            {
                var data = new DataObject();
                if (bmp != null)
                {
                    data.SetData(DataFormats.Bitmap, true, bmp);
                    var png = new MemoryStream();
                    bmp.Save(png, ImageFormat.Png);
                    data.SetData("PNG", false, png);
                }
                var files = new System.Collections.Specialized.StringCollection();
                files.Add(path);
                data.SetFileDropList(files);
                Clipboard.SetDataObject(data, true, 10, 100);
            }
            catch (Exception)
            {
            }
        }

        public static Bitmap GetImage()
        {
            try
            {
                var data = Clipboard.GetDataObject();
                if (data == null) return null;
                if (data.GetDataPresent("PNG"))
                {
                    var s = data.GetData("PNG") as Stream;
                    if (s != null)
                    {
                        using (var img = Image.FromStream(s)) return new Bitmap(img);
                    }
                }
                if (Clipboard.ContainsImage())
                {
                    var img = Clipboard.GetImage();
                    if (img != null) using (img) return new Bitmap(img);
                }
                if (Clipboard.ContainsFileDropList())
                {
                    foreach (var f in Clipboard.GetFileDropList())
                        if (Library.IsImage(f)) return ImageIO.Load(f);
                }
            }
            catch (Exception)
            {
            }
            return null;
        }
    }
}

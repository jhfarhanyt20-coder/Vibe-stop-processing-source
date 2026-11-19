using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Net;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using Microsoft.Win32;

[assembly: AssemblyTitle("Vibe Stop Processing")]
[assembly: AssemblyProduct("Vibe Stop Processing")]
[assembly: AssemblyCompany("Vibe")]
[assembly: AssemblyDescription("PC Shutdown Assistant")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Vibe. All rights reserved.")]
[assembly: AssemblyVersion("2.5.1.0")]
[assembly: AssemblyFileVersion("2.5.1.0")]

namespace StopProcessing
{
    // ------------------------------------------------------------------
    //  App info, settings and the GitHub-Releases based updater
    // ------------------------------------------------------------------
    static class AppInfo
    {
        public const string Owner = "jhfarhanyt20-coder";
        public const string Repo = "Vibe-stop-processing";
        public const string AssetName = "StopProcessing.exe";

        public static Version Version
        {
            get { return Assembly.GetExecutingAssembly().GetName().Version; }
        }

        public static string VersionText
        {
            get { var v = Version; return v.Major + "." + v.Minor + "." + Math.Max(0, v.Build); }
        }

        public static string RepoUrl
        {
            get { return "https://github.com/" + Owner + "/" + Repo; }
        }

        public static string DataDir
        {
            get
            {
                string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StopProcessing");
                Directory.CreateDirectory(d);
                return d;
            }
        }
    }

    class AppSettings
    {
        public bool AutoUpdate = true;
        public bool StartWithWindows = true;
        public bool CloseToTray = true;
        public bool TrayHintShown = false;
        public DateTime LastCheck = DateTime.MinValue;

        private static string FilePath { get { return Path.Combine(AppInfo.DataDir, "settings.ini"); } }

        public static AppSettings Load()
        {
            var s = new AppSettings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    int i = line.IndexOf('=');
                    if (i <= 0) continue;
                    string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                    if (k == "AutoUpdate") s.AutoUpdate = v != "0";
                    else if (k == "StartWithWindows") s.StartWithWindows = v != "0";
                    else if (k == "CloseToTray") s.CloseToTray = v != "0";
                    else if (k == "TrayHintShown") s.TrayHintShown = v != "0";
                    else if (k == "LastCheck")
                    {
                        long ticks;
                        if (long.TryParse(v, out ticks)) s.LastCheck = new DateTime(ticks, DateTimeKind.Utc);
                    }
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                File.WriteAllLines(FilePath, new[]
                {
                    "AutoUpdate=" + (AutoUpdate ? "1" : "0"),
                    "StartWithWindows=" + (StartWithWindows ? "1" : "0"),
                    "CloseToTray=" + (CloseToTray ? "1" : "0"),
                    "TrayHintShown=" + (TrayHintShown ? "1" : "0"),
                    "LastCheck=" + LastCheck.Ticks
                });
            }
            catch { }
        }
    }

    class UpdateInfo
    {
        public Version Version;
        public string Tag, Notes, Url, Sha256;
        public long Size;
    }

    static class Updater
    {
        private static string Exe { get { return Application.ExecutablePath; } }

        private static Version Norm(Version v)
        {
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }

        private static HttpWebRequest Request(string url)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "StopProcessing-Updater/" + AppInfo.VersionText;
            req.Timeout = 20000;
            req.ReadWriteTimeout = 30000;
            return req;
        }

        /// Reads latest.json from the newest GitHub release (no API rate limit).
        /// Falls back to the GitHub API if the release has no latest.json.
        public static UpdateInfo FetchLatest()
        {
            try
            {
                // 1. find the newest tag from the /releases/latest redirect (not rate limited)
                // follow redirects by hand (also covers a renamed repository) until we reach .../tag/<version>
                string location = "https://github.com/" + AppInfo.Owner + "/" + AppInfo.Repo + "/releases/latest";
                for (int hop = 0; hop < 5 && location.IndexOf("/tag/") < 0; hop++)
                {
                    var head = Request(location);
                    head.AllowAutoRedirect = false;
                    head.Method = "HEAD";
                    string next;
                    using (var r = (HttpWebResponse)head.GetResponse())
                        next = r.Headers["Location"];
                    if (string.IsNullOrEmpty(next)) break;
                    location = new Uri(new Uri(location), next).ToString();
                }
                if (location.IndexOf("/tag/") < 0) return FetchLatestFromApi();
                string repoBase = location.Substring(0, location.IndexOf("/releases/"));
                string tag = Uri.UnescapeDataString(location.Substring(location.LastIndexOf("/tag/") + 5));

                // 2. read that release's latest.json
                var req = Request(repoBase + "/releases/download/" + tag + "/latest.json");
                string json;
                using (var resp = req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream()))
                    json = sr.ReadToEnd();
                var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                Version v;
                if (!Version.TryParse(Convert.ToString(d["version"]), out v)) return null;
                return new UpdateInfo
                {
                    Version = v,
                    Tag = "v" + Convert.ToString(d["version"]),
                    Notes = d.ContainsKey("notes") ? Convert.ToString(d["notes"]) : "",
                    Url = Convert.ToString(d["url"]),
                    Size = d.ContainsKey("size") ? Convert.ToInt64(d["size"]) : 0,
                    Sha256 = d.ContainsKey("sha256") ? Convert.ToString(d["sha256"]).ToLowerInvariant() : null
                };
            }
            catch (WebException)
            {
                return FetchLatestFromApi();
            }
        }

        private static UpdateInfo FetchLatestFromApi()
        {
            var req = Request("https://api.github.com/repos/" + AppInfo.Owner + "/" + AppInfo.Repo + "/releases/latest");
            req.Accept = "application/vnd.github+json";
            string json;
            try
            {
                using (var resp = req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream()))
                    json = sr.ReadToEnd();
            }
            catch (WebException ex)
            {
                var r = ex.Response as HttpWebResponse;
                if (r != null && r.StatusCode == HttpStatusCode.NotFound) return null; // no releases yet
                throw;
            }

            var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            string tag = Convert.ToString(d["tag_name"]);
            Version v;
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out v)) return null;

            var info = new UpdateInfo
            {
                Version = v,
                Tag = tag,
                Notes = d.ContainsKey("body") ? Convert.ToString(d["body"]) : ""
            };
            var assets = d.ContainsKey("assets") ? d["assets"] as System.Collections.IEnumerable : null;
            if (assets != null)
            {
                foreach (object o in assets)
                {
                    var a = o as Dictionary<string, object>;
                    if (a == null || !string.Equals(Convert.ToString(a["name"]), AppInfo.AssetName, StringComparison.OrdinalIgnoreCase)) continue;
                    info.Url = Convert.ToString(a["browser_download_url"]);
                    info.Size = Convert.ToInt64(a["size"]);
                    string digest = a.ContainsKey("digest") ? Convert.ToString(a["digest"]) : null;
                    if (!string.IsNullOrEmpty(digest) && digest.StartsWith("sha256:")) info.Sha256 = digest.Substring(7).ToLowerInvariant();
                }
            }
            return info;
        }

        public static bool IsNewer(UpdateInfo i)
        {
            return i != null && !string.IsNullOrEmpty(i.Url) && Norm(i.Version) > Norm(AppInfo.Version);
        }

        /// Downloads the new exe next to the current one and verifies it.
        public static string Download(UpdateInfo i, Action<int> percent)
        {
            string tmp = Exe + ".download";
            if (File.Exists(tmp)) File.Delete(tmp);

            var req = Request(i.Url);
            using (var resp = req.GetResponse())
            using (var src = resp.GetResponseStream())
            using (var dst = File.Create(tmp))
            {
                long total = resp.ContentLength > 0 ? resp.ContentLength : i.Size;
                var buf = new byte[81920];
                long got = 0;
                int n, last = -1;
                while ((n = src.Read(buf, 0, buf.Length)) > 0)
                {
                    dst.Write(buf, 0, n);
                    got += n;
                    if (total > 0)
                    {
                        int p = (int)(got * 100 / total);
                        if (p != last) { last = p; percent(p); }
                    }
                }
            }

            var fi = new FileInfo(tmp);
            if (i.Size > 0 && fi.Length != i.Size) { File.Delete(tmp); throw new Exception("Download was incomplete."); }

            using (var fs = File.OpenRead(tmp))
            {
                if (fs.ReadByte() != 'M' || fs.ReadByte() != 'Z') { fs.Close(); File.Delete(tmp); throw new Exception("Downloaded file is not a valid program."); }
            }

            if (!string.IsNullOrEmpty(i.Sha256))
            {
                string hash;
                using (var sha = SHA256.Create())
                using (var fs = File.OpenRead(tmp))
                    hash = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
                if (hash != i.Sha256) { File.Delete(tmp); throw new Exception("Checksum did not match \u2014 update rejected."); }
            }
            return tmp;
        }

        /// Swaps the running exe for the downloaded one (Windows allows renaming a running exe).
        /// Keeps "Apps & features" showing the right version after an in-app update.
        public static void UpdateInstalledVersion(Version v)
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{8F3C2A71-5B4E-4D2A-9C61-7E2B5A9D4F10}_is1", true))
                {
                    if (k != null) k.SetValue("DisplayVersion", v.Major + "." + v.Minor + "." + Math.Max(0, v.Build));
                }
            }
            catch { }
        }

        public static void Apply(string downloaded)
        {
            string old = Exe + ".old";
            try { if (File.Exists(old)) File.Delete(old); } catch { }
            if (File.Exists(old)) old = Exe + "." + DateTime.Now.Ticks + ".old";
            File.Move(Exe, old);
            try
            {
                File.Move(downloaded, Exe);
            }
            catch
            {
                File.Move(old, Exe); // roll back
                throw;
            }
        }

        public static void CleanupOld()
        {
            try
            {
                string dir = Path.GetDirectoryName(Exe);
                string name = Path.GetFileName(Exe);
                foreach (var f in Directory.GetFiles(dir, name + ".*"))
                    if (f.EndsWith(".old") || f.EndsWith(".download"))
                        try { File.Delete(f); } catch { }
            }
            catch { }
        }

        public static void Restart(bool toTray)
        {
            Process.Start(new ProcessStartInfo(Exe, toTray ? "--updated --tray" : "--updated") { UseShellExecute = true });
        }
    }

    /// Starts the app in the tray at sign-in. A scheduled task is used because the app needs
    /// admin rights, and Windows skips elevated programs in the normal Startup/Run list.
    static class StartupTask
    {
        private static string TaskName
        {
            get { return "VibeStopProcessing_" + Environment.UserName; }
        }

        public static bool Exists()
        {
            int code;
            Native.Run("schtasks.exe", "/query /tn \"" + TaskName + "\"", out code);
            return code == 0;
        }

        public static string Enable()
        {
            string user = WindowsIdentity.GetCurrent().Name;
            string exe = Application.ExecutablePath;
            string xml =
                "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
                "  <RegistrationInfo><Author>Vibe</Author><Description>Starts Vibe Stop Processing in the system tray when you sign in.</Description></RegistrationInfo>\r\n" +
                "  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + Esc(user) + "</UserId><Delay>PT10S</Delay></LogonTrigger></Triggers>\r\n" +
                "  <Principals><Principal id=\"Author\"><UserId>" + Esc(user) + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>\r\n" +
                "  <Settings>\r\n" +
                "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n" +
                "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n" +
                "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n" +
                "    <AllowHardTerminate>true</AllowHardTerminate>\r\n" +
                "    <StartWhenAvailable>false</StartWhenAvailable>\r\n" +
                "    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>\r\n" +
                "    <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>\r\n" +
                "    <AllowStartOnDemand>true</AllowStartOnDemand>\r\n" +
                "    <Enabled>true</Enabled>\r\n" +
                "    <Hidden>false</Hidden>\r\n" +
                "    <RunOnlyIfIdle>false</RunOnlyIfIdle>\r\n" +
                "    <WakeToRun>false</WakeToRun>\r\n" +
                "    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\r\n" +
                "    <Priority>7</Priority>\r\n" +
                "  </Settings>\r\n" +
                "  <Actions Context=\"Author\"><Exec><Command>" + Esc(exe) + "</Command><Arguments>--tray</Arguments></Exec></Actions>\r\n" +
                "</Task>\r\n";

            string file = Path.Combine(Path.GetTempPath(), "VibeStopProcessing_task.xml");
            File.WriteAllText(file, xml, System.Text.Encoding.Unicode);
            int code;
            string output = Native.Run("schtasks.exe", "/create /tn \"" + TaskName + "\" /xml \"" + file + "\" /f", out code);
            try { File.Delete(file); } catch { }
            return code == 0 ? null : output.Trim();
        }

        public static void Disable()
        {
            int code;
            Native.Run("schtasks.exe", "/delete /tn \"" + TaskName + "\" /f", out code);
        }

        private static string Esc(string s)
        {
            return System.Security.SecurityElement.Escape(s);
        }
    }

    class DarkMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Theme.Surface; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Surface; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Surface; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Surface; } }
        public override Color MenuBorder { get { return Theme.Line; } }
        public override Color MenuItemBorder { get { return Theme.CardHi; } }
        public override Color MenuItemSelected { get { return Theme.CardHi; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.CardHi; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.CardHi; } }
        public override Color SeparatorDark { get { return Theme.Line; } }
        public override Color SeparatorLight { get { return Theme.Line; } }
    }

    class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkMenuColors()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = !e.Item.Enabled ? Theme.Faint : e.Item.ForeColor == Theme.Red ? Theme.Red : Theme.Text;
            base.OnRenderItemText(e);
        }
    }

    // ------------------------------------------------------------------
    //  Theme
    // ------------------------------------------------------------------
    static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(16, 18, 22);
        public static readonly Color Surface = Color.FromArgb(22, 25, 30);
        public static readonly Color Card = Color.FromArgb(29, 33, 40);
        public static readonly Color CardHi = Color.FromArgb(37, 42, 50);
        public static readonly Color Inset = Color.FromArgb(23, 26, 32);
        public static readonly Color Line = Color.FromArgb(46, 51, 60);
        public static readonly Color Text = Color.FromArgb(232, 235, 241);
        public static readonly Color Soft = Color.FromArgb(196, 201, 210);
        public static readonly Color Muted = Color.FromArgb(136, 144, 158);
        public static readonly Color Faint = Color.FromArgb(92, 99, 112);
        public static readonly Color Red = Color.FromArgb(229, 72, 66);
        public static readonly Color Blue = Color.FromArgb(62, 128, 250);
        public static readonly Color Green = Color.FromArgb(70, 196, 126);
        public static readonly Color Amber = Color.FromArgb(238, 170, 70);
        public static readonly Color Secondary = Color.FromArgb(44, 49, 58);
        public static readonly Color Disabled = Color.FromArgb(40, 44, 52);

        public const string IconFont = "Segoe MDL2 Assets";

        private static readonly Dictionary<string, Font> cache = new Dictionary<string, Font>();

        public static Font F(float size, FontStyle style = FontStyle.Regular)
        {
            return Get("Segoe UI", size, style);
        }

        public static Font Semi(float size)
        {
            return Get("Segoe UI Semibold", size, FontStyle.Regular);
        }

        public static Font Mono(float size)
        {
            return Get("Consolas", size, FontStyle.Regular);
        }

        public static Font Icon(float size)
        {
            return Get(IconFont, size, FontStyle.Regular);
        }

        private static Font Get(string family, float size, FontStyle style)
        {
            string key = family + "|" + size + "|" + (int)style;
            Font f;
            if (!cache.TryGetValue(key, out f))
            {
                f = new Font(family, size, style);
                cache[key] = f;
            }
            return f;
        }

        public static Color Mix(Color a, Color b, double t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        public static Color Lighten(Color c, double t) { return Mix(c, Color.White, t); }
        public static Color Darken(Color c, double t) { return Mix(c, Color.Black, t); }

        public static GraphicsPath Round(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            if (radius <= 0 || r.Width <= 0 || r.Height <= 0)
            {
                p.AddRectangle(r);
                return p;
            }
            int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Color c, Rectangle r, int radius)
        {
            using (var p = Round(r, radius))
            using (var b = new SolidBrush(c))
                g.FillPath(b, p);
        }

        public static string FormatBytes(long bytes)
        {
            double v = bytes;
            if (v < 1024) return bytes + " B";
            v /= 1024;
            if (v < 1024) return v.ToString("0") + " KB";
            v /= 1024;
            if (v < 1024) return v.ToString("0.0") + " MB";
            v /= 1024;
            return v.ToString("0.00") + " GB";
        }
    }

    static class Native
    {
        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int RegisterWindowMessage(string name);

        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        public static extern int SetWindowTheme(IntPtr hWnd, string appName, string idList);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SHEmptyRecycleBin(IntPtr hwnd, string rootPath, uint flags);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SHQueryRecycleBin(string rootPath, ref SHQUERYRBINFO info);

        [StructLayout(LayoutKind.Sequential)]
        public struct SHQUERYRBINFO
        {
            public int cbSize;
            public long i64Size;
            public long i64NumItems;
        }

        public static void DarkScroll(Control c)
        {
            try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { }
        }

        public static void DragWindow(Form f)
        {
            ReleaseCapture();
            SendMessage(f.Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
        }

        public static string Run(string file, string args, out int exitCode)
        {
            exitCode = -1;
            try
            {
                var psi = new ProcessStartInfo(file, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var p = Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd();
                    string e = p.StandardError.ReadToEnd();
                    p.WaitForExit(15000);
                    exitCode = p.ExitCode;
                    return o + e;
                }
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }

    // ------------------------------------------------------------------
    //  Custom controls
    // ------------------------------------------------------------------

    /// Panel that lets the window edges stay resizable when it touches them.
    class EdgePanel : Panel
    {
        public EdgePanel()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x84)
            {
                var f = FindForm();
                if (f != null && f.WindowState == FormWindowState.Normal && f.FormBorderStyle == FormBorderStyle.None)
                {
                    long lp = m.LParam.ToInt64();
                    var sp = new Point((short)(lp & 0xffff), (short)((lp >> 16) & 0xffff));
                    var fp = f.PointToClient(sp);
                    const int g = 6;
                    if (fp.X < g || fp.Y < g || fp.X >= f.ClientSize.Width - g || fp.Y >= f.ClientSize.Height - g)
                    {
                        m.Result = (IntPtr)(-1); // HTTRANSPARENT -> let the form decide
                        return;
                    }
                }
            }
            base.WndProc(ref m);
        }
    }

    class Card : Panel
    {
        public int Radius = 12;

        public Card()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
            Padding = new Padding(18);
            Margin = new Padding(0);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.FillRound(g, BackColor, new Rectangle(0, 0, Width - 1, Height - 1), Radius);
        }
    }

    class StatCard : Card
    {
        private string caption, value, hint;
        public Color Accent = Theme.Blue;

        public StatCard(string caption, string value, string hint, Color accent)
        {
            this.caption = caption;
            this.value = value;
            this.hint = hint;
            Accent = accent;
        }

        public string Value
        {
            get { return value; }
            set { if (this.value != value) { this.value = value; Invalidate(); } }
        }

        public string Hint
        {
            get { return hint; }
            set { if (hint != value) { hint = value; Invalidate(); } }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(Accent))
                g.FillEllipse(b, 18, 21, 7, 7);
            TextRenderer.DrawText(g, caption, Theme.Semi(8.5f), new Point(31, 16), Theme.Muted);
            TextRenderer.DrawText(g, value, Theme.F(19f, FontStyle.Bold), new Point(14, 36), Theme.Text);
            TextRenderer.DrawText(g, hint, Theme.F(8.5f), new Rectangle(17, Height - 28, Width - 30, 18),
                Theme.Faint, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }

    class FlatButton : Control
    {
        public Color Fill = Theme.Blue, HoverFill, DownFill;
        public Color Fore = Color.White;
        public string Glyph;
        public int Radius = 8;
        private bool hot, down;

        public FlatButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            Font = Theme.Semi(10f);
            Height = 44;
            Margin = new Padding(0);
            SetColor(Theme.Blue);
        }

        public FlatButton SetColor(Color c)
        {
            Fill = c;
            HoverFill = Theme.Lighten(c, 0.10);
            DownFill = Theme.Darken(c, 0.12);
            Invalidate();
            return this;
        }

        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color fill = !Enabled ? Theme.Disabled : down ? DownFill : hot ? HoverFill : Fill;
            Theme.FillRound(g, fill, new Rectangle(0, 0, Width - 1, Height - 1), Radius);

            Color fc = Enabled ? Fore : Theme.Faint;
            var tf = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
            Size ts = TextRenderer.MeasureText(g, Text, Font, Size.Empty, tf);
            int gw = 0;
            Font gf = Theme.Icon(Font.Size * 1.0f);
            if (!string.IsNullOrEmpty(Glyph))
                gw = TextRenderer.MeasureText(g, Glyph, gf, Size.Empty, tf).Width + 10;
            int x = (Width - (ts.Width + gw)) / 2;
            if (gw > 0)
                TextRenderer.DrawText(g, Glyph, gf, new Rectangle(x, 1, gw, Height), fc, tf);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x + gw, 0, ts.Width + 4, Height), fc, tf);
        }
    }

    class CheckToggle : Control
    {
        private bool chk;
        public event EventHandler CheckedChanged;

        public CheckToggle()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            Font = Theme.F(9.5f);
            Height = 30;
            Margin = new Padding(0);
        }

        public bool Checked
        {
            get { return chk; }
            set
            {
                if (chk == value) return;
                chk = value;
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnClick(EventArgs e)
        {
            if (Enabled) Checked = !Checked;
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            DrawBox(g, new Rectangle(0, (Height - 18) / 2, 18, 18), chk);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(28, 0, Width - 28, Height), Theme.Soft,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }

        public static void DrawBox(Graphics g, Rectangle r, bool on)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (on)
            {
                Theme.FillRound(g, Theme.Blue, r, 5);
                TextRenderer.DrawText(g, "\uE73E", Theme.Icon(9f), r, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            else
            {
                using (var p = Theme.Round(r, 5))
                using (var pen = new Pen(Theme.Faint, 1.5f))
                    g.DrawPath(pen, p);
            }
        }
    }

    class SlimBar : Control
    {
        private int val, max = 100;
        public Color Bar = Theme.Blue;

        public SlimBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 6;
            Margin = new Padding(0);
        }

        public int Maximum { get { return max; } set { max = Math.Max(1, value); Invalidate(); } }
        public int Value { get { return val; } set { val = Math.Max(0, Math.Min(max, value)); Invalidate(); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int h = Math.Min(6, Height);
            int y = (Height - h) / 2;
            Theme.FillRound(g, Theme.Inset, new Rectangle(0, y, Width - 1, h), h / 2);
            int w = (int)((Width - 1) * (val / (double)max));
            if (w > h) Theme.FillRound(g, Bar, new Rectangle(0, y, w, h), h / 2);
        }
    }

    class PageHeader : Control
    {
        private string sub;

        public PageHeader(string title, string subtitle)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Text = title;
            sub = subtitle;
            Margin = new Padding(0);
        }

        public string Subtitle
        {
            get { return sub; }
            set { if (sub != value) { sub = value; Invalidate(); } }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Bg);
            TextRenderer.DrawText(g, Text, Theme.F(18f, FontStyle.Bold), new Point(-2, 0), Theme.Text);
            TextRenderer.DrawText(g, sub, Theme.F(9.5f), new Rectangle(0, 38, Width, 22), Theme.Muted,
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }

    class NavItem : Control
    {
        public string Glyph;
        private bool sel, hot;

        public NavItem(string glyph, string text)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Glyph = glyph;
            Text = text;
            Cursor = Cursors.Hand;
            Height = 42;
        }

        public bool Selected { get { return sel; } set { sel = value; Invalidate(); } }

        private bool badge;
        public bool Badge { get { return badge; } set { badge = value; Invalidate(); } }

        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Surface);
            if (badge)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var b = new SolidBrush(Theme.Blue)) g.FillEllipse(b, Width - 22, (Height - 8) / 2, 8, 8);
            }
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (sel) Theme.FillRound(g, Color.FromArgb(36, 41, 50), new Rectangle(0, 0, Width - 1, Height - 1), 8);
            else if (hot) Theme.FillRound(g, Color.FromArgb(29, 33, 40), new Rectangle(0, 0, Width - 1, Height - 1), 8);
            if (sel) Theme.FillRound(g, Theme.Blue, new Rectangle(0, 11, 3, Height - 22), 2);

            Color c = sel ? Theme.Text : hot ? Theme.Soft : Theme.Muted;
            var tf = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
            TextRenderer.DrawText(g, Glyph, Theme.Icon(12f), new Rectangle(16, 1, 24, Height), sel ? Theme.Blue : c, tf);
            TextRenderer.DrawText(g, Text, sel ? Theme.Semi(10f) : Theme.F(10f), new Rectangle(48, 0, Width - 50, Height), c, tf);
        }
    }

    class CaptionButton : Control
    {
        public string Glyph;
        public bool IsClose;
        private bool hot;

        public CaptionButton(string glyph, bool isClose)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Glyph = glyph;
            IsClose = isClose;
            Size = new Size(46, 40);
        }

        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(hot ? (IsClose ? Color.FromArgb(196, 43, 28) : Color.FromArgb(40, 45, 54)) : Theme.Surface);
            TextRenderer.DrawText(g, Glyph, Theme.Icon(7.5f), ClientRectangle, hot ? Color.White : Theme.Soft,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    /// One selectable cleanup location.
    class CleanRow : Control
    {
        public string Glyph, Desc;
        private string size = "\u2014", status = "";
        private bool chk, hot;

        public CleanRow(string glyph, string title, string desc, bool on)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Glyph = glyph;
            Text = title;
            Desc = desc;
            chk = on;
            Height = 62;
            Dock = DockStyle.Top;
            Cursor = Cursors.Hand;
        }

        public bool Checked { get { return chk; } set { chk = value; Invalidate(); } }
        public string SizeText { get { return size; } set { size = value; Invalidate(); } }
        public string StatusText { get { return status; } set { status = value; Invalidate(); } }

        protected override void OnClick(EventArgs e) { if (Enabled) Checked = !Checked; base.OnClick(e); }
        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Card);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 3, Width - 1, Height - 7);
            if (hot) Theme.FillRound(g, Theme.CardHi, r, 10);

            CheckToggle.DrawBox(g, new Rectangle(14, (Height - 18) / 2, 18, 18), chk);
            Theme.FillRound(g, Color.FromArgb(38, 48, 66), new Rectangle(46, (Height - 36) / 2, 36, 36), 9);
            TextRenderer.DrawText(g, Glyph, Theme.Icon(12f), new Rectangle(46, (Height - 36) / 2, 36, 36), Theme.Blue,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            int right = Width - 16;
            var tf = TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
            TextRenderer.DrawText(g, Text, Theme.Semi(10f), new Rectangle(96, 12, right - 96 - 120, 20), chk ? Theme.Text : Theme.Muted, tf);
            TextRenderer.DrawText(g, Desc, Theme.F(8.5f), new Rectangle(96, 33, right - 96 - 120, 18), Theme.Faint, tf);
            TextRenderer.DrawText(g, size, Theme.Semi(10.5f), new Rectangle(right - 120, 12, 120, 20), chk ? Theme.Text : Theme.Muted,
                tf | TextFormatFlags.Right);
            TextRenderer.DrawText(g, status, Theme.F(8.5f), new Rectangle(right - 160, 33, 160, 18), Theme.Muted,
                tf | TextFormatFlags.Right);
        }
    }

    /// A list row with an on/off switch (used for startup apps).
    class ToggleRow : Control
    {
        public string Desc, Source;
        private bool on, hot;
        public event EventHandler Toggled;

        public ToggleRow(string title, string desc, string source, bool isOn)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Text = title;
            Desc = desc;
            Source = source;
            on = isOn;
            Height = 54;
            Dock = DockStyle.Top;
            Cursor = Cursors.Hand;
        }

        public bool On { get { return on; } set { on = value; Invalidate(); } }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (Enabled && Toggled != null) Toggled(this, EventArgs.Empty);
        }
        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Card);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (hot) Theme.FillRound(g, Theme.CardHi, new Rectangle(0, 2, Width - 1, Height - 5), 10);

            var tile = new Rectangle(12, (Height - 34) / 2, 34, 34);
            Theme.FillRound(g, on ? Color.FromArgb(38, 48, 66) : Color.FromArgb(40, 43, 50), tile, 9);
            TextRenderer.DrawText(g, "\uE71D", Theme.Icon(11f), tile, on ? Theme.Blue : Theme.Faint,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            // switch
            var sw = new Rectangle(Width - 14 - 40, (Height - 22) / 2, 40, 22);
            Theme.FillRound(g, on ? Theme.Blue : Theme.Secondary, sw, 11);
            int kx = on ? sw.Right - 19 : sw.X + 3;
            using (var b = new SolidBrush(on ? Color.White : Theme.Muted))
                g.FillEllipse(b, kx, sw.Y + 3, 16, 16);

            var tf = TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            int srcW = TextRenderer.MeasureText(g, Source, Theme.F(8f), Size.Empty, tf).Width;
            int textW = sw.X - 58 - srcW - 24;
            TextRenderer.DrawText(g, Text, Theme.Semi(10f), new Rectangle(58, 8, textW, 20), on ? Theme.Text : Theme.Muted, tf);
            TextRenderer.DrawText(g, Desc, Theme.F(8.5f), new Rectangle(58, 29, textW, 18), Theme.Faint, tf);
            TextRenderer.DrawText(g, Source, Theme.F(8f), new Rectangle(sw.X - srcW - 14, 0, srcW + 2, Height), Theme.Faint,
                tf | TextFormatFlags.VerticalCenter);
        }
    }

    /// A clickable card for one tool.
    class ToolTile : Control
    {
        public string Glyph, Desc;
        public bool Danger;
        private readonly Action run;
        private bool hot;

        public ToolTile(string glyph, string title, string desc, Action run)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Glyph = glyph;
            Text = title;
            Desc = desc;
            this.run = run;
            Cursor = Cursors.Hand;
        }

        protected override void OnClick(EventArgs e) { base.OnClick(e); if (run != null) run(); }
        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.FillRound(g, hot ? Theme.CardHi : Theme.Card, new Rectangle(0, 0, Width - 1, Height - 1), 12);

            var tile = new Rectangle(20, 20, 44, 44);
            Color accent = Danger ? Theme.Red : Theme.Blue;
            Theme.FillRound(g, Theme.Mix(Theme.Card, accent, 0.18), tile, 11);
            TextRenderer.DrawText(g, Glyph, Theme.Icon(15f), tile, accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            var tf = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
            TextRenderer.DrawText(g, Text, Theme.F(11.5f, FontStyle.Bold), new Rectangle(78, 22, Width - 96, 24), Theme.Text, tf | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, Desc, Theme.F(9f), new Rectangle(78, 48, Width - 96, Height - 60), Theme.Muted, tf | TextFormatFlags.WordBreak);

            TextRenderer.DrawText(g, "\uE76C", Theme.Icon(10f), new Rectangle(Width - 34, 26, 16, 16), hot ? Theme.Text : Theme.Faint, tf);
        }
    }

    /// Free space bars for every fixed drive.
    class DriveList : Control
    {
        private class DriveData { public string Name; public long Total, Free; }
        private List<DriveData> data = new List<DriveData>();
        private volatile bool loading;

        public DriveList()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        /// Reads drive sizes on a background thread: a sleeping hard disk can take seconds to answer.
        public void Reload()
        {
            if (loading) return;
            loading = true;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var list = new List<DriveData>();
                try
                {
                    foreach (var d in DriveInfo.GetDrives())
                    {
                        try
                        {
                            if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                            list.Add(new DriveData { Name = d.Name.TrimEnd('\\'), Total = d.TotalSize, Free = d.TotalFreeSpace });
                        }
                        catch { }
                    }
                }
                catch { }
                loading = false;
                if (IsDisposed || !IsHandleCreated) return;
                try { BeginInvoke(new Action(() => { data = list; Invalidate(); })); } catch { }
            });
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Card);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int y = 4;
            foreach (var d in data)
            {
                long total = d.Total, free = d.Free;
                if (y + 40 > Height) break;
                double used = total > 0 ? 1.0 - free / (double)total : 0;
                Color c = used > 0.9 ? Theme.Red : used > 0.75 ? Theme.Amber : Theme.Blue;
                var tf = TextFormatFlags.NoPadding;
                TextRenderer.DrawText(g, d.Name.TrimEnd('\\'), Theme.Semi(9.5f), new Point(0, y), Theme.Text, tf);
                string info = Theme.FormatBytes(free) + " free of " + Theme.FormatBytes(total);
                TextRenderer.DrawText(g, info, Theme.F(8.5f), new Rectangle(0, y, Width, 18), Theme.Muted, tf | TextFormatFlags.Right);
                Theme.FillRound(g, Theme.Inset, new Rectangle(0, y + 22, Width - 1, 6), 3);
                int w = (int)((Width - 1) * used);
                if (w > 6) Theme.FillRound(g, c, new Rectangle(0, y + 22, w, 6), 3);
                y += 40;
            }
        }
    }

    enum CheckState2 { Unknown, Ok, Risk }

    /// One power / wake cause with its current state.
    class StatusRow : Control
    {
        public string Glyph, Desc;
        private CheckState2 state = CheckState2.Unknown;
        private string stateText = "Checking\u2026";

        public StatusRow(string glyph, string title, string desc)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Glyph = glyph;
            Text = title;
            Desc = desc;
            Height = 50;
            Dock = DockStyle.Top;
        }

        public void SetState(CheckState2 s, string text)
        {
            state = s;
            stateText = text;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Card);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color c = state == CheckState2.Ok ? Theme.Green : state == CheckState2.Risk ? Theme.Amber : Theme.Muted;
            Theme.FillRound(g, Theme.Mix(Theme.Card, c, 0.16), new Rectangle(14, (Height - 36) / 2, 36, 36), 9);
            TextRenderer.DrawText(g, Glyph, Theme.Icon(12f), new Rectangle(14, (Height - 36) / 2, 36, 36), c,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            var tf = TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
            var pf = Theme.Semi(8.5f);
            Size ps = TextRenderer.MeasureText(g, stateText, pf, Size.Empty, TextFormatFlags.NoPadding);
            int pw = ps.Width + 22;
            var pill = new Rectangle(Width - 14 - pw, (Height - 24) / 2, pw, 24);
            Theme.FillRound(g, Theme.Mix(Theme.Card, c, 0.18), pill, 12);
            TextRenderer.DrawText(g, stateText, pf, pill, c,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            int textW = pill.X - 64 - 12;
            TextRenderer.DrawText(g, Text, Theme.Semi(10f), new Rectangle(64, 6, textW, 20), Theme.Text, tf);
            TextRenderer.DrawText(g, Desc, Theme.F(8.5f), new Rectangle(64, 26, textW, 18), Theme.Faint, tf);

            using (var pen = new Pen(Color.FromArgb(37, 41, 49)))
                g.DrawLine(pen, 14, Height - 1, Width - 14, Height - 1);
        }
    }

    // ------------------------------------------------------------------
    //  Themed dialog (replaces MessageBox)
    // ------------------------------------------------------------------
    class ThemedDialog : Form
    {
        private int result = -1;

        private int enterIndex;

        private ThemedDialog(string title, string message, string[] buttons, int primary, Color accent, int enter)
        {
            enterIndex = enter == -2 ? primary : enter;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            BackColor = Theme.Line;
            Padding = new Padding(1);
            KeyPreview = true;

            const int w = 460;
            int textW = w - 2 - 52;
            Font body = Theme.F(9.5f);
            Size ms = TextRenderer.MeasureText(message, body, new Size(textW, 0), TextFormatFlags.WordBreak);
            int h = 2 + 24 + 30 + 10 + ms.Height + 26 + 38 + 20;
            ClientSize = new Size(w, h);

            var inner = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface };
            inner.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) Native.DragWindow(this); };
            Controls.Add(inner);

            inner.Controls.Add(new Panel { BackColor = accent, Bounds = new Rectangle(0, 0, w - 2, 3) });
            inner.Controls.Add(new Label
            {
                Text = title,
                Font = Theme.F(12.5f, FontStyle.Bold),
                ForeColor = Theme.Text,
                AutoSize = true,
                Location = new Point(24, 24)
            });
            inner.Controls.Add(new Label
            {
                Text = message,
                Font = body,
                ForeColor = Theme.Soft,
                Location = new Point(25, 62),
                Size = new Size(textW + 4, ms.Height + 6)
            });

            int x = w - 2 - 24;
            int y = h - 2 - 20 - 38;
            for (int i = buttons.Length - 1; i >= 0; i--)
            {
                var b = new FlatButton { Text = buttons[i], Height = 38, Font = Theme.Semi(9.5f) };
                int bw = Math.Max(100, TextRenderer.MeasureText(buttons[i], b.Font).Width + 36);
                x -= bw;
                b.Bounds = new Rectangle(x, y, bw, 38);
                b.SetColor(i == primary ? accent : Theme.Secondary);
                int idx = i;
                b.Click += (s, e) => { result = idx; Close(); };
                inner.Controls.Add(b);
                x -= 10;
            }

            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape) { result = -1; Close(); }
                else if (e.KeyCode == Keys.Enter) { result = enterIndex; Close(); }
            };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= 0x20000; // CS_DROPSHADOW
                return cp;
            }
        }

        /// enterIndex: which button the Enter key presses (-2 = the primary one). Use a safe button for destructive dialogs.
        public static int Show(IWin32Window owner, string title, string message, string[] buttons, int primary, Color accent, int enterIndex = -2)
        {
            using (var d = new ThemedDialog(title, message, buttons, primary, accent, enterIndex))
            {
                d.ShowDialog(owner);
                return d.result;
            }
        }
    }

    // ------------------------------------------------------------------
    //  Main window
    // ------------------------------------------------------------------
    public class MainForm : Form
    {
        // shell
        private CaptionButton maxButton;
        private readonly List<NavItem> navItems = new List<NavItem>();
        private readonly List<Control> pages = new List<Control>();
        private int currentPage = -1;
        private readonly bool isAdmin;

        // processes page
        private PageHeader procHeader;
        private DataGridView grid;
        private FlatButton stopButton;
        private CheckToggle shutdownAfter;
        private SlimBar stopProgress;
        private ListBox stopLog;
        private StatCard statProc, statCpu, statMem, statStoppable;
        private System.Windows.Forms.Timer refreshTimer;
        private volatile bool isStopping;
        private CancellationTokenSource stopCancel;
        private readonly Dictionary<int, Tuple<TimeSpan, DateTime>> lastSample = new Dictionary<int, Tuple<TimeSpan, DateTime>>();

        // cleaner page
        private PageHeader cleanHeader;
        private readonly List<CleanTarget> cleanTargets = new List<CleanTarget>();
        private Label cleanTotal, cleanTotalHint, cleanSummary;
        private FlatButton scanButton, cleanButton;
        private CheckToggle askLocked;
        private SlimBar cleanProgress;
        private ListBox cleanLog;
        private volatile bool isCleaning;
        private bool scannedOnce;

        // power page
        private PageHeader powerHeader;
        private StatusRow rowNicWake, rowFastStartup, rowWakeTimers, rowWakeDevices, rowMaintenance, rowAutoReboot, rowArso;
        private FlatButton fixButton, restoreButton, recheckButton, fullShutdownButton;
        private ListBox powerLog;
        private volatile bool isPowerBusy;
        private bool powerCheckedOnce;

        private static readonly string[] CRITICAL = new string[]
        {
            "System", "Idle", "Registry", "Memory Compression", "smss", "csrss",
            "wininit", "winlogon", "services", "lsass", "lsm", "svchost", "spoolsv",
            "dwm", "explorer", "sihost", "taskhostw", "ctfmon", "fontdrvhost",
            "RuntimeBroker", "StartMenuExperienceHost", "ShellExperienceHost",
            "SearchApp", "SearchIndexer", "SearchProtocolHost", "SearchFilterHost",
            "TextInputHost", "SystemSettings", "ApplicationFrameHost",
            "SecurityHealthService", "SecurityHealthSystray", "MsMpEng", "NisSrv",
            "MpDefenderCoreService", "WinDefend", "audiodg", "WUDFHost",
            "dllhost", "wmiprvse", "WmiPrvSE", "winmgmt", "taskmgr", "LogonUI",
            "UserOOBEBroker", "CredentialUIBroker", "consent", "LockApp",
            "smartscreen", "backgroundTaskHost", "ShellHost", "shellexperiencehost",
            "Widgets", "WidgetService", "PhoneExperienceHost", "YourPhone",
            "nvcontainer", "NVDisplay.Container", "NvContainerLocalSystem",
            "atiesrxx", "atieclxx", "amdow", "RtkAudUService64", "igfxEM",
            "conhost", "powercfg", "shutdown"
        };

        private static readonly string[] IMPORTANT = new string[]
        {
            "chrome", "msedge", "firefox", "brave", "opera", "vivaldi",
            "Code", "devenv", "idea64", "pycharm64", "sublime_text",
            "WINWORD", "EXCEL", "POWERPNT", "OUTLOOK", "onenote", "mspaint",
            "Teams", "ms-teams", "Skype", "Discord", "Slack", "Telegram",
            "WhatsApp", "zoom", "OneDrive", "Dropbox", "GoogleDriveFS",
            "spotify", "vlc", "wmplayer", "notepad++", "notepad",
            "Photoshop", "Illustrator", "Premiere Pro", "AfterFX",
            "Steam", "EpicGamesLauncher", "Battle.net", "FreeFire", "HD-Player"
        };

        public MainForm()
        {
            isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

            Text = "Vibe Stop Processing";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1200, 760);
            MinimumSize = new Size(1020, 640);
            var wa = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Min(Width, wa.Width - 40), Math.Min(Height, wa.Height - 20));
            BackColor = Theme.Line; // 1px window border
            Padding = new Padding(1);
            ForeColor = Theme.Text;
            Font = Theme.F(9f);
            DoubleBuffered = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            BuildShell();
            BuildCleanTargets();

            pages.Add(BuildProcessesPage());
            pages.Add(BuildCleanerPage());
            pages.Add(BuildPowerPage());
            pages.Add(BuildToolsPage());
            pages.Add(BuildSettingsPage());
            foreach (var p in pages) contentHost.Controls.Add(p);

            int startPage = 0;
            var args = Environment.GetCommandLineArgs();
            foreach (var arg in args)
                if (arg.StartsWith("--page=")) int.TryParse(arg.Substring(7), out startPage);
            ShowPage(Math.Max(0, Math.Min(pages.Count - 1, startPage)));
            startInTray = args.Contains("--tray");
            justUpdated = args.Contains("--updated");

            BuildTray();
            StartTimer();

            // background start-up work runs from here (OnShown does not fire while hidden in the tray)
            var t = new System.Windows.Forms.Timer { Interval = 4000 };
            t.Tick += (s, ev) => { t.Stop(); t.Dispose(); CheckForUpdates(false); };
            t.Start();
            Updater.CleanupOld();
            SyncStartupTask();
        }

        // ---------------- tray ----------------

        public static readonly int WM_SHOWME = Native.RegisterWindowMessage("VibeStopProcessing_ShowWindow");
        private NotifyIcon tray;
        private bool startInTray, justUpdated, allowVisible, reallyExit;

        private void BuildTray()
        {
            var menu = new ContextMenuStrip
            {
                Renderer = new DarkMenuRenderer(),
                Font = Theme.F(9.5f),
                ShowImageMargin = false,
                BackColor = Theme.Surface,
                ForeColor = Theme.Text,
                Padding = new Padding(2, 4, 2, 4)
            };
            Func<string, EventHandler, ToolStripMenuItem> item = (text, onClick) =>
            {
                var mi = new ToolStripMenuItem(text, null, onClick) { Padding = new Padding(8, 5, 8, 5), ForeColor = Theme.Text };
                menu.Items.Add(mi);
                return mi;
            };
            var open = item("Open Vibe Stop Processing", (s, e) => ShowFromTray(-1));
            open.Font = Theme.F(9.5f, FontStyle.Bold);
            menu.Items.Add(new ToolStripSeparator());
            item("Processes", (s, e) => ShowFromTray(0));
            item("Cleaner", (s, e) => ShowFromTray(1));
            item("Power Fix", (s, e) => ShowFromTray(2));
            item("Tools", (s, e) => ShowFromTray(3));
            item("Settings", (s, e) => ShowFromTray(SettingsPage));
            menu.Items.Add(new ToolStripSeparator());
            var shut = item("Shut Down PC", (s, e) => { ShowFromTray(-1); FullShutdown(); });
            shut.ForeColor = Theme.Red;
            menu.Items.Add(new ToolStripSeparator());
            item("Exit", (s, e) => ExitApp());

            tray = new NotifyIcon
            {
                Icon = Icon ?? SystemIcons.Application,
                Text = "Vibe Stop Processing",
                ContextMenuStrip = menu,
                Visible = true
            };
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowFromTray(-1); };
        }

        protected override void SetVisibleCore(bool value)
        {
            if (!allowVisible)
            {
                allowVisible = true;
                if (startInTray)
                {
                    if (!IsHandleCreated) CreateHandle();
                    value = false;
                    if (justUpdated)
                        tray.ShowBalloonTip(4000, "Vibe Stop Processing updated",
                            "Now running version " + AppInfo.VersionText + ".", ToolTipIcon.Info);
                }
            }
            base.SetVisibleCore(value);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (justUpdated)
                ThemedDialog.Show(this, "Update installed",
                    "Vibe Stop Processing was updated to version " + AppInfo.VersionText + ".", new[] { "OK" }, 0, Theme.Green);
        }

        public void ShowFromTray(int page)
        {
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            if (page >= 0) ShowPage(page);
            Activate();
            BringToFront();
            if (currentPage == 0) RefreshGrid();
        }

        private void ExitApp()
        {
            reallyExit = true;
            if (!Visible && (isStopping || isCleaning)) ShowFromTray(-1);
            Close();
            reallyExit = false; // only reached if closing was cancelled
        }

        private void SyncStartupTask()
        {
            if (!isAdmin) return;
            bool want = settings.StartWithWindows;
            var th = new Thread(() =>
            {
                // re-registering on every start keeps the task pointing at the current exe location
                if (want) StartupTask.Enable();
                else if (StartupTask.Exists()) StartupTask.Disable();
            });
            th.IsBackground = true;
            th.Start();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= 0x20000;            // CS_DROPSHADOW
                cp.Style |= 0x00020000 | 0x00080000;  // WS_MINIMIZEBOX | WS_SYSMENU (taskbar minimize)
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_SHOWME && WM_SHOWME != 0)
            {
                ShowFromTray(-1);
                return;
            }
            if (m.Msg == 0x84 && WindowState == FormWindowState.Normal)
            {
                base.WndProc(ref m);
                long lp = m.LParam.ToInt64();
                var p = PointToClient(new Point((short)(lp & 0xffff), (short)((lp >> 16) & 0xffff)));
                const int g = 6;
                bool l = p.X < g, r = p.X >= ClientSize.Width - g, t = p.Y < g, b = p.Y >= ClientSize.Height - g;
                if (t && l) m.Result = (IntPtr)13;
                else if (t && r) m.Result = (IntPtr)14;
                else if (b && l) m.Result = (IntPtr)16;
                else if (b && r) m.Result = (IntPtr)17;
                else if (l) m.Result = (IntPtr)10;
                else if (r) m.Result = (IntPtr)11;
                else if (t) m.Result = (IntPtr)12;
                else if (b) m.Result = (IntPtr)15;
                return;
            }
            base.WndProc(ref m);
        }

        // ---------------- shell ----------------

        private EdgePanel contentHost;
        private const int SettingsPage = 4;

        private void BuildShell()
        {
            contentHost = new EdgePanel { Dock = DockStyle.Fill, BackColor = Theme.Bg };

            // nav rail
            var nav = new NavPanel { Dock = DockStyle.Left, Width = 214, BackColor = Theme.Surface };
            nav.Size = new Size(214, 700);
            string[][] items =
            {
                new[] { "\uE9D9", "Processes" },
                new[] { "\uE74D", "Cleaner" },
                new[] { "\uE7E8", "Power Fix" },
                new[] { "\uE90F", "Tools" },
                new[] { "\uE713", "Settings" }
            };
            for (int i = 0; i < items.Length; i++)
            {
                var ni = new NavItem(items[i][0], items[i][1])
                {
                    Bounds = new Rectangle(12, 44 + i * 48, nav.Width - 25, 42),
                    Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
                };
                int idx = i;
                ni.Click += (s, e) => ShowPage(idx);
                navItems.Add(ni);
                nav.Controls.Add(ni);
            }

            var quickShutdown = new FlatButton
            {
                Text = "Shut Down PC",
                Glyph = "\uE7E8",
                Bounds = new Rectangle(14, nav.Height - 16 - 44, nav.Width - 29, 44),
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                Font = Theme.Semi(9.5f)
            };
            quickShutdown.SetColor(Theme.Red);
            quickShutdown.Click += (s, e) => FullShutdown();
            nav.Controls.Add(quickShutdown);

            // status bar
            var status = new StatusStrip2(isAdmin) { Dock = DockStyle.Bottom, Height = 26 };

            // title bar
            var title = new TitleBar(this) { Dock = DockStyle.Top, Height = 40 };
            var closeB = new CaptionButton("\uE8BB", true) { Dock = DockStyle.Right };
            maxButton = new CaptionButton("\uE922", false) { Dock = DockStyle.Right };
            var minB = new CaptionButton("\uE921", false) { Dock = DockStyle.Right };
            closeB.Click += (s, e) => Close();
            maxButton.Click += (s, e) => ToggleMaximize();
            minB.Click += (s, e) => WindowState = FormWindowState.Minimized;
            title.Controls.Add(minB);
            title.Controls.Add(maxButton);
            title.Controls.Add(closeB);

            Controls.Add(contentHost);
            Controls.Add(nav);
            Controls.Add(status);
            Controls.Add(title);
        }

        public void ToggleMaximize()
        {
            if (WindowState == FormWindowState.Maximized)
            {
                WindowState = FormWindowState.Normal;
            }
            else
            {
                var wa = Screen.FromHandle(Handle).WorkingArea;
                var sb = Screen.FromHandle(Handle).Bounds;
                MaximizedBounds = new Rectangle(wa.X - sb.X, wa.Y - sb.Y, wa.Width, wa.Height);
                WindowState = FormWindowState.Maximized;
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (maxButton != null)
            {
                maxButton.Glyph = WindowState == FormWindowState.Maximized ? "\uE923" : "\uE922";
                maxButton.Invalidate();
                Padding = WindowState == FormWindowState.Maximized ? new Padding(0) : new Padding(1);
            }
        }

        private void ShowPage(int idx)
        {
            if (idx == currentPage) return;
            currentPage = idx;
            for (int i = 0; i < pages.Count; i++) pages[i].Visible = i == idx;
            for (int i = 0; i < navItems.Count; i++) navItems[i].Selected = i == idx;

            if (idx == 1 && !scannedOnce) { scannedOnce = true; StartScan(); }
            if (idx == 2 && !powerCheckedOnce) { powerCheckedOnce = true; CheckPowerAsync(); }
            if (idx == 3) RefreshPcInfo();
        }

        private EdgePanel NewPage()
        {
            return new EdgePanel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.Bg,
                Padding = new Padding(26, 20, 26, 22),
                Visible = false
            };
        }

        private static TableLayoutPanel Table(int cols, int rows, Color back)
        {
            return new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = cols,
                RowCount = rows,
                BackColor = back,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
        }

        private static Label MakeLabel(string text, Font font, Color color, ContentAlignment align = ContentAlignment.MiddleLeft)
        {
            return new Label
            {
                Text = text,
                Font = font,
                ForeColor = color,
                Dock = DockStyle.Fill,
                TextAlign = align,
                Margin = new Padding(0),
                AutoEllipsis = true,
                UseMnemonic = false
            };
        }

        private static Label Caption(string text)
        {
            return MakeLabel(text, Theme.Semi(8f), Theme.Faint, ContentAlignment.BottomLeft);
        }

        private ListBox MakeLog()
        {
            var lb = new ListBox
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.Inset,
                ForeColor = Theme.Muted,
                BorderStyle = BorderStyle.None,
                Font = Theme.Mono(8.5f),
                IntegralHeight = false,
                Margin = new Padding(0, 6, 0, 0),
                SelectionMode = SelectionMode.None
            };
            lb.HandleCreated += (s, e) => Native.DarkScroll(lb);
            return lb;
        }

        private void AddLog(ListBox box, string line)
        {
            box.Items.Insert(0, DateTime.Now.ToString("HH:mm:ss") + "  " + line);
            while (box.Items.Count > 500) box.Items.RemoveAt(box.Items.Count - 1);
        }

        private void Ui(Action a)
        {
            if (IsDisposed) return;
            try { BeginInvoke(a); } catch { }
        }

        private int AskOnUi(string title, string message, string[] buttons, int primary, Color accent)
        {
            if (!InvokeRequired) return ThemedDialog.Show(this, title, message, buttons, primary, accent);
            return (int)Invoke(new Func<int>(() => ThemedDialog.Show(this, title, message, buttons, primary, accent)));
        }

        // =================================================================
        //  PAGE 1: Processes
        // =================================================================

        private Control BuildProcessesPage()
        {
            var page = NewPage();
            var layout = Table(1, 3, Theme.Bg);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 116F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            procHeader = new PageHeader("Processes", "Live process monitor \u2014 right-click a process to stop just that one.") { Dock = DockStyle.Fill };

            var stats = Table(4, 1, Theme.Bg);
            for (int i = 0; i < 4; i++) stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            statProc = new StatCard("PROCESSES", "0", "running right now", Theme.Blue);
            statCpu = new StatCard("CPU LOAD", "0 %", "all processes combined", Theme.Amber);
            statMem = new StatCard("MEMORY IN USE", "0 MB", "working set total", Theme.Green);
            statStoppable = new StatCard("STOPPABLE", "0", "apps that can be closed", Theme.Red);
            var sc = new[] { statProc, statCpu, statMem, statStoppable };
            for (int i = 0; i < 4; i++)
            {
                sc[i].Dock = DockStyle.Fill;
                sc[i].Margin = new Padding(0, 0, i < 3 ? 14 : 0, 16);
                stats.Controls.Add(sc[i], i, 0);
            }

            var body = Table(2, 1, Theme.Bg);
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330F));

            var gridCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(12, 12, 12, 12), Margin = new Padding(0, 0, 14, 0) };
            BuildGrid();
            gridCard.Controls.Add(grid);

            body.Controls.Add(gridCard, 0, 0);
            body.Controls.Add(BuildStopCard(), 1, 0);

            layout.Controls.Add(procHeader, 0, 0);
            layout.Controls.Add(stats, 0, 1);
            layout.Controls.Add(body, 0, 2);
            page.Controls.Add(layout);
            return page;
        }

        private void BuildGrid()
        {
            grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Theme.Card,
                BorderStyle = BorderStyle.None,
                GridColor = Color.FromArgb(38, 43, 51),
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AllowUserToResizeColumns = false,
                AllowUserToOrderColumns = false,
                RowHeadersVisible = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                ColumnHeadersHeight = 36,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
                EnableHeadersVisualStyles = false,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                Margin = new Padding(0)
            };
            grid.RowTemplate.Height = 30;

            grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Theme.Card,
                ForeColor = Theme.Faint,
                Font = Theme.Semi(8.5f),
                SelectionBackColor = Theme.Card,
                SelectionForeColor = Theme.Faint,
                Alignment = DataGridViewContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 8, 0)
            };
            grid.DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Theme.Card,
                ForeColor = Theme.Soft,
                SelectionBackColor = Color.FromArgb(40, 52, 74),
                SelectionForeColor = Color.White,
                Font = Theme.F(9.5f),
                Padding = new Padding(8, 0, 8, 0)
            };
            grid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(32, 36, 44) };

            AddColumn("Process", "PROCESS", 300);
            AddColumn("PID", "PID", 70);
            AddColumn("CPU %", "CPU", 70);
            AddColumn("Memory", "MEMORY", 95);
            AddColumn("Role", "STATUS", 120);

            try
            {
                typeof(DataGridView).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(grid, true, null);
            }
            catch { }

            grid.CellPainting += Grid_CellPainting;
            BuildProcessMenu();
            grid.HandleCreated += (s, e) =>
            {
                foreach (Control c in grid.Controls)
                    if (c is ScrollBar) Native.DarkScroll(c);
            };
        }

        private void AddColumn(string name, string header, int weight)
        {
            var col = new DataGridViewTextBoxColumn
            {
                HeaderText = header,
                Name = name,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                FillWeight = weight
            };
            if (name == "CPU %" || name == "Memory" || name == "PID")
            {
                col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                col.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
                col.DefaultCellStyle.Font = Theme.Mono(9f);
            }
            grid.Columns.Add(col);
        }

        private void Grid_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != grid.Columns["Role"].Index) return;
            e.PaintBackground(e.CellBounds, true);

            string role = Convert.ToString(e.Value);
            string text;
            Color c;
            if (role == "SYSTEM PROTECTED") { text = "Protected"; c = Theme.Green; }
            else if (role == "CONFIRM") { text = "Ask first"; c = Theme.Amber; }
            else { text = "Stoppable"; c = Theme.Muted; }

            var g = e.Graphics;
            var f = Theme.Semi(8f);
            Size ts = TextRenderer.MeasureText(g, text, f, Size.Empty, TextFormatFlags.NoPadding);
            var pill = new Rectangle(e.CellBounds.X + 8, e.CellBounds.Y + (e.CellBounds.Height - 20) / 2, ts.Width + 18, 20);
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.FillRound(g, Color.FromArgb(38, c), pill, 10);
            g.SmoothingMode = old;
            TextRenderer.DrawText(g, text, f, pill, c,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            e.Handled = true;
        }

        private Control BuildStopCard()
        {
            var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(20, 18, 20, 18) };
            var t = Table(1, 8, Theme.Card);
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 76F));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var desc = MakeLabel("One click stops every app you started (browsers, node, bun, terminals...) one by one, without asking. Windows processes are never touched. Right-click a process to stop just that one.",
                Theme.F(9f), Theme.Muted, ContentAlignment.TopLeft);
            desc.AutoEllipsis = false;

            stopButton = new FlatButton { Text = "Stop All Processes", Glyph = "\uE71A", Dock = DockStyle.Fill, Height = 50, Margin = new Padding(0, 2, 0, 4) };
            stopButton.SetColor(Theme.Red);
            stopButton.Click += StopButton_Click;

            shutdownAfter = new CheckToggle { Text = "Shut down PC when finished", Dock = DockStyle.Fill };

            stopProgress = new SlimBar { Dock = DockStyle.Fill, Bar = Theme.Red };

            stopLog = MakeLog();

            t.Controls.Add(MakeLabel("Shutdown routine", Theme.F(12f, FontStyle.Bold), Theme.Text), 0, 0);
            t.Controls.Add(desc, 0, 1);
            t.Controls.Add(stopButton, 0, 2);
            t.Controls.Add(shutdownAfter, 0, 3);
            t.Controls.Add(stopProgress, 0, 4);
            t.Controls.Add(Caption("ACTIVITY"), 0, 5);
            t.Controls.Add(stopLog, 0, 6);
            card.Controls.Add(t);
            return card;
        }

        // ---- process list: collected on a background thread so the window never freezes

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr h);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageName(IntPtr h, int flags, System.Text.StringBuilder name, ref int size);

        private readonly object snapshotLock = new object();
        private readonly Dictionary<int, string> pathCache = new Dictionary<int, string>();
        private static readonly int mySession = Process.GetCurrentProcess().SessionId;
        private static readonly int myPid = Process.GetCurrentProcess().Id;
        private volatile bool refreshing;
        private ContextMenuStrip procMenu;

        private class Snapshot
        {
            public List<ProcRow> Rows = new List<ProcRow>();
            public int Total, Stoppable;
            public long Memory;
            public double Cpu;
        }

        /// Fast, exception-free way to get a process's exe path (works for most elevated processes too).
        private static string ExePath(int pid)
        {
            IntPtr h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
            if (h == IntPtr.Zero) return "";
            try
            {
                var sb = new System.Text.StringBuilder(1024);
                int size = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : "";
            }
            finally { CloseHandle(h); }
        }

        private void StartTimer()
        {
            refreshTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            refreshTimer.Tick += (s, e) =>
            {
                if (!isStopping && currentPage == 0 && Visible && WindowState != FormWindowState.Minimized) RefreshGrid();
            };
            refreshTimer.Start();
            RefreshGrid();
        }

        /// Starts a background refresh of the process list (ignored if one is already running).
        private void RefreshGrid()
        {
            if (refreshing) return;
            refreshing = true;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Snapshot snap = null;
                try { snap = TakeSnapshot(); }
                catch { }
                Ui(() =>
                {
                    try { if (snap != null) ApplySnapshot(snap); }
                    finally { refreshing = false; }
                });
                if (IsDisposed) refreshing = false;
            });
        }

        private Snapshot TakeSnapshot()
        {
            lock (snapshotLock)
            {
                var snap = new Snapshot();
                var alive = new HashSet<int>();
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        int pid = p.Id;
                        alive.Add(pid);
                        snap.Total++;
                        long mem = 0;
                        int session = -1;
                        try { mem = p.WorkingSet64; } catch { }
                        try { session = p.SessionId; } catch { }
                        string path;
                        if (!pathCache.TryGetValue(pid, out path)) { path = ExePath(pid); pathCache[pid] = path; }
                        double cpu = SampleCpu(p);
                        snap.Memory += mem;
                        snap.Cpu += cpu;
                        var row = new ProcRow { Pid = pid, Name = p.ProcessName, Memory = mem, Cpu = cpu, Path = path, Session = session };
                        row.Role = IsProtected(row) ? "SYSTEM PROTECTED" : "normal";
                        if (row.Role != "SYSTEM PROTECTED") snap.Stoppable++;
                        snap.Rows.Add(row);
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
                foreach (var dead in lastSample.Keys.Where(k => !alive.Contains(k)).ToList()) lastSample.Remove(dead);
                foreach (var dead in pathCache.Keys.Where(k => !alive.Contains(k)).ToList()) pathCache.Remove(dead);
                snap.Rows = snap.Rows.OrderByDescending(r => r.Memory).ToList();
                return snap;
            }
        }

        /// Updates the grid in place (no clearing), so it doesn't flicker or lose the selection.
        private void ApplySnapshot(Snapshot snap)
        {
            int selectedPid = -1;
            if (grid.SelectedRows.Count > 0 && grid.SelectedRows[0].Tag is int) selectedPid = (int)grid.SelectedRows[0].Tag;

            int n = snap.Rows.Count;
            if (grid.Rows.Count < n) grid.Rows.Add(n - grid.Rows.Count);
            while (grid.Rows.Count > n) grid.Rows.RemoveAt(grid.Rows.Count - 1);

            int reselect = -1;
            for (int i = 0; i < n; i++)
            {
                var r = snap.Rows[i];
                var row = grid.Rows[i];
                row.Tag = r.Pid;
                SetCell(row, 0, r.Name);
                SetCell(row, 1, r.Pid.ToString());
                SetCell(row, 2, r.Cpu.ToString("0.0"));
                SetCell(row, 3, Theme.FormatBytes(r.Memory));
                SetCell(row, 4, r.Role);
                Color fc = r.Role == "SYSTEM PROTECTED" ? Theme.Muted : Theme.Soft;
                if (row.DefaultCellStyle.ForeColor != fc) row.DefaultCellStyle.ForeColor = fc;
                if (r.Pid == selectedPid) reselect = i;
            }
            if (reselect >= 0 && !grid.Rows[reselect].Selected)
            {
                grid.ClearSelection();
                grid.Rows[reselect].Selected = true;
            }
            else if (reselect < 0 && grid.SelectedRows.Count > 0) grid.ClearSelection();

            statProc.Value = snap.Total.ToString();
            statCpu.Value = Math.Min(100, snap.Cpu).ToString("0") + " %";
            statMem.Value = Theme.FormatBytes(snap.Memory);
            statStoppable.Value = snap.Stoppable.ToString();
        }

        private static void SetCell(DataGridViewRow row, int col, string value)
        {
            if (!string.Equals(row.Cells[col].Value as string, value)) row.Cells[col].Value = value;
        }

        private double SampleCpu(Process p)
        {
            try
            {
                var now = DateTime.UtcNow;
                var cpu = p.TotalProcessorTime;
                double value = 0;
                Tuple<TimeSpan, DateTime> prev;
                if (lastSample.TryGetValue(p.Id, out prev))
                {
                    double ms = (now - prev.Item2).TotalMilliseconds;
                    if (ms > 50) value = (cpu - prev.Item1).TotalMilliseconds / ms * 100.0 / Environment.ProcessorCount;
                }
                lastSample[p.Id] = Tuple.Create(cpu, now);
                return Math.Max(0, Math.Min(100, value));
            }
            catch { return 0; }
        }

        /// Windows itself, services (session 0), other users' sessions and this app are never stopped.
        private bool IsProtected(ProcRow r)
        {
            if (r.Pid <= 4 || r.Pid == myPid) return true;
            if (r.Session != mySession) return true;
            return IsCritical(r.Name, r.Path);
        }

        /// Programs that live in the Windows folder but are tools the user opened (terminals, editors...),
        /// so Stop All may close them.
        private static readonly string[] USER_TOOLS = new string[]
        {
            "cmd", "powershell", "pwsh", "powershell_ise", "WindowsTerminal", "OpenConsole", "wt", "wsl", "bash",
            "notepad", "mspaint", "write", "wordpad", "calc", "CalculatorApp", "ping", "tracert", "nslookup",
            "regedit", "mstsc", "SnippingTool", "charmap", "magnify", "osk", "xcopy", "robocopy", "timeout", "choice"
        };

        private bool IsCritical(string name, string path)
        {
            if (name.Equals("StopProcessing", StringComparison.OrdinalIgnoreCase)) return true;
            if (CRITICAL.Any(c => name.Equals(c, StringComparison.OrdinalIgnoreCase))) return true;
            if (USER_TOOLS.Any(c => name.Equals(c, StringComparison.OrdinalIgnoreCase))) return false;
            if (!string.IsNullOrEmpty(path))
            {
                string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                if (path.StartsWith(win + "\\", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        // ---- right-click menu on a process

        private void BuildProcessMenu()
        {
            procMenu = new ContextMenuStrip
            {
                Renderer = new DarkMenuRenderer(),
                Font = Theme.F(9.5f),
                ShowImageMargin = false,
                BackColor = Theme.Surface,
                ForeColor = Theme.Text,
                Padding = new Padding(2, 4, 2, 4)
            };
            grid.CellMouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Right || e.RowIndex < 0) return;
                grid.ClearSelection();
                grid.Rows[e.RowIndex].Selected = true;
                ShowProcessMenu(grid.Rows[e.RowIndex]);
            };
        }

        private void ShowProcessMenu(DataGridViewRow row)
        {
            if (!(row.Tag is int)) return;
            int pid = (int)row.Tag;
            string name = Convert.ToString(row.Cells[0].Value);
            bool prot = Convert.ToString(row.Cells[4].Value) == "SYSTEM PROTECTED";
            string path;
            if (!pathCache.TryGetValue(pid, out path)) path = ExePath(pid);

            procMenu.Items.Clear();
            var head = new ToolStripMenuItem(name + "  (PID " + pid + ")") { Enabled = false, Padding = new Padding(8, 5, 8, 5) };
            procMenu.Items.Add(head);
            procMenu.Items.Add(new ToolStripSeparator());
            if (prot)
            {
                procMenu.Items.Add(new ToolStripMenuItem("Protected \u2014 Windows process") { Enabled = false, Padding = new Padding(8, 5, 8, 5) });
            }
            else
            {
                var stop = new ToolStripMenuItem("Stop this process", null, (s, e) => StopPids(new List<int> { pid }, name))
                { Padding = new Padding(8, 5, 8, 5), ForeColor = Theme.Red };
                procMenu.Items.Add(stop);
                int same = grid.Rows.Cast<DataGridViewRow>().Count(r =>
                    Convert.ToString(r.Cells[0].Value) == name && Convert.ToString(r.Cells[4].Value) != "SYSTEM PROTECTED");
                if (same > 1)
                {
                    procMenu.Items.Add(new ToolStripMenuItem("Stop all \"" + name + "\" (" + same + ")", null, (s, e) =>
                    {
                        var pids = grid.Rows.Cast<DataGridViewRow>()
                            .Where(r => Convert.ToString(r.Cells[0].Value) == name && Convert.ToString(r.Cells[4].Value) != "SYSTEM PROTECTED" && r.Tag is int)
                            .Select(r => (int)r.Tag).ToList();
                        StopPids(pids, name);
                    }) { Padding = new Padding(8, 5, 8, 5), ForeColor = Theme.Red });
                }
            }
            if (!string.IsNullOrEmpty(path))
            {
                procMenu.Items.Add(new ToolStripSeparator());
                procMenu.Items.Add(new ToolStripMenuItem("Open file location", null, (s, e) =>
                {
                    try { Process.Start("explorer.exe", "/select,\"" + path + "\""); } catch { }
                }) { Padding = new Padding(8, 5, 8, 5) });
            }
            procMenu.Show(Cursor.Position);
        }

        private void StopPids(List<int> pids, string name)
        {
            var th = new Thread(() =>
            {
                foreach (var pid in pids)
                {
                    string r = StopOne(pid);
                    int p = pid;
                    Ui(() => AddLog(stopLog, name + " (" + p + ") \u2192 " + r));
                }
                Ui(RefreshGrid);
            });
            th.IsBackground = true;
            th.Start();
        }

        // ---- Stop All: one by one, no questions; Windows processes are never touched

        private void StopButton_Click(object sender, EventArgs e)
        {
            if (isStopping) return;
            isStopping = true;
            stopButton.Enabled = false;
            stopButton.Text = "Stopping\u2026";
            stopProgress.Value = 0;
            stopLog.Items.Clear();
            AddLog(stopLog, "Collecting running apps\u2026");

            stopCancel = new CancellationTokenSource();
            var token = stopCancel.Token;
            bool shutdownWhenDone = shutdownAfter.Checked;

            var worker = new Thread(() =>
            {
                var targets = TakeSnapshot().Rows.Where(r => r.Role != "SYSTEM PROTECTED").ToList();
                int total = targets.Count;
                Ui(() =>
                {
                    stopProgress.Maximum = Math.Max(1, total);
                    AddLog(stopLog, "Stopping " + total + " processes.");
                });

                int done = 0, ok = 0;
                foreach (var t in targets)
                {
                    if (token.IsCancellationRequested) break;
                    done++;
                    string result = StopOne(t.Pid);
                    if (result == "Stopped" || result == "Closed") ok++;
                    int d = done;
                    string r = result, nm = t.Name;
                    Ui(() =>
                    {
                        stopProgress.Value = d;
                        procHeader.Subtitle = nm + " \u2014 " + r + "   (" + d + " / " + total + ")";
                        AddLog(stopLog, nm + " \u2192 " + r);
                    });
                    Thread.Sleep(60);
                }

                int stopped = ok;
                Ui(() =>
                {
                    isStopping = false;
                    stopButton.Enabled = true;
                    stopButton.Text = "Stop All Processes";
                    procHeader.Subtitle = "Finished. Stopped " + stopped + " of " + total + " processes.";
                    AddLog(stopLog, "Finished. " + stopped + "/" + total + " stopped.");
                    RefreshGrid();
                    if (shutdownWhenDone && !token.IsCancellationRequested) ShutdownCountdown();
                });
            });
            worker.IsBackground = true;
            worker.Start();
        }

        /// Closes a window app gracefully if it can, otherwise ends it. Never asks.
        private string StopOne(int pid)
        {
            Process p;
            try { p = Process.GetProcessById(pid); }
            catch { return "Already closed"; }

            try
            {
                var row = new ProcRow { Pid = pid, Name = p.ProcessName, Path = ExePath(pid) };
                try { row.Session = p.SessionId; } catch { row.Session = -1; }
                if (IsProtected(row)) return "Protected";

                IntPtr win = IntPtr.Zero;
                try { win = p.MainWindowHandle; } catch { }
                if (win != IntPtr.Zero)
                {
                    try { p.CloseMainWindow(); } catch { }
                    if (p.WaitForExit(1200)) return "Closed";
                }
                p.Kill();
                p.WaitForExit(2000);
                return "Stopped";
            }
            catch (InvalidOperationException) { return "Already closed"; }
            catch (Win32Exception) { return "Access denied"; }
            catch (Exception ex)
            {
                string m = ex.Message;
                if (m.Length > 40) m = m.Substring(0, 40);
                return "Error: " + m;
            }
            finally { p.Dispose(); }
        }

        private void ShutdownCountdown()
        {
            int code;
            Native.Run("shutdown.exe", "/s /f /t 15", out code);
            int a = ThemedDialog.Show(this, "Shutting down",
                "Your PC will shut down in 15 seconds.",
                new[] { "Cancel shutdown", "OK" }, 1, Theme.Red);
            if (a == 0)
            {
                Native.Run("shutdown.exe", "/a", out code);
                AddLog(stopLog, "Shutdown cancelled.");
            }
        }

        // =================================================================
        //  PAGE 2: Cleaner
        // =================================================================

        private class CleanTarget
        {
            public string Name, Desc, Glyph, Pattern;
            public string[] Dirs;
            public bool Recycle, DefaultOn;
            public long Size;
            public int Files;
            public CleanRow Row;
        }

        private void BuildCleanTargets()
        {
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string pd = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

            cleanTargets.Add(new CleanTarget { Name = "User temp files", Glyph = "\uE8B7", DefaultOn = true,
                Desc = "%TEMP% \u2014 leftovers from apps and installers", Dirs = new[] { Path.GetTempPath() } });
            cleanTargets.Add(new CleanTarget { Name = "Windows temp files", Glyph = "\uE8B7", DefaultOn = true,
                Desc = Path.Combine(win, "Temp"), Dirs = new[] { Path.Combine(win, "Temp") } });
            cleanTargets.Add(new CleanTarget { Name = "Thumbnail cache", Glyph = "\uE91B", DefaultOn = true,
                Desc = "Explorer thumbnails \u2014 rebuilt automatically", Pattern = "thumbcache_*.db",
                Dirs = new[] { Path.Combine(local, @"Microsoft\Windows\Explorer") } });
            cleanTargets.Add(new CleanTarget { Name = "Crash dumps and error reports", Glyph = "\uE7BA", DefaultOn = true,
                Desc = "Old crash dumps and Windows Error Reporting files",
                Dirs = new[] { Path.Combine(local, "CrashDumps"), Path.Combine(pd, @"Microsoft\Windows\WER\ReportArchive"),
                               Path.Combine(pd, @"Microsoft\Windows\WER\ReportQueue") } });
            cleanTargets.Add(new CleanTarget { Name = "Browser cache", Glyph = "\uE774", DefaultOn = true,
                Desc = "Chrome, Edge, Brave, Opera, Firefox \u2014 logins and history are kept",
                Dirs = BrowserCacheDirs(local, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)) });
            cleanTargets.Add(new CleanTarget { Name = "Windows Update cache", Glyph = "\uE896", DefaultOn = false,
                Desc = "Update files that were already downloaded", Dirs = new[] { Path.Combine(win, @"SoftwareDistribution\Download") } });
            cleanTargets.Add(new CleanTarget { Name = "Prefetch data", Glyph = "\uE945", DefaultOn = false,
                Desc = "Launch cache \u2014 apps may open a bit slower once", Dirs = new[] { Path.Combine(win, "Prefetch") } });
            cleanTargets.Add(new CleanTarget { Name = "Recycle Bin", Glyph = "\uE74D", DefaultOn = false, Recycle = true,
                Desc = "Permanently empties the Recycle Bin", Dirs = new string[0] });
        }

        /// Only cache folders - never cookies, history, passwords or bookmarks.
        private static string[] BrowserCacheDirs(string local, string roaming)
        {
            var dirs = new List<string>();
            string[] chromium =
            {
                Path.Combine(local, @"Google\Chrome\User Data"),
                Path.Combine(local, @"Microsoft\Edge\User Data"),
                Path.Combine(local, @"BraveSoftware\Brave-Browser\User Data"),
                Path.Combine(local, @"Vivaldi\User Data"),
                Path.Combine(roaming, @"Opera Software\Opera Stable"),
                Path.Combine(local, @"Opera Software\Opera Stable"),
                Path.Combine(roaming, @"Opera Software\Opera GX Stable"),
                Path.Combine(local, @"Opera Software\Opera GX Stable")
            };
            string[] cacheNames = { "Cache", "Code Cache", "GPUCache" };
            foreach (var root in chromium)
            {
                if (!Directory.Exists(root)) continue;
                var profiles = new List<string> { root };
                try
                {
                    profiles.AddRange(Directory.GetDirectories(root).Where(d =>
                    {
                        string n = Path.GetFileName(d);
                        return n == "Default" || n.StartsWith("Profile ") || n == "Guest Profile";
                    }));
                }
                catch { }
                foreach (var p in profiles)
                    foreach (var c in cacheNames)
                    {
                        string d = Path.Combine(p, c);
                        if (Directory.Exists(d)) dirs.Add(d);
                    }
            }
            try
            {
                string ff = Path.Combine(local, @"Mozilla\Firefox\Profiles");
                if (Directory.Exists(ff))
                    foreach (var p in Directory.GetDirectories(ff))
                    {
                        string d = Path.Combine(p, "cache2");
                        if (Directory.Exists(d)) dirs.Add(d);
                    }
            }
            catch { }
            return dirs.ToArray();
        }

        private Control BuildCleanerPage()
        {
            var page = NewPage();
            var layout = Table(1, 2, Theme.Bg);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            cleanHeader = new PageHeader("Cleaner",
                "Delete temporary files in one click. Files that are in use get skipped automatically.") { Dock = DockStyle.Fill };

            var body = Table(2, 1, Theme.Bg);
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330F));

            // left: list of locations
            var listCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(10, 14, 10, 10), Margin = new Padding(0, 0, 14, 0) };
            var listHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card, AutoScroll = true };
            for (int i = cleanTargets.Count - 1; i >= 0; i--)
            {
                var t = cleanTargets[i];
                t.Row = new CleanRow(t.Glyph, t.Name, t.Desc, t.DefaultOn);
                t.Row.Click += (s, e) => UpdateCleanTotal();
                listHost.Controls.Add(t.Row);
            }
            var listTitle = new Label
            {
                Text = "   CLEANUP LOCATIONS",
                Font = Theme.Semi(8f),
                ForeColor = Theme.Faint,
                Dock = DockStyle.Top,
                Height = 26,
                TextAlign = ContentAlignment.TopLeft
            };
            listCard.Controls.Add(listHost);
            listCard.Controls.Add(listTitle);

            // right: actions
            var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(20, 18, 20, 18) };
            var t2 = Table(1, 10, Theme.Card);
            t2.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            t2.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));
            t2.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            t2.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            t2.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));
            t2.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            t2.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            t2.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            t2.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            t2.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            cleanTotal = MakeLabel("\u2014", Theme.F(22f, FontStyle.Bold), Theme.Text);
            cleanTotalHint = MakeLabel("Scanning\u2026", Theme.F(9f), Theme.Muted, ContentAlignment.TopLeft);

            cleanButton = new FlatButton { Text = "Clean Now", Glyph = "\uE74D", Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 6) };
            cleanButton.SetColor(Theme.Blue);
            cleanButton.Click += (s, e) => StartClean();

            scanButton = new FlatButton { Text = "Scan Again", Glyph = "\uE721", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 6), Font = Theme.Semi(9.5f) };
            scanButton.SetColor(Theme.Secondary);
            scanButton.Click += (s, e) => StartScan();

            askLocked = new CheckToggle { Text = "Ask once before skipping locked files", Dock = DockStyle.Fill, Checked = true };

            cleanProgress = new SlimBar { Dock = DockStyle.Fill };
            cleanSummary = MakeLabel("", Theme.F(9f), Theme.Soft, ContentAlignment.MiddleLeft);
            cleanSummary.AutoEllipsis = false;
            cleanLog = MakeLog();

            t2.Controls.Add(Caption("SELECTED TO FREE"), 0, 0);
            t2.Controls.Add(cleanTotal, 0, 1);
            t2.Controls.Add(cleanTotalHint, 0, 2);
            t2.Controls.Add(cleanButton, 0, 3);
            t2.Controls.Add(scanButton, 0, 4);
            t2.Controls.Add(askLocked, 0, 5);
            t2.Controls.Add(cleanProgress, 0, 6);
            t2.Controls.Add(cleanSummary, 0, 7);
            t2.Controls.Add(Caption("ACTIVITY"), 0, 8);
            t2.Controls.Add(cleanLog, 0, 9);
            card.Controls.Add(t2);

            body.Controls.Add(listCard, 0, 0);
            body.Controls.Add(card, 1, 0);
            layout.Controls.Add(cleanHeader, 0, 0);
            layout.Controls.Add(body, 0, 1);
            page.Controls.Add(layout);
            return page;
        }

        private void UpdateCleanTotal()
        {
            long total = 0;
            int files = 0;
            foreach (var t in cleanTargets)
                if (t.Row.Checked) { total += t.Size; files += t.Files; }
            cleanTotal.Text = Theme.FormatBytes(total);
            cleanTotalHint.Text = files.ToString("N0") + " items in selected locations";
        }

        private static void Walk(string root, string pattern, List<string> files, List<string> dirs)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                string d = stack.Pop();
                try { files.AddRange(pattern == null ? Directory.GetFiles(d) : Directory.GetFiles(d, pattern)); } catch { }
                if (pattern != null) continue; // pattern targets are top-level only

                string[] subs;
                try { subs = Directory.GetDirectories(d); } catch { continue; }
                foreach (var s in subs)
                {
                    try
                    {
                        // never follow junctions / symlinks out of the temp folder
                        if ((File.GetAttributes(s) & FileAttributes.ReparsePoint) != 0) continue;
                    }
                    catch { continue; }
                    if (dirs != null) dirs.Add(s);
                    stack.Push(s);
                }
            }
        }

        private static long RecycleBinSize(out long items)
        {
            items = 0;
            try
            {
                var info = new Native.SHQUERYRBINFO();
                info.cbSize = Marshal.SizeOf(typeof(Native.SHQUERYRBINFO));
                if (Native.SHQueryRecycleBin(null, ref info) == 0)
                {
                    items = info.i64NumItems;
                    return info.i64Size;
                }
            }
            catch { }
            return 0;
        }

        private void SetCleanBusy(bool busy)
        {
            isCleaning = busy;
            cleanButton.Enabled = !busy;
            scanButton.Enabled = !busy;
            foreach (var t in cleanTargets) t.Row.Enabled = !busy;
        }

        private void StartScan()
        {
            if (isCleaning) return;
            SetCleanBusy(true);
            cleanTotalHint.Text = "Scanning\u2026";
            foreach (var t in cleanTargets) { t.Row.SizeText = "\u2026"; t.Row.StatusText = ""; }

            var th = new Thread(() =>
            {
                foreach (var t in cleanTargets)
                {
                    long size = 0;
                    int count = 0;
                    if (t.Recycle)
                    {
                        long items;
                        size = RecycleBinSize(out items);
                        count = (int)items;
                    }
                    else
                    {
                        var files = new List<string>();
                        foreach (var d in t.Dirs) Walk(d, t.Pattern, files, null);
                        foreach (var f in files)
                        {
                            try { size += new FileInfo(f).Length; count++; } catch { }
                        }
                    }
                    t.Size = size;
                    t.Files = count;
                    var tt = t;
                    Ui(() =>
                    {
                        tt.Row.SizeText = Theme.FormatBytes(tt.Size);
                        tt.Row.StatusText = tt.Files.ToString("N0") + " items";
                    });
                }
                Ui(() => { SetCleanBusy(false); UpdateCleanTotal(); });
            });
            th.IsBackground = true;
            th.Start();
        }

        private void StartClean()
        {
            if (isCleaning) return;
            var selected = cleanTargets.Where(t => t.Row.Checked).ToList();
            if (selected.Count == 0)
            {
                ThemedDialog.Show(this, "Nothing selected", "Tick at least one location to clean.", new[] { "OK" }, 0, Theme.Blue);
                return;
            }

            SetCleanBusy(true);
            cleanButton.Text = "Cleaning\u2026";
            cleanLog.Items.Clear();
            cleanSummary.Text = "";
            cleanProgress.Value = 0;
            bool ask = askLocked.Checked;

            var th = new Thread(() =>
            {
                // collect everything first so the progress bar is accurate
                var work = new List<KeyValuePair<CleanTarget, List<string>>>();
                var dirWork = new Dictionary<CleanTarget, List<string>>();
                int totalFiles = 0;
                foreach (var t in selected)
                {
                    var files = new List<string>();
                    var dirs = new List<string>();
                    if (!t.Recycle) foreach (var d in t.Dirs) Walk(d, t.Pattern, files, dirs);
                    work.Add(new KeyValuePair<CleanTarget, List<string>>(t, files));
                    dirWork[t] = dirs;
                    totalFiles += t.Recycle ? 1 : files.Count;
                }
                int total = Math.Max(1, totalFiles);
                Ui(() => { cleanProgress.Maximum = total; AddLog(cleanLog, "Found " + totalFiles.ToString("N0") + " items to clean."); });

                long freedAll = 0;
                int deletedAll = 0, skippedAll = 0, processed = 0;
                bool skipAll = !ask, stop = false;

                foreach (var kv in work)
                {
                    if (stop) break;
                    var t = kv.Key;
                    long freed = 0;
                    int deleted = 0, skipped = 0;
                    Ui(() => { t.Row.StatusText = "Cleaning\u2026"; });

                    if (t.Recycle)
                    {
                        long items;
                        long size = RecycleBinSize(out items);
                        int hr = Native.SHEmptyRecycleBin(Handle, null, 0x1 | 0x2 | 0x4);
                        if (hr == 0 || items == 0) { freed = size; deleted = (int)items; }
                        else skipped = 1;
                        processed++;
                    }
                    else
                    {
                        foreach (var f in kv.Value)
                        {
                            if (stop) break;
                            processed++;
                            try
                            {
                                var fi = new FileInfo(f);
                                if (!fi.Exists) continue;
                                long len = fi.Length;
                                if ((fi.Attributes & FileAttributes.ReadOnly) != 0) fi.Attributes = FileAttributes.Normal;
                                fi.Delete();
                                freed += len;
                                deleted++;
                            }
                            catch (Exception ex)
                            {
                                skipped++;
                                string shortName = Path.GetFileName(f);
                                Ui(() => AddLog(cleanLog, "Skipped (in use): " + shortName));
                                if (!skipAll)
                                {
                                    // asked only once per clean: every other locked file is skipped automatically
                                    int a = AskOnUi("Some files can't be deleted",
                                        "Some files are open in other programs or protected, so they can't be deleted right now.\n\n" +
                                        "First one: " + f + "\n\n" +
                                        "Skip all of them and keep cleaning the rest?",
                                        new[] { "Skip All", "Stop Cleaning" }, 0, Theme.Blue);
                                    if (a == 0) skipAll = true;
                                    else stop = true;
                                }
                            }

                            if (processed % 40 == 0)
                            {
                                int pr = processed;
                                Ui(() => cleanProgress.Value = pr);
                            }
                        }

                        // remove now-empty folders, deepest first (root folders are kept)
                        foreach (var d in dirWork[t].OrderByDescending(x => x.Length))
                        {
                            try { Directory.Delete(d, false); } catch { }
                        }
                    }

                    freedAll += freed;
                    deletedAll += deleted;
                    skippedAll += skipped;
                    long fr = freed;
                    int de = deleted, sk = skipped, pr2 = processed;
                    Ui(() =>
                    {
                        cleanProgress.Value = pr2;
                        t.Row.StatusText = "Freed " + Theme.FormatBytes(fr) + (sk > 0 ? " \u00B7 " + sk + " skipped" : "");
                        AddLog(cleanLog, t.Name + ": " + de.ToString("N0") + " deleted, " + sk + " skipped, " + Theme.FormatBytes(fr) + " freed");
                    });
                }

                long fa = freedAll;
                int da = deletedAll, sa = skippedAll;
                bool stopped = stop;
                Ui(() =>
                {
                    cleanProgress.Value = cleanProgress.Maximum;
                    cleanButton.Text = "Clean Now";
                    cleanSummary.Text = (stopped ? "Stopped. " : "Done! ") + "Freed " + Theme.FormatBytes(fa) + "\n" +
                                        da.ToString("N0") + " deleted \u00B7 " + sa.ToString("N0") + " skipped (in use)";
                    cleanHeader.Subtitle = "Last clean freed " + Theme.FormatBytes(fa) + ".";
                    AddLog(cleanLog, "Finished. Freed " + Theme.FormatBytes(fa) + ".");
                    SetCleanBusy(false);
                    RescanSizesOnly();
                });
            });
            th.IsBackground = true;
            th.Start();
        }

        private void RescanSizesOnly()
        {
            var keep = cleanTargets.ToDictionary(t => t, t => t.Row.StatusText);
            StartScan();
            // keep the "Freed \u2026" status visible after the rescan finishes
            var timer = new System.Windows.Forms.Timer { Interval = 300 };
            timer.Tick += (s, e) =>
            {
                if (isCleaning) return;
                timer.Stop();
                timer.Dispose();
                foreach (var kv in keep) if (kv.Value.StartsWith("Freed")) kv.Key.Row.StatusText = kv.Value;
            };
            timer.Start();
        }

        // =================================================================
        //  PAGE 3: Power Fix  (PC turns itself back on after shutdown)
        // =================================================================

        private const string KeyPower = @"SYSTEM\CurrentControlSet\Control\Session Manager\Power";
        private const string KeyMaint = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\Maintenance";
        private const string KeyCrash = @"SYSTEM\CurrentControlSet\Control\CrashControl";
        private const string KeyArso = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";

        private static string WakeListFile
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StopProcessing");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "wake_devices.txt");
            }
        }

        private Control BuildPowerPage()
        {
            var page = NewPage();
            var layout = Table(1, 2, Theme.Bg);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            powerHeader = new PageHeader("Power Fix",
                "PC turns itself back on after shutdown? Find and switch off what wakes it \u2014 in one click.") { Dock = DockStyle.Fill };

            var body = Table(2, 1, Theme.Bg);
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330F));

            var left = Table(1, 2, Theme.Bg);
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 118F));
            left.Margin = new Padding(0, 0, 14, 0);

            var checksCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(8, 14, 8, 8), Margin = new Padding(0, 0, 0, 14) };
            var checksHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card, AutoScroll = true };
            rowNicWake = new StatusRow("\uE839", "Network card can power the PC on",
                "Wake-on-LAN keeps the network card awake after shutdown and turns the PC back on");
            rowFastStartup = new StatusRow("\uE945", "Fast Startup",
                "Hybrid shutdown \u2014 the PC only half shuts down and can wake back up");
            rowWakeTimers = new StatusRow("\uE823", "Wake timers",
                "Scheduled tasks and apps are allowed to power the PC on");
            rowWakeDevices = new StatusRow("\uE765", "Devices allowed to wake the PC",
                "Network card (Wake-on-LAN), mouse or keyboard can turn it on");
            rowMaintenance = new StatusRow("\uE713", "Automatic maintenance wake-up",
                "Windows wakes the PC at night to run maintenance");
            rowArso = new StatusRow("\uE895", "Auto sign-in after updates",
                "Windows restarts and signs in by itself to finish updates");
            rowAutoReboot = new StatusRow("\uE777", "Restart on system failure",
                "If shutdown crashes, Windows reboots instead of staying off");
            var rows = new[] { rowNicWake, rowFastStartup, rowWakeTimers, rowWakeDevices, rowMaintenance, rowArso, rowAutoReboot };
            for (int i = rows.Length - 1; i >= 0; i--) checksHost.Controls.Add(rows[i]);
            checksCard.Controls.Add(checksHost);
            checksCard.Controls.Add(new Label
            {
                Text = "    WHAT CAN TURN YOUR PC BACK ON",
                Font = Theme.Semi(8f),
                ForeColor = Theme.Faint,
                Dock = DockStyle.Top,
                Height = 24
            });

            var noteCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(20, 16, 20, 14), BackColor = Color.FromArgb(34, 32, 28) };
            var noteT = Table(1, 2, noteCard.BackColor);
            noteT.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            noteT.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            var noteBody = MakeLabel(
                "Some causes live in the BIOS/UEFI and can't be changed from Windows. Restart, open BIOS (usually Del or F2) and set: " +
                "Restore on AC Power Loss \u2192 Power Off,  Wake on LAN / PCIe (PME) \u2192 Disabled,  RTC / Alarm wake \u2192 Disabled,  ErP \u2192 Enabled.",
                Theme.F(9f), Theme.Soft, ContentAlignment.TopLeft);
            noteBody.AutoEllipsis = false;
            noteT.Controls.Add(MakeLabel("Still turning on by itself?", Theme.Semi(10f), Theme.Amber), 0, 0);
            noteT.Controls.Add(noteBody, 0, 1);
            noteCard.Controls.Add(noteT);

            left.Controls.Add(checksCard, 0, 0);
            left.Controls.Add(noteCard, 0, 1);

            // actions
            var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(20, 18, 20, 18) };
            var t = Table(1, 9, Theme.Card);
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var desc = MakeLabel("Turns off every Windows setting that can power your PC back on after you shut it down.",
                Theme.F(9f), Theme.Muted, ContentAlignment.TopLeft);
            desc.AutoEllipsis = false;

            fixButton = new FlatButton { Text = "Fix Auto Power-On", Glyph = "\uE90F", Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 6) };
            fixButton.SetColor(Theme.Blue);
            fixButton.Click += (s, e) => RunPowerFix(true);

            restoreButton = new FlatButton { Text = "Restore Defaults", Glyph = "\uE7A7", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 6), Font = Theme.Semi(9.5f) };
            restoreButton.SetColor(Theme.Secondary);
            restoreButton.Click += (s, e) => RunPowerFix(false);

            recheckButton = new FlatButton { Text = "Check Again", Glyph = "\uE72C", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 6), Font = Theme.Semi(9.5f) };
            recheckButton.SetColor(Theme.Secondary);
            recheckButton.Click += (s, e) => CheckPowerAsync();

            fullShutdownButton = new FlatButton { Text = "Full Shut Down Now", Glyph = "\uE7E8", Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 4) };
            fullShutdownButton.SetColor(Theme.Red);
            fullShutdownButton.Click += (s, e) => FullShutdown();

            powerLog = MakeLog();

            t.Controls.Add(MakeLabel("Power-on fix", Theme.F(12f, FontStyle.Bold), Theme.Text), 0, 0);
            t.Controls.Add(desc, 0, 1);
            t.Controls.Add(fixButton, 0, 2);
            t.Controls.Add(restoreButton, 0, 3);
            t.Controls.Add(recheckButton, 0, 4);
            t.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card, Margin = new Padding(0) }, 0, 5);
            t.Controls.Add(fullShutdownButton, 0, 6);
            t.Controls.Add(Caption("ACTIVITY"), 0, 7);
            t.Controls.Add(powerLog, 0, 8);
            card.Controls.Add(t);

            body.Controls.Add(left, 0, 0);
            body.Controls.Add(card, 1, 0);
            layout.Controls.Add(powerHeader, 0, 0);
            layout.Controls.Add(body, 0, 1);
            page.Controls.Add(layout);
            return page;
        }

        private static int? ReadDword(string key, string name)
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(key))
                {
                    if (k == null) return null;
                    object v = k.GetValue(name);
                    if (v == null) return null;
                    return Convert.ToInt32(v);
                }
            }
            catch { return null; }
        }

        private static void WriteDword(string key, string name, int value)
        {
            using (var k = Registry.LocalMachine.CreateSubKey(key))
                k.SetValue(name, value, RegistryValueKind.DWord);
        }

        private static void DeleteValue(string key, string name)
        {
            using (var k = Registry.LocalMachine.OpenSubKey(key, true))
                if (k != null) k.DeleteValue(name, false);
        }

        private static List<string> WakeArmedDevices()
        {
            int code;
            string o = Native.Run("powercfg.exe", "/devicequery wake_armed", out code);
            var list = new List<string>();
            foreach (var raw in o.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string l = raw.Trim();
                if (l.Length == 0 || l.Equals("NONE", StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(l);
            }
            return list;
        }

        /// returns null if unknown, otherwise true when wake timers are enabled on AC or DC
        private static bool? WakeTimersOn()
        {
            int code;
            string o = Native.Run("powercfg.exe", "/q SCHEME_CURRENT SUB_SLEEP RTCWAKE", out code);
            var m = Regex.Matches(o, @"0x([0-9a-fA-F]{8})");
            if (code != 0 || m.Count < 2) return null;
            int ac = Convert.ToInt32(m[m.Count - 2].Groups[1].Value, 16);
            int dc = Convert.ToInt32(m[m.Count - 1].Groups[1].Value, 16);
            return ac != 0 || dc != 0;
        }

        // ---- Wake-on-LAN: the network card stays powered after shutdown and can switch the PC back on.
        //      powercfg only controls waking from Sleep, so the adapter's own settings must be turned off too.
        private const string NicKeywords =
            "@('*WakeOnMagicPacket','*WakeOnPattern','S5WakeOnLan','WakeOnLink','*ModernStandbyWoLMagicPacket','WakeFromS5','WolShutdownWake')";

        private static string RunPs(string script)
        {
            int code;
            return Native.Run("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + script + "\"", out code);
        }

        /// "adapter|keyword" -> value ("0" = off)
        private static Dictionary<string, string> NicWakeSettings()
        {
            var d = new Dictionary<string, string>();
            string o = RunPs("$k=" + NicKeywords + "; Get-NetAdapter -Physical -ErrorAction SilentlyContinue | ForEach-Object { $n=$_.Name; " +
                             "Get-NetAdapterAdvancedProperty -Name $n -AllProperties -ErrorAction SilentlyContinue | " +
                             "Where-Object { $k -contains $_.RegistryKeyword } | ForEach-Object { $n + '|' + $_.RegistryKeyword + '|' + ($_.RegistryValue -join '') } }");
            foreach (var line in o.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var p = line.Split('|');
                if (p.Length == 3) d[p[0] + "|" + p[1]] = p[2].Trim();
            }
            return d;
        }

        private static List<string> SetNicWake(bool on)
        {
            string v = on ? "1" : "0";
            string o = RunPs("$k=" + NicKeywords + "; Get-NetAdapter -Physical -ErrorAction SilentlyContinue | ForEach-Object { $n=$_.Name; $c=$false; " +
                             "Get-NetAdapterAdvancedProperty -Name $n -AllProperties -ErrorAction SilentlyContinue | " +
                             "Where-Object { $k -contains $_.RegistryKeyword -and ($_.RegistryValue -join '') -ne '" + v + "' } | ForEach-Object { " +
                             "Set-NetAdapterAdvancedProperty -Name $n -RegistryKeyword $_.RegistryKeyword -RegistryValue " + v + " -NoRestart -ErrorAction SilentlyContinue; " +
                             "$c=$true; 'OK|' + $n + ': ' + $_.DisplayName }; if ($c) { Restart-NetAdapter -Name $n -ErrorAction SilentlyContinue } }");
            var log = new List<string>();
            foreach (var line in o.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                if (line.StartsWith("OK|")) log.Add("\u2713 " + line.Substring(3) + (on ? " \u2192 on" : " \u2192 off"));
            if (log.Count == 0) log.Add("Network card wake settings were already " + (on ? "on." : "off."));
            return log;
        }

        private void CheckPowerAsync()
        {
            if (isPowerBusy) return;
            foreach (var r in new[] { rowNicWake, rowFastStartup, rowWakeTimers, rowWakeDevices, rowMaintenance, rowArso, rowAutoReboot })
                r.SetState(CheckState2.Unknown, "Checking\u2026");

            var th = new Thread(() =>
            {
                int? hiber = ReadDword(KeyPower, "HiberbootEnabled");
                bool? timers = WakeTimersOn();
                var devices = WakeArmedDevices();
                int? maint = ReadDword(KeyMaint, "WakeUp");
                int? arso = ReadDword(KeyArso, "DisableAutomaticRestartSignOn");
                int? reboot = ReadDword(KeyCrash, "AutoReboot");
                var nic = NicWakeSettings();

                Ui(() =>
                {
                    int risks = 0;
                    int nicOn = nic.Count(x => x.Value != "0");
                    if (nic.Count == 0) rowNicWake.SetState(CheckState2.Ok, "Not supported");
                    else if (nicOn == 0) rowNicWake.SetState(CheckState2.Ok, "Off");
                    else { rowNicWake.SetState(CheckState2.Risk, "On"); risks++; }

                    if (hiber.HasValue && hiber.Value == 0) rowFastStartup.SetState(CheckState2.Ok, "Off");
                    else { rowFastStartup.SetState(CheckState2.Risk, "On"); risks++; }

                    if (!timers.HasValue) rowWakeTimers.SetState(CheckState2.Unknown, "Unknown");
                    else if (timers.Value) { rowWakeTimers.SetState(CheckState2.Risk, "Allowed"); risks++; }
                    else rowWakeTimers.SetState(CheckState2.Ok, "Disabled");

                    if (devices.Count == 0) rowWakeDevices.SetState(CheckState2.Ok, "None");
                    else { rowWakeDevices.SetState(CheckState2.Risk, devices.Count + (devices.Count == 1 ? " device" : " devices")); risks++; }
                    rowWakeDevices.Desc = devices.Count == 0
                        ? "No device can wake the PC"
                        : string.Join(", ", devices.ToArray());
                    rowWakeDevices.Invalidate();

                    if (maint.HasValue && maint.Value == 0) rowMaintenance.SetState(CheckState2.Ok, "Off");
                    else { rowMaintenance.SetState(CheckState2.Risk, "Allowed"); risks++; }

                    if (arso.HasValue && arso.Value == 1) rowArso.SetState(CheckState2.Ok, "Off");
                    else { rowArso.SetState(CheckState2.Risk, "On"); risks++; }

                    if (reboot.HasValue && reboot.Value == 0) rowAutoReboot.SetState(CheckState2.Ok, "Off");
                    else { rowAutoReboot.SetState(CheckState2.Risk, "On"); risks++; }

                    powerHeader.Subtitle = risks == 0
                        ? "All clear \u2014 Windows can't turn your PC back on by itself."
                        : risks + " setting" + (risks == 1 ? "" : "s") + " can turn your PC back on. Press \"Fix Auto Power-On\".";
                });
            });
            th.IsBackground = true;
            th.Start();
        }

        private void RunPowerFix(bool fix)
        {
            if (isPowerBusy) return;
            if (!isAdmin)
            {
                ThemedDialog.Show(this, "Administrator needed",
                    "Changing power settings needs administrator rights. Close the app, right-click it and choose \"Run as administrator\".",
                    new[] { "OK" }, 0, Theme.Amber);
                return;
            }

            if (fix)
            {
                int a = ThemedDialog.Show(this, "Fix auto power-on?",
                    "This will:\n" +
                    "\u2022  stop the network card from powering the PC on after shutdown (Wake-on-LAN)\n" +
                    "\u2022  turn off Fast Startup (shutdown becomes a real, full shutdown)\n" +
                    "\u2022  disable wake timers\n" +
                    "\u2022  stop the network card, mouse and keyboard from waking the PC\n" +
                    "\u2022  turn off maintenance wake-up and auto sign-in after updates\n" +
                    "\u2022  stay off instead of rebooting after a crash\n\n" +
                    "Your internet will drop for a few seconds while the network card restarts.\n" +
                    "Note: you won't be able to wake the PC from Sleep with the mouse or keyboard \u2014 use the power button. " +
                    "You can undo everything with \"Restore Defaults\".",
                    new[] { "Apply Fix", "Cancel" }, 0, Theme.Blue);
                if (a != 0) return;
            }
            else
            {
                int a = ThemedDialog.Show(this, "Restore defaults?",
                    "Turns Fast Startup, wake timers, maintenance wake-up, auto sign-in and crash restart back on, " +
                    "and lets the devices this app disabled wake the PC again.",
                    new[] { "Restore", "Cancel" }, 0, Theme.Blue);
                if (a != 0) return;
            }

            isPowerBusy = true;
            fixButton.Enabled = restoreButton.Enabled = recheckButton.Enabled = false;
            powerLog.Items.Clear();

            var th = new Thread(() =>
            {
                Action<string> log = msg => Ui(() => AddLog(powerLog, msg));
                int code;
                try
                {
                    if (fix)
                    {
                        Step(log, "Fast Startup \u2192 off", () => WriteDword(KeyPower, "HiberbootEnabled", 0));

                        Step(log, "Wake timers \u2192 disabled", () =>
                        {
                            Native.Run("powercfg.exe", "/setacvalueindex SCHEME_CURRENT SUB_SLEEP RTCWAKE 0", out code);
                            Native.Run("powercfg.exe", "/setdcvalueindex SCHEME_CURRENT SUB_SLEEP RTCWAKE 0", out code);
                            Native.Run("powercfg.exe", "/setactive SCHEME_CURRENT", out code);
                        });

                        var devices = WakeArmedDevices();
                        if (devices.Count > 0)
                        {
                            try
                            {
                                var saved = File.Exists(WakeListFile) ? File.ReadAllLines(WakeListFile).ToList() : new List<string>();
                                foreach (var d in devices) if (!saved.Contains(d)) saved.Add(d);
                                File.WriteAllLines(WakeListFile, saved.ToArray());
                            }
                            catch { }
                        }
                        foreach (var d in devices)
                        {
                            string dev = d;
                            Native.Run("powercfg.exe", "/devicedisablewake \"" + dev.Replace("\"", "") + "\"", out code);
                            log((code == 0 ? "Wake disabled: " : "Could not change: ") + dev);
                        }
                        if (devices.Count == 0) log("No devices were allowed to wake the PC.");

                        Step(log, "Maintenance wake-up \u2192 off", () => WriteDword(KeyMaint, "WakeUp", 0));
                        Step(log, "Auto sign-in after updates \u2192 off", () => WriteDword(KeyArso, "DisableAutomaticRestartSignOn", 1));
                        Step(log, "Restart on system failure \u2192 off", () => WriteDword(KeyCrash, "AutoReboot", 0));
                        foreach (var line in SetNicWake(false)) log(line);
                        log("Done. Use \"Full Shut Down\" for a clean power-off.");
                    }
                    else
                    {
                        Step(log, "Fast Startup \u2192 on", () => WriteDword(KeyPower, "HiberbootEnabled", 1));
                        Step(log, "Wake timers \u2192 enabled", () =>
                        {
                            Native.Run("powercfg.exe", "/setacvalueindex SCHEME_CURRENT SUB_SLEEP RTCWAKE 1", out code);
                            Native.Run("powercfg.exe", "/setdcvalueindex SCHEME_CURRENT SUB_SLEEP RTCWAKE 1", out code);
                            Native.Run("powercfg.exe", "/setactive SCHEME_CURRENT", out code);
                        });
                        if (File.Exists(WakeListFile))
                        {
                            foreach (var d in File.ReadAllLines(WakeListFile))
                            {
                                if (d.Trim().Length == 0) continue;
                                Native.Run("powercfg.exe", "/deviceenablewake \"" + d.Replace("\"", "") + "\"", out code);
                                log((code == 0 ? "Wake enabled: " : "Could not change: ") + d);
                            }
                            try { File.Delete(WakeListFile); } catch { }
                        }
                        Step(log, "Maintenance wake-up \u2192 on", () => WriteDword(KeyMaint, "WakeUp", 1));
                        Step(log, "Auto sign-in after updates \u2192 default", () => DeleteValue(KeyArso, "DisableAutomaticRestartSignOn"));
                        Step(log, "Restart on system failure \u2192 on", () => WriteDword(KeyCrash, "AutoReboot", 1));
                        foreach (var line in SetNicWake(true)) log(line);
                        log("Defaults restored.");
                    }
                }
                finally
                {
                    Ui(() =>
                    {
                        isPowerBusy = false;
                        fixButton.Enabled = restoreButton.Enabled = recheckButton.Enabled = true;
                        CheckPowerAsync();
                    });
                }
            });
            th.IsBackground = true;
            th.Start();
        }

        private static void Step(Action<string> log, string label, Action a)
        {
            try { a(); log("\u2713 " + label); }
            catch (Exception ex) { log("\u2717 " + label + " \u2014 " + ex.Message); }
        }

        private void FullShutdown()
        {
            int a = ThemedDialog.Show(this, "Shut down the PC?",
                "Windows will do a full shutdown now (not the hybrid Fast Startup one). Save your work first.",
                new[] { "Shut Down", "Cancel" }, 0, Theme.Red);
            if (a != 0) return;
            int code;
            Native.Run("shutdown.exe", "/s /f /t 0", out code);
        }

        // =================================================================
        //  PAGE 4: Settings (updates + about)
        // =================================================================

        private readonly AppSettings settings = AppSettings.Load();
        private UpdateInfo pendingUpdate;
        private volatile bool isUpdating;
        private bool updateInstalled;
        private Label updStatus, updLastCheck;
        private CheckToggle autoUpdateToggle;
        private FlatButton checkUpdateButton, installUpdateButton;
        private SlimBar updProgress;
        private TextBox updNotes;

        // =================================================================
        //  PAGE 4: Tools (one-click fixes every user needs sooner or later)
        // =================================================================

        private PageHeader toolsHeader;
        private ListBox toolsLog;
        private Label pcInfo;
        private DriveList drives;
        private volatile bool toolBusy;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            public uint dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX buffer);

        [DllImport("psapi.dll")]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        [DllImport("kernel32.dll")]
        private static extern ulong GetTickCount64();

        private static MEMORYSTATUSEX Memory()
        {
            var m = new MEMORYSTATUSEX();
            GlobalMemoryStatusEx(m);
            return m;
        }

        private Control BuildToolsPage()
        {
            var page = NewPage();
            var layout = Table(1, 2, Theme.Bg);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            toolsHeader = new PageHeader("Tools", "One-click fixes for the most common PC problems.") { Dock = DockStyle.Fill };

            var body = Table(2, 1, Theme.Bg);
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330F));

            // ---- tool tiles (2 x 3) + a full-width "Delete Permanently" row
            var grid2 = Table(2, 4, Theme.Bg);
            grid2.Margin = new Padding(0, 0, 14, 0);
            grid2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            grid2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            for (int i = 0; i < 4; i++) grid2.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));

            var tiles = new[]
            {
                new ToolTile("\uE945", "Free Up RAM", "Release memory that apps are holding but not using.", FreeRam),
                new ToolTile("\uE90F", "Repair Windows", "Find and fix damaged Windows system files (DISM + SFC).", RepairWindows),
                new ToolTile("\uE774", "Fix Internet", "Reset DNS, Winsock and IP when the internet stops working.", FixInternet),
                new ToolTile("\uE72C", "Restart Explorer", "Fixes a frozen taskbar, Start menu or desktop.", () => RunTool("Restart Explorer", RestartExplorer)),
                new ToolTile("\uEDA2", "Disk Health", "Check whether your hard drive or SSD is healthy.", DiskHealth),
                new ToolTile("\uE83F", "Battery Report", "See your laptop battery's real capacity and wear.", BatteryReport)
            };
            for (int i = 0; i < tiles.Length; i++)
            {
                tiles[i].Dock = DockStyle.Fill;
                tiles[i].Margin = new Padding(i % 2 == 0 ? 0 : 7, i < 2 ? 0 : 7, i % 2 == 0 ? 7 : 0, 7);
                grid2.Controls.Add(tiles[i], i % 2, i / 2);
            }
            var wipe = new ToolTile("\uE74D", "Delete Permanently",
                "Erase any file or folder, even locked ones that won't delete normally (like a huge node_modules). Skips the Recycle Bin \u2014 can't be undone.",
                ShowPermanentDelete) { Dock = DockStyle.Fill, Margin = new Padding(0, 7, 0, 0), Danger = true };
            grid2.Controls.Add(wipe, 0, 3);
            grid2.SetColumnSpan(wipe, 2);

            // ---- this PC + activity
            var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(20, 16, 20, 16) };
            var t = Table(1, 6, Theme.Card);
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 92F));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 92F));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            pcInfo = MakeLabel("", Theme.F(8.5f), Theme.Soft, ContentAlignment.TopLeft);
            pcInfo.AutoEllipsis = false;
            drives = new DriveList { Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 0) };
            toolsLog = MakeLog();

            t.Controls.Add(MakeLabel("This PC", Theme.F(12f, FontStyle.Bold), Theme.Text), 0, 0);
            t.Controls.Add(pcInfo, 0, 1);
            t.Controls.Add(Caption("DRIVES"), 0, 2);
            t.Controls.Add(drives, 0, 3);
            t.Controls.Add(Caption("ACTIVITY"), 0, 4);
            t.Controls.Add(toolsLog, 0, 5);
            card.Controls.Add(t);

            body.Controls.Add(grid2, 0, 0);
            body.Controls.Add(card, 1, 0);
            layout.Controls.Add(toolsHeader, 0, 0);
            layout.Controls.Add(body, 0, 1);
            page.Controls.Add(layout);
            return page;
        }

        private void RefreshPcInfo()
        {
            string win = "Windows", cpu = "Unknown CPU";
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    string name = Convert.ToString(k.GetValue("ProductName"));
                    int build;
                    int.TryParse(Convert.ToString(k.GetValue("CurrentBuildNumber")), out build);
                    if (build >= 22000) name = name.Replace("Windows 10", "Windows 11"); // Win11 still reports "Windows 10"
                    string ver = Convert.ToString(k.GetValue("DisplayVersion"));
                    win = name + (string.IsNullOrEmpty(ver) ? "" : " (" + ver + ")");
                }
                using (var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                    cpu = Convert.ToString(k.GetValue("ProcessorNameString")).Trim();
            }
            catch { }
            var m = Memory();
            var up = TimeSpan.FromMilliseconds(GetTickCount64());
            string upText = up.Days > 0 ? up.Days + "d " + up.Hours + "h" : up.Hours + "h " + up.Minutes + "m";
            pcInfo.Text = win + "\n" + cpu + "\n" +
                          "RAM  " + Theme.FormatBytes((long)(m.ullTotalPhys - m.ullAvailPhys)) + " used of " + Theme.FormatBytes((long)m.ullTotalPhys) +
                          " (" + m.dwMemoryLoad + "%)\n" +
                          "Up for  " + upText + (up.TotalDays >= 3 ? "  \u2014 a restart would help" : "");
            drives.Reload();
        }

        /// Runs a tool on a background thread, logging what it says. Only one tool runs at a time.
        private void RunTool(string name, Func<bool> work)
        {
            if (toolBusy)
            {
                ThemedDialog.Show(this, "Please wait", "Another tool is still running.", new[] { "OK" }, 0, Theme.Blue);
                return;
            }
            toolBusy = true;
            toolsHeader.Subtitle = name + " is running\u2026";
            AddLog(toolsLog, name + ": started");
            var th = new Thread(() =>
            {
                bool ok;
                try { ok = work(); }
                catch (Exception ex) { ok = false; ToolLog(name + ": " + ex.Message); }
                Ui(() =>
                {
                    toolBusy = false;
                    toolsHeader.Subtitle = name + (ok ? " finished." : " didn't finish \u2014 see Activity.");
                    AddLog(toolsLog, name + (ok ? ": done" : ": failed"));
                    RefreshPcInfo();
                });
            });
            th.IsBackground = true;
            th.Start();
        }

        private void ToolLog(string line)
        {
            Ui(() => AddLog(toolsLog, line));
        }

        private static string Clean(string s)
        {
            return (s ?? "").Replace("\0", "");
        }

        // ---- 1. Free up RAM

        private void FreeRam()
        {
            RunTool("Free Up RAM", () =>
            {
                ulong before = Memory().ullAvailPhys;
                int trimmed = 0;
                foreach (var p in Process.GetProcesses())
                {
                    try { if (p.Id > 4 && EmptyWorkingSet(p.Handle)) trimmed++; }
                    catch { }
                    finally { p.Dispose(); }
                }
                Thread.Sleep(800);
                ulong after = Memory().ullAvailPhys;
                long freed = after > before ? (long)(after - before) : 0;
                ToolLog("Freed " + Theme.FormatBytes(freed) + " from " + trimmed + " processes.");
                Ui(() => ThemedDialog.Show(this, "RAM freed",
                    Theme.FormatBytes(freed) + " of memory was released.\n\nAvailable now: " + Theme.FormatBytes((long)after) + ".",
                    new[] { "OK" }, 0, Theme.Green));
                return true;
            });
        }

        // ---- 2. Repair Windows (DISM + SFC)

        private void RepairWindows()
        {
            if (!isAdmin) { NeedAdmin(); return; }
            int a = ThemedDialog.Show(this, "Repair Windows?",
                "This checks every Windows system file and repairs damaged ones. It usually takes 10\u201330 minutes and needs an internet connection.\n\n" +
                "You can keep using the PC while it runs.",
                new[] { "Start Repair", "Cancel" }, 0, Theme.Blue);
            if (a != 0) return;

            RunTool("Repair Windows", () =>
            {
                int code;
                ToolLog("Step 1/2: DISM is repairing the Windows image\u2026");
                string dism = Clean(Native.Run("dism.exe", "/Online /Cleanup-Image /RestoreHealth", out code));
                ToolLog(code == 0 ? "DISM: Windows image is healthy." : "DISM finished with code " + code + ".");

                ToolLog("Step 2/2: SFC is checking system files\u2026");
                string sfc = Clean(Native.Run("sfc.exe", "/scannow", out code));
                string result;
                if (sfc.Contains("did not find any integrity violations")) result = "No problems found. Your Windows files are healthy.";
                else if (sfc.Contains("successfully repaired")) result = "Damaged files were found and repaired. Restart your PC to finish.";
                else if (sfc.Contains("unable to fix")) result = "Some damaged files could not be repaired. Run the repair again after a restart.";
                else result = "The check finished (code " + code + ").";
                ToolLog("SFC: " + result);
                Ui(() => ThemedDialog.Show(this, "Repair finished", result, new[] { "OK" }, 0, Theme.Green));
                return true;
            });
        }

        // ---- 3. Fix internet

        private void FixInternet()
        {
            if (!isAdmin) { NeedAdmin(); return; }
            int a = ThemedDialog.Show(this, "Fix internet connection?",
                "This clears the DNS cache and resets Winsock and TCP/IP settings. Your internet may drop for a few seconds.\n\n" +
                "Restart the PC afterwards for the full fix.",
                new[] { "Fix Now", "Cancel" }, 0, Theme.Blue);
            if (a != 0) return;

            RunTool("Fix Internet", () =>
            {
                int code;
                Native.Run("ipconfig.exe", "/flushdns", out code); ToolLog("DNS cache cleared" + (code == 0 ? "" : " (code " + code + ")"));
                Native.Run("netsh.exe", "winsock reset", out code); ToolLog("Winsock reset" + (code == 0 ? "" : " (code " + code + ")"));
                Native.Run("netsh.exe", "int ip reset", out code); ToolLog("TCP/IP reset" + (code == 0 ? "" : " (code " + code + ")"));
                Native.Run("ipconfig.exe", "/renew", out code); ToolLog("IP address renewed" + (code == 0 ? "" : " (code " + code + ")"));
                Ui(() =>
                {
                    int r = ThemedDialog.Show(this, "Network reset done",
                        "Restart your PC to finish the fix.", new[] { "Restart Later", "Restart Now" }, 0, Theme.Green);
                    if (r == 1) { int c; Native.Run("shutdown.exe", "/r /t 5", out c); }
                });
                return true;
            });
        }

        // ---- 4. Restart Explorer

        private static bool RestartExplorer()
        {
            foreach (var p in Process.GetProcessesByName("explorer"))
            {
                try { p.Kill(); p.WaitForExit(3000); } catch { }
                finally { p.Dispose(); }
            }
            // Windows normally restarts the shell by itself; start it if it didn't
            for (int i = 0; i < 20; i++)
            {
                Thread.Sleep(250);
                if (Process.GetProcessesByName("explorer").Length > 0) return true;
            }
            Process.Start(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"));
            return true;
        }

        // ---- 5. Disk health

        private void DiskHealth()
        {
            RunTool("Disk Health", () =>
            {
                int code;
                string o = Native.Run("powershell.exe",
                    "-NoProfile -NonInteractive -Command \"Get-PhysicalDisk | ForEach-Object { $_.FriendlyName + '|' + $_.MediaType + '|' + $_.HealthStatus + '|' + $_.Size }\"",
                    out code);
                var lines = o.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Where(l => l.Contains("|")).ToList();
                if (lines.Count == 0) { ToolLog("Couldn't read disk health."); return false; }

                bool allOk = true;
                var report = new List<string>();
                foreach (var l in lines)
                {
                    var p = l.Split('|');
                    long size;
                    long.TryParse(p.Length > 3 ? p[3] : "0", out size);
                    string health = p.Length > 2 ? p[2].Trim() : "Unknown";
                    if (!health.Equals("Healthy", StringComparison.OrdinalIgnoreCase)) allOk = false;
                    string line = p[0].Trim() + "  (" + (p.Length > 1 ? p[1].Trim() : "") + ", " + Theme.FormatBytes(size) + ")  \u2014  " + health;
                    report.Add("\u2022  " + line);
                    ToolLog(line);
                }
                string msg = string.Join("\n", report.ToArray()) + "\n\n" +
                    (allOk ? "All drives report healthy." : "A drive reports a problem. Back up your important files as soon as possible.");
                Ui(() => ThemedDialog.Show(this, allOk ? "Drives are healthy" : "Drive warning", msg, new[] { "OK" }, 0, allOk ? Theme.Green : Theme.Red));
                return true;
            });
        }

        // ---- 6. Battery report

        private void BatteryReport()
        {
            RunTool("Battery Report", () =>
            {
                string file = Path.Combine(AppInfo.DataDir, "battery-report.html");
                int code;
                string o = Native.Run("powercfg.exe", "/batteryreport /output \"" + file + "\"", out code);
                if (code != 0 || !File.Exists(file))
                {
                    ToolLog("No battery found \u2014 this looks like a desktop PC.");
                    Ui(() => ThemedDialog.Show(this, "No battery found", "This PC doesn't have a battery, so there's no report to show.", new[] { "OK" }, 0, Theme.Blue));
                    return true;
                }
                ToolLog("Battery report saved and opened.");
                Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
                return true;
            });
        }

        private void ShowPermanentDelete()
        {
            if (!Visible) ShowFromTray(-1);
            using (var f = new PermanentDeleteForm(line => AddLog(toolsLog, line)))
                f.ShowDialog(this);
            RefreshPcInfo();
        }

        private void NeedAdmin()
        {
            ThemedDialog.Show(this, "Administrator needed",
                "This tool needs administrator rights. Close the app, right-click it and choose \"Run as administrator\".",
                new[] { "OK" }, 0, Theme.Amber);
        }

        private Control BuildSettingsPage()
        {
            var page = NewPage();
            var layout = Table(1, 2, Theme.Bg);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var header = new PageHeader("Settings", "Updates and information about this app.") { Dock = DockStyle.Fill };

            var body = Table(2, 1, Theme.Bg);
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330F));

            // ---- updates card
            var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(22, 18, 22, 18), Margin = new Padding(0, 0, 14, 0) };
            var t = Table(1, 10, Theme.Card);
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));  // title
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));  // installed version
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));  // status
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));  // last check
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));  // toggle
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));  // hint
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 62F));  // buttons
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 18F));  // progress
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));  // notes caption
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));  // notes

            updStatus = MakeLabel("Not checked yet", Theme.F(13f, FontStyle.Bold), Theme.Text);
            updLastCheck = MakeLabel(LastCheckText(), Theme.F(8.5f), Theme.Faint, ContentAlignment.TopLeft);

            autoUpdateToggle = new CheckToggle { Text = "Install updates automatically", Dock = DockStyle.Fill, Checked = settings.AutoUpdate };
            autoUpdateToggle.CheckedChanged += (s, e) => { settings.AutoUpdate = autoUpdateToggle.Checked; settings.Save(); };

            var hint = MakeLabel("When this is off, the app tells you about new versions and you install them here.",
                Theme.F(8.5f), Theme.Muted, ContentAlignment.TopLeft);

            var btnRow = Table(2, 1, Theme.Card);
            btnRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            btnRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            checkUpdateButton = new FlatButton { Text = "Check for Updates", Glyph = "\uE72C", Dock = DockStyle.Fill, Margin = new Padding(0, 8, 6, 8), Font = Theme.Semi(9.5f) };
            checkUpdateButton.SetColor(Theme.Secondary);
            checkUpdateButton.Click += (s, e) => CheckForUpdates(true);
            installUpdateButton = new FlatButton { Text = "Install Update", Glyph = "\uE896", Dock = DockStyle.Fill, Margin = new Padding(6, 8, 0, 8), Font = Theme.Semi(9.5f), Enabled = false };
            installUpdateButton.SetColor(Theme.Blue);
            installUpdateButton.Click += (s, e) => InstallUpdate(false);
            btnRow.Controls.Add(checkUpdateButton, 0, 0);
            btnRow.Controls.Add(installUpdateButton, 1, 0);

            updProgress = new SlimBar { Dock = DockStyle.Fill };

            updNotes = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = Theme.Inset,
                ForeColor = Theme.Soft,
                Font = Theme.F(9f),
                ScrollBars = ScrollBars.Vertical,
                Text = "Release notes appear here when an update is found.",
                Margin = new Padding(0, 6, 0, 0)
            };
            updNotes.HandleCreated += (s, e) => Native.DarkScroll(updNotes);

            t.Controls.Add(MakeLabel("Updates", Theme.F(12f, FontStyle.Bold), Theme.Text), 0, 0);
            t.Controls.Add(MakeLabel("Installed version  " + AppInfo.VersionText, Theme.F(9f), Theme.Muted), 0, 1);
            t.Controls.Add(updStatus, 0, 2);
            t.Controls.Add(updLastCheck, 0, 3);
            t.Controls.Add(autoUpdateToggle, 0, 4);
            t.Controls.Add(hint, 0, 5);
            t.Controls.Add(btnRow, 0, 6);
            t.Controls.Add(updProgress, 0, 7);
            t.Controls.Add(Caption("RELEASE NOTES"), 0, 8);
            t.Controls.Add(updNotes, 0, 9);
            card.Controls.Add(t);

            // ---- about card
            var about = new Card { Dock = DockStyle.Fill, Padding = new Padding(20, 18, 20, 18) };
            var a = Table(1, 7, Theme.Card);
            a.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            a.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            a.RowStyles.Add(new RowStyle(SizeType.Absolute, 70F));
            a.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
            a.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            a.RowStyles.Add(new RowStyle(SizeType.Absolute, 80F));
            a.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var desc = MakeLabel("Closes apps before shutdown, cleans temporary files and stops your PC from turning itself back on.",
                Theme.F(9f), Theme.Muted, ContentAlignment.TopLeft);
            desc.AutoEllipsis = false;
            var site = new FlatButton { Text = "Website & Downloads", Glyph = "\uE774", Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 6), Font = Theme.Semi(9.5f) };
            site.SetColor(Theme.Secondary);
            site.Click += (s, e) => { try { Process.Start(AppInfo.RepoUrl); } catch { } };
            var lic = MakeLabel("\u00A9 2026 Vibe. All rights reserved.\nLicensed for personal use only. Modifying, " +
                "reverse-engineering or redistributing this software is not allowed.", Theme.F(8.5f), Theme.Faint, ContentAlignment.TopLeft);
            lic.AutoEllipsis = false;

            a.Controls.Add(MakeLabel("Vibe Stop Processing", Theme.F(12f, FontStyle.Bold), Theme.Text), 0, 0);
            a.Controls.Add(MakeLabel("Version " + AppInfo.VersionText, Theme.F(9f), Theme.Blue), 0, 1);
            a.Controls.Add(desc, 0, 2);
            a.Controls.Add(site, 0, 3);
            a.Controls.Add(Caption("LICENSE"), 0, 4);
            a.Controls.Add(lic, 0, 5);
            about.Controls.Add(a);

            // ---- general card (start-up + tray)
            var general = new Card { Dock = DockStyle.Fill, Padding = new Padding(20, 16, 20, 12), Margin = new Padding(0, 0, 0, 14) };
            var gt = Table(1, 4, Theme.Card);
            gt.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            gt.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            gt.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            gt.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            var startToggle = new CheckToggle { Text = "Start with Windows (in the tray)", Dock = DockStyle.Fill, Checked = settings.StartWithWindows };
            startToggle.CheckedChanged += (s, e) =>
            {
                settings.StartWithWindows = startToggle.Checked;
                settings.Save();
                SyncStartupTask();
            };
            var trayToggle = new CheckToggle { Text = "Close button keeps it in the tray", Dock = DockStyle.Fill, Checked = settings.CloseToTray };
            trayToggle.CheckedChanged += (s, e) => { settings.CloseToTray = trayToggle.Checked; settings.Save(); };
            gt.Controls.Add(MakeLabel("General", Theme.F(12f, FontStyle.Bold), Theme.Text), 0, 0);
            gt.Controls.Add(startToggle, 0, 1);
            gt.Controls.Add(trayToggle, 0, 2);
            general.Controls.Add(gt);

            var right = Table(1, 2, Theme.Bg);
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 136F));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            right.Controls.Add(general, 0, 0);
            right.Controls.Add(about, 0, 1);

            body.Controls.Add(card, 0, 0);
            body.Controls.Add(right, 1, 0);
            layout.Controls.Add(header, 0, 0);
            layout.Controls.Add(body, 0, 1);
            page.Controls.Add(layout);
            return page;
        }

        private string LastCheckText()
        {
            if (settings.LastCheck == DateTime.MinValue) return "Never checked";
            return "Last checked " + settings.LastCheck.ToLocalTime().ToString("d MMM yyyy, h:mm tt");
        }

        private void SetUpdateStatus(string text, Color color)
        {
            updStatus.Text = text;
            updStatus.ForeColor = color;
        }

        private void CheckForUpdates(bool userInitiated)
        {
            if (isUpdating || updateInstalled) return;
            isUpdating = true;
            checkUpdateButton.Enabled = false;
            SetUpdateStatus("Checking for updates\u2026", Theme.Muted);

            var th = new Thread(() =>
            {
                UpdateInfo info = null;
                string error = null;
                try { info = Updater.FetchLatest(); }
                catch (Exception ex) { error = ex.Message; }

                Ui(() =>
                {
                    isUpdating = false;
                    checkUpdateButton.Enabled = true;
                    settings.LastCheck = DateTime.UtcNow;
                    settings.Save();
                    updLastCheck.Text = LastCheckText();

                    if (error != null)
                    {
                        SetUpdateStatus("Couldn't check for updates", Theme.Amber);
                        updNotes.Text = "Check your internet connection and try again.\r\n\r\n" + error;
                        if (userInitiated)
                            ThemedDialog.Show(this, "Update check failed", "Couldn't reach the update server.\n\n" + error, new[] { "OK" }, 0, Theme.Amber);
                        return;
                    }

                    if (!Updater.IsNewer(info))
                    {
                        pendingUpdate = null;
                        installUpdateButton.Enabled = false;
                        navItems[SettingsPage].Badge = false;
                        SetUpdateStatus("You're up to date", Theme.Green);
                        updNotes.Text = info != null && !string.IsNullOrEmpty(info.Notes)
                            ? "What's in " + info.Tag + ":\r\n\r\n" + info.Notes.Replace("\r\n", "\n").Replace("\n", "\r\n")
                            : "You have the latest version.";
                        return;
                    }

                    pendingUpdate = info;
                    installUpdateButton.Enabled = true;
                    navItems[SettingsPage].Badge = true;
                    SetUpdateStatus("Version " + info.Tag.TrimStart('v', 'V') + " is available", Theme.Blue);
                    updNotes.Text = string.IsNullOrEmpty(info.Notes)
                        ? "No release notes."
                        : info.Notes.Replace("\r\n", "\n").Replace("\n", "\r\n");

                    if (!userInitiated && settings.AutoUpdate)
                    {
                        InstallUpdate(true);
                    }
                    else
                    {
                        int ans = ThemedDialog.Show(this, "Update available",
                            "Stop Processing " + info.Tag.TrimStart('v', 'V') + " is available. You have " + AppInfo.VersionText + ".",
                            new[] { "Install now", "Later" }, 0, Theme.Blue);
                        if (ans == 0) InstallUpdate(false);
                    }
                });
            });
            th.IsBackground = true;
            th.Start();
        }

        private void InstallUpdate(bool automatic)
        {
            var info = pendingUpdate;
            if (info == null || isUpdating || updateInstalled) return;
            isUpdating = true;
            checkUpdateButton.Enabled = false;
            installUpdateButton.Enabled = false;
            updProgress.Maximum = 100;
            updProgress.Value = 0;
            SetUpdateStatus("Downloading " + info.Tag + "\u2026", Theme.Blue);

            var th = new Thread(() =>
            {
                string error = null;
                try
                {
                    string file = Updater.Download(info, p => Ui(() => updProgress.Value = p));
                    Updater.Apply(file);
                    Updater.UpdateInstalledVersion(info.Version);
                }
                catch (Exception ex) { error = ex.Message; }

                Ui(() =>
                {
                    isUpdating = false;
                    checkUpdateButton.Enabled = true;
                    if (error != null)
                    {
                        installUpdateButton.Enabled = true;
                        SetUpdateStatus("Update failed", Theme.Red);
                        updNotes.Text = error;
                        if (!automatic)
                            ThemedDialog.Show(this, "Update failed", error, new[] { "OK" }, 0, Theme.Red);
                        return;
                    }

                    updateInstalled = true;
                    navItems[SettingsPage].Badge = false;
                    updProgress.Value = 100;
                    SetUpdateStatus("Update installed \u2014 restart to finish", Theme.Green);

                    if (isStopping || isCleaning) return; // don't interrupt a running job

                    if (!Visible)
                    {
                        // running quietly in the tray: restart straight back into the tray
                        try { Updater.Restart(true); reallyExit = true; Close(); } catch { reallyExit = false; }
                        return;
                    }
                    int ans = ThemedDialog.Show(this, "Update ready",
                        "Vibe Stop Processing " + info.Tag.TrimStart('v', 'V') + " has been installed. Restart the app now to use it?",
                        new[] { "Restart now", "Later" }, 0, Theme.Green);
                    if (ans == 0)
                    {
                        try { Updater.Restart(false); reallyExit = true; Close(); } catch { reallyExit = false; }
                    }
                });
            });
            th.IsBackground = true;
            th.Start();
        }

        // =================================================================

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            bool systemClosing = e.CloseReason == CloseReason.WindowsShutDown || e.CloseReason == CloseReason.TaskManagerClosing;

            // the close button only hides the window; Exit from the tray menu really quits
            if (!reallyExit && !systemClosing && settings.CloseToTray && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                if (!settings.TrayHintShown)
                {
                    settings.TrayHintShown = true;
                    settings.Save();
                    tray.ShowBalloonTip(5000, "Vibe Stop Processing is still running",
                        "It's in the system tray. Right-click the icon and choose Exit to close it.", ToolTipIcon.Info);
                }
                return;
            }

            if ((isStopping || isCleaning) && !systemClosing)
            {
                int r = ThemedDialog.Show(this, "Still working",
                    "A run is still in progress. Close anyway?", new[] { "Close", "Keep open" }, 1, Theme.Amber);
                if (r != 0) { e.Cancel = true; return; }
            }
            if (stopCancel != null) stopCancel.Cancel();
            if (refreshTimer != null) refreshTimer.Stop();
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            base.OnFormClosing(e);
        }

        private class ProcRow
        {
            public int Pid;
            public string Name;
            public long Memory;
            public double Cpu;
            public string Role;
            public string Path;
            public int Session;
        }

        private static Mutex singleInstance;

        [STAThread]
        static void Main()
        {
            // only one copy runs; starting it again just brings the existing window forward
            bool owned;
            singleInstance = new Mutex(true, @"Local\VibeStopProcessing_SingleInstance", out owned);
            if (!owned && Environment.GetCommandLineArgs().Contains("--updated"))
            {
                // after an update the old copy is still closing - wait for it
                try { owned = singleInstance.WaitOne(15000); }
                catch (AbandonedMutexException) { owned = true; }
            }
            if (!owned)
            {
                Native.PostMessage((IntPtr)0xffff, WM_SHOWME, IntPtr.Zero, IntPtr.Zero);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            GC.KeepAlive(singleInstance);
        }
    }

    // ------------------------------------------------------------------
    //  Shell pieces
    // ------------------------------------------------------------------
    /// "Delete Permanently": wipes a folder and everything in it without using the Recycle Bin.
    class PermanentDeleteForm : Form
    {
        private readonly TextBox pathBox;
        private readonly Label info;
        private readonly FlatButton deleteBtn, cancelBtn, browseBtn;
        private readonly CheckToggle keepFolder;
        private readonly System.Windows.Forms.Timer debounce;
        private readonly Action<string> log;
        private int scanId;
        private string scanPath;
        private long scanSize;
        private int scanFiles;
        private volatile bool deleting;

        public PermanentDeleteForm(Action<string> log)
        {
            this.log = log;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            BackColor = Theme.Line;
            Padding = new Padding(1);
            KeyPreview = true;
            ClientSize = new Size(600, 340);

            var inner = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface };
            inner.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) Native.DragWindow(this); };
            Controls.Add(inner);
            inner.Controls.Add(new Panel { BackColor = Theme.Red, Bounds = new Rectangle(0, 0, 598, 3) });
            inner.Controls.Add(new Label { Text = "Delete Permanently", Font = Theme.F(13f, FontStyle.Bold), ForeColor = Theme.Text, AutoSize = true, Location = new Point(24, 22) });
            inner.Controls.Add(new Label
            {
                Text = "Deletes a file or folder (with everything inside) \u2014 even locked ones. Nothing goes to the Recycle Bin.",
                Font = Theme.F(9.5f), ForeColor = Theme.Muted, Bounds = new Rectangle(25, 56, 550, 22), UseMnemonic = false
            });
            inner.Controls.Add(new Label { Text = "FILE OR FOLDER PATH", Font = Theme.Semi(8f), ForeColor = Theme.Faint, Bounds = new Rectangle(25, 90, 200, 18) });

            var pathHost = new Panel { BackColor = Theme.Inset, Bounds = new Rectangle(24, 110, 430, 38) };
            pathBox = new TextBox
            {
                BorderStyle = BorderStyle.None, BackColor = Theme.Inset, ForeColor = Theme.Text,
                Font = Theme.F(10f), Bounds = new Rectangle(10, 10, 410, 22)
            };
            pathHost.Controls.Add(pathBox);
            inner.Controls.Add(pathHost);

            browseBtn = new FlatButton { Text = "Browse", Glyph = "\uE8B7", Bounds = new Rectangle(464, 110, 112, 38), Font = Theme.Semi(9.5f) };
            browseBtn.SetColor(Theme.Secondary);
            browseBtn.Click += (s, e) => Browse();
            inner.Controls.Add(browseBtn);

            info = new Label
            {
                Text = "Type or paste a folder path, or click Browse.", Font = Theme.F(9.5f), ForeColor = Theme.Soft,
                Bounds = new Rectangle(25, 158, 550, 22), UseMnemonic = false, AutoEllipsis = true
            };
            inner.Controls.Add(info);

            keepFolder = new CheckToggle { Text = "Keep the empty folder (delete only what's inside it)", Bounds = new Rectangle(24, 186, 520, 30) };
            inner.Controls.Add(keepFolder);

            var warn = new Panel { BackColor = Theme.Mix(Theme.Surface, Theme.Red, 0.16), Bounds = new Rectangle(24, 226, 552, 44) };
            warn.Controls.Add(new Label { Text = "\uE7BA", Font = Theme.Icon(13f), ForeColor = Theme.Red, Bounds = new Rectangle(12, 0, 26, 44), TextAlign = ContentAlignment.MiddleLeft });
            warn.Controls.Add(new Label
            {
                Text = "Files deleted here can NOT be recovered. Double-check the path before you continue.",
                Font = Theme.Semi(9.5f), ForeColor = Theme.Lighten(Theme.Red, 0.25), Bounds = new Rectangle(42, 0, 500, 44),
                TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false
            });
            inner.Controls.Add(warn);

            deleteBtn = new FlatButton { Text = "Delete Permanently", Glyph = "\uE74D", Bounds = new Rectangle(576 - 190, 286, 190, 38), Font = Theme.Semi(9.5f), Enabled = false };
            deleteBtn.SetColor(Theme.Red);
            deleteBtn.Click += (s, e) => ConfirmAndDelete();
            inner.Controls.Add(deleteBtn);
            cancelBtn = new FlatButton { Text = "Cancel", Bounds = new Rectangle(576 - 190 - 10 - 110, 286, 110, 38), Font = Theme.Semi(9.5f) };
            cancelBtn.SetColor(Theme.Secondary);
            cancelBtn.Click += (s, e) => Close();
            inner.Controls.Add(cancelBtn);

            debounce = new System.Windows.Forms.Timer { Interval = 600 };
            debounce.Tick += (s, e) => { debounce.Stop(); Scan(); };
            pathBox.TextChanged += (s, e) => { deleteBtn.Enabled = false; debounce.Stop(); debounce.Start(); };
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape && !deleting) Close(); };
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; return cp; }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (deleting) { e.Cancel = true; return; } // never leave half-way
            debounce.Stop();
            base.OnFormClosing(e);
        }

        private void Browse()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Choose the folder to delete permanently", ShowNewFolderButton = false })
            {
                if (Directory.Exists(pathBox.Text.Trim().Trim('"'))) dlg.SelectedPath = pathBox.Text.Trim().Trim('"');
                if (dlg.ShowDialog(this) == DialogResult.OK) pathBox.Text = dlg.SelectedPath;
            }
        }

        /// Returns why a path must not be wiped, or null if it's allowed.
        public static string BlockedReason(string full)
        {
            string f = full.TrimEnd('\\');
            string root = (Path.GetPathRoot(f) ?? "").TrimEnd('\\');
            if (string.Equals(f, root, StringComparison.OrdinalIgnoreCase)) return "That is a whole drive.";

            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var protectedDirs = new[]
            {
                win,
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                profile,
                Path.GetDirectoryName(profile),
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Path.Combine(profile, "Downloads"),
                Path.Combine(profile, "OneDrive"),
                Path.GetDirectoryName(Application.ExecutablePath)
            };
            foreach (var p in protectedDirs)
            {
                if (string.IsNullOrEmpty(p)) continue;
                string pp = p.TrimEnd('\\');
                if (string.Equals(f, pp, StringComparison.OrdinalIgnoreCase))
                    return "This is an important Windows or user folder.";
                if (pp.StartsWith(f + "\\", StringComparison.OrdinalIgnoreCase))
                    return "This folder contains important Windows or user folders.";
            }
            if (f.StartsWith(win + "\\", StringComparison.OrdinalIgnoreCase)) return "Files inside the Windows folder are protected.";
            return null;
        }

        private string ResolvePath(out string error)
        {
            error = null;
            string raw = pathBox.Text.Trim().Trim('"').Trim();
            if (raw.Length == 0) { error = "Type or paste a folder path, or click Browse."; return null; }
            string full;
            try { full = Path.GetFullPath(raw).TrimEnd('\\'); }
            catch { error = "That path isn't valid."; return null; }
            if (!File.Exists(full) && !Directory.Exists(full)) { error = "Nothing found at that path."; return null; }
            string blocked = BlockedReason(full);
            if (blocked != null) { error = "Blocked for safety: " + blocked; return null; }
            return full;
        }

        private void Scan()
        {
            string error;
            string full = ResolvePath(out error);
            int id = ++scanId;
            scanPath = null;
            deleteBtn.Enabled = false;
            if (full == null)
            {
                info.ForeColor = error.StartsWith("Blocked") ? Theme.Amber : Theme.Soft;
                info.Text = error;
                return;
            }
            info.ForeColor = Theme.Soft;
            if (File.Exists(full))
            {
                long len = 0;
                try { len = new FileInfo(full).Length; } catch { }
                scanPath = full;
                scanSize = len;
                scanFiles = 1;
                keepFolder.Checked = false;
                keepFolder.Enabled = false;
                info.ForeColor = Theme.Text;
                info.Text = "1 file  \u00b7  " + Theme.FormatBytes(len);
                deleteBtn.Enabled = true;
                return;
            }
            keepFolder.Enabled = true;
            info.Text = "Calculating size\u2026";
            var th = new Thread(() =>
            {
                long size = 0;
                int files = 0, folders = 0;
                var stack = new Stack<string>();
                stack.Push(full);
                while (stack.Count > 0 && id == scanId)
                {
                    string d = stack.Pop();
                    try
                    {
                        foreach (var f in Directory.GetFiles(d))
                        {
                            files++;
                            try { size += new FileInfo(f).Length; } catch { }
                        }
                        foreach (var s in Directory.GetDirectories(d))
                        {
                            folders++;
                            try { if ((File.GetAttributes(s) & FileAttributes.ReparsePoint) != 0) continue; } catch { continue; }
                            stack.Push(s);
                        }
                    }
                    catch { }
                    if (files % 3000 == 0)
                    {
                        int fc = files;
                        Post(() => { if (id == scanId) info.Text = "Calculating size\u2026 " + fc.ToString("N0") + " files so far"; });
                    }
                }
                long sz = size;
                int fl = files, fo = folders;
                Post(() =>
                {
                    if (id != scanId || IsDisposed) return;
                    scanPath = full;
                    scanSize = sz;
                    scanFiles = fl;
                    info.ForeColor = Theme.Text;
                    info.Text = Plural(fl, "file") + " in " + Plural(fo + 1, "folder") + "  \u00B7  " + Theme.FormatBytes(sz);
                    deleteBtn.Enabled = true;
                });
            });
            th.IsBackground = true;
            th.Start();
        }

        private void Post(Action a)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(a); } catch { }
        }

        private void ConfirmAndDelete()
        {
            string full = scanPath;
            if (full == null || deleting) return;
            bool isDir = Directory.Exists(full);
            bool keep = isDir && keepFolder.Checked;

            int a = ThemedDialog.Show(this, "Delete permanently?",
                "You are about to PERMANENTLY delete" + (keep ? " everything inside" : "") + ":\n\n" +
                full + "\n\n" +
                Plural(scanFiles, "file") + "  \u00B7  " + Theme.FormatBytes(scanSize) + "\n\n" +
                "\u26A0  This can NOT be undone. Nothing will go to the Recycle Bin and it cannot be recovered.\n" +
                "Locked items will be unlocked (ownership is taken and programs using them can be closed).",
                new[] { "Delete Forever", "Cancel" }, 0, Theme.Red, 1);
            if (a != 0) return;

            deleting = true;
            deleteBtn.Enabled = cancelBtn.Enabled = browseBtn.Enabled = false;
            pathBox.ReadOnly = true;
            keepFolder.Enabled = false;
            if (log != null) log("Delete Permanently: " + full);

            var th = new Thread(() =>
            {
                string outcome;
                Color color;
                RunDelete(full, isDir, keep, out outcome, out color);
                Post(() =>
                {
                    deleting = false;
                    cancelBtn.Enabled = true;
                    cancelBtn.Text = "Close";
                    info.ForeColor = color;
                    info.Text = outcome;
                    if (log != null) log(outcome);
                    if (color != Theme.Green)
                    {
                        pathBox.ReadOnly = false;
                        keepFolder.Enabled = isDir;
                        browseBtn.Enabled = true;
                    }
                });
            });
            th.IsBackground = true;
            th.Start();
        }

        private void Status(string text)
        {
            Post(() => { info.ForeColor = Theme.Amber; info.Text = text; });
        }

        private static string Plural(int n, string word)
        {
            return n.ToString("N0") + " " + word + (n == 1 ? "" : "s");
        }

        // ---- the escalating delete: normal -> take ownership -> close locking programs -> delete on restart

        private void RunDelete(string full, bool isDir, bool keep, out string outcome, out Color color)
        {
            Status("Deleting\u2026 very large folders can take a minute.");
            TryDelete(full, isDir, keep);
            if (IsGone(full, isDir, keep)) { Done(out outcome, out color, ""); return; }

            // 2. "Access denied": take ownership and give Administrators full control, then retry
            Status("Some items are protected \u2014 taking ownership\u2026");
            int code;
            if (isDir)
            {
                Native.Run("takeown.exe", "/f \"" + full + "\" /r /d y", out code);
                Native.Run("icacls.exe", "\"" + full + "\" /grant *S-1-5-32-544:F /t /c /q", out code);
            }
            else
            {
                Native.Run("takeown.exe", "/f \"" + full + "\"", out code);
                Native.Run("icacls.exe", "\"" + full + "\" /grant *S-1-5-32-544:F /c /q", out code);
            }
            TryDelete(full, isDir, keep);
            if (IsGone(full, isDir, keep)) { Done(out outcome, out color, " (ownership was taken to unlock it)"); return; }

            // 3. files are open in other programs: find them with the Restart Manager
            Status("Some files are in use \u2014 finding which programs are using them\u2026");
            var remaining = Remaining(full, isDir, keep);
            var lockers = Locker.Find(remaining.Files.Where(f => f.Length < 260).Take(1000).ToList());
            if (lockers.Count > 0)
            {
                var closable = lockers.Where(l => !Locker.IsCritical(l.Name)).ToList();
                string names = string.Join("\n", lockers.Select(l => "\u2022  " + l.Name + "  (PID " + l.Pid + ")" + (Locker.IsCritical(l.Name) ? "  \u2014 Windows, can't be closed" : "")).ToArray());
                int choice = Ask("Files are in use",
                    "These programs are using files you want to delete:\n\n" + names + "\n\n" +
                    (closable.Count > 0 ? "Close them now and delete? Unsaved work in them will be lost.\n" : "") +
                    "Or delete the files automatically the next time you restart.",
                    closable.Count > 0 ? new[] { "Close & Delete", "Delete on Restart", "Cancel" } : new[] { "Delete on Restart", "Cancel" },
                    0, Theme.Red);
                if (closable.Count == 0 && choice >= 0) choice++; // align with the 3-button layout

                if (choice == 0)
                {
                    Status("Closing programs that are using the files\u2026");
                    bool explorerClosed = false;
                    foreach (var l in closable)
                    {
                        try
                        {
                            using (var p = Process.GetProcessById(l.Pid))
                            {
                                if (p.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase)) explorerClosed = true;
                                p.Kill();
                                p.WaitForExit(3000);
                            }
                        }
                        catch { }
                    }
                    Thread.Sleep(800);
                    TryDelete(full, isDir, keep);
                    if (explorerClosed) EnsureExplorer();
                    if (IsGone(full, isDir, keep)) { Done(out outcome, out color, " (programs using it were closed)"); return; }
                }
                else if (choice != 1)
                {
                    outcome = "Stopped. Some files are still there because programs are using them.";
                    color = Theme.Amber;
                    return;
                }
            }
            else
            {
                int choice = Ask("Can't delete everything right now",
                    "Some items can't be deleted while Windows is running.\n\nDelete them automatically the next time you restart the PC?",
                    new[] { "Delete on Restart", "Cancel" }, 0, Theme.Red);
                if (choice != 0)
                {
                    outcome = "Stopped. Some items could not be deleted.";
                    color = Theme.Amber;
                    return;
                }
            }

            // 4. last resort: let Windows delete what's left during the next restart
            remaining = Remaining(full, isDir, keep);
            int scheduled = ScheduleOnRestart(remaining, full, isDir, keep);
            outcome = scheduled > 0
                ? "Almost done \u2014 " + Plural(scheduled, "item") + " will be deleted when you restart the PC."
                : "Some items could not be deleted or scheduled. Restart the PC and try again.";
            color = scheduled > 0 ? Theme.Amber : Theme.Red;
        }

        private void Done(out string outcome, out Color color, string how)
        {
            outcome = "\u2713 Deleted permanently \u2014 " + Theme.FormatBytes(scanSize) + " freed" + how + ".";
            color = Theme.Green;
        }

        private int Ask(string title, string message, string[] buttons, int primary, Color accent)
        {
            if (IsDisposed) return -1;
            return (int)Invoke(new Func<int>(() => ThemedDialog.Show(this, title, message, buttons, primary, accent, buttons.Length - 1)));
        }

        private static void TryDelete(string full, bool isDir, bool keep)
        {
            int code;
            if (!isDir)
            {
                Native.Run("cmd.exe", "/c attrib -r -s -h \"" + full + "\"", out code);
                Native.Run("cmd.exe", "/c del /f /q /a \"\\\\?\\" + full + "\"", out code);
                return;
            }
            // clear read-only / hidden / system flags so nothing blocks the delete
            Native.Run("cmd.exe", "/c attrib -r -s -h \"" + full + "\\*\" /s /d", out code);
            if (keep)
            {
                string[] entries;
                try { entries = Directory.GetFileSystemEntries(full); } catch { entries = new string[0]; }
                foreach (var en in entries)
                {
                    if (Directory.Exists(en)) Native.Run("cmd.exe", "/c rd /s /q \"\\\\?\\" + en + "\"", out code);
                    else Native.Run("cmd.exe", "/c del /f /q /a \"\\\\?\\" + en + "\"", out code);
                }
            }
            else
            {
                // the \\?\ prefix handles very deep paths (node_modules) and odd names (trailing dots/spaces)
                Native.Run("cmd.exe", "/c rd /s /q \"\\\\?\\" + full + "\"", out code);
            }
        }

        private static bool IsGone(string full, bool isDir, bool keep)
        {
            if (!isDir) return !File.Exists(full);
            if (!keep) return !Directory.Exists(full);
            try { return Directory.GetFileSystemEntries(full).Length == 0; } catch { return false; }
        }

        private class Leftovers
        {
            public List<string> Files = new List<string>();
            public List<string> Dirs = new List<string>();
        }

        private static Leftovers Remaining(string full, bool isDir, bool keep)
        {
            var r = new Leftovers();
            if (!isDir)
            {
                if (File.Exists(full)) r.Files.Add(full);
                return r;
            }
            if (!Directory.Exists(full)) return r;
            var stack = new Stack<string>();
            stack.Push(full);
            while (stack.Count > 0)
            {
                string d = stack.Pop();
                try { r.Files.AddRange(Directory.GetFiles(d)); } catch { }
                try
                {
                    foreach (var s in Directory.GetDirectories(d))
                    {
                        r.Dirs.Add(s);
                        bool link = false;
                        try { link = (File.GetAttributes(s) & FileAttributes.ReparsePoint) != 0; } catch { }
                        if (!link) stack.Push(s);
                    }
                }
                catch { }
            }
            return r;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool MoveFileEx(string existing, string newName, int flags);

        private static int ScheduleOnRestart(Leftovers r, string full, bool isDir, bool keep)
        {
            const int MOVEFILE_DELAY_UNTIL_REBOOT = 0x4;
            int n = 0;
            foreach (var f in r.Files)
                if (MoveFileEx(@"\\?\" + f, null, MOVEFILE_DELAY_UNTIL_REBOOT)) n++;
            // folders after their files, deepest first, so each one is empty by the time it's removed
            foreach (var d in r.Dirs.OrderByDescending(x => x.Length))
                if (MoveFileEx(@"\\?\" + d, null, MOVEFILE_DELAY_UNTIL_REBOOT)) n++;
            if (isDir && !keep && MoveFileEx(@"\\?\" + full, null, MOVEFILE_DELAY_UNTIL_REBOOT)) n++;
            return n;
        }

        private static void EnsureExplorer()
        {
            for (int i = 0; i < 16; i++)
            {
                Thread.Sleep(250);
                if (Process.GetProcessesByName("explorer").Length > 0) return;
            }
            try { Process.Start(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")); } catch { }
        }
    }

    /// Finds which programs have a file open, using the Windows Restart Manager (same as "file in use" dialogs).
    static class Locker
    {
        public class Info { public int Pid; public string Name; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RM_UNIQUE_PROCESS
        {
            public int dwProcessId;
            public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct RM_PROCESS_INFO
        {
            public RM_UNIQUE_PROCESS Process;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string strAppName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string strServiceShortName;
            public int ApplicationType;
            public uint AppStatus;
            public uint TSSessionId;
            [MarshalAs(UnmanagedType.Bool)] public bool bRestartable;
        }

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

        [DllImport("rstrtmgr.dll")]
        private static extern int RmEndSession(uint pSessionHandle);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmRegisterResources(uint pSessionHandle, uint nFiles, string[] rgsFilenames,
            uint nApplications, RM_UNIQUE_PROCESS[] rgApplications, uint nServices, string[] rgsServiceNames);

        [DllImport("rstrtmgr.dll")]
        private static extern int RmGetList(uint dwSessionHandle, out uint pnProcInfoNeeded, ref uint pnProcInfo,
            [In, Out] RM_PROCESS_INFO[] rgAffectedApps, ref uint lpdwRebootReasons);

        private static readonly string[] Critical =
        {
            "System", "Idle", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass",
            "svchost", "MsMpEng", "dwm", "fontdrvhost", "Memory Compression", "StopProcessing"
        };

        public static bool IsCritical(string name)
        {
            return Critical.Any(c => name.Equals(c, StringComparison.OrdinalIgnoreCase));
        }

        public static List<Info> Find(List<string> files)
        {
            var result = new List<Info>();
            if (files == null || files.Count == 0) return result;
            uint handle;
            if (RmStartSession(out handle, 0, Guid.NewGuid().ToString()) != 0) return result;
            try
            {
                if (RmRegisterResources(handle, (uint)files.Count, files.ToArray(), 0, null, 0, null) != 0) return result;
                uint needed, count = 0, reasons = 0;
                int rc = RmGetList(handle, out needed, ref count, null, ref reasons);
                if (rc == 234 && needed > 0) // ERROR_MORE_DATA
                {
                    var infos = new RM_PROCESS_INFO[needed];
                    count = needed;
                    if (RmGetList(handle, out needed, ref count, infos, ref reasons) == 0)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            int pid = infos[i].Process.dwProcessId;
                            if (result.Any(x => x.Pid == pid)) continue;
                            string name = infos[i].strAppName;
                            try { using (var p = Process.GetProcessById(pid)) name = p.ProcessName; } catch { }
                            result.Add(new Info { Pid = pid, Name = name });
                        }
                    }
                }
            }
            finally { RmEndSession(handle); }
            return result;
        }
    }

    class TitleBar : EdgePanel
    {
        private readonly MainForm owner;

        public TitleBar(MainForm owner)
        {
            this.owner = owner;
            BackColor = Theme.Surface;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            if (e.Clicks == 2) { owner.ToggleMaximize(); return; }
            Native.DragWindow(owner);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var logo = new Rectangle(14, 9, 22, 22);
            using (var p = Theme.Round(logo, 6))
            using (var b = new LinearGradientBrush(logo, Color.FromArgb(240, 92, 70), Color.FromArgb(196, 40, 60), 45f))
                g.FillPath(b, p);
            TextRenderer.DrawText(g, "\uE7E8", Theme.Icon(8.5f), logo, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            var tf = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
            var bf = Theme.F(9.5f, FontStyle.Bold);
            int bw = TextRenderer.MeasureText(g, "VIBE", bf, Size.Empty, tf).Width;
            TextRenderer.DrawText(g, "VIBE", bf, new Rectangle(46, 0, bw + 4, Height), Color.FromArgb(240, 92, 70), tf);
            string name = "Stop Processing";
            var nf = Theme.Semi(9.5f);
            int nx = 46 + bw + 6;
            int nw = TextRenderer.MeasureText(g, name, nf, Size.Empty, tf).Width;
            TextRenderer.DrawText(g, name, nf, new Rectangle(nx, 0, nw + 4, Height), Theme.Text, tf);
            TextRenderer.DrawText(g, "PC Shutdown Assistant", Theme.F(9f), new Rectangle(nx + nw + 10, 0, 220, Height), Theme.Faint, tf);

            using (var pen = new Pen(Color.FromArgb(30, 34, 40)))
                g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
        }
    }

    class NavPanel : EdgePanel
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Surface);
            TextRenderer.DrawText(g, "TOOLS", Theme.Semi(8f), new Point(26, 18), Theme.Faint);
            TextRenderer.DrawText(g, "WHEN YOU'RE DONE", Theme.Semi(8f), new Point(26, Height - 16 - 44 - 26), Theme.Faint);
            using (var pen = new Pen(Color.FromArgb(30, 34, 40)))
                g.DrawLine(pen, Width - 1, 0, Width - 1, Height);
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }
    }

    class StatusStrip2 : EdgePanel
    {
        private readonly bool admin;

        public StatusStrip2(bool admin)
        {
            this.admin = admin;
            BackColor = Theme.Surface;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Color.FromArgb(30, 34, 40)))
                g.DrawLine(pen, 0, 0, Width, 0);
            Color c = admin ? Theme.Green : Theme.Amber;
            using (var b = new SolidBrush(c)) g.FillEllipse(b, 14, (Height - 7) / 2, 7, 7);
            var tf = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
            TextRenderer.DrawText(g,
                admin ? "Running as administrator" : "Limited mode \u2014 run as administrator for Power Fix and full cleaning",
                Theme.F(8.5f), new Rectangle(28, 0, Width - 200, Height), Theme.Muted, tf);
            TextRenderer.DrawText(g, "Vibe Stop Processing " + AppInfo.VersionText, Theme.F(8.5f), new Rectangle(0, 0, Width - 14, Height), Theme.Faint,
                tf | TextFormatFlags.Right);
        }
    }
}

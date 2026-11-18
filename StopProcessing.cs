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
[assembly: AssemblyVersion("2.2.0.0")]
[assembly: AssemblyFileVersion("2.2.0.0")]

namespace StopProcessing
{
    // ------------------------------------------------------------------
    //  App info, settings and the GitHub-Releases based updater
    // ------------------------------------------------------------------
    static class AppInfo
    {
        public const string Owner = "jhfarhanyt20-coder";
        public const string Repo = "stop-processing";
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
                var req = Request("https://github.com/" + AppInfo.Owner + "/" + AppInfo.Repo + "/releases/latest/download/latest.json");
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

        public static void Restart()
        {
            Process.Start(new ProcessStartInfo(Exe, "--updated") { UseShellExecute = true });
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
            Height = 54;
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
            TextRenderer.DrawText(g, Text, Theme.Semi(10f), new Rectangle(64, 8, textW, 20), Theme.Text, tf);
            TextRenderer.DrawText(g, Desc, Theme.F(8.5f), new Rectangle(64, 29, textW, 18), Theme.Faint, tf);

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

        private ThemedDialog(string title, string message, string[] buttons, int primary, Color accent)
        {
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
                else if (e.KeyCode == Keys.Enter) { result = primary; Close(); }
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

        public static int Show(IWin32Window owner, string title, string message, string[] buttons, int primary, Color accent)
        {
            using (var d = new ThemedDialog(title, message, buttons, primary, accent))
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
        private StatusRow rowFastStartup, rowWakeTimers, rowWakeDevices, rowMaintenance, rowAutoReboot, rowArso;
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
            pages.Add(BuildSettingsPage());
            foreach (var p in pages) contentHost.Controls.Add(p);

            int startPage = 0;
            foreach (var arg in Environment.GetCommandLineArgs())
                if (arg.StartsWith("--page=")) int.TryParse(arg.Substring(7), out startPage);
            ShowPage(Math.Max(0, Math.Min(pages.Count - 1, startPage)));
            StartTimer();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Updater.CleanupOld();
            if (Environment.GetCommandLineArgs().Any(a => a == "--updated"))
            {
                ThemedDialog.Show(this, "Update installed",
                    "Stop Processing was updated to version " + AppInfo.VersionText + ".", new[] { "OK" }, 0, Theme.Green);
            }

            // check for updates a few seconds after start so the UI is responsive first
            var t = new System.Windows.Forms.Timer { Interval = 4000 };
            t.Tick += (s, ev) => { t.Stop(); t.Dispose(); CheckForUpdates(false); };
            t.Start();
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

            procHeader = new PageHeader("Processes", "Live process monitor \u2014 pick what stays, stop the rest.") { Dock = DockStyle.Fill };

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
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var desc = MakeLabel("Closes every non-system app one by one. Windows processes are never touched and important apps ask first.",
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

        private void StartTimer()
        {
            refreshTimer = new System.Windows.Forms.Timer { Interval = 1500 };
            refreshTimer.Tick += (s, e) =>
            {
                if (!isStopping && currentPage == 0 && WindowState != FormWindowState.Minimized) RefreshGrid();
            };
            refreshTimer.Start();
            RefreshGrid();
        }

        private void RefreshGrid()
        {
            var rows = new List<ProcRow>();
            int totalProc = 0, stoppable = 0;
            long totalMem = 0;
            double totalCpu = 0;
            var alive = new HashSet<int>();

            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    totalProc++;
                    long mem = 0;
                    string path = string.Empty;
                    try { mem = p.WorkingSet64; } catch { }
                    try { path = p.MainModule.FileName; } catch { }
                    double cpu = SampleCpu(p);
                    alive.Add(p.Id);
                    totalMem += mem;
                    totalCpu += cpu;
                    var row = new ProcRow { Pid = p.Id, Name = p.ProcessName, Memory = mem, Cpu = cpu, Role = RoleOf(p.ProcessName, path) };
                    if (row.Role != "SYSTEM PROTECTED") stoppable++;
                    rows.Add(row);
                }
                catch { }
                finally { p.Dispose(); }
            }

            foreach (var dead in lastSample.Keys.Where(k => !alive.Contains(k)).ToList()) lastSample.Remove(dead);

            rows = rows.OrderByDescending(r => r.Memory).ToList();

            int selectedPid = -1;
            if (grid.CurrentRow != null) int.TryParse(Convert.ToString(grid.CurrentRow.Cells["PID"].Value), out selectedPid);
            int scroll = grid.FirstDisplayedScrollingRowIndex >= 0 ? grid.FirstDisplayedScrollingRowIndex : 0;

            grid.SuspendLayout();
            grid.Rows.Clear();
            int reselect = -1;
            foreach (var r in rows)
            {
                int idx = grid.Rows.Add(r.Name, r.Pid.ToString(), r.Cpu.ToString("0.0"), Theme.FormatBytes(r.Memory), r.Role);
                if (r.Pid == selectedPid) reselect = idx;
                if (r.Role == "SYSTEM PROTECTED") grid.Rows[idx].DefaultCellStyle.ForeColor = Theme.Muted;
            }
            grid.ResumeLayout();

            try
            {
                grid.ClearSelection();
                if (reselect >= 0) grid.Rows[reselect].Selected = true;
                if (scroll > 0 && scroll < grid.Rows.Count) grid.FirstDisplayedScrollingRowIndex = scroll;
            }
            catch { }

            statProc.Value = totalProc.ToString();
            statCpu.Value = Math.Min(100, totalCpu).ToString("0") + " %";
            statMem.Value = Theme.FormatBytes(totalMem);
            statStoppable.Value = stoppable.ToString();
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

        private string RoleOf(string name, string path)
        {
            if (IsCritical(name, path)) return "SYSTEM PROTECTED";
            if (IMPORTANT.Any(i => name.Equals(i, StringComparison.OrdinalIgnoreCase))) return "CONFIRM";
            return "normal";
        }

        private bool IsCritical(string name, string path)
        {
            if (name.Equals("StopProcessing", StringComparison.OrdinalIgnoreCase)) return true;
            if (CRITICAL.Any(c => name.Equals(c, StringComparison.OrdinalIgnoreCase))) return true;
            if (!string.IsNullOrEmpty(path))
            {
                string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                if (path.StartsWith(win, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private void StopButton_Click(object sender, EventArgs e)
        {
            if (isStopping) return;
            RefreshGrid();

            var targets = new List<ProcRow>();
            foreach (DataGridViewRow row in grid.Rows)
            {
                string role = Convert.ToString(row.Cells["Role"].Value);
                if (role == "SYSTEM PROTECTED") continue;
                int pid;
                if (!int.TryParse(Convert.ToString(row.Cells["PID"].Value), out pid)) continue;
                targets.Add(new ProcRow { Pid = pid, Name = Convert.ToString(row.Cells["Process"].Value), Role = role });
            }

            int ans = ThemedDialog.Show(this, "Stop all processes?",
                "This will stop " + targets.Count + " processes one by one.\n\n" +
                "Windows system processes are never touched, and you'll be asked before any important app is closed." +
                (shutdownAfter.Checked ? "\n\nThe PC will shut down when the run is finished." : ""),
                new[] { "Stop " + targets.Count + " processes", "Cancel" }, 0, Theme.Red);
            if (ans != 0) return;

            isStopping = true;
            stopButton.Enabled = false;
            stopButton.Text = "Stopping\u2026";
            stopProgress.Maximum = Math.Max(1, targets.Count);
            stopProgress.Value = 0;
            stopLog.Items.Clear();
            AddLog(stopLog, "Queued " + targets.Count + " processes.");

            stopCancel = new CancellationTokenSource();
            var token = stopCancel.Token;
            bool shutdownWhenDone = shutdownAfter.Checked;

            var worker = new Thread(() =>
            {
                int done = 0, ok = 0;
                foreach (var t in targets)
                {
                    if (token.IsCancellationRequested) break;
                    done++;
                    string result = StopOne(t);
                    if (result == "Stopped") ok++;
                    int d = done;
                    string r = result, nm = t.Name;
                    Ui(() =>
                    {
                        stopProgress.Value = d;
                        procHeader.Subtitle = nm + " \u2014 " + r + "   (" + d + " / " + targets.Count + ")";
                        AddLog(stopLog, nm + " \u2192 " + r);
                    });
                    Thread.Sleep(200);
                }

                int stopped = ok;
                Ui(() =>
                {
                    isStopping = false;
                    stopButton.Enabled = true;
                    stopButton.Text = "Stop All Processes";
                    procHeader.Subtitle = "Finished. Stopped " + stopped + " of " + targets.Count + " processes.";
                    AddLog(stopLog, "Finished. " + stopped + "/" + targets.Count + " stopped.");
                    RefreshGrid();
                    if (shutdownWhenDone && !token.IsCancellationRequested) ShutdownCountdown();
                });
            });
            worker.IsBackground = true;
            worker.Start();
        }

        private string StopOne(ProcRow t)
        {
            Process p;
            try { p = Process.GetProcessById(t.Pid); }
            catch { return "Already closed"; }

            try
            {
                string path = string.Empty;
                try { path = p.MainModule.FileName; } catch { }
                if (IsCritical(p.ProcessName, path)) return "Protected";

                string name = p.ProcessName;
                if (t.Role == "CONFIRM")
                {
                    int a = AskOnUi("Close " + name + "?",
                        "\"" + name + "\" looks like an important app. Unsaved work in it may be lost.",
                        new[] { "Close it", "Keep running" }, 0, Theme.Amber);
                    if (a != 0) return "Skipped";
                }

                p.CloseMainWindow();
                if (!p.WaitForExit(1500))
                {
                    int a = AskOnUi("Force stop " + name + "?",
                        "\"" + name + "\" did not close on its own.",
                        new[] { "Force stop", "Leave running" }, 0, Theme.Red);
                    if (a != 0) return "Left running";
                    p.Kill();
                    p.WaitForExit(1500);
                }
                return "Stopped";
            }
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
            Native.Run("shutdown.exe", "/s /t 15", out code);
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
            cleanTargets.Add(new CleanTarget { Name = "Windows Update cache", Glyph = "\uE896", DefaultOn = false,
                Desc = "Update files that were already downloaded", Dirs = new[] { Path.Combine(win, @"SoftwareDistribution\Download") } });
            cleanTargets.Add(new CleanTarget { Name = "Prefetch data", Glyph = "\uE945", DefaultOn = false,
                Desc = "Launch cache \u2014 apps may open a bit slower once", Dirs = new[] { Path.Combine(win, "Prefetch") } });
            cleanTargets.Add(new CleanTarget { Name = "Recycle Bin", Glyph = "\uE74D", DefaultOn = false, Recycle = true,
                Desc = "Permanently empties the Recycle Bin", Dirs = new string[0] });
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

            askLocked = new CheckToggle { Text = "Ask me before skipping locked files", Dock = DockStyle.Fill, Checked = true };

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
                                    int a = AskOnUi("File in use \u2014 skip it?",
                                        "This file is open in another program or protected, so it can't be deleted right now:\n\n" +
                                        f + "\n\n" + ex.Message,
                                        new[] { "Skip", "Skip All", "Stop Cleaning" }, 1, Theme.Blue);
                                    if (a == 1) skipAll = true;
                                    else if (a == 2 || a == -1) stop = true;
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
            var rows = new[] { rowFastStartup, rowWakeTimers, rowWakeDevices, rowMaintenance, rowArso, rowAutoReboot };
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

        private void CheckPowerAsync()
        {
            if (isPowerBusy) return;
            foreach (var r in new[] { rowFastStartup, rowWakeTimers, rowWakeDevices, rowMaintenance, rowArso, rowAutoReboot })
                r.SetState(CheckState2.Unknown, "Checking\u2026");

            var th = new Thread(() =>
            {
                int? hiber = ReadDword(KeyPower, "HiberbootEnabled");
                bool? timers = WakeTimersOn();
                var devices = WakeArmedDevices();
                int? maint = ReadDword(KeyMaint, "WakeUp");
                int? arso = ReadDword(KeyArso, "DisableAutomaticRestartSignOn");
                int? reboot = ReadDword(KeyCrash, "AutoReboot");

                Ui(() =>
                {
                    int risks = 0;
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
                    "\u2022  turn off Fast Startup (shutdown becomes a real, full shutdown)\n" +
                    "\u2022  disable wake timers\n" +
                    "\u2022  stop the network card, mouse and keyboard from waking the PC\n" +
                    "\u2022  turn off maintenance wake-up and auto sign-in after updates\n" +
                    "\u2022  stay off instead of rebooting after a crash\n\n" +
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
            Native.Run("shutdown.exe", "/s /t 0", out code);
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

            body.Controls.Add(card, 0, 0);
            body.Controls.Add(about, 1, 0);
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
                        navItems[3].Badge = false;
                        SetUpdateStatus("You're up to date", Theme.Green);
                        updNotes.Text = info != null && !string.IsNullOrEmpty(info.Notes)
                            ? "What's in " + info.Tag + ":\r\n\r\n" + info.Notes.Replace("\r\n", "\n").Replace("\n", "\r\n")
                            : "You have the latest version.";
                        return;
                    }

                    pendingUpdate = info;
                    installUpdateButton.Enabled = true;
                    navItems[3].Badge = true;
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
                    navItems[3].Badge = false;
                    updProgress.Value = 100;
                    SetUpdateStatus("Update installed \u2014 restart to finish", Theme.Green);

                    if (isStopping || isCleaning) return; // don't interrupt a running job
                    int ans = ThemedDialog.Show(this, "Update ready",
                        "Stop Processing " + info.Tag.TrimStart('v', 'V') + " has been installed. Restart the app now to use it?",
                        new[] { "Restart now", "Later" }, 0, Theme.Green);
                    if (ans == 0)
                    {
                        try { Updater.Restart(); Close(); } catch { }
                    }
                });
            });
            th.IsBackground = true;
            th.Start();
        }

        // =================================================================

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (isStopping || isCleaning)
            {
                int r = ThemedDialog.Show(this, "Still working",
                    "A run is still in progress. Close anyway?", new[] { "Close", "Keep open" }, 1, Theme.Amber);
                if (r != 0) { e.Cancel = true; return; }
                if (stopCancel != null) stopCancel.Cancel();
            }
            if (refreshTimer != null) refreshTimer.Stop();
            base.OnFormClosing(e);
        }

        private class ProcRow
        {
            public int Pid;
            public string Name;
            public long Memory;
            public double Cpu;
            public string Role;
        }

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    // ------------------------------------------------------------------
    //  Shell pieces
    // ------------------------------------------------------------------
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

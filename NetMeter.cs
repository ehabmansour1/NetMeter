// NetMeter - upload/download speed on the taskbar, today/month usage card on hover.
// Build: build.ps1 (uses the C# compiler that ships with Windows).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Networking.Connectivity;

// File metadata shown in Properties > Details (also required for code signing).
[assembly: AssemblyTitle("NetMeter")]
[assembly: AssemblyDescription("NetMeter - taskbar upload/download meter")]
[assembly: AssemblyProduct("NetMeter")]
[assembly: AssemblyCompany("Ehab Mansour")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Ehab Mansour. MIT License.")]
[assembly: AssemblyVersion("1.6.0.0")]
[assembly: AssemblyFileVersion("1.6.0.0")]
[assembly: AssemblyInformationalVersion("1.6.0")]

static class Program
{
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    [STAThread]
    static void Main(string[] args)
    {
        Settings.Load();
        if (args.Length == 2 && args[0] == "--dump")
        {
            System.IO.File.WriteAllText(args[1], Usage.Query().ToText());
            return;
        }
        if (args.Length == 2 && args[0] == "--card")
        {
            using (var bmp = new UsageCard(1f).Build(Usage.Query(), Theme.Current()))
                bmp.Save(args[1]);
            return;
        }
        if (args.Length == 2 && args[0] == "--settings")
        {
            SetProcessDPIAware();
            Application.EnableVisualStyles();
            // Shows the real window for a moment and captures it from the screen.
            using (var f = new SettingsForm { ShowInTaskbar = false })
            {
                f.Show();
                for (int i = 0; i < 10; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(60); }
                var r = f.Bounds;
                using (var bmp = new Bitmap(r.Width, r.Height))
                {
                    using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
                    bmp.Save(args[1]);
                }
            }
            return;
        }
        bool created;
        var mutex = new System.Threading.Mutex(true, "NetMeter_SingleInstance", out created);
        if (!created) return;
        SetProcessDPIAware();
        AutoStart.Init();
        Application.EnableVisualStyles();
        Application.Run(new Meter());
        GC.KeepAlive(mutex);
    }
}

// ---------- settings (HKCU\Software\NetMeter) ----------

static class Settings
{
    const string Key = @"Software\NetMeter";

    public static int UpdateMs = 1000;          // 500 / 1000 / 2000
    public static bool Bits;                    // Mbps instead of MB/s
    public static int TextPx = 12;              // 11 / 12 / 13 / 14
    public static bool ArrowsRight = true;
    public static int UpColor, DownColor;       // ARGB; 0 = theme default
    public static bool TinyAsZero;              // under 1 KB/s shows as 0
    public static bool ShowTopApps = true;
    public static int TopAppsCount = 5;         // 3 / 5 / 10
    public static int HoverDelayMs = 400;       // 0 / 400 / 1000
    public static bool DecimalUnits;            // 1 GB = 1000^3 (ISP style) instead of 1024^3
    public static int MonthStartDay = 1;        // 1..28 (billing day)
    public static bool ShowSplit = true;        // download / upload line under each total on the card

    public static event Action Changed;

    public static void Load()
    {
        try
        {
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key))
            {
                if (k == null) return;
                UpdateMs = Get(k, "UpdateMs", UpdateMs);
                Bits = Get(k, "Bits", 0) == 1;
                TextPx = Get(k, "TextPx", TextPx);
                ArrowsRight = Get(k, "ArrowsRight", 1) == 1;
                UpColor = Get(k, "UpColor", 0);
                DownColor = Get(k, "DownColor", 0);
                TinyAsZero = Get(k, "TinyAsZero", 0) == 1;
                ShowTopApps = Get(k, "ShowTopApps", 1) == 1;
                TopAppsCount = Get(k, "TopAppsCount", TopAppsCount);
                HoverDelayMs = Get(k, "HoverDelayMs", HoverDelayMs);
                DecimalUnits = Get(k, "DecimalUnits", 0) == 1;
                MonthStartDay = Math.Max(1, Math.Min(28, Get(k, "MonthStartDay", 1)));
                ShowSplit = Get(k, "ShowSplit", 1) == 1;
            }
        }
        catch { }
    }

    public static void Save()
    {
        try
        {
            using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(Key))
            {
                k.SetValue("UpdateMs", UpdateMs);
                k.SetValue("Bits", Bits ? 1 : 0);
                k.SetValue("TextPx", TextPx);
                k.SetValue("ArrowsRight", ArrowsRight ? 1 : 0);
                k.SetValue("UpColor", UpColor);
                k.SetValue("DownColor", DownColor);
                k.SetValue("TinyAsZero", TinyAsZero ? 1 : 0);
                k.SetValue("ShowTopApps", ShowTopApps ? 1 : 0);
                k.SetValue("TopAppsCount", TopAppsCount);
                k.SetValue("HoverDelayMs", HoverDelayMs);
                k.SetValue("DecimalUnits", DecimalUnits ? 1 : 0);
                k.SetValue("MonthStartDay", MonthStartDay);
                k.SetValue("ShowSplit", ShowSplit ? 1 : 0);
            }
        }
        catch { }
        if (Changed != null) Changed();
    }

    public static void ResetDefaults()
    {
        UpdateMs = 1000; Bits = false; TextPx = 12; ArrowsRight = true; UpColor = 0; DownColor = 0;
        TinyAsZero = false; ShowTopApps = true; TopAppsCount = 5; HoverDelayMs = 400; DecimalUnits = false; MonthStartDay = 1; ShowSplit = true;
        Save();
    }

    // First day of the current (billing) month.
    public static DateTime MonthStart()
    {
        var t = DateTime.Today;
        var start = new DateTime(t.Year, t.Month, MonthStartDay);
        return t >= start ? start : start.AddMonths(-1);
    }

    static int Get(Microsoft.Win32.RegistryKey k, string name, int def)
    {
        try { return Convert.ToInt32(k.GetValue(name, def)); } catch { return def; }
    }
}

// ---------- start with Windows (HKCU Run key, no admin needed) ----------

static class AutoStart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", AppKey = @"Software\NetMeter", Name = "NetMeter";

    static string Command { get { return "\"" + Application.ExecutablePath + "\""; } }

    public static bool Enabled
    {
        get
        {
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue(Name) != null;
        }
    }

    public static void Set(bool on)
    {
        using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey))
        {
            if (on) k.SetValue(Name, Command);
            else k.DeleteValue(Name, false);
        }
    }

    // The first launch turns auto-start on; after that the menu choice is respected.
    // While it's on, the path is refreshed in case the exe was moved.
    public static void Init()
    {
        try
        {
            using (var app = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(AppKey))
            {
                if (app.GetValue("AutoStartConfigured") == null)
                {
                    Set(true);
                    app.SetValue("AutoStartConfigured", 1);
                }
                else if (Enabled) Set(true);
            }
        }
        catch { }
    }
}

// ---------- today / month usage (same data as Settings > Network > Data usage) ----------

class AppUse
{
    public string Name;
    public ulong Bytes;
    public Bitmap Icon;   // may be null (services, packaged apps)
}

class Usage
{
    public ulong TodayDown, TodayUp, MonthDown, MonthUp;
    public List<AppUse> TopApps = new List<AppUse>();

    // The C# 5 compiler that ships with Windows can't bind AsTask<T> to WinRT async operations,
    // so it is resolved by reflection (the same thing PowerShell does).
    static readonly MethodInfo AsTaskOp = typeof(WindowsRuntimeSystemExtensions).GetMethods().First(m =>
        m.Name == "AsTask" && m.IsGenericMethod && m.GetParameters().Length == 1 &&
        m.GetParameters()[0].ParameterType.Name == "IAsyncOperation`1");

    static IEnumerable<T> Await<T>(object op)
    {
        var task = (Task)AsTaskOp.MakeGenericMethod(typeof(IReadOnlyList<T>)).Invoke(null, new[] { op });
        task.Wait();
        return ((System.Collections.IEnumerable)task.GetType().GetProperty("Result").GetValue(task)).Cast<T>();
    }

    static IEnumerable<NetworkUsage> Read(ConnectionProfile p, DateTimeOffset from, DateTimeOffset to, NetworkUsageStates states)
    {
        return Await<NetworkUsage>(p.GetNetworkUsageAsync(from, to, DataUsageGranularity.Total, states));
    }

    // Today's usage per app, merged by display name, biggest first.
    static List<AppUse> TopAppsToday(IEnumerable<ConnectionProfile> profiles, DateTimeOffset from, DateTimeOffset to, NetworkUsageStates states, int count)
    {
        var byName = new Dictionary<string, AppUse>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in profiles)
            foreach (var a in Await<AttributedNetworkUsage>(p.GetAttributedNetworkUsageAsync(from, to, states)))
            {
                string exe;
                string name = AppNames.Friendly(a.AttributionId, a.AttributionName, out exe);
                AppUse e;
                if (!byName.TryGetValue(name, out e)) byName[name] = e = new AppUse { Name = name, Icon = AppNames.Icon(exe) };
                e.Bytes += a.BytesReceived + a.BytesSent;
            }
        return byName.Values.Where(e => e.Bytes > 0).OrderByDescending(e => e.Bytes).Take(count).ToList();
    }

    public static Usage Query()
    {
        var now = DateTimeOffset.Now;
        var today = new DateTimeOffset(DateTime.Today);
        var monthStart = new DateTimeOffset(Settings.MonthStart());
        var states = new NetworkUsageStates { Roaming = TriStates.DoNotCare, Shared = TriStates.DoNotCare };
        var r = new Usage();
        var profiles = NetworkInformation.GetConnectionProfiles();
        foreach (var p in profiles)
        {
            foreach (var u in Read(p, today, now, states)) { r.TodayDown += u.BytesReceived; r.TodayUp += u.BytesSent; }
            foreach (var u in Read(p, monthStart, now, states)) { r.MonthDown += u.BytesReceived; r.MonthUp += u.BytesSent; }
        }
        if (Settings.ShowTopApps)
        {
            try { r.TopApps = TopAppsToday(profiles, today, now, states, Settings.TopAppsCount); }
            catch { }
        }
        return r;
    }

    public string ToText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Today: " + Fmt.Size(TodayDown + TodayUp) + "  (down " + Fmt.Size(TodayDown) + ", up " + Fmt.Size(TodayUp) + ")");
        sb.AppendLine("This month: " + Fmt.Size(MonthDown + MonthUp) + "  (down " + Fmt.Size(MonthDown) + ", up " + Fmt.Size(MonthUp) + ")");
        sb.AppendLine("Top apps today:");
        foreach (var a in TopApps) sb.AppendLine("  " + Fmt.Size(a.Bytes) + "  " + a.Name);
        return sb.ToString();
    }
}

// Turns the usage records' app IDs (\device\harddiskvolumeN\...\app.exe, package family names,
// service names) into readable names and icons.
static class AppNames
{
    static readonly Dictionary<string, Bitmap> icons = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);

    public static string Friendly(string id, string name, out string exe)
    {
        exe = null;
        if (!string.IsNullOrEmpty(name)) return name;
        if (string.IsNullOrEmpty(id)) return "Windows (system)";
        if (id.StartsWith(@"\device\", StringComparison.OrdinalIgnoreCase))
        {
            exe = DosPath(id);
            string file = System.IO.Path.GetFileNameWithoutExtension(id);
            if (exe != null)
            {
                try
                {
                    string desc = System.Diagnostics.FileVersionInfo.GetVersionInfo(exe).FileDescription;
                    if (!string.IsNullOrWhiteSpace(desc)) return desc.Trim();
                }
                catch { }
            }
            return file;
        }
        int us = id.LastIndexOf('_');
        if (us > 0)   // packaged app: Publisher.AppName_hash
        {
            string family = id.Substring(0, us);
            return family.Substring(family.LastIndexOf('.') + 1);
        }
        return id;    // service name, e.g. DoSvc
    }

    public static Bitmap Icon(string exe)
    {
        if (exe == null) return null;
        Bitmap b;
        lock (icons)
        {
            if (icons.TryGetValue(exe, out b)) return b;
            try
            {
                using (var ico = System.Drawing.Icon.ExtractAssociatedIcon(exe)) b = ico.ToBitmap();
            }
            catch { b = null; }
            icons[exe] = b;
            return b;
        }
    }

    static string DosPath(string devicePath)
    {
        foreach (var drive in Environment.GetLogicalDrives())
        {
            string d = drive.TrimEnd('\\');
            var sb = new StringBuilder(260);
            if (QueryDosDevice(d, sb, sb.Capacity) == 0) continue;
            string dev = sb.ToString() + @"\";
            if (devicePath.StartsWith(dev, StringComparison.OrdinalIgnoreCase))
                return d + @"\" + devicePath.Substring(dev.Length);
        }
        return null;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern uint QueryDosDevice(string device, StringBuilder target, int max);
}

static class Fmt
{
    // 1024 like Windows Settings, or 1000 like most ISPs (Settings.DecimalUnits).
    static double K { get { return Settings.DecimalUnits ? 1000.0 : 1024.0; } }

    public static string Size(ulong bytes)
    {
        double mb = K * K, gb = mb * K;
        if (bytes < gb) return (bytes / mb).ToString("0") + " MB";
        double g = bytes / gb;
        return g.ToString(g < 10 ? "0.00" : g < 100 ? "0.0" : "0") + " GB";
    }

    // style 0: "1.5 KB/s" / "12 Mbps", 1: "1.5K/s" / "12M", 2: "1.5K" / "12M" (shorter forms for a narrow meter)
    public static string Speed(double bytesPerSec, int style)
    {
        double v = Settings.Bits ? bytesPerSec * 8 : bytesPerSec, k = K;
        string num, unit;
        if (Settings.TinyAsZero && v < k) v = 0;
        if (v < k && !(Settings.TinyAsZero)) { num = ((int)v).ToString(); unit = ""; }
        else if (v < k * k) { num = (v / k).ToString(v != 0 && v < 10 * k ? "0.0" : "0"); unit = "K"; }
        else { num = (v / (k * k)).ToString(v < 10 * k * k ? "0.0" : "0"); unit = "M"; }

        if (Settings.Bits)
            return style == 0 ? num + " " + unit + "bps" : num + unit;
        if (style == 0) return num + " " + unit + "B/s";
        return num + (unit == "" ? "B" : unit) + (style == 1 ? "/s" : "");
    }

    // Widest text the meter has to fit at full size.
    public static string WidestSpeed { get { return Settings.Bits ? "999 Mbps" : "999 KB/s"; } }
}

// ---------- shared look ----------

class Theme
{
    public bool Light;
    public Color Text, Secondary, Up, Down, CardBack, CardBorder, Divider;

    public static Theme Current()
    {
        bool light = false;
        try
        {
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                light = k != null && Convert.ToInt32(k.GetValue("SystemUsesLightTheme", 0)) == 1;
        }
        catch { }
        var t = light
            ? new Theme { Light = true, Text = Color.FromArgb(26, 26, 26), Secondary = Color.FromArgb(96, 96, 96),
                          Up = Color.FromArgb(196, 89, 0), Down = Color.FromArgb(0, 103, 192),
                          CardBack = Color.FromArgb(249, 249, 249), CardBorder = Color.FromArgb(222, 222, 222), Divider = Color.FromArgb(229, 229, 229) }
            : new Theme { Light = false, Text = Color.FromArgb(255, 255, 255), Secondary = Color.FromArgb(170, 170, 170),
                          Up = Color.FromArgb(255, 170, 90), Down = Color.FromArgb(96, 205, 255),
                          CardBack = Color.FromArgb(44, 44, 44), CardBorder = Color.FromArgb(64, 64, 64), Divider = Color.FromArgb(61, 61, 61) };
        if (Settings.UpColor != 0) t.Up = Color.FromArgb(Settings.UpColor);
        if (Settings.DownColor != 0) t.Down = Color.FromArgb(Settings.DownColor);
        return t;
    }

    public static bool SystemLight { get { return Current().Light; } }
}

static class Gfx
{
    public static Font MakeFont(string family, float px, string fallback)
    {
        var f = new Font(family, px, FontStyle.Regular, GraphicsUnit.Pixel);
        if (f.Name == family) return f;
        f.Dispose();
        return new Font(fallback, px, FontStyle.Regular, GraphicsUnit.Pixel);
    }

    // Text goes through GDI (TextRenderer) so it gets hinted ClearType like normal Windows apps.
    // That needs an opaque background colour, and GDI leaves alpha at 0 - call MakeOpaque afterwards.
    const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

    public static float Width(Graphics g, string s, Font f)
    {
        return TextRenderer.MeasureText(g, s, f, Size.Empty, Flags).Width;
    }

    public static void Text(Graphics g, string s, Font f, Color c, Color back, float x, float y)
    {
        TextRenderer.DrawText(g, s, f, new Point((int)Math.Round(x), (int)Math.Round(y)), c, back, Flags);
    }

    // Like Text, but cut with "…" if wider than maxWidth.
    public static void TextFit(Graphics g, string s, Font f, Color c, Color back, float x, float y, float maxWidth)
    {
        var r = new Rectangle((int)Math.Round(x), (int)Math.Round(y), (int)Math.Max(1, maxWidth), f.Height + 2);
        TextRenderer.DrawText(g, s, f, r, c, back, Flags | TextFormatFlags.EndEllipsis);
    }

    public static void MakeOpaque(Bitmap bmp)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var px = new byte[data.Stride * data.Height];
        Marshal.Copy(data.Scan0, px, 0, px.Length);
        for (int i = 3; i < px.Length; i += 4) px[i] = 255;
        Marshal.Copy(px, 0, data.Scan0, px.Length);
        bmp.UnlockBits(data);
    }

    // Colour currently on screen at (x, y) - used to match the taskbar behind the meter.
    public static Color ScreenPixel(int x, int y)
    {
        IntPtr dc = GetDC(IntPtr.Zero);
        uint c = GetPixel(dc, x, y);
        ReleaseDC(IntPtr.Zero, dc);
        return Color.FromArgb((int)(c & 0xFF), (int)((c >> 8) & 0xFF), (int)((c >> 16) & 0xFF));
    }

    // Vector arrow (crisper than the font's arrow glyphs). (x, y) is the top-left of a w x h box.
    public static void Arrow(Graphics g, bool up, float x, float y, float w, float h, float stroke, Color c)
    {
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float cx = x + w / 2, tip = up ? y : y + h, tail = up ? y + h : y, head = w / 2;
        float back = up ? head : -head;
        using (var p = new Pen(c, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
        {
            g.DrawLine(p, cx, tail, cx, tip);
            g.DrawLines(p, new[] { new PointF(cx - head, tip + back), new PointF(cx, tip), new PointF(cx + head, tip + back) });
        }
        g.SmoothingMode = old;
    }

    public static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        float d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    // Push a 32-bit ARGB bitmap to a layered window (per-pixel alpha) at screen position (x, y).
    public static void Present(IntPtr hwnd, Bitmap bmp, int x, int y)
    {
        IntPtr screenDc = GetDC(IntPtr.Zero), memDc = CreateCompatibleDC(screenDc);
        IntPtr hBmp = bmp.GetHbitmap(Color.FromArgb(0)), old = SelectObject(memDc, hBmp);
        try
        {
            var size = new SIZE { cx = bmp.Width, cy = bmp.Height };
            var src = new POINT();
            var pos = new POINT { x = x, y = y };
            var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            UpdateLayeredWindow(hwnd, screenDc, ref pos, ref size, memDc, ref src, 0, ref blend, 2 /*ULW_ALPHA*/);
        }
        finally
        {
            SelectObject(memDc, old);
            DeleteObject(hBmp);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst, ref POINT pos, ref SIZE size, IntPtr src, ref POINT srcPos, int key, ref BLENDFUNCTION blend, int flags);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern uint GetPixel(IntPtr dc, int x, int y);
}

// Borderless, never-activated, always-on-top window drawn with per-pixel alpha.
class LayeredForm : Form
{
    protected bool ClickThrough;

    public LayeredForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80000 /*LAYERED*/ | 0x80 /*TOOLWINDOW*/ | 0x8000000 /*NOACTIVATE*/ | 0x8 /*TOPMOST*/;
            if (ClickThrough) cp.ExStyle |= 0x20 /*TRANSPARENT*/;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation { get { return true; } }
}

// ---------- the hover card ----------

class UsageCard : LayeredForm
{
    readonly float s;

    public UsageCard(float scale)
    {
        s = scale;
        ClickThrough = true;
    }

    int Shadow { get { return (int)(12 * s); } }

    public void ShowAbove(Rectangle anchor, Usage u, Theme t)
    {
        int shadow = Shadow;
        using (var bmp = Build(u, t))
        {
            // Centred above the meter, kept on screen, 8px gap above the taskbar.
            var screen = Screen.FromRectangle(anchor).Bounds;
            int px = anchor.Left + anchor.Width / 2 - bmp.Width / 2;
            px = Math.Max(screen.Left, Math.Min(px, screen.Right - bmp.Width + shadow - (int)(8 * s)));
            int py = anchor.Top - bmp.Height + shadow - (int)(8 * s);
            Gfx.Present(Handle, bmp, px, py);
        }
        SetWindowPos(Handle, new IntPtr(-1) /*TOPMOST*/, 0, 0, 0, 0, 0x1 | 0x2 | 0x10 | 0x40 /*NOSIZE|NOMOVE|NOACTIVATE|SHOWWINDOW*/);
        IsOpen = true;
    }

    const float MaxAppName = 150;
    float splitCol;   // x offset of the upload column, shared by both sections so they line up

    // Card width that fits the content (no fixed width, so no empty space).
    int ContentWidth(Usage u)
    {
        using (var bmp = new Bitmap(1, 1))
        using (var g = Graphics.FromImage(bmp))
        using (var small = Gfx.MakeFont("Segoe UI Variable Small Semibol", 11 * s, "Segoe UI Semibold"))
        using (var big = Gfx.MakeFont("Segoe UI Variable Display Semib", 26 * s, "Segoe UI Semibold"))
        using (var body = Gfx.MakeFont("Segoe UI Variable Text", 13 * s, "Segoe UI"))
        {
            float aw = 8 * s, arrowGap = 6 * s, colGap = 22 * s, labelGap = 24 * s;
            string monthLabel = Settings.MonthStartDay != 1 ? "BILLING MONTH" : "THIS MONTH";
            string monthWhen = Settings.MonthStartDay != 1 ? "Since " + Settings.MonthStart().ToString("MMM d") : DateTime.Today.ToString("MMMM yyyy");

            splitCol = aw + arrowGap + Math.Max(Gfx.Width(g, Fmt.Size(u.TodayDown), body), Gfx.Width(g, Fmt.Size(u.MonthDown), body)) + colGap;
            float split = splitCol + aw + arrowGap + Math.Max(Gfx.Width(g, Fmt.Size(u.TodayUp), body), Gfx.Width(g, Fmt.Size(u.MonthUp), body));

            float need = Math.Max(Settings.ShowSplit ? split : 0, Math.Max(
                Gfx.Width(g, "TODAY", small) + labelGap + Gfx.Width(g, DateTime.Today.ToString("ddd, MMM d"), small),
                Gfx.Width(g, monthLabel, small) + labelGap + Gfx.Width(g, monthWhen, small)));
            need = Math.Max(need, Gfx.Width(g, Fmt.Size(u.MonthDown + u.MonthUp), big));
            foreach (var a in u.TopApps)
                need = Math.Max(need, 26 * s + Math.Min(Gfx.Width(g, a.Name, body), MaxAppName * s) + 14 * s + Gfx.Width(g, Fmt.Size(a.Bytes), body));
            return (int)Math.Ceiling(need + 2 * 18 * s);
        }
    }

    // The whole card image, shadow included.
    public Bitmap Build(Usage u, Theme t)
    {
        int apps = u.TopApps.Count;
        // top 16 + two sections (80 with the down/up line, 56 without) + 14 between, then the apps block
        int section = Settings.ShowSplit ? 80 : 56, totals = 16 + section + 14 + section;
        int shadow = Shadow, w = ContentWidth(u), h = (int)((apps > 0 ? totals + 46 + 30 * apps : totals + 6) * s);
        var bmp = new Bitmap(w + 2 * shadow, h + 2 * shadow, PixelFormat.Format32bppArgb);
        using (var content = new Bitmap(w, h, PixelFormat.Format32bppArgb))
        {
            // Content first, on a solid background so the text gets ClearType.
            using (var g = Graphics.FromImage(content))
            {
                g.Clear(t.CardBack);
                float pad = 18 * s, x = pad, right = w - pad;
                float y = Section(g, t, "TODAY", DateTime.Today.ToString("ddd, MMM d"), u.TodayDown, u.TodayUp, x, right, 16 * s);
                using (var p = new Pen(t.Divider, 1)) g.DrawLine(p, pad, y, w - pad, y);
                bool billing = Settings.MonthStartDay != 1;
                y = Section(g, t, billing ? "BILLING MONTH" : "THIS MONTH",
                    billing ? "Since " + Settings.MonthStart().ToString("MMM d") : DateTime.Today.ToString("MMMM yyyy"),
                    u.MonthDown, u.MonthUp, x, right, y + 14 * s);
                if (apps > 0)
                {
                    using (var p = new Pen(t.Divider, 1)) g.DrawLine(p, pad, y, w - pad, y);
                    TopApps(g, t, u.TopApps, x, right, y + 14 * s);
                }
            }
            Gfx.MakeOpaque(content);

            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var card = new RectangleF(shadow, shadow, w, h);

                // soft shadow
                for (int i = shadow; i >= 1; i--)
                {
                    float k = 1f - (float)i / shadow;
                    using (var path = Gfx.RoundRect(RectangleF.Inflate(card, i, i), 8 * s + i))
                    using (var b = new SolidBrush(Color.FromArgb((int)(t.Light ? 7 * k * k : 14 * k * k), 0, 0, 0)))
                        g.FillPath(b, path);
                }
                // Rounded card filled with the content (anti-aliased corners), then the border.
                using (var path = Gfx.RoundRect(card, 8 * s))
                {
                    using (var b = new TextureBrush(content, WrapMode.Clamp))
                    {
                        b.TranslateTransform(shadow, shadow);
                        g.FillPath(b, path);
                    }
                    using (var p = new Pen(t.CardBorder, 1)) g.DrawPath(p, path);
                }
            }
        }
        return bmp;
    }

    public bool IsOpen { get; private set; }

    public void HideCard()
    {
        if (!IsOpen) return;
        ShowWindow(Handle, 0 /*SW_HIDE*/);
        IsOpen = false;
    }

    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);

    // Draws one block (label, big total, down/up split) and returns the y just below it.
    float Section(Graphics g, Theme t, string label, string when, ulong down, ulong up, float x, float right, float y)
    {
        using (var small = Gfx.MakeFont("Segoe UI Variable Small Semibol", 11 * s, "Segoe UI Semibold"))
        using (var big = Gfx.MakeFont("Segoe UI Variable Display Semib", 26 * s, "Segoe UI Semibold"))
        using (var body = Gfx.MakeFont("Segoe UI Variable Text", 13 * s, "Segoe UI"))
        {
            Gfx.Text(g, label, small, t.Secondary, t.CardBack, x, y);
            Gfx.Text(g, when, small, t.Secondary, t.CardBack, right - Gfx.Width(g, when, small), y);

            // Totals only: centre the number; with the down/up line it stays left-aligned above it.
            string total = Fmt.Size(down + up);
            float tx = Settings.ShowSplit ? x - 1 * s : (x + right - Gfx.Width(g, total, big)) / 2;
            Gfx.Text(g, total, big, t.Text, t.CardBack, tx, y + 16 * s);

            if (!Settings.ShowSplit) return y + 56 * s;
            float row = y + 52 * s, aw = 8 * s, ah = 10 * s, half = splitCol;
            Gfx.Arrow(g, false, x, row + 3 * s, aw, ah, 1.6f * s, t.Down);
            Gfx.Text(g, Fmt.Size(down), body, t.Text, t.CardBack, x + aw + 6 * s, row);
            Gfx.Arrow(g, true, x + half, row + 3 * s, aw, ah, 1.6f * s, t.Up);
            Gfx.Text(g, Fmt.Size(up), body, t.Text, t.CardBack, x + half + aw + 6 * s, row);
            return row + 28 * s;
        }
    }

    // "TOP APPS TODAY": icon, name, amount, and a bar relative to the biggest app.
    void TopApps(Graphics g, Theme t, List<AppUse> apps, float x, float right, float y)
    {
        using (var small = Gfx.MakeFont("Segoe UI Variable Small Semibol", 11 * s, "Segoe UI Semibold"))
        using (var body = Gfx.MakeFont("Segoe UI Variable Text", 13 * s, "Segoe UI"))
        using (var track = new SolidBrush(t.Divider))
        using (var fill = new SolidBrush(t.Down))
        using (var placeholder = new SolidBrush(t.Divider))
        {
            Gfx.Text(g, "TOP APPS TODAY", small, t.Secondary, t.CardBack, x, y);
            ulong max = apps[0].Bytes;
            float icon = 16 * s, textX = x + icon + 10 * s, row = y + 22 * s;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            foreach (var a in apps)
            {
                var iconRect = new RectangleF(x, row + 1 * s, icon, icon);
                if (a.Icon != null) g.DrawImage(a.Icon, iconRect);
                else using (var path = Gfx.RoundRect(iconRect, 3 * s)) g.FillPath(placeholder, path);

                string size = Fmt.Size(a.Bytes);
                float sizeW = Gfx.Width(g, size, body);
                Gfx.Text(g, size, body, t.Secondary, t.CardBack, right - sizeW, row);
                Gfx.TextFit(g, a.Name, body, t.Text, t.CardBack, textX, row, right - sizeW - 10 * s - textX);

                float barY = row + 20 * s, barW = right - textX, barH = 3 * s;
                using (var path = Gfx.RoundRect(new RectangleF(textX, barY, barW, barH), barH / 2)) g.FillPath(track, path);
                float fw = Math.Max(barH, barW * (float)((double)a.Bytes / max));
                using (var path = Gfx.RoundRect(new RectangleF(textX, barY, fw, barH), barH / 2)) g.FillPath(fill, path);
                row += 30 * s;
            }
        }
    }
}

// ---------- the taskbar meter ----------

class Meter : LayeredForm
{
    readonly Timer tick = new Timer { Interval = 250 };
    readonly float scale;
    readonly int H, MinWidth;
    int W, FullWidth;
    readonly UsageCard card;
    SettingsForm settingsForm;
    DateTime hoverSince;

    long lastRx = -1, lastTx = -1;
    DateTime lastSample = DateTime.MinValue, lastUsage = DateTime.MinValue;
    double upBps, downBps;
    Usage usage;
    bool usageBusy, hiddenForFullscreen;
    int hoverTicks;
    Color back = Color.FromArgb(32, 32, 32);
    DateTime lastBackSample = DateTime.MinValue;

    public Meter()
    {
        using (var g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;
        FullWidth = W = ComputeFullWidth();
        MinWidth = (int)(40 * scale);
        // Shorter than the taskbar so the taskbar's 1px top edge stays visible above the meter.
        H = (int)(40 * scale);
        Size = new Size(W, H);
        Text = "NetMeter";
        card = new UsageCard(scale);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Settings…", null, delegate { OpenSettings(); });
        menu.Items.Add("Refresh usage", null, delegate { RefreshUsage(); });
        var autoStart = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true, Checked = AutoStart.Enabled };
        autoStart.CheckedChanged += delegate { AutoStart.Set(autoStart.Checked); };
        menu.Items.Add(autoStart);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, delegate { Close(); });
        // The meter never takes focus, so Windows wouldn't tell the menu about clicks elsewhere;
        // bringing it to the foreground lets a click anywhere close the menu.
        menu.Opening += delegate { autoStart.Checked = AutoStart.Enabled; SetForegroundWindow(Handle); };
        menu.Opened += delegate { SetForegroundWindow(menu.Handle); };
        ContextMenuStrip = menu;

        Settings.Changed += OnSettingsChanged;
        tick.Tick += delegate { OnTick(); };
        tick.Start();
        RefreshUsage();
    }

    void OnTick()
    {
        bool full = ForegroundIsFullscreen();
        if (full != hiddenForFullscreen)
        {
            hiddenForFullscreen = full;
            if (full) Hide(); else Show();
        }
        if (!full) { PlaceNextToTray(); MatchTaskbarColour(); }

        if ((DateTime.Now - lastSample).TotalMilliseconds >= Settings.UpdateMs - 50) SampleSpeed();
        if ((DateTime.Now - lastUsage).TotalSeconds >= 30) RefreshUsage();

        // Hover card: appears after the chosen delay over the meter, hides as soon as the mouse leaves.
        bool over = Visible && Bounds.Contains(Cursor.Position) && !ContextMenuStrip.Visible;
        if (over)
        {
            if (hoverTicks++ == 0) hoverSince = DateTime.Now;
            if (!card.IsOpen && (DateTime.Now - hoverSince).TotalMilliseconds >= Settings.HoverDelayMs)
            {
                if ((DateTime.Now - lastUsage).TotalSeconds > 5) RefreshUsage();
                ShowCard();
            }
        }
        else
        {
            hoverTicks = 0;
            card.HideCard();
        }
    }

    void ShowCard()
    {
        if (usage != null) card.ShowAbove(Bounds, usage, Theme.Current());
    }

    void OpenSettings()
    {
        if (settingsForm == null || settingsForm.IsDisposed) settingsForm = new SettingsForm();
        settingsForm.Show();
        settingsForm.Activate();
    }

    void OnSettingsChanged()
    {
        tick.Interval = Math.Min(250, Settings.UpdateMs / 2);
        FullWidth = ComputeFullWidth();
        lastUsage = DateTime.MinValue;   // re-query with the new units / month start / app count
        PlaceNextToTray();
        Render();
    }

    // Width needed for the longest speed at full size, plus the arrow column.
    int ComputeFullWidth()
    {
        using (var g = Graphics.FromHwnd(IntPtr.Zero))
        using (var font = MeterFont())
            return (int)Math.Ceiling(Gfx.Width(g, Fmt.WidestSpeed, font) + 18 * scale);
    }

    Font MeterFont()
    {
        return Gfx.MakeFont("Segoe UI Variable Text", Settings.TextPx * scale, "Segoe UI");
    }

    // ---------- speed ----------

    void SampleSpeed()
    {
        long rx, tx;
        ReadBytes(out rx, out tx);
        var now = DateTime.Now;
        if (lastRx >= 0)
        {
            double secs = (now - lastSample).TotalSeconds;
            upBps = Math.Max(0, tx - lastTx) / secs;
            downBps = Math.Max(0, rx - lastRx) / secs;
            Render();
        }
        lastRx = rx; lastTx = tx; lastSample = now;
    }

    static void ReadBytes(out long rx, out long tx)
    {
        rx = 0; tx = 0;
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
            string d = (ni.Description + " " + ni.Name).ToLowerInvariant();
            if (d.Contains("hyper-v") || d.Contains("virtual") || d.Contains("vmware") || d.Contains("vethernet") || d.Contains("loopback") || d.Contains("wsl")) continue;
            try
            {
                var s = ni.GetIPStatistics();
                rx += s.BytesReceived; tx += s.BytesSent;
            }
            catch { }
        }
    }

    void RefreshUsage()
    {
        if (usageBusy) return;
        usageBusy = true;
        lastUsage = DateTime.Now;
        Task.Run(() =>
        {
            Usage u = null;
            try { u = Usage.Query(); }
            catch { }
            BeginInvoke((Action)delegate
            {
                usageBusy = false;
                if (u == null) return;
                usage = u;
                if (card.IsOpen) ShowCard();
            });
        });
    }

    // The text needs a solid background for ClearType, so copy the taskbar colour from the
    // small gap between the meter and the tray (re-checked every 2s: theme, wallpaper tint).
    void MatchTaskbarColour()
    {
        if ((DateTime.Now - lastBackSample).TotalSeconds < 2) return;
        lastBackSample = DateTime.Now;
        var c = Gfx.ScreenPixel(Left + W + (int)(2 * scale), Top + H / 2);
        if (c.ToArgb() != back.ToArgb()) { back = c; Render(); }
    }

    // ---------- placement ----------

    int appsRight = -1;
    bool scanBusy;
    DateTime lastScan = DateTime.MinValue;

    // Right edge of the Start/Search/app buttons (Windows 11 draws them in XAML, so only UI Automation
    // can see them). Runs in the background every 2s.
    void ScanTaskbarButtons()
    {
        if (scanBusy || (DateTime.Now - lastScan).TotalSeconds < 2) return;
        scanBusy = true;
        lastScan = DateTime.Now;
        IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
        Task.Run(() =>
        {
            int r = -1;
            try
            {
                var cond = new System.Windows.Automation.OrCondition(
                    new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.ClassNameProperty, "Taskbar.TaskListButtonAutomationPeer"),
                    new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.AutomationIdProperty, "StartButton"),
                    new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.AutomationIdProperty, "SearchButton"),
                    new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.AutomationIdProperty, "TaskViewButton"),
                    new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.AutomationIdProperty, "WidgetsButton"));
                var root = System.Windows.Automation.AutomationElement.FromHandle(taskbar);
                foreach (System.Windows.Automation.AutomationElement e in root.FindAll(System.Windows.Automation.TreeScope.Descendants, cond))
                {
                    var b = e.Current.BoundingRectangle;
                    if (b.IsEmpty || b.Width <= 0) continue;
                    if (b.Right > r) r = (int)b.Right;
                }
            }
            catch { }
            BeginInvoke((Action)delegate { appsRight = r; scanBusy = false; });
        });
    }

    void PlaceNextToTray()
    {
        IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero) return;
        RECT tb, tray;
        GetWindowRect(taskbar, out tb);
        IntPtr trayWnd = FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        int right = trayWnd != IntPtr.Zero && GetWindowRect(trayWnd, out tray) && tray.Left > tb.Left ? tray.Left : tb.Right - (int)(200 * scale);
        int gap = (int)(4 * scale);

        // Stay next to the tray; when the app buttons reach into that space, shrink to the free gap
        // (Render switches to a shorter number format) instead of covering the last button.
        ScanTaskbarButtons();
        int width = FullWidth;
        if (appsRight > 0 && appsRight < right)
            width = Math.Max(MinWidth, Math.Min(FullWidth, right - appsRight - 2 * gap));
        bool resized = width != W;
        W = width;

        int x = right - W - gap;
        int y = tb.Top + (tb.Bottom - tb.Top - H) / 2;
        SetWindowPos(Handle, HWND_TOPMOST, x, y, W, H, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        if (resized) Render();
    }

    static bool ForegroundIsFullscreen()
    {
        IntPtr h = GetForegroundWindow();
        if (h == IntPtr.Zero) return false;
        var cls = new StringBuilder(256);
        GetClassName(h, cls, cls.Capacity);
        string c = cls.ToString();
        if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd") return false;
        var screen = Screen.FromHandle(h);
        if (!screen.Primary) return false;
        RECT r;
        if (!GetWindowRect(h, out r)) return false;
        var b = screen.Bounds;
        return r.Left <= b.Left && r.Top <= b.Top && r.Right >= b.Right && r.Bottom >= b.Bottom;
    }

    // ---------- drawing ----------

    protected override void OnShown(EventArgs e) { base.OnShown(e); Render(); }

    void Render()
    {
        if (!IsHandleCreated) return;
        var t = Theme.Current();
        using (var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp))
            using (var font = MeterFont())
            {
                g.Clear(back);
                // Longest number format that fits the current width: "1.5 KB/s", then "1.5K/s", then "1.5K".
                string up = null, down = null;
                for (int style = 0; style <= 2; style++)
                {
                    up = Fmt.Speed(upBps, style);
                    down = Fmt.Speed(downBps, style);
                    float need = 2 * scale + Math.Max(Gfx.Width(g, up, font), Gfx.Width(g, down, font)) + 5 * scale + 7 * scale + 2 * scale;
                    if (need <= W) break;
                }
                float lineH = (Settings.TextPx + 5) * scale, top = (H - 2 * lineH) / 2f;
                DrawLine(g, true, up, top, font, t);
                DrawLine(g, false, down, top + lineH, font, t);
            }
            Gfx.MakeOpaque(bmp);
            Gfx.Present(Handle, bmp, Left, Top);
        }
    }

    // Numbers are right-aligned next to the tray. Arrows sit in a column at the right edge,
    // or (setting) just left of each number.
    void DrawLine(Graphics g, bool up, string value, float y, Font font, Theme t)
    {
        float aw = 7 * scale, ah = 9 * scale, gap = 5 * scale, right = W - 2 * scale;
        float valueW = Gfx.Width(g, value, font), arrowY = y + (Settings.TextPx - 9) * scale;
        Color c = up ? t.Up : t.Down;
        if (Settings.ArrowsRight)
        {
            Gfx.Text(g, value, font, t.Text, back, right - aw - gap - valueW, y);
            Gfx.Arrow(g, up, right - aw, arrowY, aw, ah, 1.5f * scale, c);
        }
        else
        {
            Gfx.Text(g, value, font, t.Text, back, right - valueW, y);
            Gfx.Arrow(g, up, right - valueW - gap - aw, arrowY, aw, ah, 1.5f * scale, c);
        }
    }

    // ---------- Win32 ----------

    static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    const uint SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] static extern IntPtr FindWindow(string cls, string name);
    [DllImport("user32.dll")] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string name);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
}

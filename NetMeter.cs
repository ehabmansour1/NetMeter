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
[assembly: AssemblyVersion("1.2.0.0")]
[assembly: AssemblyFileVersion("1.2.0.0")]
[assembly: AssemblyInformationalVersion("1.2.0")]

static class Program
{
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    [STAThread]
    static void Main(string[] args)
    {
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
        var monthStart = new DateTimeOffset(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1));
        var states = new NetworkUsageStates { Roaming = TriStates.DoNotCare, Shared = TriStates.DoNotCare };
        var r = new Usage();
        var profiles = NetworkInformation.GetConnectionProfiles();
        foreach (var p in profiles)
        {
            foreach (var u in Read(p, today, now, states)) { r.TodayDown += u.BytesReceived; r.TodayUp += u.BytesSent; }
            foreach (var u in Read(p, monthStart, now, states)) { r.MonthDown += u.BytesReceived; r.MonthUp += u.BytesSent; }
        }
        try { r.TopApps = TopAppsToday(profiles, today, now, states, 5); }
        catch { }
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
    const double GB = 1024.0 * 1024 * 1024, MB = 1024.0 * 1024;

    public static string Size(ulong bytes)
    {
        if (bytes < GB) return (bytes / MB).ToString("0") + " MB";
        double g = bytes / GB;
        return g.ToString(g < 10 ? "0.00" : g < 100 ? "0.0" : "0") + " GB";
    }

    public static string Speed(double bps)
    {
        if (bps < 1024) return ((int)bps) + " B/s";
        if (bps < MB) return (bps / 1024).ToString(bps < 10 * 1024 ? "0.0" : "0") + " KB/s";
        return (bps / MB).ToString(bps < 10 * MB ? "0.0" : "0") + " MB/s";
    }
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
        return light
            ? new Theme { Light = true, Text = Color.FromArgb(26, 26, 26), Secondary = Color.FromArgb(96, 96, 96),
                          Up = Color.FromArgb(196, 89, 0), Down = Color.FromArgb(0, 103, 192),
                          CardBack = Color.FromArgb(249, 249, 249), CardBorder = Color.FromArgb(222, 222, 222), Divider = Color.FromArgb(229, 229, 229) }
            : new Theme { Light = false, Text = Color.FromArgb(255, 255, 255), Secondary = Color.FromArgb(170, 170, 170),
                          Up = Color.FromArgb(255, 170, 90), Down = Color.FromArgb(96, 205, 255),
                          CardBack = Color.FromArgb(44, 44, 44), CardBorder = Color.FromArgb(64, 64, 64), Divider = Color.FromArgb(61, 61, 61) };
    }
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

    // The whole card image, shadow included.
    public Bitmap Build(Usage u, Theme t)
    {
        int apps = u.TopApps.Count;
        int shadow = Shadow, w = (int)(272 * s), h = (int)((apps > 0 ? 236 + 30 * apps : 196) * s);
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
                y = Section(g, t, "THIS MONTH", DateTime.Today.ToString("MMMM yyyy"), u.MonthDown, u.MonthUp, x, right, y + 14 * s);
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

            Gfx.Text(g, Fmt.Size(down + up), big, t.Text, t.CardBack, x - 1 * s, y + 16 * s);

            float row = y + 52 * s, aw = 8 * s, ah = 10 * s, half = (right - x) / 2;
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
    readonly int W, H;
    readonly UsageCard card;

    long lastRx = -1, lastTx = -1;
    DateTime lastSample = DateTime.MinValue, lastUsage = DateTime.MinValue;
    string upText = "0 B/s", downText = "0 B/s";
    Usage usage;
    bool usageBusy, hiddenForFullscreen;
    int hoverTicks;
    Color back = Color.FromArgb(32, 32, 32);
    DateTime lastBackSample = DateTime.MinValue;

    public Meter()
    {
        using (var g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;
        W = (int)(76 * scale);
        // Shorter than the taskbar so the taskbar's 1px top edge stays visible above the meter.
        H = (int)(40 * scale);
        Size = new Size(W, H);
        Text = "NetMeter";
        card = new UsageCard(scale);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Refresh usage", null, delegate { RefreshUsage(); });
        var autoStart = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true, Checked = AutoStart.Enabled };
        autoStart.CheckedChanged += delegate { AutoStart.Set(autoStart.Checked); };
        menu.Opening += delegate { autoStart.Checked = AutoStart.Enabled; };
        menu.Items.Add(autoStart);
        menu.Items.Add("Exit", null, delegate { Close(); });
        ContextMenuStrip = menu;

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

        if ((DateTime.Now - lastSample).TotalMilliseconds >= 950) SampleSpeed();
        if ((DateTime.Now - lastUsage).TotalSeconds >= 30) RefreshUsage();

        // Hover card: appears after ~0.4s over the meter, hides as soon as the mouse leaves.
        bool over = Visible && Bounds.Contains(Cursor.Position) && !ContextMenuStrip.Visible;
        if (over)
        {
            if (++hoverTicks == 2)
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

    // ---------- speed ----------

    void SampleSpeed()
    {
        long rx, tx;
        ReadBytes(out rx, out tx);
        var now = DateTime.Now;
        if (lastRx >= 0)
        {
            double secs = (now - lastSample).TotalSeconds;
            upText = Fmt.Speed(Math.Max(0, tx - lastTx) / secs);
            downText = Fmt.Speed(Math.Max(0, rx - lastRx) / secs);
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

    void PlaceNextToTray()
    {
        IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero) return;
        RECT tb, tray;
        GetWindowRect(taskbar, out tb);
        IntPtr trayWnd = FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        int right = trayWnd != IntPtr.Zero && GetWindowRect(trayWnd, out tray) && tray.Left > tb.Left ? tray.Left : tb.Right - (int)(200 * scale);
        int x = right - W - (int)(4 * scale);
        int y = tb.Top + (tb.Bottom - tb.Top - H) / 2;
        SetWindowPos(Handle, HWND_TOPMOST, x, y, W, H, SWP_NOACTIVATE | SWP_SHOWWINDOW);
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
            using (var font = Gfx.MakeFont("Segoe UI Variable Text", 12 * scale, "Segoe UI"))
            {
                g.Clear(back);
                float lineH = 17 * scale, top = (H - 2 * lineH) / 2f;
                DrawLine(g, true, upText, top, font, t);
                DrawLine(g, false, downText, top + lineH, font, t);
            }
            Gfx.MakeOpaque(bmp);
            Gfx.Present(Handle, bmp, Left, Top);
        }
    }

    // Right-aligned so the numbers sit next to the tray; the arrow sits just left of its number.
    void DrawLine(Graphics g, bool up, string value, float y, Font font, Theme t)
    {
        float right = W - 2 * scale, valueW = Gfx.Width(g, value, font);
        float aw = 7 * scale, ah = 9 * scale;
        Gfx.Text(g, value, font, t.Text, back, right - valueW, y);
        Gfx.Arrow(g, up, right - valueW - aw - 5 * scale, y + 3 * scale, aw, ah, 1.5f * scale, up ? t.Up : t.Down);
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
}

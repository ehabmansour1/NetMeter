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
        bool created;
        var mutex = new System.Threading.Mutex(true, "NetMeter_SingleInstance", out created);
        if (!created) return;
        SetProcessDPIAware();
        Application.EnableVisualStyles();
        Application.Run(new Meter());
        GC.KeepAlive(mutex);
    }
}

// ---------- today / month usage (same data as Settings > Network > Data usage) ----------

class Usage
{
    public ulong TodayDown, TodayUp, MonthDown, MonthUp;

    // The C# 5 compiler that ships with Windows can't bind AsTask<T> to WinRT async operations,
    // so it is resolved by reflection (the same thing PowerShell does).
    static readonly MethodInfo AsTaskOp = typeof(WindowsRuntimeSystemExtensions).GetMethods().First(m =>
        m.Name == "AsTask" && m.IsGenericMethod && m.GetParameters().Length == 1 &&
        m.GetParameters()[0].ParameterType.Name == "IAsyncOperation`1");

    static IEnumerable<NetworkUsage> Read(ConnectionProfile p, DateTimeOffset from, DateTimeOffset to, NetworkUsageStates states)
    {
        object op = p.GetNetworkUsageAsync(from, to, DataUsageGranularity.Total, states);
        var task = (Task)AsTaskOp.MakeGenericMethod(typeof(IReadOnlyList<NetworkUsage>)).Invoke(null, new[] { op });
        task.Wait();
        return ((System.Collections.IEnumerable)task.GetType().GetProperty("Result").GetValue(task)).Cast<NetworkUsage>();
    }

    public static Usage Query()
    {
        var now = DateTimeOffset.Now;
        var today = new DateTimeOffset(DateTime.Today);
        var monthStart = new DateTimeOffset(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1));
        var states = new NetworkUsageStates { Roaming = TriStates.DoNotCare, Shared = TriStates.DoNotCare };
        var r = new Usage();
        foreach (var p in NetworkInformation.GetConnectionProfiles())
        {
            foreach (var u in Read(p, today, now, states)) { r.TodayDown += u.BytesReceived; r.TodayUp += u.BytesSent; }
            foreach (var u in Read(p, monthStart, now, states)) { r.MonthDown += u.BytesReceived; r.MonthUp += u.BytesSent; }
        }
        return r;
    }

    public string ToText()
    {
        return "Today: " + Fmt.Size(TodayDown + TodayUp) + "  (down " + Fmt.Size(TodayDown) + ", up " + Fmt.Size(TodayUp) + ")\r\n" +
               "This month: " + Fmt.Size(MonthDown + MonthUp) + "  (down " + Fmt.Size(MonthDown) + ", up " + Fmt.Size(MonthUp) + ")";
    }
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

    public static float Width(Graphics g, string s, Font f)
    {
        return g.MeasureString(s, f, PointF.Empty, StringFormat.GenericTypographic).Width;
    }

    public static void Text(Graphics g, string s, Font f, Color c, float x, float y)
    {
        using (var b = new SolidBrush(c)) g.DrawString(s, f, b, x, y, StringFormat.GenericTypographic);
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

    // Anti-aliased text on a transparent surface looks thin once Windows composites it; lift the
    // partial-coverage alpha so strokes get the same weight as the taskbar clock.
    public static void ThickenAlpha(Bitmap bmp, double gamma)
    {
        var lut = new byte[256];
        for (int i = 0; i < 256; i++) lut[i] = i < 2 ? (byte)i : (byte)Math.Round(255 * Math.Pow(i / 255.0, gamma));
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var px = new byte[data.Stride * data.Height];
        Marshal.Copy(data.Scan0, px, 0, px.Length);
        for (int i = 3; i < px.Length; i += 4) px[i] = lut[px[i]];
        Marshal.Copy(px, 0, data.Scan0, px.Length);
        bmp.UnlockBits(data);
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

    public void ShowAbove(Rectangle anchor, Usage u, Theme t)
    {
        int shadow = (int)(12 * s), w = (int)(256 * s), h = (int)(196 * s);
        using (var bmp = new Bitmap(w + 2 * shadow, h + 2 * shadow, PixelFormat.Format32bppArgb))
        {
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
                using (var path = Gfx.RoundRect(card, 8 * s))
                {
                    using (var b = new SolidBrush(t.CardBack)) g.FillPath(b, path);
                    using (var p = new Pen(t.CardBorder, 1)) g.DrawPath(p, path);
                }

                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                float pad = 18 * s, x = card.X + pad, right = card.Right - pad;
                float y = card.Y + 16 * s;
                y = Section(g, t, "TODAY", DateTime.Today.ToString("ddd, MMM d"), u.TodayDown, u.TodayUp, x, right, y);
                using (var p = new Pen(t.Divider, 1)) g.DrawLine(p, card.X + pad, y, card.Right - pad, y);
                Section(g, t, "THIS MONTH", DateTime.Today.ToString("MMMM yyyy"), u.MonthDown, u.MonthUp, x, right, y + 14 * s);
            }

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
            Gfx.Text(g, label, small, t.Secondary, x, y);
            Gfx.Text(g, when, small, t.Secondary, right - Gfx.Width(g, when, small), y);

            Gfx.Text(g, Fmt.Size(down + up), big, t.Text, x - 1 * s, y + 16 * s);

            float row = y + 52 * s, aw = 8 * s, ah = 10 * s, half = (right - x) / 2;
            Gfx.Arrow(g, false, x, row + 3 * s, aw, ah, 1.6f * s, t.Down);
            Gfx.Text(g, Fmt.Size(down), body, t.Text, x + aw + 6 * s, row);
            Gfx.Arrow(g, true, x + half, row + 3 * s, aw, ah, 1.6f * s, t.Up);
            Gfx.Text(g, Fmt.Size(up), body, t.Text, x + half + aw + 6 * s, row);
            return row + 28 * s;
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

    public Meter()
    {
        using (var g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;
        W = (int)(76 * scale);
        H = (int)(48 * scale);
        Size = new Size(W, H);
        Text = "NetMeter";
        card = new UsageCard(scale);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Refresh usage", null, delegate { RefreshUsage(); });
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
        if (!full) PlaceNextToTray();

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
                // Alpha 1 instead of 0 so the whole box receives the mouse (hover + right-click).
                g.Clear(Color.FromArgb(1, t.Text));
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                float lineH = 17 * scale, top = (H - 2 * lineH) / 2f + 1 * scale;
                DrawLine(g, true, upText, top, font, t);
                DrawLine(g, false, downText, top + lineH, font, t);
            }
            Gfx.ThickenAlpha(bmp, 0.75);
            Gfx.Present(Handle, bmp, Left, Top);
        }
    }

    // Right-aligned so the numbers sit next to the tray; the arrow sits just left of its number.
    void DrawLine(Graphics g, bool up, string value, float y, Font font, Theme t)
    {
        float right = W - 2 * scale, valueW = Gfx.Width(g, value, font);
        float aw = 7 * scale, ah = 9 * scale;
        Gfx.Text(g, value, font, t.Text, right - valueW, y);
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

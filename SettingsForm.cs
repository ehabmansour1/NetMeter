// NetMeter settings window: right-click the meter > Settings…
// Changes apply immediately (Settings.Save raises Settings.Changed).
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

class SettingsForm : Form
{
    readonly float s;
    readonly bool dark;
    readonly Color back, card, text, secondary, border, accent;
    readonly Font body, small, header;
    int y;

    public SettingsForm()
    {
        using (var g = Graphics.FromHwnd(IntPtr.Zero)) s = g.DpiX / 96f;
        dark = !Theme.SystemLight;
        back = dark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 243, 243);
        card = dark ? Color.FromArgb(43, 43, 43) : Color.FromArgb(251, 251, 251);
        text = dark ? Color.White : Color.FromArgb(26, 26, 26);
        secondary = dark ? Color.FromArgb(170, 170, 170) : Color.FromArgb(96, 96, 96);
        border = dark ? Color.FromArgb(29, 29, 29) : Color.FromArgb(229, 229, 229);
        accent = AccentColour(dark);

        body = Gfx.MakeFont("Segoe UI Variable Text", 14 * s, "Segoe UI");
        small = Gfx.MakeFont("Segoe UI Variable Small", 12 * s, "Segoe UI");
        header = Gfx.MakeFont("Segoe UI Variable Text Semibold", 14 * s, "Segoe UI Semibold");

        Text = "NetMeter Settings";
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = back;
        ForeColor = text;
        Font = body;
        AutoScroll = true;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        Build();
    }

    void Build()
    {
        SuspendLayout();
        Controls.Clear();
        y = (int)(16 * s);

        Section("General");
        Add(Toggle("Start with Windows", null, AutoStart.Enabled, v => AutoStart.Set(v)));
        Add(Choice("Update speed every", null, new[] { "0.5 seconds", "1 second", "2 seconds" },
            new[] { 500, 1000, 2000 }, Settings.UpdateMs, v => Settings.UpdateMs = v));
        Add(Choice("Show speed in", null, new[] { "Bytes (KB/s, MB/s)", "Bits (Kbps, Mbps)" },
            new[] { 0, 1 }, Settings.Bits ? 1 : 0, v => Settings.Bits = v == 1));

        Section("Appearance");
        Add(Choice("Text size", null, new[] { "Small", "Normal", "Large", "Extra large" },
            new[] { 11, 12, 13, 14 }, Settings.TextPx, v => Settings.TextPx = v));
        Add(Choice("Arrows", null, new[] { "Right of the numbers", "Left of the numbers" },
            new[] { 1, 0 }, Settings.ArrowsRight ? 1 : 0, v => Settings.ArrowsRight = v == 1));
        Add(Swatch("Upload color", true));
        Add(Swatch("Download color", false));
        Add(Toggle("Show speeds under 1 KB/s as 0", "Calmer when the PC is idle",
            Settings.TinyAsZero, v => Settings.TinyAsZero = v));

        Section("Hover card");
        Add(Toggle("Show top apps", null, Settings.ShowTopApps, v => Settings.ShowTopApps = v));
        Add(Choice("Number of apps", null, new[] { "3", "5", "10" },
            new[] { 3, 5, 10 }, Settings.TopAppsCount, v => Settings.TopAppsCount = v));
        Add(Choice("Show card after", null, new[] { "Instantly", "0.4 seconds", "1 second" },
            new[] { 0, 400, 1000 }, Settings.HoverDelayMs, v => Settings.HoverDelayMs = v));

        Section("Data");
        Add(Choice("Count 1 GB as", "Most internet providers use 1000",
            new[] { "1024 MB (like Windows)", "1000 MB (like ISPs)" },
            new[] { 0, 1 }, Settings.DecimalUnits ? 1 : 0, v => Settings.DecimalUnits = v == 1));
        Add(Choice("Month starts on day", "Match your internet bill",
            Enumerable.Range(1, 28).Select(d => d.ToString()).ToArray(),
            Enumerable.Range(1, 28).ToArray(), Settings.MonthStartDay, v => Settings.MonthStartDay = v));

        // footer
        y += (int)(8 * s);
        var reset = FooterButton("Reset to defaults", false);
        reset.Location = new Point((int)(20 * s), y);
        reset.Click += delegate
        {
            Settings.ResetDefaults();
            AutoStart.Set(true);
            Build();
        };
        var close = FooterButton("Close", true);
        close.Location = new Point(RowWidth + (int)(20 * s) - close.PreferredSize.Width, y);
        close.Click += delegate { Close(); };
        Controls.Add(reset);
        Controls.Add(close);
        y += reset.PreferredSize.Height + (int)(20 * s);

        // Size to the content, but never taller than the screen (then it scrolls).
        int maxH = Screen.FromPoint(Cursor.Position).WorkingArea.Height - (int)(80 * s);
        ClientSize = new Size(RowWidth + (int)(40 * s), Math.Min(y, maxH));
        ResumeLayout();
    }

    int RowWidth { get { return (int)(440 * s); } }

    void Section(string title)
    {
        if (y > 20 * s) y += (int)(10 * s);
        var l = new Label { Text = title, Font = header, ForeColor = text, AutoSize = true, BackColor = back };
        l.Location = new Point((int)(22 * s), y);
        Controls.Add(l);
        y += (int)(28 * s);
    }

    void Add(RowPanel row)
    {
        row.Location = new Point((int)(20 * s), y);
        Controls.Add(row);
        y += row.Height + (int)(4 * s);
    }

    // A rounded settings card: title (+ optional description) on the left, a control on the right.
    RowPanel Row(string title, string description, Control control)
    {
        int h = (int)((description == null ? 44 : 58) * s);
        var row = new RowPanel { Fill = card, Border = border, Back = back, Radius = 5 * s, Size = new Size(RowWidth, h) };
        var t = new Label { Text = title, AutoSize = true, ForeColor = text, BackColor = card, Font = body };
        row.Controls.Add(t);
        if (description != null)
        {
            var d = new Label { Text = description, AutoSize = true, ForeColor = secondary, BackColor = card, Font = small };
            row.Controls.Add(d);
            t.Location = new Point((int)(16 * s), (int)(9 * s));
            d.Location = new Point((int)(16 * s), (int)(31 * s));
        }
        else t.Location = new Point((int)(16 * s), (h - t.PreferredHeight) / 2);
        control.Location = new Point(RowWidth - control.Width - (int)(16 * s), (h - control.Height) / 2);
        row.Controls.Add(control);
        return row;
    }

    RowPanel Toggle(string title, string description, bool value, Action<bool> set)
    {
        var tg = new ToggleSwitch(s) { On = value, Accent = accent, Back = card, Border = secondary, KnobOn = dark ? Color.Black : Color.White, KnobOff = secondary };
        tg.Changed += delegate { set(tg.On); Settings.Save(); };
        return Row(title, description, tg);
    }

    RowPanel Choice(string title, string description, string[] labels, int[] values, int current, Action<int> set)
    {
        var cb = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Standard,
            BackColor = dark ? Color.FromArgb(55, 55, 55) : Color.White,
            ForeColor = text,
            Font = body,
            Width = (int)(190 * s),
            IntegralHeight = false,
            MaxDropDownItems = 12
        };
        cb.Items.AddRange(labels);
        int idx = Array.IndexOf(values, current);
        cb.SelectedIndex = idx >= 0 ? idx : 0;
        cb.HandleCreated += delegate { if (dark) SetWindowTheme(cb.Handle, "DarkMode_CFD", null); };
        cb.SelectedIndexChanged += delegate { set(values[cb.SelectedIndex]); Settings.Save(); };
        return Row(title, description, cb);
    }

    RowPanel Swatch(string title, bool up)
    {
        var sw = new ColorSwatch(s) { Back = card, Border = secondary };
        Func<Color> current = () => up ? Theme.Current().Up : Theme.Current().Down;
        sw.Colour = current();
        sw.Click += delegate
        {
            using (var dlg = new ColorDialog { Color = sw.Colour, FullOpen = true })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (up) Settings.UpColor = dlg.Color.ToArgb(); else Settings.DownColor = dlg.Color.ToArgb();
                Settings.Save();
                sw.Colour = current();
            }
        };
        var row = Row(title, null, sw);
        return row;
    }

    Button FooterButton(string label, bool primary)
    {
        var b = new Button
        {
            Text = label,
            Font = body,
            FlatStyle = FlatStyle.Flat,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding((int)(14 * s), (int)(4 * s), (int)(14 * s), (int)(4 * s)),
            BackColor = primary ? accent : card,
            ForeColor = primary ? (dark ? Color.Black : Color.White) : text,
            Cursor = Cursors.Hand
        };
        b.FlatAppearance.BorderColor = primary ? accent : (dark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(210, 210, 210));
        return b;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int on = dark ? 1 : 0;
        DwmSetWindowAttribute(Handle, 20 /*DWMWA_USE_IMMERSIVE_DARK_MODE*/, ref on, 4);
        if (dark) SetWindowTheme(Handle, "DarkMode_Explorer", null);   // dark scrollbar if it ever scrolls
    }

    // Windows accent colour (DWM stores it as ABGR); falls back to the default Windows 11 blue.
    static Color AccentColour(bool dark)
    {
        try
        {
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM"))
            {
                object v = k == null ? null : k.GetValue("AccentColor");
                if (v != null)
                {
                    uint abgr = unchecked((uint)Convert.ToInt32(v));
                    var c = Color.FromArgb((int)(abgr & 0xFF), (int)((abgr >> 8) & 0xFF), (int)((abgr >> 16) & 0xFF));
                    // Windows lightens the accent on dark backgrounds.
                    return dark ? Color.FromArgb((c.R + 255) / 2, (c.G + 255) / 2, (c.B + 255) / 2) : c;
                }
            }
        }
        catch { }
        return dark ? Color.FromArgb(76, 194, 255) : Color.FromArgb(0, 95, 184);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { body.Dispose(); small.Dispose(); header.Dispose(); }
        base.Dispose(disposing);
    }

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h, string app, string idList);
}

// Rounded card behind each setting.
class RowPanel : Panel
{
    public Color Fill, Border, Back;
    public float Radius;

    public RowPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Back);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Gfx.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Radius))
        {
            using (var b = new SolidBrush(Fill)) g.FillPath(b, path);
            using (var p = new Pen(Border, 1)) g.DrawPath(p, path);
        }
    }
}

// Windows 11 style on/off switch.
class ToggleSwitch : Control
{
    bool on;
    public Color Accent, Back, Border, KnobOn, KnobOff;
    public event EventHandler Changed;

    public bool On { get { return on; } set { on = value; Invalidate(); } }

    public ToggleSwitch(float s)
    {
        Size = new Size((int)(40 * s), (int)(20 * s));
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Cursor = Cursors.Hand;
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        on = !on;
        Invalidate();
        if (Changed != null) Changed(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Back);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var path = Gfx.RoundRect(r, r.Height / 2))
        {
            if (on) using (var b = new SolidBrush(Accent)) g.FillPath(b, path);
            else using (var p = new Pen(Border, 1)) g.DrawPath(p, path);
        }
        float d = Height * 0.6f, x = on ? Width - d - Height * 0.22f : Height * 0.22f;
        using (var b = new SolidBrush(on ? KnobOn : KnobOff)) g.FillEllipse(b, x, (Height - d) / 2f, d, d);
    }
}

// Clickable color chip.
class ColorSwatch : Control
{
    Color colour;
    public Color Back, Border;
    public Color Colour { get { return colour; } set { colour = value; Invalidate(); } }

    public ColorSwatch(float s)
    {
        Size = new Size((int)(44 * s), (int)(24 * s));
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Cursor = Cursors.Hand;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Back);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Gfx.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 4))
        {
            using (var b = new SolidBrush(colour)) g.FillPath(b, path);
            using (var p = new Pen(Border, 1)) g.DrawPath(p, path);
        }
    }
}

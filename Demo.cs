// NetMeter --demo <folder>: draws the README / social-preview images with sample numbers,
// using the real meter, card and settings drawing code (so no real usage data is published).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

static class Demo
{
    const float S = 1.35f;   // drawing scale for the images

    public static void Run(string folder)
    {
        Directory.CreateDirectory(folder);
        // Defaults in memory only - nothing is saved to the registry.
        Settings.ShowSplit = true; Settings.ShowTopApps = true; Settings.TopAppsCount = 5;
        Settings.Bits = false; Settings.DecimalUnits = false; Settings.MonthStartDay = 1;
        Settings.TextPx = 12; Settings.ArrowsRight = true; Settings.TinyAsZero = false;
        Settings.UpColor = 0; Settings.DownColor = 0;

        var t = Theme.For(false);   // dark
        var card = new UsageCard(S);

        using (var img = card.Build(Sample(5), t)) img.Save(Path.Combine(folder, "card.png"));

        Settings.TopAppsCount = 3;
        using (var hero = Hero(card.Build(Sample(3), t), t))
        {
            hero.Save(Path.Combine(folder, "social-preview.png"));
            using (var strip = hero.Clone(new Rectangle(hero.Width - 560, hero.Height - Strip, 560, Strip), hero.PixelFormat))
                strip.Save(Path.Combine(folder, "taskbar.png"));
        }
        SettingsShot(Path.Combine(folder, "settings.png"));
    }

    static int Strip { get { return (int)(48 * S); } }

    static Usage Sample(int apps)
    {
        const ulong MB = 1024UL * 1024, GB = MB * 1024;
        var u = new Usage
        {
            TodayDown = 2 * GB + 890 * MB, TodayUp = 562 * MB,
            MonthDown = 131 * GB + 300 * MB, MonthUp = 17 * GB + 410 * MB
        };
        var all = new List<AppUse>
        {
            App("Google Chrome", 1270 * MB, @"C:\Program Files\Google\Chrome\Application\chrome.exe"),
            App("Microsoft Edge", 806 * MB, @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"),
            App("Visual Studio Code", 412 * MB, Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Programs\Microsoft VS Code\Code.exe")),
            App("Windows Explorer", 236 * MB, @"C:\Windows\explorer.exe"),
            App("OBS Studio", 118 * MB, @"C:\Program Files\obs-studio\bin\64bit\obs64.exe"),
        };
        u.TopApps = all.GetRange(0, apps);
        return u;
    }

    static AppUse App(string name, ulong bytes, string exe)
    {
        return new AppUse { Name = name, Bytes = bytes, Icon = File.Exists(exe) ? AppNames.Icon(exe) : null };
    }

    // 1280x640: title on the left, the hover card above a taskbar with the meter on the right.
    static Bitmap Hero(Bitmap cardImg, Theme t)
    {
        int w = 1280, h = 640, strip = Strip;
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (cardImg)
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (var bg = new LinearGradientBrush(new Rectangle(0, 0, w, h), Color.FromArgb(8, 22, 52), Color.FromArgb(14, 78, 160), 25f))
                g.FillRectangle(bg, 0, 0, w, h);
            using (var glow = new GraphicsPath())
            {
                glow.AddEllipse(560, -260, 1000, 900);
                using (var pb = new PathGradientBrush(glow) { CenterColor = Color.FromArgb(90, 80, 170, 255), SurroundColors = new[] { Color.FromArgb(0, 80, 170, 255) } })
                    g.FillPath(pb, glow);
            }

            // title block
            using (var titleF = Gfx.MakeFont("Segoe UI Variable Display Semib", 76, "Segoe UI Semibold"))
            using (var subF = Gfx.MakeFont("Segoe UI Variable Text", 25, "Segoe UI"))
            using (var pillF = Gfx.MakeFont("Segoe UI Variable Text Semibold", 17, "Segoe UI Semibold"))
            using (var white = new SolidBrush(Color.White))
            using (var soft = new SolidBrush(Color.FromArgb(210, 225, 255)))
            {
                using (var ico = new Icon(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "icon.ico"), 256, 256))
                using (var icoBmp = ico.ToBitmap())
                    g.DrawImage(icoBmp, 80, 120, 84, 84);
                g.DrawString("NetMeter", titleF, white, 182, 112);
                g.DrawString("Live upload & download speed on your Windows 11 taskbar. Hover it to see today's and this month's data usage, and the apps using it.",
                    subF, soft, new RectangleF(84, 232, 560, 160));
                float px = 84;
                foreach (var pill in new[] { "Free & open source", "~50 KB", "No install" })
                {
                    var sz = g.MeasureString(pill, pillF);
                    var r = new RectangleF(px, 420, sz.Width + 24, 40);
                    using (var path = Gfx.RoundRect(r, 20))
                    using (var b = new SolidBrush(Color.FromArgb(40, 255, 255, 255)))
                        g.FillPath(b, path);
                    g.DrawString(pill, pillF, white, px + 12, 420 + (40 - sz.Height) / 2);
                    px += r.Width + 12;
                }
            }

            // taskbar
            var taskbarBack = Color.FromArgb(28, 28, 28);
            using (var b = new SolidBrush(taskbarBack)) g.FillRectangle(b, 0, h - strip, w, strip);
            using (var p = new Pen(Color.FromArgb(58, 58, 58))) g.DrawLine(p, 0, h - strip, w, h - strip);
            int trayX = TrayMock(g, w, h - strip, strip, taskbarBack);

            int mw = (int)(80 * S), mh = (int)(40 * S);
            int mx = trayX - mw - (int)(6 * S), my = h - strip + (strip - mh) / 2;
            using (var meter = new Bitmap(mw, mh, PixelFormat.Format32bppArgb))
            {
                using (var mg = Graphics.FromImage(meter)) Meter.PaintMeter(mg, S, mw, mh, 58.4 * 1024, 4.6 * 1024 * 1024, taskbarBack, t);
                Gfx.MakeOpaque(meter);
                g.DrawImage(meter, mx, my);
            }

            // card above the meter, kept inside the image
            int shadow = (int)(12 * S);
            int cx = Math.Min(mx + mw / 2 - cardImg.Width / 2, w - cardImg.Width + shadow - 16);
            int cy = h - strip - cardImg.Height + shadow - (int)(8 * S);
            g.DrawImage(cardImg, cx, cy);
        }
        return bmp;
    }

    // Language, network, volume and clock, like the Windows 11 tray. Returns its left edge.
    static int TrayMock(Graphics g, int w, int top, int h, Color back)
    {
        using (var icons = Gfx.MakeFont("Segoe Fluent Icons", 15 * S, "Segoe MDL2 Assets"))
        using (var text = Gfx.MakeFont("Segoe UI Variable Text", 12 * S, "Segoe UI"))
        {
            var white = Color.White;
            float right = w - 20 * S;
            string time = "1:07 AM", date = "10/20/2026";
            float clockW = Math.Max(Gfx.Width(g, time, text), Gfx.Width(g, date, text));
            Gfx.Text(g, time, text, white, back, right - Gfx.Width(g, time, text), top + 7 * S);
            Gfx.Text(g, date, text, white, back, right - Gfx.Width(g, date, text), top + 25 * S);
            float x = right - clockW - 22 * S;
            foreach (var glyph in new[] { "\uE767", "\uE701" })   // volume, Wi-Fi
            {
                x -= Gfx.Width(g, glyph, icons);
                Gfx.Text(g, glyph, icons, white, back, x, top + (h - icons.Height) / 2f);
                x -= 12 * S;
            }
            x -= Gfx.Width(g, "ENG", text);
            Gfx.Text(g, "ENG", text, white, back, x, top + (h - text.Height) / 2f);
            return (int)(x - 12 * S);
        }
    }

    // The real Settings window, captured from the screen.
    static void SettingsShot(string file)
    {
        using (var f = new SettingsForm { ShowInTaskbar = false, TopMost = true })
        {
            f.SelectPage(1);
            f.Show();
            for (int i = 0; i < 10; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(60); }
            // Visible frame only (Bounds includes the invisible resize borders).
            RECT fr;
            var r = DwmGetWindowAttribute(f.Handle, 9 /*DWMWA_EXTENDED_FRAME_BOUNDS*/, out fr, Marshal.SizeOf(typeof(RECT))) == 0
                ? Rectangle.FromLTRB(fr.Left, fr.Top, fr.Right, fr.Bottom) : f.Bounds;
            using (var bmp = new Bitmap(r.Width, r.Height))
            {
                using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
                bmp.Save(file);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT value, int size);
}

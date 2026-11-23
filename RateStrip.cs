using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Cache;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
[assembly: System.Reflection.AssemblyTitle("中行美元牌价")]
[assembly: System.Reflection.AssemblyProduct("中行美元牌价")]

internal sealed class Quote
{
    public string Rate;
    public DateTime Published;
    public const string Url = "https://www.boc.cn/sourcedb/whpj/";

    public static Quote Fetch()
    {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        var request = (HttpWebRequest)WebRequest.Create(Url);
        request.UserAgent = "BOC USD Rate for Windows";
        request.CachePolicy = new HttpRequestCachePolicy(HttpRequestCacheLevel.NoCacheNoStore);
        request.Timeout = 15000;
        request.ReadWriteTimeout = 15000;
        request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
        using (var response = (HttpWebResponse)request.GetResponse())
        using (var reader = new System.IO.StreamReader(response.GetResponseStream(),
            System.Text.Encoding.UTF8))
        {
            var html = reader.ReadToEnd();
            var quote = Parse(html);
            if (quote == null) throw new FormatException("USD quote missing");
            return quote;
        }
    }

    public static Quote Parse(string html)
    {
        const RegexOptions flags = RegexOptions.IgnoreCase | RegexOptions.Singleline;
        Quote latest = null;
        foreach (Match table in Regex.Matches(html, @"<table\b[^>]*>(.*?)</table\s*>", flags))
        {
            int currency = -1, rate = -1, date = -1, time = -1;
            foreach (Match row in Regex.Matches(table.Groups[1].Value, @"<tr\b[^>]*>(.*?)</tr\s*>", flags))
            {
                var cells = Regex.Matches(row.Groups[1].Value, @"<t[dh]\b[^>]*>(.*?)</t[dh]\s*>", flags)
                    .Cast<Match>().Select(cell => WebUtility.HtmlDecode(
                        Regex.Replace(cell.Groups[1].Value, "<[^>]*>", "")).Trim()).ToArray();
                if (cells.Contains("货币名称"))
                {
                    currency = Array.IndexOf(cells, "货币名称");
                    rate = Array.IndexOf(cells, "现汇买入价");
                    date = Array.IndexOf(cells, "发布日期");
                    time = Array.IndexOf(cells, "发布时间");
                    continue;
                }
                if (new[] { currency, rate, date, time }.Any(index => index < 0 || index >= cells.Length)
                    || cells[currency] != "美元") continue;

                decimal value;
                DateTime published;
                // BOC can include a time in the date cell. Use the dedicated time column.
                string publicationDate = Regex.Split(cells[date], @"\s+")[0];
                if (!decimal.TryParse(cells[rate], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture,
                    out value) || value <= 0 || !DateTime.TryParseExact(publicationDate + " " + cells[time],
                    new[] { "yyyy/M/d H:mm:ss", "yyyy-M-d H:mm:ss" }, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out published)) continue;
                if (latest == null || published > latest.Published)
                    latest = new Quote { Rate = cells[rate], Published = published };
            }
        }
        return latest;
    }
}

internal sealed class RateStrip : Form
{
    private string displayText;
    private string measuredText;
    private uint measuredDpi;
    private int measuredHeight, measuredWidth;
    private float quotePixels;
    private Size quoteSize;
    private Rectangle menuCutout;
    private Size clippedSize;
    private readonly NotifyIcon tray;
    private readonly Icon trayIcon;
    private readonly ContextMenuStrip menu;
    private readonly System.Windows.Forms.Timer refresh;
    private readonly System.Windows.Forms.Timer position;
    private Quote latest;
    private bool fetching;
    private bool mouseWasDown;
    private readonly Form quoteInput;
    private readonly ToolStripMenuItem startup;
    private readonly System.Windows.Forms.Timer menuDismiss;
    private static string StartupShortcut
    {
        get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            "BOC USD Rate.lnk"); }
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [STAThread]
    private static void Main()
    {
        bool first;
        using (var mutex = new Mutex(true, @"Local\BocUsdRateStrip", out first))
        {
            if (!first) return;
            if (!SetProcessDpiAwarenessContext(new IntPtr(-4))) SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            TryEnableStartupByDefault();
            using (var strip = new RateStrip()) Application.Run(strip);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y,
        int width, int height, uint flags);
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= 0x08080080; // WS_EX_NOACTIVATE | WS_EX_LAYERED | WS_EX_TOOLWINDOW
            return parameters;
        }
    }

    public RateStrip()
    {
        Text = "中行美元牌价";
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        Font = new Font("Microsoft YaHei", 9);
        menu = new ContextMenuStrip();
        menuDismiss = new System.Windows.Forms.Timer { Interval = 25 };
        menuDismiss.Tick += (sender, args) =>
        {
            bool down = GetAsyncKeyState(1) < 0 || GetAsyncKeyState(2) < 0;
            if (down && !mouseWasDown && !menu.Bounds.Contains(Cursor.Position))
                menu.Close(ToolStripDropDownCloseReason.AppClicked);
            mouseWasDown = down;
        };
        menu.Closed += (sender, args) => menuDismiss.Stop();
        menu.Items.Add("刷新", null, (sender, args) => UpdateQuote());
        startup = new ToolStripMenuItem("开机启动");
        startup.Click += (sender, args) => ToggleStartup();
        menu.Items.Add(startup);
        menu.Items.Add("退出软件", null, (sender, args) => Close());
        quoteInput = new QuoteInputWindow();
        quoteInput.MouseUp += (sender, args) => { if (args.Button == MouseButtons.Right) ShowMenu(); };
        MouseUp += (sender, args) => { if (args.Button == MouseButtons.Right) ShowMenu(); };
        trayIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        tray = new NotifyIcon { Icon = trayIcon, Visible = true };
        tray.MouseClick += (sender, args) =>
        {
            if (args.Button == MouseButtons.Left) BeginInvoke(new Action(PositionStrip));
            if (args.Button == MouseButtons.Right) ShowMenu();
        };
        refresh = new System.Windows.Forms.Timer { Interval = 30 * 60 * 1000 };
        refresh.Tick += (sender, args) => UpdateQuote();
        position = new System.Windows.Forms.Timer { Interval = 100 };
        position.Tick += (sender, args) => PositionStrip();
        SetDisplay("美元  —", "正在获取");
        PositionStrip();
        Shown += (sender, args) =>
        {
            position.Start();
            RenderQuote();
            PositionStrip();
            refresh.Start();
            UpdateQuote();
        };
    }

    private void PositionStrip()
    {

        var taskbar = FindWindow("Shell_TrayWnd", null);
        var notificationArea = FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        NativeRect bar, notification;
        if (taskbar == IntPtr.Zero || notificationArea == IntPtr.Zero
            || !GetWindowRect(taskbar, out bar) || !GetWindowRect(notificationArea, out notification)
            || bar.Right - bar.Left <= bar.Bottom - bar.Top
            || bar.Bottom - bar.Top < 12
            || notification.Left - bar.Left < 24
            || bar.Bottom > Screen.PrimaryScreen.Bounds.Bottom + 1
            || bar.Top < Screen.PrimaryScreen.Bounds.Top)
        {
            if (Visible) Hide();
            quoteInput.Hide();
            return;
        }

        var dpi = GetDpiForWindow(taskbar);
        if (dpi == 0) dpi = 96;
        bool layoutChanged = FitQuote(dpi, bar.Bottom - bar.Top, notification.Left - bar.Left);
        var bounds = new Rectangle(notification.Left - quoteSize.Width,
            bar.Top + (bar.Bottom - bar.Top - quoteSize.Height) / 2, quoteSize.Width, quoteSize.Height);
        if (Bounds != bounds) { Bounds = bounds; layoutChanged = true; }
        if (!Visible && position.Enabled) { Show(); layoutChanged = true; }
        if (layoutChanged) RenderQuote();
        if (IsMenuActive())
        {
            quoteInput.Hide();
            SetWindowPos(Handle, new IntPtr(1), 0, 0, 0, 0, 0x0013);
            return;
        }
        quoteInput.Bounds = bounds;
        if (position.Enabled && !quoteInput.Visible) quoteInput.Show();
        if (quoteInput.Visible)
            SetWindowPos(quoteInput.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0013);
        SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0013);
        ClipUnderShellPopup(taskbar, bounds);
    }

    private delegate bool WindowVisitor(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(WindowVisitor visitor, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, System.Text.StringBuilder name, int capacity);
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr window, IntPtr region, bool redraw);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")]
    private static extern int CombineRgn(IntPtr target, IntPtr first, IntPtr second, int mode);

    private void ClipUnderShellPopup(IntPtr taskbar, Rectangle bounds)
    {
        uint shellProcess;
        GetWindowThreadProcessId(taskbar, out shellProcess);
        Rectangle cutout = Rectangle.Empty;
        var name = new System.Text.StringBuilder(128);
        EnumWindows((window, unused) =>
        {
            if (window == taskbar || !IsWindowVisible(window)) return true;
            uint process;
            GetWindowThreadProcessId(window, out process);
            if (process != shellProcess) return true;
            NativeRect rect;
            if (!GetWindowRect(window, out rect)) return true;
            var overlap = Rectangle.Intersect(bounds, Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom));
            if (overlap.Width <= 0 || overlap.Height <= 0) return true;
            GetClassName(window, name, name.Capacity);
            string windowClass = name.ToString();
            if (windowClass == "Progman" || windowClass == "WorkerW" ||
                windowClass == "Shell_SecondaryTrayWnd" || windowClass == "CabinetWClass" ||
                windowClass == "ExploreWClass") return true;
            int cloaked;
            if (DwmGetWindowAttribute(window, 14, out cloaked, sizeof(int)) == 0 && cloaked != 0)
                return true;
            overlap.Offset(-bounds.X, -bounds.Y);
            cutout = cutout.IsEmpty ? overlap : Rectangle.Union(cutout, overlap);
            return true;
        }, IntPtr.Zero);
        ApplyMenuCutout(cutout, bounds.Size);
    }

    private void ApplyMenuCutout(Rectangle cutout, Size size)
    {
        if (menuCutout == cutout && clippedSize == size) return;
        menuCutout = cutout;
        clippedSize = size;
        SetCutout(Handle, cutout, size);
        SetCutout(quoteInput.Handle, cutout, size);
    }

    private static void SetCutout(IntPtr window, Rectangle cutout, Size size)
    {
        if (cutout.IsEmpty)
        {
            SetWindowRgn(window, IntPtr.Zero, true);
            return;
        }
        var region = CreateRectRgn(0, 0, size.Width, size.Height);
        var hole = CreateRectRgn(cutout.Left, cutout.Top, cutout.Right, cutout.Bottom);
        CombineRgn(region, region, hole, 4); // RGN_DIFF
        DeleteObject(hole);
        if (SetWindowRgn(window, region, true) == 0) DeleteObject(region);
    }

    private sealed class QuoteInputWindow : Form
    {
        public QuoteInputWindow()
        {
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Black;
            // Zero alpha passes mouse clicks through to Explorer.
            Opacity = 0.004;
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                parameters.ExStyle |= 0x08000080;
                return parameters;
            }
        }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Blend
    {
        public byte Operation, Flags, Opacity, AlphaFormat;
    }

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr bitmap);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr bitmap);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr dc);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr screen, ref Point position,
        ref Size size, IntPtr source, ref Point origin, uint colorKey, ref Blend blend, uint flags);

    private bool FitQuote(uint dpi, int availableHeight, int availableWidth)
    {
        if (measuredText == displayText && measuredDpi == dpi &&
            measuredHeight == availableHeight && measuredWidth == availableWidth) return false;

        float scale = dpi / 96f;
        int padding = Math.Max(2, (int)Math.Ceiling(2 * scale));
        quotePixels = Math.Min(11 * scale, (availableHeight - 2 * padding) / 2.6f);
        using (var sample = new Bitmap(1, 1))
        using (var graphics = Graphics.FromImage(sample))
        {
            graphics.PageUnit = GraphicsUnit.Pixel;
            while (true)
            {
                using (var font = new Font(Font.FontFamily, quotePixels, GraphicsUnit.Pixel))
                {
                    var textSize = graphics.MeasureString(displayText, font);
                    int width = (int)Math.Ceiling(textSize.Width) + 2 * padding;
                    int height = (int)Math.Ceiling(textSize.Height) + 2 * padding;
                    if (width <= availableWidth && height <= availableHeight || quotePixels <= 3)
                    {
                        quoteSize = new Size(Math.Min(width, availableWidth), Math.Min(height, availableHeight));
                        break;
                    }
                }
                quotePixels = Math.Max(3, quotePixels * 0.9f);
            }
        }
        measuredText = displayText;
        measuredDpi = dpi;
        measuredHeight = availableHeight;
        measuredWidth = availableWidth;
        return true;
    }

    private Bitmap DrawQuote(Size size)
    {
        var bitmap = new Bitmap(size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var font = new Font(Font.FontFamily, quotePixels, GraphicsUnit.Pixel))
        using (var format = new StringFormat())
        {
            graphics.PageUnit = GraphicsUnit.Pixel;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            format.Alignment = StringAlignment.Center;
            format.LineAlignment = StringAlignment.Center;
            graphics.DrawString(displayText, font, Brushes.Black,
                new RectangleF(0, 0, size.Width, size.Height), format);
        }
        return bitmap;
    }

    private void RenderQuote()
    {
        var size = ClientSize;
        if (!IsHandleCreated || quotePixels <= 0 || size.Width <= 0 || size.Height <= 0) return;
        using (var bitmap = DrawQuote(size))
        {
            var dc = CreateCompatibleDC(IntPtr.Zero);
            var handle = bitmap.GetHbitmap(Color.FromArgb(0));
            var previous = SelectObject(dc, handle);
            try
            {
                var position = Location;
                var origin = Point.Empty;
                var blend = new Blend { Opacity = 255, AlphaFormat = 1 };
                UpdateLayeredWindow(Handle, IntPtr.Zero, ref position, ref size, dc, ref origin, 0, ref blend, 2);
            }
            finally
            {
                SelectObject(dc, previous);
                DeleteObject(handle);
                DeleteDC(dc);
            }
        }
    }

    private static bool IsMenuActive()
    {
        return FindWindow("#32768", null) != IntPtr.Zero;
    }

    private void ShowMenu()
    {
        var anchor = Cursor.Position;
        NativeRect bar;
        var taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar != IntPtr.Zero && GetWindowRect(taskbar, out bar))
            anchor.Y = bar.Top;
        startup.Checked = System.IO.File.Exists(StartupShortcut);
        mouseWasDown = GetAsyncKeyState(1) < 0 || GetAsyncKeyState(2) < 0;
        menu.Show(anchor, ToolStripDropDownDirection.AboveLeft);
        menuDismiss.Start();
    }

    private static string StartupPreference
    {
        get { return System.IO.Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "BocUsdRateStrip", "startup-disabled"); }
    }

    private static void CreateStartupShortcut()
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        dynamic shortcut = shell.CreateShortcut(StartupShortcut);
        shortcut.TargetPath = Application.ExecutablePath;
        shortcut.WorkingDirectory = Application.StartupPath;
        shortcut.Save();
    }

    private static bool IsStartupError(Exception ex)
    {
        return ex is UnauthorizedAccessException || ex is System.Security.SecurityException
            || ex is System.IO.IOException || ex is COMException;
    }

    private static void TryEnableStartupByDefault()
    {
        try
        {
            if (!System.IO.File.Exists(StartupPreference))
                CreateStartupShortcut();
        }
        catch (Exception ex)
        {
            if (!IsStartupError(ex)) throw;
        }
    }

    private void ToggleStartup()
    {
        try
        {
            if (startup.Checked)
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(StartupPreference));
                System.IO.File.WriteAllText(StartupPreference, "disabled");
                System.IO.File.Delete(StartupShortcut);
            }
            else
            {
                CreateStartupShortcut();
                if (System.IO.File.Exists(StartupPreference)) System.IO.File.Delete(StartupPreference);
            }
        }
        catch (Exception ex)
        {
            if (!IsStartupError(ex)) throw;
            MessageBox.Show("无法更改开机启动设置。", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        startup.Checked = System.IO.File.Exists(StartupShortcut);
    }
    private void SetDisplay(string first, string second, string status = "")
    {
        displayText = first + "\r\n" + second;
        if (IsHandleCreated) PositionStrip();
        tray.Text = first + "\n" + second + status;
    }

    private async void UpdateQuote()
    {
        if (fetching) return;
        fetching = true;
        try
        {
            var fetched = await Task.Run((Func<Quote>)Quote.Fetch);
            if (IsDisposed) return;
            if (latest == null || fetched.Published >= latest.Published) latest = fetched;
            SetDisplay("美元  " + latest.Rate, latest.Published.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            if (!(ex is WebException || ex is System.IO.IOException || ex is FormatException)) throw;
            if (IsDisposed) return;
            if (latest == null) SetDisplay("美元  —", "获取失败");
            else SetDisplay("美元  " + latest.Rate,
                latest.Published.ToString("HH:mm:ss", CultureInfo.InvariantCulture), "\n更新失败，保留上次报价");
        }
        finally { fetching = false; }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        quoteInput.Dispose();
        refresh.Dispose();
        position.Dispose();
        tray.Visible = false;
        tray.Dispose();
        if (trayIcon != null) trayIcon.Dispose();
        menu.Dispose();
        menuDismiss.Dispose();
        base.OnFormClosed(e);
    }
}



















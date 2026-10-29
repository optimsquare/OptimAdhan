using System.Windows;
using System.Windows.Forms;
using OptimAdhan.App.Services;
using OptimAdhan.App.ViewModels;
using Drawing = System.Drawing;

namespace OptimAdhan.App.Views;

/// <summary>Notification-area icon and menu: the app's home while no window is open.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly AppController _app;
    private readonly App _shell;
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _next = new() { Enabled = false };
    private readonly ToolStripMenuItem _widget = new("Show desktop widget");
    private readonly ToolStripMenuItem _stop = new("Stop Adhan");
    private readonly ToolStripMenuItem _skipNext = new("Mute next Adhan");
    private readonly ToolStripMenuItem _muteHour = new("Mute all for 1 hour");
    private readonly System.Windows.Threading.DispatcherTimer _tooltipTimer = new() { Interval = TimeSpan.FromSeconds(30) };

    public TrayIcon(AppController app, App shell)
    {
        _app = app;
        _shell = shell;

        var menu = new ContextMenuStrip
        {
            Renderer = new MidnightMenuRenderer(),
            Font = new Drawing.Font("Segoe UI", 9.75f),
            ShowImageMargin = false,
            ShowCheckMargin = true,
            Padding = new Padding(4),
        };
        _next.Font = new Drawing.Font("Segoe UI Semibold", 9.75f);
        menu.Items.Add(_next);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open OptimAdhan", null, (_, _) => _shell.ShowMainWindow());
        menu.Items.Add(_widget);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_stop);
        menu.Items.Add(_skipNext);
        menu.Items.Add(_muteHour);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings", null, (_, _) => _shell.ShowMainWindow(MainWindow.Page.Settings));
        menu.Items.Add("Exit", null, (_, _) => _shell.ExitApplication());
        menu.Opening += (_, _) => UpdateMenu();

        _widget.Click += (_, _) => _shell.SetWidgetVisible(!_app.Settings.Widget.Visible);
        _stop.Click += (_, _) => _app.StopAudio();
        _skipNext.Click += (_, _) => _app.ToggleSkipNextAdhan();
        _muteHour.Click += (_, _) =>
            _app.MuteFor(_app.MutedUntilUtc is { } u && u > DateTime.UtcNow ? null : TimeSpan.FromHours(1));

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = AppInfo.ProductName,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                _shell.ShowMainWindow();
            }
        };

        // Follow the taskbar's light/dark setting so the crescent always stands out.
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        _tooltipTimer.Tick += (_, _) => UpdateTooltip();
        _tooltipTimer.Start();
        _app.SettingsChanged += (_, _) => UpdateTooltip();
        UpdateTooltip();
    }

    public void ShowNotification(string title, string body) =>
        _icon.ShowBalloonTip(8000, title, string.IsNullOrEmpty(body) ? " " : body, ToolTipIcon.None);

    public void Dispose()
    {
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _tooltipTimer.Stop();
        _icon.Visible = false;
        _icon.Dispose();
    }

    private void OnUserPreferenceChanged(object? sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (e.Category == Microsoft.Win32.UserPreferenceCategory.General)
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => _icon.Icon = LoadIcon());
        }
    }

    /// <summary>The brand's tray crescent: gold on a dark taskbar, navy on a light one.</summary>
    private static Drawing.Icon LoadIcon()
    {
        var file = TaskbarUsesLightTheme() ? "tray-dark.ico" : "tray-gold.ico";
        var stream = System.Windows.Application.GetResourceStream(
            new Uri($"pack://application:,,,/Assets/Brand/{file}")).Stream;
        return new Drawing.Icon(stream, SystemInformation.SmallIconSize);
    }

    private static bool TaskbarUsesLightTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
        }
        catch
        {
            return false;
        }
    }

    private string? NextPrayerText()
    {
        if (_app.Schedule?.NextPrayer(DateTime.UtcNow) is not { } next)
        {
            return null;
        }

        var left = next.Utc - DateTime.UtcNow;
        return $"{PrayerNames.English(next.Prayer)} at {Format.Time(next.Time, _app.Settings.Use24HourClock)} (in {Format.ShortCountdown(left)})";
    }

    private void UpdateTooltip()
    {
        var text = NextPrayerText() is { } next ? $"{AppInfo.ProductName}\n{next}" : $"{AppInfo.ProductName} — set your location";
        _icon.Text = text.Length > 127 ? text[..127] : text;
    }

    private void UpdateMenu()
    {
        _next.Text = NextPrayerText() ?? "Set your location to begin";
        _widget.Checked = _app.Settings.Widget.Visible;
        _widget.Enabled = _app.Schedule is not null;
        _stop.Enabled = _app.Audio.IsPlaying;
        _skipNext.Checked = _app.IsNextAdhanSkipped;
        _skipNext.Enabled = _app.Settings.Adhan.Enabled && _app.Schedule is not null;
        var muted = _app.MutedUntilUtc is { } until && until > DateTime.UtcNow;
        _muteHour.Text = muted ? "Unmute" : "Mute all for 1 hour";
    }
}

/// <summary>Midnight-and-gold styling for the tray menu (WinForms otherwise draws it light grey).</summary>
internal sealed class MidnightMenuRenderer : ToolStripProfessionalRenderer
{
    private static readonly Drawing.Color Background = Drawing.Color.FromArgb(0x0E, 0x1D, 0x3A);
    private static readonly Drawing.Color Hover = Drawing.Color.FromArgb(0x1E, 0x33, 0x5C);
    private static readonly Drawing.Color Border = Drawing.Color.FromArgb(0x2E, 0x43, 0x6A);
    private static readonly Drawing.Color Text = Drawing.Color.FromArgb(0xF6, 0xF1, 0xE7);
    private static readonly Drawing.Color Muted = Drawing.Color.FromArgb(0x8E, 0x96, 0xA6);
    private static readonly Drawing.Color Gold = Drawing.Color.FromArgb(0xFF, 0xD6, 0x5A);

    public MidnightMenuRenderer()
        : base(new MidnightColors())
    {
        RoundedEdges = true;
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) => e.Graphics.Clear(Background);

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Drawing.Pen(Border);
        e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item.Selected && e.Item.Enabled)
        {
            using var brush = new Drawing.SolidBrush(Hover);
            e.Graphics.FillRectangle(brush, new Drawing.Rectangle(2, 0, e.Item.Width - 4, e.Item.Height));
        }
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        // The disabled first item is the "next prayer" header: gold, not greyed out.
        e.TextColor = e.Item.Owner?.Items.IndexOf(e.Item) == 0 ? Gold : e.Item.Enabled ? Text : Muted;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var r = e.ImageRectangle;
        using var pen = new Drawing.Pen(Gold, 2f);
        e.Graphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.DrawLines(pen, [
            new Drawing.PointF(r.Left + r.Width * 0.2f, r.Top + r.Height * 0.55f),
            new Drawing.PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.75f),
            new Drawing.PointF(r.Left + r.Width * 0.82f, r.Top + r.Height * 0.3f),
        ]);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Drawing.Pen(Border);
        var y = e.Item.Height / 2;
        e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
    }

    private sealed class MidnightColors : ProfessionalColorTable
    {
        public override Drawing.Color ToolStripDropDownBackground => Background;

        public override Drawing.Color ImageMarginGradientBegin => Background;

        public override Drawing.Color ImageMarginGradientMiddle => Background;

        public override Drawing.Color ImageMarginGradientEnd => Background;

        public override Drawing.Color MenuBorder => Border;

        public override Drawing.Color MenuItemSelected => Hover;

        public override Drawing.Color MenuItemBorder => Hover;

        public override Drawing.Color CheckBackground => Background;

        public override Drawing.Color CheckSelectedBackground => Hover;
    }
}

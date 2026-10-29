using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using OptimAdhan.App.Services;
using OptimAdhan.App.ViewModels;
using OptimAdhan.Core.Settings;
using MainPage = OptimAdhan.App.Views.MainWindow.Page;

namespace OptimAdhan.App.Views;

/// <summary>
/// Desktop widget: a floating frosted-glass panel. Purely a view; closing it
/// never affects the Adhan scheduler. Windows 11 composites the acrylic blur
/// on the GPU; the app only redraws text when a value changes (about once a
/// minute, every second only during the final hour before a prayer).
/// </summary>
public partial class WidgetWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    private readonly AppController _app;
    private readonly Dashboard _dashboard;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _hasBackdrop;
    private WidgetLayout? _shownLayout;

    public WidgetWindow(AppController app)
    {
        InitializeComponent();
        _app = app;
        _dashboard = new Dashboard(app);
        DataContext = _dashboard;
        _dashboard.PropertyChanged += OnDashboardChanged;

        _clock.Tick += (_, _) => RefreshAndReschedule();
        _clock.Start();
        _app.SettingsChanged += OnSettingsChanged;

        SourceInitialized += (_, _) => ApplyMaterial();
        ProgressTrack.SizeChanged += (_, _) => UpdateProgress();
        CompactProgressTrack.SizeChanged += (_, _) => UpdateProgress();
        Loaded += (_, _) =>
        {
            ApplySettings();
            UpdateProgress();
            PlaceInitially();
        };
        Closing += (_, _) =>
        {
            // Closed from outside the app (for example Alt+F4): treat it as "hide" so settings stay truthful.
            if (!ClosingByApp && _app.Settings.Widget.Visible)
            {
                Dispatcher.BeginInvoke(() => _app.Update(s => s with { Widget = s.Widget with { Visible = false } }));
            }
        };
        Closed += (_, _) =>
        {
            _clock.Stop();
            _app.SettingsChanged -= OnSettingsChanged;
            _dashboard.PropertyChanged -= OnDashboardChanged;
        };
    }

    /// <summary>Set by the app when it closes the widget itself (hide or exit).</summary>
    public bool ClosingByApp { get; set; }

    private void ApplyMaterial()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        // Keep it out of Alt+Tab and never steal focus from what the user is doing.
        SetWindowLong(hwnd, GwlExStyle, GetWindowLong(hwnd, GwlExStyle) | WsExToolWindow | WsExNoActivate);
        WindowEffects.UseDarkFrame(hwnd);
        WindowEffects.RoundCorners(hwnd);
        WindowEffects.SetBorderColor(hwnd, Color.FromRgb(0x2A, 0x3C, 0x58));
        _hasBackdrop = WindowEffects.TrySetBackdrop(this, WindowEffects.Backdrop.Acrylic);
        if (_hasBackdrop)
        {
            WindowEffects.KeepVisuallyActive(this);
        }
        else
        {
            // Windows 10, or transparency effects turned off: an opaque midnight panel.
            HwndSource.FromHwnd(hwnd)!.CompositionTarget.BackgroundColor = Color.FromRgb(0x07, 0x14, 0x2D);
        }
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        ApplySettings();
        RefreshAndReschedule();
    }

    /// <summary>
    /// Everything the widget shows changes once a minute, except the countdowns in
    /// their final hour (they gain seconds). So it wakes on the next minute boundary,
    /// or every second only when a seconds countdown is on screen.
    /// </summary>
    private void RefreshAndReschedule()
    {
        _dashboard.Refresh();
        var showsSeconds = _dashboard.NextCountdownCompact.Contains(':')
            || (_dashboard.IsRamadan && _dashboard.FastCountdownCompact.Contains(':'));
        var now = DateTime.Now;
        _clock.Interval = showsSeconds
            ? TimeSpan.FromSeconds(1)
            : TimeSpan.FromMilliseconds(60_000 - (now.Second * 1000 + now.Millisecond) + 30);
    }

    private void OnDashboardChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Dashboard.Progress))
        {
            UpdateProgress();
        }
        else if (e.PropertyName == nameof(Dashboard.IsRamadan))
        {
            InfoFooter.Visibility = _dashboard.IsRamadan ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void UpdateProgress()
    {
        ProgressFill.Width = Math.Max(0, ProgressTrack.ActualWidth * _dashboard.Progress);
        CompactProgressFill.Width = Math.Max(0, CompactProgressTrack.ActualWidth * _dashboard.Progress);
    }

    private void ApplySettings()
    {
        var w = _app.Settings.Widget;
        Topmost = w.AlwaysOnTop;

        // "Glass tint" (0.4–1.0) sets how strongly the panel covers the blurred wallpaper.
        // Even the lightest setting keeps enough tint for readable text on bright wallpapers.
        var alpha = _hasBackdrop ? (byte)Math.Round(255 * (0.52 + 0.44 * Math.Clamp((w.Opacity - 0.4) / 0.6, 0, 1))) : (byte)255;
        TintStart.Color = Color.FromArgb(alpha, 0x0A, 0x2A, 0x30);
        TintMid.Color = Color.FromArgb(alpha, 0x0B, 0x1C, 0x3C);
        TintEnd.Color = Color.FromArgb(alpha, 0x07, 0x14, 0x2D);

        var compact = w.Layout == WidgetLayout.Compact;
        if (_shownLayout != w.Layout)
        {
            StandardLayout.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            CompactLayout.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
            if (_shownLayout is not null && WindowEffects.AnimationsEnabled)
            {
                var target = compact ? CompactLayout : StandardLayout;
                target.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
            }

            _shownLayout = w.Layout;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, UpdateProgress);
        }

        InfoFooter.Visibility = _dashboard.IsRamadan ? Visibility.Collapsed : Visibility.Visible;
        CompactItem.IsChecked = compact;
        TopItem.IsChecked = w.AlwaysOnTop;
        LockItem.IsChecked = w.Locked;
        Root.Cursor = w.Locked ? null : Cursors.SizeAll;
    }

    private void PlaceInitially()
    {
        var w = _app.Settings.Widget;
        var area = SystemParameters.WorkArea;
        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
        var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;

        // Restore the saved position only if it is still on a screen (monitors can change).
        if (w.Left is { } left && w.Top is { } top
            && left >= virtualLeft && top >= virtualTop
            && left + 60 <= virtualRight && top + 60 <= virtualBottom)
        {
            Left = left;
            Top = top;
        }
        else
        {
            Left = area.Right - ActualWidth - 16;
            Top = area.Top + 16;
        }
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ((App)Application.Current).ShowMainWindow();
            return;
        }

        if (_app.Settings.Widget.Locked)
        {
            return;
        }

        DragMove();
        _app.Update(s => s with { Widget = s.Widget with { Left = Math.Round(Left), Top = Math.Round(Top) } });
    }

    private void OnOpen(object sender, RoutedEventArgs e) => ((App)Application.Current).ShowMainWindow();

    private void OnSettings(object sender, RoutedEventArgs e) => ((App)Application.Current).ShowMainWindow(MainPage.Settings);

    private void OnToggleCompact(object sender, RoutedEventArgs e) =>
        _app.Update(s => s with { Widget = s.Widget with { Layout = CompactItem.IsChecked ? WidgetLayout.Compact : WidgetLayout.Standard } });

    private void OnToggleTop(object sender, RoutedEventArgs e) =>
        _app.Update(s => s with { Widget = s.Widget with { AlwaysOnTop = TopItem.IsChecked } });

    private void OnToggleLock(object sender, RoutedEventArgs e) =>
        _app.Update(s => s with { Widget = s.Widget with { Locked = LockItem.IsChecked } });

    private void OnHide(object sender, RoutedEventArgs e) => ((App)Application.Current).SetWidgetVisible(false);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
}

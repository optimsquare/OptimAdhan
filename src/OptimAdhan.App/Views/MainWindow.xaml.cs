using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using System.Windows.Threading;
using OptimAdhan.App.Controls;
using OptimAdhan.App.Services;
using OptimAdhan.App.ViewModels;
using OptimAdhan.Core.Calculation;
using OptimAdhan.Core.Hijri;
using OptimAdhan.Core.Location;
using OptimAdhan.Core.Scheduling;
using OptimAdhan.Core.Settings;

namespace OptimAdhan.App.Views;

public sealed record Option<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>One prayer's row of Adhan settings, plus its reminder in the Notifications card.</summary>
public sealed record PrayerAdhanControls(CheckBox Adhan, ComboBox Sound, Button Preview, ComboBox Reminder);

public sealed record DayRow(string Dow, int DayNumber, string HijriText, string Fajr, string Sunrise, string Dhuhr,
    string Asr, string Maghrib, string Isha, bool IsToday, string Tag, bool IsMonthStart)
{
    public bool HasTag => Tag.Length > 0;
}

/// <summary>One day of the dashboard timetable; Times follow the header of its kind (see DayRowTemplate).</summary>
public sealed record DayTableRow(string Dow, string DateText, string Tag, bool IsToday, bool IsRamadan, string[] Times)
{
    public bool HasTag => Tag.Length > 0;
}

public sealed record RamadanRow(int Day, string DateText, string Sehri, string Fajr, string Dhuhr,
    string Asr, string Iftar, string Isha, bool IsToday, Brush DayBadge);

public partial class MainWindow : Window
{
    public enum Page
    {
        Today,
        Timetable,
        Ramadan,
        Settings,
        About,
    }

    private static readonly int[] ReminderChoices = [0, 5, 10, 15, 20, 30];

    private const double ExpandedSidebarWidth = 236;
    private const double RailWidth = 72;

    /// <summary>Below this window width an expanded sidebar shows as the icon rail (the user's choice is kept).</summary>
    private const double AutoCollapseWidth = 1000;

    /// <summary>Largest uniform scale for Today on big and full-screen displays.</summary>
    private const double MaxTodayScale = 1.5;

    /// <summary>The day timetable card: one row (DayRowTemplate height) and everything around the rows.</summary>
    private const double DayRowHeight = 32;
    private const double DayTableChrome = 108;

    /// <summary>After browsing, the timetable returns to today by itself (it is often left on a wall or second screen).</summary>
    private static readonly TimeSpan BrowseTimeout = TimeSpan.FromMinutes(3);
    private static readonly Color FrameBorder = Color.FromRgb(0x1C, 0x2C, 0x4D);

    private static readonly Brush[] AshraBadges =
    [
        Frozen(Color.FromArgb(0x24, 0xE9, 0xCF, 0x96)),
        Frozen(Color.FromArgb(0x3D, 0xE9, 0xCF, 0x96)),
        Frozen(Color.FromArgb(0x5C, 0xE9, 0xCF, 0x96)),
    ];

    private readonly AppController _app;
    private readonly Dashboard _dashboard;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Dictionary<Prayer, PrayerAdhanControls> _perPrayer = [];
    private readonly Dictionary<Prayer, TextBox> _adjustments = [];
    private IReadOnlyList<RamadanRow> _ramadanRows = [];
    private UniformGrid? _cardsGrid;
    private DateOnly _month;
    private bool _loading;
    private CancellationTokenSource? _locationCts;
    private readonly WindowChrome _windowedChrome;
    private readonly DispatcherTimer _fullScreenBarTimer = new() { Interval = TimeSpan.FromSeconds(2.5) };

    /// <summary>Saves a typed minute offset or angle once the user pauses, so Today updates without leaving the box.</summary>
    private readonly DispatcherTimer _numberCommit = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private bool _committingWhileTyping;
    private Page _page = Page.Today;
    private NavigationMode _navigation;
    private NavigationMode _lastVisibleNavigation;
    private NavigationMode? _shownNavigation;
    private bool _expandedWhileNarrow;
    private bool _fullScreen;
    private Rect _windowedBounds;
    private WindowState _windowedState;
    private Page _pageBeforeFullScreen;
    private (DateOnly Today, int Offset, PrayerScheduleService Schedule, bool Use24)? _dayTableKey;
    private TimetableWindow? _dayTable;
    private IReadOnlyList<DayTableRow> _dayTableRows = [];
    private int _dayTableOffset;
    private int _dayTableShown;
    private DateTime _dayTableBrowsedUtc;

    public MainWindow(AppController app)
    {
        InitializeComponent();
        _app = app;
        _dashboard = new Dashboard(app);
        DataContext = _dashboard;

        _month = app.Schedule is { } s ? s.LocalDate(DateTime.UtcNow) : DateOnly.FromDateTime(DateTime.Today);
        _month = new DateOnly(_month.Year, _month.Month, 1);

        _windowedChrome = WindowChrome.GetWindowChrome(this);
        _navigation = app.Settings.MainWindow.Navigation;
        _lastVisibleNavigation = _navigation == NavigationMode.Hidden ? NavigationMode.Expanded : _navigation;
        RestorePlacement(app.Settings.MainWindow);

        BuildSettingsControls();
        LoadSettings();
        RefreshAll();
        ApplyNavigation(animate: false);

        SourceInitialized += (_, _) => ApplyMaterial();
        StateChanged += (_, _) => UpdateMaximizeState();
        SizeChanged += (_, _) => OnWindowSizeChanged();
        DpiChanged += (_, _) => OnDisplayChanged();
        _clock.Tick += (_, _) => Tick();
        _clock.Start();
        _fullScreenBarTimer.Tick += (_, _) => FadeFullScreenBar();
        _app.SettingsChanged += OnSettingsChanged;
        _app.AudioStateChanged += OnAudioStateChanged;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        Closing += (_, _) =>
        {
            if (_numberCommit.IsEnabled)
            {
                CommitNumbers(); // a value typed just before closing still counts
            }

            SavePlacement();
        };
        Closed += (_, _) =>
        {
            _clock.Stop();
            _fullScreenBarTimer.Stop();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            _locationCts?.Cancel();
            _app.SettingsChanged -= OnSettingsChanged;
            _app.AudioStateChanged -= OnAudioStateChanged;
        };
        VersionRun.Text = $"Version {AppInfo.Version}";
    }

    public void Navigate(Page page)
    {
        if (_fullScreen && page != Page.Today)
        {
            // Full screen is the Today dashboard; anything else returns to the window first.
            _pageBeforeFullScreen = page;
            ExitFullScreen();
            return;
        }

        var nav = page switch
        {
            Page.Timetable => NavMonthly,
            Page.Ramadan => NavRamadan,
            Page.Settings => NavSettings,
            Page.About => NavAbout,
            _ => NavToday,
        };
        if (nav.IsChecked == true)
        {
            ShowPage(page);
        }
        else
        {
            nav.IsChecked = true; // raises OnNav
        }
    }

    // ───────────────────────── Shell ─────────────────────────

    private void ApplyMaterial()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        WindowEffects.UseDarkFrame(hwnd);
        WindowEffects.SetBorderColor(hwnd, FrameBorder);

        // The main window is solid midnight with soft painted light (no system backdrop):
        // identical on Windows 10 and 11, and no native caption buttons bleeding through.
    }

    private void Tick()
    {
        if (WindowState == WindowState.Minimized || !IsVisible)
        {
            return;
        }

        _dashboard.Refresh();
        RefreshDayTable();
        MuteNextButton.Content = _app.IsNextAdhanSkipped ? "Unmute next Adhan" : "Mute next Adhan";
        RamadanTeaserPanel.Visibility = _dashboard.IsRamadan ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnNav(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized)
        {
            return;
        }

        ShowPage(sender switch
        {
            _ when sender == NavMonthly => Page.Timetable,
            _ when sender == NavRamadan => Page.Ramadan,
            _ when sender == NavSettings => Page.Settings,
            _ when sender == NavAbout => Page.About,
            _ => Page.Today,
        });
    }

    private void ShowPage(Page page)
    {
        _page = page;
        FrameworkElement target = page switch
        {
            Page.Timetable => MonthlyPage,
            Page.Ramadan => RamadanPage,
            Page.Settings => SettingsPage,
            Page.About => AboutPage,
            _ => TodayPage,
        };

        foreach (UIElement child in Pages.Children)
        {
            child.Visibility = child == target ? Visibility.Visible : Visibility.Collapsed;
        }

        if (page == Page.Timetable)
        {
            RefreshTimetable();
        }
        else if (page == Page.Ramadan)
        {
            RefreshRamadan();
        }

        // A short fade-and-rise, skipped when Windows animations are turned off.
        if (WindowEffects.AnimationsEnabled)
        {
            var shift = new TranslateTransform(0, 10);
            target.RenderTransform = shift;
            var duration = TimeSpan.FromMilliseconds(220);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            target.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, duration) { EasingFunction = ease });
        }
    }

    private void UpdateMaximizeState()
    {
        if (_fullScreen && WindowState == WindowState.Maximized)
        {
            // Win+Up or a title-bar double-click from full screen: become an ordinary maximized window.
            _windowedState = WindowState.Maximized;
            ExitFullScreen();
            return;
        }

        // Maximized WindowChrome windows overhang the screen by the frame width.
        RootBorder.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        MaxIcon.Data = Geometry.Parse(WindowState == WindowState.Maximized
            ? "M2.5,0.5 H9.5 V7.5 M0.5,2.5 H7.5 V9.5 H0.5 Z"
            : "M0.5,0.5 H9.5 V9.5 H0.5 Z");
        AutomationProperties.SetName(MaxButton, WindowState == WindowState.Maximized ? "Restore" : "Maximize");
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void OnMaximize(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(this);
        }
        else
        {
            SystemCommands.MaximizeWindow(this);
        }
    }

    private void OnCloseWindow(object sender, RoutedEventArgs e) => Close();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return; // an open drop-down or text box used the key
        }

        var modifiers = Keyboard.Modifiers;
        if (e.Key == Key.F11 && modifiers == ModifierKeys.None)
        {
            ToggleFullScreen();
        }
        else if (e.Key == Key.Escape && _fullScreen)
        {
            ExitFullScreen();
        }
        else if (e.Key == Key.B && modifiers == ModifierKeys.Control)
        {
            ToggleSidebar();
        }
        else if (e.Key is >= Key.D1 and <= Key.D5 && modifiers == ModifierKeys.Control)
        {
            Navigate((Page)(e.Key - Key.D1)); // Ctrl+1…5 follow the sidebar order
        }
        else
        {
            return;
        }

        e.Handled = true;
    }

    // ───────────────────────── Window placement ─────────────────────────

    private void RestorePlacement(MainWindowSettings saved)
    {
        if (saved is { Left: { } left, Top: { } top, Width: { } width, Height: { } height }
            && MonitorPlacement.IsOnScreen(left, top, width))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
            Width = Math.Max(width, MinWidth);
            Height = Math.Max(height, MinHeight);
        }

        if (saved.Maximized)
        {
            WindowState = WindowState.Maximized;
            UpdateMaximizeState();
        }
    }

    /// <summary>Remembers the windowed size and maximized state. Full screen itself is not restored at start-up.</summary>
    private void SavePlacement()
    {
        var bounds = _fullScreen ? _windowedBounds : RestoreBounds;
        if (bounds.IsEmpty || bounds.Width < 1)
        {
            return;
        }

        var maximized = _fullScreen ? _windowedState == WindowState.Maximized : WindowState == WindowState.Maximized;
        _app.Update(s => s with
        {
            MainWindow = s.MainWindow with
            {
                Left = Math.Round(bounds.Left),
                Top = Math.Round(bounds.Top),
                Width = Math.Round(bounds.Width),
                Height = Math.Round(bounds.Height),
                Maximized = maximized,
            },
        });
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(OnDisplayChanged);

    /// <summary>Monitor, resolution or scaling changed: keep full screen covering the monitor exactly.</summary>
    private void OnDisplayChanged()
    {
        if (_fullScreen)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                if (_fullScreen)
                {
                    MonitorPlacement.Cover(this, MonitorPlacement.MonitorOf(this));
                }
            });
        }
    }

    // ───────────────────────── Sidebar: expanded, icon rail, hidden ─────────────────────────

    private bool IsNarrow => ActualWidth > 0 && ActualWidth < AutoCollapseWidth;

    /// <summary>What the sidebar shows now: the user's choice, folded to the rail in narrow windows, gone in full screen.</summary>
    private NavigationMode ShownNavigation =>
        _fullScreen ? NavigationMode.Hidden
        : _navigation == NavigationMode.Expanded && IsNarrow && !_expandedWhileNarrow ? NavigationMode.Collapsed
        : _navigation;

    private void SetNavigation(NavigationMode mode)
    {
        if (mode != NavigationMode.Hidden)
        {
            _lastVisibleNavigation = mode;
        }

        _expandedWhileNarrow = mode == NavigationMode.Expanded && IsNarrow;
        if (mode != _navigation)
        {
            _navigation = mode;
            _app.Update(s => s with { MainWindow = s.MainWindow with { Navigation = mode } });
        }

        ApplyNavigation(animate: true);
    }

    private void ApplyNavigation(bool animate)
    {
        var mode = ShownNavigation;
        if (mode == _shownNavigation)
        {
            return;
        }

        var sidebarHadFocus = Sidebar.IsKeyboardFocusWithin;
        _shownNavigation = mode;
        var compact = mode != NavigationMode.Expanded;
        Shell.SetIsCompact(Sidebar, compact);
        BrandText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        Brand.Margin = compact ? new Thickness(2, 6, 0, 22) : new Thickness(4, 6, 0, 22);
        QuickToggles.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        RailToggles.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        HiddenNavBar.Visibility = mode == NavigationMode.Hidden && !_fullScreen ? Visibility.Visible : Visibility.Collapsed;
        var paneAction = mode == NavigationMode.Expanded ? "Collapse navigation" : "Expand navigation";
        PaneButton.ToolTip = paneAction;
        AutomationProperties.SetName(PaneButton, paneAction);

        var width = mode switch
        {
            NavigationMode.Expanded => ExpandedSidebarWidth,
            NavigationMode.Collapsed => RailWidth,
            _ => 0,
        };
        Sidebar.Visibility = Visibility.Visible;
        if (animate && IsLoaded && WindowEffects.AnimationsEnabled)
        {
            var slide = new DoubleAnimation(Sidebar.ActualWidth, width, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            slide.Completed += (_, _) => SettleSidebar(mode, width);
            Sidebar.BeginAnimation(WidthProperty, slide);
        }
        else
        {
            SettleSidebar(mode, width);
        }

        if (sidebarHadFocus && mode == NavigationMode.Hidden && !_fullScreen)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => RestorePaneButton.Focus());
        }
    }

    private void SettleSidebar(NavigationMode mode, double width)
    {
        if (_shownNavigation != mode)
        {
            return; // a newer change took over mid-slide
        }

        Sidebar.BeginAnimation(WidthProperty, null);
        Sidebar.Width = width;
        Sidebar.Visibility = width > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnWindowSizeChanged()
    {
        if (!IsNarrow)
        {
            _expandedWhileNarrow = false;
        }

        ApplyNavigation(animate: true);
    }

    private void OnPaneButton(object sender, RoutedEventArgs e) =>
        SetNavigation(ShownNavigation == NavigationMode.Expanded ? NavigationMode.Collapsed : NavigationMode.Expanded);

    private void OnRestoreSidebar(object sender, RoutedEventArgs e) => ShowSidebar();

    private void ShowSidebar()
    {
        SetNavigation(_lastVisibleNavigation);
        var selected = NavPanel.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true) ?? NavToday;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => selected.Focus());
    }

    /// <summary>Ctrl+B: hide the sidebar, or bring back whichever form it had.</summary>
    private void ToggleSidebar()
    {
        if (_fullScreen)
        {
            return;
        }

        if (_navigation == NavigationMode.Hidden)
        {
            ShowSidebar();
        }
        else
        {
            SetNavigation(NavigationMode.Hidden);
        }
    }

    private void OnViewMenu(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = ViewButton, Placement = PlacementMode.Bottom };
        menu.Items.Add(NavigationMenuItem("Expanded sidebar", NavigationMode.Expanded, ""));
        menu.Items.Add(NavigationMenuItem("Icons only", NavigationMode.Collapsed, ""));
        menu.Items.Add(NavigationMenuItem("Hide sidebar", NavigationMode.Hidden, "Ctrl+B"));
        menu.Items.Add(new Separator());
        var fullScreen = new MenuItem { Header = "Full-screen dashboard", InputGestureText = "F11" };
        fullScreen.Click += (_, _) => EnterFullScreen();
        menu.Items.Add(fullScreen);
        menu.IsOpen = true;
    }

    private MenuItem NavigationMenuItem(string header, NavigationMode mode, string gesture)
    {
        var item = new MenuItem { Header = header, InputGestureText = gesture, IsCheckable = true, IsChecked = _navigation == mode };
        item.Click += (_, _) => SetNavigation(mode);
        return item;
    }

    // ───────────────────────── Full screen ─────────────────────────

    private void OnToggleFullScreen(object sender, RoutedEventArgs e) => ToggleFullScreen();

    private void ToggleFullScreen()
    {
        if (_fullScreen)
        {
            ExitFullScreen();
        }
        else
        {
            EnterFullScreen();
        }
    }

    /// <summary>
    /// The Today dashboard alone, covering the monitor the window is on (taskbar included).
    /// It is an ordinary window sized to the monitor, never topmost, so Adhan alerts still appear above it.
    /// </summary>
    private void EnterFullScreen()
    {
        if (_fullScreen)
        {
            return;
        }

        var monitor = MonitorPlacement.MonitorOf(this);
        _windowedBounds = RestoreBounds;
        _windowedState = WindowState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
        _pageBeforeFullScreen = _page;
        _fullScreen = true;

        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(0),
            GlassFrameThickness = new Thickness(0),
            UseAeroCaptionButtons = false,
            CornerRadius = new CornerRadius(0),
        });
        WindowState = WindowState.Normal;
        UpdateMaximizeState();
        TitleRow.Height = new GridLength(0);
        TitleBar.Visibility = Visibility.Collapsed;
        TodayContent.Margin = new Thickness(48, 36, 48, 36);
        Navigate(Page.Today);
        ApplyNavigation(animate: false);
        WindowEffects.SetEdgeToEdge(new WindowInteropHelper(this).Handle, true, FrameBorder);
        MonitorPlacement.Cover(this, monitor);
        FullScreenBar.Visibility = Visibility.Visible;
        ShowFullScreenBar();
        ApplyTodayLayout();
    }

    private void ExitFullScreen()
    {
        if (!_fullScreen)
        {
            return;
        }

        _fullScreen = false;
        _fullScreenBarTimer.Stop();
        FullScreenBar.Visibility = Visibility.Collapsed;
        Cursor = null;
        WindowChrome.SetWindowChrome(this, _windowedChrome);
        WindowEffects.SetEdgeToEdge(new WindowInteropHelper(this).Handle, false, FrameBorder);
        TitleRow.Height = new GridLength(44);
        TitleBar.Visibility = Visibility.Visible;
        TodayContent.Margin = new Thickness(32, 4, 32, 32);
        if (WindowState == WindowState.Normal)
        {
            Left = _windowedBounds.Left;
            Top = _windowedBounds.Top;
            Width = _windowedBounds.Width;
            Height = _windowedBounds.Height;
        }

        if (_windowedState == WindowState.Maximized)
        {
            WindowState = WindowState.Maximized;
        }

        UpdateMaximizeState();
        ApplyNavigation(animate: false);
        Navigate(_pageBeforeFullScreen);
        ApplyTodayLayout();
    }

    private void ShowFullScreenBar()
    {
        FullScreenBar.BeginAnimation(OpacityProperty, null);
        FullScreenBar.Opacity = 1;
        Cursor = null;
        _fullScreenBarTimer.Stop();
        _fullScreenBarTimer.Start();
    }

    /// <summary>After a moment without the mouse, the exit button and pointer fade so nothing sits over the dashboard.</summary>
    private void FadeFullScreenBar()
    {
        _fullScreenBarTimer.Stop();
        if (!_fullScreen || FullScreenBar.IsMouseOver || FullScreenBar.IsKeyboardFocusWithin)
        {
            if (_fullScreen)
            {
                _fullScreenBarTimer.Start();
            }

            return;
        }

        FullScreenBar.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(WindowEffects.AnimationsEnabled ? 400 : 0)));
        Cursor = Cursors.None;
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        if (_fullScreen)
        {
            ShowFullScreenBar();
        }
    }

    // ───────────────────────── Responsive Today ─────────────────────────

    private void OnTodayViewportChanged(object sender, SizeChangedEventArgs e) => ApplyTodayLayout();

    private void OnCardsGridLoaded(object sender, RoutedEventArgs e)
    {
        _cardsGrid = (UniformGrid)sender;
        ApplyTodayLayout();
    }

    /// <summary>
    /// Fits Today to its space. Narrow windows re-flow (side cards under the hero, prayer cards in
    /// two rows, hero stacked); tall windows add the week ahead; large and full-screen displays
    /// scale the whole dashboard uniformly, capped, centred at its design width. Runs on resize only.
    /// </summary>
    private void ApplyTodayLayout()
    {
        var width = TodayPage.ActualWidth;
        var height = TodayPage.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return; // not on screen; runs again when it is
        }

        var margin = TodayContent.Margin;
        var designWidth = TodayContent.MaxWidth + margin.Left + margin.Right;
        var layoutWidth = Math.Min(width, designWidth);
        if (Reflow(layoutWidth - margin.Left - margin.Right))
        {
            // Measurements are only trustworthy after the re-flow has been laid out once.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, ApplyTodayLayout);
        }

        // The day timetable is extra: as many days as fit without scrolling, or none if fewer than three
        // would (fewer is fine only when that is all that is left, on the last days of Ramadan).
        // Rows have a fixed height, so the table is sized arithmetically rather than by trial measuring.
        DayTableCard.Visibility = Visibility.Collapsed;
        var natural = NaturalTodayHeight(layoutWidth);
        var fit = (int)Math.Floor((height - natural - DayTableChrome) / DayRowHeight);
        var days = _dashboard.HasSchedule ? Math.Min(fit, _dayTableRows.Count) : 0;
        if (days > 0 && days >= Math.Min(3, _dayTableRows.Count))
        {
            ShowDayTable(days);
            natural += DayTableChrome + days * DayRowHeight;
        }

        var scale = Math.Clamp(Math.Min(width / designWidth, height / natural), 1, MaxTodayScale);
        if (Math.Abs(scale - TodayScale.ScaleX) > 0.01)
        {
            TodayScale.ScaleX = TodayScale.ScaleY = scale;
        }

        TodayScaleHost.VerticalAlignment = _fullScreen ? VerticalAlignment.Center : VerticalAlignment.Top;
    }

    private void ShowDayTable(int days)
    {
        DayTableCard.Visibility = Visibility.Visible;
        if (_dayTable is not { } table || (DayTableList.ItemsSource as ICollection<DayTableRow>)?.Count == days)
        {
            return;
        }

        _dayTableShown = days;
        DayTableList.ItemsSource = _dayTableRows.Take(days).ToList();
        var culture = CultureInfo.CurrentCulture;
        var first = table.Days[0];
        var last = table.Days[days - 1];
        if (table.Ramadan is { } ramadan)
        {
            DayTableTitle.Text = $"RAMADAN {ramadan.HijriYear} IMSAKIA";
            DayTableRange.Text = first.RamadanDay == last.RamadanDay ? $"Day {first.RamadanDay} of {ramadan.Days}"
                : $"Days {first.RamadanDay}–{last.RamadanDay} of {ramadan.Days}";
        }
        else
        {
            DayTableTitle.Text = table.IsAtToday ? $"NEXT {days} DAYS" : "PRAYER TIMES";
            DayTableRange.Text = $"{first.Date.ToString("ddd d MMM", culture)} – {last.Date.ToString("ddd d MMM", culture)}";
        }

        DayTableEarlier.IsEnabled = table.HasEarlier;
        DayTableLater.IsEnabled = table.HasLater(days);
        DayTableToday.Visibility = table.IsAtToday ? Visibility.Collapsed : Visibility.Visible;
    }

    private double NaturalTodayHeight(double width)
    {
        TodayContent.Measure(new Size(width, double.PositiveInfinity));
        return TodayContent.DesiredSize.Height;
    }

    /// <summary>Arranges Today for its width; true when anything moved.</summary>
    private bool Reflow(double contentWidth)
    {
        var stackSide = contentWidth < 840;
        var stackHero = contentWidth < 600;
        var columns = contentWidth < 700 ? 3 : 6;
        var changed = Grid.GetRow(SidePanel) != (stackSide ? 1 : 0)
            || Grid.GetRow(HeroText) != (stackHero ? 1 : 0)
            || (_cardsGrid is not null && _cardsGrid.Columns != columns);

        SideColumn.Width = stackSide ? new GridLength(0) : new GridLength(300);
        Grid.SetColumn(SidePanel, stackSide ? 0 : 1);
        Grid.SetRow(SidePanel, stackSide ? 1 : 0);
        SidePanel.Margin = stackSide ? new Thickness(0, 14, 0, 0) : new Thickness(16, 0, 0, 0);

        Grid.SetColumn(HeroText, stackHero ? 0 : 1);
        Grid.SetRow(HeroText, stackHero ? 1 : 0);
        Grid.SetColumnSpan(HeroText, stackHero ? 2 : 1);
        HeroText.Margin = stackHero ? new Thickness(0, 22, 0, 0) : new Thickness(34, 0, 0, 0);

        if (_cardsGrid is not null)
        {
            _cardsGrid.Columns = columns;
        }

        DayTableLink.Visibility = contentWidth < 760 ? Visibility.Collapsed : Visibility.Visible;
        DayTableRange.Visibility = contentWidth < 640 ? Visibility.Collapsed : Visibility.Visible;
        return changed;
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        LoadSettings();
        RefreshAll();
    }

    private void OnAudioStateChanged(object? sender, EventArgs e) =>
        StopAudioButton.Visibility = _app.Audio.IsPlaying ? Visibility.Visible : Visibility.Collapsed;

    private void RefreshAll()
    {
        Tick();
        ApplyTodayLayout();
        if (MonthlyPage.Visibility == Visibility.Visible)
        {
            RefreshTimetable();
        }
        else if (RamadanPage.Visibility == Visibility.Visible)
        {
            RefreshRamadan();
        }
    }

    private void OnLocationChip(object sender, RoutedEventArgs e) => Navigate(Page.Settings);

    private void OnOpenRamadan(object sender, RoutedEventArgs e) => Navigate(Page.Ramadan);

    private void OnMuteNext(object sender, RoutedEventArgs e) => _app.ToggleSkipNextAdhan();

    private void OnDayTableLink(object sender, RoutedEventArgs e) =>
        Navigate(_dayTable?.Kind == TimetableKind.Ramadan ? Page.Ramadan : Page.Timetable);

    private void OnDayTableEarlier(object sender, RoutedEventArgs e) => BrowseDayTable((_dayTable?.Offset ?? 0) - Math.Max(_dayTableShown, 1));

    private void OnDayTableLater(object sender, RoutedEventArgs e) => BrowseDayTable((_dayTable?.Offset ?? 0) + Math.Max(_dayTableShown, 1));

    private void OnDayTableToday(object sender, RoutedEventArgs e) => BrowseDayTable(0);

    private void BrowseDayTable(int offset)
    {
        _dayTableOffset = offset;
        _dayTableBrowsedUtc = DateTime.UtcNow;
        RefreshDayTable();
        if (offset == 0 && DayTableToday.IsKeyboardFocusWithin)
        {
            DayTableLater.Focus(); // the Back to today button is about to disappear
        }
    }

    // The expanded switches and the icon-rail toggles are two views of the same two settings.
    private void OnSidebarAdhan(object sender, RoutedEventArgs e) =>
        _app.Update(s => s with { Adhan = s.Adhan with { Enabled = ((CheckBox)sender).IsChecked == true } });

    private void OnSidebarWidget(object sender, RoutedEventArgs e) =>
        _app.Update(s => s with { Widget = s.Widget with { Visible = ((CheckBox)sender).IsChecked == true } });

    // ───────────────────────── Timetable ─────────────────────────

    private string T(DateTimeOffset? time) => Format.Time(time, _app.Settings.Use24HourClock);

    /// <summary>
    /// The dashboard timetable: today and the following days, or today and the rest of Ramadan.
    /// "Today" is the prayer location's date, so the table moves on by itself at the location's
    /// midnight. Rebuilt only when that date, the browsing position, the location or the clock format changes.
    /// </summary>
    private void RefreshDayTable()
    {
        if (_app.Schedule is not { } schedule)
        {
            _dayTableKey = null;
            _dayTable = null;
            _dayTableRows = [];
            DayTableList.ItemsSource = null;
            return;
        }

        var utcNow = DateTime.UtcNow;
        if (_dayTableOffset != 0 && utcNow - _dayTableBrowsedUtc > BrowseTimeout)
        {
            _dayTableOffset = 0;
        }

        var key = (schedule.LocalDate(utcNow), _dayTableOffset, schedule, _app.Settings.Use24HourClock);
        if (_dayTableKey == key)
        {
            return;
        }

        _dayTableKey = key;
        var table = DashboardTimetable.Build(schedule, utcNow, _dayTableOffset);
        _dayTable = table;
        var ramadan = table.Kind == TimetableKind.Ramadan;
        var culture = CultureInfo.CurrentCulture;
        _dayTableRows = table.Days.Select(d => new DayTableRow(
            d.Date.ToString("ddd", culture),
            d.Date.ToString("d MMM", culture),
            ramadan ? (d.Date == table.Today ? $"Today · Day {d.RamadanDay}" : $"Day {d.RamadanDay}")
                : d.Date == table.Today ? "Today"
                : d.Date.DayOfWeek == DayOfWeek.Friday ? "Jumu'ah"
                : d.Hijri?.Day == 1 ? d.Hijri.Value.MonthName : "",
            d.Date == table.Today,
            ramadan,
            ramadan
                ? [T(d.SehriEnd), T(d.Get(Prayer.Fajr)), T(d.Get(Prayer.Dhuhr)), T(d.Get(Prayer.Asr)), T(d.Iftar), T(d.Get(Prayer.Isha))]
                : [T(d.Get(Prayer.Fajr)), T(d.Get(Prayer.Sunrise)), T(d.Get(Prayer.Dhuhr)), T(d.Get(Prayer.Asr)), T(d.Get(Prayer.Maghrib)), T(d.Get(Prayer.Isha))]))
            .ToList();

        string[] heads = ramadan
            ? ["SEHRI ENDS", "FAJR", "DHUHR", "ASR", "IFTAR", "ISHA"]
            : ["FAJR", "SUNRISE", "DHUHR", "ASR", "MAGHRIB", "ISHA"];
        TextBlock[] headers = [Head0, Head1, Head2, Head3, Head4, Head5];
        for (var i = 0; i < headers.Length; i++)
        {
            headers[i].Text = heads[i];
        }

        Head0.Foreground = ramadan ? Frozen(Color.FromRgb(0xC9, 0xD8, 0xFF)) : (Brush)FindResource("ChampagneBrush");
        Head4.Foreground = (Brush)FindResource(ramadan ? "GoldBrush" : "ChampagneBrush");
        DayTableLink.Content = ramadan ? "Full Imsakia ›" : "Full month ›";

        DayTableList.ItemsSource = null; // ApplyTodayLayout shows as many of these as fit
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, ApplyTodayLayout);
    }

    private void RefreshTimetable()
    {
        MonthTitle.Text = _month.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
        if (_app.Schedule is not { } schedule)
        {
            MonthGrid.ItemsSource = null;
            MonthSubtitle.Text = "";
            TimetableNote.Text = "Set your location in Settings to see the timetable.";
            return;
        }

        var today = schedule.LocalDate(DateTime.UtcNow);
        var days = schedule.GetMonth(_month.Year, _month.Month);
        var rows = days.Select(d => new DayRow(
            d.Date.ToString("ddd", CultureInfo.CurrentCulture),
            d.Date.Day,
            d.Hijri is { } h ? $"{h.Day} {h.MonthName}" : "",
            T(d.Get(Prayer.Fajr)), T(d.Get(Prayer.Sunrise)), T(d.Get(Prayer.Dhuhr)),
            T(d.Get(Prayer.Asr)), T(d.Get(Prayer.Maghrib)), T(d.Get(Prayer.Isha)),
            d.Date == today,
            d.Date == today ? "Today"
                : d.Date.DayOfWeek == DayOfWeek.Friday ? "Jumu'ah"
                : d.Hijri?.Day == 1 ? d.Hijri.Value.MonthName : "",
            d.Hijri?.Day == 1 && d.Date != today)).ToList();
        MonthGrid.ItemsSource = rows;

        var hijriMonths = days.Where(d => d.Hijri is not null).Select(d => $"{d.Hijri!.Value.MonthName} {d.Hijri.Value.Year}").Distinct().ToList();
        MonthSubtitle.Text = string.Join(" – ", hijriMonths.Count > 1 ? [hijriMonths[0], hijriMonths[^1]] : hijriMonths) + " AH";
        TimetableNote.Text = $"{schedule.Location.DisplayName} · {CalculationMethods.DisplayName(schedule.Parameters.Method)} · times in {schedule.Location.TimeZoneId}";

        if (rows.FirstOrDefault(r => r.IsToday) is { } todayRow)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => MonthGrid.ScrollIntoView(todayRow));
        }
    }

    private void OnPreviousMonth(object sender, RoutedEventArgs e)
    {
        _month = _month.AddMonths(-1);
        RefreshTimetable();
    }

    private void OnNextMonth(object sender, RoutedEventArgs e)
    {
        _month = _month.AddMonths(1);
        RefreshTimetable();
    }

    private void OnThisMonth(object sender, RoutedEventArgs e)
    {
        var today = _app.Schedule?.LocalDate(DateTime.UtcNow) ?? DateOnly.FromDateTime(DateTime.Today);
        _month = new DateOnly(today.Year, today.Month, 1);
        RefreshTimetable();
    }

    // ───────────────────────── Ramadan ─────────────────────────

    private void RefreshRamadan()
    {
        ImsakSummary.Text = _app.Settings.Ramadan.ImsakMinutesBeforeFajr == 0
            ? "At Fajr"
            : $"{_app.Settings.Ramadan.ImsakMinutesBeforeFajr} min before Fajr";

        if (_app.Schedule is not { } schedule)
        {
            RamadanGrid.ItemsSource = null;
            RamadanOverline.Text = "IMSAKIA";
            RamadanTitle.Text = "Ramadan";
            RamadanSubtitle.Text = "Set your location in Settings to see the Ramadan schedule.";
            RamadanStatusLabel.Text = RamadanStatusValue.Text = "";
            return;
        }

        var today = schedule.LocalDate(DateTime.UtcNow);
        var (period, days) = schedule.GetRamadan(today);
        var culture = CultureInfo.CurrentCulture;

        RamadanOverline.Text = $"IMSAKIA · {schedule.Location.DisplayName.ToUpper(culture)}";
        RamadanTitle.Text = $"Ramadan {period.HijriYear} AH";
        RamadanSubtitle.Text = $"{period.FirstDay.ToString("dddd d MMMM", culture)} – {period.LastDay.ToString("dddd d MMMM yyyy", culture)} · {period.Days} days";
        if (period.DayOf(today) is { } dayNumber)
        {
            RamadanStatusLabel.Text = "Today is";
            RamadanStatusValue.Text = $"Day {dayNumber}";
        }
        else
        {
            var until = period.FirstDay.DayNumber - today.DayNumber;
            RamadanStatusLabel.Text = "Begins in";
            RamadanStatusValue.Text = until == 1 ? "1 day" : $"{until} days";
        }

        _ramadanRows = days.Select(d => new RamadanRow(
            d.RamadanDay ?? 0,
            d.Date.ToString("ddd d MMM", culture),
            T(d.SehriEnd), T(d.Get(Prayer.Fajr)), T(d.Get(Prayer.Dhuhr)), T(d.Get(Prayer.Asr)),
            T(d.Iftar), T(d.Get(Prayer.Isha)), d.Date == today,
            AshraBadges[Math.Clamp(((d.RamadanDay ?? 1) - 1) / 10, 0, 2)])).ToList();
        ApplyAshraFilter();
    }

    private void OnAshraFilter(object sender, RoutedEventArgs e)
    {
        if (IsInitialized)
        {
            ApplyAshraFilter();
        }
    }

    private void ApplyAshraFilter()
    {
        var (from, to) = Ashra1.IsChecked == true ? (1, 10)
            : Ashra2.IsChecked == true ? (11, 20)
            : Ashra3.IsChecked == true ? (21, 30)
            : (1, 30);
        var rows = _ramadanRows.Where(r => r.Day >= from && r.Day <= to).ToList();
        RamadanGrid.ItemsSource = rows;
        if (rows.FirstOrDefault(r => r.IsToday) is { } todayRow)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => RamadanGrid.ScrollIntoView(todayRow));
        }
    }

    // ───────────────────────── Settings ─────────────────────────

    private void BuildSettingsControls()
    {
        MethodBox.ItemsSource = CalculationMethods.All
            .Select(m => new Option<CalculationMethod>(m, CalculationMethods.DisplayName(m))).ToList();
        HighLatBox.ItemsSource = new[]
        {
            new Option<HighLatitudeSetting>(HighLatitudeSetting.Automatic, "Automatic (recommended)"),
            new Option<HighLatitudeSetting>(HighLatitudeSetting.MiddleOfTheNight, "Middle of the night"),
            new Option<HighLatitudeSetting>(HighLatitudeSetting.SeventhOfTheNight, "Seventh of the night"),
            new Option<HighLatitudeSetting>(HighLatitudeSetting.TwilightAngle, "Twilight angle"),
        };
        PolarBox.ItemsSource = new[]
        {
            new Option<PolarCircleResolution>(PolarCircleResolution.AqrabBalad, "Nearest latitude (recommended)"),
            new Option<PolarCircleResolution>(PolarCircleResolution.AqrabYaum, "Nearest day"),
            new Option<PolarCircleResolution>(PolarCircleResolution.Unresolved, "Don't adjust"),
        };
        HijriOffsetBox.ItemsSource = Enumerable.Range(-HijriCalendarService.MaxOffset, HijriCalendarService.MaxOffset * 2 + 1)
            .Select(i => new Option<int>(i, i == 0 ? "No change (Umm al-Qura)" : $"{i:+0;-0} day{(Math.Abs(i) == 1 ? "" : "s")}"))
            .ToList();
        ImsakBox.ItemsSource = new[] { 0, 5, 10, 15, 20 }
            .Select(i => new Option<int>(i, i == 0 ? "At Fajr" : $"{i} minutes before Fajr")).ToList();

        foreach (var prayer in new[] { Prayer.Fajr, Prayer.Sunrise, Prayer.Dhuhr, Prayer.Asr, Prayer.Maghrib, Prayer.Isha })
        {
            var box = new TextBox { Width = 56, Margin = new Thickness(0, 4, 10, 0), HorizontalContentAlignment = HorizontalAlignment.Center };
            AutomationProperties.SetName(box, $"{PrayerNames.English(prayer)} adjustment in minutes");
            WireNumberBox(box);
            var cell = new StackPanel();
            cell.Children.Add(new TextBlock { Text = PrayerNames.English(prayer), FontSize = 12, Foreground = (Brush)FindResource("TextSecondaryBrush") });
            cell.Children.Add(box);
            AdjustmentsGrid.Children.Add(cell);
            _adjustments[prayer] = box;
        }

        var reminderOptions = ReminderChoices
            .Select(m => new Option<int>(m, m == 0 ? "No reminder" : $"{m} min before")).ToList();
        var row = 0;
        foreach (var prayer in PrayerScheduleService.FivePrayers)
        {
            var name = PrayerNames.English(prayer);

            // Adhan and sounds: name, recording (Default or a specific one), preview, on/off.
            PerPrayerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = PrayerLabel(prayer);
            var sound = new ComboBox { DisplayMemberPath = nameof(Option<string?>.Label), Margin = new Thickness(0, 6, 0, 6), MinWidth = 150 };
            AutomationProperties.SetName(sound, $"{name} Adhan recording");
            sound.SelectionChanged += OnAdhanChanged;
            var preview = new Button
            {
                Style = (Style)FindResource("IconButton"), Width = 34, Height = 34, Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center, ToolTip = $"Preview the {name} Adhan",
                Content = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M7,4 L20,12 L7,20 Z"), Fill = (Brush)FindResource("GoldBrush"), Width = 10, Height = 12, Stretch = Stretch.Uniform,
                },
            };
            AutomationProperties.SetName(preview, $"Preview {name} Adhan");
            preview.Click += (_, _) => _app.PlayAdhanPreview(PrayerSoundId(prayer));
            var adhan = new CheckBox { VerticalAlignment = VerticalAlignment.Center, ToolTip = $"Play the Adhan at {name}" };
            AutomationProperties.SetName(adhan, $"{name} Adhan");
            adhan.Click += OnAdhanChanged;
            AddToRow(PerPrayerGrid, row, label, sound, preview, adhan);

            // Notifications: an optional reminder before the prayer.
            RemindersGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var reminder = new ComboBox { ItemsSource = reminderOptions, Margin = new Thickness(0, 6, 0, 6), MinWidth = 150, HorizontalAlignment = HorizontalAlignment.Right };
            AutomationProperties.SetName(reminder, $"{name} reminder");
            reminder.SelectionChanged += OnAdhanChanged;
            AddToRow(RemindersGrid, row, PrayerLabel(prayer), reminder);

            _perPrayer[prayer] = new PrayerAdhanControls(adhan, sound, preview, reminder);
            row++;
        }

        SoundBox.ItemsSource = _app.Sounds.Adhans;

        WireNumberBox(FajrAngleBox);
        WireNumberBox(IshaAngleBox);
        _numberCommit.Tick += (_, _) =>
        {
            _committingWhileTyping = true;
            try
            {
                CommitNumbers();
            }
            finally
            {
                _committingWhileTyping = false;
            }
        };
    }

    /// <summary>
    /// Number boxes save after a short pause in typing, on Enter, and when focus leaves.
    /// Previously they saved only on leaving the box, so a typed offset seemed to do nothing.
    /// </summary>
    private void WireNumberBox(TextBox box)
    {
        box.TextChanged += (_, _) =>
        {
            if (!_loading && IsLoaded)
            {
                _numberCommit.Stop();
                _numberCommit.Start();
            }
        };
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                CommitNumbers();
                box.SelectAll();
                e.Handled = true;
            }
        };
        box.LostFocus += (_, _) => CommitNumbers();
    }

    private void CommitNumbers()
    {
        _numberCommit.Stop();
        OnCalculationChanged(this, new RoutedEventArgs());
    }

    /// <summary>Shows a saved value, but never rewrites a box mid-typing (that would move the caret).</summary>
    private void ShowNumber(TextBox box, double value)
    {
        if (!(_committingWhileTyping && box.IsKeyboardFocusWithin))
        {
            box.Text = value.ToString(CultureInfo.CurrentCulture);
        }
    }

    private StackPanel PrayerLabel(Prayer prayer)
    {
        var label = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
        label.Children.Add(new TextBlock { Text = PrayerNames.English(prayer), FontWeight = FontWeights.SemiBold, Width = 64, VerticalAlignment = VerticalAlignment.Center });
        label.Children.Add(new TextBlock
        {
            Text = PrayerNames.Arabic(prayer), Style = (Style)FindResource("ArabicText"), FontSize = 18, LineHeight = 24, MinWidth = 44,
        });
        return label;
    }

    private static void AddToRow(Grid grid, int row, params UIElement[] cells)
    {
        for (var column = 0; column < cells.Length; column++)
        {
            Grid.SetRow(cells[column], row);
            Grid.SetColumn(cells[column], column);
            grid.Children.Add(cells[column]);
        }
    }

    /// <summary>"Default · Adhan 1" first, then every recording; the Default label follows the current Default Adhan.</summary>
    private List<Option<string?>> PrayerSoundOptions(string defaultSoundId)
    {
        var options = new List<Option<string?>> { new(null, $"Default · {_app.Sounds.FindAdhan(defaultSoundId)?.Name ?? "none"}") };
        options.AddRange(_app.Sounds.Adhans.Select(a => new Option<string?>(a.Id, a.Name)));
        return options;
    }

    /// <summary>What the prayer's row would play right now: its own recording or the Default Adhan.</summary>
    private string PrayerSoundId(Prayer prayer) =>
        Selected<string?>(_perPrayer[prayer].Sound, null) ?? (SoundBox.SelectedItem as AdhanSound)?.Id ?? _app.Settings.Adhan.DefaultSoundId;

    private static void Select<T>(ComboBox box, T value) =>
        box.SelectedItem = box.ItemsSource?.OfType<Option<T>>().FirstOrDefault(o => EqualityComparer<T>.Default.Equals(o.Value, value));

    private static T Selected<T>(ComboBox box, T fallback) => box.SelectedItem is Option<T> o ? o.Value : fallback;

    private void LoadSettings()
    {
        _loading = true;
        try
        {
            var s = _app.Settings;
            WelcomeBanner.Visibility = s.Location is null ? Visibility.Visible : Visibility.Collapsed;
            if (s.Location is { } loc)
            {
                CurrentLocationText.Text = loc.DisplayName;
                CurrentLocationDetail.Text = string.Create(CultureInfo.CurrentCulture,
                    $"{loc.Latitude:0.0000}°, {loc.Longitude:0.0000}° · {loc.TimeZoneId}{(loc.CountryCode is { } cc ? $" · {cc}" : "")}");
                LatBox.Text = loc.Latitude.ToString("0.######", CultureInfo.CurrentCulture);
                LonBox.Text = loc.Longitude.ToString("0.######", CultureInfo.CurrentCulture);
            }
            else
            {
                CurrentLocationText.Text = "No location set";
                CurrentLocationDetail.Text = "Detect it or search below.";
            }

            var c = s.Calculation;
            Select(MethodBox, c.Method);
            CustomAnglesPanel.Visibility = c.Method == CalculationMethod.Custom ? Visibility.Visible : Visibility.Collapsed;
            ShowNumber(FajrAngleBox, c.CustomFajrAngle);
            ShowNumber(IshaAngleBox, c.CustomIshaAngle);
            MadhabStandard.IsChecked = c.Madhab == Madhab.Shafi;
            MadhabHanafi.IsChecked = c.Madhab == Madhab.Hanafi;
            Select(HighLatBox, c.HighLatitudeRule);
            Select(PolarBox, c.PolarCircleResolution);
            Select(HijriOffsetBox, c.HijriDayOffset);
            Select(ImsakBox, s.Ramadan.ImsakMinutesBeforeFajr);
            var adj = c.Adjustments;
            ShowNumber(_adjustments[Prayer.Fajr], adj.Fajr);
            ShowNumber(_adjustments[Prayer.Sunrise], adj.Sunrise);
            ShowNumber(_adjustments[Prayer.Dhuhr], adj.Dhuhr);
            ShowNumber(_adjustments[Prayer.Asr], adj.Asr);
            ShowNumber(_adjustments[Prayer.Maghrib], adj.Maghrib);
            ShowNumber(_adjustments[Prayer.Isha], adj.Isha);
            MethodHint.Text = s.Location?.CountryCode is { } country && !c.MethodChosenByUser
                ? $"Suggested for your country ({country}). Match your local masjid if it differs."
                : "Match the method your local masjid uses.";

            var a = s.Adhan;
            AdhanEnabledBox.IsChecked = a.Enabled;
            SidebarAdhanSwitch.IsChecked = RailAdhanSwitch.IsChecked = a.Enabled;
            RailAdhanSwitch.ToolTip = a.Enabled ? "Adhan is on" : "Adhan is off";
            SoundBox.SelectedItem = _app.Sounds.FindAdhan(a.DefaultSoundId);
            SoundBox.IsEnabled = a.Enabled;
            VolumeSlider.Value = a.Volume;
            VolumeText.Text = $"{a.Volume}%";
            foreach (var (prayer, row) in _perPrayer)
            {
                var alert = a.For(prayer);
                row.Adhan.IsChecked = alert.AdhanEnabled;
                row.Adhan.IsEnabled = a.Enabled;
                row.Sound.ItemsSource = PrayerSoundOptions(a.DefaultSoundId);
                // An override saved under an older ID shows as the recording it maps to.
                Select(row.Sound, alert.SoundId is null ? null : _app.Sounds.FindAdhan(alert.SoundId)?.Id);
                row.Sound.IsEnabled = row.Preview.IsEnabled = a.Enabled && alert.AdhanEnabled;
                Select(row.Reminder, alert.ReminderMinutesBefore);
            }

            VoiceRemindersBox.IsChecked = s.Ramadan.VoiceRemindersEnabled;
            QuietFullScreenBox.IsChecked = a.QuietDuringFullScreen;
            NotificationsBox.IsChecked = a.ShowNotifications;

            StartupBox.IsChecked = s.StartWithWindows;
            Clock24Box.IsChecked = s.Use24HourClock;
            WidgetVisibleBox.IsChecked = s.Widget.Visible;
            SidebarWidgetSwitch.IsChecked = RailWidgetSwitch.IsChecked = s.Widget.Visible;
            SidebarWidgetSwitch.IsEnabled = RailWidgetSwitch.IsEnabled = s.Location is not null;
            RailWidgetSwitch.ToolTip = s.Widget.Visible ? "Desktop widget is shown" : "Desktop widget is hidden";
            WidgetStandard.IsChecked = s.Widget.Layout == WidgetLayout.Standard;
            WidgetCompact.IsChecked = s.Widget.Layout == WidgetLayout.Compact;
            WidgetOpacitySlider.Value = s.Widget.Opacity;
            WidgetTopBox.IsChecked = s.Widget.AlwaysOnTop;
            WidgetLockBox.IsChecked = s.Widget.Locked;
        }
        finally
        {
            _loading = false;
        }
    }

    private int ReadAdjustment(Prayer prayer) =>
        int.TryParse(_adjustments[prayer].Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var v) ? Math.Clamp(v, -120, 120) : 0;

    private static double ReadAngle(TextBox box, double fallback) =>
        double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var v) && v is >= 5 and <= 30 ? v : fallback;

    private void OnCalculationChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || !IsInitialized)
        {
            return;
        }

        _app.Update(s =>
        {
            var method = Selected(MethodBox, s.Calculation.Method);
            return s with
            {
                Calculation = s.Calculation with
                {
                    Method = method,
                    MethodChosenByUser = s.Calculation.MethodChosenByUser || method != s.Calculation.Method,
                    Madhab = MadhabHanafi.IsChecked == true ? Madhab.Hanafi : Madhab.Shafi,
                    HighLatitudeRule = Selected(HighLatBox, s.Calculation.HighLatitudeRule),
                    PolarCircleResolution = Selected(PolarBox, s.Calculation.PolarCircleResolution),
                    HijriDayOffset = Selected(HijriOffsetBox, s.Calculation.HijriDayOffset),
                    CustomFajrAngle = ReadAngle(FajrAngleBox, s.Calculation.CustomFajrAngle),
                    CustomIshaAngle = ReadAngle(IshaAngleBox, s.Calculation.CustomIshaAngle),
                    Adjustments = new PrayerAdjustments(
                        ReadAdjustment(Prayer.Fajr), ReadAdjustment(Prayer.Sunrise), ReadAdjustment(Prayer.Dhuhr),
                        ReadAdjustment(Prayer.Asr), ReadAdjustment(Prayer.Maghrib), ReadAdjustment(Prayer.Isha)),
                },
                Ramadan = s.Ramadan with { ImsakMinutesBeforeFajr = Selected(ImsakBox, s.Ramadan.ImsakMinutesBeforeFajr) },
            };
        });
    }

    private void OnAdhanChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || !IsInitialized)
        {
            return;
        }

        _app.Update(s =>
        {
            var adhan = s.Adhan with
            {
                Enabled = AdhanEnabledBox.IsChecked == true,
                DefaultSoundId = (SoundBox.SelectedItem as AdhanSound)?.Id ?? s.Adhan.DefaultSoundId,
                QuietDuringFullScreen = QuietFullScreenBox.IsChecked == true,
                ShowNotifications = NotificationsBox.IsChecked == true,
            };
            foreach (var (prayer, row) in _perPrayer)
            {
                var alert = adhan.For(prayer);
                adhan = adhan.With(prayer, alert with
                {
                    AdhanEnabled = row.Adhan.IsChecked == true,
                    // Null keeps following the Default Adhan, so changing the default reaches this prayer.
                    SoundId = row.Sound.SelectedItem is Option<string?> o ? o.Value : alert.SoundId,
                    ReminderMinutesBefore = Selected(row.Reminder, 0),
                });
            }

            return s with { Adhan = adhan, Ramadan = s.Ramadan with { VoiceRemindersEnabled = VoiceRemindersBox.IsChecked == true } };
        });
    }

    private void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_loading && IsLoaded)
        {
            VolumeText.Text = $"{(int)e.NewValue}%";
            _app.Update(s => s with { Adhan = s.Adhan with { Volume = (int)e.NewValue } });
        }
    }

    private void OnTestSound(object sender, RoutedEventArgs e) =>
        _app.PlayAdhanPreview((SoundBox.SelectedItem as AdhanSound)?.Id ?? _app.Settings.Adhan.DefaultSoundId);

    private void OnStopSound(object sender, RoutedEventArgs e) => _app.StopAudio();

    private void OnTestAlert(object sender, RoutedEventArgs e) => _app.TriggerTestAlert();

    private void OnGeneralChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || !IsInitialized)
        {
            return;
        }

        _app.Update(s => s with
        {
            StartWithWindows = StartupBox.IsChecked == true,
            Use24HourClock = Clock24Box.IsChecked == true,
            Widget = s.Widget with
            {
                Visible = WidgetVisibleBox.IsChecked == true,
                Layout = WidgetCompact.IsChecked == true ? WidgetLayout.Compact : WidgetLayout.Standard,
                AlwaysOnTop = WidgetTopBox.IsChecked == true,
                Locked = WidgetLockBox.IsChecked == true,
            },
        });
    }

    private void OnWidgetOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_loading && IsLoaded)
        {
            _app.Update(s => s with { Widget = s.Widget with { Opacity = Math.Round(e.NewValue, 2) } });
        }
    }

    // ───────────────────────── Location ─────────────────────────

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnSearch(sender, e);
        }
    }

    private async void OnSearch(object sender, RoutedEventArgs e)
    {
        var query = SearchBox.Text.Trim();
        if (query.Length < 2)
        {
            LocationMessage.Text = "Type a city or place name, then press Enter.";
            return;
        }

        SetLocationBusy(true, "Searching…");
        try
        {
            var results = await _app.Geocoder.SearchAsync(query);
            SearchResults.ItemsSource = results;
            var any = results.Count > 0;
            SearchResults.Visibility = UseResultButton.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
            if (any)
            {
                SearchResults.SelectedIndex = 0;
            }

            LocationMessage.Text = any
                ? "Choose the right place, then select 'Use selected place' (or double-click it)."
                : "No places found. Check the spelling, or enter coordinates instead.";
        }
        catch (Exception ex)
        {
            Log.Error("Location search failed", ex);
            LocationMessage.Text = "Search is unavailable right now (are you online?). You can enter coordinates instead.";
        }
        finally
        {
            SetLocationBusy(false);
        }
    }

    private void OnUseSearchResult(object sender, RoutedEventArgs e)
    {
        if (SearchResults.SelectedItem is LocationInfo location)
        {
            _app.SetLocation(location);
            SearchResults.Visibility = UseResultButton.Visibility = Visibility.Collapsed;
            SearchBox.Text = "";
            LocationMessage.Text = $"Location set to {location.DisplayName}.";
        }
    }

    private async void OnDetectLocation(object sender, RoutedEventArgs e)
    {
        _locationCts?.Cancel();
        _locationCts = new CancellationTokenSource();
        SetLocationBusy(true, "Detecting your location…");
        try
        {
            var (position, error) = await LocationDetector.DetectAsync(_locationCts.Token);
            if (position is null)
            {
                LocationMessage.Text = error;
                return;
            }

            await ApplyCoordinatesAsync(position.Latitude, position.Longitude, LocationSource.Detected);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            SetLocationBusy(false);
        }
    }

    private async void OnApplyCoordinates(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(LatBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var lat)
            && !double.TryParse(LatBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out lat)
            || !double.TryParse(LonBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var lon)
            && !double.TryParse(LonBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out lon)
            || lat is < -90 or > 90 || lon is < -180 or > 180)
        {
            LocationMessage.Text = "Enter a latitude between -90 and 90 and a longitude between -180 and 180 (for example 23.8103 and 90.4125).";
            return;
        }

        SetLocationBusy(true, "Applying…");
        try
        {
            await ApplyCoordinatesAsync(lat, lon, LocationSource.Manual);
        }
        finally
        {
            SetLocationBusy(false);
        }
    }

    /// <summary>Uses the coordinates immediately; the place name is a best-effort extra.</summary>
    private async Task ApplyCoordinatesAsync(double lat, double lon, LocationSource source)
    {
        LocationInfo? named = null;
        try
        {
            named = await _app.Geocoder.ReverseAsync(lat, lon);
        }
        catch (Exception ex)
        {
            Log.Error("Reverse geocoding failed", ex);
        }

        var fallbackName = string.Create(CultureInfo.CurrentCulture, $"{lat:0.0000}°, {lon:0.0000}°");
        var location = named is not null
            ? named with { Source = source }
            : LocationInfo.Create(fallbackName, lat, lon, null, source);
        _app.SetLocation(location);
        LocationMessage.Text = $"Location set to {location.DisplayName}.";
    }

    private void SetLocationBusy(bool busy, string? message = null)
    {
        SearchButton.IsEnabled = DetectButton.IsEnabled = !busy;
        if (message is not null)
        {
            LocationMessage.Text = message;
        }

        Cursor = busy ? Cursors.AppStarting : null;
    }

    // ───────────────────────── About ─────────────────────────

    private void OnOpenSettingsFolder(object sender, RoutedEventArgs e) =>
        OpenFolder(Path.GetDirectoryName(DevMode.IsEnabled ? DevMode.SettingsPath : SettingsStore.DefaultPath)!);

    private void OnOpenLogFolder(object sender, RoutedEventArgs e) => OpenFolder(Log.Folder);

    private void OnOpenPayPal(object sender, RoutedEventArgs e) => OpenInBrowser(AppInfo.PayPalUrl);

    private void OnOpenWebsite(object sender, RoutedEventArgs e) => OpenInBrowser(AppInfo.WebsiteUrl);

    private void OnReportIssue(object sender, RoutedEventArgs e) => OpenInBrowser(AppInfo.IssuesUrl);

    // Links go to the user's default browser; OptimAdhan never shows payment or account forms itself.
    private static void OpenInBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"Could not open {url}", ex);
        }
    }

    private static void OpenFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

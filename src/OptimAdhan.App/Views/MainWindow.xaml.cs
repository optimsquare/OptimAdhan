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
using System.Windows.Threading;
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

public sealed record DayRow(string Dow, int DayNumber, string HijriText, string Fajr, string Sunrise, string Dhuhr,
    string Asr, string Maghrib, string Isha, bool IsToday, string Tag, bool IsMonthStart)
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

    private static readonly Brush[] AshraBadges =
    [
        Frozen(Color.FromArgb(0x24, 0xE9, 0xCF, 0x96)),
        Frozen(Color.FromArgb(0x3D, 0xE9, 0xCF, 0x96)),
        Frozen(Color.FromArgb(0x5C, 0xE9, 0xCF, 0x96)),
    ];

    private readonly AppController _app;
    private readonly Dashboard _dashboard;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Dictionary<Prayer, (CheckBox Adhan, ComboBox Reminder)> _perPrayer = [];
    private readonly Dictionary<Prayer, TextBox> _adjustments = [];
    private IReadOnlyList<RamadanRow> _ramadanRows = [];
    private UniformGrid? _cardsGrid;
    private DateOnly _month;
    private bool _loading;
    private CancellationTokenSource? _locationCts;

    public MainWindow(AppController app)
    {
        InitializeComponent();
        _app = app;
        _dashboard = new Dashboard(app);
        DataContext = _dashboard;

        _month = app.Schedule is { } s ? s.LocalDate(DateTime.UtcNow) : DateOnly.FromDateTime(DateTime.Today);
        _month = new DateOnly(_month.Year, _month.Month, 1);

        BuildSettingsControls();
        LoadSettings();
        RefreshAll();

        SourceInitialized += (_, _) => ApplyMaterial();
        StateChanged += (_, _) => UpdateMaximizeState();
        SizeChanged += (_, _) => ApplyResponsiveLayout();
        _clock.Tick += (_, _) => Tick();
        _clock.Start();
        _app.SettingsChanged += OnSettingsChanged;
        _app.AudioStateChanged += OnAudioStateChanged;
        Closed += (_, _) =>
        {
            _clock.Stop();
            _locationCts?.Cancel();
            _app.SettingsChanged -= OnSettingsChanged;
            _app.AudioStateChanged -= OnAudioStateChanged;
        };
        VersionText.Text = $"Version {AppInfo.Version}";
    }

    public void Navigate(Page page)
    {
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
        WindowEffects.SetBorderColor(hwnd, Color.FromRgb(0x1C, 0x2C, 0x4D));

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

    private void OnCardsGridLoaded(object sender, RoutedEventArgs e)
    {
        _cardsGrid = (UniformGrid)sender;
        ApplyResponsiveLayout();
    }

    /// <summary>Re-flows Today for narrow windows (small laptops, high display scaling).</summary>
    private void ApplyResponsiveLayout()
    {
        var width = ActualWidth - 236;
        var narrow = width < 900;
        SideColumn.Width = narrow ? new GridLength(0) : new GridLength(300);
        Grid.SetColumn(SidePanel, narrow ? 0 : 1);
        Grid.SetRow(SidePanel, narrow ? 1 : 0);
        SidePanel.Margin = narrow ? new Thickness(0, 14, 0, 0) : new Thickness(16, 0, 0, 0);
        if (_cardsGrid is not null)
        {
            _cardsGrid.Columns = width < 760 ? 3 : 6;
        }
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

    private void OnSidebarAdhan(object sender, RoutedEventArgs e) =>
        _app.Update(s => s with { Adhan = s.Adhan with { Enabled = SidebarAdhanSwitch.IsChecked == true } });

    private void OnSidebarWidget(object sender, RoutedEventArgs e) =>
        _app.Update(s => s with { Widget = s.Widget with { Visible = SidebarWidgetSwitch.IsChecked == true } });

    // ───────────────────────── Timetable ─────────────────────────

    private string T(DateTimeOffset? time) => Format.Time(time, _app.Settings.Use24HourClock);

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
            var box = new TextBox { Width = 64, Margin = new Thickness(0, 4, 10, 0), HorizontalContentAlignment = HorizontalAlignment.Center };
            AutomationProperties.SetName(box, $"{PrayerNames.English(prayer)} adjustment in minutes");
            box.LostFocus += OnCalculationChanged;
            var cell = new StackPanel();
            cell.Children.Add(new TextBlock { Text = PrayerNames.English(prayer), FontSize = 12, Foreground = (Brush)FindResource("TextSecondaryBrush") });
            cell.Children.Add(box);
            AdjustmentsGrid.Children.Add(cell);
            _adjustments[prayer] = box;
        }

        var reminderOptions = ReminderChoices
            .Select(m => new Option<int>(m, m == 0 ? "No reminder" : $"Remind {m} min before")).ToList();
        var row = 0;
        foreach (var prayer in PrayerScheduleService.FivePrayers)
        {
            PerPrayerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            label.Children.Add(new TextBlock { Text = PrayerNames.English(prayer), FontWeight = FontWeights.SemiBold, Width = 76, VerticalAlignment = VerticalAlignment.Center });
            label.Children.Add(new TextBlock
            {
                Text = PrayerNames.Arabic(prayer), Style = (Style)FindResource("ArabicText"), FontSize = 18, LineHeight = 24,
            });
            var adhan = new CheckBox { Content = "Adhan", Margin = new Thickness(0, 6, 24, 6) };
            AutomationProperties.SetName(adhan, $"{PrayerNames.English(prayer)} Adhan");
            adhan.Click += OnAdhanChanged;
            var reminder = new ComboBox { ItemsSource = reminderOptions, Margin = new Thickness(0, 6, 0, 6), MinWidth = 210, HorizontalAlignment = HorizontalAlignment.Right };
            AutomationProperties.SetName(reminder, $"{PrayerNames.English(prayer)} reminder");
            reminder.SelectionChanged += OnAdhanChanged;
            Grid.SetRow(label, row);
            Grid.SetRow(adhan, row);
            Grid.SetColumn(adhan, 1);
            Grid.SetRow(reminder, row);
            Grid.SetColumn(reminder, 2);
            PerPrayerGrid.Children.Add(label);
            PerPrayerGrid.Children.Add(adhan);
            PerPrayerGrid.Children.Add(reminder);
            _perPrayer[prayer] = (adhan, reminder);
            row++;
        }

        SoundBox.ItemsSource = _app.Sounds.Adhans;
        FajrSoundBox.ItemsSource = _app.Sounds.Adhans;
    }

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
            FajrAngleBox.Text = c.CustomFajrAngle.ToString(CultureInfo.CurrentCulture);
            IshaAngleBox.Text = c.CustomIshaAngle.ToString(CultureInfo.CurrentCulture);
            MadhabStandard.IsChecked = c.Madhab == Madhab.Shafi;
            MadhabHanafi.IsChecked = c.Madhab == Madhab.Hanafi;
            Select(HighLatBox, c.HighLatitudeRule);
            Select(PolarBox, c.PolarCircleResolution);
            Select(HijriOffsetBox, c.HijriDayOffset);
            Select(ImsakBox, s.Ramadan.ImsakMinutesBeforeFajr);
            var adj = c.Adjustments;
            _adjustments[Prayer.Fajr].Text = adj.Fajr.ToString(CultureInfo.CurrentCulture);
            _adjustments[Prayer.Sunrise].Text = adj.Sunrise.ToString(CultureInfo.CurrentCulture);
            _adjustments[Prayer.Dhuhr].Text = adj.Dhuhr.ToString(CultureInfo.CurrentCulture);
            _adjustments[Prayer.Asr].Text = adj.Asr.ToString(CultureInfo.CurrentCulture);
            _adjustments[Prayer.Maghrib].Text = adj.Maghrib.ToString(CultureInfo.CurrentCulture);
            _adjustments[Prayer.Isha].Text = adj.Isha.ToString(CultureInfo.CurrentCulture);
            MethodHint.Text = s.Location?.CountryCode is { } country && !c.MethodChosenByUser
                ? $"Suggested for your country ({country}). Match your local masjid if it differs."
                : "Match the method your local masjid uses.";

            var a = s.Adhan;
            AdhanEnabledBox.IsChecked = a.Enabled;
            SidebarAdhanSwitch.IsChecked = a.Enabled;
            SoundBox.SelectedItem = _app.Sounds.Adhans.FirstOrDefault(x => x.Id == a.DefaultSoundId) ?? _app.Sounds.Adhans.FirstOrDefault();
            FajrSoundBox.SelectedItem = _app.Sounds.Adhans.FirstOrDefault(x => x.Id == a.FajrSoundId) ?? _app.Sounds.Adhans.FirstOrDefault();
            VolumeSlider.Value = a.Volume;
            VolumeText.Text = $"{a.Volume}%";
            foreach (var (prayer, (adhan, reminder)) in _perPrayer)
            {
                var alert = a.For(prayer);
                adhan.IsChecked = alert.AdhanEnabled;
                adhan.IsEnabled = a.Enabled;
                Select(reminder, alert.ReminderMinutesBefore);
            }

            VoiceRemindersBox.IsChecked = s.Ramadan.VoiceRemindersEnabled;
            QuietFullScreenBox.IsChecked = a.QuietDuringFullScreen;
            NotificationsBox.IsChecked = a.ShowNotifications;

            StartupBox.IsChecked = s.StartWithWindows;
            Clock24Box.IsChecked = s.Use24HourClock;
            WidgetVisibleBox.IsChecked = s.Widget.Visible;
            SidebarWidgetSwitch.IsChecked = s.Widget.Visible;
            SidebarWidgetSwitch.IsEnabled = s.Location is not null;
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
                FajrSoundId = (FajrSoundBox.SelectedItem as AdhanSound)?.Id ?? s.Adhan.FajrSoundId,
                QuietDuringFullScreen = QuietFullScreenBox.IsChecked == true,
                ShowNotifications = NotificationsBox.IsChecked == true,
            };
            foreach (var (prayer, (box, reminder)) in _perPrayer)
            {
                adhan = adhan.With(prayer, adhan.For(prayer) with
                {
                    AdhanEnabled = box.IsChecked == true,
                    ReminderMinutesBefore = Selected(reminder, 0),
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

    private void OnTestFajrSound(object sender, RoutedEventArgs e) =>
        _app.PlayAdhanPreview((FajrSoundBox.SelectedItem as AdhanSound)?.Id ?? _app.Settings.Adhan.FajrSoundId);

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

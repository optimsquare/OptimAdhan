using System.IO;
using System.Windows.Threading;
using Microsoft.Win32;
using OptimAdhan.Core.Location;
using OptimAdhan.Core.Scheduling;
using OptimAdhan.Core.Settings;

namespace OptimAdhan.App.Services;

/// <summary>
/// The background heart of the app: owns settings, the prayer schedule, the
/// alert dispatcher and its timer, and audio. Lives for the whole process,
/// whether or not any window is open. All members are used on the UI thread.
/// </summary>
public sealed class AppController : IDisposable
{
    /// <summary>Upper bound on how long the timer sleeps, so clock jumps are noticed quickly.</summary>
    private static readonly TimeSpan MaxSleep = TimeSpan.FromSeconds(30);

    private readonly SettingsStore _store;
    private readonly AlertDispatcher _dispatcher = new();
    private readonly DispatcherTimer _timer;

    public AppController(SettingsStore store, SoundLibrary sounds)
    {
        _store = store;
        Sounds = sounds;
        Settings = store.Load();
        Geocoder = new NominatimGeocoder($"OptimAdhan/{AppInfo.Version} (Windows desktop; +https://optimsquare.io)");

        Audio.PlaybackStopped += (_, _) => AudioStateChanged?.Invoke(this, EventArgs.Empty);

        _timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = MaxSleep };
        _timer.Tick += (_, _) => Pump();

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.TimeChanged += OnTimeChanged;

        Rebuild();
        _timer.Start();
    }

    public event EventHandler? SettingsChanged;

    public event EventHandler<AlertOccurrence>? AlertRaised;

    public event EventHandler? AudioStateChanged;

    public AppSettings Settings { get; private set; }

    /// <summary>Null until a location is set (or if its time zone is unavailable).</summary>
    public PrayerScheduleService? Schedule { get; private set; }

    public string? ScheduleError { get; private set; }

    public SoundLibrary Sounds { get; }

    public AudioPlayer Audio { get; } = new();

    public IGeocoder Geocoder { get; }

    public DateTime? MutedUntilUtc => _dispatcher.MutedUntilUtc;

    public bool IsNextAdhanSkipped => _dispatcher.IsNextAdhanSkipped(DateTime.UtcNow);

    public void Update(Func<AppSettings, AppSettings> change)
    {
        var previous = Settings;
        Settings = change(Settings);
        try
        {
            _store.Save(Settings);
        }
        catch (Exception ex)
        {
            Log.Error("Saving settings failed", ex);
        }

        if (previous.StartWithWindows != Settings.StartWithWindows)
        {
            StartupRegistration.Apply(Settings.StartWithWindows);
        }

        if (previous.Location != Settings.Location
            || previous.Calculation != Settings.Calculation
            || previous.Adhan != Settings.Adhan
            || previous.Ramadan != Settings.Ramadan)
        {
            Rebuild();
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Sets a new location; suggests the country's usual method unless the user chose one.</summary>
    public void SetLocation(LocationInfo location) => Update(s => s with
    {
        Location = location,
        Calculation = s.Calculation.MethodChosenByUser || location.CountryCode is null
            ? s.Calculation
            : s.Calculation with { Method = Core.Calculation.CalculationMethods.ForCountry(location.CountryCode) },
    });

    public void ToggleSkipNextAdhan()
    {
        _dispatcher.ToggleSkipNextAdhan(DateTime.UtcNow);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void MuteFor(TimeSpan? duration)
    {
        _dispatcher.MutedUntilUtc = duration is { } d ? DateTime.UtcNow + d : null;
        if (duration is not null)
        {
            Audio.Stop();
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void PlayAdhanPreview(string soundId)
    {
        if (Sounds.AdhanPath(soundId) is { } path)
        {
            Audio.Play(path, Settings.Adhan.Volume);
            AudioStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void StopAudio() => Audio.Stop();

    /// <summary>
    /// Sends a sample Adhan for the next prayer through the real delivery path
    /// (sound, popup, full-screen rule), so users can check their setup.
    /// </summary>
    public void TriggerTestAlert()
    {
        var next = Schedule?.NextPrayer(DateTime.UtcNow);
        var prayer = next?.Prayer ?? Core.Calculation.Prayer.Dhuhr;
        var alert = new ScheduledAlert
        {
            Kind = AlertKind.Adhan,
            Utc = DateTime.UtcNow,
            Date = DateOnly.FromDateTime(DateTime.Today),
            Prayer = prayer,
            SoundId = Settings.Adhan.SoundFor(prayer),
            IsTest = true,
        };
        Deliver(new AlertOccurrence(alert, AlertDelivery.Play));
    }

    public void Dispose()
    {
        _timer.Stop();
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.TimeChanged -= OnTimeChanged;
        Audio.Stop();
        (Geocoder as IDisposable)?.Dispose();
    }

    private void Rebuild()
    {
        Schedule = null;
        ScheduleError = null;
        if (Settings.Location is { } location)
        {
            try
            {
                Schedule = new PrayerScheduleService(location, Settings.Calculation, Settings.Ramadan);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
            {
                ScheduleError = $"Prayer times could not be calculated for {location.DisplayName}: {ex.Message}";
                Log.Error(ScheduleError, ex);
            }
        }

        _dispatcher.Configure(Schedule, Settings.Adhan, Settings.Ramadan, DateTime.UtcNow);
        Log.Info(Schedule is null
            ? "Scheduler idle: no location"
            : $"Scheduler configured for {Schedule.Location.DisplayName} ({Schedule.Location.TimeZoneId}), {Schedule.Parameters.Method}");
        ScheduleNextTick();
    }

    private void Pump()
    {
        var now = DateTime.UtcNow;
        foreach (var occurrence in _dispatcher.Poll(now))
        {
            Deliver(occurrence);
        }

        ScheduleNextTick();
    }

    private void ScheduleNextTick()
    {
        var now = DateTime.UtcNow;
        var wait = _dispatcher.NextDueUtc(now) is { } due ? due - now : MaxSleep;
        _timer.Interval = wait < TimeSpan.FromMilliseconds(250) ? TimeSpan.FromMilliseconds(250)
            : wait > MaxSleep ? MaxSleep
            : wait;
    }

    private void Deliver(AlertOccurrence occurrence)
    {
        var (alert, delivery) = occurrence;
        if (delivery == AlertDelivery.Play && Settings.Adhan.QuietDuringFullScreen && FullScreenDetector.IsFullScreenActive())
        {
            delivery = AlertDelivery.Silent;
        }

        Log.Info($"Alert {alert.Key} at {alert.Utc:u}: {delivery}");

        if (delivery == AlertDelivery.Play)
        {
            var path = alert.Kind == AlertKind.Adhan
                ? Sounds.AdhanPath(alert.SoundId)
                : Sounds.ReminderPath(alert.SoundId);

            if (path is not null && File.Exists(path))
            {
                Audio.Play(path, Settings.Adhan.Volume);
            }
            else if (alert.SoundId is not null)
            {
                Log.Error($"Sound '{alert.SoundId}' not found for {alert.Key}");
            }
        }

        AlertRaised?.Invoke(this, occurrence with { Delivery = delivery });
        AudioStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            Log.Info("Resumed from sleep");
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(Pump);
        }
    }

    private void OnTimeChanged(object? sender, EventArgs e)
    {
        // Clock or time zone changed: refresh cached zone data and re-evaluate.
        Log.Info("System time changed");
        TimeZoneInfo.ClearCachedData();
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            Rebuild();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        });
    }
}

public static class AppInfo
{
    public const string ProductName = "OptimAdhan";
    public const string Company = "Optim Square LLC";
    public const string Tagline = "Your Daily Prayer Companion";
    public const string WebsiteUrl = "https://optimsquare.io";
    public const string IssuesUrl = "https://github.com/optimsquare/OptimAdhan/issues/new";
    // Same donation link as the PrayerAndRamadan web app's "Support Our Work" dialog (components/footer.tsx).
    public const string PayPalUrl = "https://paypal.me/RRAZU";

    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";
}

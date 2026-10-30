using OptimAdhan.Core.Calculation;
using OptimAdhan.Core.Location;

namespace OptimAdhan.Core.Settings;

/// <summary>All user preferences. Persisted as JSON by <see cref="SettingsStore"/>.</summary>
public sealed record AppSettings
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    /// <summary>Null until the user completes first-run setup.</summary>
    public LocationInfo? Location { get; init; }

    public CalculationSettings Calculation { get; init; } = new();

    public AdhanSettings Adhan { get; init; } = new();

    public RamadanSettings Ramadan { get; init; } = new();

    public WidgetSettings Widget { get; init; } = new();

    public MainWindowSettings MainWindow { get; init; } = new();

    public bool StartWithWindows { get; init; } = true;

    public bool Use24HourClock { get; init; }
}

public enum HighLatitudeSetting
{
    /// <summary>Seventh of the night above 48° latitude, otherwise middle of the night (adhan's recommendation).</summary>
    Automatic,
    MiddleOfTheNight,
    SeventhOfTheNight,
    TwilightAngle,
}

public sealed record CalculationSettings
{
    public CalculationMethod Method { get; init; } = CalculationMethod.MoonsightingCommittee;

    /// <summary>True once the user picks a method, so changing country no longer overrides it.</summary>
    public bool MethodChosenByUser { get; init; }

    public Madhab Madhab { get; init; } = Madhab.Shafi;

    public HighLatitudeSetting HighLatitudeRule { get; init; } = HighLatitudeSetting.Automatic;

    public PolarCircleResolution PolarCircleResolution { get; init; } = PolarCircleResolution.AqrabBalad;

    public double CustomFajrAngle { get; init; } = 18;

    public double CustomIshaAngle { get; init; } = 17;

    /// <summary>Per-prayer minute offsets, for example to match the local masjid timetable.</summary>
    public PrayerAdjustments Adjustments { get; init; }

    /// <summary>Shift Hijri dates by up to ±2 days to follow local moonsighting.</summary>
    public int HijriDayOffset { get; init; }

    public CalculationParameters ToParameters(Coordinates coordinates)
    {
        var parameters = CalculationMethods.For(Method);
        if (Method == CalculationMethod.Custom)
        {
            parameters = parameters with { FajrAngle = CustomFajrAngle, IshaAngle = CustomIshaAngle };
        }

        return parameters with
        {
            Madhab = Madhab,
            HighLatitudeRule = HighLatitudeRule switch
            {
                HighLatitudeSetting.MiddleOfTheNight => Calculation.HighLatitudeRule.MiddleOfTheNight,
                HighLatitudeSetting.SeventhOfTheNight => Calculation.HighLatitudeRule.SeventhOfTheNight,
                HighLatitudeSetting.TwilightAngle => Calculation.HighLatitudeRule.TwilightAngle,
                _ => coordinates.Latitude > 48
                    ? Calculation.HighLatitudeRule.SeventhOfTheNight
                    : Calculation.HighLatitudeRule.MiddleOfTheNight,
            },
            PolarCircleResolution = PolarCircleResolution,
            Adjustments = Adjustments,
        };
    }
}

/// <summary>Alert behaviour for one of the five prayers.</summary>
public sealed record PrayerAlert
{
    public bool AdhanEnabled { get; init; } = true;

    /// <summary>Sound ID from the sound library; null uses the default for this prayer.</summary>
    public string? SoundId { get; init; }

    /// <summary>Minutes before the prayer for a reminder notification; 0 turns it off.</summary>
    public int ReminderMinutesBefore { get; init; }
}

public sealed record AdhanSettings
{
    /// <summary>Master switch for all Adhan audio.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>0–100.</summary>
    public int Volume { get; init; } = 80;

    public string DefaultSoundId { get; init; } = "makkah";

    public string FajrSoundId { get; init; } = "makkah";

    public PrayerAlert Fajr { get; init; } = new();

    public PrayerAlert Dhuhr { get; init; } = new();

    public PrayerAlert Asr { get; init; } = new();

    public PrayerAlert Maghrib { get; init; } = new();

    public PrayerAlert Isha { get; init; } = new();

    public bool ShowNotifications { get; init; } = true;

    /// <summary>When a full-screen app or presentation is active, show a notification instead of playing audio.</summary>
    public bool QuietDuringFullScreen { get; init; }

    public PrayerAlert For(Prayer prayer) => prayer switch
    {
        Prayer.Fajr => Fajr,
        Prayer.Dhuhr => Dhuhr,
        Prayer.Asr => Asr,
        Prayer.Maghrib => Maghrib,
        Prayer.Isha => Isha,
        _ => throw new ArgumentOutOfRangeException(nameof(prayer), prayer, "Only the five daily prayers have alerts."),
    };

    public AdhanSettings With(Prayer prayer, PrayerAlert alert) => prayer switch
    {
        Prayer.Fajr => this with { Fajr = alert },
        Prayer.Dhuhr => this with { Dhuhr = alert },
        Prayer.Asr => this with { Asr = alert },
        Prayer.Maghrib => this with { Maghrib = alert },
        Prayer.Isha => this with { Isha = alert },
        _ => throw new ArgumentOutOfRangeException(nameof(prayer), prayer, "Only the five daily prayers have alerts."),
    };

    public string SoundFor(Prayer prayer) =>
        For(prayer).SoundId ?? (prayer == Prayer.Fajr ? FajrSoundId : DefaultSoundId);
}

public sealed record RamadanSettings
{
    /// <summary>Minutes before Fajr that Sehri ends (Imsak). The web app used 10.</summary>
    public int ImsakMinutesBeforeFajr { get; init; } = 10;

    /// <summary>Spoken Sehri/Iftar countdown reminders during Ramadan.</summary>
    public bool VoiceRemindersEnabled { get; init; } = true;
}

public enum WidgetLayout
{
    Compact,
    Standard,
}

public sealed record WidgetSettings
{
    public bool Visible { get; init; } = true;

    public WidgetLayout Layout { get; init; } = WidgetLayout.Standard;

    /// <summary>Window position in device-independent pixels; null places it at the top-right of the work area.</summary>
    public double? Left { get; init; }

    public double? Top { get; init; }

    /// <summary>Glass tint strength, 0.4 (most see-through) to 1.0 (most solid).</summary>
    public double Opacity { get; init; } = 0.8;

    public bool AlwaysOnTop { get; init; }

    public bool Locked { get; init; }
}

/// <summary>How the main window's sidebar is shown.</summary>
public enum NavigationMode
{
    /// <summary>Icons and labels.</summary>
    Expanded,

    /// <summary>A narrow rail of icons with tooltips.</summary>
    Collapsed,

    /// <summary>No sidebar; a title-bar button brings it back.</summary>
    Hidden,
}

public sealed record MainWindowSettings
{
    public NavigationMode Navigation { get; init; } = NavigationMode.Expanded;

    /// <summary>Restored (not maximized) bounds in device-independent pixels; null centres the window.</summary>
    public double? Left { get; init; }

    public double? Top { get; init; }

    public double? Width { get; init; }

    public double? Height { get; init; }

    public bool Maximized { get; init; }
}

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using OptimAdhan.App.Services;
using OptimAdhan.Core.Calculation;
using OptimAdhan.Core.Scheduling;

namespace OptimAdhan.App.ViewModels;

public abstract class Observable : INotifyPropertyChanged
{
    private readonly Dictionary<string, object?> _values = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    protected T Get<T>(T fallback = default!, [CallerMemberName] string? name = null) =>
        _values.TryGetValue(name!, out var v) && v is T t ? t : fallback;

    /// <summary>Raises PropertyChanged only when the value really changes, so bound text redraws only then.</summary>
    protected bool Set<T>(T value, [CallerMemberName] string? name = null)
    {
        if (_values.TryGetValue(name!, out var old) && EqualityComparer<T>.Default.Equals((T?)old, value))
        {
            return false;
        }

        _values[name!] = value;
        Notify(name!);
        return true;
    }

    protected void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class PrayerNames
{
    public static string English(Prayer p) => p switch
    {
        Prayer.Fajr => "Fajr",
        Prayer.Sunrise => "Sunrise",
        Prayer.Dhuhr => "Dhuhr",
        Prayer.Asr => "Asr",
        Prayer.Maghrib => "Maghrib",
        Prayer.Isha => "Isha",
        _ => p.ToString(),
    };

    public static string Arabic(Prayer p) => p switch
    {
        Prayer.Fajr => "الفجر",
        Prayer.Sunrise => "الشروق",
        Prayer.Dhuhr => "الظهر",
        Prayer.Asr => "العصر",
        Prayer.Maghrib => "المغرب",
        Prayer.Isha => "العشاء",
        _ => "",
    };

    /// <summary>24×24 stroke icon for each time of day (sun position, then moon).</summary>
    public static string Icon(Prayer p) => p switch
    {
        Prayer.Fajr => "M3,18 H21 M6,14 A6,6 0 0 1 18,14 M12,4 V8 M9,7 L12,4 L15,7",
        Prayer.Sunrise => "M3,18 H21 M7,14 A5,5 0 0 1 17,14 M12,6 V8 M4.9,9.9 L6.3,11.3 M19.1,9.9 L17.7,11.3",
        Prayer.Dhuhr => "M12,8 A4,4 0 1 0 12.01,8 M12,2 V4 M12,20 V22 M2,12 H4 M20,12 H22 M4.9,4.9 L6.3,6.3 M17.7,17.7 L19.1,19.1 M4.9,19.1 L6.3,17.7 M17.7,6.3 L19.1,4.9",
        Prayer.Asr => "M10,9 A4,4 0 1 0 10.01,9 M3,21 H21 M17,6 L19,4 M20,11 H22",
        Prayer.Maghrib => "M3,18 H21 M7,18 A5,5 0 0 1 17,18 M12,8 V12 M9,11 L12,14 L15,11",
        _ => "M20,14.5 A8.5,8.5 0 1 1 9.5,4 A7,7 0 0 0 20,14.5 Z",
    };
}

public static class Format
{
    public static string Time(DateTimeOffset? t, bool use24Hour) =>
        t is { } v ? v.ToString(use24Hour ? "HH:mm" : "h:mm tt", CultureInfo.CurrentCulture) : "—";

    /// <summary>"1:38:12" style countdown (hours shown only when needed).</summary>
    public static string Countdown(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes}:{span.Seconds:00}";
    }

    /// <summary>"1 h 38 min" — used in sentences and the tray.</summary>
    public static string ShortCountdown(TimeSpan span) =>
        span.TotalHours >= 1
            ? $"{(int)span.TotalHours} h {span.Minutes} min"
            : $"{Math.Max(1, (int)Math.Ceiling(span.TotalMinutes))} min";

    /// <summary>
    /// Widget countdown: "1h 38m", then "47 min", then "9:42" with live seconds only in the
    /// final ten minutes. Always exact to the displayed unit (whole minutes are floored, so it
    /// never shows a prayer as later than it is), and it changes at most once a minute until
    /// the last ten, which keeps the widget's redraws, and CPU, minimal.
    /// </summary>
    public static string CompactCountdown(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        return span.TotalHours >= 1 ? $"{(int)span.TotalHours}h {span.Minutes:00}m"
            : span.TotalMinutes >= 10 ? $"{(int)span.TotalMinutes} min"
            : Countdown(span);
    }

    /// <summary>"UTC+6", "UTC+5:30", "UTC−4".</summary>
    public static string Offset(TimeSpan offset)
    {
        var d = offset.Duration();
        return $"UTC{(offset < TimeSpan.Zero ? "−" : "+")}{(int)d.TotalHours}{(d.Minutes > 0 ? $":{d.Minutes:00}" : "")}";
    }
}

public enum PrayerState
{
    Passed,
    Current,
    Next,
    Upcoming,
}

public sealed class PrayerRow : Observable
{
    public required Prayer Prayer { get; init; }

    public string Name => PrayerNames.English(Prayer);

    public string Arabic => PrayerNames.Arabic(Prayer);

    public string Icon => PrayerNames.Icon(Prayer);

    public required string TimeText { get; init; }

    public required DateTimeOffset? Time { get; init; }

    public bool IsSunrise => Prayer == Prayer.Sunrise;

    public PrayerState State { get => Get(PrayerState.Upcoming); set { if (Set(value)) { Raise(); } } }

    public bool IsNext => State == PrayerState.Next;

    public bool IsPast => State == PrayerState.Passed;

    public bool IsCurrent => State == PrayerState.Current;

    public string StatusText => State switch
    {
        PrayerState.Passed => "Passed",
        PrayerState.Current => "Now",
        PrayerState.Next => "Next",
        _ => IsSunrise ? "" : "Upcoming",
    };

    private void Raise()
    {
        Notify(nameof(IsNext));
        Notify(nameof(IsPast));
        Notify(nameof(IsCurrent));
        Notify(nameof(StatusText));
    }
}

/// <summary>
/// Live "today" state shared by the main window and the widget. Call
/// <see cref="Refresh"/> once a second while something visible is bound to it;
/// bound text only redraws when its value actually changes.
/// </summary>
public sealed class Dashboard : Observable
{
    private static readonly Prayer[] Shown =
        [Prayer.Fajr, Prayer.Sunrise, Prayer.Dhuhr, Prayer.Asr, Prayer.Maghrib, Prayer.Isha];

    private readonly AppController _app;
    private DateOnly? _rowsDate;
    private PrayerScheduleService? _rowsSchedule;
    private bool _rowsUse24;

    public Dashboard(AppController app)
    {
        _app = app;
        Refresh();
    }

    /// <summary>All six times, Sunrise included.</summary>
    public ObservableCollection<PrayerRow> Rows { get; } = [];

    /// <summary>The five prayers only (widget strip).</summary>
    public ObservableCollection<PrayerRow> FiveRows { get; } = [];

    public bool HasSchedule { get => Get(false); private set => Set(value); }

    public string LocationName { get => Get(""); private set => Set(value); }

    /// <summary>City only ("Dhaka"), for tight spaces.</summary>
    public string City { get => Get(""); private set => Set(value); }

    /// <summary>Shown when the location's time zone differs from the PC's.</summary>
    public string TimeZoneNote { get => Get(""); private set => Set(value); }

    public string ZoneShort { get => Get(""); private set => Set(value); }

    public string Clock { get => Get(""); private set => Set(value); }

    public string ClockShort { get => Get(""); private set => Set(value); }

    public string Gregorian { get => Get(""); private set => Set(value); }

    public string GregorianShort { get => Get(""); private set => Set(value); }

    public string Hijri { get => Get(""); private set => Set(value); }

    public string NextName { get => Get(""); private set => Set(value); }

    public string NextArabic { get => Get(""); private set => Set(value); }

    /// <summary>The scheduled prayer (Adhan) time, e.g. "2:56 PM".</summary>
    public string NextTime { get => Get(""); private set => Set(value); }

    /// <summary>Live countdown with seconds, e.g. "1:38:12".</summary>
    public string NextCountdown { get => Get(""); private set => Set(value); }

    public string NextCountdownCompact { get => Get(""); private set => Set(value); }

    /// <summary>"Asr in 1 h 38 min".</summary>
    public string NextShort { get => Get(""); private set => Set(value); }

    /// <summary>0–1: how far we are from the previous prayer to the next (drives the ring and bars).</summary>
    public double Progress { get => Get(0.0); private set => Set(Math.Round(value, 3)); }

    public string CurrentTitle { get => Get(""); private set => Set(value); }

    public string CurrentDetail { get => Get(""); private set => Set(value); }

    public bool IsRamadan { get => Get(false); private set => Set(value); }

    public string RamadanDay { get => Get(""); private set => Set(value); }

    public string SehriTime { get => Get(""); private set => Set(value); }

    public string IftarTime { get => Get(""); private set => Set(value); }

    public string FastLabel { get => Get(""); private set => Set(value); }

    public string FastCountdown { get => Get(""); private set => Set(value); }

    public string FastCountdownCompact { get => Get(""); private set => Set(value); }

    /// <summary>Outside Ramadan: "Begins in 102 days".</summary>
    public string RamadanTeaser { get => Get(""); private set => Set(value); }

    public string RamadanTeaserDate { get => Get(""); private set => Set(value); }

    /// <summary>Year-round footer: sunrise, sunset, Jumu'ah, Ramadan countdown.</summary>
    public string InfoLine { get => Get(""); private set => Set(value); }

    public bool AdhanOn { get => Get(false); private set => Set(value); }

    public string AdhanStatus { get => Get(""); private set => Set(value); }

    public string Status { get => Get(""); private set => Set(value); }

    public bool HasStatus { get => Get(false); private set => Set(value); }

    public string MuteNote { get => Get(""); private set => Set(value); }

    public string MethodNote { get => Get(""); private set => Set(value); }

    public string TimesHeading { get => Get(""); private set => Set(value); }

    public void Refresh()
    {
        var schedule = _app.Schedule;
        var use24 = _app.Settings.Use24HourClock;
        HasSchedule = schedule is not null;
        UpdateAdhanStatus();

        if (schedule is null)
        {
            LocationName = _app.Settings.Location?.DisplayName ?? "No location set";
            City = LocationName;
            Status = _app.ScheduleError ?? "Set your location to see prayer times.";
            HasStatus = true;
            Rows.Clear();
            FiveRows.Clear();
            _rowsDate = null;
            return;
        }

        var utcNow = DateTime.UtcNow;
        var now = schedule.ToLocal(utcNow);
        var date = DateOnly.FromDateTime(now.DateTime);
        var today = schedule.GetDay(date);
        var culture = CultureInfo.CurrentCulture;

        LocationName = schedule.Location.DisplayName;
        City = schedule.Location.DisplayName.Split(',')[0].Trim();
        ZoneShort = Format.Offset(now.Offset);
        var pcOffset = TimeZoneInfo.Local.GetUtcOffset(utcNow);
        TimeZoneNote = now.Offset == pcOffset ? "" : $"Times in {schedule.Location.TimeZoneId} ({Format.Offset(now.Offset)})";
        Clock = now.ToString(use24 ? "HH:mm:ss" : "h:mm:ss tt", culture);
        ClockShort = now.ToString(use24 ? "HH:mm" : "h:mm tt", culture);
        Gregorian = now.ToString("dddd, d MMMM yyyy", culture);
        GregorianShort = now.ToString("ddd d MMM", culture);
        Hijri = today.Hijri?.ToString() ?? "";
        Status = today.IsComplete ? "" : "Some times cannot be calculated for this location and date. See Settings › Fine-tune › High latitudes.";
        HasStatus = Status.Length > 0;
        MethodNote = $"{CalculationMethods.DisplayName(schedule.Parameters.Method)} · {(schedule.Parameters.Madhab == Madhab.Hanafi ? "Hanafi Asr" : "Standard Asr")}";

        // After Isha the list switches to tomorrow, so the next prayer (Fajr) is in it.
        var next = schedule.NextPrayer(utcNow);
        var listDate = next is { } nx && DateOnly.FromDateTime(nx.Time.DateTime) > date ? DateOnly.FromDateTime(nx.Time.DateTime) : date;
        var listDay = listDate == date ? today : schedule.GetDay(listDate);
        TimesHeading = listDate == date ? "Today's prayer times" : "Tomorrow's prayer times";

        if (_rowsDate != listDate || !ReferenceEquals(_rowsSchedule, schedule) || _rowsUse24 != use24)
        {
            Rows.Clear();
            FiveRows.Clear();
            foreach (var p in Shown)
            {
                var t = listDay.Get(p);
                var row = new PrayerRow { Prayer = p, Time = t, TimeText = Format.Time(t, use24) };
                Rows.Add(row);
                if (p != Prayer.Sunrise)
                {
                    FiveRows.Add(row);
                }
            }

            _rowsDate = listDate;
            _rowsSchedule = schedule;
            _rowsUse24 = use24;
        }

        var current = schedule.CurrentPrayer(utcNow);

        // Fajr's time ends at sunrise: between sunrise and Dhuhr no prayer is "current".
        if (current is { Prayer: Prayer.Fajr } && today.Get(Prayer.Sunrise) is { } sunrise
            && utcNow >= sunrise.UtcDateTime && next is { } upcoming && upcoming.Utc > sunrise.UtcDateTime)
        {
            current = new PrayerTime(Prayer.Sunrise, sunrise);
        }
        foreach (var row in Rows)
        {
            row.State = next is { } n && row.Prayer == n.Prayer && row.Time == n.Time ? PrayerState.Next
                : current is { } c && row.Prayer == c.Prayer && row.Time == c.Time ? PrayerState.Current
                : row.Time is { } rt && rt.UtcDateTime <= utcNow ? PrayerState.Passed
                : PrayerState.Upcoming;
        }

        if (next is { } np)
        {
            var left = np.Utc - utcNow;
            NextName = PrayerNames.English(np.Prayer);
            NextArabic = PrayerNames.Arabic(np.Prayer);
            NextTime = Format.Time(np.Time, use24);
            NextCountdown = Format.Countdown(left);
            NextCountdownCompact = Format.CompactCountdown(left);
            NextShort = $"{NextName} in {Format.ShortCountdown(left)}";
            if (current is { } cp && np.Utc > cp.Utc)
            {
                Progress = Math.Clamp((utcNow - cp.Utc).TotalSeconds / (np.Utc - cp.Utc).TotalSeconds, 0, 1);
                CurrentTitle = cp.Prayer == Prayer.Sunrise ? "After sunrise" : $"{PrayerNames.English(cp.Prayer)} time";
                CurrentDetail = cp.Prayer == Prayer.Sunrise
                    ? $"Fajr ended at sunrise, {Format.Time(cp.Time, use24)}. {NextName} begins in {Format.ShortCountdown(left)}."
                    : $"Began {Format.Time(cp.Time, use24)}. {NextName} begins in {Format.ShortCountdown(left)}.";
            }
            else
            {
                // No earlier time to measure from (some times could not be calculated): show no progress
                // rather than a stale value.
                Progress = 0;
                CurrentTitle = CurrentDetail = "";
            }
        }
        else
        {
            NextName = NextArabic = NextTime = NextCountdown = NextCountdownCompact = NextShort = "";
            Progress = 0;
        }

        UpdateRamadan(schedule, today, utcNow, use24);
        UpdateInfoLine(today, use24);
        UpdateMuteNote(utcNow);
    }

    private void UpdateAdhanStatus()
    {
        var adhan = _app.Settings.Adhan;
        AdhanOn = adhan.Enabled;
        // Only on or off: which recording plays for each prayer belongs in Settings.
        AdhanStatus = adhan.Enabled ? "Adhan On" : "Adhan Off";
    }

    private void UpdateRamadan(PrayerScheduleService schedule, DaySchedule today, DateTime utcNow, bool use24)
    {
        // During Ramadan, after Iftar the next fast's Sehri is tomorrow's.
        var tomorrow = schedule.GetDay(today.Date.AddDays(1));
        IsRamadan = today.IsRamadan || tomorrow.IsRamadan;

        if (!IsRamadan)
        {
            var period = schedule.Hijri.GetCurrentOrNextRamadan(today.Date);
            var days = period.FirstDay.DayNumber - today.Date.DayNumber;
            RamadanTeaser = days == 1 ? "Begins tomorrow" : $"Begins in {days} days";
            RamadanTeaserDate = $"{period.FirstDay.ToString("dddd d MMMM yyyy", CultureInfo.CurrentCulture)} · {period.HijriYear} AH";
            FastLabel = FastCountdown = FastCountdownCompact = RamadanDay = "";
            return;
        }

        var afterIftar = today.Iftar is { } iftarToday && utcNow >= iftarToday.UtcDateTime;
        var fastDay = today.IsRamadan && !afterIftar ? today : tomorrow;

        RamadanDay = fastDay.RamadanDay is { } d ? $"Ramadan · day {d}" : "Ramadan ended";
        SehriTime = Format.Time(fastDay.SehriEnd, use24);
        IftarTime = Format.Time(fastDay.Iftar, use24);

        if (fastDay.RamadanDay is null)
        {
            FastLabel = FastCountdown = FastCountdownCompact = "";
        }
        else if (fastDay.SehriEnd is { } sehri && utcNow < sehri.UtcDateTime)
        {
            FastLabel = "Sehri ends in";
            FastCountdown = Format.Countdown(sehri.UtcDateTime - utcNow);
            FastCountdownCompact = Format.CompactCountdown(sehri.UtcDateTime - utcNow);
        }
        else if (fastDay.Iftar is { } iftar)
        {
            FastLabel = "Iftar in";
            FastCountdown = Format.Countdown(iftar.UtcDateTime - utcNow);
            FastCountdownCompact = Format.CompactCountdown(iftar.UtcDateTime - utcNow);
        }
    }

    private void UpdateInfoLine(DaySchedule today, bool use24)
    {
        var parts = new List<string>();
        if (today.Date.DayOfWeek == DayOfWeek.Friday)
        {
            parts.Add($"Jumu'ah today · Dhuhr {Format.Time(today.Get(Prayer.Dhuhr), use24)}");
        }

        parts.Add($"Sunrise {Format.Time(today.Get(Prayer.Sunrise), use24)}");
        parts.Add($"Sunset {Format.Time(today.Get(Prayer.Maghrib), use24)}");
        if (!IsRamadan && RamadanTeaser.Length > 0)
        {
            parts.Add($"Ramadan {RamadanTeaser.Replace("Begins ", "", StringComparison.Ordinal)}");
        }

        InfoLine = string.Join("  ·  ", parts);
    }

    private void UpdateMuteNote(DateTime utcNow)
    {
        if (!_app.Settings.Adhan.Enabled)
        {
            MuteNote = "Adhan is turned off";
        }
        else if (_app.MutedUntilUtc is { } until && until > utcNow)
        {
            MuteNote = $"Muted until {Format.Time(_app.Schedule?.ToLocal(until), _app.Settings.Use24HourClock)}";
        }
        else if (_app.IsNextAdhanSkipped)
        {
            MuteNote = "Next Adhan muted";
        }
        else
        {
            MuteNote = "";
        }
    }
}

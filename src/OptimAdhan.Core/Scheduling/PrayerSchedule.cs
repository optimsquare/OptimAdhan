using OptimAdhan.Core.Calculation;
using OptimAdhan.Core.Hijri;
using OptimAdhan.Core.Location;
using OptimAdhan.Core.Settings;

namespace OptimAdhan.Core.Scheduling;

/// <summary>A prayer time expressed in the location's own time zone.</summary>
public readonly record struct PrayerTime(Prayer Prayer, DateTimeOffset Time)
{
    public DateTime Utc => Time.UtcDateTime;
}

/// <summary>Everything shown for one civil day at the selected location.</summary>
public sealed record DaySchedule
{
    public required DateOnly Date { get; init; }

    public required HijriDate? Hijri { get; init; }

    /// <summary>Fajr, Sunrise, Dhuhr, Asr, Maghrib and Isha in order. Missing entries could not be calculated.</summary>
    public required IReadOnlyList<PrayerTime> Times { get; init; }

    /// <summary>End of Sehri (Imsak): Fajr minus the configured minutes.</summary>
    public required DateTimeOffset? SehriEnd { get; init; }

    /// <summary>Iftar is at Maghrib.</summary>
    public DateTimeOffset? Iftar => Get(Prayer.Maghrib);

    /// <summary>Day of Ramadan (1–30), or null outside Ramadan.</summary>
    public required int? RamadanDay { get; init; }

    public bool IsRamadan => RamadanDay is not null;

    public bool IsComplete => Times.Count == 6;

    public DateTimeOffset? Get(Prayer prayer)
    {
        foreach (var t in Times)
        {
            if (t.Prayer == prayer)
            {
                return t.Time;
            }
        }

        return null;
    }
}

/// <summary>
/// Builds day, month and Ramadan schedules from one calculation engine, so the
/// daily view, the timetable and the Imsakia always agree. All "today" logic
/// uses the location's time zone and all instants are UTC, so daylight saving
/// transitions and PC time zone changes cannot shift prayer times.
/// </summary>
public sealed class PrayerScheduleService
{
    public static readonly Prayer[] FivePrayers = [Prayer.Fajr, Prayer.Dhuhr, Prayer.Asr, Prayer.Maghrib, Prayer.Isha];

    private static readonly Prayer[] AllTimes =
        [Prayer.Fajr, Prayer.Sunrise, Prayer.Dhuhr, Prayer.Asr, Prayer.Maghrib, Prayer.Isha];

    private readonly CalculationParameters _parameters;
    private readonly HijriCalendarService _hijri;
    private readonly int _imsakMinutes;
    private readonly Dictionary<DateOnly, DaySchedule> _cache = [];

    public PrayerScheduleService(LocationInfo location, CalculationSettings calculation, RamadanSettings ramadan)
    {
        Location = location ?? throw new ArgumentNullException(nameof(location));
        TimeZone = TimeZones.Find(location.TimeZoneId);
        _parameters = calculation.ToParameters(location.Coordinates);
        _hijri = new HijriCalendarService(calculation.HijriDayOffset);
        _imsakMinutes = ramadan.ImsakMinutesBeforeFajr;
    }

    public LocationInfo Location { get; }

    public TimeZoneInfo TimeZone { get; }

    public CalculationParameters Parameters => _parameters;

    public HijriCalendarService Hijri => _hijri;

    /// <summary>The civil date at the location for a UTC instant.</summary>
    public DateOnly LocalDate(DateTime utcNow) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(EnsureUtc(utcNow), TimeZone));

    public DateTimeOffset ToLocal(DateTime utc) => TimeZoneInfo.ConvertTime(new DateTimeOffset(EnsureUtc(utc)), TimeZone);

    /// <summary>Day schedules are immutable for a given service, so a few recent days are cached.</summary>
    public DaySchedule GetDay(DateOnly date)
    {
        lock (_cache)
        {
            if (_cache.TryGetValue(date, out var cached))
            {
                return cached;
            }
        }

        var day = CalculateDay(date);
        lock (_cache)
        {
            if (_cache.Count >= 64)
            {
                _cache.Clear();
            }

            _cache[date] = day;
        }

        return day;
    }

    private DaySchedule CalculateDay(DateOnly date)
    {
        HijriDate? hijri = HijriCalendarService.IsSupported(date) ? _hijri.ToHijri(date) : null;
        int? ramadanDay = hijri is { IsRamadan: true } h ? h.Day : null;

        var times = PrayerTimes.Calculate(Location.Coordinates, date, ParametersFor(ramadanDay is not null));
        var list = new List<PrayerTime>(6);
        foreach (var prayer in AllTimes)
        {
            if (times.TimeFor(prayer) is { } utc)
            {
                list.Add(new PrayerTime(prayer, ToLocal(utc)));
            }
        }

        return new DaySchedule
        {
            Date = date,
            Hijri = hijri,
            Times = list,
            SehriEnd = times.Fajr is { } fajr ? ToLocal(fajr.AddMinutes(-_imsakMinutes)) : null,
            RamadanDay = ramadanDay,
        };
    }

    /// <summary>
    /// The Umm al-Qura method sets Isha 120 minutes after Maghrib during Ramadan
    /// instead of 90 (as AlAdhan and the official Makkah timetable do; adhan-js does not).
    /// </summary>
    private CalculationParameters ParametersFor(bool isRamadan) =>
        isRamadan && _parameters.Method == CalculationMethod.UmmAlQura
            ? _parameters with { IshaInterval = 120 }
            : _parameters;

    public IReadOnlyList<DaySchedule> GetDays(DateOnly start, int count) =>
        Enumerable.Range(0, count).Select(i => GetDay(start.AddDays(i))).ToList();

    public IReadOnlyList<DaySchedule> GetMonth(int year, int month) =>
        GetDays(new DateOnly(year, month, 1), DateTime.DaysInMonth(year, month));

    /// <summary>Imsakia: every day of the Ramadan containing, or following, <paramref name="date"/>.</summary>
    public (RamadanPeriod Period, IReadOnlyList<DaySchedule> Days) GetRamadan(DateOnly date)
    {
        var period = _hijri.GetCurrentOrNextRamadan(date);
        return (period, GetDays(period.FirstDay, period.Days));
    }

    /// <summary>The next of the five prayers strictly after <paramref name="utcNow"/> (Sunrise excluded).</summary>
    public PrayerTime? NextPrayer(DateTime utcNow)
    {
        utcNow = EnsureUtc(utcNow);
        var date = LocalDate(utcNow);
        for (var i = 0; i < 3; i++)
        {
            foreach (var t in GetDay(date.AddDays(i)).Times)
            {
                if (t.Prayer != Prayer.Sunrise && t.Utc > utcNow)
                {
                    return t;
                }
            }
        }

        return null;
    }

    /// <summary>The most recent of the five prayers at or before <paramref name="utcNow"/>.</summary>
    public PrayerTime? CurrentPrayer(DateTime utcNow)
    {
        utcNow = EnsureUtc(utcNow);
        var date = LocalDate(utcNow);
        for (var i = 0; i < 3; i++)
        {
            var times = GetDay(date.AddDays(-i)).Times;
            for (var j = times.Count - 1; j >= 0; j--)
            {
                if (times[j].Prayer != Prayer.Sunrise && times[j].Utc <= utcNow)
                {
                    return times[j];
                }
            }
        }

        return null;
    }

    internal static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}

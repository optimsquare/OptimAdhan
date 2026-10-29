using System.Globalization;

namespace OptimAdhan.Core.Hijri;

public readonly record struct HijriDate(int Year, int Month, int Day)
{
    private static readonly string[] MonthNames =
    [
        "Muharram", "Safar", "Rabi al-Awwal", "Rabi al-Thani", "Jumada al-Awwal", "Jumada al-Thani",
        "Rajab", "Sha'ban", "Ramadan", "Shawwal", "Dhu al-Qi'dah", "Dhu al-Hijjah",
    ];

    private static readonly string[] ArabicMonthNames =
    [
        "مُحَرَّم", "صَفَر", "رَبِيع الأَوَّل", "رَبِيع الثَّانِي", "جُمَادَى الأُولَى", "جُمَادَى الآخِرَة",
        "رَجَب", "شَعْبَان", "رَمَضَان", "شَوَّال", "ذُو القَعْدَة", "ذُو الحِجَّة",
    ];

    public const int Ramadan = 9;

    public string MonthName => MonthNames[Month - 1];

    public string ArabicMonthName => ArabicMonthNames[Month - 1];

    public bool IsRamadan => Month == Ramadan;

    public override string ToString() => $"{Day} {MonthName} {Year} AH";
}

/// <summary>Gregorian date range of Ramadan in a given Hijri year.</summary>
public readonly record struct RamadanPeriod(int HijriYear, DateOnly FirstDay, int Days)
{
    public DateOnly LastDay => FirstDay.AddDays(Days - 1);

    public bool Contains(DateOnly date) => date >= FirstDay && date <= LastDay;

    /// <summary>1-based day of Ramadan, or null when <paramref name="date"/> is outside it.</summary>
    public int? DayOf(DateOnly date) => Contains(date) ? date.DayNumber - FirstDay.DayNumber + 1 : null;
}

/// <summary>
/// Hijri dates from the Umm al-Qura calendar built into .NET (the official
/// tabulated calendar of Saudi Arabia), so no network call is needed.
/// Islamic months begin on local moonsighting, which may differ by a day or two
/// from any computed calendar, so users can shift dates by <c>dayOffset</c>.
/// </summary>
public sealed class HijriCalendarService
{
    public const int MaxOffset = 2;

    private static readonly UmAlQuraCalendar Calendar = new();

    private readonly int _dayOffset;

    public HijriCalendarService(int dayOffset = 0)
    {
        if (Math.Abs(dayOffset) > MaxOffset)
        {
            throw new ArgumentOutOfRangeException(nameof(dayOffset), dayOffset, $"Offset must be within ±{MaxOffset} days.");
        }

        _dayOffset = dayOffset;
    }

    public static DateOnly MinSupportedDate { get; } = DateOnly.FromDateTime(Calendar.MinSupportedDateTime).AddDays(MaxOffset);

    public static DateOnly MaxSupportedDate { get; } = DateOnly.FromDateTime(Calendar.MaxSupportedDateTime).AddDays(-MaxOffset);

    public static bool IsSupported(DateOnly date) => date >= MinSupportedDate && date <= MaxSupportedDate;

    /// <summary>Hijri date for a civil (local) Gregorian date.</summary>
    public HijriDate ToHijri(DateOnly date)
    {
        EnsureSupported(date);
        var shifted = date.AddDays(_dayOffset).ToDateTime(TimeOnly.MinValue);
        return new HijriDate(Calendar.GetYear(shifted), Calendar.GetMonth(shifted), Calendar.GetDayOfMonth(shifted));
    }

    public DateOnly ToGregorian(HijriDate hijri)
    {
        var date = DateOnly.FromDateTime(Calendar.ToDateTime(hijri.Year, hijri.Month, hijri.Day, 0, 0, 0, 0));
        return date.AddDays(-_dayOffset);
    }

    public RamadanPeriod GetRamadan(int hijriYear)
    {
        var first = ToGregorian(new HijriDate(hijriYear, HijriDate.Ramadan, 1));
        return new RamadanPeriod(hijriYear, first, Calendar.GetDaysInMonth(hijriYear, HijriDate.Ramadan));
    }

    /// <summary>
    /// The Ramadan that contains <paramref name="date"/>, otherwise the next one.
    /// Used so the Imsakia view always has a meaningful schedule to show.
    /// </summary>
    public RamadanPeriod GetCurrentOrNextRamadan(DateOnly date)
    {
        var hijri = ToHijri(date);
        var year = hijri.Month > HijriDate.Ramadan ? hijri.Year + 1 : hijri.Year;
        return GetRamadan(year);
    }

    private static void EnsureSupported(DateOnly date)
    {
        if (!IsSupported(date))
        {
            throw new ArgumentOutOfRangeException(nameof(date), date,
                $"Hijri conversion supports {MinSupportedDate:yyyy-MM-dd} to {MaxSupportedDate:yyyy-MM-dd}.");
        }
    }
}

using OptimAdhan.Core.Hijri;

namespace OptimAdhan.Core.Scheduling;

public enum TimetableKind
{
    /// <summary>Today and the following days.</summary>
    Rolling,

    /// <summary>Today and the rest of Ramadan, with Sehri and Iftar.</summary>
    Ramadan,
}

/// <summary>The days the dashboard's timetable shows, worked out from the location's own date.</summary>
public sealed record TimetableWindow(
    TimetableKind Kind,
    DateOnly Today,
    DateOnly Start,
    IReadOnlyList<DaySchedule> Days,
    RamadanPeriod? Ramadan)
{
    public bool IsAtToday => Start == Today;

    /// <summary>Days from today to the first day shown (negative when browsing back).</summary>
    public int Offset => Start.DayNumber - Today.DayNumber;

    public bool HasEarlier => Ramadan is { } r ? Start > r.FirstDay : Offset > -DashboardTimetable.MaxBrowseDays;

    /// <summary>Whether days exist beyond the <paramref name="shown"/> rows currently on screen.</summary>
    public bool HasLater(int shown) => Ramadan is { } r ? Start.AddDays(shown) <= r.LastDay : Offset < DashboardTimetable.MaxBrowseDays;
}

/// <summary>
/// The dashboard's rolling timetable. It always starts from today at the prayer location
/// (not the PC's date), so it moves on by itself at the location's midnight. During Ramadan
/// it becomes the Imsakia from today to the last day of the month. Nothing is ever removed:
/// past dates are simply not the default view, and stay available by browsing or in the calendar.
/// </summary>
public static class DashboardTimetable
{
    public const int MaxDays = 7;

    /// <summary>How far the rolling view can be browsed either way (the calendar covers the rest).</summary>
    public const int MaxBrowseDays = 366;

    public static TimetableWindow Build(PrayerScheduleService schedule, DateTime utcNow, int dayOffset = 0)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        var today = schedule.LocalDate(utcNow);

        if (schedule.GetDay(today).IsRamadan)
        {
            var period = schedule.GetRamadan(today).Period;
            var start = Clamp(today.AddDays(dayOffset), period.FirstDay, period.LastDay);
            var count = Math.Min(MaxDays, period.LastDay.DayNumber - start.DayNumber + 1);
            return new TimetableWindow(TimetableKind.Ramadan, today, start, schedule.GetDays(start, count), period);
        }

        var rollingStart = today.AddDays(Math.Clamp(dayOffset, -MaxBrowseDays, MaxBrowseDays));
        return new TimetableWindow(TimetableKind.Rolling, today, rollingStart, schedule.GetDays(rollingStart, MaxDays), null);
    }

    private static DateOnly Clamp(DateOnly date, DateOnly min, DateOnly max) =>
        date < min ? min : date > max ? max : date;
}

using OptimAdhan.Core.Location;
using OptimAdhan.Core.Scheduling;
using OptimAdhan.Core.Settings;

namespace OptimAdhan.Core.Tests.Scheduling;

public class DashboardTimetableTests
{
    private static PrayerScheduleService For(LocationInfo location) =>
        new(location, new CalculationSettings(), new RamadanSettings());

    private static DateTime Utc(int y, int m, int d, int h, int min = 0) => new(y, m, d, h, min, 0, DateTimeKind.Utc);

    [Fact]
    public void OutsideRamadanShowsSevenDaysStartingToday()
    {
        // 29 October 2026, 8 PM in New York (00:00 UTC on the 30th).
        var table = DashboardTimetable.Build(For(TestLocations.NewYork), Utc(2026, 10, 30, 0));

        Assert.Equal(TimetableKind.Rolling, table.Kind);
        Assert.Equal(new DateOnly(2026, 10, 29), table.Today);
        Assert.Equal(new DateOnly(2026, 10, 29), table.Days[0].Date);
        Assert.Equal(new DateOnly(2026, 11, 4), table.Days[^1].Date);
        Assert.Equal(7, table.Days.Count);
        Assert.True(table.IsAtToday);
    }

    [Fact]
    public void RollsForwardAtTheLocationsMidnight()
    {
        var schedule = For(TestLocations.NewYork);

        // 23:59 and 00:01 New York time (EDT, UTC−4) on the night of 29–30 October.
        var before = DashboardTimetable.Build(schedule, Utc(2026, 10, 30, 3, 59));
        var after = DashboardTimetable.Build(schedule, Utc(2026, 10, 30, 4, 1));

        Assert.Equal(new DateOnly(2026, 10, 29), before.Days[0].Date);
        Assert.Equal(new DateOnly(2026, 10, 30), after.Days[0].Date);
        Assert.Equal(new DateOnly(2026, 11, 5), after.Days[^1].Date);
    }

    [Fact]
    public void FollowsTheLocationsDateNotUtcOrThePcs()
    {
        // 20:00 UTC on 29 October is already the 30th in Dhaka (UTC+6) but still the 29th in New York.
        var instant = Utc(2026, 10, 29, 20);

        Assert.Equal(new DateOnly(2026, 10, 30), DashboardTimetable.Build(For(TestLocations.Dhaka), instant).Today);
        Assert.Equal(new DateOnly(2026, 10, 29), DashboardTimetable.Build(For(TestLocations.NewYork), instant).Today);
    }

    [Fact]
    public void BrowsingReachesPastAndFutureDaysWithoutChangingToday()
    {
        var schedule = For(TestLocations.NewYork);
        var now = Utc(2026, 10, 30, 0);

        var earlier = DashboardTimetable.Build(schedule, now, dayOffset: -7);
        var later = DashboardTimetable.Build(schedule, now, dayOffset: 14);

        Assert.Equal(new DateOnly(2026, 10, 22), earlier.Days[0].Date);
        Assert.Equal(new DateOnly(2026, 11, 12), later.Days[0].Date);
        Assert.Equal(new DateOnly(2026, 10, 29), later.Today);
        Assert.False(later.IsAtToday);
        Assert.True(later.HasLater(7) && later.HasEarlier);
    }

    [Fact]
    public void DuringRamadanShowsTodayAndTheRemainingRamadanDays()
    {
        var schedule = For(TestLocations.Makkah);
        var (period, _) = schedule.GetRamadan(new DateOnly(2027, 1, 1));
        var today = period.FirstDay.AddDays(10); // Ramadan 11

        var table = DashboardTimetable.Build(schedule, NoonAt(schedule, today));

        Assert.Equal(TimetableKind.Ramadan, table.Kind);
        Assert.Equal(period, table.Ramadan);
        Assert.Equal(today, table.Days[0].Date);
        Assert.Equal(11, table.Days[0].RamadanDay);
        Assert.Equal(7, table.Days.Count);
        Assert.All(table.Days, d => Assert.NotNull(d.RamadanDay));
        Assert.All(table.Days, d => Assert.NotNull(d.SehriEnd));
    }

    [Fact]
    public void NearTheEndOfRamadanOnlyRamadanDaysAreShown()
    {
        var schedule = For(TestLocations.Makkah);
        var (period, _) = schedule.GetRamadan(new DateOnly(2027, 1, 1));

        var table = DashboardTimetable.Build(schedule, NoonAt(schedule, period.LastDay.AddDays(-2)));

        Assert.Equal(3, table.Days.Count);
        Assert.Equal(period.LastDay, table.Days[^1].Date);
        Assert.False(table.HasLater(3));
    }

    [Fact]
    public void RamadanBrowsingStaysWithinTheMonth()
    {
        var schedule = For(TestLocations.Makkah);
        var (period, _) = schedule.GetRamadan(new DateOnly(2027, 1, 1));
        var now = NoonAt(schedule, period.FirstDay.AddDays(3));

        var first = DashboardTimetable.Build(schedule, now, dayOffset: -30);
        var last = DashboardTimetable.Build(schedule, now, dayOffset: 60);

        Assert.Equal(period.FirstDay, first.Days[0].Date);
        Assert.False(first.HasEarlier);
        Assert.Equal(period.LastDay, last.Days[0].Date);
        Assert.Single(last.Days);
    }

    [Fact]
    public void TheDayAfterRamadanReturnsToTheRollingTimetable()
    {
        var schedule = For(TestLocations.Makkah);
        var (period, _) = schedule.GetRamadan(new DateOnly(2027, 1, 1));

        var table = DashboardTimetable.Build(schedule, NoonAt(schedule, period.LastDay.AddDays(1)));

        Assert.Equal(TimetableKind.Rolling, table.Kind);
        Assert.Null(table.Ramadan);
    }

    private static DateTime NoonAt(PrayerScheduleService schedule, DateOnly date) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(new TimeOnly(12, 0)), schedule.TimeZone);
}

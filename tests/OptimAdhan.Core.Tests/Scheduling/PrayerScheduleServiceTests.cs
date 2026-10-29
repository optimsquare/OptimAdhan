using OptimAdhan.Core.Calculation;
using OptimAdhan.Core.Location;
using OptimAdhan.Core.Scheduling;
using OptimAdhan.Core.Settings;

namespace OptimAdhan.Core.Tests.Scheduling;

public class PrayerScheduleServiceTests
{
    private static PrayerScheduleService For(LocationInfo location, CalculationSettings? calc = null, RamadanSettings? ramadan = null) =>
        new(location, calc ?? new CalculationSettings(), ramadan ?? new RamadanSettings());

    [Fact]
    public void TimeZoneIsResolvedFromCoordinates()
    {
        Assert.Equal("Asia/Riyadh", TestLocations.Makkah.TimeZoneId);
        Assert.Equal("America/New_York", TestLocations.NewYork.TimeZoneId);
        Assert.Equal("Asia/Dhaka", TestLocations.Dhaka.TimeZoneId);
        Assert.Equal("Australia/Sydney", TestLocations.Sydney.TimeZoneId);
    }

    [Fact]
    public void TimesUseTheLocationsTimeZoneNotThePcs()
    {
        var day = For(TestLocations.Makkah).GetDay(new DateOnly(2026, 6, 21));

        Assert.True(day.IsComplete);
        Assert.All(day.Times, t => Assert.Equal(TimeSpan.FromHours(3), t.Time.Offset));
        var fajr = day.Get(Prayer.Fajr)!.Value;
        Assert.Equal(new DateOnly(2026, 6, 21), DateOnly.FromDateTime(fajr.DateTime));
        Assert.InRange(fajr.Hour, 3, 5);
    }

    [Fact]
    public void LocalDateFollowsTheLocationAcrossMidnight()
    {
        var sydney = For(TestLocations.Sydney);

        // 14:30 UTC on 1 March is 01:30 on 2 March in Sydney (UTC+11 in summer).
        Assert.Equal(new DateOnly(2026, 3, 2), sydney.LocalDate(new DateTime(2026, 3, 1, 14, 30, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void DaylightSavingChangeShiftsLocalClockButNotTheSun()
    {
        var ny = For(TestLocations.NewYork);
        var before = ny.GetDay(new DateOnly(2026, 3, 7));
        var after = ny.GetDay(new DateOnly(2026, 3, 8)); // US DST starts 02:00 local

        Assert.Equal(TimeSpan.FromHours(-5), before.Get(Prayer.Dhuhr)!.Value.Offset);
        Assert.Equal(TimeSpan.FromHours(-4), after.Get(Prayer.Dhuhr)!.Value.Offset);

        // In UTC, solar noon moves by well under a minute from one day to the next...
        var utcShift = after.Get(Prayer.Dhuhr)!.Value.UtcDateTime - before.Get(Prayer.Dhuhr)!.Value.UtcDateTime;
        Assert.InRange(utcShift.TotalHours, 23.98, 24.02);

        // ...while the wall clock jumps by about an hour.
        var wallShift = after.Get(Prayer.Dhuhr)!.Value.TimeOfDay - before.Get(Prayer.Dhuhr)!.Value.TimeOfDay;
        Assert.InRange(wallShift.TotalMinutes, 58, 62);
    }

    [Fact]
    public void PolarSummerIsResolvedByDefault()
    {
        var day = For(TestLocations.Tromso, new CalculationSettings { Method = CalculationMethod.MuslimWorldLeague })
            .GetDay(new DateOnly(2026, 6, 21));

        Assert.True(day.IsComplete);
    }

    [Fact]
    public void UnresolvedPolarTimesAreMissingNeverInvented()
    {
        var calc = new CalculationSettings
        {
            Method = CalculationMethod.MuslimWorldLeague,
            PolarCircleResolution = PolarCircleResolution.Unresolved,
        };
        var day = For(TestLocations.Tromso, calc).GetDay(new DateOnly(2026, 6, 21));

        Assert.False(day.IsComplete);
        Assert.Null(day.Get(Prayer.Sunrise));
        Assert.Null(day.SehriEnd);
        Assert.NotNull(day.Get(Prayer.Dhuhr)); // the sun still transits
    }

    [Fact]
    public void AutomaticHighLatitudeRuleUsesSeventhOfNightAbove48Degrees()
    {
        var oslo = LocationInfo.Create("Oslo", 59.9139, 10.7522, "NO", LocationSource.Manual);
        Assert.Equal(HighLatitudeRule.SeventhOfTheNight, For(oslo).Parameters.HighLatitudeRule);
        Assert.Equal(HighLatitudeRule.MiddleOfTheNight, For(TestLocations.Dhaka).Parameters.HighLatitudeRule);
    }

    [Fact]
    public void UserAdjustmentsShiftTimesByWholeMinutes()
    {
        var date = new DateOnly(2026, 6, 15);
        var plain = For(TestLocations.Dhaka).GetDay(date);
        var adjusted = For(TestLocations.Dhaka, new CalculationSettings { Adjustments = new(Fajr: 2, Isha: -3) }).GetDay(date);

        Assert.Equal(TimeSpan.FromMinutes(2), adjusted.Get(Prayer.Fajr) - plain.Get(Prayer.Fajr));
        Assert.Equal(TimeSpan.FromMinutes(-3), adjusted.Get(Prayer.Isha) - plain.Get(Prayer.Isha));
        Assert.Equal(plain.Get(Prayer.Dhuhr), adjusted.Get(Prayer.Dhuhr));
    }

    [Fact]
    public void SehriEndsTheConfiguredMinutesBeforeFajrAndIftarIsMaghrib()
    {
        var day = For(TestLocations.Dhaka, ramadan: new RamadanSettings { ImsakMinutesBeforeFajr = 15 })
            .GetDay(new DateOnly(2027, 2, 20));

        Assert.Equal(day.Get(Prayer.Fajr)!.Value.AddMinutes(-15), day.SehriEnd);
        Assert.Equal(day.Get(Prayer.Maghrib), day.Iftar);
    }

    [Fact]
    public void ImsakiaCoversTheWholeOfRamadan()
    {
        var (period, days) = For(TestLocations.Dhaka).GetRamadan(new DateOnly(2026, 10, 28));

        Assert.Equal(1448, period.HijriYear);
        Assert.Equal(new DateOnly(2027, 2, 8), period.FirstDay);
        Assert.Equal(period.Days, days.Count);
        Assert.Equal(Enumerable.Range(1, period.Days), days.Select(d => d.RamadanDay!.Value));
        Assert.All(days, d => Assert.True(d.IsComplete));
    }

    [Fact]
    public void DailyViewAndImsakiaAgree()
    {
        var schedule = For(TestLocations.NewYork);
        var (_, days) = schedule.GetRamadan(new DateOnly(2027, 2, 1));

        foreach (var d in days)
        {
            Assert.Equal(schedule.GetDay(d.Date).Times, d.Times);
        }
    }

    [Fact]
    public void UmmAlQuraIshaIs120MinutesAfterMaghribInRamadanOnly()
    {
        var schedule = For(TestLocations.Makkah, new CalculationSettings { Method = CalculationMethod.UmmAlQura });
        var ramadan = schedule.GetDay(new DateOnly(2027, 2, 20));
        var normal = schedule.GetDay(new DateOnly(2027, 4, 20));

        Assert.Equal(TimeSpan.FromMinutes(120), ramadan.Get(Prayer.Isha) - ramadan.Get(Prayer.Maghrib));
        Assert.Equal(TimeSpan.FromMinutes(90), normal.Get(Prayer.Isha) - normal.Get(Prayer.Maghrib));
    }

    [Fact]
    public void NextPrayerAfterIshaIsTomorrowsFajr()
    {
        var schedule = For(TestLocations.Dhaka);
        var today = schedule.GetDay(new DateOnly(2026, 10, 28));
        var afterIsha = today.Get(Prayer.Isha)!.Value.UtcDateTime.AddMinutes(1);

        var next = schedule.NextPrayer(afterIsha);

        Assert.Equal(Prayer.Fajr, next!.Value.Prayer);
        Assert.Equal(schedule.GetDay(new DateOnly(2026, 10, 29)).Get(Prayer.Fajr), next.Value.Time);
        Assert.Equal(Prayer.Isha, schedule.CurrentPrayer(afterIsha)!.Value.Prayer);
    }

    [Fact]
    public void NextPrayerSkipsSunrise()
    {
        var schedule = For(TestLocations.Dhaka);
        var day = schedule.GetDay(new DateOnly(2026, 10, 28));

        var next = schedule.NextPrayer(day.Get(Prayer.Fajr)!.Value.UtcDateTime.AddMinutes(1));

        Assert.Equal(Prayer.Dhuhr, next!.Value.Prayer);
    }

    [Theory]
    [InlineData("US", CalculationMethod.NorthAmerica)]
    [InlineData("sa", CalculationMethod.UmmAlQura)]
    [InlineData("BD", CalculationMethod.Karachi)]
    [InlineData("GB", CalculationMethod.MuslimWorldLeague)]
    [InlineData(null, CalculationMethod.MuslimWorldLeague)]
    public void SuggestsMethodByCountryLikeTheWebApp(string? country, CalculationMethod expected) =>
        Assert.Equal(expected, CalculationMethods.ForCountry(country));
}

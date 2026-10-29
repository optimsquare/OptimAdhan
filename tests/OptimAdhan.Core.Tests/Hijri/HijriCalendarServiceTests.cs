using OptimAdhan.Core.Hijri;

namespace OptimAdhan.Core.Tests.Hijri;

public class HijriCalendarServiceTests
{
    [Fact]
    public void ConvertsKnownDates()
    {
        var service = new HijriCalendarService();
        Assert.Equal(new HijriDate(1447, 9, 1), service.ToHijri(new DateOnly(2026, 2, 18)));
        Assert.Equal(new HijriDate(1448, 9, 1), service.ToHijri(new DateOnly(2027, 2, 8)));
    }

    [Fact]
    public void OffsetShiftsDatesForLocalMoonsighting()
    {
        var date = new DateOnly(2027, 2, 8);
        Assert.Equal(new HijriDate(1448, 8, 30), new HijriCalendarService(-1).ToHijri(date)); // Sha'ban 1448 has 30 days
        Assert.Equal(new DateOnly(2027, 2, 9), new HijriCalendarService(-1).GetRamadan(1448).FirstDay);
        Assert.Equal(new DateOnly(2027, 2, 7), new HijriCalendarService(1).GetRamadan(1448).FirstDay);
    }

    [Fact]
    public void CurrentOrNextRamadan()
    {
        var service = new HijriCalendarService();
        Assert.Equal(1447, service.GetCurrentOrNextRamadan(new DateOnly(2026, 3, 1)).HijriYear); // during
        Assert.Equal(1448, service.GetCurrentOrNextRamadan(new DateOnly(2026, 10, 28)).HijriYear); // after
        Assert.Equal(1447, service.GetCurrentOrNextRamadan(new DateOnly(2026, 1, 10)).HijriYear); // before
    }

    [Fact]
    public void RamadanPeriodDayNumbers()
    {
        var period = new RamadanPeriod(1448, new DateOnly(2027, 2, 8), 29);
        Assert.Equal(1, period.DayOf(new DateOnly(2027, 2, 8)));
        Assert.Equal(29, period.DayOf(period.LastDay));
        Assert.Null(period.DayOf(new DateOnly(2027, 3, 9)));
    }

    [Fact]
    public void RejectsOffsetsBeyondTwoDays() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new HijriCalendarService(3));
}

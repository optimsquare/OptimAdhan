using OptimAdhan.Core.Calculation;
using OptimAdhan.Core.Scheduling;
using OptimAdhan.Core.Settings;

namespace OptimAdhan.Core.Tests.Scheduling;

public class AlertPlannerTests
{
    private static readonly PrayerScheduleService Dhaka =
        new(TestLocations.Dhaka, new CalculationSettings(), new RamadanSettings());

    /// <summary>Midnight in Dhaka (UTC+6, no DST) as a UTC instant.</summary>
    private static DateTime LocalMidnightUtc(DateOnly date) =>
        DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc).AddHours(-6);

    private static IReadOnlyList<ScheduledAlert> PlanDay(DateOnly date, AdhanSettings? adhan = null, RamadanSettings? ramadan = null)
    {
        var start = LocalMidnightUtc(date);
        return AlertPlanner.Plan(Dhaka, adhan ?? new AdhanSettings(), ramadan ?? new RamadanSettings(), start, start.AddDays(1));
    }

    [Fact]
    public void PlansOneAdhanPerPrayerAtThePrayerTime()
    {
        var date = new DateOnly(2026, 10, 28);
        var alerts = PlanDay(date);
        var day = Dhaka.GetDay(date);

        var adhans = alerts.Where(a => a.Kind == AlertKind.Adhan).ToList();
        Assert.Equal(PrayerScheduleService.FivePrayers, adhans.Select(a => a.Prayer!.Value));
        Assert.All(adhans, a => Assert.Equal(day.Get(a.Prayer!.Value)!.Value.UtcDateTime, a.Utc));
        Assert.DoesNotContain(alerts, a => a.Kind is AlertKind.SehriReminder or AlertKind.IftarReminder);
    }

    [Fact]
    public void FajrUsesItsOwnSound()
    {
        var alerts = PlanDay(new DateOnly(2026, 10, 28), new AdhanSettings { DefaultSoundId = "madina", FajrSoundId = "fajr" });

        Assert.Equal("fajr", alerts.Single(a => a.Prayer == Prayer.Fajr).SoundId);
        Assert.Equal("madina", alerts.Single(a => a.Prayer == Prayer.Asr).SoundId);
    }

    [Fact]
    public void DisabledPrayersAndMasterSwitchAreRespected()
    {
        var date = new DateOnly(2026, 10, 28);
        var noAsr = PlanDay(date, new AdhanSettings { Asr = new PrayerAlert { AdhanEnabled = false } });
        Assert.DoesNotContain(noAsr, a => a.Kind == AlertKind.Adhan && a.Prayer == Prayer.Asr);
        Assert.Equal(4, noAsr.Count(a => a.Kind == AlertKind.Adhan));

        Assert.Empty(PlanDay(date, new AdhanSettings { Enabled = false }));
    }

    [Fact]
    public void PrayerRemindersComeBeforeThePrayer()
    {
        var alerts = PlanDay(new DateOnly(2026, 10, 28), new AdhanSettings { Dhuhr = new PrayerAlert { ReminderMinutesBefore = 15 } });

        var reminder = alerts.Single(a => a.Kind == AlertKind.PrayerReminder);
        var adhan = alerts.Single(a => a.Kind == AlertKind.Adhan && a.Prayer == Prayer.Dhuhr);
        Assert.Equal(adhan.Utc.AddMinutes(-15), reminder.Utc);
    }

    [Fact]
    public void RamadanAddsSehriAndIftarCountdowns()
    {
        var date = new DateOnly(2027, 2, 20);
        var alerts = PlanDay(date);
        var day = Dhaka.GetDay(date);

        var sehri = alerts.Where(a => a.Kind == AlertKind.SehriReminder).ToList();
        Assert.Equal(AlertPlanner.SehriReminderMinutes, sehri.Select(a => a.MinutesBefore));
        Assert.Equal(day.SehriEnd!.Value.UtcDateTime, sehri.Single(a => a.MinutesBefore == 0).Utc);
        Assert.Equal("sehri_end", sehri.Single(a => a.MinutesBefore == 0).SoundId);

        var iftar = alerts.Where(a => a.Kind == AlertKind.IftarReminder).ToList();
        Assert.Equal(AlertPlanner.IftarReminderMinutes, iftar.Select(a => a.MinutesBefore));
        Assert.Equal(day.Iftar!.Value.UtcDateTime.AddMinutes(-1), iftar.Single(a => a.MinutesBefore == 1).Utc);

        Assert.DoesNotContain(PlanDay(date, ramadan: new RamadanSettings { VoiceRemindersEnabled = false }),
            a => a.Kind is AlertKind.SehriReminder or AlertKind.IftarReminder);
    }

    [Fact]
    public void AlertsAreOrderedWithUniqueKeysAndWithinTheWindow()
    {
        var start = LocalMidnightUtc(new DateOnly(2027, 2, 20));
        var alerts = AlertPlanner.Plan(Dhaka, new AdhanSettings(), new RamadanSettings(), start, start.AddDays(3));

        Assert.Equal(alerts.OrderBy(a => a.Utc), alerts);
        Assert.Equal(alerts.Count, alerts.Select(a => a.Key).Distinct().Count());
        Assert.All(alerts, a => Assert.InRange(a.Utc, start, start.AddDays(3)));
        Assert.Equal(15, alerts.Count(a => a.Kind == AlertKind.Adhan));
    }
}

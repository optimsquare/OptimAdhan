using OptimAdhan.Core.Calculation;
using OptimAdhan.Core.Scheduling;
using OptimAdhan.Core.Settings;

namespace OptimAdhan.Core.Tests.Scheduling;

public class AlertDispatcherTests
{
    private static readonly PrayerScheduleService Dhaka =
        new(TestLocations.Dhaka, new CalculationSettings(), new RamadanSettings());

    private static readonly DateOnly Date = new(2026, 10, 28);

    private static DateTime TimeOf(Prayer prayer) => Dhaka.GetDay(Date).Get(prayer)!.Value.UtcDateTime;

    private static AlertDispatcher Started(DateTime nowUtc)
    {
        var dispatcher = new AlertDispatcher();
        dispatcher.Configure(Dhaka, new AdhanSettings(), new RamadanSettings(), nowUtc);
        return dispatcher;
    }

    [Fact]
    public void FiresOnTimeExactlyOnce()
    {
        var asr = TimeOf(Prayer.Asr);
        var dispatcher = Started(asr.AddHours(-1));

        Assert.Empty(dispatcher.Poll(asr.AddSeconds(-1)));
        var due = dispatcher.Poll(asr.AddSeconds(3));
        Assert.Equal(AlertDelivery.Play, Assert.Single(due).Delivery);
        Assert.Equal(Prayer.Asr, due[0].Alert.Prayer);
        Assert.Empty(dispatcher.Poll(asr.AddSeconds(30)));
    }

    [Fact]
    public void StartingTheAppDoesNotReplayEarlierAlerts()
    {
        var dispatcher = Started(TimeOf(Prayer.Asr).AddMinutes(1));

        Assert.Empty(dispatcher.Poll(TimeOf(Prayer.Asr).AddMinutes(1)));
    }

    [Fact]
    public void WakingFromSleepGivesMissedNoticeWithoutSound()
    {
        var asr = TimeOf(Prayer.Asr);
        var dispatcher = Started(asr.AddHours(-1));

        var due = dispatcher.Poll(asr.AddMinutes(20)); // PC was asleep through Asr

        Assert.Equal(AlertDelivery.Missed, Assert.Single(due).Delivery);
    }

    [Fact]
    public void LongSleepDropsStaleAlerts()
    {
        var dhuhr = TimeOf(Prayer.Dhuhr);
        var dispatcher = Started(dhuhr.AddHours(-1));

        // Waking just before Maghrib: Dhuhr is hours old (dropped); Asr is the only recent miss.
        var due = dispatcher.Poll(TimeOf(Prayer.Maghrib).AddMinutes(-1));

        Assert.DoesNotContain(due, o => o.Alert.Prayer == Prayer.Dhuhr);
        Assert.All(due, o => Assert.NotEqual(AlertDelivery.Play, o.Delivery));
    }

    [Fact]
    public void SkipNextAdhanSilencesOnlyThatOne()
    {
        var asr = TimeOf(Prayer.Asr);
        var dispatcher = Started(asr.AddHours(-1));

        dispatcher.ToggleSkipNextAdhan(asr.AddHours(-1));
        Assert.True(dispatcher.IsNextAdhanSkipped(asr.AddHours(-1)));

        Assert.Equal(AlertDelivery.Silent, Assert.Single(dispatcher.Poll(asr)).Delivery);
        Assert.Equal(AlertDelivery.Play, Assert.Single(dispatcher.Poll(TimeOf(Prayer.Maghrib))).Delivery);
    }

    [Fact]
    public void ToggleSkipTwiceRestoresSound()
    {
        var asr = TimeOf(Prayer.Asr);
        var dispatcher = Started(asr.AddHours(-1));

        dispatcher.ToggleSkipNextAdhan(asr.AddHours(-1));
        dispatcher.ToggleSkipNextAdhan(asr.AddHours(-1));

        Assert.Equal(AlertDelivery.Play, Assert.Single(dispatcher.Poll(asr)).Delivery);
    }

    [Fact]
    public void MuteUntilSilencesEverythingInTheWindow()
    {
        var asr = TimeOf(Prayer.Asr);
        var dispatcher = Started(asr.AddHours(-1));
        dispatcher.MutedUntilUtc = asr.AddMinutes(10);

        Assert.Equal(AlertDelivery.Silent, Assert.Single(dispatcher.Poll(asr)).Delivery);
        Assert.Equal(AlertDelivery.Play, Assert.Single(dispatcher.Poll(TimeOf(Prayer.Maghrib))).Delivery);
    }

    [Fact]
    public void NextDueIsTheNextUnhandledAlert()
    {
        var asr = TimeOf(Prayer.Asr);
        var dispatcher = Started(asr.AddHours(-1));

        Assert.Equal(asr, dispatcher.NextDueUtc(asr.AddHours(-1)));
        dispatcher.Poll(asr);
        Assert.Equal(TimeOf(Prayer.Maghrib), dispatcher.NextDueUtc(asr));
    }

    [Fact]
    public void KeepsFiringAcrossDaysWithoutReconfiguring()
    {
        var dispatcher = Started(TimeOf(Prayer.Isha).AddMinutes(1));
        var fired = new List<AlertOccurrence>();

        // Simulate the app polling every 30 s for three days.
        var start = TimeOf(Prayer.Isha).AddMinutes(1);
        for (var t = start; t < start.AddDays(3); t = t.AddSeconds(30))
        {
            fired.AddRange(dispatcher.Poll(t));
        }

        Assert.Equal(15, fired.Count(o => o.Alert.Kind == AlertKind.Adhan));
        Assert.All(fired, o => Assert.Equal(AlertDelivery.Play, o.Delivery));
        Assert.Equal(fired.Count, fired.Select(o => o.Alert.Key).Distinct().Count());
    }

    [Fact]
    public void NoLocationMeansNothingIsScheduled()
    {
        var dispatcher = new AlertDispatcher();
        dispatcher.Configure(null, new AdhanSettings(), new RamadanSettings(), DateTime.UtcNow);

        Assert.Empty(dispatcher.Poll(DateTime.UtcNow.AddDays(1)));
        Assert.Null(dispatcher.NextDueUtc(DateTime.UtcNow));
    }
}

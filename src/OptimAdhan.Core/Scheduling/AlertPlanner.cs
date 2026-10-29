using OptimAdhan.Core.Calculation;
using OptimAdhan.Core.Settings;

namespace OptimAdhan.Core.Scheduling;

public enum AlertKind
{
    Adhan,

    /// <summary>Notification a configured number of minutes before a prayer.</summary>
    PrayerReminder,

    /// <summary>Ramadan countdown before Sehri ends; 0 minutes means Sehri has ended.</summary>
    SehriReminder,

    /// <summary>Ramadan countdown before Iftar.</summary>
    IftarReminder,
}

/// <summary>One thing that should happen at a moment in time.</summary>
public sealed record ScheduledAlert
{
    public required AlertKind Kind { get; init; }

    public required DateTime Utc { get; init; }

    /// <summary>The local date the alert belongs to.</summary>
    public required DateOnly Date { get; init; }

    public Prayer? Prayer { get; init; }

    public int MinutesBefore { get; init; }

    /// <summary>Sound to play: a sound library ID for Adhan, a reminder clip ID otherwise; null for silent.</summary>
    public string? SoundId { get; init; }

    /// <summary>A user-triggered sample ("Test alert now"), not a real prayer time.</summary>
    public bool IsTest { get; init; }

    /// <summary>Stable identity, used to make sure an alert never fires twice.</summary>
    public string Key => $"{Date:yyyy-MM-dd}:{Kind}:{Prayer}:{MinutesBefore}";
}

/// <summary>
/// Turns schedules and settings into a time-ordered list of alerts. Pure and
/// deterministic, so the timing logic is fully unit-testable; the app's
/// scheduler only has to wait for the next item.
/// </summary>
public static class AlertPlanner
{
    /// <summary>Voice reminder clips carried over from the web app (minutes before Sehri end).</summary>
    public static readonly int[] SehriReminderMinutes = [15, 10, 5, 1, 0];

    /// <summary>Voice reminder clips carried over from the web app (minutes before Iftar).</summary>
    public static readonly int[] IftarReminderMinutes = [10, 5, 1];

    public static IReadOnlyList<ScheduledAlert> Plan(
        PrayerScheduleService schedule, AdhanSettings adhan, RamadanSettings ramadan, DateTime fromUtc, DateTime toUtc)
    {
        fromUtc = PrayerScheduleService.EnsureUtc(fromUtc);
        toUtc = PrayerScheduleService.EnsureUtc(toUtc);

        var alerts = new List<ScheduledAlert>();
        var first = schedule.LocalDate(fromUtc).AddDays(-1);
        var last = schedule.LocalDate(toUtc).AddDays(1);

        for (var date = first; date <= last; date = date.AddDays(1))
        {
            var day = schedule.GetDay(date);
            AddPrayerAlerts(alerts, day, adhan);
            if (day.IsRamadan && ramadan.VoiceRemindersEnabled)
            {
                AddRamadanAlerts(alerts, day);
            }
        }

        return alerts
            .Where(a => a.Utc >= fromUtc && a.Utc < toUtc)
            .OrderBy(a => a.Utc)
            .ThenBy(a => a.Kind)
            .ToList();
    }

    private static void AddPrayerAlerts(List<ScheduledAlert> alerts, DaySchedule day, AdhanSettings adhan)
    {
        foreach (var prayer in PrayerScheduleService.FivePrayers)
        {
            if (day.Get(prayer) is not { } time)
            {
                continue;
            }

            var alert = adhan.For(prayer);
            if (adhan.Enabled && alert.AdhanEnabled)
            {
                alerts.Add(new ScheduledAlert
                {
                    Kind = AlertKind.Adhan, Utc = time.UtcDateTime, Date = day.Date, Prayer = prayer,
                    SoundId = adhan.SoundFor(prayer),
                });
            }

            if (alert.ReminderMinutesBefore > 0)
            {
                alerts.Add(new ScheduledAlert
                {
                    Kind = AlertKind.PrayerReminder, Utc = time.UtcDateTime.AddMinutes(-alert.ReminderMinutesBefore),
                    Date = day.Date, Prayer = prayer, MinutesBefore = alert.ReminderMinutesBefore,
                });
            }
        }
    }

    private static void AddRamadanAlerts(List<ScheduledAlert> alerts, DaySchedule day)
    {
        if (day.SehriEnd is { } sehri)
        {
            foreach (var minutes in SehriReminderMinutes)
            {
                alerts.Add(new ScheduledAlert
                {
                    Kind = AlertKind.SehriReminder, Utc = sehri.UtcDateTime.AddMinutes(-minutes), Date = day.Date,
                    MinutesBefore = minutes, SoundId = minutes == 0 ? "sehri_end" : $"sehri_{minutes}_min_before",
                });
            }
        }

        if (day.Iftar is { } iftar)
        {
            foreach (var minutes in IftarReminderMinutes)
            {
                alerts.Add(new ScheduledAlert
                {
                    Kind = AlertKind.IftarReminder, Utc = iftar.UtcDateTime.AddMinutes(-minutes), Date = day.Date,
                    MinutesBefore = minutes, SoundId = $"iftar_{minutes}_min_before",
                });
            }
        }
    }
}

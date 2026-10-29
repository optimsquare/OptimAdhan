using OptimAdhan.Core.Settings;

namespace OptimAdhan.Core.Scheduling;

public enum AlertDelivery
{
    /// <summary>On time: play the sound and show the alert.</summary>
    Play,

    /// <summary>On time but muted by the user: show the alert without sound.</summary>
    Silent,

    /// <summary>Too late to play (for example, the PC was asleep): show a quiet "began at" notice.</summary>
    Missed,
}

public readonly record struct AlertOccurrence(ScheduledAlert Alert, AlertDelivery Delivery);

/// <summary>
/// Decides which planned alerts are due, exactly once each. The app's timer
/// calls <see cref="Poll"/>; this class holds no timers and never reads the
/// clock itself, so it is fully deterministic and testable.
/// </summary>
public sealed class AlertDispatcher
{
    /// <summary>How late an alert may still play its sound.</summary>
    public static readonly TimeSpan PlayGrace = TimeSpan.FromMinutes(2);

    /// <summary>How late a missed Adhan still gets a "began at" notice.</summary>
    public static readonly TimeSpan MissedNoticeWindow = TimeSpan.FromMinutes(60);

    private static readonly TimeSpan Horizon = TimeSpan.FromHours(48);
    private static readonly TimeSpan ReplanWhenLessThan = TimeSpan.FromHours(24);

    private readonly HashSet<string> _handled = [];
    private readonly HashSet<string> _skip = [];
    private IReadOnlyList<ScheduledAlert> _plan = [];
    private PrayerScheduleService? _schedule;
    private AdhanSettings _adhan = new();
    private RamadanSettings _ramadan = new();
    private DateTime _planEndUtc;

    public PrayerScheduleService? Schedule => _schedule;

    /// <summary>When set, Adhan and reminders are delivered silently until this instant.</summary>
    public DateTime? MutedUntilUtc { get; set; }

    /// <summary>
    /// Sets or replaces what to schedule. Alerts already in the past are marked
    /// handled, so starting the app or changing settings never replays old alerts.
    /// </summary>
    public void Configure(PrayerScheduleService? schedule, AdhanSettings adhan, RamadanSettings ramadan, DateTime nowUtc)
    {
        _schedule = schedule;
        _adhan = adhan;
        _ramadan = ramadan;
        Replan(nowUtc);
        foreach (var alert in _plan)
        {
            if (alert.Utc <= nowUtc)
            {
                _handled.Add(alert.Key);
            }
        }
    }

    /// <summary>Returns alerts that became due since the last poll, each exactly once.</summary>
    public IReadOnlyList<AlertOccurrence> Poll(DateTime nowUtc)
    {
        if (_schedule is null)
        {
            return [];
        }

        if (_planEndUtc - nowUtc < ReplanWhenLessThan)
        {
            Replan(nowUtc);
        }

        var due = new List<AlertOccurrence>();
        foreach (var alert in _plan)
        {
            if (alert.Utc > nowUtc)
            {
                break; // the plan is sorted
            }

            if (!_handled.Add(alert.Key))
            {
                continue;
            }

            var lateBy = nowUtc - alert.Utc;
            if (lateBy <= PlayGrace)
            {
                var muted = _skip.Remove(alert.Key) || (MutedUntilUtc is { } until && nowUtc < until);
                due.Add(new AlertOccurrence(alert, muted ? AlertDelivery.Silent : AlertDelivery.Play));
            }
            else if (alert.Kind == AlertKind.Adhan && lateBy <= MissedNoticeWindow)
            {
                due.Add(new AlertOccurrence(alert, AlertDelivery.Missed));
            }
        }

        // Keep only alerts still relevant, so the set does not grow forever.
        if (_handled.Count > 500)
        {
            var keep = _plan.Select(a => a.Key).ToHashSet();
            _handled.IntersectWith(keep);
        }

        return due;
    }

    /// <summary>The next instant something is due, so the app can sleep until then.</summary>
    public DateTime? NextDueUtc(DateTime nowUtc) =>
        _plan.FirstOrDefault(a => a.Utc > nowUtc && !_handled.Contains(a.Key))?.Utc;

    public ScheduledAlert? NextAdhan(DateTime nowUtc) =>
        _plan.FirstOrDefault(a => a.Kind == AlertKind.Adhan && a.Utc > nowUtc);

    public bool IsNextAdhanSkipped(DateTime nowUtc) => NextAdhan(nowUtc) is { } next && _skip.Contains(next.Key);

    /// <summary>Silences only the upcoming Adhan. Calling again un-silences it.</summary>
    public void ToggleSkipNextAdhan(DateTime nowUtc)
    {
        if (NextAdhan(nowUtc) is { } next && !_skip.Remove(next.Key))
        {
            _skip.Add(next.Key);
        }
    }

    private void Replan(DateTime nowUtc)
    {
        _plan = _schedule is null
            ? []
            : AlertPlanner.Plan(_schedule, _adhan, _ramadan, nowUtc - MissedNoticeWindow, nowUtc + Horizon);
        _planEndUtc = nowUtc + Horizon;
    }
}

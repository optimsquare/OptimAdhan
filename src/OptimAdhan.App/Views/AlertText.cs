using OptimAdhan.App.Services;
using OptimAdhan.App.ViewModels;
using OptimAdhan.Core.Scheduling;

namespace OptimAdhan.App.Views;

/// <summary>User-facing wording for alerts. Ramadan messages follow the web app's reminders.</summary>
public static class AlertText
{
    public static (string Title, string Body) For(AlertOccurrence occurrence, AppController app)
    {
        var alert = occurrence.Alert;
        var use24 = app.Settings.Use24HourClock;
        var at = Format.Time(app.Schedule?.ToLocal(alert.Utc), use24);
        var name = alert.Prayer is { } p ? PrayerNames.English(p) : "";

        return alert.Kind switch
        {
            AlertKind.Adhan when occurrence.Delivery == AlertDelivery.Missed =>
                ($"{name} began at {at}", "Adhan not played: your PC was asleep or off"),
            AlertKind.Adhan =>
                ($"It's time for {name}", "Adhan is playing"),
            AlertKind.PrayerReminder =>
                ($"{name} in {alert.MinutesBefore} minutes", $"{name} begins at {Format.Time(app.Schedule?.ToLocal(alert.Utc.AddMinutes(alert.MinutesBefore)), use24)}"),
            AlertKind.SehriReminder => alert.MinutesBefore switch
            {
                0 => ("Sehri has ended", "The fast begins now."),
                15 => ("Sehri ends in 15 minutes", "Finish your meal soon."),
                5 => ("Sehri ends in 5 minutes", "Stop eating soon."),
                1 => ("1 minute until Sehri ends", ""),
                var m => ($"Sehri ends in {m} minutes", ""),
            },
            AlertKind.IftarReminder => alert.MinutesBefore switch
            {
                10 => ("Iftar in 10 minutes", "Prepare your meal."),
                1 => ("Iftar in 1 minute", ""),
                var m => ($"Iftar in {m} minutes", ""),
            },
            _ => (alert.Kind.ToString(), ""),
        };
    }

    /// <summary>Wraps right-to-left text in Unicode isolates so it cannot reorder the surrounding English.</summary>
    public static string Isolate(string text) => (char)0x2068 + text + (char)0x2069; // FSI … PDI
}

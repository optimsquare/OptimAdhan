using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using OptimAdhan.Core.Calculation;
using OptimAdhan.Core.Location;
using OptimAdhan.Core.Scheduling;
using OptimAdhan.Core.Settings;
using Xunit.Abstractions;

namespace OptimAdhan.Core.Tests.Calculation;

/// <summary>
/// Compares the engine with the independent AlAdhan API (used by the web app's
/// Ramadan calendar). Needs internet, so it only runs when the environment
/// variable OPTIMADHAN_NETWORK_TESTS=1 is set.
/// </summary>
public class AlAdhanCrossCheckTests(ITestOutputHelper output)
{
    // AlAdhan method IDs: https://aladhan.com/calculation-methods
    private static readonly Dictionary<CalculationMethod, int> AlAdhanMethodIds = new()
    {
        [CalculationMethod.Karachi] = 1,
        [CalculationMethod.NorthAmerica] = 2,
        [CalculationMethod.MuslimWorldLeague] = 3,
        [CalculationMethod.UmmAlQura] = 4,
        [CalculationMethod.Egyptian] = 5,
        [CalculationMethod.Tehran] = 7,
        [CalculationMethod.Kuwait] = 9,
        [CalculationMethod.Qatar] = 10,
        [CalculationMethod.Singapore] = 11,
        [CalculationMethod.Turkey] = 13,
        [CalculationMethod.MoonsightingCommittee] = 15,
        [CalculationMethod.Dubai] = 16,
    };

    private static readonly (string Name, double Lat, double Lon, CalculationMethod Method)[] Cities =
    [
        ("Makkah", 21.4225, 39.8262, CalculationMethod.UmmAlQura),
        ("Dhaka", 23.8103, 90.4125, CalculationMethod.Karachi),
        ("Karachi", 24.8607, 67.0011, CalculationMethod.Karachi),
        ("Jakarta", -6.2088, 106.8456, CalculationMethod.Singapore),
        ("Singapore", 1.3521, 103.8198, CalculationMethod.Singapore),
        ("Istanbul", 41.0082, 28.9784, CalculationMethod.Turkey),
        ("Cairo", 30.0444, 31.2357, CalculationMethod.Egyptian),
        ("Dubai", 25.2048, 55.2708, CalculationMethod.Dubai),
        ("Doha", 25.2854, 51.531, CalculationMethod.Qatar),
        ("Kuwait City", 29.3759, 47.9774, CalculationMethod.Kuwait),
        ("Tehran", 35.6892, 51.389, CalculationMethod.Tehran),
        ("New York", 40.7128, -74.006, CalculationMethod.NorthAmerica),
        ("Chicago", 41.8781, -87.6298, CalculationMethod.MoonsightingCommittee),
        ("Los Angeles", 34.0522, -118.2437, CalculationMethod.NorthAmerica),
        ("Sydney", -33.8688, 151.2093, CalculationMethod.MuslimWorldLeague),
        ("Cape Town", -33.9249, 18.4241, CalculationMethod.MuslimWorldLeague),
        ("London", 51.5074, -0.1278, CalculationMethod.MoonsightingCommittee),
    ];

    private static readonly DateOnly[] Dates =
        [new(2026, 3, 8), new(2026, 6, 21), new(2026, 11, 1), new(2027, 2, 20)];

    /// <summary>
    /// AlAdhan's Asr drifts up to 2–3 minutes from adhan's around the equinoxes.
    /// A brute-force solar-position check (see docs/ACCURACY.md) found adhan's Asr
    /// within about a minute of the true time, so only Asr gets the wider tolerance.
    /// </summary>
    private const double AsrToleranceMinutes = 3;

    [Fact]
    public async Task AgreesWithAlAdhanWithinOneMinute()
    {
        if (Environment.GetEnvironmentVariable("OPTIMADHAN_NETWORK_TESTS") != "1")
        {
            output.WriteLine("Skipped: set OPTIMADHAN_NETWORK_TESTS=1 to run.");
            return;
        }

        using var http = new HttpClient { BaseAddress = new Uri("https://api.aladhan.com/v1/") };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("OptimAdhan-tests/0.1");

        var failures = new List<string>();
        var compared = 0;
        var maxDelta = 0.0;

        foreach (var (name, lat, lon, method) in Cities)
        {
            var location = LocationInfo.Create(name, lat, lon, null, LocationSource.Manual);
            var calc = new CalculationSettings { Method = method, HighLatitudeRule = HighLatitudeSetting.MiddleOfTheNight };
            var schedule = new PrayerScheduleService(location, calc, new RamadanSettings());
            var m = schedule.Parameters.MethodAdjustments;

            foreach (var date in Dates)
            {
                // tune= applies the same method offsets adhan defines (Imsak,Fajr,Sunrise,Dhuhr,Asr,Maghrib,Sunset,Isha,Midnight).
                var tune = $"0,{m.Fajr},{m.Sunrise},{m.Dhuhr},{m.Asr},{m.Maghrib},0,{m.Isha},0";
                var url = string.Create(CultureInfo.InvariantCulture,
                    $"timings/{date:dd-MM-yyyy}?latitude={lat}&longitude={lon}&method={AlAdhanMethodIds[method]}&school=0&tune={tune}&timezonestring={Uri.EscapeDataString(location.TimeZoneId)}");

                using var doc = JsonDocument.Parse(await http.GetStringAsync(url));
                var timings = doc.RootElement.GetProperty("data").GetProperty("timings");
                var day = schedule.GetDay(date);

                foreach (var (prayer, key) in new[] { (Prayer.Fajr, "Fajr"), (Prayer.Sunrise, "Sunrise"), (Prayer.Dhuhr, "Dhuhr"),
                             (Prayer.Asr, "Asr"), (Prayer.Maghrib, "Maghrib"), (Prayer.Isha, "Isha") })
                {
                    var theirs = TimeOnly.ParseExact(timings.GetProperty(key).GetString()![..5], "HH:mm", CultureInfo.InvariantCulture);
                    var ours = TimeOnly.FromTimeSpan(day.Get(prayer)!.Value.TimeOfDay);
                    var delta = Math.Abs((ours - theirs).TotalMinutes);
                    delta = Math.Min(delta, 1440 - delta);
                    maxDelta = Math.Max(maxDelta, delta);
                    compared++;
                    output.WriteLine($"{name,-12} {date:yyyy-MM-dd} {method,-22} {key,-8} ours {ours:HH:mm} aladhan {theirs:HH:mm} Δ{delta}");
                    if (delta > (prayer == Prayer.Asr ? AsrToleranceMinutes : 1))
                    {
                        failures.Add($"{name} {date} {method} {key}: ours {ours:HH:mm}, AlAdhan {theirs:HH:mm}");
                    }
                }

                await Task.Delay(250); // be polite to the free API
            }
        }

        output.WriteLine($"Compared {compared} times; max difference {maxDelta} min.");
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }
}

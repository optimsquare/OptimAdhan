using System.IO.Compression;
using System.Text.Json;
using OptimAdhan.Core.Calculation;

namespace OptimAdhan.Core.Tests.Calculation;

/// <summary>
/// The C# engine must reproduce adhan-js 4.4.3 (the library the web app uses)
/// exactly, for every city, date, method, madhab and high-latitude option in the
/// fixture. Regenerate the fixture with tools/generate-adhan-fixtures.js.
/// </summary>
public class AdhanJsParityTests
{
    private sealed record FixtureCase(
        string City, double Lat, double Lon, string Date, string Method, string Madhab,
        string HighLatitudeRule, string PolarCircleResolution, Dictionary<string, int>? Adjustments,
        long? Fajr, long? Sunrise, long? Dhuhr, long? Asr, long? Sunset, long? Maghrib, long? Isha);

    private sealed record Fixture(string Generator, List<FixtureCase> Cases);

    private static Fixture LoadFixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "adhan-js-4.4.3.json.gz");
        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        return JsonSerializer.Deserialize<Fixture>(gzip, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    [Fact]
    public void MatchesAdhanJsForEveryFixtureCase()
    {
        var fixture = LoadFixture();
        Assert.True(fixture.Cases.Count > 50_000, "fixture unexpectedly small");

        var mismatches = new List<string>();
        foreach (var c in fixture.Cases)
        {
            var adjustments = c.Adjustments is null
                ? PrayerAdjustments.None
                : new PrayerAdjustments(c.Adjustments["fajr"], c.Adjustments["sunrise"], c.Adjustments["dhuhr"],
                    c.Adjustments["asr"], c.Adjustments["maghrib"], c.Adjustments["isha"]);

            var parameters = CalculationMethods.For(Enum.Parse<CalculationMethod>(c.Method)) with
            {
                Madhab = Enum.Parse<Madhab>(c.Madhab),
                HighLatitudeRule = Enum.Parse<HighLatitudeRule>(c.HighLatitudeRule),
                PolarCircleResolution = Enum.Parse<PolarCircleResolution>(c.PolarCircleResolution),
                Adjustments = adjustments,
            };

            var times = PrayerTimes.Calculate(new Coordinates(c.Lat, c.Lon), DateOnly.Parse(c.Date), parameters);

            Check(c, "fajr", c.Fajr, times.Fajr);
            Check(c, "sunrise", c.Sunrise, times.Sunrise);
            Check(c, "dhuhr", c.Dhuhr, times.Dhuhr);
            Check(c, "asr", c.Asr, times.Asr);
            Check(c, "sunset", c.Sunset, times.Sunset);
            Check(c, "maghrib", c.Maghrib, times.Maghrib);
            Check(c, "isha", c.Isha, times.Isha);
        }

        Assert.True(mismatches.Count == 0,
            $"{mismatches.Count} mismatches vs {fixture.Generator}. First 20:\n" + string.Join("\n", mismatches.Take(20)));

        void Check(FixtureCase c, string name, long? expectedMs, DateTime? actual)
        {
            long? actualMs = actual is { } a ? new DateTimeOffset(a).ToUnixTimeMilliseconds() : null;
            if (expectedMs != actualMs)
            {
                var exp = expectedMs is { } e ? DateTimeOffset.FromUnixTimeMilliseconds(e).ToString("u") : "none";
                var act = actualMs is { } v ? DateTimeOffset.FromUnixTimeMilliseconds(v).ToString("u") : "none";
                mismatches.Add($"{c.City} {c.Date} {c.Method}/{c.Madhab}/{c.HighLatitudeRule}/{c.PolarCircleResolution} {name}: expected {exp}, got {act} (Δ {(actualMs - expectedMs) ?? 0} ms)");
            }
        }
    }
}

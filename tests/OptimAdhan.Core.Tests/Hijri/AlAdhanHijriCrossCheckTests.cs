using System.Text.Json;
using OptimAdhan.Core.Hijri;
using Xunit.Abstractions;

namespace OptimAdhan.Core.Tests.Hijri;

/// <summary>Compares Umm al-Qura conversion with AlAdhan's gToH API. Runs only with OPTIMADHAN_NETWORK_TESTS=1.</summary>
public class AlAdhanHijriCrossCheckTests(ITestOutputHelper output)
{
    [Fact]
    public async Task MatchesAlAdhanConversion()
    {
        if (Environment.GetEnvironmentVariable("OPTIMADHAN_NETWORK_TESTS") != "1")
        {
            output.WriteLine("Skipped: set OPTIMADHAN_NETWORK_TESTS=1 to run.");
            return;
        }

        using var http = new HttpClient { BaseAddress = new Uri("https://api.aladhan.com/v1/") };
        var service = new HijriCalendarService();
        var failures = new List<string>();

        var dates = new List<DateOnly>();
        for (var d = new DateOnly(2026, 1, 1); d <= new DateOnly(2028, 12, 31); d = d.AddDays(23))
        {
            dates.Add(d);
        }

        foreach (var year in new[] { 1447, 1448, 1449 })
        {
            var ramadan = service.GetRamadan(year);
            dates.AddRange([ramadan.FirstDay.AddDays(-1), ramadan.FirstDay, ramadan.LastDay, ramadan.LastDay.AddDays(1)]);
            output.WriteLine($"Ramadan {year}: {ramadan.FirstDay:yyyy-MM-dd} to {ramadan.LastDay:yyyy-MM-dd} ({ramadan.Days} days)");
        }

        foreach (var date in dates)
        {
            using var doc = JsonDocument.Parse(await http.GetStringAsync($"gToH/{date:dd-MM-yyyy}"));
            var h = doc.RootElement.GetProperty("data").GetProperty("hijri");
            var theirs = new HijriDate(int.Parse(h.GetProperty("year").GetString()!),
                h.GetProperty("month").GetProperty("number").GetInt32(), int.Parse(h.GetProperty("day").GetString()!));
            var ours = service.ToHijri(date);
            output.WriteLine($"{date:yyyy-MM-dd}: ours {ours}, AlAdhan {theirs}");
            if (ours != theirs)
            {
                failures.Add($"{date:yyyy-MM-dd}: ours {ours}, AlAdhan {theirs}");
            }

            await Task.Delay(200);
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }
}

using OptimAdhan.Core.Calculation;
using OptimAdhan.Core.Settings;

namespace OptimAdhan.Core.Tests.Settings;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "OptimAdhanTests", Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void MissingFileGivesDefaults()
    {
        var settings = new SettingsStore(SettingsPath).Load();
        Assert.Null(settings.Location);
        Assert.True(settings.Adhan.Enabled);
    }

    [Fact]
    public void RoundTrips()
    {
        var store = new SettingsStore(SettingsPath);
        var settings = new AppSettings
        {
            Location = TestLocations.Dhaka,
            Calculation = new CalculationSettings { Method = CalculationMethod.Karachi, Madhab = Madhab.Hanafi, Adjustments = new(Isha: 4) },
            Adhan = new AdhanSettings { Volume = 55, Asr = new PrayerAlert { AdhanEnabled = false, ReminderMinutesBefore = 10 } },
        };

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal(settings.Location, loaded.Location);
        Assert.Equal(settings.Calculation, loaded.Calculation);
        Assert.Equal(settings.Adhan.Asr, loaded.Adhan.Asr);
        Assert.Equal(55, loaded.Adhan.Volume);
        Assert.Contains("\"Karachi\"", File.ReadAllText(SettingsPath)); // enums stored by name, readable
    }

    [Fact]
    public void MainWindowPreferencesRoundTrip()
    {
        var store = new SettingsStore(SettingsPath);
        var window = new MainWindowSettings { Navigation = NavigationMode.Hidden, Left = 40, Top = 30, Width = 1300, Height = 860, Maximized = true };

        store.Save(new AppSettings { MainWindow = window });

        Assert.Equal(window, store.Load().MainWindow);
        Assert.Contains("\"Hidden\"", File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void OlderFilesWithoutMainWindowGetDefaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, "{ \"Version\": 1, \"Use24HourClock\": true }");

        var settings = new SettingsStore(SettingsPath).Load();

        Assert.True(settings.Use24HourClock);
        Assert.Equal(NavigationMode.Expanded, settings.MainWindow.Navigation);
        Assert.Null(settings.MainWindow.Width);
    }

    [Fact]
    public void Version1FajrSoundBecomesFajrsOwnChoice()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath,
            "{ \"Version\": 1, \"Adhan\": { \"DefaultSoundId\": \"makkah\", \"FajrSoundId\": \"madina\", \"Asr\": { \"AdhanEnabled\": false } } }");

        var store = new SettingsStore(SettingsPath);
        var settings = store.Load();

        Assert.Equal(AppSettings.CurrentVersion, settings.Version);
        Assert.Equal("makkah", settings.Adhan.DefaultSoundId);
        Assert.Equal("madina", settings.Adhan.SoundFor(Prayer.Fajr));
        Assert.Equal("makkah", settings.Adhan.SoundFor(Prayer.Dhuhr));
        Assert.False(settings.Adhan.Asr.AdhanEnabled);

        store.Save(settings);
        Assert.DoesNotContain("FajrSoundId", File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void Version1MatchingFajrSoundKeepsFollowingTheDefault()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath,
            "{ \"Version\": 1, \"Adhan\": { \"DefaultSoundId\": \"makkah\", \"FajrSoundId\": \"makkah\" } }");

        var settings = new SettingsStore(SettingsPath).Load();

        Assert.Null(settings.Adhan.Fajr.SoundId);
        Assert.Equal("adhan-2", (settings.Adhan with { DefaultSoundId = "adhan-2" }).SoundFor(Prayer.Fajr));
    }

    [Fact]
    public void PerPrayerSoundChoicesRoundTrip()
    {
        var store = new SettingsStore(SettingsPath);
        var adhan = new AdhanSettings { DefaultSoundId = "adhan-3", Maghrib = new PrayerAlert { SoundId = "adhan-5", ReminderMinutesBefore = 10 } };

        store.Save(new AppSettings { Adhan = adhan });
        var loaded = store.Load().Adhan;

        Assert.Equal(adhan.Maghrib, loaded.Maghrib);
        Assert.Equal("adhan-3", loaded.SoundFor(Prayer.Asr));
        Assert.Equal("adhan-5", loaded.SoundFor(Prayer.Maghrib));
    }

    [Fact]
    public void CorruptFileIsPreservedAndDefaultsUsed()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, "{ not json");

        var settings = new SettingsStore(SettingsPath).Load();

        Assert.Null(settings.Location);
        Assert.True(File.Exists(SettingsPath + ".corrupt"));
    }
}

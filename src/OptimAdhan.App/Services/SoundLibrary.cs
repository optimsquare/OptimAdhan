using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OptimAdhan.App.Services;

public sealed record AdhanSound(string Id, string Name, string Path);

/// <summary>
/// Reads Assets/Audio/sounds.json, so recordings can be replaced or added
/// without code changes. Missing files are skipped rather than failing.
/// </summary>
public sealed class SoundLibrary
{
    private readonly Dictionary<string, string> _reminders;

    private SoundLibrary(IReadOnlyList<AdhanSound> adhans, Dictionary<string, string> reminders)
    {
        Adhans = adhans;
        _reminders = reminders;
    }

    public IReadOnlyList<AdhanSound> Adhans { get; }

    public static SoundLibrary Load(string audioFolder)
    {
        var manifestPath = System.IO.Path.Combine(audioFolder, "sounds.json");
        if (!File.Exists(manifestPath))
        {
            return new SoundLibrary([], []);
        }

        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? new Manifest();

        var adhans = manifest.Adhan
            .Select(a => new AdhanSound(a.Id, a.Name, System.IO.Path.Combine(audioFolder, a.File)))
            .Where(a => File.Exists(a.Path))
            .ToList();

        var reminders = manifest.Reminders
            .Select(kv => (kv.Key, Path: System.IO.Path.Combine(audioFolder, kv.Value)))
            .Where(r => File.Exists(r.Path))
            .ToDictionary(r => r.Key, r => r.Path, StringComparer.OrdinalIgnoreCase);

        return new SoundLibrary(adhans, reminders);
    }

    /// <summary>Path for an Adhan sound ID, falling back to the first available sound.</summary>
    public string? AdhanPath(string? id) =>
        (Adhans.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Adhans.FirstOrDefault())?.Path;

    public string? ReminderPath(string? id) => id is not null && _reminders.TryGetValue(id, out var path) ? path : null;

    private sealed class Manifest
    {
        public List<Entry> Adhan { get; set; } = [];

        public Dictionary<string, string> Reminders { get; set; } = [];
    }

    private sealed class Entry
    {
        public string Id { get; set; } = "";

        public string Name { get; set; } = "";

        public string File { get; set; } = "";

        [JsonPropertyName("fajr")] public bool SuitableForFajr { get; set; }
    }
}

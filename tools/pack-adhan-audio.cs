// Packs the bundled source recordings into the app's .oadh files (see docs/AUDIO.md).
// Reads assets/audio/adhan-source/library.json, strips metadata (MP3 ID3 tags: titles, artists,
// cover art; WAV LIST/INFO chunks) and writes src/OptimAdhan.App/Assets/Audio/<Folder>/<id>.oadh:
// "adhan" entries go to Adhan/, "dua" to Dua/, "reminders" (spoken Ramadan countdowns) to Reminders/. Source files are only read.
// Usage (from the workspace root):  dotnet run tools/pack-adhan-audio.cs
#:project ../src/OptimAdhan.Core/OptimAdhan.Core.csproj

using System.Text.Json;
using OptimAdhan.Core.Audio;

var root = Directory.GetCurrentDirectory();
var sourceDir = Path.Combine(root, "assets", "audio", "adhan-source");
var audioDir = Path.Combine(root, "src", "OptimAdhan.App", "Assets", "Audio");
var libraryPath = Path.Combine(sourceDir, "library.json");
if (!File.Exists(libraryPath))
{
    Console.Error.WriteLine($"Not found: {libraryPath}. Run from the workspace root.");
    return 1;
}

(string Section, string Folder)[] sections = [("adhan", "Adhan"), ("dua", "Dua"), ("reminders", "Reminders")];

using var library = JsonDocument.Parse(File.ReadAllText(libraryPath));
foreach (var (section, folder) in sections)
{
    if (!library.RootElement.TryGetProperty(section, out var entries))
    {
        continue;
    }

    var outputDir = Path.Combine(audioDir, folder);
    Directory.CreateDirectory(outputDir);
    foreach (var entry in entries.EnumerateArray())
    {
        var id = entry.GetProperty("id").GetString()!;
        // Relative to adhan-source/, so a source can also live in a sibling folder (../dua-source/...).
        var source = Path.GetFullPath(Path.Combine(sourceDir, entry.GetProperty("source").GetString()!));
        var audio = PackedAudio.StripMetadata(File.ReadAllBytes(source));
        var packed = PackedAudio.Pack(audio);
        if (!PackedAudio.Unpack(packed).AsSpan().SequenceEqual(audio))
        {
            throw new InvalidOperationException($"Round trip failed for {id}");
        }

        var output = Path.Combine(outputDir, id + PackedAudio.Extension);
        File.WriteAllBytes(output, packed);
        Console.WriteLine($"{id}: {new FileInfo(source).Length / 1024,5} KB source -> {packed.Length / 1024,5} KB {folder}/{Path.GetFileName(output)}");
    }
}

return 0;

// Packs the source Adhan recordings into the app's .oadh files (see docs/AUDIO.md).
// Reads assets/audio/adhan-source/library.json, strips ID3 tags (titles, artists, cover art),
// and writes src/OptimAdhan.App/Assets/Audio/Adhan/<id>.oadh. Source files are only read.
// Usage (from the workspace root):  dotnet run tools/pack-adhan-audio.cs
#:project ../src/OptimAdhan.Core/OptimAdhan.Core.csproj

using System.Text.Json;
using OptimAdhan.Core.Audio;

var root = Directory.GetCurrentDirectory();
var sourceDir = Path.Combine(root, "assets", "audio", "adhan-source");
var outputDir = Path.Combine(root, "src", "OptimAdhan.App", "Assets", "Audio", "Adhan");
var libraryPath = Path.Combine(sourceDir, "library.json");
if (!File.Exists(libraryPath))
{
    Console.Error.WriteLine($"Not found: {libraryPath}. Run from the workspace root.");
    return 1;
}

using var library = JsonDocument.Parse(File.ReadAllText(libraryPath));
Directory.CreateDirectory(outputDir);
foreach (var entry in library.RootElement.GetProperty("adhan").EnumerateArray())
{
    var id = entry.GetProperty("id").GetString()!;
    var source = Path.Combine(sourceDir, entry.GetProperty("source").GetString()!);
    var audio = PackedAudio.StripId3Tags(File.ReadAllBytes(source));
    var packed = PackedAudio.Pack(audio);
    if (!PackedAudio.Unpack(packed).AsSpan().SequenceEqual(audio))
    {
        throw new InvalidOperationException($"Round trip failed for {id}");
    }

    var output = Path.Combine(outputDir, id + PackedAudio.Extension);
    File.WriteAllBytes(output, packed);
    Console.WriteLine($"{id}: {new FileInfo(source).Length / 1024,5} KB source -> {packed.Length / 1024,5} KB {Path.GetFileName(output)}");
}

return 0;

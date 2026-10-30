using System.Text.Json;
using System.Text.Json.Serialization;

namespace OptimAdhan.Core.Settings;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as JSON. Writes go to a temporary
/// file that then replaces the original, so a crash or power loss mid-save can
/// never leave a half-written settings file.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly object _sync = new();

    public SettingsStore(string path)
    {
        _path = path;
    }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Optim Square", "OptimAdhan", "settings.json");

    public string FilePath => _path;

    /// <summary>
    /// Returns saved settings, or defaults when none exist. A corrupt file is
    /// kept as <c>settings.json.corrupt</c> for diagnosis and defaults are used.
    /// </summary>
    public AppSettings Load()
    {
        lock (_sync)
        {
            if (!File.Exists(_path))
            {
                return new AppSettings();
            }

            try
            {
                var json = File.ReadAllText(_path);
                return (JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings()).Migrate();
            }
            catch (JsonException)
            {
                File.Copy(_path, _path + ".corrupt", overwrite: true);
                return new AppSettings();
            }
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_sync)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temp, _path, overwrite: true);
        }
    }
}

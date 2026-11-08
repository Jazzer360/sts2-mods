using System.Text.Json;

namespace Sts2Mods.Core.Settings;

/// <summary>
/// Loads and saves a settings object as JSON next to the mod's DLL.
///
/// The file uses a <c>.cfg</c> extension on purpose: the game's mod loader parses every
/// <c>*.json</c> under <c>mods/</c> as a mod manifest (see docs/MODDING_NOTES.md).
/// </summary>
public sealed class JsonSettingsFile<T> where T : class, new()
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        IncludeFields = false,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly ModLog _log;

    public JsonSettingsFile(string path, ModLog log)
    {
        Path = path;
        _log = log;
    }

    public string Path { get; }

    public T Value { get; private set; } = new();

    public static string DefaultPathFor(Type modType, string modId)
    {
        var dir = System.IO.Path.GetDirectoryName(modType.Assembly.Location);
        if (string.IsNullOrEmpty(dir))
        {
            dir = AppContext.BaseDirectory;
        }
        return System.IO.Path.Combine(dir, modId + ".settings.cfg");
    }

    public T Load()
    {
        try
        {
            if (File.Exists(Path))
            {
                Value = JsonSerializer.Deserialize<T>(File.ReadAllText(Path), Options) ?? new T();
                _log.Info($"settings loaded from {Path}");
            }
            else
            {
                Value = new T();
                Save();
                _log.Info($"settings created at {Path}");
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"settings at {Path} couldn't be read; using defaults. {ex.Message}");
            Value = new T();
        }
        return Value;
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(Path, JsonSerializer.Serialize(Value, Options));
        }
        catch (Exception ex)
        {
            _log.Warn($"settings couldn't be saved to {Path}: {ex.Message}");
        }
    }
}

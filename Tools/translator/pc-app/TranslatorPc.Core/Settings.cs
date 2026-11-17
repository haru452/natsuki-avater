// The PC translator's settings, as JSON in %APPDATA%\VRCTranslatorPC\settings.json.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TranslatorPc.Core;

public class Settings
{
    public string DeepLKey { get; set; } = "";
    /// <summary>What Japanese is translated into: EN or ZH.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Lang TargetLang { get; set; } = Lang.EN;
    public bool ShowOriginal { get; set; } = true;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DisplayMode Display { get; set; } = DisplayMode.KAT;
    public int KatSync { get; set; } = 8;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRCTranslatorPC", "settings.json");

    /// <summary>The saved settings; defaults when the file is missing or unreadable.</summary>
    public static Settings Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? new Settings() : new Settings();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return new Settings();
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>How the key appears on screen: never the key itself.</summary>
    public static string MaskKey(string key) =>
        string.IsNullOrWhiteSpace(key) ? "なし" : key.Trim().EndsWith(":fx") ? "あり（…:fx）" : "あり";
}

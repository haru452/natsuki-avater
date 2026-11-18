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
    /// <summary>Seconds the pet's text stays up after its last page is written, then it is cleared (0: until the next).</summary>
    public double KatHoldSeconds { get; set; } = 10;
    /// <summary>
    /// Other players' voices on the phone: false (for now) shows what they said as it is, in every language;
    /// true translates what is not Japanese into Japanese with DeepL (Japanese is never sent).
    /// </summary>
    public bool TranslateOthers { get; set; } = false;
    /// <summary>Saying one of these makes the pet turn and smile (PetCall): Homare (誉) and Natsuki. Also hotwords.</summary>
    public List<string> PetNames { get; set; } = new() { "ほまれ", "誉", "homare", "なつき", "natsuki" };
    /// <summary>
    /// How the PC mishears her name, counted as her name too (not hotwords): the soft ほ of ほまれちゃん is lost or heard
    /// as お/あ/う. Only with ちゃん, so 生まれ・まれに・丸い are not a call.
    /// </summary>
    public List<string> PetNameSoundAlikes { get; set; } = new() { "まれちゃん", "まるちゃん" };
    /// <summary>Her names and how they get misheard: what a call is listened for with.</summary>
    [JsonIgnore] public IEnumerable<string> PetCallNames => PetNames.Concat(PetNameSoundAlikes);
    /// <summary>Phrases for Sit / Wait / Come / Stand, said after her name (PetCommands; hiragana, Japanese only).</summary>
    public Dictionary<string, List<string>> PetCommandWords { get; set; } = PetCommands.Defaults();

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRCTranslatorPC", "settings.json");

    /// <summary>The saved settings; defaults when the file is missing or unreadable.</summary>
    public static Settings Load(string path)
    {
        try
        {
            var s = File.Exists(path) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? new Settings() : new Settings();
            // Commands added since the file was saved get their default phrases.
            foreach (var (command, phrases) in PetCommands.Defaults()) s.PetCommandWords.TryAdd(command, phrases);
            return s;
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

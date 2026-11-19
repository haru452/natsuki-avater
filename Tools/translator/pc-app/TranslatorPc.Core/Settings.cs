// The PC translator's settings, as JSON in %APPDATA%\VRCTranslatorPC\settings.json.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TranslatorPc.Core;

public class Settings
{
    public string DeepLKey { get; set; } = "";
    /// <summary>For talking with her (PetChat): the Gemini API key, entered with G.</summary>
    public string GeminiKey { get; set; } = "";
    /// <summary>
    /// Talking with her (the 会話 toggle or C): whatever is said goes to her and she answers in English (needs GeminiKey).
    /// </summary>
    public bool PetChat { get; set; } = true;
    /// <summary>
    /// Translating (the avatar's 翻訳 toggle or T), remembered: VRChat does not say the toggle's state when the app
    /// starts, so after a restart the app went on translating while the avatar's toggle was off.
    /// Off together with PetChat, she listens only for commands.
    /// </summary>
    public bool Translate { get; set; } = true;
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
    /// Also counted as her name (not hotwords). The PC mishears ほまれちゃん (the soft ほ is lost or heard as お/あ/う:
    /// まれちゃん・まるちゃん・おばれちゃん…), but ちゃん itself comes through, and the wearer calls no one else ～ちゃん:
    /// so ちゃん alone is a call. 生まれ・まれに・丸い are not.
    /// </summary>
    public List<string> PetNameSoundAlikes { get; set; } = new() { "ちゃん" };
    /// <summary>Words taken out before listening for her name: ちゃんと (properly) is not a call.</summary>
    public List<string> PetCallIgnore { get; set; } = new() { "ちゃんと" };
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
            // Commands added since the file was saved get their default phrases, and phrases the defaults now give to
            // another command leave the old one (こっちむいて moved from FaceFront to LookAtMe and still turned her front).
            var defaults = PetCommands.Defaults();
            var owner = defaults.SelectMany(d => d.Value.Select(p => (Phrase: p, Command: d.Key)))
                .GroupBy(x => x.Phrase).ToDictionary(g => g.Key, g => g.First().Command);
            foreach (var (command, phrases) in s.PetCommandWords)
                phrases.RemoveAll(p => owner.TryGetValue(p, out var o) && o != command);
            foreach (var (command, phrases) in defaults) s.PetCommandWords.TryAdd(command, phrases);
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

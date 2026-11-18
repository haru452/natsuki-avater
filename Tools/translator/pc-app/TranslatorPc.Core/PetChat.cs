// Talking with Homare: called by name (and not told to do something), she answers in English on her bubble, through
// the Gemini API. Only the wearer's own recognized words go to Gemini, and only when they call her.
// The HTTP call lives in the exe (PetChat takes it as a function), so this stays testable.
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace TranslatorPc.Core;

/// <summary>A reply that did not come. The message is for the screen and never holds the key.</summary>
public class PetChatException : Exception
{
    public PetChatException(string message) : base(message) { }
}

public interface IPetChat
{
    /// <summary>Her answer to what the wearer said (English, ready for her bubble); throws PetChatException.</summary>
    string Reply(string said);
}

public static class Gemini
{
    public const string Base = "https://generativelanguage.googleapis.com/v1beta/";
    public const string ListUrl = Base + "models?pageSize=1000";
    /// <summary>Her bubble's room for one answer (KAT pages hold 128 letters).</summary>
    public const int MaxReply = 120;

    public const string Persona =
        "You are Homare, a small, energetic and a little cheeky girl who follows the user around in VRChat as their pet "
        + "companion. You tease the user playfully but you like them. The user speaks Japanese (from speech recognition, "
        + "so it may be misheard; they often call you \"ちゃん\" or by your name). Always answer in English only, in one or "
        + "two short casual sentences (under 100 characters), plain ASCII, no emoji, no Japanese.";

    public static string ReplyUrl(string model) => Base + model + ":generateContent";

    /// <summary>
    /// The newest plain Flash model ("models/gemini-3.6-flash"; not lite, preview, TTS, image or embedding): model names
    /// change every few months, so it is looked up rather than written in.
    /// </summary>
    public static string PickModel(string listJson)
    {
        var plain = new Regex(@"^models/gemini-(\d+)\.(\d+)-flash$");
        var best = JsonNode.Parse(listJson)?["models"]?.AsArray()
            .Select(m => (Name: m?["name"]?.GetValue<string>() ?? "",
                Methods: m?["supportedGenerationMethods"]?.AsArray().Select(x => x?.GetValue<string>()).ToList() ?? new()))
            .Where(m => m.Methods.Contains("generateContent"))
            .Select(m => (m.Name, Match: plain.Match(m.Name)))
            .Where(m => m.Match.Success)
            .OrderByDescending(m => int.Parse(m.Match.Groups[1].Value))
            .ThenByDescending(m => int.Parse(m.Match.Groups[2].Value))
            .Select(m => m.Name)
            .FirstOrDefault();
        return best ?? throw new PetChatException("Gemini で使える Flash モデルが見つかりません");
    }

    public static string Body(IEnumerable<(bool Her, string Text)> history, string said)
    {
        var contents = new JsonArray();
        foreach (var (her, text) in history.Append((false, said)))
            contents.Add(new JsonObject
            {
                ["role"] = her ? "model" : "user",
                ["parts"] = new JsonArray(new JsonObject { ["text"] = text }),
            });
        return new JsonObject
        {
            ["system_instruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = Persona }) },
            ["contents"] = contents,
            ["generationConfig"] = new JsonObject { ["temperature"] = 1.0 },
        }.ToJsonString();
    }

    /// <summary>The answer's text (a thinking model's thoughts left out).</summary>
    public static string Parse(string json)
    {
        try
        {
            var parts = JsonNode.Parse(json)?["candidates"]?[0]?["content"]?["parts"]?.AsArray();
            var text = string.Concat(parts?.Where(p => p?["thought"]?.GetValue<bool>() != true)
                .Select(p => p?["text"]?.GetValue<string>() ?? "") ?? Array.Empty<string>()).Trim();
            if (text.Length > 0) return text;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentOutOfRangeException) { }
        throw new PetChatException("Gemini から返事がありませんでした");
    }

    /// <summary>Plain ASCII (her bubble shows nothing else), one line, at most MaxReply letters.</summary>
    public static string ForPet(string reply)
    {
        var sb = new StringBuilder();
        foreach (char c in reply)
            sb.Append(c switch
            {
                '“' or '”' or '„' => "\"",
                '‘' or '’' => "'",
                '—' or '–' => "-",
                '…' => "...",
                _ when c is >= ' ' and <= '~' => c.ToString(),
                _ when char.IsWhiteSpace(c) => " ",
                _ => "",
            });
        var text = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        if (text.Length <= MaxReply) return text;
        int cut = text.LastIndexOf(' ', MaxReply - 3);
        return text[..(cut > 0 ? cut : MaxReply - 3)].TrimEnd() + "...";
    }

    public static string Problem(int status) => status switch
    {
        400 or 401 or 403 => "Gemini のキーが違うか、使えません（G キーで入れ直してください）",
        404 => "Gemini のモデルが見つかりません",
        429 => "Gemini が混んでいます（無料枠の上限かもしれません）。少し待ってください",
        _ => $"Gemini でエラーが出ました（{status}）",
    };
}

/// <summary>Her side of the talk: finds the model once, remembers the last few exchanges.</summary>
public class PetChat : IPetChat
{
    public const int KeepExchanges = 6;

    readonly Func<string> key;
    readonly Func<string, string?, (int Status, string Json)> http;
    readonly List<(bool Her, string Text)> history = new();
    readonly object gate = new();
    string? model;

    /// <param name="http">GET (body null) or POST to Gemini with the key, returning the status and the answer</param>
    public PetChat(Func<string> key, Func<string, string?, (int Status, string Json)> http)
    {
        this.key = key;
        this.http = http;
    }

    /// <summary>The model in use ("gemini-3.6-flash"), once found.</summary>
    public string? Model => model?.Replace("models/", "");

    public string Reply(string said)
    {
        if (key().Trim().Length == 0) throw new PetChatException("Gemini のキーがありません（G キーで入れてください）");
        lock (gate)
        {
            if (model == null)
            {
                var (listStatus, list) = http(Gemini.ListUrl, null);
                if (listStatus != 200) throw new PetChatException(Gemini.Problem(listStatus));
                model = Gemini.PickModel(list);
            }
            var (status, json) = http(Gemini.ReplyUrl(model), Gemini.Body(history, said));
            if (status != 200) throw new PetChatException(Gemini.Problem(status));
            var reply = Gemini.ForPet(Gemini.Parse(json));
            if (reply.Length == 0) throw new PetChatException("Gemini の返事が表示できない文字だけでした");
            history.Add((false, said));
            history.Add((true, reply));
            while (history.Count > KeepExchanges * 2) history.RemoveRange(0, 2);
            return reply;
        }
    }
}

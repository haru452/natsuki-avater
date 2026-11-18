// DeepL API: the request and the answer (the HTTP call lives in the exe, so this stays testable).
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TranslatorPc.Core;

/// <summary>A translation that did not come back. The message is for the screen and never holds the key.</summary>
public class DeepLException : Exception
{
    public DeepLException(string message) : base(message) { }
}

/// <summary>Translates one sentence; throws DeepLException (or a network exception) when it cannot.</summary>
public interface ITranslator
{
    string Translate(string text, Lang source, Lang target);
}

public static class DeepL
{
    /// <summary>Free-plan keys end in ":fx" and must use the free host.</summary>
    public static string Endpoint(string key) => key.Trim().EndsWith(":fx")
        ? "https://api-free.deepl.com/v2/translate"
        : "https://api.deepl.com/v2/translate";

    static string SourceCode(Lang l) => l switch { Lang.JA => "JA", Lang.EN => "EN", Lang.ZH => "ZH", _ => throw new ArgumentException(l.ToString()) };

    static string TargetCode(Lang l) => l switch { Lang.JA => "JA", Lang.EN => "EN-US", Lang.ZH => "ZH-HANS", _ => throw new ArgumentException(l.ToString()) };

    /// <summary>A language the recognizer cannot name (OTHER: Korean and the like) is left for DeepL to detect.</summary>
    public static string Body(string text, Lang source, Lang target)
    {
        var body = new JsonObject { ["text"] = new JsonArray(text) };
        if (source != Lang.OTHER) body["source_lang"] = SourceCode(source);
        body["target_lang"] = TargetCode(target);
        return body.ToJsonString();
    }

    public static string Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var text = doc.RootElement.GetProperty("translations")[0].GetProperty("text").GetString();
            if (text != null) return text;
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException) { }
        throw new DeepLException("DeepL の返事が読めませんでした");
    }

    /// <summary>What a failed HTTP status means for the wearer.</summary>
    public static string Problem(int status) => status switch
    {
        403 => "DeepL のキーが違います（K キーで入れ直してください）",
        456 => "DeepL の今月の無料分を使い切りました",
        429 => "DeepL が混んでいます（少し待ってください）",
        _ => $"DeepL エラー（{status}）",
    };
}

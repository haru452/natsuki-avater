// Port of the phone app's Lang and Compose: KAT cannot show kanji, so the pet gets kana-only Japanese;
// the chatbox gets kanji.
namespace TranslatorPc.Core;

public enum Lang { NONE, JA, EN, ZH, OTHER }

public static class LangCodes
{
    /// <summary>"ja", "en", "zh" or SenseVoice's "&lt;|ja|&gt;" form. Empty is NONE; anything else is OTHER.</summary>
    public static Lang Parse(string code) => code.Trim().Replace("<|", "").Replace("|>", "").ToLowerInvariant() switch
    {
        "" => Lang.NONE,
        "ja" => Lang.JA,
        "en" => Lang.EN,
        "zh" => Lang.ZH,
        _ => Lang.OTHER,
    };
}

/// <summary>One translation. KanaOriginal / KanaTranslation are the Japanese side written in kana only (for KAT).</summary>
public record TranslationResult(Lang Source, Lang Target, string Original, string Translation,
    string KanaOriginal = "", string KanaTranslation = "");

public enum DisplayMode { KAT, CHATBOX, BOTH }

public static class Compose
{
    public const int Row = 32;
    public const int ChatboxLimit = 144;

    /// <summary>Japanese speech: translation, then (kana original) on the next row. English: kana translation, then (original).</summary>
    public static string Kat(TranslationResult r, bool showOriginal)
    {
        var (main, original) = r.Source switch
        {
            Lang.JA => (r.Translation, r.KanaOriginal),
            Lang.ZH => (r.KanaTranslation, ""),
            _ => (r.KanaTranslation, r.Original),
        };
        if (!showOriginal || original.Length == 0) return main;
        int pad = ((-Core.Kat.Cells(main)) % Row + Row) % Row;
        return main + new string(' ', pad) + "(" + original + ")";
    }

    public static string Chatbox(TranslationResult r, bool showOriginal)
    {
        var text = r.Translation;
        if (showOriginal && r.Original.Length > 0)
        {
            var withOriginal = text + "\n(" + r.Original + ")";
            if (Length(withOriginal) <= ChatboxLimit) return withOriginal;
        }
        if (Length(text) <= ChatboxLimit) return text;
        return string.Concat(Core.Kat.Chars(text).Take(ChatboxLimit - 1)) + "…";
    }

    static int Length(string s) => s.EnumerateRunes().Count();

    /// <summary>Pages of at most limit KAT cells, never splitting a character, preferring to break at a space.</summary>
    public static List<string> Paginate(string text, int limit = Core.Kat.TextLimit, int wordBacktrack = 20)
    {
        var pages = new List<string>();
        var page = "";
        int used = 0;
        foreach (var ch in Core.Kat.Chars(text))
        {
            int width = Core.Kat.CharWidth(ch);
            if (used + width > limit)
            {
                int cut = page.LastIndexOf(' ');
                if (ch != " " && cut > 0 && page.Length - cut <= wordBacktrack)
                {
                    pages.Add(page[..cut]);
                    page = page[(cut + 1)..];
                }
                else
                {
                    pages.Add(page);
                    page = "";
                }
                page = page.TrimStart(' ');
                used = Core.Kat.Cells(page);
                if (ch == " " && page.Length == 0) continue;
            }
            page += ch;
            used += width;
        }
        if (page.Length > 0 || pages.Count == 0) pages.Add(page);
        return pages;
    }
}

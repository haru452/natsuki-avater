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

    /// <summary>
    /// The language a recognized text is written in (sherpa-onnx's .NET result has no language): Hangul → OTHER,
    /// any kana → JA, kanji only → ZH, Latin letters → EN, nothing readable → NONE.
    /// </summary>
    public static Lang FromText(string text)
    {
        if (text.Any(c => c >= 0xAC00 && c <= 0xD7AF || c >= 0x1100 && c <= 0x11FF)) return Lang.OTHER;
        if (text.Any(c => c >= 0x3040 && c <= 0x30FF)) return Lang.JA;
        if (text.Any(c => c >= 0x4E00 && c <= 0x9FFF || c >= 0x3400 && c <= 0x4DBF)) return Lang.ZH;
        if (text.Any(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z')) return Lang.EN;
        return Lang.NONE;
    }
}

/// <summary>One translation. KanaOriginal / KanaTranslation are the Japanese side written in kana only (for KAT).</summary>
public record TranslationResult(Lang Source, Lang Target, string Original, string Translation,
    string KanaOriginal = "", string KanaTranslation = "");

public enum DisplayMode { KAT, CHATBOX, BOTH }

public static class Compose
{
    public const int Row = 32;
    public const int ChatboxLimit = 144;

    /// <summary>
    /// The pet's text: the English translation only. Kana next to it got mixed up on the pet ("here" showed as
    /// "heりe"), so Japanese goes to the chatbox instead (KatDisplay).
    /// </summary>
    public static string Kat(TranslationResult r, bool showOriginal) => r.Translation;

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

    /// <summary>
    /// Pages for the pet, rowsPerPage rows of Row cells each: a word that does not fit on the row starts the next one
    /// (KAT itself just fills each row, splitting words: "I di / d it!"). A row that ends early is padded with spaces
    /// so the next starts on a new line. A word longer than a row is cut.
    /// </summary>
    public static List<string> KatPages(string text, int rowsPerPage = Core.Kat.TextLimit / Row)
    {
        var rows = new List<string>();
        string row = "";
        int used = 0;
        void NewRow() { rows.Add(row); row = ""; used = 0; }
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int width = Core.Kat.Cells(word);
            if (used > 0 && used + 1 + width > Row) NewRow();
            if (used == 0 && width > Row)
            {
                foreach (var ch in Core.Kat.Chars(word))
                {
                    int w = Core.Kat.CharWidth(ch);
                    if (used + w > Row) NewRow();
                    row += ch;
                    used += w;
                }
                continue;
            }
            if (used > 0) { row += " "; used++; }
            row += word;
            used += width;
        }
        if (used > 0) rows.Add(row);
        if (rows.Count == 0) return new List<string> { "" };

        var pages = new List<string>();
        for (int start = 0; start < rows.Count; start += rowsPerPage)
        {
            var pageRows = rows.Skip(start).Take(rowsPerPage).ToList();
            pages.Add(string.Concat(pageRows.Select((r, i) =>
                i < pageRows.Count - 1 ? r + new string(' ', Row - Core.Kat.Cells(r)) : r)));
        }
        return pages;
    }

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

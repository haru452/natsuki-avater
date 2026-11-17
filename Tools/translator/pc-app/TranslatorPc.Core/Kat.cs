// Port of the phone app's KatOsc: KAT (KillFrenzy Avatar Text) character codes and OSC message groups.
// The character table matches KAT v1.3.2's KAT_CharTiles.png (16 x 16 tiles).
using System.Text;

namespace TranslatorPc.Core;

public static class Kat
{
    public const int TextLimit = 128;
    public const int ClearPointer = 255;
    public const string Prefix = "/avatar/parameters/";
    public const string Pointer = Prefix + "KAT_Pointer";
    /// <summary>Must match TranslatorConfig.KatSyncChoices on the avatar side.</summary>
    public static readonly int[] SyncChoices = { 4, 8, 16 };

    static readonly (int start, string chars)[] Rows =
    {
        (129, "ふあうえおやゆよわをほへたてい"),
        (144, "すかんなにらせちとしはきくまのり"),
        (160, "れけむつさそひこみもねるめろ。ぶ"),
        (176, "ぷぼぽべぺだでずがぜぢどじばぱぎ"),
        (192, "ぐげづざぞびぴごぁぃぅぇぉゃゅょ"),
        (208, "ヌフアウエオヤユヨワヲホヘタテイ"),
        (224, "スカンナニラセチトシハキクマノリ"),
        (240, "レケムツサソヒコミモネルメロ゛゜"),
    };

    /// <summary>Characters the tile sheet lacks, replaced with the closest one it has.</summary>
    static readonly Dictionary<string, string> Substitutes = new()
    {
        ["っ"] = "つ", ["ゎ"] = "わ", ["ゔ"] = "う゛",
        ["ッ"] = "ツ", ["ャ"] = "ヤ", ["ュ"] = "ユ", ["ョ"] = "ヨ", ["ヮ"] = "ワ",
        ["ァ"] = "ア", ["ィ"] = "イ", ["ゥ"] = "ウ", ["ェ"] = "エ", ["ォ"] = "オ",
        ["ー"] = "-", ["、"] = ",", ["・"] = ".", ["「"] = "[", ["」"] = "]", ["『"] = "[", ["』"] = "]",
        ["〜"] = "~", ["～"] = "~", ["…"] = "...", ["’"] = "'", ["‘"] = "'", ["“"] = "\"", ["”"] = "\"",
        ["—"] = "-", ["–"] = "-", ["゙"] = "゛", ["゚"] = "゜", ["\n"] = " ", ["\t"] = " ", ["\r"] = " ",
    };

    static readonly Dictionary<string, int> Table = BuildTable();
    static readonly int Unknown = Table["?"];

    static Dictionary<string, int> BuildTable()
    {
        var t = new Dictionary<string, int>();
        for (int c = 32; c < 127; c++) t[((char)c).ToString()] = c - 32;
        t["€"] = 95;
        t["ぬ"] = 127;
        foreach (var (start, chars) in Rows)
            for (int i = 0; i < chars.Length; i++) t[chars[i].ToString()] = start + i;
        return t;
    }

    /// <summary>The text split into code points (so emoji stay whole).</summary>
    public static List<string> Chars(string text) => text.EnumerateRunes().Select(r => r.ToString()).ToList();

    static void AddCodes(string ch, List<int> into, int depth = 0)
    {
        if (Table.TryGetValue(ch, out int code)) { into.Add(code); return; }
        if (depth > 3) { into.Add(Unknown); return; }
        if (Substitutes.TryGetValue(ch, out var sub))
        {
            foreach (var c in Chars(sub)) AddCodes(c, into, depth + 1);
            return;
        }
        foreach (var form in new[] { NormalizationForm.FormKC, NormalizationForm.FormD })
        {
            string normalized;
            try { normalized = ch.Normalize(form); }
            catch (ArgumentException) { break; } // a lone surrogate
            if (normalized != ch)
            {
                foreach (var c in Chars(normalized)) AddCodes(c, into, depth + 1);
                return;
            }
        }
        into.Add(Unknown);
    }

    /// <summary>Text to KAT character codes, at most TextLimit. Unsupported characters become '?'.</summary>
    public static List<int> EncodeText(string text)
    {
        var codes = new List<int>();
        foreach (var ch in Chars(text))
        {
            AddCodes(ch, codes);
            if (codes.Count >= TextLimit) break;
        }
        return codes.Count > TextLimit ? codes.GetRange(0, TextLimit) : codes;
    }

    /// <summary>KAT cells one character takes: 2 for katakana written with a separate voicing mark.</summary>
    public static int CharWidth(string ch)
    {
        var codes = new List<int>();
        AddCodes(ch, codes);
        return codes.Count;
    }

    public static int Cells(string text) => Chars(text).Sum(CharWidth);

    /// <summary>KAT maps codes 0..127 to 0..1 and 129..255 to -1..-1/127.</summary>
    public static float CodeToFloat(int code) => (code > 127 ? code - 256 : code) / 127f;

    /// <summary>
    /// Groups of OSC messages in send order: optionally a clear, then per slot the pointer followed by its
    /// characters. KatDisplay sends a slot's characters a moment after its pointer.
    /// </summary>
    public static List<List<byte[]>> BuildMessages(string text, int sync = 8, bool clear = true)
    {
        if (!SyncChoices.Contains(sync)) throw new ArgumentException($"sync must be one of 4, 8, 16, got {sync}");
        var codes = EncodeText(text);
        var groups = new List<List<byte[]>>();
        if (clear) groups.Add(new List<byte[]> { Osc.Message(Pointer, ClearPointer) });
        for (int start = 0; start < codes.Count; start += sync)
        {
            var group = new List<byte[]> { Osc.Message(Pointer, start / sync + 1) };
            for (int i = 0; i < sync; i++)
            {
                int code = start + i < codes.Count ? codes[start + i] : 0;
                group.Add(Osc.Message(Prefix + "KAT_CharSync" + i, CodeToFloat(code)));
            }
            groups.Add(group);
        }
        return groups;
    }
}

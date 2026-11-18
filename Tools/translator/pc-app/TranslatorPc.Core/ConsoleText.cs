// Text for the console window: Japanese (and other wide letters) take two columns, so lengths in characters wrap
// lines past the edge, and a screen taller than the window scrolls on every redraw.
namespace TranslatorPc.Core;

public static class ConsoleText
{
    public static int Columns(char c) => IsWide(c) ? 2 : 1;

    public static int Columns(string s) => s.Sum(Columns);

    /// <summary>Cut to at most `width` columns and pad with spaces to exactly `width`.</summary>
    public static string Fit(string s, int width)
    {
        var sb = new System.Text.StringBuilder();
        int used = 0;
        foreach (char c in s)
        {
            int w = Columns(c);
            if (used + w > width) break;
            sb.Append(c);
            used += w;
        }
        return sb.Append(' ', width - used).ToString();
    }

    /// <summary>The status lines and as many of the newest messages as fit in `height` lines (one spare for the cursor).</summary>
    public static IReadOnlyList<string> Lines(IReadOnlyList<string> status, IReadOnlyList<string> messages, int height)
    {
        int room = Math.Max(0, height - 1 - status.Count);
        return status.Concat(messages.Skip(Math.Max(0, messages.Count - room))).ToList();
    }

    static bool IsWide(char c) =>
        c is >= '\u1100' and <= '\u115F'        // Hangul Jamo
            or >= '\u2E80' and <= '\u303E'      // CJK radicals, punctuation
            or >= '\u3041' and <= '\u33FF'      // kana, CJK symbols
            or >= '\u3400' and <= '\u4DBF'      // CJK extension A
            or >= '\u4E00' and <= '\u9FFF'      // CJK
            or >= '\uA000' and <= '\uA4CF'      // Yi
            or >= '\uAC00' and <= '\uD7A3'      // Hangul
            or >= '\uF900' and <= '\uFAFF'      // CJK compatibility
            or >= '\uFE30' and <= '\uFE4F'      // CJK compatibility forms
            or >= '\uFF00' and <= '\uFF60'      // full-width forms
            or >= '\uFFE0' and <= '\uFFE6'
            or '―';                             // the box line in the screen's heading
}

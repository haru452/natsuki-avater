// Port of Tools/translator/kat_osc.py: KAT (KillFrenzy Avatar Text) character codes and OSC messages.
// The character table matches KAT v1.3.2's KAT_CharTiles.png (16 x 16 tiles).
// The avatar's toggle owns KAT_Visible; the sender only clears (KAT_Pointer = 255) and writes.
// Each group sends the pointer before its characters, because KAT writes the CharSync floats into
// whichever slot the pointer names.
using System;
using System.Collections.Generic;
using System.Text;

namespace Translator.Core
{
    public static class KatOsc
    {
        public const int TextLimit = 128;
        public const int ClearPointer = 255;
        public const string ParamPrefix = "/avatar/parameters/";
        // Must match TranslatorConfig.KatSyncChoices on the avatar side.
        public static readonly int[] SyncChoices = { 4, 8, 16 };

        static readonly Dictionary<int, string> Rows = new Dictionary<int, string>
        {
            { 129, "ふあうえおやゆよわをほへたてい" },
            { 144, "すかんなにらせちとしはきくまのり" },
            { 160, "れけむつさそひこみもねるめろ。ぶ" },
            { 176, "ぷぼぽべぺだでずがぜぢどじばぱぎ" },
            { 192, "ぐげづざぞびぴごぁぃぅぇぉゃゅょ" },
            { 208, "ヌフアウエオヤユヨワヲホヘタテイ" },
            { 224, "スカンナニラセチトシハキクマノリ" },
            { 240, "レケムツサソヒコミモネルメロ゛゜" },
        };

        // Characters the tile sheet lacks, replaced with the closest one it has.
        static readonly Dictionary<string, string> Substitutes = new Dictionary<string, string>
        {
            { "っ", "つ" }, { "ゎ", "わ" }, { "ゔ", "う゛" },
            { "ッ", "ツ" }, { "ャ", "ヤ" }, { "ュ", "ユ" }, { "ョ", "ヨ" }, { "ヮ", "ワ" },
            { "ァ", "ア" }, { "ィ", "イ" }, { "ゥ", "ウ" }, { "ェ", "エ" }, { "ォ", "オ" },
            { "ー", "-" }, { "、", "," }, { "・", "." }, { "「", "[" }, { "」", "]" }, { "『", "[" }, { "』", "]" },
            { "〜", "~" }, { "～", "~" }, { "…", "..." }, { "’", "'" }, { "‘", "'" }, { "“", "\"" }, { "”", "\"" },
            { "—", "-" }, { "–", "-" }, { "゙", "゛" }, { "゚", "゜" }, { "\n", " " }, { "\t", " " }, { "\r", " " },
        };

        static readonly Dictionary<string, int> Table = BuildTable();
        static readonly int Unknown = Table["?"];
        static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        static Dictionary<string, int> BuildTable()
        {
            var t = new Dictionary<string, int>();
            for (int c = 32; c < 127; c++) t[((char)c).ToString()] = c - 32;
            t["€"] = 95;
            t["ぬ"] = 127;
            foreach (var row in Rows)
                for (int i = 0; i < row.Value.Length; i++) t[row.Value[i].ToString()] = row.Key + i;
            return t;
        }

        static IEnumerable<string> CodePoints(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    yield return text.Substring(i, 2);
                    i++;
                }
                else yield return text[i].ToString();
            }
        }

        static void AddCodes(string ch, List<int> into, int depth = 0)
        {
            if (Table.TryGetValue(ch, out int code)) { into.Add(code); return; }
            if (depth > 3) { into.Add(Unknown); return; }
            if (Substitutes.TryGetValue(ch, out string sub))
            {
                foreach (var part in CodePoints(sub)) AddCodes(part, into, depth + 1);
                return;
            }
            if (ch.Length == 1 && !char.IsSurrogate(ch[0]))
            {
                foreach (var form in new[] { NormalizationForm.FormKC, NormalizationForm.FormD })
                {
                    string normalized = ch.Normalize(form);
                    if (normalized != ch)
                    {
                        foreach (var part in CodePoints(normalized)) AddCodes(part, into, depth + 1);
                        return;
                    }
                }
            }
            into.Add(Unknown);
        }

        /// <summary>Text to KAT character codes, at most TextLimit. Unsupported characters become '?'.</summary>
        public static List<int> EncodeText(string text)
        {
            var codes = new List<int>();
            foreach (var ch in CodePoints(text ?? ""))
            {
                AddCodes(ch, codes);
                if (codes.Count >= TextLimit) break;
            }
            if (codes.Count > TextLimit) codes.RemoveRange(TextLimit, codes.Count - TextLimit);
            return codes;
        }

        /// <summary>How many KAT cells one character takes (2 for katakana written with a separate voicing mark).</summary>
        public static int CharWidth(char ch)
        {
            var codes = new List<int>();
            AddCodes(ch.ToString(), codes);
            return codes.Count;
        }

        /// <summary>KAT maps codes 0..127 to 0..1 and 129..255 to -1..-1/127.</summary>
        public static float CodeToFloat(int code) => (code > 127 ? code - 256 : code) / 127f;

        static void AddPadded(List<byte> into, byte[] raw)
        {
            into.AddRange(raw);
            int pad = 4 - raw.Length % 4;
            for (int i = 0; i < pad; i++) into.Add(0);
        }

        static void AddBigEndian(List<byte> into, byte[] b)
        {
            if (BitConverter.IsLittleEndian) Array.Reverse(b);
            into.AddRange(b);
        }

        /// <summary>One OSC message. Values may be bool, int, float or string (sent as UTF-8).</summary>
        public static byte[] OscMessage(string address, params object[] values)
        {
            var tags = new StringBuilder(",");
            var body = new List<byte>();
            foreach (var value in values)
            {
                switch (value)
                {
                    case bool b: tags.Append(b ? 'T' : 'F'); break;
                    case int i: tags.Append('i'); AddBigEndian(body, BitConverter.GetBytes(i)); break;
                    case float f: tags.Append('f'); AddBigEndian(body, BitConverter.GetBytes(f)); break;
                    case double d: tags.Append('f'); AddBigEndian(body, BitConverter.GetBytes((float)d)); break;
                    case string s: tags.Append('s'); AddPadded(body, Encoding.UTF8.GetBytes(s)); break;
                    default: throw new ArgumentException("unsupported OSC value: " + value);
                }
            }
            var o = new List<byte>();
            AddPadded(o, Encoding.ASCII.GetBytes(address));
            AddPadded(o, Encoding.ASCII.GetBytes(tags.ToString()));
            o.AddRange(body);
            return o.ToArray();
        }

        static string ReadString(byte[] data, ref int pos)
        {
            int end = Array.IndexOf(data, (byte)0, pos);
            if (end < 0) throw new FormatException("unterminated OSC string");
            string s = StrictUtf8.GetString(data, pos, end - pos);
            pos = (end / 4 + 1) * 4;
            return s;
        }

        static byte[] Take4(byte[] data, ref int pos)
        {
            if (pos + 4 > data.Length) throw new FormatException("truncated OSC argument");
            var b = new byte[4];
            Array.Copy(data, pos, b, 0, 4);
            if (BitConverter.IsLittleEndian) Array.Reverse(b);
            pos += 4;
            return b;
        }

        /// <summary>(address, values) from one OSC message. Throws FormatException on anything malformed.</summary>
        public static (string Address, object[] Args) ParseMessage(byte[] data)
        {
            try
            {
                int pos = 0;
                string address = ReadString(data, ref pos);
                if (!address.StartsWith("/")) throw new FormatException("OSC address must start with /");
                if (pos > data.Length) throw new FormatException("missing OSC type tags");
                string tags = ReadString(data, ref pos);
                if (!tags.StartsWith(",")) throw new FormatException("missing OSC type tags");
                var values = new List<object>();
                foreach (char tag in tags.Substring(1))
                {
                    switch (tag)
                    {
                        case 'T': values.Add(true); break;
                        case 'F': values.Add(false); break;
                        case 'i': values.Add(BitConverter.ToInt32(Take4(data, ref pos), 0)); break;
                        case 'f': values.Add(BitConverter.ToSingle(Take4(data, ref pos), 0)); break;
                        case 's':
                            if (pos > data.Length) throw new FormatException("truncated OSC argument");
                            values.Add(ReadString(data, ref pos));
                            break;
                        default: throw new FormatException("unsupported OSC type tag " + tag);
                    }
                }
                return (address, values.ToArray());
            }
            catch (DecoderFallbackException e)
            {
                throw new FormatException(e.Message, e);
            }
        }

        /// <summary>
        /// Groups of OSC messages in send order; wait between groups so each one syncs.
        /// clear = false rewrites the same slots without wiping first (periodic resends, no flicker).
        /// </summary>
        public static List<List<byte[]>> BuildMessages(string text, int sync = 8, bool clear = true)
        {
            if (Array.IndexOf(SyncChoices, sync) < 0)
                throw new ArgumentException($"sync must be one of 4, 8, 16, got {sync}");
            var codes = EncodeText(text);
            var groups = new List<List<byte[]>>();
            if (clear) groups.Add(new List<byte[]> { OscMessage(ParamPrefix + "KAT_Pointer", ClearPointer) });
            for (int start = 0; start < codes.Count; start += sync)
            {
                var group = new List<byte[]> { OscMessage(ParamPrefix + "KAT_Pointer", start / sync + 1) };
                for (int i = 0; i < sync; i++)
                {
                    int code = start + i < codes.Count ? codes[start + i] : 0;
                    group.Add(OscMessage($"{ParamPrefix}KAT_CharSync{i}", CodeToFloat(code)));
                }
                groups.Add(group);
            }
            return groups;
        }
    }
}

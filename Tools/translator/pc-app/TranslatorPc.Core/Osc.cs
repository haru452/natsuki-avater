// OSC messages: the osc_message / parse_message part of Tools/translator/kat_osc.py.
using System.Buffers.Binary;
using System.Text;

namespace TranslatorPc.Core;

public static class Osc
{
    static readonly UTF8Encoding StrictUtf8 = new(false, true);

    static void AddPadded(List<byte> into, byte[] raw)
    {
        into.AddRange(raw);
        into.AddRange(new byte[4 - raw.Length % 4]);
    }

    /// <summary>One OSC message. Values may be bool, int, float or string (UTF-8).</summary>
    public static byte[] Message(string address, params object[] values)
    {
        var tags = new StringBuilder(",");
        var body = new List<byte>();
        var four = new byte[4];
        foreach (var value in values)
        {
            switch (value)
            {
                case bool b: tags.Append(b ? 'T' : 'F'); break;
                case int i: tags.Append('i'); BinaryPrimitives.WriteInt32BigEndian(four, i); body.AddRange(four); break;
                case float f: tags.Append('f'); BinaryPrimitives.WriteSingleBigEndian(four, f); body.AddRange(four); break;
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
        if (pos > data.Length) throw new FormatException("truncated OSC message");
        int end = Array.IndexOf(data, (byte)0, pos);
        if (end < 0) throw new FormatException("unterminated OSC string");
        string s;
        try { s = StrictUtf8.GetString(data, pos, end - pos); }
        catch (DecoderFallbackException e) { throw new FormatException("bad UTF-8 in OSC string", e); }
        pos = (end / 4 + 1) * 4;
        return s;
    }

    static ReadOnlySpan<byte> Take4(byte[] data, ref int pos)
    {
        if (pos + 4 > data.Length) throw new FormatException("truncated OSC argument");
        var span = new ReadOnlySpan<byte>(data, pos, 4);
        pos += 4;
        return span;
    }

    /// <summary>(address, values) from one OSC message. Throws FormatException on anything malformed.</summary>
    public static (string Address, object[] Args) Parse(byte[] data)
    {
        int pos = 0;
        string address = ReadString(data, ref pos);
        if (!address.StartsWith('/')) throw new FormatException("OSC address must start with /");
        string tags = ReadString(data, ref pos);
        if (!tags.StartsWith(',')) throw new FormatException("missing OSC type tags");
        var values = new List<object>();
        foreach (char tag in tags[1..])
        {
            switch (tag)
            {
                case 'T': values.Add(true); break;
                case 'F': values.Add(false); break;
                case 'i': values.Add(BinaryPrimitives.ReadInt32BigEndian(Take4(data, ref pos))); break;
                case 'f': values.Add(BinaryPrimitives.ReadSingleBigEndian(Take4(data, ref pos))); break;
                case 's': values.Add(ReadString(data, ref pos)); break;
                default: throw new FormatException("unsupported OSC type tag " + tag);
            }
        }
        return (address, values.ToArray());
    }
}

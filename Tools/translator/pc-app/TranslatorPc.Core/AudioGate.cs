// Audio to the phone: the packet layout, and the rules for what may be sent and when.
using System.Buffers.Binary;
using System.Net;

namespace TranslatorPc.Core;

public enum StreamKind : byte { OwnMic = 0, Vrchat = 1 }

/// <summary>"VTA1", kind (1 byte), sequence (4 bytes, big-endian), 320 samples of 16 kHz mono 16-bit little-endian.</summary>
public static class AudioPacket
{
    public const int Samples = 320;
    public const int Port = 9003;
    public const int Length = 9 + Samples * 2;
    static readonly byte[] Magic = "VTA1"u8.ToArray();

    public static byte[] Encode(StreamKind kind, uint seq, short[] samples)
    {
        if (samples.Length != Samples) throw new ArgumentException($"need {Samples} samples, got {samples.Length}");
        var p = new byte[Length];
        Magic.CopyTo(p, 0);
        p[4] = (byte)kind;
        BinaryPrimitives.WriteUInt32BigEndian(p.AsSpan(5), seq);
        for (int i = 0; i < Samples; i++) BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(9 + i * 2), samples[i]);
        return p;
    }
}

/// <summary>
/// Your microphone goes out only while the phone asked for the PC mic and the avatar's translator is on;
/// VRChat's sound only while the phone wants the other players' voices. Nothing once the phone has gone quiet.
/// </summary>
public class AudioGate
{
    readonly Relay relay;

    public AudioGate(Relay relay) => this.relay = relay;

    public IPEndPoint? Destination(StreamKind kind, DateTime now)
    {
        var phone = relay.Phone;
        if (phone == null || !relay.PhoneAlive(now)) return null;
        bool allowed = kind switch
        {
            StreamKind.OwnMic => phone.PcMic && relay.TranslatorActive,
            StreamKind.Vrchat => phone.OthersOn,
            _ => false,
        };
        return allowed ? new IPEndPoint(phone.Address, AudioPacket.Port) : null;
    }
}

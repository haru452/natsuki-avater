using System.Net;
using TranslatorPc.Core;
using Xunit;

namespace TranslatorPc.Tests;

public class AudioPacketTests
{
    [Fact]
    public void EncodeLayout()
    {
        var samples = new short[320];
        samples[0] = 0x0102;
        samples[319] = -2;
        var p = AudioPacket.Encode(StreamKind.Vrchat, 0x01020304u, samples);
        Assert.Equal(649, p.Length);
        Assert.Equal("VTA1"u8.ToArray(), p[..4]);
        Assert.Equal(1, p[4]);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, p[5..9]);
        Assert.Equal(new byte[] { 0x02, 0x01 }, p[9..11]);    // little-endian samples
        Assert.Equal(new byte[] { 0xFE, 0xFF }, p[647..649]);
    }

    [Fact]
    public void EncodeRejectsWrongLength() =>
        Assert.Throws<ArgumentException>(() => AudioPacket.Encode(StreamKind.OwnMic, 0, new short[300]));
}

public class AudioGateTests
{
    static readonly DateTime T0 = new(2026, 11, 17, 12, 0, 0);
    static readonly IPEndPoint PhoneFrom = new(IPAddress.Parse("192.168.3.218"), 50000);
    static readonly IPEndPoint PhoneAudio = new(IPAddress.Parse("192.168.3.218"), 9003);

    static (Relay, AudioGate) Make(bool othersOn, bool pcMic, bool active)
    {
        var relay = new Relay(new IPEndPoint(IPAddress.Loopback, 9000));
        relay.FromPhone(Osc.Message(Relay.Hello, 1, othersOn, pcMic), PhoneFrom, T0);
        if (active) relay.FromVrchat(Osc.Message(Relay.Active, true));
        return (relay, new AudioGate(relay));
    }

    [Fact]
    public void NothingWithoutAPhone()
    {
        var gate = new AudioGate(new Relay(new IPEndPoint(IPAddress.Loopback, 9000)));
        Assert.Null(gate.Destination(StreamKind.OwnMic, T0));
        Assert.Null(gate.Destination(StreamKind.Vrchat, T0));
    }

    [Fact]
    public void EverythingWhenAllowed()
    {
        var (_, gate) = Make(othersOn: true, pcMic: true, active: true);
        Assert.Equal(PhoneAudio, gate.Destination(StreamKind.OwnMic, T0));
        Assert.Equal(PhoneAudio, gate.Destination(StreamKind.Vrchat, T0));
    }

    [Fact]
    public void MicOnlyWhenThePhoneWantsThePcMic() =>
        Assert.Null(Make(othersOn: true, pcMic: false, active: true).Item2.Destination(StreamKind.OwnMic, T0));

    [Fact]
    public void MicNeverWhileTheTranslatorIsOff()
    {
        var (relay, gate) = Make(othersOn: true, pcMic: true, active: true);
        relay.FromVrchat(Osc.Message(Relay.Active, false));
        Assert.Null(gate.Destination(StreamKind.OwnMic, T0));
        Assert.NotNull(gate.Destination(StreamKind.Vrchat, T0));
    }

    [Fact]
    public void VrchatSoundOnlyWhenOthersOn() =>
        Assert.Null(Make(othersOn: false, pcMic: true, active: true).Item2.Destination(StreamKind.Vrchat, T0));

    [Fact]
    public void StopsWhenThePhoneGoesQuiet()
    {
        var (_, gate) = Make(othersOn: true, pcMic: true, active: true);
        Assert.Null(gate.Destination(StreamKind.OwnMic, T0.AddSeconds(16)));
        Assert.Null(gate.Destination(StreamKind.Vrchat, T0.AddSeconds(16)));
    }
}

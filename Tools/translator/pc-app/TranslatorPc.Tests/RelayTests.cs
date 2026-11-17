// Ported from Tools/translator/pc/test_relay.py and the OSC part of Tools/translator/test_kat_osc.py.
// Difference: FromPhone answers every hello with an ack itself (Python had a separate ack_for).
using System.Net;
using TranslatorPc.Core;
using Xunit;

namespace TranslatorPc.Tests;

public class OscTests
{
    static byte[] B(string s) => s.Select(c => (byte)c).ToArray();

    [Fact]
    public void MessageLayout()
    {
        Assert.Equal(B("/a\0\0,i\0\0\0\0\0\x01"), Osc.Message("/a", 1));
        Assert.Equal(B("/a\0\0,T\0\0"), Osc.Message("/a", true));
        Assert.Equal(B("/abc\0\0\0\0,F\0\0"), Osc.Message("/abc", false));
    }

    [Fact]
    public void RoundTripEachType()
    {
        foreach (var value in new object[] { true, false, 7, -3, 0.5f, "おはよう Hi" })
        {
            var (address, args) = Osc.Parse(Osc.Message("/a/b", value));
            Assert.Equal("/a/b", address);
            Assert.Equal(new[] { value }, args);
        }
    }

    [Fact]
    public void SeveralArguments()
    {
        var (address, args) = Osc.Parse(Osc.Message("/translator/hello", 5, true, false));
        Assert.Equal("/translator/hello", address);
        Assert.Equal(new object[] { 5, true, false }, args);
    }

    [Fact]
    public void BrokenPacketsThrow()
    {
        foreach (var data in new[] { B(""), B("/a"), B("/a\0\0"), B("/a\0\0,i\0\0\0\x01"), B("/a\0\0,x\0\0"),
                     new byte[] { 0xff, 0xfe, 0, 0, (byte)',', (byte)'i', 0, 0 } })
            Assert.Throws<FormatException>(() => Osc.Parse(data));
    }
}

public class RelayTests
{
    static readonly IPEndPoint Vrchat = new(IPAddress.Loopback, 9000);
    static readonly IPEndPoint PhoneFrom = new(IPAddress.Parse("192.168.1.20"), 50123);
    static readonly IPEndPoint PhoneIn = new(IPAddress.Parse("192.168.1.20"), 9001);
    static readonly byte[] Hello = Osc.Message("/translator/hello", 1, false, true);
    static readonly byte[] Ack = Osc.Message("/translator/ack", true);
    static readonly byte[] ActiveOn = Osc.Message("/avatar/parameters/TranslatorActive", true);
    static readonly byte[] LocalAI = Osc.Message("/avatar/parameters/TranslatorLocalAI", false);
    static readonly byte[] Change = Osc.Message("/avatar/change", "avtr_123");
    static readonly DateTime T0 = new(2026, 11, 17, 12, 0, 0);

    readonly Relay r = new(Vrchat);

    /// <summary>Packets as comparable text: byte arrays compare by reference, so compare their hex.</summary>
    static List<string> L(params (byte[], IPEndPoint)[] items) => items.Select(i => Convert.ToHexString(i.Item1) + "->" + i.Item2).ToList();
    static List<string> L(List<(byte[] Packet, IPEndPoint To)> items) => items.Select(i => Convert.ToHexString(i.Packet) + "->" + i.To).ToList();

    [Fact]
    public void VrchatValuesWaitForThePhone()
    {
        Assert.Empty(r.FromVrchat(ActiveOn));
        Assert.Equal(L((Ack, PhoneIn), (ActiveOn, PhoneIn)), L(r.FromPhone(Hello, PhoneFrom, T0)));
    }

    [Fact]
    public void AfterHelloValuesGoStraightToThePhone()
    {
        r.FromPhone(Hello, PhoneFrom, T0);
        Assert.Equal(L((ActiveOn, PhoneIn)), L(r.FromVrchat(ActiveOn)));
        Assert.Equal(L((LocalAI, PhoneIn)), L(r.FromVrchat(LocalAI)));
    }

    [Fact]
    public void AvatarChangeForgetsOldValues()
    {
        r.FromVrchat(ActiveOn);
        r.FromPhone(Hello, PhoneFrom, T0);
        Assert.Equal(L((Change, PhoneIn)), L(r.FromVrchat(Change)));
        Assert.Equal(L((Ack, PhoneIn)), L(r.FromPhone(Osc.Message("/translator/hello", 2, false, true), PhoneFrom, T0)));
    }

    [Fact]
    public void OtherAvatarOutputIsDropped()
    {
        r.FromPhone(Hello, PhoneFrom, T0);
        foreach (var (a, v) in new (string, object)[] { ("/avatar/parameters/GestureLeft", 3), ("/avatar/parameters/VelocityX", 0.5f), ("/avatar/parameters/KAT_Pointer", 1) })
            Assert.Empty(r.FromVrchat(Osc.Message(a, v)));
    }

    [Fact]
    public void PhoneTextGoesToVrchat()
    {
        var kat = Osc.Message("/avatar/parameters/KAT_Pointer", 255);
        var chat = Osc.Message("/chatbox/input", "hi", true, false);
        Assert.Equal(L((kat, Vrchat)), L(r.FromPhone(kat, PhoneFrom, T0)));
        Assert.Equal(L((chat, Vrchat)), L(r.FromPhone(chat, PhoneFrom, T0)));
    }

    [Fact]
    public void OnlyTextMessagesReachVrchat()
    {
        foreach (var (a, v) in new (string, object)[] { ("/avatar/parameters/GestureLeft", 1), ("/input/Jump", 1), ("/avatar/parameters/TranslatorActive", true) })
            Assert.Empty(r.FromPhone(Osc.Message(a, v), PhoneFrom, T0));
    }

    [Fact]
    public void PhoneThatMovesIsFollowed()
    {
        r.FromPhone(Hello, PhoneFrom, T0);
        r.FromPhone(Hello, new IPEndPoint(IPAddress.Parse("192.168.1.33"), 40000), T0);
        Assert.Equal(L((ActiveOn, new IPEndPoint(IPAddress.Parse("192.168.1.33"), 9001))), L(r.FromVrchat(ActiveOn)));
    }

    [Fact]
    public void HelloReplaysValuesOnlyForANewPhone()
    {
        r.FromVrchat(ActiveOn);
        Assert.Equal(2, r.FromPhone(Hello, PhoneFrom, T0).Count);
        Assert.Equal(L((Ack, PhoneIn)), L(r.FromPhone(Hello, PhoneFrom, T0)));
    }

    [Fact]
    public void RestartedPhoneGetsTheValuesAgain()
    {
        r.FromVrchat(ActiveOn);
        r.FromPhone(Osc.Message("/translator/hello", 111, false, true), PhoneFrom, T0);
        Assert.Equal(L((Ack, PhoneIn), (ActiveOn, PhoneIn)), L(r.FromPhone(Osc.Message("/translator/hello", 222, false, true), PhoneFrom, T0)));
    }

    [Fact]
    public void BrokenPacketsAreIgnored()
    {
        Assert.Empty(r.FromVrchat(new byte[] { 0, 1, 2 }));
        Assert.Empty(r.FromPhone(new byte[] { 1, 2, 3 }, PhoneFrom, T0));
        Assert.Null(r.Phone);
    }

    [Fact]
    public void HelloCarriesFlags()
    {
        r.FromPhone(Osc.Message("/translator/hello", 5, true, false), PhoneFrom, T0);
        Assert.True(r.Phone!.OthersOn);
        Assert.False(r.Phone.PcMic);
        Assert.Equal(5, r.Phone.Session);
    }

    [Fact]
    public void OldHelloWithOneArgumentStillWorks()
    {
        r.FromPhone(Osc.Message("/translator/hello", 5), PhoneFrom, T0);
        Assert.False(r.Phone!.OthersOn);
        Assert.False(r.Phone.PcMic);
    }

    [Fact]
    public void PhoneIsForgottenAfter15Seconds()
    {
        r.FromPhone(Hello, PhoneFrom, T0);
        Assert.True(r.PhoneAlive(T0.AddSeconds(14)));
        Assert.False(r.PhoneAlive(T0.AddSeconds(16)));
    }

    [Fact]
    public void TranslatorActiveFollowsVrchat()
    {
        Assert.False(r.TranslatorActive);
        r.FromVrchat(ActiveOn);
        Assert.True(r.TranslatorActive);
        r.FromVrchat(Change);
        Assert.False(r.TranslatorActive);
        r.FromVrchat(Osc.Message("/avatar/parameters/TranslatorActive", 1));
        Assert.True(r.TranslatorActive);
        r.FromVrchat(Osc.Message("/avatar/parameters/TranslatorActive", false));
        Assert.False(r.TranslatorActive);
    }
}

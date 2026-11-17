// Port of the Relay class in Tools/translator/pc/pc_osc_relay.py: decides where each OSC packet goes.
// No sockets here, so it can be tested on its own.
using System.Net;

namespace TranslatorPc.Core;

/// <summary>The phone as it last said hello: where it is and what it wants.</summary>
public record Phone(IPAddress Address, int Session, bool OthersOn, bool PcMic, DateTime LastHello);

public class Relay
{
    public const string Hello = "/translator/hello";
    public const string Ack = "/translator/ack";
    public const string Active = "/avatar/parameters/TranslatorActive";
    public const string LocalAI = "/avatar/parameters/TranslatorLocalAI";
    public const string AvatarChange = "/avatar/change";
    public static readonly TimeSpan PhoneTimeout = TimeSpan.FromSeconds(15);

    static readonly HashSet<string> ForwardToPhone = new() { Active, LocalAI, AvatarChange };
    // The phone may only write text: the KAT values and the chatbox. Nothing that moves or changes the avatar.
    static readonly string[] FromPhonePrefixes = { "/avatar/parameters/KAT_", "/chatbox/" };

    readonly IPEndPoint vrchat;
    readonly int phonePort;
    readonly Dictionary<string, byte[]> latest = new();  // address -> last packet, for a phone that (re)joins

    public Relay(IPEndPoint vrchat, int phonePort = 9001)
    {
        this.vrchat = vrchat;
        this.phonePort = phonePort;
    }

    public Phone? Phone { get; private set; }
    public bool TranslatorActive { get; private set; }
    public IPEndPoint? PhoneEndPoint => Phone == null ? null : new IPEndPoint(Phone.Address, phonePort);

    public bool PhoneAlive(DateTime now) => Phone != null && now - Phone.LastHello <= PhoneTimeout;

    static bool Truthy(object v) => v switch { bool b => b, int i => i != 0, float f => f != 0f, _ => false };

    public List<(byte[] Packet, IPEndPoint To)> FromPhone(byte[] data, IPEndPoint from, DateTime now)
    {
        var o = new List<(byte[], IPEndPoint)>();
        string address;
        object[] args;
        try { (address, args) = Osc.Parse(data); }
        catch (FormatException) { return o; }

        if (address == Hello)
        {
            int session = args.Length > 0 && args[0] is int s ? s : 0;
            bool othersOn = args.Length > 1 && Truthy(args[1]);
            bool pcMic = args.Length > 2 && Truthy(args[2]);
            bool isNew = Phone == null || !Phone.Address.Equals(from.Address) || Phone.Session != session;
            Phone = new Phone(from.Address, session, othersOn, pcMic, now);
            var to = PhoneEndPoint!;
            // Every hello is answered, so the phone can show the PC is there before VRChat sends anything.
            o.Add((Osc.Message(Ack, true), to));
            if (isNew) foreach (var packet in latest.Values) o.Add((packet, to));
            return o;
        }
        if (FromPhonePrefixes.Any(address.StartsWith)) o.Add((data, vrchat));
        return o;
    }

    public List<(byte[] Packet, IPEndPoint To)> FromVrchat(byte[] data)
    {
        var o = new List<(byte[], IPEndPoint)>();
        string address;
        object[] args;
        try { (address, args) = Osc.Parse(data); }
        catch (FormatException) { return o; }
        if (!ForwardToPhone.Contains(address)) return o;

        if (address == AvatarChange)
        {
            latest.Clear();  // a new avatar starts with the translator off
            TranslatorActive = false;
        }
        else
        {
            latest[address] = data;
            if (address == Active && args.Length > 0) TranslatorActive = Truthy(args[0]);
        }
        if (PhoneEndPoint is { } to) o.Add((data, to));
        return o;
    }
}

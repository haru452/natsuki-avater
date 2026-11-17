// translator_pc.exe: the PC side of VRC Translator.
// - Relays OSC between VRChat (9000/9001) and the phone app (9002 in, 9001 out).
// - Sends your PC microphone (while 翻訳 is on) and VRChat's sound (when the phone asks) to the phone on 9003.
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using TranslatorPc.Core;

namespace TranslatorPc;

static class Program
{
    const int FromVrchatPort = 9001, FromPhonePort = 9002;
    static readonly IPEndPoint Vrchat = new(IPAddress.Loopback, 9000);

    static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.Title = "VRC Translator PC";

        UdpClient fromPhone, fromVrchat;
        try
        {
            fromPhone = Bind(FromPhonePort);
            fromVrchat = Bind(FromVrchatPort);
        }
        catch (SocketException e)
        {
            Console.WriteLine($"ポート {FromPhonePort} か {FromVrchatPort} が使われています（{e.Message}）。");
            Console.WriteLine("このプログラムがもう開いていないか、古い中継（pc_osc_relay.py）が動いていないか確かめてください。");
            Console.WriteLine("Enter で終わります。");
            Console.ReadLine();
            return 1;
        }

        var relay = new Relay(Vrchat);
        var gate = new AudioGate(relay);
        var gateLock = new object();
        IPEndPoint? Destination(StreamKind k) { lock (gateLock) return gate.Destination(k, DateTime.UtcNow); }

        var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };

        // The phone socket also sends everything (to VRChat and to the phone).
        void SendAll(List<(byte[] Packet, IPEndPoint To)> outgoing)
        {
            foreach (var (packet, to) in outgoing)
                try { fromPhone.Send(packet, packet.Length, to); } catch (SocketException) { }
        }
        StartReceiver(fromPhone, stop.Token, (data, from) =>
        {
            List<(byte[], IPEndPoint)> o;
            lock (gateLock) o = relay.FromPhone(data, from, DateTime.UtcNow);
            SendAll(o);
        });
        StartReceiver(fromVrchat, stop.Token, (data, from) =>
        {
            // Our own forward (sent from FromPhonePort) landing back here: never relay it again, or it loops.
            if (from.Port == FromPhonePort && IPAddress.IsLoopback(from.Address)) return;
            List<(byte[], IPEndPoint)> o;
            lock (gateLock) o = relay.FromVrchat(data);
            SendAll(o);
        });

        using var audioSocket = new UdpClient();
        var micSender = new AudioSender(StreamKind.OwnMic, Destination, audioSocket);
        var vrcSender = new AudioSender(StreamKind.Vrchat, Destination, audioSocket);
        using var mic = new MicSource(micSender);
        using var vrchat = new VrchatSource(vrcSender);

        var screen = new StatusScreen();
        while (!stop.IsCancellationRequested)
        {
            Phone? phone;
            bool alive, active;
            lock (gateLock) { phone = relay.Phone; alive = relay.PhoneAlive(DateTime.UtcNow); active = relay.TranslatorActive; }
            mic.Update(Destination(StreamKind.OwnMic) != null);
            vrchat.Update(Destination(StreamKind.Vrchat) != null);
            screen.Show(new[]
            {
                "VRC Translator PC（Ctrl+C で終わります）",
                "",
                "この PC の IP:   " + string.Join(", ", LocalAddresses()) + "（スマホのアプリに入れます）",
                "スマホ:          " + (phone == null ? "スマホを待っています" : alive ? $"見つかった（{phone.Address}）" : $"返事がありません（{phone.Address}）"),
                "翻訳:            " + (active ? "オン" : "オフ（アバターの「翻訳」で切り替え）"),
                "自分のマイク:    " + mic.State + $"  {micSender.TakeCount()} 個/秒",
                "VRChat の音:     " + vrchat.State + $"  {vrcSender.TakeCount()} 個/秒",
                phone != null && !phone.PcMic ? "（スマホのアプリは「マイク: スマホ」になっています）" : "",
            });
            stop.Token.WaitHandle.WaitOne(1000);
        }
        fromPhone.Close();
        fromVrchat.Close();
        return 0;
    }

    static UdpClient Bind(int port)
    {
        var c = new UdpClient(AddressFamily.InterNetwork);
        c.Client.Bind(new IPEndPoint(IPAddress.Any, port));
        // Windows reports "port unreachable" from an earlier send as an error on the next receive; ignore it.
        const int SIO_UDP_CONNRESET = -1744830452;
        c.Client.IOControl(SIO_UDP_CONNRESET, new byte[] { 0 }, null);
        return c;
    }

    static void StartReceiver(UdpClient socket, CancellationToken stop, Action<byte[], IPEndPoint> onPacket)
    {
        new Thread(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    var from = new IPEndPoint(IPAddress.Any, 0);
                    var data = socket.Receive(ref from);
                    onPacket(data, from);
                }
                catch (SocketException) { if (stop.IsCancellationRequested) return; }
                catch (ObjectDisposedException) { return; }
            }
        }) { IsBackground = true }.Start();
    }

    /// <summary>This PC's LAN addresses (the ones with a gateway, so not VPN or virtual adapters).</summary>
    static IEnumerable<string> LocalAddresses()
    {
        var found = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .Select(n => n.GetIPProperties())
            .Where(p => p.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork))
            .SelectMany(p => p.UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(a => a.Address.ToString())
            .ToList();
        return found.Count > 0 ? found : new List<string> { "（見つかりません）" };
    }
}

/// <summary>Redraws the same lines in place once a second (or just prints when the output is redirected).</summary>
class StatusScreen
{
    int top = -1;

    public void Show(string[] lines)
    {
        if (Console.IsOutputRedirected)
        {
            Console.WriteLine(string.Join(" | ", lines.Where(l => l.Length > 0)));
            return;
        }
        if (top < 0) { Console.Clear(); top = 0; }
        Console.SetCursorPosition(0, top);
        int width = Math.Max(20, Console.WindowWidth - 1);
        foreach (var l in lines) Console.WriteLine(l.Length > width ? l[..width] : l.PadRight(width));
    }
}

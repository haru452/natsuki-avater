// A very small web server for the phone's page (OthersFeed): GET only, one answer per connection. A plain socket
// (not HttpListener), so it needs no admin rights; Windows asks once whether to let it through the firewall.
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using TranslatorPc.Core;

namespace TranslatorPc;

sealed class PhoneServer : IDisposable
{
    readonly TcpListener listener;
    readonly OthersFeed feed;

    public PhoneServer(OthersFeed feed, int port = OthersFeed.Port)
    {
        this.feed = feed;
        listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        _ = Task.Run(Accept);
    }

    /// <summary>The address to type on the phone (this PC on the home network), or null when there is none.</summary>
    public static string? Url(int port = OthersFeed.Port)
    {
        var address = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                        && n.GetIPProperties().GatewayAddresses.Count > 0)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
        return address == null ? null : $"http://{address}:{port}/";
    }

    async Task Accept()
    {
        while (true)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(); }
            catch (Exception e) when (e is ObjectDisposedException or SocketException) { return; }
            _ = Task.Run(() => Serve(client));
        }
    }

    void Serve(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = 3000;
                using var stream = client.GetStream();
                var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                var request = reader.ReadLine() ?? "";
                while (!string.IsNullOrEmpty(reader.ReadLine())) { } // headers
                var parts = request.Split(' ');
                var (status, type, body) = parts.Length >= 2 && parts[0] == "GET"
                    ? feed.Handle(parts[1])
                    : (405, "text/plain; charset=utf-8", "GET only");
                var bytes = Encoding.UTF8.GetBytes(body);
                var head = $"HTTP/1.1 {status} {(status == 200 ? "OK" : "Error")}\r\nContent-Type: {type}\r\n" +
                           $"Content-Length: {bytes.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
                stream.Write(Encoding.ASCII.GetBytes(head));
                stream.Write(bytes);
            }
            catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException) { }
        }
    }

    public void Dispose() => listener.Stop();
}

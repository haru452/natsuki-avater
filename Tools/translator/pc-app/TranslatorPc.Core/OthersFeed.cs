// The phone's page: the other players' translated lines, served by the exe's small web server (PhoneServer).
// Requests and answers are plain strings here, so the page is testable without a socket.
using System.Text.Json.Nodes;

namespace TranslatorPc.Core;

public record OthersLine(int Id, DateTime At, Lang Lang, string Original, string Translation);

public class OthersFeed
{
    public const int Port = 8765;
    /// <summary>The page asks every second; after this long without a question the phone is taken to be gone.</summary>
    public const double ViewerTimeoutSeconds = 10;
    public const int Keep = 50;

    readonly Func<DateTime> now;
    readonly object gate = new();
    readonly List<OthersLine> lines = new();
    int nextId = 1;
    DateTime lastAsked = DateTime.MinValue;

    public OthersFeed(Func<DateTime> now) => this.now = now;

    public void Add(Lang lang, string original, string translation)
    {
        lock (gate)
        {
            lines.Add(new OthersLine(nextId++, now(), lang, original, translation));
            if (lines.Count > Keep) lines.RemoveAt(0);
        }
    }

    public bool HasViewer
    {
        get { lock (gate) return (now() - lastAsked).TotalSeconds <= ViewerTimeoutSeconds; }
    }

    public IReadOnlyList<OthersLine> Since(int afterId)
    {
        lock (gate) return lines.Where(l => l.Id > afterId).ToList();
    }

    /// <summary>One request: "/" is the page, "/lines?after=N" the lines after N as JSON (and says the phone is there).</summary>
    public (int Status, string ContentType, string Body) Handle(string target)
    {
        var (path, query) = target.IndexOf('?') is var q and >= 0 ? (target[..q], target[(q + 1)..]) : (target, "");
        if (path == "/") return (200, "text/html; charset=utf-8", OthersPage.Html);
        if (path != "/lines") return (404, "text/plain; charset=utf-8", "not found");
        lock (gate) lastAsked = now();
        int after = 0;
        foreach (var part in query.Split('&'))
            if (part.StartsWith("after=") && int.TryParse(part[6..], out var n)) after = n;
        var array = new JsonArray();
        foreach (var l in Since(after))
            array.Add(new JsonObject
            {
                ["id"] = l.Id,
                ["time"] = l.At.ToString("HH:mm"),
                ["lang"] = l.Lang.ToString(),
                ["original"] = l.Original,
                ["translation"] = l.Translation,
            });
        return (200, "application/json; charset=utf-8", new JsonObject { ["lines"] = array }.ToJsonString());
    }
}

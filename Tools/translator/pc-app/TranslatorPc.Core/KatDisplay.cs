// Port of the phone app's KatDisplay. The pet's text (KAT) carries English only; Japanese and Chinese go to the chatbox.
namespace TranslatorPc.Core;

/// <summary>What the own-voice pipeline drives; KatDisplay sends to VRChat, tests use a fake.</summary>
public interface IDisplay
{
    bool ChatboxOnly { get; set; }
    void Thinking();
    void Show(TranslationResult r);
    void Error();
    void Clear();
    void Stop();
    void Tick();
}

/// <summary>
/// Thinking / Show / Error / Clear / Stop, then Tick every ~50 ms. A shown page is rewritten (without clearing)
/// every resendSeconds (1 s) so late joiners get all of it and a cell that caught a wrong character in passing
/// ("it" showed as "iふ") is put right; long text turns pages pageSeconds after a page is written.
/// An answer on screen is never cut short: "...", errors and clears wait, and a newer answer queues behind it.
/// Its last page stays up holdSeconds after it is written, then the text is cleared (0: until the next answer).
/// Not thread-safe: call it under one lock.
/// </summary>
public class KatDisplay : IDisplay
{
    public const int MaxBacklog = 3;
    /// <summary>Characters follow their pointer this long after it (both within one step).</summary>
    public const double PointerSeconds = 0.15;

    readonly Action<byte[]> send;
    readonly int sync;
    readonly bool showOriginal;
    readonly Func<double> clock;
    readonly double pageSeconds, resendSeconds, stepSeconds, errorSeconds, holdSeconds;
    readonly Action<string> onEvent;
    readonly bool katMode, chatboxMode;

    List<List<byte[]>> queue = new();
    double next;
    List<string> pages = new();
    int page;
    double? writtenAt;
    /// <summary>When the last page was first written (resends leave it be): the hold counts from here.</summary>
    double? lastPageSince;
    double? errorUntil;
    List<List<string>> backlog = new();

    public KatDisplay(Action<byte[]> send, DisplayMode mode, int sync, bool showOriginal, Func<double> clock,
        double pageSeconds = 5, double resendSeconds = 1, double stepSeconds = 0.25, double errorSeconds = 3,
        Action<string>? onEvent = null, double holdSeconds = 0)
    {
        this.holdSeconds = holdSeconds;
        this.send = send;
        this.sync = sync;
        this.showOriginal = showOriginal;
        this.clock = clock;
        this.pageSeconds = pageSeconds;
        this.resendSeconds = resendSeconds;
        this.stepSeconds = stepSeconds;
        this.errorSeconds = errorSeconds;
        this.onEvent = onEvent ?? (_ => { });
        katMode = mode is DisplayMode.KAT or DisplayMode.BOTH;
        chatboxMode = mode is DisplayMode.CHATBOX or DisplayMode.BOTH;
    }

    /// <summary>Set before Thinking(): when the answer will not be English, "..." goes to the chatbox, not KAT.</summary>
    public bool ChatboxOnly { get; set; }

    /// <summary>The "..." is on the pet (Thinking put it there and nothing has replaced it yet).</summary>
    bool dotsOnKat;

    bool UseKat => katMode && !ChatboxOnly;
    bool UseChatbox => chatboxMode || ChatboxOnly;

    public void Thinking()
    {
        if (UseKat && !Busy())
        {
            Reset();
            Write("...", clear: true);
            dotsOnKat = true;
        }
        if (UseChatbox) SafeSend(Osc.Message("/chatbox/typing", true));
    }

    public void Show(TranslationResult r)
    {
        // Kana next to English got mixed up on the pet ("here" showed as "heりe"): English only there.
        bool chatOnly = r.Target != Lang.EN;
        bool dots = dotsOnKat;
        dotsOnKat = false;
        if (katMode && !chatOnly)
        {
            var newPages = Compose.KatPages(Compose.Kat(r, showOriginal));
            if (Busy())
            {
                backlog.Add(newPages);
                onEvent($"表示を待ちます（前の訳を表示中。待ち {backlog.Count} 件）: {r.Translation}");
                while (backlog.Count > MaxBacklog)
                {
                    onEvent("表示を飛ばしました（待ちが多すぎます）: " + string.Concat(backlog[0]));
                    backlog.RemoveAt(0);
                }
            }
            else Start(newPages);
        }
        else if (dots && !Busy())
        {
            // The "..." went up before we knew the answer would not be English: take it down.
            Reset();
            queue = new() { new() { Osc.Message(Core.Kat.Pointer, Core.Kat.ClearPointer) } };
            next = clock();
        }
        if (chatboxMode || chatOnly)
        {
            SafeSend(Osc.Message("/chatbox/input", Compose.Chatbox(r, showOriginal), true, false));
            SafeSend(Osc.Message("/chatbox/typing", false));
        }
    }

    public void Error()
    {
        if (UseKat && !Busy())
        {
            Reset();
            Write("(error)", clear: true);
            errorUntil = clock() + errorSeconds;
        }
        if (UseChatbox) SafeSend(Osc.Message("/chatbox/typing", false));
    }

    /// <summary>Wipe the text (e.g. the "..." when the audio turned out to hold no speech).</summary>
    public void Clear()
    {
        if (UseKat && !Busy())
        {
            Reset();
            queue = new() { new() { Osc.Message(Core.Kat.Pointer, Core.Kat.ClearPointer) } };
            next = clock();
        }
        if (UseChatbox) SafeSend(Osc.Message("/chatbox/typing", false));
    }

    /// <summary>Forget everything; the avatar's toggle clears the text itself.</summary>
    public void Stop()
    {
        Reset();
        backlog = new();
    }

    public void Tick()
    {
        double now = clock();
        if (queue.Count > 0)
        {
            if (now >= next)
            {
                var group = queue[0];
                queue.RemoveAt(0);
                bool isPointer = group.Count > 1 && Osc.Parse(group[0]).Address == Core.Kat.Pointer;
                if (isPointer)
                {
                    // KAT writes whatever characters are current into the pointer's cells until the pointer moves,
                    // so move the pointer first and send its characters a moment later ("yeah" became "yeあh").
                    SafeSend(group[0]);
                    queue.Insert(0, group.Skip(1).ToList());
                    next = now + PointerSeconds;
                    return;
                }
                group.ForEach(SafeSend);
                var first = Osc.Parse(group[0]);
                bool isClear = group.Count == 1 && first.Address == Core.Kat.Pointer && first.Args[0] is int p && p == Core.Kat.ClearPointer;
                bool isChars = first.Address != Core.Kat.Pointer;
                next = now + stepSeconds * (isClear ? 2 : 1) - (isChars ? PointerSeconds : 0);
                if (queue.Count == 0)
                {
                    writtenAt = now;
                    if (pages.Count > 0 && page == pages.Count - 1) lastPageSince ??= now;
                }
            }
            return;
        }
        if (errorUntil is double until)
        {
            if (now >= until)
            {
                errorUntil = null;
                SafeSend(Osc.Message(Core.Kat.Pointer, Core.Kat.ClearPointer));
            }
            return;
        }
        if (writtenAt is not double written || pages.Count == 0) return;
        if (page < pages.Count - 1)
        {
            if (now - written >= pageSeconds) { page++; Write(pages[page], clear: true); }
        }
        else if (backlog.Count > 0)
        {
            if (now - written >= pageSeconds) { var b = backlog[0]; backlog.RemoveAt(0); Start(b); }
        }
        else if (holdSeconds > 0 && lastPageSince is double since && now - since >= holdSeconds)
        {
            // Up long enough: take it down (until the next answer).
            Reset();
            queue = new() { new() { Osc.Message(Core.Kat.Pointer, Core.Kat.ClearPointer) } };
            next = now;
        }
        else if (now - written >= resendSeconds) Write(pages[page], clear: false);
    }

    /// <summary>True while an answer is being written, or has not yet been up for pageSeconds.</summary>
    bool Busy()
    {
        if (backlog.Count > 0) return true;
        if (pages.Count == 0) return false;
        if (queue.Count > 0 || writtenAt is null || page < pages.Count - 1) return true;
        return clock() - writtenAt.Value < pageSeconds;
    }

    void Start(List<string> newPages)
    {
        Reset();
        pages = newPages;
        Write(newPages[0], clear: true);
    }

    void Reset()
    {
        queue = new();
        next = 0;
        pages = new();
        page = 0;
        writtenAt = null;
        lastPageSince = null;
        errorUntil = null;
    }

    void Write(string text, bool clear)
    {
        queue = Core.Kat.BuildMessages(text, sync, clear);
        next = clock();
        writtenAt = null;
    }

    void SafeSend(byte[] packet)
    {
        try { send(packet); }
        catch (Exception) { /* a network hiccup must never stop the translator */ }
    }
}

// Ported from the phone app's KatDisplayTests.
using TranslatorPc.Core;
using static ComposeTests;

public class KatDisplayTests
{
    double now;
    List<(string Address, object[] Args)> packets = new();
    KatDisplay d;

    public KatDisplayTests() => d = New(DisplayMode.KAT, false);

    KatDisplay New(DisplayMode mode, bool showOriginal, Action<byte[]>? send = null, Action<string>? onEvent = null) =>
        new(send ?? (p => packets.Add(Osc.Parse(p))), mode, 8, showOriginal, () => now, onEvent: onEvent);

    List<(string Address, object[] Args)> Take() { var t = packets; packets = new(); return t; }

    void Run(KatDisplay disp, double seconds, double step = 0.05)
    {
        double end = now + seconds;
        while (now < end - 1e-9)
        {
            now = Math.Round((now + step) * 10000) / 10000;
            disp.Tick();
        }
    }

    static bool IsClear((string Address, object[] Args) p) => p.Address.EndsWith("KAT_Pointer") && p.Args[0] is int i && i == 255;

    /// <summary>Rebuild what KAT shows from pointer/char packets (pointer 255 clears).</summary>
    static Dictionary<int, float> KatText(List<(string Address, object[] Args)> ps, int sync = 8)
    {
        var cells = new Dictionary<int, float>();
        int pointer = 0;
        foreach (var (address, args) in ps)
        {
            if (address.EndsWith("KAT_Pointer"))
            {
                pointer = (int)args[0];
                if (pointer == 255) cells.Clear();
            }
            else if (address.Contains("KAT_CharSync") && pointer is >= 1 and <= 254)
                cells[(pointer - 1) * sync + int.Parse(address[(address.LastIndexOf("KAT_CharSync") + 12)..])] = (float)args[0];
        }
        return cells;
    }

    [Fact]
    public void ThinkingWritesDots()
    {
        d.Thinking();
        Run(d, 1.0);
        var shown = KatText(Take());
        for (int i = 0; i < 3; i++) Assert.Equal(Kat.CodeToFloat(14), shown[i], 6);
    }

    [Fact]
    public void ShowWritesAllGroupsThenResendsWithoutClearing()
    {
        d.Show(JaResult);
        Run(d, 1.5);
        var first = Take();
        Assert.True(IsClear(first[0]));
        Assert.Equal(16, KatText(first).Count);
        Run(d, 3.0);
        var again = Take();
        Assert.NotEmpty(again);
        Assert.DoesNotContain(again, IsClear);
    }

    [Fact]
    public void CharactersFollowTheirPointerAfterAPause()
    {
        d.Show(R("ja", "x", new string('a', 40), "x"));
        double lastPointerAt = double.NaN;
        for (int i = 0; i < 80; i++)
        {
            now = Math.Round((now + 0.05) * 10000) / 10000;
            d.Tick();
            var sent = Take();
            bool pointer = sent.Any(p => p.Address.EndsWith("KAT_Pointer"));
            bool chars = sent.Any(p => p.Address.Contains("KAT_CharSync"));
            Assert.False(pointer && chars, $"pointer and characters together at {now}");
            if (pointer) lastPointerAt = now;
            if (chars) Assert.True(now - lastPointerAt >= 0.1 - 1e-6);
        }
    }

    [Fact]
    public void LongTextTurnsPagesAfterItIsWritten()
    {
        d.Show(R("ja", "x", string.Join(" ", Enumerable.Repeat("word", 40)), "x"));
        Run(d, 4.5);
        Run(d, 4.7);
        Assert.DoesNotContain(Take().Skip(1), IsClear);
        Run(d, 1.5);
        Assert.Contains(Take(), IsClear);
    }

    [Fact]
    public void ErrorShowsThenClears()
    {
        d.Error();
        Run(d, 1.0);
        Assert.NotEmpty(KatText(Take()));
        Run(d, 3.0);
        Assert.True(IsClear(Take().Last()));
        Run(d, 5.0);
        Assert.Empty(Take());
    }

    [Fact]
    public void BackToBackResultsEachGetShown()
    {
        d.Show(R("ja", "x", "first answer", "x"));
        Run(d, 0.3);
        d.Thinking();
        d.Show(R("ja", "y", "second answer", "y"));
        Run(d, 0.8);
        Assert.Equal(16, KatText(packets).Count);
        Run(d, 4.0);
        Assert.DoesNotContain(Take().Skip(1), IsClear);
        Run(d, 3.0);
        Assert.Contains(Take(), IsClear);
    }

    [Fact]
    public void ClearWipesTheDots()
    {
        d.Thinking();
        Run(d, 1.0);
        Take();
        d.Clear();
        Run(d, 1.0);
        Assert.True(IsClear(Assert.Single(Take())));
        Run(d, 5.0);
        Assert.Empty(Take());
    }

    [Fact]
    public void StopSendsNothingMore()
    {
        d.Show(JaResult);
        Run(d, 0.3);
        d.Stop();
        Take();
        Run(d, 10.0);
        Assert.Empty(Take());
    }

    [Fact]
    public void SendFailuresDoNotEscape()
    {
        var broken = New(DisplayMode.BOTH, true, _ => throw new IOException("network is unreachable"));
        broken.Thinking();
        broken.Show(JaResult);
        Run(broken, 10.0);
    }

    [Fact]
    public void QueuedAndDroppedAnswersAreReported()
    {
        var events = new List<string>();
        var k = New(DisplayMode.KAT, false, onEvent: events.Add);
        k.Show(R("ja", "a", "first", "a"));
        for (int i = 0; i < 4; i++) k.Show(R("ja", "b", "next " + i, "b"));
        Assert.Equal(4, events.Count(e => e.StartsWith("表示を待ちます")));
        Assert.Equal(1, events.Count(e => e.StartsWith("表示を飛ばしました") && e.Contains("next 0")));
    }

    [Fact]
    public void ZhTargetGoesToChatboxEvenInKatMode()
    {
        var k = New(DisplayMode.KAT, true);
        k.ChatboxOnly = true;
        k.Thinking();
        k.Show(new TranslationResult(Lang.JA, Lang.ZH, "こんにちは", "你好"));
        Run(k, 5.0);
        var got = Take();
        Assert.Contains(got, p => p.Address == "/chatbox/input" && (string)p.Args[0] == "你好\n(こんにちは)");
        Assert.DoesNotContain(got, p => p.Address.Contains("KAT_"));
    }

    [Fact]
    public void JapaneseTranslationGoesToChatboxEvenInKatMode()
    {
        var k = New(DisplayMode.KAT, true);
        k.ChatboxOnly = true;
        k.Thinking();
        k.Show(new TranslationResult(Lang.EN, Lang.JA, "Good morning", "おはよう", KanaTranslation: "おはよう"));
        Run(k, 5.0);
        var got = Take();
        Assert.Contains(got, p => p.Address == "/chatbox/input" && (string)p.Args[0] == "おはよう\n(Good morning)");
        Assert.DoesNotContain(got, p => p.Address.Contains("KAT_"));
    }

    [Fact]
    public void ThinkingDotsAreWipedWhenTheAnswerGoesToTheChatbox()
    {
        var k = New(DisplayMode.KAT, true);
        k.Thinking(); // before recognition: "..." on the pet
        Run(k, 2.0);
        Take();
        k.ChatboxOnly = true; // it was English: the answer is Japanese
        k.Show(new TranslationResult(Lang.EN, Lang.JA, "Good morning", "おはよう"));
        Run(k, 2.0);
        var got = Take();
        Assert.Contains(got, p => p.Address == Kat.Pointer && (int)p.Args[0] == Kat.ClearPointer);
        Assert.DoesNotContain(got, p => p.Address.Contains("KAT_CharSync"));
    }
}

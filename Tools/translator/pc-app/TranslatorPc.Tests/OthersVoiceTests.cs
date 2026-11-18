using System.Text.Json;
using TranslatorPc.Core;

public class OthersVoiceTests
{
    class FakeRecognizer : IRecognizer
    {
        public RecognizedSpeech Answer = new(Lang.EN, "Nice to meet you", true);
        public RecognizedSpeech Recognize(short[] pcm) => Answer;
    }

    class FakeTranslator : ITranslator
    {
        public List<(string, Lang, Lang)> Calls = new();
        public Exception? Fail;
        public string Translate(string text, Lang source, Lang target)
        {
            Calls.Add((text, source, target));
            if (Fail != null) throw Fail;
            return "はじめまして";
        }
    }

    readonly FakeRecognizer stt = new();
    readonly FakeTranslator translator = new();
    readonly List<string> said = new();
    readonly OthersFeed feed = new(() => new DateTime(2026, 11, 18, 21, 5, 0));
    double now;

    bool translate = true;

    OthersVoice Make() => new(stt, translator, feed, () => now, said.Add, () => translate) { Listening = true };

    [Theory]
    [InlineData(Lang.EN, "Nice to meet you")]
    [InlineData(Lang.JA, "こんにちは")]
    public void NotTranslating_ShowsWhatWasSaid_InEveryLanguage_WithoutDeepL(Lang lang, string text)
    {
        translate = false;
        stt.Answer = new(lang, text, true);
        var v = Make();
        Speak(v);
        v.ProcessNext(0);
        Assert.Empty(translator.Calls);
        var line = feed.Since(0).Single();
        Assert.Equal((lang, text, ""), (line.Lang, line.Original, line.Translation));
    }

    [Fact]
    public void ForNow_OthersAreNotTranslated() => Assert.False(new Settings().TranslateOthers);

    static readonly List<short[]> Sentence =
        SegmenterTests.Frames(1, 0).Concat(SegmenterTests.Frames(1, 6000)).Concat(SegmenterTests.Frames(1.5, 0)).ToList();

    static void Speak(OthersVoice v) => Sentence.ForEach(v.FeedFrame);

    [Theory]
    [InlineData(Lang.EN, "Nice to meet you")]
    [InlineData(Lang.ZH, "很高兴认识你")]
    [InlineData(Lang.OTHER, "반갑습니다")]
    public void NotJapanese_IsTranslatedIntoJapanese_AndShown(Lang lang, string text)
    {
        stt.Answer = new(lang, text, true);
        var v = Make();
        Speak(v);
        Assert.True(v.ProcessNext(0));
        Assert.Equal((text, lang, Lang.JA), translator.Calls.Single());
        var line = feed.Since(0).Single();
        Assert.Equal((lang, text, "はじめまして"), (line.Lang, line.Original, line.Translation));
    }

    [Fact]
    public void Japanese_IsNotTranslated_NorSentToDeepL()
    {
        stt.Answer = new(Lang.JA, "こんにちは", true);
        var v = Make();
        Speak(v);
        v.ProcessNext(0);
        Assert.Empty(translator.Calls);
        Assert.Empty(feed.Since(0));
    }

    [Fact]
    public void NoiseAndMusic_AreSkipped()
    {
        stt.Answer = new(Lang.NONE, "♪", false);
        var v = Make();
        Speak(v);
        v.ProcessNext(0);
        Assert.Empty(translator.Calls);
    }

    [Fact]
    public void NotListening_HearsNothing()
    {
        var v = Make();
        v.Listening = false;
        Speak(v);
        Assert.False(v.ProcessNext(0));
    }

    [Fact]
    public void DeepLTrouble_IsSaid_NotShown()
    {
        translator.Fail = new DeepLException("DeepL のキーが違います");
        var v = Make();
        Speak(v);
        v.ProcessNext(0);
        Assert.Empty(feed.Since(0));
        Assert.Contains(said, s => s.Contains("DeepL のキーが違います"));
    }

    [Fact]
    public void OldRecordings_AreSkipped()
    {
        var v = Make();
        Speak(v);
        now = OthersVoice.StaleSeconds + 1;
        Assert.True(v.ProcessNext(0));
        Assert.Empty(translator.Calls);
    }
}

public class OthersFeedTests
{
    DateTime now = new(2026, 11, 18, 21, 5, 0);
    OthersFeed Make() => new(() => now);

    [Fact]
    public void ThePage_AndTheLinesAfterAnId()
    {
        var feed = Make();
        var (status, type, body) = feed.Handle("/");
        Assert.Equal(200, status);
        Assert.StartsWith("text/html", type);
        Assert.Contains("/lines?after=", body);
        feed.Add(Lang.EN, "Hello", "こんにちは");
        feed.Add(Lang.EN, "Bye", "またね");
        var (s2, t2, json) = feed.Handle("/lines?after=1");
        Assert.Equal(200, s2);
        Assert.StartsWith("application/json", t2);
        var lines = JsonDocument.Parse(json).RootElement.GetProperty("lines");
        Assert.Equal(1, lines.GetArrayLength());
        Assert.Equal("またね", lines[0].GetProperty("translation").GetString());
        Assert.Equal("Bye", lines[0].GetProperty("original").GetString());
        Assert.Equal("21:05", lines[0].GetProperty("time").GetString());
        Assert.Equal(404, feed.Handle("/secret").Status);
    }

    [Fact]
    public void ThePhoneCountsAsThere_OnlyWhileThePageKeepsAsking()
    {
        var feed = Make();
        Assert.False(feed.HasViewer, "no page open yet");
        feed.Handle("/");
        Assert.False(feed.HasViewer, "loading the page is not enough: it must ask for lines");
        feed.Handle("/lines?after=0");
        Assert.True(feed.HasViewer);
        now = now.AddSeconds(OthersFeed.ViewerTimeoutSeconds + 1);
        Assert.False(feed.HasViewer, "closed or asleep: stop listening");
    }

    [Fact]
    public void KeepsOnlyTheLatestLines()
    {
        var feed = Make();
        for (int i = 0; i < OthersFeed.Keep + 5; i++) feed.Add(Lang.EN, "x" + i, "y" + i);
        var all = feed.Since(0);
        Assert.Equal(OthersFeed.Keep, all.Count);
        Assert.Equal("x" + (OthersFeed.Keep + 4), all[^1].Original);
    }

    [Fact]
    public void DeepL_DetectsALanguageTheRecognizerCannotName()
    {
        using var doc = JsonDocument.Parse(DeepL.Body("반갑습니다", Lang.OTHER, Lang.JA));
        Assert.False(doc.RootElement.TryGetProperty("source_lang", out _));
        Assert.Equal("JA", doc.RootElement.GetProperty("target_lang").GetString());
    }
}

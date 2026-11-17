// Ported from the phone app's SegmenterTests and OwnLanguageTests.
using TranslatorPc.Core;

public class SegmenterTests
{
    const int Quiet = 60, Speech = 6000;

    /// <summary>30 ms frames of a sine of the given amplitude (0 = silence).</summary>
    internal static List<short[]> Frames(double seconds, int amplitude, double freq = 220)
    {
        int n = (int)Math.Round(seconds * 1000 / Segmenter.FrameMs);
        var frames = new List<short[]>();
        for (int f = 0; f < n; f++)
        {
            var s = new short[Segmenter.FrameSamples];
            for (int i = 0; i < s.Length; i++)
                s[i] = (short)(amplitude * Math.Sin(2 * Math.PI * freq * (f * s.Length + i) / Segmenter.Rate));
            frames.Add(s);
        }
        return frames;
    }

    static List<short[]> FeedAll(Segmenter seg, IEnumerable<short[]> chunks) =>
        chunks.Select(seg.Feed).Where(x => x != null).Select(x => x!).ToList();

    static double SecondsOf(short[] pcm) => (double)pcm.Length / Segmenter.Rate;

    [Fact]
    public void OneUtteranceWithPreroll()
    {
        var seg = new Segmenter(new SegmenterSettings());
        var got = FeedAll(seg, Frames(1, Quiet).Concat(Frames(1, Speech)).Concat(Frames(1.5, Quiet)));
        var one = Assert.Single(got);
        Assert.InRange(SecondsOf(one), 1.25, 1.6);
        Assert.False(seg.Recording);
    }

    [Fact]
    public void WaitsFor12SecondsOfSilence()
    {
        var seg = new Segmenter(new SegmenterSettings());
        Assert.Empty(FeedAll(seg, Frames(1, Quiet).Concat(Frames(1, Speech)).Concat(Frames(1, Quiet))));
        Assert.True(seg.Recording, "a pause of a second is part of the same sentence");
        Assert.Single(FeedAll(seg, Frames(0.3, Quiet)));
    }

    [Fact]
    public void ShortNoiseIsDropped() =>
        Assert.Empty(FeedAll(new Segmenter(new SegmenterSettings()), Frames(1, Quiet).Concat(Frames(0.3, Speech)).Concat(Frames(1.5, Quiet))));

    [Fact]
    public void LongSpeechIsCutAtMax()
    {
        var got = FeedAll(new Segmenter(new SegmenterSettings { MaxS = 5 }), Frames(0.5, Quiet).Concat(Frames(6, Speech)));
        Assert.InRange(SecondsOf(Assert.Single(got)), 4.9, 5.1);
    }

    [Fact]
    public void ResetDiscardsARecording()
    {
        var seg = new Segmenter(new SegmenterSettings());
        FeedAll(seg, Frames(1, Quiet).Concat(Frames(1, Speech)));
        Assert.True(seg.Recording);
        seg.Reset();
        Assert.Empty(FeedAll(seg, Frames(1.5, Quiet)));
    }

    // The PC's microphone array suppresses noise to exact zeros, and quiet speech sits around 200-600.
    [Fact]
    public void PcMicHearsQuietSpeech() =>
        Assert.Single(FeedAll(new Segmenter(SegmenterSettings.ForPcMic()), Frames(1, 0).Concat(Frames(1, 400)).Concat(Frames(1.5, 0))));
}

public class SpeechChoiceTests
{
    class Fake : IRecognizer
    {
        public RecognizedSpeech Answer = new(Lang.NONE, "", false);
        public int Calls;
        public RecognizedSpeech Recognize(short[] pcm) { Calls++; return Answer; }
    }

    readonly Fake japanese = new() { Answer = new(Lang.JA, "音声入力の質が良くない。", true) };

    RecognizedSpeech Heard(Lang lang, string text) =>
        new JaEnRecognizer(new Fake { Answer = new(lang, text, true) }, japanese).Recognize(new short[16000]);

    [Fact]
    public void JapaneseIsKept()
    {
        Assert.Equal("できてないというか。", Heard(Lang.JA, "できてないというか。").Text);
        Assert.Equal(0, japanese.Calls);
    }

    [Fact]
    public void EnglishSentenceIsKept() => Assert.Equal(Lang.EN, Heard(Lang.EN, "Where are you from?").Lang);

    // Seen on the phone: short Japanese came back as Chinese, Korean or one English word.
    [Theory]
    [InlineData(Lang.ZH, "好嘞。")]
    [InlineData(Lang.OTHER, "也希요。")]
    [InlineData(Lang.EN, "The.")]
    [InlineData(Lang.EN, "嗯なか。")]
    public void OtherLanguagesAreHeardAgainAsJapanese(Lang lang, string text) =>
        Assert.Equal("音声入力の質が良くない。", Heard(lang, text).Text);

    // With a Japanese-only model (ReazonSpeech) as the second recognizer, Japanese is heard again by it:
    // SenseVoice got "ラバタ可いね" for "アバター可愛いね".
    RecognizedSpeech Refined(Lang lang, string text) =>
        new JaEnRecognizer(new Fake { Answer = new(lang, text, true) }, japanese, refineJapanese: true).Recognize(new short[16000]);

    [Fact]
    public void RefiningHearsJapaneseAgainWithTheJapaneseModel()
    {
        Assert.Equal("音声入力の質が良くない。", Refined(Lang.JA, "音声入力の質が良く無い").Text);
        Assert.Equal(1, japanese.Calls);
    }

    [Fact]
    public void RefiningLeavesEnglishAlone()
    {
        Assert.Equal("Where are you from?", Refined(Lang.EN, "Where are you from?").Text);
        Assert.Equal(0, japanese.Calls);
    }

    [Fact]
    public void RefiningKeepsTheFirstGuessWhenTheJapaneseModelHearsNothing()
    {
        japanese.Answer = new(Lang.NONE, "", false);
        Assert.Equal("こんにちは。", Refined(Lang.JA, "こんにちは。").Text);
    }
}

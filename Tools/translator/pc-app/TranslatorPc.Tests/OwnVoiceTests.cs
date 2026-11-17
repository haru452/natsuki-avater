using System.Text.Json;
using TranslatorPc.Core;

public class DeepLTests
{
    [Fact]
    public void FreeKeysUseTheFreeHost()
    {
        Assert.Equal("https://api-free.deepl.com/v2/translate", DeepL.Endpoint("abc:fx"));
        Assert.Equal("https://api.deepl.com/v2/translate", DeepL.Endpoint("abc"));
    }

    [Theory]
    [InlineData(Lang.JA, Lang.EN, "JA", "EN-US")]
    [InlineData(Lang.JA, Lang.ZH, "JA", "ZH-HANS")]
    [InlineData(Lang.EN, Lang.JA, "EN", "JA")]
    public void BodyNamesBothLanguages(Lang source, Lang target, string from, string to)
    {
        using var doc = JsonDocument.Parse(DeepL.Body("こんにちは \"x\"", source, target));
        Assert.Equal("こんにちは \"x\"", doc.RootElement.GetProperty("text")[0].GetString());
        Assert.Equal(from, doc.RootElement.GetProperty("source_lang").GetString());
        Assert.Equal(to, doc.RootElement.GetProperty("target_lang").GetString());
    }

    [Fact]
    public void ParsesTheTranslation() =>
        Assert.Equal("Hello", DeepL.Parse("{\"translations\":[{\"detected_source_language\":\"JA\",\"text\":\"Hello\"}]}"));

    [Fact]
    public void OddAnswersAreAnErrorNotACrash() => Assert.Throws<DeepLException>(() => DeepL.Parse("{\"message\":\"x\"}"));

    [Theory]
    [InlineData(403, "キー")]
    [InlineData(456, "使い切り")]
    [InlineData(429, "混んで")]
    [InlineData(500, "500")]
    public void StatusCodesSayWhatToDo(int status, string expected) => Assert.Contains(expected, DeepL.Problem(status));
}

public class SettingsTests
{
    [Fact]
    public void SavesAndLoads_DefaultsWhenMissingOrBroken()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var path = Path.Combine(dir, "settings.json");
            Assert.Equal("", Settings.Load(path).DeepLKey);
            Assert.Equal(Lang.EN, Settings.Load(path).TargetLang);
            var s = new Settings { DeepLKey = "k:fx", TargetLang = Lang.ZH };
            s.Save(path);
            var back = Settings.Load(path);
            Assert.Equal("k:fx", back.DeepLKey);
            Assert.Equal(Lang.ZH, back.TargetLang);
            File.WriteAllText(path, "{not json");
            Assert.Equal("", Settings.Load(path).DeepLKey);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void KeyIsMaskedForTheScreen()
    {
        Assert.Equal("なし", Settings.MaskKey(""));
        Assert.Equal("あり（…:fx）", Settings.MaskKey("0123456789abcdef:fx"));
        Assert.DoesNotContain("0123", Settings.MaskKey("0123456789abcdef:fx"));
    }
}

public class OwnVoiceTests
{
    class FakeRecognizer : IRecognizer
    {
        public RecognizedSpeech Answer = new(Lang.JA, "おはよう", true);
        public RecognizedSpeech Recognize(short[] pcm) => Answer;
    }

    class FakeTranslator : ITranslator
    {
        public List<(string, Lang, Lang)> Calls = new();
        public Func<string, string> Reply = t => "Good morning";
        public string Translate(string text, Lang source, Lang target) { Calls.Add((text, source, target)); return Reply(text); }
    }

    class FakeDisplay : IDisplay
    {
        public List<string> Events = new();
        public bool TargetIsChinese { get; set; }
        public void Thinking() => Events.Add("thinking");
        public void Show(TranslationResult r) => Events.Add($"show {r.Translation} ({r.KanaOriginal}{r.KanaTranslation}) -> {r.Target}");
        public void Error() => Events.Add("error");
        public void Clear() => Events.Add("clear");
        public void Stop() => Events.Add("stop");
        public void Tick() { }
    }

    class FakeKana : IKana { public string ToKana(string text) => "かな(" + text + ")"; }

    readonly FakeRecognizer stt = new();
    readonly FakeTranslator translator = new();
    readonly FakeDisplay display = new();
    readonly List<string> said = new();
    readonly List<string> logged = new();
    double now;

    OwnVoice Make(Lang target = Lang.EN) => new(new Settings { TargetLang = target }, stt, translator, display, new FakeKana(),
        (src, orig, trans) => logged.Add($"{src} {orig} {trans}"), () => now, said.Add);

    static readonly List<short[]> Sentence = SegmenterTests.Frames(1, 0).Concat(SegmenterTests.Frames(1, 6000)).Concat(SegmenterTests.Frames(1.5, 0)).ToList();

    static void Speak(OwnVoice p) => Sentence.ForEach(p.FeedFrame);

    [Fact]
    public void JapaneseIsTranslatedShownWithKanaAndLogged()
    {
        var p = Make();
        p.OnOsc(OwnVoice.ActiveParam, new object[] { true });
        Speak(p);
        Assert.True(p.ProcessNext(0));
        Assert.Equal(("おはよう", Lang.JA, Lang.EN), translator.Calls.Single());
        Assert.Equal(new[] { "thinking", "show Good morning (かな(おはよう)) -> EN" }, display.Events);
        Assert.Equal("JA おはよう Good morning", logged.Single());
        Assert.Contains(said, s => s.StartsWith("時間: 認識"));
    }

    [Fact]
    public void EnglishGoesToJapaneseWithKana()
    {
        stt.Answer = new(Lang.EN, "Good morning", true);
        translator.Reply = _ => "おはよう";
        var p = Make();
        p.OnOsc(OwnVoice.ActiveParam, new object[] { true });
        Speak(p);
        p.ProcessNext(0);
        Assert.Equal(Lang.JA, translator.Calls.Single().Item3);
        Assert.Contains("show おはよう (かな(おはよう)) -> JA", display.Events);
    }

    [Fact]
    public void ChineseTargetIsMarkedForTheChatbox()
    {
        var p = Make(Lang.ZH);
        p.OnOsc(OwnVoice.ActiveParam, new object[] { true });
        Speak(p);
        p.ProcessNext(0);
        Assert.True(display.TargetIsChinese);
        Assert.Equal(Lang.ZH, translator.Calls.Single().Item3);
    }

    [Fact]
    public void TranslatorFailureShowsErrorAndKeepsGoing()
    {
        translator.Reply = _ => throw new DeepLException("DeepL のキーが違います");
        var p = Make();
        p.OnOsc(OwnVoice.ActiveParam, new object[] { true });
        Speak(p);
        p.ProcessNext(0);
        Assert.Contains("error", display.Events);
        Assert.Contains(said, s => s.Contains("DeepL のキーが違います"));
        translator.Reply = _ => "ok";
        Speak(p);
        p.ProcessNext(0);
        Assert.Contains("show ok (かな(おはよう)) -> EN", display.Events);
    }

    [Fact]
    public void NonSpeechClearsTheDots()
    {
        stt.Answer = new(Lang.JA, "ん", false);
        var p = Make();
        p.OnOsc(OwnVoice.ActiveParam, new object[] { true });
        Speak(p);
        p.ProcessNext(0);
        Assert.Empty(translator.Calls);
        Assert.Equal(new[] { "thinking", "clear" }, display.Events);
    }

    [Fact]
    public void NothingIsHeardWhileOff_AndTurningOffDropsWhatWasRecorded()
    {
        var p = Make();
        Speak(p);
        Assert.False(p.ProcessNext(0));
        p.OnOsc(OwnVoice.ActiveParam, new object[] { true });
        Speak(p);
        p.OnOsc(OwnVoice.ActiveParam, new object[] { false });
        p.OnOsc(OwnVoice.ActiveParam, new object[] { true });
        p.ProcessNext(0);
        Assert.Empty(translator.Calls);
    }

    [Fact]
    public void AvatarChangeTurnsItOff()
    {
        var p = Make();
        p.OnOsc(OwnVoice.ActiveParam, new object[] { true });
        p.OnOsc(OwnVoice.AvatarChange, new object[] { "avtr_x" });
        Assert.False(p.Active);
    }

    [Fact]
    public void OldRecordingsAreSkipped()
    {
        var p = Make();
        p.OnOsc(OwnVoice.ActiveParam, new object[] { true });
        Speak(p);
        now += 30;
        p.ProcessNext(0);
        Assert.Empty(translator.Calls);
    }
}

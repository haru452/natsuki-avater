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
        public bool ChatboxOnly { get; set; }
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
        Assert.False(display.ChatboxOnly, "English goes on the pet");
        Assert.Contains(said, s => s.StartsWith("時間: 認識"));
    }

    [Fact]
    public void EnglishIsShownAsItIs_NoJapaneseAnywhere()
    {
        stt.Answer = new(Lang.EN, "Good morning", true);
        translator.Reply = _ => "おはよう";
        var p = Make();
        p.OnOsc(OwnVoice.ActiveParam, new object[] { true });
        Speak(p);
        p.ProcessNext(0);
        Assert.Empty(translator.Calls);
        Assert.Contains(display.Events, e => e.StartsWith("show Good morning") && e.EndsWith("-> EN"));
        Assert.DoesNotContain(display.Events, e => e.Contains("おはよう"));
        Assert.False(display.ChatboxOnly, "on the pet");
    }

    [Fact]
    public void HerName_CallsThePet_BeforeTranslating()
    {
        stt.Answer = new(Lang.JA, "誉、元気？", true);
        var sent = new List<string>();
        int translatedWhenCalled = -1;
        var call = new PetCall(p => { sent.Add(Osc.Parse(p).Address); translatedWhenCalled = translator.Calls.Count; }, () => now);
        var p = new OwnVoice(new Settings(), stt, translator, display, new FakeKana(),
            (_, _, _) => { }, () => now, said.Add, call);
        p.OnOsc(OwnVoice.ActiveParam, new object[] { true });
        Speak(p);
        p.ProcessNext(0);
        Assert.Equal(new[] { PetCall.CalledParam }, sent);
        Assert.Equal(0, translatedWhenCalled);
        Assert.Contains(said, s => s.StartsWith("名前を呼ばれました"));

        stt.Answer = new(Lang.JA, "おはよう", true);
        Speak(p);
        p.ProcessNext(0);
        Assert.Single(sent);
    }

    [Fact]
    public void HerName_InAnyRecognizersText_CallsThePet()
    {
        stt.Answer = new(Lang.JA, "すわって", true, new[] { "すわって", "ほまれ 座 って。" }); // (the fake kana leaves kanji alone)
        var sent = new List<string>();
        var call = new PetCall(p => sent.Add(Osc.Parse(p).Address), () => now);
        var p = new OwnVoice(new Settings(), stt, translator, display, new FakeKana(), (_, _, _) => { }, () => now, said.Add, call);
        p.OnOsc(OwnVoice.ActiveParam, new object[] { true });
        Speak(p);
        p.ProcessNext(0);
        Assert.Equal(new[] { PetCall.CalledParam, PetCommands.SitParam }, sent);
    }

    [Fact]
    public void TranslationOff_StillHearsHerName_ButShowsAndSendsNothing()
    {
        var sent = new List<string>();
        var call = new PetCall(p => sent.Add(Osc.Parse(p).Address), () => now);
        var p = new OwnVoice(new Settings(), stt, translator, display, new FakeKana(),
            (src, orig, trans) => logged.Add(orig), () => now, said.Add, call);
        p.OnOsc(OwnVoice.ActiveParam, new object[] { true });
        Assert.True(p.Translating, "on until the avatar says otherwise");
        p.OnOsc(OwnVoice.TranslateParam, new object[] { false });
        Assert.False(p.Translating);
        Assert.Equal(new[] { "clear" }, display.Events);

        stt.Answer = new(Lang.JA, "誉、元気？", true);
        Speak(p);
        p.ProcessNext(0);
        Assert.Equal(new[] { PetCall.CalledParam }, sent);
        stt.Answer = new(Lang.JA, "おはよう", true);
        Speak(p);
        p.ProcessNext(0);
        Assert.Empty(translator.Calls);
        Assert.Empty(logged);
        Assert.Equal(new[] { "clear" }, display.Events);

        p.OnOsc(OwnVoice.TranslateParam, new object[] { true });
        Speak(p);
        p.ProcessNext(0);
        Assert.Single(translator.Calls);
    }

    [Fact]
    public void ChatToggle_TurnsTalkingWithHerOnAndOff()
    {
        var settings = new Settings();
        var p = new OwnVoice(settings, stt, translator, display, new FakeKana(), (_, _, _) => { }, () => now, said.Add);
        Assert.True(settings.PetChat, "on until switched off");
        p.OnOsc(OwnVoice.ChatParam, new object[] { false });
        Assert.False(settings.PetChat);
        Assert.Equal(new[] { "会話: オフ" }, said);
        Assert.False(p.SetChatting(false), "already off");
        Assert.True(p.SetChatting(true));
        Assert.True(settings.PetChat);
        Assert.Equal(new[] { "会話: オフ", "会話: オン" }, said);
    }

    class FakeChat : IPetChat
    {
        public readonly List<string> Heard = new();
        public string Reply(string said) { Heard.Add(said); return "Hi!"; }
    }

    [Fact]
    public void Talking_EverythingSaidIsForHer_SheLooksAtTheWearer_NothingIsTranslated()
    {
        var sent = new List<(string, object)>();
        var call = new PetCall(b => { var (a, args) = Osc.Parse(b); sent.Add((a, args[0])); }, () => now);
        var chat = new FakeChat();
        var settings = new Settings { GeminiKey = "key" };
        var p = new OwnVoice(settings, stt, translator, display, new FakeKana(), (_, _, _) => { }, () => now, said.Add, call, chat);
        p.OnOsc(OwnVoice.ActiveParam, new object[] { true });

        stt.Answer = new(Lang.JA, "割れちゃう", true); // her name misheard: still for her
        Speak(p);
        p.ProcessNext(0);
        stt.Answer = new(Lang.EN, "It is difficult to pronounce.", true);
        Speak(p);
        p.ProcessNext(0);
        Assert.Equal(new[] { "割れちゃう", "It is difficult to pronounce." }, chat.Heard);
        Assert.Empty(translator.Calls);
        Assert.Contains((PetCommands.FaceParam, (object)true), sent);

        sent.Clear();
        stt.Answer = new(Lang.JA, "誉、すわって", true); // a command is done, not answered or translated
        Speak(p);
        p.ProcessNext(0);
        Assert.Equal(2, chat.Heard.Count);
        Assert.Empty(translator.Calls);
        Assert.Contains((PetCommands.SitParam, (object)true), sent);

        sent.Clear();
        p.OnOsc(OwnVoice.ChatParam, new object[] { false }); // talk over: she stops looking, and speech is translated again
        Assert.Contains((PetCommands.FaceParam, (object)false), sent);
        stt.Answer = new(Lang.JA, "おはよう", true);
        Speak(p);
        p.ProcessNext(0);
        Assert.Single(translator.Calls);
        Assert.Equal(2, chat.Heard.Count);
    }

    [Fact]
    public void ChineseTargetIsMarkedForTheChatbox()
    {
        var p = Make(Lang.ZH);
        p.OnOsc(OwnVoice.ActiveParam, new object[] { true });
        Speak(p);
        p.ProcessNext(0);
        Assert.True(display.ChatboxOnly);
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

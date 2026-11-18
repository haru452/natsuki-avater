using System.Text.Json;
using TranslatorPc.Core;

public class GeminiTests
{
    const string Models = """
    {"models":[
      {"name":"models/gemini-2.5-flash","supportedGenerationMethods":["generateContent","countTokens"]},
      {"name":"models/gemini-3.6-flash","supportedGenerationMethods":["generateContent"]},
      {"name":"models/gemini-3.8-flash-lite","supportedGenerationMethods":["generateContent"]},
      {"name":"models/gemini-3.8-flash-lite-tts","supportedGenerationMethods":["generateContent"]},
      {"name":"models/gemini-3.8-flash-preview-tts","supportedGenerationMethods":["generateContent"]},
      {"name":"models/gemini-3.8-flash-image","supportedGenerationMethods":["generateContent"]},
      {"name":"models/gemini-3.8-pro","supportedGenerationMethods":["generateContent"]},
      {"name":"models/gemini-4.0-flash-embedding","supportedGenerationMethods":["embedContent"]},
      {"name":"models/gemini-3.10-flash","supportedGenerationMethods":["generateContent"]}
    ]}
    """;

    // Short replies need no big model: the plain Flash models were all busy (503) when tried, Flash-Lite answered in 1 s.
    [Fact]
    public void Candidates_LiteFirst_NewestFirst_ThenFlash() =>
        Assert.Equal(new[] { "models/gemini-3.8-flash-lite", "models/gemini-3.10-flash", "models/gemini-3.6-flash", "models/gemini-2.5-flash" },
            Gemini.Candidates(Models));

    [Fact]
    public void NoFlashModel_IsAnError() =>
        Assert.Throws<PetChatException>(() => Gemini.Candidates("{\"models\":[{\"name\":\"models/x\",\"supportedGenerationMethods\":[]}]}"));

    [Fact]
    public void Body_HasHerCharacter_TheTalkSoFar_AndWhatWasJustSaid()
    {
        var history = new List<(bool Her, string Text)> { (false, "ほまれ、おはよう"), (true, "Morning! You're late!") };
        using var doc = JsonDocument.Parse(Gemini.Body(history, "ちゃん、元気？"));
        var system = doc.RootElement.GetProperty("system_instruction").GetProperty("parts")[0].GetProperty("text").GetString()!;
        Assert.Contains("Homare", system);
        Assert.Contains("English", system);
        Assert.Contains("cheeky", system);
        var contents = doc.RootElement.GetProperty("contents");
        Assert.Equal(3, contents.GetArrayLength());
        Assert.Equal(new[] { "user", "model", "user" }, contents.EnumerateArray().Select(c => c.GetProperty("role").GetString()));
        Assert.Equal("ちゃん、元気？", contents[2].GetProperty("parts")[0].GetProperty("text").GetString());
    }

    [Fact]
    public void Parse_TakesTheAnswer_NotItsThinking() =>
        Assert.Equal("Hey! I'm great!", Gemini.Parse("""
            {"candidates":[{"content":{"role":"model","parts":[{"text":"plan the reply","thought":true},{"text":"Hey! I'm great!"}]}}]}
            """));

    [Fact]
    public void Parse_NoAnswer_IsAnError() => Assert.Throws<PetChatException>(() => Gemini.Parse("{\"candidates\":[]}"));

    [Theory]
    [InlineData("Hi there!  😊 Let's go!", "Hi there! Let's go!")]   // the pet's text shows ASCII only
    [InlineData("“Nope” — you’re slow…", "\"Nope\" - you're slow...")]
    [InlineData("ほまれだよ Hello", "Hello")]
    public void ForThePet_PlainAscii(string reply, string shown) => Assert.Equal(shown, Gemini.ForPet(reply));

    [Fact]
    public void ForThePet_ShortEnoughForHerBubble()
    {
        var shown = Gemini.ForPet(string.Join(" ", Enumerable.Repeat("blah", 60)));
        Assert.True(shown.Length <= Gemini.MaxReply, shown.Length.ToString());
        Assert.EndsWith("...", shown);
    }

    [Theory]
    [InlineData(400, "キー")]
    [InlineData(403, "キー")]
    [InlineData(429, "混んで")]
    [InlineData(500, "500")]
    public void StatusCodesSayWhatToDo(int status, string expected) => Assert.Contains(expected, Gemini.Problem(status));
}

public class PetChatTests
{
    readonly List<(string Url, string? Body)> requests = new();
    string key = "k";
    int status = 200;
    /// <summary>Per model: what it answers (a status, or "timeout").</summary>
    readonly Dictionary<string, string> broken = new();
    string models = "{\"models\":[{\"name\":\"models/gemini-3.6-flash\",\"supportedGenerationMethods\":[\"generateContent\"]}]}";

    PetChat Make() => new(() => key, (url, body) =>
    {
        requests.Add((url, body));
        if (body == null) return (200, models);
        foreach (var (model, how) in broken)
            if (url.Contains("/" + model + ":"))
            {
                if (how == "timeout") throw new TaskCanceledException();
                return (int.Parse(how), "{}");
            }
        return (status, "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"Reply " + requests.Count + "\"}]}}]}");
    });

    const string ThreeModels = "{\"models\":["
        + "{\"name\":\"models/gemini-3.1-flash-lite\",\"supportedGenerationMethods\":[\"generateContent\"]},"
        + "{\"name\":\"models/gemini-3.5-flash-lite\",\"supportedGenerationMethods\":[\"generateContent\"]},"
        + "{\"name\":\"models/gemini-3.8-flash\",\"supportedGenerationMethods\":[\"generateContent\"]}]}";

    [Fact]
    public void ABusyOrMissingModel_GivesWayToTheNext_WhichIsKept()
    {
        models = ThreeModels;
        broken["gemini-3.5-flash-lite"] = "503";
        broken["gemini-3.1-flash-lite"] = "timeout";
        var chat = Make();
        Assert.StartsWith("Reply", chat.Reply("ちゃん、元気？"));
        Assert.Equal("gemini-3.8-flash", chat.Model);
        int before = requests.Count;
        chat.Reply("ちゃん、おはよう");
        Assert.Equal(before + 1, requests.Count);
        Assert.Contains("gemini-3.8-flash:", requests[^1].Url);
    }

    [Fact]
    public void AllBusy_SaysSo()
    {
        models = ThreeModels;
        foreach (var m in new[] { "gemini-3.5-flash-lite", "gemini-3.1-flash-lite", "gemini-3.8-flash" }) broken[m] = "503";
        var e = Assert.Throws<PetChatException>(() => Make().Reply("ちゃん"));
        Assert.Contains("混んで", e.Message);
    }

    [Fact]
    public void AWrongKey_IsNotTriedOnEveryModel()
    {
        models = ThreeModels;
        status = 403;
        Assert.Throws<PetChatException>(() => Make().Reply("ちゃん"));
        Assert.Equal(2, requests.Count); // the list, and one model
    }

    [Fact]
    public void FindsTheModelOnce_ThenReplies_RememberingTheTalk()
    {
        var chat = Make();
        Assert.Equal("Reply 2", chat.Reply("ちゃん、おはよう"));
        Assert.Equal("Reply 3", chat.Reply("ちゃん、元気？"));
        Assert.Single(requests, r => r.Body == null);
        Assert.EndsWith("models/gemini-3.6-flash:generateContent", requests[2].Url);
        using var doc = JsonDocument.Parse(requests[2].Body!);
        Assert.Equal(3, doc.RootElement.GetProperty("contents").GetArrayLength()); // the first exchange and the new line
        Assert.Equal("gemini-3.6-flash", chat.Model);
    }

    [Fact]
    public void RemembersOnlyTheLastFewExchanges()
    {
        var chat = Make();
        for (int i = 0; i < PetChat.KeepExchanges + 5; i++) chat.Reply("ちゃん " + i);
        using var doc = JsonDocument.Parse(requests[^1].Body!);
        Assert.Equal(PetChat.KeepExchanges * 2 + 1, doc.RootElement.GetProperty("contents").GetArrayLength());
    }

    [Fact]
    public void NoKey_SaysHowToAddOne_AndSendsNothing()
    {
        key = "";
        var e = Assert.Throws<PetChatException>(() => Make().Reply("ちゃん"));
        Assert.Contains("G キー", e.Message);
        Assert.Empty(requests);
    }

    [Fact]
    public void ARefusedRequest_IsAnError_AndNotRemembered()
    {
        var chat = Make();
        status = 429;
        Assert.Throws<PetChatException>(() => chat.Reply("ちゃん、おはよう"));
        status = 200;
        chat.Reply("ちゃん、元気？");
        using var doc = JsonDocument.Parse(requests[^1].Body!);
        Assert.Equal(1, doc.RootElement.GetProperty("contents").GetArrayLength());
    }
}

public class OwnVoiceChatTests
{
    class Stt : IRecognizer
    {
        public RecognizedSpeech Answer = new(Lang.JA, "ちゃん、元気？", true);
        public RecognizedSpeech Recognize(short[] pcm) => Answer;
    }

    class Translator : ITranslator
    {
        public int Calls;
        public string Translate(string text, Lang source, Lang target) { Calls++; return "Are you well?"; }
    }

    class Chat : IPetChat
    {
        public List<string> Heard = new();
        public Exception? Fail;
        public string Reply(string said) { Heard.Add(said); if (Fail != null) throw Fail; return "Super! And you, slowpoke?"; }
    }

    class Display : IDisplay
    {
        public List<string> Shown = new();
        public bool ChatboxOnly { get; set; }
        public void Thinking() { }
        public void Show(TranslationResult r) => Shown.Add($"{r.Translation} -> {r.Target}");
        public void Error() => Shown.Add("error");
        public void Clear() { }
        public void Stop() { }
        public void Tick() { }
    }

    class Kana : IKana { public string ToKana(string text) => text; }

    readonly Stt stt = new();
    readonly Translator translator = new();
    readonly Chat chat = new();
    readonly Display display = new();
    readonly List<string> said = new();
    double now;

    OwnVoice Make(bool chatOn = true, string key = "k")
    {
        var call = new PetCall(_ => { }, () => now);
        var p = new OwnVoice(new Settings { PetChat = chatOn, GeminiKey = key }, stt, translator, display, new Kana(),
            (_, _, _) => { }, () => now, said.Add, call, chat);
        p.OnOsc(OwnVoice.ActiveParam, new object[] { true });
        return p;
    }

    static readonly List<short[]> Sentence =
        SegmenterTests.Frames(1, 0).Concat(SegmenterTests.Frames(1, 6000)).Concat(SegmenterTests.Frames(1.5, 0)).ToList();

    void Say(OwnVoice p) { Sentence.ForEach(p.FeedFrame); p.ProcessNext(0); }

    [Fact]
    public void CalledByName_SheAnswers_InsteadOfTranslating()
    {
        var p = Make();
        Say(p);
        Assert.Equal(new[] { "ちゃん、元気？" }, chat.Heard);
        Assert.Equal(new[] { "Super! And you, slowpoke? -> EN" }, display.Shown);
        Assert.Equal(0, translator.Calls);
        Assert.Contains(said, s => s.Contains("Super! And you, slowpoke?"));
    }

    [Fact]
    public void SheAnswers_EvenWithTranslationOff_ForPlayingAlone()
    {
        var p = Make();
        p.OnOsc(OwnVoice.TranslateParam, new object[] { false });
        Say(p);
        Assert.Single(chat.Heard);
        Assert.Contains("Super! And you, slowpoke? -> EN", display.Shown);
    }

    [Fact]
    public void ACommand_IsObeyed_NotAnswered()
    {
        stt.Answer = new(Lang.JA, "ちゃん、すわって", true); // (the fake kana leaves kanji alone)
        Say(Make());
        Assert.Empty(chat.Heard);
    }

    [Fact]
    public void WithoutHerName_SheStillAnswers_WhileTalking()
    {
        stt.Answer = new(Lang.JA, "おはよう", true);
        Say(Make());
        Assert.Equal(new[] { "おはよう" }, chat.Heard);
        Assert.Equal(0, translator.Calls);
    }

    [Fact]
    public void NoGeminiKey_SpeechIsTranslatedAsBefore()
    {
        Say(Make(key: ""));
        Assert.Empty(chat.Heard);
        Assert.Equal(1, translator.Calls);
    }

    [Fact]
    public void ChatOff_NothingGoesToGemini() { Say(Make(chatOn: false)); Assert.Empty(chat.Heard); }

    [Fact]
    public void ATroubledReply_IsSaid_NotACrash()
    {
        chat.Fail = new PetChatException("Gemini のキーが違います");
        Say(Make());
        Assert.Contains(said, s => s.Contains("Gemini のキーが違います"));
        Assert.Contains("error", display.Shown);
    }
}

using TranslatorPc.Core;

public class PetCommandsTests
{
    static readonly Settings S = new();

    static PetCommand? Find(string text, string kana = "") => PetCommands.Find(text, kana, S.PetCommandWords, S.PetNames);

    [Theory]
    [InlineData("座って", "すわって")]
    [InlineData("座ってみて", "すわってみて")]
    [InlineData("ちょっと座ってて", "ちょっとすわってて")]
    [InlineData("おすわり", "")]
    [InlineData("オスワリ", "")]
    [InlineData("ここで休んで", "ここでやすんで")]
    public void Sit(string text, string kana) => Assert.Equal(PetCommand.Sit, Find(text, kana));

    [Theory]
    [InlineData("待ってて", "まってて")]
    [InlineData("ここで待ってて", "ここでまってて")]
    [InlineData("ちょっと待っててね", "ちょっとまっててね")]
    [InlineData("待て", "まて")]
    [InlineData("ストップ", "")]
    [InlineData("そこにいて", "")]
    [InlineData("止まって", "とまって")]
    public void Wait(string text, string kana) => Assert.Equal(PetCommand.Wait, Find(text, kana));

    [Theory]
    [InlineData("おいで", "")]
    [InlineData("こっち来て", "こっちきて")]
    [InlineData("ついてきて", "")]
    [InlineData("もういいよ", "")]
    [InlineData("行くよ", "いくよ")]
    public void Come(string text, string kana) => Assert.Equal(PetCommand.Come, Find(text, kana));

    [Theory]
    [InlineData("立って", "たって")]
    [InlineData("起きて", "おきて")] // has きて (come) inside: the longer phrase wins
    public void Stand(string text, string kana) => Assert.Equal(PetCommand.Stand, Find(text, kana));

    [Theory]
    [InlineData("後ろ向いて", "うしろむいて")]
    [InlineData("後ろを向いてみて", "うしろをむいてみて")]
    [InlineData("あっち向いて", "あっちむいて")]
    [InlineData("背中向けて", "せなかむけて")]
    public void TurnAround(string text, string kana) => Assert.Equal(PetCommand.TurnAround, Find(text, kana));

    [Theory]
    [InlineData("前向いて", "まえむいて")]
    [InlineData("前を向いて", "まえをむいて")]
    [InlineData("こっち向いて", "こっちむいて")]
    [InlineData("振り向いて", "ふりむいて")]
    public void FaceFront(string text, string kana) => Assert.Equal(PetCommand.FaceFront, Find(text, kana));

    [Theory]
    [InlineData("おはよう", "おはよう")]
    [InlineData("ほまれ", "ほまれ")]
    [InlineData("Sit down", "")] // Japanese only
    public void NotACommand(string text, string kana) => Assert.Null(Find(text, kana));

    [Fact]
    public void EveryDefaultPhraseIsFoundAsItsOwnCommand()
    {
        foreach (var (key, list) in PetCommands.Defaults())
        foreach (var phrase in list)
            Assert.Equal(Enum.Parse<PetCommand>(key), Find(phrase));
    }

    [Fact]
    public void CommandsSetTheAvatarFlags()
    {
        Assert.Equal(new[] { (PetCommands.SitParam, true) }, PetCommands.Parameters(PetCommand.Sit));
        Assert.Equal(new[] { (PetCommands.WaitParam, true) }, PetCommands.Parameters(PetCommand.Wait));
        Assert.Equal(new[] { (PetCommands.WaitParam, false), (PetCommands.SitParam, false), (PetCommands.TurnParam, false) },
            PetCommands.Parameters(PetCommand.Come));
        Assert.Equal(new[] { (PetCommands.TurnParam, true) }, PetCommands.Parameters(PetCommand.TurnAround));
        Assert.Equal(new[] { (PetCommands.TurnParam, false) }, PetCommands.Parameters(PetCommand.FaceFront));
        Assert.Equal("/avatar/parameters/TranslatorPetTurn", PetCommands.TurnParam);
        Assert.Equal(new[] { (PetCommands.SitParam, false) }, PetCommands.Parameters(PetCommand.Stand));
        Assert.Equal("/avatar/parameters/TranslatorPetSit", PetCommands.SitParam);
        Assert.Equal("/avatar/parameters/TranslatorPetWait", PetCommands.WaitParam);
    }

    // ---- PetCall.Hear: her name, then a command in the same sentence or soon after ----

    readonly List<(string, object)> sent = new();
    double now;

    PetCall Make() => new(p => { var (a, args) = Osc.Parse(p); sent.Add((a, args[0])); }, () => now);

    [Fact]
    public void NameAndCommandInOneSentence()
    {
        var call = Make();
        Assert.StartsWith("ペットへの命令: 座る", call.Hear("誉、座って", "ほまれ、すわって", S));
        Assert.Equal(new[] { (PetCall.CalledParam, (object)true), (PetCommands.SitParam, true) }, sent);
    }

    [Fact]
    public void CommandSoonAfterTheName_ButNotLaterOrTwice()
    {
        var call = Make();
        Assert.StartsWith("名前を呼ばれました", call.Hear("ほまれ", "ほまれ", S));
        now = PetCall.CommandSeconds - 0.5;
        Assert.StartsWith("ペットへの命令: 待つ", call.Hear("待ってて", "まってて", S));
        Assert.Contains((PetCommands.WaitParam, (object)true), sent);
        sent.Clear();
        Assert.Null(call.Hear("座って", "すわって", S)); // one command per call
        Assert.Empty(sent);

        call.Hear("ほまれ", "ほまれ", S);
        sent.Clear();
        now += PetCall.CommandSeconds + 0.1;
        Assert.Null(call.Hear("座って", "すわって", S)); // too late: said to someone else
        Assert.Empty(sent);
    }

    [Fact]
    public void WithoutHerName_CommandsAreIgnored()
    {
        var call = Make();
        Assert.Null(call.Hear("ちょっと待って", "ちょっとまって", S));
        Assert.Empty(sent);
    }

    [Fact]
    public void OlderSettingsGetTheNewCommands()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var path = Path.Combine(dir, "settings.json");
            File.WriteAllText(path, "{\"PetCommandWords\":{\"Sit\":[\"ざぶとん\"]}}");
            var s = Settings.Load(path);
            Assert.Equal(new[] { "ざぶとん" }, s.PetCommandWords["Sit"]);
            Assert.Contains("うしろむいて", s.PetCommandWords["TurnAround"]);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void SettingsKeepTheirPhrases()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var path = Path.Combine(dir, "settings.json");
            var s = new Settings();
            s.PetCommandWords["Sit"].Add("ざぶとん");
            s.Save(path);
            var back = Settings.Load(path);
            Assert.Contains("ざぶとん", back.PetCommandWords["Sit"]);
            Assert.Equal(PetCommand.Sit, PetCommands.Find("ざぶとん", "", back.PetCommandWords, back.PetNames));
        }
        finally { Directory.Delete(dir, true); }
    }
}

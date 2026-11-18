using TranslatorPc.Core;

public class PetCallTests
{
    static readonly List<string> Names = new Settings().PetNames;

    [Theory]
    [InlineData("誉、こっちおいで", "")]
    [InlineData("ほまれちゃん", "")]
    [InlineData("ホマレ", "")]
    [InlineData("穂稀", "ほまれ")]          // a kanji she was not written with, heard by its reading
    [InlineData("Hey Homare!", "")]
    [InlineData("なつき、おはよう", "")]
    [InlineData("Natsuki come here", "")]
    public void HearsHerName(string text, string kana) => Assert.True(PetCall.Heard(text, kana, Names));

    [Theory]
    [InlineData("おはよう", "おはよう")]
    [InlineData("Good morning", "")]
    [InlineData("", "")]
    public void OtherwiseNot(string text, string kana) => Assert.False(PetCall.Heard(text, kana, Names));

    // How the PC actually heard "ほまれちゃん" (2026-11-18): the soft ほ is lost or becomes お/あ/う; ちゃん comes through.
    [Theory]
    [InlineData("まれちゃんこっち向いて", "まれちゃんこっちむいて")]
    [InlineData("まるちゃんこっち向いて", "まるちゃんこっちむいて")]
    [InlineData("おまるちゃん", "おまるちゃん")]
    [InlineData("生まれちゃん", "うまれちゃん")]
    [InlineData("おばれちゃん", "おばれちゃん")]
    public void HearsHerName_AsThePcMishearsIt(string text, string kana) =>
        Assert.True(PetCall.Heard(text, kana, new Settings()));

    // Plain words that sound like her name, and ちゃんと (properly), are not a call.
    [Theory]
    [InlineData("生まれた", "うまれた")]
    [InlineData("まれにある", "まれにある")]
    [InlineData("丸い", "まるい")]
    [InlineData("ばれちゃう", "ばれちゃう")]
    [InlineData("ちゃんとやって", "ちゃんとやって")]
    public void SoundAlikeWords_AreNotACall(string text, string kana) =>
        Assert.False(PetCall.Heard(text, kana, new Settings()));

    [Fact]
    public void ChanAlone_IsACall() => Assert.True(PetCall.Heard("ちゃん、座って", "ちゃん、すわって", new Settings()));

    [Fact]
    public void SoundAlikes_AreNotHotwords() => Assert.DoesNotContain("ちゃん", new Settings().PetNames);

    [Fact]
    public void BlankNamesNeverMatch() => Assert.False(PetCall.Heard("おはよう", "", new[] { "", " " }));

    [Fact]
    public void FlagIsOnForAMomentThenOff()
    {
        var sent = new List<(string, object[])>();
        double now = 0;
        var call = new PetCall(p => sent.Add(Osc.Parse(p)), () => now);
        call.Tick();
        Assert.Empty(sent);
        call.Call();
        Assert.Equal(PetCall.CalledParam, sent.Single().Item1);
        Assert.Equal(new object[] { true }, sent.Single().Item2);
        now = PetCall.PulseSeconds - 0.01;
        call.Tick();
        Assert.Single(sent);
        now = PetCall.PulseSeconds;
        call.Tick();
        Assert.Equal(new object[] { false }, sent[1].Item2);
        call.Tick();
        Assert.Equal(2, sent.Count);
        Assert.Equal("/avatar/parameters/TranslatorPetCalled", PetCall.CalledParam);
    }
}

// Ported from the phone app's ComposeTests.
using TranslatorPc.Core;

public class ComposeTests
{
    /// <summary>kana is the Japanese side: the original for Japanese speech, the translation otherwise.</summary>
    internal static TranslationResult R(string lang, string original, string translation, string kana) =>
        lang == "ja"
            ? new TranslationResult(Lang.JA, Lang.EN, original, translation, KanaOriginal: kana)
            : new TranslationResult(LangCodes.Parse(lang), Lang.JA, original, translation, KanaTranslation: kana);

    internal static readonly TranslationResult JaResult = R("ja", "おはようございます", "Good morning", "おはようございます");
    internal static readonly TranslationResult EnResult = R("en", "Good morning", "おはよう", "おはよう");

    [Fact]
    public void JapaneseSpeechShowsEnglishThenKanaOriginal()
    {
        var text = Compose.Kat(JaResult, true);
        Assert.StartsWith("Good morning", text);
        Assert.Equal(32, text.IndexOf('('));
        Assert.EndsWith("(おはようございます)", text);
    }

    [Fact]
    public void EnglishSpeechShowsKanaThenEnglishOriginal()
    {
        var text = Compose.Kat(EnResult, true);
        Assert.StartsWith("おはよう", text);
        Assert.EndsWith("(Good morning)", text.TrimEnd());
        Assert.Equal(32, Kat.Cells(text[..text.IndexOf('(')]));
    }

    [Fact]
    public void WithoutOriginal() => Assert.Equal("Good morning", Compose.Kat(JaResult, false));

    [Fact]
    public void RowPaddingCountsVoicedKatakanaAsTwoCells()
    {
        var text = Compose.Kat(R("en", "Game", "ゲーム", "ゲーム"), true);
        Assert.Equal(32, Kat.Cells(text[..text.IndexOf('(')]));
    }

    [Fact]
    public void MainLineExactlyOneRowNeedsNoPadding() =>
        Assert.Equal(new string('a', 32) + "(x)", Compose.Kat(R("ja", "x", new string('a', 32), "x"), true));

    [Fact]
    public void ChatboxUsesKanjiAndFits144()
    {
        Assert.Equal("おはよう、元気？\n(Good morning)", Compose.Chatbox(R("en", "Good morning", "おはよう、元気？", "おはよう、げんき？"), true));
        Assert.Equal(new string('b', 100), Compose.Chatbox(R("ja", new string('あ', 100), new string('b', 100), new string('あ', 100)), true));
        var output = Compose.Chatbox(R("ja", "x", new string('c', 200), "x"), true);
        Assert.Equal(144, output.Length);
        Assert.EndsWith("…", output);
    }

    [Fact]
    public void EmptyKanaOriginalDropsTheBracket() =>
        Assert.Equal("Tokyo", Compose.Kat(new TranslationResult(Lang.JA, Lang.EN, "東京", "Tokyo", KanaOriginal: ""), true));

    [Fact]
    public void LangParsesSenseVoiceTags()
    {
        Assert.Equal(Lang.JA, LangCodes.Parse("<|ja|>"));
        Assert.Equal(Lang.ZH, LangCodes.Parse("zh"));
        Assert.Equal(Lang.OTHER, LangCodes.Parse("<|ko|>"));
        Assert.Equal(Lang.NONE, LangCodes.Parse(""));
    }

    [Fact]
    public void ShortTextIsOnePage() => Assert.Equal(new[] { "hello" }, Compose.Paginate("hello"));

    [Fact]
    public void LongTextBreaksBetweenWords()
    {
        var text = string.Join(" ", Enumerable.Repeat("word", 40));
        var pages = Compose.Paginate(text);
        Assert.Equal(2, pages.Count);
        Assert.All(pages, p => Assert.True(Kat.Cells(p) <= 128));
        Assert.False(pages[0].EndsWith("wor"));
        Assert.Equal(text.Split(' '), string.Join(" ", pages.Select(p => p.Trim())).Split(' '));
    }

    [Fact]
    public void NeverSplitsAVoicedKatakana()
    {
        var pages = Compose.Paginate(new string('ア', 127) + "ガ" + "イ");
        Assert.Equal(2, pages.Count);
        Assert.StartsWith("ガ", pages[1]);
    }
}

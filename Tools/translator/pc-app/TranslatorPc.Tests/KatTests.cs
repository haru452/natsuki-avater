// Ported from the phone app's KatOscTests (same cases, same values).
using TranslatorPc.Core;

public class KatTests
{
    static List<int> E(string s) => Kat.EncodeText(s);
    static List<int> Cat(List<int> a, params int[] more) => a.Concat(more).ToList();

    [Fact]
    public void AsciiCodes()
    {
        Assert.Equal(new[] { 0 }, E(" "));
        Assert.Equal(new[] { 33 }, E("A"));
        Assert.Equal(new[] { 94 }, E("~"));
    }

    [Fact]
    public void KanaCodes()
    {
        Assert.Equal(new[] { 127 }, E("ぬ"));
        Assert.Equal(new[] { 129 }, E("ふ"));
        Assert.Equal(new[] { 133, 154, 136, 131 }, E("おはよう"));
        Assert.Equal(new[] { 208 }, E("ヌ"));
        Assert.Equal(new[] { 253 }, E("ロ"));
    }

    [Fact]
    public void UnknownCharBecomesQuestion()
    {
        Assert.Equal(E("?"), E("漢"));
        Assert.Equal(E("?"), E("😀"));
    }

    [Fact]
    public void MissingKanaAreSubstituted()
    {
        Assert.Equal(E("つ"), E("っ"));
        Assert.Equal(E("ツ"), E("ッ"));
        Assert.Equal(E("-"), E("ー"));
        Assert.Equal(E(","), E("、"));
        Assert.Equal(E("ヤ"), E("ャ"));
    }

    [Fact]
    public void VoicedKatakanaUsesSeparateMark()
    {
        Assert.Equal(Cat(E("カ"), 254), E("ガ"));
        Assert.Equal(Cat(E("ハ"), 255), E("パ"));
        Assert.Equal(2, Kat.CharWidth("ガ"));
        Assert.Equal(3, Kat.Cells("aガ"));
    }

    [Fact]
    public void FullwidthBecomesHalfwidth() => Assert.Equal(E("A1!"), E("Ａ１！"));

    [Fact]
    public void NeverEmitsUnsendableCode()
    {
        var codes = E(string.Concat(Enumerable.Range(0x20, 0x3100 - 0x20).Select(c => (char)c)));
        Assert.DoesNotContain(128, codes);
        Assert.All(codes, c => Assert.InRange(c, 0, 255));
    }

    [Fact]
    public void TruncatesAtLimit()
    {
        Assert.Equal(128, E(new string('x', 300)).Count);
        Assert.Equal(128, E(string.Concat(Enumerable.Repeat("ガ", 100))).Count);
    }

    [Fact]
    public void NewlineAndTabBecomeSpace() => Assert.Equal(E("a b c"), E("a\nb\tc"));

    [Fact]
    public void FloatMapping()
    {
        Assert.Equal(0f, Kat.CodeToFloat(0), 6);
        Assert.Equal(1f, Kat.CodeToFloat(127), 6);
        Assert.Equal(-1f, Kat.CodeToFloat(129), 6);
        Assert.Equal(-1f / 127f, Kat.CodeToFloat(255), 6);
    }

    [Fact]
    public void Groups_ClearThenPointerAndCharacters()
    {
        var groups = Kat.BuildMessages("Good morning", 8);
        Assert.Equal(3, groups.Count);
        Assert.Equal(Osc.Message("/avatar/parameters/KAT_Pointer", 255), Assert.Single(groups[0]));
        Assert.Equal(9, groups[1].Count);
        Assert.Equal(Osc.Message("/avatar/parameters/KAT_Pointer", 1), groups[1][0]);
        Assert.Equal(Osc.Message("/avatar/parameters/KAT_CharSync0", 39 / 127f), groups[1][1]);
        Assert.Equal(Osc.Message("/avatar/parameters/KAT_Pointer", 2), groups[2][0]);
    }

    [Fact]
    public void RewriteWithoutClear()
    {
        var groups = Kat.BuildMessages("Good morning", 8, clear: false);
        Assert.Equal(2, groups.Count);
        Assert.Equal(Osc.Message("/avatar/parameters/KAT_Pointer", 1), groups[0][0]);
    }

    [Fact]
    public void LastGroupPaddedWithSpaces() =>
        Assert.Equal(Osc.Message("/avatar/parameters/KAT_CharSync7", 0f), Kat.BuildMessages("Hi", 8)[1][8]);

    [Fact]
    public void LongTextUsesAllPointers()
    {
        Assert.Equal(1 + 16, Kat.BuildMessages(new string('x', 300), 8).Count);
        Assert.Equal(1 + 32, Kat.BuildMessages(new string('x', 300), 4).Count);
    }

    [Fact]
    public void EmptyTextOnlyClears() => Assert.Single(Kat.BuildMessages("", 8));

    [Fact]
    public void RejectsBadSync() => Assert.Throws<ArgumentException>(() => Kat.BuildMessages("a", 3));
}

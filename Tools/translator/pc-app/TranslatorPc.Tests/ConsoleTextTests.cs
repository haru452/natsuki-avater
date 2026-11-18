using TranslatorPc.Core;

public class ConsoleTextTests
{
    [Theory]
    [InlineData("abc", 3)]
    [InlineData("翻訳", 4)]          // Japanese takes two columns each
    [InlineData("ペット: オン", 12)]
    [InlineData("（）", 4)]          // full-width brackets too
    [InlineData("ｱ", 1)]            // half-width katakana: one
    public void Columns(string text, int columns) => Assert.Equal(columns, ConsoleText.Columns(text));

    [Theory]
    [InlineData("翻訳:        オフ（名前を呼ぶと反応だけします）", 20)]
    [InlineData("abc", 10)]
    [InlineData("あいうえお", 5)]   // cut between two-column letters: never past the edge
    public void Fit_FillsTheLineExactly_NeverWrapping(string text, int width) =>
        Assert.Equal(width, ConsoleText.Columns(ConsoleText.Fit(text, width)));

    [Fact]
    public void Fit_KeepsWhatFits() => Assert.Equal("翻訳  ", ConsoleText.Fit("翻訳", 6));

    [Fact]
    public void Lines_FitTheWindowHeight_KeepingTheStatusAndTheNewestMessages()
    {
        var status = new[] { "a", "b", "c" };
        var messages = new[] { "1", "2", "3", "4", "5" };
        Assert.Equal(new[] { "a", "b", "c", "4", "5" }, ConsoleText.Lines(status, messages, height: 6));
        Assert.Equal(new[] { "a", "b", "c", "1", "2", "3", "4", "5" }, ConsoleText.Lines(status, messages, height: 30));
    }
}

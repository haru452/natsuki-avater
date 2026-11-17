using TranslatorPc.Core;
using Xunit;

public class MicChoiceTests
{
    static readonly string[] Devices = { "マイク配列 (Realtek(R) Audio)", "ヘッドセット マイク (Arctis 7 Chat)" };

    [Fact]
    public void VrchatsChosenMicIsUsed() => Assert.Equal(1, MicChoice.Pick(Devices, "ヘッドセット マイク (Arctis 7 Chat)"));

    [Fact]
    public void NameMatchIgnoresCaseAndSpaces() => Assert.Equal(1, MicChoice.Pick(Devices, "  ヘッドセット マイク (ARCTIS 7 Chat) "));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Unplugged USB Mic")]
    public void OtherwiseWindowsDefault(string? vrchat) => Assert.Null(MicChoice.Pick(Devices, vrchat));
}

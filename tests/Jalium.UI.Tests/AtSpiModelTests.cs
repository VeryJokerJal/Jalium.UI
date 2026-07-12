using Jalium.UI.Automation;
using Jalium.UI.Controls.Automation.AtSpi;
using Jalium.UI.Controls.Automation;

namespace Jalium.UI.Tests;

public sealed class AtSpiModelTests
{
    [Fact]
    public void LinuxDiagnostics_OnNonLinux_DoesNotInitializeNativeInterop()
    {
        if (OperatingSystem.IsLinux())
            return;

        Assert.False(LinuxAccessibility.IsAtSpiActive);
        Assert.Equal("not-linux", LinuxAccessibility.AtSpiStatus);
        Assert.Null(LinuxAccessibility.AtSpiLastError);
    }

    [Theory]
    [InlineData(AutomationControlType.Button, null, 43)]
    [InlineData(AutomationControlType.Edit, "TextBox", 79)]
    [InlineData(AutomationControlType.Edit, "PasswordBox", 40)]
    [InlineData(AutomationControlType.Window, null, 23)]
    [InlineData(AutomationControlType.TreeItem, null, 91)]
    [InlineData(AutomationControlType.TitleBar, null, 104)]
    public void RoleMapping_UsesAtSpiProtocolValues(
        AutomationControlType controlType,
        string? className,
        int expected)
    {
        Assert.Equal((uint)expected, (uint)AtSpiModel.MapRole(controlType, className));
    }

    [Fact]
    public void PackStates_SplitsTheProtocolBitsetIntoTwoUInt32Words()
    {
        ulong states = AtSpiModel.State(AtSpiState.Enabled) |
                       AtSpiModel.State(AtSpiState.Focused) |
                       AtSpiModel.State(AtSpiState.SelectableText) |
                       AtSpiModel.State(AtSpiState.ReadOnly);

        uint[] words = AtSpiModel.PackStates(states);

        Assert.Equal((1u << 8) | (1u << 12), words[0]);
        Assert.Equal((1u << (38 - 32)) | (1u << (43 - 32)), words[1]);
    }

    [Theory]
    [InlineData("alpha beta", 7, 1, "beta", 6, 10)]
    [InlineData("first\nsecond", 8, 4, "second", 6, 12)]
    [InlineData("A😀B", 1, 0, "😀", 1, 3)]
    public void GetStringAtOffset_UsesHalfOpenUtf16Offsets(
        string value,
        int offset,
        uint granularity,
        string expected,
        int expectedStart,
        int expectedEnd)
    {
        var result = AtSpiModel.GetStringAtOffset(value, offset, granularity);

        Assert.Equal(expected, result.Text);
        Assert.Equal(expectedStart, result.Start);
        Assert.Equal(expectedEnd, result.End);
    }

    [Fact]
    public void Quote_ProducesSafeGVariantStringSyntax()
    {
        Assert.Equal("'a\\'b\\\\c\\n\\u0001'", AtSpiModel.Quote("a'b\\c\n\u0001"));
    }

    [Theory]
    [InlineData("abcdef", -4, 3, "abc")]
    [InlineData("abcdef", 2, -1, "cdef")]
    [InlineData("abcdef", 20, 30, "")]
    public void SliceText_ClampsUntrustedOffsets(string value, int start, int end, string expected)
    {
        Assert.Equal(expected, AtSpiModel.SliceText(value, start, end));
    }
}

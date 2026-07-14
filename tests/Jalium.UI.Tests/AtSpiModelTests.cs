using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
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
    [InlineData("A😀B", 1, 0, "😀", 1, 2)]
    [InlineData("alpha beta", 7, 1, "beta", 6, 10)]
    [InlineData("One. Two!", 6, 2, "Two!", 5, 9)]
    [InlineData("first\nsecond", 8, 3, "second", 6, 12)]
    [InlineData("first\u2028line\nsecond", 7, 4, "first\u2028line\n", 0, 11)]
    public void GetStringAtOffset_UsesOfficialScalarGranularities(
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

    [Theory]
    [InlineData(0, 5, "w", 5, 6)]
    [InlineData(1, 5, "two. ", 4, 9)]
    [InlineData(2, 5, " two", 3, 7)]
    [InlineData(3, 10, "Next!\n", 9, 15)]
    [InlineData(4, 10, " Next!", 8, 14)]
    [InlineData(5, 10, "one two. Next!\n", 0, 15)]
    [InlineData(6, 16, "\nlast", 14, 19)]
    public void LegacyTextAtOffset_ImplementsAllSevenBoundaryTypes(
        uint boundaryType,
        int offset,
        string expected,
        int expectedStart,
        int expectedEnd)
    {
        var result = AtSpiModel.GetLegacyTextAtOffset(
            "one two. Next!\nlast",
            offset,
            boundaryType,
            AtSpiLegacyTextPosition.At);

        Assert.Equal((expected, expectedStart, expectedEnd), result);
    }

    [Theory]
    [InlineData(0, 5, "two", 4, 7)]
    [InlineData(1, 4, "one ", 0, 4)]
    [InlineData(2, 4, "two", 4, 7)]
    [InlineData(2, 1, "two", 4, 7)]
    public void LegacyTextRelativeQueries_UseIndependentRanges(
        int position,
        int offset,
        string expected,
        int expectedStart,
        int expectedEnd)
    {
        var result = AtSpiModel.GetLegacyTextAtOffset(
            "one two", offset, 1, (AtSpiLegacyTextPosition)position);

        Assert.Equal((expected, expectedStart, expectedEnd), result);
    }

    [Fact]
    public void ScalarMapper_ConvertsEmojiWithoutExposingSurrogateOffsets()
    {
        const string value = "A😀B";

        Assert.Equal(3, AtSpiModel.GetCharacterCount(value));
        Assert.Equal(3, AtSpiModel.ScalarToUtf16Offset(value, 2));
        Assert.Equal(1, AtSpiModel.Utf16ToScalarOffset(value, 2));
        Assert.Equal(2, AtSpiModel.Utf16ToScalarOffset(value, 2, roundUp: true));
        Assert.Equal(0x1F600, AtSpiModel.GetCharacterAtOffset(value, 1));
        Assert.Equal("😀", AtSpiModel.SliceText(value, 1, 2));
    }

    [Fact]
    public void TextChange_UsesScalarLongestCommonPrefixAndSuffix()
    {
        AtSpiTextChange change = AtSpiModel.GetTextChange("A😀BC", "A😎XYC");

        Assert.Equal(1, change.Offset);
        Assert.Equal("😀B", change.DeletedText);
        Assert.Equal(2, change.DeletedCount);
        Assert.Equal("😎XY", change.InsertedText);
        Assert.Equal(3, change.InsertedCount);
    }

    [Theory]
    [InlineData(0u, true, true)]
    [InlineData(0u, false, false)]
    [InlineData(1u, true, false)]
    [InlineData(2u, true, false)]
    public void ScreenCoordinates_OnlyUseWindowOriginWhenBackendHasGlobalCoordinates(
        uint coordinateType,
        bool hasGlobalScreenCoordinates,
        bool expected)
    {
        Assert.Equal(
            expected,
            AtSpiAccessibilityBridge.ShouldApplyScreenWindowOffset(
                coordinateType,
                hasGlobalScreenCoordinates));
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
    [InlineData("A😀B", 1, 2, "😀")]
    public void SliceText_ClampsUntrustedOffsets(string value, int start, int end, string expected)
    {
        Assert.Equal(expected, AtSpiModel.SliceText(value, start, end));
    }

    [Fact]
    public void Introspection_AdvertisesSelectionTableAndClipboardEditingContracts()
    {
        var document = System.Xml.Linq.XDocument.Parse(
            AtSpiAccessibilityBridge.IntrospectionDocument);
        var interfaces = document.Root!.Elements("interface")
            .ToDictionary(element => (string)element.Attribute("name")!);

        Assert.Contains("org.a11y.atspi.Selection", interfaces.Keys);
        Assert.Contains("org.a11y.atspi.Table", interfaces.Keys);
        Assert.Contains("Text", interfaces["org.a11y.atspi.Value"]
            .Elements("property").Select(element => (string)element.Attribute("name")!));

        var copyText = interfaces["org.a11y.atspi.EditableText"]
            .Elements("method")
            .Single(element => (string)element.Attribute("name")! == "CopyText");
        Assert.DoesNotContain(copyText.Elements("arg"),
            argument => (string?)argument.Attribute("direction") == "out");

        var table = interfaces["org.a11y.atspi.Table"];
        Assert.Contains(table.Elements("method"),
            method => (string?)method.Attribute("name") == "GetRowColumnExtentsAtIndex");
        Assert.Contains(table.Elements("method"),
            method => (string?)method.Attribute("name") == "GetSelectedRows");
    }
}

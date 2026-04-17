using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Data;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class HexEditorTests
{
    private static void ResetApplicationState()
    {
        var currentField = typeof(Application).GetField("_current",
            BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);
        var resetMethod = typeof(ThemeManager).GetMethod("Reset",
            BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);
    }

    [Fact]
    public void HexEditor_DefaultProperties()
    {
        var editor = new HexEditor();

        Assert.Equal(16, editor.BytesPerRow);
        Assert.Equal(8, editor.ColumnGroupSize);
        Assert.Equal(HexDisplayFormat.Byte, editor.DisplayFormat);
        Assert.Equal(Endianness.Little, editor.Endianness);
        Assert.False(editor.IsReadOnly);
    }

    [Fact]
    public void HexEditor_DefaultSelectionProperties()
    {
        var editor = new HexEditor();

        Assert.Equal(-1L, editor.SelectionStart);
        Assert.Equal(0L, editor.SelectionLength);
        Assert.Equal(0L, editor.CaretOffset);
    }

    [Fact]
    public void HexEditor_DefaultColumnVisibility()
    {
        var editor = new HexEditor();

        Assert.True(editor.ShowOffsetColumn);
        Assert.True(editor.ShowAsciiColumn);
        Assert.False(editor.ShowDataInterpretation);
    }

    [Fact]
    public void HexEditor_Data_DefaultsToNull()
    {
        var editor = new HexEditor();
        Assert.Null(editor.Data);
    }

    [Fact]
    public void HexEditor_Data_CanBeSet()
    {
        var editor = new HexEditor();
        var testData = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F };
        editor.Data = testData;

        Assert.NotNull(editor.Data);
        Assert.Equal(5, editor.Data!.Length);
        Assert.Equal(0x48, editor.Data[0]);
    }

    [Fact]
    public void HexEditor_BytesPerRow_CanBeSet()
    {
        var editor = new HexEditor();
        editor.BytesPerRow = 32;
        Assert.Equal(32, editor.BytesPerRow);
    }

    [Fact]
    public void HexEditor_ColumnGroupSize_CanBeSet()
    {
        var editor = new HexEditor();
        editor.ColumnGroupSize = 4;
        Assert.Equal(4, editor.ColumnGroupSize);
    }

    [Fact]
    public void HexEditor_DisplayFormat_CanBeSet()
    {
        var editor = new HexEditor();
        editor.DisplayFormat = HexDisplayFormat.Word16;
        Assert.Equal(HexDisplayFormat.Word16, editor.DisplayFormat);
    }

    [Fact]
    public void HexEditor_Endianness_CanBeSet()
    {
        var editor = new HexEditor();
        editor.Endianness = Endianness.Big;
        Assert.Equal(Endianness.Big, editor.Endianness);
    }

    [Fact]
    public void HexEditor_IsReadOnly_CanBeSet()
    {
        var editor = new HexEditor();
        editor.IsReadOnly = true;
        Assert.True(editor.IsReadOnly);
    }

    [Fact]
    public void HexEditor_SelectionStart_CanBeSet()
    {
        var editor = new HexEditor();
        editor.SelectionStart = 10;
        Assert.Equal(10L, editor.SelectionStart);
    }

    [Fact]
    public void HexEditor_SelectionLength_CanBeSet()
    {
        var editor = new HexEditor();
        editor.SelectionLength = 5;
        Assert.Equal(5L, editor.SelectionLength);
    }

    [Fact]
    public void HexEditor_CaretOffset_CanBeSet()
    {
        var editor = new HexEditor();
        editor.CaretOffset = 42;
        Assert.Equal(42L, editor.CaretOffset);
    }

    [Fact]
    public void HexEditor_FindBytes_ReturnsNegativeOne_WhenNoData()
    {
        var editor = new HexEditor();
        var result = editor.FindBytes(new byte[] { 0xFF });
        Assert.Equal(-1L, result);
    }

    [Fact]
    public void HexEditor_FindBytes_ReturnsNegativeOne_WhenEmptyPattern()
    {
        var editor = new HexEditor();
        editor.Data = new byte[] { 0x01, 0x02, 0x03 };
        var result = editor.FindBytes(Array.Empty<byte>());
        Assert.Equal(-1L, result);
    }

    [Fact]
    public void HexEditor_FindBytes_FindsPattern()
    {
        var editor = new HexEditor();
        editor.Data = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 };
        editor.CaretOffset = -1; // Start from beginning (FindBytes starts from CaretOffset+1)
        var result = editor.FindBytes(new byte[] { 0x02, 0x03 });
        Assert.Equal(2L, result);
    }

    [Fact]
    public void HexEditor_FindBytes_ReturnsNegativeOne_WhenPatternNotFound()
    {
        var editor = new HexEditor();
        editor.Data = new byte[] { 0x00, 0x01, 0x02 };
        editor.CaretOffset = -1;
        var result = editor.FindBytes(new byte[] { 0xFF, 0xFE });
        Assert.Equal(-1L, result);
    }

    [Fact]
    public void HexEditor_BrushProperties_DefaultToNull()
    {
        var editor = new HexEditor();
        Assert.Null(editor.OffsetForeground);
        Assert.Null(editor.HexForeground);
        Assert.Null(editor.AsciiForeground);
        Assert.Null(editor.ModifiedByteBrush);
        Assert.Null(editor.SelectionBrush);
        Assert.Null(editor.GutterBackground);
        Assert.Null(editor.ColumnSeparatorBrush);
    }

    [Fact]
    public void HexEditor_OffsetFormat_DefaultsToX8()
    {
        var editor = new HexEditor();
        Assert.Equal("X8", editor.OffsetFormat);
    }

    [Fact]
    public void HexEditor_HexStringFormatting_ByteToHex()
    {
        // Verify basic byte-to-hex formatting works as expected
        byte testByte = 0xAB;
        string hex = testByte.ToString("X2");
        Assert.Equal("AB", hex);
    }

    [Fact]
    public void HexEditor_HexStringFormatting_OffsetFormat()
    {
        long offset = 0x1234;
        string formatted = offset.ToString("X8");
        Assert.Equal("00001234", formatted);
    }
}

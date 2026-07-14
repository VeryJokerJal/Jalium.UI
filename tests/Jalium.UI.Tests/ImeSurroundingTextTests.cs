using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Documents;

namespace Jalium.UI.Tests;

public sealed class ImeSurroundingTextTests
{
    [Fact]
    public void TextBoxSnapshot_UsesUtf16CursorAndAnchor()
    {
        var textBox = new TextBox { Text = "A😀e\u0301Z" };
        textBox.Select(1, 4);

        Assert.True(((IImeSupport)textBox).TryGetImeSurroundingText(out var snapshot));
        Assert.Equal("A😀e\u0301Z", snapshot.Text);
        Assert.Equal(5, snapshot.CursorIndex);
        Assert.Equal(1, snapshot.AnchorIndex);
    }

    [Fact]
    public void TextBoxDeleteSurrounding_UsesUtf8ByteCounts()
    {
        var textBox = new TextBox { Text = "hello", CaretIndex = 3 };

        Assert.True(((IImeSupport)textBox).DeleteImeSurroundingText(1, 1));

        Assert.Equal("heo", textBox.Text);
        Assert.Equal(2, textBox.CaretIndex);
        Assert.Equal(0, textBox.SelectionLength);
    }

    [Theory]
    [InlineData("A😀B", 3, 1, "AB")]
    [InlineData("Ae\u0301B", 3, 1, "AB")]
    public void TextBoxDeleteSurrounding_ExpandsMidByteRequestToWholeGrapheme(
        string text,
        int caret,
        int beforeUtf8Bytes,
        string expected)
    {
        var textBox = new TextBox { Text = text, CaretIndex = caret };

        Assert.True(((IImeSupport)textBox).DeleteImeSurroundingText(beforeUtf8Bytes, 0));

        Assert.Equal(expected, textBox.Text);
        Assert.Equal(1, textBox.CaretIndex);
    }

    [Fact]
    public void TextBoxDeleteSurrounding_AlwaysDeletesCurrentSelection()
    {
        var textBox = new TextBox { Text = "ab😀cd" };
        textBox.Select(2, 2);

        Assert.True(((IImeSupport)textBox).DeleteImeSurroundingText(0, 0));

        Assert.Equal("abcd", textBox.Text);
        Assert.Equal(2, textBox.CaretIndex);
        Assert.Equal(0, textBox.SelectionLength);
    }

    [Fact]
    public void TextBoxBaseDerivedEditors_InheritSurroundingTextContract()
    {
        var autoComplete = new AutoCompleteBox { Text = "A😀B", CaretIndex = 3 };
        var numberBox = new NumberBox { Text = "12.5", CaretIndex = 4 };

        Assert.True(((IImeSupport)autoComplete).TryGetImeSurroundingText(out var autoSnapshot));
        Assert.Equal(("A😀B", 3, 3),
            (autoSnapshot.Text, autoSnapshot.CursorIndex, autoSnapshot.AnchorIndex));

        Assert.True(((IImeSupport)numberBox).TryGetImeSurroundingText(out var numberSnapshot));
        Assert.Equal(("12.5", 4, 4),
            (numberSnapshot.Text, numberSnapshot.CursorIndex, numberSnapshot.AnchorIndex));
    }

    [Fact]
    public void EditControl_ProvidesAndDeletesSurroundingText()
    {
        var editor = new EditControl { Text = "A😀B" };
        editor.CaretOffset = 3;

        Assert.True(((IImeSupport)editor).TryGetImeSurroundingText(out var snapshot));
        Assert.Equal(("A😀B", 3, 3),
            (snapshot.Text, snapshot.CursorIndex, snapshot.AnchorIndex));

        Assert.True(((IImeSupport)editor).DeleteImeSurroundingText(1, 0));
        Assert.Equal("AB", editor.Text);
        Assert.Equal(1, editor.CaretOffset);
    }

    [Fact]
    public void RichTextBox_ProvidesAndDeletesPlainSurroundingText()
    {
        var richTextBox = new RichTextBox(FlowDocument.FromText("A😀B"));
        richTextBox.CaretPosition = richTextBox.Document.GetPositionAtOffset(
            3, LogicalDirection.Forward);
        string documentText = $"A😀B{Environment.NewLine}";

        Assert.True(((IImeSupport)richTextBox).TryGetImeSurroundingText(out var snapshot));
        Assert.Equal((documentText, 3, 3),
            (snapshot.Text, snapshot.CursorIndex, snapshot.AnchorIndex));

        Assert.True(((IImeSupport)richTextBox).DeleteImeSurroundingText(1, 0));
        Assert.Equal($"AB{Environment.NewLine}", richTextBox.Document.GetText());
        Assert.Equal(1, richTextBox.CaretPosition?.DocumentOffset);
    }

    [Fact]
    public void PasswordBox_NeverExposesOrDeletesSecretText()
    {
        var passwordBox = new PasswordBox { Password = "secret😀" };
        IImeSupport support = passwordBox;

        Assert.False(support.TryGetImeSurroundingText(out var snapshot));
        Assert.Equal(default, snapshot);
        Assert.False(support.DeleteImeSurroundingText(100, 100));
        Assert.Equal("secret😀", passwordBox.Password);
    }

    [Fact]
    public void Terminal_KeepsSafeNoSurroundingTextDefault()
    {
        IImeSupport support = new Terminal();

        Assert.False(support.TryGetImeSurroundingText(out _));
        Assert.False(support.DeleteImeSurroundingText(1, 1));
    }

    [Fact]
    public void PlatformContext_ConvertsUtf16IndicesAndCaretDipsToNativeUnits()
    {
        var snapshot = new ImeSurroundingTextSnapshot(
            "A😀e\u0301Z",
            CursorIndex: 3,
            AnchorIndex: 1);

        PlatformImeContext context = PlatformImeContext.Create(
            enabled: true,
            snapshot,
            new Rect(2.5, 3, 1, 4),
            new Point(10, 20),
            dpiScale: 2);

        Assert.True(context.Enabled);
        Assert.Equal("A😀e\u0301Z", context.SurroundingText);
        Assert.Equal(5, context.CursorUtf8ByteOffset);
        Assert.Equal(1, context.AnchorUtf8ByteOffset);
        Assert.Equal((25, 46, 2, 8),
            (context.CaretX, context.CaretY, context.CaretWidth, context.CaretHeight));
    }

    [Fact]
    public void PlatformContext_UsesNullToMeanSurroundingTextIsNotAvailable()
    {
        PlatformImeContext context = PlatformImeContext.Create(
            enabled: true,
            surroundingText: null,
            new Rect(1, 2, 0, 0),
            default,
            dpiScale: 1);

        Assert.True(context.Enabled);
        Assert.Null(context.SurroundingText);
        Assert.Equal(0, context.CursorUtf8ByteOffset);
        Assert.Equal(0, context.AnchorUtf8ByteOffset);
        Assert.Equal(1, context.CaretWidth);
        Assert.Equal(1, context.CaretHeight);
    }

    [Fact]
    public void PlatformContext_TrimsLongSurroundingTextAroundCursorTo4000Utf8Bytes()
    {
        string text = new('x', 6000);
        var snapshot = new ImeSurroundingTextSnapshot(text, 3000, 3000);

        PlatformImeContext context = PlatformImeContext.Create(
            enabled: true, snapshot, new Rect(0, 0, 1, 1), default, dpiScale: 1);

        Assert.NotNull(context.SurroundingText);
        Assert.Equal(4000, System.Text.Encoding.UTF8.GetByteCount(context.SurroundingText!));
        Assert.Equal(2000, context.CursorUtf8ByteOffset);
        Assert.Equal(context.CursorUtf8ByteOffset, context.AnchorUtf8ByteOffset);
    }

    [Fact]
    public void PlatformContext_TrimmingNeverSplitsEmojiAndRejectsOversizedSelection()
    {
        string text = "x" + string.Concat(Enumerable.Repeat("😀", 1100)) + "y";
        int cursor = 1 + (550 * 2);

        PlatformImeContext trimmed = PlatformImeContext.Create(
            enabled: true,
            new ImeSurroundingTextSnapshot(text, cursor, cursor),
            new Rect(0, 0, 1, 1),
            default,
            dpiScale: 1);

        Assert.NotNull(trimmed.SurroundingText);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(trimmed.SurroundingText!) <= 4000);
        Assert.False(char.IsLowSurrogate(trimmed.SurroundingText![0]));
        Assert.False(char.IsHighSurrogate(trimmed.SurroundingText[^1]));

        PlatformImeContext oversizedSelection = PlatformImeContext.Create(
            enabled: true,
            new ImeSurroundingTextSnapshot(text, 1, text.Length - 1),
            new Rect(0, 0, 1, 1),
            default,
            dpiScale: 1);
        Assert.Null(oversizedSelection.SurroundingText);
    }

    [Fact]
    public void DeleteSurroundingEvent_UsesReservedNativeEventNumber46()
    {
        Assert.Equal(46, (int)PlatformEventType.DeleteSurroundingText);
    }
}

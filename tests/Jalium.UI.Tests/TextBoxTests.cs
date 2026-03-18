using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

/// <summary>
/// TextBox 控件测试
/// </summary>
public class TextBoxTests
{
    [Fact]
    public void TextBox_ShouldHaveDefaultProperties()
    {
        // Arrange & Act
        var textBox = new TextBox();

        // Assert
        Assert.Equal(string.Empty, textBox.Text);
        Assert.Equal(0, textBox.MaxLength);
        Assert.Equal(TextWrapping.NoWrap, textBox.TextWrapping);
        Assert.Equal(TextTrimming.CharacterEllipsis, textBox.TextTrimming);
        Assert.Equal(TextAlignment.Left, textBox.TextAlignment);
        Assert.Equal(string.Empty, textBox.PlaceholderText);
        Assert.False(textBox.IsSpellCheckEnabled);
        Assert.True(textBox.Focusable);
    }

    [Fact]
    public void TextBox_Text_ShouldBeSettable()
    {
        // Arrange
        var textBox = new TextBox();

        // Act
        textBox.Text = "Hello World";

        // Assert
        Assert.Equal("Hello World", textBox.Text);
    }

    [Fact]
    public void TextBox_Text_ShouldBeReadable()
    {
        // Arrange
        var textBox = new TextBox();
        textBox.Text = "Test Content";

        // Act
        var text = textBox.Text;

        // Assert
        Assert.Equal("Test Content", text);
    }

    [Fact]
    public void TextBox_MaxLength_ShouldBeSettable()
    {
        // Arrange
        var textBox = new TextBox();

        // Act
        textBox.MaxLength = 100;

        // Assert
        Assert.Equal(100, textBox.MaxLength);
    }

    [Fact]
    public void TextBox_TextWrapping_ShouldBeSettable()
    {
        // Arrange
        var textBox = new TextBox();

        // Act
        textBox.TextWrapping = TextWrapping.Wrap;

        // Assert
        Assert.Equal(TextWrapping.Wrap, textBox.TextWrapping);
    }

    [Fact]
    public void TextBox_TextAlignment_ShouldBeSettable()
    {
        // Arrange
        var textBox = new TextBox();

        // Act
        textBox.TextAlignment = TextAlignment.Center;

        // Assert
        Assert.Equal(TextAlignment.Center, textBox.TextAlignment);
    }

    [Fact]
    public void TextBox_Placeholder_ShouldBeSettable()
    {
        // Arrange
        var textBox = new TextBox();

        // Act
        textBox.PlaceholderText = "Enter your name";

        // Assert
        Assert.Equal("Enter your name", textBox.PlaceholderText);
    }

    [Fact]
    public void TextBox_TextChanged_ShouldRaiseEvent()
    {
        // Arrange
        var textBox = new TextBox();
        var textChangedRaised = false;
        textBox.TextChanged += (s, e) => textChangedRaised = true;

        // Act
        textBox.Text = "New Text";

        // Assert
        Assert.True(textChangedRaised);
    }

    [Fact]
    public void TextBox_Clear_ShouldEmptyText()
    {
        // Arrange
        var textBox = new TextBox();
        textBox.Text = "Some content";

        // Act
        textBox.Clear();

        // Assert
        Assert.Equal(string.Empty, textBox.Text);
    }

    [Fact]
    public void TextBox_AppendText_ShouldAddToEnd()
    {
        // Arrange
        var textBox = new TextBox();
        textBox.Text = "Hello";

        // Act
        textBox.AppendText(" World");

        // Assert
        Assert.Equal("Hello World", textBox.Text);
    }

    [Fact]
    public void TextBox_LineCount_SingleLine_ShouldBeOne()
    {
        // Arrange
        var textBox = new TextBox();
        textBox.Text = "Single line text";

        // Act
        var lineCount = textBox.LineCount;

        // Assert
        Assert.Equal(1, lineCount);
    }

    [Fact]
    public void TextBox_LineCount_MultiLine_ShouldCountLines()
    {
        // Arrange
        var textBox = new TextBox();
        textBox.Text = "Line 1\nLine 2\nLine 3";

        // Act
        var lineCount = textBox.LineCount;

        // Assert
        Assert.Equal(3, lineCount);
    }

    [Fact]
    public void TextBox_GetLineText_ShouldReturnCorrectLine()
    {
        // Arrange
        var textBox = new TextBox();
        textBox.Text = "First\nSecond\nThird";

        // Act & Assert
        Assert.Equal("First", textBox.GetLineText(0));
        Assert.Equal("Second", textBox.GetLineText(1));
        Assert.Equal("Third", textBox.GetLineText(2));
    }

    [Fact]
    public void TextBox_GetLineLength_ShouldReturnCorrectLength()
    {
        // Arrange
        var textBox = new TextBox();
        textBox.Text = "Short\nMedium length\nLonger line here";

        // Act & Assert
        Assert.Equal(5, textBox.GetLineLength(0));  // "Short"
        Assert.Equal(13, textBox.GetLineLength(1)); // "Medium length"
        Assert.Equal(16, textBox.GetLineLength(2)); // "Longer line here"
    }

    [Fact]
    public void TextBox_GetCharacterIndexFromLineIndex_ShouldReturnCorrectIndex()
    {
        // Arrange
        var textBox = new TextBox();
        textBox.Text = "Line1\nLine2\nLine3";

        // Act & Assert
        Assert.Equal(0, textBox.GetCharacterIndexFromLineIndex(0));  // Start of "Line1"
        Assert.Equal(6, textBox.GetCharacterIndexFromLineIndex(1));  // Start of "Line2"
        Assert.Equal(12, textBox.GetCharacterIndexFromLineIndex(2)); // Start of "Line3"
    }

    [Fact]
    public void TextBox_GetLineIndexFromCharacterIndex_ShouldReturnCorrectLine()
    {
        // Arrange
        var textBox = new TextBox();
        textBox.Text = "Line1\nLine2\nLine3";

        // Act & Assert
        Assert.Equal(0, textBox.GetLineIndexFromCharacterIndex(0));  // In Line1
        Assert.Equal(0, textBox.GetLineIndexFromCharacterIndex(3));  // In Line1
        Assert.Equal(1, textBox.GetLineIndexFromCharacterIndex(6));  // In Line2
        Assert.Equal(2, textBox.GetLineIndexFromCharacterIndex(12)); // In Line3
    }

    [Fact]
    public void TextBox_IsReadOnly_ShouldDefaultToFalse()
    {
        // Arrange & Act
        var textBox = new TextBox();

        // Assert
        Assert.False(textBox.IsReadOnly);
    }

    [Fact]
    public void TextBox_IsReadOnly_ShouldBeSettable()
    {
        // Arrange
        var textBox = new TextBox();

        // Act
        textBox.IsReadOnly = true;

        // Assert
        Assert.True(textBox.IsReadOnly);
    }

    [Fact]
    public void TextBox_AcceptsReturn_ShouldDefaultToFalse()
    {
        // Arrange & Act
        var textBox = new TextBox();

        // Assert
        Assert.False(textBox.AcceptsReturn);
    }

    [Fact]
    public void TextBox_AcceptsReturn_ShouldBeSettable()
    {
        // Arrange
        var textBox = new TextBox();

        // Act
        textBox.AcceptsReturn = true;

        // Assert
        Assert.True(textBox.AcceptsReturn);
    }

    [Fact]
    public void TextBox_EmptyText_LineCount_ShouldBeOne()
    {
        // Arrange
        var textBox = new TextBox();
        textBox.Text = string.Empty;

        // Act
        var lineCount = textBox.LineCount;

        // Assert - even empty text has one line
        Assert.Equal(1, lineCount);
    }

    [Fact]
    public void TextBox_CRLFLineEndings_ShouldParseCorrectly()
    {
        // Arrange
        var textBox = new TextBox();
        textBox.Text = "Line1\r\nLine2\r\nLine3";

        // Act
        var lineCount = textBox.LineCount;

        // Assert
        Assert.Equal(3, lineCount);
    }

    [Fact]
    public void TextBox_SpellCheck_ShouldBeSettable()
    {
        // Arrange
        var textBox = new TextBox();

        // Act
        textBox.IsSpellCheckEnabled = true;

        // Assert
        Assert.True(textBox.IsSpellCheckEnabled);
    }

    [Fact]
    public void TextBox_SpellCheckLanguage_ShouldDefaultToEnUS()
    {
        // Arrange & Act
        var textBox = new TextBox();

        // Assert
        Assert.Equal("en-US", textBox.SpellCheckLanguage);
    }

    [Fact]
    public void TextBox_SpellCheckLanguage_ShouldBeSettable()
    {
        // Arrange
        var textBox = new TextBox();

        // Act
        textBox.SpellCheckLanguage = "zh-CN";

        // Assert
        Assert.Equal("zh-CN", textBox.SpellCheckLanguage);
    }

    [Fact]
    public void TextBox_DetectUrls_ShouldBeSettable()
    {
        // Arrange
        var textBox = new TextBox();

        // Act
        textBox.DetectUrls = true;

        // Assert
        Assert.True(textBox.DetectUrls);
    }

    [Fact]
    public void TextBox_DeleteSelection_ShouldBeUndoable()
    {
        // Arrange
        var textBox = new TextBox { Text = "Hello World" };
        textBox.SelectAll();

        // Act
        textBox.RaiseEvent(new KeyEventArgs(UIElement.KeyDownEvent, Key.Delete, ModifierKeys.None, isDown: true, isRepeat: false, timestamp: 0));

        // Assert
        Assert.Equal(string.Empty, textBox.Text);
        Assert.True(textBox.CanUndo);

        textBox.Undo();

        Assert.Equal("Hello World", textBox.Text);
    }

    [Fact]
    public void TextBox_CtrlZ_ShouldRestoreDeletedSelection()
    {
        // Arrange
        var textBox = new TextBox { Text = "Hello World" };
        textBox.SelectAll();

        textBox.RaiseEvent(new KeyEventArgs(UIElement.KeyDownEvent, Key.Delete, ModifierKeys.None, isDown: true, isRepeat: false, timestamp: 0));

        // Act
        textBox.RaiseEvent(new KeyEventArgs(UIElement.KeyDownEvent, Key.Z, ModifierKeys.Control, isDown: true, isRepeat: false, timestamp: 1));

        // Assert
        Assert.Equal("Hello World", textBox.Text);
    }
}

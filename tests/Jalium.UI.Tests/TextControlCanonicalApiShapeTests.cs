using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Tests;

public sealed class TextControlCanonicalApiShapeTests
{
    private const BindingFlags DeclaredPublic =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    [Fact]
    public void TextBoxBase_DeclaresOnlyTheWpfSurface()
    {
        Assert.Equal(typeof(Control), typeof(TextBoxBase).BaseType);
        ConstructorInfo constructor = Assert.Single(typeof(TextBoxBase).GetConstructors(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
        Assert.True(constructor.IsAssembly);

        AssertNames(typeof(TextBoxBase).GetFields(DeclaredPublic),
            "AcceptsReturnProperty", "AcceptsTabProperty", "AutoWordSelectionProperty",
            "CaretBrushProperty", "HorizontalScrollBarVisibilityProperty",
            "IsInactiveSelectionHighlightEnabledProperty", "IsReadOnlyCaretVisibleProperty",
            "IsReadOnlyProperty", "IsSelectionActiveProperty", "IsUndoEnabledProperty",
            "SelectionBrushProperty", "SelectionChangedEvent", "SelectionOpacityProperty",
            "SelectionTextBrushProperty", "TextChangedEvent", "UndoLimitProperty",
            "VerticalScrollBarVisibilityProperty");
        AssertNames(typeof(TextBoxBase).GetProperties(DeclaredPublic),
            "AcceptsReturn", "AcceptsTab", "AutoWordSelection", "CanRedo", "CanUndo",
            "CaretBrush", "ExtentHeight", "ExtentWidth", "HorizontalOffset",
            "HorizontalScrollBarVisibility", "IsInactiveSelectionHighlightEnabled", "IsReadOnly",
            "IsReadOnlyCaretVisible", "IsSelectionActive", "IsUndoEnabled", "SelectionBrush",
            "SelectionOpacity", "SelectionTextBrush", "SpellCheck", "UndoLimit", "VerticalOffset",
            "VerticalScrollBarVisibility", "ViewportHeight", "ViewportWidth");
        AssertMethodNames(typeof(TextBoxBase),
            "AppendText", "BeginChange", "Copy", "Cut", "DeclareChangeBlock", "EndChange",
            "LineDown", "LineLeft", "LineRight", "LineUp", "LockCurrentUndoUnit", "OnApplyTemplate",
            "PageDown", "PageLeft", "PageRight", "PageUp", "Paste", "Redo", "ScrollToEnd",
            "ScrollToHome", "ScrollToHorizontalOffset", "ScrollToVerticalOffset", "SelectAll", "Undo");
        Assert.False(typeof(TextBoxBase).GetProperty(nameof(TextBoxBase.HorizontalOffset))!.CanWrite);
        Assert.False(typeof(TextBoxBase).GetProperty(nameof(TextBoxBase.VerticalOffset))!.CanWrite);
    }

    [Fact]
    public void TextBox_DeclaresSelectionAndLineApisOnTheCorrectOwner()
    {
        Assert.Equal(typeof(TextBoxBase), typeof(TextBox).BaseType);
        AssertNames(typeof(TextBox).GetFields(DeclaredPublic),
            "CharacterCasingProperty", "MaxLengthProperty", "MaxLinesProperty", "MinLinesProperty",
            "TextAlignmentProperty", "TextDecorationsProperty", "TextProperty", "TextWrappingProperty");
        AssertNames(typeof(TextBox).GetProperties(DeclaredPublic),
            "CaretIndex", "CharacterCasing", "LineCount", "MaxLength", "MaxLines", "MinLines",
            "SelectedText", "SelectionLength", "SelectionStart", "Text", "TextAlignment",
            "TextDecorations", "TextWrapping", "Typography");
        AssertMethodNames(typeof(TextBox),
            "Clear", "GetCharacterIndexFromLineIndex", "GetCharacterIndexFromPoint",
            "GetFirstVisibleLineIndex", "GetLastVisibleLineIndex", "GetLineIndexFromCharacterIndex",
            "GetLineLength", "GetLineText", "GetNextSpellingErrorCharacterIndex",
            "GetRectFromCharacterIndex", "GetRectFromCharacterIndex", "GetSpellingError",
            "GetSpellingErrorLength", "GetSpellingErrorStart", "ScrollToLine", "Select",
            "ShouldSerializeText");
    }

    [Fact]
    public void RichTextBox_DeclaresOnlyItsDocumentSurface()
    {
        Assert.Equal(typeof(TextBoxBase), typeof(RichTextBox).BaseType);
        AssertNames(typeof(RichTextBox).GetFields(DeclaredPublic), "IsDocumentEnabledProperty");
        AssertNames(typeof(RichTextBox).GetProperties(DeclaredPublic),
            "CaretPosition", "Document", "IsDocumentEnabled", "Selection");
        AssertMethodNames(typeof(RichTextBox),
            "GetNextSpellingErrorPosition", "GetPositionFromPoint", "GetSpellingError",
            "GetSpellingErrorRange", "ShouldSerializeDocument");
    }

    [Fact]
    public void PasswordBox_IsSealedAndDeclaresOnlyItsWpfSurface()
    {
        Assert.True(typeof(PasswordBox).IsSealed);
        Assert.Equal(typeof(Control), typeof(PasswordBox).BaseType);
        AssertNames(typeof(PasswordBox).GetFields(DeclaredPublic),
            "CaretBrushProperty", "IsInactiveSelectionHighlightEnabledProperty",
            "IsSelectionActiveProperty", "MaxLengthProperty", "PasswordChangedEvent",
            "PasswordCharProperty", "SelectionBrushProperty", "SelectionOpacityProperty",
            "SelectionTextBrushProperty");
        AssertNames(typeof(PasswordBox).GetProperties(DeclaredPublic),
            "CaretBrush", "IsInactiveSelectionHighlightEnabled", "IsSelectionActive", "MaxLength",
            "Password", "PasswordChar", "SecurePassword", "SelectionBrush", "SelectionOpacity",
            "SelectionTextBrush");
        AssertMethodNames(typeof(PasswordBox), "Clear", "OnApplyTemplate", "Paste", "SelectAll");
    }

    [Fact]
    public void PlatformTextHelpers_AreNotExported()
    {
        string[] forbidden =
        [
            "Jalium.UI.IImeSupport",
            "Jalium.UI.ImeSurroundingTextSnapshot",
            "Jalium.UI.Controls.SpellChecker",
            "Jalium.UI.Controls.PasswordRevealMode",
            "Jalium.UI.Controls.FormattedRegion",
            "Jalium.UI.Controls.FormattedRegionType",
            "Jalium.UI.Controls.FormattingResult",
        ];
        HashSet<string> exports = typeof(TextBox).Assembly.GetExportedTypes()
            .Select(type => type.FullName!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (string fullName in forbidden)
            Assert.DoesNotContain(fullName, exports);
    }

    private static void AssertMethodNames(Type type, params string[] expected)
    {
        string[] actual = type.GetMethods(DeclaredPublic)
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expected.Order(StringComparer.Ordinal), actual);
    }

    private static void AssertNames<TMember>(IEnumerable<TMember> members, params string[] expected)
        where TMember : MemberInfo
    {
        string[] actual = members.Select(member => member.Name).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expected.Order(StringComparer.Ordinal), actual);
    }
}

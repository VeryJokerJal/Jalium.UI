using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Documents;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public class TextControlsRemainingParityTests
{
    [Fact]
    public void FontFamily_UsesOneCanonicalFontFamilyDependencyPropertyAcrossTextOwners()
    {
        Assert.Same(TextElement.FontFamilyProperty, Control.FontFamilyProperty);
        Assert.Same(TextElement.FontFamilyProperty, TextBlock.FontFamilyProperty);
        Assert.Equal(typeof(FontFamily), TextElement.FontFamilyProperty.PropertyType);
        Assert.Equal(typeof(FontFamily), typeof(TextElement).GetProperty(nameof(TextElement.FontFamily))!.PropertyType);
        Assert.Equal(typeof(FontFamily), typeof(Control).GetProperty(nameof(Control.FontFamily))!.PropertyType);
        Assert.Equal(typeof(FontFamily), typeof(TextBlock).GetProperty(nameof(TextBlock.FontFamily))!.PropertyType);
        Assert.Same(SystemFonts.MessageFontFamily, TextElement.FontFamilyProperty.DefaultMetadata.DefaultValue);
        Assert.IsType<FontFamily>(TextElement.FontFamilyProperty.GetMetadata(typeof(Control)).DefaultValue);
        Assert.IsType<FontFamily>(TextElement.FontFamilyProperty.GetMetadata(typeof(TextBlock)).DefaultValue);
        Assert.False(TextElement.FontFamilyProperty.IsValidValue(null));
        Assert.False(TextElement.FontFamilyProperty.IsValidValue("Segoe UI"));

        var target = new Border();
        var family = new FontFamily("Shared Family");
        TextElement.SetFontFamily(target, family);
        Assert.Same(family, TextBlock.GetFontFamily(target));

        var converted = Assert.IsType<FontFamily>(
            TypeConverterRegistry.ConvertValue("Test Family", typeof(FontFamily)));
        Assert.Equal("Test Family", converted.Source);
    }

    [Fact]
    public void PasswordBox_SelectionSurface_TracksFocusAndOwnsWpfDependencyProperties()
    {
        var box = new PasswordBox();
        Assert.Equal(0.4, box.SelectionOpacity);
        Assert.False(box.IsSelectionActive);
        Assert.NotNull(box.SelectionTextBrush);

        var updateFocus = typeof(UIElement).GetMethod(
            "UpdateIsKeyboardFocused", BindingFlags.Instance | BindingFlags.NonPublic)!;
        updateFocus.Invoke(box, [true]);
        Assert.True(box.IsSelectionActive);
        updateFocus.Invoke(box, [false]);
        Assert.False(box.IsSelectionActive);

        Assert.True(PasswordBox.IsSelectionActiveProperty.ReadOnly);
        Assert.Same(TextBoxBase.SelectionBrushProperty, PasswordBox.SelectionBrushProperty);
        Assert.Same(TextBoxBase.SelectionTextBrushProperty, PasswordBox.SelectionTextBrushProperty);
        Assert.Same(TextBoxBase.SelectionOpacityProperty, PasswordBox.SelectionOpacityProperty);
        Assert.Same(TextBoxBase.CaretBrushProperty, PasswordBox.CaretBrushProperty);
        Assert.Same(TextBoxBase.IsInactiveSelectionHighlightEnabledProperty, PasswordBox.IsInactiveSelectionHighlightEnabledProperty);
        Assert.Same(TextBoxBase.IsSelectionActiveProperty, PasswordBox.IsSelectionActiveProperty);
        AssertFields<PasswordBox>(
            nameof(PasswordBox.IsInactiveSelectionHighlightEnabledProperty),
            nameof(PasswordBox.IsSelectionActiveProperty),
            nameof(PasswordBox.SelectionOpacityProperty),
            nameof(PasswordBox.SelectionTextBrushProperty));
    }

    [Fact]
    public void TextBox_PointLineRectSpellingAndMarkupApis_UseLiveEditorState()
    {
        var box = new TextBox
        {
            Text = "alpha\nbeta",
            AcceptsReturn = true,
            Width = 240,
            Height = 80,
            TextDecorations = TextDecorations.Underline,
        };
        box.Measure(new Size(240, 80));
        box.Arrange(new Rect(0, 0, 240, 80));

        Assert.Equal(0, box.GetFirstVisibleLineIndex());
        Assert.True(box.GetLastVisibleLineIndex() >= box.GetFirstVisibleLineIndex());
        Assert.InRange(box.GetCharacterIndexFromPoint(new Point(8, 8), true), 0, box.Text.Length);
        Assert.Equal(-1, box.GetCharacterIndexFromPoint(new Point(230, 8), false));
        Assert.InRange(box.GetCharacterIndexFromPoint(new Point(230, 8), true), 0, box.Text.Length);
        Assert.False(box.GetRectFromCharacterIndex(2).IsEmpty);
        Assert.NotNull(box.Typography);

        var error = new SpellingError(0, 5, "alpha", SpellingErrorType.GetSuggestions, null, []);
        typeof(TextBox).GetField("_spellingErrors", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(box, new List<SpellingError> { error });
        Assert.Same(error, box.GetSpellingError(2));
        Assert.Equal(0, box.GetSpellingErrorStart(2));
        Assert.Equal(5, box.GetSpellingErrorLength(2));
        Assert.Equal(0, box.GetNextSpellingErrorCharacterIndex(0, LogicalDirection.Forward));

        ((IAddChild)box).AddText("!");
        Assert.EndsWith("!", box.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void TextBox_LastVisibleLineExcludesTheRowStartingAtTheViewportBottom()
    {
        var box = new TextBox
        {
            Text = "one\ntwo",
            AcceptsReturn = true,
            Width = 200,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0)
        };
        box.Measure(new Size(200, 200));
        box.Arrange(new Rect(0, 0, 200, 200));
        var lineHeight = box.GetRectFromCharacterIndex(0).Height;
        box.Height = lineHeight;
        box.Measure(new Size(200, lineHeight));
        box.Arrange(new Rect(0, 0, 200, lineHeight));

        Assert.Equal(0, box.GetLastVisibleLineIndex());
    }

    [Fact]
    public void TextBox_TextDecorationsParticipateInRendering()
    {
        var box = new TextBox
        {
            Text = "decorated",
            TextDecorations = new TextDecorationCollection(TextDecorations.Underline),
            Width = 200,
            Height = 40
        };
        box.Measure(new Size(200, 40));
        box.Arrange(new Rect(0, 0, 200, 40));
        var drawingContext = new TextEffects.RecordingDrawingContext();

        box.Render(drawingContext);

        Assert.Contains("DrawLine", drawingContext.Events);
    }

    [Fact]
    public void TextBlock_ContentHostSurface_UsesCanonicalFontAndStableTextPointers()
    {
        var block = new TextBlock
        {
            Text = "hello",
            FontFamily = new FontFamily("Test Family"),
            Width = 180,
            Height = 40,
        };
        block.Measure(new Size(180, 40));
        block.Arrange(new Rect(0, 0, 180, 40));

        Assert.Equal("Test Family", block.FontFamily.Source);
        Assert.Same(block.ContentStart.Document, block.ContentEnd.Document);
        Assert.Equal(0, block.ContentStart.DocumentOffset);
        Assert.True(block.ContentEnd.DocumentOffset >= 5);
        Assert.NotNull(block.GetPositionFromPoint(new Point(4, 4), true));

        var host = Assert.IsAssignableFrom<IContentHost>(block);
        Assert.Same(block, host.InputHitTest(new Point(4, 4)));
        Assert.Empty(host.GetRectangles(new ContentElement()));

        Assert.Equal(typeof(FontFamily), typeof(TextBlock).GetProperty(nameof(TextBlock.FontFamily))!.PropertyType);
        Assert.Equal(typeof(TextPointer), typeof(TextBlock).GetProperty(nameof(TextBlock.ContentStart))!.PropertyType);
        Assert.Equal(typeof(TextPointer), typeof(TextBlock).GetProperty(nameof(TextBlock.ContentEnd))!.PropertyType);
    }

    [Fact]
    public void RichTextBox_SelectionPositionSpellingAndDocumentSurface_AreFunctional()
    {
        var box = new RichTextBox { IsDocumentEnabled = true, Width = 240, Height = 100 };
        box.SetPlainText("alpha beta");
        box.Measure(new Size(240, 100));
        box.Arrange(new Rect(0, 0, 240, 100));

        Assert.IsType<TextSelection>(box.Selection);
        Assert.True(typeof(TextSelection).IsSealed);
        Assert.True(box.ShouldSerializeDocument());
        Assert.NotNull(box.GetPositionFromPoint(new Point(8, 8), true));
        Assert.Null(box.GetPositionFromPoint(new Point(230, 8), false));
        Assert.NotNull(box.GetPositionFromPoint(new Point(230, 8), true));

        var error = new SpellingError(0, 5, "alpha", SpellingErrorType.GetSuggestions, null, []);
        typeof(RichTextBox).GetField("_spellingErrors", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(box, new List<SpellingError> { error });
        typeof(RichTextBox).GetField("_spellCheckedText", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(box, box.GetPlainText());

        Assert.Same(error, box.GetSpellingError(box.Document.ContentStart));
        Assert.Equal("alpha", box.GetSpellingErrorRange(box.Document.ContentStart)!.Text);
        Assert.Equal(0, box.GetNextSpellingErrorPosition(
            box.Document.ContentStart, LogicalDirection.Forward)!.DocumentOffset);
        TextRange rangeView = box.Selection;
        rangeView.Select(box.Document.ContentEnd, box.Document.ContentStart);
        Assert.Equal(box.Document.ContentEnd.DocumentOffset, box.Selection.AnchorPosition.DocumentOffset);
        Assert.Equal(box.Document.ContentStart.DocumentOffset, box.Selection.MovingPosition.DocumentOffset);
        AssertFields<RichTextBox>(nameof(RichTextBox.IsDocumentEnabledProperty));
    }

    [Fact]
    public void RichTextBox_IsDocumentEnabledControlsEmbeddedElementsAndRestoresTheirState()
    {
        var enabledChild = new Button { IsEnabled = true };
        var disabledChild = new Button { IsEnabled = false };
        var document = new FlowDocument();
        document.Blocks.Add(new BlockUIContainer { Child = enabledChild });
        document.Blocks.Add(new BlockUIContainer { Child = disabledChild });

        var box = new RichTextBox(document);

        Assert.False(enabledChild.IsEnabled);
        Assert.False(disabledChild.IsEnabled);

        box.IsDocumentEnabled = true;

        Assert.True(enabledChild.IsEnabled);
        Assert.False(disabledChild.IsEnabled);
    }

    private static void AssertFields<T>(params string[] names)
    {
        foreach (var name in names)
        {
            Assert.Equal(typeof(DependencyProperty),
                typeof(T).GetField(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)?.FieldType);
        }
    }
}

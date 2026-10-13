using System.Collections;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSShapedWordNavigationTests : MacOSGeometryTestBase
{
    private const string Mixed = "abc אבגדה 123 xyz العربية 中文 words tail";
    private static readonly MethodInfo Event = typeof(Window).GetMethod("OnPlatformEvent", BindingFlags.Instance | BindingFlags.NonPublic)!;

    public static IEnumerable<object[]> WrapCases()
    {
        foreach (bool rich in new[] { false, true })
        foreach (bool shift in new[] { false, true })
        foreach (int width in new[] { 80, 130, 240 }) yield return [rich, shift, width];
    }

    [Theory]
    [MemberData(nameof(WrapCases))]
    public void OptionLeft_UsesCurrentVisualLine(bool rich, bool shift, int width)
    {
        Keyboard.Initialize(); Keyboard.ClearFocus();
        using var context = new RenderContext(RenderBackend.Metal);
        Control box = rich ? Rich() : new TextBox { Text = Mixed, TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Helvetica"), FontSize = 20, AcceptsReturn = true };
        box.Padding = new Thickness(0); box.BorderThickness = new Thickness(0);
        var window = new DisplayedTestWindow { Content = box };
        try
        {
            Arrange(box, width);
            var ime = (IImeSupport)box; Assert.True(ime.TrySetImeSelection(5, 0)); Assert.True(box.Focus());
            Dispatch(window, false, shift);
            Assert.True(ime.TryGetImeSurroundingText(out var selection));
            // Captured from an own-process same-font NSTextView. With the RTL
            // word on a separate row, Option-Left stops at 4 instead of 3.
            int expected = width < 240 ? 4 : 3;
            Assert.Equal(expected, selection.CursorIndex);
            Assert.Equal(shift ? 5 : expected, selection.AnchorIndex);
            Assert.Equal(0, ((Jalium.UI.Controls.Primitives.TextBoxBase)box).HorizontalOffset);
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    public static IEnumerable<object[]> DirectionCases()
    {
        foreach (bool documentDirection in new[] { false, true })
        foreach (bool shift in new[] { false, true })
        foreach (bool right in new[] { false, true }) yield return [documentDirection, shift, right];
    }

    [Theory]
    [MemberData(nameof(DirectionCases))]
    public void ExplicitParagraphDirection_ControlsWordMovement(bool documentDirection, bool shift, bool right)
    {
        Keyboard.Initialize(); Keyboard.ClearFocus();
        using var context = new RenderContext(RenderBackend.Metal);
        var box = Rich(); var paragraph = (Paragraph)box.Document.Blocks[0];
        if (documentDirection) box.Document.FlowDirection = FlowDirection.RightToLeft;
        else paragraph.FlowDirection = FlowDirection.RightToLeft;
        var window = new DisplayedTestWindow { Content = box };
        try
        {
            Arrange(box, 130);
            var ime = (IImeSupport)box; Assert.True(ime.TrySetImeSelection(5, 0)); Assert.True(box.Focus());
            Dispatch(window, right, shift);
            Assert.True(ime.TryGetImeSurroundingText(out var selection));
            Assert.Equal(right ? 4 : 9, selection.CursorIndex);
            Assert.Equal(shift ? 5 : selection.CursorIndex, selection.AnchorIndex);
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Resize_RebuildsWordLayout(bool rich)
    {
        Keyboard.Initialize(); Keyboard.ClearFocus();
        using var context = new RenderContext(RenderBackend.Metal);
        Control box = rich ? Rich() : new TextBox { Text = Mixed, TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Helvetica"), FontSize = 20, AcceptsReturn = true };
        box.Padding = new Thickness(0); box.BorderThickness = new Thickness(0);
        var window = new DisplayedTestWindow { Content = box };
        try
        {
            Assert.True(box.Focus()); var ime = (IImeSupport)box;
            foreach (int width in new[] { 130, 240, 80 })
            {
                Arrange(box, width); Assert.True(ime.TrySetImeSelection(5, 0)); Dispatch(window, false, false);
                Assert.True(ime.TryGetImeSurroundingText(out var selection));
                Assert.Equal(width < 240 ? 4 : 3, selection.CursorIndex);
            }
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    [Fact]
    public void DirectionMutation_InvalidatesParagraphAndPreservesNaturalDefault()
    {
        Keyboard.Initialize(); Keyboard.ClearFocus();
        using var context = new RenderContext(RenderBackend.Metal);
        var box = Rich("שלום עולם abc def"); var paragraph = (Paragraph)box.Document.Blocks[0];
        var window = new DisplayedTestWindow { Content = box };
        try
        {
            Arrange(box, 240); Assert.True(box.Focus()); var ime = (IImeSupport)box;
            Assert.True(ime.TrySetImeSelection(0, 0)); Dispatch(window, false, false);
            Assert.Equal(4, box.CaretPosition!.DocumentOffset); // natural RTL
            var old = NativeParagraph(box);
            paragraph.FlowDirection = FlowDirection.LeftToRight;
            Assert.True(ime.TrySetImeSelection(0, 0)); Dispatch(window, true, false);
            Assert.Equal(4, box.CaretPosition.DocumentOffset); // explicitly LTR
            Assert.True(old.IsDisposed); Assert.NotSame(old, NativeParagraph(box));
            paragraph.ClearValue(Block.FlowDirectionProperty);
            Assert.True(ime.TrySetImeSelection(0, 0)); Dispatch(window, false, false);
            Assert.Equal(4, box.CaretPosition.DocumentOffset);
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void StyledDocument_UsesParagraphFontsAndWholeDocumentOffsets(bool shift)
    {
        Keyboard.Initialize(); Keyboard.ClearFocus();
        using var context = new RenderContext(RenderBackend.Metal);
        var doc = new FlowDocument { FontFamily = "Helvetica", FontSize = 20 };
        var first = new Paragraph(new Run("prefix")); first.Margin = new Thickness(0);
        var second = new Paragraph { Margin = new Thickness(0), FlowDirection = FlowDirection.RightToLeft };
        second.Inlines.Add(new Run(Mixed[..2]) { FontSize = 20 });
        second.Inlines.Add(new Run(Mixed[2..14]) { FontFamily = new FontFamily("Helvetica-Bold"), FontSize = 40 });
        second.Inlines.Add(new Run(Mixed[14..]) { FontSize = 20 });
        doc.Blocks.Add(first); doc.Blocks.Add(second);
        var box = new RichTextBox(doc) { Padding = new Thickness(0), BorderThickness = new Thickness(0) };
        var window = new DisplayedTestWindow { Content = box };
        try
        {
            Arrange(box, 130); Assert.True(box.Focus()); var ime = (IImeSupport)box;
            Assert.True(ime.TrySetImeSelection(12, 0)); Dispatch(window, true, shift);
            Assert.True(ime.TryGetImeSurroundingText(out var selection));
            Assert.Equal(11, selection.CursorIndex); Assert.Equal(shift ? 12 : 11, selection.AnchorIndex);
            Assert.True(ime.TryGetImeTextRangeGeometry(7, 8, false, out var geometry));
            Assert.True(geometry.Rectangle.Height > 20);
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void EditControl_WordMovementRetainsShapedCaretAndResetsOnExplicitSelection(bool shift)
    {
        Keyboard.Initialize(); Keyboard.ClearFocus();
        using var context = new RenderContext(RenderBackend.Metal);
        var box = new EditControl { Text = "שלום עולם", FontFamily = new FontFamily("Helvetica"), FontSize = 20, ShowLineNumbers = false };
        var window = new DisplayedTestWindow { Content = box };
        try
        {
            Arrange(box, 240); Assert.True(box.Focus()); var ime = (IImeSupport)box;
            Assert.True(ime.TrySetImeSelection(0, 0)); Dispatch(window, false, shift);
            Assert.True(ime.TryGetImeSurroundingText(out var selection));
            Assert.Equal(4, selection.CursorIndex); Assert.Equal(shift ? 0 : 4, selection.AnchorIndex);
            Assert.True(TextMeasurement.HitTestTextPositionWrapped(box.Text, "Helvetica", 20, 400, 0, 100000, 4, false, out var hit));
            Assert.Equal(hit.CaretX, ime.GetImeCaretRectangle().X, 2);
            Assert.True(ime.TrySetImeSelection(0, 0));
            Assert.True(TextMeasurement.HitTestTextPositionWrapped(box.Text, "Helvetica", 20, 400, 0, 100000, 0, false, out hit));
            Assert.Equal(hit.CaretX, ime.GetImeCaretRectangle().X, 2);
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    private static RichTextBox Rich(string text = Mixed)
    {
        var doc = new FlowDocument { FontFamily = "Helvetica", FontSize = 20 };
        doc.Blocks.Add(new Paragraph(new Run(text)) { Margin = new Thickness(0) });
        return new RichTextBox(doc) { Padding = new Thickness(0), BorderThickness = new Thickness(0) };
    }
    private static void Arrange(Control box, int width) { box.Measure(new Size(width, 700)); box.Arrange(new Rect(0, 0, width, 700)); }
    private static void Dispatch(Window window, bool right, bool shift) => Event.Invoke(window,
        [new PlatformEvent { Type = PlatformEventType.KeyDown, KeyCode = right ? 0x27 : 0x25, Modifiers = 4 | (shift ? 1 : 0) }]);
    private static NativeTextParagraph NativeParagraph(RichTextBox box)
    {
        object layout = typeof(RichTextBox).GetMethod("EnsureLayout", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(box, [box.RenderSize.Width])!;
        var blocks = (IList)layout.GetType().GetProperty("Blocks")!.GetValue(layout)!;
        return (NativeTextParagraph)blocks[0]!.GetType().GetProperty("NativeParagraph")!.GetValue(blocks[0])!;
    }
}

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
public sealed class MacOSNoWrapAndInheritedDirectionTests : MacOSGeometryTestBase
{
    private static readonly string LongLine = string.Concat(Enumerable.Repeat("MMMM ", 3000)) + "abc אבגדה 123 xyz tail";
    private static readonly MethodInfo Event = typeof(Window).GetMethod("OnPlatformEvent", BindingFlags.Instance | BindingFlags.NonPublic)!;

    [Theory]
    [InlineData(double.PositiveInfinity)] [InlineData(double.MaxValue)] [InlineData(0)]
    public void UnboundedMeasurement_KeepsWholeLongLineAndDoesNotPolluteWrapCache(double width)
    {
        using var context = new RenderContext(RenderBackend.Metal);
        var text = new FormattedText(LongLine, "Helvetica", 20) { MaxTextWidth = width, MaxTextHeight = double.MaxValue };
        Assert.True(TextMeasurement.MeasureText(text)); Assert.Equal(1, text.LineCount);
        Assert.InRange(text.Width, 216792, 216794); // Same-font NSTextView: 216792.168 DIP.
        var wrapped = new FormattedText(LongLine, "Helvetica", 20) { MaxTextWidth = 130 };
        Assert.True(TextMeasurement.MeasureText(wrapped)); Assert.True(wrapped.LineCount > 1000);
        TextMeasurement.ClearCache();
        Assert.True(TextMeasurement.MeasureText(text)); Assert.Equal(1, text.LineCount);
        Assert.True(TextMeasurement.MeasureText(wrapped)); Assert.True(wrapped.LineCount > 1000);
    }

    [Theory]
    [InlineData(400)] [InlineData(700)]
    public void UnboundedHitTests_AgreeWithWholeLineFontLayout(int weight)
    {
        using var context = new RenderContext(RenderBackend.Metal);
        using var format = context.CreateTextFormat("Helvetica", 20, weight, 0);
        NativeMethods.TextFormatSetWordWrapping(format.Handle, 1);
        Assert.True(format.HitTestTextPosition(LongLine, 130, 1000, (uint)LongLine.Length, false, out var reference));
        Assert.True(TextMeasurement.HitTestTextPositionWrapped(LongLine, "Helvetica", 20, weight, 0,
            float.PositiveInfinity, (uint)LongLine.Length, false, out var caret));
        Assert.Equal(reference.CaretX, caret.CaretX); Assert.Equal(0, caret.CaretY);
        Assert.True(TextMeasurement.TryGetVisualLineMetrics(LongLine, "Helvetica", 20, weight, 0,
            float.PositiveInfinity, (uint)LongLine.Length, false, out var line));
        Assert.Equal(0u, line.TextPosition); Assert.Equal((uint)LongLine.Length, line.Length); Assert.Equal(0, line.Y);
        Assert.True(TextMeasurement.HitTestTextRangeWrapped(LongLine, "Helvetica", 20, weight, 0,
            float.PositiveInfinity, (uint)(LongLine.Length - 4), 4, out var range));
        Assert.Equal(0, range.Y); Assert.True(range.X > 100000);
        Assert.True(TextMeasurement.HitTestPointWrapped(LongLine, "Helvetica", 20, weight, 0,
            float.PositiveInfinity, caret.CaretX, 0, out var point));
        Assert.Equal((uint)LongLine.Length, point.IsTrailingHit != 0 ? (uint)GraphemeClusters.NextBoundary(LongLine, (int)point.TextPosition) : point.TextPosition);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CommandRight_ReachesNoWrapLineEndAndKeepsSelectionAnchor(bool shift)
    {
        Keyboard.Initialize(); Keyboard.ClearFocus(); using var context = new RenderContext(RenderBackend.Metal);
        var box = new TextBox { Text = LongLine, TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Helvetica"), FontSize = 20, Padding = new Thickness(0), BorderThickness = new Thickness(0) };
        var window = new DisplayedTestWindow { Content = box };
        try
        {
            Arrange(box); box.Select(1, 0); Assert.True(box.Focus());
            Dispatch(window, 0x27, 8 | (shift ? 1 : 0));
            Assert.True(((IImeSupport)box).TryGetImeSurroundingText(out var selection));
            Assert.Equal(LongLine.Length, selection.CursorIndex); Assert.Equal(shift ? 1 : LongLine.Length, selection.AnchorIndex);
            Assert.True(box.HorizontalOffset > 100000);
            Assert.Equal(0, ((IImeSupport)box).GetImeCaretRectangle().Y);
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CodeEditor_LongLineCaretUsesActualFontAndWholeLine(bool bold)
    {
        Keyboard.Initialize(); Keyboard.ClearFocus(); using var context = new RenderContext(RenderBackend.Metal);
        var box = new EditControl { Text = LongLine, FontFamily = new FontFamily("Helvetica"), FontSize = 20,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, ShowLineNumbers = false };
        var window = new DisplayedTestWindow { Content = box };
        try
        {
            Arrange(box); Assert.True(box.Focus()); Assert.True(((IImeSupport)box).TrySetImeSelection(LongLine.Length, 0));
            var view = typeof(EditControl).GetField("_view", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(box)!;
            var getPoint = view.GetType().GetMethod("GetPointFromOffset")!;
            var first = (Point)getPoint.Invoke(view, [0, false, false])!;
            var end = (Point)getPoint.Invoke(view, [LongLine.Length, false, false])!;
            using var format = context.CreateTextFormat("Helvetica", 20, bold ? 700 : 400, 0);
            NativeMethods.TextFormatSetWordWrapping(format.Handle, 1);
            Assert.True(format.HitTestTextPosition(LongLine, 130, 1000, (uint)LongLine.Length, false, out var reference));
            Assert.Equal(reference.CaretX, end.X - first.X, 2); Assert.Equal(first.Y, end.Y);
            Assert.True(box.HorizontalOffsetForTesting > 100000);
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    [Fact]
    public void NoWrap_PreservesHardNewlineAndWholeEmojiCaret()
    {
        using var context = new RenderContext(RenderBackend.Metal);
        string text = LongLine + "\r\n👨‍👩‍👧‍👦 tail";
        Assert.True(TextMeasurement.TryGetVisualLineMetrics(text, "Helvetica", 20, 400, 0,
            float.PositiveInfinity, (uint)(LongLine.Length + 2), false, out var second));
        Assert.Equal((uint)(LongLine.Length + 2), second.TextPosition); Assert.True(second.Y > 0);
        Assert.True(TextMeasurement.HitTestTextRangeWrapped(text, "Helvetica", 20, 400, 0,
            float.PositiveInfinity, (uint)(LongLine.Length + 3), 1, out var family));
        Assert.Equal((uint)(LongLine.Length + 2), family.TextPosition); Assert.Equal(11u, family.Length);
        Assert.Equal(second.Y, family.Y);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void AncestorDeclaredDirection_OverridesNaturalAndMutationsInvalidate(bool panel, bool rtl)
    {
        Keyboard.Initialize(); Keyboard.ClearFocus(); using var context = new RenderContext(RenderBackend.Metal);
        var doc = new FlowDocument { FontFamily = "Helvetica", FontSize = 20 };
        doc.Blocks.Add(new Paragraph(new Run("שלום עולם abc def")) { Margin = new Thickness(0) });
        var box = new RichTextBox(doc) { Padding = new Thickness(0), BorderThickness = new Thickness(0) };
        var stack = new StackPanel(); stack.Children.Add(box);
        var window = new DisplayedTestWindow { Content = stack };
        FrameworkElement ancestor = panel ? stack : window;
        try
        {
            ancestor.FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            Arrange(box); Assert.True(box.Focus()); var ime = (IImeSupport)box;
            Assert.True(ime.TrySetImeSelection(0, 0)); Dispatch(window, rtl ? 0x25 : 0x27, 4);
            Assert.Equal(4, box.CaretPosition!.DocumentOffset);
            var old = NativeParagraph(box);
            ancestor.FlowDirection = rtl ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;
            Assert.True(ime.TrySetImeSelection(0, 0)); Dispatch(window, rtl ? 0x27 : 0x25, 4);
            Assert.Equal(4, box.CaretPosition.DocumentOffset); Assert.True(old.IsDisposed);
            ancestor.ClearValue(FrameworkElement.FlowDirectionProperty);
            Assert.True(ime.TrySetImeSelection(0, 0)); Dispatch(window, 0x25, 4);
            Assert.Equal(4, box.CaretPosition.DocumentOffset); // Restore natural RTL.
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ClosestDeclaredDirection_WinsOverAncestor(bool paragraphOverride)
    {
        Keyboard.Initialize(); Keyboard.ClearFocus(); using var context = new RenderContext(RenderBackend.Metal);
        var paragraph = new Paragraph(new Run("שלום עולם abc def")) { Margin = new Thickness(0) };
        var doc = new FlowDocument { FontFamily = "Helvetica", FontSize = 20 }; doc.Blocks.Add(paragraph);
        if (paragraphOverride) paragraph.FlowDirection = FlowDirection.LeftToRight; else doc.FlowDirection = FlowDirection.LeftToRight;
        var box = new RichTextBox(doc) { Padding = new Thickness(0), BorderThickness = new Thickness(0) };
        var window = new DisplayedTestWindow { Content = box, FlowDirection = FlowDirection.RightToLeft };
        try
        {
            Arrange(box); Assert.True(box.Focus()); Assert.True(((IImeSupport)box).TrySetImeSelection(0, 0)); Dispatch(window, 0x27, 4);
            Assert.Equal(4, box.CaretPosition!.DocumentOffset);
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    private static void Arrange(Control box) { box.Measure(new Size(240, 80)); box.Arrange(new Rect(0, 0, 240, 80)); }
    private static void Dispatch(Window window, int key, int modifiers) => Event.Invoke(window,
        [new PlatformEvent { Type = PlatformEventType.KeyDown, KeyCode = key, Modifiers = modifiers }]);
    private static NativeTextParagraph NativeParagraph(RichTextBox box)
    {
        object layout = typeof(RichTextBox).GetMethod("EnsureLayout", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(box, [box.RenderSize.Width])!;
        var blocks = (IList)layout.GetType().GetProperty("Blocks")!.GetValue(layout)!;
        return (NativeTextParagraph)blocks[0]!.GetType().GetProperty("NativeParagraph")!.GetValue(blocks[0])!;
    }
}

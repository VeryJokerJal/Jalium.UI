using System.Runtime.InteropServices;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Automation.MacOS;
using Jalium.UI.Data;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed unsafe class MacOSTextAccessibilityNavigationTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void NativeNavigationUsesUtf16LinesAndWholeGraphemes(int type)
    {
        using var fixture = new Fixture(type, "A🙂e\u0301中\r\n尾\n");
        int secondStart = fixture.Text.IndexOf('尾');
        Assert.Equal(1, fixture.Navigate(MacOSAXTextNavigation.LineForIndex, secondStart).TextStart);
        var first = fixture.Navigate(MacOSAXTextNavigation.RangeForLine, 0);
        Assert.Equal((0, secondStart), (first.TextStart, first.TextLength));
        var emoji = fixture.Navigate(MacOSAXTextNavigation.RangeForIndex, 2);
        Assert.Equal((1, 2), (emoji.TextStart, emoji.TextLength));
        var combining = fixture.Navigate(MacOSAXTextNavigation.RangeForIndex, 4);
        Assert.Equal((3, 2), (combining.TextStart, combining.TextLength));
        Assert.False(fixture.TryNavigate(MacOSAXTextNavigation.RangeForLine, -1, out _));
        Assert.False(fixture.TryNavigate(MacOSAXTextNavigation.RangeForIndex, int.MaxValue, out _));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void VisibleRangesFollowRequestedScrollWithoutMovingSelection(int type)
    {
        using var fixture = new Fixture(type, string.Join('\n', Enumerable.Range(1, 80).Select(n => $"第{n}行🙂")));
        fixture.Select(1, 2);
        string selection = Assert.Single(fixture.Provider.GetSelection()).GetText(-1);
        var visible = fixture.Provider.GetVisibleRanges();
        Assert.NotEmpty(visible);
        Assert.True(visible.Sum(range => range.GetText(-1).Length) < fixture.Text.Length);
        var tail = fixture.Provider.DocumentRange.FindText("第80行", false, false)!;
        tail.ScrollIntoView(false);
        Assert.True(fixture.Provider.GetVisibleRanges().Any(range => range.GetText(-1).Contains("第80行", StringComparison.Ordinal)), fixture.Describe());
        Assert.Equal(selection, Assert.Single(fixture.Provider.GetSelection()).GetText(-1));
        Assert.Empty(fixture.Source.GetBoundingRectangles(0, 1));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void SelectionReplacementKeepsUndoAndRejectsReadOnlyOrDisabledOwners(int type)
    {
        using var fixture = new Fixture(type, "A🙂e\u0301中");
        string original = fixture.Text, expected = original[..1] + "中文🙂" + original[3..];
        fixture.Select(1, 2);
        Assert.True(fixture.Replace("中文🙂"));
        Assert.Equal(expected, fixture.Text);
        fixture.Undo(); Assert.Equal(original, fixture.Text);
        fixture.Redo(); Assert.Equal(expected, fixture.Text);
        fixture.ReadOnly = true;
        fixture.Select(1, 2); Assert.False(fixture.Replace("forbidden"));
        Assert.Equal("中文", Assert.Single(fixture.Provider.GetSelection()).GetText(-1));
        fixture.ReadOnly = false; fixture.Window.IsEnabled = false;
        Assert.False(fixture.Replace("disabled"));
        Assert.False(fixture.TryNavigate(MacOSAXTextNavigation.SetInsertionLine, 0, out _));
        Assert.Equal(expected, fixture.Text);
    }

    [Fact]
    public void SelectedTextReplacementPreservesTextBinding()
    {
        using var fixture = new Fixture(0, "original");
        var source = new TextBox { Text = "original" };
        var binding = new Binding("Text") { Source = source };
        fixture.Editor.SetBinding(TextBox.TextProperty, binding);
        fixture.Select(0, 8); Assert.True(fixture.Replace("中文🙂"));
        Assert.Same(binding, BindingOperations.GetBindingBase(fixture.Editor, TextBox.TextProperty));
        fixture.Undo(); Assert.Equal("original", fixture.Text);
        source.Text = "updated"; Assert.Equal("updated", fixture.Text);
    }

    [Fact]
    public void FoldedTextKeepsDisjointVisibleRanges()
    {
        using var fixture = new Fixture(2, "header {\n hidden🙂\n}\nlast");
        var editor = (EditControl)fixture.Editor;
        Assert.True(editor.ToggleFold(1));
        string visible = string.Concat(fixture.Provider.GetVisibleRanges().Select(range => range.GetText(-1)));
        Assert.Contains("header", visible); Assert.True(visible.Contains("last", StringComparison.Ordinal), fixture.Describe()); Assert.DoesNotContain("hidden", visible);
        Assert.True(editor.ToggleFold(1));
        Assert.Contains(fixture.Provider.GetVisibleRanges(), range => range.GetText(-1).Contains("hidden", StringComparison.Ordinal));
    }

    [Fact]
    public void InvalidScreenPointsAndSingularTransformsCannotInventTextRanges()
    {
        using var fixture = new Fixture(0, "中文🙂");
        Assert.Throws<ArgumentException>(() => fixture.Provider.RangeFromPoint(new Point(double.NaN, 0)));
        fixture.Editor.RenderTransform = new ScaleTransform(0, 1);
        Assert.Null(fixture.Provider.RangeFromPoint(new Point(10, 10)));
        Assert.False(fixture.TryNavigate(MacOSAXTextNavigation.RangeForPosition, 0, out _, new Point(10, 10)));
    }

    [Fact]
    public void DefunctTextCannotNavigateOrReplaceSelection()
    {
        using var fixture = new Fixture(0, "original");
        fixture.Window.Content = null;
        Assert.False(fixture.TryNavigate(MacOSAXTextNavigation.RangeForIndex, 0, out _));
        Assert.False(fixture.Replace("removed"));
        Assert.Equal("original", fixture.Text);
    }

    [Fact]
    public void TextNavigationAppendsToTheUnchangedNativeAbi()
    {
        Assert.Equal(136, Marshal.SizeOf<MacOSAXRequest>());
        Assert.Equal(11, (int)MacOSAXOperation.WindowButton); Assert.Equal(12, (int)MacOSAXOperation.TextNavigation);
        Assert.Equal(1u << 21, (uint)MacOSAXFlags.NavigableText); Assert.Equal(1u << 22, (uint)MacOSAXFlags.EditableText);
        Assert.Equal(1u << 23, (uint)MacOSAXFlags.Offscreen);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void AncestorClipLimitsTextFramesAndVisibleRangesWithoutEditing(int type)
    {
        using var fixture = new Fixture(type, "中文🙂 first\nsecond line");
        fixture.Window.Content = null;
        var parent = new Border { Child = fixture.Editor };
        fixture.Window.Content = parent; fixture.Layout();
        Rect glyph = Assert.Single(fixture.Source.GetBoundingRectangles(0, 1));
        Point origin = fixture.Editor.GetRenderMatrix().Transform(new Point(glyph.Left, glyph.Top));
        Rect clip = new(origin.X, origin.Y + glyph.Height / 2, glyph.Width / 2, glyph.Height / 2);
        parent.Clip = new RectangleGeometry(clip);
        Rect partial = Assert.Single(fixture.Source.GetBoundingRectangles(0, 1));
        Assert.Equal(glyph.Width / 2, partial.Width, 5); Assert.Equal(glyph.Height / 2, partial.Height, 5);
        Assert.Empty(fixture.Source.GetBoundingRectangles(fixture.Text.IndexOf("second", StringComparison.Ordinal), 1));
        Assert.DoesNotContain(fixture.Provider.GetVisibleRanges(), range => range.GetText(-1).Contains("second", StringComparison.Ordinal));
        parent.Clip = new RectangleGeometry(new Rect(1000, 1000, 10, 10));
        Assert.Empty(fixture.Provider.GetVisibleRanges()); Assert.Empty(fixture.Provider.DocumentRange.GetBoundingRectangles());
        Assert.True(fixture.Editor.GetAutomationPeer()!.IsOffscreen());
        parent.Clip = null;
        Assert.NotEmpty(fixture.Provider.GetVisibleRanges()); Assert.False(fixture.Editor.GetAutomationPeer()!.IsOffscreen());
        Assert.StartsWith("中文🙂 first", fixture.Text);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void HiddenAncestorsAndClipHolesCannotProduceVisibleTextOrHits(int type)
    {
        using var fixture = new Fixture(type, "中文🙂 first");
        fixture.Window.Content = null;
        var parent = new Border { Child = fixture.Editor };
        fixture.Window.Content = parent; fixture.Layout();
        Rect glyph = Assert.Single(fixture.Source.GetBoundingRectangles(0, 1));
        Point origin = fixture.Editor.GetRenderMatrix().Transform(new Point(glyph.Left, glyph.Top));
        var hole = new Rect(origin.X - 1, origin.Y - 1, glyph.Width + 2, glyph.Height + 2);
        var mask = new GeometryGroup { FillRule = FillRule.EvenOdd };
        mask.Children.Add(new RectangleGeometry(new Rect(0, 0, 360, 180))); mask.Children.Add(new RectangleGeometry(hole));
        parent.Clip = mask;
        Assert.Empty(fixture.Source.GetBoundingRectangles(0, 1));
        Assert.Null(fixture.Provider.RangeFromPoint(fixture.Editor.PointToScreen(new(glyph.Left + glyph.Width / 2, glyph.Top + glyph.Height / 2))));
        parent.Clip = null; parent.Visibility = Visibility.Hidden;
        Assert.Empty(fixture.Provider.GetVisibleRanges()); Assert.Empty(fixture.Source.GetBoundingRectangles(0, 1));
        Assert.True(fixture.Editor.GetAutomationPeer()!.IsOffscreen());
        parent.Visibility = Visibility.Visible;
        Assert.NotEmpty(fixture.Provider.GetVisibleRanges()); Assert.Equal(fixture.Text, fixture.Provider.DocumentRange.GetText(-1));
    }

    private sealed class Fixture : IDisposable
    {
        internal DisplayedTestWindow Window { get; }
        internal Control Editor { get; }
        private readonly MacOSAccessibilityTree _tree;
        private readonly ulong _id;
        internal ITextProvider Provider => (ITextProvider)Editor.GetAutomationPeer()!.GetPattern(Automation.Peers.PatternInterface.Text)!;
        internal IAutomationTextProviderSource Source => ((AutomationTextProvider)Provider).Source;
        internal string Text => Source.Text;
        internal string Describe() => $"text={Text}, viewport={((IAutomationTextViewSource)Source).TextViewport}, lines="
            + string.Join(";", ((IAutomationTextViewSource)Source).GetTextLines().Select(line => $"{line.Start}:{line.Length}@{line.Bounds}"))
            + ", visible=" + string.Join(";", AutomationTextNavigation.VisibleRanges(Source, (IAutomationTextViewSource)Source).Select(range => $"{range.Start}:{range.Length}"));
        internal bool ReadOnly { set { if (Editor is EditControl code) code.IsReadOnly = value; else ((Controls.Primitives.TextBoxBase)Editor).IsReadOnly = value; } }
        internal Fixture(int type, string text)
        {
            Editor = type switch { 0 => new TextBox { Text = text, AcceptsReturn = true },
                1 => new RichTextBox(), _ => new EditControl { Text = text, IsScrollInertiaEnabled = false } };
            if (Editor is RichTextBox rich) rich.SetPlainText(text);
            Editor.Height = 90;
            Window = new DisplayedTestWindow { TitleBarStyle = WindowTitleBarStyle.Native, Width = 360, Height = 180, Content = Editor };
            Window.Measure(new Size(360, 180)); Window.Arrange(new Rect(0, 0, 360, 180));
            _tree = new(Window);
            var request = new MacOSAXRequest { NodeId = 1, Operation = MacOSAXOperation.Child, Index = 0 };
            Assert.True(_tree.Handle(ref request)); _id = request.ResultId;
        }
        internal void Layout() { Window.Measure(new Size(360, 180)); Window.Arrange(new Rect(0, 0, 360, 180)); }
        internal MacOSAXRequest Navigate(MacOSAXTextNavigation operation, int index)
        { Assert.True(TryNavigate(operation, index, out var request)); return request; }
        internal bool TryNavigate(MacOSAXTextNavigation operation, int index, out MacOSAXRequest request, Point point = default)
        { request = new() { NodeId = _id, Operation = MacOSAXOperation.TextNavigation, Index = (int)operation, TextStart = index, X = point.X, Y = point.Y }; return _tree.Handle(ref request); }
        internal void Select(int start, int length)
        { var request = new MacOSAXRequest { NodeId = _id, Operation = MacOSAXOperation.SetTextSelection, TextStart = start, TextLength = length }; Assert.True(_tree.Handle(ref request)); }
        internal bool Replace(string text)
        {
            fixed (char* characters = text)
            { var request = new MacOSAXRequest { NodeId = _id, Operation = MacOSAXOperation.TextNavigation, Index = (int)MacOSAXTextNavigation.ReplaceSelection, Text = characters, TextCount = text.Length, TextCapacity = text.Length }; return _tree.Handle(ref request); }
        }
        internal void Undo() { if (Editor is EditControl code) code.Undo(); else ((Controls.Primitives.TextBoxBase)Editor).Undo(); }
        internal void Redo() { if (Editor is EditControl code) code.Redo(); else ((Controls.Primitives.TextBoxBase)Editor).Redo(); }
        public void Dispose() { Keyboard.Focus(null); Window.Close(); }
    }
}

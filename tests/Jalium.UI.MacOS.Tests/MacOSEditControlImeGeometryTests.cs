using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSEditControlImeGeometryTests : MacOSGeometryTestBase
{
    [Theory]
    [InlineData(12)]
    [InlineData(32)]
    public void FirstRangeQueryUsesArrangedLayoutBeforeAnyCaretQuery(double fontSize)
    {
        using var fixture = new Fixture("MMMM\nnext", fontSize);
        Assert.True(fixture.Support.TryGetImeTextRangeGeometry(0, 2, false, out var range));
        Rect caret = fixture.Support.GetImeCaretRectangle();
        Assert.Equal(caret.X, range.Rectangle.X, 3);
        Assert.Equal(caret.Y, range.Rectangle.Y, 3);
        Assert.Equal(caret.Height, range.Rectangle.Height, 3);
        Assert.True(range.Rectangle.Width > 0);
        Assert.Equal(0, fixture.Editor.SelectionStart);
        Assert.Equal(0, fixture.Editor.SelectionLength);
        Assert.Equal("MMMM\nnext", fixture.Editor.Text);
    }

    [Theory]
    [InlineData(12)]
    [InlineData(32)]
    public void FirstPointQueryUsesCurrentFontAndViewport(double fontSize)
    {
        using var fixture = new Fixture("MMMMMMMM", fontSize, false);
        var prefix = new FormattedText("MM", "Menlo", fontSize);
        TextMeasurement.MeasureText(prefix);
        Assert.True(prefix.Width > 0);
        Assert.True(fixture.Support.TryGetImeCharacterIndex(
            new Point(prefix.Width, fontSize / 2), false, out int index));
        Assert.Equal(2, index);
        Assert.Equal(0, fixture.Editor.SelectionStart);
        Assert.Equal(0, fixture.Editor.SelectionLength);
    }

    [Theory]
    [InlineData(2, 1, 1, 2)]
    [InlineData(4, 1, 3, 2)]
    public void FirstRangeQueryPreservesWholeGraphemes(int start, int length, int expectedStart, int expectedLength)
    {
        using var fixture = new Fixture("A😀e\u0301Z", 24);
        Assert.True(fixture.Support.TryGetImeTextRangeGeometry(start, length, false, out var range));
        Assert.Equal(expectedStart, range.Start);
        Assert.Equal(expectedLength, range.Length);
        Assert.True(range.Rectangle.Width > 0);
        Assert.True(range.Rectangle.Height > 0);
    }

    [Fact]
    public void FirstCompositionRangeUsesTheSameOriginAsTheCaret()
    {
        using var fixture = new Fixture("original", 32);
        fixture.Support.OnImeCompositionStart();
        fixture.Support.OnImeCompositionUpdate("M中", 0);
        try
        {
            Assert.True(fixture.Support.TryGetImeTextRangeGeometry(0, 1, true, out var range));
            Rect caret = fixture.Support.GetImeCaretRectangle();
            Assert.Equal(caret.X, range.Rectangle.X, 3);
            Assert.Equal(caret.Y, range.Rectangle.Y, 3);
            Assert.True(range.Rectangle.Width > 0);
            Assert.Equal("original", fixture.Editor.Text);
        }
        finally { fixture.Support.OnImeCompositionEnd(null); }
    }

    private sealed class Fixture : IDisposable
    {
        internal EditControl Editor { get; }
        internal IImeSupport Support => Editor;
        private readonly DisplayedTestWindow _window;

        internal Fixture(string text, double fontSize, bool showLineNumbers = true)
        {
            Editor = new EditControl { Text = text, FontFamily = new FontFamily("Menlo"),
                FontSize = fontSize, ShowLineNumbers = showLineNumbers, Height = 160 };
            _window = new DisplayedTestWindow { TitleBarStyle = WindowTitleBarStyle.Native,
                Width = 640, Height = 220, Content = Editor };
            _window.Measure(new Size(640, 220));
            _window.Arrange(new Rect(0, 0, 640, 220));
            Assert.Equal(nint.Zero, _window.Handle);
            Assert.True(Editor.ActualWidth > 0);
            Assert.Equal(160, Editor.ActualHeight);
        }

        public void Dispose() => _window.Close();
    }
}

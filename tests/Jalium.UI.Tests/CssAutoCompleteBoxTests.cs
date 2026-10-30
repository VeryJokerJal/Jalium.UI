using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssAutoCompleteBoxTests
{
    [Fact]
    public void DirectBackgroundLayers_UseInputContentBox()
    {
        var box = DirectBox();
        Css.SetStyle(box,
            "border:8px solid transparent;padding:4px;background-color:blue;" +
            "background-image:linear-gradient(to right,red,lime);" +
            "background-size:20px 10px;background-position:right bottom;" +
            "background-repeat:no-repeat;background-origin:content-box;" +
            "background-clip:content-box");
        CssBorderRadiusTests.Layout(box, 80, 60);

        var pixels = CssBorderRadiusTests.Pixels(box, 80, 60);
        Assert.Equal((byte)0, Channel(pixels, 10, 30, 3));
        Assert.Equal((byte)255, Channel(pixels, 30, 43, 0));
        Assert.True(Channel(pixels, 50, 43, 2) > 100);
        Assert.True(Channel(pixels, 65, 43, 1) > 100);
    }

    [Fact]
    public void DirectBorder_PaintsDistinctSideColors()
    {
        var box = DirectBox();
        Css.SetStyle(box, "border:8px solid; border-color:red green blue yellow");
        CssBorderRadiusTests.Layout(box, 80, 60);

        var pixels = CssBorderRadiusTests.Pixels(box, 80, 60);
        Assert.Equal((byte)255, Channel(pixels, 40, 4, 2));
        Assert.Equal((byte)128, Channel(pixels, 76, 30, 1));
        Assert.Equal((byte)255, Channel(pixels, 40, 56, 0));
        Assert.Equal((byte)255, Channel(pixels, 4, 30, 1));
    }

    [Fact]
    public void CssEdges_AlignTextAndImeCaret()
    {
        var box = DirectBox();
        box.Text = "A";
        Css.SetStyle(box, "border:8px solid transparent;padding:4px");
        CssBorderRadiusTests.Layout(box, 80, 60);

        var drawing = new TextOriginContext();
        box.Render(drawing);
        var value = Assert.Single(drawing.Texts, entry => entry.Text == "A");
        Assert.Equal(12, value.Origin.X);
        Assert.Equal(12, box.GetImeCaretPosition().X);
        Assert.InRange(Math.Abs(box.GetImeCaretRectangle().Y - value.Origin.Y), 0, 3);
    }

    [Fact]
    public void OpenDropDown_KeepsInputBorderAboveSuggestions()
    {
        var box = DirectBox();
        box.FilteredItems.Add("apple");
        box.IsDropDownOpen = true;
        Css.SetStyle(box, "border:8px solid red;background-color:blue");
        CssBorderRadiusTests.Layout(box, 80, 100);

        var pixels = CssBorderRadiusTests.Pixels(box, 80, 100);
        Assert.Equal((byte)255, Channel(pixels, 40, 68, 2));
        Assert.True(Channel(pixels, 40, 96, 2) < 200);
    }

    [Fact]
    public void OpenDropDown_PositionsBackgroundImageInInputContentBox()
    {
        var box = DirectBox();
        box.FilteredItems.Add("apple");
        box.IsDropDownOpen = true;
        Css.SetStyle(box,
            "border:8px solid transparent;padding:4px;background-color:blue;" +
            "background-image:linear-gradient(to right,red,lime);" +
            "background-size:20px 10px;background-position:right bottom;" +
            "background-repeat:no-repeat;background-origin:content-box;" +
            "background-clip:content-box");
        CssBorderRadiusTests.Layout(box, 80, 100);

        var pixels = CssBorderRadiusTests.Pixels(box, 80, 100);
        Assert.True(Channel(pixels, 50, 59, 2) > 100);
        Assert.True(Channel(pixels, 65, 59, 1) > 100);
    }

    [Fact]
    public void ConstrainedOpenDropDown_KeepsInputBoxVisible()
    {
        var box = DirectBox();
        box.FilteredItems.Add("apple");
        box.IsDropDownOpen = true;
        Css.SetStyle(box, "border:8px solid red;padding:4px");
        CssBorderRadiusTests.Layout(box, 80, 60);

        var pixels = CssBorderRadiusTests.Pixels(box, 80, 60);
        Assert.Equal((byte)255, Channel(pixels, 40, 52, 2));
    }

    [Fact]
    public void CssEdges_MoveSuggestionHitRegionWithInputBox()
    {
        var box = new ProbeAutoCompleteBox
        {
            Template = null,
            PlaceholderText = string.Empty,
        };
        box.FilteredItems.Add("apple");
        box.IsDropDownOpen = true;
        Css.SetStyle(box, "border:8px solid transparent;padding:4px");
        CssBorderRadiusTests.Layout(box, 80, 100);

        box.ProbeCaretAt(new Point(10, 75));
        Assert.Equal("apple", box.Text);
        Assert.False(box.IsDropDownOpen);
    }

    [Fact]
    public void CssEdges_AddToDirectDesiredInputHeight()
    {
        var box = DirectBox();
        box.FilteredItems.Add("apple");
        box.IsDropDownOpen = true;
        Css.SetStyle(box, "border:8px solid transparent;padding:4px");
        box.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        Assert.Equal(84, box.DesiredSize.Height);
    }

    [Fact]
    public void NativeBackgroundAndBorderBrush_KeepPriority()
    {
        var box = DirectBox();
        box.Background = Brushes.Lime;
        box.BorderBrush = Brushes.Lime;
        Css.SetStyle(box,
            "background-color:blue;background-clip:content-box;" +
            "border:8px solid; border-color:red blue yellow white;padding:4px");
        CssBorderRadiusTests.Layout(box, 80, 60);

        var pixels = CssBorderRadiusTests.Pixels(box, 80, 60);
        Assert.Equal((byte)255, Channel(pixels, 10, 30, 1));
        Assert.Equal((byte)0, Channel(pixels, 10, 30, 0));
        Assert.Equal((byte)255, Channel(pixels, 40, 4, 1));
        Assert.Equal((byte)0, Channel(pixels, 40, 4, 2));
    }

    [Fact]
    public void RemovingCssEdges_RestoresNativeInputHeight()
    {
        var box = DirectBox();
        Css.SetStyle(box, "border:8px solid transparent;padding:4px;background-color:blue");
        CssBorderRadiusTests.Layout(box, 80, 60);
        var styled = CssBorderRadiusTests.Pixels(box, 80, 60);
        Assert.Equal((byte)255, Channel(styled, 40, 50, 0));

        Css.SetStyle(box, string.Empty);
        CssBorderRadiusTests.Layout(box, 80, 60);
        var reset = CssBorderRadiusTests.Pixels(box, 80, 60);
        Assert.Equal((byte)0, Channel(reset, 40, 50, 3));
    }

    private static AutoCompleteBox DirectBox() => new()
    {
        Template = null,
        Foreground = Brushes.Transparent,
        PlaceholderText = string.Empty,
    };

    private sealed class ProbeAutoCompleteBox : AutoCompleteBox
    {
        internal int ProbeCaretAt(Point position) => GetCaretIndexFromPosition(position);
    }

    private sealed class TextOriginContext : DrawingContextAdapter
    {
        internal List<(string Text, Point Origin)> Texts { get; } = [];

        public override void DrawText(FormattedText formattedText, Point origin)
            => Texts.Add((formattedText.Text, origin));
        public override void DrawLine(Pen pen, Point point0, Point point1) { }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY) { }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawImage(ImageSource imageSource, Rect rectangle) { }
        public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect, CornerRadius cornerRadius) { }
        public override void PushTransform(Transform transform) { }
        public override void PushClip(Geometry clipGeometry) { }
        public override void PushOpacity(double opacity) { }
        public override void Pop() { }
        public override void Close() { }
    }

    private static byte Channel(byte[] pixels, int x, int y, int channel)
        => pixels[(y * 80 + x) * 4 + channel];
}

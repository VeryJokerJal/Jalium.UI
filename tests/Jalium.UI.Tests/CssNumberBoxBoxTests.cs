using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssNumberBoxBoxTests
{
    [Fact]
    public void DirectBackgroundLayers_UseContentBoxForClipAndPosition()
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
        Assert.Equal((byte)0, Channel(pixels, 80, 10, 30, 3));
        Assert.Equal((byte)255, Channel(pixels, 80, 30, 30, 0));
        Assert.True(Channel(pixels, 80, 50, 43, 2) > 100);
        Assert.True(Channel(pixels, 80, 65, 43, 1) > 100);
    }

    [Fact]
    public void DirectBorder_PaintsDistinctSideColors()
    {
        var box = DirectBox();
        Css.SetStyle(box, "border:8px solid; border-color:red green blue yellow");
        CssBorderRadiusTests.Layout(box, 80, 60);

        var pixels = CssBorderRadiusTests.Pixels(box, 80, 60);
        Assert.Equal((byte)255, Channel(pixels, 80, 40, 4, 2));
        Assert.Equal((byte)128, Channel(pixels, 80, 76, 30, 1));
        Assert.Equal((byte)255, Channel(pixels, 80, 40, 56, 0));
        Assert.Equal((byte)255, Channel(pixels, 80, 4, 30, 1));
    }

    [Fact]
    public void CssEdges_KeepHeaderInsideContentBox()
    {
        var box = DirectBox();
        box.Header = "A";
        box.Foreground = Brushes.Red;
        Css.SetStyle(box, "border:8px solid transparent;padding:4px");
        CssBorderRadiusTests.Layout(box, 80, 60);

        var drawing = new TextOriginContext();
        box.Render(drawing);
        var header = Assert.Single(drawing.Texts, entry => entry.Text == "A");
        Assert.Equal(new Point(12, 12), header.Origin);
    }

    [Fact]
    public void CssEdges_InsetDirectSpinButtonsAndText()
    {
        var box = new NumberBox { Template = null };
        Css.SetStyle(box, "border:8px solid transparent;padding:4px");
        CssBorderRadiusTests.Layout(box, 80, 60);
        CssBorderRadiusTests.Pixels(box, 80, 60);

        var up = RectField(box, "_upButtonRect");
        var down = RectField(box, "_downButtonRect");
        var text = RectField(box, "_textRect");
        Assert.Equal(56, box.DesiredSize.Height);
        Assert.Equal(36, up.X);
        Assert.Equal(12, up.Y);
        Assert.Equal(32, up.Width);
        Assert.Equal(18, up.Height);
        Assert.Equal(36, down.X);
        Assert.Equal(30, down.Y);
        Assert.Equal(12, text.X);
        Assert.Equal(12, text.Y);
        Assert.Equal(24, text.Width);
        Assert.Equal(36, text.Height);
    }

    [Fact]
    public void CssEdges_MoveDirectImeCaretWithText()
    {
        var box = DirectBox();
        Css.SetStyle(box, "border:8px solid transparent;padding:4px");
        CssBorderRadiusTests.Layout(box, 80, 60);
        CssBorderRadiusTests.Pixels(box, 80, 60);
        box.CaretIndexCore = 0;

        Assert.Equal(12, box.GetImeCaretPosition().X);
        var drawing = new TextOriginContext();
        box.Render(drawing);
        var value = Assert.Single(drawing.Texts, entry => entry.Text == "0");
        Assert.InRange(Math.Abs(box.GetImeCaretRectangle().Y - value.Origin.Y), 0, 3);
    }

    [Fact]
    public void NativeBackground_KeepsPriority()
    {
        var box = DirectBox();
        box.Background = Brushes.Lime;
        Css.SetStyle(box,
            "background-color:blue;background-clip:content-box;" +
            "border:8px solid transparent;padding:4px");
        CssBorderRadiusTests.Layout(box, 80, 60);

        var pixels = CssBorderRadiusTests.Pixels(box, 80, 60);
        Assert.Equal((byte)255, Channel(pixels, 80, 10, 30, 1));
        Assert.Equal((byte)0, Channel(pixels, 80, 10, 30, 0));
    }

    [Fact]
    public void NativeBorderBrush_KeepsPriorityOverCssSideColors()
    {
        var box = DirectBox();
        box.BorderBrush = Brushes.Lime;
        Css.SetStyle(box, "border:8px solid; border-color:red blue yellow white");
        CssBorderRadiusTests.Layout(box, 80, 60);

        var pixels = CssBorderRadiusTests.Pixels(box, 80, 60);
        Assert.Equal((byte)255, Channel(pixels, 80, 40, 4, 1));
        Assert.Equal((byte)0, Channel(pixels, 80, 40, 4, 2));
    }

    [Fact]
    public void CssBackground_UsesNativeBorderAndPaddingForItsContentBox()
    {
        var box = DirectBox();
        box.BorderThickness = new Thickness(8);
        box.Padding = new Thickness(4);
        box.Header = "A";
        box.Foreground = Brushes.Red;
        Css.SetStyle(box, "background-color:blue;background-clip:content-box");
        CssBorderRadiusTests.Layout(box, 80, 60);

        var pixels = CssBorderRadiusTests.Pixels(box, 80, 60);
        Assert.Equal((byte)0, Channel(pixels, 80, 10, 30, 3));
        Assert.Equal((byte)255, Channel(pixels, 80, 30, 40, 0));
        var drawing = new TextOriginContext();
        box.Render(drawing);
        var header = Assert.Single(drawing.Texts, entry => entry.Text == "A");
        Assert.Equal(new Point(12, 12), header.Origin);
    }

    [Fact]
    public void CssRadius_ClipsNativeBackgroundAroundWholeDirectBox()
    {
        var box = DirectBox();
        box.Header = "A";
        box.BorderThickness = new Thickness(8);
        box.Padding = new Thickness(4);
        box.Background = Brushes.Blue;
        Css.SetStyle(box, "border-radius:50%");
        CssBorderRadiusTests.Layout(box, 80, 60);

        var pixels = CssBorderRadiusTests.Pixels(box, 80, 60);
        Assert.Equal((byte)0, Channel(pixels, 80, 1, 1, 3));
        Assert.Equal((byte)255, Channel(pixels, 80, 40, 10, 0));
    }

    [Fact]
    public void RemovingCssEdges_RestoresDirectSpinGeometry()
    {
        var box = new NumberBox { Template = null };
        Css.SetStyle(box, "border:8px solid transparent;padding:4px");
        CssBorderRadiusTests.Layout(box, 80, 60);
        CssBorderRadiusTests.Pixels(box, 80, 60);
        Assert.Equal(36, RectField(box, "_upButtonRect").X);

        Css.SetStyle(box, string.Empty);
        CssBorderRadiusTests.Layout(box, 80, 60);
        CssBorderRadiusTests.Pixels(box, 80, 60);
        Assert.Equal(48, RectField(box, "_upButtonRect").X);
        Assert.Equal(0, RectField(box, "_upButtonRect").Y);
    }

    private static NumberBox DirectBox() => new()
    {
        Template = null,
        SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden,
        Foreground = Brushes.Transparent,
    };

    private static Rect RectField(NumberBox box, string name)
    {
        var field = typeof(NumberBox).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<Rect>(field!.GetValue(box));
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

    private static byte Channel(byte[] pixels, int width, int x, int y, int channel)
        => pixels[(y * width + x) * 4 + channel];
}

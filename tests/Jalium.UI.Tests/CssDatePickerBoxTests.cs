using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;
public class CssDatePickerBoxTests
{
    [Fact]
    public void BackgroundLayers_UseWholeBoxAndContentBoxGeometry()
    {
        var picker = DirectPicker();
        Css.SetStyle(picker, "border:8px solid transparent;padding:4px;background-color:blue;" + "background-image:linear-gradient(to right,red,lime);" + "background-size:20px 10px;background-position:right bottom;" + "background-repeat:no-repeat;background-origin:content-box;" + "background-clip:content-box");
        CssBorderRadiusTests.Layout(picker, 80, 60);
        var pixels = CssBorderRadiusTests.Pixels(picker, 80, 60);
        Assert.Equal((byte)0, Channel(pixels, 10, 30, 3));
        Assert.Equal((byte)255, Channel(pixels, 30, 43, 0));
        Assert.True(Channel(pixels, 50, 43, 2) > 100);
        Assert.True(Channel(pixels, 65, 43, 1) > 100);
    }

    [Fact]
    public void CssBackground_CoversHeaderArea()
    {
        var picker = DirectPicker();
        picker.Header = "A";
        Css.SetStyle(picker, "background-color:blue");
        CssBorderRadiusTests.Layout(picker, 80, 80);
        var pixels = CssBorderRadiusTests.Pixels(picker, 80, 80);
        Assert.Equal((byte)255, Channel(pixels, 10, 5, 0));
        Assert.Equal((byte)255, Channel(pixels, 10, 35, 0));
    }

    [Fact]
    public void Border_PaintsDistinctSideColors()
    {
        var picker = DirectPicker();
        Css.SetStyle(picker, "border:8px solid; border-color:red green blue yellow");
        CssBorderRadiusTests.Layout(picker, 80, 60);
        var pixels = CssBorderRadiusTests.Pixels(picker, 80, 60);
        Assert.Equal((byte)255, Channel(pixels, 40, 4, 2));
        Assert.Equal((byte)128, Channel(pixels, 76, 30, 1));
        Assert.Equal((byte)255, Channel(pixels, 40, 56, 0));
        Assert.Equal((byte)255, Channel(pixels, 4, 30, 1));
    }

    [Fact]
    public void CssEdges_InsetHeaderTextAndCalendarButton()
    {
        var picker = DirectPicker();
        picker.Header = "A";
        picker.Foreground = Brushes.Red;
        Css.SetStyle(picker, "border:8px solid transparent;padding:4px");
        CssBorderRadiusTests.Layout(picker, 80, 80);
        var drawing = new TextOriginContext();
        picker.Render(drawing);
        var header = Assert.Single(drawing.Texts, entry => entry.Text == "A");
        var headerPart = Assert.IsType<ContentPresenter>(picker.FindName("PART_Header"));
        Assert.Equal(new Point(12, 12), headerPart.TransformToAncestor(picker));
        Assert.Equal(12, header.Origin.X);
        Assert.InRange(header.Origin.Y, 12, 16);
        var button = ButtonBounds(picker);
        Assert.Equal(36, button.X);
        Assert.Equal(32, button.Width);
        Assert.True(button.Y > 12);
        Assert.True(button.Bottom <= 68);
    }

    [Fact]
    public void NativeBackgroundAndBorderBrush_KeepPriority()
    {
        var picker = DirectPicker();
        picker.Background = Brushes.Lime;
        picker.BorderBrush = Brushes.Lime;
        Css.SetStyle(picker, "background-color:blue;background-clip:content-box;" + "border:8px solid; border-color:red blue yellow white;padding:4px");
        CssBorderRadiusTests.Layout(picker, 80, 60);
        var pixels = CssBorderRadiusTests.Pixels(picker, 80, 60);
        Assert.Equal((byte)255, Channel(pixels, 10, 30, 1));
        Assert.Equal((byte)0, Channel(pixels, 10, 30, 0));
        Assert.Equal((byte)255, Channel(pixels, 40, 4, 1));
        Assert.Equal((byte)0, Channel(pixels, 40, 4, 2));
    }

    [Fact]
    public void CssRadius_ClipsNativeBackgroundAroundWholeBox()
    {
        var picker = DirectPicker();
        picker.Header = "A";
        picker.Background = Brushes.Blue;
        Css.SetStyle(picker, "border-radius:50%");
        CssBorderRadiusTests.Layout(picker, 80, 60);
        var pixels = CssBorderRadiusTests.Pixels(picker, 80, 60);
        Assert.Equal((byte)0, Channel(pixels, 1, 1, 3));
        Assert.Equal((byte)255, Channel(pixels, 40, 10, 0));
    }

    [Fact]
    public void RemovingCssEdges_RestoresNativeButtonGeometry()
    {
        var picker = DirectPicker();
        CssBorderRadiusTests.Layout(picker, 80, 60);
        var baseline = ButtonBounds(picker);
        Css.SetStyle(picker, "border:8px solid transparent;padding:4px");
        CssBorderRadiusTests.Layout(picker, 80, 60);
        picker.Render(new TextOriginContext());
        Assert.Equal(36, ButtonBounds(picker).X);
        Css.SetStyle(picker, string.Empty);
        CssBorderRadiusTests.Layout(picker, 80, 60);
        picker.Render(new TextOriginContext());
        var restored = ButtonBounds(picker);
        Assert.Equal(baseline, restored);
    }

    private static DatePicker DirectPicker()
    {
        var picker = new DatePicker
        {
            Foreground = Brushes.Transparent,
            PlaceholderText = string.Empty
        };
        DatePickerTemplateTestHelper.Apply(picker);
        return picker;
    }

    private static Rect ButtonBounds(DatePicker picker)
    {
        var button = Assert.IsType<Button>(picker.FindName("PART_Button"));
        var position = button.TransformToAncestor(picker);
        return new Rect(position, button.RenderSize);
    }

    private sealed class TextOriginContext : DrawingContextAdapter, IOffsetDrawingContext
    {
        public Point Offset { get; set; }
        internal List<(string Text, Point Origin)> Texts { get; } = [];

        public override void DrawText(FormattedText formattedText, Point origin) => Texts.Add((formattedText.Text, new Point(origin.X + Offset.X, origin.Y + Offset.Y)));
        public override void DrawLine(Pen pen, Point point0, Point point1)
        {
        }

        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle)
        {
        }

        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY)
        {
        }

        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY)
        {
        }

        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry)
        {
        }

        public override void DrawImage(ImageSource imageSource, Rect rectangle)
        {
        }

        public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect, CornerRadius cornerRadius)
        {
        }

        public override void PushTransform(Transform transform)
        {
        }

        public override void PushClip(Geometry clipGeometry)
        {
        }

        public override void PushOpacity(double opacity)
        {
        }

        public override void Pop()
        {
        }

        public override void Close()
        {
        }
    }

    private static byte Channel(byte[] pixels, int x, int y, int channel) => pixels[(y * 80 + x) * 4 + channel];
}

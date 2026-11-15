using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssLegacyMenuBoxTests
{
    private const int Width = 100;
    private const int Height = 60;
    private const string LayeredBackground =
        "border:8px solid transparent;padding:4px;background-color:blue;" +
        "background-image:linear-gradient(to right,red,lime);" +
        "background-size:20px 10px;background-position:right bottom;" +
        "background-repeat:no-repeat;background-origin:content-box;" +
        "background-clip:content-box";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MenuSurface_PaintsPositionedCssBackgroundLayers(bool item)
    {
        var control = CreateControl(item);
        Css.SetStyle(control, LayeredBackground);
        CssBorderRadiusTests.Layout(control, Width, Height);

        var pixels = CssBorderRadiusTests.Pixels(control, Width, Height);
        Assert.Equal((byte)0, Channel(pixels, 10, 30, 3));
        Assert.Equal((byte)255, Channel(pixels, 40, 20, 0));
        Assert.True(Channel(pixels, 70, 43, 2) > 100);
        Assert.True(Channel(pixels, 85, 43, 1) > 100);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MenuSurface_PaintsEllipticalRadiusAndBorderSides(bool item)
    {
        var control = CreateControl(item);
        Css.SetStyle(control,
            "border:8px solid;border-color:red green blue yellow;" +
            "border-radius:50% / 30%;background:none");
        CssBorderRadiusTests.Layout(control, Width, Height);

        var pixels = CssBorderRadiusTests.Pixels(control, Width, Height);
        Assert.Equal((byte)0, Channel(pixels, 2, 2, 3));
        Assert.Equal((byte)255, Channel(pixels, 50, 4, 2));
        Assert.Equal((byte)128, Channel(pixels, 96, 30, 1));
        Assert.Equal((byte)255, Channel(pixels, 50, 56, 0));
        Assert.Equal((byte)255, Channel(pixels, 4, 30, 1));
        Assert.Equal((byte)0, Channel(pixels, 50, 30, 3));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MenuSurface_DoubleBorderLeavesMiddleBandClear(bool item)
    {
        var control = CreateControl(item);
        Css.SetStyle(control, "background:none;border:9px double red");
        CssBorderRadiusTests.Layout(control, Width, Height);

        var pixels = CssBorderRadiusTests.Pixels(control, Width, Height);
        Assert.Equal((byte)255, Channel(pixels, 50, 1, 2));
        Assert.Equal((byte)0, Channel(pixels, 50, 4, 3));
        Assert.Equal((byte)255, Channel(pixels, 50, 7, 2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MenuSurface_LocalBrushesKeepPriorityOverCss(bool item)
    {
        var control = CreateControl(item);
        control.Background = Brushes.Red;
        control.BorderBrush = Brushes.Blue;
        Css.SetStyle(control,
            "background-color:blue;background-image:linear-gradient(lime,lime);border:8px solid red");
        CssBorderRadiusTests.Layout(control, Width, Height);

        var pixels = CssBorderRadiusTests.Pixels(control, Width, Height);
        Assert.Equal((byte)255, Channel(pixels, 40, 20, 2));
        Assert.Equal((byte)0, Channel(pixels, 40, 20, 1));
        Assert.Equal((byte)255, Channel(pixels, 50, 4, 0));
        Assert.Equal((byte)0, Channel(pixels, 50, 4, 2));
    }

    [Fact]
    public void Menu_ReflowsItemsHostIntoCssContentBoxAndBack()
    {
        var menu = new InspectableMenu { Width = Width, Height = Height };
        menu.Items.Add(new MenuItem { Header = "File" });
        Css.SetStyle(menu, "border:8px solid transparent;padding:4px 6px 3px 12px");
        CssBorderRadiusTests.Layout(menu, Width, Height);
        Assert.Equal(new Rect(20, 12, 66, 37), Assert.IsType<StackPanel>(menu.Host).VisualBounds);

        Css.SetStyle(menu, "");
        CssBorderRadiusTests.Layout(menu, Width, Height);
        Assert.Equal(new Rect(0, 0, Width, Height), menu.Host!.VisualBounds);
    }

    [Fact]
    public void MenuItem_BackgroundNoneSuppressesSelectedHoverFillAndRemovalRestoresIt()
    {
        var item = new MenuItem { Width = Width, Height = Height };
        item.RaiseEvent(new Jalium.UI.Input.MouseEventArgs(UIElement.MouseEnterEvent) { Source = item });
        Assert.True(item.IsSelected);
        Css.SetStyle(item, "background:none");
        CssBorderRadiusTests.Layout(item, Width, Height);
        Assert.Equal((byte)0, Channel(CssBorderRadiusTests.Pixels(item, Width, Height), 50, 30, 3));

        Css.SetStyle(item, "");
        CssBorderRadiusTests.Layout(item, Width, Height);
        Assert.Equal((byte)255, Channel(CssBorderRadiusTests.Pixels(item, Width, Height), 50, 30, 3));
    }

    [Fact]
    public void MenuItem_CssEdgesMoveHeaderAndGestureAndRestoreOnRemoval()
    {
        var item = new MenuItem
        {
            Width = Width, Height = Height, Header = "Open", InputGestureText = "Ctrl+O",
        };
        CssBorderRadiusTests.Layout(item, Width, Height);
        var plain = Capture(item);

        Css.SetStyle(item, "border:8px solid transparent;padding:4px 6px 3px 12px");
        CssBorderRadiusTests.Layout(item, Width, Height);
        var styled = Capture(item);
        Assert.Equal(TextOrigin(plain, "Open").X + 20, TextOrigin(styled, "Open").X);
        Assert.Equal(TextOrigin(plain, "Ctrl+O").X - 14, TextOrigin(styled, "Ctrl+O").X);
        Assert.Equal(TextOrigin(plain, "Open").Y + 0.5, TextOrigin(styled, "Open").Y);

        Css.SetStyle(item, "");
        CssBorderRadiusTests.Layout(item, Width, Height);
        var restored = Capture(item);
        Assert.Equal(TextOrigin(plain, "Open"), TextOrigin(restored, "Open"));
        Assert.Equal(TextOrigin(plain, "Ctrl+O"), TextOrigin(restored, "Ctrl+O"));
    }

    [Fact]
    public void TopLevelMenuItem_CssEdgesMoveHeaderInsideItsOwnBox()
    {
        var menu = new Menu { Width = 150, Height = Height };
        var item = new MenuItem { Width = Width, Height = Height, Header = "File" };
        menu.Items.Add(item);
        CssBorderRadiusTests.Layout(menu, 150, Height);
        var plain = TextOrigin(Capture(item), "File");

        Css.SetStyle(item, "border:8px solid transparent;padding:4px 6px 3px 12px");
        CssBorderRadiusTests.Layout(menu, 150, Height);
        var styled = TextOrigin(Capture(item), "File");
        Assert.Equal(plain.X + 20, styled.X);
        Assert.Equal(plain.Y + 0.5, styled.Y);
    }

    [Fact]
    public void SubmenuItem_CssEdgesIncreaseDesiredSize()
    {
        var item = new MenuItem { Header = "Open" };
        item.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var plain = item.DesiredSize;

        Css.SetStyle(item, "border:8px solid transparent;padding:4px 6px 3px 12px");
        CssEvaluationScheduler.FlushIfPending(item.Dispatcher);
        item.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.Equal(plain.Width + 34, item.DesiredSize.Width);
        Assert.Equal(plain.Height + 23, item.DesiredSize.Height);
    }

    [Fact]
    public void MenuItem_CssColorPaintsHeaderGestureAndCheckmark()
    {
        var item = new MenuItem
        {
            Width = Width, Height = Height, Header = "Open", InputGestureText = "Ctrl+O",
            IsCheckable = true, IsChecked = true,
        };
        Css.SetStyle(item, "color:red;padding:4px 6px 3px 12px");
        CssBorderRadiusTests.Layout(item, Width, Height);

        var capture = Capture(item);
        Assert.All(capture.Texts, entry => Assert.Equal(Color.FromRgb(255, 0, 0),
            Assert.IsType<SolidColorBrush>(entry.Text.Foreground).Color));
        Assert.Equal(2, capture.Lines.Count);
        Assert.All(capture.Lines, entry => Assert.Equal(Color.FromRgb(255, 0, 0),
            Assert.IsType<SolidColorBrush>(entry.Pen.Brush).Color));
        Assert.Equal(20, capture.Lines[0].Start.X);
    }

    [Fact]
    public void MenuItem_CssColorAndRightEdgeApplyToSubmenuArrow()
    {
        var item = new MenuItem { Width = Width, Height = Height, Header = "Open" };
        item.Items.Add(new MenuItem { Header = "Child" });
        CssBorderRadiusTests.Layout(item, Width, Height);
        var plain = Assert.Single(Capture(item).Geometries);

        Css.SetStyle(item, "color:red;padding:4px 6px 3px 12px");
        CssBorderRadiusTests.Layout(item, Width, Height);
        var styled = Assert.Single(Capture(item).Geometries);
        Assert.Equal(plain.Geometry.Bounds.X - 6, styled.Geometry.Bounds.X);
        Assert.Equal(Color.FromRgb(255, 0, 0),
            Assert.IsType<SolidColorBrush>(styled.Brush).Color);
    }

    [Fact]
    public void MenuItem_InheritsCssColorFromParent()
    {
        var item = new MenuItem { Width = Width, Height = Height, Header = "Open" };
        var host = new Border { Child = item, Width = Width, Height = Height };
        Css.SetStyle(host, "color:red");
        CssBorderRadiusTests.Layout(host, Width, Height);
        Assert.Equal(Color.FromRgb(255, 0, 0),
            Assert.IsType<SolidColorBrush>(Assert.Single(Capture(item).Texts).Text.Foreground).Color);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThemedMenuSurface_CssLayersOverrideStyleSetters(bool item)
    {
        ResetApplicationState();
        _ = new Application();
        try
        {
            var control = CreateControl(item);
            Css.SetStyle(control, LayeredBackground);
            var host = new Grid { Width = Width, Height = Height };
            host.Children.Add(control);
            CssBorderRadiusTests.Layout(host, Width, Height);
            var pixels = CssBorderRadiusTests.Pixels(control, Width, Height);
            Assert.Equal((byte)0, Channel(pixels, 10, 30, 3));
            Assert.True(Channel(pixels, 85, 43, 1) > 100);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private static Control CreateControl(bool item) => item
        ? new MenuItem { Width = Width, Height = Height }
        : new Menu { Width = Width, Height = Height };

    private static Point TextOrigin(RenderCapture capture, string text)
        => Assert.Single(capture.Texts, entry => entry.Text.Text == text).Origin;

    private static RenderCapture Capture(Control item)
    {
        var context = new RenderCapture();
        item.Render(context);
        return context;
    }

    private static byte Channel(byte[] pixels, int x, int y, int channel)
        => pixels[(y * Width + x) * 4 + channel];

    private static void ResetApplicationState()
    {
        typeof(Application).GetField("_current", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, null);
        typeof(ThemeManager).GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static)
            ?.Invoke(null, null);
    }

    private sealed class InspectableMenu : Menu
    {
        internal Panel? Host => ItemsHost;
    }

    private sealed class RenderCapture : DrawingContextAdapter
    {
        internal List<(FormattedText Text, Point Origin)> Texts { get; } = [];
        internal List<(Pen Pen, Point Start, Point End)> Lines { get; } = [];
        internal List<(Brush? Brush, Geometry Geometry)> Geometries { get; } = [];

        public override void DrawText(FormattedText formattedText, Point origin)
            => Texts.Add((formattedText, origin));
        public override void DrawLine(Pen pen, Point point0, Point point1)
            => Lines.Add((pen, point0, point1));
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry)
            => Geometries.Add((brush, geometry));
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY) { }
        public override void DrawImage(ImageSource imageSource, Rect rectangle) { }
        public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect, CornerRadius cornerRadius) { }
        public override void PushTransform(Transform transform) { }
        public override void PushClip(Geometry clipGeometry) { }
        public override void PushOpacity(double opacity) { }
        public override void Pop() { }
        public override void Close() { }
    }
}

using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssMenuItemBoxTests
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
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void MenuItem_PaintsPositionedCssBackgroundLayers(int kind)
    {
        var item = CreateItem(kind);
        Css.SetStyle(item, LayeredBackground);
        CssBorderRadiusTests.Layout(item, Width, Height);

        var pixels = CssBorderRadiusTests.Pixels(item, Width, Height);
        Assert.Equal((byte)0, Channel(pixels, 10, 30, 3));
        Assert.Equal((byte)255, Channel(pixels, 40, 20, 0));
        Assert.True(Channel(pixels, 70, 43, 2) > 100);
        Assert.True(Channel(pixels, 85, 43, 1) > 100);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void MenuItem_PaintsEllipticalRadiusAndBorderSides(int kind)
    {
        var item = CreateItem(kind);
        Css.SetStyle(item,
            "border:8px solid;border-color:red green blue yellow;" +
            "border-radius:50% / 30%;background:none");
        CssBorderRadiusTests.Layout(item, Width, Height);

        var pixels = CssBorderRadiusTests.Pixels(item, Width, Height);
        Assert.Equal((byte)0, Channel(pixels, 2, 2, 3));
        Assert.Equal((byte)255, Channel(pixels, 50, 4, 2));
        Assert.Equal((byte)128, Channel(pixels, 96, 30, 1));
        Assert.Equal((byte)255, Channel(pixels, 50, 56, 0));
        Assert.Equal((byte)255, Channel(pixels, 4, 30, 1));
        Assert.Equal((byte)0, Channel(pixels, 50, 30, 3));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void MenuItem_DoubleBorderLeavesMiddleBandClear(int kind)
    {
        var item = CreateItem(kind);
        Css.SetStyle(item, "background:none;border:9px double red");
        CssBorderRadiusTests.Layout(item, Width, Height);

        var pixels = CssBorderRadiusTests.Pixels(item, Width, Height);
        Assert.Equal((byte)255, Channel(pixels, 50, 1, 2));
        Assert.Equal((byte)0, Channel(pixels, 50, 4, 3));
        Assert.Equal((byte)255, Channel(pixels, 50, 7, 2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void MenuItem_LocalBackgroundKeepsPriorityOverCssLayers(int kind)
    {
        var item = CreateItem(kind);
        item.Background = Brushes.Red;
        Css.SetStyle(item, "background-color:blue;background-image:linear-gradient(lime,lime)");
        CssBorderRadiusTests.Layout(item, Width, Height);

        var pixels = CssBorderRadiusTests.Pixels(item, Width, Height);
        Assert.Equal((byte)255, Channel(pixels, 40, 20, 2));
        Assert.Equal((byte)0, Channel(pixels, 40, 20, 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void MenuItem_LocalBorderBrushKeepsPriorityOverCssSideColors(int kind)
    {
        var item = CreateItem(kind);
        item.BorderBrush = Brushes.Blue;
        Css.SetStyle(item, "background:none;border:8px solid red");
        CssBorderRadiusTests.Layout(item, Width, Height);

        var pixels = CssBorderRadiusTests.Pixels(item, Width, Height);
        Assert.Equal((byte)255, Channel(pixels, 50, 4, 0));
        Assert.Equal((byte)0, Channel(pixels, 50, 4, 2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ThemedMenuItem_CssBackgroundAndColorOverrideStyleSetter(int kind)
    {
        ResetApplicationState();
        _ = new Application();
        try
        {
            var item = CreateItem(kind);
            if (item is MenuBarItem bar) bar.Title = "File";
            else ((MenuFlyoutItem)item).Text = "File";
            Css.SetStyle(item, LayeredBackground + ";color:red");
            var host = new Grid { Width = Width, Height = Height };
            host.Children.Add(item);
            CssBorderRadiusTests.Layout(host, Width, Height);

            var pixels = CssBorderRadiusTests.Pixels(item, Width, Height);
            Assert.Equal((byte)0, Channel(pixels, 10, 30, 3));
            Assert.True(Channel(pixels, 85, 43, 1) > 100);
            Assert.Equal(Color.FromRgb(255, 0, 0),
                Assert.IsType<SolidColorBrush>(Assert.Single(Capture(item).Texts).Text.Foreground).Color);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void MenuItem_CssColorAndLocalForegroundPaintText(int kind)
    {
        var item = CreateItem(kind);
        if (item is MenuBarItem bar) bar.Title = "File";
        else ((MenuFlyoutItem)item).Text = "File";
        Css.SetStyle(item, "color:red");
        CssBorderRadiusTests.Layout(item, Width, Height);

        var context = new TextCaptureContext();
        item.Render(context);
        Assert.Equal(Color.FromRgb(255, 0, 0),
            Assert.IsType<SolidColorBrush>(Assert.Single(context.Texts).Text.Foreground).Color);

        item.Foreground = Brushes.Blue;
        context.Texts.Clear();
        item.Render(context);
        Assert.Equal(Color.FromRgb(0, 0, 255),
            Assert.IsType<SolidColorBrush>(Assert.Single(context.Texts).Text.Foreground).Color);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void MenuItem_InheritsCssColorFromParent(int kind)
    {
        var host = new Grid { Width = Width, Height = Height };
        var item = CreateItem(kind);
        if (item is MenuBarItem bar) bar.Title = "File";
        else ((MenuFlyoutItem)item).Text = "File";
        host.Children.Add(item);
        Css.SetStyle(host, "color:red");
        CssBorderRadiusTests.Layout(host, Width, Height);

        Assert.Equal(Color.FromRgb(255, 0, 0),
            Assert.IsType<SolidColorBrush>(host.GetValue(Control.ForegroundProperty)).Color);
        Assert.Equal(Color.FromRgb(255, 0, 0),
            Assert.IsType<SolidColorBrush>(item.Foreground).Color);
        Assert.Equal(Color.FromRgb(255, 0, 0),
            Assert.IsType<SolidColorBrush>(Assert.Single(Capture(item).Texts).Text.Foreground).Color);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void MenuItem_CssContentEdgesMoveTextAndRestoreOnRemoval(int kind)
    {
        var item = CreateItem(kind);
        if (item is MenuBarItem bar) bar.Title = "File";
        else ((MenuFlyoutItem)item).Text = "File";
        CssBorderRadiusTests.Layout(item, Width, Height);
        var plain = TextOrigin(item);

        Css.SetStyle(item, "border:8px solid transparent;padding:4px 6px 3px 12px");
        CssBorderRadiusTests.Layout(item, Width, Height);
        var styled = TextOrigin(item);
        Assert.Equal(plain.X + (kind == 0 ? 3 : 20), styled.X);
        Assert.Equal(plain.Y + 0.5, styled.Y);

        Css.SetStyle(item, "");
        CssBorderRadiusTests.Layout(item, Width, Height);
        Assert.Equal(plain, TextOrigin(item));
    }

    [Fact]
    public void MenuFlyoutItem_CssEdgesIncreaseDesiredSize()
    {
        var item = new MenuFlyoutItem { Text = "File" };
        item.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var plain = item.DesiredSize;

        Css.SetStyle(item, "border:8px solid transparent;padding:4px 6px 3px 12px");
        CssEvaluationScheduler.FlushIfPending(item.Dispatcher);
        item.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.Equal(plain.Width + 34, item.DesiredSize.Width);
        Assert.Equal(plain.Height + 23, item.DesiredSize.Height);
    }

    [Fact]
    public void MenuFlyoutItem_CssColorAndRightEdgeApplyToAccelerator()
    {
        var item = new MenuFlyoutItem
        {
            Width = Width, Height = Height, Text = "Open",
            KeyboardAcceleratorTextOverride = "Ctrl+O",
        };
        CssBorderRadiusTests.Layout(item, Width, Height);
        var plain = Capture(item);
        var plainAccelerator = Assert.Single(plain.Texts, entry => entry.Text.Text == "Ctrl+O");

        Css.SetStyle(item, "color:red;padding:4px 6px 3px 12px");
        CssBorderRadiusTests.Layout(item, Width, Height);
        var styled = Capture(item);
        var accelerator = Assert.Single(styled.Texts, entry => entry.Text.Text == "Ctrl+O");
        Assert.Equal(plainAccelerator.Origin.X - 6, accelerator.Origin.X);
        Assert.Equal(Color.FromRgb(255, 0, 0),
            Assert.IsType<SolidColorBrush>(accelerator.Text.Foreground).Color);
    }

    [Fact]
    public void MenuFlyoutSubItem_CssEdgesMoveArrowAndColorIt()
    {
        var item = new MenuFlyoutSubItem { Width = Width, Height = Height };
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
    public void ToggleMenuFlyoutItem_CssEdgesMoveCheckmarkAndColorIt()
    {
        var item = new ToggleMenuFlyoutItem
        {
            Width = Width, Height = Height, IsChecked = true,
        };
        CssBorderRadiusTests.Layout(item, Width, Height);
        var plain = Capture(item);
        var plainTransform = Assert.Single(plain.Transforms);

        Css.SetStyle(item, "color:red;padding:4px 6px 3px 12px");
        CssBorderRadiusTests.Layout(item, Width, Height);
        var styled = Capture(item);
        var styledTransform = Assert.Single(styled.Transforms);
        Assert.Equal(plainTransform.X + 12, styledTransform.X);
        Assert.Equal(plainTransform.Y + 0.5, styledTransform.Y);
        Assert.Equal(Color.FromRgb(255, 0, 0),
            Assert.IsType<SolidColorBrush>(Assert.Single(styled.Geometries).Pen?.Brush).Color);
    }

    private static Control CreateItem(int kind) => kind switch
    {
        0 => new MenuBarItem { Width = Width, Height = Height },
        1 => new MenuFlyoutItem { Width = Width, Height = Height },
        2 => new MenuFlyoutSubItem { Width = Width, Height = Height },
        3 => new ToggleMenuFlyoutItem { Width = Width, Height = Height },
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static Point TextOrigin(Control item)
    {
        var context = new TextCaptureContext();
        item.Render(context);
        return Assert.Single(context.Texts).Origin;
    }

    private static TextCaptureContext Capture(Control item)
    {
        var context = new TextCaptureContext();
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

    private sealed class TextCaptureContext : DrawingContextAdapter
    {
        internal List<(FormattedText Text, Point Origin)> Texts { get; } = [];
        internal List<(Brush? Brush, Pen? Pen, Geometry Geometry)> Geometries { get; } = [];
        internal List<TranslateTransform> Transforms { get; } = [];

        public override void DrawText(FormattedText formattedText, Point origin)
            => Texts.Add((formattedText, origin));
        public override void DrawLine(Pen pen, Point point0, Point point1) { }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY) { }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry)
            => Geometries.Add((brush, pen, geometry));
        public override void DrawImage(ImageSource imageSource, Rect rectangle) { }
        public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect, CornerRadius cornerRadius) { }
        public override void PushTransform(Transform transform)
        {
            if (transform is TranslateTransform translation)
                Transforms.Add(translation);
        }
        public override void PushClip(Geometry clipGeometry) { }
        public override void PushOpacity(double opacity) { }
        public override void Pop() { }
        public override void Close() { }
    }
}

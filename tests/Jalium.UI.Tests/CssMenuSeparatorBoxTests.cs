using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssMenuSeparatorBoxTests
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
    public void Separator_PaintsPositionedCssBackgroundLayers(int kind)
    {
        var separator = Create(kind);
        Css.SetStyle(separator, LayeredBackground);
        CssBorderRadiusTests.Layout(separator, Width, Height);

        var pixels = CssBorderRadiusTests.Pixels(separator, Width, Height);
        Assert.Equal((byte)0, Channel(pixels, 10, 25, 3));
        Assert.Equal((byte)255, Channel(pixels, 40, 20, 0));
        Assert.True(Channel(pixels, 70, 43, 2) > 100);
        Assert.True(Channel(pixels, 85, 43, 1) > 100);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Separator_PaintsEllipticalRadiusAndBorderSides(int kind)
    {
        var separator = Create(kind);
        Css.SetStyle(separator,
            "border:8px solid;border-color:red green blue yellow;" +
            "border-radius:50% / 30%;background:none");
        CssBorderRadiusTests.Layout(separator, Width, Height);

        var pixels = CssBorderRadiusTests.Pixels(separator, Width, Height);
        Assert.Equal((byte)0, Channel(pixels, 2, 2, 3));
        Assert.Equal((byte)255, Channel(pixels, 50, 4, 2));
        Assert.Equal((byte)128, Channel(pixels, 96, 30, 1));
        Assert.Equal((byte)255, Channel(pixels, 50, 56, 0));
        Assert.Equal((byte)255, Channel(pixels, 4, 30, 1));
        Assert.Equal((byte)0, Channel(pixels, 40, 25, 3));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Separator_DoubleBorderLeavesMiddleBandClear(int kind)
    {
        var separator = Create(kind);
        Css.SetStyle(separator, "background:none;border:9px double red");
        CssBorderRadiusTests.Layout(separator, Width, Height);

        var pixels = CssBorderRadiusTests.Pixels(separator, Width, Height);
        Assert.Equal((byte)255, Channel(pixels, 40, 1, 2));
        Assert.Equal((byte)0, Channel(pixels, 40, 4, 3));
        Assert.Equal((byte)255, Channel(pixels, 40, 7, 2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Separator_CssPaddingMovesLineAndRestoresOnRemoval(int kind)
    {
        var separator = Create(kind);
        CssBorderRadiusTests.Layout(separator, Width, Height);
        var plain = Assert.Single(Capture(separator).Lines);

        Css.SetStyle(separator, "padding:4px 6px 3px 12px");
        CssBorderRadiusTests.Layout(separator, Width, Height);
        var styled = Assert.Single(Capture(separator).Lines);
        if (kind is 2 or 3)
        {
            Assert.Equal(plain.Start.X + 3, styled.Start.X);
            Assert.Equal(plain.Start.Y + 4, styled.Start.Y);
            Assert.Equal(plain.End.Y - 3, styled.End.Y);
        }
        else
        {
            Assert.Equal(plain.Start.X + 12, styled.Start.X);
            Assert.Equal(plain.End.X - 6, styled.End.X);
            Assert.Equal(plain.Start.Y + 0.5, styled.Start.Y);
        }

        Css.SetStyle(separator, "");
        CssBorderRadiusTests.Layout(separator, Width, Height);
        var restored = Assert.Single(Capture(separator).Lines);
        Assert.Equal(plain.Start, restored.Start);
        Assert.Equal(plain.End, restored.End);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Separator_CssColorPaintsLineUnlessStrokeBrushIsLocal(int kind)
    {
        var separator = Create(kind);
        Css.SetStyle(separator, "color:red");
        CssBorderRadiusTests.Layout(separator, Width, Height);
        Assert.Equal(Color.FromRgb(255, 0, 0),
            Assert.IsType<SolidColorBrush>(Assert.Single(Capture(separator).Lines).Pen.Brush).Color);

        if (separator is Separator generic)
            generic.StrokeBrush = Brushes.Blue;
        else
            separator.Foreground = Brushes.Blue;
        Assert.Equal(Color.FromRgb(0, 0, 255),
            Assert.IsType<SolidColorBrush>(Assert.Single(Capture(separator).Lines).Pen.Brush).Color);
    }

    [Theory]
    [InlineData(0, 34, 32)]
    [InlineData(1, 34, 24)]
    [InlineData(2, 35, 23)]
    [InlineData(3, 35, 55)]
    public void Separator_CssEdgesContributeToIntrinsicSize(int kind, int width, int height)
    {
        var separator = Create(kind);
        separator.Width = double.NaN;
        separator.Height = double.NaN;
        Css.SetStyle(separator, "border:8px solid transparent;padding:4px 6px 3px 12px");
        CssEvaluationScheduler.FlushIfPending(separator.Dispatcher);
        separator.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.Equal(width, separator.DesiredSize.Width);
        Assert.Equal(height, separator.DesiredSize.Height);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThemedSeparator_PaintsCssBoxWithoutAnUninsetTemplateLine(bool vertical)
    {
        ResetApplicationState();
        var app = new Application();
        try
        {
            var separator = new Separator
            {
                Width = Width, Height = Height, Margin = new Thickness(0),
                Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal,
            };
            var host = new Grid { Width = Width, Height = Height };
            host.Children.Add(separator);
            Css.SetStyle(separator,
                "border:8px solid red;padding:4px 6px 3px 12px;" +
                "background-color:blue;background-clip:content-box;color:lime");
            CssBorderRadiusTests.Layout(host, Width, Height);

            Assert.Null(separator.Template);
            var line = Assert.Single(Capture(separator).Lines);
            Assert.Equal(Color.FromRgb(0, 255, 0),
                Assert.IsType<SolidColorBrush>(line.Pen.Brush).Color);
            if (vertical)
            {
                Assert.Equal(53, line.Start.X);
                Assert.Equal(12, line.Start.Y);
                Assert.Equal(49, line.End.Y);
            }
            else
            {
                Assert.Equal(20, line.Start.X);
                Assert.Equal(86, line.End.X);
                Assert.Equal(30.5, line.Start.Y);
            }

            var pixels = CssBorderRadiusTests.Pixels(separator, Width, Height);
            Assert.Equal((byte)255, Channel(pixels, 50, 4, 2));
            Assert.Equal((byte)255, Channel(pixels, 40, 20, 0));
            Assert.Equal((byte)0, Channel(pixels, 10, 30, 3));
            Assert.Same(app.Resources["ControlBorder"], separator.StrokeBrush);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ThemedSeparator_RestoresThemeStrokeWhenCssColorIsRemoved()
    {
        ResetApplicationState();
        _ = new Application();
        try
        {
            var separator = new Separator
            {
                Width = Width, Height = Height, Margin = new Thickness(0),
            };
            var host = new Grid { Width = Width, Height = Height };
            host.Children.Add(separator);
            Css.SetStyle(separator, "color:red");
            CssBorderRadiusTests.Layout(host, Width, Height);
            Assert.Equal(Color.FromRgb(255, 0, 0),
                Assert.IsType<SolidColorBrush>(Assert.Single(Capture(separator).Lines).Pen.Brush).Color);
            Assert.Equal((byte)0,
                Channel(CssBorderRadiusTests.Pixels(separator, Width, Height), 40, 20, 3));

            Css.SetStyle(separator, "");
            CssBorderRadiusTests.Layout(host, Width, Height);
            Assert.Same(separator.StrokeBrush, Assert.Single(Capture(separator).Lines).Pen.Brush);

            Css.SetStyle(separator, "color:red");
            separator.StrokeBrush = Brushes.Blue;
            CssBorderRadiusTests.Layout(host, Width, Height);
            Assert.Same(Brushes.Blue, Assert.Single(Capture(separator).Lines).Pen.Brush);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ThemedAppBarSeparator_UsesCssEdgesForIntrinsicSizeAndLineColor()
    {
        ResetApplicationState();
        _ = new Application();
        try
        {
            var separator = new AppBarSeparator { Margin = new Thickness(0) };
            var host = new Grid { Width = Width, Height = Height };
            host.Children.Add(separator);
            CssBorderRadiusTests.Layout(host, Width, Height);
            Assert.Equal(1, separator.DesiredSize.Width);
            Assert.Equal(32, separator.DesiredSize.Height);

            Css.SetStyle(separator,
                "border:8px solid transparent;padding:4px 6px 3px 12px;color:red");
            CssBorderRadiusTests.Layout(host, Width, Height);

            Assert.True(double.IsNaN(separator.Width));
            Assert.Equal(35, separator.DesiredSize.Width);
            Assert.Equal(55, separator.DesiredSize.Height);
            var line = Assert.Single(Capture(separator).Lines);
            Assert.Equal(Color.FromRgb(255, 0, 0),
                Assert.IsType<SolidColorBrush>(line.Pen.Brush).Color);
            Assert.Equal(53, line.Start.X);
            Assert.Equal(16, line.Start.Y);
            Assert.Equal(45, line.End.Y);

            Css.SetStyle(separator, "");
            CssBorderRadiusTests.Layout(host, Width, Height);
            Assert.Equal(1, separator.DesiredSize.Width);
            Assert.Equal(32, separator.DesiredSize.Height);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private static Control Create(int kind) => kind switch
    {
        0 => new MenuFlyoutSeparator { Width = Width, Height = Height },
        1 => new Separator { Width = Width, Height = Height, Template = null },
        2 => new Separator
        {
            Width = Width, Height = Height, Template = null,
            Orientation = Orientation.Vertical,
        },
        3 => new AppBarSeparator { Width = Width, Height = Height },
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static void ResetApplicationState()
    {
        typeof(Application).GetField("_current", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, null);
        typeof(ThemeManager).GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static)
            ?.Invoke(null, null);
    }

    private static CaptureContext Capture(Control separator)
    {
        var context = new CaptureContext();
        separator.Render(context);
        return context;
    }

    private static byte Channel(byte[] pixels, int x, int y, int channel)
        => pixels[(y * Width + x) * 4 + channel];

    private sealed class CaptureContext : DrawingContextAdapter
    {
        internal List<(Pen Pen, Point Start, Point End)> Lines { get; } = [];
        public override void DrawLine(Pen pen, Point point0, Point point1)
            => Lines.Add((pen, point0, point1));
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY) { }
        public override void DrawText(FormattedText formattedText, Point origin) { }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawImage(ImageSource imageSource, Rect rectangle) { }
        public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect, CornerRadius cornerRadius) { }
        public override void PushTransform(Transform transform) { }
        public override void PushClip(Geometry clipGeometry) { }
        public override void PushOpacity(double opacity) { }
        public override void Pop() { }
        public override void Close() { }
    }
}

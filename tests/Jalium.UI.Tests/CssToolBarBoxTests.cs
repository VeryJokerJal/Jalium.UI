using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssToolBarBoxTests
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
    public void ToolBarSurfaces_PaintPositionedCssBackgroundLayers(bool tray)
    {
        var surface = Create(tray);
        Css.SetStyle(surface, LayeredBackground);
        CssBorderRadiusTests.Layout(surface, Width, Height);

        var pixels = CssBorderRadiusTests.Pixels(surface, Width, Height);
        Assert.Equal((byte)0, Channel(pixels, 10, 25, 3));
        Assert.Equal((byte)255, Channel(pixels, 40, 20, 0));
        Assert.True(Channel(pixels, 70, 43, 2) > 100);
        Assert.True(Channel(pixels, 85, 43, 1) > 100);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ToolBarSurfaces_PaintEllipticalRadiusAndBorderSides(bool tray)
    {
        var surface = Create(tray);
        Css.SetStyle(surface,
            "border:8px solid;border-color:red green blue yellow;" +
            "border-radius:50% / 30%;background:none");
        CssBorderRadiusTests.Layout(surface, Width, Height);

        var pixels = CssBorderRadiusTests.Pixels(surface, Width, Height);
        Assert.Equal((byte)0, Channel(pixels, 2, 2, 3));
        Assert.Equal((byte)255, Channel(pixels, 50, 4, 2));
        Assert.Equal((byte)128, Channel(pixels, 96, 30, 1));
        Assert.Equal((byte)255, Channel(pixels, 50, 56, 0));
        Assert.Equal((byte)255, Channel(pixels, 4, 30, 1));
        Assert.Equal((byte)0, Channel(pixels, 40, 25, 3));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ToolBarSurfaces_NativeBackgroundRetainsPriority(bool tray)
    {
        var surface = Create(tray);
        if (surface is ToolBar toolBar)
            toolBar.Background = Brushes.Lime;
        else
            ((ToolBarTray)surface).Background = Brushes.Lime;
        Css.SetStyle(surface,
            "background-color:blue;background-clip:content-box;" +
            "border:8px solid transparent;padding:4px");
        CssBorderRadiusTests.Layout(surface, Width, Height);

        var pixels = CssBorderRadiusTests.Pixels(surface, Width, Height);
        Assert.Equal((byte)255, Channel(pixels, 10, 30, 1));
        Assert.Equal((byte)0, Channel(pixels, 10, 30, 0));
    }

    [Fact]
    public void ToolBar_CssEdgesMoveItemsAndChangeOverflowDecision()
    {
        var first = new Button { Width = 60, Height = 20 };
        var second = new Button { Width = 30, Height = 20 };
        var toolBar = new ToolBar { Width = Width, Height = Height };
        toolBar.Items.Add(first);
        toolBar.Items.Add(second);
        CssBorderRadiusTests.Layout(toolBar, Width, Height);
        Assert.False(toolBar.HasOverflowItems);

        Css.SetStyle(toolBar, "border:8px solid transparent;padding:4px 6px 3px 12px");
        CssBorderRadiusTests.Layout(toolBar, Width, Height);
        var host = Assert.IsType<ToolBarPanel>(toolBar.GetVisualChild(0));
        Assert.Equal(new Rect(20, 12, 66, 37), host.VisualBounds);
        Assert.True(toolBar.HasOverflowItems);
        Assert.True(ToolBar.GetIsOverflowItem(second));

        Css.SetStyle(toolBar, "");
        CssBorderRadiusTests.Layout(toolBar, Width, Height);
        Assert.Equal(new Rect(0, 0, Width, Height), host.VisualBounds);
        Assert.False(toolBar.HasOverflowItems);
        Assert.False(ToolBar.GetIsOverflowItem(second));
    }

    [Fact]
    public void VerticalToolBar_CssEdgesChangeOverflowDecision()
    {
        var first = new Button { Width = 20, Height = 60 };
        var second = new Button { Width = 20, Height = 30 };
        var toolBar = new ToolBar { Width = 60, Height = 100 };
        var tray = new ToolBarTray { Width = 60, Height = 100, Orientation = Orientation.Vertical };
        tray.ToolBars.Add(toolBar);
        toolBar.Items.Add(first);
        toolBar.Items.Add(second);
        CssBorderRadiusTests.Layout(tray, 60, 100);
        Assert.False(toolBar.HasOverflowItems);

        Css.SetStyle(toolBar, "border:8px solid transparent;padding:4px 6px 3px 12px");
        CssBorderRadiusTests.Layout(tray, 60, 100);
        var host = Assert.IsType<ToolBarPanel>(toolBar.GetVisualChild(0));
        Assert.Equal(new Rect(20, 12, 26, 77), host.VisualBounds);
        Assert.True(ToolBar.GetIsOverflowItem(second));

        Css.SetStyle(toolBar, "");
        CssBorderRadiusTests.Layout(tray, 60, 100);
        Assert.Equal(new Rect(0, 0, 60, 100), host.VisualBounds);
        Assert.False(toolBar.HasOverflowItems);
    }

    [Theory]
    [InlineData(Orientation.Horizontal)]
    [InlineData(Orientation.Vertical)]
    public void ToolBarTray_CssEdgesMoveBandsAndIncreaseIntrinsicSize(Orientation orientation)
    {
        var toolBar = new ToolBar { Width = 30, Height = 20 };
        var tray = new ToolBarTray { Width = Width, Height = Height, Orientation = orientation };
        tray.ToolBars.Add(toolBar);
        Css.SetStyle(tray, "border:8px solid transparent;padding:4px");
        CssBorderRadiusTests.Layout(tray, Width, Height);
        Assert.Equal(new Rect(12, 12, 30, 20), toolBar.VisualBounds);

        tray.Width = double.NaN;
        tray.Height = double.NaN;
        CssEvaluationScheduler.FlushIfPending(tray.Dispatcher);
        tray.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.Equal(new Size(54, 44), tray.DesiredSize);

        Css.SetStyle(tray, "");
        tray.Width = Width;
        tray.Height = Height;
        CssBorderRadiusTests.Layout(tray, Width, Height);
        Assert.Equal(new Rect(0, 0, 30, 20), toolBar.VisualBounds);
    }

    [Fact]
    public void ThemedToolBarAndTray_PaintThemeBackgroundAndReservePadding()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();
        try
        {
            var toolBar = new ToolBar { Width = Width, Height = Height };
            toolBar.Items.Add(new Button { Width = 20, Height = 20 });
            var toolBarHost = new Grid { Width = Width, Height = Height };
            toolBarHost.Children.Add(toolBar);
            CssBorderRadiusTests.Layout(toolBarHost, Width, Height);
            Assert.Same(app.Resources["CommandBarBackground"], toolBar.Background);
            Assert.Equal(new Thickness(6), toolBar.Padding);
            var host = Assert.IsType<ToolBarPanel>(toolBar.GetVisualChild(0));
            Assert.Equal(new Rect(6, 6, 88, 48), host.VisualBounds);
            Assert.Equal((byte)255,
                Channel(CssBorderRadiusTests.Pixels(toolBar, Width, Height), 50, 45, 3));

            Css.SetStyle(toolBar, "background:none;padding:10px");
            CssBorderRadiusTests.Layout(toolBarHost, Width, Height);
            Assert.Null(toolBar.Background);
            Assert.Equal(new Rect(10, 10, 80, 40), host.VisualBounds);

            Css.SetStyle(toolBar, "");
            CssBorderRadiusTests.Layout(toolBarHost, Width, Height);
            Assert.Same(app.Resources["CommandBarBackground"], toolBar.Background);
            Assert.Equal(new Rect(6, 6, 88, 48), host.VisualBounds);

            var tray = new ToolBarTray { Width = Width, Height = Height };
            var trayHost = new Grid { Width = Width, Height = Height };
            trayHost.Children.Add(tray);
            CssBorderRadiusTests.Layout(trayHost, Width, Height);
            Assert.Same(app.Resources["LayerFillColorAltBrush"], tray.Background);
            Assert.Equal((byte)255,
                Channel(CssBorderRadiusTests.Pixels(tray, Width, Height), 50, 45, 3));

            var nestedBar = new ToolBar { Width = 20, Height = 20 };
            tray.ToolBars.Add(nestedBar);
            Css.SetStyle(tray, "background:none;border:8px solid transparent;padding:4px");
            CssBorderRadiusTests.Layout(trayHost, Width, Height);
            Assert.Null(tray.Background);
            Assert.Equal(new Rect(12, 12, 20, 20), nestedBar.VisualBounds);
            Assert.Equal((byte)0,
                Channel(CssBorderRadiusTests.Pixels(tray, Width, Height), 50, 45, 3));

            Css.SetStyle(tray, "");
            CssBorderRadiusTests.Layout(trayHost, Width, Height);
            Assert.Same(app.Resources["LayerFillColorAltBrush"], tray.Background);
            Assert.Equal(new Rect(0, 0, 20, 20), nestedBar.VisualBounds);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private static FrameworkElement Create(bool tray) => tray
        ? new ToolBarTray { Width = Width, Height = Height }
        : new ToolBar { Width = Width, Height = Height };

    private static byte Channel(byte[] pixels, int x, int y, int channel)
        => pixels[(y * Width + x) * 4 + channel];

    private static void ResetApplicationState()
    {
        typeof(Application).GetField("_current", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, null);
        typeof(ThemeManager).GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static)
            ?.Invoke(null, null);
    }
}

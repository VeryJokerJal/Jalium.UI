using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssMenuBarBoxTests
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
    public void Bar_PaintsPositionedCssBackgroundLayers(bool command)
    {
        var bar = CreateBar(command);
        Css.SetStyle(bar, LayeredBackground);
        CssBorderRadiusTests.Layout(bar, Width, Height);

        var pixels = CssBorderRadiusTests.Pixels(bar, Width, Height);
        Assert.Equal((byte)0, Channel(pixels, 10, 30, 3));
        Assert.Equal((byte)255, Channel(pixels, 40, 20, 0));
        Assert.True(Channel(pixels, 70, 43, 2) > 100);
        Assert.True(Channel(pixels, 85, 43, 1) > 100);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Bar_PaintsEllipticalRadiusAndBorderSides(bool command)
    {
        var bar = CreateBar(command);
        Css.SetStyle(bar,
            "border:8px solid;border-color:red green blue yellow;" +
            "border-radius:50% / 30%;background:none");
        CssBorderRadiusTests.Layout(bar, Width, Height);

        var pixels = CssBorderRadiusTests.Pixels(bar, Width, Height);
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
    public void Bar_DoubleBorderLeavesMiddleBandClear(bool command)
    {
        var bar = CreateBar(command);
        Css.SetStyle(bar, "background:none;border:9px double red");
        CssBorderRadiusTests.Layout(bar, Width, Height);

        var pixels = CssBorderRadiusTests.Pixels(bar, Width, Height);
        Assert.Equal((byte)255, Channel(pixels, 50, 1, 2));
        Assert.Equal((byte)0, Channel(pixels, 50, 4, 3));
        Assert.Equal((byte)255, Channel(pixels, 50, 7, 2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Bar_ReflowsItemsIntoCssContentBoxAndBack(bool command)
    {
        var bar = CreateBar(command);
        FrameworkElement child;
        if (command)
        {
            ((CommandBar)bar).OverflowButtonVisibility = CommandBarOverflowButtonVisibility.Visible;
            Css.SetStyle(bar, "border:8px solid transparent;padding:4px 6px 3px 12px");
            CssBorderRadiusTests.Layout(bar, Width, Height);
            child = GetPrivateField<Button>(bar, "_moreButton");
            Assert.Equal(new Rect(46, 12, 40, 37), child.VisualBounds);
        }
        else
        {
            ((MenuBar)bar).Items.Add(new MenuBarItem { Title = "File" });
            Css.SetStyle(bar, "border:8px solid transparent;padding:4px 6px 3px 12px");
            CssBorderRadiusTests.Layout(bar, Width, Height);
            child = GetPrivateField<StackPanel>(bar, "_panel");
            Assert.Equal(new Rect(20, 12, 66, 37), child.VisualBounds);
        }

        Css.SetStyle(bar, "");
        CssBorderRadiusTests.Layout(bar, Width, Height);
        Assert.Equal(command ? 60.0 : 0.0, child.VisualBounds.X);
        Assert.Equal(0, child.VisualBounds.Y);
    }

    [Fact]
    public void CommandBar_BackgroundNoneSuppressesNativeFillAndRemovalRestoresIt()
    {
        var bar = (CommandBar)CreateBar(command: true);
        Css.SetStyle(bar, "background:none");
        CssBorderRadiusTests.Layout(bar, Width, Height);
        Assert.Equal((byte)0, Channel(CssBorderRadiusTests.Pixels(bar, Width, Height), 50, 30, 3));

        Css.SetStyle(bar, "");
        CssBorderRadiusTests.Layout(bar, Width, Height);
        Assert.Equal((byte)255, Channel(CssBorderRadiusTests.Pixels(bar, Width, Height), 50, 30, 3));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Bar_LocalBackgroundKeepsPriorityOverCssLayers(bool command)
    {
        var bar = CreateBar(command);
        bar.Background = Brushes.Red;
        Css.SetStyle(bar, "background-color:blue;background-image:linear-gradient(lime,lime)");
        CssBorderRadiusTests.Layout(bar, Width, Height);

        var pixels = CssBorderRadiusTests.Pixels(bar, Width, Height);
        Assert.Equal((byte)255, Channel(pixels, 40, 20, 2));
        Assert.Equal((byte)0, Channel(pixels, 40, 20, 1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Bar_LocalBorderBrushKeepsPriorityOverCssSideColors(bool command)
    {
        var bar = CreateBar(command);
        bar.BorderBrush = Brushes.Blue;
        Css.SetStyle(bar, "background:none;border:8px solid red");
        CssBorderRadiusTests.Layout(bar, Width, Height);

        var pixels = CssBorderRadiusTests.Pixels(bar, Width, Height);
        Assert.Equal((byte)255, Channel(pixels, 50, 4, 0));
        Assert.Equal((byte)0, Channel(pixels, 50, 4, 2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThemeBar_CssBackgroundOverridesStyleSetter(bool command)
    {
        ResetApplicationState();
        _ = new Application();
        try
        {
            var bar = CreateBar(command);
            Css.SetStyle(bar, LayeredBackground);
            var host = new Grid { Width = Width, Height = Height };
            host.Children.Add(bar);
            CssBorderRadiusTests.Layout(host, Width, Height);

            var pixels = CssBorderRadiusTests.Pixels(bar, Width, Height);
            Assert.Equal((byte)0, Channel(pixels, 10, 30, 3));
            Assert.Equal((byte)255, Channel(pixels, 40, 20, 0));
            Assert.True(Channel(pixels, 70, 43, 2) > 100);
            Assert.True(Channel(pixels, 85, 43, 1) > 100);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private static Control CreateBar(bool command) => command
        ? new CommandBar
        {
            Width = Width,
            Height = Height,
            OverflowButtonVisibility = CommandBarOverflowButtonVisibility.Collapsed,
        }
        : new MenuBar { Width = Width, Height = Height };

    private static T GetPrivateField<T>(object owner, string name)
        where T : class
    {
        var field = owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        return Assert.IsType<T>(field?.GetValue(owner));
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
}

using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssDirectControlBorderTests
{
    [Theory]
    [InlineData("ScrollViewer")]
    [InlineData("RichTextBox")]
    [InlineData("TabControl")]
    [InlineData("Label")]
    [InlineData("TextBox")]
    [InlineData("PasswordBox")]
    public void DirectControl_PaintsCssSideColors(string controlName)
    {
        Control control = controlName switch
        {
            "ScrollViewer" => new ScrollViewer(),
            "RichTextBox" => new RichTextBox(),
            "TabControl" => new TabControl(),
            "Label" => new Label(),
            "TextBox" => new TextBox(),
            "PasswordBox" => new PasswordBox(),
            _ => throw new ArgumentOutOfRangeException(nameof(controlName)),
        };
        control.Background = null;
        Css.SetStyle(control,
            "border:8px solid; border-color:red green blue yellow");
        CssBorderRadiusTests.Layout(control, 80, 60);
        var pixels = CssBorderRadiusTests.Pixels(control, 80, 60);
        Assert.Equal((byte)255, pixels[(4 * 80 + 40) * 4 + 2]);
        Assert.Equal((byte)128, pixels[(30 * 80 + 76) * 4 + 1]);
        Assert.Equal((byte)255, pixels[(56 * 80 + 40) * 4]);
        Assert.Equal((byte)255, pixels[(30 * 80 + 4) * 4 + 1]);
    }

    [Fact]
    public void ScrollViewer_DoubleBorderHasAnUnpaintedMiddleBand()
    {
        var viewer = new ScrollViewer { Background = null };
        Css.SetStyle(viewer, "border:9px double red");
        CssBorderRadiusTests.Layout(viewer, 80, 60);
        var pixels = CssBorderRadiusTests.Pixels(viewer, 80, 60);
        Assert.Equal((byte)255, pixels[(1 * 80 + 40) * 4 + 2]);
        Assert.Equal((byte)0, pixels[(4 * 80 + 40) * 4 + 3]);
        Assert.Equal((byte)255, pixels[(7 * 80 + 40) * 4 + 2]);
    }

    [Theory]
    [InlineData("Calendar", 400, 350)]
    [InlineData("StatusBar", 80, 60)]
    [InlineData("StatusBarItem", 80, 60)]
    [InlineData("ScrollBar", 80, 60)]
    public void AdditionalDirectControl_PaintsCssSideColors(string controlName, int width, int height)
    {
        Control control = controlName switch
        {
            "Calendar" => new Calendar(),
            "ScrollBar" => new ScrollBar(),
            "StatusBar" => new StatusBar(),
            "StatusBarItem" => new StatusBarItem(),
            _ => throw new ArgumentOutOfRangeException(nameof(controlName)),
        };
        control.Background = null;
        Css.SetStyle(control, "border:8px solid; border-color:red green blue yellow");
        CssBorderRadiusTests.Layout(control, width, height);
        var pixels = CssBorderRadiusTests.Pixels(control, width, height);
        Assert.Equal((byte)255, pixels[(4 * width + width / 2) * 4 + 2]);
        Assert.Equal((byte)128, pixels[(height / 2 * width + width - 4) * 4 + 1]);
        Assert.Equal((byte)255, pixels[((height - 4) * width + width / 2) * 4]);
        Assert.Equal((byte)255, pixels[(height / 2 * width + 4) * 4 + 1]);
    }
}

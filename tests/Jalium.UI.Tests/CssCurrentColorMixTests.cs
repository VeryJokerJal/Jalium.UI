using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssCurrentColorMixTests
{
    private static void Layout(FrameworkElement root)
    {
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        root.Measure(new Size(300, 200));
        root.Arrange(new Rect(0, 0, 300, 200));
        root.UpdateLayout();
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        Assert.False(CssEvaluationScheduler.HasPending(root.Dispatcher));
    }

    private static Color BrushColor(Brush? brush) => Assert.IsType<SolidColorBrush>(brush).Color;

    [Fact]
    public void BackgroundMix_UsesTheSameElementsWinningColorAndReevaluates()
    {
        const string mix = "color-mix(in srgb, currentColor 50%, green)";
        Assert.True(Css.Supports("background-color", mix));
        var text = new TextBlock { Text = "X" };
        Css.SetStyle(text, "color:red;background-color:" + mix);
        Layout(text);
        Assert.Equal(0xFF804000u, BrushColor(text.Background).ToArgb());

        Css.SetStyle(text, "color:blue;background-color:" + mix);
        Layout(text);
        Assert.Equal(0xFF004080u, BrushColor(text.Background).ToArgb());
    }

    [Fact]
    public void ColorMix_UsesInheritedColorAndHandlesNestedMixes()
    {
        // css/css-color/color-mix-currentcolor-nested-for-color-property.html.
        var child = new TextBlock { Text = "X" };
        var parent = new Button { Content = child };
        Css.SetStyle(parent, "color:color(srgb 0 0 1)");
        Css.SetStyle(child, "color:color-mix(in srgb, " +
            "color-mix(in srgb, currentColor 50%, color(srgb 0 1 0)), white)");
        Layout(parent);
        Assert.Equal(0xFF80BFBFu, BrushColor(child.Foreground).ToArgb());
    }

    [Fact]
    public void NativeForegroundValue_OutranksCssColorForCurrentColorMix()
    {
        var button = new Button { Foreground = new SolidColorBrush(Colors.Blue) };
        Css.SetStyle(button, "color:red;background-color:color-mix(in srgb,currentColor,white)");
        Layout(button);
        Assert.Equal(0xFF8080FFu, BrushColor(button.Background).ToArgb());
        Assert.Equal(Colors.Blue.ToArgb(), BrushColor(button.Foreground).ToArgb());
    }

    [Fact]
    public void VariableSubstitution_CanSupplyCurrentColorInsideAMix()
    {
        var button = new Button();
        Css.SetStyle(button, "--accent:currentColor;color:blue;" +
            "background-color:color-mix(in srgb,var(--accent),white)");
        Layout(button);
        Assert.Equal(0xFF8080FFu, BrushColor(button.Background).ToArgb());
    }

    [Fact]
    public void BorderAndOutlineMixes_UseCurrentColorIncludingEscapedIdentifier()
    {
        var button = new Button();
        Css.SetStyle(button, "color:blue;border:2px solid color-mix(in srgb,current\\43olor,white);" +
            "outline:2px solid color-mix(in srgb,currentColor,white)");
        Layout(button);
        Assert.Equal(0xFF8080FFu, BrushColor(button.BorderBrush).ToArgb());
        Assert.Equal(0xFF8080FFu, BrushColor(button.OutlineBrush).ToArgb());
    }

    [Fact]
    public void InheritedBackgroundMix_UsesTheChildsCurrentColor()
    {
        // css/css-color/color-mix-currentcolor-001.html: inherited mix stays live.
        var child = new Button();
        var parent = new Button { Content = child };
        Css.SetStyle(parent, "color:red;background-color:color-mix(in srgb,currentColor 50%,green)");
        Css.SetStyle(child, "color:green;background-color:inherit");
        Layout(parent);
        Assert.Equal(0xFF804000u, BrushColor(parent.Background).ToArgb());
        Assert.Equal(0xFF008000u, BrushColor(child.Background).ToArgb());

        Css.SetStyle(child, "color:blue;background-color:inherit");
        Layout(parent);
        Assert.Equal(0xFF004080u, BrushColor(child.Background).ToArgb());
        Css.SetStyle(child, "color:green;background-color:inherit");
        Layout(parent);

        Css.SetStyle(parent, "color:red;background-color:blue");
        Layout(parent);
        Assert.Equal(0xFF0000FFu, BrushColor(child.Background).ToArgb());

        var local = new SolidColorBrush(Colors.Gold);
        parent.Background = local;
        Layout(parent);
        Assert.Same(local, parent.Background);
        Assert.Equal(Colors.Gold.ToArgb(), BrushColor(child.Background).ToArgb());

        parent.ClearValue(Button.BackgroundProperty);
        Layout(parent);
        Assert.Equal(Colors.Blue.ToArgb(), BrushColor(child.Background).ToArgb());

        Css.SetStyle(child, "color:green;background-color:blue");
        Layout(parent);
        Assert.Empty(CssNode.Get(child).CssRuntimeState!.InheritedColorObservers!);
        Assert.Equal(Colors.Blue.ToArgb(), BrushColor(child.Background).ToArgb());
    }

    [Fact]
    public void InheritedDirectCurrentColor_RemainsSymbolicForTheChild()
    {
        var child = new Button();
        var parent = new Button { Content = child };
        Css.SetStyle(parent, "color:red;background-color:currentColor");
        Css.SetStyle(child, "color:green;background-color:inherit");
        Layout(parent);
        Assert.Equal(0xFFFF0000u, BrushColor(parent.Background).ToArgb());
        Assert.Equal(0xFF008000u, BrushColor(child.Background).ToArgb());
    }

    [Fact]
    public void InheritedBorderAndOutlineColors_ResolveAgainstTheChild()
    {
        var child = new Button();
        var parent = new Button { Content = child };
        Css.SetStyle(parent, "color:red;border:2px solid color-mix(in srgb,currentColor,white);" +
            "outline:2px solid currentColor");
        Css.SetStyle(child, "color:blue;border-color:inherit;outline-color:inherit");
        Layout(parent);
        Assert.Equal(0xFF8080FFu, BrushColor(child.BorderBrush).ToArgb());
        Assert.Equal(0xFF0000FFu, BrushColor(child.OutlineBrush).ToArgb());
    }

    [Fact]
    public void InheritedColorObserver_FollowsAReparentedChild()
    {
        var child = new Button();
        Css.SetStyle(child, "background-color:inherit");
        var first = new Button { Content = child };
        Css.SetStyle(first, "background-color:red");
        Layout(first);
        Assert.Equal(0xFFFF0000u, BrushColor(child.Background).ToArgb());

        first.Content = null;
        var second = new Button { Content = child };
        Css.SetStyle(second, "background-color:blue");
        Layout(second);
        Assert.Equal(0xFF0000FFu, BrushColor(child.Background).ToArgb());

        second.Background = new SolidColorBrush(Colors.Gold);
        Layout(second);
        Assert.Equal(Colors.Gold.ToArgb(), BrushColor(child.Background).ToArgb());
    }

    [Fact]
    public void MutableNativeForegroundBrush_RecomputesAMixWithoutReplacingTheProperty()
    {
        var foreground = new SolidColorBrush(Colors.Red);
        var button = new Button { Foreground = foreground };
        Css.SetStyle(button, "background-color:color-mix(in srgb,currentColor,white)");
        Layout(button);
        Assert.Equal(0xFFFF8080u, BrushColor(button.Background).ToArgb());

        foreground.Color = Colors.Blue;
        Layout(button);
        Assert.Same(foreground, button.Foreground);
        Assert.Equal(0xFF8080FFu, BrushColor(button.Background).ToArgb());

        var replacement = new SolidColorBrush(Colors.Green);
        button.Foreground = replacement;
        Layout(button);
        var fromReplacement = BrushColor(button.Background);
        Assert.Equal((byte)128, fromReplacement.R);
        Assert.InRange(fromReplacement.G, (byte)191, (byte)192);
        Assert.Equal((byte)128, fromReplacement.B);
        foreground.Color = Colors.Red;
        Layout(button);
        Assert.Equal(fromReplacement.ToArgb(), BrushColor(button.Background).ToArgb());

        Css.SetStyle(button, "background-color:blue");
        Layout(button);
        Assert.Empty(CssNode.Get(button).CssRuntimeState!.ColorBrushObservers!);
        replacement.Color = Colors.Red;
        Layout(button);
        Assert.Equal(Colors.Blue.ToArgb(), BrushColor(button.Background).ToArgb());
    }

    [Fact]
    public void MutableInheritedForegroundBrush_RecomputesAChildsColorMix()
    {
        var foreground = new SolidColorBrush(Colors.Red);
        var child = new TextBlock { Text = "X" };
        var parent = new Button { Foreground = foreground, Content = child };
        Css.SetStyle(child, "color:color-mix(in srgb,currentColor,white)");
        Layout(parent);
        Assert.Equal(0xFFFF8080u, BrushColor(child.Foreground).ToArgb());

        foreground.Color = Colors.Blue;
        Layout(parent);
        Assert.Equal(0xFF8080FFu, BrushColor(child.Foreground).ToArgb());
    }
}

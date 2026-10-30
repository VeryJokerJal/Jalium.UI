using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class CssMathFontSizeTests
{
    [Theory]
    [InlineData("font-size", "math")]
    [InlineData("font", "math serif")]
    [InlineData("math-depth", "0")]
    [InlineData("math-depth", "-2")]
    [InlineData("math-depth", "add(2)")]
    [InlineData("math-depth", "add(-2)")]
    [InlineData("math-depth", "calc(1 + 1)")]
    [InlineData("math-depth", "add(calc(1 + 1))")]
    [InlineData("math-depth", "auto-add")]
    [InlineData("math-style", "normal")]
    [InlineData("math-style", "compact")]
    public void MathFontSyntaxIsSupported(string property, string value)
        => Assert.True(Css.Supports(property, value));

    [Theory]
    [InlineData("math-depth", "add(1.5)")]
    [InlineData("math-depth", "add(1) extra")]
    [InlineData("math-depth", "1.0")]
    [InlineData("math-depth", "auto-add extra")]
    [InlineData("math-style", "display")]
    [InlineData("font-size", "math extra")]
    public void InvalidMathFontSyntaxIsRejected(string property, string value)
        => Assert.False(Css.Supports(property, value));

    [Fact]
    public void MathDepthAndInheritedMathStyleDetermineSizeBeforeFontSize()
    {
        var grandchild = new TextBlock { Text = "x" };
        var child = new Border { Child = grandchild };
        var root = new Border { Child = child };
        Css.SetStyle(root, "font-size:100px;math-depth:1;math-style:compact");
        Css.SetStyle(child, "font-size:math;math-style:normal;math-depth:auto-add");
        Css.SetStyle(grandchild, "font-size:math;math-depth:auto-add");
        Layout(root);

        Assert.Equal(2, child.GetValue(CssMathProperties.DepthProperty));
        Assert.Equal(2, grandchild.GetValue(CssMathProperties.DepthProperty));
        Assert.Equal(71, (double)child.GetValue(TextElement.FontSizeProperty)!, 8);
        Assert.Equal(71, grandchild.FontSize, 8);

        Css.SetStyle(root, "font-size:100px;math-depth:1;math-style:normal");
        Layout(root);
        Assert.Equal(1, child.GetValue(CssMathProperties.DepthProperty));
        Assert.Equal(1, grandchild.GetValue(CssMathProperties.DepthProperty));
        Assert.Equal(100, (double)child.GetValue(TextElement.FontSizeProperty)!, 8);
        Assert.Equal(100, grandchild.FontSize, 8);
    }

    [Fact]
    public void FontShorthandUsesComputedDepthAndRevertsWhenDepthIsRemoved()
    {
        var child = new TextBlock { Text = "x" };
        var root = new Border { Child = child };
        Css.SetStyle(root, "font-size:100px;math-depth:0");
        Css.SetStyle(child, "font:math serif;math-depth:add(2)");
        Layout(root);

        Assert.Equal(2, child.GetValue(CssMathProperties.DepthProperty));
        Assert.Equal(50.41, child.FontSize, 8);

        Css.SetStyle(child, "font:math serif");
        Layout(root);
        Assert.Equal(0, child.GetValue(CssMathProperties.DepthProperty));
        Assert.Equal(100, child.FontSize, 8);

        Css.SetStyle(child, "font-size:math;math-depth:add(-1)");
        Layout(root);
        Assert.Equal(100 / 0.71, child.FontSize, 8);
    }

    [Fact]
    public void ZeroInheritedSizeRemainsZeroAndWideDepthValuesCompute()
    {
        var child = new TextBlock { Text = "x" };
        var root = new Border { Child = child };
        Css.SetStyle(root, "font-size:0;math-depth:3");
        Css.SetStyle(child, "font-size:math;math-depth:initial");
        Layout(root);
        Assert.Equal(0, child.GetValue(CssMathProperties.DepthProperty));
        Assert.Equal(0, child.FontSize);

        Css.SetStyle(root, "font-size:100px;math-depth:3");
        Css.SetStyle(child, "font-size:math;math-depth:inherit");
        Layout(root);
        Assert.Equal(3, child.GetValue(CssMathProperties.DepthProperty));
        Assert.Equal(100, child.FontSize, 8);
    }

    [Fact]
    public void MathValuesParticipateInComputedStyleQueries()
    {
        var target = new Border();
        var container = new StackPanel();
        container.Children.Add(target);
        var root = new Border { Child = container };
        Css.SetClass(target, "target");
        Css.SetStyle(root, "font-size:100px;math-depth:0;math-style:compact");
        Css.SetStyle(container, "container-type:normal;font-size:math;math-depth:auto-add");
        Css.SetStyleSheet(root,
            ".target { width:11px } @container style(font-size:math) and style(math-depth:add(1)) and style(math-style:compact) { .target { width:101px } }");
        Layout(root);
        Assert.Equal(101, target.Width);

        Css.SetStyle(root, "font-size:100px;math-depth:0;math-style:normal");
        Layout(root);
        Assert.Equal(11, target.Width);
    }

    [Fact]
    public void NativeDepthAndParentSizeChangesRecomputeMathFontSize()
    {
        var child = new TextBlock { Text = "x" };
        var root = new Border { Child = child };
        Css.SetStyle(root, "font-size:100px;math-depth:0");
        Css.SetStyle(child, "font-size:math;math-depth:add(2)");
        Layout(root);
        Assert.Equal(50.41, child.FontSize, 8);

        child.SetValue(CssMathProperties.DepthProperty, 1);
        Layout(root);
        Assert.Equal(71, child.FontSize, 8);

        Css.SetStyle(root, "font-size:200px;math-depth:0");
        Layout(root);
        Assert.Equal(142, child.FontSize, 8);

        child.ClearValue(CssMathProperties.DepthProperty);
        Layout(root);
        Assert.Equal(100.82, child.FontSize, 8);
    }

    [Fact]
    public void MathFontSizeWorksOnDocumentRuns()
    {
        var run = new Run("x");
        var text = new TextBlock();
        text.Inlines.Add(run);
        var root = new Border { Child = text };
        Css.SetStyle(text, "font-size:100px;math-depth:0");
        Css.SetStyle(run, "font-size:math;math-depth:add(1)");
        Layout(root);

        Assert.Equal(1, run.GetValue(CssMathProperties.DepthProperty));
        Assert.Equal(71, run.FontSize, 8);

        Css.SetStyle(text, "font-size:200px;math-depth:0");
        Layout(root);
        Assert.Equal(142, run.FontSize, 8);
    }

    [Theory]
    [InlineData(RenderBackend.Software)]
    [InlineData(RenderBackend.D3D12)]
    [InlineData(RenderBackend.Vulkan)]
    public void InstalledMathFontConstantsControlDepthTransitions(RenderBackend backend)
    {
        if (!OperatingSystem.IsWindows() ||
            !File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "Fonts", "cambria.ttc"))) return;
        AssertMathFontDepthTransitions(backend);
    }

    [Fact]
    public void LinuxInstalledMathFontConstantsControlDepthTransitions()
    {
        // Opt in after exposing Cambria Math to fontconfig.
        if (!OperatingSystem.IsLinux() ||
            Environment.GetEnvironmentVariable("JALIUM_TEST_CAMBRIA_MATH") != "1") return;
        AssertMathFontDepthTransitions(RenderBackend.Software);
    }

    private static void AssertMathFontDepthTransitions(RenderBackend backend)
    {
        using var context = RenderContext.GetOrCreateCurrent(backend, forceReplace: true);
        Assert.Equal(backend, context.Backend);
        var constants = TextMeasurement.GetFontMathConstants("Cambria Math");
        Assert.True(constants.HasMathTable);
        Assert.True(Math.Abs(constants.ScriptPercentScaleDown - .71f) > .005f ||
            Math.Abs(constants.ScriptScriptPercentScaleDown - .5041f) > .005f);

        var child = new TextBlock { Text = "x" };
        var root = new Border { Child = child };
        Css.SetStyle(root, "font-family:'Cambria Math';font-size:100px;math-depth:0");
        Css.SetStyle(child, "font-family:Arial;font-size:math;math-depth:add(1)");
        Layout(root);
        Assert.Equal(100 * constants.ScriptPercentScaleDown, child.FontSize, 4);

        Css.SetStyle(child, "font-size:math;math-depth:add(2)");
        Layout(root);
        Assert.Equal(100 * constants.ScriptScriptPercentScaleDown, child.FontSize, 4);

        Css.SetStyle(root, "font-family:'Cambria Math';font-size:100px;math-depth:2");
        Css.SetStyle(child, "font-size:math;math-depth:add(-1)");
        Layout(root);
        Assert.Equal(100 * constants.ScriptPercentScaleDown /
            constants.ScriptScriptPercentScaleDown, child.FontSize, 4);

        Css.SetStyle(child, "font-size:math;math-depth:add(1)");
        Layout(root);
        Assert.Equal(71, child.FontSize, 4);
    }

    private static void Layout(Border root)
    {
        root.Measure(new Size(500, 500));
        root.Arrange(new Rect(0, 0, 500, 500));
        root.UpdateLayout();
    }
}

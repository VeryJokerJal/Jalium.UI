using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssComputedValueTests
{
    static CssComputedValueTests() => System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(
        typeof(Jalium.UI.Markup.TypeConverterRegistry).Module.ModuleHandle);

    private static void Layout(FrameworkElement root, double width = 400, double height = 200)
    {
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
    }

    [Fact]
    public void Variables_AreCaseSensitiveAndResolveBeforeShorthandExpansion()
    {
        var border = new Border();
        Css.SetStyle(border, "--Space: 10px 20px; --space: 3px; margin: var(--Space); margin-left: var(--space)");
        Assert.Equal(new Thickness(3, 10, 20, 10), border.Margin);
        Css.SetStyle(border, "margin-left: 3px; --space: 10px 20px; margin: var(--space)");
        Assert.Equal(new Thickness(20, 10, 20, 10), border.Margin);
    }

    [Fact]
    public void Variables_InheritComputedTokensAndRecomputeAfterParentChanges()
    {
        var child = new Border();
        var middle = new Border { Child = child };
        var parent = new Border { Child = middle };
        Css.SetStyle(parent, "--size: 25px; --alias: var(--size)");
        Css.SetStyle(child, "--size: 80px; width: var(--alias)");
        Layout(parent);
        Assert.Equal(25, child.Width);
        Css.SetStyle(parent, "--size: 40px; --alias: var(--size)");
        Layout(parent);
        Assert.Equal(40, child.Width);
    }

    [Theory]
    [InlineData("--a:var(--a, 80px)")]
    [InlineData("--a:var(--b); --b:var(--a)")]
    [InlineData("--b:10px; --a:var(--b, var(--a))")]
    public void VariableCycles_AreInvalidIncludingUnusedFallbackEdges(string declarations)
    {
        var border = new Border();
        Css.SetStyle(border, declarations + "; width:var(--a, 32px)");
        Assert.Equal(32, border.Width);
    }

    [Fact]
    public void InvalidAtComputedTime_DoesNotRestoreEarlierDeclaration()
    {
        var border = new Border();
        Css.SetStyle(border, "width:80px; width:var(--missing)");
        Assert.True(double.IsNaN(border.Width));
        Css.SetStyle(border, "--number:20; width:80px; width:var(--number)px");
        Assert.True(double.IsNaN(border.Width));
    }

    [Fact]
    public void RootSelector_IsStructuralAndVariablesHonorImportant()
    {
        var child = new Border();
        var root = new Border { Child = child };
        Css.SetStyleSheet(root, ":root { --size:60px !important; opacity:.5 } Border { width:var(--size) }");
        Css.SetStyle(root, "--size:20px");
        Layout(root);
        Assert.Equal(.5, root.Opacity);
        Assert.Equal(1, child.Opacity);
        Assert.Equal(60, child.Width);
    }

    [Theory]
    [InlineData("calc(100% - 20px)", 380)]
    [InlineData("min(50%, 120px)", 120)]
    [InlineData("max(25%, 160px)", 160)]
    [InlineData("clamp(120px, 50%, 180px)", 180)]
    [InlineData("calc((100% - 40px) / 2)", 180)]
    [InlineData("calc(2 * 30px + 10%)", 100)]
    public void MathLengths_ResolveAgainstTheContainingBlock(string expression, double expected)
    {
        var child = new Border();
        var root = new Border { Child = child };
        Css.SetStyle(child, "width:" + expression);
        Layout(root);
        Assert.Equal(expected, child.ActualWidth, 6);
        Layout(root, 600);
        Assert.True(double.IsFinite(child.ActualWidth));
    }

    [Theory]
    [InlineData("calc(10px + 2)")]
    [InlineData("calc(2px * 3px)")]
    [InlineData("calc(100%-20px)")]
    [InlineData("clamp(1px, 2px)")]
    public void InvalidMath_DoesNotAssignAWidth(string expression)
    {
        var border = new Border();
        Css.SetStyle(border, "width:" + expression);
        Assert.True(double.IsNaN(border.Width));
    }

    [Fact]
    public void MathMargins_KeepPercentagesUntilLayout()
    {
        var child = new Border { HorizontalAlignment = HorizontalAlignment.Left };
        var root = new Border { Child = child };
        Css.SetStyle(child, "height:20px; margin:calc(10% + 4px); width:calc(100% - 100px)");
        Layout(root);
        Assert.Equal(44, child.VisualBounds.X, 6);
        Assert.Equal(300, child.ActualWidth, 6);
    }

    [Fact]
    public void LocalValues_OutrankCssAndEstablishTheEmBasis()
    {
        var child = new TextBlock { Width = 100, FontSize = 20 };
        Css.SetStyle(child, "width:calc(50% + 20px); font-size:10px; margin:2em");
        Layout(child);
        Assert.Equal(100, child.Width);
        Assert.Equal(new Thickness(40), child.Margin);
    }

    [Fact]
    public void GlobalKeywords_InheritAndResetPropertiesAndIndividualEdges()
    {
        var child = new Border();
        var root = new Border { Child = child, Opacity = .7, Margin = new Thickness(12) };
        Css.SetStyle(child, "opacity:inherit; margin:3px; margin-left:inherit");
        Assert.Equal(.7, child.Opacity);
        Assert.Equal(new Thickness(12, 3, 3, 3), child.Margin);
        Css.SetStyle(child, "opacity:initial; margin:initial");
        Assert.Equal(1, child.Opacity);
        Assert.Equal(default, child.Margin);
    }

    [Fact]
    public void ViewportUnits_ResolveUsingRootDimensions()
    {
        var child = new Border();
        var root = new Border { Width = 400, Height = 200, Child = child };
        Css.SetStyle(child, "width:25vw; height:50vh");
        Layout(root);
        Assert.Equal(100, child.Width);
        Assert.Equal(100, child.Height);
    }

    [Theory]
    [InlineData("0 auto", 150)]
    [InlineData("0 0 0 auto", 300)]
    [InlineData("0 auto 0 0", 0)]
    public void AutoMargins_DistributeRemainingHorizontalSpace(string margin, double x)
    {
        var child = new Border { HorizontalAlignment = HorizontalAlignment.Left };
        var root = new Border { Child = child };
        Css.SetStyle(child, "width:100px; height:20px; margin:" + margin);
        Layout(root);
        Assert.Equal(x, child.VisualBounds.X, 6);
    }

    [Fact]
    public void PercentageTranslation_RecomputesAfterElementResizing()
    {
        var child = new Border(); var root = new Border { Child = child };
        Css.SetStyle(child, "width:50%; height:40px; transform:translate(-50%, calc(50% + 2px))");
        Layout(root);
        var transform = Assert.IsType<TranslateTransform>(child.RenderTransform);
        Assert.Equal(-100, transform.X); Assert.Equal(22, transform.Y);
        Layout(root, 600);
        Assert.Equal(-150, Assert.IsType<TranslateTransform>(child.RenderTransform).X);
    }

    [Fact]
    public void EmptyCustomValue_DoesNotUseTheVarFallback()
    {
        var child = new Border(); Css.SetStyle(child, "--empty:; width:var(--empty, 30px)");
        Assert.True(double.IsNaN(child.Width));
    }

    [Fact]
    public void FontRelativeLengths_RecomputeWhenInheritedNativeFontChanges()
    {
        var child = new Border(); var root = new Border { Child = child };
        Css.SetStyle(child, "width:2em"); Layout(root);
        Jalium.UI.Documents.TextElement.SetFontSize(root, 24);
        Layout(root);
        Assert.Equal(48, child.Width);
    }

    [Fact]
    public void LocalSizing_IncludingAuto_IgnoresCssContentBoxAndPercentageSentinels()
    {
        var fixedChild = new Border { Width = 100 };
        Css.SetStyle(fixedChild, "width:200px; box-sizing:content-box; padding:20px");
        Layout(fixedChild);
        Assert.Equal(100, fixedChild.ActualWidth);

        var autoChild = new Border { Width = double.NaN, HorizontalAlignment = HorizontalAlignment.Left };
        var root = new Border { Child = autoChild };
        Css.SetStyle(autoChild, "width:50%"); Layout(root);
        Assert.Equal(0, autoChild.ActualWidth);
    }

    [Fact]
    public void CalculatedNegativeSizes_ClampAtUsedValueTime()
    {
        var child = new Border(); var root = new Border { Child = child };
        Css.SetStyle(child, "width:calc(10% - 100px)"); Layout(root);
        Assert.Equal(0, child.ActualWidth);
        Css.SetStyle(child, "width:20px; width:-5px");
        Assert.Equal(20, child.Width);
    }

    [Fact]
    public void AHiddenBorder_TakesNoSpaceRegardlessOfDeclarationOrder()
    {
        var child = new Border();
        Css.SetStyle(child, "border-style:none; border-width:5px; border-color:red");
        Assert.Null(child.BorderBrush); Assert.Equal(default, child.BorderThickness);
    }

    [Fact]
    public void RepeatedEvaluation_ReusesComputedColorResources()
    {
        var child = new Border();
        Css.SetStyle(child, "--brand:red; color:var(--brand); background:currentcolor");
        var brush = child.Background;
        for (var i = 0; i < 5; i++) CssEngine.EvaluateElement(child);
        Assert.Same(brush, child.Background);
    }

    [Fact]
    public void EscapedPropertyAndFunctionNames_ResolveToTheSameIdentifiers()
    {
        var child = new Border();
        Css.SetStyle(child, @"--\53 pace:12px; w\69 dth:v\61 r(--Space); c\6f lor:r\65 d");
        Assert.Equal(12, child.Width);
        Assert.Equal(Color.FromRgb(255, 0, 0), Assert.IsType<SolidColorBrush>(Jalium.UI.Documents.TextElement.GetForeground(child)).Color);
    }
}

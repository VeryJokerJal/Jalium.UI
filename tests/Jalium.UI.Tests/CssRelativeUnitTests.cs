using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class CssRelativeUnitTests
{
    private static void Layout(FrameworkElement root, double width = 400, double height = 200)
    {
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
    }

    [Theory]
    [InlineData("ex", 0)]
    [InlineData("cap", 1)]
    [InlineData("ch", 2)]
    [InlineData("ic", 3)]
    public void FontRulers_UseTheCurrentNativeFontAndRecomputeAfterSizeChanges(string unit, int ruler)
    {
        var text = new TextBlock { FontSize = 20 };
        Css.SetStyle(text, "font-family:Consolas; width:calc(3" + unit + " + 2px)");
        double Expected()
        {
            var metrics = TextMeasurement.GetFontUnitMetrics(text.FontFamily.Source, text.FontSize,
                text.FontWeight.ToOpenTypeWeight(), 0);
            return 2 + 3d * (ruler switch { 0 => metrics.XHeight, 1 => metrics.CapHeight, 2 => metrics.ZeroAdvance, _ => metrics.IdeographicAdvance });
        }
        Layout(text); Assert.Equal(Expected(), text.ActualWidth, 5);
        text.FontSize = 30; Layout(text); Assert.Equal(Expected(), text.ActualWidth, 5);
    }

    [Theory]
    [InlineData("rex", "ex")]
    [InlineData("rcap", "cap")]
    [InlineData("rch", "ch")]
    [InlineData("ric", "ic")]
    public void RootFontRulers_IgnoreChildFontChanges(string rootUnit, string localUnit)
    {
        var child = new Border(); var root = new Border { Child = child };
        Css.SetStyle(root, "font-size:20px; font-family:Consolas");
        Css.SetStyle(child, "font-size:10px; width:4" + rootUnit);
        Layout(root);
        var reader = new CssTokenReader("4" + localUnit);
        Assert.True(reader.TryReadLength(out var length));
        Assert.True(length.TryResolve(CssEngine.BuildLengthContext(root), CssPercentBasis.NotSupported, out var expected));
        Assert.Equal(expected, child.ActualWidth, 5);
        Css.SetStyle(child, "font-size:40px; width:4" + rootUnit); Layout(root);
        Assert.Equal(expected, child.ActualWidth, 5);
        Css.SetStyle(root, "font-size:30px; font-family:Consolas"); Layout(root);
        Assert.True(length.TryResolve(CssEngine.BuildLengthContext(root), CssPercentBasis.NotSupported, out expected));
        Assert.Equal(expected, child.ActualWidth, 5);
    }

    [Fact]
    public void RootFontSize_RemUsesInitialSizeAndDoesNotGrowOnReevaluation()
    {
        var root = new TextBlock();
        Css.SetStyle(root, "font-size:2rem; width:1rem; line-height:2; height:1lh");
        for (var i = 0; i < 8; i++)
        {
            Layout(root);
            Assert.Equal(28, root.FontSize); Assert.Equal(28, root.ActualWidth); Assert.Equal(56, root.ActualHeight);
            CssEngine.EvaluateElement(root);
        }
    }

    [Fact]
    public void LineHeightUnits_UseTheParentLineForLineHeightAndTheOwnLineForOtherProperties()
    {
        var child = new TextBlock(); var parent = new Border { Child = child };
        Css.SetStyle(parent, "font-size:20px; line-height:2");
        Css.SetStyle(child, "font-size:10px; line-height:1.5lh; width:1rlh; height:1lh");
        Layout(parent);
        Assert.Equal(60, child.LineHeight); Assert.Equal(60, child.ActualHeight); Assert.Equal(40, child.ActualWidth);
        Css.SetStyle(parent, "font-size:30px; line-height:2"); Layout(parent);
        Assert.Equal(90, child.LineHeight); Assert.Equal(90, child.ActualHeight); Assert.Equal(60, child.ActualWidth);
    }

    [Fact]
    public void RootLineHeight_RlhUsesInitialMetricsOnItsOwnDeclaration()
    {
        var root = new TextBlock();
        var initial = TextMeasurement.GetFontUnitMetrics(SystemFonts.MessageFontFamily.Source, 14).LineHeight;
        Css.SetStyle(root, "font-size:20px; line-height:2rlh; height:1lh");
        for (var i = 0; i < 4; i++)
        {
            Layout(root); Assert.Equal(initial * 2d, root.LineHeight, 5); Assert.Equal(initial * 2d, root.ActualHeight, 5);
            CssEngine.EvaluateElement(root);
        }
    }

    [Fact]
    public void DocumentFontUnits_UseParentMetricsAndPreserveTextAndBindings()
    {
        var run = new Run("原始内容"); var paragraph = new Paragraph { FontSize = 20 };
        paragraph.Inlines.Add(run);
        Css.SetStyle(run, "font-size:2ch");
        CssEvaluationScheduler.FlushIfPending(run.Dispatcher);
        var expected = TextMeasurement.GetFontUnitMetrics(paragraph.FontFamily.Source, 20).ZeroAdvance * 2d;
        Assert.Equal(expected, run.FontSize, 5); Assert.Equal("原始内容", run.Text);
        var source = new TextBlock { FontSize = 30 };
        var binding = run.SetBinding(TextElement.FontSizeProperty, new Binding(nameof(TextBlock.FontSize)) { Source = source });
        Css.SetStyle(run, "font-size:4cap !important");
        Assert.Equal(30, run.FontSize); Assert.Same(binding, run.GetBindingExpression(TextElement.FontSizeProperty));
    }

    [Theory]
    [InlineData("lh", "line-height", "line-height:normal")]
    [InlineData("rlh", "line-height", "line-height:normal")]
    [InlineData("ch", "font-family", "font-family:serif")]
    public void RegisteredFontDependencies_RecoverCyclesInsteadOfGrowing(string unit, string property, string baseline)
    {
        var root = new TextBlock();
        Css.SetStyleSheet(root, "@property --ruler {syntax:'<length>'; inherits:false; initial-value:12px}");
        Css.SetStyle(root, baseline + "; --ruler:2" + unit + "; " + property + ":var(--ruler)");
        Layout(root);
        Assert.Equal("12px", root.CssRuntimeState!.CustomProperties!["--ruler"]);
        var before = root.LineHeight;
        for (var i = 0; i < 4; i++) { CssEngine.EvaluateElement(root); Layout(root); }
        Assert.Equal(before, root.LineHeight);
    }

    [Theory]
    [InlineData("svw", 60)] [InlineData("svh", 40)] [InlineData("svi", 60)] [InlineData("svb", 40)]
    [InlineData("svmin", 40)] [InlineData("svmax", 60)]
    [InlineData("lvw", 80)] [InlineData("lvh", 60)] [InlineData("lvi", 80)] [InlineData("lvb", 60)]
    [InlineData("lvmin", 60)] [InlineData("lvmax", 80)]
    [InlineData("dvw", 70)] [InlineData("dvh", 50)] [InlineData("dvi", 70)] [InlineData("dvb", 50)]
    [InlineData("dvmin", 50)] [InlineData("dvmax", 70)]
    [InlineData("vw", 80)] [InlineData("vh", 60)] [InlineData("vi", 80)] [InlineData("vb", 60)]
    [InlineData("vmin", 60)] [InlineData("vmax", 80)]
    public void ViewportVariants_ResolveAgainstTheHostSuppliedRuler(string unit, double expected)
    {
        var child = new Border(); var root = new Border { Child = child };
        Css.SetViewportMetrics(root, new(new Size(600, 400), new Size(800, 600), new Size(700, 500)));
        Css.SetStyle(child, "width:10" + unit); Layout(root);
        Assert.Equal(expected, child.ActualWidth, 6);
    }

    [Fact]
    public void ViewportChanges_UpdateDynamicUnitsAndPreserveNativeBindings()
    {
        var child = new Border(); var root = new Border { Child = child };
        Css.SetViewportMetrics(root, new(new Size(600, 400), new Size(800, 600), new Size(700, 500)));
        Css.SetStyle(child, "width:10dvw; height:10svh; margin-left:1lvw"); Layout(root);
        Assert.Equal(70, child.ActualWidth); Assert.Equal(40, child.ActualHeight);
        Css.SetViewportMetrics(root, new(new Size(600, 400), new Size(800, 600), new Size(800, 600))); Layout(root);
        Assert.Equal(80, child.ActualWidth); Assert.Equal(40, child.ActualHeight);
        var source = new Border { Width = 99 };
        var binding = child.SetBinding(FrameworkElement.WidthProperty, new Binding(nameof(Border.Width)) { Source = source });
        Css.SetViewportMetrics(root, null); Layout(root);
        Assert.Equal(99, child.ActualWidth); Assert.Same(binding, child.GetBindingExpression(FrameworkElement.WidthProperty));
    }

    [Fact]
    public void LogicalViewportAxes_UseTheExplicitHostOrientation()
    {
        var child = new Border(); var root = new Border { Child = child };
        Css.SetViewportMetrics(root, new(new Size(600, 400), new Size(800, 600), new Size(700, 500), isVertical: true));
        Css.SetStyle(child, "width:10vi; height:10dvb"); Layout(root);
        Assert.Equal(60, child.ActualWidth); Assert.Equal(70, child.ActualHeight);
    }

    [Fact]
    public void EmptyViewportUnits_AreZeroAndContainerFallbackUsesTheSmallViewport()
    {
        var child = new Border(); var root = new Border { Child = child };
        Css.SetViewportMetrics(root, new(new Size(0, 0), new Size(0, 0), new Size(0, 0)));
        Css.SetStyle(child, "width:100vw; height:100dvh"); Layout(root);
        Assert.Equal(0, child.ActualWidth); Assert.Equal(0, child.ActualHeight);
        Css.SetViewportMetrics(root, new(new Size(600, 400), new Size(800, 600), new Size(700, 500)));
        Css.SetStyle(child, "width:10cqw"); Layout(root);
        Assert.Equal(60, child.ActualWidth);
    }

    [Fact]
    public void FontCacheChanges_InvalidateDependentStyles()
    {
        var child = new Border(); var root = new Border { Child = child };
        Css.SetStyle(child, "width:calc(50% + 1ch)"); Layout(root);
        TextMeasurement.ClearCache();
        Assert.True(CssEvaluationScheduler.HasPending(child.Dispatcher));
        Assert.False(child.IsMeasureValid);
        Layout(root); Assert.True(double.IsFinite(child.ActualWidth));
    }

    [Fact]
    public void RemovingFontUnitDeclarations_DropsTheFontResourceDependency()
    {
        var child = new Border(); var root = new Border { Child = child };
        Css.SetStyle(child, "width:3ch"); Layout(root);
        Css.SetStyle(child, "width:30px"); Layout(root);
        TextMeasurement.ClearCache();
        Assert.True(child.IsMeasureValid);
        Assert.Equal(30, child.ActualWidth);
    }
}

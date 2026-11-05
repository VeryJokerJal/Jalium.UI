using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssGeneratedContentTests
{
    [Theory]
    [InlineData("StackPanel::before", true)]
    [InlineData("StackPanel:after", true)]
    [InlineData(".host:hover::after", true)]
    [InlineData(".host::before > Border", false)]
    [InlineData(".host::before.after", false)]
    [InlineData(".host::before::after", false)]
    [InlineData(":has(::before)", false)]
    [InlineData(":not(::after)", false)]
    public void GeneratedSelectorsEnforceSubjectAndFunctionGrammar(string selector, bool valid)
    {
        Assert.Equal(valid, CssSelectorParser.ParseGroup(selector, out _) is not null);
    }

    [Fact]
    public void NativeStackIncludesGeneratedBoxesWithoutChangingItsChildren()
    {
        var root = Host(); var child = new Border { Height = 20 }; root.Children.Add(child);
        Add(root, """
            .host::before { content: "before"; width: 30px; height: 12px; }
            .host::after { content: "after"; width: 40px; height: 8px; }
            """);
        Layout(root);
        var before = Before(root); var after = After(root);
        Assert.Equal("before", before.Text); Assert.Equal("after", after.Text);
        Assert.Single(root.Children); Assert.Same(child, root.Children[0]);
        Assert.Equal(0, before.VisualBounds.Y, 6); Assert.Equal(12, child.VisualBounds.Y, 6);
        Assert.Equal(32, after.VisualBounds.Y, 6);
        Assert.Equal(40, root.DesiredSize.Height, 6);
    }

    [Theory]
    [InlineData("display: block;", 10, 30)]
    [InlineData("display: flex;", 10, 30)]
    [InlineData("display: grid; grid-template-columns: 10px 20px 30px;", 10, 30)]
    public void GeneratedBoxesParticipateInCssFormatting(string containerStyle, double childX, double afterX)
    {
        var root = Host(); var child = new Border(); Css.SetClass(child, "real"); root.Children.Add(child);
        Add(root, $$"""
            .host { {{containerStyle}} }
            .host::before { content: ""; display: inline-block; width: 10px; height: 12px; vertical-align: top; }
            .host > .real { display: inline-block; width: 20px; height: 12px; vertical-align: top; }
            .host::after { content: ""; display: inline-block; width: 30px; height: 12px; vertical-align: top; }
            """);
        Layout(root);
        Assert.Equal(childX, child.VisualBounds.X, 6);
        Assert.Equal(afterX, After(root).VisualBounds.X, 6);
        Assert.Equal(0, Before(root).VisualBounds.Y, 6);
    }

    [Fact]
    public void GeneratedBoxesInheritFontsColorAndVariablesFromTheirOrigin()
    {
        var root = Host();
        Add(root, """
            .host { color: red; font-size: 20px; --label: "继承"; }
            .host::before { content: var(--label); }
            TextBlock { color: blue; }
            """);
        Layout(root);
        var before = Before(root);
        Assert.Equal("继承", before.Text);
        Assert.Equal(20, before.FontSize, 6);
        Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(before.Foreground).Color);
        Css.SetStyle(root, "color: green; font-size: 24px; --label: 'changed';");
        Layout(root);
        Assert.Equal("changed", before.Text);
        Assert.Equal(24, before.FontSize, 6);
        Assert.Equal(Colors.Green, Assert.IsType<SolidColorBrush>(before.Foreground).Color);
    }

    [Fact]
    public void GeneratedBoxesDoNotAffectEmptyChildIndexOrHasSelectors()
    {
        var root = Host();
        Add(root, """
            .host:empty { width: 80px; }
            .host:empty::before { content: "x"; height: 12px; }
            .host:has(> TextBlock) { width: 190px; }
            .host > Border:first-child:nth-child(1):last-child { width: 31px; }
            """);
        Layout(root);
        Assert.Equal(80, root.Width, 6); Assert.Equal("x", Before(root).Text);
        Layout(root); Assert.Equal("x", Before(root).Text);
        var real = new Border { Height = 10 }; root.Children.Add(real);
        Layout(root);
        Assert.True(double.IsNaN(root.Width)); Assert.Equal(31, real.Width, 6);
        Assert.Equal(Visibility.Collapsed, Before(root).Visibility);
    }

    [Fact]
    public void HoverCanGenerateAndRemoveContentWithoutAccumulatingBoxes()
    {
        var root = Host(); root.Children.Add(new Border { Height = 20 });
        Add(root, ".host:hover::before { content: 'hover'; height: 12px; }");
        Layout(root);
        var before = Before(root);
        Assert.Equal(Visibility.Collapsed, before.Visibility); Assert.Equal(20, root.DesiredSize.Height, 6);
        foreach (var hovered in new[] { true, false, true, false, true })
        {
            root.SetIsMouseOver(hovered); Layout(root);
            Assert.Same(before, Before(root));
            Assert.Equal(hovered ? Visibility.Visible : Visibility.Collapsed, before.Visibility);
            Assert.Equal(hovered ? 32 : 20, root.DesiredSize.Height, 6);
            Assert.Single(root.Children);
        }
    }

    [Fact]
    public void AttrReadsAndObservesTheOriginIncludingFallbackAndNativeAttributes()
    {
        var root = Host(); root.Tag = "标签";
        Css.GetAttributes(root)["data-label"] = "first";
        Add(root, ".host::before { content: '[' attr(data-label, 'missing') ']' attr(Tag); }");
        Layout(root);
        var before = Before(root); Assert.Equal("[first]标签", before.Text);
        Css.GetAttributes(root)["data-label"] = "second"; Layout(root);
        Assert.Equal("[second]标签", before.Text);
        root.Tag = "new"; Layout(root); Assert.Equal("[second]new", before.Text);
        Css.GetAttributes(root).Remove("data-label"); Layout(root);
        Assert.Equal("[missing]new", before.Text);
    }

    [Fact]
    public void ContentNoneAndSheetRemovalRestoreTheNativeLayout()
    {
        var root = Host(); var child = new Border { Height = 20 }; root.Children.Add(child);
        var sheet = Add(root, ".host::before { content: ''; width: 20px; height: 12px; }");
        Layout(root); var before = Before(root);
        Assert.Equal(32, root.DesiredSize.Height, 6);
        Css.GetStyleSheets(root).Remove(sheet); Layout(root);
        Assert.Equal(Visibility.Collapsed, before.Visibility); Assert.Equal(20, root.DesiredSize.Height, 6);
        Assert.Equal(0, child.VisualBounds.Y, 6);
        Add(root, ".host::before { content: none; height: 40px; }"); Layout(root);
        Assert.Equal(20, root.DesiredSize.Height, 6);
        Assert.Single(root.Children);
    }

    [Fact]
    public void LayersSpecificityAndLegacySyntaxCascadeOnThePseudoElement()
    {
        var root = Host();
        Add(root, """
            @layer base, theme;
            @layer base { .host::before { content: "base"; color: red !important; } }
            @layer theme { #root:before { content: "theme"; color: blue !important; } }
            .host::before { content: "outside"; }
            """);
        Layout(root);
        Assert.Equal("outside", Before(root).Text);
        Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(Before(root).Foreground).Color);
    }

    [Fact]
    public void EmptyContentStillProducesAStyledBoxAndPointerTargetsItsOrigin()
    {
        var root = Host();
        Add(root, ".host::before { content: ''; width: 30px; height: 20px; background-color: red; }");
        Layout(root);
        Assert.Equal(Visibility.Visible, Before(root).Visibility);
        Assert.Equal(30, Before(root).ActualWidth, 6);
        var point = new Point(Before(root).VisualBounds.X + 10, Before(root).VisualBounds.Y + 10);
        Assert.Same(root, root.HitTest(point)?.VisualHit);
        Assert.Same(root, root.InputHitTest(point));
    }

    [Fact]
    public void CompiledBuilderPreservesThePseudoTargetAndItsSpecificity()
    {
        var builder = new CssStyleSheetBuilder(1);
        var compound = builder.Compound("StackPanel", false, null, false, true, false, null, ["host"],
            null, 0, null, null, (byte)CssPseudoElement.Before);
        var selector = builder.Selector([compound], [], (1 << 10) + 2, 0, 0, false, false);
        var content = builder.Declaration("content", "'compiled'", false);
        builder.Rule([selector], [content], -1, null, -1);
        var root = Host(); Css.GetStyleSheets(root).Add(builder.Build());
        Layout(root);
        Assert.Equal("compiled", Before(root).Text);
        Assert.Equal(2 + (1 << 10), CssSelectorParser.ParseGroup("StackPanel.host::before", out _)![0].Specificity);
    }

    [Fact]
    public void NestedRulesScopeAndSupportsCanTargetGeneratedContent()
    {
        var root = Host(); var child = new StackPanel(); Css.SetClass(child, "scope"); root.Children.Add(child);
        Add(root, """
            @supports (content: "ok") {
                @scope (.scope) {
                    :scope { &::before { content: "nested"; } }
                }
            }
            """);
        Layout(root);
        Assert.Equal("nested", Before(child).Text);
        Assert.Null(CssGeneratedContent.Get(root));
    }

    [Theory]
    [InlineData("'示例' / 'accessible label'", "示例", "accessible label", true)]
    [InlineData("'装饰' / ''", "装饰", "", false)]
    public void ContentAlternativesReachAccessibilityWithoutChangingVisibleText(string declaration,
        string visible, string name, bool contentElement)
    {
        var root = Host(); Add(root, $".host::before {{ content: {declaration}; }}"); Layout(root);
        var text = Before(root);
        Assert.Equal(visible, text.Text);
        var peer = Jalium.UI.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(text)!;
        Assert.Equal(name, peer.GetName()); Assert.Equal(contentElement, peer.IsContentElement());
    }

    [Fact]
    public void ContentChangesSettleWithoutRepeatedSubtreeEvaluation()
    {
        var root = Host(); Add(root, ".host::before { content: attr(Tag, 'x'); }");
        Layout(root);
        CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
        Assert.False(CssEvaluationScheduler.HasPending(Dispatcher.CurrentDispatcher));
        root.Tag = "stable"; Layout(root);
        CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
        Assert.False(CssEvaluationScheduler.HasPending(Dispatcher.CurrentDispatcher));
        Assert.Equal("stable", Before(root).Text);
    }

    [Theory]
    [InlineData("stack")]
    [InlineData("grid")]
    [InlineData("flex")]
    [InlineData("wrap")]
    [InlineData("dock")]
    [InlineData("canvas")]
    [InlineData("uniform")]
    public void BuiltInPanelsMeasureAndArrangeTheirGeneratedBoxes(string type)
    {
        Panel root = type switch
        {
            "stack" => new StackPanel(), "grid" => new Grid(), "flex" => new FlexPanel(),
            "wrap" => new WrapPanel(), "dock" => new DockPanel(), "canvas" => new Canvas(),
            _ => new Jalium.UI.Controls.Primitives.UniformGrid { Columns = 3 },
        };
        Css.SetClass(root, "host");
        root.Children.Add(new Border { Width = 20, Height = 20 });
        Add(root, """
            .host::before { content: ""; width: 10px; height: 12px; }
            .host::after { content: ""; width: 30px; height: 8px; }
            """);
        Layout(root);
        Assert.Equal(10, Before(root).ActualWidth, 6); Assert.Equal(12, Before(root).ActualHeight, 6);
        Assert.Equal(30, After(root).ActualWidth, 6); Assert.Equal(8, After(root).ActualHeight, 6);
        Assert.Single(root.Children);
    }

    [Theory]
    [InlineData("'x' /* join */ '\\41'", "xA")]
    [InlineData("'\\1f642 '", "🙂")]
    [InlineData("var(--missing, 'fallback')", "fallback")]
    public void ContentUsesCssEscapesConcatenationAndVariableFallback(string value, string expected)
    {
        var root = Host(); Add(root, $".host::before {{ content: {value}; }}"); Layout(root);
        Assert.Equal(expected, Before(root).Text);
    }

    [Theory]
    [InlineData("normal", true)]
    [InlineData("none", true)]
    [InlineData("''", true)]
    [InlineData("'visible' / 'label'", true)]
    [InlineData("'unterminated", false)]
    [InlineData("'text' /", false)]
    [InlineData("url('image.png')", true)]
    [InlineData("counter(item)", true)]
    public void AuthoringReportsOnlyImplementedContentGrammar(string value, bool supported)
    {
        Assert.Equal(CssPropertySupport.Supported, CssAuthoring.GetProperty("content").Support);
        Assert.Equal(supported, CssAuthoring.IsValueSupported("content", value));
    }

    [Fact]
    public void NormalAndNoneRetainTheirComputedMeaningForElementsAndPseudos()
    {
        var root = Host(); Css.SetStyle(root, "content: normal;");
        Add(root, ".host::before { content: inherit; }"); Layout(root);
        Assert.False(Assert.IsType<CssGeneratedContentValue>(root.GetValue(CssGeneratedContent.ContentProperty)).IsNone);
        Assert.True(Assert.IsType<CssGeneratedContentValue>(Before(root).GetValue(CssGeneratedContent.ContentProperty)).IsNone);
        Css.SetStyle(root, "content: none;"); Layout(root);
        Assert.True(Assert.IsType<CssGeneratedContentValue>(root.GetValue(CssGeneratedContent.ContentProperty)).IsNone);
    }

    private static StackPanel Host()
    {
        var host = new StackPanel { Name = "root" }; Css.SetClass(host, "host"); return host;
    }
    private static CssStyleSheet Add(FrameworkElement host, string text)
    {
        var sheet = CssStyleSheet.Parse(text); Assert.Empty(sheet.Diagnostics);
        Css.GetStyleSheets(host).Add(sheet); return sheet;
    }
    private static CssGeneratedText Before(Panel host) => Assert.IsType<CssGeneratedText>(CssGeneratedContent.Get(host)?.Before);
    private static CssGeneratedText After(Panel host) => Assert.IsType<CssGeneratedText>(CssGeneratedContent.Get(host)?.After);
    private static void Layout(FrameworkElement root)
    {
        for (var i = 0; i < 4; i++)
        {
            CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
            root.Measure(new Size(240, 120)); root.Arrange(new Rect(0, 0, 240, 120));
        }
    }
}

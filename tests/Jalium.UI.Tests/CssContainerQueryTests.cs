using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssContainerQueryTests
{
    static CssContainerQueryTests() => System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(
        typeof(Jalium.UI.Markup.TypeConverterRegistry).Module.ModuleHandle);

    private static void Layout(FrameworkElement root, double width = 800, double height = 400)
    {
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        root.Measure(new(width, height)); root.Arrange(new(0, 0, width, height));
        root.UpdateLayout();
        Assert.False(CssEvaluationScheduler.HasPending(root.Dispatcher));
    }

    private static (Border Root, StackPanel Container, Border Child) Tree(string condition, string type = "size")
    {
        var child = new Border();
        var container = new StackPanel { Width = 400, Height = 200, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        container.Children.Add(child);
        var root = new Border { Child = container };
        Css.SetClass(child, "target");
        Css.SetStyle(container, "container-type:" + type);
        Css.SetStyleSheet(root, ".target { width:11px; height:10px; opacity:.9 } @container " + condition + " { .target { width:101px; opacity:.2 } }");
        Layout(root);
        return (root, container, child);
    }

    [Theory]
    [InlineData("(width:400px)", true)]
    [InlineData("(inline-size >= 400px)", true)]
    [InlineData("(min-width:400px)", true)]
    [InlineData("(max-width:399px)", false)]
    [InlineData("(399px < width)", true)]
    [InlineData("(400px >= width)", true)]
    [InlineData("(399px < width <= 400px)", true)]
    [InlineData("(401px > width > 399px)", true)]
    [InlineData("(width)", true)]
    [InlineData("(height:200px)", true)]
    [InlineData("(block-size > 200px)", false)]
    [InlineData("(aspect-ratio:2/1)", true)]
    [InlineData("(aspect-ratio > 1)", true)]
    [InlineData("(orientation:landscape)", true)]
    [InlineData("(orientation:portrait)", false)]
    [InlineData("not (width > 400px)", true)]
    [InlineData("((width > 400px) or (height:200px))", true)]
    [InlineData("(width > 400px) and (height:200px)", false)]
    [InlineData("(width > 400px), (height:200px)", true)]
    [InlineData("(width:calc(200px * 2))", true)]
    [InlineData("not (unknown-feature:yes)", false)]
    [InlineData("(width:400px) or (unknown-feature:yes)", false)]
    [InlineData("not (width:20%)", false)]
    [InlineData("not (width:400)", false)]
    [InlineData("not (width:var(--missing))", false)]
    [InlineData("not (500px < width < var(--missing))", false)]
    [InlineData("scroll-state(stuck:top)", false)]
    public void Conditions_SelectOneContainerAndEvaluateTypedFeatures(string condition, bool matches)
    {
        var (_, _, child) = Tree(condition);
        Assert.Equal(matches ? 101 : 11, child.Width);
        Assert.Equal(matches ? .2 : .9, child.Opacity);
    }

    [Fact]
    public void Resize_UpdatesQueriesAndRestoresThePreviousCascadeWithinUpdateLayout()
    {
        var (root, container, child) = Tree("(width > 300px)");
        Assert.Equal(101, child.ActualWidth);
        container.Width = 280; root.UpdateLayout();
        Assert.Equal(11, child.ActualWidth);
        container.Width = 450; root.UpdateLayout();
        Assert.Equal(101, child.ActualWidth);
        Assert.False(CssEvaluationScheduler.HasPending(root.Dispatcher));
    }

    [Fact]
    public void Query_UsesContentBoxAndContainerFont()
    {
        var child = new Border();
        var container = new Border { Width = 230, Height = 160, Padding = new Thickness(10), BorderThickness = new Thickness(5), Child = child };
        var root = new Border { Child = container };
        Css.SetStyle(container, "container:card / size; font-size:20px");
        Css.SetClass(child, "target"); Css.SetStyle(child, "font-size:10px");
        Css.SetStyleSheet(root, "@container card (width:10em) and (height:130px) { .target {opacity:.2} }");
        Layout(root); Assert.Equal(.2, child.Opacity);
        container.Padding = new Thickness(20); root.UpdateLayout();
        Assert.Equal(1, child.Opacity);
    }

    [Fact]
    public void Names_AreCaseSensitiveAndCanSelectAnOuterContainer()
    {
        var (root, inner, child) = Tree("Outer (width:800px)");
        Css.SetStyle(root, "container:Outer / size"); Css.SetStyle(inner, "container:Inner / size");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(root, "container:outer / size"); root.UpdateLayout();
        Assert.Equal(11, child.Width);
        Css.SetStyle(inner, "container:foo Outer / inline-size");
        Css.SetStyleSheet(root, "@container Outer { .target {opacity:.3} }");
        root.UpdateLayout(); Assert.Equal(.3, child.Opacity);
    }

    [Fact]
    public void CombinedFeatures_SkipAnInlineContainerForAQueryRequiringBothAxes()
    {
        var (root, _, child) = Tree("(width:800px) and (height:400px)", "inline-size");
        Css.SetStyle(root, "container-type:size"); Layout(root);
        Assert.Equal(101, child.Width);
    }

    [Fact]
    public void NoEligibleContainer_AndSelfQueriesRemainUnknownUnderNot()
    {
        var (root, container, child) = Tree("not (height > 100px)", "inline-size");
        Assert.Equal(11, child.Width);
        Css.SetStyleSheet(root, "@container (width > 100px) { StackPanel {opacity:.2} }");
        Layout(root); Assert.Equal(1, container.Opacity);
    }

    [Fact]
    public void ContainerShorthand_ResetsTypeAndSupportsVariablesAndGlobalKeywords()
    {
        var (_, container, _) = Tree("(width > 300px)");
        Css.SetStyle(container, "--c:Card / inline-size; container:var(--c)");
        Assert.Equal(CssContainerType.InlineSize, CssContainerProperties.Type(container));
        Assert.True(CssContainerProperties.HasName(container, "Card"));
        Css.SetStyle(container, "container-type:size; container:Card");
        Assert.Equal(CssContainerType.Normal, CssContainerProperties.Type(container));
        Css.SetStyle(container, "container:Card / size; container:initial");
        Assert.Equal(CssContainerType.Normal, CssContainerProperties.Type(container));
        Assert.False(CssContainerProperties.HasName(container, "Card"));
    }

    [Theory]
    [InlineData("container-type", "size", true)]
    [InlineData("container-type", "inline-size", true)]
    [InlineData("container-type", "inline-size size", false)]
    [InlineData("container-type", "scroll-state", false)]
    [InlineData("container-name", "none a", false)]
    [InlineData("container-name", "and", false)]
    [InlineData("container-name", "Card card", true)]
    [InlineData("container", "none / size", true)]
    [InlineData("container", "card /", false)]
    public void Supports_RejectsUnimplementedOrMalformedDeclarations(string property, string value, bool expected)
        => Assert.Equal(expected, Css.Supports(property, value));

    [Theory]
    [InlineData("")]
    [InlineData("none")]
    [InlineData("(width:300px),")]
    [InlineData("(width:300px) and (height:200px) or (height:100px)")]
    [InlineData("not (width:300px) and (height:200px)")]
    [InlineData("(width\\\n:300px)")]
    public void InvalidConditions_DropOnlyTheirRuleAndReportADiagnostic(string condition)
    {
        var sheet = CssStyleSheet.Parse("@container " + condition + " {.target {opacity:.1}} .target {width:22px}");
        Assert.Contains(sheet.Diagnostics, d => d.Message.Contains("invalid @container"));
        Assert.Single(sheet.Rules);
    }

    [Fact]
    public void ConditionalRules_NestWithMediaSupportsLayersAndOtherContainers()
    {
        var (root, container, child) = Tree("(width:400px)");
        Css.SetStyle(root, "container:outer / size"); Css.SetStyle(container, "container:inner / inline-size");
        Css.SetStyleSheet(root, """
            @layer base, responsive;
            @layer base { .target {opacity:.8} }
            @media (min-width:700px) { @supports (container-type:inline-size) {
              @container outer (width:800px) { @container inner (width:400px) {
                @layer responsive { .target {opacity:.2} }
              }}
            }}
            """);
        Layout(root); Assert.Equal(.2, child.Opacity);
        container.Width = 300; root.UpdateLayout(); Assert.Equal(.8, child.Opacity);
    }

    [Theory]
    [InlineData("style(--mode:Compact)", true)]
    [InlineData("style(--mode:compact)", false)]
    [InlineData("style(--Mode:Compact)", false)]
    [InlineData("style(--mode)", true)]
    [InlineData("style(--empty)", true)]
    [InlineData("style(--missing)", false)]
    [InlineData("style(--missing:initial)", true)]
    [InlineData("style(--mode:var(--alias))", true)]
    [InlineData("style(--mode:var(--absent, Compact))", true)]
    [InlineData("style((--mode:Compact) and (--enabled:yes))", true)]
    [InlineData("style(not (--enabled:no))", true)]
    [InlineData("style(--tokens:10.0px solid 'hi')", true)]
    [InlineData("style(--mode:C\\6f mpact)", true)]
    [InlineData("style(--colon:a:b)", true)]
    [InlineData("not style(--mode:Compact!important)", false)]
    [InlineData("not style(--mode:Compact;)", false)]
    public void CustomPropertyStyleQueries_UseComputedTokens(string condition, bool expected)
    {
        var (root, container, child) = Tree(condition, "normal");
        Css.SetStyle(container, "--mode:Compact; --alias:Compact; --enabled:yes; --empty:; --tokens:10px solid \"hi\"; --colon:a:b");
        Layout(root); Assert.Equal(expected ? 101 : 11, child.Width);
    }

    [Fact]
    public void StyleAndSizeVariables_RefreshWhenTheSelectedContainerChanges()
    {
        var (root, container, child) = Tree("(width > var(--breakpoint)) and style(--mode:wide)");
        Css.SetStyle(container, "container-type:inline-size; --breakpoint:300px; --mode:wide");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(container, "container-type:inline-size; --breakpoint:500px; --mode:wide");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
        Css.SetStyle(container, "container-type:inline-size; --breakpoint:300px; --mode:narrow");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Theory]
    [InlineData("10cqw", 40)]
    [InlineData("10cqi", 40)]
    [InlineData("10cqh", 20)]
    [InlineData("10cqb", 20)]
    [InlineData("10cqmin", 20)]
    [InlineData("10cqmax", 40)]
    [InlineData("10\\63 qw", 40)]
    [InlineData("calc(10cqw + 5px)", 45)]
    [InlineData("clamp(10px, 10cqi, 50px)", 40)]
    public void ContainerUnits_ResolveInExistingLengthConverters(string width, double expected)
    {
        var (root, _, child) = Tree("(width < 0px)");
        Css.SetStyle(child, "width:" + width); Layout(root);
        Assert.Equal(expected, child.ActualWidth, 6);
    }

    [Fact]
    public void ContainerUnits_SelectEachAxisIndependentlyAndFallbackToTheHostViewport()
    {
        var (root, container, child) = Tree("(width < 0px)", "inline-size");
        Css.SetStyle(root, "container-type:size");
        Css.SetStyle(child, "width:10cqw; height:10cqh"); Layout(root);
        Assert.Equal(40, child.ActualWidth); Assert.Equal(40, child.ActualHeight);
        Css.SetStyle(container, ""); Css.SetStyle(root, ""); root.UpdateLayout();
        Assert.Equal(80, child.ActualWidth); Assert.Equal(40, child.ActualHeight);
    }

    [Fact]
    public void ContainerUnitDependencies_SurviveAnUnchangedStylePassAndReparenting()
    {
        var (root, container, child) = Tree("(width < 0px)");
        Css.SetStyle(child, "width:calc(10cqw + 5px); transform:translateX(10cqw)"); Layout(root);
        CssEngine.EvaluateElement(child);
        container.Width = 500; root.UpdateLayout();
        Assert.Equal(55, child.ActualWidth); Assert.Equal(50, child.RenderTransform!.Value.OffsetX);
        container.Children.Remove(child); root.Child = child;
        root.UpdateLayout(); Assert.Equal(85, child.ActualWidth); Assert.Equal(80, child.RenderTransform!.Value.OffsetX);
    }

    [Fact]
    public void ContainerUnits_AreRetainedByGridTracksAndFollowResizing()
    {
        var child = new Border(); var grid = new StackPanel(); grid.Children.Add(child);
        var container = new Border { Width = 400, Height = 200, Child = grid };
        var root = new Border { Child = container };
        Css.SetStyle(container, "container-type:size");
        Css.SetStyle(grid, "display:grid; grid-template-columns:10cqw 1fr; grid-template-rows:20px");
        Layout(root); Assert.Equal(40, child.ActualWidth);
        CssEngine.EvaluateElement(grid); container.Width = 500; root.UpdateLayout();
        Assert.Equal(50, child.ActualWidth);
    }

    [Fact]
    public void NativeLocalValuesAndBindings_OutrankQueryDeclarationsAndSurviveRemoval()
    {
        var (root, container, child) = Tree("(width > 300px)");
        child.Width = 70;
        BindingOperations.SetBinding(child, FrameworkElement.HeightProperty, new Binding(nameof(FrameworkElement.Height)) { Source = container });
        Css.SetStyleSheet(root, "@container (width > 300px) { .target {width:900px !important; height:900px !important} }");
        Layout(root); Assert.Equal(70, child.Width); Assert.Equal(200, child.Height);
        container.Width = 200; container.Height = 90; root.UpdateLayout();
        Assert.Equal(70, child.Width); Assert.Equal(90, child.Height);
        Assert.NotNull(BindingOperations.GetBindingExpression(child, FrameworkElement.HeightProperty));
        Assert.Same(child, container.Children[0]); Assert.Same(container, child.VisualParent);
    }

    [Fact]
    public void QueryDisplayChanges_RestoreTheNativeStackPanelWithoutReparenting()
    {
        var (root, container, _) = Tree("(width < 0px)");
        var panel = new StackPanel(); var first = new Border { Width = 20, Height = 20 }; var second = new Border { Width = 20, Height = 20 };
        panel.Children.Add(first); panel.Children.Add(second); container.Children.Clear(); container.Children.Add(panel);
        Css.SetClass(panel, "flow");
        Css.SetStyleSheet(root, "@container (width > 300px) { .flow {display:flex; flex-direction:row} }");
        Layout(root); Assert.Equal(20, second.VisualBounds.X); Assert.Equal(0, second.VisualBounds.Y);
        container.Width = 200; root.UpdateLayout();
        // Native Stretch centers a locally constrained 20px control in the 200px slot.
        Assert.Equal(90, second.VisualBounds.X); Assert.Equal(20, second.VisualBounds.Y);
        Assert.Same(panel, second.VisualParent); Assert.Same(first, panel.Children[0]);
    }

    [Theory]
    [InlineData("normal", 516, 56)]
    [InlineData("inline-size", 16, 56)]
    [InlineData("size", 16, 16)]
    public void SizeContainment_ExcludesIntrinsicContentButPreservesNativeChrome(string type, double width, double height)
    {
        var container = new Border { Padding = new Thickness(8), Child = new Border { Width = 500, Height = 40 } };
        Css.SetStyle(container, "container-type:" + type);
        container.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
        Assert.Equal(width, container.DesiredSize.Width); Assert.Equal(height, container.DesiredSize.Height);
        container.Width = 130; container.Height = 80;
        container.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
        Assert.Equal(130, container.DesiredSize.Width); Assert.Equal(80, container.DesiredSize.Height);
        container.ClearValue(FrameworkElement.WidthProperty); container.ClearValue(FrameworkElement.HeightProperty);
        Css.SetStyle(container, ""); container.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
        Assert.Equal(516, container.DesiredSize.Width); Assert.Equal(56, container.DesiredSize.Height);
    }

    [Fact]
    public void ContainerSizeIsIndependentOfResponsiveChildContent()
    {
        var child = new Border(); var container = new Border { Child = child, HorizontalAlignment = HorizontalAlignment.Left };
        var root = new Border { Child = container };
        Css.SetStyle(container, "container-type:inline-size; min-width:200px"); Css.SetClass(child, "target");
        Css.SetStyleSheet(root, ".target {width:800px} @container (width < 300px) {.target {width:1000px}}");
        Layout(root); Assert.Equal(200, container.ActualWidth); Assert.Equal(1000, child.Width);
        for (var i = 0; i < 3; i++) root.UpdateLayout();
        Assert.Equal(200, container.ActualWidth); Assert.False(CssEvaluationScheduler.HasPending(root.Dispatcher));
    }

    [Fact]
    public void QueryGeneratedVariables_PropagateThroughDescendantsDuringAResize()
    {
        var (root, container, child) = Tree("(width < 0px)");
        var grandchild = new Border(); child.Child = grandchild;
        Css.SetStyle(grandchild, "width:var(--responsive-width)");
        Css.SetStyleSheet(root, ".target {--responsive-width:20px} @container (width > 300px) {.target {--responsive-width:60px}}");
        Layout(root); Assert.Equal(60, grandchild.ActualWidth);
        container.Width = 200; root.UpdateLayout(); Assert.Equal(20, grandchild.ActualWidth);
    }

    [Fact]
    public void DocumentNodes_CanProvideAndConsumeStyleQueriesWithoutChangingText()
    {
        var run = new Run("原始文本"); var paragraph = new Paragraph(run);
        Css.SetStyle(paragraph, "--mode:wide; container-name:paragraph");
        Css.SetStyleSheet(paragraph, "@container paragraph style(--mode:wide) { Run {font-size:24px} }");
        CssEvaluationScheduler.FlushIfPending(paragraph.Dispatcher);
        Assert.Equal(24, run.FontSize); Assert.Equal("原始文本", run.Text);
        Css.SetStyle(paragraph, "--mode:narrow; container-name:paragraph");
        CssEvaluationScheduler.FlushIfPending(paragraph.Dispatcher);
        Assert.NotEqual(24, run.FontSize); Assert.Equal("原始文本", run.Text);
    }

    private sealed class ManagedHost : Border, ILayoutManagerHost
    {
        public LayoutManager LayoutManager { get; } = new();
    }

    [Fact]
    public void LayoutManager_SettlesSizeQueriesInItsRealMeasureArrangeQueues()
    {
        var child = new Border(); var container = new Border { Width = 400, Child = child };
        var root = new ManagedHost { Child = container };
        Css.SetClass(child, "target"); Css.SetStyle(container, "container-type:inline-size");
        Css.SetStyleSheet(root, ".target {width:20px; height:10px} @container (width > 300px) {.target {width:60px}}");
        root.LayoutManager.UpdateLayout(root, new(800, 400));
        Assert.Equal(60, child.ActualWidth);
        container.Width = 200; root.UpdateLayout();
        Assert.Equal(20, child.ActualWidth); Assert.False(CssEvaluationScheduler.HasPending(root.Dispatcher));
    }

    private sealed class CountedBorder : Border
    {
        internal int Measurements;
        protected override Size MeasureOverride(Size availableSize) { Measurements++; return base.MeasureOverride(availableSize); }
    }

    [Fact]
    public void ContainerResize_DoesNotRemeasureAnIndependentSibling()
    {
        var leftChild = new Border(); var rightChild = new CountedBorder();
        var left = new Border { Width = 400, Child = leftChild };
        var right = new Border { Width = 400, Child = rightChild };
        var root = new Grid(); root.Children.Add(left); root.Children.Add(right);
        foreach (var container in new[] { left, right }) Css.SetStyle(container, "container-type:inline-size");
        foreach (var child in new Border[] { leftChild, rightChild }) Css.SetClass(child, "target");
        Css.SetStyleSheet(root, ".target {width:20px; height:10px} @container (width > 300px) {.target {width:60px}}");
        Layout(root); var count = rightChild.Measurements;
        left.Width = 200; root.UpdateLayout();
        Assert.Equal(20, leftChild.ActualWidth); Assert.Equal(60, rightChild.ActualWidth);
        Assert.Equal(count, rightChild.Measurements);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference RemoveQueriedChild(Border root)
    {
        var child = new Border(); root.Child = child;
        Css.SetClass(child, "target");
        Css.SetStyle(root, "container-type:inline-size");
        Css.SetStyleSheet(root, "@container (width > 300px) {.target {width:10cqw; height:10px}}");
        Layout(root); root.Child = null; root.UpdateLayout();
        return new WeakReference(child);
    }

    [Fact]
    public void QuerySubscriptions_DoNotRetainDetachedControls()
    {
        var root = new Border(); var removed = RemoveQueriedChild(root);
        for (var i = 0; i < 3 && removed.IsAlive; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
        Assert.False(removed.IsAlive);
        GC.KeepAlive(root);
    }

    [Fact]
    public void EscapedUnits_AreContextualInTransformsAndInheritedFontSizes()
    {
        var (root, container, child) = Tree("(width < 0px)");
        var text = new TextBlock { Text = "unchanged" }; child.Child = text;
        Css.SetStyle(child, "font-size:5cqi; transform:translateX(10\\63 qw)");
        Layout(root); Assert.Equal(20, text.FontSize); Assert.Equal(40, child.RenderTransform!.Value.OffsetX);
        container.Width = 500; root.UpdateLayout();
        Assert.Equal(25, text.FontSize); Assert.Equal(50, child.RenderTransform!.Value.OffsetX);
        Assert.Equal("unchanged", text.Text);
    }

    [Fact]
    public void CollapsedAncestor_RemovesTheQueryBoxAndRestoresItOnReappearance()
    {
        var (root, container, child) = Tree("(width:400px)");
        root.Visibility = Visibility.Collapsed; root.UpdateLayout();
        Assert.Equal(11, child.Width);
        root.Visibility = Visibility.Visible; root.UpdateLayout();
        Assert.Equal(101, child.Width); Assert.Equal(400, container.ActualWidth);
    }

    [Fact]
    public void QueryContainers_EstablishIndependentFlowAndContainTheirFloats()
    {
        var root = new StackPanel(); var container = new StackPanel(); var floated = new Border();
        container.Children.Add(floated); root.Children.Add(container);
        Css.SetStyle(root, "display:flow-root; line-height:0");
        Css.SetStyle(container, "display:block; container-type:inline-size");
        Css.SetStyle(floated, "float:left; width:100px; height:50px");
        Layout(root); Assert.Equal(50, container.ActualHeight);
        Css.SetStyle(container, "display:block"); root.UpdateLayout();
        Assert.Equal(0, container.ActualHeight);
    }

    [Fact]
    public void ContainerQuerySelectors_RespectTheExistingTemplateBoundary()
    {
        Border? part = null;
        var template = new ControlTemplate(typeof(Button)); template.SetVisualTree(() => part = new Border { Child = new ContentPresenter() });
        var button = new Button { Template = template }; button.ApplyTemplate();
        var content = new Border(); button.Content = content;
        var root = new Border { Child = button };
        Css.SetStyle(root, "container:page / inline-size");
        Css.SetStyleSheet(root, "@container page (width > 300px) {Border {opacity:.2}}");
        Layout(root); Assert.NotNull(part); Assert.Equal(1, part.Opacity); Assert.Equal(.2, content.Opacity);
        Assert.Same(template, button.Template);
    }

    [Fact]
    public void ContainerUnitsInQueryThresholds_UseTheSelectedContainersOwnAncestors()
    {
        var child = new Border(); var inner = new Border { Width = 400, Child = child };
        var outer = new Border { Width = 800, Child = inner }; var root = new Border { Child = outer };
        Css.SetClass(child, "target");
        Css.SetStyle(inner, "container:inner / inline-size"); Css.SetStyle(outer, "container-type:inline-size");
        Css.SetStyleSheet(root, "@container inner (width >= 50cqw) {.target {opacity:.2}}");
        Layout(root, 1200); Assert.Equal(.2, child.Opacity);
        outer.Width = 1000; root.UpdateLayout(); Assert.Equal(1, child.Opacity);
    }
}

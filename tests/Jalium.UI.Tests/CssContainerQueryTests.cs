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
    [InlineData("style(0px <= env(safe-area-inset-top))", true)]
    [InlineData("style(3px < env(unknown, 5px))", true)]
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
    [InlineData("container-type", "scroll-state", true)]
    [InlineData("container-type", "size scroll-state", true)]
    [InlineData("container-type", "scroll-state inline-size", true)]
    [InlineData("container-type", "scroll-state scroll-state", false)]
    [InlineData("container-type", "size inline-size scroll-state", false)]
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

    [Theory]
    [InlineData("style(--gap:attr(gap type(<length>)))", "5px", true)]
    [InlineData("style(--gap:attr(gap type(<length>)))", "1in", false)]
    [InlineData("style(--gap:attr(gap type(<length>)))", "invalid", false)]
    [InlineData("style(--gap:attr(gap type(<length>), 5px))", "invalid", true)]
    [InlineData("style(--gap:attr(gap type(<length>), var(--missing)))", "5px", true)]
    [InlineData("style(--gap:attr(missing type(<length>), var(--fallback)))", "5px", true)]
    [InlineData("style(--gap:attr(gap))", "5px", false)]
    public void CustomPropertyStyleQueries_UseTypedAttributeValuesAndLazyFallbacks(
        string condition, string attribute, bool expected)
    {
        var (root, container, child) = Tree(condition, "normal");
        Css.SetStyle(container, "--gap:5px; --fallback:5px");
        Css.SetAttribute(container, "gap", attribute);
        Layout(root); Assert.Equal(expected ? 101 : 11, child.Width);
    }

    [Fact]
    public void CustomPropertyStyleQueries_RefreshWhenComparedAttributeChanges()
    {
        var (root, container, child) = Tree("style(--gap:attr(gap type(<length>), 5px))", "normal");
        Css.SetStyle(container, "--gap:5px");
        Css.SetAttribute(container, "gap", "5px");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetAttribute(container, "gap", "3px");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
        Css.SetAttribute(container, "gap", null);
        root.UpdateLayout(); Assert.Equal(101, child.Width);
    }

    [Fact]
    public void StyleQueryAttributeUsesTheConditionStylesheetNamespace()
    {
        var (root, container, child) = Tree("style(--gap:attr(p|gap type(<length>)))", "normal");
        Css.SetStyleSheet(root, "@namespace p 'urn:props'; .target {width:11px} " +
            "@container style(--gap:attr(p|gap type(<length>))) { .target {width:101px} }");
        Css.SetStyle(container, "--gap:5px");
        Css.SetAttribute(container, "urn:props", "gap", "5px");
        Css.SetAttribute(container, "gap", "9px");

        Layout(root);
        Assert.Equal(101, child.Width);
        Css.SetAttribute(container, "urn:props", "gap", "7px");
        Layout(root);
        Assert.Equal(11, child.Width);
    }

    [Fact]
    public void CustomPropertyStyleQueries_CompareRawAttributeStringsAsStringTokens()
    {
        var (root, container, child) = Tree("style(--word:attr(label))", "normal");
        Css.SetStyle(container, "--word:\"wide\"");
        Css.SetAttribute(container, "label", "wide");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(container, "--word:wide");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Fact]
    public void RegisteredPropertyStyleQueries_ComputeTypedAttributeExpectations()
    {
        var (root, container, child) = Tree("style(--gap:attr(gap type(<length>)))", "normal");
        Css.SetStyleSheet(root, "@property --gap { syntax: '<length>'; inherits: false; initial-value: 1px } " +
            ".target { width:11px } @container style(--gap:attr(gap type(<length>))) { .target { width:101px } }");
        Css.SetStyle(container, "--gap:96px");
        Css.SetAttribute(container, "gap", "1in");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetAttribute(container, "gap", "2in");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Theory]
    [InlineData("opacity", ".3", "30%", true)]
    [InlineData("OPACITY", ".3", "30%", true)]
    [InlineData("opacity", ".3", ".4", false)]
    [InlineData("direction", "rtl", "rtl", true)]
    [InlineData("float", "left", "right", false)]
    [InlineData("clear", "both", "both", true)]
    [InlineData("container-type", "inline-size", "inline-size", true)]
    [InlineData("container-name", "Card", "card", false)]
    [InlineData("user-select", "none", "none", true)]
    [InlineData("image-rendering", "pixelated", "pixelated", true)]
    [InlineData("object-fit", "cover", "contain", false)]
    [InlineData("overflow-wrap", "anywhere", "anywhere", true)]
    [InlineData("word-wrap", "anywhere", "anywhere", true)]
    [InlineData("word-break", "break-all", "break-all", true)]
    [InlineData("line-break", "strict", "normal", false)]
    [InlineData("text-transform", "uppercase", "uppercase", true)]
    [InlineData("text-autospace", "ideograph-alpha ideograph-numeric", "normal", true)]
    [InlineData("hyphens", "auto", "auto", true)]
    [InlineData("text-wrap-mode", "nowrap", "nowrap", true)]
    [InlineData("white-space-collapse", "preserve", "preserve", true)]
    [InlineData("white-space-trim", "discard-before", "discard-before", true)]
    [InlineData("caret-animation", "manual", "manual", true)]
    [InlineData("box-decoration-break", "clone", "clone", true)]
    public void OrdinaryStyleQueries_CompareComputedStorageValues(
        string property, string authored, string expectedValue, bool expected)
    {
        var (root, container, child) = Tree($"style({property}:{expectedValue})", "normal");
        Css.SetStyle(container, $"{property}:{authored}");
        Layout(root); Assert.Equal(expected ? 101 : 11, child.Width);
    }

    [Theory]
    [InlineData("container", "Card / inline-size", "Card / inline-size", true)]
    [InlineData("container", "Card / inline-size", "Card", false)]
    [InlineData("text-wrap", "nowrap", "nowrap", true)]
    [InlineData("text-wrap", "nowrap", "wrap", false)]
    [InlineData("white-space", "pre", "pre", true)]
    [InlineData("white-space", "pre", "normal", false)]
    public void OrdinaryStyleQueries_CompareEveryShorthandLonghand(
        string property, string authored, string expectedValue, bool expected)
    {
        var (root, container, child) = Tree($"style({property}:{expectedValue})", "normal");
        Css.SetStyle(container, $"{property}:{authored}");
        Layout(root); Assert.Equal(expected ? 101 : 11, child.Width);
    }

    [Fact]
    public void OrdinaryStyleQueries_BooleanShorthandMatchesWhenOneLonghandDiffers()
    {
        var (root, container, child) = Tree("style(container)", "normal");
        Css.SetStyle(container, "container:Card");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(container, "container:none / normal");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Theory]
    [InlineData("opacity")]
    [InlineData("direction")]
    [InlineData("float")]
    [InlineData("clear")]
    [InlineData("container-type")]
    [InlineData("container-name")]
    [InlineData("user-select")]
    [InlineData("image-rendering")]
    [InlineData("object-fit")]
    [InlineData("text-justify")]
    [InlineData("text-group-align")]
    [InlineData("overflow-wrap")]
    [InlineData("word-break")]
    [InlineData("line-break")]
    [InlineData("text-autospace")]
    [InlineData("hanging-punctuation")]
    [InlineData("hyphens")]
    [InlineData("text-transform")]
    [InlineData("white-space-collapse")]
    [InlineData("text-wrap-mode")]
    [InlineData("text-wrap-style")]
    [InlineData("white-space-trim")]
    [InlineData("caret-animation")]
    [InlineData("caret-shape")]
    [InlineData("box-decoration-break")]
    public void OrdinaryStyleQueries_UseCssInitialValuesForBooleanFeatures(string property)
    {
        var (root, _, child) = Tree($"style({property}:initial)", "normal");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyleSheet(root, $".target {{ width:11px }} @container style({property}) {{ .target {{ width:101px }} }}");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Theory]
    [InlineData("style(opacity)", false)]
    [InlineData("style(opacity:initial)", true)]
    [InlineData("style(opacity:unset)", true)]
    [InlineData("style(opacity:1)", true)]
    [InlineData("style(opacity:invalid)", false)]
    [InlineData("not style(opacity:.2)", true)]
    [InlineData("not style(unsupported-property:value)", false)]
    public void OrdinaryStyleQueries_UseInitialAndUnknownFeatureRules(string condition, bool expected)
    {
        var (root, _, child) = Tree(condition, "normal");
        Layout(root); Assert.Equal(expected ? 101 : 11, child.Width);
    }

    [Fact]
    public void OrdinaryStyleQueries_ResolveValuesOnTheContainerAndRefreshNativeChanges()
    {
        var (root, container, child) = Tree("style(opacity:var(--expected, attr(alpha number)))", "normal");
        Css.SetAttribute(container, "alpha", ".5");
        container.Opacity = .5;
        Layout(root); Assert.Equal(101, child.Width);
        container.Opacity = .3;
        root.UpdateLayout(); Assert.Equal(11, child.Width);
        Css.SetStyle(container, "--expected:.3");
        root.UpdateLayout(); Assert.Equal(101, child.Width);
    }

    [Theory]
    [InlineData("style(direction:rtl)", true)]
    [InlineData("style(direction:inherit)", true)]
    [InlineData("style(direction:unset)", true)]
    [InlineData("style(direction:initial)", false)]
    [InlineData("style(direction)", true)]
    public void OrdinaryStyleQueries_ReadInheritedComputedDirection(string condition, bool expected)
    {
        var (root, _, child) = Tree(condition, "normal");
        Css.SetStyle(root, "direction:rtl");
        Layout(root); Assert.Equal(expected ? 101 : 11, child.Width);
    }

    [Fact]
    public void OrdinaryStyleQueries_ComputeExplicitInheritanceForNonInheritedOpacity()
    {
        var (root, container, child) = Tree("style(opacity:inherit)", "normal");
        Css.SetStyle(root, "opacity:.4");
        Css.SetStyle(container, "opacity:.4");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(container, "opacity:.6");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Fact]
    public void OrdinaryStyleQueries_RefreshAfterInheritedTextPropertyChanges()
    {
        var (root, _, child) = Tree("style(text-transform:uppercase)", "normal");
        Css.SetStyle(root, "text-transform:uppercase");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(root, "text-transform:lowercase");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Theory]
    [InlineData("width", "1in", "96px", true)]
    [InlineData("height", "2in", "192px", true)]
    [InlineData("inline-size", "96px", "96px", true)]
    [InlineData("block-size", "96px", "96px", true)]
    [InlineData("max-width", "400px", "400px", true)]
    [InlineData("max-height", "400px", "300px", false)]
    [InlineData("max-inline-size", "400px", "400px", true)]
    [InlineData("max-block-size", "400px", "400px", true)]
    [InlineData("min-width", "100px", "100px", true)]
    [InlineData("min-height", "100px", "80px", false)]
    [InlineData("min-inline-size", "100px", "100px", true)]
    [InlineData("min-block-size", "100px", "100px", true)]
    [InlineData("width", "50%", "50%", true)]
    [InlineData("width", "50%", "400px", false)]
    [InlineData("max-width", "50%", "50%", true)]
    [InlineData("max-height", "50%", "400px", false)]
    [InlineData("min-width", "50%", "50%", true)]
    [InlineData("min-height", "50%", "100px", false)]
    [InlineData("width", "0px", ".0000000001px", false)]
    [InlineData("width", "0%", "0px", false)]
    [InlineData("width", "0%", "0%", true)]
    [InlineData("width", "calc(0% + 5px)", "5px", false)]
    [InlineData("width", "calc(25% + 10px)", "calc(25% + 10px)", true)]
    [InlineData("width", "calc(25% + 10px)", "calc(10px + 25%)", true)]
    [InlineData("width", "calc(25% + 10px)", "calc(50% / 2 + 10px)", true)]
    [InlineData("width", "calc(25% + 10px)", "calc(20% + 10px)", false)]
    [InlineData("width", "calc(25% + 10px)", "calc(25% + 11px)", false)]
    [InlineData("width", "calc(50%)", "50%", true)]
    [InlineData("width", "calc(1em + 25%)", "calc(14px + 25%)", true)]
    public void OrdinarySizeStyleQueries_CompareComputedLengthsAndPercentages(
        string property, string authored, string expectedValue, bool expected)
    {
        var (root, container, child) = Tree($"style({property}:{expectedValue})", "normal");
        container.ClearValue(FrameworkElement.WidthProperty);
        container.ClearValue(FrameworkElement.HeightProperty);
        Css.SetStyle(container, $"{property}:{authored}");
        Layout(root); Assert.Equal(expected ? 101 : 11, child.Width);
    }

    [Theory]
    [InlineData("width", "auto")]
    [InlineData("height", "auto")]
    [InlineData("inline-size", "auto")]
    [InlineData("block-size", "auto")]
    [InlineData("max-width", "none")]
    [InlineData("max-height", "none")]
    [InlineData("max-inline-size", "none")]
    [InlineData("max-block-size", "none")]
    [InlineData("min-width", "auto")]
    [InlineData("min-height", "auto")]
    [InlineData("min-inline-size", "auto")]
    [InlineData("min-block-size", "auto")]
    public void OrdinarySizeStyleQueries_CompareInitialAndBooleanValues(string property, string initial)
    {
        var (root, container, child) = Tree($"style({property}:{initial})", "normal");
        container.ClearValue(FrameworkElement.WidthProperty);
        container.ClearValue(FrameworkElement.HeightProperty);
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyleSheet(root, $".target {{ width:11px }} @container style({property}) {{ .target {{ width:101px }} }}");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Theory]
    [InlineData("width", "400px", "style(width)", true)]
    [InlineData("width", "400px", "style(width:initial)", false)]
    [InlineData("width", "400px", "style(width:unset)", false)]
    [InlineData("max-width", "400px", "style(max-width)", true)]
    [InlineData("max-width", "400px", "style(max-width:initial)", false)]
    [InlineData("min-width", "0px", "style(min-width)", true)]
    [InlineData("min-width", "0px", "style(min-width:initial)", false)]
    [InlineData("min-inline-size", "0px", "style(min-inline-size:unset)", false)]
    public void OrdinarySizeStyleQueries_CompareCssWideValues(
        string property, string authored, string condition, bool matches)
    {
        var (root, container, child) = Tree(condition, "normal");
        container.ClearValue(FrameworkElement.WidthProperty);
        container.ClearValue(FrameworkElement.HeightProperty);
        Css.SetStyle(container, $"{property}:{authored}");
        Layout(root); Assert.Equal(matches ? 101 : 11, child.Width);
    }

    [Fact]
    public void OrdinarySizeStyleQueries_ResolveExpectedEmAgainstContainerFont()
    {
        var (root, container, child) = Tree("style(width:calc(1em + 25%))", "normal");
        container.ClearValue(FrameworkElement.WidthProperty);
        Css.SetStyle(container, "font-size:20px;width:calc(20px + 25%)");
        Layout(root); Assert.Equal(101, child.Width);
    }

    [Fact]
    public void OrdinarySizeStyleQueries_ResolveExpectedVarOnSelectedContainer()
    {
        var (root, container, child) = Tree("style(width:var(--query-width))", "normal");
        container.ClearValue(FrameworkElement.WidthProperty);
        Css.SetStyle(container, "--query-width:50%;width:50%");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(container, "--query-width:40%;width:50%");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Theory]
    [InlineData("min-width")]
    [InlineData("min-height")]
    [InlineData("min-inline-size")]
    [InlineData("min-block-size")]
    public void OrdinaryMinSizeStyleQueries_DistinguishAutoFromZero(string property)
    {
        var (root, container, child) = Tree($"style({property}:auto)", "normal");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(container, $"{property}:0px");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
        Css.SetStyle(container, $"{property}:auto");
        root.UpdateLayout(); Assert.Equal(101, child.Width);
    }

    [Fact]
    public void OrdinaryMinSizeStyleQueries_InheritDefaultAuto()
    {
        var (root, container, child) = Tree("style(min-width:auto)", "normal");
        Css.SetStyle(container, "min-width:inherit");
        Layout(root); Assert.Equal(101, child.Width);
        root.MinWidth = 20;
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Fact]
    public void OrdinaryMinSizeStyleQueries_NativeZeroOverridesDefaultAuto()
    {
        var (root, container, child) = Tree("style(min-width:0px)", "normal");
        Layout(root); Assert.Equal(11, child.Width);
        container.MinWidth = 0;
        root.UpdateLayout(); Assert.Equal(101, child.Width);
        container.ClearValue(FrameworkElement.MinWidthProperty);
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Fact]
    public void OrdinaryMinSizeStyleQueries_NativeZeroOverridesCssAuto()
    {
        var (root, container, child) = Tree("style(min-width:auto)", "normal");
        Css.SetStyle(container, "min-width:auto");
        Layout(root); Assert.Equal(101, child.Width);
        container.MinWidth = 0;
        root.UpdateLayout(); Assert.Equal(11, child.Width);
        container.ClearValue(FrameworkElement.MinWidthProperty);
        root.UpdateLayout(); Assert.Equal(101, child.Width);
    }

    [Fact]
    public void OrdinarySizeStyleQueries_NativeAutoOverridesCssPercentage()
    {
        var (root, container, child) = Tree("style(width:50%)", "normal");
        container.ClearValue(FrameworkElement.WidthProperty);
        Css.SetStyle(container, "width:50%");
        Layout(root); Assert.Equal(101, child.Width);
        container.Width = double.NaN;
        root.UpdateLayout(); Assert.Equal(11, child.Width);
        container.ClearValue(FrameworkElement.WidthProperty);
        root.UpdateLayout(); Assert.Equal(101, child.Width);
    }

    [Theory]
    [InlineData("style(box-sizing:content-box)", true)]
    [InlineData("style(box-sizing:initial)", true)]
    [InlineData("style(box-sizing)", false)]
    [InlineData("style(box-sizing:border-box)", false)]
    [InlineData("style(BOX-SIZING:content-box)", true)]
    [InlineData("style(aspect-ratio:auto)", true)]
    [InlineData("style(ASPECT-RATIO:auto)", true)]
    [InlineData("style(aspect-ratio)", false)]
    [InlineData("style(position:static)", true)]
    [InlineData("style(position)", false)]
    public void OrdinaryLayoutStyleQueries_CompareInitialValues(string condition, bool matches)
    {
        var (_, _, child) = Tree(condition, "normal");
        Assert.Equal(matches ? 101 : 11, child.Width);
    }

    [Theory]
    [InlineData("box-sizing", "border-box", "border-box", true)]
    [InlineData("box-sizing", "border-box", "content-box", false)]
    [InlineData("box-sizing", "border-box", "initial", false)]
    [InlineData("box-sizing", "border-box", "unset", false)]
    [InlineData("aspect-ratio", "2 / 1", "2", true)]
    [InlineData("aspect-ratio", "2 / 1", "4 / 2", false)]
    [InlineData("aspect-ratio", "2 / 1", "initial", false)]
    [InlineData("aspect-ratio", "2 / 1", "unset", false)]
    [InlineData("aspect-ratio", "auto 2 / 1", "2 / 1 auto", true)]
    [InlineData("aspect-ratio", "auto 2 / 1", "2 / 1", false)]
    [InlineData("aspect-ratio", "0 / 1", "auto", false)]
    [InlineData("aspect-ratio", "0 / 1", "0 / 1", true)]
    [InlineData("position", "relative", "relative", true)]
    [InlineData("position", "absolute", "absolute", true)]
    [InlineData("position", "fixed", "fixed", true)]
    [InlineData("position", "sticky", "sticky", true)]
    [InlineData("position", "fixed", "static", false)]
    [InlineData("position", "sticky", "relative", false)]
    [InlineData("position", "fixed", "initial", false)]
    [InlineData("position", "sticky", "unset", false)]
    public void OrdinaryLayoutStyleQueries_CompareComputedValues(
        string property, string authored, string expectedValue, bool matches)
    {
        var (root, container, child) = Tree($"style({property}:{expectedValue})", "normal");
        Css.SetStyle(container, $"{property}:{authored}");
        Layout(root); Assert.Equal(matches ? 101 : 11, child.Width);
    }

    [Theory]
    [InlineData("box-sizing", "border-box", "content-box")]
    [InlineData("aspect-ratio", "2 / 1", "3 / 1")]
    [InlineData("position", "relative", "absolute")]
    public void OrdinaryLayoutStyleQueries_RefreshWhenComputedValueChanges(
        string property, string first, string second)
    {
        var (root, container, child) = Tree($"style({property}:{first})", "normal");
        Css.SetStyle(container, $"{property}:{first}");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(container, $"{property}:{second}");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Theory]
    [InlineData("box-sizing", "border-box", "content-box")]
    [InlineData("aspect-ratio", "2 / 1", "3 / 1")]
    [InlineData("position", "fixed", "sticky")]
    public void OrdinaryLayoutStyleQueries_InheritComputedParentValue(
        string property, string first, string second)
    {
        var (root, container, child) = Tree($"style({property}:{first})", "normal");
        Css.SetStyle(root, $"{property}:{first}");
        Css.SetStyle(container, $"{property}:inherit");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(root, $"{property}:{second}");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Theory]
    [InlineData("box-sizing", "border-box", "content-box")]
    [InlineData("aspect-ratio", "2 / 1", "3 / 1")]
    [InlineData("position", "relative", "absolute")]
    public void OrdinaryLayoutStyleQueries_CompareInheritToParent(
        string property, string first, string second)
    {
        var (root, container, child) = Tree($"style({property}:inherit)", "normal");
        Css.SetStyle(root, $"{property}:{first}");
        Css.SetStyle(container, $"{property}:{first}");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(container, $"{property}:{second}");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Theory]
    [InlineData("box-sizing", "content-box", "border-box")]
    [InlineData("aspect-ratio", "2 / 1", "3 / 1")]
    [InlineData("position", "static", "sticky")]
    public void OrdinaryLayoutStyleQueries_RefreshAfterContainerStateRuleChanges(
        string property, string initial, string hovered)
    {
        var (root, container, child) = Tree($"style({property}:{hovered})", "normal");
        Css.SetClass(container, "query-box");
        Css.SetStyleSheet(root,
            $".query-box {{{property}:{initial}}} .query-box:hover {{{property}:{hovered}}} " +
            $".target {{width:11px}} @container style({property}:{hovered}) {{.target {{width:101px}}}}");
        Layout(root); Assert.Equal(11, child.Width);
        container.SetIsMouseOver(true);
        root.UpdateLayout(); Assert.Equal(101, child.Width);
        container.SetIsMouseOver(false);
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Theory]
    [InlineData("top")]
    [InlineData("right")]
    [InlineData("bottom")]
    [InlineData("left")]
    [InlineData("inset-inline-start")]
    [InlineData("inset-inline-end")]
    [InlineData("inset-block-start")]
    [InlineData("inset-block-end")]
    [InlineData("inset")]
    [InlineData("inset-inline")]
    [InlineData("inset-block")]
    public void OrdinaryInsetStyleQueries_CompareInitialAndBooleanValues(string property)
    {
        var (_, _, child) = Tree($"style({property}:auto)", "normal");
        Assert.Equal(101, child.Width);
        var (root, container, boolChild) = Tree($"style({property})", "normal");
        Assert.Equal(11, boolChild.Width);
        Css.SetStyle(container, property + ":10px");
        root.UpdateLayout(); Assert.Equal(101, boolChild.Width);
    }

    [Theory]
    [InlineData("top", "20px", "20px", true)]
    [InlineData("right", "-10px", "-10px", true)]
    [InlineData("bottom", "25%", "25%", true)]
    [InlineData("left", "50%", "200px", false)]
    [InlineData("left", "0%", "0px", false)]
    [InlineData("left", "calc(10px + 5%)", "calc(5% + 10px)", true)]
    [InlineData("left", "calc(10px + 5%)", "calc(10px + 6%)", false)]
    [InlineData("inset-inline-start", "15px", "15px", true)]
    [InlineData("inset-inline-end", "20%", "20%", true)]
    [InlineData("inset-block-start", "-4px", "-4px", true)]
    [InlineData("inset-block-end", "30%", "30%", true)]
    [InlineData("top", "0px", "initial", false)]
    [InlineData("right", "10px", "unset", false)]
    [InlineData("bottom", "5px", "revert", false)]
    public void OrdinaryInsetStyleQueries_CompareComputedLengths(
        string property, string authored, string expected, bool matches)
    {
        var (root, container, child) = Tree($"style({property}:{expected})", "normal");
        Css.SetStyle(container, $"{property}:{authored}");
        Layout(root); Assert.Equal(matches ? 101 : 11, child.Width);
    }

    [Theory]
    [InlineData("inset", "10px 20px 30px 40px", "10px 20px 30px 40px", true)]
    [InlineData("inset", "10px 20px", "10px 20px 10px 20px", true)]
    [InlineData("inset", "10px 20px", "10px 20px 10px 25px", false)]
    [InlineData("inset-inline", "5% 10px", "5% 10px", true)]
    [InlineData("inset-inline", "5% 10px", "5% 11px", false)]
    [InlineData("inset-block", "-5px", "-5px -5px", true)]
    public void OrdinaryInsetStyleQueries_CompareShorthandLonghands(
        string property, string authored, string expected, bool matches)
    {
        var (root, container, child) = Tree($"style({property}:{expected})", "normal");
        Css.SetStyle(container, $"{property}:{authored}");
        Layout(root); Assert.Equal(matches ? 101 : 11, child.Width);
    }

    [Fact]
    public void OrdinaryInsetStyleQueries_MapLogicalEdgesThroughRightToLeftDirection()
    {
        var (root, container, child) = Tree("style(right:15px) and style(inset-inline-start:15px)", "normal");
        Css.SetStyle(container, "direction:rtl; inset-inline-start:15px");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(container, "direction:ltr; inset-inline-start:15px");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Fact]
    public void OrdinaryInsetStyleQueries_RetainStaticPercentagesAndRefreshOnChange()
    {
        var (root, container, child) = Tree("style(left:25%)", "normal");
        Css.SetStyle(container, "left:25%");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(container, "left:30%");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Fact]
    public void OrdinaryInsetStyleQueries_NativeCanvasOffsetOverridesCssPercentage()
    {
        var (root, container, child) = Tree("style(left:25%)", "normal");
        Css.SetStyle(container, "left:25%");
        Layout(root); Assert.Equal(101, child.Width);
        Canvas.SetLeft(container, 100);
        root.UpdateLayout(); Assert.Equal(11, child.Width);
        Canvas.SetLeft(container, double.NaN);
        root.UpdateLayout(); Assert.Equal(11, child.Width);
        container.ClearValue(Canvas.LeftProperty);
        root.UpdateLayout(); Assert.Equal(101, child.Width);
    }

    [Fact]
    public void OrdinaryInsetStyleQueries_InheritPercentageFromStaticParent()
    {
        var (root, container, child) = Tree("style(top:inherit)", "normal");
        Css.SetStyle(root, "top:25%");
        Css.SetStyle(container, "top:25%");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(root, "top:30%");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Fact]
    public void OrdinaryInsetStyleQueries_ApplyExplicitInheritanceFromStaticParent()
    {
        var (root, container, child) = Tree("style(top:25%)", "normal");
        Css.SetStyle(root, "top:25%");
        Css.SetStyle(container, "top:inherit");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(root, "top:30%");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Theory]
    [InlineData("inset")]
    [InlineData("inset-inline")]
    [InlineData("inset-block")]
    public void OrdinaryInsetStyleQueries_CompareShorthandCssWideValues(string property)
    {
        var (root, container, child) = Tree($"style({property}:initial)", "normal");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(container, property + ":10px");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
        Css.SetStyle(container, property + ":unset");
        root.UpdateLayout(); Assert.Equal(101, child.Width);
    }

    [Fact]
    public void OrdinaryInsetStyleQueries_RefreshAfterContainerStateRuleChanges()
    {
        var (root, container, child) = Tree("style(left:25%)", "normal");
        Css.SetClass(container, "query-box");
        Css.SetStyleSheet(root,
            ".query-box {left:10%} .query-box:hover {left:25%} " +
            ".target {width:11px} @container style(left:25%) {.target {width:101px}}");
        Layout(root); Assert.Equal(11, child.Width);
        container.SetIsMouseOver(true);
        root.UpdateLayout(); Assert.Equal(101, child.Width);
        container.SetIsMouseOver(false);
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Theory]
    [InlineData("margin-top")]
    [InlineData("margin-right")]
    [InlineData("margin-bottom")]
    [InlineData("margin-left")]
    [InlineData("margin-inline-start")]
    [InlineData("margin-inline-end")]
    [InlineData("margin-block-start")]
    [InlineData("margin-block-end")]
    [InlineData("padding-top")]
    [InlineData("padding-right")]
    [InlineData("padding-bottom")]
    [InlineData("padding-left")]
    [InlineData("padding-inline-start")]
    [InlineData("padding-inline-end")]
    [InlineData("padding-block-start")]
    [InlineData("padding-block-end")]
    public void OrdinarySpacingStyleQueries_InitialAndBooleanValues(string property)
    {
        var (_, _, initialChild) = Tree($"style({property}:0px)", "normal");
        Assert.Equal(101, initialChild.Width);
        var (root, container, child) = Tree($"style({property})", "normal");
        Assert.Equal(11, child.Width);
        Css.SetStyle(container, property + ":10px");
        root.UpdateLayout(); Assert.Equal(101, child.Width);
    }

    [Theory]
    [InlineData("margin-left", "-10px", "-10px", true)]
    [InlineData("margin-top", "auto", "auto", true)]
    [InlineData("margin-right", "25%", "25%", true)]
    [InlineData("margin-left", "0%", "0px", false)]
    [InlineData("margin-bottom", "25%", "50px", false)]
    [InlineData("margin-inline-start", "12px", "12px", true)]
    [InlineData("margin-inline-end", "15%", "15%", true)]
    [InlineData("margin-block-start", "-4px", "-4px", true)]
    [InlineData("margin-block-end", "auto", "auto", true)]
    [InlineData("padding-left", "20px", "20px", true)]
    [InlineData("padding-top", "25%", "25%", true)]
    [InlineData("padding-right", "25%", "50px", false)]
    [InlineData("padding-bottom", "calc(10px + 5%)", "calc(5% + 10px)", true)]
    [InlineData("padding-left", "calc(0% + 5px)", "5px", false)]
    [InlineData("padding-inline-start", "10px", "10px", true)]
    [InlineData("padding-inline-end", "5%", "5%", true)]
    [InlineData("padding-block-start", "10px", "10px", true)]
    [InlineData("padding-block-end", "5%", "5%", true)]
    [InlineData("padding-top", "calc(-5px)", "0px", true)]
    [InlineData("padding-top", "calc(-5px)", "-5px", false)]
    [InlineData("padding-top", "calc(-5%)", "0%", true)]
    [InlineData("padding-top", "calc(-5%)", "0px", false)]
    [InlineData("padding-top", "5px", "initial", false)]
    [InlineData("margin-left", "0px", "unset", true)]
    [InlineData("margin-left", "5px", "revert", false)]
    public void OrdinarySpacingStyleQueries_CompareComputedValues(
        string property, string authored, string expected, bool matches)
    {
        var (root, container, child) = Tree($"style({property}:{expected})", "normal");
        Css.SetStyle(container, property + ":" + authored);
        Layout(root); Assert.Equal(matches ? 101 : 11, child.Width);
    }

    [Theory]
    [InlineData("margin", "10px 20px 30px 40px", "10px 20px 30px 40px", true)]
    [InlineData("margin", "10px 20px", "10px 20px 10px 20px", true)]
    [InlineData("margin", "10px 20px", "10px 20px 10px 21px", false)]
    [InlineData("margin-inline", "auto 20%", "auto 20%", true)]
    [InlineData("margin-block", "5px", "5px 5px", true)]
    [InlineData("padding", "1px 2px 3px 4px", "1px 2px 3px 4px", true)]
    [InlineData("padding", "1px 2px 3px", "1px 2px 4px", false)]
    [InlineData("padding-inline", "5% 10px", "5% 10px", true)]
    [InlineData("padding-block", "10px", "10px 10px", true)]
    public void OrdinarySpacingStyleQueries_CompareShorthandLonghands(
        string property, string authored, string expected, bool matches)
    {
        var (root, container, child) = Tree($"style({property}:{expected})", "normal");
        Css.SetStyle(container, property + ":" + authored);
        Layout(root); Assert.Equal(matches ? 101 : 11, child.Width);
    }

    [Fact]
    public void OrdinarySpacingStyleQueries_MapLogicalEdgesThroughDirection()
    {
        var (root, container, child) = Tree(
            "style(margin-right:15px) and style(margin-inline-start:15px)", "normal");
        Css.SetStyle(container, "direction:rtl; margin-inline-start:15px");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(container, "direction:ltr; margin-inline-start:15px");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Fact]
    public void OrdinarySpacingStyleQueries_NativeMarginOverridesComputedPercentage()
    {
        var (root, container, child) = Tree("style(margin-left:25%)", "normal");
        Css.SetStyle(container, "margin-left:25%");
        Layout(root); Assert.Equal(101, child.Width);
        container.Margin = new Thickness(100, 0, 0, 0);
        root.UpdateLayout(); Assert.Equal(11, child.Width);
        container.ClearValue(FrameworkElement.MarginProperty);
        root.UpdateLayout(); Assert.Equal(101, child.Width);
    }

    [Fact]
    public void OrdinarySpacingStyleQueries_NativeZeroOverridesZeroPercentageWithoutValueChange()
    {
        var (root, container, child) = Tree("style(margin-left:0%)", "normal");
        Css.SetStyle(container, "margin-left:0%");
        Layout(root); Assert.Equal(101, child.Width);
        container.Margin = default;
        root.UpdateLayout(); Assert.Equal(11, child.Width);
        container.ClearValue(FrameworkElement.MarginProperty);
        root.UpdateLayout(); Assert.Equal(101, child.Width);
    }

    [Fact]
    public void OrdinarySpacingStyleQueries_NativePaddingOverridesComputedPercentage()
    {
        var child = new Border();
        var container = new Border { Width = 400, Height = 200, Child = child };
        var root = new Border { Child = container };
        Css.SetClass(child, "target");
        Css.SetStyle(container, "padding-left:25%");
        Css.SetStyleSheet(root,
            ".target {width:11px} @container style(padding-left:25%) {.target {width:101px}}");
        Layout(root); Assert.Equal(101, child.Width);
        container.Padding = new Thickness(100, 0, 0, 0);
        root.UpdateLayout(); Assert.Equal(11, child.Width);
        container.ClearValue(Border.PaddingProperty);
        root.UpdateLayout(); Assert.Equal(101, child.Width);
    }

    [Fact]
    public void OrdinarySpacingStyleQueries_InheritComputedPercentage()
    {
        var (root, container, child) = Tree("style(margin-top:25%)", "normal");
        Css.SetStyle(root, "margin-top:25%");
        Css.SetStyle(container, "margin-top:inherit");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(root, "margin-top:30%");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Fact]
    public void OrdinarySpacingStyleQueries_InheritPaddingFromPanelWithoutNativePaddingProperty()
    {
        var child = new Border();
        var inner = new StackPanel(); inner.Children.Add(child);
        var outer = new StackPanel(); outer.Children.Add(inner);
        var root = new Border { Child = outer };
        Css.SetClass(child, "target");
        Css.SetStyle(outer, "padding-left:25%");
        Css.SetStyle(inner, "padding-left:inherit");
        Css.SetStyleSheet(root,
            ".target {width:11px} @container style(padding-left:25%) {.target {width:101px}}");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(outer, "padding-left:30%");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Theory]
    [InlineData("margin")]
    [InlineData("margin-inline")]
    [InlineData("margin-block")]
    [InlineData("padding")]
    [InlineData("padding-inline")]
    [InlineData("padding-block")]
    public void OrdinarySpacingStyleQueries_CompareShorthandCssWideValues(string property)
    {
        var (root, container, child) = Tree($"style({property}:initial)", "normal");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(container, property + ":10px");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
        Css.SetStyle(container, property + ":unset");
        root.UpdateLayout(); Assert.Equal(101, child.Width);
    }

    [Fact]
    public void OrdinarySpacingStyleQueries_RefreshAfterContainerStateRuleChanges()
    {
        var (root, container, child) = Tree("style(padding-left:25%)", "normal");
        Css.SetClass(container, "query-box");
        Css.SetStyleSheet(root,
            ".query-box {padding-left:10%} .query-box:hover {padding-left:25%} " +
            ".target {width:11px} @container style(padding-left:25%) {.target {width:101px}}");
        Layout(root); Assert.Equal(11, child.Width);
        container.SetIsMouseOver(true);
        root.UpdateLayout(); Assert.Equal(101, child.Width);
        container.SetIsMouseOver(false);
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Theory]
    [InlineData("style(display:inline)", true)]
    [InlineData("style(display:inline flow)", true)]
    [InlineData("style(display:initial)", true)]
    [InlineData("style(display:unset)", true)]
    [InlineData("style(display)", false)]
    [InlineData("style(display:block)", false)]
    public void OrdinaryDisplayStyleQueries_CompareInitialValue(string condition, bool matches)
    {
        var (_, _, child) = Tree(condition, "normal");
        Assert.Equal(matches ? 101 : 11, child.Width);
    }

    [Theory]
    [InlineData("block", "block flow", true)]
    [InlineData("flow-root", "block flow-root", true)]
    [InlineData("inline-block", "inline flow-root", true)]
    [InlineData("inline-flex", "inline flex", true)]
    [InlineData("inline-grid", "inline grid", true)]
    [InlineData("flex", "inline-flex", false)]
    [InlineData("grid", "flex", false)]
    [InlineData("none", "none", true)]
    [InlineData("none", "block", false)]
    [InlineData("block", "revert", false)]
    [InlineData("block", "block flow-root grid", false)]
    public void OrdinaryDisplayStyleQueries_CompareComputedTypes(
        string authored, string queried, bool matches)
    {
        var (root, container, child) = Tree($"style(display:{queried})", "normal");
        Css.SetStyle(container, "display:" + authored);
        Layout(root); Assert.Equal(matches ? 101 : 11, child.Width);
    }

    [Fact]
    public void OrdinaryDisplayStyleQueries_InheritRootComputedBlockValue()
    {
        var (root, container, child) = Tree("style(display:block)", "normal");
        Css.SetStyle(container, "display:inherit");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(root, "display:inline-flex");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
        Css.SetStyle(root, "display:inline");
        root.UpdateLayout(); Assert.Equal(101, child.Width);
    }

    [Fact]
    public void OrdinaryDisplayStyleQueries_RootInitialComputesToBlock()
    {
        var child = new Border();
        var root = new StackPanel(); root.Children.Add(child);
        Css.SetClass(child, "target");
        Css.SetStyle(root, "container-name:root");
        Css.SetStyleSheet(root,
            ".target {width:11px} @container root style(display) and style(display:initial) " +
            "{.target {width:101px}}");
        Layout(root); Assert.Equal(101, child.Width);
    }

    [Theory]
    [InlineData("display:inline;float:left")]
    [InlineData("display:inline;position:absolute")]
    [InlineData("display:inline;position:fixed")]
    public void OrdinaryDisplayStyleQueries_BlockifyFloatsAndPositionedBoxes(string authored)
    {
        var (root, container, child) = Tree("style(display:block)", "normal");
        Css.SetStyle(container, authored);
        Layout(root); Assert.Equal(101, child.Width);
    }

    [Fact]
    public void OrdinaryDisplayStyleQueries_CompareBlockifiedInitialAndFlowRoot()
    {
        var (root, container, child) = Tree(
            "style(display:initial) and style(display:block)", "normal");
        Css.SetStyle(container, "display:inline;float:left");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyleSheet(root,
            ".target {width:11px} @container style(display:block flow-root) {.target {width:101px}}");
        Css.SetStyle(container, "display:inline-block;float:left");
        Layout(root); Assert.Equal(101, child.Width);
    }

    [Theory]
    [InlineData("flex")]
    [InlineData("grid")]
    public void OrdinaryDisplayStyleQueries_BlockifyFlexAndGridItems(string parentDisplay)
    {
        var child = new Border();
        var container = new StackPanel(); container.Children.Add(child);
        var root = new StackPanel(); root.Children.Add(container);
        Css.SetClass(child, "target");
        Css.SetStyle(root, "display:" + parentDisplay);
        Css.SetStyle(container, "display:inline-flex");
        Css.SetStyleSheet(root,
            ".target {width:11px} @container style(display:flex) {.target {width:101px}}");
        Layout(root); Assert.Equal(101, child.Width);
    }

    [Fact]
    public void OrdinaryDisplayStyleQueries_RetainCssValueUnderNativeVisibilityOverride()
    {
        var (root, container, child) = Tree("style(display:flex)", "normal");
        Css.SetStyle(container, "display:flex");
        Layout(root); Assert.Equal(101, child.Width);
        container.Visibility = Visibility.Hidden;
        root.UpdateLayout(); Assert.Equal(101, child.Width);
    }

    [Fact]
    public void OrdinaryDisplayStyleQueries_RefreshWhenParentDisplayChanges()
    {
        var child = new Border();
        var container = new StackPanel(); container.Children.Add(child);
        var root = new StackPanel(); root.Children.Add(container);
        Css.SetClass(child, "target");
        Css.SetStyle(root, "display:block");
        Css.SetStyle(container, "display:inline");
        Css.SetStyleSheet(root,
            ".target {width:11px} @container style(display:block) {.target {width:101px}}");
        Layout(root); Assert.Equal(11, child.Width);
        Css.SetStyle(root, "display:flex");
        root.UpdateLayout(); Assert.Equal(101, child.Width);
        Css.SetStyle(root, "display:block");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
        root.SetValue(CssDisplayProperties.SpecificationProperty,
            new CssDisplaySpecification(CssDisplayMode.Flex));
        root.UpdateLayout(); Assert.Equal(101, child.Width);
        root.ClearValue(CssDisplayProperties.SpecificationProperty);
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Fact]
    public void OrdinaryDisplayStyleQueries_ExplicitInheritanceTracksParentBlockification()
    {
        var child = new Border();
        var container = new StackPanel(); container.Children.Add(child);
        var parent = new StackPanel(); parent.Children.Add(container);
        var root = new StackPanel(); root.Children.Add(parent);
        Css.SetClass(child, "target");
        Css.SetStyle(root, "display:block");
        Css.SetStyle(parent, "display:inline");
        Css.SetStyle(container, "display:inherit");
        Css.SetStyleSheet(root,
            ".target {width:11px} @container style(display:block) {.target {width:101px}}");
        Layout(root); Assert.Equal(11, child.Width);
        parent.SetValue(CssFloatProperties.FloatProperty, CssFloatSide.Left);
        root.UpdateLayout(); Assert.Equal(101, child.Width);
        parent.ClearValue(CssFloatProperties.FloatProperty);
        root.UpdateLayout(); Assert.Equal(11, child.Width);
        Css.SetStyle(parent, "display:inline;position:absolute");
        root.UpdateLayout(); Assert.Equal(101, child.Width);
    }

    [Fact]
    public void OrdinaryDisplayStyleQueries_RefreshAfterContainerStateRuleChanges()
    {
        var (root, container, child) = Tree("style(display:flex)", "normal");
        Css.SetClass(container, "query-box");
        Css.SetStyleSheet(root,
            ".query-box {display:block} .query-box:hover {display:flex} " +
            ".target {width:11px} @container style(display:flex) {.target {width:101px}}");
        Layout(root); Assert.Equal(11, child.Width);
        container.SetIsMouseOver(true);
        root.UpdateLayout(); Assert.Equal(101, child.Width);
        container.SetIsMouseOver(false);
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Theory]
    [InlineData("style(visibility:visible)", true)]
    [InlineData("style(visibility:initial)", true)]
    [InlineData("style(visibility:unset)", true)]
    [InlineData("style(visibility)", false)]
    [InlineData("style(visibility:hidden)", false)]
    public void OrdinaryVisibilityStyleQueries_CompareInitialValue(string condition, bool matches)
    {
        var (_, _, child) = Tree(condition, "normal");
        Assert.Equal(matches ? 101 : 11, child.Width);
    }

    [Theory]
    [InlineData("hidden", "hidden", true)]
    [InlineData("collapse", "collapse", true)]
    [InlineData("visible", "hidden", false)]
    [InlineData("hidden", "collapse", false)]
    [InlineData("collapse", "hidden", false)]
    [InlineData("hidden", "initial", false)]
    [InlineData("hidden", "revert", false)]
    [InlineData("hidden", "hidden visible", false)]
    public void OrdinaryVisibilityStyleQueries_CompareComputedKeyword(
        string authored, string queried, bool matches)
    {
        var (root, container, child) = Tree($"style(visibility:{queried})", "normal");
        Css.SetStyle(container, "visibility:" + authored);
        Layout(root); Assert.Equal(matches ? 101 : 11, child.Width);
    }

    [Fact]
    public void OrdinaryVisibilityStyleQueries_TrackInheritedAndNativeParentValues()
    {
        var (root, container, child) = Tree("style(visibility:hidden)", "normal");
        Css.SetStyle(root, "visibility:hidden");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(container, "visibility:visible");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
        Css.SetStyle(container, "visibility:unset");
        root.UpdateLayout(); Assert.Equal(101, child.Width);
        root.Visibility = Visibility.Visible;
        root.UpdateLayout(); Assert.Equal(11, child.Width);
        root.ClearValue(UIElement.VisibilityProperty);
        root.UpdateLayout(); Assert.Equal(101, child.Width);
    }

    [Fact]
    public void OrdinaryVisibilityStyleQueries_CompareInheritedExpectedValue()
    {
        var (root, container, child) = Tree(
            "style(visibility:inherit) and style(visibility)", "normal");
        Css.SetStyle(root, "visibility:hidden");
        Css.SetStyle(container, "visibility:inherit");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(container, "visibility:visible");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
        Css.SetStyle(root, "visibility:visible");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Fact]
    public void OrdinaryVisibilityStyleQueries_ExplicitVisibleSurvivesHiddenNativeAncestor()
    {
        var (root, container, child) = Tree("style(visibility:visible)", "normal");
        root.Visibility = Visibility.Hidden;
        Css.SetStyle(container, "visibility:visible");
        Layout(root); Assert.Equal(101, child.Width);
    }

    [Fact]
    public void OrdinaryVisibilityStyleQueries_NativeLocalValueOverridesCssValue()
    {
        var (root, container, child) = Tree("style(visibility:hidden)", "normal");
        Css.SetStyle(container, "visibility:hidden");
        Layout(root); Assert.Equal(101, child.Width);
        container.Visibility = Visibility.Visible;
        root.UpdateLayout(); Assert.Equal(11, child.Width);
        container.ClearValue(UIElement.VisibilityProperty);
        root.UpdateLayout(); Assert.Equal(101, child.Width);
    }

    [Fact]
    public void OrdinaryVisibilityStyleQueries_DisplayNoneDoesNotChangeVisibilityValue()
    {
        var (root, container, child) = Tree("style(visibility:visible)", "normal");
        Css.SetStyle(container, "display:none");
        Layout(root); Assert.Equal(101, child.Width);
    }

    [Fact]
    public void OrdinaryVisibilityStyleQueries_IgnoreAnimatedDisplayVisibilityMirror()
    {
        var (root, container, _) = Tree("style(visibility:visible)", "normal");
        Css.SetStyle(container, "display:block;visibility:visible");
        Layout(root);
        container.SetAnimatedValue(CssDisplayProperties.SpecificationProperty,
            new CssDisplaySpecification(CssDisplayMode.Native, None: true), holdEndValue: false);
        Assert.Equal(Visibility.Collapsed, container.Visibility);
        Assert.Equal(CssVisibilityMode.Visible,
            CssDisplayProperties.ComputedVisibility(CssNode.Get(container)!));
        container.ClearAnimatedValue(CssDisplayProperties.SpecificationProperty);
    }

    [Fact]
    public void OrdinaryVisibilityStyleQueries_RefreshAfterContainerStateRuleChanges()
    {
        var (root, container, child) = Tree("style(visibility:hidden)", "normal");
        Css.SetClass(container, "query-box");
        Css.SetStyleSheet(root,
            ".query-box {visibility:visible} .query-box:hover {visibility:hidden} " +
            ".target {width:11px} @container style(visibility:hidden) {.target {width:101px}}");
        Layout(root); Assert.Equal(11, child.Width);
        container.SetIsMouseOver(true);
        root.UpdateLayout(); Assert.Equal(101, child.Width);
        container.SetIsMouseOver(false);
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Theory]
    [InlineData("style(color:black)", true)]
    [InlineData("style(color:initial)", true)]
    [InlineData("style(color)", false)]
    [InlineData("style(background-color:transparent)", true)]
    [InlineData("style(background-color:initial)", true)]
    [InlineData("style(background-color)", false)]
    public void OrdinaryColorStyleQueries_CompareInitialValues(string condition, bool matches)
    {
        var (_, _, child) = Tree(condition, "normal");
        Assert.Equal(matches ? 101 : 11, child.Width);
    }

    [Theory]
    [InlineData("color", "#f00", "rgb(255 0 0)", true)]
    [InlineData("color", "red", "blue", false)]
    [InlineData("color", "red", "initial", false)]
    [InlineData("color", "red", "revert", false)]
    [InlineData("background-color", "#0000ff", "blue", true)]
    [InlineData("background-color", "red", "blue", false)]
    [InlineData("background-color", "red", "unset", false)]
    public void OrdinaryColorStyleQueries_CompareAbsoluteColors(
        string property, string authored, string queried, bool matches)
    {
        var (root, container, child) = Tree($"style({property}:{queried})", "normal");
        Css.SetStyle(container, $"{property}:{authored}");
        Layout(root); Assert.Equal(matches ? 101 : 11, child.Width);
    }

    [Theory]
    [InlineData("color", "currentColor", true)]
    [InlineData("color", "red", false)]
    [InlineData("background-color", "currentColor", true)]
    [InlineData("background-color", "red", false)]
    [InlineData("background-color", "color-mix(in srgb, currentColor 50%, blue)", false)]
    public void OrdinaryColorStyleQueries_RetainSymbolicCurrentColor(
        string property, string queried, bool matches)
    {
        var (root, container, child) = Tree($"style({property}:{queried})", "normal");
        Css.SetStyle(root, "color:red");
        Css.SetStyle(container, $"{property}:currentColor");
        Layout(root); Assert.Equal(matches ? 101 : 11, child.Width);
    }

    [Fact]
    public void OrdinaryColorStyleQueries_InheritParentAndRefreshWhenItChanges()
    {
        var (root, container, child) = Tree("style(color:red) and style(color:inherit)", "normal");
        Css.SetStyle(root, "color:red");
        Css.SetStyle(container, "color:inherit");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(root, "color:blue");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Fact]
    public void OrdinaryColorStyleQueries_ImplicitlyInheritColorWithoutADeclaration()
    {
        var (root, _, child) = Tree("style(color:red)", "normal");
        Css.SetStyle(root, "color:red");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(root, "color:blue");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Fact]
    public void OrdinaryColorStyleQueries_KeepInheritedCurrentColorSymbolic()
    {
        var (root, container, child) = Tree("style(color:currentColor)", "normal");
        Css.SetStyle(root, "color:currentColor");
        Css.SetStyle(container, "color:inherit");
        Layout(root); Assert.Equal(101, child.Width);
    }

    [Fact]
    public void OrdinaryColorStyleQueries_InheritedColorMixKeepsItsParentUsedColor()
    {
        var (root, container, child) = Tree(
            "style(color:color-mix(in srgb,currentColor 50%,blue))", "normal");
        Css.SetStyle(root, "color:color-mix(in srgb,currentColor 50%,blue)");
        Css.SetStyle(container, "color:inherit");
        Layout(root); Assert.Equal(101, child.Width);
        var rootColor = Assert.IsType<SolidColorBrush>(root.GetValue(TextElement.ForegroundProperty)).Color;
        var containerColor = Assert.IsType<SolidColorBrush>(container.GetValue(TextElement.ForegroundProperty)).Color;
        Assert.Equal(rootColor, containerColor);
    }

    [Fact]
    public void OrdinaryColorStyleQueries_InheritNativeColorInsteadOfOverriddenSymbol()
    {
        var (root, container, child) = Tree("style(color:red)", "normal");
        Css.SetStyle(root, "color:currentColor");
        root.SetValue(TextElement.ForegroundProperty, new SolidColorBrush(Colors.Red));
        Css.SetStyle(container, "color:inherit");
        Layout(root); Assert.Equal(101, child.Width);
    }

    [Fact]
    public void OrdinaryColorStyleQueries_KeepInheritedBackgroundCurrentColorSymbolic()
    {
        var (root, container, child) = Tree("style(background-color:currentColor)", "normal");
        Css.SetStyle(root, "color:red;background-color:currentColor");
        Css.SetStyle(container, "color:blue;background-color:inherit");
        Layout(root); Assert.Equal(101, child.Width);
    }

    [Fact]
    public void OrdinaryColorStyleQueries_RetainCurrentColorMixUntilDeclarationChanges()
    {
        var (root, container, child) = Tree(
            "style(background-color:color-mix(in srgb, currentColor 50%, blue))", "normal");
        Css.SetStyle(container, "color:red;background-color:color-mix(in srgb,currentcolor 50%,blue)");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(container, "color:red;background-color:blue");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Fact]
    public void OrdinaryColorStyleQueries_InheritNativeBackgroundColor()
    {
        var (root, container, child) = Tree("style(background-color:red)", "normal");
        root.Background = new SolidColorBrush(Colors.Red);
        Css.SetStyle(container, "background-color:inherit");
        Layout(root); Assert.Equal(101, child.Width);
        root.Background = new SolidColorBrush(Colors.Blue);
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Fact]
    public void OrdinaryColorStyleQueries_NativeForegroundOverridesCssColor()
    {
        var (root, container, child) = Tree("style(color:red)", "normal");
        Css.SetStyle(container, "color:red");
        Layout(root); Assert.Equal(101, child.Width);
        container.SetValue(TextElement.ForegroundProperty, new SolidColorBrush(Colors.Blue));
        root.UpdateLayout(); Assert.Equal(11, child.Width);
        container.ClearValue(TextElement.ForegroundProperty);
        root.UpdateLayout(); Assert.Equal(101, child.Width);
    }

    [Fact]
    public void OrdinaryColorStyleQueries_NativeBackgroundOverridesCssBackgroundColor()
    {
        var (root, container, child) = Tree("style(background-color:red)", "normal");
        Css.SetStyle(container, "background-color:red");
        Layout(root); Assert.Equal(101, child.Width);
        container.Background = new SolidColorBrush(Colors.Blue);
        root.UpdateLayout(); Assert.Equal(11, child.Width);
        container.ClearValue(Panel.BackgroundProperty);
        root.UpdateLayout(); Assert.Equal(101, child.Width);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OrdinaryColorStyleQueries_RefreshWhenNativeBrushColorChanges(bool foreground)
    {
        var property = foreground ? "color" : "background-color";
        var (root, container, child) = Tree($"style({property}:red)", "normal");
        var brush = new SolidColorBrush(Colors.Red);
        if (foreground) container.SetValue(TextElement.ForegroundProperty, brush);
        else container.Background = brush;
        Layout(root); Assert.Equal(101, child.Width);
        brush.Color = Colors.Blue;
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Fact]
    public void OrdinaryColorStyleQueries_RefreshAfterContainerStateRuleChanges()
    {
        var (root, container, child) = Tree("style(color:blue)", "normal");
        Css.SetClass(container, "query-box");
        Css.SetStyleSheet(root,
            ".query-box {color:red} .query-box:hover {color:blue} " +
            ".target {width:11px} @container style(color:blue) {.target {width:101px}}");
        Layout(root); Assert.Equal(11, child.Width);
        container.SetIsMouseOver(true);
        root.UpdateLayout(); Assert.Equal(101, child.Width);
        container.SetIsMouseOver(false);
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Theory]
    [InlineData("color")]
    [InlineData("background-color")]
    public void OrdinaryColorStyleQueries_StateRuleReplacesSymbolicColor(string property)
    {
        var (root, container, child) = Tree($"style({property}:currentColor)", "normal");
        Css.SetClass(container, "query-box");
        Css.SetStyle(root, "color:red");
        Css.SetStyleSheet(root,
            $".query-box {{{property}:currentColor}} .query-box:hover {{{property}:blue}} " +
            $".target {{width:11px}} @container style({property}:currentColor) {{.target {{width:101px}}}}");
        Layout(root); Assert.Equal(101, child.Width);
        container.SetIsMouseOver(true);
        root.UpdateLayout(); Assert.Equal(11, child.Width);
        container.SetIsMouseOver(false);
        root.UpdateLayout(); Assert.Equal(101, child.Width);
    }

    [Theory]
    [InlineData("style(font-weight:normal)", true)]
    [InlineData("style(font-weight:400)", true)]
    [InlineData("style(font-weight:initial)", true)]
    [InlineData("style(font-weight:unset)", true)]
    [InlineData("style(font-weight)", false)]
    [InlineData("style(font-weight:bold)", false)]
    public void OrdinaryFontWeightStyleQueries_CompareInitialValue(string condition, bool matches)
    {
        var (_, _, child) = Tree(condition, "normal");
        Assert.Equal(matches ? 101 : 11, child.Width);
    }

    [Theory]
    [InlineData("bold", "700", true)]
    [InlineData("700", "bold", true)]
    [InlineData("500", "500", true)]
    [InlineData("500", "400", false)]
    [InlineData("bold", "initial", false)]
    [InlineData("bold", "revert", false)]
    [InlineData("bold", "1001", false)]
    public void OrdinaryFontWeightStyleQueries_CompareResolvedWeights(
        string authored, string queried, bool matches)
    {
        var (root, container, child) = Tree($"style(font-weight:{queried})", "normal");
        Css.SetStyle(container, "font-weight:" + authored);
        Layout(root); Assert.Equal(matches ? 101 : 11, child.Width);
    }

    [Fact]
    public void OrdinaryFontWeightStyleQueries_ResolveRelativeWeightAgainstParent()
    {
        var (root, container, child) = Tree("style(font-weight:bolder) and style(font-weight:700)", "normal");
        Css.SetStyle(root, "font-weight:400");
        Css.SetStyle(container, "font-weight:bolder");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(root, "font-weight:200");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Fact]
    public void OrdinaryFontWeightStyleQueries_ExpectedRelativeWeightTracksNativeParent()
    {
        var (root, container, child) = Tree("style(font-weight:bolder)", "normal");
        Css.SetStyle(root, "font-weight:400");
        Css.SetStyle(container, "font-weight:700");
        Layout(root); Assert.Equal(101, child.Width);
        root.SetValue(TextElement.FontWeightProperty, FontWeight.FromOpenTypeWeight(200));
        root.UpdateLayout(); Assert.Equal(11, child.Width);
        root.ClearValue(TextElement.FontWeightProperty);
        root.UpdateLayout(); Assert.Equal(101, child.Width);
    }

    [Fact]
    public void OrdinaryFontWeightStyleQueries_ResolveLighterAgainstParent()
    {
        var (root, container, child) = Tree("style(font-weight:lighter) and style(font-weight:400)", "normal");
        Css.SetStyle(root, "font-weight:700");
        Css.SetStyle(container, "font-weight:lighter");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(root, "font-weight:400");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Fact]
    public void OrdinaryFontWeightStyleQueries_TrackImplicitAndExplicitInheritance()
    {
        var (root, container, child) = Tree("style(font-weight:700) and style(font-weight:inherit)", "normal");
        Css.SetStyle(root, "font-weight:700");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(container, "font-weight:inherit");
        root.UpdateLayout(); Assert.Equal(101, child.Width);
        Css.SetStyle(root, "font-weight:400");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Fact]
    public void OrdinaryFontWeightStyleQueries_NativeWeightOverridesCss()
    {
        var (root, container, child) = Tree("style(font-weight:700)", "normal");
        Css.SetStyle(container, "font-weight:bold");
        Layout(root); Assert.Equal(101, child.Width);
        container.SetValue(TextElement.FontWeightProperty, FontWeights.Normal);
        root.UpdateLayout(); Assert.Equal(11, child.Width);
        container.ClearValue(TextElement.FontWeightProperty);
        root.UpdateLayout(); Assert.Equal(101, child.Width);
    }

    [Theory]
    [InlineData("style(font-size:medium)", true)]
    [InlineData("style(font-size:14px)", true)]
    [InlineData("style(font-size:initial)", true)]
    [InlineData("style(font-size:unset)", true)]
    [InlineData("style(font-size)", false)]
    [InlineData("style(font-size:large)", false)]
    public void OrdinaryFontSizeStyleQueries_CompareInitialValue(string condition, bool matches)
    {
        var (_, _, child) = Tree(condition, "normal");
        Assert.Equal(matches ? 101 : 11, child.Width);
    }

    [Theory]
    [InlineData("15.4px", "1.1em", true)]
    [InlineData("1.1em", "15.4px", true)]
    [InlineData("16.8px", "large", true)]
    [InlineData("20px", "15pt", true)]
    [InlineData("20px", "calc(1em + 6px)", true)]
    [InlineData("20px", "21px", false)]
    [InlineData("20px", "initial", false)]
    [InlineData("20px", "revert", false)]
    [InlineData("20px", "0px", false)]
    [InlineData("xxx-large", "42px", true)]
    [InlineData("42px", "xxx-large", true)]
    [InlineData("xx-large", "xxx-large", false)]
    [InlineData("0px", "0", true)]
    [InlineData("0%", "0px", true)]
    public void OrdinaryFontSizeStyleQueries_CompareComputedPixels(
        string authored, string queried, bool matches)
    {
        var (root, container, child) = Tree($"style(font-size:{queried})", "normal");
        Css.SetStyle(container, "font-size:" + authored);
        Layout(root); Assert.Equal(matches ? 101 : 11, child.Width);
    }

    [Fact]
    public void OrdinaryFontSizeStyleQueries_ResolvePercentAndLargerAgainstParent()
    {
        var (root, container, child) = Tree("style(font-size:150%) and style(font-size:30px)", "normal");
        Css.SetStyle(root, "font-size:20px");
        Css.SetStyle(container, "font-size:150%");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(container, "font-size:larger");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Fact]
    public void OrdinaryFontSizeStyleQueries_ResolveSmallerAgainstParent()
    {
        var (root, container, child) = Tree("style(font-size:smaller) and style(font-size:20px)", "normal");
        Css.SetStyle(root, "font-size:24px");
        Css.SetStyle(container, "font-size:smaller");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(root, "font-size:20px");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Fact]
    public void OrdinaryFontSizeStyleQueries_RootRemUsesComputedRootSize()
    {
        var (root, container, child) = Tree("style(font-size:2rem) and style(font-size:40px)", "normal");
        Css.SetStyle(root, "font-size:20px");
        Css.SetStyle(container, "font-size:2rem");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(root, "font-size:18px");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Fact]
    public void OrdinaryFontSizeStyleQueries_InheritAndTrackNativeParentSize()
    {
        var (root, container, child) = Tree("style(font-size:inherit) and style(font-size:20px)", "normal");
        Css.SetStyle(root, "font-size:20px");
        Css.SetStyle(container, "font-size:inherit");
        Layout(root); Assert.Equal(101, child.Width);
        root.SetValue(TextElement.FontSizeProperty, 18.0);
        root.UpdateLayout(); Assert.Equal(11, child.Width);
        root.ClearValue(TextElement.FontSizeProperty);
        root.UpdateLayout(); Assert.Equal(101, child.Width);
    }

    [Fact]
    public void OrdinaryFontSizeStyleQueries_NativeSizeOverridesCss()
    {
        var (root, container, child) = Tree("style(font-size:20px)", "normal");
        Css.SetStyle(container, "font-size:20px");
        Layout(root); Assert.Equal(101, child.Width);
        container.SetValue(TextElement.FontSizeProperty, 18.0);
        root.UpdateLayout(); Assert.Equal(11, child.Width);
        container.ClearValue(TextElement.FontSizeProperty);
        root.UpdateLayout(); Assert.Equal(101, child.Width);
    }

    [Fact]
    public void OrdinaryFontSizeStyleQueries_NativeZeroSizeOverridesCss()
    {
        var (root, container, child) = Tree("style(font-size:0px)", "normal");
        Css.SetStyle(container, "font-size:20px");
        Layout(root); Assert.Equal(11, child.Width);
        container.SetValue(TextElement.FontSizeProperty, 0.0);
        root.UpdateLayout(); Assert.Equal(101, child.Width);
        container.ClearValue(TextElement.FontSizeProperty);
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Theory]
    [InlineData("style(font-style:normal)", true)]
    [InlineData("style(font-style:initial)", true)]
    [InlineData("style(font-style:unset)", true)]
    [InlineData("style(font-style)", false)]
    [InlineData("style(font-style:italic)", false)]
    public void OrdinaryFontStyleStyleQueries_CompareInitialValue(string condition, bool matches)
    {
        var (_, _, child) = Tree(condition, "normal");
        Assert.Equal(matches ? 101 : 11, child.Width);
    }

    [Theory]
    [InlineData("italic", "italic", true)]
    [InlineData("italic", "oblique", false)]
    [InlineData("oblique", "oblique", true)]
    [InlineData("oblique", "oblique 14deg", false)]
    [InlineData("oblique 14deg", "oblique", false)]
    [InlineData("oblique 14deg", "oblique 14deg", true)]
    [InlineData("oblique 45deg", "oblique 0.125turn", true)]
    [InlineData("oblique 45deg", "oblique 50grad", true)]
    [InlineData("oblique -30deg", "oblique -0.08333333333333333turn", true)]
    [InlineData("oblique 45deg", "oblique 30deg", false)]
    [InlineData("oblique 45deg", "oblique 91deg", false)]
    [InlineData("oblique 45deg", "normal 45deg", false)]
    [InlineData("oblique 45deg", "revert", false)]
    [InlineData("oblique calc(100deg)", "oblique 90deg", true)]
    public void OrdinaryFontStyleStyleQueries_CompareComputedAngles(
        string authored, string queried, bool matches)
    {
        var (root, container, child) = Tree($"style(font-style:{queried})", "normal");
        Css.SetStyle(container, "font-style:" + authored);
        Layout(root); Assert.Equal(matches ? 101 : 11, child.Width);
    }

    [Fact]
    public void OrdinaryFontStyleStyleQueries_RejectInvalidAnglesAndShorthandWidths()
    {
        Assert.True(Css.Supports("font-style", "oblique 90deg"));
        Assert.True(Css.Supports("font-style", "oblique -90deg"));
        Assert.False(Css.Supports("font-style", "oblique 91deg"));
        Assert.False(Css.Supports("font-style", "oblique 0"));
        Assert.False(Css.Supports("font-style", "italic 10deg"));
        Assert.False(Css.Supports("font", "82% 16px Arial"));
        Assert.True(Css.Supports("font", "82% Arial"));
        Assert.True(Css.Supports("font", "condensed 16px Arial"));
    }

    [Fact]
    public void OrdinaryFontStyleStyleQueries_InheritExplicitAngleFromParent()
    {
        var (root, container, child) = Tree("style(font-style:inherit) and style(font-style:oblique 25deg)", "normal");
        Css.SetStyle(root, "font-style:oblique 25deg");
        Css.SetStyle(container, "font-style:inherit");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(root, "font-style:oblique 20deg");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Fact]
    public void OrdinaryFontStyleStyleQueries_ImplicitlyInheritExplicitAngle()
    {
        var (root, _, child) = Tree("style(font-style:oblique 25deg)", "normal");
        Css.SetStyle(root, "font-style:oblique 25deg");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(root, "font-style:oblique 20deg");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Fact]
    public void OrdinaryFontStyleStyleQueries_NativeSourceOverridesSameObliqueClass()
    {
        var (root, container, child) = Tree("style(font-style:oblique 25deg)", "normal");
        Css.SetStyle(container, "font-style:oblique 25deg");
        Layout(root); Assert.Equal(101, child.Width);
        Assert.Equal(FontStyles.Oblique, container.GetValue(TextElement.FontStyleProperty));
        container.SetValue(TextElement.FontStyleProperty, FontStyles.Oblique);
        root.UpdateLayout(); Assert.Equal(11, child.Width);
        container.ClearValue(TextElement.FontStyleProperty);
        root.UpdateLayout(); Assert.Equal(101, child.Width);
    }

    [Fact]
    public void OrdinaryFontStyleStyleQueries_NativeParentOverridesExplicitAngle()
    {
        var (root, container, child) = Tree("style(font-style:oblique 25deg)", "normal");
        Css.SetStyle(root, "font-style:oblique 25deg");
        Css.SetStyle(container, "font-style:inherit");
        Layout(root); Assert.Equal(101, child.Width);
        root.SetValue(TextElement.FontStyleProperty, FontStyles.Oblique);
        root.UpdateLayout(); Assert.Equal(11, child.Width);
        root.ClearValue(TextElement.FontStyleProperty);
        root.UpdateLayout(); Assert.Equal(101, child.Width);
    }

    [Fact]
    public void OrdinaryFontStyleStyleQueries_ShorthandPreservesAndResetsAngle()
    {
        var (root, container, child) = Tree("style(font-style:oblique 25deg)", "normal");
        Css.SetStyle(container, "font:oblique 25deg bold 16px Arial");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(container, "font:oblique 16px Arial");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
        Css.SetStyle(container, "font:oblique 25deg 16px Arial");
        root.UpdateLayout(); Assert.Equal(101, child.Width);
        Css.SetStyle(container, "font:16px Arial");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Fact]
    public void OrdinaryFontStyleStyleQueries_FontCssWideKeywordsIncludeAngle()
    {
        var (root, container, child) = Tree("style(font-style:normal)", "normal");
        Css.SetStyle(root, "font-style:oblique 25deg");
        Css.SetStyle(container, "font:initial");
        Layout(root); Assert.Equal(101, child.Width);

        var (inheritedRoot, inheritedContainer, inheritedChild) = Tree("style(font-style:oblique 25deg)", "normal");
        Css.SetStyle(inheritedRoot, "font-style:oblique 25deg");
        Css.SetStyle(inheritedContainer, "font:inherit");
        Layout(inheritedRoot); Assert.Equal(101, inheritedChild.Width);
    }

    [Fact]
    public void OrdinaryFontStyleStyleQueries_StateRuleChangesExplicitAngle()
    {
        var (root, container, child) = Tree("style(font-style:oblique 25deg)", "normal");
        Css.SetClass(container, "query-box");
        Css.SetStyleSheet(root,
            ".query-box {font-style:oblique 25deg} .query-box:hover {font-style:oblique 20deg} " +
            ".target {width:11px} @container style(font-style:oblique 25deg) {.target {width:101px}}");
        Layout(root); Assert.Equal(101, child.Width);
        container.SetIsMouseOver(true);
        root.UpdateLayout(); Assert.Equal(11, child.Width);
        container.SetIsMouseOver(false);
        root.UpdateLayout(); Assert.Equal(101, child.Width);
    }

    [Theory]
    [InlineData("style(font-family:sans-serif)", true)]
    [InlineData("style(font-family:initial)", true)]
    [InlineData("style(font-family:unset)", true)]
    [InlineData("style(font-family)", false)]
    [InlineData("style(font-family:Segoe UI)", false)]
    public void OrdinaryFontFamilyStyleQueries_CompareInitialGeneric(string condition, bool matches)
    {
        var (_, _, child) = Tree(condition, "normal");
        Assert.Equal(matches ? 101 : 11, child.Width);
    }

    [Theory]
    [InlineData("Arial, serif", "Arial, serif", true)]
    [InlineData("Arial, serif", "Arial, Times New Roman", false)]
    [InlineData("Arial, serif", "serif, Arial", false)]
    [InlineData("monospace", "Consolas", false)]
    [InlineData("New Century Schoolbook", "'New Century Schoolbook'", true)]
    [InlineData("New\\20 Century", "'New Century'", true)]
    [InlineData("SERIF", "serif", true)]
    [InlineData("'Arial'", "Arial", true)]
    [InlineData("'sans-serif', sans-serif", "'sans-serif', sans-serif", true)]
    [InlineData("'sans-serif', sans-serif", "sans-serif, sans-serif", false)]
    [InlineData("'ACME, Inc.', serif", "'ACME, Inc.', serif", true)]
    [InlineData("generic(fangsong), serif", "generic(fangsong), serif", true)]
    public void OrdinaryFontFamilyStyleQueries_CompareOrderedComputedList(
        string authored, string queried, bool matches)
    {
        var (root, container, child) = Tree($"style(font-family:{queried})", "normal");
        Css.SetStyle(container, "font-family:" + authored);
        Layout(root); Assert.Equal(matches ? 101 : 11, child.Width);
    }

    [Fact]
    public void OrdinaryFontFamilyStyleQueries_ValidateFamilySyntax()
    {
        Assert.True(Css.Supports("font-family", "'sans-serif', sans-serif"));
        Assert.True(Css.Supports("font-family", "'Hawaii 5-0', serif"));
        Assert.False(Css.Supports("font-family", "Ahem!"));
        Assert.False(Css.Supports("font-family", "'Lucida' Grande"));
        Assert.False(Css.Supports("font-family", "Red/Black"));
        Assert.False(Css.Supports("font-family", "Hawaii 5-0"));
        Assert.False(Css.Supports("font-family", "Arial,,serif"));
        Assert.False(Css.Supports("font-family", "Arial,"));
        Assert.False(Css.Supports("font-family", "Arial, inherit"));
        Assert.False(Css.Supports("font-family", "generic(unknown)"));
    }

    [Fact]
    public void OrdinaryFontFamilyStyleQueries_InheritComputedList()
    {
        var (root, container, child) = Tree(
            "style(font-family:inherit) and style(font-family:Arial, serif)", "normal");
        Css.SetStyle(root, "font-family:Arial, serif");
        Css.SetStyle(container, "font-family:inherit");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(root, "font-family:Arial, sans-serif");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Fact]
    public void OrdinaryFontFamilyStyleQueries_ImplicitlyInheritComputedList()
    {
        var (root, _, child) = Tree("style(font-family:monospace)", "normal");
        Css.SetStyle(root, "font-family:monospace");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(root, "font-family:serif");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Fact]
    public void OrdinaryFontFamilyStyleQueries_NativeSourceOverridesEquivalentRenderingFamily()
    {
        var (root, container, child) = Tree("style(font-family:sans-serif)", "normal");
        Css.SetStyle(container, "font-family:sans-serif");
        Layout(root); Assert.Equal(101, child.Width);
        Assert.Equal("Segoe UI", Assert.IsType<FontFamily>(container.GetValue(TextElement.FontFamilyProperty)).Source);
        container.SetValue(TextElement.FontFamilyProperty, new FontFamily("Segoe UI"));
        root.UpdateLayout(); Assert.Equal(11, child.Width);
        container.ClearValue(TextElement.FontFamilyProperty);
        root.UpdateLayout(); Assert.Equal(101, child.Width);
    }

    [Fact]
    public void OrdinaryFontFamilyStyleQueries_NativeParentOverridesCssGeneric()
    {
        var (root, container, child) = Tree("style(font-family:sans-serif)", "normal");
        Css.SetStyle(root, "font-family:sans-serif");
        Css.SetStyle(container, "font-family:inherit");
        Layout(root); Assert.Equal(101, child.Width);
        root.SetValue(TextElement.FontFamilyProperty, new FontFamily("Segoe UI"));
        root.UpdateLayout(); Assert.Equal(11, child.Width);
        root.ClearValue(TextElement.FontFamilyProperty);
        root.UpdateLayout(); Assert.Equal(101, child.Width);
    }

    [Fact]
    public void OrdinaryFontFamilyStyleQueries_NativeQuotedCommaStaysInOneFamily()
    {
        var (root, container, child) = Tree("style(font-family:'ACME, Inc.', Arial)", "normal");
        container.SetValue(TextElement.FontFamilyProperty, new FontFamily("\"ACME, Inc.\", Arial"));
        Layout(root); Assert.Equal(101, child.Width);
        container.SetValue(TextElement.FontFamilyProperty, new FontFamily("ACME, Inc., Arial"));
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Fact]
    public void OrdinaryFontFamilyStyleQueries_ShorthandRetainsAndResetsList()
    {
        var (root, container, child) = Tree("style(font-family:Arial, serif)", "normal");
        Css.SetStyle(container, "font:italic 16px Arial, serif");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(container, "font:16px Arial");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
        Css.SetStyle(container, "font:italic 16px Arial, serif");
        root.UpdateLayout(); Assert.Equal(101, child.Width);
    }

    [Fact]
    public void OrdinaryFontFamilyStyleQueries_FontCssWideKeywordsRetainList()
    {
        var (root, container, child) = Tree("style(font-family:sans-serif)", "normal");
        Css.SetStyle(root, "font-family:monospace");
        Css.SetStyle(container, "font:initial");
        Layout(root); Assert.Equal(101, child.Width);

        var (inheritedRoot, inheritedContainer, inheritedChild) = Tree("style(font-family:monospace)", "normal");
        Css.SetStyle(inheritedRoot, "font-family:monospace");
        Css.SetStyle(inheritedContainer, "font:inherit");
        Layout(inheritedRoot); Assert.Equal(101, inheritedChild.Width);
    }

    [Fact]
    public void OrdinaryFontFamilyStyleQueries_StateRuleChangesGenericToNamedWithSameNativeFont()
    {
        var (root, container, child) = Tree("style(font-family:sans-serif)", "normal");
        Css.SetClass(container, "query-box");
        Css.SetStyleSheet(root,
            ".query-box {font-family:sans-serif} .query-box:hover {font-family:'Segoe UI'} " +
            ".target {width:11px} @container style(font-family:sans-serif) {.target {width:101px}}");
        Layout(root); Assert.Equal(101, child.Width);
        container.SetIsMouseOver(true);
        root.UpdateLayout(); Assert.Equal(11, child.Width);
        container.SetIsMouseOver(false);
        root.UpdateLayout(); Assert.Equal(101, child.Width);
    }

    [Theory]
    [InlineData("style(font-stretch:normal)", true)]
    [InlineData("style(font-stretch:initial)", true)]
    [InlineData("style(font-stretch:unset)", true)]
    [InlineData("style(font-stretch)", false)]
    [InlineData("style(font-stretch:condensed)", false)]
    public void OrdinaryFontStretchStyleQueries_CompareInitialValue(string condition, bool matches)
    {
        var (_, _, child) = Tree(condition, "normal");
        Assert.Equal(matches ? 101 : 11, child.Width);
    }

    [Theory]
    [InlineData("ultra-condensed", "ultra-condensed", true)]
    [InlineData("condensed", "condensed", true)]
    [InlineData("semi-expanded", "semi-expanded", true)]
    [InlineData("ultra-expanded", "ultra-expanded", true)]
    [InlineData("condensed", "expanded", false)]
    [InlineData("condensed", "normal", false)]
    [InlineData("condensed", "revert", false)]
    [InlineData("condensed", "75%", true)]
    [InlineData("75%", "condensed", true)]
    [InlineData("125%", "expanded", true)]
    [InlineData("calc(50% + 25%)", "condensed", true)]
    [InlineData("82%", "82%", true)]
    [InlineData("82%", "semi-condensed", false)]
    [InlineData("0%", "0%", true)]
    [InlineData("-1%", "normal", true)]
    public void OrdinaryFontStretchStyleQueries_CompareKeywords(
        string authored, string queried, bool matches)
    {
        var (root, container, child) = Tree($"style(font-stretch:{queried})", "normal");
        Css.SetStyle(container, "font-stretch:" + authored);
        Layout(root); Assert.Equal(matches ? 101 : 11, child.Width);
    }

    [Fact]
    public void OrdinaryFontStretchStyleQueries_TrackInheritedParentStretch()
    {
        var (root, container, child) = Tree("style(font-stretch:inherit) and style(font-stretch:condensed)", "normal");
        Css.SetStyle(root, "font-stretch:condensed");
        Css.SetStyle(container, "font-stretch:inherit");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(root, "font-stretch:expanded");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Fact]
    public void OrdinaryFontStretchStyleQueries_NativeStretchOverridesCss()
    {
        var (root, container, child) = Tree("style(font-stretch:condensed)", "normal");
        Css.SetStyle(container, "font-stretch:condensed");
        Layout(root); Assert.Equal(101, child.Width);
        container.SetValue(TextElement.FontStretchProperty, FontStretches.Expanded);
        root.UpdateLayout(); Assert.Equal(11, child.Width);
        container.ClearValue(TextElement.FontStretchProperty);
        root.UpdateLayout(); Assert.Equal(101, child.Width);
    }

    [Fact]
    public void OrdinaryFontStretchStyleQueries_PreserveContinuousPercentageThroughInheritance()
    {
        var (root, container, child) = Tree("style(font-stretch:82%)", "normal");
        Css.SetStyle(root, "font-stretch:82%");
        Css.SetStyle(container, "font-stretch:inherit");
        Layout(root); Assert.Equal(101, child.Width);
        Assert.Equal(FontStretches.SemiCondensed,
            container.GetValue(TextElement.FontStretchProperty));
        Css.SetStyle(root, "font-stretch:90%");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Fact]
    public void OrdinaryFontStretchStyleQueries_ImplicitlyInheritContinuousPercentage()
    {
        var (root, _, child) = Tree("style(font-stretch:82%)", "normal");
        Css.SetStyle(root, "font-stretch:82%");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(root, "font-stretch:90%");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Fact]
    public void OrdinaryFontStretchStyleQueries_NativeParentOverridesContinuousPercentage()
    {
        var (root, container, child) = Tree("style(font-stretch:82%)", "normal");
        Css.SetStyle(root, "font-stretch:82%");
        Css.SetStyle(container, "font-stretch:inherit");
        Layout(root); Assert.Equal(101, child.Width);
        root.SetValue(TextElement.FontStretchProperty, FontStretches.Expanded);
        root.UpdateLayout(); Assert.Equal(11, child.Width);
        root.ClearValue(TextElement.FontStretchProperty);
        root.UpdateLayout(); Assert.Equal(101, child.Width);
    }

    [Fact]
    public void OrdinaryFontStretchStyleQueries_NativeParentWithSameWidthClassOverridesPercentage()
    {
        var (root, container, child) = Tree("style(font-stretch:82%)", "normal");
        Css.SetStyle(root, "font-stretch:82%");
        Css.SetStyle(container, "font-stretch:inherit");
        Layout(root); Assert.Equal(101, child.Width);
        root.SetValue(TextElement.FontStretchProperty, FontStretches.SemiCondensed);
        root.UpdateLayout(); Assert.Equal(11, child.Width);
        root.ClearValue(TextElement.FontStretchProperty);
        root.UpdateLayout(); Assert.Equal(101, child.Width);
    }

    [Fact]
    public void OrdinaryFontStretchStyleQueries_NativeLocalOverridesContinuousPercentage()
    {
        var (root, container, child) = Tree("style(font-stretch:82%)", "normal");
        Css.SetStyle(container, "font-stretch:82%");
        Layout(root); Assert.Equal(101, child.Width);
        container.SetValue(TextElement.FontStretchProperty, FontStretches.SemiCondensed);
        root.UpdateLayout(); Assert.Equal(11, child.Width);
        container.ClearValue(TextElement.FontStretchProperty);
        root.UpdateLayout(); Assert.Equal(101, child.Width);
    }

    [Fact]
    public void OrdinaryFontStretchStyleQueries_FontWidthAliasSharesTheCascade()
    {
        Assert.True(Css.Supports("font-width", "75%"));
        var (root, container, child) = Tree("style(font-width:75%)", "normal");
        Css.SetStyle(container, "font-stretch:expanded;font-width:75%");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(container, "font-width:75%;font-stretch:expanded");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Fact]
    public void OrdinaryFontStretchStyleQueries_ShorthandResetsComputedWidth()
    {
        var (root, container, child) = Tree("style(font-stretch:75%)", "normal");
        Css.SetStyle(container, "font:condensed 16px Arial");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(container, "font:16px Arial");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Fact]
    public void OrdinaryFontStretchStyleQueries_ShorthandPercentageCanStillBeFontSize()
    {
        var (root, container, child) = Tree(
            "style(font-size:11.48px) and style(font-stretch:normal)", "normal");
        Css.SetStyle(container, "font:82% Arial");
        Layout(root); Assert.Equal(101, child.Width);
    }

    [Fact]
    public void OrdinaryFontStretchStyleQueries_FontCssWideKeywordsIncludeWidth()
    {
        var (root, container, child) = Tree("style(font-stretch:normal)", "normal");
        Css.SetStyle(root, "font-stretch:82%");
        Css.SetStyle(container, "font:initial");
        Layout(root); Assert.Equal(101, child.Width);

        var (inheritedRoot, inheritedContainer, inheritedChild) = Tree("style(font-stretch:82%)", "normal");
        Css.SetStyle(inheritedRoot, "font-stretch:82%");
        Css.SetStyle(inheritedContainer, "font:inherit");
        Layout(inheritedRoot); Assert.Equal(101, inheritedChild.Width);
    }

    [Fact]
    public void OrdinaryFontStretchStyleQueries_StateRuleReplacesContinuousWidth()
    {
        var (root, container, child) = Tree("style(font-stretch:82%)", "normal");
        Css.SetClass(container, "query-box");
        Css.SetStyleSheet(root,
            ".query-box {font-stretch:82%} .query-box:hover {font-stretch:expanded} " +
            ".target {width:11px} @container style(font-stretch:82%) {.target {width:101px}}");
        Layout(root); Assert.Equal(101, child.Width);
        container.SetIsMouseOver(true);
        root.UpdateLayout(); Assert.Equal(11, child.Width);
        container.SetIsMouseOver(false);
        root.UpdateLayout(); Assert.Equal(101, child.Width);
    }

    [Theory]
    [InlineData("overflow-x")]
    [InlineData("overflow-y")]
    [InlineData("overflow-inline")]
    [InlineData("overflow-block")]
    [InlineData("overflow")]
    public void OrdinaryOverflowStyleQueries_InitialAndBooleanValues(string property)
    {
        var (_, _, initialChild) = Tree($"style({property}:visible)", "normal");
        Assert.Equal(101, initialChild.Width);
        var (root, container, child) = Tree($"style({property})", "normal");
        Assert.Equal(11, child.Width);
        Css.SetStyle(container, property + ":hidden");
        root.UpdateLayout(); Assert.Equal(101, child.Width);
    }

    [Theory]
    [InlineData("overflow-x:hidden", "overflow-x:hidden", true)]
    [InlineData("overflow-y:scroll", "overflow-y:scroll", true)]
    [InlineData("overflow-inline:clip", "overflow-x:clip", true)]
    [InlineData("overflow-block:auto", "overflow-y:auto", true)]
    [InlineData("overflow-x:overlay", "overflow-inline:auto", true)]
    [InlineData("overflow-x:hidden", "overflow-inline:scroll", false)]
    [InlineData("overflow-x:visible;overflow-y:hidden", "overflow-x:auto", true)]
    [InlineData("overflow-x:visible;overflow-y:hidden", "overflow-x:visible", true)]
    [InlineData("overflow-x:visible;overflow-y:hidden", "overflow-inline:initial", true)]
    [InlineData("overflow-x:visible;overflow-y:hidden", "overflow-x:unset", true)]
    [InlineData("overflow-x:clip;overflow-y:scroll", "overflow-x:hidden", true)]
    [InlineData("overflow-x:clip;overflow-y:scroll", "overflow-inline:clip", true)]
    [InlineData("overflow-x:clip;overflow-y:scroll", "overflow-x:visible", false)]
    [InlineData("overflow-x:hidden", "overflow-x:revert", false)]
    [InlineData("overflow-x:hidden", "overflow-x:visible hidden", false)]
    public void OrdinaryOverflowStyleQueries_CompareComputedAxes(
        string authored, string queried, bool matches)
    {
        var (root, container, child) = Tree($"style({queried})", "normal");
        Css.SetStyle(container, authored);
        Layout(root); Assert.Equal(matches ? 101 : 11, child.Width);
    }

    [Theory]
    [InlineData("overflow:clip", "overflow:clip", true)]
    [InlineData("overflow:clip visible", "overflow:clip", false)]
    [InlineData("overflow:visible hidden", "overflow:auto hidden", true)]
    [InlineData("overflow:visible hidden", "overflow:visible hidden", true)]
    [InlineData("overflow:visible hidden", "overflow:clip hidden", false)]
    [InlineData("overflow:clip scroll", "overflow:hidden scroll", true)]
    [InlineData("overflow:overlay", "overflow:auto", true)]
    [InlineData("overflow:clip scroll", "overflow:clip", false)]
    [InlineData("overflow:hidden", "overflow:revert-layer", false)]
    [InlineData("overflow:hidden", "overflow:hidden scroll visible", false)]
    public void OrdinaryOverflowStyleQueries_CompareShorthandAxes(
        string authored, string queried, bool matches)
    {
        var (root, container, child) = Tree($"style({queried})", "normal");
        Css.SetStyle(container, authored);
        Layout(root); Assert.Equal(matches ? 101 : 11, child.Width);
    }

    [Fact]
    public void OrdinaryOverflowStyleQueries_CompareInheritanceAndCssWideValues()
    {
        var (root, container, child) = Tree(
            "style(overflow-x:inherit) and style(overflow:inherit)", "normal");
        Css.SetStyle(root, "overflow:hidden");
        Css.SetStyle(container, "overflow:hidden");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(root, "overflow:scroll");
        root.UpdateLayout(); Assert.Equal(11, child.Width);

        Css.SetStyleSheet(root,
            ".target {width:11px} @container style(overflow:initial) {.target {width:101px}}");
        Css.SetStyle(container, "overflow:unset");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(container, "overflow:hidden");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Fact]
    public void OrdinaryOverflowStyleQueries_LogicalAxesAndNativeClipping()
    {
        var (root, container, child) = Tree(
            "style(overflow-x:hidden) and style(overflow-inline:hidden)", "normal");
        Css.SetStyle(container, "direction:rtl; overflow-inline:hidden");
        Layout(root); Assert.Equal(101, child.Width);
        container.ClipToBounds = false;
        root.UpdateLayout(); Assert.Equal(101, child.Width);
        Css.SetStyle(container, "direction:rtl; overflow-inline:clip");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Fact]
    public void OrdinaryOverflowStyleQueries_RefreshAfterContainerStateRuleChanges()
    {
        var (root, container, child) = Tree("style(overflow-x:hidden)", "normal");
        Css.SetClass(container, "query-box");
        Css.SetStyleSheet(root,
            ".query-box {overflow-x:clip} .query-box:hover {overflow-x:hidden} " +
            ".target {width:11px} @container style(overflow-x:hidden) {.target {width:101px}}");
        Layout(root); Assert.Equal(11, child.Width);
        container.SetIsMouseOver(true);
        root.UpdateLayout(); Assert.Equal(101, child.Width);
        container.SetIsMouseOver(false);
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Fact]
    public void OrdinarySizeStyleQueries_RefreshWhenNativeWidthChanges()
    {
        var (root, container, child) = Tree("style(width:400px)", "normal");
        Layout(root); Assert.Equal(101, child.Width);
        container.Width = 300;
        root.UpdateLayout(); Assert.Equal(11, child.Width);
        container.Width = 400;
        root.UpdateLayout(); Assert.Equal(101, child.Width);
    }

    [Fact]
    public void OrdinarySizeStyleQueries_RefreshWhenCssPercentageChanges()
    {
        var (root, container, child) = Tree("style(width:50%)", "normal");
        container.ClearValue(FrameworkElement.WidthProperty);
        Css.SetStyle(container, "width:50%");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(container, "width:40%");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Fact]
    public void OrdinarySizeStyleQueries_NativeWidthOverridesTheCssPercentage()
    {
        var (root, container, child) = Tree("style(width:50%)", "normal");
        container.ClearValue(FrameworkElement.WidthProperty);
        Css.SetStyle(container, "width:50%");
        Layout(root); Assert.Equal(101, child.Width);
        container.Width = 400;
        root.UpdateLayout(); Assert.Equal(11, child.Width);
        container.ClearValue(FrameworkElement.WidthProperty);
        root.UpdateLayout(); Assert.Equal(101, child.Width);
    }

    [Fact]
    public void OrdinarySizeStyleQueries_ComputeExplicitWidthInheritance()
    {
        var (root, container, child) = Tree("style(width:inherit)", "normal");
        root.Width = 320;
        container.ClearValue(FrameworkElement.WidthProperty);
        Css.SetStyle(container, "width:inherit");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(container, "width:300px");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Theory]
    [InlineData("style(5 >= calc(3 + 1))", true)]
    [InlineData("style(0 = 0px)", true)]
    [InlineData("style(0 = 0deg)", false)]
    [InlineData("style(0px = .0000000001px)", false)]
    [InlineData("style(0 < 3px)", true)]
    [InlineData("style(1em > 1px)", true)]
    [InlineData("style(3turn > 3deg)", true)]
    [InlineData("style(3% >= 3%)", true)]
    [InlineData("style(3s > 3ms)", true)]
    [InlineData("style(3dppx > 96dpi)", true)]
    [InlineData("style(3kHz > 100Hz)", true)]
    [InlineData("style(--length <= 1em)", true)]
    [InlineData("style(--length > calc(3px + 3px))", true)]
    [InlineData("style(--number <= var(--number))", true)]
    [InlineData("style(calc(var(--number) + 1) >= 4)", true)]
    [InlineData("style(var(--missing, 20px) > 10px)", true)]
    [InlineData("style(2 < --number <= 5)", true)]
    [InlineData("style(10px <= --length < 11px)", true)]
    [InlineData("style(--number >= 3 > 1)", true)]
    [InlineData("style(3px > 3)", false)]
    [InlineData("style(3em > 3deg)", false)]
    [InlineData("style(1px >= 1%)", false)]
    [InlineData("style(--length > 3)", false)]
    [InlineData("style(--missing > 1px)", false)]
    [InlineData("style(--number + 1 >= 3)", false)]
    [InlineData("style(2px < --length > 1px)", false)]
    [InlineData("not style(3px > 3deg)", true)]
    public void StyleRanges_CompareComputedNumericTypes(string condition, bool expected)
    {
        var (root, container, child) = Tree(condition, "normal");
        Css.SetStyle(container, "--length:10px; --number:3");
        Layout(root); Assert.Equal(expected ? 101 : 11, child.Width);
    }

    [Fact]
    public void StyleRanges_RefreshAfterCustomPropertiesAndContainerFontChange()
    {
        var (root, container, child) = Tree("style(10px < --limit <= 2em)", "normal");
        Css.SetStyle(container, "--limit:15px; font-size:10px");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(container, "--limit:25px; font-size:10px");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
        Css.SetStyle(container, "--limit:25px; font-size:20px");
        root.UpdateLayout(); Assert.Equal(101, child.Width);
    }

    [Fact]
    public void StyleRanges_ResolveEscapedCustomPropertyNames()
    {
        var (root, container, child) = Tree("style(--n\\3a x > 7px)", "normal");
        Css.SetStyle(container, "--n\\3a x:8px");
        Layout(root); Assert.Equal(101, child.Width);
    }

    [Fact]
    public void StyleRanges_UseRegisteredInitialAndComputedValues()
    {
        var (root, container, child) = Tree("style(--limit = 96px)", "normal");
        Css.SetStyleSheet(root, "@property --limit { syntax: '<length>'; inherits: false; initial-value: 1in } " +
            ".target { width:11px } @container style(--limit = 96px) { .target { width:101px } }");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(container, "--limit:2in");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Theory]
    [InlineData("style(1px < attr(gap type(<length>)) < 10px)", "5px", true)]
    [InlineData("style(1px < attr(gap type(<length>)) < 10px)", "invalid", false)]
    [InlineData("style(1px < attr(gap type(<length>), 6px) < 10px)", "invalid", true)]
    [InlineData("style(attr(gap type(<length>), var(--missing)) > 3px)", "5px", true)]
    [InlineData("style(attr(missing type(<length>), var(--missing)) > 3px)", "5px", false)]
    [InlineData("style(1px < attr(gap) < 10px)", "5px", false)]
    [InlineData("style(1px < attr(missing, 6px) < 10px)", "5px", true)]
    [InlineData("style(attr(class, 5px) = 5px)", "5px", true)]
    [InlineData("style(1px < attr(missing, '6px') < 10px)", "5px", false)]
    [InlineData("style(calc(attr(gap type(<length>)) + 1px) = 6px)", "5px", true)]
    [InlineData("style(attr(gap px) = 5px)", "5", true)]
    [InlineData("style(attr(gap number) = 5)", "5", true)]
    [InlineData("style(attr(gap number) = 5)", "calc(5)", false)]
    [InlineData("style(attr(gap %) = 5%)", "5", true)]
    [InlineData("style(attr(gap type(<angle>)) > 180deg)", "1turn", true)]
    [InlineData("style(attr(gap type(<frequency>)) > 1000Hz)", "2kHz", true)]
    public void StyleRanges_UseTypedAttributesAndFallbacks(string condition, string attribute, bool expected)
    {
        var (root, container, child) = Tree(condition, "normal");
        Css.SetAttribute(container, "gap", attribute);
        Layout(root); Assert.Equal(expected ? 101 : 11, child.Width);
    }

    [Fact]
    public void StyleRanges_AttributeChangesRefreshSelectedContainer()
    {
        var (root, container, child) = Tree("style(1px < attr(gap type(<length>), var(--fallback)) < 10px)", "normal");
        Css.SetStyle(container, "--fallback:6px");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetAttribute(container, "gap", "12px");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
        Css.SetAttribute(container, "gap", "5px");
        root.UpdateLayout(); Assert.Equal(101, child.Width);
        Css.SetAttribute(container, "gap", null);
        root.UpdateLayout(); Assert.Equal(101, child.Width);
    }

    [Fact]
    public void StyleRanges_SubstituteAttributesInsideUnregisteredCustomProperties()
    {
        var (root, container, child) = Tree("style(--gap = 5px)", "normal");
        Css.SetAttribute(container, "gap", "5px");
        Css.SetStyle(container, "--gap:attr(gap type(<length>), var(--missing))");
        Layout(root); Assert.Equal(101, child.Width);
    }

    [Fact]
    public void StyleRanges_SubstituteTheAttributeNameOnTheSelectedContainer()
    {
        var (root, container, child) = Tree("style(attr(var(--attribute) type(<length>)) = 5px)", "normal");
        Css.SetStyle(container, "--attribute:gap");
        Css.SetAttribute(container, "gap", "5px");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetStyle(container, "--attribute:other");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Fact]
    public void StyleRanges_NativeAttributeChangesRefreshTheQuery()
    {
        var (root, container, child) = Tree("style(attr(Tag type(<length>)) > 4px)", "normal");
        container.Tag = "5px";
        Layout(root); Assert.Equal(101, child.Width);
        container.Tag = "3px";
        root.UpdateLayout(); Assert.Equal(11, child.Width);
    }

    [Fact]
    public void StyleRanges_ReadAttributesFromTheNamedContainer()
    {
        var child = new Border(); var inner = new StackPanel(); var outer = new StackPanel();
        inner.Children.Add(child); outer.Children.Add(inner);
        var root = new Border { Child = outer };
        Css.SetClass(child, "target"); Css.SetStyle(outer, "container-name:outer");
        Css.SetAttribute(outer, "gap", "8px"); Css.SetAttribute(inner, "gap", "2px");
        Css.SetStyleSheet(root, ".target {width:11px} @container outer style(attr(gap type(<length>)) > 5px) { .target {width:101px} }");
        Layout(root); Assert.Equal(101, child.Width);
        Css.SetAttribute(outer, "gap", "3px");
        root.UpdateLayout(); Assert.Equal(11, child.Width);
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

    [Fact]
    public void DocumentNodes_FontStyleSourceChangeRefreshesExplicitAngleQuery()
    {
        var run = new Run("原始文本"); var paragraph = new Paragraph(run);
        Css.SetStyle(paragraph, "font-style:oblique 25deg; container-name:paragraph");
        Css.SetStyleSheet(paragraph,
            "Run {font-size:12px} @container paragraph style(font-style:oblique 25deg) {Run {font-size:24px}}");
        CssEvaluationScheduler.FlushIfPending(paragraph.Dispatcher);
        Assert.Equal(24, run.FontSize);

        paragraph.SetValue(TextElement.FontStyleProperty, FontStyles.Oblique);
        CssEvaluationScheduler.FlushIfPending(paragraph.Dispatcher);
        Assert.Equal(12, run.FontSize);

        paragraph.ClearValue(TextElement.FontStyleProperty);
        CssEvaluationScheduler.FlushIfPending(paragraph.Dispatcher);
        Assert.Equal(24, run.FontSize);
    }

    [Fact]
    public void DocumentNodes_FontFamilySourceChangeRefreshesGenericQuery()
    {
        var run = new Run("原始文本"); var paragraph = new Paragraph(run);
        Css.SetStyle(paragraph, "font-family:sans-serif; container-name:paragraph");
        Css.SetStyleSheet(paragraph,
            "Run {font-size:12px} @container paragraph style(font-family:sans-serif) {Run {font-size:24px}}");
        CssEvaluationScheduler.FlushIfPending(paragraph.Dispatcher);
        Assert.Equal(24, run.FontSize);

        paragraph.SetValue(TextElement.FontFamilyProperty, new FontFamily("Segoe UI"));
        CssEvaluationScheduler.FlushIfPending(paragraph.Dispatcher);
        Assert.Equal(12, run.FontSize);

        paragraph.ClearValue(TextElement.FontFamilyProperty);
        CssEvaluationScheduler.FlushIfPending(paragraph.Dispatcher);
        Assert.Equal(24, run.FontSize);
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

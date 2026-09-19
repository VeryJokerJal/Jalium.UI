using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssRegisteredPropertyTests
{
    static CssRegisteredPropertyTests() => System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(
        typeof(Jalium.UI.Markup.TypeConverterRegistry).Module.ModuleHandle);

    private static string Registration(string syntax = "<length>", string initial = "12px", bool inherits = false, string name = "--value")
        => $"@property {name} {{ syntax:'{syntax}'; inherits:{inherits.ToString().ToLowerInvariant()}; initial-value:{initial}; }}";

    private static void Layout(FrameworkElement root, double width = 400, double height = 300)
    {
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        root.Measure(new(width, height)); root.Arrange(new(0, 0, width, height)); root.UpdateLayout();
        Assert.False(CssEvaluationScheduler.HasPending(root.Dispatcher));
    }

    private static string? Computed(DependencyObject element, string property = "--value")
        => CssNode.Get(element).CssRuntimeState?.CustomProperties?.GetValueOrDefault(property);

    [Theory]
    [InlineData("<length>", "1in", "96px")]
    [InlineData("<length>", "2.54cm", "96px")]
    [InlineData("<length>", "calc(10px * 2)", "20px")]
    [InlineData("<percentage>", "30%", "30%")]
    [InlineData("<length-percentage>", "calc(50% + 10px)", "calc(50% + 10px)")]
    [InlineData("<number>", "calc(3 / 2)", "1.5")]
    [InlineData("<integer>", "calc(2.6)", "3")]
    [InlineData("<integer>", "calc(-2.5)", "-2")]
    [InlineData("<angle>", "1turn", "360deg")]
    [InlineData("<time>", "1000ms", "1s")]
    [InlineData("<resolution>", "96dpi", "1dppx")]
    [InlineData("<resolution>", "calc(1dppx + 96dpi)", "2dppx")]
    [InlineData("<color>", "#008000", "rgb(0, 128, 0)")]
    [InlineData("<color>", "#badbee33", "rgba(186, 219, 238, 0.2)")]
    [InlineData("foo | bar", "bar", "bar")]
    [InlineData("<custom-ident>", "MyValue", "MyValue")]
    [InlineData("<length>+", "1in 2px", "96px 2px")]
    [InlineData("<length>#", "1in, 2px", "96px, 2px")]
    [InlineData("<number> | <length>", "2", "2")]
    [InlineData("<transform-function>", "translateX(10px)", "translateX(10px)")]
    [InlineData("<transform-list>", "translateX(10px) scale(2)", "translateX(10px) scale(2)")]
    [InlineData("*", "calc(1px + 1px)", "calc(1px + 1px)")]
    public void InitialValues_AreTypedAndCanonical(string syntax, string initial, string expected)
    {
        var root = new Border(); Css.SetStyleSheet(root, Registration(syntax, initial));
        Layout(root); Assert.Equal(expected, Computed(root));
    }

    [Theory]
    [InlineData("<length> <number>")]
    [InlineData("<length> || <number>")]
    [InlineData("* | <number>")]
    [InlineData("<made-up-type>")]
    [InlineData("<length># +")]
    [InlineData("<transform-list>+")]
    [InlineData("< length>")]
    [InlineData("<length >")]
    [InlineData("<length> +")]
    [InlineData("inherit")]
    [InlineData("<Length>")]
    public void InvalidSyntaxStrings_AreRejected(string syntax)
    {
        var sheet = CssStyleSheet.Parse(Registration(syntax));
        Assert.Empty(sheet.Properties); Assert.Contains(sheet.Diagnostics, d => d.Message.Contains("invalid @property"));
    }

    [Theory]
    [InlineData("<length>", "1em")]
    [InlineData("<length>", "1rem")]
    [InlineData("<length>", "10cqi")]
    [InlineData("<length>", "calc(1px + 1em)")]
    [InlineData("<length>", "10%")]
    [InlineData("<length>", "1")]
    [InlineData("<color>", "currentcolor")]
    [InlineData("<image>", "linear-gradient(currentcolor, red)")]
    [InlineData("<integer>", "1.0")]
    [InlineData("<integer>", "1e2")]
    [InlineData("<number>", "1px")]
    [InlineData("*", "var(--other)")]
    public void InvalidOrDependentInitialValues_DoNotRegister(string syntax, string initial)
        => Assert.Empty(CssStyleSheet.Parse(Registration(syntax, initial)).Properties);

    [Fact]
    public void Descriptors_RequireSyntaxAndInheritsAndIgnoreInvalidDescriptors()
    {
        Assert.Empty(CssStyleSheet.Parse("@property --x {syntax:'<length>'; initial-value:1px}").Properties);
        Assert.Empty(CssStyleSheet.Parse("@property --x {inherits:false; initial-value:1px}").Properties);
        Assert.Empty(CssStyleSheet.Parse("@property --x {syntax:'<length>'; inherits:false}").Properties);
        Assert.Single(CssStyleSheet.Parse("@property --x {syntax:'<length>'; syntax:42; inherits:false; inherits:maybe; initial-value:1px; ignored:yes}").Properties);
        Assert.Empty(CssStyleSheet.Parse("@property --x {syntax:'<length>' !important; inherits:false; initial-value:1px}").Properties);
    }

    [Fact]
    public void UniversalSyntax_DistinguishesMissingAndEmptyInitialValues()
    {
        var root = new Border();
        Css.SetStyleSheet(root, "@property --missing {syntax:'*';inherits:false} @property --empty {syntax:'*';inherits:false;initial-value:}");
        Css.SetStyle(root, "background:var(--empty) green; width:var(--missing,30px)"); Layout(root);
        Assert.Null(Computed(root, "--missing")); Assert.Equal(string.Empty, Computed(root, "--empty"));
        Assert.Equal(Colors.Green, Assert.IsType<SolidColorBrush>(root.Background).Color); Assert.Equal(30, root.Width);
    }

    [Fact]
    public void InvalidWinningDeclaration_UsesRegisteredDefaultWithoutResurrectingAnEarlierValue()
    {
        var root = new Border();
        Css.SetStyleSheet(root, Registration() + ":scope {--value:100px; --value:red; width:var(--value,90px)}");
        Layout(root); Assert.Equal("12px", Computed(root)); Assert.Equal(12, root.ActualWidth);
        Css.SetStyle(root, "--value:40px!important"); root.UpdateLayout(); Assert.Equal(40, root.ActualWidth);
    }

    [Theory]
    [InlineData(false, "", "12px")]
    [InlineData(true, "", "40px")]
    [InlineData(false, "--value:inherit", "40px")]
    [InlineData(false, "--value:\\69 nherit", "40px")]
    [InlineData(true, "--value:initial", "12px")]
    [InlineData(false, "--value:unset", "12px")]
    [InlineData(true, "--value:unset", "40px")]
    [InlineData(false, "--value:invalid", "12px")]
    [InlineData(true, "--value:invalid", "40px")]
    public void InheritanceAndGlobalKeywords_UseTheRegistrationFlag(bool inherits, string declarations, string expected)
    {
        var child = new Border(); var root = new Border { Child = child };
        Css.SetStyleSheet(root, Registration(inherits: inherits)); Css.SetStyle(root, "--value:40px"); Css.SetStyle(child, declarations);
        Layout(root); Assert.Equal(expected, Computed(child));
    }

    [Fact]
    public void TypedValues_AreComputedBeforeReferencesAndInheritedAsPixels()
    {
        var child = new TextBlock { Text = "original" }; var root = new Border { Child = child };
        Css.SetStyleSheet(root, Registration(initial:"0px", inherits:true));
        Css.SetStyle(root, "font-size:20px; --value:2em; --forwarded:var(--value)");
        Css.SetStyle(child, "font-size:10px; width:var(--forwarded)"); Layout(root);
        Assert.Equal("40px", Computed(root)); Assert.Equal("40px", Computed(child)); Assert.Equal(40, child.Width);
        Css.SetStyle(root, "font-size:30px; --value:2em; --forwarded:var(--value)"); root.UpdateLayout();
        Assert.Equal(60, child.Width); Assert.Equal("original", child.Text);
    }

    [Fact]
    public void RelativeLengthPercentageMath_RemainsSymbolicThroughSubstitution()
    {
        var child = new Border(); var root = new Border { Child = child };
        Css.SetStyleSheet(root, Registration("<length-percentage>", "0px"));
        Css.SetStyle(child, "font-size:20px; --value:calc(50% - 2em); width:var(--value)"); Layout(root);
        Assert.Equal("calc(50% - 40px)", Computed(child)); Assert.Equal(160, child.ActualWidth);
        Layout(root, 600); Assert.Equal(260, child.ActualWidth);
    }

    [Fact]
    public void RegistrationReplacementAndRemoval_RecomputeExistingDeclarations()
    {
        var root = new Border(); Css.SetStyle(root, "--value:2em; font-size:20px; width:var(--value)");
        Css.SetStyleSheet(root, Registration()); Layout(root); Assert.Equal("40px", Computed(root));
        Css.SetStyleSheet(root, Registration("<color>", "red")); root.UpdateLayout();
        Assert.Equal("rgb(255, 0, 0)", Computed(root)); Assert.True(double.IsNaN(root.Width));
        Css.SetStyleSheet(root, ""); root.UpdateLayout();
        Assert.Equal("2em", Computed(root)); Assert.Equal(40, root.Width);
    }

    [Fact]
    public void Registrations_AreDocumentWideButDoNotLeakToAnotherNativeRoot()
    {
        var source = new Border(); var consumer = new Border(); var root = new StackPanel();
        root.Children.Add(source); root.Children.Add(consumer);
        Css.SetStyleSheet(source, Registration(initial:"70px")); Css.SetStyle(consumer, "width:var(--value,20px)");
        Layout(root); Assert.Equal(70, consumer.Width); Assert.Equal("70px", Computed(root));
        var unrelated = new Border(); Css.SetStyle(unrelated, "width:var(--value,20px)"); Layout(unrelated); Assert.Equal(20, unrelated.Width);
        root.Children.Remove(source); root.UpdateLayout(); Assert.Equal(20, consumer.Width);
        root.Children.Add(source); root.UpdateLayout(); Assert.Equal(70, consumer.Width);
    }

    [Fact]
    public void LastValidRegistration_WinsWithoutChangingTheDeclarationCascade()
    {
        var root = new Border();
        Css.SetStyleSheet(root, Registration(initial:"10px") + Registration(initial:"30px") + Registration(initial:"1em"));
        Layout(root); Assert.Equal("30px", Computed(root));
    }

    [Fact]
    public void ConditionalRegistrations_TrackMediaAndIgnoreContainerConditions()
    {
        var root = new Border();
        Css.SetStyleSheet(root, Registration(initial:"10px") + "@media (min-width:500px) {" + Registration(initial:"30px") + "}");
        Layout(root); Assert.Equal("10px", Computed(root));
        Layout(root, 600); Assert.Equal("30px", Computed(root));
        Css.SetStyleSheet(root, "@container never (width > 10000px) {" + Registration(initial:"25px") + "}");
        root.UpdateLayout(); Assert.Equal("25px", Computed(root));
    }

    [Fact]
    public void RegisteredFallbacks_AreTypeCheckedEvenWhenUnused()
    {
        var child = new Border(); var root = new Border { Child = child };
        Css.SetStyleSheet(root, Registration());
        Css.SetStyle(child, "--value:30px; width:var(--value,red)"); Layout(root);
        Assert.True(double.IsNaN(child.Width));
        Css.SetStyle(child, "--value:30px; width:var(--value,50px)"); root.UpdateLayout(); Assert.Equal(30, child.Width);
        Css.SetStyle(child, "--unregistered:30px; width:var(--unregistered,var(--missing))"); root.UpdateLayout(); Assert.Equal(30, child.Width);
    }

    [Fact]
    public void Cycles_UseRegisteredDefaultsAndIncludeUnusedFallbackEdges()
    {
        var root = new Border(); Css.SetStyleSheet(root, Registration());
        Css.SetStyle(root, "--value:var(--other); --other:var(--value); width:var(--value)"); Layout(root);
        Assert.Equal("12px", Computed(root)); Assert.Null(Computed(root,"--other")); Assert.Equal(12, root.Width);
        Css.SetStyle(root, "--ok:20px; --value:var(--ok,var(--value)); width:var(--value)"); root.UpdateLayout();
        Assert.Equal(12, root.Width);
    }

    [Theory]
    [InlineData("2em")]
    [InlineData("2rem")]
    [InlineData("calc(2em + 1px)")]
    public void FontDependencyCycles_ResetTheFontAndRegisteredProperty(string value)
    {
        var root = new TextBlock(); Css.SetStyleSheet(root, Registration(initial:"5px"));
        Css.SetStyle(root, "--value:" + value + "; font-size:var(--value); width:var(--value)"); Layout(root);
        Assert.Equal(14, root.FontSize); Assert.Equal("5px", Computed(root)); Assert.Equal(5, root.Width);
    }

    [Fact]
    public void LocalFontValue_BreaksTheCssFontDependencyCycle()
    {
        var root = new TextBlock { FontSize = 20 }; Css.SetStyleSheet(root, Registration(initial:"5px"));
        Css.SetStyle(root, "--value:2em; font-size:var(--value); width:var(--value)"); Layout(root);
        Assert.Equal(20, root.FontSize); Assert.Equal(40, root.Width); Assert.Equal("40px", Computed(root));
    }

    [Fact]
    public void NativeLocalDimensionsAndBindings_KeepTheirPriority()
    {
        var child = new Border { Width = 70 }; var root = new Border { Child = child, Height = 90 };
        BindingOperations.SetBinding(child, FrameworkElement.HeightProperty, new Binding(nameof(Border.Height)) { Source = root });
        Css.SetStyleSheet(root, Registration(initial:"200px"));
        Css.SetStyle(child, "width:var(--value)!important; height:var(--value)!important"); Layout(root);
        Assert.Equal(70, child.Width); Assert.Equal(90, child.Height);
        Css.SetStyleSheet(root, ""); root.UpdateLayout();
        Assert.Equal(70, child.Width); Assert.NotNull(BindingOperations.GetBindingExpression(child, FrameworkElement.HeightProperty));
    }

    [Fact]
    public void StyleQueries_CompareTypedValuesAndRegisteredInitialValues()
    {
        var child = new Border(); var root = new Border { Child = child };
        Css.SetClass(child, "child");
        Css.SetStyleSheet(root, Registration(initial:"1in") + "@container style(--value) {.child {opacity:.2}} @container style(--value:96px) {.child {width:40px}}");
        Layout(root); Assert.Equal(1, child.Opacity); Assert.Equal(40, child.Width);
        Css.SetStyle(root, "--value:2in"); root.UpdateLayout(); Assert.Equal(.2, child.Opacity); Assert.True(double.IsNaN(child.Width));
    }

    [Fact]
    public void ContainerLengthsAndViewportInitials_RefreshThroughRegisteredValues()
    {
        var child = new Border(); var container = new Border { Width = 300, Child = child }; var root = new Border { Child = container };
        Css.SetStyleSheet(root, Registration(initial:"10vw"));
        Css.SetStyle(container, "container-type:inline-size"); Css.SetStyle(child, "--value:10cqi; width:var(--value)");
        Layout(root); Assert.Equal(30, child.Width); Assert.Equal("40px", Computed(root));
        container.Width = 200; root.UpdateLayout(); Assert.Equal(20, child.Width);
        Layout(root, 600); Assert.Equal("60px", Computed(root));
    }

    [Theory]
    [InlineData("--color", "1em", true)]
    [InlineData("--color", "", true)]
    [InlineData("--", "red", false)]
    [InlineData("--bad name", "red", false)]
    [InlineData("--color", "calc(", false)]
    [InlineData("--color", "red!important", false)]
    public void Supports_UsesCustomPropertyParseSyntaxInsteadOfRegisteredTypes(string name, string value, bool expected)
        => Assert.Equal(expected, Css.Supports(name, value));

    [Fact]
    public void ViewportInitialValues_DoNotFeedBackFromTheStyledRootWidth()
    {
        var root = new Border(); Css.SetStyleSheet(root, Registration(initial:"10vw"));
        Css.SetStyle(root, "width:var(--value)"); Layout(root);
        Assert.Equal(40, root.ActualWidth); Assert.Equal("40px", Computed(root));
        Layout(root, 600); Assert.Equal(60, root.ActualWidth); Assert.Equal("60px", Computed(root));
    }

    [Fact]
    public void ViewportChanges_AreObservedEvenWhenTheNativeRootSizeIsFixed()
    {
        var root = new Border { Width = 100 }; Css.SetStyleSheet(root, Registration(initial:"10vw"));
        Layout(root); Assert.Equal(100, root.ActualWidth); Assert.Equal("40px", Computed(root));
        Layout(root, 600); Assert.Equal(100, root.ActualWidth); Assert.Equal("60px", Computed(root));
    }

    [Fact]
    public void RegisteredColor_ComputesCurrentColorBeforeInheritanceAndStyleQueries()
    {
        var child = new Border(); var root = new Border { Child = child };
        Css.SetStyleSheet(root, Registration("<color>", "black", true) + "@container style(--value:currentcolor) { .child {opacity:.3} }");
        Css.SetClass(child,"child"); Css.SetStyle(root, "--value:currentcolor; color:blue");
        Css.SetStyle(child,"color:red; background:var(--value)"); Layout(root);
        Assert.Equal("rgb(0, 0, 255)", Computed(root)); Assert.Equal(Colors.Blue, Assert.IsType<SolidColorBrush>(child.Background).Color);
        Assert.Equal(.3, child.Opacity);
    }

    [Fact]
    public void RegisteredListValues_UseExistingShorthandAndTransformConverters()
    {
        var child = new Border(); var root = new Border { Child = child };
        Css.SetStyleSheet(root, Registration("<length>+", "0px") + Registration("<transform-list>", "scale(1)", name:"--transform"));
        Css.SetStyle(child,"font-size:20px; --value:1em 2em; margin:var(--value); --transform:translateX(2em) scale(2); transform:var(--transform)");
        Layout(root); Assert.Equal(new Thickness(40,20,40,20), child.Margin);
        Assert.Equal("translateX(40px) scale(2)", Computed(child,"--transform"));
        Assert.NotNull(child.RenderTransform);
    }

    [Fact]
    public void DocumentRuns_UseRegisteredValuesWithoutChangingOriginalText()
    {
        var run = new Run("原始文本"); var paragraph = new Paragraph(run);
        Css.SetStyleSheet(paragraph, Registration("<number>", "2", true));
        Css.SetStyle(run, "font-size:calc(var(--value) * 10px)");
        for (var i = 0; i < 4 && CssEvaluationScheduler.HasPending(paragraph.Dispatcher); i++) CssEvaluationScheduler.FlushIfPending(paragraph.Dispatcher);
        Assert.Equal(20, run.FontSize); Assert.Equal("原始文本", run.Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cycles_InvalidateEveryBranchOfAStronglyConnectedComponent(bool registered)
    {
        var root = new Border();
        if (registered) Css.SetStyleSheet(root, Registration("*", "fallback", name:"--a"));
        Css.SetStyle(root, "--a:var(--b) var(--c); --b:var(--d); --d:var(--a); --c:var(--d); --after:var(--c,30px); width:var(--after)");
        Layout(root); Assert.Null(Computed(root,"--c")); Assert.Equal(30, root.Width);
    }

    [Fact]
    public void FontCycles_IncludeBranchesDiscoveredAfterTheInitialBackEdge()
    {
        var root = new TextBlock(); Css.SetStyleSheet(root, Registration(name:"--b",initial:"5px"));
        Css.SetStyle(root,"--a:calc(var(--b) + var(--c)); --b:1em; --c:var(--b); --after:var(--c,30px); font-size:var(--a); width:var(--after)");
        Layout(root); Assert.Equal(14, root.FontSize); Assert.Null(Computed(root,"--c")); Assert.Equal(30, root.Width);
    }

    [Fact]
    public void EscapedGlobalKeywords_AlsoWorkForOrdinaryAndUnregisteredProperties()
    {
        var child = new Border(); var root = new Border { Child = child };
        Css.SetStyle(root,"--unregistered:40px; width:200px");
        Css.SetStyle(child,"--unregistered:\\69 nherit; width:\\69 nherit"); Layout(root);
        Assert.Equal("40px",Computed(child,"--unregistered")); Assert.Equal(200,child.Width);
    }

    [Fact]
    public void UnquotedUrlEscapes_AreDecodedWithoutLoadingResources()
    {
        var root = new Border(); Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(
            Registration("<url>","url(default.png)") + ":scope {--value:url(images\\2f icon.png)}", "url-test", new Uri("https://example.test/css/app.css")));
        Layout(root); Assert.Equal("url(\"https://example.test/css/images/icon.png\")",Computed(root));
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference UseSharedRegistrations(CssStyleSheetCollection shared)
    {
        var root = new Border(); Css.SetStyleSheets(root,shared); Css.SetStyle(root,"width:var(--value)");
        Layout(root); return new WeakReference(root);
    }

    [Fact]
    public void SharedRegistrationSheets_DoNotRetainUnusedNativeRoots()
    {
        var sheets = new CssStyleSheetCollection { CssStyleSheet.Parse(Registration()) };
        var weak = UseSharedRegistrations(sheets);
        for (var i = 0; i < 3 && weak.IsAlive; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
        Assert.False(weak.IsAlive);
        sheets.Add(CssStyleSheet.Parse(Registration(initial:"20px")));
        GC.KeepAlive(sheets);
    }

    [Theory]
    [InlineData("<\\6c ength>")]
    [InlineData("\u00a0<length>")]
    public void SyntaxGrammar_AfterStringDecodingUsesLiteralTypeNamesAndAsciiWhitespace(string syntax)
        => Assert.Null(CssPropertySyntax.Parse(syntax));
}

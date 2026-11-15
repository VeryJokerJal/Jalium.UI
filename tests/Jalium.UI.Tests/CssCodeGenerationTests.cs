using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Jalium.UI.Automation;
using Jalium.UI.Compiler;
using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Media.Effects;
using Jalium.UI.Markup;
using Jalium.UI.Styling;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Jalium.UI.Tests;

public class CssCodeGenerationTests
{
    static CssCodeGenerationTests() => RuntimeHelpers.RunModuleConstructor(typeof(TypeConverterRegistry).Module.ModuleHandle);

    private static readonly MetadataReference[] References = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator).Append(typeof(Css).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(path => MetadataReference.CreateFromFile(path)).ToArray();

    private static Assembly Compile(string source)
    {
        Assert.DoesNotContain("CssStyleSheet.Parse(", source);
        Assert.DoesNotContain("CssParser.", source);
        var compilation = CSharpCompilation.Create("CompiledCssTests_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview))], References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        var assembly = Assembly.Load(stream.ToArray());
        RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
        return assembly;
    }

    private static CssStyleSheet Create(Assembly assembly, int index = 0, Uri? uri = null)
        => assembly.GetType("Jalium.UI.Generated.__JaliumCompiledCss")!
            .GetMethod("Create" + index, BindingFlags.Static | BindingFlags.NonPublic)!
            .CreateDelegate<Func<string?, Uri?, CssStyleSheet>>()("generated", uri);

    private static CssStyleSheet RoundTrip(string css)
    {
        var parsed = CssStyleSheet.Parse(css);
        var source = CssSourceEmitter.Generate([new("unused", parsed, false)]);
        var generated = Create(Compile(source));
        // Re-emitting the graph checks every field, ordering and shared reference,
        // including namespace identities and anonymous-layer remapping.
        Assert.Equal(source, CssSourceEmitter.Generate([new("unused", generated, false)]));
        return generated;
    }

    private static void Layout(FrameworkElement root, double width = 600)
    {
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        root.Measure(new(width, 400));
        root.Arrange(new(0, 0, width, 400));
        root.UpdateLayout();
    }

    [Theory]
    [InlineData("Button.primary#go:hover > Border:not(.off):nth-child(2n + 1 of .item) {width:40px!important; margin:1px 2px}")]
    [InlineData(".card, #hero { > .child {width:40px} & {opacity:.5} width:50px; @media (width > 500px) {height:30px} }")]
    [InlineData("@scope (.card) to (.stop) { & { .child {width:30px} } @scope (.inner) { &:has(>.error) {opacity:.4} } }")]
    [InlineData("@layer one,two; @import 'base.css' layer supports(display:grid) (min-width:500px); @layer { .x{width:2px} @layer inner {.y{opacity:.3}} }")]
    [InlineData("@namespace ui 'urn:ui'; @namespace p 'urn:props'; @supports selector(ui|Border[p|mode='on']) {ui|Border[p|mode='on' i]{width:40px}}")]
    [InlineData("@namespace p 'urn:props'; .box{width:attr(p|size px,7px);--size:attr(p|size px,7px)} @keyframes grow{to{width:attr(p|size px,9px)}}")]
    [InlineData("@supports (width:env(safe-area-inset-left)) {.box{width:env(unknown,12px);--inset:env(safe-area-inset-top)}}")]
    [InlineData("@supports selector(:lang(fr, '*-CH')) {Border:lang(fr, '*-CH'){opacity:.4}}")]
    [InlineData("@supports selector(:dir(rtl)) {Border:dir(rtl){opacity:.4}}")]
    [InlineData("@supports selector(:placeholder-shown) {TextBox:placeholder-shown{opacity:.4}}")]
    [InlineData("@supports selector(:read-write) and selector(:read-only) {TextBox:read-write{opacity:.4}}")]
    [InlineData("@supports selector(:valid) and selector(:invalid) {TextBox:invalid{opacity:.4}}")]
    [InlineData("@supports selector(:unchecked) {ListBoxItem:unchecked{opacity:.4}}")]
    [InlineData("@supports selector(:open) {Expander:open{opacity:.4}}")]
    [InlineData("@supports selector(:default) {Button:default{opacity:.4}}")]
    [InlineData("@supports selector(:required) and selector(:optional) {TextBox:required{opacity:.4}}")]
    [InlineData("@supports font-format(woff2) and font-tech(features-opentype) {.card{width:40px}}")]
    [InlineData("@property --size {syntax:'<length># | auto';inherits:false;initial-value:10px,20px} .x{width:var(--size)}")]
    [InlineData("@layer fonts{@media (width > 500px){@font-face{font-family:'Compiled Font';src:local('Arial'),url(font.woff) format('woff');font-weight:100 900;font-stretch:75% 125%;font-style:italic;unicode-range:U+20-7f,U+4??;font-display:swap}}}")]
    [InlineData("@font-face{font-family:Collection;src:url(fonts/coll.woff2#Ahem%2DSecond) format(woff2),url(fallback.ttf)}")]
    [InlineData(".card{opacity:1;@starting-style{opacity:0}} @starting-style{@layer theme{.card{opacity:.2}}}")]
    [InlineData("@media (width >= 400px){@keyframes Fade{from,25%{opacity:0}25%{opacity:.4}to{opacity:1}}} .card{opacity:1}")]
    [InlineData(".x{background-image:linear-gradient(to right, red calc(50% + 10px), 75%, blue 100%)}")]
    [InlineData(".x{background-image:repeating-linear-gradient(to right, red 0px, blue 20px)}")]
    [InlineData(".x{background-image:conic-gradient(from 45deg at 30% 70% in oklch, red 0deg, blue 1turn)}")]
    [InlineData(".x{color:blue;background-image:conic-gradient(currentColor 0deg, color-mix(in srgb,currentColor,white) 1turn)}")]
    [InlineData(".x{background-color:color-mix(in oklch longer hue, red 30%, blue 70%)}")]
    [InlineData(".x{color:blue;background-color:rgb(from currentColor r g calc(b / 2));background-image:linear-gradient(to right, rgb(from red r g b), blue)}")]
    [InlineData(".x{box-shadow:1em 0 calc(.25em + 1px) red;filter:blur(calc(1rem + 2px))}")]
    [InlineData(".x{filter:brightness(2) contrast(.5)}")]
    [InlineData("TextBox{caret-color:#123456;caret-animation:manual}")]
    [InlineData("TextBox{caret:block manual #123456;caret-shape:underscore}")]
    [InlineData("TextBlock{outline:calc(.5em + 2px) auto;outline-color:auto}")]
    [InlineData("TextBlock{font-weight:bolder} TextBlock.note{font:italic lighter 16px Arial}")]
    [InlineData("TextBlock{font-weight:1000;font:1000 16px Arial}")]
    [InlineData("ScrollViewer{scrollbar-gutter:stable both-edges;scrollbar-width:thin}")]
    [InlineData(".host{container-type:inline-size} @container (width > 20px){.box{width:10cqw}}")]
    [InlineData(".grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:calc(1em + 2px)} .x{transform:translate(50%,2em);color:rgb(20 30 40 / .5)}")]
    [InlineData(".w-1\\/2[data-name='雪\\22 path'] { --text: '\"\\\\line'; opacity: .4; }")]
    [InlineData(".broken??? {width:9px} .valid{width:20px; broken declaration; opacity:.7}")]
    public void AllSyntax_RoundTripsThroughCompilableCSharp(string css) => RoundTrip(css);

    [Fact]
    public void GeneratedRelativeFontWeightTracksItsNativeParent()
    {
        var parent = new StackPanel();
        var child = new TextBlock();
        parent.Children.Add(child);
        parent.SetValue(Jalium.UI.Documents.TextElement.FontWeightProperty, FontWeight.FromOpenTypeWeight(400));
        Css.GetStyleSheets(parent).Add(RoundTrip("TextBlock {font-weight:bolder}"));
        Layout(parent);
        Assert.Equal(700, child.FontWeight.ToOpenTypeWeight());

        parent.SetValue(Jalium.UI.Documents.TextElement.FontWeightProperty, FontWeight.FromOpenTypeWeight(700));
        Layout(parent);
        Assert.Equal(900, child.FontWeight.ToOpenTypeWeight());
    }

    [Fact]
    public void GeneratedCaretAnimationReachesNativeTextInput()
    {
        var parent = new StackPanel();
        var child = new TextBox();
        parent.Children.Add(child);
        Css.GetStyleSheets(parent).Add(RoundTrip("StackPanel { caret-animation: manual }"));

        Layout(parent);
        Assert.True(CssCaretAnimationProperties.IsManual(child));
    }

    [Fact]
    public void GeneratedCaretShorthandSetsColorAnimationAndShape()
    {
        var box = new TextBox();
        Css.GetStyleSheets(box).Add(RoundTrip("TextBox { caret: block manual #123456 }"));

        Layout(box);
        Assert.Equal(CssCaretShape.Block, CssCaretShapeProperties.Get(box));
        Assert.True(CssCaretAnimationProperties.IsManual(box));
        Assert.Equal(0xFF123456u,
            Assert.IsType<SolidColorBrush>(CssCaretColorProperties.Get(box)).Color.ToArgb());
    }

    [Fact]
    public void GeneratedOutlineAutoRetainsRelativeWidthAndColorMode()
    {
        var block = new TextBlock { FontSize = 20 };
        Css.GetStyleSheets(block).Add(RoundTrip("TextBlock { outline: calc(.5em + 2px) auto }"));

        Layout(block);
        Assert.Equal(12, block.OutlineThickness, 2);
        Assert.Equal(OutlineStyle.Auto, block.OutlineStyle);
        Assert.True(CssOutlineAutoColorProperties.IsAuto(block));
    }

    [Fact]
    public void GeneratedLanguageSelector_TracksInheritedLanguage()
    {
        var sheet = RoundTrip("Border:lang(fr, '*-CH') { opacity:.4 }");
        var root = new StackPanel();
        var child = new Border();
        root.Children.Add(child);
        Css.GetStyleSheets(root).Add(sheet);
        Layout(root);
        Assert.Equal(1, child.Opacity);

        root.Language = XmlLanguage.GetLanguage("de-Latn-CH");
        Layout(root);
        Assert.Equal(.4, child.Opacity);

        root.Language = XmlLanguage.GetLanguage("en-US");
        Layout(root);
        Assert.Equal(1, child.Opacity);
    }

    [Fact]
    public void GeneratedDirectionSelector_TracksNativeDirection()
    {
        var sheet = RoundTrip("StackPanel, Border { direction:ltr } Border:dir(rtl) { opacity:.4 }");
        var root = new StackPanel();
        var child = new Border();
        root.Children.Add(child);
        Css.GetStyleSheets(root).Add(sheet);
        Layout(root);
        Assert.Equal(1, child.Opacity);

        root.FlowDirection = FlowDirection.RightToLeft;
        Layout(root);
        Assert.Equal(FlowDirection.LeftToRight, child.FlowDirection);
        Assert.Equal(.4, child.Opacity);

        root.ClearValue(FrameworkElement.FlowDirectionProperty);
        Layout(root);
        Assert.Equal(1, child.Opacity);
    }

    [Fact]
    public void GeneratedPlaceholderSelector_TracksDisplayedText()
    {
        var sheet = RoundTrip("TextBox:placeholder-shown { opacity:.4 }");
        var root = new StackPanel();
        var child = new TextBox { PlaceholderText = "Hint" };
        root.Children.Add(child);
        Css.GetStyleSheets(root).Add(sheet);
        Layout(root);
        Assert.Equal(.4, child.Opacity);

        child.Text = "entered";
        Layout(root);
        Assert.Equal(1, child.Opacity);

        child.Text = "";
        Layout(root);
        Assert.Equal(.4, child.Opacity);
    }

    [Fact]
    public void GeneratedMutabilitySelector_TracksReadOnlyState()
    {
        var sheet = RoundTrip("TextBox:read-write { opacity:.4 } TextBox:read-only { opacity:.8 }");
        var root = new StackPanel();
        var child = new TextBox();
        root.Children.Add(child);
        Css.GetStyleSheets(root).Add(sheet);
        Layout(root);
        Assert.Equal(.4, child.Opacity);

        child.IsReadOnly = true;
        Layout(root);
        Assert.Equal(.8, child.Opacity);

        child.IsReadOnly = false;
        Layout(root);
        Assert.Equal(.4, child.Opacity);
    }

    [Fact]
    public void GeneratedValiditySelector_TracksNativeValidationState()
    {
        var sheet = RoundTrip("TextBox:valid { opacity:.4 } TextBox:invalid { opacity:.8 }");
        var root = new StackPanel();
        var child = new TextBox();
        root.Children.Add(child);
        Css.GetStyleSheets(root).Add(sheet);
        Layout(root);
        Assert.Equal(.4, child.Opacity);

        Validation.MarkInvalid(child, new ValidationError("Required"));
        Layout(root);
        Assert.Equal(.8, child.Opacity);

        Validation.ClearInvalid(child);
        Layout(root);
        Assert.Equal(.4, child.Opacity);
    }

    [Fact]
    public void GeneratedChoiceSelector_TracksNativeSelection()
    {
        var sheet = RoundTrip("ListBoxItem:unchecked { opacity:.4 } ListBoxItem:checked { opacity:.8 }");
        var root = new StackPanel();
        var item = new ListBoxItem();
        root.Children.Add(item);
        Css.GetStyleSheets(root).Add(sheet);
        Layout(root);
        Assert.Equal(.4, item.Opacity);

        item.IsSelected = true;
        Layout(root);
        Assert.Equal(.8, item.Opacity);
    }

    [Fact]
    public void GeneratedOpenSelector_TracksNativeExpandedState()
    {
        var sheet = RoundTrip("Expander:open { opacity:.4 }");
        var root = new StackPanel();
        var expander = new Expander();
        root.Children.Add(expander);
        Css.GetStyleSheets(root).Add(sheet);
        Layout(root);
        Assert.Equal(1, expander.Opacity);

        expander.IsExpanded = true;
        Layout(root);
        Assert.Equal(.4, expander.Opacity);

        expander.IsExpanded = false;
        Layout(root);
        Assert.Equal(1, expander.Opacity);
    }

    [Fact]
    public void GeneratedDefaultSelector_TracksNativeButtonConfiguration()
    {
        var sheet = RoundTrip("Button:default { opacity:.4 }");
        var root = new StackPanel();
        var button = new Button();
        root.Children.Add(button);
        Css.GetStyleSheets(root).Add(sheet);
        Layout(root);
        Assert.Equal(1, button.Opacity);

        button.IsDefault = true;
        Layout(root);
        Assert.Equal(.4, button.Opacity);

        button.IsDefault = false;
        Layout(root);
        Assert.Equal(1, button.Opacity);
    }

    [Fact]
    public void GeneratedOptionalitySelectors_TrackNativeRequiredMetadata()
    {
        var sheet = RoundTrip("TextBox:optional { opacity:.4 } TextBox:required { opacity:.8 }");
        var root = new StackPanel();
        var input = new TextBox();
        root.Children.Add(input);
        Css.GetStyleSheets(root).Add(sheet);
        Layout(root);
        Assert.Equal(.4, input.Opacity);

        AutomationProperties.SetIsRequiredForForm(input, true);
        Layout(root);
        Assert.Equal(.8, input.Opacity);

        AutomationProperties.SetIsRequiredForForm(input, false);
        Layout(root);
        Assert.Equal(.4, input.Opacity);

        var binding = new Binding("Value") { Source = new { Value = "ready" } };
        binding.ValidationRules.Add(new RequiredValidationRule());
        input.SetBinding(TextBox.TextProperty, binding);
        Layout(root);
        Assert.Equal(.8, input.Opacity);

        binding.ValidationRules.Clear();
        Layout(root);
        Assert.Equal(.4, input.Opacity);

        binding.ValidationRules.Add(new RequiredValidationRule());
        Layout(root);
        Assert.Equal(.8, input.Opacity);

        input.ClearBinding(TextBox.TextProperty);
        Layout(root);
        Assert.Equal(.4, input.Opacity);
    }

    [Fact]
    public void GeneratedRangeSelectors_TrackNativeAndBoundRangeLimits()
    {
        var sheet = RoundTrip(
            "NumberBox:in-range, TextBox:in-range { opacity:.4 } " +
            "NumberBox:out-of-range, TextBox:out-of-range { opacity:.8 }");
        var root = new StackPanel();
        var number = new NumberBox { Minimum = 1, Maximum = 10, Value = 5 };
        var text = new TextBox();
        root.Children.Add(number);
        root.Children.Add(text);
        Css.GetStyleSheets(root).Add(sheet);
        Layout(root);
        Assert.Equal(.4, number.Opacity);
        Assert.Equal(1, text.Opacity);

        number.Text = "11";
        Layout(root);
        Assert.Equal(.8, number.Opacity);

        var binding = new Binding("Value") { Source = new { Value = "11" } };
        binding.ValidationRules.Add(new RangeValidationRule { Minimum = 1, Maximum = 10 });
        text.SetBinding(TextBox.TextProperty, binding);
        Layout(root);
        Assert.Equal(.8, text.Opacity);

        binding.ValidationRules.Clear();
        Layout(root);
        Assert.Equal(1, text.Opacity);
    }

    [Fact]
    public void GeneratedPresentationSelectors_TrackFullscreenWindowState()
    {
        var sheet = RoundTrip("Window:fullscreen { opacity:.6 } Window:modal { opacity:.8 }");
        var window = new Window { Width = 100, Height = 50 };
        Css.GetStyleSheets(window).Add(sheet);
        Layout(window);
        Assert.Equal(1, window.Opacity);

        window.WindowState = WindowState.FullScreen;
        Layout(window);
        Assert.Equal(.8, window.Opacity);

        window.WindowState = WindowState.Normal;
        Layout(window);
        Assert.Equal(1, window.Opacity);
    }

    [Fact]
    public void GeneratedMediaSelectors_TrackPlaybackAndMute()
    {
        var sheet = RoundTrip("MediaElement:paused { opacity:.4 } MediaElement:playing { opacity:.6 } MediaElement:muted { opacity:.8 }");
        using var media = new MediaElement();
        Css.GetStyleSheets(media).Add(sheet);
        Layout(media);
        Assert.Equal(.4, media.Opacity);

        var clock = new Jalium.UI.Media.MediaTimeline().CreateClock();
        media.Clock = clock;
        clock.Controller.Begin();
        Layout(media);
        Assert.Equal(.6, media.Opacity);

        media.IsMuted = true;
        Layout(media);
        Assert.Equal(.8, media.Opacity);

        clock.Controller.Pause();
        Layout(media);
        Assert.Equal(.8, media.Opacity);

        media.IsMuted = false;
        Layout(media);
        Assert.Equal(.4, media.Opacity);
    }

    [Fact]
    public void GeneratedRadius_PreservesEllipticalMathAndLiveBoxDimensions()
    {
        var border = new Border();
        Css.GetStyleSheets(border).Add(RoundTrip("Border { border-radius:calc(25% + 2px) / 20%; border-top-right-radius:10px 4px }"));
        CssBorderRadiusTests.Layout(border, 200, 100);
        Assert.Equal(new Size(52, 20), CssBorderRadiusTests.Used(border).TopLeft);
        Assert.Equal(new Size(10, 4), CssBorderRadiusTests.Used(border).TopRight);
        CssBorderRadiusTests.Layout(border, 100, 200);
        Assert.Equal(new Size(27, 40), CssBorderRadiusTests.Used(border).TopLeft);
    }

    [Fact]
    public void GeneratedConicGradient_AppliesAndResolvesAtUsedSize()
    {
        var sheet = RoundTrip(".box{background-image:conic-gradient(from 90deg, red 0 50%, blue 50% 100%)}");
        var box = new Border(); Css.SetClass(box, "box"); Css.GetStyleSheets(box).Add(sheet);
        Layout(box, 200);
        var source = Assert.IsType<ImageBrush>(box.Background);
        var resolved = Assert.IsType<ImageBrush>(source.CssGradientLayout!.Resolve(200, 100));
        var bitmap = Assert.IsType<Jalium.UI.Media.Imaging.BitmapImage>(resolved.ImageSource);
        Assert.Equal(200, bitmap.PixelWidth);
        Assert.Equal(100, bitmap.PixelHeight);
    }

    [Fact]
    public void GeneratedRelativeColor_FollowsNativeForegroundChanges()
    {
        var sheet = RoundTrip(".box{background-color:rgb(from currentColor r g b / .5)}");
        var button = new Button { Foreground = new SolidColorBrush(Colors.Blue) };
        Css.SetClass(button, "box");
        Css.GetStyleSheets(button).Add(sheet);
        Layout(button);
        Assert.Equal(0x800000FFu,
            Assert.IsType<SolidColorBrush>(button.Background).Color.ToArgb());
        button.Foreground = new SolidColorBrush(Colors.Red);
        Layout(button);
        Assert.Equal(0x80FF0000u,
            Assert.IsType<SolidColorBrush>(button.Background).Color.ToArgb());
    }

    [Fact]
    public void GeneratedEffects_ResolveRelativeLengthsAfterFontSizeChanges()
    {
        var sheet = RoundTrip(".box{box-shadow:1em 0 red;filter:blur(.25em)}");
        var button = new Button { FontSize = 20 };
        Css.SetClass(button, "box");
        Css.GetStyleSheets(button).Add(sheet);
        Layout(button);
        var first = Assert.IsType<EffectGroup>(button.Effect);
        Assert.Equal(20, Assert.IsType<DropShadowEffect>(first.Children[0]).OffsetX, 5);
        Assert.Equal(5, Assert.IsType<BlurEffect>(first.Children[1]).Radius, 5);

        button.FontSize = 30;
        Layout(button);
        var updated = Assert.IsType<EffectGroup>(button.Effect);
        Assert.NotSame(first, updated);
        Assert.Equal(30, Assert.IsType<DropShadowEffect>(updated.Children[0]).OffsetX, 5);
        Assert.Equal(7.5, Assert.IsType<BlurEffect>(updated.Children[1]).Radius, 5);
    }

    [Fact]
    public void GeneratedDropShadow_KeepsItsInputAlphaMaskSemantics()
    {
        var sheet = RoundTrip(".box{filter:drop-shadow(12px 0 1px red)}");
        var box = new Border();
        Css.SetClass(box, "box");
        Css.GetStyleSheets(box).Add(sheet);
        Layout(box);
        Assert.True(Assert.IsType<DropShadowEffect>(box.Effect).CssUsesAlphaMask);
    }

    [Fact]
    public void GeneratedColorFilterChain_KeepsIntermediateClampingStages()
    {
        var sheet = RoundTrip(".box{filter:brightness(2) contrast(.5)}");
        var box = new Border();
        Css.SetClass(box, "box");
        Css.GetStyleSheets(box).Add(sheet);
        Layout(box);
        Assert.Equal(2, Assert.IsType<CssColorMatrixChainEffect>(box.Effect).Matrices.Length);
    }

    [Fact]
    public void GeneratedRules_TrackStateAndPreserveLocalValues()
    {
        var sheet = RoundTrip(".card { > .child {width:40px;opacity:.8} &:disabled > .child{width:60px;opacity:.3} }");
        var root = new StackPanel(); var child = new Border(); root.Children.Add(child);
        Css.SetClass(root, "card"); Css.SetClass(child, "child"); Css.GetStyleSheets(root).Add(sheet);
        Layout(root); Assert.Equal(40, child.Width); Assert.Equal(.8, child.Opacity);
        root.IsEnabled = false; Layout(root); Assert.Equal(60, child.Width); Assert.Equal(.3, child.Opacity);
        child.Width = 77; root.IsEnabled = true; Layout(root); Assert.Equal(77, child.Width);
        child.ClearValue(FrameworkElement.WidthProperty); Layout(root); Assert.Equal(40, child.Width);
        Css.GetStyleSheets(root).Clear(); Layout(root); Assert.True(double.IsNaN(child.Width));
    }

    [Fact]
    public void GeneratedNamespaceAndRegisteredValues_AreLive()
    {
        var sheet = RoundTrip("@namespace ui 'urn:ui'; @namespace p 'urn:props'; @property --size {syntax:'<length>';inherits:false;initial-value:24px} ui|Border[p|mode='on']{width:var(--size)}");
        var box = new Border(); XamlBuilder.SetXmlIdentity(box, "urn:ui", "Border");
        Css.SetAttribute(box, "urn:props", "mode", "on"); Css.GetStyleSheets(box).Add(sheet);
        Layout(box); Assert.Equal(24, box.Width);
        Css.SetAttribute(box, "urn:props", "mode", null); Layout(box); Assert.True(double.IsNaN(box.Width));
    }

    [Fact]
    public void GeneratedAttributeSubstitutionKeepsNamespaceAndCustomPropertyContext()
    {
        var sheet = RoundTrip("@namespace p 'urn:props'; Border {width:attr(p|size px, 7px);" +
            "--height:attr(p|size px, 7px); height:var(--height)}");
        var border = new Border();
        Css.SetAttribute(border, "urn:props", "size", "42");
        Css.GetStyleSheets(border).Add(sheet);

        Layout(border);
        Assert.Equal(42, border.Width);
        Assert.Equal(42, border.Height);

        Css.SetAttribute(border, "urn:props", "size", null);
        Layout(border);
        Assert.Equal(7, border.Width);
        Assert.Equal(7, border.Height);
    }

    [Fact]
    public void GeneratedEnvironmentFallbackComputesAtRuntime()
    {
        var sheet = RoundTrip("Border { width:env(unknown, 23px) }");
        var border = new Border();
        Css.GetStyleSheets(border).Add(sheet);

        Layout(border);
        Assert.Equal(23, border.Width);
    }

    [Fact]
    public void AnonymousLayers_AreDeterministicInSourceAndIndependentAtRuntime()
    {
        const string css = "@layer {.box{width:10px}} @layer {.box{height:20px}}";
        var source = CssSourceEmitter.Generate([new("unused", CssStyleSheet.Parse(css), false)]);
        Assert.Equal(source, CssSourceEmitter.Generate([new("unused", CssStyleSheet.Parse(css), false)]));
        var assembly = Compile(source); var first = Create(assembly); var second = Create(assembly);
        Assert.NotEqual(first.Layers[0].Name, second.Layers[0].Name);
        Assert.Equal(first.Layers[0].Name, first.Rules[0].LayerName);
    }

    [Fact]
    public async Task CompiledResourceImports_PreserveConditionsBaseUriAndExplicitResolvers()
    {
        var prefix = "/Css" + Guid.NewGuid().ToString("N") + ";component/styles/";
        var main = new Uri(prefix + "main.css", UriKind.Relative);
        var source = CssSourceEmitter.Generate([
            new(main.ToString(), CssStyleSheet.Parse("@import 'base.css' layer(base) supports(display:grid) (min-width:500px); .box{height:20px} @keyframes fade{to{opacity:1}}"), true),
            new(prefix + "base.css", CssStyleSheet.Parse(".box{width:40px} @keyframes fade{to{opacity:.5}}"), true)]);
        Compile(source);
        Assert.Single(CssStyleSheet.FromUri(main).Imports);
        var sheet = await CssStyleSheet.LoadAsync(main);
        Assert.Equal(prefix + "base.css", sheet.Rules[0].BaseUri!.ToString());
        Assert.Equal([".5", "1"], sheet.Keyframes.Select(rule =>
            Assert.Single(Assert.Single(rule.Blocks).Declarations).RawValue));
        Assert.Equal(prefix + "base.css", sheet.Keyframes[0].BaseUri!.ToString());
        Assert.NotNull(sheet.Keyframes[0].Condition);
        Assert.Equal(main.ToString(), sheet.Keyframes[1].BaseUri!.ToString());
        var box = new Border(); var root = new StackPanel(); root.Children.Add(box);
        Css.SetClass(box, "box"); Css.GetStyleSheets(root).Add(sheet);
        Layout(root, 600); Assert.Equal(40, box.Width);
        Layout(root, 400); Assert.True(double.IsNaN(box.Width)); Assert.Equal(20, box.Height);
        var resolver = new ExplicitResolver();
        var overrideSheet = await CssStyleSheet.LoadAsync(main, resolver);
        Assert.Equal(1, resolver.Calls); Assert.Equal("99px", overrideSheet.Rules[0].Declarations[0].RawValue);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CssStyleSheet.LoadAsync(main, cancelled.Token));
    }

    [Fact]
    public void StaticMarkup_CompilesInlineAndScopedCss_WhileDynamicTextStillWorks()
    {
        var directory = Path.Combine(Path.GetTempPath(), "jalium-css-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var xaml = Path.Combine(directory, "View.jalxaml");
            const string inline = "width:31px;opacity:.61";
            const string scoped = ".compiled-markup {height:23px}";
            File.WriteAllText(xaml, $"<StackPanel Css.StyleSheet=\"{scoped}\"><Border Css.Style=\"{inline}\"/><Border Css.Style=\"{{Binding CssText}}\"/></StackPanel>");
            var output = Path.Combine(directory, "Styles.g.cs");
            var manifest = Path.Combine(directory, "inputs.json");
            File.WriteAllText(manifest, JsonSerializer.Serialize(new { AssemblyName = "Fixture", OutputPath = output, StyleSheets = Array.Empty<object>(), XamlFiles = new[] { xaml } }));
            Assert.Equal(0, CssCompiler.Run(manifest));
            var source = File.ReadAllText(output); Assert.DoesNotContain("Binding CssText", source);
            Compile(source);
            Assert.True(CssCompiledStyleRegistry.TryCreateText(inline, true, null, out _));
            Assert.True(CssCompiledStyleRegistry.TryCreateText(scoped, false, null, out _));
            var box = new Border(); Css.SetClass(box, "compiled-markup");
            Css.SetStyleSheet(box, scoped); Css.SetStyle(box, inline); Layout(box);
            Assert.Equal(31, box.Width); Assert.Equal(23, box.Height); Assert.Equal(.61, box.Opacity);
            Css.SetStyle(box, "width:47px"); Layout(box); Assert.Equal(47, box.Width); Assert.Equal(1, box.Opacity);
            Css.SetStyleSheet(box, ""); Layout(box); Assert.True(double.IsNaN(box.Height));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void Builder_RejectsUnknownFormat() => Assert.Throws<NotSupportedException>(() => new CssStyleSheetBuilder(999));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RazorMarkup_WithUnescapedComparisons_DoesNotBreakCssGeneration(bool hasCss)
    {
        var directory = Path.Combine(Path.GetTempPath(), "jalium-css-razor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var input = Path.Combine(directory, "View.jalxaml");
            var style = hasCss ? "Css.Style=\"width:83px;opacity:.42\"" : "";
            File.WriteAllText(input, $"<StackPanel xmlns=\"http://schemas.jalium.ui/2024\">@if (Count < 2) {{ <Border {style} /> }}</StackPanel>");
            var output = Path.Combine(directory, "Css.g.cs");
            var manifest = Path.Combine(directory, "inputs.json");
            File.WriteAllText(manifest, JsonSerializer.Serialize(new { AssemblyName = "Fixture", OutputPath = output, StyleSheets = Array.Empty<object>(), XamlFiles = new[] { input } }));
            Assert.Equal(0, CssCompiler.Run(manifest));
            var source = File.ReadAllText(output);
            var assembly = Compile(source);
            if (hasCss)
            {
                var sheet = Create(assembly);
                Assert.Equal("83px", Assert.Single(sheet.Rules).Declarations[0].RawValue);
                Assert.True(CssCompiledStyleRegistry.TryCreateText("width:83px;opacity:.42", true, null, out _));
            }
            else Assert.DoesNotContain("RegisterText(", source);
        }
        finally { Directory.Delete(directory, true); }
    }

    private sealed class ExplicitResolver : ICssResourceResolver
    {
        internal int Calls;
        public ValueTask<CssResource> ResolveAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult(new CssResource(uri, new MemoryStream(Encoding.UTF8.GetBytes(".box{width:99px}"))));
        }
    }
}

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Jalium.UI.Compiler;
using Jalium.UI.Controls;
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
    [InlineData("@property --size {syntax:'<length># | auto';inherits:false;initial-value:10px,20px} .x{width:var(--size)}")]
    [InlineData(".host{container-type:inline-size} @container (width > 20px){.box{width:10cqw}}")]
    [InlineData(".grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:calc(1em + 2px)} .x{transform:translate(50%,2em);color:rgb(20 30 40 / .5)}")]
    [InlineData(".w-1\\/2[data-name='雪\\22 path'] { --text: '\"\\\\line'; opacity: .4; }")]
    [InlineData(".broken??? {width:9px} .valid{width:20px; broken declaration; opacity:.7}")]
    public void AllSyntax_RoundTripsThroughCompilableCSharp(string css) => RoundTrip(css);

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
            new(main.ToString(), CssStyleSheet.Parse("@import 'base.css' layer(base) supports(display:grid) (min-width:500px); .box{height:20px}"), true),
            new(prefix + "base.css", CssStyleSheet.Parse(".box{width:40px}"), true)]);
        Compile(source);
        Assert.Single(CssStyleSheet.FromUri(main).Imports);
        var sheet = await CssStyleSheet.LoadAsync(main);
        Assert.Equal(prefix + "base.css", sheet.Rules[0].BaseUri!.ToString());
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

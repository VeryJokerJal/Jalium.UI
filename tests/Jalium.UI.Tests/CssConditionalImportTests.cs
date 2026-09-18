using System.Text;
using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssConditionalImportTests
{
    static CssConditionalImportTests() => System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(
        typeof(Jalium.UI.Markup.TypeConverterRegistry).Module.ModuleHandle);

    private sealed class TrackedStream(string value) : MemoryStream(Encoding.UTF8.GetBytes(value))
    {
        internal bool Disposed;
        protected override void Dispose(bool disposing) {Disposed=true; base.Dispose(disposing);}
    }
    private sealed class Resources(Dictionary<string,string> files) : ICssResourceResolver
    {
        internal readonly List<Uri> Requests=[];
        internal readonly List<TrackedStream> Streams=[];
        internal readonly Dictionary<string,string> Redirects=[];
        internal Action<Uri>? Requested;
        public ValueTask<CssResource> ResolveAsync(Uri uri,CancellationToken cancellationToken=default)
        {
            cancellationToken.ThrowIfCancellationRequested(); Requests.Add(uri); Requested?.Invoke(uri);
            if (!files.TryGetValue(uri.ToString(),out var value)) throw new FileNotFoundException(uri.ToString());
            var stream=new TrackedStream(value); Streams.Add(stream);
            var final=Redirects.TryGetValue(uri.ToString(),out var redirect) ? new Uri(redirect,UriKind.RelativeOrAbsolute) : uri;
            return ValueTask.FromResult(new CssResource(final,stream));
        }
    }
    private static readonly Uri MainUri=new("https://example.test/main.css");

    private static void Layout(FrameworkElement root,double width=400,double height=200)
    {
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        root.Measure(new(width,height)); root.Arrange(new(0,0,width,height)); root.UpdateLayout();
        Assert.False(CssEvaluationScheduler.HasPending(root.Dispatcher));
    }
    private static (Border Root,Border Child) Attach(CssStyleSheet sheet,double width=400)
    {
        var child=new Border(); var root=new Border {Child=child}; Css.SetClass(child,"box"); Css.GetStyleSheets(root).Add(sheet); Layout(root,width); return(root,child);
    }

    [Theory]
    [InlineData("'part.css'")]
    [InlineData("url(part.css)")]
    [InlineData("url('part.css') layer(base)")]
    [InlineData("'part.css' layer")]
    [InlineData("'part.css' layer()")]
    [InlineData("'part.css' supports(display:grid)")]
    [InlineData("'part.css' supports((display:grid) and (width:calc(10px + 2px)))")]
    [InlineData("'part.css' layer(framework.widgets) supports(selector(.box)) screen and (width >= 300px)")]
    [InlineData("url(p\\61 rt.css) layer(f\\6f o)")]
    public void ImportGrammar_IsRetainedWithoutResourceIo(string declaration)
    {
        var sheet=CssStyleSheet.Parse("@import "+declaration+";");
        var import=Assert.Single(sheet.Imports); Assert.Equal("part.css",import.Reference);
        Assert.Single(sheet.Diagnostics); Assert.Equal(CssDiagnosticSeverity.Info,sheet.Diagnostics[0].Severity);
    }

    [Theory]
    [InlineData("url(part file.css)")]
    [InlineData("'part.css' layer(a..b)")]
    [InlineData("'part.css' layer(a,b)")]
    [InlineData("'part.css' supports()")]
    [InlineData("'part.css' supports((display:grid) and (width:1px) or (height:1px))")]
    public void MalformedImports_AreDropped(string declaration)
    {
        var sheet=CssStyleSheet.Parse("@import "+declaration+"; .box{width:20px}");
        Assert.Empty(sheet.Imports); Assert.Single(sheet.Rules); Assert.Contains(sheet.Diagnostics,d=>d.Message.Contains("invalid @import"));
    }

    [Theory]
    [InlineData("display:grid",true)]
    [InlineData("(display:grid)",true)]
    [InlineData("(display:grid) and (width:10px)",true)]
    [InlineData("not (made-up:yes)",true)]
    [InlineData("selector(.box)",true)]
    [InlineData("display:grid!important",true)]
    [InlineData("(display:grid!important)",true)]
    [InlineData("at-rule(@import)",true)]
    [InlineData("at-rule(@doesnotexist)",false)]
    [InlineData("--custom:any tokens",true)]
    [InlineData("display:made-up",false)]
    [InlineData("selector(::unsupported)",false)]
    [InlineData("(made-up:yes) or (display:made-up)",false)]
    public async Task SupportsConditions_ControlFetching(string condition,bool loaded)
    {
        var resources=new Resources(new()
        {
            [MainUri.ToString()]="@import 'part.css' supports("+condition+"); .box {height:20px}",
            ["https://example.test/part.css"]=".box {width:40px}",
        });
        var sheet=await CssStyleSheet.LoadAsync(MainUri,resources); var (_,child)=Attach(sheet);
        Assert.Equal(loaded?2:1,resources.Requests.Count); Assert.Equal(loaded?40:double.NaN,child.Width);
        Assert.All(resources.Streams,stream=>Assert.True(stream.Disposed)); Assert.Empty(sheet.Diagnostics);
    }

    [Fact]
    public async Task MediaConditions_ReactToViewportChangesWithoutRefetching()
    {
        var resources=new Resources(new()
        {
            [MainUri.ToString()]="@import 'wide.css' supports(display:grid) screen and (500px <= width < 800px); .box {height:20px}",
            ["https://example.test/wide.css"]=".box {width:60px; background:green}",
        });
        var sheet=await CssStyleSheet.LoadAsync(MainUri,resources); var (root,child)=Attach(sheet);
        Assert.True(double.IsNaN(child.Width)); Layout(root,600); Assert.Equal(60,child.Width);
        Assert.Equal(Colors.Green,Assert.IsType<SolidColorBrush>(child.Background).Color);
        Layout(root,900); Assert.True(double.IsNaN(child.Width)); Assert.Equal(2,resources.Requests.Count);
    }

    [Fact]
    public async Task NestedLayerImports_PrefixChildLayersAndPreserveImportantOrder()
    {
        var resources=new Resources(new()
        {
            [MainUri.ToString()]="@layer library, app; @import 'base.css' layer(library); @layer app {.box {width:80px!important; height:80px}}",
            ["https://example.test/base.css"]="@import 'part.css' layer(parts); .box {width:50px!important; height:50px}",
            ["https://example.test/part.css"]="@layer controls {.box {width:40px!important; height:40px}}",
        });
        var sheet=await CssStyleSheet.LoadAsync(MainUri,resources); var (_,child)=Attach(sheet);
        Assert.Equal(40,child.Width); Assert.Equal(80,child.Height);
        Assert.Contains(sheet.Layers,layer=>layer.Name=="library.parts.controls");
        Assert.Equal("https://example.test/part.css",sheet.Rules[0].BaseUri!.ToString());
    }

    [Fact]
    public async Task RepeatedAnonymousLayers_AreDistinctEvenWhenTheResourceIsCached()
    {
        var resources=new Resources(new()
        {
            [MainUri.ToString()]="@import 'a.css'; @import 'b.css'; @import 'a.css';",
            ["https://example.test/a.css"]="@layer {.box {width:10px}}",
            ["https://example.test/b.css"]="@layer {.box {width:20px}}",
        });
        var sheet=await CssStyleSheet.LoadAsync(MainUri,resources); var (_,child)=Attach(sheet);
        Assert.Equal(10,child.Width); Assert.Equal(3,resources.Requests.Count);
        Assert.Equal(3,sheet.Rules.Select(rule=>rule.LayerName).Distinct().Count());
    }

    [Fact]
    public async Task RepeatedNamedLayers_KeepTheirFirstOccurrenceOrder()
    {
        var resources=new Resources(new()
        {
            [MainUri.ToString()]="@import 'a.css'; @import 'b.css'; @import 'a.css';",
            ["https://example.test/a.css"]="@layer a {.box {width:10px}}",
            ["https://example.test/b.css"]="@layer b {.box {width:20px}}",
        });
        var sheet=await CssStyleSheet.LoadAsync(MainUri,resources); var (_,child)=Attach(sheet);
        Assert.Equal(20,child.Width); Assert.Equal(3,resources.Requests.Count);
    }

    [Fact]
    public async Task FailedImports_StillReserveTheirDeclaredLayer()
    {
        var resources=new Resources(new()
        {
            [MainUri.ToString()]="@import 'missing.css' layer(early); @import 'later.css' layer(later); @layer early {.box {width:10px}}",
            ["https://example.test/later.css"]=".box {width:20px}",
        });
        var sheet=await CssStyleSheet.LoadAsync(MainUri,resources); var (_,child)=Attach(sheet);
        Assert.Equal(20,child.Width); Assert.Contains(sheet.Diagnostics,d=>d.Message.Contains("missing.css"));
    }

    [Fact]
    public async Task FalseImportConditions_DoNotReserveTheLayerUntilTheyMatch()
    {
        var resources=new Resources(new()
        {
            [MainUri.ToString()]="@import 'part.css' layer(early) (min-width:500px); @layer later {.box {width:20px}} @layer early {.box {width:10px}}",
            ["https://example.test/part.css"]=".box {width:40px}",
        });
        var sheet=await CssStyleSheet.LoadAsync(MainUri,resources); var (root,child)=Attach(sheet);
        Assert.Equal(10,child.Width); Layout(root,600); Assert.Equal(20,child.Width); Layout(root,300); Assert.Equal(10,child.Width);
    }

    [Fact]
    public async Task FalseSupports_DoesNotReuseAnotherImportsCachedRulesOrLayer()
    {
        var resources=new Resources(new()
        {
            [MainUri.ToString()]="@import 'part.css' layer(first); @import 'part.css' layer(second) supports(made-up:yes); @layer later {.box {width:20px}} @layer second {.box {width:30px}}",
            ["https://example.test/part.css"]=".box {width:40px}",
        });
        var sheet=await CssStyleSheet.LoadAsync(MainUri,resources); var (_,child)=Attach(sheet);
        Assert.Equal(30,child.Width); Assert.Equal(3,sheet.Rules.Length); Assert.Equal(2,resources.Requests.Count);
    }

    [Fact]
    public async Task ImportCycles_AreExpandedPerAncestorChainInsteadOfCachedAsTruncatedTrees()
    {
        var resources=new Resources(new()
        {
            [MainUri.ToString()]="@import 'a.css'; @import 'b.css';",
            ["https://example.test/a.css"]="@import 'b.css'; .box {width:10px}",
            ["https://example.test/b.css"]="@import 'a.css'; .box {width:20px}",
        });
        var sheet=await CssStyleSheet.LoadAsync(MainUri,resources); var (_,child)=Attach(sheet);
        Assert.Equal(4,sheet.Rules.Length); Assert.Equal(new[]{"20px","10px","10px","20px"},sheet.Rules.Select(r=>r.Declarations[0].RawValue));
        Assert.Equal(20,child.Width); Assert.Equal(3,resources.Requests.Count);
        Assert.Equal(2,sheet.Diagnostics.Count(d=>d.Message.Contains("cyclic")));
    }

    [Fact]
    public async Task RedirectCycles_DisposeTheResourceAndDoNotDuplicateRules()
    {
        var resources=new Resources(new()
        {
            [MainUri.ToString()]="@import 'alias.css'; .box {width:10px}",
            ["https://example.test/alias.css"]=".box {width:20px}",
        });
        resources.Redirects["https://example.test/alias.css"]=MainUri.ToString();
        var sheet=await CssStyleSheet.LoadAsync(MainUri,resources);
        Assert.Single(sheet.Rules); Assert.Contains(sheet.Diagnostics,d=>d.Message.Contains("cyclic redirected"));
        Assert.All(resources.Streams,stream=>Assert.True(stream.Disposed));
    }

    [Fact]
    public async Task Cancellation_DisposesTheCurrentStreamAndStopsFurtherImports()
    {
        using var cancel=new CancellationTokenSource();
        var resources=new Resources(new()
        {
            [MainUri.ToString()]="@import 'a.css'; @import 'b.css';",
            ["https://example.test/a.css"]=".box{width:10px}",
        });
        resources.Requested=uri=>{if(uri.AbsolutePath=="/a.css") cancel.Cancel();};
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>CssStyleSheet.LoadAsync(MainUri,resources,cancel.Token));
        Assert.Equal(2,resources.Requests.Count); Assert.All(resources.Streams,stream=>Assert.True(stream.Disposed));
    }

    [Fact]
    public async Task ConditionalImports_PreserveScopesAndRegisteredPropertyActivation()
    {
        var resources=new Resources(new()
        {
            [MainUri.ToString()]="@import 'part.css' layer(theme) (width > 500px); .box {height:20px}",
            ["https://example.test/part.css"]="@property --size {syntax:'<length>';inherits:false;initial-value:40px} @scope (:root) {& { .box {width:var(--size)} }}",
        });
        var sheet=await CssStyleSheet.LoadAsync(MainUri,resources); var (root,child)=Attach(sheet);
        Assert.True(double.IsNaN(child.Width)); Layout(root,600); Assert.Equal(40,child.Width);
        Layout(root,300); Assert.True(double.IsNaN(child.Width)); Assert.Empty(CssRegisteredProperties.For(root));
    }

    [Fact]
    public async Task ImportedDeclarations_RespectNativeLocalValuesAndBindings()
    {
        var resources=new Resources(new()
        {
            [MainUri.ToString()]="@import 'part.css' supports(display:grid);",
            ["https://example.test/part.css"]=".box {width:400px!important; height:400px!important}",
        });
        var sheet=await CssStyleSheet.LoadAsync(MainUri,resources); var (root,child)=Attach(sheet);
        child.Width=70; root.Height=90;
        BindingOperations.SetBinding(child,FrameworkElement.HeightProperty,new Binding(nameof(root.Height)){Source=root});
        Layout(root); Assert.Equal(70,child.Width); Assert.Equal(90,child.Height);
        Css.SetStyleSheets(root,null); root.UpdateLayout(); Assert.Equal(70,child.Width);
        Assert.NotNull(BindingOperations.GetBindingExpression(child,FrameworkElement.HeightProperty));
    }

    [Fact]
    public async Task LayerStatementsAfterAnImport_DisallowSubsequentImports()
    {
        var resources=new Resources(new()
        {
            [MainUri.ToString()]="@layer before; @import 'a.css'; @layer between; @import 'b.css';",
            ["https://example.test/a.css"]=".box{width:10px}",
        });
        var sheet=await CssStyleSheet.LoadAsync(MainUri,resources); Assert.Equal(2,resources.Requests.Count);
        Assert.Contains(sheet.Diagnostics,d=>d.Message.Contains("@import must precede"));
    }

    [Fact]
    public async Task EscapedLayerNames_AreCanonicalAndEscapedDotsAreNotNesting()
    {
        var resources=new Resources(new()
        {
            [MainUri.ToString()]="@layer f\\6f o, foo\\.bar; @import 'part.css' layer(foo); @layer f\\6f o {.box {width:20px}} @layer foo\\.bar {.box {height:30px}} @layer foo.bar {.box {height:40px}}",
            ["https://example.test/part.css"]=".box{width:10px}",
        });
        var sheet=await CssStyleSheet.LoadAsync(MainUri,resources); var (_,child)=Attach(sheet);
        Assert.Equal(20,child.Width); Assert.Equal(30,child.Height);
    }

    [Fact]
    public async Task ImportedPropertyRegistrations_FollowNormalLayerPriority()
    {
        var resources=new Resources(new()
        {
            [MainUri.ToString()]="@layer low, high; @import 'high.css' layer(high); @import 'low.css' layer(low); .box {width:var(--width)}",
            ["https://example.test/high.css"]="@property --width {syntax:'<length>';inherits:false;initial-value:40px}",
            ["https://example.test/low.css"]="@property --width {syntax:'<length>';inherits:false;initial-value:80px}",
        });
        var sheet=await CssStyleSheet.LoadAsync(MainUri,resources); var (_,child)=Attach(sheet);
        Assert.Equal(40,child.Width);
    }

    [Theory]
    [InlineData("screen",true)]
    [InlineData("not print",true)]
    [InlineData("only screen and (width)",true)]
    [InlineData("(width:400px)",true)]
    [InlineData("(400px = width)",true)]
    [InlineData("(399px < width <= 400px)",true)]
    [InlineData("(401px > width > 399px)",true)]
    [InlineData("(height:200px)",true)]
    [InlineData("(aspect-ratio:2/1)",true)]
    [InlineData("(max-aspect-ratio:2)",true)]
    [InlineData("(orientation:landscape)",true)]
    [InlineData("(width > 1000px), (height:200px)",true)]
    [InlineData("((width > 1000px) or (height:200px)) and (width:400px)",true)]
    [InlineData("not (width <= -100px)",true)]
    [InlineData("(width:calc(200px * 2))",true)]
    [InlineData("(w\\69 dth:400px)",true)]
    [InlineData("not (unknown-feature:yes)",false)]
    [InlineData("not (width:10%)",false)]
    [InlineData("not (width:400)",false)]
    [InlineData("not (width:var(--x))",false)]
    [InlineData("(orientation = landscape)",false)]
    [InlineData("not (min-orientation:landscape)",false)]
    [InlineData("not (500px < width < invalid)",false)]
    [InlineData("(width:400px) and (height:200px) or (width:1px)",false)]
    [InlineData("screen or (width:400px)",false)]
    [InlineData("screen and(width:400px)",false)]
    [InlineData("layer",false)]
    [InlineData("unknown",false)]
    public void MediaGrammar_UsesTypedRangesBooleanLogicAndUnknownValues(string query,bool expected)
    {
        var root=new Border(); Layout(root);
        Assert.Equal(expected,new CssCondition("media",query).Evaluate(root));
    }

    [Fact]
    public void InvalidSupportsRules_DoNotApplyUnparenthesizedDeclarations()
    {
        var sheet=CssStyleSheet.Parse("@supports display:grid {.box {width:40px}} .box {height:20px}");
        var (_,child)=Attach(sheet); Assert.True(double.IsNaN(child.Width)); Assert.Equal(20,child.Height);
        Assert.Contains(sheet.Diagnostics,d=>d.Message.Contains("invalid @supports"));
    }
}

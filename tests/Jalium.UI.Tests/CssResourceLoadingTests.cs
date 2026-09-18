using System.Text;
using Jalium.UI.Controls;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssResourceLoadingTests
{
    private sealed class Resources(Dictionary<string, string> files) : ICssResourceResolver
    {
        public List<Uri> Requests { get; } = [];
        public ValueTask<CssResource> ResolveAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(uri);
            if (!files.TryGetValue(uri.ToString(), out var text)) throw new FileNotFoundException(uri.ToString());
            return ValueTask.FromResult(new CssResource(uri, new MemoryStream(Encoding.UTF8.GetBytes(text))));
        }
    }

    [Fact]
    public async Task ImportLoading_PreservesResourceBasesOrderAndSharesFetches()
    {
        var resources = new Resources(new()
        {
            ["https://example.test/css/main.css"] = "@import 'parts/base.css'; @import 'parts/base.css'; .box {width:30px}",
            ["https://example.test/css/parts/base.css"] = ".box {width:20px}",
        });
        var sheet = await CssStyleSheet.LoadAsync(new Uri("https://example.test/css/main.css"), resources);
        Assert.Equal(2, resources.Requests.Count);
        Assert.Equal(3, sheet.Rules.Length);
        Assert.Equal("https://example.test/css/parts/base.css", sheet.Rules[0].BaseUri!.ToString());
        Assert.Equal(new[] { 0, 1, 2 }, sheet.Rules.Select(r => r.RuleIndex));
        Assert.Empty(sheet.Diagnostics);
        var target = new Border(); Css.SetClass(target, "box"); Css.GetStyleSheets(target).Add(sheet);
        CssEvaluationScheduler.FlushIfPending(target.Dispatcher);
        Assert.Equal(30, target.Width);
    }

    [Fact]
    public async Task ImportCyclesAndMissingImports_DoNotDiscardTheRootRules()
    {
        var resources = new Resources(new()
        {
            ["https://example.test/a.css"] = "@import 'b.css'; @import 'missing.css'; .root {width:30px}",
            ["https://example.test/b.css"] = "@import 'a.css'; .child {width:20px}",
        });
        var sheet = await CssStyleSheet.LoadAsync(new Uri("https://example.test/a.css"), resources);
        Assert.Equal(2, sheet.Rules.Length);
        Assert.Contains(sheet.Diagnostics, d => d.Message.Contains("cyclic"));
        Assert.Contains(sheet.Diagnostics, d => d.Message.Contains("missing.css"));
    }

    [Fact]
    public async Task Cancellation_StopsBeforeFetchingAnyResources()
    {
        var resources = new Resources(new());
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CssStyleSheet.LoadAsync(new Uri("https://example.test/a.css"), resources, cancellation.Token));
        Assert.Empty(resources.Requests);
    }

    [Fact]
    public async Task RelativePackReferences_PreserveTheApplicationResourcePrefix()
    {
        var resources = new Resources(new()
        {
            ["/App;component/styles/main.css"] = "@import '../shared/base.css'; .main {width:30px}",
            ["/App;component/shared/base.css"] = ".base {width:20px}",
        });
        var sheet = await CssStyleSheet.LoadAsync(new Uri("/App;component/styles/main.css", UriKind.Relative), resources);
        Assert.Equal(2, sheet.Rules.Length); Assert.Empty(sheet.Diagnostics);
    }

    [Fact]
    public async Task ImportedRegistrations_PreserveTheirOwnInitialAndDeclarationUrlBases()
    {
        var resources = new Resources(new()
        {
            ["https://example.test/main.css"] = "@import 'parts/base.css'; :scope {--image: url(main.png)}",
            ["https://example.test/parts/base.css"] = """
                @property --initial {syntax:'<url>';inherits:false;initial-value:url(initial.png)}
                @property --image {syntax:'<url>';inherits:false;initial-value:url(fallback.png)}
                @property --declared {syntax:'<url>';inherits:true;initial-value:url(fallback.png)}
                :scope {--declared:url(declared.png); --untyped:var(--declared)}
                """,
        });
        var sheet = await CssStyleSheet.LoadAsync(new Uri("https://example.test/main.css"), resources);
        Assert.Equal(3, sheet.Properties.Length); Assert.Equal(2, resources.Requests.Count);
        var child = new Border(); var root = new Border { Child = child }; Css.GetStyleSheets(root).Add(sheet);
        root.Measure(new(400,300)); root.UpdateLayout();
        var computed = CssNode.Get(root).CssRuntimeState!.CustomProperties!;
        Assert.Equal("url(\"https://example.test/parts/initial.png\")", computed["--initial"]);
        Assert.Equal("url(\"https://example.test/main.png\")", computed["--image"]);
        Assert.Equal("url(\"https://example.test/parts/declared.png\")", computed["--declared"]);
        Assert.Equal(computed["--declared"], CssNode.Get(child).CssRuntimeState!.CustomProperties!["--declared"]);
        Assert.Contains("https://example.test/parts/declared.png", computed["--untyped"]);
        Assert.Equal(2, resources.Requests.Count); // Registration parsing/computation does not fetch images.
    }

    [Fact]
    public async Task RegistrationOrder_PlacesImportedDefinitionsBeforeTheImportingSheet()
    {
        var resources = new Resources(new()
        {
            ["https://example.test/main.css"] = "@import 'base.css'; @property --x {syntax:'<number>';inherits:false;initial-value:2}",
            ["https://example.test/base.css"] = "@property --x {syntax:'<number>';inherits:false;initial-value:1}",
        });
        var sheet = await CssStyleSheet.LoadAsync(new Uri("https://example.test/main.css"), resources);
        var root = new Border(); Css.GetStyleSheets(root).Add(sheet); root.Measure(new(100,100)); root.UpdateLayout();
        Assert.Equal("2", CssNode.Get(root).CssRuntimeState!.CustomProperties!["--x"]);
    }

    [Theory]
    [InlineData("@property --x {syntax:'*';inherits:false}")]
    [InlineData("@layer block {}")]
    [InlineData("@supports (width:1px) {}")]
    public async Task ImportsAfterBlockRules_AreNotFetched(string prefix)
    {
        var resources = new Resources(new() { ["https://example.test/main.css"] = prefix + " @import 'late.css';" });
        var sheet = await CssStyleSheet.LoadAsync(new Uri("https://example.test/main.css"),resources);
        Assert.Single(resources.Requests); Assert.Contains(sheet.Diagnostics,d => d.Message.Contains("@import must precede"));
    }

    [Fact]
    public async Task ImportedScopeAndNesting_KeepTheirBindingsAndRelativeResourceBase()
    {
        var resources=new Resources(new()
        {
            ["https://example.test/main.css"]="@import 'parts/scoped.css';",
            ["https://example.test/parts/scoped.css"]="@scope (.card) { & { .child {width:40px} } }",
        });
        var sheet=await CssStyleSheet.LoadAsync(new Uri("https://example.test/main.css"),resources);
        Assert.NotNull(Assert.Single(sheet.Rules).Scope);
        Assert.Equal("https://example.test/parts/scoped.css",sheet.Rules[0].BaseUri!.ToString());
        var child=new Border(); var card=new Border {Child=child}; Css.SetClass(card,"card"); Css.SetClass(child,"child");
        Css.GetStyleSheets(card).Add(sheet); card.Measure(new(400,200)); card.UpdateLayout(); Assert.Equal(40,child.Width);
    }
}

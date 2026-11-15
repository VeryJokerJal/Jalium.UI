using System.Runtime.CompilerServices;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;

RuntimeHelpers.RunModuleConstructor(typeof(TypeConverterRegistry).Module.ModuleHandle);

var host = new StackPanel();
var child = new Border();
host.Children.Add(child);
Css.SetClass(child, "card");
Css.SetStyle(host, "display:flex; width:400px; height:100px");
Css.SetStyleSheet(host, """
    @layer base, app;
    :scope { --size: 25% }
    @layer base { .card { flex:0 0 10px } }
    @layer app { .card { flex:0 0 var(--size); background-color:red } }
    .card:is(:first-child, :last-child) { height:calc(10px * 3) }
    """);
host.Measure(new Size(400, 100));
host.UpdateLayout();
host.Arrange(new Rect(0, 0, 400, 100));
if (Math.Abs(child.ActualWidth - 100) > .001 || Math.Abs(child.ActualHeight - 30) > .001)
    throw new InvalidOperationException($"Unexpected CSS layout: {child.ActualWidth} x {child.ActualHeight}");

var run = new Run("Native XAML");
Css.SetStyle(run, "--size:12px; font-size:calc(var(--size) * 2); font-weight:bold");
if (run.FontSize != 24 || run.FontWeight.ToOpenTypeWeight() != 700 || run.Text != "Native XAML")
    throw new InvalidOperationException("CSS document styles failed");

var gridHost = new StackPanel();
var gridFirst = new Border(); var gridSecond = new Border();
gridHost.Children.Add(gridFirst); gridHost.Children.Add(gridSecond);
CssMappings.RegisterAlias("app-grid-columns", "grid-template-columns");
Css.SetStyle(gridHost, "display:grid; --tracks:repeat(2,minmax(0,1fr)); app-grid-columns:var(--tracks); grid-auto-rows:40px; gap:10px");
var subgrid = new StackPanel(); gridHost.Children.Add(subgrid);
var subFirst = new Border(); var subSecond = new Border();
subgrid.Children.Add(subFirst); subgrid.Children.Add(subSecond);
Css.SetStyle(subgrid, "display:grid; grid-template-columns:subgrid; grid-column:1 / -1; column-gap:0; padding:5px");
gridHost.Measure(new Size(410, 90)); gridHost.UpdateLayout(); gridHost.Arrange(new Rect(0, 0, 410, 90));
if (Math.Abs(gridFirst.ActualWidth - 200) > .001 || Math.Abs(gridSecond.VisualBounds.X - 210) > .001 ||
    !ReferenceEquals(gridFirst.VisualParent, gridHost))
    throw new InvalidOperationException("CSS Grid layout or native ownership failed");
if (Math.Abs(subFirst.ActualWidth - 200) > .001 || Math.Abs(subSecond.VisualBounds.X - 205) > .001 ||
    !ReferenceEquals(subFirst.VisualParent, subgrid))
    throw new InvalidOperationException("CSS subgrid layout or padding failed");

var flow = new StackPanel(); var flowFirst = new Border(); var flowSecond = new Border();
flow.Children.Add(flowFirst); flow.Children.Add(flowSecond);
Css.SetStyle(flow, "display:flow-root; width:300px; line-height:0");
Css.SetStyle(flowFirst, "height:20px; margin-bottom:10px");
Css.SetStyle(flowSecond, "height:20px; margin-top:20px");
flow.Measure(new Size(300, 60)); flow.UpdateLayout(); flow.Arrange(new Rect(0, 0, 300, 60));
if (Math.Abs(flowSecond.VisualBounds.Y - 40) > .001 || Math.Abs(flowSecond.ActualWidth - 300) > .001)
    throw new InvalidOperationException("CSS block flow or margin collapsing failed");

var floatRoot = new StackPanel(); var floated = new Border(); var beside = new Border(); var cleared = new Border();
floatRoot.Children.Add(floated); floatRoot.Children.Add(beside); floatRoot.Children.Add(cleared);
Css.SetStyle(floatRoot, "display:flow-root; line-height:0; width:300px");
Css.SetStyle(floated, "float:left; width:100px; height:40px");
Css.SetStyle(beside, "display:inline-block; width:150px; height:20px");
Css.SetStyle(cleared, "clear:both; height:20px");
floatRoot.Measure(new Size(300, 60)); floatRoot.UpdateLayout(); floatRoot.Arrange(new Rect(0, 0, 300, 60));
if (Math.Abs(beside.VisualBounds.X - 100) > .001 || Math.Abs(cleared.VisualBounds.Y - 40) > .001 || !ReferenceEquals(floated.VisualParent, floatRoot))
    throw new InvalidOperationException("CSS float exclusion or clearance failed");

var queryChild = new Border(); var queryContainer = new Border { Width = 400, Height = 100, Child = queryChild };
var queryRoot = new Border { Child = queryContainer };
Css.SetStyle(queryContainer, "container:card / inline-size; --density:wide");
Css.SetClass(queryChild, "query-child");
Css.SetStyleSheet(queryRoot, """
    .query-child {width:20px; height:10px}
    @container card (width > 300px) and style(--density:wide) {.query-child {width:10cqi}}
    """);
queryRoot.Measure(new Size(800, 200)); queryRoot.UpdateLayout();
if (Math.Abs(queryChild.ActualWidth - 40) > .001) throw new InvalidOperationException("CSS container query or relative unit failed");
queryContainer.Width = 200; queryRoot.UpdateLayout();
if (Math.Abs(queryChild.ActualWidth - 20) > .001 || !ReferenceEquals(queryChild.VisualParent, queryContainer))
    throw new InvalidOperationException("CSS container invalidation or native ownership failed");

var registeredText = new Jalium.UI.Controls.TextBlock { Text = "registered" };
var registeredRoot = new Border { Child = registeredText };
Css.SetStyleSheet(registeredRoot, """
    @property --registered-size {syntax:'<length>';inherits:true;initial-value:12px}
    @property --registered-color {syntax:'<color>';inherits:true;initial-value:blue}
    """);
Css.SetStyle(registeredRoot, "font-size:20px; --registered-size:2em");
Css.SetStyle(registeredText, "width:var(--registered-size); color:var(--registered-color)");
registeredRoot.Measure(new Size(400,100)); registeredRoot.UpdateLayout();
if (Math.Abs(registeredText.ActualWidth - 40) > .001 || registeredText.Text != "registered")
    throw new InvalidOperationException("Registered CSS value computation or native text preservation failed");
Css.SetStyle(registeredRoot, "--registered-size:invalid"); registeredRoot.UpdateLayout();
if (Math.Abs(registeredText.ActualWidth - 12) > .001)
    throw new InvalidOperationException("Registered CSS invalid-value fallback failed");

var scopedChild=new Border(); var scopedGroup=new Border {Child=scopedChild}; var scopedRoot=new Border {Child=scopedGroup};
Css.SetClass(scopedGroup,"scope-card"); Css.SetClass(scopedChild,"scope-child");
Css.SetStyleSheet(scopedRoot,"""
    @scope (.scope-card) to (:disabled) {
        width:300px;
        & { > .scope-child {width:40px; height:20px} }
    }
    .scope-child {width:80px}
    """);
scopedRoot.Measure(new Size(600,100)); scopedRoot.UpdateLayout();
if (Math.Abs(scopedChild.ActualWidth-40)>.001 || Math.Abs(scopedGroup.ActualWidth-300)>.001)
    throw new InvalidOperationException("CSS nesting or scope proximity failed");
scopedChild.IsEnabled=false; scopedRoot.UpdateLayout();
if (Math.Abs(scopedChild.ActualWidth-80)>.001 || !ReferenceEquals(scopedChild.VisualParent,scopedGroup))
    throw new InvalidOperationException("CSS scope invalidation or native ownership failed");

var importResolver=new CssSmokeResources();
var importedSheet=CssStyleSheet.LoadAsync(new Uri("https://css-smoke.test/main.css"),importResolver).GetAwaiter().GetResult();
var importChild=new Border(); var importRoot=new Border {Child=importChild}; Css.SetClass(importChild,"imported");
Css.GetStyleSheets(importRoot).Add(importedSheet);
importRoot.Measure(new Size(600,100)); importRoot.Arrange(new Rect(0,0,600,100)); importRoot.UpdateLayout();
if (Math.Abs(importChild.ActualWidth-40)>.001 || importResolver.Requests!=2)
    throw new InvalidOperationException("CSS conditional/layered import failed");
importRoot.Measure(new Size(300,100)); importRoot.Arrange(new Rect(0,0,300,100)); importRoot.UpdateLayout();
if (!double.IsNaN(importChild.Width) || importResolver.Requests!=2)
    throw new InvalidOperationException("CSS import media invalidation failed");

var namespaceBox=new Border();
XamlBuilder.SetXmlIdentity(namespaceBox,"urn:css-smoke:ui","Card");
namespaceBox.Tag="active";
XamlBuilder.RecordXmlAttribute(namespaceBox,"urn:css-smoke:props","Tag","active");
Css.SetAttribute(namespaceBox,"urn:css-smoke:props","mode","active");
Css.SetStyleSheet(namespaceBox,"@namespace ui 'urn:css-smoke:ui'; @namespace p 'urn:css-smoke:props'; ui|Card[p|mode=active]{width:40px} ui|Card[p|Tag=active]{height:20px}");
namespaceBox.Measure(new Size(200,100)); namespaceBox.UpdateLayout();
if(Math.Abs(namespaceBox.ActualWidth-40)>.001) throw new InvalidOperationException("CSS namespace matching failed");
Css.SetAttribute(namespaceBox,"urn:css-smoke:props","mode",null); namespaceBox.UpdateLayout();
if(!double.IsNaN(namespaceBox.Width)) throw new InvalidOperationException("CSS namespace attribute invalidation failed");
namespaceBox.Tag="inactive"; namespaceBox.UpdateLayout();
if(!double.IsNaN(namespaceBox.Height)) throw new InvalidOperationException("CSS namespaced native attribute invalidation failed");

var transitionBox = new Border(); var transitionHost = new Grid();
Css.SetStyle(transitionBox, "opacity:0; transition:opacity 30s linear");
transitionHost.Children.Add(transitionBox);
transitionHost.Measure(new Size(20, 20));
transitionHost.Arrange(new Rect(0, 0, 20, 20));
Css.SetStyle(transitionBox, "opacity:1; transition:opacity 30s linear");
if (transitionBox.Opacity != 0) throw new InvalidOperationException("CSS transition did not start");
transitionBox.Opacity = 1;
if (transitionBox.Opacity != 1) throw new InvalidOperationException("A local value did not stop the CSS transition");
Css.SetStyle(transitionBox, string.Empty);
if (transitionBox.Opacity != 1) throw new InvalidOperationException("Removing CSS changed the native local value");

var mathBox = new Jalium.UI.Controls.TextBlock { FontSize = 20, Text = "CSS math" };
var mathHost = new Border { Child = mathBox };
Css.SetStyle(mathBox, "width:round(8em,40px); opacity:calc(1em / 100px); height:min(infinity * 1px,20px)");
mathHost.Measure(new Size(400, 100)); mathHost.Arrange(new Rect(0, 0, 400, 100)); mathHost.UpdateLayout();
if (mathBox.ActualWidth != 160 || mathBox.Opacity != .2 || mathBox.ActualHeight != 20)
    throw new InvalidOperationException("CSS math function evaluation failed");
mathBox.FontSize = 40; mathHost.UpdateLayout();
if (mathBox.ActualWidth != 320 || mathBox.Opacity != .4)
    throw new InvalidOperationException("CSS math context invalidation failed");

using (var fontContext = Jalium.UI.Interop.RenderContext.GetOrCreateCurrent(Jalium.UI.Interop.RenderBackend.Software))
{
    var metrics = Jalium.UI.Interop.TextMeasurement.GetFontUnitMetrics("Arial", 20);
    if ((metrics.Available & 20) != 20 || metrics.ZeroAdvance <= 0)
        throw new InvalidOperationException("Native font-unit metrics were not loaded");
    var ruler = new Jalium.UI.Controls.TextBlock { FontSize = 20, FontFamily = new Jalium.UI.Media.FontFamily("Arial"), Text = "native rulers" };
    var rulerHost = new Border { Child = ruler };
    Css.SetStyle(ruler, "width:12ch; height:2lh; line-height:1.5");
    rulerHost.Measure(new Size(400, 100)); rulerHost.Arrange(new Rect(0, 0, 400, 100)); rulerHost.UpdateLayout();
    if (Math.Abs(ruler.ActualWidth - metrics.ZeroAdvance * 12) > .001 || ruler.ActualHeight != 60)
        throw new InvalidOperationException("Native font rulers did not reach CSS layout");
    Css.SetViewportMetrics(rulerHost, new CssViewportMetrics(new Size(200, 100), new Size(400, 300), new Size(300, 200)));
    Css.SetStyle(ruler, "width:10svw; height:10dvh"); rulerHost.UpdateLayout();
    if (ruler.ActualWidth != 20 || ruler.ActualHeight != 20)
        throw new InvalidOperationException("Native viewport context did not reach CSS layout");
    Jalium.UI.Interop.TextMeasurement.ClearCache();
}

var faceText = new Jalium.UI.Controls.TextBlock { Text = "XXXX", FontSize = 20 };
var faceRoot = new Border { Child = faceText };
Css.GetStyleSheets(faceRoot).Add(CssStyleSheet.Parse(
    "@font-face{font-family:AotFont;src:url(Ahem.woff2) format(woff2);font-display:swap} TextBlock{font-family:AotFont}",
    "font", new Uri("https://css-smoke.test/fonts.css"), new CssSmokeFontResources()));
using (var faceContext = Jalium.UI.Interop.RenderContext.GetOrCreateCurrent(Jalium.UI.Interop.RenderBackend.Software))
{
    faceRoot.Measure(new Size(400, 100)); faceRoot.Arrange(new Rect(0, 0, 400, 100));
    faceRoot.UpdateLayout();
    Css.WaitForFontsAsync(faceRoot).GetAwaiter().GetResult(); faceRoot.UpdateLayout();
    if (faceText.DesiredSize.Width != 82 || faceText.FontSize != 20 || faceText.Text != "XXXX")
        throw new InvalidOperationException($"CSS downloadable font failed: width={faceText.DesiredSize.Width}, size={faceText.FontSize}, text={faceText.Text}, errors={string.Join(';', Css.GetFontLoadErrors(faceRoot))}");
    Css.GetStyleSheets(faceRoot).Clear();
    Jalium.UI.Interop.TextMeasurement.ClearCache();
}

var roundedBox = new Border();
Css.SetStyle(roundedBox, "border-radius:50%; background-color:lime");
roundedBox.Measure(new Size(120, 60)); roundedBox.Arrange(new Rect(0, 0, 120, 60)); roundedBox.UpdateLayout();
if (roundedBox.HitTest(new Point(1, 1)) is not null || roundedBox.HitTest(new Point(60, 30))?.VisualHit != roundedBox)
    throw new InvalidOperationException("CSS elliptical corner hit testing failed");
var roundedBitmap = new Jalium.UI.Media.Imaging.RenderTargetBitmap(120, 60, 96, 96, Jalium.UI.Media.PixelFormat.Bgra32);
roundedBitmap.Render(roundedBox);
var roundedPixels = new byte[120 * 60 * 4];
roundedBitmap.CopyPixels(new Int32Rect(0, 0, 120, 60), roundedPixels, 120 * 4, 0);
if (roundedPixels[(30 * 120 + 60) * 4 + 1] != 255 || roundedPixels[(1 * 120 + 1) * 4 + 3] != 0)
    throw new InvalidOperationException("CSS elliptical corner painting failed");
roundedBox.CornerRadius = new CornerRadius(0);
if (roundedBox.HitTest(new Point(1, 1))?.VisualHit != roundedBox)
    throw new InvalidOperationException("Native corner radius did not take precedence");

using (var imageSource = BitmapImage.FromPixels(
    [0, 0, 255, 255, 0, 255, 0, 255], 2, 1))
{
    var imageBox = new Border
    {
        Background = new ImageBrush(imageSource),
    };
    Css.SetStyle(imageBox, "border-radius:50%");
    imageBox.Measure(new Size(80, 40)); imageBox.Arrange(new Rect(0, 0, 80, 40)); imageBox.UpdateLayout();
    var imageBitmap = new RenderTargetBitmap(80, 40, 96, 96, PixelFormat.Bgra32);
    imageBitmap.Render(imageBox);
    var imagePixels = new byte[80 * 40 * 4];
    imageBitmap.CopyPixels(new Int32Rect(0, 0, 80, 40), imagePixels, 80 * 4, 0);
    if (imagePixels[(1 * 80 + 1) * 4 + 3] != 0 ||
        imagePixels[(20 * 80 + 10) * 4 + 2] != 255 ||
        imagePixels[(20 * 80 + 70) * 4 + 1] != 255)
        throw new InvalidOperationException("CSS elliptical ImageBrush painting failed");
}

var outlineBox = new Border
{
    Width = 100,
    Height = 40,
    HorizontalAlignment = HorizontalAlignment.Center,
    VerticalAlignment = VerticalAlignment.Center,
};
var outlineHost = new Grid { Width = 140, Height = 80 };
outlineHost.Children.Add(outlineBox);
Css.SetStyle(outlineBox,
    "border-radius:50px / 10px; outline:2px solid lime; outline-offset:4px");
outlineHost.Measure(new Size(140, 80)); outlineHost.Arrange(new Rect(0, 0, 140, 80)); outlineHost.UpdateLayout();
var outlineBitmap = new RenderTargetBitmap(140, 80, 96, 96, PixelFormat.Bgra32);
outlineBitmap.Render(outlineHost);
var outlinePixels = new byte[140 * 80 * 4];
outlineBitmap.CopyPixels(new Int32Rect(0, 0, 140, 80), outlinePixels, 140 * 4, 0);
if (outlinePixels[(20 * 140 + 29) * 4 + 1] < 180 ||
    outlinePixels[(15 * 140 + 40) * 4 + 1] > 40)
    throw new InvalidOperationException("CSS elliptical outline painting failed");

var shadowBox = new Border();
Css.SetStyle(shadowBox,
    "border-radius:60px 18px / 14px 34px; background-color:blue; box-shadow:0 8px 18px rgba(56,189,248,.65)");
shadowBox.Measure(new Size(180, 90)); shadowBox.Arrange(new Rect(0, 0, 180, 90)); shadowBox.UpdateLayout();
if (shadowBox.Effect is not Jalium.UI.Media.Effects.DropShadowEffect ||
    shadowBox.CornerRadius != new CornerRadius(60, 18, 60, 18))
    throw new InvalidOperationException("CSS elliptical drop-shadow setup failed");

var insetBox = new Border { BorderThickness = new Thickness(6) };
Css.SetStyle(insetBox,
    "border-radius:50px / 10px; box-shadow:inset 0 0 8px 5px red");
insetBox.Measure(new Size(100, 40)); insetBox.Arrange(new Rect(0, 0, 100, 40));
insetBox.UpdateLayout();
if (insetBox.Effect is not Jalium.UI.Media.Effects.InnerShadowEffect { SpreadRadius: 5 } ||
    insetBox.BorderThickness != new Thickness(6) ||
    insetBox.CornerRadius != new CornerRadius(50))
    throw new InvalidOperationException("CSS elliptical inset-shadow setup failed");
insetBox.CornerRadius = new CornerRadius(8);
if (insetBox.CornerRadius != new CornerRadius(8) ||
    insetBox.Effect is not Jalium.UI.Media.Effects.InnerShadowEffect { SpreadRadius: 5 })
    throw new InvalidOperationException("Native CornerRadius did not take over the CSS inset shadow");

var radiusOwner = new Control();
Border? radiusChrome = null;
var radiusTemplate = new ControlTemplate(typeof(Control));
radiusTemplate.SetVisualTree(() =>
{
    radiusChrome = new Border { Background = Jalium.UI.Media.Brushes.Lime };
    Jalium.UI.Data.TemplateBindingExtensions.SetTemplateBinding(radiusChrome, Border.CornerRadiusProperty, Control.CornerRadiusProperty);
    return radiusChrome;
});
radiusOwner.Template = radiusTemplate;
Css.SetStyle(radiusOwner, "border-radius:50%; overflow:hidden");
radiusOwner.Measure(new Size(120, 60)); radiusOwner.Arrange(new Rect(0, 0, 120, 60)); radiusOwner.UpdateLayout();
if (radiusChrome is null || radiusOwner.HitTest(new Point(1, 1)) is not null ||
    Jalium.UI.Data.BindingOperations.GetBindingExpressionBase(radiusChrome, Border.CornerRadiusProperty) is null)
    throw new InvalidOperationException("CSS template radius forwarding failed");
var replacementRadiusTemplate = new ControlTemplate(typeof(Control));
replacementRadiusTemplate.SetVisualTree(() => new Border());
radiusOwner.Template = replacementRadiusTemplate;
if (radiusChrome.TemplatedParent is not null)
    throw new InvalidOperationException("Discarded template retained its owner");

if (RuntimeFeature.IsDynamicCodeSupported)
    throw new InvalidOperationException("This smoke test must run from a NativeAOT publish");
Console.WriteLine("CSS NativeAOT smoke passed");

internal sealed class CssSmokeResources : ICssResourceResolver
{
    internal int Requests;
    public ValueTask<CssResource> ResolveAsync(Uri uri,CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested(); Requests++;
        var css=uri.AbsolutePath switch
        {
            "/main.css"=>"@import 'unused.css' supports(missing:yes); @import 'base.css' layer(base) supports(display:grid) (min-width:500px);",
            "/base.css"=>".imported {width:40px; height:20px}",
            _=>throw new FileNotFoundException(uri.ToString()),
        };
        return ValueTask.FromResult(new CssResource(uri,new MemoryStream(System.Text.Encoding.UTF8.GetBytes(css))));
    }
}

internal sealed class CssSmokeFontResources : ICssResourceResolver
{
    public ValueTask<CssResource> ResolveAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new CssResource(uri,
            typeof(CssSmokeFontResources).Assembly.GetManifestResourceStream("CssAot.Ahem.woff2")!));
    }
}

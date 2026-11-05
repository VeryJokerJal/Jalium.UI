using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.CssCodeGenSmoke;
using Jalium.UI.Styling;

// CSS files deliberately are not embedded resources. A parser/resource fallback
// would fail to load these URIs, so this checks the generated registration path.
var sheet = await CssStyleSheet.LoadAsync(new Uri(
    "/Jalium.UI.Css.CodeGenSmoke;component/Styles/main.css", UriKind.Relative));
var root = new StackPanel();
var card = new Border();
root.Children.Add(card);
Css.SetClass(card, "compiled-card");
Css.GetStyleSheets(root).Add(sheet);
Layout(root);
Check(card.Width == 64 && card.Height == 24 && card.Margin.Left == 2, "compiled resource/import/property");
card.IsEnabled = false;
Layout(root);
Check(card.Opacity == .4, "dynamic selector state");
card.Width = 90;
Css.SetStyle(card, "width: 120px !important");
Layout(root);
Check(card.Width == 90, "native local precedence");

var view = new TestView();
Layout(view);
var markupCard = (Border)view.Children[0];
Check(markupCard.Width == 31 && markupCard.Height == 23 && markupCard.Opacity == .61, "generated XAML inline/scoped CSS");
Css.SetStyle(markupCard, "width: 47px");
Layout(view);
Check(markupCard.Width == 47 && markupCard.Opacity == 1, "dynamic text fallback");
var fileScope = (StackPanel)view.Children[1];
var fileCard = (Border)fileScope.Children[0];
Check(fileCard.Width == 42 && fileCard.Opacity == .73, "XAML CSS file source");
Check(CssStyleSheet.FromUri(new Uri("Styles/markup.css", UriKind.Relative)).SourceLabel ==
    "/Jalium.UI.Css.CodeGenSmoke;component/Styles/markup.css",
    "application-relative compiled CSS resource");
Check((await CssStyleSheet.LoadAsync(new Uri("Styles/markup.css", UriKind.Relative))).SourceLabel ==
    "/Jalium.UI.Css.CodeGenSmoke;component/Styles/markup.css",
    "application-relative async CSS resource");

var cssDirectory = Path.Combine(Path.GetTempPath(), "jalium-css-aot-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(cssDirectory);
var cssFile = Path.Combine(cssDirectory, "main.css");
var importedFile = Path.Combine(cssDirectory, "base.css");
try
{
    File.WriteAllText(cssFile, "@import 'base.css'; .disk-card { width: 19px }");
    File.WriteAllText(importedFile, ".disk-card { height: 13px }");
    Check(CssStyleSheet.FromFile(cssFile).SourceLabel == cssFile, "synchronous CSS file loading");
    var diskRoot = new StackPanel();
    var diskCard = new Border();
    Css.SetClass(diskCard, "disk-card");
    diskRoot.Children.Add(diskCard);
    Css.GetStyleSheets(diskRoot).Add(await CssStyleSheet.LoadFileAsync(cssFile));
    Layout(diskRoot);
    Check(diskCard.Width == 19 && diskCard.Height == 13, "CSS file imports");
}
finally
{
    File.Delete(cssFile);
    File.Delete(importedFile);
    Directory.Delete(cssDirectory);
}
Console.WriteLine("CSS generated C# smoke passed");

static void Layout(FrameworkElement root)
{
    root.Measure(new Size(640, 400));
    root.Arrange(new Rect(0, 0, 640, 400));
    root.UpdateLayout();
}

static void Check(bool condition, string feature)
{
    if (!condition) throw new InvalidOperationException("CSS code generation smoke failed: " + feature);
}

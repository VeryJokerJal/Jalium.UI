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

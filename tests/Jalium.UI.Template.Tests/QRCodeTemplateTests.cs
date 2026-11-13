using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;
namespace Jalium.UI.Tests;
[Collection("macOS Window globals")]
public sealed class QRCodeTemplateTests
{
    private static readonly Lazy<ResourceDictionary> Defaults=new(()=>
    {
        Jalium.UI.Markup.ThemeLoader.Initialize();using var stream=new MemoryStream();
        return ThemeManager.XamlLoader!(stream,"Themes/Generic.jalxaml",typeof(Control).Assembly)!;
    });
    [Fact]
    public void DefaultTemplateOwnsTheChromeAndTheViewRendersQrModules()
    {
        var qr=Create();Layout(qr);Assert.Same(qr,Part<QRCodeView>(qr,"PART_SymbolView").Owner);Assert.Same(qr.Background,Part<Border>(qr,"PART_Border").Background);Assert.NotNull(qr.SymbolClip);
        var ink=new Ink();qr.Render(ink);Assert.True(ink.BlackRectangles>10);Assert.Equal("Ready",VisualStateManager.GetCurrentStateName(qr,"ContentStates"));
        qr.IsEnabled=false;Assert.Equal(.65,Part<Grid>(qr,"PART_Root").Opacity);qr.IsEnabled=true;Assert.Equal(1,Part<Grid>(qr,"PART_Root").Opacity);
    }
    [Fact]
    public void EmptyAndErrorStatesFollowEncodingWithoutReplacingTheControl()
    {
        var qr=Create();Layout(qr);qr.Text=string.Empty;Layout(qr);Assert.Equal("Empty",VisualStateManager.GetCurrentStateName(qr,"ContentStates"));
        qr.Version=1;qr.Text=new string('x',1000);Layout(qr);Assert.NotEmpty(qr.GenerationErrorText);Assert.Equal("Error",VisualStateManager.GetCurrentStateName(qr,"ContentStates"));
        qr.Text="OK";Layout(qr);Assert.Empty(qr.GenerationErrorText);Assert.Equal("Ready",VisualStateManager.GetCurrentStateName(qr,"ContentStates"));
    }
    [Fact]
    public void RetiredViewsReleaseTheirOwnerWhileEncodedDataSurvives()
    {
        var qr=Create();Layout(qr);var retired=Part<QRCodeView>(qr,"PART_SymbolView");var symbol=typeof(QRCode).GetField("_symbol",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(qr);
        var blank=new ControlTemplate(typeof(QRCode));blank.SetVisualTree(()=>new Border());qr.Template=blank;Layout(qr);Assert.Null(retired.Owner);Assert.Equal("https://jaliumui.com",qr.Text);
        qr.ClearValue(Control.TemplateProperty);Layout(qr);Assert.Same(qr,Part<QRCodeView>(qr,"PART_SymbolView").Owner);Assert.Same(symbol,typeof(QRCode).GetField("_symbol",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(qr));
        var view=new QRCodeView();Layout(view);var ink=new Ink();view.Render(ink);Assert.Equal(0,ink.BlackRectangles);
    }
    private static QRCode Create(){var qr=new QRCode{Text="https://jaliumui.com",Background=Brushes.White,Foreground=Brushes.Black};qr.Resources.MergedDictionaries.Add(Defaults.Value);return qr;}
    private static T Part<T>(Control owner,string name)where T:class=>Assert.IsAssignableFrom<T>(owner.FindName(name));
    private static void Layout(FrameworkElement element){for(var i=0;i<3;i++){element.InvalidateMeasure();element.Measure(new Size(220,220));element.Arrange(new Rect(0,0,220,220));element.UpdateLayout();}}
    private sealed class Ink : DrawingContextAdapter
    {
        internal readonly List<string> Text = new();
        internal int BlackRectangles;
        public override void DrawText(FormattedText text, Point origin) => Text.Add(text.Text);
        public override void DrawLine(Pen pen, Point a, Point b) { }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rect) {if(brush is SolidColorBrush color&&color.Color==Color.Black)BlackRectangles++;}
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rect, double x, double y) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double x, double y) { }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawImage(ImageSource image, Rect rect) { }
        public override void DrawBackdropEffect(Rect rect, IBackdropEffect effect, CornerRadius radius) { }
        public override void PushTransform(Transform transform) { }
        public override void PushClip(Geometry geometry) { }
        public override void PushOpacity(double opacity) { }
        public override void Pop() { }
        public override void Close() { }
    }
}

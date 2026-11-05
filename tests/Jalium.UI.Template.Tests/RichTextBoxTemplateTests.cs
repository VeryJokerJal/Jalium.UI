using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Styling;
namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class RichTextBoxTemplateTests
{
    private static readonly Lazy<ResourceDictionary> Defaults = new(() =>
    {
        Jalium.UI.Markup.ThemeLoader.Initialize(); using var stream = new MemoryStream();
        return ThemeManager.XamlLoader!(stream,"Themes/Generic.jalxaml",typeof(Control).Assembly)!;
    });
    [Fact]
    public void DefaultTemplateOwnsChromeAndDrawsDocumentInkThroughAnIndependentView()
    {
        var box=Create("alpha");box.Background=Brushes.Blue;box.BorderBrush=Brushes.Red;box.BorderThickness=new Thickness(2,3,4,5);box.CornerRadius=new CornerRadius(7);Layout(box);
        var border=Assert.IsType<Border>(box.FindName("PART_Border"));var view=Assert.IsType<RichTextBoxView>(box.FindName("PART_TextView"));
        Assert.Same(box,view.Owner);Assert.Same(box.Background,border.Background);Assert.Equal(box.BorderThickness,border.BorderThickness);Assert.Equal(box.CornerRadius,border.CornerRadius);
        var ink=new Ink();box.Render(ink);Assert.Contains("alpha",ink.Text);
        box.Background=Brushes.Green;Assert.Same(Brushes.Green,border.Background);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ReplacementReleasesOldPresentationAndPreservesDocumentSelectionAndEdits(bool cssDocument)
    {
        var box=Create("retained");if(cssDocument)Css.SetStyle(box.Document,"line-height:24px");Layout(box);box.SelectAll();var document=box.Document;var selected=box.Selection.Text;
        var retired=Assert.IsType<RichTextBoxView>(box.FindName("PART_TextView"));var blank=new ControlTemplate(typeof(RichTextBox));blank.SetVisualTree(()=>new Border());box.Template=blank;Layout(box);
        Assert.Null(retired.Owner);Assert.Same(document,box.Document);Assert.Equal(selected,box.Selection.Text);
        var ink=new Ink();box.Render(ink);Assert.Empty(ink.Text);retired.Render(ink);Assert.Empty(ink.Text);
        box.ClearValue(Control.TemplateProperty);Layout(box);Assert.NotSame(retired,box.FindName("PART_TextView"));Assert.Same(document,box.Document);
        box.Selection.Text="changed";Layout(box);ink=new Ink();box.Render(ink);Assert.Contains("changed",ink.Text);
    }
    [Fact]
    public void InstancesHaveIndependentViewsAndTheirOwnVisualStates()
    {
        Keyboard.Initialize();var first=Create("first");var second=Create("second");Layout(first);Layout(second);
        Assert.NotSame(first.FindName("PART_TextView"),second.FindName("PART_TextView"));Assert.True(first.Focus());
        Assert.Equal("Focused",VisualStateManager.GetCurrentStateName(first,"FocusStates"));first.IsReadOnly=true;Assert.Equal("ReadOnly",VisualStateManager.GetCurrentStateName(first,"EditingStates"));
        first.IsEnabled=false;Assert.Equal("Disabled",VisualStateManager.GetCurrentStateName(first,"CommonStates"));Assert.Equal("Normal",VisualStateManager.GetCurrentStateName(second,"CommonStates"));
        first.Document=new FlowDocument(new Paragraph(new Run("updated")));Layout(first);var ink=new Ink();first.Render(ink);Assert.Contains("updated",ink.Text);ink=new Ink();second.Render(ink);Assert.Contains("second",ink.Text);
        var ownerless=new RichTextBoxView();Layout(ownerless);ink=new Ink();ownerless.Render(ink);Assert.Empty(ink.Text);
        Assert.False(ownerless.Focusable);
    }
    [Fact]
    public void LegacyContentHostCanStillPresentInkAndMissingNamedViewDoesNotCreateAHiddenDefault()
    {
        var box=Create("legacy");var legacy=new ControlTemplate(typeof(RichTextBox));legacy.SetVisualTree(()=>new Grid{Name="PART_ContentHost"});box.Template=legacy;Layout(box);
        var ink=new Ink();box.Render(ink);Assert.Contains("legacy",ink.Text);
        var custom=new ControlTemplate(typeof(RichTextBox));custom.SetVisualTree(()=>new RichTextBoxView{Name="PART_TextView"});box.Template=custom;Layout(box);
        Assert.Same(box,Assert.IsType<RichTextBoxView>(box.FindName("PART_TextView")).Owner);
        ink=new Ink();box.Render(ink);Assert.Contains("legacy",ink.Text);
    }
    private static RichTextBox Create(string text)
    {
        var box=new RichTextBox(new FlowDocument(new Paragraph(new Run(text))));box.Resources.MergedDictionaries.Add(Defaults.Value);return box;
    }
    [Fact]
    public void NullTemplateRestoresTheUnthemedCssDocumentSurface()
    {
        var box=Create("document");Css.SetStyle(box.Document,"line-height:24px");Layout(box);
        var blank=new ControlTemplate(typeof(RichTextBox));blank.SetVisualTree(()=>new Border());box.Template=blank;Layout(box);
        box.Template=null;Layout(box);var ink=new Ink();box.Render(ink);Assert.Contains("document",ink.Text);
    }
    [Fact]
    public void CssDocumentContentBelongsToTheTemplateSurfaceAndKeepsItsTransform()
    {
        var box=Create("CSS body");Css.SetStyle(box.Document,"line-height:24px");Layout(box);
        var view=Assert.IsType<RichTextBoxView>(box.FindName("PART_TextView"));
        Assert.Equal(1,box.VisualChildrenCount);Assert.Equal(1,view.VisualChildrenCount);
        var root=Assert.IsAssignableFrom<FrameworkElement>(box.GetVisualChild(0));root.RenderTransform=new TranslateTransform(17,9);
        var body=Assert.IsAssignableFrom<FrameworkElement>(view.GetVisualChild(0));var position=body.TransformToVisual(box).Transform(new Point(0,0));
        Assert.Equal(box.Padding.Left+box.BorderThickness.Left+17,position.X,3);Assert.Equal(box.Padding.Top+box.BorderThickness.Top+9,position.Y,3);
    }
    [Fact]
    public void RemovingAndRestoringDocumentCssDoesNotLeaveADuplicateSurface()
    {
        var box=Create("transition");Css.SetStyle(box.Document,"line-height:24px");Layout(box);
        var view=Assert.IsType<RichTextBoxView>(box.FindName("PART_TextView"));Assert.Equal(1,view.VisualChildrenCount);
        Css.SetStyle(box.Document,string.Empty);Layout(box);Assert.Equal(0,view.VisualChildrenCount);
        var ink=new Ink();box.Render(ink);Assert.Equal(1,ink.Text.Count(text=>text=="transition"));
        Css.SetStyle(box.Document,"line-height:28px");Layout(box);Assert.Equal(1,view.VisualChildrenCount);ink=new Ink();box.Render(ink);Assert.Equal(1,ink.Text.Count(text=>text=="transition"));
    }
    private static void Layout(FrameworkElement element){for(var i=0;i<3;i++){element.InvalidateMeasure();element.Measure(new Size(320,160));element.Arrange(new Rect(0,0,320,160));element.UpdateLayout();}}
    private sealed class Ink : DrawingContextAdapter
    {
        internal readonly List<string> Text = new();
        public override void DrawText(FormattedText text, Point origin) => Text.Add(text.Text);
        public override void DrawLine(Pen pen, Point a, Point b) { }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rect) { }
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

using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssInputForegroundTests
{
    public CssInputForegroundTests() => CssInputFixtureEnvironment.Reset();

    [Theory]
    [InlineData("text", false)]
    [InlineData("auto", false)]
    [InlineData("number", false)]
    [InlineData("password", false)]
    [InlineData("rich", false)]
    [InlineData("rich-shared", false)]
    [InlineData("text", true)]
    [InlineData("auto", true)]
    [InlineData("number", true)]
    [InlineData("password", true)]
    [InlineData("rich", true)]
    [InlineData("rich-shared", true)]
    public void CssColorPaintsTheComputedForegroundAndTracksLiveChanges(string kind, bool inherited)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, input, source) = Scene(kind);
        var before = source();
        foreach (var value in new[] { "#146342", "rgba(32,83,122,.5)", "transparent" })
        {
            Css.SetStyle(inherited ? root : input, "color:" + value + ";");
            Layout(root);
            AssertPaintMatchesForeground(root, input);
            Assert.False(input.HasLocalValue(Control.ForegroundProperty));
            Assert.Equal(before, source());
        }
    }

    [Theory]
    [InlineData("text")]
    [InlineData("auto")]
    [InlineData("number")]
    [InlineData("rich")]
    [InlineData("rich-shared")]
    public void DefaultThemeFallbackSurvivesCssStyleRemoval(string kind)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, input, _) = Scene(kind);
        var theme = new SolidColorBrush(Color.FromRgb(32,83,122));
        root.Resources["TextPrimary"] = theme;
        Layout(root);
        Assert.Equal(theme.Color, Body(root).Color);
        Css.SetStyle(input, "color:#146342;"); Layout(root);
        AssertPaintMatchesForeground(root, input);
        Css.SetStyle(input, ""); Layout(root);
        Assert.Equal(theme.Color, Body(root).Color);
        var changed = new SolidColorBrush(Color.FromRgb(102,45,112));
        root.Resources["TextPrimary"] = changed; Layout(root);
        Assert.Equal(changed.Color, Body(root).Color);
    }

    [Theory]
    [InlineData("text")]
    [InlineData("auto")]
    [InlineData("number")]
    public void NativeStyleAndInheritedValuesUseTheSamePaintPrecedence(string kind)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, input, _) = Scene(kind);
        var style = new Style(input.GetType());
        style.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(20,99,66))));
        input.Style = style; Layout(root); AssertPaintMatchesForeground(root, input);
        input.Foreground = new SolidColorBrush(Color.FromRgb(102,45,112));
        Css.SetStyle(input, "color:#20537a !important;"); Layout(root);
        AssertPaintMatchesForeground(root, input);
        input.ClearValue(Control.ForegroundProperty); Layout(root); AssertPaintMatchesForeground(root, input);
        Css.SetStyle(input, ""); input.Style = null;
        root.SetValue(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(137,65,18)));
        Layout(root); AssertPaintMatchesForeground(root, input);
    }

    [Fact]
    public void ExplicitCurrentColorCaretAndEmphasisFollowAuthoredInputInk()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, input, _) = Scene("text");
        Css.SetStyle(input, "color:#146342;text-emphasis:'※';caret-color:currentColor;"); Layout(root);
        var capture = new Capture(); root.Render(capture);
        var body = capture.Text.First(row => row.Text == "ABCD");
        Assert.Equal(Color.FromRgb(20,99,66), body.Color);
        Assert.All(capture.Text.Where(row => row.Text == "※"), row => Assert.Equal(body.Color,row.Color));
        var caret = CssCaretColorProperties.Get(input);
        Assert.Equal(body.Color, Assert.IsType<SolidColorBrush>(caret).Color);
    }

    [Theory]
    [InlineData("text")]
    [InlineData("auto")]
    [InlineData("number")]
    [InlineData("password")]
    [InlineData("rich")]
    [InlineData("rich-shared")]
    public void VariablesAndDynamicStatesRepaintWithoutChangingSource(string kind)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, input, source) = Scene(kind); var before=source();
        Css.SetClass(input,"subject");
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(
            ".subject{color:var(--ink);}.subject:read-only{color:#20537a;}.subject:disabled{color:#662d70;}"));
        Css.SetStyle(root,"--ink:#146342;"); Layout(root); AssertPaintMatchesForeground(root,input);
        Assert.Equal(Color.FromRgb(20,99,66),Body(root).Color);
        Css.SetStyle(root,"--ink:#894112;"); Layout(root); Assert.Equal(Color.FromRgb(137,65,18),Body(root).Color);
        if(input is TextBoxBase text) text.IsReadOnly=true; else ((PasswordBox)input).IsReadOnly=true;
        Layout(root); Assert.Equal(Color.FromRgb(32,83,122),Body(root).Color);
        input.IsEnabled=false; Layout(root); Assert.Equal(Color.FromRgb(102,45,112),Body(root).Color);
        input.IsEnabled=true;
        if(input is TextBoxBase editable) editable.IsReadOnly=false; else ((PasswordBox)input).IsReadOnly=false;
        Layout(root); Assert.Equal(Color.FromRgb(137,65,18),Body(root).Color); Assert.Equal(before,source());
    }

    [Theory]
    [InlineData("text")]
    [InlineData("auto")]
    [InlineData("number")]
    [InlineData("password")]
    [InlineData("rich")]
    [InlineData("rich-shared")]
    public void MutableInheritedBrushesFollowReparentingAndReleaseTheOldColor(string kind)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root,input,source)=Scene(kind);var before=source();
        var brush=new SolidColorBrush(Color.FromRgb(20,99,66));root.SetValue(Control.ForegroundProperty,brush);
        Css.SetStyle(input,"color:inherit;");Layout(root);Assert.Equal(brush.Color,Body(root).Color);
        brush.Color=Color.FromRgb(32,83,122);Layout(root);Assert.Equal(brush.Color,Body(root).Color);
        root.Children.Remove(input);var next=new StackPanel{Width=312,Height=120};next.Children.Add(input);
        Css.SetStyle(next,"color:#894112;");Layout(next);Assert.Equal(Color.FromRgb(137,65,18),Body(next).Color);
        brush.Color=Color.FromRgb(102,45,112);Layout(next);Assert.Equal(Color.FromRgb(137,65,18),Body(next).Color);
        Assert.Equal(before,source());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RichDocumentUnderlineUsesTheSameInheritedInkAsItsBody(bool css)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var run=new Run("ABCD");var paragraph=new Paragraph(run);var document=new FlowDocument(paragraph);
        if(css)Css.SetStyle(paragraph,"text-decoration:underline;");else paragraph.TextDecorations=TextDecorations.Underline;
        var input=new RichTextBox{Document=document,Width=312,Height=80,Background=null,Padding=default,BorderThickness=default};
        var root=new StackPanel{Width=312,Height=120};root.Children.Add(input);
        Css.SetStyle(input,"color:#146342;");Layout(root);var capture=new Capture();root.Render(capture);
        Assert.NotEmpty(capture.Lines);Assert.All(capture.Lines,color=>Assert.Equal(Color.FromRgb(20,99,66),color));
        Assert.Same(document,input.Document);Assert.Same(paragraph,run.Parent);
    }

    private static void AssertPaintMatchesForeground(StackPanel root, Control input)
        => Assert.Equal(Assert.IsType<SolidColorBrush>(input.Foreground).Color, Body(root).Color);

    [Theory]
    [InlineData("flex", "document")]
    [InlineData("flex", "paragraph")]
    [InlineData("flex", "run")]
    [InlineData("contents", "document")]
    [InlineData("contents", "paragraph")]
    [InlineData("contents", "run")]
    public void SharedProjectionPreservesDocumentAndInlineColorOverrides(string display, string scope)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, control, source) = Scene("rich-shared");
        var input = Assert.IsType<RichTextBox>(control);
        var paragraph = Assert.Single(input.Document.Blocks.OfType<Paragraph>());
        var run = Assert.Single(paragraph.Inlines.OfType<Run>());
        var before = source();
        var boxStyle = "display:" + display + ";";
        Css.SetStyle(paragraph, boxStyle);
        Css.SetStyle(input, "color:#146342;");
        DependencyObject node = scope == "document" ? input.Document : scope == "paragraph" ? paragraph : run;
        var retainedStyle = scope == "paragraph" ? boxStyle : "";
        Css.SetStyle(node, retainedStyle + "color:#20537a;");
        Layout(root);
        Assert.Equal(Color.FromRgb(32, 83, 122), Body(root).Color);
        Assert.False(node.HasLocalValue(TextElement.ForegroundProperty));
        Css.SetStyle(node, retainedStyle);
        Layout(root);
        Assert.Equal(Color.FromRgb(20, 99, 66), Body(root).Color);
        node.SetValue(TextElement.ForegroundProperty, new SolidColorBrush(Color.FromRgb(137, 65, 18)));
        Layout(root);
        Assert.Equal(Color.FromRgb(137, 65, 18), Body(root).Color);
        node.ClearValue(TextElement.ForegroundProperty);
        Layout(root);
        Assert.Equal(Color.FromRgb(20, 99, 66), Body(root).Color);
        Assert.Equal(before, source());
        Assert.Same(paragraph, run.Parent);
        Assert.Same(input, input.Document.Parent);
    }

    private static (string Text, Color Color) Body(StackPanel root)
    {
        var capture = new Capture(); root.Render(capture);
        var text=capture.Text.Where(row=>row.Text.Contains("ABCD",StringComparison.Ordinal)||row.Text=="12345"||row.Text=="••••").ToArray();
        if(text.Length>0)return Assert.Single(text);
        string Tree(DependencyObject visual)
        {
            var own=visual is FrameworkElement element?visual.GetType().Name+":"+element.RenderSize:visual.GetType().Name;
            if(visual is TextBlock block)own+=" text="+block.Text+" foreground="+block.Foreground;
            if(visual is RichTextBox rich)own+=" ownerInk="+rich.Foreground+" documentInk="+rich.Document.Foreground+" paragraphInk="+rich.Document.Blocks.OfType<Paragraph>().First().Foreground+" documentParent="+rich.Document.Parent?.GetType().Name;
            return own+"["+string.Join(";",Enumerable.Range(0,VisualTreeHelper.GetChildrenCount(visual)).Select(index=>Tree(VisualTreeHelper.GetChild(visual,index))))+"]";
        }
        Assert.True(capture.GlyphInk.Count>0,"No text or glyph ink captured. Text calls: "+string.Join(" | ",capture.Text.Select(row=>row.Text))+" Tree: "+Tree(root));
        var color=capture.GlyphInk[0];Assert.All(capture.GlyphInk,value=>Assert.Equal(color,value));return("",color);
    }

    private static (StackPanel Root, Control Input, Func<string> Source) Scene(string kind)
    {
        var root = new StackPanel { Width=312, Height=120 };
        Control input; Func<string> source;
        if (kind == "text") { var value=new TextBox { Text="ABCD" }; input=value;source=()=>value.Text; }
        else if (kind == "auto") { var value=new AutoCompleteBox { Text="ABCD" }; input=value;source=()=>value.Text; }
        else if (kind == "number") { var value=new NumberBox { Value=12345 }; input=value;source=()=>value.Text; }
        else if (kind == "password") { var value=new PasswordBox { Password="ABCD", PasswordChar='•' }; value.Resources.MergedDictionaries.Add(MenuFlyoutTemplateTestHelper.Themes.Value); input=value;source=()=>value.Password; }
        else { var paragraph=new Paragraph(new Run("ABCD"));if(kind=="rich-shared")Css.SetStyle(paragraph,"display:flex;");var document=new FlowDocument(paragraph);var value=new RichTextBox { Document=document };input=value;source=()=>document.GetText(); }
        input.Width=312;input.Height=80;input.FontFamily=new FontFamily("Georgia");input.FontSize=16;
        input.Background=null;input.BorderThickness=default;input.Padding=default;
        root.Children.Add(input);Layout(root);return(root,input,source);
    }

    private static void Layout(FrameworkElement root)
    {
        for(var pass=0;pass<6;pass++)
        {
            CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
            root.Measure(new(312,120));root.Arrange(new(0,0,312,120));root.UpdateLayout();
        }
    }

    private sealed class Capture : DrawingContextAdapter,IOffsetDrawingContext
    {
        public Point Offset{get;set;}
        internal readonly List<(string Text, Color Color)> Text=[];
        internal readonly List<Color> Lines=[];
        internal readonly List<Color> GlyphInk=[];
        public override void DrawGlyphRun(Brush? foreground,GlyphRun glyphRun)
        { if(foreground is SolidColorBrush brush)GlyphInk.Add(brush.Color); }
        public override void DrawText(FormattedText text, Point point)
        { if(text.Foreground is SolidColorBrush brush) Text.Add((text.Text,brush.Color)); }
        public override void DrawRectangle(Brush? b,Pen? p,Rect r){} public override void DrawGeometry(Brush? b,Pen? p,Geometry g){}
        public override void DrawLine(Pen p,Point a,Point b){if(p.Brush is SolidColorBrush brush)Lines.Add(brush.Color);} public override void DrawRoundedRectangle(Brush? b,Pen? p,Rect r,double x,double y){}
        public override void DrawEllipse(Brush? b,Pen? p,Point q,double x,double y){} public override void DrawImage(ImageSource i,Rect r){}
        public override void DrawBackdropEffect(Rect r,IBackdropEffect e,CornerRadius c){} public override void PushClip(Geometry g){}
        public override void PushTransform(Transform t){} public override void PushOpacity(double d){} public override void Pop(){} public override void Close(){}
    }
}

// These fixtures exercise CSS input defaults. Other serial UI tests can leave an
// Application/native control theme behind, changing minimum sizes and password
// masks. Reset only the test environment before creating a CSS scene.
internal static class CssInputFixtureEnvironment
{
    internal static void Reset()
    {
        AccessTextTemplateTestHelper.Register();
    }
}

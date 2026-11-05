using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Effects;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssSelectionContextTests
{
    private static readonly Color Ordinary = Color.FromRgb(35,60,50);
    private static readonly Color Cream = Color.FromRgb(255,248,222);
    private const string Source = "Alpha affinity continues through another line of ordinary text.";
    private static (StackPanel Root, TextBlock Text) Scene(string pseudo, string paint, string extra = "")
    {
        var root = new StackPanel(); Css.SetClass(root,"parent");
        Css.SetStyle(root,"display:block;font-family:Georgia;font-size:16px;line-height:24px;color:#233c32;");
        var text = new TextBlock { Text = Source, IsTextSelectionEnabled = true }; Css.SetClass(text,"subject");
        Css.SetStyle(text,"display:block;white-space:pre-wrap;"); root.Children.Add(text);
        var rules = ".subject" + pseudo + "{color:green;" + (pseudo == "::first-line" ? "font-size:20px;" : "font-size:30px;") + "}" +
            ".subject::selection{" + paint + "}" + extra;
        var sheet = CssStyleSheet.Parse(rules); Assert.Empty(sheet.Diagnostics); Css.GetStyleSheets(root).Add(sheet);
        Layout(root); text.SelectAll(); return (root,text);
    }
    private static void Layout(FrameworkElement root)
    {
        for(var pass=0;pass<6;pass++) { CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); root.Measure(new(260,240)); root.Arrange(new(0,0,260,240)); root.UpdateLayout(); }
    }
    private static Capture Draw(TextBlock text) { var capture = new Capture(); text.Render(capture); return capture; }

    [Theory, InlineData("::first-line"), InlineData("::first-letter")]
    public void CurrentColorPaintFollowsTheInterveningTypography(string pseudo)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root,text) = Scene(pseudo,"color:currentColor;background-color:currentColor;text-shadow:2px 3px 0 currentColor;text-decoration:underline currentColor;text-decoration-skip-inset:none;");
        var sourceForeground = text.Foreground; var sourceSize = text.RenderSize; var paint = Draw(text);
        Assert.Contains(Colors.Green,paint.Fills); Assert.Contains(Colors.Green,paint.Strokes);
        Assert.Contains(paint.Shadows,shadow => shadow.Color==Colors.Green && Math.Abs(shadow.OffsetX-2)<.001 && Math.Abs(shadow.OffsetY-3)<.001);
        Assert.Same(sourceForeground,text.Foreground); Assert.Equal(sourceSize,text.RenderSize); Assert.Equal(Source,text.SelectedText); Assert.Equal(Source,text.Text);
        Layout(root); Assert.Equal(sourceSize,text.RenderSize);
    }
    [Theory, InlineData("::first-line"), InlineData("::first-letter")]
    public void ColorExpressionsUseTheActualLowerHighlightColor(string pseudo)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (_,text)=Scene(pseudo,"color:color-mix(in srgb,currentColor 50%,red 50%);background:color-mix(in srgb,currentColor 50%,blue 50%);text-shadow:2px 3px 0 currentColor;text-decoration:underline;text-decoration-color:color-mix(in srgb,currentColor 50%,white 50%);text-decoration-skip-inset:none;");
        var paint=Draw(text);var foreground=Color.FromRgb(128,64,0);
        Assert.Contains(paint.Ink,c=>c.ToArgb()==foreground.ToArgb());Assert.Contains(paint.Fills,c=>c.ToArgb()==Color.FromRgb(64,32,128).ToArgb());
        // Color functions retain fractional channels between nested mixes:
        // (127.5 + 255) / 2 is 191.25, without an intermediate byte rounding.
        Assert.Contains(paint.Strokes,c=>Near(c,Color.FromRgb(191,160,128)));
        Assert.Contains(paint.Shadows,s=>Near(s.Color,foreground));
        Assert.Equal(Ordinary,Assert.IsType<SolidColorBrush>(text.Foreground).Color);
    }
    [Fact]
    public void InheritedShadowKeepsParentLengthsAndUsesTheChildsHighlightColor()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root,text)=Scene("::first-line","color:#fff8de;", ".parent::selection{text-shadow:.5em .25em 0 currentColor;} .subject{font-size:32px;}");
        var paint=Draw(text);Assert.Contains(paint.Shadows,s=>s.Color==Cream&&Math.Abs(s.OffsetX-8)<.001&&Math.Abs(s.OffsetY-4)<.001);
        Assert.Equal(32,text.FontSize);Assert.Equal(Source,text.SelectedText);Assert.True(root.IsArrangeValid);
    }
    [Fact]
    public void InheritedDecorationExpressionUsesTheChildHighlightColor()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (_,text)=Scene("::first-line","color:#fff8de;", ".parent::selection{text-decoration:underline;text-decoration-color:color-mix(in srgb,currentColor 50%,red 50%);text-decoration-skip-inset:none;}");
        Assert.Contains(Draw(text).Strokes,c=>c.ToArgb()==Color.FromRgb(255,124,111).ToArgb());
    }
    [Fact]
    public void AChangedTypographicColorRefreshesEveryPaintChannel()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root,text)=Scene("::first-line","color:currentColor;background:currentColor;text-shadow:2px 3px 0 currentColor;text-decoration:underline currentColor;text-decoration-skip-inset:none;",
            ".subject.changed::first-line{color:blue;}");
        var before=Draw(text);Assert.Contains(before.Shadows,s=>s.Color==Colors.Green);
        Css.SetClass(text,"subject changed");Layout(root);var after=Draw(text);
        Assert.Contains(Colors.Blue,after.Fills);Assert.Contains(Colors.Blue,after.Strokes);Assert.Contains(after.Shadows,s=>s.Color==Colors.Blue);
        Assert.Equal(Source,text.SelectedText);Assert.Equal(Ordinary,Assert.IsType<SolidColorBrush>(text.Foreground).Color);
    }
    [Fact]
    public void ExplicitHighlightColorOverridesTheUnderlyingPseudoColor()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (_,text)=Scene("::first-line","color:#fff8de;background:currentColor;text-shadow:2px 3px 0 currentColor;text-decoration:underline currentColor;text-decoration-skip-inset:none;");
        var paint=Draw(text);Assert.Contains(Cream,paint.Fills);Assert.Contains(Cream,paint.Strokes);Assert.All(paint.Shadows,s=>Assert.Equal(Cream,s.Color));
    }
    [Fact]
    public void InheritedBackgroundExpressionUsesTheChildHighlightColor()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (_,text)=Scene("::first-line","color:#fff8de;", ".parent::selection{background:color-mix(in srgb,currentColor 50%,blue 50%);}");
        Assert.Contains(Draw(text).Fills,c=>Near(c,Color.FromRgb(128,124,239)));
    }
    [Fact]
    public void ReplacingAnInheritedShadowReleasesItsPreviousFormula()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root,text)=Scene("::first-line","color:#fff8de;", ".parent::selection{text-shadow:.5em .25em 0 currentColor;}");
        Assert.NotEmpty(Draw(text).Shadows);
        Css.GetStyleSheets(root).Clear();Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(".subject::selection{color:#fff8de;background:currentColor;text-shadow:none;}"));Layout(root);
        var paint=Draw(text);Assert.Empty(paint.Shadows);Assert.Contains(Cream,paint.Fills);Assert.Equal(Source,text.SelectedText);
    }
    [Fact]
    public void FragmentBackgroundsArePaintedBeforeAnyHighlightShadow()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root,_)=Scene("::first-line","color:currentColor;background:currentColor;text-shadow:2px 3px 0 currentColor;");
        var paint=new Capture();root.Render(paint);
        Assert.Contains("shadow",paint.VisibleOrder);
        Assert.True(paint.VisibleOrder.LastIndexOf("background")<paint.VisibleOrder.IndexOf("shadow"),string.Join(",",paint.VisibleOrder));
    }
    [Fact]
    public void OrdinaryShadowsKeepNativeSelectionPaintOrderWithoutAHighlightStyle()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root,_)=Scene("::first-line","", ".subject::first-line{text-shadow:2px 3px 0 currentColor;}");
        var paint=new Capture();root.Render(paint);
        Assert.Contains("shadow",paint.VisibleOrder);
        Assert.True(paint.VisibleOrder.IndexOf("background")<paint.VisibleOrder.IndexOf("shadow"),string.Join(",",paint.VisibleOrder));
    }
    [Theory]
    [InlineData("text",false),InlineData("text",true),InlineData("password",false),InlineData("password",true)]
    [InlineData("rich-native",false),InlineData("rich-native",true),InlineData("rich-shared",false),InlineData("rich-shared",true)]
    public void ShadowAndDecorationUseTheEffectiveSelectedForeground(string kind,bool authorColor)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        const string value="Office affinity";var root=new StackPanel();FrameworkElement editor;Action select;
        if(kind=="password")
        {
            var password=new PasswordBox{Password=value,SelectionTextBrush=new SolidColorBrush(Cream),IsInactiveSelectionHighlightEnabled=true};editor=password;select=()=>password.SelectAll();
        }
        else if(kind=="text")
        {
            var input=new TextBox{Text=value,SelectionTextBrush=new SolidColorBrush(Cream),IsInactiveSelectionHighlightEnabled=true};editor=input;select=()=>input.SelectAll();
        }
        else
        {
            var run=new Run(value);var paragraph=new Paragraph(run);var document=new FlowDocument(paragraph);
            var rich=new RichTextBox{Document=document,SelectionTextBrush=new SolidColorBrush(Cream),IsInactiveSelectionHighlightEnabled=true,IsReadOnly=true};editor=rich;
            select=()=>rich.Selection.Select(run.ContentStart,run.ContentEnd);
            if(kind=="rich-shared")Css.SetStyle(paragraph,"display:flex;");
        }
        var control=(Control)editor;control.Foreground=new SolidColorBrush(Ordinary);control.BorderThickness=default;control.Padding=default;editor.Width=260;editor.Height=72;
        root.Children.Add(editor);Css.SetClass(editor,"editor");Css.SetStyle(root,"font-family:Georgia;font-size:16px;line-height:24px;color:#233c32;");
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(".editor::selection{"+(authorColor?"color:green;":"")+"text-shadow:2px 3px 0 currentColor;text-decoration:underline currentColor;text-decoration-skip-inset:none;}"));
        Layout(root);select();var paint=new Capture();editor.Render(paint);var expected=authorColor?Colors.Green:Cream;
        Assert.Contains(paint.Shadows,shadow=>Near(shadow.Color,expected));Assert.Contains(paint.Strokes,color=>Near(color,expected));
        Assert.Contains(paint.Ink,color=>Near(color,expected));Assert.Equal(Ordinary,Assert.IsType<SolidColorBrush>(control.Foreground).Color);
        if(editor is PasswordBox masked)Assert.Equal(value,masked.Password);
        else if(editor is TextBox input)Assert.Equal(value,input.Text);
        else Assert.Equal(value,((RichTextBox)editor).Document.GetText().TrimEnd('\n'));
    }
    [Fact]
    public void HiddenTypographyDoesNotPaintAnEarlySelectionLayer()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root,text)=Scene("::first-line","color:#fff8de;background:#20537a;text-shadow:2px 3px 0 currentColor;",".subject{visibility:hidden;}.subject.shown{visibility:visible;}");
        var paint=new Capture();root.Render(paint);
        Assert.Empty(paint.Fills);Assert.Empty(paint.Shadows);Assert.Empty(paint.Ink);Assert.Equal(Source,text.SelectedText);
        var size=text.RenderSize;
        for(var pass=0;pass<3;pass++)
        {
            Css.SetClass(text,"subject shown");Layout(root);paint=new Capture();root.Render(paint);
            Assert.Contains(Color.FromRgb(32,83,122),paint.Fills);Assert.Contains(paint.Shadows,shadow=>Near(shadow.Color,Cream));Assert.Equal(size,text.RenderSize);
            Css.SetClass(text,"subject");Layout(root);paint=new Capture();root.Render(paint);
            Assert.Empty(paint.Fills);Assert.Empty(paint.Shadows);Assert.Empty(paint.Ink);Assert.Equal(Source,text.SelectedText);Assert.Equal(size,text.RenderSize);
        }
    }
    [Fact]
    public void AThrowingBackgroundRestoresTheFragmentPaintScope()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root,_)=Scene("::first-line","color:#fff8de;background:#20537a;");
        var sources=new List<TextBlock>();
        void Find(Visual visual)
        {
            if(visual is TextBlock text&&text.HasInlineSelection)sources.Add(text);
            for(var i=0;i<VisualTreeHelper.GetChildrenCount(visual);i++)if(VisualTreeHelper.GetChild(visual,i)is Visual child)Find(child);
        }
        Find(root);Assert.True(sources.Count>=2);
        try
        {
            Assert.All(sources,source=>Assert.Null(CssInlineSelectionPainter.Overlay(source)));
            Assert.Throws<PaintFailure>(()=>root.Render(new Capture{FailBackground=true}));
            Assert.All(sources,source=>
            {
                Assert.Null(CssInlineSelectionPainter.Overlay(source));
                Assert.False(CssInlineSelectionPainter.Suppressed(source));Assert.False(CssInlineSelectionPainter.ShadowsSuppressed(source));
            });
        }
        finally
        {
            // Keep the deliberately failing pre-fix control from retaining this
            // test's sources in the thread-local state used by later tests.
            var field=typeof(CssInlineSelectionPainter).GetField("s_overlays",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
            if(field?.GetValue(null)is System.Collections.IDictionary overlays)foreach(var source in sources)overlays.Remove(source);
        }
    }
    private sealed class PaintFailure:Exception { }
    [Theory,InlineData(true),InlineData(false)]
    public void AThrowingGlyphRestoresEffectsAndFragmentState(bool duringCapture)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root,_)=Scene("::first-line","color:#fff8de;background:#20537a;text-shadow:2px 3px 0 currentColor;");
        var sources=new List<TextBlock>();
        void Find(Visual visual)
        {
            if(visual is TextBlock text&&text.HasInlineSelection)sources.Add(text);
            for(var i=0;i<VisualTreeHelper.GetChildrenCount(visual);i++)if(VisualTreeHelper.GetChild(visual,i)is Visual child)Find(child);
        }
        Find(root);Assert.True(sources.Count>=2);var paint=new Capture{FailInk=true,FailDuringCapture=duringCapture};
        Assert.Throws<PaintFailure>(()=>root.Render(paint));Assert.Equal(0,paint.CaptureDepth);
        Assert.All(sources,source=>
        {
            Assert.Null(CssInlineSelectionPainter.Overlay(source));Assert.False(CssInlineSelectionPainter.Suppressed(source));Assert.False(CssInlineSelectionPainter.ShadowsSuppressed(source));
        });
        var retry=new Capture();root.Render(retry);Assert.Contains(Cream,retry.Ink);Assert.Contains(retry.Shadows,shadow=>Near(shadow.Color,Cream));
    }
    [Theory,InlineData(false,312),InlineData(false,600),InlineData(true,312),InlineData(true,600)]
    public void EditorUnderlineFollowsTheTextBaselineWhenRowHeightIsRounded(bool password,int width)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var root=new StackPanel();FrameworkElement editor=password
            ?new PasswordBox{Password="Office",SelectionTextBrush=new SolidColorBrush(Cream),IsInactiveSelectionHighlightEnabled=true}
            :new TextBox{Text="Office",SelectionTextBrush=new SolidColorBrush(Cream),IsInactiveSelectionHighlightEnabled=true};
        var control=(Control)editor;control.Foreground=new SolidColorBrush(Ordinary);control.BorderThickness=default;control.Padding=default;control.Width=width;control.Height=72;
        root.Children.Add(editor);Css.SetClass(editor,"editor");Css.SetStyle(root,"font-family:Georgia;font-size:16px;line-height:24px;");
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(".editor::selection{text-decoration:underline currentColor;text-decoration-thickness:1px;text-underline-offset:2px;text-decoration-skip-inset:none;}"));
        Layout(root);if(editor is PasswordBox secret)secret.SelectAll();else((TextBox)editor).SelectAll();
        var paint=new Capture();editor.Render(paint);Assert.NotEmpty(paint.Baselines);
        var strokes=paint.Lines.Where(line=>Near(line.Color,Cream)).ToArray();Assert.NotEmpty(strokes);
        foreach(var line in strokes)Assert.Equal(paint.Baselines[0]+2.5,line.Start.Y,6);
    }
    // CSS color math retains fractions through nested functions; conversion
    // through float linear RGB may round a half-byte to either adjacent byte.
    private static bool Near(Color a,Color b)=>a.A==b.A&&Math.Abs(a.R-b.R)<=1&&Math.Abs(a.G-b.G)<=1&&Math.Abs(a.B-b.B)<=1;

    private sealed class Capture : DrawingContextAdapter, IOffsetDrawingContext, IEffectDrawingContext
    {
        internal bool FailBackground;
        internal bool FailInk;internal bool FailDuringCapture;internal int CaptureDepth=>_captureDepth;
        public Point Offset { get; set; }
        public bool SupportsCssTextShadowsOnly => true;
        internal readonly List<Color> Ink=[];internal readonly List<Color> Fills=[];internal readonly List<Color> Strokes=[];internal readonly List<DropShadowEffect> Shadows=[];
        internal readonly List<double> Baselines=[];internal readonly List<(Point Start,Point End,Color Color)> Lines=[];
        internal readonly List<string> VisibleOrder=[];private int _captureDepth;
        private void Brush(Brush? brush,List<Color> list){if(brush is SolidColorBrush b&&b.Color.A>0)list.Add(b.Color);}
        private void Effect(IEffect effect)
        {
            if(effect is DropShadowEffect shadow)Shadows.Add(shadow);
            else if(effect is EffectGroup group)foreach(var child in group.Children)Effect(child);
        }
        public override void DrawText(FormattedText text,Point origin)
        {
            if(FailInk&&(_captureDepth>0)==FailDuringCapture)throw new PaintFailure();
            Brush(text.Foreground,Ink);
            var metrics=TextMeasurement.GetFontMetrics(text.FontFamily,text.FontSize,text.FontWeight,text.FontStyle);
            Baselines.Add(origin.Y+Offset.Y+(text.IsMeasured?text.Baseline:metrics.Ascent));
        }
        private void Fill(Brush? brush)
        {
            Brush(brush,Fills);
            if(_captureDepth==0&&brush is SolidColorBrush {Color.A:>0})
            {
                if(FailBackground)throw new PaintFailure();VisibleOrder.Add("background");
            }
        }
        public override void DrawRectangle(Brush? brush,Pen? pen,Rect rect){Fill(brush);Brush(pen?.Brush,Strokes);}
        public override void DrawGeometry(Brush? brush,Pen? pen,Geometry geometry){Fill(brush);Brush(pen?.Brush,Strokes);}
        public override void DrawLine(Pen pen,Point a,Point b)
        {
            Brush(pen.Brush,Strokes);if(pen.Brush is SolidColorBrush color)Lines.Add((new(a.X+Offset.X,a.Y+Offset.Y),new(b.X+Offset.X,b.Y+Offset.Y),color.Color));
        }
        public override void DrawRoundedRectangle(Brush? brush,Pen? pen,Rect rect,double x,double y){Brush(brush,Fills);Brush(pen?.Brush,Strokes);}
        public override void DrawEllipse(Brush? brush,Pen? pen,Point center,double x,double y){Brush(brush,Fills);Brush(pen?.Brush,Strokes);}
        public override void DrawImage(ImageSource image,Rect rect) { }
        public override void DrawBackdropEffect(Rect rect,IBackdropEffect effect,CornerRadius radii) { }
        public override void PushClip(Geometry geometry) { }public override void PushTransform(Transform transform) { }public override void PushOpacity(double opacity) { }public override void Pop() { }public override void Close() { }
        public void BeginEffectCapture(float x,float y,float w,float h)=>_captureDepth++;public void EndEffectCapture()=>_captureDepth--;
        public void ApplyCssTextShadowsOnly(IEffect effect,float x,float y,float w,float h,float ox,float oy){Effect(effect);if(_captureDepth==0)VisibleOrder.Add("shadow");}
        public void ApplyElementEffect(IEffect effect,float x,float y,float w,float h,float ox=0,float oy=0,float tl=0,float tr=0,float br=0,float bl=0){Effect(effect);if(_captureDepth==0)VisibleOrder.Add("shadow");}
    }
}

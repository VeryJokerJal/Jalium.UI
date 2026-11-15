using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssEditorEmphasisTests
{
    [Theory]
    [InlineData("text")]
    [InlineData("password")]
    [InlineData("rich")]
    [InlineData("rich-shared")]
    public void EditorsRenderInheritedMarksAndRetainTheirOriginalSource(string kind)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var(root,control,source)=Scene(kind,"ABCD");var before=source();
        var capture=new Capture();root.Render(capture);var marks=capture.Text.Where(row=>row.Value=="※").ToArray();
        Assert.Equal(4,marks.Length);Assert.All(marks,row=>Assert.Equal(Color.FromRgb(20,99,66),row.Color));Assert.Equal(before,source());
        Css.SetStyle(control,"text-emphasis:none;");Layout(root);capture=new Capture();root.Render(capture);
        Assert.DoesNotContain(capture.Text,row=>row.Value=="※");Assert.Equal(before,source());
    }

    [Fact]
    public void InputAnnotationSpaceIsIncludedInCaretAndWrappedRowGeometry()
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var(root,control,_)=Scene("text","One ordinary line\nAnother source line");var text=(TextBox)control;text.TextWrapping=TextWrapping.Wrap;
        Css.SetStyle(text,"text-emphasis:none;");Layout(root);var naturalFirst=text.GetRectFromCharacterIndex(0);var naturalNext=text.GetRectFromCharacterIndex(18);
        Css.SetStyle(text,"text-emphasis:'※' #146342;text-emphasis-position:over right;");Layout(root);var first=text.GetRectFromCharacterIndex(0);var next=text.GetRectFromCharacterIndex(18);
        Assert.True(first.Top>naturalFirst.Top);Assert.True(next.Top-first.Top>naturalNext.Top-naturalFirst.Top);Assert.Equal(naturalFirst.Left,first.Left);
        var paint=new Capture();root.Render(paint);var body=paint.Text.First(row=>row.Value.StartsWith("One ordinary",StringComparison.Ordinal));var mark=paint.Text.First(row=>row.Value=="※");Assert.True(mark.Point.Y<body.Point.Y);
    }

    [Theory]
    [InlineData("over right")]
    [InlineData("under left")]
    public void WrappedInputCaretHitTestingAndImeRangesShareTheRenderedRows(string position)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        const string value = "ordinary words fill several rows with another source line";
        var (root, control, _) = Scene("text", value);
        var input = (TextBox)control;
        input.Width = 134;
        input.TextWrapping = TextWrapping.Wrap;
        Css.SetStyle(input, "text-emphasis:none;"); Layout(root);
        var natural = Enumerable.Range(0, value.Length).Select(input.GetRectFromCharacterIndex).ToArray();
        Css.SetStyle(input, "text-emphasis:'※' #146342;text-emphasis-position:" + position + ";"); Layout(root);
        Assert.True(natural.Select(rect => rect.Y).Distinct().Count() > 2);
        for (var index = 0; index < value.Length; index++)
        {
            var caret = input.GetRectFromCharacterIndex(index);
            Assert.Equal(natural[index].X, caret.X);
            Assert.Equal(natural[index].Height, caret.Height);
            Assert.True(((IImeSupport)input).TryGetImeTextRangeGeometry(index, 1, false, out var range));
            Assert.InRange(Math.Abs(caret.Top - range.Rectangle.Top), 0, 1);
            if (value[index] == ' ') continue;
            var hit = input.GetCharacterIndexFromPoint(new Point(caret.X + .1, caret.Y + caret.Height / 2), true);
            Assert.Equal(index, hit);
        }
        var first = input.GetRectFromCharacterIndex(0);
        var last = input.GetRectFromCharacterIndex(value.Length - 1);
        Assert.True(last.Y - first.Y > natural[^1].Y - natural[0].Y);
        Assert.Equal(position.StartsWith("over", StringComparison.Ordinal), first.Y > natural[0].Y);
    }

    [Theory]
    [InlineData("text", 4)]
    [InlineData("rich", 4)]
    [InlineData("rich-shared", 4)]
    [InlineData("password", 11)]
    public void CompleteGraphemesAndOpaquePasswordUnitsArePreserved(string kind, int expected)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        const string value = "Ae\u0301 😀汉、。 \u200b";
        var (root, control, source) = Scene(kind, value);
        var capture = new Capture(); root.Render(capture);
        Assert.Equal(expected, capture.Text.Count(row => row.Value == "※"));
        Assert.Equal(value, source());
        if (control is PasswordBox password)
        {
            password.IsPasswordRevealed = true; Layout(root);
            capture = new Capture(); root.Render(capture);
            Assert.Equal(4, capture.Text.Count(row => row.Value == "※"));
            Assert.Equal(value, password.Password);
        }
    }

    [Theory]
    [InlineData("text")]
    [InlineData("password")]
    [InlineData("rich")]
    [InlineData("rich-shared")]
    public void EmptyAndZeroSizeEditorsDoNotCreateAnnotationInk(string kind)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, control, source) = Scene(kind, "");
        var capture = new Capture(); root.Render(capture);
        Assert.DoesNotContain(capture.Text, row => row.Value == "※");
        var (zeroRoot, zeroControl, zeroSource) = Scene(kind, "AB");
        Css.SetStyle(zeroControl, "font-size:0;text-emphasis:'※';"); Layout(zeroRoot);
        capture = new Capture(); zeroRoot.Render(capture);
        Assert.DoesNotContain(capture.Text, row => row.Value == "※"); Assert.Equal("AB", zeroSource());
    }

    [Fact]
    public void SelectionAndScrollingIncludeAnnotationSpaceWithoutChangingText()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var value = string.Join('\n', Enumerable.Repeat("ABCD", 20));
        var (root, control, source) = Scene("text", value); var input = (TextBox)control;
        input.Select(0, 4); var capture = new Capture(); root.Render(capture);
        var selection = Assert.Single(capture.Rectangles.Where(rect => rect.Width > 1));
        var caret = input.GetRectFromCharacterIndex(0);
        Assert.True(selection.Height > caret.Height, $"selection={selection} caret={caret}");
        Assert.True(selection.Top < caret.Top, $"selection={selection} caret={caret}");
        input.ScrollToLine(19); var last = input.GetRectFromCharacterIndex(value.Length - 1);
        Assert.True(last.Top < input.Height); Assert.True(input.VerticalOffset > 0);
        Assert.Equal(value, source());
    }

    [Fact]
    public void WrappedCompositionUsesTheSameAnnotationPitchAndCanBeCancelled()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, control, _) = Scene("text", "ABCD"); var input = (TextBox)control;
        input.Width = 134; input.TextWrapping = TextWrapping.Wrap; Layout(root);
        const string composition = "ordinary words fill more than one composition row";
        var ime = (IImeSupport)input; ime.OnImeCompositionStart(); ime.OnImeCompositionUpdate(composition, 0);
        var anchor = input.GetRectFromCharacterIndex(0); var tops = new List<double>();
        for (var offset = 0; offset < composition.Length;)
        {
            Assert.True(ime.TryGetImeTextRangeGeometry(offset, composition.Length - offset, true, out var range));
            Assert.True(range.Length > 0); tops.Add(range.Rectangle.Top); offset += range.Length;
            var point = new Point(range.Rectangle.Left + .1, range.Rectangle.Top + range.Rectangle.Height / 2);
            Assert.True(ime.TryGetImeCharacterIndex(point, true, out var index)); Assert.Equal(range.Start, index);
        }
        Assert.True(tops.Count > 2); Assert.Equal(anchor.Top, tops[0]);
        var capture = new Capture(); root.Render(capture);
        Assert.Equal(4 + composition.Count(char.IsLetter), capture.Text.Count(row => row.Value == "※"));
        ime.OnImeCompositionEnd(null); Assert.Equal("ABCD", input.Text);
    }

    [Theory]
    [InlineData("text", "over right", "underline")]
    [InlineData("text", "over right", "overline")]
    [InlineData("text", "over right", "line-through")]
    [InlineData("text", "under left", "underline")]
    [InlineData("text", "under left", "overline")]
    [InlineData("text", "under left", "line-through")]
    [InlineData("password", "over right", "underline")]
    [InlineData("password", "over right", "overline")]
    [InlineData("password", "over right", "line-through")]
    [InlineData("password", "under left", "underline")]
    [InlineData("password", "under left", "overline")]
    [InlineData("password", "under left", "line-through")]
    public void SelectionDecorationsFollowTheBodyBaseline(string kind, string position, string line)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, control, _) = Scene(kind, "ABCD"); Css.SetClass(control, "subject");
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(
            ".subject::selection{text-decoration:" + line + " #20537a;text-decoration-thickness:1px;text-underline-offset:0;}"));
        if (control is TextBox input) input.SelectAll(); else ((PasswordBox)control).Select(0, 4);
        Css.SetStyle(control, "text-emphasis:none;"); Layout(root);
        var natural = new Capture(); root.Render(natural); Assert.NotEmpty(natural.Lines);
        Css.SetStyle(control, "text-emphasis:'※';text-emphasis-position:" + position + ";"); Layout(root);
        var annotated = new Capture(); root.Render(annotated); Assert.NotEmpty(annotated.Lines);
        var difference = annotated.Lines.Min(y => y) - natural.Lines.Min(y => y);
        Assert.Equal(position.StartsWith("over", StringComparison.Ordinal), difference > 0);
        if (control is TextBox text)
            Assert.Equal(text.GetRectFromCharacterIndex(0).Top, difference, precision: 4);
    }

    [Theory]
    [InlineData("rich")]
    [InlineData("rich-shared")]
    public void RichEditorReplacementAndUndoKeepAnnotationsOutOfTheDocument(string kind)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, control, _) = Scene(kind, "ABCD"); var input = (RichTextBox)control;
        Assert.True(((IImeSupport)input).TryReplaceImeText(1, 2, "xy")); Layout(root);
        Assert.Equal("AxyD", input.Document.GetText().TrimEnd('\n'));
        var capture = new Capture(); root.Render(capture);
        Assert.Equal(4, capture.Text.Count(row => row.Value == "※"));
        Assert.True(input.Undo()); Layout(root);
        Assert.Equal("ABCD", input.Document.GetText().TrimEnd('\n'));
    }

    [Fact]
    public void PasswordMaskFontChangesKeepCaretAndAnnotationGeometryTogether()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, control, _) = Scene("password", "ABCD"); var password = (PasswordBox)control;
        foreach (var style in new[] { "font-weight:700;font-style:italic;", "font-weight:400;font-style:normal;" })
        {
            Css.SetStyle(password, style + "text-emphasis:'※' #146342;"); Layout(root);
            password.Select(4, 0);
            var caret = ((IImeSupport)password).GetImeCaretRectangle();
            var mask = new FormattedText(new string(password.PasswordChar, 4), password.FontFamily.GetRenderingSource(password), password.FontSize)
            { FontWeight = password.FontWeight.ToOpenTypeWeight(), FontStyle = password.FontStyle.ToOpenTypeStyle() };
            TextMeasurement.MeasureText(mask); Assert.Equal(Math.Round(mask.Width), caret.X);
            var capture = new Capture(); root.Render(capture);
            var body = capture.Text.First(row => row.Value == mask.Text);
            Assert.Equal(mask.FontWeight, body.Weight); Assert.Equal(mask.FontStyle, body.Style);
            Assert.Equal(4, capture.Text.Count(row => row.Value == "※"));
        }
    }

    private static(StackPanel Root,Control Control,Func<string> Source) Scene(string kind,string value)
    {
        var root=new StackPanel{Width=312,Height=220};Css.SetStyle(root,"display:block;font-family:Georgia;font-size:16px;line-height:normal;color:#233c32;");
        Control control;Func<string> source;
        if(kind=="text")
        {var input=new TextBox{Text=value,Width=312,Height=180,Background=null,BorderThickness=default,Padding=default,IsInactiveSelectionHighlightEnabled=true};control=input;source=()=>input.Text;}
        else if(kind=="password")
        {var input=new PasswordBox{Password=value,Width=312,Height=180,Background=null,BorderThickness=default,Padding=default,IsInactiveSelectionHighlightEnabled=true};var template=new ControlTemplate(typeof(PasswordBox));template.SetVisualTree(()=>new PasswordBoxView{Name="PART_TextView",Owner=input});input.Template=template;control=input;source=()=>input.Password;}
        else
        {
            var run=new Run(value);var paragraph=new Paragraph(run);var document=new FlowDocument(paragraph);var input=new RichTextBox{Document=document,Width=312,Height=180,Background=null,BorderThickness=default,Padding=default};
            if(kind=="rich-shared")Css.SetStyle(paragraph,"display:flex;");control=input;source=()=>{Assert.Same(document,input.Document);Assert.Same(paragraph,run.Parent);return document.GetText().TrimEnd('\n');};
        }
        root.Children.Add(control);Css.SetStyle(control,"text-emphasis:'※' #146342;text-emphasis-position:over right;");Layout(root);return(root,control,source);
    }
    private static void Layout(FrameworkElement root)
    {for(var pass=0;pass<6;pass++){CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);root.Measure(new(312,220));root.Arrange(new(0,0,312,220));root.UpdateLayout();}}
    private sealed class Capture:DrawingContextAdapter,IOffsetDrawingContext
    {
        public Point Offset{get;set;}internal readonly List<(string Value,double Size,Color Color,Point Point,int Weight,int Style)>Text=[];
        internal readonly List<Rect> Rectangles=[];
        internal readonly List<double> Lines=[];
        public override void DrawText(FormattedText text,Point point){if(text.Foreground is SolidColorBrush brush)Text.Add((text.Text,text.FontSize,brush.Color,new(point.X+Offset.X,point.Y+Offset.Y),text.FontWeight,text.FontStyle));}
        public override void DrawRectangle(Brush? b,Pen? p,Rect r){if(b is not null && p is null)Rectangles.Add(new Rect(r.X+Offset.X,r.Y+Offset.Y,r.Width,r.Height));}public override void DrawGeometry(Brush? b,Pen? p,Geometry g){}public override void DrawLine(Pen p,Point a,Point b){Lines.Add(a.Y+Offset.Y);}
        public override void DrawRoundedRectangle(Brush? b,Pen? p,Rect r,double x,double y){}public override void DrawEllipse(Brush? b,Pen? p,Point q,double x,double y){}
        public override void DrawImage(ImageSource i,Rect r){}public override void DrawBackdropEffect(Rect r,IBackdropEffect e,CornerRadius c){}
        public override void PushClip(Geometry g){}public override void PushTransform(Transform t){}public override void PushOpacity(double d){}public override void Pop(){}public override void Close(){}
    }
}

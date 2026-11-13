using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssFirstLineTests
{
    [Theory]
    [InlineData("TextBlock::first-line", true)]
    [InlineData(".body:first-line", true)]
    [InlineData("StackPanel > .body:hover::first-line", true)]
    [InlineData(".body::first-line > Run", false)]
    [InlineData(".body::first-line:hover", false)]
    [InlineData(".body::first-line::first-letter", false)]
    [InlineData(":is(::first-line)", true)]
    [InlineData(".body::first-line()", false)]
    public void SelectorsAreParsedAtTheFinalSubject(string selector, bool valid)
        => Assert.Equal(valid, CssSelectorParser.ParseGroup(selector, out _) is not null);

    [Theory]
    [InlineData("font-size:28px;color:red;", "font-size:28px;color:red;")]
    [InlineData("font-size:2em;color:red;", "font-size:32px;color:red;")]
    [InlineData("font-size:28px;font-weight:700;font-style:italic;color:red;", "font-size:28px;font-weight:700;font-style:italic;color:red;")]
    [InlineData("font-size:28px;letter-spacing:1.5px;word-spacing:2px;color:red;", "font-size:28px;letter-spacing:1.5px;word-spacing:2px;color:red;")]
    [InlineData("font-size:28px;line-height:1.4;color:red;", "font-size:28px;line-height:1.4;color:red;")]
    [InlineData("font-size:28px;text-transform:uppercase;color:red;", "font-size:28px;text-transform:uppercase;color:red;")]
    [InlineData("font-size:28px;text-decoration:underline;color:red;", "font-size:28px;text-decoration:underline;color:red;")]
    [InlineData("font-size:28px;background:yellow;color:red;", "font-size:28px;background:yellow;color:red;")]
    [InlineData("font-size:28px;background:yellow;opacity:.6;color:red;", "font-size:28px;background:yellow;opacity:.6;color:red;")]
    public void ForcedFirstLineMatchesLiteralInlineStyles(string style, string literal)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        const string source = "Alpha e\u0301cole ßeta 中文\nGamma delta remains ordinary.";
        var actual = Text(source); var original = Assert.Single(actual.Inlines);
        Add(actual, ".body {display:block;white-space:pre-line;line-height:1;} .body::first-line {" + style + "}");
        var reference = Literal(source, source.IndexOf('\n') + 1, literal);
        Compare(actual, reference, 440, 180);
        Assert.Same(original, Assert.Single(actual.Inlines)); Assert.Equal(source, actual.Text);
        actual.SelectAll(); Assert.Equal(source, actual.SelectedText);
    }

    [Theory]
    [InlineData(180)]
    [InlineData(240)]
    public void LaterOrdinaryTextCannotRefillTheStyledFirstLine(int width)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var text = Text("AAAA BBBB CCCC DDDD EEEE FFFF");
        Add(text, ".body {display:block;white-space:normal;line-height:1;} .body::first-line {font-size:32px;color:red;}");
        Layout(text, width, 240);
        var red = string.Concat(Draw(text).Where(p => p.Color == Colors.Red).Select(p => p.Text));
        Assert.Equal("AAAA BBBB", red.TrimEnd());
        Assert.True(text.IsMeasureValid); Assert.True(text.IsArrangeValid);
        var first = text.DocumentTextBounds(0, 1)[0]; var thirdWord = text.DocumentTextBounds(10, 1)[0];
        Assert.True(thirdWord.Top > first.Top + 20);
    }

    [Fact]
    public void InlineRelativeAndExplicitInheritedValuesUseFictionalFirstLineParents()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        const string source = "Alpha beta\nGamma delta";
        var actual = Text(""); var run = new Run(source); actual.Inlines.Add(run);
        var originalParent = run.Parent;
        Css.SetStyle(run, "font-size:2em;color:blue;");
        Add(actual, ".body {display:block;white-space:pre-line;line-height:1;} .body::first-line {font-size:24px;color:red;}");
        var reference = Literal(source, source.IndexOf('\n') + 1, "font-size:48px;color:blue;", 32, Colors.Blue);
        Compare(actual, reference, 600, 180);
        Assert.Equal(32, run.FontSize); Assert.Same(originalParent, run.Parent);
        Assert.Same(actual, ((FrameworkContentElement)run).Parent);
    }

    [Fact]
    public void CustomPropertiesKeepTheOrdinaryParentAndFirstLetterInheritsFirstLine()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        const string source = "Alpha beta\nGamma delta";
        var actual = Text(""); var run = new Run(source); actual.Inlines.Add(run);
        Css.SetStyle(run, "color:var(--ink);");
        Add(actual, ".body {display:block;white-space:pre-line;line-height:1;--ink:blue;} .body::first-line {font-size:24px;--ink:red;} .body::first-letter {font-size:2em;color:green;}");
        var reference = Text("", 1);
        Add(reference, ".body {display:block;white-space:pre-line;line-height:0;}");
        var first = new Run("A"); var line = new Run(source[1..(source.IndexOf('\n')+1)]); var tail = new Run(source[(source.IndexOf('\n')+1)..]);
        reference.Inlines.Add(first); reference.Inlines.Add(line); reference.Inlines.Add(tail);
        Css.SetStyle(first, "display:inline;font-size:48px;line-height:1;color:green;");
        Css.SetStyle(line, "display:inline;font-size:24px;line-height:1;color:blue;");
        Css.SetStyle(tail, "display:inline;font-size:16px;line-height:1;color:blue;");
        Compare(actual, reference, 600, 180);
        Assert.Equal(16, run.FontSize); Assert.Equal(Colors.Blue, Assert.IsType<SolidColorBrush>(run.Foreground).Color);
    }

    [Fact]
    public void EmptyFirstLineDoesNotStyleTheFollowingTextAndCanShrinkItsRootStrut()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var text = Text("\nAlpha beta");
        Add(text, ".body {display:block;white-space:pre;line-height:1;} .body::first-line {font-size:20px;line-height:.5;color:red;}");
        Layout(text, 300, 160);
        Assert.DoesNotContain(Draw(text), p => p.Color == Colors.Red && p.Text.Length > 0 && p.Text != "\n");
        var lines = text.GetDocumentLines(300);
        Assert.True(lines.Count >= 2); Assert.Equal(10, lines[0].Height, 3); Assert.Equal(16, lines[1].Height, 3);
    }

    [Fact]
    public void ContentClassAndHoverChangesRebuildAndThenLeaveAStableProjection()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var text = Text("Alpha beta\nGamma delta");
        Add(text, ".body {display:block;white-space:pre-line;line-height:1;} .body::first-line {color:red;} .body:hover::first-line {font-size:28px;color:blue;}");
        foreach (var hovered in new[] { true, false, true })
        {
            text.SetIsMouseOver(hovered); Layout(text, 360, 180);
            Assert.Contains(Draw(text), p => p.Text.Contains("Alpha") && p.Color == (hovered ? Colors.Blue : Colors.Red));
            Assert.True(text.IsMeasureValid); Assert.True(text.IsArrangeValid);
        }
        text.Text = "Beta gamma\nDelta epsilon"; Layout(text, 360, 180);
        Assert.Contains(Draw(text), p => p.Text.Contains("Beta") && p.Color == Colors.Blue);
        Css.SetClass(text, "other"); Layout(text, 360, 180);
        Assert.DoesNotContain(Draw(text), p => p.Color == Colors.Red || p.Color == Colors.Blue);
    }

    [Fact]
    public void SeveralAuthoredRunsShareOneLineAndRepeatedWidthsRestoreItsBoundary()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var text = Text(""); var first = new Run("AAAA BBBB "); var second = new Run("CCCC DDDD EEEE FFFF");
        text.Inlines.Add(first); text.Inlines.Add(second);
        Add(text, ".body {display:block;white-space:normal;line-height:1;} .body::first-line {font-size:32px;color:red;}");
        foreach (var (width, prefix) in new[] { (180,"AAAA BBBB"), (300,"AAAA BBBB CCCC"), (100,"AAAA"), (300,"AAAA BBBB CCCC"), (180,"AAAA BBBB") })
        {
            Layout(text,width,240);
            Assert.Equal(prefix, string.Concat(Draw(text).Where(p => p.Color == Colors.Red).Select(p => p.Text)).TrimEnd());
            Assert.Contains(Draw(text), p => p.Color == Colors.Black && Math.Abs(p.Size-16)<.001);
            Assert.True(text.IsMeasureValid); Assert.True(text.IsArrangeValid);
        }
        Assert.Equal(new[] { first, second }, text.Inlines.ToArray());
        text.SelectAll(); Assert.Equal("AAAA BBBB CCCC DDDD EEEE FFFF",text.SelectedText);
    }

    [Fact]
    public void AncestorAndFirstBlockPseudosNestAndFollowRemovalOfTheFirstChild()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var root = new StackPanel(); Css.SetClass(root,"root");
        var first = Text("Alpha beta\nGamma delta"); var second = Text("Delta epsilon\nZeta eta");
        root.Children.Add(first); root.Children.Add(second);
        Add(root,".root {display:block;} .body {display:block;white-space:pre-line;line-height:1;} .root::first-line {font-size:2em;color:red;} .body::first-line {font-size:1.5em;}");
        Layout(root,600,300);
        Assert.Contains(Draw(first),p=>p.Text.Contains("Alpha") && p.Color==Colors.Red && Math.Abs(p.Size-48)<.001);
        Assert.Contains(Draw(second),p=>p.Text.Contains("Delta") && p.Color==Colors.Black && Math.Abs(p.Size-24)<.001);
        Assert.Contains(Draw(first),p=>p.Text.Contains("Gamma") && p.Color==Colors.Black && Math.Abs(p.Size-16)<.001);
        root.Children.Remove(first); Layout(root,600,300);
        Assert.Contains(Draw(second),p=>p.Text.Contains("Delta") && p.Color==Colors.Red && Math.Abs(p.Size-48)<.001);
        Layout(first,600,180);
        Assert.DoesNotContain(Draw(first),p=>p.Color==Colors.Red);
    }

    [Theory]
    [InlineData("inline-block")]
    [InlineData("inline-flex")]
    [InlineData("inline-grid")]
    public void AtomicInlineDescendantsKeepTheirOrdinaryInheritedFont(string display)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var root = new StackPanel(); Css.SetClass(root,"root");
        var atom = Text("Alpha\nBeta"); Css.SetClass(atom,"atom"); var tail=Text("Tail");
        tail.ClearValue(TextBlock.FontSizeProperty); tail.ClearValue(TextBlock.ForegroundProperty);
        root.Children.Add(atom); root.Children.Add(tail);
        Add(root,".root {display:block;font-size:16px;line-height:1;} .atom {display:"+display+";white-space:pre;} .body {display:inline;} .root::first-line {font-size:40px;color:red;}");
        Layout(root,500,240);
        Assert.DoesNotContain(Draw(atom),p=>p.Color==Colors.Red || p.Size>16.001);
        Assert.Contains(Draw(tail),p=>p.Text.Contains("Tail") && p.Color==Colors.Red && Math.Abs(p.Size-40)<.001);
    }

    [Fact]
    public void GeneratedBeforeAndAfterUseTheSameFirstLineWithoutChangingSourceSelection()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var text=Text("Alpha\nGamma"); text.Tag="Note ";
        Add(text,".body {display:block;white-space:pre;line-height:1;} .body::before {content:attr(Tag);} .body::after {content:' End';} .body::first-line {font-size:28px;color:red;}");
        Layout(text,500,180);
        Assert.Equal("Note Alpha",string.Concat(Draw(text).Where(p=>p.Color==Colors.Red).Select(p=>p.Text)).TrimEnd('\r','\n'));
        Assert.Contains(Draw(text),p=>p.Text.Contains("End") && p.Color==Colors.Black && Math.Abs(p.Size-16)<.001);
        text.SelectAll(); Assert.Equal("Alpha\nGamma",text.SelectedText);
        text.Tag=""; Layout(text,500,180);
        Assert.Equal("Alpha",string.Concat(Draw(text).Where(p=>p.Color==Colors.Red).Select(p=>p.Text)).TrimEnd('\r','\n'));
    }

    [Fact]
    public void NativeFontOverridesAndOriginalSelectorsArePreservedInsideTheFirstLine()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var actual=Text(""); var span=new Span(); var run=new Run("Alpha beta\nGamma delta") { FontSize=21 };
        span.Inlines.Add(run); actual.Inlines.Add(span); Css.SetClass(span,"outer"); Css.SetClass(run,"inner");
        Add(actual,".body {display:block;white-space:pre-line;line-height:1;} .body > .outer > .inner {color:blue;} .body::first-line {font-size:40px;color:red;}");
        Layout(actual,500,180);
        Assert.All(Draw(actual).Where(p=>p.Text.Trim().Length>0),p=>{Assert.Equal(21,p.Size,3); Assert.Equal(Colors.Blue,p.Color);});
        Assert.Same(span,run.Parent); Assert.Same(actual,((FrameworkContentElement)span).Parent);
    }

    [Fact]
    public void ParagraphProjectionPreservesTheOriginalDocumentAndReplacesItsContent()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var paragraph=new Paragraph(); var run=new Run("Alpha beta\nGamma delta"); paragraph.Inlines.Add(run);
        var document=new FlowDocument { FontFamily=new FontFamily("monospace"),FontSize=16,Foreground=Brushes.Black }; document.Blocks.Add(paragraph);
        Css.SetClass(paragraph,"paragraph");
        var sheet=CssStyleSheet.Parse(".paragraph {white-space:pre-line;line-height:1;} .paragraph::first-line {font-size:28px;color:red;}");
        Assert.Empty(sheet.Diagnostics); Css.GetStyleSheets(document).Add(sheet); CssEvaluationScheduler.EvaluateSubtree(CssNode.Get(document));
        var text=new TextBlock(); text.PresentDocumentParagraph(paragraph,document); Layout(text,500,180);
        Assert.Contains(Draw(text),p=>p.Text.Contains("Alpha") && p.Color==Colors.Red && Math.Abs(p.Size-28)<.001);
        Assert.Contains(Draw(text),p=>p.Text.Contains("Gamma") && p.Color!=Colors.Red && Math.Abs(p.Size-16)<.001);
        Assert.Same(run,Assert.Single(paragraph.Inlines)); Assert.Same(paragraph,run.Parent);
        text.Select(1,3); Assert.Equal("lph",text.SelectedText);
        run.Text="Delta epsilon\nZeta eta"; text.PresentDocumentParagraph(paragraph,document); Layout(text,500,180);
        Assert.Contains(Draw(text),p=>p.Text.Contains("Delta") && p.Color==Colors.Red);
        Assert.DoesNotContain(Draw(text),p=>p.Text.Contains("Alpha"));
    }

    [Fact]
    public void ReplacingRunsDoesNotRetainTheirPrivateStyleAliases()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var text=Text("");
        Add(text,".body {display:block;white-space:pre-line;line-height:1;} .body::first-line {font-size:28px;color:red;}");
        var references=new List<WeakReference>();
        for(var index=0;index<20;index++) references.Add(Replace());
        text.Inlines.Clear(); text.Inlines.Add(new Run("Final line\nTail")); Layout(text,500,180);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.DoesNotContain(references,reference=>reference.IsAlive);
        Assert.Contains(Draw(text),p=>p.Text.Contains("Final") && p.Color==Colors.Red);
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        WeakReference Replace()
        {
            text.Inlines.Clear(); var run=new Run("Content "+references.Count+"\nTail"); text.Inlines.Add(run);
            Layout(text,500,180); return new WeakReference(run);
        }
    }

    [Fact]
    public void InlineBackgroundPaintsAboveTheFirstLineBackground()
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        const string source="Alpha beta\nGamma delta";
        var actual=Text(""); var run=new Run(source); actual.Inlines.Add(run); Css.SetStyle(run,"background:blue;");
        Add(actual,".body {display:block;white-space:pre-line;line-height:1;} .body::first-line {font-size:28px;background:yellow;}");
        var reference=Text("",1); Add(reference,".body {display:block;white-space:pre-line;line-height:0;}");
        var prefix=new Span(); var prefixText=new Run(source[..(source.IndexOf('\n')+1)]);
        prefix.Inlines.Add(prefixText); reference.Inlines.Add(prefix);
        Css.SetStyle(prefix,"display:inline;line-height:1;font-size:28px;background:yellow;");
        Css.SetStyle(prefixText,"display:inline;background:blue;");
        var tail=new Run(source[(source.IndexOf('\n')+1)..]); reference.Inlines.Add(tail);
        Css.SetStyle(tail,"display:inline;line-height:1;font-size:16px;background:blue;");
        Compare(actual,reference,500,180);
    }

    [Theory]
    [InlineData("font-size:28px;background:linear-gradient(90deg,red,blue);", "Alpha ", "beta\nGamma delta")]
    [InlineData("font-size:48px;font-style:italic;letter-spacing:-25px;opacity:.5;color:blue;", "W", "W\nTail")]
    public void FirstLinePaintingIsContinuousAcrossAuthoredRunBoundaries(string style,string first,string second)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var actual=Text(""); actual.Inlines.Add(new Run(first)); actual.Inlines.Add(new Run(second));
        Add(actual,".body {display:block;white-space:pre-line;line-height:1;} .body::first-line {"+style+"}");
        var source=first+second; var reference=Literal(source,source.IndexOf('\n')+1,style);
        Compare(actual,reference,500,180);
    }

    [Fact]
    public void FirstLineOpacityIncludesAtomicInlineChildren()
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var root=new StackPanel(); Css.SetClass(root,"root");
        var atom=new Border { Width=12,Height=12,Background=Brushes.Blue }; Css.SetClass(atom,"atom");
        var text=Text("Alpha\nGamma"); text.ClearValue(TextBlock.FontSizeProperty); text.ClearValue(TextBlock.ForegroundProperty);
        var tailAtom=new Border {Width=12,Height=12,Background=Brushes.Green}; Css.SetClass(tailAtom,"atom");
        root.Children.Add(atom); root.Children.Add(text); root.Children.Add(tailAtom);
        Add(root,".root {display:block;font-size:16px;color:black;line-height:1;} .atom {display:inline-block;} .body {display:inline;white-space:pre-line;} .root::first-line {font-size:28px;opacity:0;}");
        Layout(root,500,180);
        var pixels=Pixels(root,500,180);
        Assert.DoesNotContain(Enumerable.Range(0,500*28).Select(index=>pixels[index*4+3]),alpha=>alpha!=0);
        Assert.Contains(pixels.Skip(500*28*4),value=>value!=0);
    }

    [Fact]
    public void RemovingThePseudoRuleReleasesItsPaintAllocation()
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var actual=Text("Alpha beta\nGamma delta");
        Add(actual,".body {display:block;white-space:pre-line;line-height:1;} .body::first-line {font-size:28px;background:yellow;opacity:.5;}");
        Layout(actual,500,180);
        Css.SetClass(actual,"plain"); Css.SetStyle(actual,"display:block;white-space:pre-line;line-height:1;");
        var reference=Text("Alpha beta\nGamma delta"); Add(reference,".body {display:block;white-space:pre-line;line-height:1;}");
        Compare(actual,reference,500,180);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(300)]
    public void IntrinsicWidthsUseTheirOwnFirstLineRatherThanTheLastFiniteWidth(int priorWidth)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        const string source="AAAA BBBB CCCC DDDD\nTail remains normal";
        var actual=Text(source);
        Add(actual,".body {display:block;white-space:pre-line;line-height:1;} .body::first-line {font-size:32px;color:red;}");
        Layout(actual,priorWidth,240);
        var minReference=Literal(source,5,"font-size:32px;color:red;");
        var maxReference=Text(source[..source.IndexOf('\n')],32);
        Add(maxReference,".body {display:block;white-space:pre-line;line-height:1;}");
        var min=new CssLayoutBox(minReference,0); min.MeasureIntrinsicWidth();
        var max=new CssLayoutBox(maxReference,0); max.MeasureIntrinsicWidth();
        var box=new CssLayoutBox(actual,0); box.MeasureIntrinsicWidth();
        Assert.Equal(min.MinContent.Width,box.MinContent.Width,3);
        Assert.Equal(max.MaxContent.Width,box.MaxContent.Width,3);
        Layout(actual,priorWidth,240);
        Assert.True(actual.IsMeasureValid); Assert.True(actual.IsArrangeValid);
        Assert.Contains(Draw(actual),p=>p.Text.Contains("Tail") && p.Size==16 && p.Color==Colors.Black);
        Assert.Equal(source,actual.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  \t ")]
    public void EmptyCollapsedInlineContentDoesNotStealTheFirstNestedBlockLine(string leading)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var text=Text(""); var empty=new Span(); empty.Inlines.Add(new Run(leading));
        var block=new Run("Alpha beta\nGamma delta"); text.Inlines.Add(empty); text.Inlines.Add(block);
        Css.SetStyle(block,"display:block;white-space:pre-line;line-height:1;");
        Add(text,".body {display:block;white-space:normal;line-height:1;} .body::first-line {font-size:28px;color:red;}");
        Layout(text,500,180);
        Assert.Contains(Draw(text),p=>p.Text.Contains("Alpha") && p.Color==Colors.Red && p.Size==28);
        Assert.Contains(Draw(text),p=>p.Text.Contains("Gamma") && p.Color==Colors.Black && p.Size==16);
        Assert.Same(empty,text.Inlines[0]); Assert.Same(block,text.Inlines[1]);
    }

    [Fact]
    public void AThrowingRendererCannotLeaveAFirstLinePaintPassActive()
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var text=Text("Alpha\nGamma");
        Add(text,".body {display:block;white-space:pre-line;line-height:1;} .body::first-line {font-size:28px;background:yellow;opacity:.5;color:red;}");
        Layout(text,300,180);
        Assert.Throws<InvalidOperationException>(()=>text.Render(new Capture { ThrowFirstDraw=true }));
        var recovery=Text("Recovery"); Layout(recovery,300,180);
        Assert.Contains(Draw(recovery),p=>p.Text=="Recovery");
        Assert.Contains(Draw(text),p=>p.Text.Contains("Gamma") && p.Color==Colors.Black);
    }

    [Fact]
    public void ChangingANativeFloatWidthRecomputesTheFirstLineWithinTheSameContainingWidth()
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var root=new StackPanel(); Css.SetClass(root,"root");
        var floating=new Border {Width=100,Height=50}; Css.SetClass(floating,"float");
        var text=Text("AAAA BBBB CCCC DDDD EEEE FFFF"); text.ClearValue(TextBlock.FontSizeProperty); text.ClearValue(TextBlock.ForegroundProperty);
        root.Children.Add(floating); root.Children.Add(text);
        Add(root,".root {display:block;font-size:16px;color:black;line-height:1;} .float {float:left;} .body {display:inline;} .root::first-line {font-size:32px;color:red;}");
        foreach(var (floatWidth,prefix) in new[]{(100,"AAAA BBBB"),(20,"AAAA BBBB CCCC"),(100,"AAAA BBBB")})
        {
            floating.Width=floatWidth; Layout(root,300,240);
            Assert.Equal(prefix,string.Concat(Draw(text).Where(p=>p.Color==Colors.Red).Select(p=>p.Text)).TrimEnd());
            var first=text.DocumentTextBounds(0,1)[0]; var offset=text.TransformToAncestor(root);
            Assert.True(first.Left+offset.X>=floating.VisualBounds.Right-.001);
        }
    }

    [Theory]
    [InlineData(312,17)]
    [InlineData(672,21)]
    public void CombiningCharacterBoundsRemainWholeWhenTheFirstLineCrossesARunBoundary(int width,int end)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        const string source="Alpha e\u0301cole ßeta 中文\nGamma delta remains ordinary.";
        var actual=Text(""); actual.Inlines.Add(new Run(source[..6])); actual.Inlines.Add(new Run(source[6..]));
        Add(actual,".body {display:block;white-space:pre-line;line-height:1;} .body::first-line {font-size:28px;color:red;}");
        var reference=Literal(source,end,"font-size:28px;color:red;");
        if(width==312)
        {
            Css.SetStyle(reference.Inlines[0],"display:block;white-space:normal;font-size:28px;line-height:1;color:red;");
            Css.SetStyle(reference.Inlines[1],"display:block;font-size:16px;line-height:1;color:black;");
        }
        Compare(actual,reference,width,180);
        Assert.Equal(actual.DocumentTextBounds(6,2),actual.DocumentTextBounds(6,1));
        Assert.Equal(actual.DocumentTextBounds(6,2),actual.DocumentTextBounds(7,1));
    }

    [Fact]
    public void LineClampHidesLaterLinesAndRemovingItKeepsOnlyOneStyledFirstLine()
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        const string source="Alpha beta gamma delta epsilon zeta eta theta iota kappa lambda mu nu xi omicron pi rho sigma tau.";
        var text=Text(source);
        Add(text,".body {display:block;white-space:normal;line-height:1;line-clamp:2;} .body::first-line {font-size:28px;color:red;}");
        Layout(text,180,240);
        Assert.Equal("Alpha beta",string.Concat(Draw(text).Where(p=>p.Color==Colors.Red).Select(p=>p.Text)).TrimEnd());
        Assert.True(text.GetDocumentLines(180).Select(line=>line.Top).Distinct().Count()<=2);
        Assert.Empty(text.DocumentTextBounds(source.Length-4,2)); text.SelectAll(); Assert.Equal(source,text.SelectedText);
        Css.SetStyle(text,"line-clamp:none;"); Layout(text,180,240);
        Assert.True(text.GetDocumentLines(180).Select(line=>line.Top).Distinct().Count()>2);
        Assert.NotEmpty(text.DocumentTextBounds(source.Length-4,2));
        Assert.Equal("Alpha beta",string.Concat(Draw(text).Where(p=>p.Color==Colors.Red).Select(p=>p.Text)).TrimEnd());
    }

    [Fact]
    public void ColumnsUseTheColumnWidthAndDoNotRestartTheFirstLineStyle()
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        const string source="Alpha beta gamma delta epsilon zeta eta theta iota kappa lambda mu nu xi omicron pi rho sigma tau. Alpha beta gamma delta epsilon zeta eta theta.";
        var text=Text(source);
        Add(text,".body {display:block;white-space:normal;line-height:1;column-count:2;column-gap:20px;height:120px;} .body::first-line {font-size:28px;color:red;}");
        Layout(text,300,120);
        Assert.Equal("Alpha",string.Concat(Draw(text).Where(p=>p.Color==Colors.Red).Select(p=>p.Text)).TrimEnd());
        Assert.True(Enumerable.Range(0,source.Length).SelectMany(index=>text.DocumentTextBounds(index,1)).Any(rect=>rect.Left>=160));
        text.SelectAll(); Assert.Equal(source,text.SelectedText);
        Css.SetStyle(text,"column-count:1;"); Layout(text,300,120);
        Assert.Equal("Alpha beta gamma",string.Concat(Draw(text).Where(p=>p.Color==Colors.Red).Select(p=>p.Text)).TrimEnd());
        Assert.True(text.IsMeasureValid); Assert.True(text.IsArrangeValid);
    }

    [Fact]
    public void ExactGlyphMidpointsUseTheSameCaretSideAcrossAuthoredRunBoundaries()
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var actual=Text(""); actual.Inlines.Add(new Run("Alpha ")); actual.Inlines.Add(new Run("beta\nTail"));
        Add(actual,".body {display:block;white-space:pre-line;line-height:1;} .body::first-line {font-size:28px;color:red;}");
        var reference=Text("Alpha beta",28); Add(reference,".body {display:block;white-space:pre-line;line-height:1;}");
        Layout(actual,312,180); Layout(reference,312,180);
        foreach(var index in Enumerable.Range(0,5))
        {
            var bounds=actual.DocumentTextBounds(index,1)[0]; var point=new Point(bounds.Left+bounds.Width/2,bounds.Top+bounds.Height/2);
            Assert.Equal(reference.DocumentCharacterFromPoint(point),actual.DocumentCharacterFromPoint(point));
        }
    }

    [Fact]
    public void LayersScopesAndSelectorSupportsUseTheAuthoredOriginAfterReparenting()
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var root=new StackPanel(); var scope=new StackPanel(); var outside=new StackPanel(); Css.SetClass(scope,"scope");
        root.Children.Add(scope); root.Children.Add(outside);
        var text=Text("Alpha beta\nGamma delta"); scope.Children.Add(text);
        Add(root,".body {display:block;white-space:pre-line;line-height:1;} @layer base,top; @layer base {.body::first-line {font-size:20px;color:red;}} @layer top {@scope (.scope) {.body::first-line {font-size:28px;color:green;}}} @supports selector(.body::first-line) {.body::first-line {color:blue;}}");
        Layout(root,500,240);
        Assert.Contains(Draw(text),p=>p.Text.Contains("Alpha") && p.Size==28 && p.Color==Colors.Blue);
        scope.Children.Remove(text); outside.Children.Add(text); Layout(root,500,240);
        Assert.Contains(Draw(text),p=>p.Text.Contains("Alpha") && p.Size==20 && p.Color==Colors.Blue);
        Assert.Contains(Draw(text),p=>p.Text.Contains("Gamma") && p.Size==16 && p.Color==Colors.Black);
    }

    [Fact]
    public void CompiledSheetBuildersPreserveTheFirstLineTarget()
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        const string source="Alpha beta\nGamma delta";
        var actual=Text(source); Add(actual,".body {display:block;white-space:pre-line;line-height:1;}");
        var builder=new CssStyleSheetBuilder(1);
        var compound=builder.Compound("TextBlock",false,null,false,true,false,null,["body"],null,0,null,null,(byte)CssPseudoElement.FirstLine);
        var selector=builder.Selector([compound],[],(1<<10)+2,0,0,false,false);
        var size=builder.Declaration("font-size","28px",false); var color=builder.Declaration("color","red",false);
        builder.Rule([selector],[size,color],-1,null,-1); Css.GetStyleSheets(actual).Add(builder.Build());
        var reference=Literal(source,source.IndexOf('\n')+1,"font-size:28px;color:red;"); Compare(actual,reference,500,180);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DelayedFontArrivalRecomputesTheFirstLineWithoutChangingItsSource(bool firstLineOnly)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        const string source="xxxx xxxx xxxx xxxx xxxx xxxx";
        var resolver=new PendingFontResolver(); var text=Text(source); var original=Assert.Single(text.Inlines);
        text.ClearValue(TextBlock.FontFamilyProperty);
        var face="FirstLineArriving"+(firstLineOnly?"Pseudo":"Body"); var family=face+",serif";
        var sheet=CssStyleSheet.Parse("@font-face {font-family:"+face+";src:url("+face+".ttf);font-display:swap;size-adjust:200%;}"+
            ".body {display:block;white-space:normal;line-height:1;"+(firstLineOnly?"":"font-family:"+family+";")+"}"+
            ".body::first-line {font-size:32px;color:red;"+(firstLineOnly?"font-family:"+family+";":"")+"}",
            "first-line font arrival",new Uri("https://first-line.invalid/"),resolver);
        Assert.Empty(sheet.Diagnostics); Css.GetStyleSheets(text).Add(sheet); Layout(text,180,300);
        Assert.True(SpinWait.SpinUntil(()=>resolver.Requested,TimeSpan.FromSeconds(5)));
        var before=string.Concat(Draw(text).Where(p=>p.Color==Colors.Red).Select(p=>p.Text)).TrimEnd();
        resolver.Complete(File.ReadAllBytes(CssFontSizeAdjustTests.Fixture("Primary")));
#pragma warning disable xUnit1031
        Css.WaitForFontsAsync(text).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
#pragma warning restore xUnit1031
        CssFontFaces.FlushNotifications(text.Dispatcher); Assert.Empty(Css.GetFontLoadErrors(text));
        var probe=Text(source,32); probe.ClearValue(TextBlock.FontFamilyProperty);
        Css.SetClass(probe,"reference"); Css.GetStyleSheets(probe).Add(sheet);
        Css.SetStyle(probe,"display:block;white-space:normal;line-height:1;font-family:"+family+";");
        Layout(probe,180,300);
#pragma warning disable xUnit1031
        Css.WaitForFontsAsync(probe).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
#pragma warning restore xUnit1031
        CssFontFaces.FlushNotifications(probe.Dispatcher); Assert.Empty(Css.GetFontLoadErrors(probe));
        foreach(var width in new[]{180,300,180})
        {
            Layout(probe,width,300); Layout(text,width,300);
            var first=probe.DocumentSharedLine(0,1); Assert.NotNull(first);
            var end=Enumerable.Range(0,source.Length).TakeWhile(index=>ReferenceEquals(probe.DocumentSharedLine(index,index+1),first)).Count();
            var after=string.Concat(Draw(text).Where(p=>p.Color==Colors.Red).Select(p=>p.Text)).TrimEnd();
            Assert.Equal(source[..end].TrimEnd(),after);
            if(width==180) Assert.NotEqual(before,after);
            Assert.True(text.IsMeasureValid); Assert.True(text.IsArrangeValid);
        }
        Assert.Same(original,Assert.Single(text.Inlines)); text.SelectAll(); Assert.Equal(source,text.SelectedText);
    }

    [Theory]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void SelectionUsesTheCurrentRenderingRootsDpiAfterRepeatedTransitions(double targetDpi)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var text=Text("Alpha\nTail remains ordinary."); var root=new StackPanel();root.Children.Add(text);
        Add(root,".body {display:block;white-space:pre-line;line-height:1;} .body::first-line {font-size:48px;color:red;}");
        foreach(var dpi in new[]{1,targetDpi,1})
        {
            VisualTreeHelper.SetRootDpi(root,new(dpi,dpi)); Layout(root,312,180); text.Select(0,5);
            var capture=new Capture();root.Render(capture);var selection=Assert.Single(capture.Rectangles);
            var bounds=Assert.Single(text.DocumentTextBounds(0,5));
            Assert.Equal(Math.Round(bounds.Right*dpi)/dpi,selection.Right,6);
            Assert.Equal(Math.Round(bounds.Bottom*dpi)/dpi,selection.Bottom,6);
            Assert.Equal("Alpha",text.SelectedText); Assert.True(root.IsArrangeValid);
        }
    }

    [Fact]
    public void PrivateFirstLineBoxesPreserveTabOrderKeyboardSelectionAndAccessibleText()
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        Keyboard.Initialize();Keyboard.ClearFocus();
        var text=Text("Alpha beta\nGamma delta");var first=new Button {Content="前"};var next=new Button {Content="后"};
        var root=new StackPanel();root.Children.Add(first);root.Children.Add(text);root.Children.Add(next);
        Add(root,".body {display:block;white-space:pre-line;line-height:1;} .body::first-line {font-size:28px;color:red;}");
        var window=new DisplayedTestWindow {Width=360,Height=240,Content=root,TitleBarStyle=WindowTitleBarStyle.Native};
        try
        {
            Layout(window,360,240);Assert.True(first.Focus());Assert.True(KeyboardNavigation.MoveFocus(first));Assert.Same(next,Keyboard.FocusedElement);
            var command=new KeyEventArgs(UIElement.KeyDownEvent,Key.A,ModifierKeys.Control,true,false,0);
            text.RaiseEvent(command);Assert.True(command.Handled);Assert.Equal(text.Text,text.SelectedText);
            Assert.Equal(text.Text,text.GetAutomationPeer()!.GetName());Assert.Empty(text.GetAutomationPeer()!.GetChildren());
            text.Text="Beta gamma\nDelta epsilon";Layout(window,360,240);Assert.Same(next,Keyboard.FocusedElement);
            Assert.Equal(text.Text,text.GetAutomationPeer()!.GetName());Assert.Empty(text.GetAutomationPeer()!.GetChildren());
            Assert.True(KeyboardNavigation.MoveFocus(next,reverse:true));Assert.Same(first,Keyboard.FocusedElement);
        }
        finally {Keyboard.ClearFocus();window.Close();}
    }

    private sealed class PendingFontResolver : ICssResourceResolver
    {
        private readonly TaskCompletionSource<byte[]> _bytes=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal volatile bool Requested;
        internal void Complete(byte[] bytes)=>_bytes.TrySetResult(bytes);
        public async ValueTask<CssResource> ResolveAsync(Uri uri,CancellationToken cancellationToken=default)
        {
            Requested=true;
            return new(uri,new MemoryStream(await _bytes.Task.WaitAsync(cancellationToken)),"font/ttf");
        }
    }

    [Theory]
    [InlineData("@scope (.scope) {.body::first-line {color:red;}}",true)]
    [InlineData("@scope (.scope::first-line) {.body {color:red;}}",false)]
    [InlineData("@scope (.scope) to (.body::first-line) {.body {color:red;}}",false)]
    public void ScopeBoundariesAndScopedRuleSubjectsHaveDifferentPseudoElementRestrictions(string css,bool valid)
    {
        var sheet=CssStyleSheet.Parse(css);
        Assert.Equal(valid,sheet.Diagnostics.Count==0);
    }

    private static TextBlock Text(string value, double size = 16)
    {
        var text = new TextBlock { Text = value, FontSize = size, FontFamily = new FontFamily("monospace"), Foreground = Brushes.Black, IsTextSelectionEnabled = true };
        Css.SetClass(text, "body"); return text;
    }
    private static void Add(FrameworkElement owner, string css)
    {
        var sheet = CssStyleSheet.Parse(css); Assert.Empty(sheet.Diagnostics); Css.GetStyleSheets(owner).Add(sheet);
    }
    private static TextBlock Literal(string source, int end, string style, double tailSize = 16, Color? tailColor = null)
    {
        var text = Text("", 1);
        Add(text, ".body {display:block;white-space:pre-line;line-height:0;}");
        var first = new Run(source[..end]); var tail = new Run(source[end..]); text.Inlines.Add(first); text.Inlines.Add(tail);
        Css.SetStyle(first, "display:inline;line-height:1;font-size:16px;color:black;" + style);
        Css.SetStyle(tail, "display:inline;line-height:1;font-size:" + tailSize.ToString(System.Globalization.CultureInfo.InvariantCulture) + "px;");
        tail.Foreground = new SolidColorBrush(tailColor ?? Colors.Black);
        return text;
    }
    private static void Layout(FrameworkElement root, int width, int height)
    {
        for (var pass = 0; pass < 5; pass++)
        { CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); root.Measure(new(width,height)); root.Arrange(new(0,0,width,height)); root.UpdateLayout(); }
    }
    private static void Compare(TextBlock actual, TextBlock reference, int width, int height)
    {
        Layout(reference,width,height); Layout(actual,width,height);
        for (var index = 0; index < actual.Text.Length; index++)
        {
            var expected = reference.DocumentTextBounds(index,1); var found = actual.DocumentTextBounds(index,1); Assert.Equal(expected.Count,found.Count);
            for (var part = 0; part < expected.Count; part++)
            { Assert.Equal(expected[part].X,found[part].X,3); Assert.Equal(expected[part].Y,found[part].Y,3); Assert.Equal(expected[part].Width,found[part].Width,3); Assert.Equal(expected[part].Height,found[part].Height,3); }
        }
        var before = Pixels(reference,width,height); var after = Pixels(actual,width,height); var largest = before.Zip(after).Max(pair => Math.Abs(pair.First-pair.Second));
        // This public bitmap entry rasterizes managed shapes, not native text.
        // Verify text commands separately; the host matrix captures real glyphs.
        static IEnumerable<(char Character,double Size,int Style,Color Color)> Glyphs(IEnumerable<Paint> paints)
            => paints.SelectMany(paint=>paint.Text.Select(character=>(character,paint.Size,paint.Style,paint.Color)));
        var expectedGlyphs=Glyphs(Draw(reference)).ToArray(); var actualGlyphs=Glyphs(Draw(actual)).ToArray();
        Assert.NotEmpty(expectedGlyphs); Assert.Equal(expectedGlyphs,actualGlyphs);
        Assert.True(largest<=3,$"Rendered channels differ by {largest}"); Assert.True(actual.IsMeasureValid); Assert.True(actual.IsArrangeValid);
    }
    private static byte[] Pixels(FrameworkElement root,int width,int height)
    {
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Bgra32); bitmap.Render(root);
        var pixels=new byte[width*height*4]; bitmap.CopyPixels(pixels,width*4,0); return pixels;
    }
    private static List<Paint> Draw(FrameworkElement root) { var context = new Capture(); root.Render(context); return context.Paints; }
    private sealed record Paint(string Text, double Size, int Style, Color Color);
    private sealed class Capture : DrawingContextAdapter,IOffsetDrawingContext
    {
        internal readonly List<Paint> Paints = [];
        internal readonly List<Rect> Rectangles = [];
        public Point Offset {get;set;}
        internal bool ThrowFirstDraw;
        public override void DrawText(FormattedText text, Point origin)
        {
            if(ThrowFirstDraw) {ThrowFirstDraw=false;throw new InvalidOperationException("Expected renderer failure");}
            Paints.Add(new(text.Text,text.FontSize,text.FontStyle,text.Foreground is SolidColorBrush brush?brush.Color:Colors.Transparent));
        }
        public override void DrawLine(Pen pen, Point a, Point b) { }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rect)
        {if(brush is not null) Rectangles.Add(new(rect.X+Offset.X,rect.Y+Offset.Y,rect.Width,rect.Height));}
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rect, double x, double y) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double x, double y) { }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawImage(ImageSource source, Rect rect) { }
        public override void PushClip(Geometry clip) { }
        public override void PushOpacity(double opacity) { }
        public override void PushTransform(Transform transform) { }
        public override void Pop() { }
        public override void Close() { }
        public override void DrawBackdropEffect(Rect rect, IBackdropEffect effect, CornerRadius radius) { }
    }
}

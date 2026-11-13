using System.Globalization;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSCssInlineBidiHyphenTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (bool rtl in new[] { false, true })
            foreach (string mode in new[] { "normal", "bidi-override", "plaintext", "isolate" })
                foreach (string hyphen in new[] { "-", "--", "", "\u2010" })
                    yield return [rtl, mode, hyphen];
    }

    [Theory] [MemberData(nameof(Cases))]
    public void ConditionalHyphenKeepsParagraphContextAndOriginalRanges(bool rtl, string mode, string hyphen)
        => Check(rtl, mode, hyphen, false);

    [Theory]
    [InlineData(false, "normal", "--")] [InlineData(true, "normal", "--")]
    [InlineData(false, "isolate", "-")] [InlineData(true, "isolate", "-")]
    [InlineData(false, "bidi-override", "‐")] [InlineData(true, "bidi-override", "‐")]
    public void GeneratedGlyphsUseTheOriginalSoftHyphenRun(bool rtl, string mode, string hyphen)
        => Check(rtl, mode, hyphen, true);

    private static void Check(bool rtl, string mode, string hyphen, bool styled)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        const string first = "אב ABC\u00ADDEFG", second = " HI";
        const int shy = 6;
        var root = new StackPanel();
        var a = new TextBlock { Text = first, IsTextSelectionEnabled = true };
        var b = new TextBlock { Text = second, IsTextSelectionEnabled = true };
        Run? prefixRun = null, hyphenRun = null;
        var generatedBrush = new SolidColorBrush(Colors.Blue);
        if (styled)
        {
            a.Text = ""; prefixRun = new Run(first[..shy]);
            hyphenRun = new Run("\u00AD") { FontWeight = FontWeights.Bold, FontStyle = FontStyles.Italic, Foreground = generatedBrush };
            a.Inlines.Add(prefixRun); a.Inlines.Add(hyphenRun); a.Inlines.Add(new Run(first[(shy + 1)..]));
        }
        root.Children.Add(a); root.Children.Add(b);
        string style = $"display:block;width:300px;height:180px;box-sizing:border-box;padding:8px;font-family:DejaVu Sans;font-size:22px;line-height:40px;white-space:normal;direction:{(rtl ? "rtl" : "ltr")};unicode-bidi:{(mode == "isolate" ? "normal" : mode)};color:#d83253;hyphens:manual;hyphenate-limit-chars:1 1 1;hyphenate-character:\"{hyphen}\"";
        Css.SetStyle(root, style);
        Css.SetStyle(a, "display:inline" + (mode == "isolate" ? ";direction:rtl;unicode-bidi:isolate" : ""));
        Css.SetStyle(b, "display:inline"); Layout(root, 300);
        string open = mode == "bidi-override" ? (rtl ? "\u202E" : "\u202D") : "";
        string innerOpen = mode == "isolate" ? "\u2067" : "";
        string innerClose = mode == "isolate" ? "\u2069" : "";
        string visibleFirst = first[..shy] + hyphen + "\u2028" + first[(shy + 1)..];
        string referenceText = open + innerOpen + visibleFirst + innerClose + second + (open.Length > 0 ? "\u202C" : "");
        string family = a.FontFamily.GetRenderingSource(a);
        var units = Enumerable.Range(0, referenceText.Length).Select(i => new NativeMethods.ParagraphAdvanceUnit
        {
            TextPosition = (uint)i, Length = 1,
            Flags = char.GetUnicodeCategory(referenceText[i]) == UnicodeCategory.Format || referenceText[i] == '\u2028' ? 1u : referenceText[i] == ' ' ? 4u : 0u
        }).ToArray();
        int generatedStart = open.Length + innerOpen.Length + shy;
        NativeTextParagraph.Span[] spans = styled
            ? [new(referenceText[..generatedStart],family,22,400,0,Colors.Black),
               new(referenceText.Substring(generatedStart,hyphen.Length),family,22,700,1,Colors.Black),
               new(referenceText[(generatedStart+hyphen.Length)..],family,22,400,0,Colors.Black)]
            : [new(referenceText, family, 22, 400, 0, Colors.Black)];
        using var reference = NativeTextParagraph.TryCreateAdvanced(spans,
            family, 22, 10000000, 1, root.FlowDirection, units, contextPaint: true, naturalDirection: mode == "plaintext", textOptionsOwner: a);
        Assert.NotNull(reference); Assert.Equal(2, reference.Lines.Length);
        int width = (int)Math.Ceiling(Math.Max(reference.Lines[0].Metrics.Line.Width, reference.Lines[1].Metrics.Line.Width)) + 17;
        string narrow = style.Replace("width:300px", "width:" + width + "px");
        Css.SetStyle(root, narrow); Layout(root, width);
        var firstRange = Assert.Single(a.GetSourceRangeSegments(0, 1));
        double baseline = a.TransformToAncestor(root).Y + firstRange.Baseline;
        double Left(int row) => ((rtl || mode == "plaintext") ? width - 8 - reference.Lines[row].Metrics.Line.Width : 8) - reference.Lines[row].Metrics.Line.X;
        var brush = Assert.IsType<SolidColorBrush>(a.Foreground);
        var expected = Pixels(dc =>
        {
            for (int row = 0; row < 2; row++)
            {
                var line = reference.Lines[row];
                int start = (int)line.Metrics.Line.TextPosition, end = start + (int)line.Metrics.Line.Length;
                while (end > start && referenceText[end - 1] == '\u2028') end--;
                if (styled && row == 0)
                {
                    Draw(start, generatedStart, brush);
                    Draw(generatedStart, generatedStart + hyphen.Length, generatedBrush);
                    Draw(generatedStart + hyphen.Length, end, brush);
                }
                else Draw(start, end, brush);
                void Draw(int from, int to, SolidColorBrush ink)
                {
                    if (to <= from) return;
                    dc.DrawText(Options(new FormattedText(referenceText, family, 22)
                    {
                        Foreground = ink,
                        PlatformTextLine = line.ForSource(from, to - from, ink.Color, 1)
                            .WithBrushLineBox(line.Metrics.Baseline - (baseline - 8), 40)
                    }, a), new(Left(row), baseline + row * 40 - line.Metrics.Baseline));
                }
            }
        }, context);
        Assert.Equal(expected, Pixels(root.Render, context));
        foreach (var (source, rawStart) in new[] { (a, open.Length + innerOpen.Length), (b, open.Length + innerOpen.Length + visibleFirst.Length + innerClose.Length) })
        {
            var offset = source.TransformToAncestor(root);
            string logical = ReferenceEquals(source,a) ? first : second;
            for (int i = 0; i < logical.Length; i++)
            {
                if (logical[i] == ' ') continue;
                bool generated = ReferenceEquals(source, a) && i == shy;
                int index = rawStart + i + (ReferenceEquals(source, a) && i > shy ? hyphen.Length : 0);
                int length = generated ? hyphen.Length : 1;
                int row = ReferenceEquals(source, a) && i <= shy ? 0 : 1;
                var wanted = reference.Selection((uint)row, index, length);
                var actual = source.GetSourceRangeSegments(i, 1);
                if (length == 0) continue;
                Assert.True(wanted.Length > 0, $"Reference range {i} for {(ReferenceEquals(source,a)?"first":"second")} source, hyphen '{hyphen}'.");
                Assert.True(actual.Count > 0, $"Actual range {i} for {(ReferenceEquals(source,a)?"first":"second")} source, hyphen '{hyphen}'.");
                Assert.Equal(Left(row) + wanted.Min(r => r.X), offset.X + actual.Min(r => Math.Min(r.StartX, r.EndX)), 3);
                Assert.Equal(wanted.Sum(r => (double)r.Width), actual.Sum(r => Math.Abs(r.EndX - r.StartX)), 3);
                Assert.Equal(baseline + row * 40, offset.Y + actual[0].Baseline, 3);
                foreach (var range in actual.Where(r => Math.Abs(r.EndX - r.StartX) > .01))
                {
                    var hit = Assert.IsType<TextPointer>(source.GetPositionFromPoint(new((range.StartX + range.EndX) / 2, range.Baseline - 2), false));
                    Assert.InRange(source.ContentStart.GetOffsetToPosition(hit), i, i + 1);
                }
            }
            source.SelectAll(); Assert.Equal(logical, source.SelectedText); source.ClearSelection();
        }
        var recording = new DrawingRecorder(); recording.BindWholeFrame(); root.Render(recording); var frame = recording.Commit();
        Assert.True(frame.IsFullyRecordable);
        if (prefixRun is not null) prefixRun.Text = first[..shy].Replace('A','Z'); else a.Text = first.Replace('A', 'Z');
        Layout(root, width);
        Assert.Equal(expected, Pixels(dc => DrawingReplayer.Replay(frame, dc), context));
        Assert.NotEqual(expected, Pixels(root.Render, context));
        if (prefixRun is not null) prefixRun.Text = first[..shy]; else a.Text = first;
        Css.SetStyle(root, style); Layout(root, 300);
        Assert.Single(a.GetSourceRangeSegments(0, first.Length).Select(range => range.Baseline).Distinct());
        Css.SetStyle(root, narrow); Layout(root, width); Assert.Equal(expected, Pixels(root.Render, context));
        if (styled) { Assert.Same(prefixRun, a.Inlines[0]); Assert.Same(hyphenRun, a.Inlines[1]); Assert.Equal("\u00AD", hyphenRun!.Text); }
    }

    [Theory] [InlineData(false, "none")] [InlineData(true, "none")]
    [InlineData(false, "manual")] [InlineData(true, "manual")]
    public void AnUnbrokenConditionalHyphenHasNoGlyphOrAdvance(bool rtl, string hyphens)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var root = new StackPanel(); var a = new TextBlock { Text = "אב ABC\u00ADDEFG" }; var b = new TextBlock { Text = " HI" };
        root.Children.Add(a); root.Children.Add(b);
        Css.SetStyle(root, $"display:block;width:300px;height:180px;box-sizing:border-box;padding:8px;font-family:DejaVu Sans;font-size:22px;line-height:40px;white-space:normal;direction:{(rtl?"rtl":"ltr")};hyphens:{hyphens};color:#d83253");
        Css.SetStyle(a,"display:inline"); Css.SetStyle(b,"display:inline"); Layout(root,300);
        const string literal="אב ABCDEFG HI"; string family=a.FontFamily.GetRenderingSource(a);
        using var reference=NativeTextParagraph.TryCreate([new(literal,family,22,400,0,Colors.Black)],family,22,10000000,1,
            TextAlignment.Left,root.FlowDirection,noWrap:true,textOptionsOwner:a);
        Assert.NotNull(reference); var line=Assert.Single(reference.Lines); var brush=Assert.IsType<SolidColorBrush>(a.Foreground);
        double baseline=a.TransformToAncestor(root).Y+Assert.Single(a.GetSourceRangeSegments(0,1)).Baseline;
        double x=(rtl?292-line.Metrics.Line.Width:8)-line.Metrics.Line.X;
        var expected=Pixels(dc=>dc.DrawText(Options(new FormattedText(literal,family,22){Foreground=brush,
            PlatformTextLine=line.ForSource(0,literal.Length,brush.Color,1).WithBrushLineBox(line.Metrics.Baseline-(baseline-8),40)},a),new(x,baseline-line.Metrics.Baseline)),context);
        Assert.Equal(expected,Pixels(root.Render,context));
        var hidden=a.GetSourceRangeSegments(6,1);Assert.All(hidden,r=>Assert.Equal(0,Math.Abs(r.EndX-r.StartX),3));
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void HyphensNoneDoesNotBreakAnOverflowingWord(bool rtl)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var root=new StackPanel();var a=new TextBlock{Text="אבABCDEFG\u00ADHIJKLM"};var b=new TextBlock{Text="NOP"};root.Children.Add(a);root.Children.Add(b);
        Css.SetStyle(root,$"display:block;width:80px;font-family:DejaVu Sans;font-size:22px;line-height:40px;direction:{(rtl?"rtl":"ltr")};hyphens:none");
        Css.SetStyle(a,"display:inline");Css.SetStyle(b,"display:inline");Layout(root,80);
        var rows=a.GetSourceRangeSegments(0,a.Text.Length).Select(r=>r.Baseline).Concat(b.GetSourceRangeSegments(0,b.Text.Length).Select(r=>r.Baseline)).Distinct();
        Assert.Single(rows);Assert.Empty(a.GetSourceRangeSegments(9,1));
    }

    private static FormattedText Options(FormattedText text, DependencyObject source)
    {
        text.ApplyTextOptionsFrom(source);
        return text;
    }
    private static void Layout(FrameworkElement root, int width)
    {
        for (int i = 0; i < 3; i++) { Dispatcher.GetForCurrentThread().ProcessQueue(); root.Measure(new(width, 180)); root.Arrange(new(0, 0, width, 180)); root.UpdateLayout(); }
    }
    private static byte[] Pixels(Action<DrawingContext> paint, RenderContext context)
    {
        using var target = context.CreateOffscreenRenderTarget(400, 200); target.RequestReadback(); target.BeginDraw(); target.Clear(0, 0, 0, 0);
        using (var dc = new RenderTargetDrawingContext(target, context)) { paint(dc); dc.Close(); }
        target.EndDraw(); var pixels = new byte[400 * 200 * 4];
        Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, 400 * 4, out _, out _)); return pixels;
    }
}

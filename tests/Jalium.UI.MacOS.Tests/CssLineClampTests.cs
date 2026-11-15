using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Styling;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssLineClampTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();
    public static IEnumerable<object[]> Cases()
    {
        foreach (var path in CssParagraphWrapStyleTests.Paths)
        foreach (var lines in new[] { 1, 2 })
        foreach (var marker in new[] { "no-ellipsis", "ellipsis", "'… 阅读更多'" })
        foreach (var wrap in new[] { "auto", "balance" }) yield return [path, lines, marker, wrap];
    }
    [Theory] [MemberData(nameof(Cases))]
    public void ClampLimitsActualLinesAndRetainsOriginalTextAndRunIdentity(string path, int lines, string marker, string wrap)
    {
        using var f = new WrapStyleFixture(path, CssParagraphWrapStyleTests.Words);
        f.SetStyle(wrap, extra: $"line-clamp:{lines} {marker};"); f.Layout();
        Assert.Equal(lines, f.Lines().Length);
        Assert.InRange(f.Root.DesiredSize.Height, 24 * lines - .1, 24 * lines + 3);
        AssertOriginal(f);
        var end = f.Lines()[^1].End; Assert.True(end < f.Words.Length);
        f.SetStyle(wrap); f.Layout(); Assert.Equal(3, f.Lines().Length); AssertOriginal(f);
    }

    public static IEnumerable<object[]> Paths() => CssParagraphWrapStyleTests.Paths.Select(path => new object[] { path });
    [Theory] [MemberData(nameof(Paths))]
    public void LonghandsNoneClearingAndNativeNoWrapCyclesRestoreTheFullParagraph(string path)
    {
        using var f = new WrapStyleFixture(path, CssParagraphWrapStyleTests.Words);
        f.SetStyle("auto"); f.Layout(); var full = f.Lines();
        f.SetStyle("auto", extra: "max-lines:2;block-ellipsis:ellipsis;continue:collapse;"); f.Layout(); Assert.Equal(2, f.Lines().Length);
        Css.SetStyle(f.Root, Css.GetStyle(f.Root) + "continue:normal;"); f.Layout(); Assert.Equal(full, f.Lines());
        f.SetStyle("auto", extra: "line-clamp:1;line-clamp:none;"); f.Layout(); Assert.Equal(full, f.Lines());
        f.SetStyle("auto", extra: "line-clamp:1;"); f.Layout(); Assert.Single(f.Lines());
        f.Root.SetValue(TextBlock.TextWrappingProperty, TextWrapping.NoWrap); f.Layout(); Assert.Single(f.Lines());
        f.Root.ClearValue(TextBlock.TextWrappingProperty); f.Layout(); Assert.Single(f.Lines());
        f.SetStyle("auto"); f.Layout(); Assert.Equal(full, f.Lines()); AssertOriginal(f);
    }

    [Theory] [MemberData(nameof(Paths))]
    public void AutoClampUsesTheRealHeightConstraintAndPadding(string path)
    {
        using var f = new WrapStyleFixture(path, CssParagraphWrapStyleTests.Words);
        f.SetStyle("auto", extra: "line-clamp:auto;max-height:52px;padding:2px;"); f.Layout();
        Assert.Equal(2, f.Lines().Length); Assert.InRange(f.Root.DesiredSize.Height, 51.9, 52.1);
        f.SetStyle("auto", extra: "line-clamp:auto;max-height:76px;padding:2px;"); f.Layout(); Assert.Equal(3, f.Lines().Length);
        AssertOriginal(f);
    }

    [Theory] [MemberData(nameof(Paths))]
    public void AParagraphThatFitsDoesNotAcquireAnEllipsisOrLoseSourceRanges(string path)
    {
        using var f = new WrapStyleFixture(path, "MMMM MMMM");
        f.SetStyle("auto"); f.Layout(); var full = f.Lines();
        f.SetStyle("auto", extra: "line-clamp:2;"); f.Layout(); Assert.Equal(full, f.Lines());
        Assert.Equal(f.Words.Length, f.Lines()[^1].End); AssertOriginal(f);
    }

    [Theory] [MemberData(nameof(Paths))]
    public void ForcedBreaksAndEmptyLinesConsumeTheVisibleLineBudget(string path)
    {
        using var f = new WrapStyleFixture(path, "M M\n\nM M\nM M");
        f.SetStyle("auto", extra: "white-space:pre-line;line-clamp:2;"); f.Layout();
        Assert.Single(f.Lines()); Assert.InRange(f.Root.DesiredSize.Height, 47.9, 50.1); AssertOriginal(f);
        f.SetStyle("auto", extra: "white-space:pre-line;line-clamp:3;"); f.Layout();
        Assert.Equal(2, f.Lines().Length); Assert.InRange(f.Root.DesiredSize.Height, 71.9, 74.1);
    }

    [Theory] [MemberData(nameof(Paths))]
    public void LegacySyntaxOnlyClampsWithTheRequiredDisplayAndOrientation(string path)
    {
        using var f = new WrapStyleFixture(path, CssParagraphWrapStyleTests.Words);
        f.SetStyle("auto", extra: "-webkit-line-clamp:2;"); f.Layout(); Assert.Equal(3, f.Lines().Length);
        f.SetStyle("auto", extra: "display:-webkit-box;-webkit-box-orient:vertical;-webkit-line-clamp:2;"); f.Layout(); Assert.Equal(2, f.Lines().Length);
        Css.SetStyle(f.Root, Css.GetStyle(f.Root) + "display:block;"); f.Layout(); Assert.Equal(3, f.Lines().Length);
        f.SetStyle("auto", extra: "display:-webkit-inline-box;-webkit-box-orient:vertical;line-clamp:1;"); f.Layout(); Assert.Single(f.Lines());
        AssertOriginal(f);
    }

    [Fact]
    public void OneBudgetCountsLinesAcrossNestedBlocksAndSuppressesFollowingSiblings()
    {
        var root = Root(2); var first = Text("M"); root.Children.Add(first);
        var frame = new Border(); var inner = new StackPanel(); frame.Child = inner; root.Children.Add(frame);
        Css.SetStyle(frame, "display:block;"); Css.SetStyle(inner, "display:block;");
        var body = Text(CssParagraphWrapStyleTests.Words); inner.Children.Add(body);
        var after = Text("AFTER"); root.Children.Add(after); Layout(root);
        Assert.Single(first.GetSourceRangeSegments(0, first.Text.Length)); Assert.Single(body.GetSourceRangeSegments(0, body.Text.Length));
        Assert.Empty(after.GetSourceRangeSegments(0, after.Text.Length)); Assert.InRange(root.DesiredSize.Height, 47.9, 50.1);
        Assert.Equal(Visibility.Visible, after.Visibility); body.SelectAll(); Assert.Equal(CssParagraphWrapStyleTests.Words, body.SelectedText);
        Css.SetStyle(root, Css.GetStyle(root) + "line-clamp:none;"); Layout(root); Assert.Equal(3, body.GetSourceRangeSegments(0, body.Text.Length).Count);
        Assert.NotEmpty(after.GetSourceRangeSegments(0, after.Text.Length)); Assert.Same(inner, frame.Child); Assert.Same(body, Assert.Single(inner.Children));
    }

    [Fact]
    public void IndependentFormattingContextLinesDoNotConsumeTheOuterBudget()
    {
        var root = Root(1); var independent = new StackPanel(); Css.SetStyle(independent, "display:flow-root;");
        var inside = Text(CssParagraphWrapStyleTests.Words); independent.Children.Add(inside); root.Children.Add(independent);
        var retained = Text("M"); var suppressed = Text("AFTER"); root.Children.Add(retained); root.Children.Add(suppressed); Layout(root);
        Assert.Equal(3, inside.GetSourceRangeSegments(0, inside.Text.Length).Count); Assert.Single(retained.GetSourceRangeSegments(0, 1));
        Assert.Empty(suppressed.GetSourceRangeSegments(0, suppressed.Text.Length));
        Assert.InRange(root.DesiredSize.Height, 95.9, 100.1);
    }

    [Theory] [InlineData("left")] [InlineData("right")]
    public void TallFloatsDoNotExtendTheClampedHeight(string side)
    {
        var root = Root(2); var floating = new Border(); Css.SetStyle(floating, $"float:{side};width:30px;height:200px;background:#146342;"); root.Children.Add(floating);
        var body = Text(CssParagraphWrapStyleTests.Words); Css.SetStyle(body, "display:inline;"); root.Children.Add(body);
        var host = new Canvas(); host.Children.Add(root); Layout(host);
        Assert.Equal(2, body.GetSourceRangeSegments(0, body.Text.Length).Count);
        Assert.InRange(root.ActualHeight, 47.9, 50.1); Assert.Equal(Visibility.Visible, floating.Visibility);
        Assert.Null(floating.InputHitTest(new(15, 80)));
    }

    [Theory] [MemberData(nameof(Paths))]
    public void AnonymousMarkerHitsTheOriginalRetainedBoundary(string path)
    {
        using var f = new WrapStyleFixture(path, CssParagraphWrapStyleTests.Words);
        foreach (var wrap in new[] { "auto", "balance" })
        {
            f.SetStyle(wrap, extra: "line-clamp:2;"); f.Layout(); var last = f.Lines()[^1];
            var offset = 0;
            foreach (var text in f.Sources)
            {
                if (last.End > offset + text.Text.Length) { offset += text.Text.Length; continue; }
                var at = text.TransformToAncestor(f.Root);
                var hit = Assert.IsType<TextPointer>(text.GetPositionFromPoint(new(last.Right + 4 - at.X, last.Baseline - 3 - at.Y), false));
                Assert.Equal(last.End - offset, text.ContentStart.GetOffsetToPosition(hit)); break;
            }
            AssertOriginal(f);
        }
    }

    [Theory] [MemberData(nameof(Paths))]
    public void EmergencyBreaksAreNotRetreatPointsForTheBlockEllipsis(string path)
    {
        using var f = new WrapStyleFixture(path, new string('M', 50));
        f.SetStyle("auto", extra: "overflow-wrap:anywhere;line-clamp:1;"); f.Layout();
        Assert.Empty(f.Lines()); Assert.InRange(f.Root.DesiredSize.Height, 23.9, 26.1);
        var text = f.Sources[0]; var at = text.TransformToAncestor(f.Root);
        var hit = Assert.IsType<TextPointer>(text.GetPositionFromPoint(new(4 - at.X, 10 - at.Y), false));
        Assert.Equal(0, text.ContentStart.GetOffsetToPosition(hit)); AssertOriginal(f);
    }

    [Theory] [MemberData(nameof(Paths))]
    public void EmptyStringIsEquivalentToNoEllipsisAndWideMarkersDoNotAddLines(string path)
    {
        using var f = new WrapStyleFixture(path, CssParagraphWrapStyleTests.Words);
        f.SetStyle("auto", extra: "line-clamp:2 no-ellipsis;"); f.Layout(); var plain = f.Lines(); var height = f.Root.DesiredSize.Height;
        f.SetStyle("auto", extra: "line-clamp:2 ''; "); f.Layout(); Assert.Equal(plain, f.Lines()); Assert.Equal(height, f.Root.DesiredSize.Height);
        f.SetStyle("auto", extra: "line-clamp:2 'a very long custom continuation marker which does not fit';"); f.Layout();
        Assert.Single(f.Lines()); Assert.InRange(f.Root.DesiredSize.Height, height - .1, height + .1); AssertOriginal(f);
        foreach (var text in f.Sources)
        {
            var at = text.TransformToAncestor(f.Root);
            Assert.Null(text.GetPositionFromPoint(new(180 - at.X, 36 - at.Y), false));
        }
    }

    [Theory] [MemberData(nameof(Paths))]
    public void CssWideKeywordsRestoreTheLonghandsTogether(string path)
    {
        using var f = new WrapStyleFixture(path, CssParagraphWrapStyleTests.Words);
        foreach (var keyword in new[] { "initial", "unset", "revert", "revert-layer" })
        {
            f.SetStyle("auto", extra: $"line-clamp:1;line-clamp:{keyword};"); f.Layout(); Assert.Equal(3, f.Lines().Length);
        }
        f.SetStyle("auto", extra: "line-clamp:calc(1.5);"); f.Layout(); Assert.Equal(2, f.Lines().Length); AssertOriginal(f);
    }

    [Fact]
    public void PureAtomicInlineRowsUseTheSameLineBudgetAndMarkerRetreat()
    {
        var root = Root(1); var boxes = Enumerable.Range(0, 7).Select(_ => new Border()).ToArray();
        foreach (var box in boxes) { Css.SetStyle(box, "display:inline-block;width:48px;height:16px;background:green;"); root.Children.Add(box); }
        Layout(root); Assert.InRange(root.DesiredSize.Height, 23.9, 26.1);
        Assert.NotNull(boxes[0].InputHitTest(new(4, 4))); Assert.NotNull(boxes[1].InputHitTest(new(4, 4)));
        Assert.Null(boxes[2].InputHitTest(new(4, 4))); Assert.Null(boxes[6].InputHitTest(new(4, 4)));
        Assert.Null(boxes[2].InputHitTest(new(0, 0))); Assert.Null(boxes[6].InputHitTest(new(0, 0)));
        Assert.All(boxes, box => Assert.True(box.IsVisible));
        Css.SetStyle(root, Css.GetStyle(root) + "line-clamp:none;"); Layout(root);
        Assert.NotNull(boxes[6].InputHitTest(new(4, 4))); Assert.InRange(root.DesiredSize.Height, 71.9, 74.1);
    }

    [Fact]
    public void AnIndependentParagraphKeepsItsOwnBalancingWhileTheOuterFlowIsCounted()
    {
        using var paragraph = new WrapStyleFixture("block", CssParagraphWrapStyleTests.Words);
        paragraph.SetStyle("balance", extra: "display:flow-root;"); paragraph.Layout(); var expected = paragraph.Lines();
        var root = Root(1); root.Children.Add(paragraph.Root); root.Children.Add(Text("M")); root.Children.Add(Text("AFTER")); Layout(root);
        Assert.Equal(expected, paragraph.Lines());
    }

    [Fact]
    public void BalancingTallerRunsReevaluatesTheAutomaticClampPoint()
    {
        var text = new TextBlock();
        text.Inlines.Add(new Run("M") { FontSize = 36 }); text.Inlines.Add(new Run(" MMMM MMMM "));
        text.Inlines.Add(new Run("M") { FontSize = 36 }); text.Inlines.Add(new Run(" MMMM MMMM M MMMM MMMM"));
        const string style = "font-family:'Courier New';font-size:16px;line-height:normal;white-space:normal;width:150px;";
        Css.SetStyle(text, style + "line-clamp:2 no-ellipsis;"); Layout(text); var limit = text.DesiredSize.Height;
        Css.SetStyle(text, style + $"text-wrap:balance;line-clamp:2 auto no-ellipsis;max-height:{limit.ToString(System.Globalization.CultureInfo.InvariantCulture)}px;"); Layout(text);
        Assert.All(text.GetSourceRangeSegments(0, text.Text.Length), range => Assert.True(range.Baseline <= limit + .1, $"{range.Baseline} > {limit}"));
        Assert.Equal("M MMMM MMMM M MMMM MMMM M MMMM MMMM", text.Text);
    }

    [Theory] [InlineData("max-height:10px;")] [InlineData("height:10px;")]
    public void TheStartClampPointCanSuppressEveryLine(string constraint)
    {
        using var f = new WrapStyleFixture("block", CssParagraphWrapStyleTests.Words);
        f.SetStyle("auto", extra: "line-clamp:auto;" + constraint); f.Layout(); Assert.Empty(f.Lines()); AssertOriginal(f);
    }

    [Theory] [InlineData("block")] [InlineData("flex")] [InlineData("grid")] [InlineData("table-cell")]
    public void LayoutConsumersRetainTheClampedHeightAndRestoreIt(string display)
    {
        var text = Text(CssParagraphWrapStyleTests.Words); var box = Root(2); box.Children.Add(text);
        var parent = new StackPanel();
        if (display == "table-cell")
        {
            Css.SetStyle(parent, "display:table;width:150px;border-spacing:0;");
            var row = new StackPanel(); Css.SetStyle(row, "display:table-row;"); parent.Children.Add(row); row.Children.Add(box);
            Css.SetStyle(box, Css.GetStyle(box) + "display:table-cell;");
        }
        else { Css.SetStyle(parent, $"display:{display};width:150px;grid-template-columns:150px;align-items:start;"); parent.Children.Add(box); }
        var host = new Canvas(); host.Children.Add(parent); Layout(host);
        Assert.Equal(2, text.GetSourceRangeSegments(0, text.Text.Length).Count); Assert.InRange(parent.DesiredSize.Height, 47.9, 50.1);
        Css.SetStyle(box, Css.GetStyle(box) + "line-clamp:none;"); Layout(host);
        Assert.Equal(3, text.GetSourceRangeSegments(0, text.Text.Length).Count); Assert.InRange(parent.DesiredSize.Height, 71.9, 74.1);
    }

    [Theory]
    [InlineData("line-clamp", "2")]
    [InlineData("max-lines", "2")]
    [InlineData("block-ellipsis", "ellipsis")]
    [InlineData("continue", "collapse")]
    [InlineData("-webkit-line-clamp", "2")]
    public void StyleQueriesCompareComputedLonghandsAndExpandedShorthands(string property, string value)
    {
        var root = new StackPanel(); var probe = new Border(); Css.SetClass(probe, "probe"); root.Children.Add(probe);
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse($".probe{{opacity:.2;}} @container summary style({property}:{value}){{.probe{{opacity:.8;}}}}"));
        Css.SetStyle(root, $"container-name:summary;{property}:{value};"); Layout(root); Assert.Equal(.8, probe.Opacity, 5);
        Css.SetStyle(root, "container-name:summary;"); Layout(root); Assert.Equal(.2, probe.Opacity, 5);
    }

    [Fact]
    public void CollapsedContentRetainsSequentialFocusAndAccessibleOriginalNames()
    {
        Keyboard.Initialize(); Keyboard.ClearFocus();
        var before = new Button { Content = "摘要之前" }; var after = new Button { Content = "完整后续内容" };
        var root = Root(1); root.Children.Add(before); root.Children.Add(Text("M")); root.Children.Add(after);
        var window = new Window { Content = root, Width = 240, Height = 180, TitleBarStyle = WindowTitleBarStyle.Native };
        if (OperatingSystem.IsMacOS()) typeof(Window).GetMethod("SetMacOSVisibilityForDisplay", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(window, [Visibility.Visible]);
        try
        {
            Layout(window); Assert.True(before.Focus()); Assert.True(KeyboardNavigation.MoveFocus(before)); Assert.Same(after, Keyboard.FocusedElement);
            Assert.True(after.IsVisible); Assert.Null(after.InputHitTest(new(4, 4)));
            Assert.Null(after.InputHitTest(new(0, 0)));
            Assert.Equal("完整后续内容", FrameworkElementAutomationPeer.CreatePeerForElement(after)!.GetName());
            Css.SetStyle(root, Css.GetStyle(root) + "line-clamp:none;"); Layout(window); Assert.Same(after, Keyboard.FocusedElement);
            Assert.True(KeyboardNavigation.MoveFocus(after, reverse: true)); Assert.Same(before, Keyboard.FocusedElement);
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    public static IEnumerable<object[]> UnicodeCases()
    {
        foreach (var path in CssParagraphWrapStyleTests.Paths)
        foreach (var words in new[] { "春风吹过山林，也吹过河岸。我们沿着小路慢慢走，再读一段新的旅程。", "Ma\u0301 Ma\u0301 Ma\u0301 Ma\u0301 Ma\u0301 Ma\u0301 Ma\u0301 Ma\u0301", "M👩‍👩‍👧‍👦 M🇨🇳 M👩‍👩‍👧‍👦 M🇨🇳 M👩‍👩‍👧‍👦 M🇨🇳", "MM\u00a0MM MM\u2060MM MM MM MM\u00a0MM MM MM MM" })
        foreach (var wrap in new[] { "auto", "balance" }) yield return [path, words, wrap];
    }

    [Theory] [MemberData(nameof(UnicodeCases))]
    public void RetainedUnicodeClustersKeepTheirOriginalSourceHitOffsets(string path, string words, string wrap)
    {
        using var f = new WrapStyleFixture(path, words); f.SetStyle(wrap, 90, "line-clamp:2;"); f.Layout(); AssertOriginal(f);
        Assert.InRange(f.Root.DesiredSize.Height, 23.9, 50.1);
        foreach (var text in f.Sources)
        {
            var starts = System.Globalization.StringInfo.ParseCombiningCharacters(text.Text).Append(text.Text.Length).ToArray();
            foreach (var (start, end) in starts.Zip(starts.Skip(1)))
            {
                if (text.Text.AsSpan(start, end - start).Trim().IsEmpty) continue;
                foreach (var range in text.GetSourceRangeSegments(start, end - start))
                {
                    var hit = Assert.IsType<TextPointer>(text.GetPositionFromPoint(new(range.StartX + .1, range.Baseline - 2), false));
                    Assert.Equal(start, text.ContentStart.GetOffsetToPosition(hit));
                }
            }
        }
    }

    [Fact]
    public void GeneratedInlineContentParticipatesWithoutBecomingOriginalSourceText()
    {
        var root = Root(2); var text = Text(CssParagraphWrapStyleTests.Words); Css.SetClass(text, "source"); root.Children.Add(text);
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(".source::before{content:'MMMM ';}")); Layout(root);
        var ranges = text.GetSourceRangeSegments(0, text.Text.Length); Assert.Equal(2, ranges.Count);
        var last = ranges[^1]; var hit = Assert.IsType<TextPointer>(text.GetPositionFromPoint(new(last.EndX + 4, last.Baseline - 2), false));
        Assert.True(text.ContentStart.GetOffsetToPosition(hit) < text.Text.Length);
        text.SelectAll(); Assert.Equal(CssParagraphWrapStyleTests.Words, text.SelectedText);
        Css.SetStyle(root, Css.GetStyle(root) + "line-clamp:none;"); Layout(root); Assert.Equal(3, text.GetSourceRangeSegments(0, text.Text.Length).Count);
    }

    [Fact]
    public void AnIndependentGeneratedParagraphDoesNotConsumeTheOuterLineCount()
    {
        var root = Root(1); var text = Text(CssParagraphWrapStyleTests.Words); Css.SetClass(text, "source");
        Css.SetStyle(text, "display:flow-root;"); root.Children.Add(text); var retained = Text("M"); root.Children.Add(retained); var after = Text("AFTER"); root.Children.Add(after);
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(".source::before{content:'MMMM ';}")); Layout(root);
        Assert.Equal(3, text.GetSourceRangeSegments(0, text.Text.Length).Count); Assert.Single(retained.GetSourceRangeSegments(0, 1));
        Assert.Empty(after.GetSourceRangeSegments(0, after.Text.Length)); Assert.InRange(root.DesiredSize.Height, 95.9, 98.1);
    }

    [Fact]
    public void AZeroAreaFloatClipRejectsItsOrigin()
    {
        var root = Root(1); var floating = new Border(); Css.SetStyle(floating, "float:left;width:30px;height:200px;background:green;"); root.Children.Add(floating);
        var host = new Canvas(); host.Children.Add(root); Layout(host);
        Assert.Equal(0, root.ActualHeight); Assert.Null(floating.InputHitTest(new(0, 0))); Assert.Null(floating.InputHitTest(new(4, 4)));
        Css.SetStyle(root, Css.GetStyle(root) + "line-clamp:none;"); Layout(host);
        Assert.InRange(root.ActualHeight, 199.9, 200.1); Assert.Same(floating, floating.InputHitTest(new(0, 0)));
    }

    private static StackPanel Root(int lines)
    {
        var root = new StackPanel(); Css.SetStyle(root, $"display:block;width:150px;font-family:'Courier New';font-size:16px;line-height:24px;white-space:normal;line-clamp:{lines};"); return root;
    }
    private static TextBlock Text(string words) { var text = new TextBlock { Text = words, IsTextSelectionEnabled = true }; Css.SetStyle(text, "display:block;"); return text; }
    private static void Layout(FrameworkElement root) => WrapStyleFixture.Layout(root, 400);
    private static void AssertOriginal(WrapStyleFixture f)
    {
        Assert.Equal(f.Words, string.Concat(f.Sources.Select(text => text.Text)));
        foreach (var text in f.Sources) { text.SelectAll(); Assert.Equal(text.Text, text.SelectedText); text.ClearSelection(); }
        if (f.Runs.Count > 0) Assert.Equal(f.Runs, f.Sources[0].Inlines.Cast<Run>());
    }
}

// Grammar cases transcribed from css/css-overflow/parsing in web-platform-tests.
// The downloaded sources and checksums are in artifacts/css-line-clamp-20261018/sources/wpt.
public sealed class CssLineClampGrammarTests
{
    public static IEnumerable<object[]> WptGrammar()
    {
        yield return ["block-ellipsis", "hidden", false];
        yield return ["block-ellipsis", "none", false];
        yield return ["block-ellipsis", "auto", false];
        yield return ["block-ellipsis", "none auto", false];
        yield return ["block-ellipsis", "no-ellipsis auto", false];
        yield return ["block-ellipsis", "ellipsis auto", false];
        yield return ["block-ellipsis", "auto \"string\"", false];
        yield return ["block-ellipsis", "ellipsis \"string\"", false];
        yield return ["block-ellipsis", "\"string\" none", false];
        yield return ["block-ellipsis", "\"string\" no-ellipsis", false];
        yield return ["block-ellipsis", "\"first\" \"second\"", false];
        yield return ["block-ellipsis", "no-ellipsis", true];
        yield return ["block-ellipsis", "ellipsis", true];
        yield return ["block-ellipsis", "\" etc., etc. \"", true];
        yield return ["continue", "none", false];
        yield return ["continue", "auto", false];
        yield return ["continue", "auto discard", false];
        yield return ["continue", "auto collapse", false];
        yield return ["continue", "auto -webkit-legacy", false];
        yield return ["continue", "normal discard", false];
        yield return ["continue", "normal collapse", false];
        yield return ["continue", "normal -webkit-legacy", false];
        yield return ["continue", "collapse -webkit-legacy", false];
        yield return ["continue", "normal", true];
        yield return ["continue", "discard", true];
        yield return ["continue", "collapse", true];
        yield return ["continue", "-webkit-legacy", true];
        yield return ["line-clamp", "0", false];
        yield return ["line-clamp", "-5", false];
        yield return ["line-clamp", "none 2", false];
        yield return ["line-clamp", "none no-ellipsis", false];
        yield return ["line-clamp", "3 none", false];
        yield return ["line-clamp", "-webkit-legacy", false];
        yield return ["line-clamp", "0 -webkit-legacy", false];
        yield return ["line-clamp", "none", true];
        yield return ["line-clamp", "1", true];
        yield return ["line-clamp", "6", true];
        yield return ["line-clamp", "auto", true];
        yield return ["line-clamp", "ellipsis", true];
        yield return ["line-clamp", "\" etc., etc. \"", true];
        yield return ["line-clamp", "7 no-ellipsis", true];
        yield return ["line-clamp", "8 auto", true];
        yield return ["line-clamp", "8 ellipsis", true];
        yield return ["line-clamp", "9 \" etc., etc. \"", true];
        yield return ["line-clamp", "no-ellipsis 10", true];
        yield return ["line-clamp", "auto 11", true];
        yield return ["line-clamp", "ellipsis 11", true];
        yield return ["line-clamp", "\" etc., etc. \" 12", true];
        yield return ["line-clamp", "3 ellipsis auto", true];
        yield return ["line-clamp", "1 -webkit-legacy", true];
        yield return ["line-clamp", "auto -webkit-legacy", true];
        yield return ["line-clamp", "no-ellipsis -webkit-legacy", true];
        yield return ["line-clamp", "3 auto -webkit-legacy", true];
        yield return ["line-clamp", "3 ellipsis auto -webkit-legacy", true];
        yield return ["max-lines", "none", false];
        yield return ["max-lines", "0", false];
        yield return ["max-lines", "-5", false];
        yield return ["max-lines", "none none", false];
        yield return ["max-lines", "auto auto", false];
        yield return ["max-lines", "1 none", false];
        yield return ["max-lines", "none 2", false];
        yield return ["max-lines", "3 4", false];
        yield return ["max-lines", "auto", true];
        yield return ["max-lines", "1", true];
        yield return ["max-lines", "6", true];
        yield return ["max-lines", "7 auto", true];
        yield return ["max-lines", "auto 8", true];
    }
    [Theory] [MemberData(nameof(WptGrammar))]
    public void AcceptsTheCurrentWptValueGrammar(string property, string value, bool valid)
        => Assert.Equal(valid, CssAuthoring.IsValueSupported(property, value));

    [Theory]
    [InlineData("line-clamp", "none", false)]
    [InlineData("-webkit-line-clamp", "none", false)]
    [InlineData("max-lines", "auto", false)]
    [InlineData("block-ellipsis", "no-ellipsis", true)]
    [InlineData("continue", "normal", false)]
    [InlineData("-webkit-box-orient", "horizontal", false)]
    public void AuthoringMetadataMatchesTheRealInitialAndInheritance(string name, string initial, bool inherited)
    {
        var property = CssAuthoring.GetProperty(name);
        Assert.Equal(CssPropertySupport.Supported, property.Support);
        Assert.Equal(initial, property.InitialValue); Assert.Equal(inherited, property.IsInherited);
        Assert.Contains(initial, property.Values);
    }

    [Theory]
    [InlineData("line-clamp", "2 auto auto", false)]
    [InlineData("line-clamp", "auto 2 3", false)]
    [InlineData("line-clamp", "2 ellipsis no-ellipsis", false)]
    [InlineData("line-clamp", "-webkit-legacy 2", false)]
    [InlineData("line-clamp", "2 -webkit-legacy ellipsis", false)]
    [InlineData("line-clamp", "2 -webkit-legacy -webkit-legacy", false)]
    [InlineData("line-clamp", "2.0", false)]
    [InlineData("line-clamp", "2e0", false)]
    [InlineData("line-clamp", "calc(1 + 1)", true)]
    [InlineData("line-clamp", "calc(0)", true)]
    [InlineData("line-clamp", "calc(1.5)", true)]
    [InlineData("line-clamp", "calc(2px)", false)]
    [InlineData("line-clamp", "2 \"\"", true)]
    [InlineData("max-lines", "auto 2147483647", true)]
    [InlineData("-webkit-line-clamp", "auto", false)]
    [InlineData("-webkit-line-clamp", "2 ellipsis", false)]
    [InlineData("-webkit-line-clamp", "calc(2)", true)]
    public void RejectsDuplicateAndOutOfOrderTermsAndUsesCalculatedIntegers(string name, string value, bool valid)
        => Assert.Equal(valid, CssAuthoring.IsValueSupported(name, value));
}

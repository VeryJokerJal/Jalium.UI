using Jalium.UI.Automation;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssInlineClampSplitTests : IDisposable
{
    private readonly Interop.RenderContext _context = Interop.RenderContext.GetOrCreateCurrent(Interop.RenderBackend.Metal);
    private const string Font = "font-family:'Courier New';font-size:16px;line-height:24px;color:#146342;";

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(5, false)]
    [InlineData(12, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(5, true)]
    [InlineData(12, true)]
    public void AnInlineSectionKeepsTheFirstBlockTextWhenItsOuterFlowIsClamped(int count, bool decorated)
    {
        using var f = Create(decorated);
        var original = f.Document.GetText();
        Root(f, count); f.Layout(180, 600);
        foreach (var provider in new[] { f.Provider, f.ReferenceProvider })
        {
            Assert.StartsWith("AA", provider.DocumentRange.GetText(-1));
            var first = provider.DocumentRange.FindText("AA", false, false);
            Assert.NotNull(first);
            var rectangles = CssRichTextBoxTests.Fixture.Bounds(first);
            Assert.NotEmpty(rectangles);
            Assert.All(rectangles, rectangle => Assert.Equal(24, rectangle.Height, 4));
        }
        Assert.Equal(original, f.Document.GetText()); f.OriginalNodes();
        var start = f.Document.GetDocumentOffset(f.Runs[0], 1);
        Assert.True(((IImeSupport)f.Editor).TrySetImeSelection(start, 0));
        var expected = CssRichTextBoxTests.Fixture.Bounds(f.Provider.DocumentRange.FindText("AA", false, false)!).Single();
        Assert.Equal(expected.Top, f.Editor.GetImeCaretRectangle().Top, 4);
        Assert.Equal(24, f.Editor.GetImeCaretRectangle().Height, 4);
        var point = new Point(expected.Left + 2, expected.Top + expected.Height / 2);
        Assert.InRange(f.Editor.GetPositionFromPoint(point, true)!.DocumentOffset, 0, 2);

        Root(f, null); f.Layout(180, 600);
        Assert.Contains("MM NN OO", f.Provider.DocumentRange.GetText(-1));
        Assert.Contains("ZZ", f.Provider.DocumentRange.GetText(-1));
        f.SameTextAndGeometry(); f.OriginalNodes();
        Assert.Equal(original, f.Document.GetText());
    }

    [Theory]
    [InlineData(2, "auto")]
    [InlineData(3, "auto")]
    [InlineData(2, "balance")]
    [InlineData(3, "balance")]
    [InlineData(2, "pretty")]
    [InlineData(3, "pretty")]
    [InlineData(2, "avoid-short-last-line")]
    [InlineData(3, "avoid-short-last-line")]
    public void DiscardedFollowingBlocksStayHiddenWhileRetainedBlocksRemainVisible(int count, string wrap)
    {
        using var f = Create(true); var original = f.Document.GetText();
        Root(f, count, $"text-wrap-style:{wrap};"); f.Layout(180, 600);
        foreach (var provider in new[] { f.Provider, f.ReferenceProvider })
        {
            Assert.NotEmpty(CssRichTextBoxTests.Fixture.Bounds(provider.DocumentRange.FindText("AA", false, false)!));
            var tail = provider.DocumentRange.FindText("ZZ", false, false);
            if (tail is not null) Assert.Empty(tail.GetBoundingRectangles());
        }
        var lines = ((IAutomationTextViewSource)f.Editor.GetAutomationPeer()!).GetTextLines().Where(line => line.Bounds.Height > 0).ToArray();
        Assert.InRange(lines.Length, 1, count);
        var extent = (double)typeof(RichTextBox).GetMethod("GetVerticalScrollExtentHeight", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(f.Editor, [24d])!;
        Assert.InRange(extent, count * 24 - .001, count * 24 + .001);
        f.OriginalNodes(); Assert.Equal(original, f.Document.GetText());
        Root(f, null, $"text-wrap-style:{wrap};"); f.Layout(180, 600);
        Assert.NotEmpty(f.Provider.DocumentRange.FindText("ZZ", false, false)!.GetBoundingRectangles());
        Assert.Equal(original, f.Document.GetText()); f.OriginalNodes();
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("balance")]
    public void EditingTheRetainedPrefixKeepsHiddenSourcesAndUndoHistory(string wrap)
    {
        using var f = Create(true); Root(f, 2, $"text-wrap-style:{wrap};"); f.Layout(180, 600);
        var before = f.Document.GetText();
        Assert.NotEmpty(f.Provider.DocumentRange.FindText("AA", false, false)!.GetBoundingRectangles());
        var input = (IImeSupport)f.Editor; Assert.True(input.TrySetImeSelection(0, 2)); Assert.True(input.TryReplaceImeText(0, 2, "新🙂"));
        f.Layout(180, 600); Assert.NotEmpty(f.Provider.DocumentRange.FindText("新", false, false)!.GetBoundingRectangles());
        Assert.Contains("MM NN OO", f.Document.GetText()); Assert.Contains("ZZ", f.Document.GetText());
        Assert.True(f.Editor.Undo()); Assert.Equal(before, f.Document.GetText()); f.Layout(180, 600); f.OriginalNodes();
        Assert.NotEmpty(f.Provider.DocumentRange.FindText("AA", false, false)!.GetBoundingRectangles());
        Assert.True(f.Editor.Redo()); Assert.StartsWith("新🙂", f.Document.GetText()); f.Layout(180, 600); f.OriginalNodes();
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("balance")]
    public void OrdinaryControlBlocksInsideAnInlineFrameShareTheSameClampPlan(string wrap)
    {
        var first = new TextBlock { Text = "AA BB CC DD EE FF GG HH II JJ KK LL", FontFamily = "Courier New", FontSize = 16, LineHeight = 24 };
        var last = new TextBlock { Text = "ZZ", FontFamily = "Courier New", FontSize = 16, LineHeight = 24 };
        Css.SetStyle(first, "display:block;white-space:normal;"); Css.SetStyle(last, "display:block;");
        var body = new StackPanel(); body.Children.Add(first); body.Children.Add(last); Css.SetStyle(body, "display:contents;");
        var frame = new Border { Child = body }; Css.SetStyle(frame, "display:inline;padding:2px;border:1px solid green;box-decoration-break:clone;");
        var root = new StackPanel(); root.Children.Add(frame);
        void Layout(int? count)
        {
            Css.SetStyle(root, Font + "display:block;width:90px;" + $"text-wrap-style:{wrap};" + (count is null ? "" : $"line-clamp:{count} no-ellipsis;"));
            for (var i = 0; i < 3; i++)
            {
                Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
                root.Measure(new(180, 600)); root.Arrange(new(0, 0, 180, 600));
            }
        }
        Layout(2);
        Assert.NotEmpty(first.GetSourceRangeSegments(0, 2)); Assert.Empty(last.GetSourceRangeSegments(0, 2));
        Assert.InRange(root.DesiredSize.Height, 47.999, 48.001);
        Layout(null); Assert.NotEmpty(last.GetSourceRangeSegments(0, 2));
        Assert.Equal("AA BB CC DD EE FF GG HH II JJ KK LL", first.Text); Assert.Equal("ZZ", last.Text);
        Assert.Same(body, frame.Child); Assert.Same(first, body.Children[0]); Assert.Same(last, body.Children[1]); Assert.Same(frame, Assert.Single(root.Children));
    }

    [Theory]
    [InlineData("auto", "ellipsis")]
    [InlineData("balance", "ellipsis")]
    [InlineData("auto", "'… 续'")]
    [InlineData("balance", "'… 续'")]
    public void TheEarlierInlineSegmentIsRetainedWithoutAnExtraEllipsis(string wrap, string marker)
    {
        TextBlock Text(string value, string display)
        {
            var text = new TextBlock { Text = value, FontFamily = "Courier New", FontSize = 16, LineHeight = 24 };
            Css.SetStyle(text, $"display:{display};white-space:normal;"); return text;
        }
        var head = Text("MMMM MMM", "inline"); var block = Text("AA BB CC DD EE FF GG HH II", "block"); var tail = Text("TAIL", "inline");
        var body = new StackPanel(); body.Children.Add(head); body.Children.Add(block); body.Children.Add(tail); Css.SetStyle(body, "display:contents;");
        var frame = new Border { Child = body }; Css.SetStyle(frame, "display:inline;box-decoration-break:clone;padding:2px;border:1px solid green;");
        var root = new StackPanel(); root.Children.Add(frame);
        Css.SetStyle(root, Font + $"display:block;width:90px;line-clamp:2 {marker};text-wrap-style:{wrap};");
        for (var i = 0; i < 3; i++)
        {
            Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
            root.Measure(new(180, 600)); root.Arrange(new(0, 0, 180, 600));
        }
        Assert.NotEmpty(head.GetSourceRangeSegments(5, 3));
        Assert.NotEmpty(block.GetSourceRangeSegments(0, 2));
        Assert.Empty(tail.GetSourceRangeSegments(0, tail.Text.Length));
        Assert.InRange(root.DesiredSize.Height, 47.999, 48.001);
        Assert.Equal("MMMM MMM", head.Text); Assert.Equal("TAIL", tail.Text);
        Assert.Same(head, body.Children[0]); Assert.Same(block, body.Children[1]); Assert.Same(tail, body.Children[2]);
    }

    private static CssRichTextBoxTests.Fixture Create(bool decorated)
    {
        var f = new CssRichTextBoxTests.Fixture("section");
        f.Runs[0].Text = "AA BB CC "; f.Runs[1].Text = "DD EE FF ";
        f.Runs[2].Text = "GG HH II "; f.Runs[3].Text = "JJ KK LL MM NN OO";
        f.Style(decorated ? "display:inline;box-decoration-break:clone;padding:2px;border:1px solid green;background:#eef5f0;" : "display:inline;");
        return f;
    }
    private static void Root(CssRichTextBoxTests.Fixture f, int? count, string extra = "") => Css.SetStyle(f.Document,
        Font + "display:block;width:90px;" + extra + (count is null ? "" : $"line-clamp:{count} no-ellipsis;"));
    public void Dispose() { Keyboard.ClearFocus(); _context.Dispose(); }
}

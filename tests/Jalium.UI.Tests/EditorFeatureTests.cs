using Jalium.UI;
using Jalium.UI.Controls.Editor;

namespace Jalium.UI.Tests;

public class EditorFeatureTests
{
    [Fact]
    public void EditorView_GetOffsetFromPoint_NegativeX_ShouldClampToLineStart()
    {
        var doc = new TextDocument("abcd\nefgh");
        var view = new EditorView { Document = doc };
        view.UpdateLayout("Cascadia Code", 14);

        int offset = view.GetOffsetFromPoint(new Point(-100, 0), showLineNumbers: false);

        Assert.Equal(0, offset);
    }

    [Fact]
    public void EditorView_GetOffsetFromPoint_LargeX_ShouldClampToLineEnd()
    {
        var doc = new TextDocument("abcd\nefgh");
        var view = new EditorView { Document = doc };
        view.UpdateLayout("Cascadia Code", 14);

        int offset = view.GetOffsetFromPoint(new Point(10000, 0), showLineNumbers: false);

        Assert.Equal(4, offset);
    }

    [Fact]
    public void EditorView_GetOffsetFromPoint_LargeY_ShouldClampToLastLine()
    {
        var doc = new TextDocument("a\nbb\nccc");
        var view = new EditorView { Document = doc };
        view.UpdateLayout("Cascadia Code", 14);

        int offset = view.GetOffsetFromPoint(new Point(0, 10000), showLineNumbers: false);
        var line = doc.GetLineByOffset(offset);
        Assert.Equal(3, line.LineNumber);
    }

    [Fact]
    public void EditorView_WhenLineHeightIsZero_ShouldExposeSafeVisibleLines()
    {
        var doc = new TextDocument("a\nb\nc");
        var view = new EditorView { Document = doc };

        Assert.Equal(1, view.FirstVisibleLineNumber);
        Assert.Equal(3, view.LastVisibleLineNumber);
        Assert.True(view.VisibleLineCount >= 1);
    }

    [Fact]
    public void EditorView_TotalContentHeight_WhenLineHeightIsZero_ShouldStillBePositive()
    {
        var doc = new TextDocument("a\nb\nc");
        var view = new EditorView { Document = doc };

        Assert.True(view.TotalContentHeight > 0);
    }

    [Fact]
    public void FindReplaceManager_FindNext_ShouldWrap()
    {
        var doc = new TextDocument("foo bar foo");
        var mgr = new FindReplaceManager
        {
            Document = doc,
            SearchText = "foo"
        };

        var r1 = mgr.FindNext(0);
        var r2 = mgr.FindNext(4);
        var r3 = mgr.FindNext(100);

        Assert.Equal(0, r1!.Value.Offset);
        Assert.Equal(8, r2!.Value.Offset);
        Assert.Equal(0, r3!.Value.Offset);
    }

    [Fact]
    public void FindReplaceManager_FindPrevious_ShouldWrap()
    {
        var doc = new TextDocument("foo bar foo");
        var mgr = new FindReplaceManager
        {
            Document = doc,
            SearchText = "foo"
        };

        var r1 = mgr.FindPrevious(8);
        var r2 = mgr.FindPrevious(0);

        Assert.Equal(0, r1!.Value.Offset);
        Assert.Equal(8, r2!.Value.Offset);
    }

    [Fact]
    public void FindReplaceManager_ReplaceAll_ShouldReturnCount()
    {
        var doc = new TextDocument("a a a");
        var mgr = new FindReplaceManager
        {
            Document = doc,
            SearchText = "a"
        };

        int replaced = mgr.ReplaceAll("b");

        Assert.Equal(3, replaced);
        Assert.Equal("b b b", doc.Text);
    }

    [Fact]
    public void BraceFoldingStrategy_ShouldCreateSectionsForMultilineBraces()
    {
        var doc = new TextDocument("{\n  x\n}\n");
        var strategy = new BraceFoldingStrategy();

        var sections = strategy.CreateFoldings(doc).ToList();

        Assert.Single(sections);
        Assert.Equal(1, sections[0].StartLine);
        Assert.Equal(3, sections[0].EndLine);
    }

    [Fact]
    public void BraceFoldingStrategy_ShouldCaptureStartColumnAndTrimFoldTitle()
    {
        var doc = new TextDocument("else {\n  x\n}\n");
        var strategy = new BraceFoldingStrategy();

        var sections = strategy.CreateFoldings(doc).ToList();

        Assert.Single(sections);
        Assert.Equal(5, sections[0].StartColumn);
        Assert.Equal("else", sections[0].Title);
    }

    [Fact]
    public void BraceFoldingStrategy_AllmanBraceLine_ShouldAnchorToPreviousDeclarationLine()
    {
        var doc = new TextDocument("public class MainWindow\n{\n    int x;\n}\n");
        var strategy = new BraceFoldingStrategy();

        var sections = strategy.CreateFoldings(doc).ToList();

        Assert.Single(sections);
        Assert.Equal(1, sections[0].StartLine);
        Assert.Equal(4, sections[0].EndLine);
        Assert.Equal("public class MainWindow".Length, sections[0].StartColumn);
        Assert.Equal(2, sections[0].GuideStartLine);
        Assert.Equal(4, sections[0].GuideEndLine);
    }

    [Fact]
    public void BraceFoldingStrategy_ShouldCreateSectionsForRegionDirectives()
    {
        var doc = new TextDocument("class C\n{\n    #region Test Region\n    int value;\n    #endregion\n}\n");
        var strategy = new BraceFoldingStrategy();

        var sections = strategy.CreateFoldings(doc).ToList();

        FoldingSection? region = null;
        for (int i = 0; i < sections.Count; i++)
        {
            if (sections[i].StartLine == 3 && sections[i].EndLine == 5)
            {
                region = sections[i];
                break;
            }
        }

        Assert.NotNull(region);
        Assert.Equal(4, region!.StartColumn);
        Assert.Equal("#region Test Region", region.Title);
        Assert.Equal(3, region.GuideStartLine);
        Assert.Equal(5, region.GuideEndLine);
    }

    [Fact]
    public void BraceFoldingStrategy_ShouldSupportNestedRegionDirectives()
    {
        var doc = new TextDocument("#region A\n#region B\nx\n#endregion\n#endregion\n");
        var strategy = new BraceFoldingStrategy();

        var sections = strategy.CreateFoldings(doc).ToList();

        bool hasOuter = false;
        bool hasInner = false;
        for (int i = 0; i < sections.Count; i++)
        {
            var section = sections[i];
            if (section.StartLine == 1 && section.EndLine == 5)
                hasOuter = true;
            if (section.StartLine == 2 && section.EndLine == 4)
                hasInner = true;
        }

        Assert.True(hasOuter);
        Assert.True(hasInner);
    }

    [Fact]
    public void XmlFoldingStrategy_ShouldCreateSectionsForNestedElements()
    {
        var doc = new TextDocument("<Grid>\n  <StackPanel>\n    <TextBlock />\n  </StackPanel>\n</Grid>\n");
        var strategy = new XmlFoldingStrategy();

        var sections = strategy.CreateFoldings(doc).ToList();

        Assert.Equal(2, sections.Count);
        var outer = sections.Single(s => s.StartLine == 1 && s.EndLine == 5);
        var inner = sections.Single(s => s.StartLine == 2 && s.EndLine == 4);

        Assert.Equal(0, outer.StartColumn);
        Assert.Equal(1, outer.GuideStartLine);
        Assert.Equal(5, outer.GuideEndLine);
        Assert.Equal(2, inner.StartColumn);
    }

    [Fact]
    public void XmlFoldingStrategy_MultiLineStartTag_ShouldSetGuideStartToTagCloseLine()
    {
        var doc = new TextDocument("<Grid\n    Margin=\"12\">\n    <TextBlock />\n</Grid>\n");
        var strategy = new XmlFoldingStrategy();

        var sections = strategy.CreateFoldings(doc).ToList();

        Assert.Single(sections);
        var section = sections[0];
        Assert.Equal(1, section.StartLine);
        Assert.Equal(4, section.EndLine);
        Assert.Equal(2, section.GuideStartLine);
        Assert.Equal(4, section.GuideEndLine);
    }

    [Fact]
    public void XmlFoldingStrategy_ShouldIgnoreSelfClosingElements()
    {
        var doc = new TextDocument("<Root>\n  <Item />\n</Root>\n");
        var strategy = new XmlFoldingStrategy();

        var sections = strategy.CreateFoldings(doc).ToList();

        Assert.Single(sections);
        Assert.Equal(1, sections[0].StartLine);
        Assert.Equal(3, sections[0].EndLine);
    }

    [Fact]
    public void XmlFoldingStrategy_ShouldFoldMultilineComments()
    {
        var doc = new TextDocument("<!--\n  line\n-->\n");
        var strategy = new XmlFoldingStrategy();

        var sections = strategy.CreateFoldings(doc).ToList();

        Assert.Single(sections);
        Assert.Equal(1, sections[0].StartLine);
        Assert.Equal(3, sections[0].EndLine);
        Assert.Equal(0, sections[0].StartColumn);
    }

    [Fact]
    public void JalxamlSyntaxHighlighter_NumericAttributeValues_ShouldUseNumberClassification()
    {
        var highlighter = JalxamlSyntaxHighlighter.Create();
        const string line = "<Grid Margin=\"0,0\" Width=\"0\" Text=\"Hello\" />";

        var (tokens, _) = highlighter.HighlightLine(1, line, highlighter.GetInitialState());

        Assert.Contains(tokens, t =>
            t.Classification == TokenClassification.Number &&
            line.AsSpan(t.StartOffset, t.Length).SequenceEqual("0,0"));
        Assert.Contains(tokens, t =>
            t.Classification == TokenClassification.Number &&
            line.AsSpan(t.StartOffset, t.Length).SequenceEqual("0"));
    }

    [Fact]
    public void JalxamlSyntaxHighlighter_NonNumericAttributeValue_ShouldRemainString()
    {
        var highlighter = JalxamlSyntaxHighlighter.Create();
        const string line = "<Grid Margin=\"0,Auto\" />";

        var (tokens, _) = highlighter.HighlightLine(1, line, highlighter.GetInitialState());

        Assert.DoesNotContain(tokens, t =>
            t.Classification == TokenClassification.Number &&
            line.AsSpan(t.StartOffset, t.Length).SequenceEqual("0,Auto"));
    }

    [Fact]
    public void FoldingManager_ToggleFold_ShouldChangeState()
    {
        var doc = new TextDocument("{\n  x\n}\n");
        var strategy = new BraceFoldingStrategy();
        var manager = new FoldingManager { Document = doc };
        manager.UpdateFoldings(strategy);

        bool toggled = manager.ToggleFold(1);
        Assert.True(toggled);

        var section = manager.GetFoldingAt(1);
        Assert.NotNull(section);
        Assert.True(section!.IsFolded);
    }

    [Fact]
    public void FoldingManager_Version_ShouldIncreaseWhenStructureOrStateChanges()
    {
        var doc = new TextDocument("{\n  x\n}\n");
        var strategy = new BraceFoldingStrategy();
        var manager = new FoldingManager { Document = doc };

        int initialVersion = manager.Version;
        manager.UpdateFoldings(strategy);
        int afterBuild = manager.Version;
        Assert.True(afterBuild > initialVersion);

        Assert.True(manager.ToggleFold(1));
        int afterToggle = manager.Version;
        Assert.True(afterToggle > afterBuild);

        manager.ExpandAll();
        int afterExpand = manager.Version;
        Assert.True(afterExpand > afterToggle);
    }

    [Fact]
    public void EditorView_GetOffsetFromPoint_WithLineNumbersInGutter_ShouldClampToLineStart()
    {
        var doc = new TextDocument("abcd\nefgh");
        var view = new EditorView { Document = doc };
        view.UpdateLayout("Cascadia Code", 14);

        int offset = view.GetOffsetFromPoint(new Point(0, 0), showLineNumbers: true);

        Assert.Equal(0, offset);
    }

    [Fact]
    public void EditorView_GetPointFromOffset_WithoutLayout_ShouldReturnZero()
    {
        var doc = new TextDocument("abcd");
        var view = new EditorView { Document = doc };

        var point = view.GetPointFromOffset(2, showLineNumbers: false);

        Assert.Equal(Point.Zero, point);
    }

    [Fact]
    public void EditorView_GetPointFromOffset_AfterLayout_ShouldIncreaseYAcrossLines()
    {
        var doc = new TextDocument("a\nb");
        var view = new EditorView { Document = doc };
        view.UpdateLayout("Cascadia Code", 14);

        var firstLinePoint = view.GetPointFromOffset(0, showLineNumbers: false);
        var secondLinePoint = view.GetPointFromOffset(2, showLineNumbers: false);

        Assert.True(secondLinePoint.Y > firstLinePoint.Y);
    }

    [Fact]
    public void EditorView_VisibleLineCount_WithoutDocument_ShouldBeOne()
    {
        var view = new EditorView();
        Assert.Equal(1, view.VisibleLineCount);
    }

    [Fact]
    public void FindReplaceManager_CaseSensitive_ShouldRespectCase()
    {
        var doc = new TextDocument("Foo foo");
        var mgr = new FindReplaceManager
        {
            Document = doc,
            SearchText = "foo",
            CaseSensitive = true
        };

        mgr.FindAll();

        Assert.Single(mgr.Results);
        Assert.Equal(4, mgr.Results[0].Offset);
    }

    [Fact]
    public void FindReplaceManager_WholeWord_ShouldExcludePartials()
    {
        var doc = new TextDocument("cat scatter cat");
        var mgr = new FindReplaceManager
        {
            Document = doc,
            SearchText = "cat",
            WholeWord = true
        };

        mgr.FindAll();

        Assert.Equal(2, mgr.Results.Count);
        Assert.Equal(0, mgr.Results[0].Offset);
        Assert.Equal(12, mgr.Results[1].Offset);
    }

    [Fact]
    public void FindReplaceManager_UseRegex_ShouldMatchPattern()
    {
        var doc = new TextDocument("a1 a22 b333");
        var mgr = new FindReplaceManager
        {
            Document = doc,
            SearchText = "a\\d+",
            UseRegex = true
        };

        mgr.FindAll();

        Assert.Equal(2, mgr.Results.Count);
        Assert.Equal(0, mgr.Results[0].Offset);
        Assert.Equal(3, mgr.Results[1].Offset);
    }

    [Fact]
    public void FindReplaceManager_ReplaceCurrent_ShouldReplaceOnlyCurrentMatch()
    {
        var doc = new TextDocument("foo foo");
        var mgr = new FindReplaceManager
        {
            Document = doc,
            SearchText = "foo"
        };

        mgr.FindAll();
        bool replaced = mgr.ReplaceCurrent("bar");

        Assert.True(replaced);
        Assert.Equal("bar foo", doc.Text);
    }

    [Fact]
    public void FindReplaceManager_DocumentMutation_ShouldInvalidateCachedResults()
    {
        var doc = new TextDocument("x x");
        var mgr = new FindReplaceManager
        {
            Document = doc,
            SearchText = "x"
        };

        mgr.FindAll();
        Assert.Equal(2, mgr.Results.Count);

        doc.Insert(0, "x ");

        Assert.Empty(mgr.Results);
        Assert.Null(mgr.CurrentResult);
    }

    [Fact]
    public void FoldingManager_CollapseExpandAll_ShouldToggleHasFoldedSections()
    {
        var doc = new TextDocument("{\n  x\n}\n");
        var manager = new FoldingManager { Document = doc };
        manager.UpdateFoldings(new BraceFoldingStrategy());

        manager.CollapseAll();
        Assert.True(manager.HasFoldedSections);

        manager.ExpandAll();
        Assert.False(manager.HasFoldedSections);
    }

    [Fact]
    public void FoldingManager_UpdateFoldings_ShouldPreserveFoldedState()
    {
        var doc = new TextDocument("{\n  x\n}\n");
        var manager = new FoldingManager { Document = doc };
        var strategy = new BraceFoldingStrategy();

        manager.UpdateFoldings(strategy);
        Assert.True(manager.ToggleFold(1));
        Assert.True(manager.GetFoldingAt(1)!.IsFolded);

        manager.UpdateFoldings(strategy);

        Assert.True(manager.GetFoldingAt(1)!.IsFolded);
    }

    [Fact]
    public void FoldingManager_IsLineHidden_ShouldRespectFoldState()
    {
        var doc = new TextDocument("{\n  x\n}\n");
        var manager = new FoldingManager { Document = doc };
        manager.UpdateFoldings(new BraceFoldingStrategy());

        Assert.True(manager.ToggleFold(1));
        Assert.False(manager.IsLineHidden(1));
        Assert.True(manager.IsLineHidden(2));
        Assert.True(manager.IsLineHidden(3));
    }

    [Fact]
    public void EditorView_GetPointFromOffset_WhenLineHidden_ShouldProjectToFoldHeader()
    {
        var doc = new TextDocument("{\n  x\n}\nnext");
        var manager = new FoldingManager { Document = doc };
        manager.UpdateFoldings(new BraceFoldingStrategy());
        Assert.True(manager.ToggleFold(1));

        var view = new EditorView
        {
            Document = doc,
            Folding = manager
        };
        view.UpdateLayout("Cascadia Code", 14);

        var headerPoint = view.GetPointFromOffset(doc.GetLineByNumber(1).Offset, showLineNumbers: false);
        var hiddenPoint = view.GetPointFromOffset(doc.GetLineByNumber(2).Offset, showLineNumbers: false);
        Assert.Equal(headerPoint.Y, hiddenPoint.Y);

        int nextVisibleLine = manager.GetFoldingAt(1)!.EndLine + 1;
        var nextVisiblePoint = view.GetPointFromOffset(doc.GetLineByNumber(nextVisibleLine).Offset, showLineNumbers: false);
        Assert.True(nextVisiblePoint.Y > hiddenPoint.Y);
    }

    [Fact]
    public void EditorView_InvalidateFromLine_ShouldPreserveEarlierHighlightState()
    {
        var doc = new TextDocument("a\nb\nc\nd");
        var highlighter = new CountingSyntaxHighlighter();
        var view = new EditorView
        {
            Document = doc,
            Highlighter = highlighter
        };
        view.UpdateLayout("Cascadia Code", 14);

        int line4Offset = doc.GetLineByNumber(4).Offset;
        Assert.True(view.TryGetTokenAtOffset(line4Offset, out _, out _, out _));
        int firstPassCalls = highlighter.HighlightCallCount;
        Assert.True(firstPassCalls >= 4);

        Assert.True(view.TryGetTokenAtOffset(line4Offset, out _, out _, out _));
        Assert.Equal(firstPassCalls, highlighter.HighlightCallCount);

        view.InvalidateFromLine(3);
        Assert.True(view.TryGetTokenAtOffset(line4Offset, out _, out _, out _));
        int delta = highlighter.HighlightCallCount - firstPassCalls;
        Assert.InRange(delta, 2, 3);
    }

    [Fact]
    public void BracketMatcher_FindMatchingBracketPair_WhenCaretAfterOpen_ShouldReturnPair()
    {
        var doc = new TextDocument("a(b)c");

        var pair = BracketMatcher.FindMatchingBracketPair(doc, 2);

        Assert.True(pair.HasValue);
        Assert.Equal(1, pair.Value.bracketOffset);
        Assert.Equal(3, pair.Value.matchOffset);
    }

    [Fact]
    public void BracketMatcher_FindMatchingBracketPair_WhenCaretOnClose_ShouldReturnPair()
    {
        var doc = new TextDocument("a(b)c");

        var pair = BracketMatcher.FindMatchingBracketPair(doc, 3);

        Assert.True(pair.HasValue);
        Assert.Equal(3, pair.Value.bracketOffset);
        Assert.Equal(1, pair.Value.matchOffset);
    }

    [Fact]
    public void BracketMatcher_FindMatchingBracketPair_WhenNoBracketNearby_ShouldReturnNull()
    {
        var doc = new TextDocument("abc");

        var pair = BracketMatcher.FindMatchingBracketPair(doc, 1);

        Assert.False(pair.HasValue);
    }

    private sealed class CountingSyntaxHighlighter : ISyntaxHighlighter
    {
        public int HighlightCallCount { get; private set; }

        public object? GetInitialState() => null;

        public (SyntaxToken[] tokens, object? stateAtLineEnd) HighlightLine(int lineNumber, string lineText, object? stateAtLineStart)
        {
            HighlightCallCount++;
            if (lineText.Length == 0)
                return ([], null);

            return ([new SyntaxToken(0, lineText.Length, TokenClassification.PlainText)], null);
        }
    }
}

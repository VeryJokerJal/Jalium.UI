using Jalium.UI.Controls;
using Jalium.UI.Automation;
using Jalium.UI.Input;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssMultiColumnTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();

    [Theory]
    [InlineData("columns:100px 2;", 2, 100d)]
    [InlineData("columns:2 100px;", 2, 100d)]
    [InlineData("columns:100px;", 2, 100d)]
    [InlineData("column-count:2;", 2, 100d)]
    [InlineData("columns:80px 8;", 2, 100d)]
    [InlineData("column-width:0px;column-count:2;", 2, 100d)]
    [InlineData("columns:1000px;", 1, 220d)]
    public void CountAndWidthFollowTheAvailableInlineSize(string value, int count, double width)
    {
        var root = Root(Boxes(6)); Style(root, value); Layout(root);
        Assert.Equal(count, CssMultiColumnLayout.For(root).ColumnCount);
        Assert.Equal(width, CssMultiColumnLayout.For(root).ColumnWidth, 3);
        Assert.Equal(220, root.ActualWidth, 3);
        Assert.All(root.Children.Cast<UIElement>(), child => Assert.Same(root, child.VisualParent));
    }

    [Theory]
    [InlineData("column-count:0;")] [InlineData("column-count:-1;")] [InlineData("column-count:1.5;")]
    [InlineData("column-width:20%;")] [InlineData("column-width:-1px;")]
    [InlineData("column-width:20;")]
    [InlineData("columns:2 3;")] [InlineData("columns:10px 20px;")] [InlineData("columns:auto auto auto;")]
    [InlineData("column-fill:stretch;")] [InlineData("column-span:2;")]
    public void InvalidValuesAreRejectedByTheSameRuntimeAndAuthoringParser(string value)
    {
        var parts = value.TrimEnd(';').Split(':', 2);
        Assert.False(Css.Supports(parts[0], parts[1]));
        Assert.False(CssAuthoring.IsValueSupported(parts[0], parts[1]));
    }

    [Fact]
    public void NormalGapUsesOneEmAndDoesNotChangeTheGridGapDefault()
    {
        var root = Root(Boxes(4)); Css.SetStyle(root, "display:block;column-count:2;font-size:20px;"); Layout(root);
        Assert.Equal(100, CssMultiColumnLayout.For(root).ColumnWidth, 3);
        Assert.Equal(0, CssGapProperties.Resolve(new Grid(), false, 220));
        Css.SetStyle(root, "display:block;column-count:2;column-gap:10%;"); Layout(root);
        Assert.Equal(99, CssMultiColumnLayout.For(root).ColumnWidth, 3);
    }

    [Theory]
    [InlineData("balance")] [InlineData("balance-all")]
    public void BalancedColumnsMoveOriginalAtomicControlsWithoutSplittingThem(string fill)
    {
        var buttons = Enumerable.Range(0, 6).Select(_ => new Button { Height = 24, Padding = new(0), BorderThickness = new(0) }).ToArray();
        var root = Root(buttons); Style(root, "column-count:2;column-fill:" + fill + ";"); Layout(root);
        Assert.Equal(72, root.ActualHeight, 3);
        for (var i = 0; i < buttons.Length; i++)
        {
            var p = buttons[i].TransformToAncestor(root);
            Assert.Equal(i < 3 ? 0 : 120, p.X, 3); Assert.Equal((i % 3) * 24, p.Y, 3);
            Assert.Equal(24, buttons[i].ActualHeight, 3); Assert.Same(root, buttons[i].VisualParent);
            Assert.Same(buttons[i], root.InputHitTest(new(p.X + 5, p.Y + 5)));
        }
    }

    [Fact]
    public void AutoFillUsesTheDefiniteHeightAndCreatesOverflowColumns()
    {
        var items = Boxes(7); var root = Root(items); Style(root, "column-count:2;column-fill:auto;height:48px;"); Layout(root);
        Assert.Equal(48, root.ActualHeight, 3);
        for (var i = 0; i < items.Length; i++)
        { var p = items[i].TransformToAncestor(root); Assert.Equal((i / 2) * 120, p.X, 3); Assert.Equal((i % 2) * 24, p.Y, 3); }
        Assert.Equal(7, root.Children.Count);
    }

    [Fact]
    public void AutoFillWithoutADefiniteHeightKeepsTheFirstColumnContinuous()
    {
        var items = Boxes(6); var root = Root(items); Style(root, "column-count:2;column-fill:auto;"); Layout(root);
        Assert.Equal(144, root.ActualHeight, 3); Assert.All(items, item => Assert.Equal(0, item.VisualBounds.X, 3));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void TextFlowsByWholeLinesAndPointSelectionUsesBothCoordinates(bool rtl)
    {
        var text = Text("甲乙\n丙丁\n戊己\n庚辛\n壬癸\n子丑"); var root = Root(text);
        Style(root, "column-count:2;" + (rtl ? "direction:rtl;" : "")); Layout(root);
        var rectangles = text.DocumentTextBounds(0, text.Text.Length); Assert.Equal(6, rectangles.Count);
        Assert.Equal(rectangles[0].Y, rectangles[3].Y, 3); Assert.Equal(120, Math.Abs(rectangles[0].X - rectangles[3].X), 3);
        Assert.InRange(root.ActualHeight, 72, 78);
        for (var i = 0; i < rectangles.Count; i++)
        {
            var bounds = rectangles[i]; var point = new Point(bounds.X + (rtl ? bounds.Width - .1 : .1), bounds.Y + bounds.Height / 2);
            var position = text.GetPositionFromPoint(point, true)!;
            Assert.InRange(text.ContentStart.GetOffsetToPosition(position), i * 3, i * 3 + 2);
        }
        text.Select(9, 2); Assert.Equal("庚辛", text.SelectedText); Assert.Same(root, text.VisualParent);
    }

    [Fact]
    public void ASpannerBalancesThePrecedingGroupEvenWithAutoFill()
    {
        var before = Boxes(4); var span = new Border { Height = 30 }; Css.SetStyle(span, "display:block;column-span:all;background:blue;");
        var after = Boxes(4); var root = Root([.. before, span, .. after]); Style(root, "column-count:2;column-fill:auto;"); Layout(root);
        Assert.Equal(220, span.ActualWidth, 3); Assert.Equal(48, span.VisualBounds.Y, 3);
        Assert.Equal(120, before[2].VisualBounds.X, 3); Assert.Equal(0, before[2].VisualBounds.Y, 3);
        Assert.Equal(78, after[0].VisualBounds.Y, 3); Assert.Equal(174, root.ActualHeight, 3);
    }

    [Fact]
    public void ForcedColumnBreaksAndAvoidInsideUseTheExistingFragmentationProperties()
    {
        var first = Boxes(2); var next = Boxes(2); Css.SetStyle(next[0], "display:block;height:24px;break-before:column;");
        var root = Root([.. first, .. next]); Style(root, "column-count:2;"); Layout(root);
        Assert.Equal(0, first[0].VisualBounds.X, 3); Assert.Equal(120, next[0].VisualBounds.X, 3); Assert.Equal(0, next[0].VisualBounds.Y, 3);
        var text = Text("甲\n乙\n丙\n丁"); Css.SetStyle(text, "display:block;white-space:pre-wrap;line-height:24px;break-inside:avoid-column;");
        root = Root(text, new Border { Height = 24 }); Style(root, "column-count:2;"); Layout(root);
        var rectangles = text.DocumentTextBounds(0, text.Text.Length); Assert.Single(rectangles.Select(rect => rect.X).Distinct());
    }

    [Fact]
    public void OrphansAndWidowsKeepTwoLinesAtEachSideOfTheBreak()
    {
        var text = Text("甲\n乙\n丙\n丁\n戊"); var root = Root(text); Style(root, "column-count:2;"); Layout(root);
        var rectangles = text.DocumentTextBounds(0, text.Text.Length);
        var counts = rectangles.GroupBy(rect => Math.Round(rect.X, 3)).Select(group => group.Count()).Order().ToArray();
        Assert.Equal(new[] { 2, 3 }, counts); Assert.InRange(root.ActualHeight, 72, 78);
    }

    [Fact]
    public void RemovingColumnsRestoresTheOriginalTreeLayoutAndTextOffsets()
    {
        var text = Text("甲\n乙\n丙\n丁\n戊\n己"); var root = Root(text); Layout(root); var singleHeight = root.ActualHeight;
        Style(root, "column-count:2;"); Layout(root);
        var original = text.Text; var columnHeight = root.ActualHeight; Assert.InRange(columnHeight, 72, 78);
        Style(root, "columns:auto;"); Layout(root);
        Assert.Equal(singleHeight, root.ActualHeight, 3); Assert.Equal(220, text.ActualWidth, 3);
        Assert.All(text.DocumentTextBounds(0, text.Text.Length), rect => Assert.Equal(0, rect.X, 3));
        Assert.Equal(original, text.Text); Assert.Same(root, text.VisualParent);
        Style(root, "columns:2;"); Layout(root); Assert.Equal(columnHeight, root.ActualHeight, 3);
    }

    [Fact]
    public void FragmentBackgroundsAndRulesLeaveTheGutterClearAndRestoreCurrentColor()
    {
        var text = Text("甲\n乙\n丙\n丁\n戊\n己"); Css.SetStyle(text, "display:block;white-space:pre-wrap;line-height:24px;background:blue;color:transparent;");
        var root = Root(text); Style(root, "column-count:2;column-rule:4px solid currentColor;color:red;"); Layout(root);
        Assert.Equal(Colors.Blue, Pixel(root, 70, 10)); Assert.Equal(Colors.Blue, Pixel(root, 180, 10));
        Assert.Equal(Colors.White, Pixel(root, 103, 10)); Assert.Equal(Colors.Red, Pixel(root, 109, 10));
        Style(root, "column-count:2;column-rule:4px solid currentColor;color:green;"); Layout(root);
        Assert.Equal(Colors.Green, Pixel(root, 109, 10)); Assert.Equal(Colors.White, Pixel(root, 103, 10));
    }

    [Fact]
    public void AShorthandResetsOmittedValuesAndCssWideKeywordsParticipateInCascade()
    {
        var root = Root(Boxes(4)); Style(root, "column-width:40px;columns:2;column-rule:4px double red;column-rule:solid;"); Layout(root);
        Assert.True(((CssLayoutLength)root.GetValue(CssMultiColumnProperties.WidthProperty)!).IsAuto);
        Assert.Equal(3d, root.GetValue(CssMultiColumnProperties.RuleWidthProperty));
        Assert.Equal(CssBorderLineStyle.Solid, root.GetValue(CssMultiColumnProperties.RuleStyleProperty));
        Style(root, "column-count:2;column-count:initial;"); Layout(root);
        Assert.False(CssMultiColumnProperties.Active(root)); Assert.Equal(96, root.ActualHeight, 3);
    }

    [Fact]
    public void WidthAndCountReflowWhenTheHostBecomesNarrowerAndWider()
    {
        var root = Root(Boxes(8)); Style(root, "columns:90px 4;");
        foreach (var (width, columns) in new[] { (420d, 4), (220d, 2), (80d, 1), (420d, 4) })
        {
            Flush(); root.Measure(new(width, double.PositiveInfinity)); root.Arrange(new(0, 0, width, root.DesiredSize.Height));
            Assert.Equal(columns, CssMultiColumnLayout.For(root).ColumnCount);
            Assert.All(root.Children.Cast<FrameworkElement>(), item => Assert.InRange(item.TransformToAncestor(root).X, 0, width));
        }
    }

    [Fact]
    public void NestedFlowFramesKeepRichInlinesAndTheirSourceParents()
    {
        var text = Text(""); var run = new Run("甲乙\n丙丁\n戊己\n庚辛\n壬癸\n子丑"); text.Inlines.Add(run);
        var frame = new Border { Child = text }; Css.SetStyle(frame, "display:block;background:blue;");
        var root = Root(frame); Style(root, "columns:2;"); Layout(root);
        var rectangles = text.DocumentTextBounds(0, text.Text.Length); Assert.Equal(6, rectangles.Count);
        Assert.Equal(2, rectangles.GroupBy(rect => Math.Round(rect.X, 3)).Count()); Assert.Same(frame, text.VisualParent); Assert.Same(run, Assert.Single(text.Inlines));
        Assert.Equal(Colors.Blue, Pixel(root, 70, 10)); Assert.Equal(Colors.Blue, Pixel(root, 180, 10)); Assert.Equal(Colors.White, Pixel(root, 110, 10));
    }

    [Fact]
    public void NestedSpannersUseTheFullWidthAsTheirPercentageBasis()
    {
        var child = new Border { Height = 10 }; Css.SetStyle(child, "display:block;width:50%;");
        var span = new Border { Child = child }; Css.SetStyle(span, "display:block;column-span:all;padding:10%;box-sizing:border-box;");
        var nested = Root([.. Boxes(2), span, .. Boxes(2)]); Css.SetStyle(nested, "display:block;");
        var root = Root(nested); Style(root, "columns:2;"); Layout(root);
        Assert.Equal(220, span.ActualWidth, 3); Assert.Equal(22, span.Padding.Left, 3); Assert.Equal(88, child.ActualWidth, 3);
        Assert.Same(nested, span.VisualParent); Assert.Same(span, child.VisualParent);
    }

    [Fact]
    public void PositiveSpannerMarginsSeparateFullColumnSets()
    {
        var span = new Border { Height = 20 }; Css.SetStyle(span, "display:block;column-span:all;margin:10px 0 12px;");
        var before = Boxes(4); var after = Boxes(4); var root = Root([.. before, span, .. after]); Style(root, "columns:2;"); Layout(root);
        Assert.Equal(58, span.VisualBounds.Y, 3); Assert.Equal(90, after[0].VisualBounds.Y, 3);
        Assert.Equal(90, after[2].VisualBounds.Y, 3); Assert.Equal(138, root.ActualHeight, 3);
    }

    [Fact]
    public void FixedHeightSpannersLeaveOnlyTheRemainingHeightForTheLastSet()
    {
        var span = new Border { Height = 20 }; Css.SetStyle(span, "display:block;column-span:all;");
        var after = Boxes(4); var root = Root([.. Boxes(4), span, .. after]); Style(root, "columns:2;height:100px;column-fill:auto;"); Layout(root);
        Assert.Equal(48, span.VisualBounds.Y, 3); Assert.Equal(68, after[0].VisualBounds.Y, 3);
        Assert.True(after[^1].TransformToAncestor(root).X >= 240); Assert.Equal(100, root.ActualHeight, 3);
    }

    [Theory]
    [InlineData("break-after")] [InlineData("break-before")]
    public void AvoidColumnAtABlockBoundaryKeepsAdjacentContentTogether(string property)
    {
        var items = Boxes(4); Css.SetStyle(items[property == "break-after" ? 1 : 2], "display:block;" + property + ":avoid-column;");
        var root = Root(items); Style(root, "columns:2;"); Layout(root);
        Assert.Equal(items[1].TransformToAncestor(root).X, items[2].TransformToAncestor(root).X, 3);
    }

    [Theory]
    [InlineData(0d)] [InlineData(20d)]
    public void OversizedAtomicBoxesAndZeroHeightColumnsAlwaysMakeProgress(double height)
    {
        var first = new Button { Height = 80 }; var second = new Button { Height = 24 }; var root = Root(first, second);
        Style(root, "columns:2;column-fill:auto;height:" + height + "px;"); Layout(root);
        Assert.Equal(80, first.ActualHeight, 3); Assert.Equal(24, second.ActualHeight, 3);
        Assert.Equal(120, second.TransformToAncestor(root).X, 3); Assert.Same(root, second.VisualParent);
    }

    [Fact]
    public void TextChangesAndSourceRemovalDoNotLeaveStaleFragments()
    {
        var text = Text("甲\n乙\n丙\n丁\n戊\n己"); var root = Root(text); Style(root, "columns:2;"); Layout(root);
        text.Text = "新\n文\n字\n行"; Layout(root); Assert.Equal(4, text.DocumentTextBounds(0, text.Text.Length).Count);
        root.Children.Remove(text); var other = Root(text); Layout(other);
        Assert.All(text.DocumentTextBounds(0, text.Text.Length), rect => Assert.Equal(0, rect.X, 3));
        Assert.Same(other, text.VisualParent); Assert.Equal("新\n文\n字\n行", text.Text);
    }

    [Fact]
    public void MovingAWholeFragmentedSubtreeDoesNotKeepItsFormerColumnAllocations()
    {
        var text = Text("甲\n乙\n丙\n丁\n戊\n己"); var frame = new Border { Child = text }; Css.SetStyle(frame, "display:block;");
        var root = Root(frame); Style(root, "columns:2;"); Layout(root);
        Assert.Equal(2, text.DocumentTextBounds(0, text.Text.Length).GroupBy(rect => Math.Round(rect.X, 3)).Count());
        root.Children.Remove(frame);
        var other = Root(frame); Layout(other);
        Assert.All(text.DocumentTextBounds(0, text.Text.Length), rect => Assert.Equal(0, rect.X, 3));
        Assert.Equal(220, frame.ActualWidth, 3); Assert.Same(frame, text.VisualParent);
    }

    [Fact]
    public void NativeTextBlockHostsCanUseColumnsWithoutAnExplicitDisplayDeclaration()
    {
        var text = Text("甲\n乙\n丙\n丁\n戊\n己"); Css.SetStyle(text, "columns:2;column-gap:20px;white-space:pre-wrap;line-height:24px;"); Layout(text);
        Assert.Equal(2, text.DocumentTextBounds(0, text.Text.Length).GroupBy(rect => Math.Round(rect.X, 3)).Count());
        Assert.InRange(text.ActualHeight, 72, 78);
    }

    [Fact]
    public void ColumnsApplyInsideTableCellsAndDoNotReplaceTheTableOrFlexAlgorithms()
    {
        var text = Text("甲\n乙\n丙\n丁\n戊\n己"); var cell = new Border { Child = text }; Css.SetStyle(cell, "display:table-cell;columns:2;column-gap:20px;");
        var row = Root(cell); Css.SetStyle(row, "display:table-row;"); var table = Root(row); Css.SetStyle(table, "display:table;width:220px;table-layout:fixed;columns:3;"); Layout(table);
        Assert.False(CssMultiColumnProperties.Active(table)); Assert.Equal(2, text.DocumentTextBounds(0, text.Text.Length).GroupBy(rect => Math.Round(rect.X, 3)).Count());
        var flex = Root(Boxes(2)); Css.SetStyle(flex, "display:flex;columns:3;"); Layout(flex); Assert.False(CssMultiColumnProperties.Active(flex));
    }

    [Fact]
    public void GutterClipsMatchTextAccessibilityAndPointerGeometry()
    {
        var text = Text("甲乙\n丙丁\n戊己\n庚辛\n壬癸\n子丑"); var root = Root(text); Style(root, "columns:2;"); Layout(root);
        var rectangles = text.DocumentTextBounds(0, text.Text.Length); var visible = AutomationVisibility.ClipRectangles(text, rectangles);
        Assert.Equal(rectangles.Count, visible.Count); Assert.All(visible, rect => Assert.True(rect.Width > 0 && rect.Height > 0));
        var origin = text.TransformToAncestor(root);
        Assert.NotSame(text, root.InputHitTest(new(110, 10)));
        foreach (var rect in rectangles)
        { var point = new Point(origin.X + rect.X + 1, origin.Y + rect.Y + rect.Height / 2); Assert.Same(text, root.InputHitTest(point)); }
        Assert.Equal(text.Text, text.GetAutomationPeer()!.GetName());
    }

    [Fact]
    public void KeyboardNavigationKeepsSourceOrderAcrossColumnsAndRestoration()
    {
        Keyboard.Initialize(); Keyboard.ClearFocus(); var a = new Button { Height = 24 }; var b = new Button { Height = 24 }; var c = new Button { Height = 24 };
        var root = Root(a, b, c); Style(root, "columns:2;"); var window = new DisplayedTestWindow { Content = root, Width = 220, Height = 200 };
        try
        {
            Flush(); window.Measure(new(220, 200)); window.Arrange(new(0, 0, 220, 200));
            Assert.True(a.Focus()); Assert.True(KeyboardNavigation.MoveFocus(a)); Assert.Same(b, Keyboard.FocusedElement);
            Assert.True(KeyboardNavigation.MoveFocus(b)); Assert.Same(c, Keyboard.FocusedElement);
            Style(root, "columns:auto;"); Flush(); window.Measure(new(220, 200)); window.Arrange(new(0, 0, 220, 200));
            Assert.Same(c, Keyboard.FocusedElement); Assert.True(KeyboardNavigation.MoveFocus(c, reverse: true)); Assert.Same(b, Keyboard.FocusedElement);
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    [Fact]
    public void MultiColumnBaselinesReferToProjectedContentInsteadOfTheContinuousSourceHeight()
    {
        var text = Text("甲\n乙\n丙\n丁\n戊\n己\n庚\n辛"); var root = Root(text); Style(root, "columns:2;"); Layout(root);
        Assert.True(CssDisplayLayout.TryGetBaseline(root, root.ActualHeight, false, out var first));
        Assert.True(CssDisplayLayout.TryGetBaseline(root, root.ActualHeight, true, out var last));
        Assert.InRange(first, 0, root.ActualHeight); Assert.InRange(last, first, root.ActualHeight);
    }

    [Fact]
    public void OverflowColumnsExtendDirtyInkAndRemovingColumnsClearsThatExtension()
    {
        var root = Root(Boxes(7)); Style(root, "columns:2;column-fill:auto;height:48px;"); Layout(root);
        Assert.True(root.GetExtraDirtyPadding() >= 240);
        Style(root, "columns:auto;"); Layout(root); Assert.Equal(0, root.CssColumnInkPadding);
    }

    [Theory]
    [InlineData("columns:3;", 160d)] [InlineData("columns:100px 3;", 340d)]
    public void MaxContentWidthIncludesTheColumnCountAndGaps(string style, double expected)
    {
        var boxes = Boxes(4); foreach (var box in boxes) box.Width = 40;
        var root = Root(boxes); Style(root, style); Flush(); root.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
        Assert.Equal(expected, root.DesiredSize.Width, 3);
    }

    [Fact]
    public void MinimumHeightExtendsRulesAndMaximumHeightCreatesOverflowColumns()
    {
        var root = Root(Boxes(4)); Style(root, "columns:2;min-height:100px;"); Layout(root);
        Assert.Equal(100, root.ActualHeight, 3); Assert.Equal(100, CssMultiColumnLayout.For(root).Rules.Single().Height, 3);
        root = Root(Boxes(10)); Style(root, "columns:2;max-height:80px;"); Layout(root);
        Assert.Equal(80, root.ActualHeight, 3); Assert.True(CssMultiColumnLayout.For(root).Rules.Count > 1);
    }

    [Fact]
    public void VariablesSupportsAndRelativeLengthsShareTheColumnPropertyDefinitions()
    {
        var root = Root(Boxes(6)); Css.SetClass(root, "article"); Css.SetStyleSheet(root, """
            .article{display:block;font-size:20px;--count:2;--width:4em;}
            @supports (columns:80px 2) and (column-fill:balance-all){.article{columns:var(--width) var(--count);column-gap:1em;}}
            """); Layout(root);
        Assert.True(CssMultiColumnProperties.Active(root)); Assert.Equal(2, CssMultiColumnLayout.For(root).ColumnCount);
        Assert.Equal(100, CssMultiColumnLayout.For(root).ColumnWidth, 3);
        Css.SetStyle(root, "--count:1;"); Layout(root); Assert.Equal(1, CssMultiColumnLayout.For(root).ColumnCount);
    }

    [Fact]
    public void LeadingLineBoxSpaceCannotOccupyAnEmptyFirstColumn()
    {
        var text = Text("甲\n乙\n丙\n丁"); var root = Root(text); Style(root, "columns:3;"); Layout(root);
        var rectangles = text.DocumentTextBounds(0, text.Text.Length);
        Assert.Equal(0, text.TransformToAncestor(root).X + rectangles[0].X, 3);
        Assert.Equal(2, rectangles.GroupBy(rect => Math.Round(rect.X, 3)).Count());
    }

    [Theory]
    [InlineData("column-count")] [InlineData("column-width")] [InlineData("column-fill")]
    [InlineData("column-span")] [InlineData("column-rule-width")] [InlineData("column-rule-style")]
    public void ExplicitInheritTracksItsParentsChangingComputedColumnValue(string name)
    {
        var (first, next) = name switch
        {
            "column-count" => ((object)2, (object)3),
            "column-width" => ((object)CssLayoutLength.Px(80), (object)CssLayoutLength.Px(40)),
            "column-fill" => ((object)CssColumnFill.Balance, (object)CssColumnFill.Auto),
            "column-span" => ((object)false, (object)true),
            "column-rule-width" => ((object)3d, (object)7d),
            _ => ((object)CssBorderLineStyle.Solid, (object)CssBorderLineStyle.Double),
        };
        var child = Root(Boxes(6)); var parent = Root(child); var property = CssPropertyRegistry.Lookup(name)!.StorageProperty!;
        parent.SetValue(property, first); Css.SetStyle(child, "display:block;" + name + ":inherit;"); Layout(parent);
        Assert.Equal(first, child.GetValue(property));
        parent.SetValue(property, next); Layout(parent); Assert.Equal(next, child.GetValue(property));
        if (name == "column-count") Assert.Equal(3, CssMultiColumnLayout.For(child).ColumnCount);
    }

    [Fact]
    public void InheritedRuleColorKeepsCurrentColorSymbolicAndObservesNativeOverrides()
    {
        var child = Root(Boxes(4)); var parent = Root(child);
        Css.SetStyle(parent, "display:block;color:blue;column-rule-color:currentColor;");
        Css.SetStyle(child, "display:block;columns:2;color:red;column-rule-color:inherit;"); Layout(parent);
        Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(child.GetValue(CssMultiColumnProperties.RuleColorProperty)).Color);
        parent.SetValue(CssMultiColumnProperties.RuleColorProperty, Brushes.Green); Layout(parent);
        Assert.Equal(Colors.Green, Assert.IsType<SolidColorBrush>(child.GetValue(CssMultiColumnProperties.RuleColorProperty)).Color);
        parent.ClearValue(CssMultiColumnProperties.RuleColorProperty); Layout(parent);
        Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(child.GetValue(CssMultiColumnProperties.RuleColorProperty)).Color);
    }

    private static Border[] Boxes(int count) => Enumerable.Range(0, count).Select(_ =>
    { var item = new Border { Height = 24 }; Css.SetStyle(item, "display:block;"); return item; }).ToArray();
    private static StackPanel Root(params UIElement[] children)
    { var root = new StackPanel(); foreach (var child in children) root.Children.Add(child); Css.SetStyle(root, "display:block;column-gap:20px;"); return root; }
    private static TextBlock Text(string text)
    { var result = new TextBlock { Text = text, FontFamily = "PingFang SC", FontSize = 16, Foreground = Brushes.Black }; Css.SetStyle(result, "display:block;white-space:pre-wrap;line-height:24px;"); return result; }
    private static void Style(FrameworkElement root, string css) => Css.SetStyle(root, "display:block;column-gap:20px;" + css);
    private static void Layout(FrameworkElement root)
    { Flush(); root.Measure(new(220, double.PositiveInfinity)); root.Arrange(new(0, 0, 220, Math.Max(1, root.DesiredSize.Height))); }
    private static void Flush() { Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); }
    private static Color Pixel(FrameworkElement root, int x, int y)
    {
        var width = (int)Math.Ceiling(root.ActualWidth); var height = (int)Math.Ceiling(root.ActualHeight);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Bgra32); var drawing = new SoftwareDrawingContext(bitmap);
        drawing.DrawRectangle(Brushes.White, null, new(0, 0, width, height)); root.Render(drawing); drawing.Close();
        var pixels = new byte[width * height * 4]; bitmap.CopyPixels(pixels, width * 4, 0); var at = (y * width + x) * 4;
        return Color.FromArgb(pixels[at + 3], pixels[at + 2], pixels[at + 1], pixels[at]);
    }
}

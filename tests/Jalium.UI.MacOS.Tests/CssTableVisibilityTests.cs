using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssTableVisibilityTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RowCollapseRemovesItsHeightAndOneGapButKeepsColumnConstraints(bool borders)
    {
        var a = Cell(80, 40); var b = Cell(20, 20); var first = Row(a); var second = Row(b);
        var table = Table(first, second); Css.SetStyle(table, "display:table;border-spacing:6px 5px;" + (borders ? "border-collapse:collapse;" : ""));
        Layout(table); var width = table.ActualWidth; var height = table.ActualHeight; var removed = first.ActualHeight + (borders ? 0 : 5);
        Css.SetStyle(first, "display:table-row;visibility:collapse;"); Layout(table);
        Assert.Equal(width, table.ActualWidth, 3); Assert.Equal(height - removed, table.ActualHeight, 3);
        Assert.Equal(0, first.ActualHeight); Assert.False(a.IsVisible); Assert.Equal(borders ? 0 : 5, second.VisualBounds.Top, 3);
        Assert.Equal(80, b.ActualWidth, 3); Assert.Same(first, a.VisualParent);
        Css.SetStyle(first, "display:table-row;"); Layout(table); Assert.Equal(height, table.ActualHeight, 3); Assert.True(a.IsVisible);
    }

    [Theory]
    [InlineData("visible", 40d)] [InlineData("hidden", 40d)] [InlineData("collapse", 20d)]
    public void HiddenAndCollapsedRowsHaveDifferentGeometry(string visibility, double height)
    {
        var row = Row(Cell(20, 20)); var table = Table(row, Row(Cell(20, 20)));
        Css.SetStyle(row, "display:table-row;visibility:" + visibility + ";"); Layout(table);
        Assert.Equal(height, table.ActualHeight, 3);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ExplicitTableWidthShrinksAfterColumnDistributionInEitherDirection(bool rtl)
    {
        var columns = new[] { Column(), Column(), Column() }; var cells = new[] { Cell(5, 20), Cell(5, 20), Cell(5, 20) };
        var table = Table([.. columns, Row(cells)]); Css.SetStyle(table, Fixed + (rtl ? "direction:rtl;" : "")); Layout(table);
        var before = cells.Select(cell => cell.ActualWidth).ToArray(); Assert.All(before, width => Assert.Equal(100, width, 3));
        Css.SetStyle(columns[1], "display:table-column;visibility:collapse;"); Layout(table);
        Assert.Equal(200, table.ActualWidth, 3); Assert.Equal(200, table.DesiredSize.Width, 3);
        Assert.Equal(0, cells[1].ActualWidth); Assert.False(cells[1].IsVisible);
        Assert.Equal(before[0], cells[0].ActualWidth, 3); Assert.Equal(before[2], cells[2].ActualWidth, 3);
        Assert.Equal(rtl ? 100 : 0, cells[0].TransformToAncestor(table).X, 3); Assert.Equal(rtl ? 0 : 100, cells[2].TransformToAncestor(table).X, 3);
    }

    [Fact]
    public void ColumnGroupCollapseCannotBeOverriddenByAVisibleColumn()
    {
        var column = Column(); Css.SetStyle(column, "display:table-column;visibility:visible;"); var group = new StackPanel(); group.Children.Add(column);
        Css.SetStyle(group, "display:table-column-group;visibility:collapse;"); var a = Cell(5, 20); var b = Cell(5, 20);
        var table = Table(group, Column(), Row(a, b)); Css.SetStyle(table, Fixed); Layout(table);
        Assert.Equal(150, table.ActualWidth, 3); Assert.False(a.IsVisible); Assert.True(b.IsVisible);
        Css.SetStyle(group, "display:table-column-group;"); Layout(table); Assert.Equal(300, table.ActualWidth, 3); Assert.True(a.IsVisible);
    }

    [Fact]
    public void RowGroupCollapseRemovesEveryTrackEvenWithVisibleRowAndCellOverrides()
    {
        var a = Cell(20, 20); Css.SetStyle(a, "display:table-cell;visibility:visible;"); var first = Row(a); Css.SetStyle(first, "display:table-row;visibility:visible;");
        var group = new StackPanel(); group.Children.Add(first); group.Children.Add(Row(Cell(30, 30))); Css.SetStyle(group, "display:table-row-group;visibility:collapse;");
        var table = Table(group, Row(Cell(10, 10))); Layout(table);
        Assert.Equal(10, table.ActualHeight, 3); Assert.False(a.IsVisible); Assert.Equal(30, table.ActualWidth, 3);
    }

    [Fact]
    public void ACollapsedCellKeepsItsSlotAndAllowsAnExplicitlyVisibleChild()
    {
        var a = Cell(50, 20); var b = Cell(50, 20); var table = Table(Row(a, b)); Css.SetStyle(a, "display:table-cell;visibility:collapse;background:red;");
        Css.SetStyle(a.Child!, "visibility:visible;background:blue;"); Layout(table);
        Assert.Equal(100, table.ActualWidth, 3); Assert.Equal(20, table.ActualHeight, 3); Assert.Equal(50, b.VisualBounds.Left, 3);
        Assert.False(a.IsVisible); Assert.True(a.Child!.IsVisible); Assert.Equal(Colors.Blue, Pixel(table, 5, 5));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SpanningContentKeepsItsOriginalWidthAndClipsFromItsInlineStart(bool rtl)
    {
        var strip = new Canvas { Width = 200, Height = 20 }; Css.SetStyle(strip, "direction:ltr;");
        var red = new Border { Width = 100, Height = 20, Background = Brushes.Red }; var blue = new Border { Width = 100, Height = 20, Background = Brushes.Blue };
        strip.Children.Add(red); strip.Children.Add(blue); Canvas.SetLeft(blue, 100);
        var span = new Border { Child = strip }; Css.SetStyle(span, "display:table-cell;vertical-align:top;"); Grid.SetColumnSpan(span, 2);
        var middle = Column(); var table = Table(Column(), middle, Column(), Row(span, Cell(5, 20)));
        Css.SetStyle(table, Fixed + (rtl ? "direction:rtl;" : "")); Layout(table);
        Css.SetStyle(middle, "display:table-column;visibility:collapse;"); Layout(table);
        Assert.Equal(100, span.ActualWidth, 3); Assert.Equal(200, strip.ActualWidth, 3); Assert.Equal(rtl ? -100 : 0, strip.VisualBounds.Left, 3);
        Assert.Equal(100, blue.VisualBounds.Left, 3); Assert.Equal(0, blue.VisualBounds.Top, 3);
        Assert.Equal(rtl ? Colors.Blue : Colors.Red, Pixel(table, (int)span.TransformToAncestor(table).X + 50, 10));
        var clip = new AutomationVisibility(strip); Assert.True(clip.TryClip(new Rect(0, 0, 200, 20), out var visible)); Assert.Equal(100, visible.Width, 3);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void RemovingTheFirstMiddleOrLastSpannedTrackDoesNotLeaveAnExtraGap(int hidden)
    {
        var columns = new[] { Column(), Column(), Column() }; var span = Cell(150, 20); Grid.SetColumnSpan(span, 3);
        var table = Table([.. columns, Row(span)]); Css.SetStyle(table, Fixed + "border-spacing:5px;"); Layout(table);
        var expected = 2 * (300d - 4 * 5) / 3 + 5;
        Css.SetStyle(columns[hidden], "display:table-column;visibility:collapse;"); Layout(table);
        Assert.Equal(expected, span.ActualWidth, 3); Assert.Equal(expected + 10, table.ActualWidth, 3);
    }

    [Fact]
    public void FullyCollapsedColumnsDoNotRetainTheAuthoredTableMinimumOrGaps()
    {
        var a = Column(); var b = Column(); var table = Table(a, b, Row(Cell(10, 20), Cell(10, 20)));
        Css.SetStyle(table, Fixed + "min-width:400px;border-spacing:5px;"); Css.SetStyle(a, "display:table-column;visibility:collapse;"); Css.SetStyle(b, "display:table-column;visibility:collapse;"); Layout(table);
        Assert.Equal(0, table.ActualWidth); Assert.Equal(0, table.DesiredSize.Width);
    }

    [Fact]
    public void CollapsedRowsDoNotLeakSharedBordersAndVisibleNeighborsResolveAgain()
    {
        var first = Cell(10, 20); var hidden = Cell(10, 20); var last = Cell(10, 20);
        Css.SetStyle(first, "display:table-cell;border-bottom:4px solid red;"); Css.SetStyle(hidden, "display:table-cell;border:10px solid blue;"); Css.SetStyle(last, "display:table-cell;border-top:6px solid green;");
        var row = Row(hidden); var table = Table(Row(first), row, Row(last)); Css.SetStyle(table, "display:table;border-collapse:collapse;width:100px;"); Layout(table);
        Css.SetStyle(row, "display:table-row;visibility:collapse;"); Layout(table);
        Assert.Equal(0, Count(table, Colors.Blue)); Assert.True(Count(table, Colors.Green) > 0);
    }

    [Fact]
    public void KeyboardNavigationSkipsRemovedColumnsAndKeepsTheUnchangedFocusedSource()
    {
        Keyboard.Initialize(); Keyboard.ClearFocus(); var buttons = new[] { new Button { Content = "甲" }, new Button { Content = "乙" }, new Button { Content = "丙" } };
        var cells = buttons.Select(button => { var cell = Cell(10, 30); cell.Child = button; return cell; }).ToArray();
        var column = Column(); var table = Table(Column(), column, Column(), Row(cells)); Css.SetStyle(table, Fixed);
        var window = new DisplayedTestWindow { Width = 500, Height = 200, Content = table, TitleBarStyle = WindowTitleBarStyle.Native };
        try
        {
            Layout(window); Assert.True(buttons[0].Focus());
            Css.SetStyle(column, "display:table-column;visibility:collapse;"); Layout(window); Assert.Same(buttons[0], Keyboard.FocusedElement);
            Assert.True(KeyboardNavigation.MoveFocus(buttons[0])); Assert.Same(buttons[2], Keyboard.FocusedElement);
            Assert.True(KeyboardNavigation.MoveFocus(buttons[2], reverse: true)); Assert.Same(buttons[0], Keyboard.FocusedElement);
            Assert.False(buttons[1].Focus());
            Css.SetStyle(column, "display:table-column;"); Layout(window); Assert.True(KeyboardNavigation.MoveFocus(buttons[0])); Assert.Same(buttons[1], Keyboard.FocusedElement);
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DocumentRowsRetainOriginalTextAndColumnsWhileRemovingTheirDisplaySpace(bool borders)
    {
        var first = DocRow(DocCell("原始宽内容不应改变后续列")); var second = DocRow(DocCell("保留原文"));
        var table = DocTable(first, second); Css.SetStyle(table, "width:300px;table-layout:fixed;border-spacing:4px 5px;" + (borders ? "border-collapse:collapse;" : ""));
        var doc = Doc(table); var original = doc.GetText(); using var body = Body(doc); var text = body.Paragraphs[1];
        var width = body.TableCells[1].Bounds.Width; var height = body.ExtentHeight; var removed = body.TableCells[0].Bounds.Height + (borders ? 0 : 5);
        Css.SetStyle(first, "visibility:collapse;"); Relayout(body);
        Assert.Equal(height - removed, body.ExtentHeight, 3); Assert.Equal(width, body.TableCells[1].Bounds.Width, 3);
        Assert.False(body.Paragraphs[0].IsVisible); Assert.True(body.Paragraphs[1].IsVisible); Assert.Same(text, body.Paragraphs[1]);
        Assert.Equal(original, doc.GetText()); Assert.Same(first, table.RowGroups[0].Rows[0]); Assert.Same(first.Cells[0], body.TableCells[0].Source);
        Css.SetStyle(first, "visibility:visible;"); Relayout(body); Assert.Equal(height, body.ExtentHeight, 3); Assert.True(body.Paragraphs[0].IsVisible);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DocumentColumnsContractWithoutRewrappingSpanningParagraphs(bool rtl)
    {
        var span = DocCell("跨列文字保留原来的行宽"); span.ColumnSpan = 2; var other = DocCell("尾列");
        var table = DocTable(DocRow(span, other)); foreach (var _ in new int[3]) table.Columns.Add(new TableColumn());
        Css.SetStyle(table, "width:300px;table-layout:fixed;" + (rtl ? "direction:rtl;" : "")); var doc = Doc(table); using var body = Body(doc);
        var paragraph = body.Paragraphs[0]; var fullWidth = paragraph.ActualWidth;
        Css.SetStyle(table.Columns[1], "visibility:collapse;"); Relayout(body);
        Assert.Equal(100, body.TableCells.Single(cell => ReferenceEquals(cell.Source, span)).Bounds.Width, 3); Assert.Equal(200, fullWidth, 3);
        Assert.Equal(fullWidth, paragraph.ActualWidth, 3); Assert.Equal(100, body.GetAdditionalChildLayoutClip(paragraph)!.Bounds.Width, 3);
        Assert.Equal(rtl ? 100 : 0, paragraph.TransformToAncestor(body).X, 3);
    }

    [Fact]
    public void DocumentFullyRemovedCellsIgnoreVisibleDescendantsAndPointSelectionUsesRemainingText()
    {
        var cell = DocCell("折叠的原文"); Css.SetStyle(cell, "visibility:visible;"); Css.SetStyle(cell.Blocks[0], "visibility:visible;");
        var row = DocRow(cell); Css.SetStyle(row, "visibility:collapse;"); var kept = DocCell("继续保留"); var doc = Doc(DocTable(row, DocRow(kept))); using var body = Body(doc);
        Assert.False(body.Paragraphs[0].IsVisible); Assert.True(body.Paragraphs[1].IsVisible);
        Assert.True(body.OffsetFromPoint(new(5, 5)) >= doc.GetDocumentOffset((Paragraph)kept.Blocks[0], 0));
        Assert.Contains("折叠的原文", doc.GetText());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DocumentRowSpansClipLinesAndPaginationKeepsTheSameContractedGeometry(bool borders)
    {
        var span = DocCell("第一行\n第二行\n第三行"); span.RowSpan = 3;
        var first = DocRow(span, DocCell("甲")); var middle = DocRow(DocCell("乙")); var last = DocRow(DocCell("丙"));
        var table = DocTable(first, middle, last); Css.SetStyle(table, "width:300px;table-layout:fixed;" + (borders ? "border-collapse:collapse;border:2px solid green;" : ""));
        if (borders) Css.SetStyle(span, "border:2px solid red;");
        var doc = Doc(table); doc.PageHeight = 48; using var body = Body(doc); var oldHeight = body.ExtentHeight; var oldWidth = body.Paragraphs[0].ActualWidth;
        var middleHeight = body.TableCells.Single(cell => ReferenceEquals(cell.Source, middle.Cells[0])).Bounds.Height;
        Css.SetStyle(middle, "visibility:collapse;"); Relayout(body);
        Assert.Equal(oldHeight - middleHeight, body.ExtentHeight, 3); Assert.Equal(oldWidth, body.Paragraphs[0].ActualWidth, 3);
        Assert.Equal(body.TableCells.Single(cell => ReferenceEquals(cell.Source, span)).Bounds, body.GetAdditionalChildLayoutClip(body.Paragraphs[0])!.Bounds);
        Assert.All(body.Lines, line => { Assert.True(line.Height > 0); Assert.True(line.Top + line.Height <= body.ExtentHeight + .01); });
        for (var pageIndex = 0; pageIndex < doc.ViewerPaginator.PageCount; pageIndex++)
        {
            using var page = Assert.IsType<FlowDocumentPresentation>(doc.ViewerPaginator.GetPage(pageIndex).Visual);
            Assert.Equal(body.TableCells, page.TableCells); Assert.Equal(body.ExtentHeight, page.ExtentHeight, 3);
        }
        Assert.Equal("第一行\n第二行\n第三行", Assert.IsType<Run>(Assert.IsType<Paragraph>(span.Blocks[0]).Inlines[0]).Text);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ColumnHiddenValuesDoNotHideOrResizeTheCorrespondingCells(bool grouped)
    {
        var column = Column(); Css.SetStyle(column, "display:table-column;visibility:hidden;");
        var parent = new StackPanel(); if (grouped) parent.Children.Add(column); Css.SetStyle(parent, "display:table-column-group;visibility:hidden;");
        var first = Cell(10, 20); var table = Table(grouped ? parent : column, Column(), Row(first, Cell(10, 20))); Css.SetStyle(table, Fixed); Layout(table);
        Assert.Equal(300, table.ActualWidth, 3); Assert.Equal(150, first.ActualWidth, 3); Assert.True(first.IsVisible);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void RowSpansKeepTheirOriginalContentHeightAndClipAnyRemovedTrack(int hidden)
    {
        var span = Cell(10, 90); Grid.SetRowSpan(span, 3); Css.SetStyle(span, "display:table-cell;vertical-align:top;visibility:visible;");
        var rows = new[] { Row(span, Cell(10, 30)), Row(Cell(10, 30)), Row(Cell(10, 30)) }; var table = Table(rows); Layout(table);
        Css.SetStyle(rows[hidden], "display:table-row;visibility:collapse;"); Layout(table);
        Assert.Equal(60, table.ActualHeight, 3); Assert.Equal(60, span.ActualHeight, 3); Assert.Equal(90, ((FrameworkElement)span.Child!).ActualHeight, 3); Assert.True(span.IsVisible);
        Assert.Equal(60, CssTableLayout.WrapperClip(span, null)!.Bounds.Height, 3);
    }

    [Fact]
    public void CaptionsAndExplicitGridHeightContractOnlyOnce()
    {
        var caption = new Border { Height = 10 }; Css.SetStyle(caption, "display:table-caption;"); var column = Column(); var row = Row(Cell(10, 20), Cell(10, 20), Cell(10, 20));
        var table = Table(caption, Column(), column, Column(), row, Row(Cell(10, 20), Cell(10, 20), Cell(10, 20)));
        Css.SetStyle(table, Fixed + "height:80px;"); Layout(table); Assert.Equal(90, table.ActualHeight, 3);
        Css.SetStyle(row, "display:table-row;visibility:collapse;"); Css.SetStyle(column, "display:table-column;visibility:collapse;"); Layout(table);
        Assert.Equal(200, table.ActualWidth, 3); Assert.Equal(50, table.ActualHeight, 3); Assert.Equal(200, caption.ActualWidth, 3);
        Assert.Equal(10, table.Children.OfType<StackPanel>().Last().VisualBounds.Top, 3);
    }

    [Fact]
    public void AnonymousTablesReleaseRowSpaceWithoutChangingSourceOwnership()
    {
        var row = Row(Cell(50, 30)); var kept = Row(Cell(20, 20)); var suffix = new Border { Height = 12 };
        var root = new StackPanel(); root.Children.Add(row); root.Children.Add(kept); root.Children.Add(suffix); Css.SetStyle(root, "display:block;"); Layout(root);
        Assert.Equal(50, suffix.VisualBounds.Top, 3); Css.SetStyle(row, "display:table-row;visibility:collapse;"); Layout(root);
        Assert.Equal(20, suffix.VisualBounds.Top, 3); Assert.Same(root, row.VisualParent); Assert.Equal(3, root.Children.Count);
        Css.SetStyle(row, "display:table-row;"); Layout(root); Assert.Equal(50, suffix.VisualBounds.Top, 3);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PointHitsUseVisibleSourceCellsAndNeverAnOverridingHiddenTrackChild(bool rtl)
    {
        var columns = new[] { Column(), Column(), Column() }; var cells = new[] { Cell(100, 20), Cell(100, 20), Cell(100, 20) };
        Css.SetStyle(cells[1].Child!, "visibility:visible;"); var table = Table([.. columns, Row(cells)]); Css.SetStyle(table, Fixed + (rtl ? "direction:rtl;" : "")); Layout(table);
        Css.SetStyle(columns[1], "display:table-column;visibility:collapse;"); Layout(table);
        var hit = VisualTreeHelper.HitTest(table, new Point(rtl ? 50 : 150, 10))!.VisualHit;
        Assert.Same(cells[2].Child, hit); Assert.False(cells[1].Child!.IsVisible);
    }

    [Theory]
    [InlineData("hidden")] [InlineData("collapse")]
    public void AnonymousTextPaintInheritsCellVisibilityAndHonorsAVisibleSourceOverride(string visibility)
    {
        var text = new TextBlock { Text = "隐藏文字" }; Css.SetStyle(text, "display:block;"); var cell = Cell(10, 20); cell.Child = text;
        var table = Table(Row(cell)); Layout(table); var initial = new TextCapture(); table.Render(initial); Assert.Contains("隐藏文字", initial.Text);
        Css.SetStyle(cell, "display:table-cell;visibility:" + visibility + ";"); Layout(table);
        var hidden = new TextCapture(); table.Render(hidden); Assert.DoesNotContain("隐藏文字", hidden.Text);
        var body = Assert.IsAssignableFrom<UIElement>(VisualTreeHelper.GetChild(text, 0)); Assert.False(body.IsVisible);
        Css.SetStyle(text, "display:block;visibility:visible;"); Layout(table); var visible = new TextCapture(); table.Render(visible); Assert.Contains("隐藏文字", visible.Text);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DocumentsWithEveryRowOrEveryColumnCollapsedHaveNoVisibleOrSelectableParagraphs(bool rows)
    {
        var row = DocRow(DocCell("保留来源")); var table = DocTable(row); var column = new TableColumn(); table.Columns.Add(column);
        Css.SetStyle(table, "width:300px;height:60px;table-layout:fixed;border-collapse:collapse;");
        Css.SetStyle(rows ? row : column, "visibility:collapse;"); var document = Doc(table); using var body = Body(document);
        Assert.False(body.Paragraphs[0].IsVisible); Assert.Empty(body.Lines); Assert.Equal(0, body.OffsetFromPoint(new(5, 5)));
        Assert.Equal(rows ? 0 : 60, body.ExtentHeight, 3); Assert.Contains("保留来源", document.GetText());
        Assert.Equal(1, document.ViewerPaginator.PageCount);
    }

    [Fact]
    public void DocumentRowGroupsRemoveVisibleOverridesInTheirFullyCollapsedCells()
    {
        var cell = DocCell("组内原文"); Css.SetStyle(cell.Blocks[0], "visibility:visible;"); var row = DocRow(cell); Css.SetStyle(row, "visibility:visible;");
        var table = DocTable(row); var kept = new TableRowGroup(); kept.Rows.Add(DocRow(DocCell("组外原文"))); table.RowGroups.Add(kept);
        using var body = Body(Doc(table)); var keptHeight = body.TableCells.Single(cell => ReferenceEquals(cell.Source, kept.Rows[0].Cells[0])).Bounds.Height;
        Css.SetStyle(table.RowGroups[0], "visibility:collapse;"); Relayout(body);
        Assert.False(body.Paragraphs[0].IsVisible); Assert.True(body.Paragraphs[1].IsVisible); Assert.Equal(keptHeight, body.ExtentHeight, 3);
    }

    [Fact]
    public void AnExplicitlyVisibleDocumentParagraphCanSurviveInsideAPartlyCollapsedRowSpan()
    {
        var span = DocCell("仍可显示"); span.RowSpan = 2; Css.SetStyle(span.Blocks[0], "visibility:visible;"); var first = DocRow(span, DocCell("隐藏格"));
        var kept = DocRow(DocCell("显示格")); using var body = Body(Doc(DocTable(first, kept)));
        var keptHeight = body.TableCells.Single(cell => ReferenceEquals(cell.Source, kept.Cells[0])).Bounds.Height;
        Css.SetStyle(first, "visibility:collapse;"); Relayout(body);
        Assert.True(body.Paragraphs[0].IsVisible); Assert.False(body.Paragraphs[1].IsVisible); Assert.True(body.Paragraphs[2].IsVisible);
        Assert.Equal(keptHeight, body.GetAdditionalChildLayoutClip(body.Paragraphs[0])!.Bounds.Height, 3);
    }

    [Fact]
    public void NestedDocumentBordersCannotPaintIntoTheNeighborAfterAnOuterColumnCollapses()
    {
        var nested = DocTable(DocRow(DocCell("嵌套原文"))); Css.SetStyle(nested, "width:200px;border-collapse:collapse;border:4px solid blue;");
        var cell = new TableCell(nested) { Padding = new(0) }; cell.ColumnSpan = 2; Css.SetStyle(cell, "border:0;");
        var table = DocTable(DocRow(cell, DocCell("外格"))); Css.SetStyle(table, "width:300px;table-layout:fixed;");
        foreach (var _ in new int[3]) table.Columns.Add(new TableColumn()); using var body = Body(Doc(table));
        Css.SetStyle(table.Columns[1], "visibility:collapse;"); Relayout(body);
        Assert.True(Count(body, Colors.Blue, new(0, 0, 100, body.ActualHeight)) > 0);
        Assert.Equal(0, Count(body, Colors.Blue, new(101, 0, 199, body.ActualHeight)));
        Assert.Equal(100, body.GetAdditionalChildLayoutClip(body.Paragraphs[0])!.Bounds.Width, 3);
    }

    [Fact]
    public void RemovingAnImageBorderColumnSuppressesItsWholeImageAndRestoresItExactly()
    {
        var cell = Cell(10, 20); Css.SetStyle(cell, "display:table-cell;border:4px solid red;border-image:image(blue) 20% fill / 1;");
        var column = Column(); var table = Table(column, Column(), Row(cell, Cell(10, 20))); Css.SetStyle(table, Fixed + "border-collapse:collapse;"); Layout(table); var count = Count(table, Colors.Blue); Assert.True(count > 0);
        Css.SetStyle(column, "display:table-column;visibility:collapse;"); Layout(table); Assert.Equal(0, Count(table, Colors.Blue));
        Css.SetStyle(column, "display:table-column;"); Layout(table); Assert.Equal(count, Count(table, Colors.Blue));
    }

    [Fact]
    public void ChangingTheSpanOfACollapsedColumnInvalidatesItsOwnerAndKeepsSourceCells()
    {
        var column = Column(); Css.SetStyle(column, "display:table-column;visibility:collapse;"); var cells = new[] { Cell(10, 20), Cell(10, 20), Cell(10, 20) };
        var row = Row(cells); var table = Table(Column(), column, Column(), row); Css.SetStyle(table, Fixed); Layout(table); Assert.Equal(200, table.ActualWidth, 3);
        Grid.SetColumnSpan(column, 2); Layout(table); Assert.Equal(150, table.ActualWidth, 3); Assert.False(cells[2].IsVisible);
        Grid.SetColumnSpan(column, 1); Layout(table); Assert.Equal(200, table.ActualWidth, 3); Assert.True(cells[2].IsVisible); Assert.All(cells, cell => Assert.Same(row, cell.VisualParent));
    }

    [Fact]
    public void DocumentCollapsedBordersReResolveAtTheNewSharedBoundary()
    {
        var first = DocCell("甲"); var removed = DocCell("乙"); var last = DocCell("丙");
        Css.SetStyle(first, "border-bottom:4px solid red;"); Css.SetStyle(removed, "border:10px solid blue;"); Css.SetStyle(last, "border-top:6px solid green;");
        var row = DocRow(removed); var table = DocTable(DocRow(first), row, DocRow(last)); Css.SetStyle(table, "width:300px;border-collapse:collapse;");
        using var body = Body(Doc(table)); Css.SetStyle(row, "visibility:collapse;"); Relayout(body);
        Assert.Equal(0, Count(body, Colors.Blue)); Assert.True(Count(body, Colors.Green) > 0);
    }

    private const string Fixed = "display:table;width:300px;table-layout:fixed;";
    private static Border Column() { var column = new Border(); Css.SetStyle(column, "display:table-column;"); return column; }
    private static Border Cell(double width, double height) { var cell = new Border { Child = new Border { Width = width, Height = height } }; Css.SetStyle(cell, "display:table-cell;vertical-align:top;"); return cell; }
    private static StackPanel Row(params UIElement[] children) { var row = new StackPanel(); foreach (var child in children) row.Children.Add(child); Css.SetStyle(row, "display:table-row;"); return row; }
    private static StackPanel Table(params UIElement[] children) { var table = new StackPanel(); foreach (var child in children) table.Children.Add(child); Css.SetStyle(table, "display:table;"); return table; }
    private static void Flush() { Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); }
    private static void Layout(FrameworkElement root) { Flush(); root.Measure(new(600, double.PositiveInfinity)); root.Arrange(new(0, 0, root.DesiredSize.Width, Math.Max(1, root.DesiredSize.Height))); }
    private static TableCell DocCell(string text) { var cell = new TableCell(new Paragraph(new Run(text)) { Margin = new(0) }) { Padding = new(0) }; Css.SetStyle(cell, "border:0;vertical-align:top;"); return cell; }
    private static TableRow DocRow(params TableCell[] cells) { var row = new TableRow(); foreach (var cell in cells) row.Cells.Add(cell); return row; }
    private static Table DocTable(params TableRow[] rows) { var table = new Table { CellSpacing = 0 }; var group = new TableRowGroup(); foreach (var row in rows) group.Rows.Add(row); table.RowGroups.Add(group); return table; }
    private static FlowDocument Doc(params Block[] blocks) { var document = new FlowDocument { PageWidth = 300, PageHeight = 500, PagePadding = new(0), FontFamily = "PingFang SC", FontSize = 16, LineHeight = 24, Foreground = Brushes.Black }; foreach (var block in blocks) document.Blocks.Add(block); return document; }
    private static FlowDocumentPresentation Body(FlowDocument doc) { var body = new FlowDocumentPresentation(); body.SetDocument(doc); Relayout(body); return body; }
    private static void Relayout(FlowDocumentPresentation body) { Flush(); body.Measure(new(300, 1500)); body.Arrange(new(0, 0, 300, Math.Max(1, body.DesiredSize.Height))); }
    private static byte[] Pixels(FrameworkElement root)
    {
        var width = Math.Max(1, (int)Math.Ceiling(root.ActualWidth)); var height = Math.Max(1, (int)Math.Ceiling(root.ActualHeight)); var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Bgra32);
        var drawing = new SoftwareDrawingContext(bitmap); drawing.DrawRectangle(Brushes.White, null, new(0, 0, width, height)); root.Render(drawing); drawing.Close(); var pixels = new byte[width * height * 4]; bitmap.CopyPixels(pixels, width * 4, 0); return pixels;
    }
    private static Color Pixel(FrameworkElement root, int x, int y) { var pixels = Pixels(root); var at = (y * Math.Max(1, (int)Math.Ceiling(root.ActualWidth)) + x) * 4; return Color.FromArgb(pixels[at + 3], pixels[at + 2], pixels[at + 1], pixels[at]); }
    private static int Count(FrameworkElement root, Color expected, Rect? region = null)
    {
        var pixels = Pixels(root); var count = 0; var width = Math.Max(1, (int)Math.Ceiling(root.ActualWidth)); var height = Math.Max(1, (int)Math.Ceiling(root.ActualHeight));
        var area = region ?? new Rect(0, 0, width, height);
        for (var y = Math.Max(0, (int)area.Top); y < Math.Min(height, area.Bottom); y++)
        for (var x = Math.Max(0, (int)area.Left); x < Math.Min(width, area.Right); x++)
        { var i = (y * width + x) * 4; if (Color.FromArgb(pixels[i + 3], pixels[i + 2], pixels[i + 1], pixels[i]) == expected) count++; }
        return count;
    }
    private sealed class TextCapture : DrawingContextAdapter, IOffsetDrawingContext
    {
        public Point Offset { get; set; }
        internal List<string> Text = [];
        public override void DrawText(FormattedText text, Point origin) => Text.Add(text.Text);
        public override void DrawLine(Pen pen, Point a, Point b) { }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rect) { }
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

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
public sealed class CssCollapsedBorderTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();

    [Theory]
    [InlineData("collapse")] [InlineData("separate")] [InlineData("inherit")] [InlineData("initial")] [InlineData("unset")] [InlineData("revert-layer")]
    public void SharedGrammarAcceptsBorderModelsAndGlobalValues(string value) => Assert.True(CssAuthoring.IsValueSupported("border-collapse", value));
    [Theory]
    [InlineData("auto")] [InlineData("hidden")] [InlineData("collapse separate")] [InlineData("1px")] [InlineData("discard")]
    public void InvalidBorderModelsAreRejected(string value) => Assert.False(CssAuthoring.IsValueSupported("border-collapse", value));

    [Fact]
    public void MetadataDescribesTheInheritedSeparateDefault()
    {
        var property = CssAuthoring.GetProperty("border-collapse"); Assert.True(property.IsInherited); Assert.Equal("separate", property.InitialValue); Assert.Contains("collapse", property.Values);
    }

    [Fact]
    public void SharedEdgesAreCountedOnceAndHalfBordersPositionCellContent()
    {
        var a = Cell(); var b = Cell(); var row = Row(a, b); var table = Table(row); Layout(table);
        Assert.Equal(200, table.ActualWidth, 3); Assert.Equal(28, table.ActualHeight, 3);
        Assert.Equal(2, a.TransformToAncestor(table).X, 3); Assert.Equal(100, b.TransformToAncestor(table).X, 3);
        Assert.Equal(98, a.ActualWidth, 3); Assert.Equal(4, ((FrameworkElement)a.Child!).TransformToAncestor(table).X, 3); Assert.Equal(102, ((FrameworkElement)b.Child!).TransformToAncestor(table).X, 3);
        Assert.Equal(new Thickness(4), a.BorderThickness); Assert.Equal(new Thickness(2), CssBoxMetrics.BackgroundInsets(a, 200).Border);
        Assert.Equal(Colors.Red, Pixel(table, 99, 12)); Assert.Equal(Colors.Red, Pixel(table, 101, 12)); Assert.Equal(Colors.White, Pixel(table, 102, 12));
    }

    [Fact]
    public void TablePaddingSpacingAndEmptyCellHidingDoNotApplyToCollapsedGrids()
    {
        var a = Cell(); var b = Cell(); var table = Table(Row(a, b)); Css.SetStyle(table, TableStyle + "padding:30px;border-spacing:12px 17px;empty-cells:hide;"); Layout(table);
        Assert.Equal(28, table.ActualHeight, 3); Assert.Equal(2, a.TransformToAncestor(table).X, 3); Assert.Equal(a.VisualBounds.Right, b.VisualBounds.Left, 3);
        var empty = new Border(); Css.SetStyle(empty, "display:table-cell;padding:12px;border:4px solid red;"); table.Children.Clear(); table.Children.Add(Row(empty)); Layout(table);
        Assert.Equal(32, table.ActualHeight, 3); Assert.True(Count(table, Colors.Red) > 500);
    }

    [Theory]
    [InlineData("hidden", false)] [InlineData("none", true)]
    public void HiddenSuppressesItsNeighborWhileNoneYieldsToIt(string style, bool visible)
    {
        var a = Cell("border-right:20px " + style + " red;"); var b = Cell("border-left:6px solid blue;"); var table = Table(Row(a, b)); Layout(table);
        var edge = (int)Math.Round(b.TransformToAncestor(table).X); Assert.Equal(visible ? Colors.Blue : Colors.White, Pixel(table, edge, 12));
        Assert.Equal(visible ? 3 : 0, CssBoxMetrics.BackgroundInsets(a, 200).Border.Right, 3);
    }

    [Fact]
    public void WiderBorderWinsBeforeStyleAndUpdatesBothContentInsets()
    {
        var a = Cell("border-right:4px double red;"); var b = Cell("border-left:8px dotted blue;"); var table = Table(Row(a, b)); Layout(table);
        Assert.Equal(4, CssBoxMetrics.BackgroundInsets(a, 200).Border.Right, 3); Assert.Equal(4, b.Child!.VisualBounds.X, 3);
        var x = (int)Math.Round(b.TransformToAncestor(table).X); Assert.True(Count(table, Colors.Blue, new(x - 4, 6, 8, table.ActualHeight - 12)) > 0);
        Assert.Equal(0, Count(table, Colors.Red, new(x - 4, 6, 8, table.ActualHeight - 12)));
    }

    [Theory]
    [InlineData("double", "solid")] [InlineData("solid", "dashed")] [InlineData("dashed", "dotted")]
    [InlineData("dotted", "ridge")] [InlineData("ridge", "outset")] [InlineData("outset", "groove")] [InlineData("groove", "inset")]
    public void EqualWidthsUseTheCssLineStylePriority(string preferred, string other)
    {
        var a = Cell("border:0;border-right:6px " + preferred + " red;", 60); var b = Cell("border:0;border-left:6px " + other + " blue;", 60); var table = Table(Row(a, b)); Layout(table);
        var edge = b.TransformToAncestor(table).X; var area = new Rect(edge - 3, 8, 6, 44); var pixels = Pixels(table);
        var red = 0; var blue = 0;
        ForPixels(table, pixels, area, color => { if (color.R > color.B + 20) red++; if (color.B > color.R + 20) blue++; });
        Assert.True(red > 0); Assert.Equal(0, blue);
    }

    [Theory]
    [InlineData(0, "red")] [InlineData(1, "green")] [InlineData(2, "blue")]
    [InlineData(3, "orange")] [InlineData(4, "purple")] [InlineData(5, "black")]
    public void EqualWidthsAndStylesPreferCellRowGroupColumnGroupThenTable(int highest, string winner)
    {
        var cell = Cell("border:0;"); var row = Row(cell); var group = new StackPanel(); group.Children.Add(row); Css.SetStyle(group, "display:table-row-group;");
        var column = new Border(); Css.SetStyle(column, "display:table-column;"); var columnGroup = new StackPanel(); columnGroup.Children.Add(column); Css.SetStyle(columnGroup, "display:table-column-group;");
        var table = Table(columnGroup, group); var sources = new FrameworkElement[] { table, columnGroup, column, group, row, cell }; var colors = new[] { "red", "green", "blue", "orange", "purple", "black" };
        for (var i = 0; i <= highest; i++) Css.SetStyle(sources[i], Css.GetStyle(sources[i]) + $"border-left:4px solid {colors[i]};");
        Layout(table); Assert.Equal((Color)ColorConverter.ConvertFromString(winner)!, Pixel(table, 1, 12));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SameKindColorTiesFollowTheSourceColumnInBothDirections(bool rtl)
    {
        var a = Cell("border:0;border-inline-end:4px solid red;"); var b = Cell("border:0;border-inline-start:4px solid blue;"); var table = Table(Row(a, b));
        Css.SetStyle(table, TableStyle + (rtl ? "direction:rtl;" : "direction:ltr;")); Layout(table);
        var edge = rtl ? a.TransformToAncestor(table).X : b.TransformToAncestor(table).X; Assert.Equal(Colors.Red, Pixel(table, (int)edge, 12));
        Assert.Equal(!rtl, a.TransformToAncestor(table).X < b.TransformToAncestor(table).X);
    }

    [Fact]
    public void TopCellWinsAHorizontalColorTie()
    {
        var a = Cell("border:0;border-bottom:4px solid red;"); var b = Cell("border:0;border-top:4px solid blue;"); var table = Table(Row(a), Row(b)); Layout(table);
        Assert.Equal(Colors.Red, Pixel(table, 100, (int)b.TransformToAncestor(table).Y));
    }

    [Fact]
    public void SpanningCellsSuppressGridEdgesThroughTheirInterior()
    {
        var span = Cell("border:0;", 60); Grid.SetRowSpan(span, 2); Grid.SetColumnSpan(span, 2); var first = Row(span, Cell("border:0;")); var second = Row(Cell("border:0;"));
        Css.SetStyle(first, "display:table-row;border-bottom:4px solid red;");
        var column = new Border(); Css.SetStyle(column, "display:table-column;border-right:4px solid blue;"); var table = Table(column, first, second); Layout(table);
        var y = (int)second.TransformToAncestor(table).Y; Assert.Equal(Colors.White, Pixel(table, 40, y)); Assert.Equal(Colors.Red, Pixel(table, 170, y));
        Assert.Equal(0, Count(table, Colors.Blue, new(50, 6, 30, 48))); Assert.Same(first, span.VisualParent);
    }

    [Fact]
    public void TranslucentBordersDoNotAccumulateAlphaAtSharedJunctions()
    {
        Border Make() => Cell("border:6px solid rgb(255 0 0 / 50%);", 40);
        var a = Make(); var b = Make(); var c = Make(); var d = Make(); var table = Table(Row(a, b), Row(c, d)); Layout(table);
        var point = d.TransformToAncestor(table); var color = Pixel(table, (int)point.X, (int)point.Y);
        Assert.Equal(255, color.R); Assert.InRange(color.G, (byte)126, (byte)129); Assert.Equal(color.G, color.B);
    }

    [Fact]
    public void CollapsedCellsKeepTheirBorderImagesAndIgnoreRoundedCorners()
    {
        var a = Cell("border-image:image(green) 20% fill / 1;border-radius:40px;"); var table = Table(Row(a));
        Css.SetStyle(table, TableStyle + "border-image:image(green) 20% fill / 1;border-radius:40px;"); Layout(table);
        Assert.True(Count(table, Colors.Green) > 100); Assert.Equal(Colors.Green, Pixel(table, 1, 1));
        Css.SetStyle(a, "display:table-cell;border:4px solid red;border-radius:40px;"); Css.SetStyle(table, TableStyle + "border-radius:40px;"); Layout(table);
        Assert.Equal(Colors.Red, Pixel(table, 1, 1)); Assert.Equal(Colors.White, Pixel(table, 10, 10));
    }

    [Fact]
    public void ImageCellKeepsItsOwnBorderMetricsAgainstAWiderNeighbor()
    {
        var a = Cell("border-image:image(green) 20% / 1 stretch;"); var b = Cell("border-left:12px solid blue;"); var table = Table(Row(a, b)); Layout(table);
        Assert.Equal(2, CssBoxMetrics.BackgroundInsets(a, 200).Border.Right, 3); Assert.Equal(6, b.Child!.VisualBounds.X, 3);
        Assert.Equal(Colors.Green, Pixel(table, (int)b.TransformToAncestor(table).X, 12));
    }

    [Fact]
    public void CollapsedTableImagesPaintTheOuterGridBorder()
    {
        var table = Table(Row(Cell("border:0;"))); Css.SetStyle(table, TableStyle + "border:10px solid red;border-image:image(green) 20% / 1 stretch;"); Layout(table);
        Assert.Equal(Colors.Green, Pixel(table, 1, 1)); Assert.True(Count(table, Colors.Green) > 100); Assert.Equal(0, Count(table, Colors.Red));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void EmptyCollapsedTablesKeepTheirSpecifiedBoxAndBorder(bool document)
    {
        if (document)
        {
            var table = DocTable(); Css.SetStyle(table, "width:200px;height:40px;box-sizing:border-box;border-collapse:collapse;border:4px solid red;padding:30px;"); using var body = Body(Doc(table));
            Assert.Equal(40, body.ExtentHeight, 3); Assert.Equal(Colors.Red, Pixel(body, 1, 1)); Assert.Equal(Colors.White, Pixel(body, 10, 10));
        }
        else
        {
            var table = Table(); Css.SetStyle(table, TableStyle + "height:40px;border:4px solid red;padding:30px;"); Layout(table);
            Assert.Equal(200, table.ActualWidth, 3); Assert.Equal(40, table.ActualHeight, 3); Assert.Equal(Colors.Red, Pixel(table, 1, 1)); Assert.Equal(Colors.White, Pixel(table, 10, 10));
        }
    }

    [Fact]
    public void LaterThickerOuterEdgesReserveDirtyInkInTheTableMargin()
    {
        var first = Cell("border:2px solid red;"); var second = Cell("border:2px solid red;border-left:12px solid blue;"); var table = Table(Row(first), Row(second)); Layout(table);
        Assert.Equal(1, CssBoxMetrics.BackgroundInsets(table, 200).Border.Left, 3); Assert.Equal(6, second.Child!.VisualBounds.X, 3); Assert.True(table.GetExtraDirtyPadding() >= 5);
    }

    [Theory]
    [InlineData("border-box", 120d)] [InlineData("content-box", 124d)]
    public void CaptionAndSpecifiedHeightIncludeTheCollapsedGridOnlyOnce(string sizing, double height)
    {
        var caption = new Border { Child = new Border { Height = 20 } }; Css.SetStyle(caption, "display:table-caption;background:yellow;"); var cell = Cell(); var table = Table(caption, Row(cell));
        Css.SetStyle(table, TableStyle + $"height:100px;box-sizing:{sizing};padding:30px;"); Layout(table);
        Assert.Equal(height, table.ActualHeight, 3); Assert.Equal(Colors.Yellow, Pixel(table, 100, 10)); Assert.Equal(Colors.Red, Pixel(table, 100, 20));
    }

    [Fact]
    public void ChangingNativeBordersAndRemovingCollapseRestoresTheOriginalCells()
    {
        var a = Cell(); var b = Cell(); var table = Table(Row(a, b)); var parent = a.VisualParent; Layout(table); var initial = Pixels(table);
        a.BorderThickness = new(12); Layout(table); Assert.Equal(6, b.Child!.VisualBounds.X, 3); Assert.Equal(44, table.ActualHeight, 3);
        a.ClearValue(Border.BorderThicknessProperty); Layout(table); Assert.Equal(initial, Pixels(table));
        Css.SetStyle(table, TableStyle.Replace("collapse", "separate") + "border-spacing:6px;padding:10px;"); Layout(table);
        Assert.Equal(16, a.TransformToAncestor(table).X, 3); Assert.Equal(4, a.Child!.VisualBounds.X, 3); Assert.Same(parent, a.VisualParent);
        Css.SetStyle(table, TableStyle); Layout(table); Assert.Equal(initial, Pixels(table));
    }

    [Fact]
    public void InheritedModelAndExplicitInheritRespondToAncestorChanges()
    {
        var a = Cell(); var table = Table(Row(a)); Css.SetStyle(table, TableStyle.Replace("border-collapse:collapse;", "border-collapse:inherit;")); var root = new StackPanel(); root.Children.Add(table);
        Css.SetStyle(root, "display:block;border-collapse:collapse;"); Layout(root); Assert.Equal(2, a.Child!.VisualBounds.X, 3);
        Css.SetStyle(root, "display:block;border-collapse:separate;"); Layout(root); Assert.Equal(4, a.Child.VisualBounds.X, 3);
        Css.SetStyle(root, "display:block;border-collapse:collapse;"); Layout(root); Assert.Equal(2, a.Child.VisualBounds.X, 3);
    }

    [Fact]
    public void AnonymousTablesUseInheritedCollapseWithoutReparentingSourceRows()
    {
        var a = Cell(); var b = Cell(); var row = Row(a, b); var root = new StackPanel(); root.Children.Add(row); Css.SetStyle(root, "display:block;border-collapse:collapse;"); Layout(root);
        Assert.Same(root, row.VisualParent); Assert.Same(row, a.VisualParent); Assert.Equal(2, a.Child!.VisualBounds.X, 3); Assert.True(Count(root, Colors.Red) > 100);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public void AnonymousIntrinsicTracksRestoreAfterModelDirectionAndDensityChanges(bool rtl, bool density)
    {
        Border Content(string value)
        {
            var text = new TextBlock { Text = value }; Css.SetStyle(text, "display:block;white-space:normal;line-height:1.5;");
            var cell = new Border { Child = text }; Css.SetStyle(cell, "display:table-cell;padding:8px;border:2px solid red;"); return cell;
        }
        var a = Content("原始节点"); var b = Content("父级保持不变"); var root = new StackPanel(); root.Children.Add(Row(a, b));
        const string style = "display:block;font-family:'PingFang SC';font-size:16px;";
        Css.SetStyle(root, style + "border-collapse:collapse;"); Layout(root); var initial = new[] { a.ActualWidth, b.ActualWidth }; var firstPaint = new TextCapture(); root.Render(firstPaint);
        Css.SetStyle(root, style + "border-collapse:separate;"); Layout(root);
        Css.SetStyle(root, style + "border-collapse:collapse;" + (rtl ? "direction:rtl;" : "direction:ltr;"));
        if (density) VisualTreeHelper.SetRootDpi(root, new(2, 2)); Layout(root);
        Css.SetStyle(root, style + "border-collapse:collapse;direction:ltr;"); VisualTreeHelper.SetRootDpi(root, new(1, 1)); Layout(root);
        Assert.Equal(initial, new[] { a.ActualWidth, b.ActualWidth });
        var restored = new TextCapture(); root.Render(restored); Assert.Equal(firstPaint.Origins, restored.Origins);
    }

    [Fact]
    public void PreparingFlowTextPreservesTheAlreadyArrangedOwnedParagraph()
    {
        var text = new TextBlock { Text = "原始节点", FontFamily = "PingFang SC", FontSize = 16, Foreground = Brushes.Black };
        Css.SetStyle(text, "display:block;line-height:1.5;"); Layout(text); Assert.True(text.TryCssInlineAllocation(out var initial));
        var before = new TextCapture(); text.Render(before); text.PrepareCssContent();
        Assert.True(text.TryCssInlineAllocation(out var prepared)); Assert.Equal(initial, prepared);
        var after = new TextCapture(); text.Render(after); Assert.Equal(before.Origins, after.Origins);
    }

    [Fact]
    public void TableCellTextAndHitTestingRemainAvailableAfterSharedBorderPainting()
    {
        var text = new TextBlock { Text = "原始文字", FontFamily = "PingFang SC", FontSize = 16, Foreground = Brushes.Black }; Css.SetStyle(text, "display:table-cell;border:4px solid red;padding:6px;"); var table = Table(Row(text)); Layout(table);
        var capture = new TextCapture(); table.Render(capture); Assert.Contains("原始文字", capture.Text);
        Assert.Same(text, table.InputHitTest(new(30, 15))); Assert.Same(table.Children[0], text.VisualParent);
    }

    [Fact]
    public void ChangingModelsKeepsManagedKeyboardFocusAndAutomationNames()
    {
        Keyboard.Initialize(); Keyboard.ClearFocus(); var a = new Button { Content = "第一格" }; var b = new Button { Content = "第二格" }; AutomationProperties.SetName(b, "原始第二格");
        var first = Cell(); first.Child = a; var second = Cell(); second.Child = b; var table = Table(Row(first, second)); var window = new DisplayedTestWindow { Width = 240, Height = 180, Content = table, TitleBarStyle = WindowTitleBarStyle.Native };
        try
        {
            Layout(window); Assert.True(a.Focus()); Assert.True(KeyboardNavigation.MoveFocus(a)); Assert.Same(b, Keyboard.FocusedElement);
            Css.SetStyle(table, TableStyle.Replace("collapse", "separate")); Layout(window); Assert.Same(b, Keyboard.FocusedElement);
            Assert.Equal("原始第二格", FrameworkElementAutomationPeer.CreatePeerForElement(b)!.GetName()); Assert.True(KeyboardNavigation.MoveFocus(b, reverse: true)); Assert.Same(a, Keyboard.FocusedElement);
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    [Fact]
    public void DocumentTablesUseTheSameHalfBordersAndIgnorePaddingAndNativeSpacing()
    {
        var a = DocCell("甲"); var b = DocCell("乙"); var table = DocTable(DocRow(a, b)); table.CellSpacing = 20; Css.SetStyle(table, "width:200px;box-sizing:border-box;table-layout:fixed;border-collapse:collapse;padding:30px;border:4px solid green;");
        var doc = Doc(table); var original = doc.GetText(); using var body = Body(doc);
        Assert.Equal(body.Paragraphs.Max(text => text.ActualHeight) + 8, body.ExtentHeight, 3); Assert.Equal(2, body.TableCells[0].Bounds.X, 3); Assert.Equal(100, body.TableCells[1].Bounds.X, 3);
        Assert.Equal(4, body.Paragraphs[0].TransformToAncestor(body).X, 3); Assert.Equal(102, body.Paragraphs[1].TransformToAncestor(body).X, 3);
        Assert.Equal(Colors.Red, Pixel(body, 100, 12)); Assert.Equal(Colors.White, Pixel(body, 102, 12)); Assert.Equal(original, doc.GetText()); Assert.Same(a, body.TableCells[0].Source);
    }

    [Theory]
    [InlineData("hidden", false)] [InlineData("none", true)]
    public void DocumentCellsResolveHiddenAndNoneAgainstTheirNeighbors(string style, bool visible)
    {
        var a = DocCell("甲", "border-right:20px " + style + " red;"); var b = DocCell("乙", "border-left:6px solid blue;"); var table = DocTable(DocRow(a, b)); var doc = Doc(table); using var body = Body(doc);
        var edge = (int)body.TableCells[1].Bounds.X; Assert.Equal(visible ? Colors.Blue : Colors.White, Pixel(body, edge, 12));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CollapsedDocumentHitTestingKeepsBothSourceColumns(bool rtl)
    {
        var a = DocCell("甲列原文"); var b = DocCell("乙列原文"); var table = DocTable(DocRow(a, b));
        Css.SetStyle(table, "width:200px;table-layout:fixed;border-collapse:collapse;" + (rtl ? "direction:rtl;" : "direction:ltr;"));
        var doc = Doc(table); using var body = Body(doc);
        for (var index = 0; index < 2; index++)
        {
            var text = body.Paragraphs[index]; var origin = text.TransformToAncestor(body); var offset = body.OffsetFromPoint(new(origin.X + 1, origin.Y + 8));
            var source = (Paragraph)(index == 0 ? a : b).Blocks[0]; var start = doc.GetDocumentOffset(source, 0); Assert.InRange(offset, start, start + text.Text.Length);
        }
        Assert.Equal(!rtl, body.Paragraphs[0].TransformToAncestor(body).X < body.Paragraphs[1].TransformToAncestor(body).X);
    }

    [Theory]
    [InlineData("row")] [InlineData("column")] [InlineData("group")]
    public void DocumentStructuralBordersParticipateInConflicts(string kind)
    {
        var cell = DocCell("甲", "border:0;"); var row = DocRow(cell); var table = DocTable(row); var doc = Doc(table);
        DependencyObject source = kind switch { "row" => row, "group" => table.RowGroups[0], _ => new TableColumn() };
        if (source is TableColumn column) table.Columns.Add(column);
        Css.SetStyle(source, "border-left:6px solid blue;"); using var body = Body(doc); Assert.Equal(Colors.Blue, Pixel(body, 1, 12)); Assert.Equal(6, body.Paragraphs[0].TransformToAncestor(body).X, 3);
    }

    [Fact]
    public void DocumentSpansSuppressInternalRowBorders()
    {
        var span = DocCell("跨行"); span.RowSpan = 2; var first = DocRow(span, DocCell("甲")); var second = DocRow(DocCell("乙"));
        Css.SetStyle(first, "border-bottom:6px solid blue;"); var table = DocTable(first, second); var doc = Doc(table); using var body = Body(doc);
        var y = (int)body.TableCells.Single(cell => ReferenceEquals(cell.Source, second.Cells[0])).Bounds.Top;
        Assert.NotEqual(Colors.Blue, Pixel(body, 40, y)); Assert.Equal(Colors.Blue, Pixel(body, 150, y)); Assert.Same(table.RowGroups[0], first.Parent);
    }

    [Fact]
    public void NestedDocumentTableBorderFramesFollowTheirShiftedContent()
    {
        var inner = DocTable(DocRow(DocCell("内文", "border:4px solid blue;"))); Css.SetStyle(inner, "width:100px;table-layout:fixed;border-collapse:collapse;");
        var cell = new TableCell(inner) { Padding = new(10) }; Css.SetStyle(cell, "border:4px solid red;"); var doc = Doc(new Paragraph(new Run("前文")) { Margin = new(0) }, DocTable(DocRow(cell))); using var body = Body(doc);
        var box = body.TableCells.Single(item => ReferenceEquals(item.Source, inner.RowGroups[0].Rows[0].Cells[0])).Bounds;
        Assert.True(box.Top >= 30); Assert.True(box.Left >= 10); Assert.Equal(Colors.Blue, Pixel(body, (int)box.Left, (int)box.Top + 10));
    }

    [Fact]
    public void DocumentBorderImagesSurviveModelChangesAndRemovalRestoresNormalBorders()
    {
        var cell = DocCell("正文", "border-image:image(green) 0 fill / 1;"); var table = DocTable(DocRow(cell)); var doc = Doc(table); using var body = Body(doc);
        Assert.True(Count(body, Colors.Green) > 100);
        Css.SetStyle(table, "width:200px;table-layout:fixed;border-collapse:separate;"); Relayout(body); Assert.True(Count(body, Colors.Green) > 100);
        Css.SetStyle(table, "width:200px;table-layout:fixed;border-collapse:collapse;"); Css.SetStyle(cell, "border:4px solid red;"); Relayout(body); Assert.Equal(0, Count(body, Colors.Green)); Assert.True(Count(body, Colors.Red) > 100);
    }

    [Fact]
    public void DocumentPagesPreserveEveryCollapsedBorderPixel()
    {
        var table = DocTable(DocRow(DocCell("甲")), DocRow(DocCell("乙")), DocRow(DocCell("丙")), DocRow(DocCell("丁"))); var doc = Doc(table); doc.LineHeight = 32; doc.PageHeight = 48;
        using var body = Body(doc); var expected = Count(body, Colors.Red); var actual = 0;
        for (var i = 0; i < doc.ViewerPaginator.PageCount; i++)
        {
            using var page = Assert.IsType<FlowDocumentPresentation>(doc.ViewerPaginator.GetPage(i).Visual); actual += Count(page, Colors.Red);
        }
        Assert.True(expected > 100); Assert.Equal(expected, actual);
    }

    [Fact]
    public void DocumentPagesKeepTheSameFractionalRowGeometryAndSourceCells()
    {
        var doc = Doc(DocTable(DocRow(DocCell("甲")), DocRow(DocCell("乙")), DocRow(DocCell("丙")))); doc.PageHeight = 48;
        using var body = Body(doc); Assert.Contains(body.TableCells, cell => cell.Bounds.Height != Math.Floor(cell.Bounds.Height));
        for (var i = 0; i < doc.ViewerPaginator.PageCount; i++)
        {
            using var page = Assert.IsType<FlowDocumentPresentation>(doc.ViewerPaginator.GetPage(i).Visual);
            Assert.Equal(body.TableCells, page.TableCells); Assert.Equal(body.ExtentHeight, page.ExtentHeight);
        }
    }

    private const string TableStyle = "display:table;width:200px;box-sizing:border-box;table-layout:fixed;border-collapse:collapse;";
    private static Border Cell(string extra = "", double height = 20) { var result = new Border { Child = new Border { Width = 10, Height = height } }; Css.SetStyle(result, "display:table-cell;border:4px solid red;vertical-align:top;" + extra); return result; }
    private static StackPanel Row(params UIElement[] children) { var row = new StackPanel(); foreach (var child in children) row.Children.Add(child); Css.SetStyle(row, "display:table-row;"); return row; }
    private static StackPanel Table(params UIElement[] children) { var table = new StackPanel(); foreach (var child in children) table.Children.Add(child); Css.SetStyle(table, TableStyle); return table; }
    private static void Flush() { Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); }
    private static void Layout(FrameworkElement root) { Flush(); root.Measure(new(200, double.PositiveInfinity)); root.Arrange(new(0, 0, 200, Math.Max(1, root.DesiredSize.Height))); }
    private static TableCell DocCell(string text, string extra = "") { var cell = new TableCell(new Paragraph(new Run(text)) { Margin = new(0) }) { Padding = new(0) }; Css.SetStyle(cell, "border:4px solid red;vertical-align:top;" + extra); return cell; }
    private static TableRow DocRow(params TableCell[] cells) { var row = new TableRow(); foreach (var cell in cells) row.Cells.Add(cell); return row; }
    private static Table DocTable(params TableRow[] rows) { var table = new Table { CellSpacing = 0 }; var group = new TableRowGroup(); foreach (var row in rows) group.Rows.Add(row); table.RowGroups.Add(group); Css.SetStyle(table, "width:200px;box-sizing:border-box;table-layout:fixed;border-collapse:collapse;"); return table; }
    private static FlowDocument Doc(params Block[] blocks) { var doc = new FlowDocument { PageWidth = 200, PageHeight = 200, PagePadding = new(0), FontFamily = "PingFang SC", FontSize = 16, LineHeight = 20, Foreground = Brushes.Black }; foreach (var block in blocks) doc.Blocks.Add(block); return doc; }
    private static FlowDocumentPresentation Body(FlowDocument doc) { var body = new FlowDocumentPresentation(); body.SetDocument(doc); Relayout(body); return body; }
    private static void Relayout(FlowDocumentPresentation body) { Flush(); body.Measure(new(200, 1500)); body.Arrange(new(0, 0, 200, Math.Max(1, body.DesiredSize.Height))); }
    private static byte[] Pixels(FrameworkElement root)
    {
        var width = Math.Max(1, (int)Math.Ceiling(root.ActualWidth)); var height = Math.Max(1, (int)Math.Ceiling(root.ActualHeight)); var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Bgra32);
        var drawing = new SoftwareDrawingContext(bitmap); drawing.DrawRectangle(Brushes.White, null, new(0, 0, width, height)); root.Render(drawing); drawing.Close(); var pixels = new byte[width * height * 4]; bitmap.CopyPixels(pixels, width * 4, 0); return pixels;
    }
    private static Color Pixel(FrameworkElement root, int x, int y) { var pixels = Pixels(root); var at = (y * (int)Math.Ceiling(root.ActualWidth) + x) * 4; return Color.FromArgb(pixels[at + 3], pixels[at + 2], pixels[at + 1], pixels[at]); }
    private static int Count(FrameworkElement root, Color color, Rect? area = null) { var count = 0; ForPixels(root, Pixels(root), area ?? new Rect(root.RenderSize), pixel => { if (pixel == color) count++; }); return count; }
    private static void ForPixels(FrameworkElement root, byte[] pixels, Rect area, Action<Color> action)
    {
        var width = (int)Math.Ceiling(root.ActualWidth); var height = (int)Math.Ceiling(root.ActualHeight);
        for (var y = Math.Max(0, (int)area.Top); y < Math.Min(height, area.Bottom); y++)
        for (var x = Math.Max(0, (int)area.Left); x < Math.Min(width, area.Right); x++)
        { var at = (y * width + x) * 4; action(Color.FromArgb(pixels[at + 3], pixels[at + 2], pixels[at + 1], pixels[at])); }
    }
    private sealed class TextCapture : DrawingContextAdapter, IOffsetDrawingContext
    {
        public Point Offset { get; set; }
        internal readonly List<string> Text = [];
        internal readonly List<(string Text, Point Origin)> Origins = [];
        public override void DrawText(FormattedText text, Point origin) { Text.Add(text.Text); Origins.Add((text.Text, new(origin.X + Offset.X, origin.Y + Offset.Y))); }
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

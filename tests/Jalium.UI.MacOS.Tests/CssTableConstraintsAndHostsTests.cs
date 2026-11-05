using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssTableConstraintsAndHostsTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);

    public void Dispose() => _context.Dispose();

    [Fact]
    public void MisparentedNestedTablePartsReconstructAnonymousWrappersAndElideWhitespace()
    {
        var host = new StackPanel();
        Css.SetStyle(host, "display:block;width:300px;");

        var cellA = new StackPanel();
        Css.SetStyle(cellA, "display:table-cell;width:90px;vertical-align:top;");
        var innerRow = new StackPanel();
        Css.SetStyle(innerRow, "display:table-row;");
        var innerCell1 = new Border { Width = 36, Height = 22 };
        var innerCell2 = new Border { Width = 44, Height = 22 };
        Css.SetStyle(innerCell1, "display:table-cell;");
        Css.SetStyle(innerCell2, "display:table-cell;");
        innerRow.Children.Add(innerCell1);
        innerRow.Children.Add(innerCell2);
        cellA.Children.Add(innerRow);

        var ws1 = new TextBlock { Text = "   \n\t  " };
        Css.SetStyle(ws1, "display:inline;white-space:normal;");

        var cellB = new Border { Width = 70, Height = 28 };
        Css.SetStyle(cellB, "display:table-cell;vertical-align:top;");

        var ws2 = new TextBlock { Text = "  " };
        Css.SetStyle(ws2, "display:inline;white-space:normal;");

        var row2 = new StackPanel();
        Css.SetStyle(row2, "display:table-row;");
        var row2Cell1 = new Border { Width = 90, Height = 18 };
        var row2Cell2 = new Border { Width = 70, Height = 18 };
        Css.SetStyle(row2Cell1, "display:table-cell;");
        Css.SetStyle(row2Cell2, "display:table-cell;");
        row2.Children.Add(row2Cell1);
        row2.Children.Add(row2Cell2);

        host.Children.Add(cellA);
        host.Children.Add(ws1);
        host.Children.Add(cellB);
        host.Children.Add(ws2);
        host.Children.Add(row2);

        Layout(host, 300, 200);

        // Source tree ownership is untouched.
        Assert.Same(host, cellA.Parent);
        Assert.Same(host, cellB.Parent);
        Assert.Same(host, row2.Parent);
        Assert.Same(cellA, innerRow.Parent);

        // Whitespace between table-cells is elided so cellA and cellB share row 0 at x=0 and x=90.
        Near(0, cellA.TransformToAncestor(host).X);
        Near(90, cellB.TransformToAncestor(host).X);
        Near(0, cellA.TransformToAncestor(host).Y);
        Near(0, cellB.TransformToAncestor(host).Y);

        // Inner stray table-row inside cellA creates a nested anonymous table with two side-by-side cells.
        Near(0, innerCell1.TransformToAncestor(cellA).X);
        Near(36, innerCell2.TransformToAncestor(cellA).X);

        // Toggle innerRow from table-row to block and back to table-row to verify anonymous flow-table rebuild.
        Css.SetStyle(innerRow, "display:block;");
        Css.SetStyle(innerCell1, "display:block;width:36px;height:22px;");
        Css.SetStyle(innerCell2, "display:block;width:44px;height:22px;");
        Layout(host, 300, 200);
        Near(0, innerCell1.TransformToAncestor(cellA).Y);
        Near(22, innerCell2.TransformToAncestor(cellA).Y);

        Css.SetStyle(innerRow, "display:table-row;");
        Css.SetStyle(innerCell1, "display:table-cell;");
        Css.SetStyle(innerCell2, "display:table-cell;");
        Layout(host, 300, 200);
        Near(0, innerCell1.TransformToAncestor(cellA).X);
        Near(36, innerCell2.TransformToAncestor(cellA).X);
        Near(0, innerCell2.TransformToAncestor(cellA).Y);
    }

    [Fact]
    public void ColumnAndColumnGroupMinMaxWidthBoundsApplyInCssTablesAndFlowDocumentTables()
    {
        // 1. Ordinary CSS table with <colgroup min-width:85px> and <col max-width:50px> plus trailing empty column.
        var table = new StackPanel();
        Css.SetStyle(table, "display:table;border-collapse:separate;border-spacing:0;");

        var colGroup = new StackPanel();
        Css.SetStyle(colGroup, "display:table-column-group;min-width:45px;");
        var col0 = new Border();
        var col1 = new Border();
        var col2 = new Border();
        Css.SetStyle(col0, "display:table-column;min-width:85px;");
        Css.SetStyle(col1, "display:table-column;width:120px;max-width:50px;");
        Css.SetStyle(col2, "display:table-column;min-width:40px;");
        colGroup.Children.Add(col0);
        colGroup.Children.Add(col1);
        table.Children.Add(colGroup);
        table.Children.Add(col2);

        var row = new StackPanel();
        Css.SetStyle(row, "display:table-row;");
        var cell0 = new StackPanel { Children = { new Border { Width = 30, Height = 20 } } };
        var cell1 = new StackPanel { Children = { new Border { Width = 20, Height = 20 } } };
        Css.SetStyle(cell0, "display:table-cell;");
        Css.SetStyle(cell1, "display:table-cell;");
        row.Children.Add(cell0);
        row.Children.Add(cell1);
        table.Children.Add(row);

        Layout(table, 400, 120);
        // col0 inherits min-width:85px from colgroup; col1 is clamped from 120px to max-width:50px; trailing empty col2 has min-width:40px.
        Near(85, cell0.ActualWidth);
        Near(50, cell1.ActualWidth);
        Near(175, table.ActualWidth);

        // 2. FlowDocument Table with TableColumn min-width and max-width constraints.
        var docTable = new Table { CellSpacing = 0 };
        Css.SetStyle(docTable, "width:auto;");
        var docCol0 = new TableColumn();
        var docCol1 = new TableColumn();
        var docCol2 = new TableColumn();
        Css.SetStyle(docCol0, "min-width:90px;");
        Css.SetStyle(docCol1, "width:140px;max-width:60px;");
        Css.SetStyle(docCol2, "min-width:35px;");
        docTable.Columns.Add(docCol0);
        docTable.Columns.Add(docCol1);
        docTable.Columns.Add(docCol2);

        var c0 = new TableCell(new Paragraph(new Run("A"))) { Padding = new Thickness(0), BorderThickness = new Thickness(0) };
        var c1 = new TableCell(new Paragraph(new Run("B"))) { Padding = new Thickness(0), BorderThickness = new Thickness(0) };
        var docGroup = new TableRowGroup();
        var docRow = new TableRow();
        docRow.Cells.Add(c0);
        docRow.Cells.Add(c1);
        docGroup.Rows.Add(docRow);
        docTable.RowGroups.Add(docGroup);

        var doc = new FlowDocument(docTable) { PagePadding = new Thickness(0), PageWidth = 400 };
        using var body = new FlowDocumentPresentation();
        body.SetDocument(doc);
        Layout(body, 400, 120);
        Near(90, body.TableCells[0].Bounds.Width);
        Near(60, body.TableCells[1].Bounds.Width);
    }

    [Fact]
    public void OverlappingColspanPercentagesAndNonlinearCalcPercentagesConvergeInIntrinsicAndUsedWidths()
    {
        // 1. Overlapping colspan percentage where both spanned columns already have explicit percentages (free.Length == 0).
        var table1 = new StackPanel();
        Css.SetStyle(table1, "display:table;width:200px;border-collapse:separate;border-spacing:0;");
        var r0 = new StackPanel();
        Css.SetStyle(r0, "display:table-row;");
        var c00 = new StackPanel { Children = { new Border { Width = 20, Height = 16 } } };
        var c01 = new StackPanel { Children = { new Border { Width = 20, Height = 16 } } };
        var c02 = new StackPanel { Children = { new Border { Width = 20, Height = 16 } } };
        Css.SetStyle(c00, "display:table-cell;width:20%;");
        Css.SetStyle(c01, "display:table-cell;width:20%;");
        Css.SetStyle(c02, "display:table-cell;");
        r0.Children.Add(c00);
        r0.Children.Add(c01);
        r0.Children.Add(c02);

        var r1 = new StackPanel();
        Css.SetStyle(r1, "display:table-row;");
        var span01 = new StackPanel { Children = { new Border { Width = 30, Height = 16 } } };
        Grid.SetColumnSpan(span01, 2);
        Css.SetStyle(span01, "display:table-cell;width:70%;");
        var c12 = new StackPanel { Children = { new Border { Width = 20, Height = 16 } } };
        Css.SetStyle(c12, "display:table-cell;");
        r1.Children.Add(span01);
        r1.Children.Add(c12);
        table1.Children.Add(r0);
        table1.Children.Add(r1);

        Layout(table1, 300, 120);
        // span01 requests 70% across columns 0 and 1 (which started at 20% + 20% = 40%); the extra 30% distributes 15% each -> 35% + 35% = 70% of 200px = 140px.
        Near(70, c00.ActualWidth);
        Near(70, c01.ActualWidth);
        Near(140, span01.ActualWidth);

        // 2. Nonlinear percentage clamp() on a colspan=2 cell in an intrinsic table, plus nested table cyclic isolation & resolution.
        var innerTable = new StackPanel();
        Css.SetStyle(innerTable, "display:table;border-collapse:separate;border-spacing:0;");
        var ir0 = new StackPanel();
        Css.SetStyle(ir0, "display:table-row;");
        var icSpan = new StackPanel { Children = { new Border { Width = 40, Height = 16 } } };
        Grid.SetColumnSpan(icSpan, 2);
        Css.SetStyle(icSpan, "display:table-cell;width:clamp(110px, 50%, 160px);");
        ir0.Children.Add(icSpan);
        var ir1 = new StackPanel();
        Css.SetStyle(ir1, "display:table-row;");
        var ic0 = new StackPanel { Children = { new Border { Width = 30, Height = 16 } } };
        var ic1 = new StackPanel { Children = { new Border { Width = 30, Height = 16 } } };
        Css.SetStyle(ic0, "display:table-cell;");
        Css.SetStyle(ic1, "display:table-cell;");
        ir1.Children.Add(ic0);
        ir1.Children.Add(ic1);
        innerTable.Children.Add(ir0);
        innerTable.Children.Add(ir1);

        Layout(innerTable, 400, 120);
        // Fixed-point bisection on clamp(110px, 50%, 160px) converges to 110px (55px + 55px) in intrinsic table sizing.
        Near(110, icSpan.ActualWidth);
        Near(110, innerTable.ActualWidth);

        var outerCell = new StackPanel { Children = { innerTable } };
        Css.SetStyle(outerCell, "display:table-cell;");
        var outerRow = new StackPanel { Children = { outerCell } };
        Css.SetStyle(outerRow, "display:table-row;");
        var outerTable = new StackPanel { Children = { outerRow } };
        Css.SetStyle(outerTable, "display:table;width:240px;border-collapse:separate;border-spacing:0;");

        Layout(outerTable, 400, 120);
        Near(240, outerTable.ActualWidth);
        Near(110, icSpan.ActualWidth);
        Near(110, innerTable.ActualWidth);
    }

    [Fact]
    public void CellPercentageHeightsAndRowspanBaselinesResolveCorrectlyInCssTablesAndFlowDocuments()
    {
        // 1. Explicit cell/table height enables percentage child height and updates vertical-align:middle offset; row-only height does not.
        var tableDefinite = new StackPanel();
        Css.SetStyle(tableDefinite, "display:table;width:180px;height:160px;border-collapse:separate;border-spacing:0;");
        var rowDefinite = new StackPanel();
        Css.SetStyle(rowDefinite, "display:table-row;");
        var cellDefinite = new StackPanel();
        Css.SetStyle(cellDefinite, "display:table-cell;vertical-align:middle;");
        var pctChild = new Border { Child = new Border { Width = 30, Height = 18 } };
        Css.SetStyle(pctChild, "display:block;height:50%;");
        cellDefinite.Children.Add(pctChild);
        rowDefinite.Children.Add(cellDefinite);
        tableDefinite.Children.Add(rowDefinite);

        Layout(tableDefinite, 240, 220);
        Near(160, cellDefinite.ActualHeight);
        Near(80, pctChild.ActualHeight);
        Near(40, pctChild.TransformToAncestor(cellDefinite).Y);

        var tableIndefinite = new StackPanel();
        Css.SetStyle(tableIndefinite, "display:table;width:180px;border-collapse:separate;border-spacing:0;");
        var rowOnly = new StackPanel();
        Css.SetStyle(rowOnly, "display:table-row;height:160px;");
        var cellIndefinite = new StackPanel();
        Css.SetStyle(cellIndefinite, "display:table-cell;vertical-align:top;");
        var autoChild = new Border { Child = new Border { Width = 30, Height = 18 } };
        Css.SetStyle(autoChild, "display:block;height:50%;");
        cellIndefinite.Children.Add(autoChild);
        rowOnly.Children.Add(cellIndefinite);
        tableIndefinite.Children.Add(rowOnly);

        Layout(tableIndefinite, 240, 220);
        Near(160, cellIndefinite.ActualHeight);
        Near(18, autoChild.ActualHeight);

        // 2. Rowspan > 1 baseline alignment: originating row 0 honors baseline-aligned first-line text + top padding.
        var baselineTable = new StackPanel();
        Css.SetStyle(baselineTable, "display:table;width:200px;border-collapse:separate;border-spacing:0;");
        var br0 = new StackPanel();
        Css.SetStyle(br0, "display:table-row;");
        var spanCell = new StackPanel { Children = { new TextBlock { Text = "Base", FontSize = 16 } } };
        Grid.SetRowSpan(spanCell, 2);
        Css.SetStyle(spanCell, "display:table-cell;padding-top:34px;vertical-align:baseline;");
        var peerCell0 = new StackPanel { Children = { new TextBlock { Text = "Peer", FontSize = 16 } } };
        Css.SetStyle(peerCell0, "display:table-cell;vertical-align:baseline;");
        br0.Children.Add(spanCell);
        br0.Children.Add(peerCell0);

        var br1 = new StackPanel();
        Css.SetStyle(br1, "display:table-row;");
        var peerCell1 = new Border { Width = 40, Height = 16 };
        Css.SetStyle(peerCell1, "display:table-cell;");
        br1.Children.Add(peerCell1);
        baselineTable.Children.Add(br0);
        baselineTable.Children.Add(br1);

        Layout(baselineTable, 240, 200);
        // Row 0 must be tall enough for spanCell's baseline + offset (>= 34px padding + line height), placing row 1 below it.
        Assert.True(peerCell1.TransformToAncestor(baselineTable).Y >= 46);
    }

    [Fact]
    public void FlowDocumentCaptionsPaginateAndTableOuterEffectsAndSpanBackgroundsAndCollapsedBordersAlign()
    {
        // 1. FlowDocument caption records table geometry and paginates independently of the table grid.
        var doc = new FlowDocument { PagePadding = new Thickness(0), PageWidth = 240, PageHeight = 80 };
        var docTable = new Section();
        Css.SetStyle(docTable, "display:table;width:200px;");
        var docCaption = new Paragraph(new Run("Table Caption Header"));
        Css.SetStyle(docCaption, "display:table-caption;caption-side:top;height:48px;");
        var docRow = new Section();
        Css.SetStyle(docRow, "display:table-row;");
        var docCell = new Paragraph(new Run("Row Body Cell"));
        Css.SetStyle(docCell, "display:table-cell;height:48px;");
        docRow.Blocks.Add(docCell);
        docTable.Blocks.Add(docCaption);
        docTable.Blocks.Add(docRow);
        doc.Blocks.Add(docTable);

        using var body = new FlowDocumentPresentation();
        body.SetDocument(doc);
        Layout(body, 240, 200);
        Assert.True(body.HasTables);
        var geometryField = typeof(FlowDocumentPresentation).GetField("_tableGeometry", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var geometryMap = (Dictionary<DependencyObject, Rect>)geometryField.GetValue(body)!;
        Assert.True(geometryMap.ContainsKey(docCaption));
        Assert.True(geometryMap.ContainsKey(docTable));

        var paginator = doc.ViewerPaginator;
        Assert.Equal(2, paginator.PageCount);

        // 2. CSS table with top caption: DecorationBox (GridBounds) excludes caption so box-shadow/outline wrap the grid, and ink padding expands for shadow + outline.
        var host = new StackPanel();
        Css.SetStyle(host, "display:block;width:260px;padding:20px;");
        var cssTable = new StackPanel();
        Css.SetStyle(cssTable, "display:table;width:180px;border-collapse:collapse;box-shadow:0 0 0 8px #38BDF8;outline:4px solid #F43F5E;outline-offset:2px;");
        var caption = new StackPanel { Children = { new Border { Width = 100, Height = 26 } } };
        Css.SetStyle(caption, "display:table-caption;caption-side:top;");
        var row = new StackPanel();
        Css.SetStyle(row, "display:table-row;background:linear-gradient(90deg, #0EA5E9, #6366F1);");
        var spanCell = new StackPanel { Children = { new Border { Width = 60, Height = 30 } } };
        Grid.SetColumnSpan(spanCell, 2);
        Css.SetStyle(spanCell, "display:table-cell;border:4px solid transparent;border-image:linear-gradient(90deg, #F59E0B, #10B981) 4;");
        row.Children.Add(spanCell);
        var row2 = new StackPanel();
        Css.SetStyle(row2, "display:table-row;");
        var c20 = new StackPanel { Children = { new Border { Width = 80, Height = 24 } } };
        var c21 = new StackPanel { Children = { new Border { Width = 80, Height = 24 } } };
        Css.SetStyle(c20, "display:table-cell;border:2px solid #94A3B8;");
        Css.SetStyle(c21, "display:table-cell;border:2px solid #94A3B8;");
        row2.Children.Add(c20);
        row2.Children.Add(c21);
        cssTable.Children.Add(caption);
        cssTable.Children.Add(row);
        cssTable.Children.Add(row2);
        host.Children.Add(cssTable);

        Layout(host, 260, 200);
        var decBox = CssTableLayout.DecorationBox(cssTable);
        Assert.NotNull(decBox);
        Near(26, decBox!.Value.Top);
        Assert.True(cssTable.CssTableInkPadding >= 8);
    }

    [Fact]
    public void OrdinaryCssTableNativeFlowDocumentTableAndRichTextBoxMaintainDistinctAccessibilityAndEditingContracts()
    {
        // 1. Ordinary controls styled with display:table / display:table-cell keep their control AutomationPeer and do not fabricate a native Table structure.
        var cssTable = new StackPanel();
        Css.SetStyle(cssTable, "display:table;width:160px;");
        var cssRow = new StackPanel();
        Css.SetStyle(cssRow, "display:table-row;");
        var cssCell = new Button { Content = "Cell Action" };
        Css.SetStyle(cssCell, "display:table-cell;width:160px;");
        cssRow.Children.Add(cssCell);
        cssTable.Children.Add(cssRow);
        Layout(cssTable, 160, 60);

        var tablePeer = UIElementAutomationPeer.CreatePeerForElement(cssTable);
        var cellPeer = UIElementAutomationPeer.CreatePeerForElement(cssCell);
        Assert.NotNull(cellPeer);
        Assert.Equal(AutomationControlType.Button, cellPeer!.GetAutomationControlType());
        Assert.Null(tablePeer?.GetPattern(PatternInterface.Grid));
        Assert.Null(tablePeer?.GetPattern(PatternInterface.Table));

        // 2. Native FlowDocument Table in RichTextBox supports display and live editing while preserving source tree structure.
        var rtb = new RichTextBox { Width = 320, Height = 180 };
        var table = new Table { CellSpacing = 0 };
        Css.SetStyle(table, "width:280px;height:100px;border-collapse:collapse;");
        var group = new TableRowGroup();
        var row = new TableRow();
        var run0 = new Run("Alpha");
        var para0 = new Paragraph(run0);
        Css.SetStyle(para0, "height:50%;");
        var cell0 = new TableCell(para0) { Padding = new Thickness(4), BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray };
        var run1 = new Run("Beta");
        var para1 = new Paragraph(run1);
        var cell1 = new TableCell(para1) { Padding = new Thickness(4), BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray };
        row.Cells.Add(cell0);
        row.Cells.Add(cell1);
        group.Rows.Add(row);
        table.RowGroups.Add(group);
        rtb.Document = new FlowDocument(table) { PagePadding = new Thickness(4) };

        Layout(rtb, 320, 180);
        var view = FindDescendant<FlowDocumentPresentation>(rtb)!;
        Assert.Equal(2, view.TableCells.Count);
        Near(99, view.TableCells[0].Bounds.Height);

        // Place selection at the end of "Alpha" in cell0 and insert "+Edit" via RichTextBox Selection API.
        rtb.Selection.Select(run0.ContentEnd, run0.ContentEnd);
        rtb.Selection.Text = "+Edit";
        Layout(rtb, 320, 180);

        Assert.Equal("Alpha+Edit\n", new TextRange(cell0.ContentStart, cell0.ContentEnd).Text);
        Assert.Same(cell0, para0.Parent);
        Assert.Same(row, cell0.Parent);
        Assert.Same(group, row.Parent);
        Assert.Same(table, group.Parent);

        var peer = rtb.GetAutomationPeer();
        var textProvider = peer?.GetPattern(PatternInterface.Text) as ITextProvider;
        Assert.NotNull(textProvider);
        Assert.NotNull(textProvider!.DocumentRange.FindText("Alpha+Edit", false, false));

        // 3. Standalone FlowDocumentPresentation exposes its own ITextProvider and cell layout.
        var standaloneTable = new Table { CellSpacing = 0 };
        var standaloneGroup = new TableRowGroup();
        var standaloneRow = new TableRow();
        standaloneRow.Cells.Add(new TableCell(new Paragraph(new Run("NativeCell"))) { Padding = new Thickness(2), BorderThickness = new Thickness(0) });
        standaloneGroup.Rows.Add(standaloneRow);
        standaloneTable.RowGroups.Add(standaloneGroup);
        using var standaloneBody = new FlowDocumentPresentation();
        standaloneBody.SetDocument(new FlowDocument(standaloneTable) { PagePadding = new Thickness(0), PageWidth = 200 });
        Layout(standaloneBody, 200, 80);
        var standaloneProvider = standaloneBody.GetAutomationPeer()?.GetPattern(PatternInterface.Text) as ITextProvider;
        Assert.NotNull(standaloneProvider);
        Assert.NotNull(standaloneProvider!.DocumentRange.FindText("NativeCell", false, false));
    }

    [Fact]
    public async Task CompositeTableConstraintsSceneRendersDeterministicallyAcross1xAnd2xDpi()
    {
        var (sceneRoot, probes) = BuildCompositeScene();
        Layout(sceneRoot, 360, 460);

        // Verify composite scene probes before rendering.
        Near(4, probes["anonInner1"].TransformToAncestor(probes["anonCellA"]).X);
        Near(46, probes["anonInner2"].TransformToAncestor(probes["anonCellA"]).X);
        Near(120, probes["clampSpan"].ActualWidth);
        Near(80, probes["definiteChild"].ActualHeight);
        Near(40, probes["definiteChild"].TransformToAncestor(probes["definiteCell"]).Y);
        var decBox = CssTableLayout.DecorationBox(probes["decoratedTable"]);
        Assert.NotNull(decBox);
        Near(24, decBox!.Value.Top);

        var outputDir = Environment.GetEnvironmentVariable("JALIUM_ACCEPTANCE_SCREENSHOT_DIR")
            ?? "/Volumes/My PSSD/.codex-css-acceptance/css-l2-table-constraints-20261026/after/screenshots";
        Directory.CreateDirectory(outputDir);

        foreach (var (scale, fileName) in new[] { (1.0, "table-constraints-1x.png"), (2.0, "table-constraints-2x.png") })
        {
            var (renderRoot, _) = BuildCompositeScene();
            VisualTreeHelper.SetRootDpi(renderRoot, new DpiScale(scale, scale));
            Layout(renderRoot, 360, 460);
            int widthDip = (int)Math.Ceiling(renderRoot.ActualWidth);
            int heightDip = (int)Math.Ceiling(renderRoot.ActualHeight);
            int pixelWidth = (int)Math.Ceiling(widthDip * scale);
            int pixelHeight = (int)Math.Ceiling(heightDip * scale);

            var recorder = new DrawingRecorder();
            recorder.BindWholeFrame();
            renderRoot.Render(recorder);
            var frame = recorder.Commit();
            Assert.True(frame.IsFullyRecordable);

            var replayedPixels = await Task.Run(() =>
            {
                using var target = _context.CreateOffscreenRenderTarget(pixelWidth, pixelHeight);
                target.RequestReadback();
                target.BeginDraw();
                target.Clear(0, 0, 0, 0);
                using (var dc = new RenderTargetDrawingContext(target, _context))
                {
                    DrawingReplayer.Replay(frame, dc);
                    dc.Close();
                }
                target.EndDraw();
                var buffer = new byte[pixelWidth * pixelHeight * 4];
                Assert.Equal(JaliumResult.Ok, target.FetchReadback(buffer, (uint)(pixelWidth * 4), out _, out _));
                return buffer;
            });

            var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96 * scale, 96 * scale, PixelFormat.Bgra32);
            bitmap.Render(renderRoot);
            int nonEmpty = 0;
            for (int i = 0; i < replayedPixels.Length; i += 4)
            {
                if (replayedPixels[i] != 0 || replayedPixels[i + 1] != 0 || replayedPixels[i + 2] != 0 || replayedPixels[i + 3] != 0)
                    nonEmpty++;
            }
            Assert.True(nonEmpty > (pixelWidth * pixelHeight) / 10, $"Expected non-empty render at {scale}x, got {nonEmpty} pixels.");

            var fullPath = Path.Combine(outputDir, fileName);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(fullPath))
            {
                encoder.Save(stream);
            }
            Assert.True(new FileInfo(fullPath).Length > 1024);
        }
    }

    private static (StackPanel Root, Dictionary<string, FrameworkElement> Probes) BuildCompositeScene()
    {
        var probes = new Dictionary<string, FrameworkElement>();
        var root = new StackPanel();
        Css.SetStyle(root, "display:flow-root;box-sizing:border-box;width:360px;padding:14px;background:#0F172A;");

        // Band 1: Misparented table cells with nested anonymous table and elided whitespace
        var band1 = new StackPanel();
        Css.SetStyle(band1, "display:block;margin-bottom:12px;background:#1E293B;padding:6px;");
        var anonCellA = new StackPanel();
        Css.SetStyle(anonCellA, "display:table-cell;width:110px;background:#334155;padding:4px;");
        var anonInnerRow = new StackPanel();
        Css.SetStyle(anonInnerRow, "display:table-row;");
        var anonInner1 = new Border { Width = 42, Height = 20 };
        var anonInner2 = new Border { Width = 48, Height = 20 };
        Css.SetStyle(anonInner1, "display:table-cell;background:#38BDF8;");
        Css.SetStyle(anonInner2, "display:table-cell;background:#818CF8;");
        anonInnerRow.Children.Add(anonInner1);
        anonInnerRow.Children.Add(anonInner2);
        anonCellA.Children.Add(anonInnerRow);
        var ws = new TextBlock { Text = "   \n  " };
        Css.SetStyle(ws, "display:inline;white-space:normal;");
        var anonCellB = new Border { Width = 90, Height = 28 };
        Css.SetStyle(anonCellB, "display:table-cell;background:#2DD4BF;");
        band1.Children.Add(anonCellA);
        band1.Children.Add(ws);
        band1.Children.Add(anonCellB);
        root.Children.Add(band1);
        probes["anonCellA"] = anonCellA;
        probes["anonInner1"] = anonInner1;
        probes["anonInner2"] = anonInner2;

        // Band 2: Nonlinear clamp() percentage colspan + column max-width bounds
        var band2Table = new StackPanel();
        Css.SetStyle(band2Table, "display:table;border-collapse:separate;border-spacing:0;margin-bottom:12px;background:#1E293B;");
        var b2r0 = new StackPanel();
        Css.SetStyle(b2r0, "display:table-row;");
        var clampSpan = new StackPanel { Children = { new Border { Width = 40, Height = 20 } } };
        Grid.SetColumnSpan(clampSpan, 2);
        Css.SetStyle(clampSpan, "display:table-cell;width:clamp(120px, 50%, 180px);background:#F59E0B;");
        b2r0.Children.Add(clampSpan);
        var b2r1 = new StackPanel();
        Css.SetStyle(b2r1, "display:table-row;");
        var b2c0 = new Border { Width = 30, Height = 18 };
        var b2c1 = new Border { Width = 30, Height = 18 };
        Css.SetStyle(b2c0, "display:table-cell;background:#FBBF24;");
        Css.SetStyle(b2c1, "display:table-cell;background:#FDE047;");
        b2r1.Children.Add(b2c0);
        b2r1.Children.Add(b2c1);
        band2Table.Children.Add(b2r0);
        band2Table.Children.Add(b2r1);
        root.Children.Add(band2Table);
        probes["clampSpan"] = clampSpan;

        // Band 3: Explicit table height with 50% middle-aligned child + rowspan baseline
        var band3Table = new StackPanel();
        Css.SetStyle(band3Table, "display:table;width:300px;height:160px;border-collapse:separate;border-spacing:0;margin-bottom:14px;background:#1E293B;");
        var b3r0 = new StackPanel();
        Css.SetStyle(b3r0, "display:table-row;");
        var definiteCell = new StackPanel();
        Css.SetStyle(definiteCell, "display:table-cell;width:150px;vertical-align:middle;background:#334155;");
        var definiteChild = new Border { Child = new Border { Width = 40, Height = 20 } };
        Css.SetStyle(definiteChild, "display:block;height:50%;background:#34D399;");
        definiteCell.Children.Add(definiteChild);
        var b3Right = new StackPanel { Children = { new Border { Width = 60, Height = 36 } } };
        Css.SetStyle(b3Right, "display:table-cell;width:150px;vertical-align:top;background:#60A5FA;");
        b3r0.Children.Add(definiteCell);
        b3r0.Children.Add(b3Right);
        band3Table.Children.Add(b3r0);
        root.Children.Add(band3Table);
        probes["definiteCell"] = definiteCell;
        probes["definiteChild"] = definiteChild;

        // Band 4: Caption + GridBounds box-shadow/outline + continuous spanned gradient & collapsed border
        var decoratedTable = new StackPanel();
        Css.SetStyle(decoratedTable, "display:table;width:280px;border-collapse:collapse;box-shadow:0 0 0 4px #38BDF8;outline:2px solid #F472B6;outline-offset:2px;background:#1E293B;");
        var cap = new StackPanel { Children = { new Border { Width = 120, Height = 24 } } };
        Css.SetStyle(cap, "display:table-caption;caption-side:top;background:#475569;");
        var dr0 = new StackPanel();
        Css.SetStyle(dr0, "display:table-row;background:linear-gradient(90deg, #0EA5E9, #A855F7);");
        var dcSpan = new StackPanel { Children = { new Border { Width = 80, Height = 26 } } };
        Grid.SetColumnSpan(dcSpan, 2);
        Css.SetStyle(dcSpan, "display:table-cell;border:3px solid #F43F5E;");
        dr0.Children.Add(dcSpan);
        var dr1 = new StackPanel();
        Css.SetStyle(dr1, "display:table-row;");
        var dc0 = new Border { Width = 120, Height = 22 };
        var dc1 = new Border { Width = 120, Height = 22 };
        Css.SetStyle(dc0, "display:table-cell;border:2px solid #94A3B8;background:#334155;");
        Css.SetStyle(dc1, "display:table-cell;border:2px solid #94A3B8;background:#475569;");
        dr1.Children.Add(dc0);
        dr1.Children.Add(dc1);
        decoratedTable.Children.Add(cap);
        decoratedTable.Children.Add(dr0);
        decoratedTable.Children.Add(dr1);
        root.Children.Add(decoratedTable);
        probes["decoratedTable"] = decoratedTable;

        return (root, probes);
    }

    private static T? FindDescendant<T>(Visual root) where T : Visual
    {
        if (root is T match) return match;
        for (int i = 0; i < root.VisualChildrenCount; i++)
        {
            if (root.GetVisualChild(i) is { } child && FindDescendant<T>(child) is { } found)
                return found;
        }
        return null;
    }

    private static void Layout(FrameworkElement root, double width, double height)
    {
        for (var i = 0; i < 4; i++)
        {
            Dispatcher.CurrentDispatcher.ProcessQueue();
            CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
            root.Measure(new Size(width, height));
            var arrangeWidth = double.IsFinite(width) ? Math.Max(width, root.DesiredSize.Width) : root.DesiredSize.Width;
            var arrangeHeight = double.IsFinite(height) ? Math.Max(height, root.DesiredSize.Height) : root.DesiredSize.Height;
            root.Arrange(new Rect(0, 0, arrangeWidth, arrangeHeight));
            root.UpdateLayout();
        }
    }

    private static void Near(double expected, double actual) =>
        Assert.InRange(actual, expected - 0.06, expected + 0.06);
}

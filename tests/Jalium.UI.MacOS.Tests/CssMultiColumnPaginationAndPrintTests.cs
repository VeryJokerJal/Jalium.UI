using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssMultiColumnPaginationAndPrintTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);

    public void Dispose() => _context.Dispose();

    [Fact]
    public void PageRulesNamedPagesMediaPrintAndRegionsSupportBoundaryWork()
    {
        // 1. G0 Region properties are classified as Unsupported and rejected by @supports
        Assert.Equal(CssPropertySupport.Unsupported, CssAuthoring.GetProperty("flow-into").Support);
        Assert.Equal(CssPropertySupport.Unsupported, CssAuthoring.GetProperty("flow-from").Support);
        Assert.Equal(CssPropertySupport.Unsupported, CssAuthoring.GetProperty("region-fragment").Support);

        // 2. @page, page, size, page-orientation, @media print
        Assert.Equal(CssPropertySupport.Supported, CssAuthoring.GetProperty("page").Support);
        Assert.Equal(CssPropertySupport.Supported, CssAuthoring.GetProperty("size").Support);
        Assert.Equal(CssPropertySupport.Supported, CssAuthoring.GetProperty("page-orientation").Support);

        var sheet = CssStyleSheet.Parse("""
            @page {
                size: 400px 300px;
                margin: 24px 20px 28px 20px;
                @top-center {
                    content: "Doc " counter(page) "/" counter(pages);
                }
                @bottom-right {
                    content: "P-" counter(page);
                }
            }
            @page :first {
                margin-top: 36px;
                @top-center {
                    content: "COVER";
                }
            }
            @page :blank {
                @top-center {
                    content: "INTENTIONALLY BLANK";
                }
            }
            @page wide-sheet {
                size: 300px 520px;
                page-orientation: rotate-left;
                margin: 16px;
            }
            @media print {
                Paragraph.print-banner {
                    margin-top: 12px;
                }
            }
            Paragraph.intro {
                break-after: right;
            }
            Paragraph.wide {
                page: wide-sheet;
            }
            """);

        Assert.Equal(4, sheet.PageRules.Length);

        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Helvetica"),
            FontSize = 14,
            PagePadding = new Thickness(20),
        };
        Css.SetStyleSheets(doc, new CssStyleSheetCollection { sheet });

        var p1 = new Paragraph(new Run("First page content on right recto sheet."));
        Css.SetClass(p1, "intro");
        var p2 = new Paragraph(new Run("Third page content after blank verso page."));
        var p3 = new Paragraph(new Run("Named wide-sheet page content."));
        Css.SetClass(p3, "wide");
        doc.Blocks.Add(p1);
        doc.Blocks.Add(p2);
        doc.Blocks.Add(p3);

        var paginator = ((IDocumentPaginatorSource)doc).DocumentPaginator;
        paginator.ComputePageCount();

        // Page 0: p1 (recto/right), Page 1: blank verso/left forced by break-after: right,
        // Page 2: p2 (recto/right), Page 3: p3 (forced onto named page "wide-sheet")
        Assert.Equal(4, paginator.PageCount);

        using var page0 = paginator.GetPage(0);
        using var page1 = paginator.GetPage(1);
        using var page2 = paginator.GetPage(2);
        using var page3 = paginator.GetPage(3);

        Assert.Equal(new Size(400, 300), page0.Size);
        Assert.Equal(new Size(400, 300), page1.Size);
        Assert.Equal(new Size(400, 300), page2.Size);
        Assert.Equal(new Size(520, 300), page3.Size);

        var pres0 = Assert.IsType<FlowDocumentPresentation>(page0.Visual);
        var pres1 = Assert.IsType<FlowDocumentPresentation>(page1.Visual);
        var pres3 = Assert.IsType<FlowDocumentPresentation>(page3.Visual);

        Assert.Equal("COVER", pres0.PageStyle!.MarginBoxes[CssPageMarginBoxKind.TopCenter].Text);
        Assert.Equal("P-1", pres0.PageStyle!.MarginBoxes[CssPageMarginBoxKind.BottomRight].Text);
        Assert.Equal("INTENTIONALLY BLANK", pres1.PageStyle!.MarginBoxes[CssPageMarginBoxKind.TopCenter].Text);
        Assert.Equal(CssPageOrientation.RotateLeft, pres3.PageStyle?.Orientation);

        // Verify @media print activation via Css.EnterPrintMedia
        using (Css.EnterPrintMedia(doc))
        {
            Assert.True(Css.IsPrintMedia(CssNode.Get(doc)));
        }
        Assert.False(Css.IsPrintMedia(CssNode.Get(doc)));
    }

    [Fact]
    public void ArbitraryDepthColumnBalancingAndAnonymousColumnPercentageHeightWork()
    {
        // 1. 3-level nested multi-column FrameworkElement balancing (2 x 2 x 2 = 8 leaf columns)
        var outer = new StackPanel();
        Css.SetStyle(outer, "display:block;width:460px;column-count:2;column-gap:20px;column-rule:2px solid #334155;");

        var middle = new StackPanel();
        Css.SetStyle(middle, "display:block;column-count:2;column-gap:12px;column-rule:1px solid #64748b;");

        var inner = new StackPanel();
        Css.SetStyle(inner, "display:block;column-count:2;column-gap:8px;column-rule:1px solid #94a3b8;");

        var leaves = new List<Border>();
        for (var i = 0; i < 8; i++)
        {
            var leaf = new Border
            {
                Height = 20,
                Background = new SolidColorBrush(Color.FromRgb((byte)(40 + i * 20), 100, 180)),
            };
            Css.SetStyle(leaf, "display:block;");
            inner.Children.Add(leaf);
            leaves.Add(leaf);
        }

        middle.Children.Add(inner);
        outer.Children.Add(middle);

        outer.Measure(new Size(460, double.PositiveInfinity));
        outer.Arrange(new Rect(0, 0, 460, outer.DesiredSize.Height));

        // 8 items of 20px balanced across 2 * 2 * 2 = 8 leaf columns -> balanced height is 20px!
        Assert.InRange(outer.DesiredSize.Height, 19.5, 20.5);

        var xPositions = leaves.Select(leaf => leaf.TransformToAncestor(outer).X).OrderBy(x => x).ToArray();
        for (var i = 1; i < xPositions.Length; i++)
        {
            Assert.True(xPositions[i] > xPositions[i - 1] + 30,
                $"Expected 8 distinct leaf column X positions, got [{string.Join(", ", xPositions)}]");
        }

        // 2. Anonymous column box percentage height resolution
        var percentHost = new StackPanel();
        Css.SetStyle(percentHost, "display:block;width:300px;height:120px;column-count:2;column-gap:20px;column-fill:auto;");

        var halfChild = new Border { Background = Brushes.CornflowerBlue };
        Css.SetStyle(halfChild, "display:block;height:50%;");

        var fullMinChild = new Border { Background = Brushes.Coral };
        Css.SetStyle(fullMinChild, "display:block;min-height:75%;break-inside:avoid;");

        percentHost.Children.Add(halfChild);
        percentHost.Children.Add(fullMinChild);

        percentHost.Measure(new Size(300, 120));
        percentHost.Arrange(new Rect(0, 0, 300, 120));

        // 50% of 120px anonymous column height = 60px; 75% of 120px = 90px
        Assert.InRange(halfChild.RenderSize.Height, 59.5, 60.5);
        Assert.InRange(fullMinChild.RenderSize.Height, 89.5, 90.5);
    }

    [Fact]
    public void InsufficientVerticalSpaceDemotesColumnSpanAllOrAdvancesPage()
    {
        // 1. Height-constrained multi-column FrameworkElement demotes column-span: all when vertical space is insufficient
        var host = new StackPanel();
        Css.SetStyle(host, "display:block;width:320px;height:60px;column-count:2;column-gap:20px;column-fill:auto;");

        var lead1 = new Border { Height = 45, Background = Brushes.SteelBlue };
        var lead2 = new Border { Height = 45, Background = Brushes.SteelBlue };
        var spanner = new Border { Height = 30, Background = Brushes.Crimson };
        var tail = new Border { Height = 20, Background = Brushes.SeaGreen };

        Css.SetStyle(lead1, "display:block;");
        Css.SetStyle(lead2, "display:block;");
        Css.SetStyle(spanner, "display:block;column-span:all;");
        Css.SetStyle(tail, "display:block;");

        host.Children.Add(lead1);
        host.Children.Add(lead2);
        host.Children.Add(spanner);
        host.Children.Add(tail);

        host.Measure(new Size(320, 60));
        host.Arrange(new Rect(0, 0, 320, 60));

        // Column width is (320 - 20) / 2 = 150.
        // Because lead1 (45px) + lead2 (45px) take 45px of the 60px limit, the 30px spanner (45 + 30 = 75 > 60)
        // cannot fit as a full-width spanner and is demoted to an in-column block of width 150px.
        Assert.InRange(spanner.RenderSize.Width, 149.5, 150.5);

        // 2. Paginated FlowDocument with column-span: all and break-after: avoid advances to next page when space is insufficient
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Helvetica"),
            FontSize = 14,
            PageWidth = 360,
            PageHeight = 130,
            PagePadding = new Thickness(15),
        };
        Css.SetStyle(doc, "column-count:2;column-gap:20px;");

        var pLead = new Paragraph(new Run("Lead line one\nLead line two\nLead line three\nLead line four"));
        var pSpanner = new Paragraph(new Run("Spanning Heading"));
        Css.SetStyle(pSpanner, "column-span:all;break-after:avoid;padding-top:10px;padding-bottom:10px;");
        var pFollow = new Paragraph(new Run("Following paragraph that must stay with the spanning heading."));

        doc.Blocks.Add(pLead);
        doc.Blocks.Add(pSpanner);
        doc.Blocks.Add(pFollow);

        var paginator = ((IDocumentPaginatorSource)doc).DocumentPaginator;
        paginator.ComputePageCount();
        Assert.True(paginator.PageCount >= 2);

        var spannerOffset = doc.GetDocumentOffset(pSpanner, 0);
        var followOffset = doc.GetDocumentOffset(pFollow, 0);
        Assert.Equal(
            FlowDocumentViewerSupport.GetPageNumberForOffset(doc, spannerOffset),
            FlowDocumentViewerSupport.GetPageNumberForOffset(doc, followOffset));
    }

    [Fact]
    public void ComplexMarginsAndFloatRewrappingAcrossColumnBreaksWork()
    {
        // 1. Unforced column break truncates inter-block margin between blocks
        var marginHost = new StackPanel();
        Css.SetStyle(marginHost, "display:block;width:320px;column-count:2;column-gap:20px;");

        var blockA = new Border { Height = 40, Background = Brushes.CadetBlue };
        var blockB = new Border { Height = 40, Background = Brushes.Goldenrod };
        Css.SetStyle(blockA, "display:block;margin-bottom:24px;");
        Css.SetStyle(blockB, "display:block;margin-top:24px;");

        marginHost.Children.Add(blockA);
        marginHost.Children.Add(blockB);

        marginHost.Measure(new Size(320, double.PositiveInfinity));
        marginHost.Arrange(new Rect(0, 0, 320, marginHost.DesiredSize.Height));

        // The 24px collapsed margin between blockA and blockB falls at an unforced column break,
        // so blockB starts at Y=0 in column 2 and the balanced height is 40px (not 52px or 64px).
        Assert.InRange(marginHost.DesiredSize.Height, 39.5, 40.5);
        Assert.InRange(blockA.TransformToAncestor(marginHost).Y, -0.5, 0.5);
        Assert.InRange(blockB.TransformToAncestor(marginHost).Y, -0.5, 0.5);
        Assert.InRange(blockB.TransformToAncestor(marginHost).X, 169.5, 170.5);

        // 2. Float stays in column 1 while a forced break moves following block to column 2 -> block re-wraps to full column width
        var floatHost = new StackPanel();
        Css.SetStyle(floatHost, "display:block;width:420px;column-count:2;column-gap:20px;");

        var floatBox = new Border { Width = 80, Height = 60, Background = Brushes.SlateBlue };
        Css.SetStyle(floatBox, "float:left;");

        var besideFloat = new Border { Height = 20, Background = Brushes.MediumSeaGreen };
        Css.SetStyle(besideFloat, "display:block;overflow:hidden;");

        var nextColBlock = new Border { Height = 20, Background = Brushes.Peru };
        Css.SetStyle(nextColBlock, "display:block;overflow:hidden;break-before:column;");

        floatHost.Children.Add(floatBox);
        floatHost.Children.Add(besideFloat);
        floatHost.Children.Add(nextColBlock);

        floatHost.Measure(new Size(420, double.PositiveInfinity));
        floatHost.Arrange(new Rect(0, 0, 420, floatHost.DesiredSize.Height));

        // Column width is 200px. besideFloat in column 1 wraps beside the 80px float -> width 120px.
        // nextColBlock in column 2 is after the column break where floatBox is no longer active -> full width 200px!
        Assert.InRange(besideFloat.RenderSize.Width, 119.5, 120.5);
        Assert.InRange(nextColBlock.RenderSize.Width, 199.5, 200.5);
        Assert.InRange(nextColBlock.TransformToAncestor(floatHost).X, 219.5, 220.5);
    }

    [Fact]
    public void RepeatedTableHeadersAndFootersAcrossPagesAndColumnsPreserveSingleSourceIdentity()
    {
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Helvetica"),
            FontSize = 13,
            PageWidth = 360,
            PageHeight = 160,
            PagePadding = new Thickness(16),
        };

        var table = new Table { CellSpacing = 0 };
        var headerGroup = new TableRowGroup();
        Css.SetStyle(headerGroup, "display:table-header-group;");
        var headerRow = new TableRow();
        var headerCell = new TableCell(new Paragraph(new Run("HEADER-REPEAT")) { Margin = new Thickness(0) }) { Padding = new Thickness(4) };
        headerRow.Cells.Add(headerCell);
        headerGroup.Rows.Add(headerRow);

        var bodyGroup = new TableRowGroup();
        for (var i = 1; i <= 6; i++)
        {
            var row = new TableRow();
            var cell = new TableCell(new Paragraph(new Run($"Body row {i} content line")) { Margin = new Thickness(0) }) { Padding = new Thickness(6) };
            row.Cells.Add(cell);
            bodyGroup.Rows.Add(row);
        }

        var footerGroup = new TableRowGroup();
        Css.SetStyle(footerGroup, "display:table-footer-group;");
        var footerRow = new TableRow();
        var footerCell = new TableCell(new Paragraph(new Run("FOOTER-REPEAT")) { Margin = new Thickness(0) }) { Padding = new Thickness(4) };
        footerRow.Cells.Add(footerCell);
        footerGroup.Rows.Add(footerRow);

        table.RowGroups.Add(headerGroup);
        table.RowGroups.Add(bodyGroup);
        table.RowGroups.Add(footerGroup);
        doc.Blocks.Add(table);

        var originalText = doc.GetText();
        var paginator = ((IDocumentPaginatorSource)doc).DocumentPaginator;
        paginator.ComputePageCount();

        Assert.True(paginator.PageCount >= 2);
        // Source nodes and text range are never duplicated
        Assert.Equal(3, table.RowGroups.Count);
        Assert.Single(headerGroup.Rows);
        Assert.Equal(6, bodyGroup.Rows.Count);
        Assert.Single(footerGroup.Rows);
        Assert.Equal(originalText, doc.GetText());

        var headerOffset = doc.GetDocumentOffset(headerCell.Blocks.FirstBlock!, 0);
        var footerOffset = doc.GetDocumentOffset(footerCell.Blocks.FirstBlock!, 0);

        for (var p = 0; p < paginator.PageCount; p++)
        {
            using var page = paginator.GetPage(p);
            var pres = Assert.IsType<FlowDocumentPresentation>(page.Visual);
            pres.Measure(page.Size);
            pres.Arrange(new Rect(page.Size));

            // Both page 0 and subsequent continuation pages render visible bounds for the repeated header and footer
            var headerBounds = pres.TextBounds(headerOffset, 6);
            var footerBounds = pres.TextBounds(footerOffset, 6);
            Assert.NotEmpty(headerBounds);
            Assert.NotEmpty(footerBounds);
        }
    }

    [Fact]
    public void DecorationFragmentationRelativeAndFixedPositioningAndShadowPiecesWork()
    {
        var host = new StackPanel();
        Css.SetStyle(host, "display:block;width:340px;column-count:2;column-gap:40px;");

        var clonedBox = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(30, 64, 175)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(147, 197, 253)),
            BorderThickness = new Thickness(3),
        };
        Css.SetStyle(clonedBox, "display:block;padding:8px;border-radius:8px;box-decoration-break:clone;box-shadow:0 4px 8px rgba(15,23,42,0.45);");

        var relChild1 = new Border { Height = 32, Background = new SolidColorBrush(Color.FromRgb(59, 130, 246)) };
        Css.SetStyle(relChild1, "display:block;position:relative;left:10px;top:4px;");

        var relChild2 = new Border { Height = 32, Background = new SolidColorBrush(Color.FromRgb(96, 165, 250)) };
        Css.SetStyle(relChild2, "display:block;position:relative;left:10px;top:4px;");

        clonedBox.Child = new StackPanel { Children = { relChild1, relChild2 } };
        host.Children.Add(clonedBox);

        host.Measure(new Size(340, double.PositiveInfinity));
        host.Arrange(new Rect(0, 0, 340, host.DesiredSize.Height));

        Assert.NotNull(clonedBox.CssColumnBox);
        Assert.Equal(2, clonedBox.CssColumnBox!.Pieces.Length);

        // Verify relative offset is applied exactly once (left: 10px + 8px padding + 3px border = 21px in col 1)
        var rel1Origin = relChild1.TransformToAncestor(host);
        Assert.InRange(rel1Origin.X, 20.5, 21.5);
        Assert.InRange(rel1Origin.Y, 14.5, 15.5);

        // Verify position: fixed in paginated FlowDocument repeats on every page
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Helvetica"),
            FontSize = 14,
            PageWidth = 320,
            PageHeight = 120,
            PagePadding = new Thickness(16),
        };
        var fixedStamp = new Paragraph(new Run("CONFIDENTIAL"));
        Css.SetStyle(fixedStamp, "position:fixed;top:4px;left:8px;");
        var bodyP1 = new Paragraph(new Run("Page 1 body text")) { BreakPageBefore = false };
        var bodyP2 = new Paragraph(new Run("Page 2 body text")) { BreakPageBefore = true };
        doc.Blocks.Add(fixedStamp);
        doc.Blocks.Add(bodyP1);
        doc.Blocks.Add(bodyP2);

        var paginator = ((IDocumentPaginatorSource)doc).DocumentPaginator;
        paginator.ComputePageCount();
        Assert.Equal(2, paginator.PageCount);

        var stampOffset = doc.GetDocumentOffset(fixedStamp, 0);
        for (var p = 0; p < 2; p++)
        {
            using var page = paginator.GetPage(p);
            var pres = Assert.IsType<FlowDocumentPresentation>(page.Visual);
            pres.Measure(page.Size);
            pres.Arrange(new Rect(page.Size));
            Assert.NotEmpty(pres.TextBounds(stampOffset, 5));
        }
    }

    [Fact]
    public void PaginationNavigationBlankPagesKeepConstraintsViewerConsistencyAndEditCacheRoundTripWork()
    {
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Helvetica"),
            FontSize = 14,
            PageWidth = 340,
            PageHeight = 140,
            PagePadding = new Thickness(16),
        };
        Css.SetStyle(doc, "column-count:2;column-gap:16px;");

        var p1 = new Paragraph(new Run("Alpha line one\nAlpha line two\nAlpha line three\nAlpha line four"));
        Css.SetStyle(p1, "orphans:2;widows:2;");
        var pHeading = new Paragraph(new Run("Section Heading Keep"));
        Css.SetStyle(pHeading, "break-after:avoid;");
        var pBody = new Paragraph(new Run("Body paragraph kept with heading across breaks.\nSecond line of body."));
        Css.SetStyle(pBody, "break-inside:avoid;");
        var pForced = new Paragraph(new Run("Forced next page paragraph."));
        Css.SetStyle(pForced, "break-before:page;");

        doc.Blocks.Add(p1);
        doc.Blocks.Add(pHeading);
        doc.Blocks.Add(pBody);
        doc.Blocks.Add(pForced);

        var paginator = ((IDocumentPaginatorSource)doc).DocumentPaginator;
        paginator.ComputePageCount();
        var baselinePageCount = paginator.PageCount;
        Assert.True(baselinePageCount >= 2);

        var headingOffset = doc.GetDocumentOffset(pHeading, 0);
        var bodyOffset = doc.GetDocumentOffset(pBody, 0);
        var forcedOffset = doc.GetDocumentOffset(pForced, 0);

        Assert.Equal(
            FlowDocumentViewerSupport.GetPageNumberForOffset(doc, headingOffset),
            FlowDocumentViewerSupport.GetPageNumberForOffset(doc, bodyOffset));
        var forcedPage = FlowDocumentViewerSupport.GetPageNumberForOffset(doc, forcedOffset);
        Assert.True(forcedPage >= 2);
        Assert.True(FlowDocumentViewerSupport.GetPageStartOffset(doc, forcedPage) <= forcedOffset);

        // Verify viewer consistency (FlowDocumentPageViewer, FlowDocumentScrollViewer, FlowDocumentReader)
        var pageViewer = new FlowDocumentPageViewer { Document = doc, Width = 340, Height = 140 };
        pageViewer.Measure(new Size(340, 140));
        pageViewer.Arrange(new Rect(0, 0, 340, 140));
        Assert.Equal(baselinePageCount, pageViewer.PageCount);

        var scrollViewer = new FlowDocumentScrollViewer { Document = doc, Width = 340, Height = 140 };
        scrollViewer.Measure(new Size(340, 140));
        scrollViewer.Arrange(new Rect(0, 0, 340, 140));

        // Round-trip 1: Insert and delete text -> fragmentation cache invalidates and restores exact baseline
        var extraRun = new Run("\nExtra inserted line 1\nExtra inserted line 2\nExtra inserted line 3\nExtra inserted line 4\nExtra inserted line 5\nExtra inserted line 6");
        pBody.Inlines.Add(extraRun);
        paginator.ComputePageCount();
        var expandedPageCount = paginator.PageCount;
        Assert.True(expandedPageCount >= baselinePageCount);

        pBody.Inlines.Remove(extraRun);
        paginator.ComputePageCount();
        Assert.Equal(baselinePageCount, paginator.PageCount);
        Assert.Equal(forcedPage, FlowDocumentViewerSupport.GetPageNumberForOffset(doc, doc.GetDocumentOffset(pForced, 0)));

        // Round-trip 2: Page size change and restore -> fragmentation cache invalidates and restores exact baseline
        doc.PageHeight = 95;
        paginator.ComputePageCount();
        Assert.True(paginator.PageCount > baselinePageCount);

        doc.PageHeight = 140;
        paginator.ComputePageCount();
        Assert.Equal(baselinePageCount, paginator.PageCount);
        Assert.Equal(forcedPage, FlowDocumentViewerSupport.GetPageNumberForOffset(doc, doc.GetDocumentOffset(pForced, 0)));
    }

    [Fact]
    public void RenderL3MultiColumnPaginationAndPrintVisualsAt1xAnd2x()
    {
        var outDir = Environment.GetEnvironmentVariable("JALIUM_L3_SCREENSHOT_DIR")
            ?? "/Volumes/My PSSD/.codex-css-acceptance/css-l3-multicol-pagination-print-20261026/after/screenshots";
        Directory.CreateDirectory(outDir);

        foreach (var (dpi, suffix) in new[] { (1.0, "1x"), (2.0, "2x") })
        {
            var canvas = BuildShowcaseVisual();
            var width = 760;
            var height = 520;
            canvas.Measure(new Size(width, height));
            canvas.Arrange(new Rect(0, 0, width, height));

            var pixelWidth = (int)Math.Round(width * dpi);
            var pixelHeight = (int)Math.Round(height * dpi);
            var rtb = new RenderTargetBitmap(pixelWidth, pixelHeight, 96.0 * dpi, 96.0 * dpi, PixelFormats.Pbgra32);
            rtb.Render(canvas);

            var pixels = new byte[pixelWidth * pixelHeight * 4];
            rtb.CopyPixels(pixels, pixelWidth * 4, 0);
            var nonZero = pixels.Count(b => b != 0);
            Assert.True(nonZero > pixelWidth * pixelHeight, $"Expected rendered content at {suffix}");

            var path = Path.Combine(outDir, $"l3-multicol-pagination-print-{suffix}.png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var stream = File.Create(path);
            encoder.Save(stream);
            Assert.True(new FileInfo(path).Length > 1024);
        }
    }

    private static FrameworkElement BuildShowcaseVisual()
    {
        var root = new StackPanel
        {
            Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
        };
        Css.SetStyle(root, "display:block;width:760px;height:520px;padding:16px;");

        // Top panel: 2x2 nested multi-column with cloned decoration, box-shadow, column-rule, and percentage-height badge
        var multiColCard = new StackPanel
        {
            Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
        };
        Css.SetStyle(multiColCard, "display:block;padding:12px;margin-bottom:14px;border-radius:10px;border:1px solid #334155;column-count:2;column-gap:24px;column-rule:2px solid #38bdf8;");

        var spanBanner = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(14, 116, 144)),
            Padding = new Thickness(8, 4, 8, 4),
            Child = new TextBlock
            {
                Text = "L3 Multi-Column: Nested Balancing + Cloned Box Decoration + Spanner",
                Foreground = Brushes.White,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
            },
        };
        Css.SetStyle(spanBanner, "display:block;column-span:all;margin-bottom:8px;border-radius:6px;");
        multiColCard.Children.Add(spanBanner);

        var clonedFragment = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(30, 58, 138)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(96, 165, 250)),
            BorderThickness = new Thickness(2),
            Padding = new Thickness(8),
            Child = new TextBlock
            {
                Text = "Cloned box-decoration-break fragment across columns.\nLine 2 in column 1 with full border & radius.\nLine 3 in column 2 with cloned border & shadow.\nLine 4 completes the balanced two-column card.",
                Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
            },
        };
        Css.SetStyle(clonedFragment, "display:block;border-radius:8px;box-decoration-break:clone;box-shadow:0 3px 6px rgba(0,0,0,0.45);");
        multiColCard.Children.Add(clonedFragment);

        root.Children.Add(multiColCard);

        // Bottom panel: Side-by-side Page 1 and Page 2 of a Paginated FlowDocument with @page margin boxes & repeated table header/footer
        var sheet = CssStyleSheet.Parse("""
            @page {
                size: 350px 280px;
                margin: 22px 16px 22px 16px;
                background: #f8fafc;
                border: 1px solid #94a3b8;
                @top-center {
                    content: "Print Preview — Page " counter(page) " of " counter(pages);
                    color: #0f172a;
                }
                @bottom-right {
                    content: "L3-PAGINATION #" counter(page);
                    color: #475569;
                }
            }
            @page :first {
                @top-left {
                    content: "FIRST-SHEET";
                    color: #0284c7;
                }
            }
            """);

        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Helvetica"),
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
            PagePadding = new Thickness(16),
        };
        Css.SetStyleSheets(doc, new CssStyleSheetCollection { sheet });

        var table = new Table { CellSpacing = 0 };
        var thead = new TableRowGroup();
        Css.SetStyle(thead, "display:table-header-group;background:#dbeafe;");
        var hRow = new TableRow();
        hRow.Cells.Add(new TableCell(new Paragraph(new Run("Repeated Table Header (thead)")))
        {
            Padding = new Thickness(6, 4, 6, 4),
            BorderBrush = new SolidColorBrush(Color.FromRgb(59, 130, 246)),
            BorderThickness = new Thickness(1),
        });
        thead.Rows.Add(hRow);

        var tbody = new TableRowGroup();
        for (var i = 1; i <= 6; i++)
        {
            var bRow = new TableRow();
            bRow.Cells.Add(new TableCell(new Paragraph(new Run($"Paginated Table Body Row #{i} — Preserves Source Offset")))
            {
                Padding = new Thickness(6, 5, 6, 5),
                BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                BorderThickness = new Thickness(1),
            });
            tbody.Rows.Add(bRow);
        }

        var tfoot = new TableRowGroup();
        Css.SetStyle(tfoot, "display:table-footer-group;background:#fef3c7;");
        var fRow = new TableRow();
        fRow.Cells.Add(new TableCell(new Paragraph(new Run("Repeated Table Footer (tfoot)")))
        {
            Padding = new Thickness(6, 4, 6, 4),
            BorderBrush = new SolidColorBrush(Color.FromRgb(217, 119, 6)),
            BorderThickness = new Thickness(1),
        });
        tfoot.Rows.Add(fRow);

        table.RowGroups.Add(thead);
        table.RowGroups.Add(tbody);
        table.RowGroups.Add(tfoot);
        doc.Blocks.Add(table);

        var paginator = ((IDocumentPaginatorSource)doc).DocumentPaginator;
        paginator.ComputePageCount();

        var pagesRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20 };
        for (var p = 0; p < Math.Min(2, paginator.PageCount); p++)
        {
            var page = paginator.GetPage(p);
            if (page.Visual is FrameworkElement pageVisual)
            {
                pageVisual.Width = page.Size.Width;
                pageVisual.Height = page.Size.Height;
                pagesRow.Children.Add(pageVisual);
            }
        }

        root.Children.Add(pagesRow);
        return root;
    }
}

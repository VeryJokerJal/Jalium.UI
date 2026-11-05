using System.IO;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;
using Jalium.UI.Threading;
using Xunit;
using DocList = Jalium.UI.Documents.List;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssSelectionHighlightAndEditingTests : IDisposable
{
    private readonly RenderContext _context;

    public CssSelectionHighlightAndEditingTests()
    {
        _context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        CssHighlightRegistry.Clear();
    }

    public void Dispose()
    {
        CssHighlightRegistry.Clear();
    }

    [Fact]
    public void SelectorParsing_BuiltInAndCustomHighlightPseudos_SpecificityAndValidation()
    {
        var selection = Assert.Single(CssSelectorParser.ParseGroup(".editor::selection", out var err1)!);
        Assert.Null(err1);
        Assert.Equal(CssPseudoElement.Selection, selection.DeclaredPseudoElement);

        var search = Assert.Single(CssSelectorParser.ParseGroup(".editor::search-text", out var err2)!);
        Assert.Null(err2);
        Assert.Equal(CssPseudoElement.SearchText, search.DeclaredPseudoElement);
        Assert.False(search.SearchCurrent);

        var searchCurrent = Assert.Single(CssSelectorParser.ParseGroup(".editor::search-text:current", out var err3)!);
        Assert.Null(err3);
        Assert.Equal(CssPseudoElement.SearchText, searchCurrent.DeclaredPseudoElement);
        Assert.True(searchCurrent.SearchCurrent);
        Assert.True(searchCurrent.Specificity > search.Specificity);

        var targetText = Assert.Single(CssSelectorParser.ParseGroup("Paragraph::target-text", out var err4)!);
        Assert.Null(err4);
        Assert.Equal(CssPseudoElement.TargetText, targetText.DeclaredPseudoElement);

        var spelling = Assert.Single(CssSelectorParser.ParseGroup("Run::spelling-error", out var err5)!);
        Assert.Null(err5);
        Assert.Equal(CssPseudoElement.SpellingError, spelling.DeclaredPseudoElement);

        var grammar = Assert.Single(CssSelectorParser.ParseGroup("Run::grammar-error", out var err6)!);
        Assert.Null(err6);
        Assert.Equal(CssPseudoElement.GrammarError, grammar.DeclaredPseudoElement);

        var custom = Assert.Single(CssSelectorParser.ParseGroup(".doc::highlight(citation-anchor)", out var err7)!);
        Assert.Null(err7);
        Assert.Equal(CssPseudoElement.Highlight, custom.DeclaredPseudoElement);
        Assert.Equal("citation-anchor", custom.HighlightName);

        // Invalid highlight pseudo-element syntax must be rejected:
        Assert.Null(CssSelectorParser.ParseGroup(".doc::highlight()", out _));
        Assert.Null(CssSelectorParser.ParseGroup(".doc::highlight(one two)", out _));
        Assert.Null(CssSelectorParser.ParseGroup(".doc::highlight(inherit)", out _));
        Assert.Null(CssSelectorParser.ParseGroup(".doc::highlight(default)", out _));
        Assert.Null(CssSelectorParser.ParseGroup(".doc::highlight", out _));
        Assert.Null(CssSelectorParser.ParseGroup(".doc::search-text:hover", out _));
        Assert.Null(CssSelectorParser.ParseGroup(".doc::selection:hover", out _));
        Assert.Null(CssSelectorParser.ParseGroup(".doc::highlight(note) Run", out _));
    }

    [Fact]
    public void HighlightCascade_IndependentInheritance_AllowedPropertiesAndCurrentColor()
    {
        var doc = new FlowDocument
        {
            FontSize = 15,
            Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
        };
        Css.SetClass(doc, "doc-root");

        var para = new Paragraph();
        Css.SetClass(para, "para-host");
        var run = new Run("Telemetry packet checksum verified");
        Css.SetClass(run, "token-run");
        para.Inlines.Add(run);
        doc.Blocks.Add(para);

        AddDocumentStyleSheet(doc, """
            .doc-root {
                color: #38bdf8;
            }
            .doc-root::highlight(review-tag) {
                --hl-bg: #1e293b;
                background-color: var(--hl-bg);
                color: #f8fafc;
                text-decoration-line: underline;
                text-decoration-color: currentColor;
                text-emphasis-color: color-mix(in srgb, currentColor 50%, #f59e0b);
                fill: #0ea5e9;
                stroke: currentColor;
                stroke-width: 2.5px;
                font-size: 42px;
            }
            .para-host::highlight(other-tag) {
                background-color: #ef4444;
                color: #111827;
            }
            .para-host::search-text {
                background-color: #fde047;
                color: #1e293b;
            }
            .para-host::search-text:current {
                background-color: #f97316;
                color: #ffffff;
            }
            .para-host::target-text {
                background-color: #a855f7;
                color: #faf5ff;
            }
            .token-run::selection {
                background-color: color-mix(in srgb, currentColor 50%, #0f172a);
            }
            """);

        var viewer = new FlowDocumentScrollViewer { Document = doc };
        Layout(viewer, 420, 180);

        // 1. Custom highlight 'review-tag' inherits from .doc-root::highlight(review-tag) into run:
        var reviewStyle = CssSelection.For(run, CssPseudoElement.Highlight, "review-tag");
        Assert.NotNull(reviewStyle);
        var reviewFg = Assert.IsType<SolidColorBrush>(CssSelection.Foreground(reviewStyle!, doc.Foreground));
        var reviewBg = Assert.IsType<SolidColorBrush>(CssSelection.Background(reviewStyle!, doc.Foreground));
        var reviewDeco = Assert.IsType<SolidColorBrush>(CssSelection.DecorationColor(reviewStyle!, doc.Foreground));
        var reviewEmphasis = Assert.IsType<SolidColorBrush>(CssSelection.EmphasisColor(reviewStyle!, doc.Foreground));
        var reviewFill = Assert.IsType<SolidColorBrush>(CssSelection.SvgFill(reviewStyle!, doc.Foreground));
        var reviewStroke = Assert.IsType<SolidColorBrush>(CssSelection.SvgStroke(reviewStyle!, doc.Foreground));

        Assert.Equal(Color.FromRgb(0xf8, 0xfa, 0xfc), reviewFg.Color);
        Assert.Equal(Color.FromRgb(0x1e, 0x29, 0x3b), reviewBg.Color);
        Assert.Equal(Color.FromRgb(0xf8, 0xfa, 0xfc), reviewDeco.Color);
        Assert.NotEqual(Color.FromRgb(0xf8, 0xfa, 0xfc), reviewEmphasis.Color);
        Assert.Equal(Color.FromRgb(0x0e, 0xa5, 0xe9), reviewFill.Color);
        Assert.Equal(Color.FromRgb(0xf8, 0xfa, 0xfc), reviewStroke.Color);
        Assert.Equal(2.5, CssSelection.SvgStrokeWidth(reviewStyle!));

        // Disallowed font-size on ::highlight must not alter originating element or projection font-size:
        Assert.Equal(15.0, run.FontSize);
        Assert.Equal(15.0, reviewStyle!.Values.FontSize);

        // 2. Independent highlight tree: 'other-tag' does not leak into 'review-tag':
        var otherStyle = CssSelection.For(run, CssPseudoElement.Highlight, "other-tag");
        Assert.NotNull(otherStyle);
        var otherBg = Assert.IsType<SolidColorBrush>(CssSelection.Background(otherStyle!, doc.Foreground));
        Assert.Equal(Color.FromRgb(0xef, 0x44, 0x44), otherBg.Color);

        // 3. ::search-text vs ::search-text:current specificity & cascade:
        var searchRegular = CssSelection.For(run, CssPseudoElement.SearchText, searchCurrent: false);
        var searchActive = CssSelection.For(run, CssPseudoElement.SearchText, searchCurrent: true);
        Assert.NotNull(searchRegular);
        Assert.NotNull(searchActive);
        Assert.Equal(
            Color.FromRgb(0xfd, 0xe0, 0x47),
            Assert.IsType<SolidColorBrush>(CssSelection.Background(searchRegular!, doc.Foreground)).Color);
        Assert.Equal(
            Color.FromRgb(0xf9, 0x73, 0x16),
            Assert.IsType<SolidColorBrush>(CssSelection.Background(searchActive!, doc.Foreground)).Color);

        // 4. ::target-text:
        var targetStyle = CssSelection.For(run, CssPseudoElement.TargetText);
        Assert.NotNull(targetStyle);
        Assert.Equal(
            Color.FromRgb(0xa8, 0x55, 0xf7),
            Assert.IsType<SolidColorBrush>(CssSelection.Background(targetStyle!, doc.Foreground)).Color);
    }

    [Fact]
    public void HighlightRegistry_PriorityAndRegistrationOrder_LayeredPaintingAndErrorDecorations()
    {
        var host = new TextBlock
        {
            Text = "Latency spike detected in shard replica",
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
        };
        Css.SetClass(host, "hl-host");
        AddStyleSheet(host, """
            .hl-host::highlight(alpha) {
                background-color: #0284c7;
                color: #e0f2fe;
            }
            .hl-host::highlight(beta) {
                background-color: #10b981;
                color: #ecfdf5;
            }
            .hl-host::highlight(gamma) {
                background-color: #f59e0b;
                color: #fffbeb;
            }
            .hl-host::spelling-error {
                text-decoration-color: #fb7185;
            }
            .hl-host::grammar-error {
                text-decoration-line: none;
            }
            """);
        Layout(host, 360, 48);

        var hlAlpha = new CssHighlight(priority: 5, CssHighlightType.Highlight, new CssHighlightRange(host, 0, 12));
        var hlBeta = new CssHighlight(priority: 5, CssHighlightType.Highlight, new CssHighlightRange(host, 4, 16));
        var hlGamma = new CssHighlight(priority: 1, CssHighlightType.Highlight, new CssHighlightRange(host, 0, 20));

        CssHighlightRegistry.Set("alpha", hlAlpha);
        CssHighlightRegistry.Set("beta", hlBeta);
        CssHighlightRegistry.Set("gamma", hlGamma);

        // Ordered from bottom-most to top-most: gamma (priority 1) -> alpha (priority 5, registered first) -> beta (priority 5, registered second)
        var ordered = CssHighlightRegistry.GetOrderedEntries().Select(e => e.Name).ToArray();
        Assert.Equal(new[] { "gamma", "alpha", "beta" }, ordered);

        // Updating alpha's priority to 10 moves it above beta:
        hlAlpha.Priority = 10;
        ordered = CssHighlightRegistry.GetOrderedEntries().Select(e => e.Name).ToArray();
        Assert.Equal(new[] { "gamma", "beta", "alpha" }, ordered);

        CssHighlightRegistry.SetTargetRanges(new CssHighlightRange(host, 8, 13));
        CssHighlightRegistry.SetSearchRanges(
            new CssHighlightRange(host, 14, 22, IsCurrent: false),
            new CssHighlightRange(host, 26, 31, IsCurrent: true));
        CssHighlightRegistry.SetSpellingErrorRanges(new CssHighlightRange(host, 0, 7));
        CssHighlightRegistry.SetGrammarErrorRanges(new CssHighlightRange(host, 32, 39));

        var regions = CssSelectionPainter.CollectHighlightRegions(
            host,
            host.Text.Length,
            (start, len) => [new Rect(start * 7.0, 4, len * 7.0, 20)],
            selectionRange: (0, 6),
            underlying: host.Foreground,
            baseline: 18);

        // Verify strict layer ordering: Custom Highlights -> TargetText -> SearchText ->SearchText:current -> SpellingError -> GrammarError -> Selection
        var kindsInOrder = regions.OrderBy(r => r.LayerOrder).Select(r => (r.Kind, r.HighlightName)).ToArray();
        Assert.Equal(
            new (CssPseudoElement, string?)[]
            {
                (CssPseudoElement.Highlight, "gamma"),
                (CssPseudoElement.Highlight, "beta"),
                (CssPseudoElement.Highlight, "alpha"),
                (CssPseudoElement.TargetText, null),
                (CssPseudoElement.SearchText, null),
                (CssPseudoElement.SearchText, "current"),
                (CssPseudoElement.SpellingError, null),
                (CssPseudoElement.GrammarError, null),
                (CssPseudoElement.Selection, null),
            },
            kindsInOrder);

        // Verify SpellingError draws wavy geometry while GrammarError (with text-decoration-line: none) suppresses it:
        var spellingRegion = regions.Single(r => r.Kind == CssPseudoElement.SpellingError);
        var grammarRegion = regions.Single(r => r.Kind == CssPseudoElement.GrammarError);

        var spellingCapture = new GeometryCountingContext();
        CssSelectionPainter.Decorations(spellingCapture, spellingRegion, host);
        Assert.True(spellingCapture.GeometryCount > 0);

        var grammarCapture = new GeometryCountingContext();
        CssSelectionPainter.Decorations(grammarCapture, grammarRegion, host);
        Assert.Equal(0, grammarCapture.GeometryCount);
    }

    [Fact]
    public void WritingToolsEditing_HiddenSubtree_InlineUIContainer_AndTableCellNavigation()
    {
        // 1. Multi-paragraph replacement across a hidden paragraph preserves the hidden paragraph's slot index:
        var pFirst = new Paragraph(new Run("first visible"));
        var pHidden = new Paragraph(new Run("confidential-token"));
        var pSecond = new Paragraph(new Run("second visible"));
        Css.SetStyle(pHidden, "display: none;");

        var section = new Section(pFirst);
        section.Blocks.Add(pHidden);
        section.Blocks.Add(pSecond);
        var doc = new FlowDocument(section);
        var editor = CreateEditor(doc);
        Layout(editor, 400, 240);
        editor.IsUndoEnabled = false;
        editor.IsUndoEnabled = true;

        string exposedBefore = CssWritingToolsEditingTests.Text(editor);
        Assert.Equal("first visible\nsecond visible\n", exposedBefore);

        var result = CssWritingToolsEditingTests.Input(
            editor,
            0,
            exposedBefore.Length,
            "alpha line\nbeta line\n");
        Assert.True(result.Handled);
        Layout(editor, 400, 240);

        Assert.Equal(3, section.Blocks.Count);
        Assert.Same(pHidden, section.Blocks[1]);
        Assert.Equal("confidential-token", Assert.IsType<Run>(pHidden.Inlines[0]).Text);
        Assert.Equal("alpha line\nbeta line\n", CssWritingToolsEditingTests.Text(editor));

        Assert.True(editor.Undo());
        Layout(editor, 400, 240);
        Assert.Equal(new Block[] { pFirst, pHidden, pSecond }, section.Blocks);
        Assert.Equal(exposedBefore, CssWritingToolsEditingTests.Text(editor));

        Assert.True(editor.Redo());
        Layout(editor, 400, 240);
        Assert.Same(pHidden, section.Blocks[1]);
        Assert.Equal("alpha line\nbeta line\n", CssWritingToolsEditingTests.Text(editor));

        // 2. Editing a paragraph containing an InlineUIContainer preserves the embedded UI element:
        var chip = new Border { Width = 36, Height = 18, Background = Brushes.DodgerBlue };
        var inlineContainer = new InlineUIContainer(chip);
        var hiddenInline = new Run("secret-inline");
        Css.SetStyle(hiddenInline, "display: none;");
        var leadRun = new Run("Status ");
        var tailRun = new Run(" ready");
        var hostPara = new Paragraph(leadRun);
        hostPara.Inlines.Add(hiddenInline);
        hostPara.Inlines.Add(inlineContainer);
        hostPara.Inlines.Add(tailRun);

        var docWithUi = new FlowDocument(hostPara);
        var uiEditor = CreateEditor(docWithUi);
        Layout(uiEditor, 400, 140);
        uiEditor.IsUndoEnabled = false;
        uiEditor.IsUndoEnabled = true;

        Assert.Equal("Status \uFFFC ready\n", CssWritingToolsEditingTests.Text(uiEditor));
        var uiEdit = CssWritingToolsEditingTests.Input(uiEditor, 8, 6, " active");
        Assert.True(uiEdit.Handled);
        Layout(uiEditor, 400, 140);

        Assert.Equal("Status \uFFFC active\n", CssWritingToolsEditingTests.Text(uiEditor));
        Assert.Contains(inlineContainer, hostPara.Inlines);
        Assert.Same(chip, inlineContainer.Child);
        Assert.Contains(hiddenInline, hostPara.Inlines);

        // 3. Table cell Tab / Shift+Tab navigation and clipboard export skip display:none and visibility:collapse:
        var table = new Table();
        var col0 = new TableColumn();
        var col1Collapsed = new TableColumn();
        Css.SetStyle(col1Collapsed, "visibility: collapse;");
        var col2 = new TableColumn();
        table.Columns.Add(col0);
        table.Columns.Add(col1Collapsed);
        table.Columns.Add(col2);

        var cellR0C0 = new TableCell(new Paragraph(new Run("Node-A")));
        var cellR0C1CollapsedCol = new TableCell(new Paragraph(new Run("Skip-Col")));
        var cellR0C2 = new TableCell(new Paragraph(new Run("99.98%")));
        var row0 = new TableRow();
        row0.Cells.Add(cellR0C0);
        row0.Cells.Add(cellR0C1CollapsedCol);
        row0.Cells.Add(cellR0C2);

        var row1Hidden = new TableRow();
        Css.SetStyle(row1Hidden, "display: none;");
        row1Hidden.Cells.Add(new TableCell(new Paragraph(new Run("Hidden-Row-A"))));
        row1Hidden.Cells.Add(new TableCell(new Paragraph(new Run("Hidden-Row-B"))));
        row1Hidden.Cells.Add(new TableCell(new Paragraph(new Run("Hidden-Row-C"))));

        var cellR2C0Hidden = new TableCell(new Paragraph(new Run("Hidden-Cell")));
        Css.SetStyle(cellR2C0Hidden, "display: none;");
        var cellR2C1CollapsedCol = new TableCell(new Paragraph(new Run("Skip-Col-2")));
        var cellR2C2 = new TableCell(new Paragraph(new Run("99.95%")));
        var row2 = new TableRow();
        row2.Cells.Add(cellR2C0Hidden);
        row2.Cells.Add(cellR2C1CollapsedCol);
        row2.Cells.Add(cellR2C2);

        var group = new TableRowGroup();
        group.Rows.Add(row0);
        group.Rows.Add(row1Hidden);
        group.Rows.Add(row2);
        table.RowGroups.Add(group);

        var tableDoc = new FlowDocument(table);
        var tableEditor = CreateEditor(tableDoc);
        Layout(tableEditor, 480, 220);

        var navigable = tableEditor.GetNavigableTableCells(table);
        Assert.Equal(new[] { cellR0C0, cellR0C2, cellR2C2 }, navigable);

        var step1 = tableEditor.NavigateTableCell(cellR0C0, forward: true);
        Assert.Same(cellR0C2, step1);
        var step2 = tableEditor.NavigateTableCell(step1!, forward: true);
        Assert.Same(cellR2C2, step2);
        var stepBack = tableEditor.NavigateTableCell(step2!, forward: false);
        Assert.Same(cellR0C2, stepBack);

        string exportedTable = tableEditor.ExportVisibleTableText(table);
        Assert.Equal("Node-A\t99.98%\n99.95%", exportedTable);
    }

    [Fact]
    public void RenderD2ShowcaseScreenshots_NativeAndWithoutBorder_1xAnd2x()
    {
        var outputDirs = GetScreenshotDirectories();
        foreach (var dir in outputDirs)
            Directory.CreateDirectory(dir);

        foreach (var (windowStyle, modeName) in new[]
        {
            (WindowStyle.SingleBorderWindow, "native"),
            (WindowStyle.None, "withoutborder")
        })
        {
            foreach (var (dpi, suffix) in new[] { (1.0, "1x"), (2.0, "2x") })
            {
                CssHighlightRegistry.Clear();
                var visual = BuildD2ShowcaseVisual(windowStyle);
                VisualTreeHelper.SetRootDpi(visual, new DpiScale(dpi, dpi));
                const int width = 1020;
                const int height = 620;
                Layout(visual, width, height);

                int pixelWidth = (int)Math.Round(width * dpi);
                int pixelHeight = (int)Math.Round(height * dpi);
                using var target = _context.CreateOffscreenRenderTarget(pixelWidth, pixelHeight);
                target.SetDpi((float)(96.0 * dpi), (float)(96.0 * dpi));
                target.RequestReadback();
                target.BeginDraw();
                target.Clear(0, 0, 0, 0);
                using (var dc = new RenderTargetDrawingContext(target, _context))
                {
                    visual.Render(dc);
                    dc.Close();
                }
                target.EndDraw();

                var pixels = new byte[pixelWidth * pixelHeight * 4];
                Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, (uint)(pixelWidth * 4), out _, out _));
                int nonZero = pixels.Count(b => b != 0);
                Assert.True(nonZero > pixelWidth * pixelHeight, $"Expected rendered pixels for {modeName}-{suffix}");

                var bmp = BitmapSource.Create(pixelWidth, pixelHeight, 96.0 * dpi, 96.0 * dpi, PixelFormats.Bgra32, null, pixels, pixelWidth * 4);
                var fileName = $"d2-selection-highlights-{modeName}-{suffix}.png";
                foreach (var dir in outputDirs)
                {
                    var path = Path.Combine(dir, fileName);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bmp));
                    using var stream = File.Create(path);
                    encoder.Save(stream);
                    Assert.True(new FileInfo(path).Length > 4096);
                }
            }
        }
    }

    private static FrameworkElement BuildD2ShowcaseVisual(WindowStyle windowStyle)
    {
        bool isBorderless = windowStyle == WindowStyle.None;
        var root = new StackPanel
        {
            Background = new SolidColorBrush(Color.FromRgb(9, 13, 22)),
        };
        Css.SetStyle(root, "display: block; width: 1020px; height: 620px; padding: 18px; position: relative;");

        // Top Contextual Chrome Bar
        var chromeBar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 10, 16, 10),
        };
        Css.SetStyle(chromeBar, "display: block; margin-bottom: 14px; border-radius: 10px;");

        var chromeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var modeBadge = new Border
        {
            Background = isBorderless
                ? new SolidColorBrush(Color.FromRgb(14, 116, 144))
                : new SolidColorBrush(Color.FromRgb(30, 58, 138)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 3, 8, 3),
            Child = new TextBlock
            {
                Text = isBorderless ? "WITHOUTBORDER CHROME" : "NATIVE WINDOW CHROME",
                Foreground = new SolidColorBrush(Color.FromRgb(224, 242, 254)),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
            },
        };
        Css.SetStyle(modeBadge, "border-radius: 6px;");
        chromeRow.Children.Add(modeBadge);

        chromeRow.Children.Add(new TextBlock
        {
            Text = "Milestone D2 — CSS Highlight Pseudo-Elements, Layered Painting & WritingTools Structural Editing",
            Foreground = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
            FontSize = 12.5,
            FontWeight = FontWeights.Bold,
        });

        var kbdBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 3, 8, 3),
            Child = new TextBlock
            {
                Text = "CSS.highlights [6 Active Layers]",
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                FontSize = 10,
            },
        };
        Css.SetStyle(kbdBadge, "border-radius: 5px;");
        chromeRow.Children.Add(kbdBadge);
        chromeBar.Child = chromeRow;
        root.Children.Add(chromeBar);

        // Main 3-Column Workbench Grid
        var bodyRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };

        // Column 1: Live Multi-Layer Highlight Painting Surface
        var col1 = CreateCard(344, 528, "CSS Highlight Overlay Stack", "Priority: ::highlight -> ::target -> ::search -> ::error -> ::selection");
        var col1Stack = (StackPanel)col1.Child!;

        var previewSurface = new HighlightPreviewControl { Width = 316, Height = 300 };
        Css.SetClass(previewSurface, "hl-surface");
        AddStyleSheet(previewSurface, """
            .hl-surface {
                color: #e2e8f0;
            }
            .hl-surface::highlight(citation-span) {
                background-color: #0369a1;
                color: #e0f2fe;
                text-decoration-line: underline;
                text-decoration-color: #38bdf8;
            }
            .hl-surface::highlight(metric-focus) {
                background-color: #065f46;
                color: #ecfdf5;
                fill: #10b981;
                stroke: #6ee7b7;
                stroke-width: 1.75px;
            }
            .hl-surface::target-text {
                background-color: #581c87;
                color: #f3e8ff;
            }
            .hl-surface::search-text {
                background-color: #854d0e;
                color: #fef08a;
            }
            .hl-surface::search-text:current {
                background-color: #c2410c;
                color: #fff7ed;
                text-decoration-line: underline;
                text-decoration-color: #fdba74;
            }
            .hl-surface::spelling-error {
                text-decoration-color: #f43f5e;
            }
            .hl-surface::grammar-error {
                text-decoration-color: #22c55e;
            }
            .hl-surface::selection {
                background-color: #1d4ed8;
                color: #ffffff;
            }
            """);
        previewSurface.ConfigureDemoHighlights();
        col1Stack.Children.Add(previewSurface);

        col1Stack.Children.Add(CreateMetricReadout("Custom ::highlight(metric-focus)", "Priority 20  |  SVG fill #10B981  stroke 1.75px", Color.FromRgb(16, 185, 129)));
        col1Stack.Children.Add(CreateMetricReadout("::search-text:current Specificity", "(0, 1, 1) > ::search-text (0, 0, 1)  |  #C2410C", Color.FromRgb(249, 115, 22)));
        col1Stack.Children.Add(CreateMetricReadout("::spelling-error & ::grammar-error", "Default Wavy Decoration  |  Author Override Ready", Color.FromRgb(244, 63, 94)));
        bodyRow.Children.Add(col1);

        // Column 2: Live RichTextBox Document with Selection, Replaced UI & Hidden Subtrees
        var col2 = CreateCard(304, 528, "Document Selection & Replaced Wash", "Independent Highlight Inheritance & InlineUIContainer");
        var col2Stack = (StackPanel)col2.Child!;

        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("monospace"),
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
        };
        Css.SetClass(doc, "d2-doc");

        var p1 = new Paragraph(new Run("Selected telemetry frame inherits highlight variables without mutating layout metrics."));
        Css.SetClass(p1, "p-selected");
        doc.Blocks.Add(p1);

        var pHidden = new Paragraph(new Run("INTERNAL_KEY_DO_NOT_EXPOSE_9481"));
        Css.SetStyle(pHidden, "display: none;");
        doc.Blocks.Add(pHidden);

        var p2 = new Paragraph();
        p2.Inlines.Add(new Run("Embedded Node: "));
        var inlineBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(15, 118, 110)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(45, 212, 191)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 2, 6, 2),
            Child = new TextBlock
            {
                Text = "SHARD-04",
                Foreground = new SolidColorBrush(Color.FromRgb(204, 251, 241)),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
            },
        };
        p2.Inlines.Add(new InlineUIContainer(inlineBadge));
        p2.Inlines.Add(new Run(" verified online."));
        doc.Blocks.Add(p2);

        AddDocumentStyleSheet(doc, """
            .d2-doc::selection {
                background-color: #0284c7;
                color: #f8fafc;
                text-decoration-line: underline;
                text-decoration-color: #7dd3fc;
            }
            .p-selected {
                margin-bottom: 8px;
            }
            """);

        var editor = new RichTextBox
        {
            Document = doc,
            Width = 272,
            Height = 220,
            Background = new SolidColorBrush(Color.FromRgb(11, 17, 30)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
        };
        editor.Selection.Select(p1.ContentStart, p1.ContentEnd);
        col2Stack.Children.Add(editor);

        col2Stack.Children.Add(CreateMetricReadout("Hidden Subtree Isolation", "display:none Paragraph Preserved at Slot [1]", Color.FromRgb(56, 189, 248)));
        col2Stack.Children.Add(CreateMetricReadout("InlineUIContainer Integrity", "SHARD-04 Widget Preserved Across Text Edits", Color.FromRgb(45, 212, 191)));
        col2Stack.Children.Add(CreateMetricReadout("Replaced Selection Overlay", "Translucent Wash via ReplacedOverlayBrush", Color.FromRgb(168, 85, 247)));
        bodyRow.Children.Add(col2);

        // Column 3: System WritingTools Structure & Table Navigation Inspector
        var col3 = CreateCard(308, 528, "WritingTools & Table Navigation", "Tab / Shift+Tab Skips Collapsed & Hidden Cells");
        var col3Stack = (StackPanel)col3.Child!;

        col3Stack.Children.Add(CreateTableInspectorRow("R0:C0 [Visible]", "Node-US-East", "ACTIVE FOCUS", Color.FromRgb(16, 185, 129)));
        col3Stack.Children.Add(CreateTableInspectorRow("R0:C1 [Collapsed Col]", "visibility:collapse", "SKIPPED", Color.FromRgb(100, 116, 139)));
        col3Stack.Children.Add(CreateTableInspectorRow("R0:C2 [Visible]", "99.982% SLA", "NEXT TAB ->", Color.FromRgb(56, 189, 248)));
        col3Stack.Children.Add(CreateTableInspectorRow("R1:*  [Hidden Row]", "display:none", "OMITTED", Color.FromRgb(100, 116, 139)));
        col3Stack.Children.Add(CreateTableInspectorRow("R2:C2 [Visible]", "99.954% SLA", "TAB TARGET #3", Color.FromRgb(56, 189, 248)));

        col3Stack.Children.Add(CreateMetricReadout("Exported Table Clipboard", "Node-US-East \\t 99.982% SLA \\n 99.954% SLA", Color.FromRgb(14, 165, 233)));
        col3Stack.Children.Add(CreateMetricReadout("Undo / Redo Transaction", "Atomic Graph Rebase Without Hidden Run Leak", Color.FromRgb(34, 197, 94)));
        bodyRow.Children.Add(col3);

        root.Children.Add(bodyRow);
        return root;
    }

    private static Border CreateCard(double width, double height, string title, string subtitle)
    {
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
        });
        stack.Children.Add(new TextBlock
        {
            Text = subtitle,
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            FontSize = 10,
        });

        var card = new Border
        {
            Width = width,
            Height = height,
            Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            Child = stack,
        };
        Css.SetStyle(card, "border-radius: 10px;");
        return card;
    }

    private static Border CreateMetricReadout(string label, string value, Color accent)
    {
        var row = new StackPanel { Spacing = 2 };
        row.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(accent),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
        });
        row.Children.Add(new TextBlock
        {
            Text = value,
            Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
            FontSize = 10,
        });
        var box = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(22, 31, 50)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(39, 52, 73)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(9, 6, 9, 6),
            Child = row,
        };
        Css.SetStyle(box, "border-radius: 7px;");
        return box;
    }

    private static Border CreateTableInspectorRow(string coord, string content, string badgeText, Color badgeColor)
    {
        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        top.Children.Add(new TextBlock
        {
            Text = coord,
            Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
        });
        top.Children.Add(new TextBlock
        {
            Text = badgeText,
            Foreground = new SolidColorBrush(badgeColor),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
        });

        var container = new StackPanel { Spacing = 2 };
        container.Children.Add(top);
        container.Children.Add(new TextBlock
        {
            Text = content,
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            FontSize = 10,
        });

        var rowBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(19, 28, 46)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(39, 52, 73)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(9, 6, 9, 6),
            Child = container,
        };
        Css.SetStyle(rowBorder, "border-radius: 6px;");
        return rowBorder;
    }

    private sealed class HighlightPreviewControl : FrameworkElement
    {
        private const string DemoLine1 = "Citation [RFC-9114] & Metric 99.98%";
        private const string DemoLine2 = "Target #anchor-id  Search match [current]";
        private const string DemoLine3 = "Speling-err & grammer-issue + Selection";

        public void ConfigureDemoHighlights()
        {
            CssHighlightRegistry.Set("citation-span", new CssHighlight(
                priority: 5,
                CssHighlightType.Highlight,
                new CssHighlightRange(this, 9, 19)));
            CssHighlightRegistry.Set("metric-focus", new CssHighlight(
                priority: 20,
                CssHighlightType.Highlight,
                new CssHighlightRange(this, 22, 35)));
            CssHighlightRegistry.SetTargetRanges(new CssHighlightRange(this, 36, 53));
            CssHighlightRegistry.SetSearchRanges(
                new CssHighlightRange(this, 55, 67, IsCurrent: false),
                new CssHighlightRange(this, 68, 77, IsCurrent: true));
            CssHighlightRegistry.SetSpellingErrorRanges(new CssHighlightRange(this, 78, 89));
            CssHighlightRegistry.SetGrammarErrorRanges(new CssHighlightRange(this, 92, 105));
        }

        protected override void OnRender(DrawingContext dc)
        {
            var rect = new Rect(0, 0, ActualWidth, ActualHeight);
            dc.DrawRoundedRectangle(
                new SolidColorBrush(Color.FromRgb(11, 17, 30)),
                new Pen(new SolidColorBrush(Color.FromRgb(30, 41, 59)), 1),
                rect, 8, 8);

            var underlying = new SolidColorBrush(Color.FromRgb(226, 232, 240));
            const double charWidth = 7.4;

            Rect MapRange(int start, int length)
            {
                if (start < 36)
                    return new Rect(12 + start * charWidth, 16, length * charWidth, 24);
                if (start < 78)
                    return new Rect(12 + (start - 36) * charWidth, 54, length * charWidth, 24);
                return new Rect(12 + (start - 78) * charWidth, 92, length * charWidth, 24);
            }

            var regions = CssSelectionPainter.CollectHighlightRegions(
                this,
                textLength: 118,
                (s, l) => [MapRange(s, l)],
                selectionRange: (108, 9),
                underlying: underlying,
                baseline: 109);

            var clip = new RectangleGeometry(new Rect(4, 4, ActualWidth - 8, 132));
            CssSelectionPainter.Paint(
                dc,
                clip,
                regions,
                nativeBackground: new SolidColorBrush(Color.FromRgb(29, 78, 216)),
                nativeForeground: Brushes.White,
                draw: textDc =>
                {
                    DrawLineText(textDc, DemoLine1, new Point(12, 19), underlying);
                    DrawLineText(textDc, DemoLine2, new Point(12, 57), underlying);
                    DrawLineText(textDc, DemoLine3, new Point(12, 95), underlying);
                },
                decorate: (decoDc, region) =>
                {
                    double rowBaseline = region.Bounds.Top + 17;
                    CssSelectionPainter.Decorations(decoDc, region with { Baseline = rowBaseline }, this);
                });

            // SVG Highlight Fill/Stroke & Replaced Selection Overlay Strip
            var stripRect = new Rect(12, 138, ActualWidth - 24, 148);
            dc.DrawRoundedRectangle(
                new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                new Pen(new SolidColorBrush(Color.FromRgb(39, 52, 73)), 1),
                stripRect, 6, 6);

            DrawLineText(dc, "SVG Highlight Paint (fill / stroke / width):", new Point(22, 148), new SolidColorBrush(Color.FromRgb(148, 163, 184)), 9.5);

            var svgIconGeom = new PathGeometry();
            var fig = new PathFigure { StartPoint = new Point(26, 188), IsClosed = true, IsFilled = true };
            fig.Segments.Add(new LineSegment(new Point(48, 172), true));
            fig.Segments.Add(new LineSegment(new Point(70, 188), true));
            fig.Segments.Add(new LineSegment(new Point(60, 214), true));
            fig.Segments.Add(new LineSegment(new Point(36, 214), true));
            svgIconGeom.Figures.Add(fig);
            svgIconGeom.Freeze();
            CssSelectionPainter.PaintSvgHighlight(dc, this, svgIconGeom, CssPseudoElement.Highlight, "metric-focus", underlying);

            // Replaced element with ::selection wash overlay
            var replacedRect = new Rect(92, 174, 200, 42);
            dc.DrawRoundedRectangle(
                new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                new Pen(new SolidColorBrush(Color.FromRgb(71, 85, 105)), 1),
                replacedRect, 6, 6);
            DrawLineText(dc, "IMG / REPLACED CONTENT", new Point(108, 188), new SolidColorBrush(Color.FromRgb(226, 232, 240)), 9.5);
            CssSelectionPainter.PaintReplacedOverlay(dc, this, replacedRect, new SolidColorBrush(Color.FromRgb(29, 78, 216)), underlying, new CornerRadius(6));

            // Legend Pills
            DrawLegendPill(dc, new Rect(22, 230, 132, 20), "::highlight(citation)", Color.FromRgb(3, 105, 161));
            DrawLegendPill(dc, new Rect(162, 230, 132, 20), "::highlight(metric)", Color.FromRgb(6, 95, 70));
            DrawLegendPill(dc, new Rect(22, 256, 132, 20), "::search-text:current", Color.FromRgb(194, 65, 12));
            DrawLegendPill(dc, new Rect(162, 256, 132, 20), "::target-text / ::sel", Color.FromRgb(88, 28, 135));
        }

        private static void DrawLineText(DrawingContext dc, string text, Point origin, Brush foreground, double fontSize = 11.5)
        {
            var ft = new FormattedText(text, "monospace", fontSize)
            {
                Foreground = foreground,
            };
            TextMeasurement.MeasureText(ft);
            dc.DrawText(ft, origin);
        }

        private static void DrawLegendPill(DrawingContext dc, Rect bounds, string label, Color bg)
        {
            dc.DrawRoundedRectangle(
                new SolidColorBrush(bg),
                new Pen(new SolidColorBrush(Color.FromRgb(148, 163, 184)), 0.75),
                bounds, 4, 4);
            DrawLineText(dc, label, new Point(bounds.X + 6, bounds.Y + 4), Brushes.White, 8.5);
        }
    }

    private sealed class GeometryCountingContext : DrawingContextAdapter
    {
        public int GeometryCount { get; private set; }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) => GeometryCount++;
        public override void DrawLine(Pen pen, Point point0, Point point1) => GeometryCount++;
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY) { }
        public override void DrawText(FormattedText formattedText, Point origin) { }
        public override void DrawImage(ImageSource imageSource, Rect rectangle) { }
        public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect, CornerRadius cornerRadius) { }
        public override void PushClip(Geometry clipGeometry) { }
        public override void PushOpacity(double opacity) { }
        public override void PushTransform(Transform transform) { }
        public override void Pop() { }
        public override void Close() { }
    }

    private static RichTextBox CreateEditor(FlowDocument document)
    {
        var sheet = CssStyleSheet.Parse("Paragraph { margin: 0; }");
        Css.GetStyleSheets(document).Add(sheet);
        return new RichTextBox
        {
            Document = document,
            IsUndoEnabled = true,
            Width = 420,
            Height = 220,
        };
    }

    private static void AddStyleSheet(FrameworkElement host, string css)
    {
        var sheet = CssStyleSheet.Parse(css);
        Assert.Empty(sheet.Diagnostics);
        Css.GetStyleSheets(host).Add(sheet);
        CssEvaluationScheduler.EvaluateSubtree(CssNode.Get(host));
    }

    private static void AddDocumentStyleSheet(FlowDocument document, string css)
    {
        var sheet = CssStyleSheet.Parse(css);
        Assert.Empty(sheet.Diagnostics);
        Css.GetStyleSheets(document).Add(sheet);
        CssEvaluationScheduler.EvaluateSubtree(CssNode.Get(document));
    }

    private static void Layout(FrameworkElement root, int width, int height)
    {
        for (int pass = 0; pass < 5; pass++)
        {
            CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
            root.Measure(new Size(width, height));
            root.Arrange(new Rect(0, 0, width, height));
            root.UpdateLayout();
        }
    }

    private static string[] GetScreenshotDirectories()
    {
        var list = new List<string>();
        if (Environment.GetEnvironmentVariable("JALIUM_D2_SCREENSHOT_DIR") is { Length: > 0 } envDir)
            list.Add(envDir);

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Jalium.UI.sln")))
            current = current.Parent;
        var repoRoot = current?.FullName ?? "/Users/jal/Documents/Repos/Jalium.UI";
        list.Add(Path.Combine(repoRoot, "artifacts", "screenshots", "css"));

        const string acceptanceRoot = "/Volumes/My PSSD/.codex-css-acceptance";
        if (Directory.Exists(acceptanceRoot))
            list.Add(Path.Combine(acceptanceRoot, "css-d2-selection-highlights-editing-20261026", "screenshots"));

        return list.Distinct(StringComparer.Ordinal).ToArray();
    }
}

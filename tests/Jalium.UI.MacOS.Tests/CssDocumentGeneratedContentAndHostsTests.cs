using System.IO;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
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
public sealed class CssDocumentGeneratedContentAndHostsTests : IDisposable
{
    private const string InlineSvgDataUri =
        "data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' width='14' height='14' viewBox='0 0 14 14'><rect x='1' y='1' width='12' height='12' rx='3' fill='%2338bdf8' stroke='%230284c7' stroke-width='1.5'/><circle cx='7' cy='7' r='3' fill='%23f8fafc'/></svg>";

    private readonly RenderContext _context;

    public CssDocumentGeneratedContentAndHostsTests()
    {
        FlowDocumentTemplateTestHelper.Register();
        _context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
    }

    public void Dispose()
    {
    }

    [Fact]
    public void NestedBeforeAndAfterMarkerSelectorsParseMatchAndGenerateBoxes()
    {
        Assert.NotNull(CssSelectorParser.ParseGroup(".host::before::marker", out _));
        Assert.NotNull(CssSelectorParser.ParseGroup(".host::after::marker", out _));
        Assert.Null(CssSelectorParser.ParseGroup(".host::marker::before", out _));

        var host = new StackPanel();
        Css.SetClass(host, "host");
        AddStyleSheet(host, """
            .host { display: block; width: 240px; }
            .host::before {
                content: "Generated Lead";
                display: list-item;
                list-style-position: inside;
                list-style-type: upper-roman;
            }
            .host::before::marker {
                content: "» ";
                color: #ef4444;
            }
            .host::after {
                content: "Generated Tail";
                display: list-item;
                list-style-position: inside;
            }
            .host::after::marker {
                content: "« ";
                color: #22c55e;
            }
            """);

        Layout(host, 240, 100);

        var before = CssGeneratedContent.Get(host)?.Before;
        var after = CssGeneratedContent.Get(host)?.After;
        Assert.NotNull(before);
        Assert.NotNull(after);

        var beforeMarker = CssGeneratedContent.Get(before!)?.Marker;
        var afterMarker = CssGeneratedContent.Get(after!)?.Marker;
        Assert.NotNull(beforeMarker);
        Assert.NotNull(afterMarker);
        Assert.Equal("» ", beforeMarker!.Text);
        Assert.Equal("« ", afterMarker!.Text);
        Assert.Equal(Color.FromRgb(0xef, 0x44, 0x44), Assert.IsType<SolidColorBrush>(beforeMarker.Foreground).Color);
        Assert.Equal(Color.FromRgb(0x22, 0xc5, 0x5e), Assert.IsType<SolidColorBrush>(afterMarker.Foreground).Color);
        Assert.Empty(host.Children);
    }

    [Fact]
    public void PredefinedComplexCounterStylesFormatAndSpeakAsExpected()
    {
        var host = new StackPanel();
        Css.SetClass(host, "list");
        var items = new Border[7];
        for (var i = 0; i < items.Length; i++)
        {
            items[i] = new Border { Height = 18 };
            Css.SetClass(items[i], $"item-{i + 1}");
            host.Children.Add(items[i]);
        }

        AddStyleSheet(host, $$"""
            @counter-style badge-icons {
                system: cyclic;
                symbols: url("{{InlineSvgDataUri}}");
                suffix: " ";
                speak-as: words;
            }
            .list > Border { display: list-item; list-style-position: inside; }
            .item-1 { list-style-type: simp-chinese-informal; counter-set: list-item 12; }
            .item-2 { list-style-type: simp-chinese-formal; counter-set: list-item 105; }
            .item-3 { list-style-type: japanese-informal; counter-set: list-item 23; }
            .item-4 { list-style-type: korean-hangul-formal; counter-set: list-item 15; }
            .item-5 { list-style-type: ethiopic-numeric; counter-set: list-item 123; }
            .item-6 { list-style-type: hebrew; counter-set: list-item 15; }
            .item-7 { list-style-type: badge-icons; counter-set: list-item 3; }
            """);

        Layout(host, 300, 200);

        Assert.Equal("十二、", CssGeneratedContent.Get(items[0])?.Marker?.Text);
        Assert.Equal("壹佰零伍、", CssGeneratedContent.Get(items[1])?.Marker?.Text);
        Assert.Equal("二十三、", CssGeneratedContent.Get(items[2])?.Marker?.Text);
        Assert.Equal("일십오, ", CssGeneratedContent.Get(items[3])?.Marker?.Text);
        Assert.Equal("፻፳፫፦ ", CssGeneratedContent.Get(items[4])?.Marker?.Text);
        Assert.Equal("טו. ", CssGeneratedContent.Get(items[5])?.Marker?.Text);

        var imageMarker = CssGeneratedContent.Get(items[6])?.Marker;
        Assert.NotNull(imageMarker);
        Assert.True(imageMarker!.HasImageContent);
        var peer = UIElementAutomationPeer.CreatePeerForElement(imageMarker);
        Assert.NotNull(peer);
        Assert.Equal("three", peer!.GetName());
    }

    [Fact]
    public void MarkerSideMatchSelfAndMatchParentControlOutsideMarkerPlacementInFlowDocument()
    {
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("monospace"),
            FontSize = 14,
            Foreground = Brushes.Black,
        };
        var list = new DocList();
        var itemSelf = new ListItem(new Paragraph(new Run("RTL Self")));
        var itemParent = new ListItem(new Paragraph(new Run("RTL Parent")));
        Css.SetClass(list, "list");
        Css.SetClass(itemSelf, "rtl-self");
        Css.SetClass(itemParent, "rtl-parent");
        list.ListItems.Add(itemSelf);
        list.ListItems.Add(itemParent);
        doc.Blocks.Add(list);

        AddDocumentStyleSheet(doc, """
            .list { direction: ltr; padding-left: 36px; padding-right: 36px; }
            .rtl-self { direction: rtl; marker-side: match-self; }
            .rtl-parent { direction: rtl; marker-side: match-parent; }
            """);

        var viewer = new FlowDocumentScrollViewer
        {
            Document = doc,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };
        Layout(viewer, 320, 140);

        var markers = FindVisualDescendants<CssGeneratedText>(viewer)
            .Where(m => m.Kind == CssPseudoElement.Marker)
            .ToArray();
        Assert.Equal(2, markers.Length);

        var selfMarker = markers.Single(m => ReferenceEquals(m.Origin.Target, itemSelf));
        var parentMarker = markers.Single(m => ReferenceEquals(m.Origin.Target, itemParent));

        // match-self on an RTL list item places the outside marker on the right side,
        // whereas match-parent inherits the LTR list's left-side marker placement.
        Assert.True(selfMarker.VisualBounds.X > parentMarker.VisualBounds.X + 80,
            $"Expected match-self marker X ({selfMarker.VisualBounds.X}) > match-parent marker X ({parentMarker.VisualBounds.X})");
    }

    [Fact]
    public void InlineUIContainerAndBlockUIContainerSupportCssBoxesGeneratedContentAndInteractiveChildren()
    {
        var inlineButton = new Button { Content = "InlineBtn", Width = 64, Height = 22 };
        var inlineContainer = new InlineUIContainer(inlineButton);
        Css.SetClass(inlineContainer, "inline-slot");

        var paragraph = new Paragraph();
        paragraph.Inlines.Add(new Run("Before "));
        paragraph.Inlines.Add(inlineContainer);
        paragraph.Inlines.Add(new Run(" After"));

        var blockButton = new Button { Content = "BlockBtn", Width = 80, Height = 26 };
        var blockContainer = new BlockUIContainer(blockButton);
        Css.SetClass(blockContainer, "block-slot");

        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("monospace"),
            FontSize = 14,
            Foreground = Brushes.Black,
        };
        doc.Blocks.Add(paragraph);
        doc.Blocks.Add(blockContainer);

        AddDocumentStyleSheet(doc, """
            .inline-slot {
                display: inline-block;
                border: 2px solid #0ea5e9;
                padding: 2px 4px;
                position: relative;
                top: 2px;
            }
            .inline-slot::before { content: "["; color: #0284c7; }
            .inline-slot::after { content: "]"; color: #0284c7; }
            .inline-slot > Button { min-width: 68px; }
            .block-slot {
                display: block;
                border: 2px solid #8b5cf6;
                padding: 4px;
                margin-top: 6px;
            }
            .block-slot::before { content: "BLOCK-PRE "; color: #7c3aed; }
            .block-slot::after { content: " BLOCK-POST"; color: #7c3aed; }
            .block-slot > Button { min-width: 88px; }
            """);

        var viewer = new FlowDocumentScrollViewer
        {
            Document = doc,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };
        Layout(viewer, 360, 200);

        // Public tree integrity: Child and Parent are never reparented or replaced.
        Assert.Same(inlineButton, inlineContainer.Child);
        Assert.Same(blockButton, blockContainer.Child);
        Assert.Null(inlineButton.Parent);
        Assert.Null(blockButton.Parent);
        Assert.Same(paragraph, inlineContainer.Parent);
        Assert.Same(doc, ((FrameworkContentElement)blockContainer).Parent);

        // Child selector matching through InlineUIContainer / BlockUIContainer:
        Assert.Equal(68, inlineButton.MinWidth, 3);
        Assert.Equal(88, blockButton.MinWidth, 3);

        // Generated ::before and ::after boxes exist on projected container boxes:
        var generatedTexts = FindVisualDescendants<CssGeneratedText>(viewer).Select(t => t.Text).ToArray();
        Assert.Contains("[", generatedTexts);
        Assert.Contains("]", generatedTexts);
        Assert.Contains("BLOCK-PRE ", generatedTexts);
        Assert.Contains(" BLOCK-POST", generatedTexts);

        // Plain-text extraction excludes generated content:
        Assert.DoesNotContain("BLOCK-PRE", doc.GetText());
        Assert.DoesNotContain("[", doc.GetText());

        // Hit-testing reaches the embedded interactive Button inside InlineUIContainer:
        var buttonTransform = inlineButton.TransformToAncestor(viewer);
        var buttonCenter = new Point(buttonTransform.X + inlineButton.RenderSize.Width / 2, buttonTransform.Y + inlineButton.RenderSize.Height / 2);
        var hit = VisualTreeHelper.HitTest(viewer, buttonCenter);
        Assert.NotNull(hit?.VisualHit);
        Assert.True(ReferenceEquals(hit!.VisualHit, inlineButton) || inlineButton.IsAncestorOf(hit.VisualHit!),
            $"Expected HitTest at {buttonCenter} to hit inlineButton, but got {hit.VisualHit?.GetType().Name}");
    }

    [Fact]
    public void NonVisualDocumentNodesSupportGeneratedContentImageReplacementAndAutomationAltText()
    {
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("monospace"),
            FontSize = 14,
            Foreground = Brushes.Black,
        };

        var section = new Section();
        Css.SetClass(section, "sec");
        var p1 = new Paragraph();
        Css.SetClass(p1, "para");
        var span = new Span(new Run("SpanBody"));
        Css.SetClass(span, "tag-span");
        var replacedRun = new Run("HiddenRunText");
        Css.SetClass(replacedRun, "icon-run");
        p1.Inlines.Add(span);
        p1.Inlines.Add(new Run(" "));
        p1.Inlines.Add(replacedRun);
        section.Blocks.Add(p1);

        var replacedPara = new Paragraph(new Run("OriginalParagraphText"));
        Css.SetClass(replacedPara, "replaced-para");
        section.Blocks.Add(replacedPara);

        doc.Blocks.Add(section);

        AddDocumentStyleSheet(doc, $$"""
            .sec::before { content: "SEC-HEAD"; display: block; color: #0284c7; }
            .para::before { content: "«"; color: #ef4444; }
            .para::after { content: "»"; color: #ef4444; }
            .tag-span::before { content: "#"; color: #10b981; }
            .icon-run { content: url("{{InlineSvgDataUri}}") / "Badge Icon Alt"; }
            .replaced-para { content: url("{{InlineSvgDataUri}}") / "Banner Diagram Alt"; }
            """);

        var viewer = new FlowDocumentScrollViewer
        {
            Document = doc,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };
        Layout(viewer, 360, 220);

        // Generated ::before / ::after on Section, Paragraph, Span:
        var generatedTexts = FindVisualDescendants<CssGeneratedText>(viewer).Select(t => t.Text).ToArray();
        Assert.Contains("SEC-HEAD", generatedTexts);
        Assert.Contains("«", generatedTexts);
        Assert.Contains("»", generatedTexts);
        Assert.Contains("#", generatedTexts);

        // AutomationPeer on non-visual document nodes reports CSS content alt text:
        var runPeer = ContentElementAutomationPeer.CreatePeerForElement(replacedRun);
        var paraPeer = ContentElementAutomationPeer.CreatePeerForElement(replacedPara);
        Assert.Equal("Badge Icon Alt", runPeer.GetName());
        Assert.Equal("Banner Diagram Alt", paraPeer.GetName());

        // Authored text & structure remain untouched:
        Assert.Equal("HiddenRunText", replacedRun.Text);
        Assert.Single(replacedPara.Inlines);
        Assert.DoesNotContain("SEC-HEAD", doc.GetText());

        // Style removal recovers original paragraph rendering cleanly:
        Css.SetClass(replacedPara, "");
        Layout(viewer, 360, 220);
        Assert.NotEqual("Banner Diagram Alt", paraPeer.GetName());
    }

    [Fact]
    public void SelfDrawnTextHostsSupportCssGeneratedContentAndTypographyViaPrivateAdapter()
    {
        var root = new StackPanel();
        var label = new Label { Content = "alpha beta" };
        var link = new HyperlinkButton { Content = "docs portal", NavigateUri = new Uri("https://example.com") };
        var status = new StatusBarItem { Content = "ready" };
        Css.SetClass(label, "lbl");
        Css.SetClass(link, "lnk");
        Css.SetClass(status, "stb");
        root.Children.Add(label);
        root.Children.Add(link);
        root.Children.Add(status);

        AddStyleSheet(root, $$"""
            .lbl::before { content: "[LBL] "; color: #0284c7; }
            .lbl::first-letter { font-size: 24px; color: #ef4444; }
            .lnk::after { content: " ↗"; color: #10b981; }
            .lnk::first-line { color: #6366f1; }
            .stb { content: url("{{InlineSvgDataUri}}") / "System Ready Icon"; width: 48px; height: 24px; }
            """);

        Layout(root, 320, 180);

        // Public Content property is never mutated or replaced:
        Assert.Equal("alpha beta", label.Content);
        Assert.Equal("docs portal", link.Content);
        Assert.Equal("ready", status.Content);

        // Private CSS content adapters are active on Label and HyperlinkButton:
        Assert.True(label.UsesCssContentAdapter);
        Assert.True(link.UsesCssContentAdapter);
        Assert.True(CssReplacedContent.IsActive(status));

        var statusPeer = UIElementAutomationPeer.CreatePeerForElement(status);
        Assert.NotNull(statusPeer);
        Assert.Equal("System Ready Icon", statusPeer!.GetName());

        // Removing CSS classes cleanly tears down adapters and image replacement:
        Css.SetClass(label, "");
        Css.SetClass(link, "");
        Css.SetClass(status, "");
        Layout(root, 320, 180);

        Assert.False(label.UsesCssContentAdapter);
        Assert.False(link.UsesCssContentAdapter);
        Assert.False(CssReplacedContent.IsActive(status));
        Assert.Equal("alpha beta", label.Content);
        Assert.Equal("docs portal", link.Content);
        Assert.Equal("ready", status.Content);
    }

    [Fact]
    public void ParagraphFirstLetterAndFirstLineIncludeProjectedBeforeContentAndPreserveSelection()
    {
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("monospace"),
            FontSize = 16,
            Foreground = Brushes.Black,
        };
        var paragraph = new Paragraph(new Run("words on line one\nsecond line stays default"));
        Css.SetClass(paragraph, "drop-para");
        doc.Blocks.Add(paragraph);

        AddDocumentStyleSheet(doc, """
            .drop-para {
                white-space: pre-line;
                line-height: 1.2;
                text-transform: uppercase;
            }
            .drop-para::before {
                content: "“Lead ";
            }
            .drop-para::first-letter {
                font-size: 30px;
                color: #ef4444;
            }
            .drop-para::first-line {
                color: #2563eb;
                text-emphasis-style: dot;
                text-emphasis-color: #f59e0b;
            }
            """);

        var viewer = new FlowDocumentScrollViewer
        {
            Document = doc,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };
        Layout(viewer, 420, 180);

        var paints = CaptureDraws(viewer);
        Assert.Contains(paints, p => p.Text.Contains("“L") && p.Color == Color.FromRgb(0xef, 0x44, 0x44) && Math.Abs(p.Size - 30) < 0.1);
        Assert.Contains(paints, p => p.Text.Contains("WORDS ON LINE ONE") && p.Color == Color.FromRgb(0x25, 0x63, 0xeb));
        Assert.Contains(paints, p => p.Text.Contains("SECOND LINE") && p.Color == Colors.Black);

        // Source Paragraph and Run remain untouched:
        var singleRun = Assert.IsType<Run>(Assert.Single(paragraph.Inlines));
        Assert.Equal("words on line one\nsecond line stays default", singleRun.Text);
    }

    [Fact]
    public void RenderD1DocumentBoxesAndGeneratedContentVisualsInDualWindowAndDpi()
    {
        var outDir = Environment.GetEnvironmentVariable("JALIUM_D1_SCREENSHOT_DIR")
            ?? "/Volumes/My PSSD/.codex-css-acceptance/css-d1-document-boxes-generated-content-20261026/screenshots";
        Directory.CreateDirectory(outDir);

        foreach (var (windowStyle, modeName) in new[]
        {
            (WindowStyle.SingleBorderWindow, "native"),
            (WindowStyle.None, "withoutborder")
        })
        {
            foreach (var (dpi, suffix) in new[] { (1.0, "1x"), (2.0, "2x") })
            {
                var visual = BuildD1ShowcaseVisual(windowStyle);
                VisualTreeHelper.SetRootDpi(visual, new DpiScale(dpi, dpi));
                var width = 860;
                var height = 560;
                Layout(visual, width, height);

                var pixelWidth = (int)Math.Round(width * dpi);
                var pixelHeight = (int)Math.Round(height * dpi);
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
                var nonZero = pixels.Count(b => b != 0);
                Assert.True(nonZero > pixelWidth * pixelHeight, $"Expected rendered content for {modeName}-{suffix}");

                var path = Path.Combine(outDir, $"d1-document-boxes-generated-content-{modeName}-{suffix}.png");
                var bmp = BitmapSource.Create(pixelWidth, pixelHeight, 96.0 * dpi, 96.0 * dpi, PixelFormats.Bgra32, null, pixels, pixelWidth * 4);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bmp));
                using var stream = File.Create(path);
                encoder.Save(stream);
                Assert.True(new FileInfo(path).Length > 1024);
            }
        }
    }

    private static FrameworkElement BuildD1ShowcaseVisual(WindowStyle windowStyle)
    {
        var root = new StackPanel
        {
            Background = new SolidColorBrush(Color.FromRgb(11, 15, 25)),
        };
        Css.SetStyle(root, "display: block; width: 860px; height: 560px; padding: 18px; position: relative;");

        var header = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(22, 30, 46)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 10, 14, 10),
            Child = new TextBlock
            {
                Text = $"D1 Document Boxes, Generated Content & Counter Styles — Window Mode: {(windowStyle == WindowStyle.None ? "WithoutBorder" : "Native Chrome")}",
                Foreground = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
            },
        };
        Css.SetStyle(header, "display: block; margin-bottom: 14px; border-radius: 8px;");
        root.Children.Add(header);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };

        // Card 1: FlowDocument with Paragraph ::before/::first-letter/::first-line, InlineUIContainer & BlockUIContainer
        var card1 = new Border
        {
            Width = 270,
            Height = 460,
            Background = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10),
        };
        Css.SetStyle(card1, "border-radius: 10px;");

        var doc1 = new FlowDocument
        {
            FontFamily = new FontFamily("monospace"),
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
        };
        var pLead = new Paragraph(new Run("Editorial drop-cap with generated quote and first-line accent across wrapped document lines."));
        Css.SetClass(pLead, "lead-para");
        doc1.Blocks.Add(pLead);

        var pInlineHost = new Paragraph();
        pInlineHost.Inlines.Add(new Run("Inline UI: "));
        var inlineChip = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(14, 116, 144)),
            Padding = new Thickness(6, 2, 6, 2),
            Child = new TextBlock { Text = "v2.4", Foreground = Brushes.White, FontSize = 11 },
        };
        var inlineUi = new InlineUIContainer(inlineChip);
        Css.SetClass(inlineUi, "inline-ui-box");
        pInlineHost.Inlines.Add(inlineUi);
        var spanBadge = new Span(new Run("LIVE"));
        Css.SetClass(spanBadge, "span-chip");
        pInlineHost.Inlines.Add(new Run(" "));
        pInlineHost.Inlines.Add(spanBadge);
        doc1.Blocks.Add(pInlineHost);

        var blockUiCard = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
            Padding = new Thickness(8, 6, 8, 6),
            Child = new TextBlock
            {
                Text = "BlockUIContainer Embedded Control",
                Foreground = new SolidColorBrush(Color.FromRgb(125, 211, 252)),
                FontSize = 11,
            },
        };
        var blockUi = new BlockUIContainer(blockUiCard);
        Css.SetClass(blockUi, "block-ui-box");
        doc1.Blocks.Add(blockUi);

        AddDocumentStyleSheet(doc1, """
            .lead-para { line-height: 1.35; margin-bottom: 10px; }
            .lead-para::before { content: "“"; }
            .lead-para::first-letter { font-size: 26px; color: #f43f5e; font-weight: bold; }
            .lead-para::first-line { color: #38bdf8; }
            .inline-ui-box { border: 1px solid #38bdf8; padding: 1px 3px; border-radius: 4px; }
            .inline-ui-box::before { content: "‹"; color: #38bdf8; }
            .inline-ui-box::after { content: "›"; color: #38bdf8; }
            .span-chip { background: #065f46; color: #a7f3d0; padding: 1px 5px; border-radius: 4px; }
            .span-chip::before { content: "● "; color: #34d399; }
            .block-ui-box { border: 1px solid #6366f1; padding: 5px; margin-top: 10px; border-radius: 6px; }
            .block-ui-box::before { content: "HOSTED BLOCK • "; color: #a5b4fc; font-weight: bold; }
            """);

        card1.Child = new FlowDocumentScrollViewer
        {
            Document = doc1,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };
        row.Children.Add(card1);

        // Card 2: Predefined @counter-style scripts, image symbols, marker-side & nested ::before::marker
        var card2 = new StackPanel
        {
            Width = 270,
            Height = 460,
            Background = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
            Spacing = 6,
        };
        Css.SetClass(card2, "counter-panel");
        var c2Title = new TextBlock
        {
            Text = "2. @counter-style, marker-side & ::before::marker",
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
        };
        card2.Children.Add(c2Title);

        var counterEntries = new (string ClassName, string LabelText)[]
        {
            ("cs-cjk-informal", "Simplified Chinese Informal (12)"),
            ("cs-cjk-formal", "Simplified Chinese Formal (105)"),
            ("cs-jp-informal", "Japanese Informal (23)"),
            ("cs-kr-formal", "Korean Hangul Formal (15)"),
            ("cs-ethiopic", "Ethiopic Numeric (123)"),
            ("cs-hebrew", "Hebrew Traditional (15)"),
            ("cs-image", "SVG Image Symbol Counter"),
            ("cs-nested", "Host with ::before::marker & ::after::marker"),
        };

        foreach (var (cls, text) in counterEntries)
        {
            var entry = new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                FontSize = 11.5,
            };
            Css.SetClass(entry, cls);
            card2.Children.Add(entry);
        }

        AddStyleSheet(card2, $$"""
            @counter-style svg-bullet {
                system: cyclic;
                symbols: url("{{InlineSvgDataUri}}");
                suffix: " ";
            }
            .counter-panel {
                border: 1px solid #334155;
                border-radius: 10px;
                padding: 12px;
            }
            .counter-panel > TextBlock {
                display: list-item;
                list-style-position: inside;
                padding: 4px 6px;
                background: #1e293b;
                border-radius: 5px;
            }
            .cs-cjk-informal { list-style-type: simp-chinese-informal; counter-set: list-item 12; }
            .cs-cjk-formal { list-style-type: simp-chinese-formal; counter-set: list-item 105; }
            .cs-jp-informal { list-style-type: japanese-informal; counter-set: list-item 23; }
            .cs-kr-formal { list-style-type: korean-hangul-formal; counter-set: list-item 15; }
            .cs-ethiopic { list-style-type: ethiopic-numeric; counter-set: list-item 123; }
            .cs-hebrew { list-style-type: hebrew; counter-set: list-item 15; }
            .cs-image { list-style-type: svg-bullet; }
            .cs-nested { display: block; background: #1e1b4b; }
            .cs-nested::before {
                content: "Nested Before ";
                display: list-item;
                list-style-position: inside;
                color: #c4b5fd;
            }
            .cs-nested::before::marker { content: "◆ "; color: #f43f5e; }
            .cs-nested::after {
                content: " Nested After";
                display: list-item;
                list-style-position: inside;
                color: #93c5fd;
            }
            .cs-nested::after::marker { content: " ◇ "; color: #34d399; }
            """);

        row.Children.Add(card2);

        // Card 3: Self-Drawn Controls (Label, HyperlinkButton, StatusBarItem) & Replaced Content
        var card3 = new StackPanel
        {
            Width = 264,
            Height = 460,
            Background = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
            Spacing = 10,
        };
        Css.SetClass(card3, "hosts-panel");

        var c3Title = new TextBlock
        {
            Text = "3. Self-Drawn Hosts & Image Replacement",
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
        };
        card3.Children.Add(c3Title);

        var styledLabel = new Label
        {
            Content = "Label adapter with drop-cap & ::before/::after badges",
            Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
        };
        Css.SetClass(styledLabel, "demo-label");
        card3.Children.Add(styledLabel);

        var styledLink = new HyperlinkButton
        {
            Content = "HyperlinkButton with CSS ::before & ::after",
            NavigateUri = new Uri("https://jalium.dev/docs"),
        };
        Css.SetClass(styledLink, "demo-link");
        card3.Children.Add(styledLink);

        var statusBar = new StatusBar
        {
            Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
        };
        var sbItem1 = new StatusBarItem
        {
            Content = "Build Succeeded",
            Foreground = new SolidColorBrush(Color.FromRgb(167, 243, 208)),
        };
        Css.SetClass(sbItem1, "demo-status-text");
        var sbItem2 = new StatusBarItem
        {
            Content = "ReplacedStatus",
        };
        Css.SetClass(sbItem2, "demo-status-icon");
        statusBar.Items.Add(sbItem1);
        statusBar.Items.Add(sbItem2);
        card3.Children.Add(statusBar);

        AddStyleSheet(card3, $$"""
            .hosts-panel {
                border: 1px solid #334155;
                border-radius: 10px;
                padding: 12px;
            }
            .demo-label {
                display: block;
                background: #1e293b;
                padding: 8px;
                border-radius: 6px;
                line-height: 1.35;
            }
            .demo-label::before { content: "[LABEL] "; color: #38bdf8; font-weight: bold; }
            .demo-label::after { content: " ✓"; color: #34d399; }
            .demo-label::first-letter { font-size: 20px; color: #f43f5e; }
            .demo-link {
                display: block;
                background: #1e293b;
                padding: 8px;
                border-radius: 6px;
                color: #93c5fd;
            }
            .demo-link::before { content: "↗ "; color: #60a5fa; }
            .demo-link::after { content: " →"; color: #38bdf8; }
            .demo-status-text {
                display: inline-block;
                padding: 4px 8px;
            }
            .demo-status-text::before { content: "● "; color: #10b981; }
            .demo-status-icon {
                display: inline-block;
                content: url("{{InlineSvgDataUri}}") / "Verified Status";
                width: 18px;
                height: 18px;
                margin-left: 8px;
            }
            """);

        row.Children.Add(card3);
        root.Children.Add(row);
        return root;
    }

    private static void AddStyleSheet(FrameworkElement owner, string css)
    {
        var sheet = CssStyleSheet.Parse(css);
        Assert.Empty(sheet.Diagnostics);
        Css.GetStyleSheets(owner).Add(sheet);
        CssEvaluationScheduler.EvaluateSubtree(CssNode.Get(owner));
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
        for (var pass = 0; pass < 5; pass++)
        {
            CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
            root.Measure(new Size(width, height));
            root.Arrange(new Rect(0, 0, width, height));
            root.UpdateLayout();
        }
    }

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is null) continue;
            if (child is T match) yield return match;
            foreach (var nested in FindVisualDescendants<T>(child))
                yield return nested;
        }
    }

    private sealed record Paint(string Text, double Size, int Style, Color Color);

    private static List<Paint> CaptureDraws(FrameworkElement root)
    {
        var capture = new CaptureContext();
        root.Render(capture);
        return capture.Paints;
    }

    private sealed class CaptureContext : DrawingContextAdapter, IOffsetDrawingContext
    {
        internal readonly List<Paint> Paints = [];
        public Point Offset { get; set; }

        public override void DrawText(FormattedText text, Point origin)
        {
            Paints.Add(new(
                text.Text,
                text.FontSize,
                text.FontStyle,
                text.Foreground is SolidColorBrush brush ? brush.Color : Colors.Transparent));
        }

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

using System.IO;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;
using Jalium.UI.Threading;
using Xunit;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssWritingModesRubyAndAnchorTests : IDisposable
{
    private readonly RenderContext _context;

    public CssWritingModesRubyAndAnchorTests()
    {
        _context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
    }

    public void Dispose()
    {
        CssAnchorPositioning.ClearPositionTryRules();
    }

    [Fact]
    public void WritingModeAndTextOrientationPropertiesParseInheritAndMapLogicalAxes()
    {
        var container = new StackPanel();
        var child = new TextBlock { Text = "縦書き24年" };
        container.Children.Add(child);
        Css.SetClass(container, "vertical-box");
        Css.SetClass(child, "combined-digits");

        AddStyleSheet(container, """
            .vertical-box {
                writing-mode: vertical-rl;
                text-orientation: upright;
                ruby-position: under;
                ruby-align: space-between;
                ruby-merge: merge;
            }
            .combined-digits {
                text-combine-upright: digits 2;
            }
            """);

        Layout(container, 320, 240);

        Assert.Equal(CssWritingMode.VerticalRl, CssWritingModeProperties.GetWritingMode(container));
        Assert.Equal(CssWritingMode.VerticalRl, CssWritingModeProperties.GetWritingMode(child));
        Assert.Equal(CssTextOrientation.Upright, CssWritingModeProperties.GetTextOrientation(child));
        Assert.Equal(CssRubyPosition.Under, CssWritingModeProperties.GetRubyPosition(child));
        Assert.Equal(CssRubyAlign.SpaceBetween, CssWritingModeProperties.GetRubyAlign(child));
        Assert.Equal(CssRubyMerge.Collapse, CssWritingModeProperties.GetRubyMerge(child));

        var combine = CssWritingModeProperties.GetTextCombineUpright(child);
        Assert.Equal(CssTextCombineUprightKind.Digits, combine.Kind);
        Assert.Equal(2, combine.MaxDigits);

        // Verify logical <-> physical size & rect conversions
        Assert.True(CssWritingModeProperties.IsVertical(CssWritingMode.VerticalRl));
        Assert.True(CssWritingModeProperties.IsBlockRightToLeft(CssWritingMode.VerticalRl));
        Assert.False(CssWritingModeProperties.IsBlockRightToLeft(CssWritingMode.VerticalLr));

        var (inlineSize, blockSize) = CssWritingModeProperties.ToLogicalSize(new Size(120, 300), CssWritingMode.VerticalRl);
        Assert.Equal(300, inlineSize);
        Assert.Equal(120, blockSize);

        var physicalRect = CssWritingModeProperties.ToPhysicalRect(
            inlineOffset: 20,
            blockOffset: 15,
            inlineSize: 80,
            blockSize: 30,
            containerPhysicalSize: new Size(200, 300),
            mode: CssWritingMode.VerticalRl);
        Assert.Equal(new Rect(155, 20, 30, 80), physicalRect);

        // Glyph orientation angles
        Assert.Equal(0d, CssWritingModeProperties.GetGlyphRotationDegrees('漢', CssWritingMode.VerticalRl, CssTextOrientation.Mixed));
        Assert.Equal(90d, CssWritingModeProperties.GetGlyphRotationDegrees('A', CssWritingMode.VerticalRl, CssTextOrientation.Mixed));
        Assert.Equal(0d, CssWritingModeProperties.GetGlyphRotationDegrees('A', CssWritingMode.VerticalRl, CssTextOrientation.Upright));
        Assert.Equal(-90d, CssWritingModeProperties.GetGlyphRotationDegrees('A', CssWritingMode.SidewaysLr, CssTextOrientation.Mixed));
    }

    [Fact]
    public void LogicalBoxEdgesAndCornersResolveAgainstVerticalAndSidewaysWritingModes()
    {
        // vertical-rl: block flows right-to-left (block-start = right, block-end = left),
        // inline flows top-to-bottom in LTR (inline-start = top, inline-end = bottom)
        Assert.Equal("margin-top", CssLogicalBoxEdges.PhysicalName("margin-inline-start", rightToLeft: false, CssWritingMode.VerticalRl));
        Assert.Equal("margin-bottom", CssLogicalBoxEdges.PhysicalName("margin-inline-end", rightToLeft: false, CssWritingMode.VerticalRl));
        Assert.Equal("padding-right", CssLogicalBoxEdges.PhysicalName("padding-block-start", rightToLeft: false, CssWritingMode.VerticalRl));
        Assert.Equal("padding-left", CssLogicalBoxEdges.PhysicalName("padding-block-end", rightToLeft: false, CssWritingMode.VerticalRl));
        Assert.Equal("height", CssLogicalBoxEdges.PhysicalName("inline-size", rightToLeft: false, CssWritingMode.VerticalRl));
        Assert.Equal("width", CssLogicalBoxEdges.PhysicalName("block-size", rightToLeft: false, CssWritingMode.VerticalRl));

        // vertical-lr: block flows left-to-right (block-start = left, block-end = right)
        Assert.Equal("border-left-width", CssLogicalBoxEdges.PhysicalName("border-block-start-width", rightToLeft: false, CssWritingMode.VerticalLr));
        Assert.Equal("border-right-width", CssLogicalBoxEdges.PhysicalName("border-block-end-width", rightToLeft: false, CssWritingMode.VerticalLr));

        // Corner mapping: start-start = (block-start, inline-start)
        // In vertical-rl LTR: block-start = right, inline-start = top -> border-top-right-radius
        Assert.Equal("border-top-right-radius", CssLogicalBoxEdges.PhysicalName("border-start-start-radius", rightToLeft: false, CssWritingMode.VerticalRl));
        Assert.Equal("border-bottom-left-radius", CssLogicalBoxEdges.PhysicalName("border-end-end-radius", rightToLeft: false, CssWritingMode.VerticalRl));

        // sideways-lr LTR: inline flows bottom-to-top (inline-start = bottom, inline-end = top), block flows left-to-right
        Assert.Equal("bottom", CssLogicalBoxEdges.PhysicalName("inset-inline-start", rightToLeft: false, CssWritingMode.SidewaysLr));
        Assert.Equal("top", CssLogicalBoxEdges.PhysicalName("inset-inline-end", rightToLeft: false, CssWritingMode.SidewaysLr));
    }

    [Fact]
    public void TextCombineUprightSegmentsHorizontalInVerticalTateChuYokoRuns()
    {
        var digits2 = new CssTextCombineUpright(CssTextCombineUprightKind.Digits, 2);
        var segments = CssWritingModeProperties.SegmentTateChuYoko("令和08年10月26日2026年", CssWritingMode.VerticalRl, digits2);

        // "08", "10", and "26" have 2 digits (combined); "2026" has 4 digits (> 2, not combined)
        var combinedTexts = segments.Where(static s => s.IsCombined).Select(static s => s.Text).ToArray();
        Assert.Equal(["08", "10", "26"], combinedTexts);
        Assert.All(segments.Where(static s => s.IsCombined), static s => Assert.InRange(s.HorizontalCompressionScale, 0.3, 1.0));

        // text-combine-upright: all combines entire short run
        var allSegs = CssWritingModeProperties.SegmentTateChuYoko("AB", CssWritingMode.VerticalRl, CssTextCombineUpright.All);
        var single = Assert.Single(allSegs);
        Assert.True(single.IsCombined);
        Assert.Equal("AB", single.Text);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("A\r\nB")]
    [InlineData("A\u2028B")]
    public void AllCombinationKeepsSourceWhitespaceAndIgnoredBreaks(string source)
    {
        var segment = Assert.Single(CssWritingModeProperties.SegmentTateChuYoko(source, CssWritingMode.VerticalRl, CssTextCombineUpright.All));
        Assert.True(segment.IsCombined); Assert.Equal(source, segment.Text);
        Assert.Equal(0, segment.StartIndex); Assert.Equal(source.Length, segment.Length);
    }

    [Theory]
    [InlineData("1\u0308")]
    [InlineData("1\ufe0f\u20e3")]
    public void DigitCompositionNeverSplitsATypographicCharacterUnit(string source)
    {
        var segments = CssWritingModeProperties.SegmentTateChuYoko(source, CssWritingMode.VerticalRl,
            new CssTextCombineUpright(CssTextCombineUprightKind.Digits, 2));
        Assert.DoesNotContain(segments, segment => segment.IsCombined);
        Assert.Equal(source, string.Concat(segments.Select(segment => segment.Text)));
    }

    [Fact]
    public void RubyLayoutComputesPositionAlignAndMergeInHorizontalAndVerticalWritingModes()
    {
        var pairs = new[]
        {
            new CssRubyAnnotationPair("東", "とう"),
            new CssRubyAnnotationPair("京", "きょう"),
        };

        // 1. Horizontal Over + Separate
        var horizOver = CssWritingModeProperties.ComputeRubyPlacements(
            pairs,
            baseEmSize: 16,
            CssWritingMode.HorizontalTb,
            CssRubyPosition.Over,
            CssRubyAlign.Center,
            CssRubyMerge.Separate);
        Assert.Equal(2, horizOver.Count);
        Assert.True(horizOver[0].AnnotationRect.Top < horizOver[0].BaseRect.Top);
        Assert.False(horizOver[0].IsMerged);

        // 2. Horizontal Under + Collapse (merged into 1 combined ruby span)
        var horizUnderMerged = CssWritingModeProperties.ComputeRubyPlacements(
            pairs,
            baseEmSize: 16,
            CssWritingMode.HorizontalTb,
            CssRubyPosition.Under,
            CssRubyAlign.SpaceAround,
            CssRubyMerge.Collapse);
        var merged = Assert.Single(horizUnderMerged);
        Assert.True(merged.IsMerged);
        Assert.Equal("東京", merged.BaseText);
        Assert.Equal("とうきょう", merged.AnnotationText);
        Assert.True(merged.AnnotationRect.Top >= merged.BaseRect.Bottom);

        // 3. Vertical-RL Over places annotation column to the right of the vertical base column
        var vertOver = CssWritingModeProperties.ComputeRubyPlacements(
            pairs,
            baseEmSize: 16,
            CssWritingMode.VerticalRl,
            CssRubyPosition.Over,
            CssRubyAlign.Start,
            CssRubyMerge.Separate);
        Assert.Equal(2, vertOver.Count);
        Assert.True(vertOver[0].AnnotationRect.Left >= vertOver[0].BaseRect.Right);
    }

    [Fact]
    public void AnchorNameScopeAndPositionAreaResolveAndPlaceElementsAroundAnchor()
    {
        var host = new Canvas { Width = 400, Height = 300 };
        var anchorBtn = new Border { Width = 100, Height = 40 };
        Canvas.SetLeft(anchorBtn, 140);
        Canvas.SetTop(anchorBtn, 100);
        Css.SetClass(anchorBtn, "trigger-anchor");

        var popover = new Border { Width = 120, Height = 50 };
        Css.SetClass(popover, "anchored-popover");

        // Second scoped subtree with same --menu-anchor name to verify anchor-scope isolation
        var scopedWrapper = new StackPanel();
        Css.SetClass(scopedWrapper, "scoped-branch");
        var scopedDuplicateAnchor = new Border { Width = 60, Height = 20 };
        Css.SetClass(scopedDuplicateAnchor, "trigger-anchor");
        scopedWrapper.Children.Add(scopedDuplicateAnchor);

        host.Children.Add(anchorBtn);
        host.Children.Add(scopedWrapper);
        host.Children.Add(popover);

        AddStyleSheet(host, """
            .trigger-anchor {
                anchor-name: --menu-anchor;
            }
            .scoped-branch {
                anchor-scope: --menu-anchor;
            }
            .anchored-popover {
                position: absolute;
                position-anchor: --menu-anchor;
                position-area: bottom span-right;
            }
            """);

        Layout(host, 400, 300);

        // ResolveAnchorElement must pick anchorBtn (140, 100, 100x40) and ignore scopedDuplicateAnchor inside .scoped-branch
        var resolved = CssAnchorPositioning.ResolveAnchorElement(popover, host);
        Assert.Same(anchorBtn, resolved);

        var layoutState = new CssLayoutState { Position = CssPositionMode.Absolute };
        var slot = CssAbsoluteLayout.ComputeSlot(
            layoutState,
            new Size(400, 300),
            new Size(120, 50),
            120,
            50,
            popover);

        // bottom span-right places popover starting at anchor.Left (140) and anchor.Bottom (140)
        Assert.Equal(140, slot.Left, 1);
        Assert.Equal(140, slot.Top, 1);
        Assert.Equal(120, slot.Width, 1);
        Assert.Equal(50, slot.Height, 1);
    }

    [Fact]
    public void AnchorAndAnchorSizeFunctionsEvaluateSidesPercentagesAndFallbacks()
    {
        var anchorRect = new Rect(100, 80, 120, 50);
        var containerSize = new Size(500, 400);

        Assert.True(CssAnchorPositioning.TryParseAnchorInset(1, "calc(anchor(--card bottom) + 8px)", out var topExpr));
        Assert.Equal("--card", topExpr.AnchorName);
        Assert.Equal(CssAnchorSide.Bottom, topExpr.Side);
        Assert.Equal(8.0, topExpr.OffsetPx);

        Assert.True(CssAnchorPositioning.TryEvaluateAnchorInset(
            topExpr,
            anchorRect,
            containerSize,
            CssWritingMode.HorizontalTb,
            rightToLeft: false,
            out var resolvedTop));
        Assert.Equal(138.0, resolvedTop, 1); // 80 + 50 + 8 = 138

        // Percentage side + missing-anchor fallback
        Assert.True(CssAnchorPositioning.TryParseAnchorInset(0, "anchor(--missing 50%, 42px)", out var fallbackExpr));
        Assert.True(CssAnchorPositioning.TryEvaluateAnchorInset(
            fallbackExpr,
            anchorRect: null,
            containerSize,
            CssWritingMode.HorizontalTb,
            rightToLeft: false,
            out var fallbackPx));
        Assert.Equal(42.0, fallbackPx, 1);

        // anchor-size(width) + calc offset
        Assert.True(CssAnchorPositioning.TryParseAnchorSize(
            CssAnchorSizeDimension.Width,
            "calc(anchor-size(--card width, 90px) + 16px)",
            out var sizeExpr));
        Assert.True(CssAnchorPositioning.TryEvaluateAnchorSize(
            sizeExpr,
            anchorRect,
            CssWritingMode.HorizontalTb,
            out var resolvedWidth));
        Assert.Equal(136.0, resolvedWidth, 1); // 120 + 16
    }

    [Fact]
    public void PositionTryFallbacksOrderAndPositionVisibilitySelectNonOverflowingCandidateOrHide()
    {
        var cssWithPositionTry = """
            @position-try --compact-top {
                position-area: top center;
                width: 110px;
                height: 36px;
            }
            .near-bottom-anchor {
                anchor-name: --edge-anchor;
            }
            .smart-tooltip {
                position: absolute;
                position-anchor: --edge-anchor;
                position-area: bottom center;
                position-try: most-height flip-block, --compact-top;
                position-visibility: anchors-valid no-overflow;
            }
            """;

        Assert.Equal(1, CssAnchorPositioning.ParseAndRegisterPositionTryRules(cssWithPositionTry));

        var host = new Canvas { Width = 320, Height = 220 };
        var anchor = new Border { Width = 80, Height = 30 };
        // Place anchor near bottom (Y=180..210) so a 50px tooltip at `bottom center` overflows 220px height
        Canvas.SetLeft(anchor, 120);
        Canvas.SetTop(anchor, 180);
        Css.SetClass(anchor, "near-bottom-anchor");

        var tooltip = new Border { Width = 120, Height = 50 };
        Css.SetClass(tooltip, "smart-tooltip");

        host.Children.Add(anchor);
        host.Children.Add(tooltip);

        // Compile selector rules after @position-try block
        var ruleStart = cssWithPositionTry.IndexOf(".near-bottom-anchor", StringComparison.Ordinal);
        AddStyleSheet(host, cssWithPositionTry[ruleStart..]);
        Layout(host, 320, 220);

        var placement = CssAnchorPositioning.EvaluatePlacement(
            tooltip,
            host,
            new CssLayoutState { Position = CssPositionMode.Absolute },
            new Size(320, 220),
            new Size(120, 50),
            120,
            50);

        // Primary `bottom center` would place tooltip at Y=210..260 (overflowing 220).
        // `flip-block` flips to `top center` (Y=130..180), which fits inside 220 and has the most height (180px above vs 10px below).
        Assert.True(placement.IsVisible);
        Assert.True(placement.SelectedCandidateIndex > 0);
        Assert.Equal(130, placement.Slot.Top, 1);
        Assert.False(CssAnchorPositioning.GetIsPositionHidden(tooltip));
    }

    [Fact]
    public void RenderT4AndE1WritingModesRubyAndAnchorVisualsInDualWindowAndDpi()
    {
        var outDir = Environment.GetEnvironmentVariable("JALIUM_T4_E1_SCREENSHOT_DIR")
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
                var visual = BuildShowcaseVisual(windowStyle);
                VisualTreeHelper.SetRootDpi(visual, new DpiScale(dpi, dpi));
                const int width = 860;
                const int height = 560;
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
                var nonZero = pixels.Count(static b => b != 0);
                Assert.True(nonZero > pixelWidth * pixelHeight, $"Expected rendered pixels for {modeName}-{suffix}");

                var path = Path.Combine(outDir, $"t4-e1-writing-modes-anchor-{modeName}-{suffix}.png");
                var bmp = BitmapSource.Create(pixelWidth, pixelHeight, 96.0 * dpi, 96.0 * dpi, PixelFormats.Bgra32, null, pixels, pixelWidth * 4);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bmp));
                using var stream = File.Create(path);
                encoder.Save(stream);
                Assert.True(new FileInfo(path).Length > 1024);
            }
        }
    }

    private static FrameworkElement BuildShowcaseVisual(WindowStyle windowStyle)
    {
        var root = new StackPanel
        {
            Background = new SolidColorBrush(Color.FromRgb(10, 14, 23)),
        };
        Css.SetStyle(root, "display: block; width: 860px; height: 560px; padding: 18px;");

        var header = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(19, 26, 41)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(45, 58, 79)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 10, 14, 10),
            Child = new TextBlock
            {
                Text = $"T4 Writing Modes, Ruby Layout & E1 Anchor Positioning — Mode: {(windowStyle == WindowStyle.None ? "WithoutBorder" : "Native Chrome")}",
                Foreground = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                FontSize = 13,
                FontWeight = FontWeights.Bold,
            },
        };
        Css.SetStyle(header, "display: block; margin-bottom: 14px; border-radius: 8px;");
        root.Children.Add(header);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };

        // Card 1: T4 Vertical Writing Modes (vertical-rl, vertical-lr, sideways-lr) & Tate-chu-yoko
        var card1 = new StackPanel
        {
            Width = 265,
            Height = 465,
            Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
            Spacing = 10,
        };
        Css.SetStyle(card1, "border: 1px solid #334155; border-radius: 10px; padding: 12px;");
        card1.Children.Add(new TextBlock
        {
            Text = "T4.1 Vertical Writing Modes & Tate-chu-yoko",
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
        });

        var columnsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var (modeLabel, sampleText, badgeText, accentHex) in new[]
        {
            ("vertical-rl", "縦書組版・令和08年", "upright / digits 2", Color.FromRgb(56, 189, 248)),
            ("vertical-lr", "論理軸配置・26日", "mixed / inline-start", Color.FromRgb(52, 211, 153)),
            ("sideways-lr", "SIDEWAYS-90", "sideways-lr axis", Color.FromRgb(251, 146, 60)),
        })
        {
            var colCard = new Border
            {
                Width = 74,
                Height = 395,
                Background = new SolidColorBrush(Color.FromRgb(23, 33, 53)),
                BorderBrush = new SolidColorBrush(accentHex),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6),
            };
            Css.SetStyle(colCard, "border-radius: 6px;");
            var colStack = new StackPanel { Spacing = 6 };
            colStack.Children.Add(new TextBlock
            {
                Text = modeLabel,
                Foreground = new SolidColorBrush(accentHex),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
            });
            colStack.Children.Add(new TextBlock
            {
                Text = sampleText,
                Foreground = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
            });
            colStack.Children.Add(new TextBlock
            {
                Text = badgeText,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                FontSize = 9,
            });
            colCard.Child = colStack;
            columnsRow.Children.Add(colCard);
        }

        card1.Children.Add(columnsRow);
        row.Children.Add(card1);

        // Card 2: T4 Ruby Layout (ruby-position: over / under / inter-character, ruby-align, ruby-merge)
        var card2 = new StackPanel
        {
            Width = 265,
            Height = 465,
            Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
            Spacing = 10,
        };
        Css.SetStyle(card2, "border: 1px solid #334155; border-radius: 10px; padding: 12px;");
        card2.Children.Add(new TextBlock
        {
            Text = "T4.2 Ruby Annotations (Over / Under / Merge)",
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
        });

        foreach (var (rtText, rbText, metaLabel, rtColor) in new[]
        {
            ("とう  きょう", "東    京", "ruby-position: over | ruby-align: space-around", Color.FromRgb(56, 189, 248)),
            ("しんかんせん", "新 幹 線", "ruby-merge: merge | ruby-align: center", Color.FromRgb(52, 211, 153)),
            ("にほんごくみはん", "日本語組版", "ruby-position: under | ruby-align: space-between", Color.FromRgb(244, 114, 182)),
            ("BOPOMOFO", "注音標記", "ruby-position: inter-character | vertical-rl", Color.FromRgb(250, 204, 21)),
        })
        {
            var rubyBox = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(23, 33, 53)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(45, 58, 79)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 8, 10, 8),
            };
            Css.SetStyle(rubyBox, "border-radius: 6px;");
            var rubyStack = new StackPanel { Spacing = 3 };
            rubyStack.Children.Add(new TextBlock
            {
                Text = rtText,
                Foreground = new SolidColorBrush(rtColor),
                FontSize = 10,
            });
            rubyStack.Children.Add(new TextBlock
            {
                Text = rbText,
                Foreground = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                FontSize = 16,
                FontWeight = FontWeights.Bold,
            });
            rubyStack.Children.Add(new TextBlock
            {
                Text = metaLabel,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                FontSize = 9,
            });
            rubyBox.Child = rubyStack;
            card2.Children.Add(rubyBox);
        }

        row.Children.Add(card2);

        // Card 3: E1 Anchor Positioning (anchor-name, position-area, anchor(), anchor-size(), flip-block)
        var card3 = new StackPanel
        {
            Width = 265,
            Height = 465,
            Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
            Spacing = 10,
        };
        Css.SetStyle(card3, "border: 1px solid #334155; border-radius: 10px; padding: 12px;");
        card3.Children.Add(new TextBlock
        {
            Text = "E1 Anchor Positioning & @position-try Fallbacks",
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
        });

        var stage = new Canvas
        {
            Width = 240,
            Height = 400,
            Background = new SolidColorBrush(Color.FromRgb(19, 28, 46)),
        };

        var primaryAnchor = new Border
        {
            Width = 116,
            Height = 38,
            Background = new SolidColorBrush(Color.FromRgb(14, 116, 144)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 8, 8, 8),
            Child = new TextBlock
            {
                Text = "--primary-anchor",
                Foreground = Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
            },
        };
        Canvas.SetLeft(primaryAnchor, 62);
        Canvas.SetTop(primaryAnchor, 145);

        var topCallout = new Border
        {
            Width = 160,
            Height = 46,
            Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(52, 211, 153)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6, 8, 6),
            Child = new TextBlock
            {
                Text = "position-area: top center\nflip-block fallback ready",
                Foreground = new SolidColorBrush(Color.FromRgb(167, 243, 208)),
                FontSize = 10,
            },
        };
        Canvas.SetLeft(topCallout, 40);
        Canvas.SetTop(topCallout, 86);

        var bottomPopover = new Border
        {
            Width = 176,
            Height = 64,
            Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6, 8, 6),
            Child = new TextBlock
            {
                Text = "top: calc(anchor(bottom) + 8px)\nwidth: anchor-size(width)\nposition-visibility: no-overflow",
                Foreground = new SolidColorBrush(Color.FromRgb(186, 230, 253)),
                FontSize = 10,
            },
        };
        Canvas.SetLeft(bottomPopover, 32);
        Canvas.SetTop(bottomPopover, 195);

        var edgeBadge = new Border
        {
            Width = 176,
            Height = 52,
            Background = new SolidColorBrush(Color.FromRgb(39, 39, 42)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(251, 146, 60)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6, 8, 6),
            Child = new TextBlock
            {
                Text = "@position-try --flip-up\nposition-try-order: most-height",
                Foreground = new SolidColorBrush(Color.FromRgb(254, 215, 170)),
                FontSize = 10,
            },
        };
        Canvas.SetLeft(edgeBadge, 32);
        Canvas.SetTop(edgeBadge, 285);

        stage.Children.Add(topCallout);
        stage.Children.Add(primaryAnchor);
        stage.Children.Add(bottomPopover);
        stage.Children.Add(edgeBadge);
        card3.Children.Add(stage);

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

    private static void Layout(FrameworkElement root, int width, int height)
    {
        for (var pass = 0; pass < 4; pass++)
        {
            CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
            root.Measure(new Size(width, height));
            root.Arrange(new Rect(0, 0, width, height));
            root.UpdateLayout();
        }
    }
}

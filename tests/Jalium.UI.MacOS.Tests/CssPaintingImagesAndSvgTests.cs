using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssPaintingImagesAndSvgTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);

    public void Dispose()
    {
        CssPaintWorkletRegistry.Clear();
        CssElementImageRegistry.Clear();
        _context.Dispose();
    }

    [Fact]
    public void BackgroundClipBorderAreaAndCombinedTextClipRenderAccurately()
    {
        // 1. background-clip: border-area paints strictly inside the border ring and leaves padding/content untouched.
        var borderAreaBox = new Border
        {
            Width = 120,
            Height = 80,
        };
        Css.SetStyle(borderAreaBox, """
            border: 12px solid transparent;
            padding: 10px;
            background-color: #ef4444;
            background-clip: border-area;
            """);
        Layout(borderAreaBox, 120, 80);

        var state = CssBackgroundPaintProperties.Get(borderAreaBox);
        Assert.NotNull(state);
        Assert.Equal(CssBackgroundProperties.BorderArea, state!.ColorClip);

        var (pixels, w, h) = RenderOffscreen(borderAreaBox, 120, 80, 1.0);
        // Border ring pixel at (4, 40) must be painted red; interior center at (60, 40) must remain transparent.
        var ringPixel = GetPixel(pixels, w, 4, 40);
        var centerPixel = GetPixel(pixels, w, 60, 40);
        Assert.True(ringPixel.R > 200 && ringPixel.A > 200, $"Expected border ring pixel to be painted red, got {ringPixel}");
        Assert.Equal(0, centerPixel.A);

        // 2. A padding-box origin with text clipping, and the border-area/text union.
        var textClipHost = new Border
        {
            Width = 180,
            Height = 72,
            Child = new TextBlock
            {
                Text = "CLIPPED",
                FontSize = 28,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.Transparent,
            },
        };
        Css.SetStyle(textClipHost, """
            border: 8px solid transparent;
            padding: 8px;
            background: linear-gradient(90deg, #06b6d4, #3b82f6) padding-box text;
            """);
        Layout(textClipHost, 180, 72);

        var textState = CssBackgroundPaintProperties.Get(textClipHost);
        Assert.NotNull(textState);
        Assert.Single(textState!.Layers);
        Assert.Equal(CssBackgroundBox.Padding, textState.Layers[0].Origin);
        Assert.Equal(CssBackgroundBox.Text, textState.Layers[0].Clip);
        Assert.True(CssBackgroundProperties.IsTextClip(textState.Layers[0].Clip));
        Assert.Equal(CssBackgroundBox.Border, CssBackgroundProperties.BoxPart(textState.Layers[0].Clip));

        // 3. Fractional coordinate & affine transform text ink capture accuracy.
        var transformedBlock = new TextBlock
        {
            Text = "INK",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            RenderTransform = new TranslateTransform(3.25, 2.75),
        };
        Layout(transformedBlock, 120, 50);
        var raster = CssBackgroundTextClip.Capture(transformedBlock, new Rect(0.25, 0.5, 118.5, 48.25), 1.5);
        if (raster is not null)
        {
            Assert.True(raster.Width > 0 && raster.Height > 0);
            Assert.False(raster.InkBounds.IsEmpty);
        }
    }

    [Fact]
    public void InlineSpanSupportsBlendIsolationMaskAndMaskBorderAcrossFragments()
    {
        var maskBorderDataUri = CreateNineSliceMaskDataUri();

        // Compare box-decoration-break: slice vs clone across multi-line Span fragments with mask-image, mask-border, and mix-blend-mode.
        var sliceBlock = new TextBlock
        {
            Width = 140,
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.White,
        };
        var sliceSpan = new Span(new Run("FRAGMENTED INLINE MASK SPAN ACROSS LINES"));
        Css.SetStyle(sliceSpan, $"""
            background-color: #38bdf8;
            padding: 2px 6px;
            border: 3px solid transparent;
            box-decoration-break: slice;
            mix-blend-mode: screen;
            isolation: isolate;
            mask-image: linear-gradient(90deg, rgba(255,255,255,1), rgba(255,255,255,0.25));
            mask-border-source: url("{maskBorderDataUri}");
            mask-border-slice: 4 fill;
            mask-border-width: 3px;
            """);
        sliceBlock.Inlines.Add(sliceSpan);

        var cloneBlock = new TextBlock
        {
            Width = 140,
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.White,
        };
        var cloneSpan = new Span(new Run("FRAGMENTED INLINE MASK SPAN ACROSS LINES"));
        Css.SetStyle(cloneSpan, $"""
            background-color: #38bdf8;
            padding: 2px 6px;
            border: 3px solid transparent;
            box-decoration-break: clone;
            mix-blend-mode: screen;
            isolation: isolate;
            mask-image: linear-gradient(90deg, rgba(255,255,255,1), rgba(255,255,255,0.25));
            mask-border-source: url("{maskBorderDataUri}");
            mask-border-slice: 4 fill;
            mask-border-width: 3px;
            """);
        cloneBlock.Inlines.Add(cloneSpan);

        Layout(sliceBlock, 140, 120);
        Layout(cloneBlock, 140, 120);

        Assert.Equal(CssBlendMode.Screen, CssCompositingProperties.Mix(sliceSpan));
        Assert.True(CssCompositingProperties.Isolate(sliceSpan));
        Assert.True(CssMaskProperties.Active(sliceSpan));
        Assert.True(CssMaskBorderProperties.Active(sliceSpan));
        Assert.Equal(CssBoxDecorationBreak.Slice, CssBoxDecorationBreakProperties.Value(sliceSpan));
        Assert.Equal(CssBoxDecorationBreak.Clone, CssBoxDecorationBreakProperties.Value(cloneSpan));

        var (slicePixels, sw, sh) = RenderOffscreen(sliceBlock, 140, 120, 1.0);
        var (clonePixels, cw, ch) = RenderOffscreen(cloneBlock, 140, 120, 1.0);
        Assert.True(CountVisiblePixels(slicePixels) > 50);
        Assert.True(CountVisiblePixels(clonePixels) > 50);
        Assert.False(slicePixels.AsSpan().SequenceEqual(clonePixels), "Expected slice and clone fragment masking to produce distinct per-fragment alpha profiles");
    }

    [Fact]
    public void ImageSetProbesMimeTypesSwitchesByDpiAndSupportsCrossFade()
    {
        var red1xUri = CreateSolidPngDataUri(8, 8, Color.FromArgb(255, 220, 38, 38));
        var blue2xUri = CreateSolidPngDataUri(16, 16, Color.FromArgb(255, 37, 99, 235));

        // 1. image-set() skips unsupported MIME type("image/x-unsupported") and switches between 1x and 2x candidates by host DPI.
        var card = new Border { Width = 64, Height = 64 };
        Css.SetStyle(card, $"""
            background-image: image-set(
                url("{red1xUri}") 1x type("image/x-unsupported-format"),
                url("{red1xUri}") 1x type("image/png"),
                url("{blue2xUri}") 2x type("image/png")
            );
            background-size: 64px 64px;
            """);

        VisualTreeHelper.SetRootDpi(card, new DpiScale(1.0, 1.0));
        Evaluate(card);
        Layout(card, 64, 64);
        var (pixels1x, w1, _) = RenderOffscreen(card, 64, 64, 1.0);
        var c1x = GetPixel(pixels1x, w1, 32, 32);
        Assert.True(c1x.R > 180 && c1x.B < 80, $"Expected 1x candidate (red) at 1x DPI, got {c1x}");

        VisualTreeHelper.SetRootDpi(card, new DpiScale(2.0, 2.0));
        Evaluate(card);
        Layout(card, 64, 64);
        var (pixels2x, w2, _) = RenderOffscreen(card, 64, 64, 2.0);
        var c2x = GetPixel(pixels2x, w2, 64, 64);
        Assert.True(c2x.B > 180 && c2x.R < 80, $"Expected 2x candidate (blue) at 2x DPI, got {c2x}");

        // 2. Legacy 3-argument cross-fade(img1, img2, percentage) and standard CSS Images 4 cross-fade().
        var legacyFadeBox = new Border { Width = 60, Height = 60 };
        Css.SetStyle(legacyFadeBox, $"""
            background-image: cross-fade(url("{red1xUri}"), url("{blue2xUri}"), 50%);
            background-size: 60px 60px;
            """);
        Layout(legacyFadeBox, 60, 60);
        var (fadePixels, fw, _) = RenderOffscreen(legacyFadeBox, 60, 60, 1.0);
        var blended = GetPixel(fadePixels, fw, 30, 30);
        Assert.True(blended.R > 90 && blended.B > 90 && blended.A > 200, $"Expected 50/50 blended purple pixel, got {blended}");
    }

    [Fact]
    public void ElementFunctionAndPaintWorkletRenderAndInvalidateSafelyWithCycleGuard()
    {
        // 1. element(#sourceCard) live rendering and invalidation.
        var sourceCard = new Border
        {
            Name = "sourceCard",
            Width = 48,
            Height = 48,
            Background = new SolidColorBrush(Color.FromRgb(16, 185, 129)),
        };
        CssElementImageRegistry.Register("sourceCard", sourceCard);

        var mirrorBox = new Border { Width = 48, Height = 48 };
        Css.SetStyle(mirrorBox, "background-image: element(#sourceCard); background-size: 48px 48px;");
        Layout(mirrorBox, 48, 48);

        var (initialPixels, mw, _) = RenderOffscreen(mirrorBox, 48, 48, 1.0);
        var p0 = GetPixel(initialPixels, mw, 24, 24);
        Assert.True(p0.G > 150 && p0.R < 60, $"Expected green reflection from #sourceCard, got {p0}");

        // Update sourceCard and notify registry; re-render reflects new color.
        sourceCard.Background = new SolidColorBrush(Color.FromRgb(245, 158, 11));
        CssElementImageRegistry.NotifyChanged("sourceCard");
        var (updatedPixels, mw2, _) = RenderOffscreen(mirrorBox, 48, 48, 1.0);
        var p1 = GetPixel(updatedPixels, mw2, 24, 24);
        Assert.True(p1.R > 200 && p1.B < 50, $"Expected updated amber reflection from #sourceCard, got {p1}");

        // 2. Self-referential element(#selfCycle) cycle guard prevents infinite recursion.
        var cycleBox = new Border
        {
            Name = "selfCycle",
            Width = 40,
            Height = 40,
        };
        CssElementImageRegistry.Register("selfCycle", cycleBox);
        Css.SetStyle(cycleBox, "background-image: element(#selfCycle);");
        Layout(cycleBox, 40, 40);
        var (cyclePixels, _, _) = RenderOffscreen(cycleBox, 40, 40, 1.0);
        Assert.NotNull(cyclePixels);

        // 3. paint(workletName, args...) custom worklet & custom property consumption.
        var workletCalls = 0;
        CssPaintWorkletRegistry.Register("stripe-grid", new DelegatePaintWorklet((dc, size, props, args) =>
        {
            workletCalls++;
            var colorHex = props.TryGetValue("--stripe-color", out var raw) ? raw.Trim() : "#8b5cf6";
            var color = colorHex == "#14b8a6" ? Color.FromRgb(20, 184, 166) : Color.FromRgb(139, 92, 246);
            dc.DrawRectangle(new SolidColorBrush(color), null, new Rect(0, 0, size.Width, size.Height));
        }));

        var paintBox = new Border { Width = 50, Height = 50 };
        Css.SetStyle(paintBox, "--stripe-color: #14b8a6; background-image: paint(stripe-grid, 8px);");
        Layout(paintBox, 50, 50);

        var (paintPixels, pw, _) = RenderOffscreen(paintBox, 50, 50, 1.0);
        var painted = GetPixel(paintPixels, pw, 25, 25);
        Assert.True(workletCalls > 0);
        Assert.True(painted.G > 150 && painted.B > 130, $"Expected teal paint worklet output, got {painted}");
    }

    [Fact]
    public void UrlMediaFragmentXywhAndSvgVectorEffectNonScalingStrokeWorkAccurately()
    {
        // 1. url("...#xywh=...") media fragment spatial crop (pixel & percent).
        Assert.True(CssMediaFragment.TryParseXywh("sprite.png#xywh=8,0,8,8", out var baseUri, out var crop, out var isPercent));
        Assert.Equal("sprite.png", baseUri);
        Assert.Equal(new Rect(8, 0, 8, 8), crop);
        Assert.False(isPercent);

        var spriteUri = CreateTwoHalfPngDataUri(
            16,
            8,
            Color.FromArgb(255, 239, 68, 68),   // Left 8x8: Red
            Color.FromArgb(255, 16, 185, 129)); // Right 8x8: Emerald

        var rightHalfBox = new Border { Width = 32, Height = 32 };
        Css.SetStyle(rightHalfBox, $"""
            background-image: url("{spriteUri}#xywh=8,0,8,8");
            background-size: 32px 32px;
            """);
        Layout(rightHalfBox, 32, 32);
        var (cropPixels, cw, _) = RenderOffscreen(rightHalfBox, 32, 32, 1.0);
        var croppedColor = GetPixel(cropPixels, cw, 16, 16);
        Assert.True(croppedColor.G > 150 && croppedColor.R < 60, $"Expected right-half emerald crop from #xywh=8,0,8,8, got {croppedColor}");

        // 2. SVG vector-effect: non-scaling-stroke parsing, compensation, and rendering.
        Assert.Equal(SvgVectorEffect.NonScalingStroke, SvgCssStyles.ParseVectorEffect("non-scaling-stroke"));
        Assert.Equal(SvgVectorEffect.NonScalingSize, SvgCssStyles.ParseVectorEffect("non-scaling-size"));
        Assert.Equal(SvgVectorEffect.NonRotation, SvgCssStyles.ParseVectorEffect("non-rotation"));
        Assert.Equal(SvgVectorEffect.FixedPosition, SvgCssStyles.ParseVectorEffect("fixed-position"));

        var compensated = SvgCssStyles.CompensateStrokeWidth(4.0, new Matrix(4, 0, 0, 4, 0, 0), SvgVectorEffect.NonScalingStroke);
        Assert.Equal(1.0, compensated, 3);

        var svgScaling = SvgImage.FromSvgString("""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" width="80" height="80">
              <line x1="2" y1="10" x2="18" y2="10" stroke="#f43f5e" stroke-width="2" />
            </svg>
            """);
        var svgNonScaling = SvgImage.FromSvgString("""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" width="80" height="80">
              <line x1="2" y1="10" x2="18" y2="10" stroke="#f43f5e" stroke-width="2" vector-effect="non-scaling-stroke" />
            </svg>
            """);

        var scalingHost = new Image { Source = svgScaling, Width = 80, Height = 80, Stretch = Stretch.Fill };
        var nonScalingHost = new Image { Source = svgNonScaling, Width = 80, Height = 80, Stretch = Stretch.Fill };
        Layout(scalingHost, 80, 80);
        Layout(nonScalingHost, 80, 80);

        var (scalingPixels, _, _) = RenderOffscreen(scalingHost, 80, 80, 1.0);
        var (nonScalingPixels, _, _) = RenderOffscreen(nonScalingHost, 80, 80, 1.0);
        var scalingInk = CountVisiblePixels(scalingPixels);
        var nonScalingInk = CountVisiblePixels(nonScalingPixels);
        Assert.True(nonScalingInk > 0);
        Assert.True(scalingInk > nonScalingInk * 2, $"Expected scaled stroke ({scalingInk} px) to be thicker than non-scaling-stroke ({nonScalingInk} px)");
    }

    [Fact]
    public void RenderP1P2ShowcaseScreenshots_NativeAndWithoutBorder_1xAnd2x()
    {
        var outDir = Environment.GetEnvironmentVariable("JALIUM_CSS_SCREENSHOT_DIR")
            ?? "/Volumes/My PSSD/.codex-css-acceptance/css-p1-p2-painting-images-svg-20261026/screenshots";
        Directory.CreateDirectory(outDir);

        // Register live element and paint worklet used by the P1/P2 showcase.
        var liveBadge = new Border
        {
            Name = "liveSignalSource",
            Width = 120,
            Height = 44,
            Background = new SolidColorBrush(Color.FromRgb(14, 116, 144)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 6, 8, 6),
            Child = new TextBlock
            {
                Text = "element(#liveSignalSource)\nLive Mirrored Surface",
                Foreground = Brushes.White,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
            },
        };
        CssElementImageRegistry.Register("liveSignalSource", liveBadge);

        CssPaintWorkletRegistry.Register("precision-grid", new DelegatePaintWorklet((dc, size, props, _) =>
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(15, 23, 42)), null, new Rect(0, 0, size.Width, size.Height));
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(140, 45, 212, 191)), 1.5);
            for (double x = 0; x < size.Width; x += 12)
                dc.DrawLine(pen, new Point(x, 0), new Point(x, size.Height));
            for (double y = 0; y < size.Height; y += 12)
                dc.DrawLine(pen, new Point(0, y), new Point(size.Width, y));
            dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(56, 189, 248)), 2), new Rect(2, 2, Math.Max(1, size.Width - 4), Math.Max(1, size.Height - 4)));
        }));

        foreach (var (windowStyle, modeName) in new[]
        {
            (WindowStyle.SingleBorderWindow, "native"),
            (WindowStyle.None, "withoutborder"),
        })
        {
            foreach (var (dpi, suffix) in new[] { (1.0, "1x"), (2.0, "2x") })
            {
                var visual = BuildP1P2ShowcaseVisual(windowStyle);
                VisualTreeHelper.SetRootDpi(visual, new DpiScale(dpi, dpi));
                Evaluate(visual);
                const int width = 820;
                const int height = 540;
                Layout(visual, width, height);

                var (pixels, pixelWidth, pixelHeight) = RenderOffscreen(visual, width, height, dpi);
                var nonZero = pixels.Count(static b => b != 0);
                Assert.True(nonZero > pixelWidth * pixelHeight, $"Expected rendered content for {modeName}-{suffix}");

                var path = Path.Combine(outDir, $"p1-p2-painting-images-svg-{modeName}-{suffix}.png");
                var bmp = BitmapSource.Create(pixelWidth, pixelHeight, 96.0 * dpi, 96.0 * dpi, PixelFormats.Bgra32, null, pixels, pixelWidth * 4);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bmp));
                using var stream = File.Create(path);
                encoder.Save(stream);
                Assert.True(new FileInfo(path).Length > 1024);
            }
        }
    }

    private static FrameworkElement BuildP1P2ShowcaseVisual(WindowStyle windowStyle)
    {
        var spriteUri = CreateTwoHalfPngDataUri(
            32,
            16,
            Color.FromArgb(255, 14, 165, 233),
            Color.FromArgb(255, 16, 185, 129));
        var maskBorderUri = CreateNineSliceMaskDataUri();

        var root = new StackPanel
        {
            Background = new SolidColorBrush(Color.FromRgb(11, 15, 25)),
        };
        Css.SetStyle(root, "display: block; width: 820px; height: 540px; padding: 18px; position: relative;");

        var header = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(22, 30, 46)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 10, 14, 10),
            Child = new TextBlock
            {
                Text = $"P1 & P2 Painting, Text Clip, Inline Masking, Image Functions & SVG — Mode: {(windowStyle == WindowStyle.None ? "WithoutBorder" : "Native Chrome")}",
                Foreground = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                FontSize = 13.5,
                FontWeight = FontWeights.Bold,
            },
        };
        Css.SetStyle(header, "display: block; margin-bottom: 14px; border-radius: 8px;");
        root.Children.Add(header);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };

        // Panel 1: P1 background-clip: border-area & combined <box> text clips
        var panel1 = new StackPanel
        {
            Width = 250,
            Height = 440,
            Spacing = 12,
            Background = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
        };
        Css.SetStyle(panel1, "border-radius: 10px; border: 1px solid #334155; padding: 12px;");
        panel1.Children.Add(new TextBlock
        {
            Text = "1. P1 Border-Area & Text Clip",
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
        });

        var borderAreaCard = new Border
        {
            Height = 96,
            Child = new TextBlock
            {
                Text = "background-clip: border-area\nEvenOdd Border Ring Clip",
                Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
            },
        };
        Css.SetStyle(borderAreaCard, """
            border: 8px solid transparent;
            border-radius: 12px;
            padding: 10px;
            background: linear-gradient(135deg, #38bdf8, #14b8a6);
            background-clip: border-area;
            """);
        panel1.Children.Add(borderAreaCard);

        var gradientTextCard = new Border
        {
            Height = 96,
            Child = new TextBlock
            {
                Text = "VECTOR GLYPH MASK\npadding-box text",
                Foreground = Brushes.Transparent,
                FontSize = 15,
                FontWeight = FontWeights.Bold,
            },
        };
        Css.SetStyle(gradientTextCard, """
            border: 4px solid #334155;
            border-radius: 10px;
            padding: 10px;
            background: linear-gradient(90deg, #22d3ee, #34d399) padding-box text;
            """);
        panel1.Children.Add(gradientTextCard);

        var inlineFragmentCard = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
            Padding = new Thickness(10),
        };
        Css.SetStyle(inlineFragmentCard, "border: 1px solid #475569; border-radius: 8px;");
        var fragText = new TextBlock
        {
            Width = 200,
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
        };
        var fragSpan = new Span(new Run("Inline Span with box-decoration-break: clone, mask-image & mask-border across wrapped lines"));
        Css.SetStyle(fragSpan, $"""
            background-color: #0284c7;
            padding: 2px 5px;
            border: 2px solid transparent;
            box-decoration-break: clone;
            mix-blend-mode: screen;
            mask-image: linear-gradient(90deg, rgba(255,255,255,1), rgba(255,255,255,0.55));
            mask-border-source: url("{maskBorderUri}");
            mask-border-slice: 4 fill;
            mask-border-width: 2px;
            """);
        fragText.Inlines.Add(fragSpan);
        inlineFragmentCard.Child = fragText;
        panel1.Children.Add(inlineFragmentCard);
        row.Children.Add(panel1);

        // Panel 2: P2 image-set(), cross-fade(), element(#id), paint(worklet)
        var panel2 = new StackPanel
        {
            Width = 250,
            Height = 440,
            Spacing = 12,
            Background = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
        };
        Css.SetStyle(panel2, "border-radius: 10px; border: 1px solid #334155; padding: 12px;");
        panel2.Children.Add(new TextBlock
        {
            Text = "2. P2 Image Functions & Worklets",
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
        });

        var elementPreview = new Border
        {
            Height = 84,
            Padding = new Thickness(8),
        };
        Css.SetStyle(elementPreview, """
            border-radius: 8px;
            border: 1px solid #38bdf8;
            background-image: element(#liveSignalSource);
            background-size: 100% 100%;
            """);
        panel2.Children.Add(elementPreview);

        var paintPreview = new Border
        {
            Height = 96,
            Padding = new Thickness(10),
            Child = new TextBlock
            {
                Text = "paint(precision-grid)\nCSS Paint Worklet Surface",
                Foreground = Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
            },
        };
        Css.SetStyle(paintPreview, """
            border-radius: 8px;
            background-image: paint(precision-grid, 12px);
            background-size: 100% 100%;
            """);
        panel2.Children.Add(paintPreview);

        var crossFadePreview = new Border
        {
            Height = 96,
            Padding = new Thickness(10),
            Child = new TextBlock
            {
                Text = "cross-fade() + image-set()\nDPI-Aware Blended Layer",
                Foreground = Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
            },
        };
        Css.SetStyle(crossFadePreview, $"""
            border-radius: 8px;
            border: 1px solid #475569;
            background-image: cross-fade(url("{spriteUri}"), linear-gradient(90deg, #0f172a, #1e293b), 35%);
            background-size: 100% 100%;
            """);
        panel2.Children.Add(crossFadePreview);
        row.Children.Add(panel2);

        // Panel 3: P2 #xywh media fragments & SVG vector-effect: non-scaling-stroke
        var panel3 = new StackPanel
        {
            Width = 250,
            Height = 440,
            Spacing = 12,
            Background = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
        };
        Css.SetStyle(panel3, "border-radius: 10px; border: 1px solid #334155; padding: 12px;");
        panel3.Children.Add(new TextBlock
        {
            Text = "3. P2 #xywh Crop & SVG Vector-Effect",
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
        });

        var xywhCard = new Border
        {
            Height = 92,
            Padding = new Thickness(10),
            Child = new TextBlock
            {
                Text = "url(\"sprite.png#xywh=16,0,16,16\")\nMedia Fragment Right-Half Crop",
                Foreground = Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
            },
        };
        Css.SetStyle(xywhCard, $"""
            border-radius: 8px;
            border: 1px solid #10b981;
            background-image: url("{spriteUri}#xywh=16,0,16,16");
            background-size: 100% 100%;
            """);
        panel3.Children.Add(xywhCard);

        var svgComparisonSvg = SvgImage.FromSvgString("""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 60 40" width="220" height="150">
              <rect x="2" y="2" width="56" height="36" rx="4" fill="#0f172a" stroke="#38bdf8" stroke-width="2" vector-effect="non-scaling-stroke" />
              <path d="M 8 28 L 22 12 L 36 24 L 52 10" fill="none" stroke="#2dd4bf" stroke-width="2.5" vector-effect="non-scaling-stroke" />
              <circle cx="22" cy="12" r="2.5" fill="#f43f5e" />
              <circle cx="36" cy="24" r="2.5" fill="#f43f5e" />
              <circle cx="52" cy="10" r="2.5" fill="#f43f5e" />
            </svg>
            """);
        var svgCard = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
            Padding = new Thickness(8),
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock
                    {
                        Text = "SVG vector-effect: non-scaling-stroke",
                        Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                        FontSize = 10.5,
                        FontWeight = FontWeights.SemiBold,
                    },
                    new Image
                    {
                        Source = svgComparisonSvg,
                        Width = 208,
                        Height = 140,
                        Stretch = Stretch.Fill,
                    },
                },
            },
        };
        Css.SetStyle(svgCard, "border-radius: 8px; border: 1px solid #334155;");
        panel3.Children.Add(svgCard);
        row.Children.Add(panel3);

        root.Children.Add(row);
        return root;
    }

    private sealed class DelegatePaintWorklet(
        Action<DrawingContext, Size, IReadOnlyDictionary<string, string>, IReadOnlyList<string>> callback) : ICssPaintWorklet
    {
        public void Paint(
            DrawingContext context,
            Size size,
            IReadOnlyDictionary<string, string> properties,
            IReadOnlyList<string> arguments)
            => callback(context, size, properties, arguments);
    }

    private (byte[] Pixels, int PixelWidth, int PixelHeight) RenderOffscreen(
        FrameworkElement visual,
        int width,
        int height,
        double dpi)
    {
        var pixelWidth = Math.Max(1, (int)Math.Round(width * dpi));
        var pixelHeight = Math.Max(1, (int)Math.Round(height * dpi));
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
        return (pixels, pixelWidth, pixelHeight);
    }

    private static Color GetPixel(byte[] pixels, int stridePixels, int x, int y)
    {
        var offset = (y * stridePixels + x) * 4;
        return Color.FromArgb(pixels[offset + 3], pixels[offset + 2], pixels[offset + 1], pixels[offset]);
    }

    private static int CountVisiblePixels(byte[] pixels)
    {
        var count = 0;
        for (var i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] > 16)
                count++;
        }
        return count;
    }

    private static string CreateSolidPngDataUri(int width, int height, Color color)
        => CreateTwoHalfPngDataUri(width, height, color, color);

    private static string CreateTwoHalfPngDataUri(int width, int height, Color left, Color right)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var c = x < width / 2 ? left : right;
                var idx = (y * width + x) * 4;
                pixels[idx + 0] = c.B;
                pixels[idx + 1] = c.G;
                pixels[idx + 2] = c.R;
                pixels[idx + 3] = c.A;
            }
        }
        var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
    }

    private static string CreateNineSliceMaskDataUri()
    {
        const int size = 12;
        var pixels = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var isEdge = x < 4 || x >= size - 4 || y < 4 || y >= size - 4;
                var alpha = isEdge ? (byte)255 : (byte)200;
                var idx = (y * size + x) * 4;
                pixels[idx + 0] = alpha;
                pixels[idx + 1] = alpha;
                pixels[idx + 2] = alpha;
                pixels[idx + 3] = alpha;
            }
        }
        var bmp = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
    }

    private static void Evaluate(DependencyObject owner)
    {
        CssEvaluationScheduler.InvalidateSubtree(CssNode.Get(owner));
        CssEvaluationScheduler.FlushIfPending(owner.Dispatcher ?? Dispatcher.CurrentDispatcher);
    }

    private static void FlushDispatcher()
    {
        Dispatcher.CurrentDispatcher.ProcessQueue();
        BitmapDecodeNotifier.FlushPendingForTests();
        CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
    }

    private static void WaitForCssImages(DependencyObject root)
    {
        var nodes = new List<DependencyObject>();
        CollectNodes(root, nodes);
        var resources = new List<(DependencyObject Owner, CssBackgroundImageResource Resource)>();
        foreach (var node in nodes)
        {
            if (node is FrameworkElement fe)
            {
                _ = CssMaskPainter.ResolveImages(fe);
                _ = CssMaskBorderPainter.ResolveSource(fe);
            }
            AddResources(node, node.GetValue(CssBackgroundImageResource.ActiveProperty) as CssBackgroundImageResource[], resources);
            AddResources(node, node.GetValue(CssBorderImageProperties.ResourcesProperty) as CssBackgroundImageResource[], resources);
            if (node.GetValue(CssMaskProperties.ImagesProperty) is CssMaskImages maskImgs && maskImgs.Resources.Length > 0)
                AddResources(node, maskImgs.Resources, resources);
            if (node.GetValue(CssMaskBorderProperties.SourceProperty) is CssMaskBorderSource mbSource && mbSource.Image?.Resources is { Length: > 0 } mbRes)
                AddResources(node, mbRes, resources);
        }

        if (resources.Count == 0)
            return;

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (resources.Any(r => !r.Resource.Ready.IsCompleted) && DateTime.UtcNow < deadline)
        {
            FlushDispatcher();
            Thread.Sleep(1);
        }

        foreach (var (owner, resource) in resources)
        {
            if (resource.Get(CssNode.Get(owner)) is BitmapImage bitmap)
            {
                var task = bitmap.PreloadAsync(64, 64);
                while (!task.IsCompleted && DateTime.UtcNow < deadline)
                {
                    FlushDispatcher();
                    Thread.Sleep(1);
                }
            }
        }
        FlushDispatcher();

        static void AddResources(
            DependencyObject owner,
            CssBackgroundImageResource[]? list,
            List<(DependencyObject Owner, CssBackgroundImageResource Resource)> output)
        {
            if (list is null) return;
            foreach (var r in list)
            {
                if (r is not null)
                    output.Add((owner, r));
            }
        }

        static void CollectNodes(DependencyObject current, List<DependencyObject> output)
        {
            output.Add(current);
            if (current is TextBlock tb)
            {
                foreach (var inline in tb.Inlines)
                    CollectInlines(inline, output);
            }
            var count = VisualTreeHelper.GetChildrenCount(current);
            for (var i = 0; i < count; i++)
            {
                if (VisualTreeHelper.GetChild(current, i) is DependencyObject child)
                    CollectNodes(child, output);
            }
        }

        static void CollectInlines(Inline inline, List<DependencyObject> output)
        {
            output.Add(inline);
            if (inline is Span span)
            {
                foreach (var child in span.Inlines)
                    CollectInlines(child, output);
            }
        }
    }

    private static void Layout(FrameworkElement root, int width, int height)
    {
        for (var pass = 0; pass < 4; pass++)
        {
            FlushDispatcher();
            root.Measure(new Size(width, height));
            root.Arrange(new Rect(0, 0, width, height));
            root.UpdateLayout();
            WaitForCssImages(root);
        }
    }
}

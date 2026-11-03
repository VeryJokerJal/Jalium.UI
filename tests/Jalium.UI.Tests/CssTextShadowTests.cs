using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Effects;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssTextShadowTests
{
    [Theory]
    [InlineData("none")]
    [InlineData("2px 3px red")]
    [InlineData("red 2px 3px")]
    [InlineData("0 0 4px currentColor")]
    [InlineData("2px 3px red, -4px 5px 2px blue")]
    [InlineData("calc(1em + 2px) -3px color-mix(in srgb, currentColor 50%, blue)")]
    public void AcceptsOuterTextShadows(string value)
        => Assert.True(Css.Supports("text-shadow", value));

    [Theory]
    [InlineData("2px")]
    [InlineData("2px 3px -1px red")]
    [InlineData("2px 3px 4px 5px red")]
    [InlineData("inset 2px 3px red")]
    [InlineData("2px 3px red,")]
    public void RejectsInvalidOrUnimplementedTextShadows(string value)
        => Assert.False(Css.Supports("text-shadow", value));

    [Fact]
    public void TextShadowInherits_AndCurrentColorTracksForeground()
    {
        var text = new TextBlock { Text = "MMMM", Foreground = Brushes.Red };
        var parent = new Border { Child = text };
        Css.SetStyle(parent, "color: red; text-shadow: 8px 2px currentColor");
        CssBorderRadiusTests.Layout(parent, 130, 60);
        var red = Assert.IsType<DropShadowEffect>(CssTextShadowProperties.Value(text));
        Assert.Equal(Colors.Red, red.Color);

        Css.SetStyle(text, "text-shadow: inherit");
        Css.SetStyle(parent, "text-shadow: 8px 2px blue");
        CssBorderRadiusTests.Layout(parent, 130, 60);
        var blue = Assert.IsType<DropShadowEffect>(CssTextShadowProperties.Value(text));
        Assert.Equal(Colors.Blue, blue.Color);

        Css.SetStyle(text, "text-shadow: 8px 2px currentColor");
        CssBorderRadiusTests.Layout(parent, 130, 60);
        Assert.Equal(Colors.Red,
            Assert.IsType<DropShadowEffect>(CssTextShadowProperties.Value(text)).Color);
        text.Foreground = Brushes.Green;
        CssBorderRadiusTests.Layout(parent, 130, 60);
        Assert.Equal(Colors.Green,
            Assert.IsType<DropShadowEffect>(CssTextShadowProperties.Value(text)).Color);

        Css.SetStyle(text, "text-shadow: 8px 2px");
        CssBorderRadiusTests.Layout(parent, 130, 60);
        Assert.Equal(Colors.Green,
            Assert.IsType<DropShadowEffect>(CssTextShadowProperties.Value(text)).Color);

        Css.SetStyle(text, "text-shadow: 8px 2px currentColor, 12px 2px blue");
        CssBorderRadiusTests.Layout(parent, 130, 60);
        var group = Assert.IsType<EffectGroup>(CssTextShadowProperties.Value(text));
        Assert.Equal(Colors.Green, Assert.IsType<DropShadowEffect>(group.Children[0]).Color);
        Assert.Equal(Colors.Blue, Assert.IsType<DropShadowEffect>(group.Children[1]).Color);

        Css.SetStyle(text, "text-shadow: none");
        CssBorderRadiusTests.Layout(parent, 130, 60);
        Assert.Null(CssTextShadowProperties.Value(text));
    }

    [Fact]
    public void InlineShadowCascade_ResolvesCurrentColorAndNone()
    {
        var run = new Run("MMMM");
        var span = new Span(run);
        var text = new TextBlock { Width = 160, Height = 60 };
        text.Inlines.Add(span);
        Css.SetStyle(text, "color:red; text-shadow: 2px 3px blue");
        Css.SetStyle(span,
            "color:green; text-shadow: 2px 3px currentColor, 4px 5px blue");
        CssBorderRadiusTests.Layout(text, 160, 60);
        var inherited = Assert.IsType<EffectGroup>(CssTextShadowProperties.Value(run));
        Assert.Equal(Colors.Green, Assert.IsType<DropShadowEffect>(inherited.Children[0]).Color);
        Assert.Equal(Colors.Blue, Assert.IsType<DropShadowEffect>(inherited.Children[1]).Color);

        Css.SetStyle(run, "color:yellow; text-shadow: 2px 3px currentColor");
        CssBorderRadiusTests.Layout(text, 160, 60);
        Assert.Equal(Colors.Yellow,
            Assert.IsType<DropShadowEffect>(CssTextShadowProperties.Value(run)).Color);

        Css.SetStyle(run, "text-shadow:none");
        CssBorderRadiusTests.Layout(text, 160, 60);
        Assert.Null(CssTextShadowProperties.Value(run));
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_PaintsTextShadowsDirectAndRecorded() => Verify(RenderBackend.Software);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_PaintsTextShadowsDirectAndRecorded() => Verify(RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_PaintsTextShadowsDirectAndRecorded() => Verify(RenderBackend.Vulkan);

    private static void Verify(RenderBackend backend)
    {
        const int width = 210, height = 100;
        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext(backend);
        using var target = context.CreateRenderTarget(window.Hwnd, width, height);
        using var drawing = new RenderTargetDrawingContext(target, context);

        static FrameworkElement Create(int kind)
        {
            return kind switch
            {
                1 => new TextBox { Text = "MMMM", Width = 150, Height = 65,
                    FontSize = 26, Foreground = Brushes.White },
                2 => new AccessText { Text = "MMMM", Width = 150, Height = 65,
                    FontSize = 26, Foreground = Brushes.White },
                3 => new RichTextBox { Document = new FlowDocument(new Paragraph(
                    new Run("MMMM") { FontSize = 26, Foreground = Brushes.White })),
                    Width = 150, Height = 65 },
                _ => new TextBlock { Text = "MMMM", Width = 150, Height = 65,
                    FontSize = 26, Foreground = Brushes.White },
            };
        }

        byte[] Paint(FrameworkElement visual, bool recorded)
        {
            CssBorderRadiusTests.Layout(visual, 150, 65);
            target.SetFullInvalidation();
            var deadline = Environment.TickCount64 + 2000;
            while (!target.TryBeginDraw())
            {
                Assert.True(Environment.TickCount64 < deadline);
                Thread.Sleep(1);
            }
            target.Clear(0, 0, 0, 1);
            if (recorded)
            {
                var recorder = new DrawingRecorder();
                recorder.BindWholeFrame();
                ((IOffsetDrawingContext)recorder).Offset = new Point(10, 10);
                visual.Render(recorder);
                var commands = recorder.Commit();
                Assert.True(commands.FullyRecordable);
                DrawingReplayer.Replay(commands, drawing);
            }
            else
            {
                drawing.Offset = new Point(10, 10);
                try { visual.Render(drawing); }
                finally { drawing.Offset = default; }
            }
            Assert.Equal(JaliumResult.Ok, target.RequestReadback());
            Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
            var pixels = new byte[width * height * 4];
            Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, width * 4u, out _, out _));
            return pixels;
        }

        static int ColoredPixels(byte[] pixels, bool red)
        {
            var count = 0;
            for (var i = 0; i < pixels.Length; i += 4)
                if (pixels[i + 3] > 80 &&
                    (red ? pixels[i + 2] > 150 && pixels[i + 1] < 80 && pixels[i] < 80
                        : pixels[i] > 150 && pixels[i + 1] < 80 && pixels[i + 2] < 80))
                    count++;
            return count;
        }

        foreach (var kind in new[] { 0, 1, 2, 3 })
        {
            var visual = Create(kind);
            foreach (var recorded in new[] { false, true })
            {
                Css.SetStyle(visual, "text-shadow: none");
                Assert.Null(CssTextShadowProperties.Value(visual));
                var noShadow = Paint(visual, recorded);
                var redWithoutShadow = ColoredPixels(noShadow, red: true);
                Assert.True(redWithoutShadow == 0,
                    $"{backend} kind={kind} recorded={recorded}: {redWithoutShadow} unexpected red pixels; " +
                    DescribeFirstRed(noShadow));

                Css.SetStyle(visual, "text-shadow: 12px 0 1px red; text-decoration: underline white 2px");
                var single = Paint(visual, recorded);
                Assert.True(ColoredPixels(single, red: true) > 0,
                    $"{backend} kind={kind} recorded={recorded}: no red text shadow");

                Css.SetStyle(visual, "text-shadow: 24px 0 1px blue");
                var farSingle = Paint(visual, recorded);
                Assert.True(ColoredPixels(farSingle, red: false) > 0,
                    $"{backend} kind={kind} recorded={recorded}: no far blue shadow");

                Css.SetStyle(visual, "text-shadow: 12px 0 1px red, 24px 0 1px blue");
                var multiple = Paint(visual, recorded);
                Assert.True(ColoredPixels(multiple, red: true) > 0,
                    $"{backend} kind={kind} recorded={recorded}: no front shadow");
                Assert.True(ColoredPixels(multiple, red: false) > 0,
                    $"{backend} kind={kind} recorded={recorded}: no back shadow");
            }
        }

        // Equal offsets make the two shadows overlap. CSS paints the first
        // listed shadow above the second, leaving a red-dominant purple edge.
        var stacked = Create(0);
        Css.SetStyle(stacked, "text-shadow: none");
        var stackedPlain = Paint(stacked, recorded: false);
        Css.SetStyle(stacked,
            "text-shadow: 24px 0 0 rgba(255, 0, 0, 0.75), 24px 0 0 blue");
        var stackedPixels = Paint(stacked, recorded: false);
        var redAboveBlue = 0;
        for (var pixel = 0; pixel < stackedPixels.Length; pixel += 4)
        {
            if (stackedPlain[pixel] > 10 || stackedPlain[pixel + 1] > 10 ||
                stackedPlain[pixel + 2] > 10)
                continue;
            var blue = stackedPixels[pixel];
            var green = stackedPixels[pixel + 1];
            var red = stackedPixels[pixel + 2];
            if (red > 100 && blue > 20 && red > blue + 25 && green < 25)
                redAboveBlue++;
        }
        Assert.True(redAboveBlue > 0,
            $"{backend}: the first text shadow did not paint above the second");

        var translucent = new TextBlock { Text = "MMMM", Width = 150, Height = 65,
            FontSize = 26, Foreground = new SolidColorBrush(Color.FromArgb(128, 255, 255, 255)) };
        Css.SetStyle(translucent, "text-shadow: none");
        var plainPixels = Paint(translucent, recorded: false);
        Css.SetStyle(translucent, "text-shadow: 12px 0 1px red, 24px 0 1px blue");
        var shadowPixels = Paint(translucent, recorded: false);
        static int MaxLeftText(byte[] pixels)
        {
            var max = 0;
            for (var y = 10; y < 60; y++)
                for (var x = 10; x < 21; x++)
                    max = Math.Max(max, pixels[(y * width + x) * 4 + 1]);
            return max;
        }
        Assert.InRange(MaxLeftText(shadowPixels), 0, MaxLeftText(plainPixels) + 10);

        Css.SetStyle(translucent, "text-shadow: 0 0 0 red");
        var coincident = Paint(translucent, recorded: false);
        var redExcess = 0;
        for (var y = 10; y < 60; y++)
            for (var x = 10; x < 21; x++)
            {
                var pixel = (y * width + x) * 4;
                redExcess = Math.Max(redExcess,
                    coincident[pixel + 2] - coincident[pixel + 1]);
            }
        Assert.True(redExcess > 15,
            $"{backend}: zero-offset shadow did not paint behind translucent text");

        var decoration = new TextBlock { Text = "MMMM", Width = 150, Height = 65,
            FontSize = 26, Foreground = Brushes.Transparent };
        Css.SetStyle(decoration,
            "text-decoration: underline white 3px; text-shadow: 0 10px 1px red");
        var decoratedPixels = Paint(decoration, recorded: false);
        Assert.True(ColoredPixels(decoratedPixels, red: true) > 0,
            $"{backend}: underline did not cast a text shadow");

        static (FrameworkElement Visual, Span Span, Run Run) CreateInline(bool rich)
        {
            var first = new Run("MM") { FontSize = 26, Foreground = Brushes.White };
            var second = new Run("MM") { FontSize = 26, Foreground = Brushes.White };
            var span = new Span();
            span.Inlines.Add(second);
            if (rich)
            {
                var paragraph = new Paragraph();
                paragraph.Inlines.Add(first);
                paragraph.Inlines.Add(span);
                return (new RichTextBox
                {
                    Document = new FlowDocument(paragraph), Width = 150, Height = 65
                }, span, second);
            }
            var block = new TextBlock { Width = 150, Height = 65,
                FontSize = 26, Foreground = Brushes.White };
            block.Inlines.Add(first);
            block.Inlines.Add(span);
            return (block, span, second);
        }

        static (int Count, double CenterX) TintRegion(byte[] pixels, bool red)
        {
            var count = 0;
            var sumX = 0.0;
            for (var y = 10; y < height; y++)
                for (var x = 10; x < width; x++)
                {
                    var i = (y * width + x) * 4;
                    if (pixels[i + 3] < 80 || pixels[i + 1] > 80 ||
                        (red ? pixels[i + 2] < 150 || pixels[i] > 80
                             : pixels[i] < 150 || pixels[i + 2] > 80))
                        continue;
                    count++;
                    sumX += x;
                }
            return (count, count > 0 ? sumX / count : 0);
        }

        foreach (var rich in new[] { false, true })
            foreach (var recorded in new[] { false, true })
            {
                var (inlineVisual, span, run) = CreateInline(rich);
                Css.SetStyle(inlineVisual, "text-shadow: 0 24px 0 red");
                Css.SetStyle(span, "text-shadow: 0 24px 0 blue");
                var split = Paint(inlineVisual, recorded);
                var red = TintRegion(split, red: true);
                var blue = TintRegion(split, red: false);
                Assert.True(red.Count > 0 && blue.Count > 0 &&
                            blue.CenterX > red.CenterX + 15,
                    $"{backend} rich={rich} recorded={recorded}: inline shadow colors or positions are wrong " +
                    $"(red={red}, blue={blue})");

                Css.SetStyle(span,
                    "text-shadow: 0 8px 0 blue, 0 18px 0 lime");
                var layered = Paint(inlineVisual, recorded);
                var greenPixels = 0;
                for (var pixel = 0; pixel < layered.Length; pixel += 4)
                    if (layered[pixel + 1] > 150 && layered[pixel] < 80 &&
                        layered[pixel + 2] < 80)
                        greenPixels++;
                Assert.True(TintRegion(layered, red: false).Count > 0 && greenPixels > 0,
                    $"{backend} rich={rich} recorded={recorded}: inline shadow list lost a layer");

                Css.SetStyle(span, "text-shadow: 0 24px 0 blue");
                Css.SetStyle(run, "text-shadow: none");
                var suppressed = Paint(inlineVisual, recorded);
                Assert.True(TintRegion(suppressed, red: true).Count > 0 &&
                            TintRegion(suppressed, red: false).Count == 0,
                    $"{backend} rich={rich} recorded={recorded}: Run did not suppress its inherited shadow");

                Css.SetStyle(span, "text-shadow: none");
                Css.SetStyle(run, "");
                var oneRunShadow = TintRegion(Paint(inlineVisual, recorded), red: true);
                Css.SetStyle(span, "");
                var bothRunShadows = TintRegion(Paint(inlineVisual, recorded), red: true);
                Assert.True(oneRunShadow.Count > 0 &&
                            bothRunShadows.Count > oneRunShadow.Count * 1.5,
                    $"{backend} rich={rich} recorded={recorded}: clearing Span none did not restore inheritance " +
                    $"({oneRunShadow.Count} -> {bothRunShadows.Count})");

                Css.SetStyle(inlineVisual, "text-shadow: none");
                Css.SetStyle(span, "text-shadow: none");
                var plainInline = Paint(inlineVisual, recorded);
                Css.SetStyle(span, "text-shadow: -17px 0 0 blue");
                var crossing = Paint(inlineVisual, recorded);
                Assert.True(TintRegion(crossing, red: false).Count > 0,
                    $"{backend} rich={rich} recorded={recorded}: crossing shadow is missing");
                var opaqueNeighborPixels = 0;
                var coveredNeighborPixels = 0;
                var firstCovered = string.Empty;
                var boundary = (red.CenterX + blue.CenterX) * 0.5;
                for (var y = 10; y < height; y++)
                    for (var x = 10; x < boundary; x++)
                    {
                        var i = (y * width + x) * 4;
                        if (plainInline[i] < 250 || plainInline[i + 1] < 250 ||
                            plainInline[i + 2] < 250)
                            continue;
                        opaqueNeighborPixels++;
                        if (crossing[i] < 235 || crossing[i + 1] < 235 ||
                            crossing[i + 2] < 235)
                        {
                            coveredNeighborPixels++;
                            if (firstCovered.Length == 0)
                                firstCovered = $" at ({x},{y}) BGRA=({crossing[i]},{crossing[i + 1]},{crossing[i + 2]},{crossing[i + 3]})";
                        }
                    }
                Assert.True(opaqueNeighborPixels > 10 && coveredNeighborPixels == 0,
                    $"{backend} rich={rich} recorded={recorded}: inline shadow covered adjacent opaque text " +
                    $"({coveredNeighborPixels}/{opaqueNeighborPixels}){firstCovered}");

                if (inlineVisual is RichTextBox richBox)
                {
                    Css.SetStyle(span, "");
                    Css.SetStyle(richBox.Document, "text-shadow: 0 18px 0 red");
                    Assert.True(TintRegion(Paint(inlineVisual, recorded), red: true).Count > 0,
                        $"{backend} recorded={recorded}: FlowDocument shadow is missing");
                    Css.SetStyle(richBox.Document, "text-shadow: none");
                    Assert.True(TintRegion(Paint(inlineVisual, recorded), red: true).Count == 0,
                        $"{backend} recorded={recorded}: FlowDocument none did not suppress the shadow");
                    Css.SetStyle(inlineVisual, "text-shadow: 0 18px 0 blue");
                    Assert.True(TintRegion(Paint(inlineVisual, recorded), red: false).Count == 0,
                        $"{backend} recorded={recorded}: FlowDocument none did not override the control");
                    Css.SetStyle(richBox.Document, "");
                    Assert.True(TintRegion(Paint(inlineVisual, recorded), red: false).Count > 0,
                        $"{backend} recorded={recorded}: clearing FlowDocument none did not restore inheritance");
                    var paragraph = Assert.IsType<Paragraph>(richBox.Document.Blocks[0]);
                    Css.SetStyle(paragraph, "text-shadow: 0 18px 0 red");
                    Assert.True(TintRegion(Paint(inlineVisual, recorded), red: true).Count > 0,
                        $"{backend} recorded={recorded}: Paragraph shadow is missing");
                    Css.SetStyle(paragraph, "text-shadow: none");
                    var paragraphNone = Paint(inlineVisual, recorded);
                    Assert.True(TintRegion(paragraphNone, red: true).Count == 0 &&
                                TintRegion(paragraphNone, red: false).Count == 0,
                        $"{backend} recorded={recorded}: Paragraph none did not suppress inherited shadows");
                    Css.SetStyle(paragraph, "");
                    Assert.True(TintRegion(Paint(inlineVisual, recorded), red: false).Count > 0,
                        $"{backend} recorded={recorded}: clearing Paragraph none did not restore inheritance");
                }
            }
    }

    private static string DescribeFirstRed(byte[] pixels)
    {
        for (var i = 0; i < pixels.Length; i += 4)
            if (pixels[i + 3] > 80 && pixels[i + 2] > 150 &&
                pixels[i + 1] < 80 && pixels[i] < 80)
                return $"first BGRA=({pixels[i]},{pixels[i + 1]},{pixels[i + 2]},{pixels[i + 3]}) at pixel {i / 4}";
        return "no red pixels";
    }
}

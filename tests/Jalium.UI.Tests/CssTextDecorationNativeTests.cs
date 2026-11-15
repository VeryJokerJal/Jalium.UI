using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssTextDecorationNativeTests
{
    private enum TextTarget { Block, Box, Access, RichRun, InlineRun }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_PaintsDecorationStylesDirectAndRecorded() => Verify(RenderBackend.Software, TextTarget.Block);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_PaintsDecorationStylesDirectAndRecorded() => Verify(RenderBackend.D3D12, TextTarget.Block);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_PaintsDecorationStylesDirectAndRecorded() => Verify(RenderBackend.Vulkan, TextTarget.Block);

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_PaintsTextBoxStylesDirectAndRecorded() => Verify(RenderBackend.Software, TextTarget.Box);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_PaintsTextBoxStylesDirectAndRecorded() => Verify(RenderBackend.D3D12, TextTarget.Box);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_PaintsTextBoxStylesDirectAndRecorded() => Verify(RenderBackend.Vulkan, TextTarget.Box);

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_PaintsAccessTextStylesDirectAndRecorded() => Verify(RenderBackend.Software, TextTarget.Access);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_PaintsAccessTextStylesDirectAndRecorded() => Verify(RenderBackend.D3D12, TextTarget.Access);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_PaintsAccessTextStylesDirectAndRecorded() => Verify(RenderBackend.Vulkan, TextTarget.Access);

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_PaintsRichRunStylesDirectAndRecorded() => Verify(RenderBackend.Software, TextTarget.RichRun);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_PaintsRichRunStylesDirectAndRecorded() => Verify(RenderBackend.D3D12, TextTarget.RichRun);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_PaintsRichRunStylesDirectAndRecorded() => Verify(RenderBackend.Vulkan, TextTarget.RichRun);

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_PaintsTextBlockInlineStylesDirectAndRecorded() => Verify(RenderBackend.Software, TextTarget.InlineRun);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_PaintsTextBlockInlineStylesDirectAndRecorded() => Verify(RenderBackend.D3D12, TextTarget.InlineRun);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_PaintsTextBlockInlineStylesDirectAndRecorded() => Verify(RenderBackend.Vulkan, TextTarget.InlineRun);

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_PaintsTextBlockInlineForegroundDirectAndRecorded() => VerifyInlineForeground(RenderBackend.Software);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_PaintsTextBlockInlineForegroundDirectAndRecorded() => VerifyInlineForeground(RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_PaintsTextBlockInlineForegroundDirectAndRecorded() => VerifyInlineForeground(RenderBackend.Vulkan);

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_PaintsTextBlockInlineFontSizeDirectAndRecorded() => VerifyInlineFontSize(RenderBackend.Software);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_PaintsTextBlockInlineFontSizeDirectAndRecorded() => VerifyInlineFontSize(RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_PaintsTextBlockInlineFontSizeDirectAndRecorded() => VerifyInlineFontSize(RenderBackend.Vulkan);

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_PaintsClonedDecorationFragmentsDirectAndRecorded()
        => VerifyClonedDecorationFragments(RenderBackend.Software);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_PaintsClonedDecorationFragmentsDirectAndRecorded()
        => VerifyClonedDecorationFragments(RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_PaintsClonedDecorationFragmentsDirectAndRecorded()
        => VerifyClonedDecorationFragments(RenderBackend.Vulkan);

    private static void VerifyClonedDecorationFragments(RenderBackend backend)
    {
        const int width = 200, height = 120;
        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext(backend);
        using var target = context.CreateRenderTarget(window.Hwnd, width, height);
        using var drawing = new RenderTargetDrawingContext(target, context);
        var text = new TextBlock
        {
            Text = "MMMM\nMMMM", Width = 170, Height = 105,
            FontSize = 24, Foreground = Brushes.Black,
        };

        byte[] Paint(bool clone, bool recorded)
        {
            Css.SetStyle(text, "text-decoration: underline solid red 3px; " +
                "text-decoration-inset: 8px 16px; box-decoration-break: " +
                (clone ? "clone" : "slice"));
            CssBorderRadiusTests.Layout(text, 170, 105);
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
                text.Render(recorder);
                var commands = recorder.Commit();
                Assert.True(commands.FullyRecordable);
                DrawingReplayer.Replay(commands, drawing);
            }
            else
            {
                drawing.Offset = new Point(10, 10);
                try { text.Render(drawing); }
                finally { drawing.Offset = default; }
            }
            Assert.Equal(JaliumResult.Ok, target.RequestReadback());
            Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
            var pixels = new byte[width * height * 4];
            Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, width * 4u, out _, out _));
            return pixels;
        }

        static ((int Min, int Max) First, (int Min, int Max) Last) RedBounds(byte[] pixels)
        {
            var minY = height;
            var maxY = -1;
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var index = (y * width + x) * 4;
                    if (pixels[index + 2] <= 150 || pixels[index + 1] >= 80 ||
                        pixels[index] >= 80) continue;
                    minY = Math.Min(minY, y);
                    maxY = Math.Max(maxY, y);
                }
            Assert.True(maxY - minY > 10);
            var splitY = (minY + maxY) / 2;
            var firstMin = width;
            var firstMax = -1;
            var lastMin = width;
            var lastMax = -1;
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var index = (y * width + x) * 4;
                    if (pixels[index + 2] <= 150 || pixels[index + 1] >= 80 ||
                        pixels[index] >= 80) continue;
                    if (y <= splitY)
                    {
                        firstMin = Math.Min(firstMin, x);
                        firstMax = Math.Max(firstMax, x);
                    }
                    else
                    {
                        lastMin = Math.Min(lastMin, x);
                        lastMax = Math.Max(lastMax, x);
                    }
                }
            Assert.True(firstMax >= 0 && lastMax >= 0);
            return ((firstMin, firstMax), (lastMin, lastMax));
        }

        foreach (var recorded in new[] { false, true })
        {
            var sliced = RedBounds(Paint(clone: false, recorded));
            var cloned = RedBounds(Paint(clone: true, recorded));
            Assert.InRange(cloned.First.Min - sliced.First.Min, -1, 1);
            Assert.InRange(sliced.First.Max - cloned.First.Max, 15, 17);
            Assert.InRange(cloned.Last.Min - sliced.Last.Min, 7, 9);
            Assert.InRange(cloned.Last.Max - sliced.Last.Max, -1, 1);
        }
    }

    private static void VerifyInlineFontSize(RenderBackend backend)
    {
        const int width = 240, height = 100;
        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext(backend);
        using var target = context.CreateRenderTarget(window.Hwnd, width, height);
        using var drawing = new RenderTargetDrawingContext(target, context);
        var first = new Run("M");
        var second = new Run("MMMM");
        var block = new TextBlock(first)
        {
            Width = 210, Height = 85, FontSize = 14, Foreground = Brushes.White,
        };
        block.Inlines.Add(second);

        byte[] Paint(int fontSize, bool recorded)
        {
            Css.SetStyle(second, $"font-size:{fontSize}px; color:red");
            CssBorderRadiusTests.Layout(block, 210, 85);
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
                block.Render(recorder);
                var commands = recorder.Commit();
                Assert.True(commands.FullyRecordable);
                DrawingReplayer.Replay(commands, drawing);
            }
            else
            {
                drawing.Offset = new Point(10, 10);
                try { block.Render(drawing); }
                finally { drawing.Offset = default; }
            }
            Assert.Equal(JaliumResult.Ok, target.RequestReadback());
            Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
            var pixels = new byte[width * height * 4];
            Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, width * 4u, out _, out _));
            return pixels;
        }

        static (int Width, int Height) RedBounds(byte[] pixels)
        {
            var minX = width;
            var minY = height;
            var maxX = -1;
            var maxY = -1;
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var index = (y * width + x) * 4;
                    if (pixels[index + 2] <= 180 || pixels[index + 1] >= 80 ||
                        pixels[index] >= 80) continue;
                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y);
                    maxY = Math.Max(maxY, y);
                }
            return (maxX - minX + 1, maxY - minY + 1);
        }

        foreach (var recorded in new[] { false, true })
        {
            var small = RedBounds(Paint(14, recorded));
            var large = RedBounds(Paint(30, recorded));
            Assert.True(small.Width > 0 && small.Height > 0);
            Assert.True(large.Width > small.Width + 15,
                $"{backend} recorded={recorded}: width {small.Width} -> {large.Width}");
            Assert.True(large.Height > small.Height + 8,
                $"{backend} recorded={recorded}: height {small.Height} -> {large.Height}");
        }
    }

    private static void VerifyInlineForeground(RenderBackend backend)
    {
        const int width = 160, height = 70;
        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext(backend);
        using var target = context.CreateRenderTarget(window.Hwnd, width, height);
        using var drawing = new RenderTargetDrawingContext(target, context);
        var first = new Run("MM");
        var second = new Run("MM");
        var block = new TextBlock(first)
        {
            Width = 130, Height = 45, FontSize = 24, Foreground = Brushes.White,
        };
        block.Inlines.Add(second);

        byte[] Paint(bool recorded)
        {
            CssBorderRadiusTests.Layout(block, 130, 45);
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
                block.Render(recorder);
                var commands = recorder.Commit();
                Assert.True(commands.FullyRecordable);
                DrawingReplayer.Replay(commands, drawing);
            }
            else
            {
                drawing.Offset = new Point(10, 10);
                try { block.Render(drawing); }
                finally { drawing.Offset = default; }
            }
            Assert.Equal(JaliumResult.Ok, target.RequestReadback());
            Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
            var pixels = new byte[width * height * 4];
            Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, width * 4u, out _, out _));
            return pixels;
        }

        static int CountColor(byte[] pixels, bool red)
        {
            var count = 0;
            for (var i = 0; i < pixels.Length; i += 4)
                if (pixels[i + 3] > 80 && pixels[i + 1] < 80 &&
                    (red ? pixels[i + 2] > 180 && pixels[i] < 80
                        : pixels[i] > 180 && pixels[i + 2] < 80))
                    count++;
            return count;
        }

        static int MinRedX(byte[] pixels)
        {
            for (var x = 0; x < width; x++)
                for (var y = 0; y < height; y++)
                {
                    var i = (y * width + x) * 4;
                    if (pixels[i + 2] > 180 && pixels[i + 1] < 80 && pixels[i] < 80)
                        return x;
                }
            return width;
        }

        Css.SetStyle(second, "color:red");
        foreach (var recorded in new[] { false, true })
        {
            var red = Paint(recorded);
            Assert.True(CountColor(red, red: true) > 0);
            Assert.True(MinRedX(red) > 25);
            Css.SetStyle(second, "color:blue");
            var blue = Paint(recorded);
            Assert.True(CountColor(blue, red: false) > 0);
            Assert.Equal(0, CountColor(blue, red: true));
            Css.SetStyle(second, "color:red");
        }
    }

    private static void Verify(RenderBackend backend, TextTarget targetKind)
    {
        const int width = 160, height = 70;
        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext(backend);
        using var target = context.CreateRenderTarget(window.Hwnd, width, height);
        using var drawing = new RenderTargetDrawingContext(target, context);
        var run = new Run("MMMM") { FontSize = 24, Foreground = Brushes.Black };
        FrameworkElement text = targetKind switch
        {
            TextTarget.Box => new TextBox { Text = "MMMM", Width = 130, Height = 45, FontSize = 24, Foreground = Brushes.Black },
            TextTarget.Access => new AccessText { Text = "MMMM", Width = 130, Height = 45, FontSize = 24, Foreground = Brushes.Black },
            TextTarget.RichRun => new RichTextBox { Document = new FlowDocument(new Paragraph(run)), Width = 130, Height = 45 },
            TextTarget.InlineRun => new TextBlock(run) { Width = 130, Height = 45, FontSize = 24, Foreground = Brushes.Black },
            _ => new TextBlock { Text = "MMMM", Width = 130, Height = 45, FontSize = 24, Foreground = Brushes.Black },
        };
        DependencyObject styled = targetKind is TextTarget.RichRun or TextTarget.InlineRun ? run : text;
        var decorationsProperty = targetKind switch
        {
            TextTarget.Box => TextBox.TextDecorationsProperty,
            TextTarget.Access => AccessText.TextDecorationsProperty,
            TextTarget.RichRun => TextElement.TextDecorationsProperty,
            TextTarget.InlineRun => TextElement.TextDecorationsProperty,
            _ => TextBlock.TextDecorationsProperty,
        };

        byte[] Paint(string style, bool recorded, bool completeDeclaration = false)
        {
            Css.SetStyle(styled, completeDeclaration
                ? $"text-decoration: {style}" : $"text-decoration: underline {style} red 3px");
            CssBorderRadiusTests.Layout(text, 130, 45);
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
                text.Render(recorder);
                var commands = recorder.Commit();
                Assert.True(commands.FullyRecordable);
                DrawingReplayer.Replay(commands, drawing);
            }
            else
            {
                drawing.Offset = new Point(10, 10);
                try { text.Render(drawing); }
                finally { drawing.Offset = default; }
            }
            Assert.Equal(JaliumResult.Ok, target.RequestReadback());
            Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
            var pixels = new byte[width * height * 4];
            Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, width * 4u, out _, out _));
            return pixels;
        }

        static byte[] RedMask(byte[] pixels)
        {
            var mask = new byte[width * height];
            for (var i = 0; i < mask.Length; i++)
            {
                var offset = i * 4;
                mask[i] = pixels[offset + 2] > 150 && pixels[offset + 1] < 80 &&
                    pixels[offset] < 80 ? (byte)1 : (byte)0;
            }
            return mask;
        }

        static byte[] GreenMask(byte[] pixels)
        {
            var mask = new byte[width * height];
            for (var i = 0; i < mask.Length; i++)
            {
                var offset = i * 4;
                mask[i] = pixels[offset + 1] > 80 && pixels[offset + 2] < 80 &&
                    pixels[offset] < 80 ? (byte)1 : (byte)0;
            }
            return mask;
        }

        static (int Min, int Max) RedBoundsX(byte[] mask)
        {
            var min = width;
            var max = -1;
            for (var index = 0; index < mask.Length; index++)
            {
                if (mask[index] == 0) continue;
                var x = index % width;
                min = Math.Min(min, x);
                max = Math.Max(max, x);
            }
            Assert.True(max >= 0);
            return (min, max);
        }

        foreach (var recorded in new[] { false, true })
        {
            var solid = RedMask(Paint("solid", recorded));
            Assert.Contains((byte)1, solid);
            foreach (var style in new[] { "double", "dashed", "dotted", "wavy" })
            {
                var mask = RedMask(Paint(style, recorded));
                Assert.True(mask.Contains((byte)1),
                    $"{backend} recorded={recorded} style={style} has no red pixels; " +
                    $"line={CssTextDecorationProperties.Line(styled)}, " +
                    $"computedStyle={CssTextDecorationProperties.Style(styled)}, " +
                    $"color={CssTextDecorationProperties.Color(styled)}, " +
                    $"layer={styled.GetEffectiveValueLayer(decorationsProperty)}, " +
                    $"decorations={(styled.GetValue(decorationsProperty) as TextDecorationCollection)?.Count}");
                Assert.False(solid.AsSpan().SequenceEqual(mask),
                    $"{backend} recorded={recorded}: {style} collapsed to solid");
            }

            var spelling = Paint("spelling-error solid blue 3px", recorded, completeDeclaration: true);
            Assert.Contains((byte)1, RedMask(spelling));
            Assert.DoesNotContain((byte)1, GreenMask(spelling));

            var grammar = Paint("grammar-error solid red 3px", recorded, completeDeclaration: true);
            Assert.Contains((byte)1, GreenMask(grammar));
            Assert.DoesNotContain((byte)1, RedMask(grammar));

            var full = RedMask(Paint("underline solid red 8px; text-decoration-skip-inset: none",
                recorded, completeDeclaration: true));
            var inset = RedMask(Paint("underline solid red 8px; text-decoration-skip-inset: auto",
                recorded, completeDeclaration: true));
            Assert.Contains((byte)1, full);
            Assert.Contains((byte)1, inset);
            Assert.False(full.AsSpan().SequenceEqual(inset),
                $"{backend} {targetKind} recorded={recorded}: skip-inset did not change endpoints");

            var fullBounds = RedBoundsX(solid);
            var decorationInset = RedBoundsX(RedMask(Paint(
                "underline solid red 3px; text-decoration-inset: 8px 12px",
                recorded, completeDeclaration: true)));
            Assert.InRange(decorationInset.Min - fullBounds.Min, 7, 9);
            Assert.InRange(fullBounds.Max - decorationInset.Max, 11, 13);

            var extended = RedBoundsX(RedMask(Paint(
                "underline solid red 3px; text-decoration-inset: -4px -6px",
                recorded, completeDeclaration: true)));
            // The logical start can extend outside the control's content clip.
            Assert.True(extended.Min <= fullBounds.Min);
            Assert.InRange(extended.Max - fullBounds.Max, 5, 7);

            var automatic = RedBoundsX(RedMask(Paint(
                "underline solid red 3px; text-decoration-inset: auto",
                recorded, completeDeclaration: true)));
            Assert.True(automatic.Min > fullBounds.Min && automatic.Max < fullBounds.Max,
                $"{backend} {targetKind} recorded={recorded}: auto did not separate endpoints");
        }
    }
}

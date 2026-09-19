using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class D3D12RetainedLayerClarityTests
{
    private const int Width = 1024;
    private const int Height = 128;

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void Vello_FractionalButtonCapture_PreservesIconAndTextPixels() =>
        AssertClarityAtDisplayScales(RenderingEngine.Vello);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void Impeller_FractionalButtonCapture_PreservesIconAndTextPixels() =>
        AssertClarityAtDisplayScales(RenderingEngine.Impeller);

    private static void AssertClarityAtDisplayScales(RenderingEngine engine)
    {
        foreach (var dpi in new[] { 96f, 120f, 144f, 192f })
            AssertCaptureMatchesDirectDrawing(engine, dpi);
    }

    private static void AssertCaptureMatchesDirectDrawing(RenderingEngine engine, float dpi)
    {
        using var window = new HiddenNativeWindow(Width, Height);
        using var context = new RenderContext(RenderBackend.D3D12, GpuPreference.Auto, engine);
        using var target = context.CreateRenderTarget(window.Hwnd, Width, Height);
        target.SetDpi(dpi, dpi);
        target.SetPathMsaaSampleCount(4);

        var bounds = new Rect(310.5, 12.5, 104.5, 36.5);
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.Children.Add(new PathIcon
        {
            Data = Geometry.Parse("M0,10 C0,-3 20,-3 20,10 C20,23 0,23 0,10 Z"),
            Width = 15,
            Height = 15,
            Margin = new Thickness(0, 0, 7, 0),
            Foreground = Brushes.White,
        });
        row.Children.Add(new TextBlock
        {
            Text = "GitHub",
            FontSize = 12,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var content = new Border
        {
            Width = bounds.Width,
            Height = bounds.Height,
            Padding = new Thickness(12, 0, 12, 0),
            Child = row,
        };
        content.Measure(bounds.Size);
        content.Arrange(new Rect(0, 0, bounds.Width, bounds.Height));

        byte[]? baseline = null;
        nint layer = 0;
        try
        {
            // Use a fresh context on every frame: layer geometry belongs to the
            // retained texture and must also survive drawing-context recreation.
            for (var frame = 0; frame < 3; frame++)
            {
                using var dc = new RenderTargetDrawingContext(target, context);
                target.SetFullInvalidation();
                var timer = System.Diagnostics.Stopwatch.StartNew();
                while (!target.TryBeginDraw())
                {
                    Assert.True(timer.ElapsedMilliseconds < 1000, "BeginDraw timed out.");
                    Thread.Sleep(1);
                }
                target.Clear(0, 0, 0);
                dc.Offset = new Point(bounds.X, bounds.Y);
                if (frame == 0)
                {
                    dc.PushOpacity(0.82);
                    content.Render(dc);
                    dc.Pop();
                }
                else
                {
                    if (frame == 1)
                    {
                        layer = dc.BeginLayerCapture(0, bounds);
                        Assert.NotEqual(nint.Zero, layer);
                        content.Render(dc);
                        dc.EndLayerCapture(layer);
                    }
                    dc.CompositeLayer(layer, bounds, 0.82, null, 0, 0);
                }
                Assert.Equal(JaliumResult.Ok, target.RequestReadback());
                Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
                var pixels = new byte[Width * Height * 4];
                Assert.Equal(JaliumResult.Ok,
                    target.FetchReadback(pixels, Width * 4u, out _, out _));

                if (frame == 0)
                {
                    baseline = pixels;
                    Assert.True(Enumerable.Range(0, Width * Height)
                        .Count(pixel => pixels[pixel * 4] > 40) > 100);
                    continue;
                }

                AssertRegionClose(baseline!, pixels,
                    new Rect(bounds.X + 10, bounds.Y + 4, 20, 28), dpi,
                    $"{engine}, {dpi} DPI, frame {frame}, icon");
                AssertRegionClose(baseline!, pixels,
                    new Rect(bounds.X + 32, bounds.Y + 4, 65, 28), dpi,
                    $"{engine}, {dpi} DPI, frame {frame}, text");
            }
        }
        finally
        {
            if (layer != 0) target.DestroyRetainedLayer(layer);
        }
    }

    private static void AssertRegionClose(byte[] expected, byte[] actual, Rect region,
        float dpi, string scenario)
    {
        var scale = dpi / 96.0;
        var maxDifference = 0;
        long totalDifference = 0;
        var samples = 0;
        for (var y = (int)Math.Floor(region.Y * scale); y < Math.Ceiling(region.Bottom * scale); y++)
            for (var x = (int)Math.Floor(region.X * scale); x < Math.Ceiling(region.Right * scale); x++)
                for (var channel = 0; channel < 3; channel++)
                {
                    var index = (y * Width + x) * 4 + channel;
                    var difference = Math.Abs(expected[index] - actual[index]);
                    maxDifference = Math.Max(maxDifference, difference);
                    totalDifference += difference;
                    samples++;
                }
        var meanDifference = (double)totalDifference / samples;
        Assert.True(maxDifference <= 8 && meanDifference <= 0.5,
            $"{scenario}: retained capture resampled the content " +
            $"(max difference {maxDifference}, mean {meanDifference:F3}).");
    }
}

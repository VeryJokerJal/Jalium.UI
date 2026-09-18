using System.Diagnostics;
using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

/// <summary>
/// Exercises identity workgroup prefixes, transitions to multiple workgroups,
/// and reused output texels through the actual GPU pipeline. No Impeller path
/// is used as the pixel oracle: integer rectangles have exact coverage.
/// </summary>
[Collection("Application")]
public sealed class VelloWorkgroupRenderingTests
{
    private const int Columns = 32;
    private const int CellSize = 16;
    private const int Width = Columns * CellSize;
    private const int Height = 16 * CellSize;

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_Vello_WorkgroupPrefixesAndRecycledOutput_PreservePixels() =>
        AssertWorkgroupPixels(RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_Vello_WorkgroupPrefixesAndRecycledOutput_PreservePixels() =>
        AssertWorkgroupPixels(RenderBackend.Vulkan);

    private static void AssertWorkgroupPixels(RenderBackend backend)
    {
        using var window = new HiddenNativeWindow(Width, Height);
        using var context = new RenderContext(backend, GpuPreference.Auto, RenderingEngine.Vello);
        using var target = context.CreateRenderTarget(window.Hwnd, Width, Height);
        using var dark = context.CreateSolidBrush(1f / 3, 1f / 3, 1f / 3);
        using var mid = context.CreateSolidBrush(2f / 3, 2f / 3, 2f / 3);
        using var light = context.CreateSolidBrush(1, 1, 1);
        NativeBrush[] brushes = [dark, mid, light];

        float[] rectangle = [0, 14, 1, 0, 14, 14, 0, 1, 14, 5];
        float[] ring = [0, 14, 1, 0, 14, 14, 0, 1, 14, 5,
                        2, 5, 5, 0, 10, 5, 0, 10, 10, 0, 5, 10, 5];
        // Draw scans split at 256 objects; these path streams also cross the
        // 1024-byte path-tag workgroup boundary. Revisit small scenes after
        // large ones so stale reductions cannot accidentally pass the test.
        int[] counts = [1, 1, 255, 256, 257, 512, 128, 64, 1, 1];

        target.SetDpi(96, 96);
        // Prime swapchain/readback resources as the other GPU pixel fixtures do.
        Assert.True(target.TryBeginDraw());
        target.Clear(0, 0, 0);
        Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
        for (var frame = 0; frame < counts.Length; frame++)
        {
            target.SetFullInvalidation();
            var timer = Stopwatch.StartNew();
            while (!target.TryBeginDraw())
            {
                Assert.True(timer.ElapsedMilliseconds < 1000, "Timed out starting GPU frame.");
                Thread.Sleep(1);
            }
            target.Clear(0, 0, 0);
            var hasHole = (frame & 1) != 0;
            for (var i = 0; i < counts[frame]; i++)
            {
                target.PushTransformTranslation(i % Columns * CellSize, i / Columns * CellSize);
                target.FillPath(1, 1, hasHole ? ring : rectangle, brushes[(i + frame) % 3],
                    fillRule: 0);
                target.PopTransform();
            }

            Assert.Equal(JaliumResult.Ok, target.RequestReadback());
            var endResult = target.TryEndDraw();
            Assert.True(endResult == JaliumResult.Ok, $"{backend}, frame {frame}: {endResult}.");
            var pixels = new byte[Width * Height * 4];
            Assert.Equal(JaliumResult.Ok,
                target.FetchReadback(pixels, Width * 4u, out var width, out var height));
            Assert.Equal(Width, width);
            Assert.Equal(Height, height);

            for (var i = 0; i < Columns * 16; i++)
            {
                var x = i % Columns * CellSize;
                var y = i / Columns * CellSize;
                var expected = i < counts[frame] ? ((i + frame) % 3 + 1) * 85 : 0;
                AssertPixel(pixels, x + 3, y + 3, expected, backend, frame);
                AssertPixel(pixels, x + 7, y + 7, hasHole ? 0 : expected, backend, frame);
                AssertPixel(pixels, x + 15, y + 15, 0, backend, frame);
            }
        }
    }

    private static void AssertPixel(byte[] pixels, int x, int y, int expected,
        RenderBackend backend, int frame)
    {
        var offset = (y * Width + x) * 4;
        for (var channel = 0; channel < 3; channel++)
        {
            Assert.True(Math.Abs(pixels[offset + channel] - expected) <= 1,
                $"{backend}, frame {frame}, pixel ({x},{y}), channel {channel}: " +
                $"expected {expected}, got {pixels[offset + channel]}.");
        }
    }
}

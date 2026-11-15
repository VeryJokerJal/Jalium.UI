using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssDashLineNativeTests
{
    private const int Width = 96;
    private const int Height = 40;

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_direct_dashed_line_matches_path_phase_and_caps() =>
        Verify(RenderBackend.Software);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_direct_dashed_line_matches_path_phase_and_caps() =>
        Verify(RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_direct_dashed_line_matches_path_phase_and_caps() =>
        Verify(RenderBackend.Vulkan);

    private static void Verify(RenderBackend backend)
    {
        using var window = new HiddenNativeWindow(Width, Height);
        using var context = new RenderContext(backend, GpuPreference.Auto,
            RenderingEngine.Impeller);
        using var target = context.CreateRenderTarget(window.Hwnd, Width, Height);
        using var drawing = new RenderTargetDrawingContext(target, context);
        using var nativeBrush = context.CreateSolidBrush(1, 0, 0, 1);

        byte[] Paint(bool direct, double offset, PenLineCap dashCap = PenLineCap.Flat,
            bool batched = false, bool allZero = false)
        {
            target.SetFullInvalidation();
            var deadline = Environment.TickCount64 + 2000;
            while (!target.TryBeginDraw())
            {
                Assert.True(Environment.TickCount64 < deadline);
                Thread.Sleep(1);
            }
            target.Clear(0, 0, 0, 1);
            if (direct)
            {
                var pen = new Pen(new SolidColorBrush(Color.FromRgb(255, 0, 0)), 4)
                {
                    DashStyle = allZero
                        ? new DashStyle([0, 0], offset)
                        : new DashStyle([1, 2, 3], offset),
                    DashCap = dashCap,
                };
                if (batched)
                    drawing.DrawLines(pen, [new Point(8, 20), new Point(88, 20)]);
                else
                    drawing.DrawLine(pen, new Point(8, 20), new Point(88, 20));
            }
            else if (allZero)
                target.DrawLine(8, 20, 88, 20, nativeBrush, 4);
            else
            {
                target.StrokePath(8, 20, [0, 88, 20], nativeBrush, 4,
                    closed: false, lineCap: dashCap switch
                    {
                        PenLineCap.Square => 1,
                        PenLineCap.Round => 2,
                        _ => 0,
                    },
                    dashPattern: [4, 8, 12], dashOffset: (float)(offset * 4));
            }
            Assert.Equal(JaliumResult.Ok, target.RequestReadback());
            Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
            var pixels = new byte[Width * Height * 4];
            Assert.Equal(JaliumResult.Ok,
                target.FetchReadback(pixels, Width * 4u, out _, out _));
            return pixels;
        }

        foreach (var dashCap in new[] { PenLineCap.Flat, PenLineCap.Square, PenLineCap.Round })
        {
            foreach (var offset in new[] { 0.0, -1000.0, 1000.0 })
            {
                var expected = Paint(false, offset, dashCap);
                Assert.Equal(expected, Paint(true, offset, dashCap));
                Assert.Equal(expected, Paint(true, offset, dashCap, batched: true));
            }
        }
        Assert.Equal(Paint(false, 0, allZero: true), Paint(true, 0, allZero: true));
        Assert.Equal(Paint(false, 0, allZero: true), Paint(true, 0, batched: true, allZero: true));
    }
}

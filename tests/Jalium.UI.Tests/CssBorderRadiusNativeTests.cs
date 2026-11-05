using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class CssBorderRadiusNativeTests
{
    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_PaintsTheEllipticalBorderPath() => Verify(RenderBackend.Software);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_PaintsTheEllipticalBorderPath() => Verify(RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_PaintsTheEllipticalBorderPath() => Verify(RenderBackend.Vulkan);

    private static void Verify(RenderBackend backend)
    {
        const int width = 120, height = 60;
        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext(backend);
        using var target = context.CreateRenderTarget(window.Hwnd, width, height);
        Assert.Equal(backend, context.Backend);
        var border = CssBorderRadiusTests.Box("border-radius:50%; background-color:lime; border:4px solid blue; box-sizing:border-box", width, height);
        byte[] Paint(bool reference)
        {
            for (var frame = 0; frame < 2; frame++)
            {
                target.SetFullInvalidation();
                Assert.True(target.TryBeginDraw()); target.Clear(0, 0, 0, 1);
                using (var drawing = new RenderTargetDrawingContext(target, context))
                {
                    if (reference)
                    {
                        drawing.DrawEllipse(Brushes.Blue, null, new(width / 2, height / 2), width / 2, height / 2);
                        drawing.DrawEllipse(Brushes.Lime, null, new(width / 2, height / 2), width / 2 - 4, height / 2 - 4);
                    }
                    else border.Render(drawing);
                }
                if (frame == 1) Assert.Equal(JaliumResult.Ok, target.RequestReadback());
                Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
            }
            var bytes = new byte[width * height * 4];
            Assert.Equal(JaliumResult.Ok, target.FetchReadback(bytes, width * 4u, out var w, out var h));
            Assert.Equal(width, w); Assert.Equal(height, h);
            return bytes;
        }
        var actual = Paint(false); var expected = Paint(true);
        // Exact colour assertions away from antialiased contours; the native
        // path and ellipse primitives have different tessellation/AA kernels.
        foreach (var point in new[] { (2, 2), (60, 1), (1, 30), (60, 30), (60, 10), (119, 59) })
        {
            var offset = (point.Item2 * width + point.Item1) * 4;
            Assert.Equal(expected.AsSpan(offset, 4).ToArray(), actual.AsSpan(offset, 4).ToArray());
        }
        Assert.Equal((byte)255, actual[(30 * width + 60) * 4 + 1]);
        Assert.Equal((byte)255, actual[(1 * width + 60) * 4]);

        static int Light(byte[] pixels, int x, int y)
        {
            var offset = (y * width + x) * 4;
            return pixels[offset] + pixels[offset + 1] + pixels[offset + 2];
        }
        foreach (var (radius, x, y) in new[]
                 {
                     ("", 114, 5), ("border-radius:20px", 110, 9),
                 })
        {
            border = CssBorderRadiusTests.Box(
                "border:10px solid rgba(128,0,0,.5);" +
                "border-color:rgba(128,0,0,.5) rgba(0,128,0,.5) " +
                $"rgba(0,0,128,.5) rgba(64,0,64,.5);{radius}", width, height);
            var sides = Paint(false);
            border = CssBorderRadiusTests.Box(
                $"border:10px solid rgba(128,0,0,.5);{radius}", width, height);
            var solid = Paint(false);
            Assert.InRange(Math.Abs(Light(sides, x, y) - Light(solid, x, y)), 0, 10);

            border = CssBorderRadiusTests.Box(
                "border:10px solid;" +
                "border-color:rgba(128,128,128,.5) rgba(128,128,128,.25) " +
                $"rgba(128,128,128,.75) rgb(128,128,128);{radius}", width, height);
            var mixedAlpha = Paint(false);
            var expectedJointLight = (Light(mixedAlpha, 60, 5) + Light(mixedAlpha, 115, 30)) / 2;
            Assert.InRange(Math.Abs(Light(mixedAlpha, x, y) - expectedJointLight), 0, 16);

            border = CssBorderRadiusTests.Box(
                $"border:10px inset rgba(128,128,128,.5);{radius}", width, height);
            var relief = Paint(false);
            expectedJointLight = (Light(relief, 60, 5) + Light(relief, 115, 30)) / 2;
            Assert.InRange(Math.Abs(Light(relief, x, y) - expectedJointLight), 0, 16);
        }

        border = CssBorderRadiusTests.Box(
            "border:10px dotted rgba(128,128,128,.5)", width, height);
        var dotted = Paint(false);
        border = CssBorderRadiusTests.Box(
            "border:10px solid rgba(128,128,128,.5)", width, height);
        var solidGray = Paint(false);
        Assert.InRange(Math.Abs(Light(dotted, 5, 5) - Light(solidGray, 5, 5)), 0, 16);

        foreach (var (radius, outer, inner, gap) in new[]
                 {
                     ("", (118, 1), (112, 7), (115, 4)),
                     ("border-radius:20px", (113, 6), (109, 11), (111, 8)),
                 })
        {
            border = CssBorderRadiusTests.Box(
                $"border:9px double rgba(128,128,128,.5);{radius}", width, height);
            var doubleBands = Paint(false);
            border = CssBorderRadiusTests.Box(
                $"border:9px solid rgba(128,128,128,.5);{radius}", width, height);
            var solidBands = Paint(false);
            Assert.InRange(Math.Abs(Light(doubleBands, outer.Item1, outer.Item2) -
                Light(solidBands, outer.Item1, outer.Item2)), 0, 16);
            Assert.InRange(Math.Abs(Light(doubleBands, inner.Item1, inner.Item2) -
                Light(solidBands, inner.Item1, inner.Item2)), 0, 16);
            Assert.InRange(Light(doubleBands, gap.Item1, gap.Item2), 0, 16);

            border = CssBorderRadiusTests.Box(
                $"border:9px solid rgba(128,128,128,.5);" +
                $"border-style:solid double solid double;{radius}", width, height);
            var mixedBands = Paint(false);
            Assert.InRange(Math.Abs(Light(mixedBands, outer.Item1, outer.Item2) -
                Light(solidBands, outer.Item1, outer.Item2)), 0, 16);
            Assert.InRange(Math.Abs(Light(mixedBands, inner.Item1, inner.Item2) -
                Light(solidBands, inner.Item1, inner.Item2)), 0, 16);
            Assert.InRange(Math.Abs(Light(mixedBands, gap.Item1, gap.Item2) -
                Light(solidBands, gap.Item1, gap.Item2) / 2), 0, 16);
        }
    }
}

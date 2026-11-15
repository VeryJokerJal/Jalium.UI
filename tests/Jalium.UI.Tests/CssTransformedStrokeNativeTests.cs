using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssTransformedStrokeNativeTests
{
    private const int Size = 96;

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_nonuniform_transform_scales_direct_lines_across_their_normals()
    {
        var horizontal = Render((target, brush) =>
            target.DrawLine(10, 10, 30, 10, brush, 4));
        Assert.True(Red(horizontal, 40, 35) > 220);
        Assert.True(Red(horizontal, 40, 37) < 20);

        var vertical = Render((target, brush) =>
            target.DrawLine(10, 10, 10, 30, brush, 4));
        Assert.True(Red(vertical, 23, 50) > 220);
        Assert.True(Red(vertical, 25, 50) < 20);
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_nonuniform_transform_scales_path_strokes_across_their_normals()
    {
        var horizontal = Render((target, brush) =>
            target.StrokePath(10, 10, [0, 30, 10], brush, 4, closed: false));
        Assert.True(Red(horizontal, 40, 35) > 220);
        Assert.True(Red(horizontal, 40, 37) < 20);

        var vertical = Render((target, brush) =>
            target.StrokePath(10, 10, [0, 10, 30], brush, 4, closed: false));
        Assert.True(Red(vertical, 23, 50) > 220);
        Assert.True(Red(vertical, 25, 50) < 20);
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_rotated_line_retains_its_transformed_width()
    {
        // (x,y) -> (80 - 2y, 2x): a horizontal line becomes vertical at x=60.
        var pixels = Render((target, brush) =>
            target.DrawLine(10, 10, 30, 10, brush, 4),
            [0, 2, -2, 0, 80, 0]);
        Assert.True(Red(pixels, 63, 40) > 220);
        Assert.True(Red(pixels, 65, 40) < 20);
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_nonuniform_transform_gives_round_path_caps_elliptical_extents()
    {
        var pixels = Render((target, brush) =>
            target.StrokePath(10, 10, [0, 30, 10], brush, 4,
                closed: false, lineCap: 2));
        // Source radius 2 becomes horizontal radius 4 and vertical radius 6.
        Assert.True(Red(pixels, 17, 30) > 220);
        Assert.True(Red(pixels, 15, 30) < 20);
        Assert.True(Red(pixels, 20, 35) > 220);
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_miter_limit_uses_pen_half_width_and_fills_the_outer_corner()
    {
        VerifyMiter(RenderBackend.Software);
    }

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_miter_limit_uses_pen_half_width_and_fills_the_outer_corner() =>
        VerifyMiter(RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_miter_limit_uses_pen_half_width_and_fills_the_outer_corner() =>
        VerifyMiter(RenderBackend.Vulkan);

    private static void VerifyMiter(RenderBackend backend)
    {
        byte[] Corner(float limit) => Render((target, brush) =>
            target.StrokePath(8, 8, [0, 24, 8, 0, 24, 24], brush, 4,
                closed: false, lineJoin: 0, miterLimit: limit), backend: backend);

        // The 90-degree miter reaches sqrt(2) times the half-width, so limit
        // 2 admits the whole outer square; limit 1 falls back to a bevel.
        Assert.True(Red(Corner(2), 51, 18) > 220);
        Assert.True(Red(Corner(1), 51, 18) < 20);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Software_repeated_corner_vertex_keeps_the_same_join(int lineJoin)
    {
        byte[] Corner(float[] commands) => Render((target, brush) =>
            target.StrokePath(8, 8, commands, brush, 4,
                closed: false, lineJoin: lineJoin, miterLimit: 2));

        var ordinary = Corner([0, 24, 8, 0, 24, 24]);
        var repeated = Corner([0, 24, 8, 0, 24, 8, 0, 24, 24]);
        Assert.Equal(ordinary, repeated);
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_half_pixel_dashes_average_coverage_without_changing_phase()
    {
        var pixels = Render((target, brush) =>
            target.StrokePath(.02f, .05f, [0, .08f, .05f], brush, .004f,
                closed: false, dashPattern: [.0005f, .0005f]),
            [1000, 0, 0, 1000, 0, 0]);

        // Each on/off interval is half a device pixel. The line therefore
        // contributes about half coverage across every interior pixel.
        for (var x = 40; x < 45; x++)
            Assert.InRange(Red(pixels, x, 50), (byte)80, (byte)180);
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_closed_dash_path_includes_its_closing_edge_and_joins_the_seam()
    {
        byte[] Square(float[] commands, bool closed, float[] dashes) =>
            Render((target, brush) => target.StrokePath(8, 8, commands, brush, 4,
                closed: closed, lineJoin: 0, miterLimit: 2,
                dashPattern: dashes));

        float[] sides = [0, 24, 8, 0, 24, 24, 0, 8, 24];
        var implicitClose = Square(sides, true, [4, 2]);
        var explicitClose = Square([.. sides, 5], false, [4, 2]);
        Assert.Equal(explicitClose, implicitClose);
        Assert.True(Red(implicitClose, 16, 66) > 220);

        // The first and last on-runs meet at (8,8). They form one miter join.
        var seam = Square(sides, true, [10, 2]);
        Assert.True(Red(seam, 13, 19) > 220);
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_explicit_close_path_joins_solid_stroke_like_closed_flag()
    {
        byte[] Square(float[] commands, bool closed) =>
            Render((target, brush) => target.StrokePath(8, 8, commands, brush, 4,
                closed: closed, lineJoin: 0, miterLimit: 2));

        float[] sides = [0, 24, 8, 0, 24, 24, 0, 8, 24];
        Assert.Equal(Square(sides, true), Square([.. sides, 5], false));
    }

    private static byte[] Render(Action<RenderTarget, NativeBrush> paint,
        float[]? transform = null, RenderBackend backend = RenderBackend.Software)
    {
        using var window = new HiddenNativeWindow(Size, Size);
        using var context = backend == RenderBackend.Software
            ? new RenderContext(backend)
            : new RenderContext(backend, GpuPreference.Auto, RenderingEngine.Impeller);
        using var target = context.CreateRenderTarget(window.Hwnd, Size, Size);
        using var brush = context.CreateSolidBrush(1, 0, 0, 1);
        target.SetFullInvalidation();
        var deadline = Environment.TickCount64 + 2000;
        while (!target.TryBeginDraw())
        {
            Assert.True(Environment.TickCount64 < deadline);
            Thread.Sleep(1);
        }
        target.Clear(0, 0, 0, 1);
        target.PushTransform(transform ?? [2, 0, 0, 3, 0, 0]);
        paint(target, brush);
        target.PopTransform();
        Assert.Equal(JaliumResult.Ok, target.RequestReadback());
        Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
        var pixels = new byte[Size * Size * 4];
        Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, Size * 4u, out _, out _));
        return pixels;
    }

    private static byte Red(byte[] pixels, int x, int y) => pixels[(y * Size + x) * 4 + 2];
}

using Jalium.UI.Interop;
using Xunit;

namespace Jalium.UI.Tests;

// 复现"图标线条呈半透明"：D3D12 离屏 StrokePath 后逐像素读回，
// 线条中心必须是满 alpha 的纯色；两条线交叠处与单线中心必须同色
// （半透明线条会在交叠处二次混合变亮/变深）。
public sealed class StrokeLineOpacityProbeTests
{
    private const int Width = 96;
    private const int Height = 96;

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_StrokePath_line_center_is_fully_opaque()
    {
        var pixels = Render((target, brush) =>
        {
            // 水平线 M12,48 L84,48，宽 4，round cap/join —— lucide 图标的典型形态
            float[] cmds = [0f, 84f, 48f];
            target.StrokePath(12f, 48f, cmds, cmds.Length, brush,
                strokeWidth: 4f, closed: false, lineJoin: 2, miterLimit: 4f, lineCap: 2);
        });

        AssertOpaqueWhite(pixels, 48, 48);
        AssertOpaqueWhite(pixels, 30, 48);
        AssertOpaqueWhite(pixels, 66, 48);
    }

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_StrokePath_cross_center_matches_line_body()
    {
        var pixels = Render((target, brush) =>
        {
            float[] a = [0f, 84f, 84f];
            target.StrokePath(12f, 12f, a, a.Length, brush,
                strokeWidth: 4f, closed: false, lineJoin: 2, miterLimit: 4f, lineCap: 2);
            float[] b = [0f, 12f, 84f];
            target.StrokePath(84f, 12f, b, b.Length, brush,
                strokeWidth: 4f, closed: false, lineJoin: 2, miterLimit: 4f, lineCap: 2);
        });

        // 单线主体（远离交叉点）与交叉中心取同一通道值比较。
        var body = GetBlue(pixels, 30, 30);
        var cross = GetBlue(pixels, 48, 48);
        Assert.True(body >= 250, $"line body blue={body}, expected opaque (>=250)");
        Assert.True(cross >= 250, $"cross center blue={cross}, expected opaque (>=250)");
    }

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_StrokePath_thin_scaled_line_center_is_fully_opaque()
    {
        var pixels = Render((target, brush) =>
        {
            // Jalium.One 真实形态：几何烘到 16px 画布、thickness 1.4，DPI 1.5 由 transform 提供
            float[] m = [1.5f, 0f, 0f, 1.5f, 0f, 0f];
            target.PushTransform(m);
            float[] cmds = [0f, 56f, 20f];
            target.StrokePath(8f, 20f, cmds, cmds.Length, brush,
                strokeWidth: 1.4f, closed: false, lineJoin: 2, miterLimit: 4f, lineCap: 2);
            target.PopTransform();
        });

        // 设备空间线心 y = 20*1.5 = 30；线宽 2.1px，中心像素应满覆盖
        var best = 0;
        for (var y = 28; y <= 32; y++)
        {
            var v = GetBlue(pixels, 48, y);
            if (v > best) best = v;
        }
        Assert.True(best >= 250, $"thin line peak blue={best}, expected opaque (>=250)");
    }

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_DrawPolygon_open_line_center_is_fully_opaque()
    {
        var pixels = Render((target, brush) =>
        {
            // 直线-only figure 走 DrawPolygon（managed DrawPathFigurePolygon 的路径）
            float[] pts = [12f, 48f, 84f, 48f];
            target.DrawPolygon(pts, brush, 4f, closed: false, lineJoin: 2, miterLimit: 4f);
        });

        AssertOpaqueWhite(pixels, 48, 48);
    }

    private static void AssertOpaqueWhite(byte[] pixels, int x, int y)
    {
        var i = (y * Width + x) * 4;
        var b = pixels[i];
        var g = pixels[i + 1];
        var r = pixels[i + 2];
        Assert.True(r >= 250 && g >= 250 && b >= 250,
            $"({x},{y}) = B{b} G{g} R{r}, expected opaque white");
    }

    private static byte GetBlue(byte[] pixels, int x, int y)
        => pixels[(y * Width + x) * 4];

    private static byte[] Render(Action<RenderTarget, NativeBrush> draw)
    {
        using var window = new HiddenNativeWindow(Width, Height);
        using var context = new RenderContext(
            RenderBackend.D3D12, GpuPreference.Auto, RenderingEngine.Impeller);
        using var brush = context.CreateSolidBrush(1f, 1f, 1f, 1f);
        using var target = context.CreateRenderTarget(window.Hwnd, Width, Height);

        Assert.True(brush.IsValid);
        Assert.True(target.IsValid);

        Assert.True(target.TryBeginDraw());
        target.Clear(0f, 0f, 0f);
        draw(target, brush);
        Assert.Equal(JaliumResult.Ok, target.RequestReadback());
        Assert.Equal(JaliumResult.Ok, target.TryEndDraw());

        var pixels = new byte[Width * Height * 4];
        Assert.Equal(
            JaliumResult.Ok,
            target.FetchReadback(pixels, Width * 4u, out var w, out var h));
        Assert.Equal(Width, w);
        Assert.Equal(Height, h);
        return pixels;
    }
}

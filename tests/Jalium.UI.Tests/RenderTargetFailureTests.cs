using Jalium.UI;
using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

public class RenderTargetFailureTests
{
    [Fact]
    public void Resize_WhenNativeFails_ThrowsRenderPipelineExceptionWithMetadata()
    {
        var native = new RenderTargetTestNative
        {
            ResizeResult = (int)JaliumResult.DeviceLost
        };

        using var renderTarget = CreateRenderTarget(native, width: 640, height: 480);

        var exception = Assert.Throws<RenderPipelineException>(() => renderTarget.Resize(800, 600));

        Assert.Equal("Resize", exception.Stage);
        Assert.Equal(JaliumResult.DeviceLost, exception.Result);
        Assert.Equal((int)JaliumResult.DeviceLost, exception.ResultCode);
        Assert.Equal(new nint(0x1234), exception.Hwnd);
        Assert.Equal(640, exception.Width);
        Assert.Equal(480, exception.Height);
        Assert.Equal(96.0f, exception.DpiX);
        Assert.Equal(96.0f, exception.DpiY);
        Assert.Equal(RenderBackend.D3D12.ToString(), exception.Backend);
    }

    [Fact]
    public void BeginDraw_WhenNativeFails_ThrowsRenderPipelineExceptionAndKeepsDrawingFalse()
    {
        var native = new RenderTargetTestNative
        {
            BeginDrawResult = (int)JaliumResult.InvalidState
        };

        using var renderTarget = CreateRenderTarget(native);

        var exception = Assert.Throws<RenderPipelineException>(() => renderTarget.BeginDraw());

        Assert.Equal("Begin", exception.Stage);
        Assert.Equal(JaliumResult.InvalidState, exception.Result);
        Assert.False(renderTarget.IsDrawing);
    }

    [Fact]
    public void EndDraw_WhenNativeFails_ThrowsRenderPipelineException()
    {
        var native = new RenderTargetTestNative
        {
            EndDrawResult = (int)JaliumResult.DeviceLost
        };

        using var renderTarget = CreateRenderTarget(native);
        renderTarget.BeginDraw();

        var exception = Assert.Throws<RenderPipelineException>(() => renderTarget.EndDraw());

        Assert.Equal("End", exception.Stage);
        Assert.Equal(JaliumResult.DeviceLost, exception.Result);
        Assert.False(renderTarget.IsDrawing);
    }

    private static RenderTarget CreateRenderTarget(RenderTargetTestNative native, int width = 320, int height = 240)
    {
        return new RenderTarget(
            backend: RenderBackend.D3D12,
            contextHandle: new nint(0x1111),
            hwnd: new nint(0x1234),
            width: width,
            height: height,
            useComposition: false,
            native: native);
    }
}

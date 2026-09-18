using System.Reflection;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;

namespace Jalium.UI.Tests;

public sealed class D3DImageRenderThreadSafetyTests
{
    [Fact]
    public void WholeFrameCapture_D3DImageDraw_IsMarkedUnrecordable()
    {
        using var image = new D3DImage();
        image.SetPixelSize(4, 3);

        var drawing = Capture(recorder =>
            recorder.DrawImage(image, new Rect(0, 0, 4, 3)));

        Assert.False(drawing.IsFullyRecordable);
    }

    [Fact]
    public void WholeFrameCapture_D3DImageBrush_IsMarkedUnrecordable()
    {
        using var image = new D3DImage();
        image.SetPixelSize(4, 3);

        var drawing = Capture(recorder =>
            recorder.DrawRectangle(
                new ImageBrush(image),
                pen: null,
                new Rect(0, 0, 4, 3)));

        Assert.False(drawing.IsFullyRecordable);
    }

    [Fact]
    public void NativeHandle_DoesNotRetainDisposedVideoSurfaceHandle()
    {
        const nint originalHandle = 42;
        NativeVideoSurface surface = CreateSurfaceStub(originalHandle);
        using var image = new D3DImage();

        image.SetBackBuffer(surface);
        Assert.Equal(originalHandle, image.NativeHandle);

        ClearSurfaceHandle(surface);

        Assert.True(surface.IsDisposed);
        Assert.Equal(nint.Zero, image.NativeHandle);
    }

    [Fact]
    public void CopyBackBuffer_WithSizedNativeResource_FailsExplicitly()
    {
        using var image = new D3DImage();
        image.SetPixelSize(2, 2);

        Assert.Throws<NotSupportedException>(() => image.CopyBackBuffer());
    }

    private static RecordedDrawing Capture(Action<DrawingContext> draw)
    {
        var host = new MediaRenderCacheHost();
        var recorder = host.CreateFrameRecorder();
        draw(recorder);
        return (RecordedDrawing)host.FinishRecord(recorder);
    }

    private static NativeVideoSurface CreateSurfaceStub(nint handle)
    {
        ConstructorInfo constructor = typeof(NativeVideoSurface).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(nint), typeof(int), typeof(int), typeof(NativeVideoSurfaceKind)],
            modifiers: null)!;

        return (NativeVideoSurface)constructor.Invoke(
            [handle, 4, 3, NativeVideoSurfaceKind.Bgra8Cpu]);
    }

    private static void ClearSurfaceHandle(NativeVideoSurface surface)
    {
        FieldInfo handleField = typeof(NativeVideoSurface).GetField(
            "_handle",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        handleField.SetValue(surface, nint.Zero);
    }
}

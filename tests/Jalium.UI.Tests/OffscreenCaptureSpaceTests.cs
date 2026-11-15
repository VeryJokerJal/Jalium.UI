using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class OffscreenCaptureSpaceTests
{
    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void TransitionCapture_UsesCaptureClipAcrossChildTransforms_AndRestoresDamageClip()
    {
        WithContext(drawing =>
        {
            drawing.PushDirtyRegionClip(new Rect(8, 8, 8, 8));
            var damageClip = drawing.CurrentClipBounds;
            drawing.Offset = new Point(80, 60);
            drawing.BeginTransitionCapture(0, new Rect(0, 0, 100, 80));
            Assert.Equal(new Rect(80, 60, 100, 80), drawing.CurrentClipBounds);
            drawing.PushTransform(new ScaleTransform(2, 2));
            Assert.Equal(new Rect(80, 60, 50, 40), drawing.CurrentClipBounds);
            drawing.Pop();
            drawing.EndTransitionCapture(0);
            Assert.Equal(damageClip, drawing.CurrentClipBounds);
            drawing.PopDirtyRegionClip();
        });
    }

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void RetainedCapture_MapsClipBackThroughChildTransform_AndHandlesSingularTransforms()
    {
        WithContext(drawing =>
        {
            drawing.Offset = new Point(80, 60);
            var layer = drawing.BeginLayerCapture(0, new Rect(80, 60, 100, 80));
            Assert.NotEqual(0, layer);
            try
            {
                drawing.PushTransform(new ScaleTransform(2, 2));
                Assert.Equal(new Rect(80, 60, 50, 40), drawing.CurrentClipBounds);
                drawing.Pop();
                drawing.PushTransform(new ScaleTransform(0, 0));
                Assert.Null(drawing.CurrentClipBounds);
                drawing.Pop();
            }
            finally
            {
                drawing.EndLayerCapture(layer);
                drawing.RenderTarget.DestroyRetainedLayer(layer);
            }
            Assert.Null(drawing.CurrentClipBounds);
        });
    }

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void RefusedNestedTransition_DoesNotEndItsParentEffectCapture()
    {
        WithContext(drawing =>
        {
            drawing.BeginEffectCapture(80, 60, 100, 80);
            drawing.Offset = new Point(80, 60);
            drawing.BeginTransitionCapture(1, new Rect(0, 0, 20, 20));
            drawing.EndTransitionCapture(1);
            drawing.PushTransform(new ScaleTransform(1.2, 1.2));
            drawing.DrawRectangle(Brushes.Lime, null, new Rect(10, 10, 10, 10));
            drawing.Pop();
            drawing.EndEffectCapture();
            drawing.DrawCapturedTransition(0, new Rect(0, 0, 100, 80), 1);
            Assert.Equal(JaliumResult.Ok, drawing.RenderTarget.RequestReadback());
        }, pixels =>
        {
            AssertGreen(pixels, 97, 77);
            AssertBlack(pixels, 177, 137);
        });
    }

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void UnbalancedTransitionEnd_DoesNotPopAnUnrelatedCapture()
    {
        WithContext(drawing =>
        {
            drawing.BeginTransitionCapture(0, new Rect(80, 60, 100, 80));
            drawing.EndTransitionCapture(1);
            Assert.Equal(new Rect(80, 60, 100, 80), drawing.CurrentClipBounds);
            drawing.EndTransitionCapture(0);
            Assert.Null(drawing.CurrentClipBounds);
            drawing.EndTransitionCapture(0);
            Assert.Null(drawing.CurrentClipBounds);
        });
    }

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void EmptyTransitionCapture_DoesNotReplaceTheCurrentClip()
    {
        WithContext(drawing =>
        {
            drawing.PushDirtyRegionClip(new Rect(8, 8, 8, 8));
            var clip = drawing.CurrentClipBounds;
            drawing.BeginTransitionCapture(0, new Rect(80, 60, 0, 20));
            Assert.Equal(clip, drawing.CurrentClipBounds);
            drawing.EndTransitionCapture(0);
            Assert.Equal(clip, drawing.CurrentClipBounds);
            drawing.PopDirtyRegionClip();
        });
    }

    private static void WithContext(Action<RenderTargetDrawingContext> body, Action<byte[]>? inspect = null)
    {
        const int size = 256;
        using var window = new HiddenNativeWindow(size, size);
        using var context = new RenderContext(RenderBackend.D3D12);
        using var target = context.CreateRenderTarget(window.Hwnd, size, size);
        target.SetDpi(96, 96);
        var deadline = Environment.TickCount64 + 3000;
        while (!target.TryBeginDraw())
        { Assert.True(Environment.TickCount64 < deadline); Thread.Sleep(1); }
        target.Clear(0, 0, 0);
        using (var drawing = new RenderTargetDrawingContext(target, context))
            body(drawing);
        Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
        if (inspect is null) return;
        var pixels = new byte[size * size * 4];
        Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, size * 4u, out _, out _));
        inspect(pixels);
    }

    private static void AssertGreen(byte[] pixels, int x, int y)
    {
        var index = (y * 256 + x) * 4;
        Assert.True(pixels[index + 1] > 150 && pixels[index] < 40 && pixels[index + 2] < 40,
            $"Expected green content at ({x}, {y}); actual BGRA={string.Join(",", pixels.AsSpan(index, 4).ToArray())}.");
    }

    private static void AssertBlack(byte[] pixels, int x, int y)
    {
        var index = (y * 256 + x) * 4;
        Assert.True(pixels[index] < 20 && pixels[index + 1] < 20 && pixels[index + 2] < 20);
    }
}

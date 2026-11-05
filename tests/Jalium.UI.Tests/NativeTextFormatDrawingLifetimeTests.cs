using System.Reflection;
using System.Runtime.CompilerServices;
using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class NativeTextFormatDrawingLifetimeTests
{
    [RequiresWindowsFact]
    public void DrawLease_PreservesNativeFormatAndRetiredBackendUntilLastUse()
    {
        using var context = new RenderContext(RenderBackend.Software);
        using var format = context.CreateTextFormat("Segoe UI", 16f);
        Assert.True(format.TryAcquireNativeUse(out var use));
        var nativeHandle = use.Handle;
        try
        {
            context.Dispose();
            var disposer = new Thread(format.Dispose) { IsBackground = true };
            disposer.Start();
            Assert.True(disposer.Join(TimeSpan.FromSeconds(5)), "Disposal must not wait for an in-flight native call.");

            Assert.False(format.IsValid);
            Assert.Equal(nativeHandle, format.Handle);
            Assert.NotEqual(nint.Zero, context.Handle);
            Assert.Equal(0, NativeMethods.TextFormatGetFontMetrics(use.Handle, out var metrics));
            Assert.True(metrics.LineHeight > 0);
            Assert.False(format.TryAcquireNativeUse(out var rejected));
            rejected.Dispose();
            Assert.Equal(nativeHandle, format.Handle);
        }
        finally
        {
            use.Dispose();
        }

        Assert.Equal(nint.Zero, format.Handle);
        Assert.Equal(nint.Zero, context.Handle);
        use.Dispose();
    }

    [RequiresWindowsFact]
    public void BothDrawEntrypoints_RenderLiveTextAndSkipDisposedFormat()
    {
        const int width = 192;
        const int height = 64;
        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext(RenderBackend.Software);
        using var target = context.CreateRenderTarget(window.Hwnd, width, height);
        using var white = context.CreateSolidBrush(1f, 1f, 1f, 1f);
        float[] inverse = [1f, 0f, 0f, 1f, 0f, 0f];

        foreach (bool withInverse in new[] { false, true })
        {
            using var format = context.CreateTextFormat("Segoe UI", 20f);
            var livePixels = DrawAndRead(format, withInverse);
            Assert.True(livePixels.Where((_, index) => index % 4 != 3).Any(value => value != 0),
                "A live format must produce visible text through each native entrypoint.");

            format.Dispose();
            var disposedPixels = DrawAndRead(format, withInverse);
            Assert.All(disposedPixels.Where((_, index) => index % 4 != 3), value => Assert.Equal((byte)0, value));
        }

        byte[] DrawAndRead(NativeTextFormat format, bool withInverse)
        {
            Assert.True(target.TryBeginDraw());
            target.Clear(0f, 0f, 0f, 1f);
            if (withInverse)
                target.DrawTextWithInverseTransform("Lifetime", format, 4f, 4f, 180f, 48f, white, inverse);
            else
                target.DrawText("Lifetime", format, 4f, 4f, 180f, 48f, white);

            Assert.Equal(JaliumResult.Ok, target.RequestReadback());
            Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
            var pixels = new byte[width * height * 4];
            Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, width * 4u, out var actualWidth, out var actualHeight));
            Assert.Equal(width, actualWidth);
            Assert.Equal(height, actualHeight);
            return pixels;
        }
    }

    [Fact]
    public void Finalizer_BeforeFieldInitialization_IsSafe()
    {
        var uninitialized = (NativeTextFormat)RuntimeHelpers.GetUninitializedObject(typeof(NativeTextFormat));
        GC.SuppressFinalize(uninitialized);
        var finalizer = typeof(NativeTextFormat).GetMethod("Finalize", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(finalizer);
        finalizer.Invoke(uninitialized, null);
        Assert.Equal(nint.Zero, uninitialized.Handle);
    }
}

using System.Reflection;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class NativeSolidBrushCacheTests
{
    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_EqualSolidValuesShareResourcesWithoutSharingMutation() => Verify(RenderBackend.Software);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_EqualSolidValuesShareResourcesWithoutSharingMutation() => Verify(RenderBackend.D3D12);

    private static void Verify(RenderBackend backend)
    {
        using var window = new HiddenNativeWindow(32, 32);
        using var context = new RenderContext(backend);
        using var target = context.CreateRenderTarget(window.Hwnd, 32, 32);
        using var drawing = new RenderTargetDrawingContext(target, context);
        var method = typeof(RenderTargetDrawingContext).GetMethod("GetNativeBrush",
            BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(Brush)], null)!;
        NativeBrush Get(Brush brush) => (NativeBrush)method.Invoke(drawing, [brush])!;

        var first = new SolidColorBrush(Colors.Red) { Opacity = 0.5 };
        var original = Get(first);
        var equal = Get(new SolidColorBrush(Colors.Red) { Opacity = 0.5 });
        Assert.Same(original, equal);
        Assert.Equal(0.5f, original.A);

        first.Color = Colors.Blue;
        var changed = Get(first);
        Assert.NotSame(original, changed);
        Assert.True(original.IsValid);
        Assert.Equal(1f, original.R);
        Assert.Equal(1f, changed.B);
        first.Color = Colors.Red;
        Assert.Same(original, Get(first));
        first.Opacity = 0.25;
        Assert.NotSame(original, Get(first));

        var alphaInColor = Get(new SolidColorBrush(Color.FromArgb(64, 1, 2, 3)));
        var alphaInOpacity = Get(new SolidColorBrush(Color.FromArgb(128, 1, 2, 3)) { Opacity = 0.5 });
        Assert.Same(alphaInColor, alphaInOpacity);

        drawing.ClearCache();
        Assert.False(original.IsValid);
        Assert.False(changed.IsValid);
        Assert.False(alphaInColor.IsValid);
    }
}

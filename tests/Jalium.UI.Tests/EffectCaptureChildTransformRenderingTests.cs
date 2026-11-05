using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Effects;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class EffectCaptureChildTransformRenderingTests
{
    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_Impeller_ChildRotationInsideShadowCaptureKeepsItsPosition() =>
        Verify(RenderBackend.D3D12, RenderingEngine.Impeller);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_Vello_ChildRotationInsideShadowCaptureKeepsItsPosition() =>
        Verify(RenderBackend.D3D12, RenderingEngine.Vello);

    private static void Verify(RenderBackend backend, RenderingEngine engine)
    {
        const int width = 512;
        const int height = 384;
        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext(backend, GpuPreference.Auto, engine);
        using var target = context.CreateRenderTarget(window.Hwnd, width, height);
        Assert.Equal(backend, context.Backend);
        Assert.Equal(engine, context.DefaultRenderingEngine);

        (double Angle, float Dpi, double Scale, bool Nested)[] cases =
        [
            (0, 96, 1, false),
            (90, 96, 1, false),
            (45, 144, 1.08, false),
            (90, 144, 1.08, true),
        ];
        foreach (var test in cases)
        {
            target.SetDpi(test.Dpi, test.Dpi);
            for (var frame = 0; frame < 2; frame++)
            {
                target.SetFullInvalidation();
                var wait = System.Diagnostics.Stopwatch.StartNew();
                while (!target.TryBeginDraw())
                {
                    Assert.True(wait.ElapsedMilliseconds < 1000, "Render target was not ready.");
                    Thread.Sleep(1);
                }
                target.Clear(0, 0, 0);
                using (var drawing = new RenderTargetDrawingContext(target, context))
                {
                    DrawCapturedArrow(drawing, test.Angle, test.Scale, test.Nested);
                }
                if (frame == 1) Assert.Equal(JaliumResult.Ok, target.RequestReadback());
                Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
            }

            var pixels = new byte[width * height * 4];
            Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, width * 4u, out var capturedWidth, out var capturedHeight));
            Assert.Equal(width, capturedWidth);
            Assert.Equal(height, capturedHeight);

            var matrix = Matrix.Identity;
            matrix.RotateAt(test.Angle, 118, 94);
            matrix.Scale(test.Scale, test.Scale);
            var dpiScale = test.Dpi / 96d;
            var expected = RenderTargetDrawingContext.ComputeScreenEffectCaptureRect(
                new Rect(112, 88, 12, 12), matrix, dpiScale, dpiScale);
            var count = 0;
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var offset = (y * width + x) * 4;
                if (pixels[offset] <= 100 || pixels[offset + 1] <= 100 || pixels[offset + 2] <= 100) continue;
                count++;
                Assert.InRange(x, expected.Left * dpiScale - 2, expected.Right * dpiScale + 2);
                Assert.InRange(y, expected.Top * dpiScale - 2, expected.Bottom * dpiScale + 2);
            }
            Assert.True(count >= 8,
                $"{backend}/{engine}, angle={test.Angle}, dpi={test.Dpi}, nested={test.Nested}: child stroke disappeared ({count} pixels).");
        }
    }

    private static void DrawCapturedArrow(RenderTargetDrawingContext drawing, double angle, double scale, bool nested)
    {
        var offset = (IOffsetDrawingContext)drawing;
        var transforms = (ITransformDrawingContext)drawing;
        var shadow = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 2, Color = Colors.Black, Opacity = 0.4 };
        transforms.PushTransform(new ScaleTransform(scale, scale), 0, 0);
        drawing.BeginEffectCapture(80, 60, 160, 112);
        offset.Offset = new Point(80, 60);
        drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(20, 20, 20)), null, new Rect(0, 0, 160, 112));

        if (nested) drawing.BeginEffectCapture(96, 76, 128, 80);
        offset.Offset = new Point(112, 88);
        transforms.PushTransform(new RotateTransform(angle), 6, 6);
        drawing.DrawGeometry(null, new Pen(Brushes.White, 1.5), Geometry.Parse("M3 1 L9 6 L3 11"));
        transforms.PopTransform();
        if (nested)
        {
            drawing.EndEffectCapture();
            drawing.ApplyElementEffect(shadow, 96, 76, 128, 80, 96, 76);
        }

        drawing.EndEffectCapture();
        drawing.ApplyElementEffect(shadow, 80, 60, 160, 112, 80, 60);
        transforms.PopTransform();
        offset.Offset = Point.Zero;
    }
}

using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class UniformScaleTextRenderingTests
{
    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void UniformHoverZoom_KeepsTextAlignedWithItsTransformedLayout()
    {
        const int width = 1200;
        const int height = 700;
        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext(RenderBackend.D3D12, GpuPreference.Auto, RenderingEngine.Vello);
        using var target = context.CreateRenderTarget(window.Hwnd, width, height);
        using var drawing = new RenderTargetDrawingContext(target, context);

        foreach (var dpi in new[] { 96f, 120f, 144f, 192f })
        {
            target.SetDpi(dpi, dpi);
            var text = new TextBlock
            {
                Text = "搜索命令、模板与最近项目…",
                FontSize = 14,
                Foreground = Brushes.White,
                Margin = new Thickness(42, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            TextOptions.SetTextFormattingMode(text, TextFormattingMode.Ideal);
            TextOptions.SetTextRenderingMode(text, TextRenderingMode.Grayscale);
            var row = new Grid { Height = 46, VerticalAlignment = VerticalAlignment.Top };
            row.Children.Add(text);
            var host = new Border
            {
                Width = 396,
                Height = 46,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(120, 220, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                RenderTransformOrigin = new Point(.5, .5),
                TransitionDuration = TimeSpan.Zero,
                Child = row,
            };
            var root = new Grid();
            root.Children.Add(host);
            var size = new Size(width * 96 / dpi, height * 96 / dpi);
            root.Measure(size);
            root.Arrange(new Rect(Point.Zero, size));

            double? restCenter = null;
            foreach (var scale in new[] { 1.0, 1.005, 1.015, 1.03, 1.01, 1.0 })
            {
                host.RenderTransform = scale == 1 ? null : new ScaleTransform(scale, scale);
                for (var frame = 0; frame < 2; frame++)
                {
                    target.SetFullInvalidation();
                    var wait = System.Diagnostics.Stopwatch.StartNew();
                    while (!target.TryBeginDraw())
                    {
                        Assert.True(wait.ElapsedMilliseconds < 3000, "Render target stayed busy.");
                        Thread.Sleep(1);
                    }
                    target.Clear(0, 0, 0);
                    root.Render(drawing);
                    if (frame == 1) Assert.Equal(JaliumResult.Ok, target.RequestReadback());
                    Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
                }

                var pixels = new byte[width * height * 4];
                Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, width * 4u, out _, out _));
                var bounds = text.TransformToVisual(root)!.TransformBounds(new Rect(Point.Zero, text.RenderSize));
                var dpiScale = dpi / 96d;
                double ink = 0, moment = 0;
                for (var y = (int)Math.Floor((bounds.Top - 1) * dpiScale);
                     y < Math.Ceiling((bounds.Bottom + 1) * dpiScale); y++)
                for (var x = (int)Math.Floor((bounds.Left - 1) * dpiScale);
                     x < Math.Ceiling((bounds.Right + 1) * dpiScale); x++)
                {
                    var i = (y * width + x) * 4;
                    var coverage = Math.Min(pixels[i], Math.Min(pixels[i + 1], pixels[i + 2]));
                    ink += coverage;
                    moment += (y + .5) * coverage;
                }
                Assert.True(ink > 100, "The label disappeared.");
                var center = moment / ink;
                restCenter ??= center;
                var pivotY = (220 + 46 / 2d) * dpiScale;
                var expected = pivotY + (restCenter.Value - pivotY) * scale;
                Assert.True(Math.Abs(center - expected) < .55,
                    $"DPI {dpi}, scale {scale}: text moved {center - expected:F3} physical pixels away from its layout.");
            }
        }
    }
}

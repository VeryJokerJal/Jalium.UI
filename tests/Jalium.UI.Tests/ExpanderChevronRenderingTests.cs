using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Threading;
using ShapePath = Jalium.UI.Shapes.Path;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class ExpanderChevronRenderingTests
{
    [Fact]
    public void InitiallyExpandedChevron_DoesNotMoveWhenCollapseAnimationStarts()
    {
        using var fixture = new ChevronFixture(expanded: true);
        AssertCentered(fixture);
        var before = RenderSoftware(fixture.Root);

        fixture.Expander.IsExpanded = false;

        AssertCentered(fixture);
        Assert.Equal(before, RenderSoftware(fixture.Root));
    }

    [Fact]
    public void AnimatedChevron_DoesNotAcquireASecondPivotAfterDetach()
    {
        using var fixture = new ChevronFixture(expanded: false);
        fixture.Expander.IsExpanded = true;
        fixture.StopAnimation();
        fixture.Root.Child = null;

        fixture.Expander.IsExpanded = false;
        fixture.Expander.IsExpanded = true;

        AssertCentered(fixture);
    }

    [Fact]
    public void CollapsedTemplate_ResetsAnExistingChevronRotation()
    {
        using var fixture = new ChevronFixture(expanded: false, initialAngle: 90);

        var rotation = Assert.IsType<RotateTransform>(fixture.Arrow.RenderTransform);
        Assert.Equal(0, rotation.Angle);
        AssertCentered(fixture);
    }

    [Fact]
    public void InterruptedAnimation_KeepsThePivotCenteredAfterArrowResize()
    {
        using var fixture = new ChevronFixture(expanded: true);
        fixture.Expander.IsExpanded = false;
        fixture.StopAnimation();
        fixture.Arrow.Width = 12;
        fixture.Arrow.Height = 12;
        fixture.Arrange();

        AssertCentered(fixture);
        fixture.Expander.IsExpanded = true;
        AssertCentered(fixture);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(90)]
    public void RotatedStroke_RemainsVisibleInItsClippedSlot(double angle)
    {
        using var fixture = new ChevronFixture(expanded: false);
        fixture.Arrow.RenderTransformOrigin = new Point(0.5, 0.5);
        fixture.Arrow.RenderTransform = new RotateTransform(angle);

        AssertCentered(fixture);
        AssertVisible(RenderSoftware(fixture.Root), $"Software/{angle}");
    }

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_Impeller_RotatedExpanderChevronRemainsVisible() =>
        AssertGpuRotation(RenderBackend.D3D12, RenderingEngine.Impeller);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_Vello_RotatedExpanderChevronRemainsVisible() =>
        AssertGpuRotation(RenderBackend.D3D12, RenderingEngine.Vello);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_Impeller_RotatedExpanderChevronRemainsVisible() =>
        AssertGpuRotation(RenderBackend.Vulkan, RenderingEngine.Impeller);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_Vello_RotatedExpanderChevronRemainsVisible() =>
        AssertGpuRotation(RenderBackend.Vulkan, RenderingEngine.Vello);

    private static void AssertGpuRotation(RenderBackend backend, RenderingEngine engine)
    {
        using var fixture = new ChevronFixture(expanded: true);
        using var window = new HiddenNativeWindow(ChevronFixture.Size, ChevronFixture.Size);
        using var context = new RenderContext(backend, GpuPreference.Auto, engine);
        using var target = context.CreateRenderTarget(window.Hwnd, ChevronFixture.Size, ChevronFixture.Size);
        target.SetDpi(96, 96);

        foreach (var angle in new[] { 0d, 45d, 90d })
        {
            fixture.Arrow.RenderTransformOrigin = new Point(0.5, 0.5);
            fixture.Arrow.RenderTransform = new RotateTransform(angle);
            for (var frame = 0; frame < 2; frame++)
            {
                target.SetFullInvalidation();
                var wait = System.Diagnostics.Stopwatch.StartNew();
                while (!target.TryBeginDraw())
                {
                    Assert.True(wait.ElapsedMilliseconds < 1000, "GPU render target was not ready.");
                    Thread.Sleep(1);
                }
                target.Clear(0, 0, 0);
                using (var drawing = new RenderTargetDrawingContext(target, context))
                {
                    fixture.Root.Render(drawing);
                }
                if (frame == 1) Assert.Equal(JaliumResult.Ok, target.RequestReadback());
                Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
            }
            var pixels = new byte[ChevronFixture.Size * ChevronFixture.Size * 4];
            Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, ChevronFixture.Size * 4u, out var width, out var height));
            Assert.Equal(ChevronFixture.Size, width);
            Assert.Equal(ChevronFixture.Size, height);
            AssertVisible(pixels, $"{backend}/{engine}/{angle}");
        }
    }

    private static void AssertCentered(ChevronFixture fixture)
    {
        var bounds = fixture.Arrow.TransformToVisual(fixture.Slot)!
            .TransformBounds(new Rect(0, 0, fixture.Arrow.ActualWidth, fixture.Arrow.ActualHeight));
        Assert.InRange(Math.Abs(bounds.Left + bounds.Width / 2 - fixture.Slot.ActualWidth / 2), 0, 0.01);
        Assert.InRange(Math.Abs(bounds.Top + bounds.Height / 2 - fixture.Slot.ActualHeight / 2), 0, 0.01);
    }

    private static void AssertVisible(byte[] pixels, string context)
    {
        var visible = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i] > 80 && pixels[i + 1] > 80 && pixels[i + 2] > 80) visible++;
        }
        Assert.True(visible >= 8, $"{context}: rotated chevron only painted {visible} visible pixels.");
    }

    private static byte[] RenderSoftware(Border root)
    {
        var bitmap = new RenderTargetBitmap(ChevronFixture.Size, ChevronFixture.Size, 96, 96, PixelFormat.Bgra32);
        bitmap.Render(root);
        var pixels = new byte[ChevronFixture.Size * ChevronFixture.Size * 4];
        bitmap.CopyPixels(new Int32Rect(0, 0, ChevronFixture.Size, ChevronFixture.Size), pixels, ChevronFixture.Size * 4, 0);
        return pixels;
    }

    private sealed class ChevronFixture : IDisposable
    {
        public const int Size = 96;
        public Border Root { get; }
        public Expander Expander { get; }
        public ShapePath Arrow { get; }
        public Border Slot { get; }

        public ChevronFixture(bool expanded, double? initialAngle = null)
        {
            Arrow = new ShapePath
            {
                Name = "PART_Chevron",
                Data = Geometry.Parse("M9 18l6-6-6-6"),
                Stroke = Brushes.White,
                StrokeThickness = 1.5,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Width = 7,
                Height = 11,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            if (initialAngle is { } angle) Arrow.RenderTransform = new RotateTransform(angle);
            Slot = new Border { Width = 20, Height = 20, ClipToBounds = true, Child = Arrow, HorizontalAlignment = HorizontalAlignment.Left };
            var template = new ControlTemplate(typeof(Expander));
            template.SetVisualTree(() => new StackPanel
            {
                Children =
                {
                    new Border { Name = "PART_HeaderBorder", Height = 28, Padding = new Thickness(8, 4, 8, 4), Child = Slot },
                    new Border { Name = "PART_ContentBorder", Height = 48 },
                },
            });
            Expander = new Expander { IsExpanded = expanded, Template = template };
            Root = new Border { Width = Size, Height = Size, Padding = new Thickness(8), Background = Brushes.Black, Child = Expander };
            Arrange();
        }

        public void Arrange()
        {
            Root.Measure(new Size(Size, Size));
            Root.Arrange(new Rect(0, 0, Size, Size));
        }

        public void StopAnimation() =>
            (typeof(Expander).GetField("_animationTimer", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(Expander) as DispatcherTimer)?.Stop();

        public void Dispose() => StopAnimation();
    }
}

using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class PathIconRetainedLayerTests
{
    private const int Width = 1440;
    private const int Height = 80;
    private const int ButtonX = 1200;
    private const int ButtonY = 20;

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_Vello_PressedButton_KeepsPathIconInRetainedLayer() =>
        AssertPressedIcon(RenderBackend.D3D12, RenderingEngine.Vello);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_Impeller_PressedButton_KeepsPathIconInRetainedLayer() =>
        AssertPressedIcon(RenderBackend.D3D12, RenderingEngine.Impeller);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_Vello_PressedButton_KeepsPathIconInRetainedLayer() =>
        AssertPressedIcon(RenderBackend.Vulkan, RenderingEngine.Vello);

    private static void AssertPressedIcon(RenderBackend backend, RenderingEngine engine)
    {
        using var window = new HiddenNativeWindow(Width, Height);
        using var context = new RenderContext(backend, GpuPreference.Auto, engine);
        using var target = context.CreateRenderTarget(window.Hwnd, Width, Height);
        using var dc = new RenderTargetDrawingContext(target, context);
        Assert.True(((ILayerCompositingDrawingContext)dc).SupportsRetainedLayers);
        target.SetDpi(96, 96);
        target.SetPathMsaaSampleCount(4);

        var icon = new PathIcon
        {
            // Off-origin curves require a local fit matrix. A second transform
            // on the geometry also exercises nested pushes inside the capture.
            Data = Geometry.Parse("M10,10 C10,2 30,2 30,10 L30,26 L10,26 Z"),
            Width = 15,
            Height = 15,
            Foreground = new SolidColorBrush(Colors.White),
            Margin = new Thickness(0, 0, 8, 0),
        };
        icon.Data.Transform = new ScaleTransform(1.25, 1.25);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(icon);
        row.Children.Add(new TextBlock
        {
            Text = "GitHub",
            FontSize = 12,
            Foreground = new SolidColorBrush(Colors.White),
        });
        var button = new PressableButton
        {
            Width = 100,
            Height = 36,
            Padding = new Thickness(12, 0, 12, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(ButtonX, ButtonY, 0, 0),
            Content = row,
            Template = (ControlTemplate)XamlReader.Parse("""
                <ControlTemplate xmlns="http://schemas.jalium.ui/2024" TargetType="Button">
                    <Border Background="{TemplateBinding Background}">
                        <ContentPresenter Margin="{TemplateBinding Padding}"
                            HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                            VerticalAlignment="{TemplateBinding VerticalContentAlignment}" />
                    </Border>
                </ControlTemplate>
                """),
        };
        var pressed = new Trigger { Property = UIElement.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(UIElement.OpacityProperty, 0.82));
        pressed.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Colors.Black)));
        var style = new Style { TargetType = typeof(Button) };
        style.Triggers.Add(pressed);
        button.Style = style;
        var root = new Grid();
        root.Children.Add(button);
        root.Measure(new Size(Width, Height));
        root.Arrange(new Rect(0, 0, Width, Height));

        var visiblePixels = new int[5];
        try
        {
            for (var frame = 0; frame < visiblePixels.Length; frame++)
            {
                button.SetPressed(frame is >= 1 and <= 3);
                target.SetFullInvalidation();
                var timer = System.Diagnostics.Stopwatch.StartNew();
                while (!target.TryBeginDraw())
                {
                    Assert.True(timer.ElapsedMilliseconds < 1000, "BeginDraw timed out.");
                    Thread.Sleep(1);
                }
                target.Clear(0, 0, 0);
                dc.PushDirtyRegionClip(new Rect(ButtonX, ButtonY, 100, 36));
                root.Render(dc);
                dc.PopDirtyRegionClip();
                Assert.Equal(JaliumResult.Ok, target.RequestReadback());
                Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
                var pixels = new byte[Width * Height * 4];
                Assert.Equal(JaliumResult.Ok,
                    target.FetchReadback(pixels, Width * 4u, out _, out _));

                for (var y = ButtonY; y < ButtonY + 36; y++)
                    for (var x = ButtonX + 10; x < ButtonX + 30; x++)
                        if (pixels[(y * Width + x) * 4] > 40)
                            visiblePixels[frame]++;

                if (frame is 2 or 3)
                    Assert.True(dc.CompositedLayerCountForTests > 0,
                        "The held button must exercise retained-layer capture and replay.");
            }

            Assert.True(visiblePixels[0] >= 70, $"The normal icon is incomplete: {visiblePixels[0]} pixels.");
            for (var frame = 1; frame < visiblePixels.Length; frame++)
                Assert.True(visiblePixels[frame] >= visiblePixels[0] * 0.8,
                    $"{backend}/{engine}, frame {frame}: icon lost pixels after press/capture/release " +
                    $"({string.Join(", ", visiblePixels)}).");
        }
        finally
        {
            Visual.ReleaseRetainedLayersRecursive(root);
            dc.DrainPendingRetainedLayers();
        }
    }

    private sealed class PressableButton : Button
    {
        public void SetPressed(bool value) => IsPressed = value;
    }
}

using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Gpu;
using Jalium.UI.Markup;
using GpuCornerRadius = Jalium.UI.Gpu.CornerRadius;
using GpuRect = Jalium.UI.Gpu.Rect;
using GpuThickness = Jalium.UI.Gpu.Thickness;

namespace Jalium.UI.Tests;

public class XamlTypeRegistryApplyBundleTests
{
    [Fact]
    public void ApplyBundle_WithDrawCommands_ShouldUseCompiledBundlePath()
    {
        var host = new Canvas();
        var bundle = CreateBundle(
            nodes:
            [
                new RectNode
                {
                    Id = 1,
                    ParentId = 0,
                    Bounds = new GpuRect(4, 6, 20, 12),
                    CornerRadius = new GpuCornerRadius(2, 2, 2, 2),
                    BorderThickness = new GpuThickness(1, 1, 1, 1),
                    MaterialIndex = 0,
                    TransformIndex = 0,
                    ClipIndex = 0,
                    IsVisible = true,
                    ZIndex = 0
                }
            ],
            drawCommands:
            [
                new SubmitCommand()
            ]);

        XamlTypeRegistry.ApplyBundle(host, bundle);

        Assert.Same(bundle, host.CompiledBundle);
        Assert.Empty(host.Children);
    }

    [Fact]
    public void ApplyBundle_WithoutDrawCommands_ShouldBuildFallbackVisualTree()
    {
        var host = new Canvas();
        var bundle = CreateBundle(
            nodes:
            [
                new RectNode
                {
                    Id = 1,
                    ParentId = 0,
                    Bounds = new GpuRect(10, 20, 100, 80),
                    CornerRadius = new GpuCornerRadius(4, 4, 4, 4),
                    BorderThickness = new GpuThickness(2, 2, 2, 2),
                    MaterialIndex = 0,
                    TransformIndex = 0,
                    ClipIndex = 0,
                    IsVisible = true,
                    ZIndex = 0
                },
                new RectNode
                {
                    Id = 2,
                    ParentId = 1,
                    Bounds = new GpuRect(5, 7, 30, 40),
                    CornerRadius = new GpuCornerRadius(0, 0, 0, 0),
                    BorderThickness = new GpuThickness(1, 1, 1, 1),
                    MaterialIndex = 0,
                    TransformIndex = 0,
                    ClipIndex = 0,
                    IsVisible = true,
                    ZIndex = 1
                }
            ],
            drawCommands: []);

        XamlTypeRegistry.ApplyBundle(host, bundle);

        Assert.Same(bundle, host.CompiledBundle);

        var fallbackRoot = Assert.IsType<Canvas>(Assert.Single(host.Children));
        Assert.Equal(2, fallbackRoot.Children.Count);

        var parentVisual = Assert.IsType<Border>(fallbackRoot.Children[0]);
        var childVisual = Assert.IsType<Border>(fallbackRoot.Children[1]);

        Assert.Equal(10d, Canvas.GetLeft(parentVisual));
        Assert.Equal(20d, Canvas.GetTop(parentVisual));
        Assert.Equal(100d, parentVisual.Width);
        Assert.Equal(80d, parentVisual.Height);

        Assert.Equal(15d, Canvas.GetLeft(childVisual));
        Assert.Equal(27d, Canvas.GetTop(childVisual));
        Assert.Equal(30d, childVisual.Width);
        Assert.Equal(40d, childVisual.Height);
    }

    [Fact]
    public void ApplyBundle_DefaultAttachPath_ShouldUseContentProperty()
    {
        var host = new FallbackHost();
        var bundle = CreateBundle(
            nodes:
            [
                new RectNode
                {
                    Id = 1,
                    ParentId = 0,
                    Bounds = new GpuRect(0, 0, 16, 16),
                    CornerRadius = new GpuCornerRadius(0, 0, 0, 0),
                    BorderThickness = new GpuThickness(1, 1, 1, 1),
                    MaterialIndex = 0,
                    TransformIndex = 0,
                    ClipIndex = 0,
                    IsVisible = true,
                    ZIndex = 0
                }
            ],
            drawCommands: []);

        XamlTypeRegistry.ApplyBundle(host, bundle);

        var fallbackRoot = Assert.IsType<Canvas>(host.Child);
        Assert.Single(fallbackRoot.Children);
    }

    private static CompiledUIBundle CreateBundle(SceneNode[] nodes, DrawCommand[] drawCommands)
    {
        return new CompiledUIBundle
        {
            Version = RenderIR.Version,
            Nodes = nodes,
            Materials =
            [
                new Material(0xFFFFFFFF, 0xFF000000, 0xFF000000, 0, 255, BlendMode.Normal)
            ],
            Gradients = [],
            GradientStops = [],
            Curves = [],
            AnimationTargets = [],
            Transforms = [],
            AnimationValues = [],
            DrawCommands = drawCommands,
            Textures = [],
            GlyphAtlases = [],
            PathCaches = [],
            InteractiveRegions = [],
            StateTransitions = []
        };
    }

    [ContentProperty("Child")]
    private sealed class FallbackHost : FrameworkElement
    {
        public UIElement? Child { get; set; }
    }
}

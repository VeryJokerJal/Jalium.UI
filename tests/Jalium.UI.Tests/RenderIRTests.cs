using Jalium.UI.Gpu;
using GpuRect = Jalium.UI.Gpu.Rect;
using GpuCornerRadius = Jalium.UI.Gpu.CornerRadius;
using GpuThickness = Jalium.UI.Gpu.Thickness;

namespace Jalium.UI.Tests;

/// <summary>
/// GPU 渲染 IR 测试
/// </summary>
public class RenderIRTests
{
    [Fact]
    public void RectNode_ShouldStoreProperties()
    {
        // Arrange & Act
        var node = new RectNode
        {
            Id = 1,
            ParentId = 0,
            Bounds = new GpuRect(10, 20, 100, 50),
            CornerRadius = new GpuCornerRadius(5, 5, 5, 5),
            BorderThickness = new GpuThickness(1, 1, 1, 1),
            MaterialIndex = 0,
            IsVisible = true
        };

        // Assert
        Assert.Equal(1u, node.Id);
        Assert.Equal(10f, node.Bounds.X);
        Assert.Equal(20f, node.Bounds.Y);
        Assert.Equal(100f, node.Bounds.Width);
        Assert.Equal(50f, node.Bounds.Height);
        Assert.Equal(5f, node.CornerRadius.TopLeft);
    }

    [Fact]
    public void Material_ShouldStoreColors()
    {
        // Arrange & Act
        var material = new Material(
            0xFF0000FF, // Blue - backgroundColor
            0xFF00FF00, // Green - borderColor
            0xFFFFFFFF, // White - foregroundColor
            0,          // gradientIndex
            255,        // opacity
            BlendMode.Normal
        );

        // Assert
        Assert.Equal(0xFF0000FFu, material.BackgroundColor);
        Assert.Equal(0xFF00FF00u, material.BorderColor);
        Assert.Equal(0xFFFFFFFFu, material.ForegroundColor);
    }

    [Fact]
    public void AnimationCurve_EaseIn_ShouldApplyCorrectly()
    {
        // Arrange
        var curve = new AnimationCurve(
            EasingType.EaseIn,
            durationMs: 1000,
            delayMs: 0,
            repeatCount: 0,
            autoReverse: false
        );

        // Assert
        Assert.Equal(EasingType.EaseIn, curve.Easing);
        Assert.Equal(1000u, curve.DurationMs);
    }

    [Fact]
    public void BundleSerializer_ShouldRoundTrip()
    {
        // Arrange
        var originalBundle = CreateTestBundle();
        var tempPath = Path.GetTempFileName();

        try
        {
            // Act
            BundleSerializer.Save(originalBundle, tempPath);
            var loadedBundle = BundleSerializer.Load(tempPath);

            // Assert
            Assert.Equal(originalBundle.Version, loadedBundle.Version);
            Assert.Equal(originalBundle.Nodes.Length, loadedBundle.Nodes.Length);
            Assert.Equal(originalBundle.Materials.Length, loadedBundle.Materials.Length);

            // 验证节点数据
            var originalRect = originalBundle.Nodes[0] as RectNode;
            var loadedRect = loadedBundle.Nodes[0] as RectNode;
            Assert.NotNull(originalRect);
            Assert.NotNull(loadedRect);
            Assert.Equal(originalRect.Id, loadedRect.Id);
            Assert.Equal(originalRect.Bounds.X, loadedRect.Bounds.X);
            Assert.Equal(originalRect.Bounds.Y, loadedRect.Bounds.Y);

            Assert.Single(loadedBundle.InteractiveRegions);
            Assert.Equal(10f, loadedBundle.InteractiveRegions[0].ClipBounds.X);
            Assert.Equal(20f, loadedBundle.InteractiveRegions[0].ClipBounds.Y);
            Assert.Equal(80f, loadedBundle.InteractiveRegions[0].ClipBounds.Width);
            Assert.Equal(60f, loadedBundle.InteractiveRegions[0].ClipBounds.Height);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void StateTransition_ShouldStoreAnimationReferences()
    {
        // Arrange & Act
        var transition = new StateTransition(
            TriggerType.MouseEnter,
            fromState: 0,
            toState: 1,
            animStart: 0,
            animCount: 2,
            matStart: 0,
            matCount: 1
        );

        // Assert
        Assert.Equal(TriggerType.MouseEnter, transition.Trigger);
        Assert.Equal(0u, transition.FromStateId);
        Assert.Equal(1u, transition.ToStateId);
        Assert.Equal(2u, transition.AnimationCount);
    }

    private static CompiledUIBundle CreateTestBundle()
    {
        return new CompiledUIBundle
        {
            Version = RenderIR.Version,
            Nodes =
            [
                new RectNode
                {
                    Id = 1,
                    ParentId = 0,
                    Bounds = new GpuRect(0, 0, 100, 100),
                    CornerRadius = new GpuCornerRadius(5, 5, 5, 5),
                    BorderThickness = new GpuThickness(1, 1, 1, 1),
                    MaterialIndex = 0,
                    TransformIndex = 0,
                    ClipIndex = 0,
                    IsVisible = true,
                    ZIndex = 0
                }
            ],
            Materials =
            [
                new Material(0xFFFFFFFF, 0xFF000000, 0xFF000000, 0, 255, BlendMode.Normal)
            ],
            Gradients = [],
            GradientStops = [],
            Curves =
            [
                new AnimationCurve(EasingType.Linear, 300, 0, 0, false)
            ],
            AnimationTargets = [],
            Transforms = [1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f],
            AnimationValues = [],
            DrawCommands =
            [
                new DrawRectBatchCommand { InstanceBufferOffset = 0, InstanceCount = 1, TextureIndex = 0 },
                new SubmitCommand()
            ],
            Textures = [],
            GlyphAtlases = [],
            PathCaches = [],
            InteractiveRegions =
            [
                new InteractiveRegion(
                    1,
                    new GpuRect(0, 0, 100, 100),
                    InteractionFlags.Click | InteractionFlags.Hover,
                    0,
                    new GpuRect(10, 20, 80, 60))
            ],
            StateTransitions =
            [
                new StateTransition(TriggerType.MouseEnter, 0, 1, 0, 0, 0, 0)
            ]
        };
    }
}

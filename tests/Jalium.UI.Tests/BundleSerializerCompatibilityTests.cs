using Jalium.UI.Gpu;
using GpuCornerRadius = Jalium.UI.Gpu.CornerRadius;
using GpuRect = Jalium.UI.Gpu.Rect;
using GpuThickness = Jalium.UI.Gpu.Thickness;

namespace Jalium.UI.Tests;

public class BundleSerializerCompatibilityTests
{
    [Fact]
    public void Save_ShouldWriteJuibMagic()
    {
        var bundle = CreateTestBundle();
        using var stream = new MemoryStream();

        BundleSerializer.Save(bundle, stream);
        var bytes = stream.ToArray();

        Assert.True(bytes.Length >= 4);
        Assert.True(bytes.AsSpan(0, 4).SequenceEqual("JUIB"u8));
    }

    [Theory]
    [InlineData(0x4A, 0x55, 0x49, 0x43)] // "JUIC" (legacy)
    [InlineData(0x42, 0x49, 0x55, 0x4A)] // historical uint(0x4A554942) little-endian bytes
    public void Load_ShouldAcceptLegacyHeaders(byte b0, byte b1, byte b2, byte b3)
    {
        var bundle = CreateTestBundle();
        using var stream = new MemoryStream();
        BundleSerializer.Save(bundle, stream);
        var bytes = stream.ToArray();

        bytes[0] = b0;
        bytes[1] = b1;
        bytes[2] = b2;
        bytes[3] = b3;

        var loaded = BundleSerializer.Load(bytes);

        Assert.Equal(bundle.Version, loaded.Version);
        Assert.Equal(bundle.Nodes.Length, loaded.Nodes.Length);
        Assert.Equal(bundle.DrawCommands.Length, loaded.DrawCommands.Length);
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
                    CornerRadius = new GpuCornerRadius(4, 4, 4, 4),
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
            Curves = [new AnimationCurve(EasingType.Linear, 300, 0, 0, false)],
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
            InteractiveRegions = [new InteractiveRegion(1, new GpuRect(0, 0, 100, 100), InteractionFlags.Click, 0)],
            StateTransitions = []
        };
    }
}

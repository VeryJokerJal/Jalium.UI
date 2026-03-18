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

    [Fact]
    public void Load_ShouldUpgradeVersion1InteractiveRegionsWithoutClipBounds()
    {
        var bytes = WriteLegacyVersion1Bundle();

        var loaded = BundleSerializer.Load(bytes);

        Assert.Equal(RenderIR.Version, loaded.Version);
        Assert.Single(loaded.InteractiveRegions);
        Assert.True(loaded.InteractiveRegions[0].ClipBounds.IsEmpty);
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

    private static byte[] WriteLegacyVersion1Bundle()
    {
        const byte nodeTypeRect = 1;
        const byte cmdTypeDrawRectBatch = 5;
        const byte cmdTypeSubmit = 11;

        var bundle = CreateTestBundle();
        var rect = Assert.IsType<RectNode>(bundle.Nodes[0]);
        var drawRect = Assert.IsType<DrawRectBatchCommand>(bundle.DrawCommands[0]);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        writer.Write("JUIB"u8);
        writer.Write((ushort)1);

        writer.Write(bundle.Nodes.Length);
        writer.Write(nodeTypeRect);
        writer.Write(rect.Id);
        writer.Write(rect.ParentId);
        writer.Write(rect.TransformIndex);
        writer.Write(rect.MaterialIndex);
        writer.Write(rect.ClipIndex);
        writer.Write(rect.IsVisible);
        writer.Write(rect.ZIndex);
        WriteRect(writer, rect.Bounds);
        WriteCornerRadius(writer, rect.CornerRadius);
        WriteThickness(writer, rect.BorderThickness);

        writer.Write(bundle.Materials.Length);
        foreach (var material in bundle.Materials)
        {
            writer.Write(material.BackgroundColor);
            writer.Write(material.BorderColor);
            writer.Write(material.ForegroundColor);
            writer.Write(material.GradientIndex);
            writer.Write(material.Opacity);
            writer.Write((byte)material.BlendMode);
        }

        writer.Write(bundle.Gradients.Length);
        writer.Write(bundle.GradientStops.Length);

        writer.Write(bundle.Curves.Length);
        foreach (var curve in bundle.Curves)
        {
            writer.Write((byte)curve.Easing);
            writer.Write(curve.P1X);
            writer.Write(curve.P1Y);
            writer.Write(curve.P2X);
            writer.Write(curve.P2Y);
            writer.Write(curve.DurationMs);
            writer.Write(curve.DelayMs);
            writer.Write(curve.RepeatCount);
            writer.Write(curve.AutoReverse);
        }

        writer.Write(bundle.AnimationTargets.Length);

        writer.Write(bundle.Transforms.Length);
        foreach (var transform in bundle.Transforms)
        {
            writer.Write(transform);
        }

        writer.Write(bundle.AnimationValues.Length);
        writer.Write(bundle.AnimationValues);

        writer.Write(bundle.DrawCommands.Length);
        writer.Write(cmdTypeDrawRectBatch);
        writer.Write(drawRect.InstanceBufferOffset);
        writer.Write(drawRect.InstanceCount);
        writer.Write(drawRect.TextureIndex);
        writer.Write(cmdTypeSubmit);

        writer.Write(bundle.Textures.Length);
        writer.Write(bundle.GlyphAtlases.Length);
        writer.Write(bundle.PathCaches.Length);

        writer.Write(bundle.InteractiveRegions.Length);
        foreach (var region in bundle.InteractiveRegions)
        {
            writer.Write(region.NodeId);
            WriteRect(writer, region.Bounds);
            writer.Write((byte)region.Flags);
            writer.Write(region.HandlerIndex);
        }

        writer.Write(bundle.StateTransitions.Length);
        writer.Write(bundle.BackdropFilterParams.Length);

        writer.Flush();
        return stream.ToArray();
    }

    private static void WriteRect(BinaryWriter writer, GpuRect rect)
    {
        writer.Write(rect.X);
        writer.Write(rect.Y);
        writer.Write(rect.Width);
        writer.Write(rect.Height);
    }

    private static void WriteCornerRadius(BinaryWriter writer, GpuCornerRadius cornerRadius)
    {
        writer.Write(cornerRadius.TopLeft);
        writer.Write(cornerRadius.TopRight);
        writer.Write(cornerRadius.BottomRight);
        writer.Write(cornerRadius.BottomLeft);
    }

    private static void WriteThickness(BinaryWriter writer, GpuThickness thickness)
    {
        writer.Write(thickness.Left);
        writer.Write(thickness.Top);
        writer.Write(thickness.Right);
        writer.Write(thickness.Bottom);
    }
}

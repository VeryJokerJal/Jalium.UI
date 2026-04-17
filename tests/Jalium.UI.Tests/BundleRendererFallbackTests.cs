using Jalium.UI.Gpu;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public class BundleRendererFallbackTests
{
    [Fact]
    public void Render_TextNodeWithoutResolver_ShouldDrawPlaceholderAndEmitDiagnostic()
    {
        var bundle = CreateBaseBundle(
            nodes: new SceneNode[]
            {
                new TextNode
                {
                    IsVisible = true,
                    MaterialIndex = 0,
                    TextHash = 0x1234UL,
                    GlyphAtlasIndex = 0,
                    GlyphRunIndex = 0,
                    Bounds = new Jalium.UI.Gpu.Rect(0, 0, 40, 20)
                }
            },
            materials: new[] { new Material(0, 0, 0xFF000000) },
            glyphAtlases: new[] { new GlyphAtlasRef("Segoe UI", 12, 256, 256) });

        var diagnostics = new List<string>();
        var dc = new RecordingDrawingContext();
        var renderer = new BundleRenderer(bundle)
        {
            DiagnosticSink = diagnostics.Add
        };

        renderer.Render(dc);

        Assert.Equal(1, dc.DrawTextCalls);
        Assert.Contains(diagnostics, message => message.Contains("text fallback", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Render_ImageNodeWithMissingTexture_ShouldDrawRectangleFallback()
    {
        var bundle = CreateBaseBundle(
            nodes: new SceneNode[]
            {
                new ImageNode
                {
                    IsVisible = true,
                    MaterialIndex = 0,
                    TextureIndex = 0,
                    UVRect = new Jalium.UI.Gpu.Rect(0, 0, 1, 1),
                    Bounds = new Jalium.UI.Gpu.Rect(0, 0, 30, 30),
                    NineSlice = default
                }
            },
            materials: new[] { new Material(0xFF00AA00, 0, 0xFFFFFFFF) },
            textures: new[] { new TextureRef("missing-image-file.png", 0, 0, TextureFormat.RGBA8) });

        var diagnostics = new List<string>();
        var dc = new RecordingDrawingContext();
        var renderer = new BundleRenderer(bundle)
        {
            DiagnosticSink = diagnostics.Add
        };

        renderer.Render(dc);

        Assert.Equal(0, dc.DrawImageCalls);
        Assert.True(dc.DrawRectangleCalls >= 1);
        Assert.Contains(diagnostics, message => message.Contains("image fallback", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Render_PathNode_ShouldDrawBoundsFallbackAndEmitDiagnostic()
    {
        var bundle = CreateBaseBundle(
            nodes: new SceneNode[]
            {
                new PathNode
                {
                    IsVisible = true,
                    MaterialIndex = 0,
                    PathCacheIndex = 0,
                    Bounds = new Jalium.UI.Gpu.Rect(2, 3, 50, 20),
                    FillRule = Jalium.UI.Media.FillRule.Nonzero
                }
            },
            materials: new[] { new Material(0xFF336699, 0xFF112233, 0xFFFFFFFF) },
            pathCaches: new[] { new PathCache(1UL, 0, 0, 0, 0) });

        var diagnostics = new List<string>();
        var dc = new RecordingDrawingContext();
        var renderer = new BundleRenderer(bundle)
        {
            DiagnosticSink = diagnostics.Add
        };

        renderer.Render(dc);

        Assert.True(dc.DrawRectangleCalls >= 1);
        Assert.Contains(diagnostics, message => message.Contains("path fallback", StringComparison.OrdinalIgnoreCase));
    }

    private static CompiledUIBundle CreateBaseBundle(
        SceneNode[] nodes,
        Material[] materials,
        TextureRef[]? textures = null,
        GlyphAtlasRef[]? glyphAtlases = null,
        PathCache[]? pathCaches = null)
    {
        return new CompiledUIBundle
        {
            Nodes = nodes,
            Materials = materials,
            Gradients = [],
            GradientStops = [],
            Curves = [],
            AnimationTargets = [],
            Transforms = [],
            AnimationValues = [],
            DrawCommands = [],
            Textures = textures ?? [],
            GlyphAtlases = glyphAtlases ?? [],
            PathCaches = pathCaches ?? [],
            InteractiveRegions = [],
            StateTransitions = [],
            BackdropFilterParams = []
        };
    }

    private sealed class RecordingDrawingContext : DrawingContext
    {
        public int DrawRectangleCalls { get; private set; }
        public int DrawImageCalls { get; private set; }
        public int DrawTextCalls { get; private set; }

        public override void DrawLine(Pen pen, Point point0, Point point1)
        {
        }

        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle)
        {
            DrawRectangleCalls++;
        }

        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY)
        {
            DrawRectangleCalls++;
        }

        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY)
        {
        }

        public override void DrawText(FormattedText formattedText, Point origin)
        {
            DrawTextCalls++;
        }

        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry)
        {
        }

        public override void DrawImage(ImageSource imageSource, Rect rectangle)
        {
            DrawImageCalls++;
        }

        public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect, CornerRadius cornerRadius)
        {
        }

        public override void PushTransform(Transform transform)
        {
        }

        public override void PushClip(Geometry clipGeometry)
        {
        }

        public override void PushOpacity(double opacity)
        {
        }

        public override void Pop()
        {
        }

        public override void Close()
        {
        }
    }
}

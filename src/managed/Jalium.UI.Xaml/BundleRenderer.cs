using Jalium.UI.Gpu;
using Jalium.UI.Media;

// Type aliases to distinguish between Gpu and Core types (both define Rect, Point, etc.)
using GpuRect = Jalium.UI.Gpu.Rect;
using GpuPoint = Jalium.UI.Gpu.Point;
using GpuCornerRadius = Jalium.UI.Gpu.CornerRadius;
using GpuThickness = Jalium.UI.Gpu.Thickness;

// Core types (used by Media's DrawingContext)
using CoreRect = Jalium.UI.Rect;
using CorePoint = Jalium.UI.Point;
using CoreCornerRadius = Jalium.UI.CornerRadius;

namespace Jalium.UI.Markup;

/// <summary>
/// Executes a CompiledUIBundle's pre-compiled DrawCommands on a DrawingContext.
/// This is the GPU-friendly command buffer execution path.
///
/// Design Philosophy:
/// - UI is compiled to Scene Graph + Render Pass (not control objects)
/// - Styles are compiled "materials" (Material struct)
/// - Animations are time functions (AnimationCurve, AnimationTarget)
/// - Output is GPU-friendly draw ops (similar to Vulkan/Metal command buffers)
/// </summary>
public sealed class BundleRenderer
{
    private readonly CompiledUIBundle _bundle;
    private readonly Dictionary<uint, Brush> _brushCache = new();
    private readonly Dictionary<uint, Pen> _penCache = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="BundleRenderer"/> class.
    /// </summary>
    /// <param name="bundle">The compiled UI bundle to render.</param>
    public BundleRenderer(CompiledUIBundle bundle)
    {
        _bundle = bundle ?? throw new ArgumentNullException(nameof(bundle));
    }

    /// <summary>
    /// Executes the bundle's pre-compiled draw commands on the drawing context.
    /// This is the primary GPU-friendly rendering path.
    /// </summary>
    /// <param name="drawingContext">The drawing context.</param>
    public void Render(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc)
        {
            return;
        }

        // Execute pre-compiled draw commands (GPU command buffer style)
        if (_bundle.DrawCommands.Length > 0)
        {
            ExecuteDrawCommands(dc);
        }
        else
        {
            // Fallback: render from scene nodes if no draw commands compiled
            RenderFromNodes(dc);
        }
    }

    #region Command Buffer Execution (GPU-friendly path)

    /// <summary>
    /// Executes the pre-compiled draw command sequence.
    /// This mimics a GPU command buffer / render pass execution.
    /// </summary>
    private void ExecuteDrawCommands(DrawingContext dc)
    {
        foreach (var command in _bundle.DrawCommands)
        {
            ExecuteCommand(dc, command);
        }
    }

    /// <summary>
    /// Dispatches a single draw command to the appropriate handler.
    /// </summary>
    private void ExecuteCommand(DrawingContext dc, DrawCommand command)
    {
        switch (command)
        {
            case SetRenderTargetCommand setRT:
                ExecuteSetRenderTarget(dc, setRT);
                break;

            case ClearCommand clear:
                ExecuteClear(dc, clear);
                break;

            case SetClipCommand setClip:
                ExecuteSetClip(dc, setClip);
                break;

            case SetTransformCommand setTransform:
                ExecuteSetTransform(dc, setTransform);
                break;

            case DrawRectBatchCommand rectBatch:
                ExecuteDrawRectBatch(dc, rectBatch);
                break;

            case DrawTextBatchCommand textBatch:
                ExecuteDrawTextBatch(dc, textBatch);
                break;

            case DrawImageBatchCommand imageBatch:
                ExecuteDrawImageBatch(dc, imageBatch);
                break;

            case DrawPathCommand drawPath:
                ExecuteDrawPath(dc, drawPath);
                break;

            case ApplyEffectCommand applyEffect:
                ExecuteApplyEffect(dc, applyEffect);
                break;

            case ApplyBackdropFilterCommand backdropFilter:
                ExecuteApplyBackdropFilter(dc, backdropFilter);
                break;

            case CaptureBackdropCommand captureBackdrop:
                ExecuteCaptureBackdrop(dc, captureBackdrop);
                break;

            case CompositeLayerCommand composite:
                ExecuteCompositeLayer(dc, composite);
                break;
        }
    }

    private void ExecuteSetRenderTarget(DrawingContext dc, SetRenderTargetCommand cmd)
    {
        // For software rendering, render target switching is handled differently
        // In a true GPU path, this would switch framebuffers
        if (cmd.Clear && cmd.ClearColor != 0)
        {
            var brush = GetBrush(cmd.ClearColor);
            // Clear would fill the entire target - handled by the compositor
        }
    }

    private void ExecuteClear(DrawingContext dc, ClearCommand cmd)
    {
        // Clear command - in software rendering this is typically a no-op
        // as the window background handles clearing
    }

    private void ExecuteSetClip(DrawingContext dc, SetClipCommand cmd)
    {
        var clipRect = ConvertRect(cmd.ClipRect);
        dc.PushClip(new RectangleGeometry(clipRect));
    }

    private void ExecuteSetTransform(DrawingContext dc, SetTransformCommand cmd)
    {
        var matrix = GetTransformMatrix(cmd.TransformIndex);
        dc.PushTransform(new MatrixTransform(matrix));
    }

    /// <summary>
    /// Executes a batched rectangle draw command.
    /// This is the GPU-optimized path for drawing multiple rectangles with the same texture/material.
    /// </summary>
    private void ExecuteDrawRectBatch(DrawingContext dc, DrawRectBatchCommand cmd)
    {
        // In a true GPU path, this would be a single instanced draw call
        // For software rendering, we iterate the instances

        // Find all RectNodes that belong to this batch
        // The batch is defined by InstanceBufferOffset and InstanceCount
        var rectNodes = _bundle.Nodes
            .OfType<RectNode>()
            .Where(n => n.IsVisible)
            .OrderBy(n => n.ZIndex)
            .Take((int)cmd.InstanceCount);

        foreach (var node in rectNodes)
        {
            RenderRectNode(dc, node);
        }
    }

    private void ExecuteDrawTextBatch(DrawingContext dc, DrawTextBatchCommand cmd)
    {
        // Text batch rendering - would use glyph atlas in GPU path
        // For now, find and render text nodes
        var textNodes = _bundle.Nodes
            .OfType<TextNode>()
            .Where(n => n.IsVisible)
            .Take((int)cmd.GlyphCount);

        foreach (var node in textNodes)
        {
            RenderTextNode(dc, node);
        }
    }

    private void ExecuteDrawImageBatch(DrawingContext dc, DrawImageBatchCommand cmd)
    {
        // Image batch rendering
        var imageNodes = _bundle.Nodes
            .OfType<ImageNode>()
            .Where(n => n.IsVisible && n.TextureIndex == cmd.TextureIndex)
            .Take((int)cmd.InstanceCount);

        foreach (var node in imageNodes)
        {
            RenderImageNode(dc, node);
        }
    }

    private void ExecuteDrawPath(DrawingContext dc, DrawPathCommand cmd)
    {
        // Path rendering from PathCache
        if (cmd.PathCacheIndex < _bundle.PathCaches.Length)
        {
            var pathCache = _bundle.PathCaches[cmd.PathCacheIndex];
            // TODO: Reconstruct geometry from tessellated path data
        }
    }

    private void ExecuteApplyEffect(DrawingContext dc, ApplyEffectCommand cmd)
    {
        // Effect application (blur, shadow, etc.)
        // In GPU path, this would be a shader pass
    }

    private void ExecuteApplyBackdropFilter(DrawingContext dc, ApplyBackdropFilterCommand cmd)
    {
        // Backdrop filter (frosted glass, acrylic, mica)
        var bounds = ConvertRect(cmd.Region);
        var cornerRadius = ConvertCornerRadius(cmd.CornerRadius);

        // Create appropriate backdrop effect based on params
        var effect = CreateBackdropEffect(cmd.Params);
        if (effect != null)
        {
            dc.DrawBackdropEffect(bounds, effect, cornerRadius);
        }
    }

    private void ExecuteCaptureBackdrop(DrawingContext dc, CaptureBackdropCommand cmd)
    {
        // Capture current framebuffer content for backdrop effects
        // This is handled by the compositor in actual implementation
    }

    private void ExecuteCompositeLayer(DrawingContext dc, CompositeLayerCommand cmd)
    {
        // Layer compositing with blend modes
        // In GPU path, this would blend offscreen textures
    }

    private IBackdropEffect? CreateBackdropEffect(BackdropFilterParams p)
    {
        if (p.MaterialType == MaterialType.Acrylic)
        {
            return new AcrylicEffect
            {
                BlurRadius = p.BlurRadius,
                TintColor = UnpackColor(p.TintColor),
                TintOpacity = p.TintOpacity,
                NoiseIntensity = p.NoiseIntensity
            };
        }
        else if (p.MaterialType == MaterialType.Mica || p.MaterialType == MaterialType.MicaAlt)
        {
            return new MicaEffect
            {
                TintColor = UnpackColor(p.TintColor),
                TintOpacity = p.TintOpacity,
                UseAlt = p.MaterialType == MaterialType.MicaAlt
            };
        }
        else if (p.BlurRadius > 0)
        {
            return new BlurEffect { BlurRadius = p.BlurRadius };
        }

        return null;
    }

    #endregion

    #region Fallback: Node-based Rendering

    /// <summary>
    /// Fallback rendering path when DrawCommands are not available.
    /// Renders directly from scene nodes (less optimal).
    /// </summary>
    private void RenderFromNodes(DrawingContext dc)
    {
        // Sort by Z-Index and render visible nodes
        var sortedNodes = _bundle.Nodes
            .Where(n => n.IsVisible)
            .OrderBy(n => n.ZIndex)
            .ToList();

        foreach (var node in sortedNodes)
        {
            RenderNode(dc, node);
        }
    }

    private void RenderNode(DrawingContext dc, SceneNode node)
    {
        // Apply transform if present
        bool hasTransform = node.TransformIndex > 0 &&
                            node.TransformIndex * 6 + 5 < _bundle.Transforms.Length;

        if (hasTransform)
        {
            var matrix = GetTransformMatrix(node.TransformIndex);
            dc.PushTransform(new MatrixTransform(matrix));
        }

        try
        {
            switch (node)
            {
                case RectNode rect:
                    RenderRectNode(dc, rect);
                    break;
                case TextNode text:
                    RenderTextNode(dc, text);
                    break;
                case ImageNode image:
                    RenderImageNode(dc, image);
                    break;
                case PathNode path:
                    RenderPathNode(dc, path);
                    break;
                case BackdropFilterNode backdrop:
                    RenderBackdropFilterNode(dc, backdrop);
                    break;
            }
        }
        finally
        {
            if (hasTransform)
            {
                dc.Pop();
            }
        }
    }

    #endregion

    #region Node Rendering

    private void RenderRectNode(DrawingContext dc, RectNode node)
    {
        if (node.MaterialIndex >= _bundle.Materials.Length)
        {
            return;
        }

        var material = _bundle.Materials[node.MaterialIndex];
        var brush = GetBrush(material.BackgroundColor);

        Pen? pen = null;
        if (material.BorderColor != 0 && node.BorderThickness.Left > 0)
        {
            pen = GetPen(material.BorderColor, node.BorderThickness.Left);
        }

        var bounds = ConvertRect(node.Bounds);

        bool hasRoundCorners = node.CornerRadius.TopLeft > 0 ||
                               node.CornerRadius.TopRight > 0 ||
                               node.CornerRadius.BottomLeft > 0 ||
                               node.CornerRadius.BottomRight > 0;

        if (hasRoundCorners)
        {
            var cornerRadius = ConvertCornerRadius(node.CornerRadius);
            dc.DrawRoundedRectangle(brush, pen, bounds, cornerRadius);
        }
        else
        {
            dc.DrawRectangle(brush, pen, bounds);
        }
    }

    private void RenderTextNode(DrawingContext dc, TextNode node)
    {
        // TODO: Text rendering requires glyph atlas or text content lookup
        // The bundle stores TextHash - need text string recovery mechanism
    }

    private void RenderImageNode(DrawingContext dc, ImageNode node)
    {
        if (node.TextureIndex >= _bundle.Textures.Length)
            return;

        var texture = _bundle.Textures[node.TextureIndex];
        // TODO: Load image from texture.Path and draw
        // var imageSource = LoadImageSource(texture.Path);
        // var bounds = ConvertRect(node.Bounds);
        // dc.DrawImage(imageSource, bounds);
    }

    private void RenderPathNode(DrawingContext dc, PathNode node)
    {
        // TODO: Reconstruct geometry from PathCache
    }

    private void RenderBackdropFilterNode(DrawingContext dc, BackdropFilterNode node)
    {
        if (node.ParamsIndex >= _bundle.BackdropFilterParams.Length)
            return;

        var p = _bundle.BackdropFilterParams[node.ParamsIndex];
        var bounds = ConvertRect(node.FilterRegion);
        var cornerRadius = ConvertCornerRadius(node.CornerRadius);

        var effect = CreateBackdropEffect(p);
        if (effect != null)
        {
            dc.DrawBackdropEffect(bounds, effect, cornerRadius);
        }
    }

    #endregion

    #region Resource Management

    private Brush GetBrush(uint argbColor)
    {
        if (!_brushCache.TryGetValue(argbColor, out var brush))
        {
            brush = new SolidColorBrush(UnpackColor(argbColor));
            _brushCache[argbColor] = brush;
        }
        return brush;
    }

    private Pen GetPen(uint argbColor, float thickness)
    {
        // Simple cache key combining color and thickness
        uint key = argbColor ^ (uint)(thickness * 1000);
        if (!_penCache.TryGetValue(key, out var pen))
        {
            pen = new Pen(GetBrush(argbColor), thickness);
            _penCache[key] = pen;
        }
        return pen;
    }

    private static Color UnpackColor(uint argb)
    {
        return Color.FromArgb(
            (byte)(argb >> 24),
            (byte)(argb >> 16),
            (byte)(argb >> 8),
            (byte)argb);
    }

    private Matrix GetTransformMatrix(uint index)
    {
        int offset = (int)(index * 6);

        if (offset + 5 >= _bundle.Transforms.Length)
            return Matrix.Identity;

        return new Matrix(
            _bundle.Transforms[offset],     // M11
            _bundle.Transforms[offset + 1], // M12
            _bundle.Transforms[offset + 2], // M21
            _bundle.Transforms[offset + 3], // M22
            _bundle.Transforms[offset + 4], // OffsetX
            _bundle.Transforms[offset + 5]  // OffsetY
        );
    }

    #endregion

    #region Type Conversions

    private static CoreRect ConvertRect(GpuRect rect)
    {
        return new CoreRect(rect.X, rect.Y, rect.Width, rect.Height);
    }

    private static CorePoint ConvertPoint(GpuPoint point)
    {
        return new CorePoint(point.X, point.Y);
    }

    private static CoreCornerRadius ConvertCornerRadius(GpuCornerRadius radius)
    {
        return new CoreCornerRadius(
            radius.TopLeft,
            radius.TopRight,
            radius.BottomRight,
            radius.BottomLeft);
    }

    #endregion
}

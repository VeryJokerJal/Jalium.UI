using System.Drawing;
using Jalium.UI;

namespace Jalium.UI.Interop;

/// <summary>
/// Represents a native render target for drawing.
/// </summary>
public sealed class RenderTarget : IDisposable
{
    [ThreadStatic]
    private static int _drawTextDepth;

    private readonly IRenderTargetNative _native;
    private readonly RenderContext? _ownerContext;
    private readonly RenderBackend _backend;
    private readonly NativeSurfaceDescriptor _surface;
    private readonly nint _hwnd;
    private nint _handle;
    private bool _disposed;
    private bool _isDrawing;
    private int _ownerContextReleased;
    private float _dpiX = 96.0f;
    private float _dpiY = 96.0f;

    /// <summary>
    /// Gets the native handle.
    /// </summary>
    public nint Handle => _handle;

    /// <summary>
    /// Gets whether the render target is valid.
    /// </summary>
    public bool IsValid => _handle != nint.Zero && !_disposed;

    /// <summary>
    /// Gets whether a drawing session is active.
    /// </summary>
    public bool IsDrawing => _isDrawing;

    /// <summary>
    /// Gets the backend associated with this render target.
    /// </summary>
    public RenderBackend Backend => _backend;

    /// <summary>
    /// Gets or sets the width.
    /// </summary>
    public int Width { get; private set; }

    /// <summary>
    /// Gets or sets the height.
    /// </summary>
    public int Height { get; private set; }

    /// <summary>
    /// Gets whether the native backend preserves back-buffer contents across presents,
    /// allowing partial redraw + dirty-rect presentation.
    /// </summary>
    public bool SupportsPartialPresentation { get; }

    internal RenderTarget(RenderContext context, NativeSurfaceDescriptor surface, int width, int height, bool useComposition = false)
        : this(
            context.Backend,
            context.Handle,
            surface,
            width,
            height,
            useComposition,
            native: null,
            ownerContext: context)
    {
    }

    internal RenderTarget(
        RenderBackend backend,
        nint contextHandle,
        NativeSurfaceDescriptor surface,
        int width,
        int height,
        bool useComposition,
        IRenderTargetNative? native = null,
        RenderContext? ownerContext = null)
    {
        _native = native ?? DefaultRenderTargetNative.Instance;
        _ownerContext = ownerContext;
        _backend = backend;
        _surface = surface;
        _hwnd = surface.Platform == NativePlatform.Windows ? surface.Handle0 : nint.Zero;
        Width = width;
        Height = height;

        _ownerContext?.RegisterRenderTarget();
        try
        {
            _handle = useComposition
                ? _native.CreateForCompositionSurface(contextHandle, surface, width, height)
                : _native.CreateForSurface(contextHandle, surface, width, height);
            if (_handle == nint.Zero)
            {
                int resultCode = _native.GetContextLastError(contextHandle);
                ThrowRenderPipelineException("Create", resultCode);
            }

            SupportsPartialPresentation = _native.SupportsPartialPresentation(_handle);
        }
        catch
        {
            ReleaseOwnerContextReference();
            throw;
        }
    }

    /// <summary>
    /// Resizes the render target.
    /// </summary>
    /// <param name="width">The new width.</param>
    /// <param name="height">The new height.</param>
    public void Resize(int width, int height)
    {
        ThrowIfDisposed();
        if (width <= 0 || height <= 0) return;

        int resultCode = _native.Resize(_handle, width, height);
        ThrowIfNativeFailure("Resize", resultCode);

        Width = width;
        Height = height;
    }

    /// <summary>
    /// Begins a drawing session.
    /// </summary>
    public void BeginDraw()
    {
        ThrowIfDisposed();
        if (_isDrawing) return;

        int resultCode = _native.BeginDraw(_handle);
        ThrowIfNativeFailure("Begin", resultCode);
        _isDrawing = true;
    }

    /// <summary>
    /// Attempts to begin a drawing session.  Returns false if the GPU is still
    /// processing the previous frame for this buffer, allowing the caller to
    /// skip the frame without blocking the UI thread.
    /// </summary>
    public bool TryBeginDraw()
    {
        ThrowIfDisposed();
        if (_isDrawing) return true;

        int resultCode = _native.BeginDraw(_handle);
        if (resultCode == (int)JaliumResult.Ok)
        {
            _isDrawing = true;
            return true;
        }

        // D3D12 uses InvalidState here when the GPU is still presenting the
        // previous back buffer. Callers can skip the frame and retry later.
        if (resultCode == (int)JaliumResult.InvalidState)
        {
            return false;
        }

        ThrowIfNativeFailure("Begin", resultCode);
        return false;
    }

    /// <summary>
    /// Ends a drawing session and presents the content.
    /// </summary>
    public void EndDraw()
    {
        ThrowIfDisposed();
        if (!_isDrawing) return;

        int resultCode;
        try
        {
            resultCode = _native.EndDraw(_handle);
        }
        finally
        {
            _isDrawing = false;
        }

        ThrowIfNativeFailure("End", resultCode);
    }

    /// <summary>
    /// Ends a drawing session without throwing on recoverable errors.
    /// Returns <see cref="JaliumResult.Ok"/> on success, or the failure result.
    /// </summary>
    public JaliumResult TryEndDraw()
    {
        if (_disposed || !_isDrawing) return JaliumResult.Ok;

        int resultCode;
        try
        {
            resultCode = _native.EndDraw(_handle);
        }
        finally
        {
            _isDrawing = false;
        }

        return JaliumResultMapper.FromCode(resultCode);
    }

    /// <summary>
    /// Clears the render target with the specified color.
    /// </summary>
    /// <param name="r">Red component (0-1).</param>
    /// <param name="g">Green component (0-1).</param>
    /// <param name="b">Blue component (0-1).</param>
    /// <param name="a">Alpha component (0-1).</param>
    public void Clear(float r, float g, float b, float a = 1.0f)
    {
        ThrowIfDisposed();
        NativeMethods.RenderTargetClear(_handle, r, g, b, a);
    }

    /// <summary>
    /// Draws a filled rectangle.
    /// </summary>
    public void FillRectangle(float x, float y, float width, float height, NativeBrush brush)
    {
        ThrowIfDisposed();
        if (brush == null || !brush.IsValid) return;
        NativeMethods.DrawFillRectangle(_handle, x, y, width, height, brush.Handle);
    }

    /// <summary>
    /// Draws a rectangle outline.
    /// </summary>
    public void DrawRectangle(float x, float y, float width, float height, NativeBrush brush, float strokeWidth = 1.0f)
    {
        ThrowIfDisposed();
        if (brush == null || !brush.IsValid) return;
        NativeMethods.DrawRectangle(_handle, x, y, width, height, brush.Handle, strokeWidth);
    }

    /// <summary>
    /// Draws a filled rounded rectangle.
    /// </summary>
    public void FillRoundedRectangle(float x, float y, float width, float height, float radiusX, float radiusY, NativeBrush brush)
    {
        ThrowIfDisposed();
        if (brush == null || !brush.IsValid) return;
        NativeMethods.DrawFillRoundedRectangle(_handle, x, y, width, height, radiusX, radiusY, brush.Handle);
    }

    /// <summary>
    /// Draws a rounded rectangle outline.
    /// </summary>
    public void DrawRoundedRectangle(float x, float y, float width, float height, float radiusX, float radiusY, NativeBrush brush, float strokeWidth = 1.0f)
    {
        ThrowIfDisposed();
        if (brush == null || !brush.IsValid) return;
        NativeMethods.DrawRoundedRectangle(_handle, x, y, width, height, radiusX, radiusY, brush.Handle, strokeWidth);
    }

    /// <summary>
    /// Draws a filled rounded rectangle with per-corner radii.
    /// </summary>
    public void FillPerCornerRoundedRectangle(float x, float y, float width, float height, float tl, float tr, float br, float bl, NativeBrush brush)
    {
        ThrowIfDisposed();
        if (brush == null || !brush.IsValid) return;
        NativeMethods.FillPerCornerRoundedRectangle(_handle, x, y, width, height, tl, tr, br, bl, brush.Handle);
    }

    /// <summary>
    /// Draws a rounded rectangle outline with per-corner radii.
    /// </summary>
    public void DrawPerCornerRoundedRectangle(float x, float y, float width, float height, float tl, float tr, float br, float bl, NativeBrush brush, float strokeWidth = 1.0f)
    {
        ThrowIfDisposed();
        if (brush == null || !brush.IsValid) return;
        NativeMethods.DrawPerCornerRoundedRectangle(_handle, x, y, width, height, tl, tr, br, bl, brush.Handle, strokeWidth);
    }

    /// <summary>
    /// Draws a filled ellipse.
    /// </summary>
    public void FillEllipse(float centerX, float centerY, float radiusX, float radiusY, NativeBrush brush)
    {
        ThrowIfDisposed();
        if (brush == null || !brush.IsValid) return;
        NativeMethods.DrawFillEllipse(_handle, centerX, centerY, radiusX, radiusY, brush.Handle);
    }

    /// <summary>
    /// Draws a batch of filled ellipses with per-ellipse color.
    /// data layout: [cx, cy, rx, ry, packedRGBA] × count (5 floats per ellipse).
    /// Single P/Invoke call eliminates per-ellipse marshaling overhead.
    /// </summary>
    public void FillEllipseBatch(float[] data, uint count)
    {
        ThrowIfDisposed();
        if (data == null || count == 0) return;
        NativeMethods.FillEllipseBatch(_handle, data, count);
    }

    /// <summary>
    /// Draws an ellipse outline.
    /// </summary>
    public void DrawEllipse(float centerX, float centerY, float radiusX, float radiusY, NativeBrush brush, float strokeWidth = 1.0f)
    {
        ThrowIfDisposed();
        if (brush == null || !brush.IsValid) return;
        NativeMethods.DrawEllipse(_handle, centerX, centerY, radiusX, radiusY, brush.Handle, strokeWidth);
    }

    /// <summary>
    /// Draws a line.
    /// </summary>
    public void DrawLine(float x1, float y1, float x2, float y2, NativeBrush brush, float strokeWidth = 1.0f)
    {
        ThrowIfDisposed();
        if (brush == null || !brush.IsValid) return;
        NativeMethods.DrawLine(_handle, x1, y1, x2, y2, brush.Handle, strokeWidth);
    }

    /// <summary>
    /// Fills a polygon defined by an array of points.
    /// </summary>
    /// <param name="points">Array of point coordinates (x0, y0, x1, y1, ...).</param>
    /// <param name="brush">Brush to fill with.</param>
    /// <param name="fillRule">Fill rule: 0 = EvenOdd, 1 = NonZero.</param>
    public void FillPolygon(float[] points, NativeBrush brush, int fillRule = 0)
    {
        ThrowIfDisposed();
        if (brush == null || !brush.IsValid || points == null || points.Length < 6) return;
        NativeMethods.FillPolygon(_handle, points, points.Length / 2, brush.Handle, fillRule);
    }

    /// <summary>
    /// Draws a polygon outline.
    /// </summary>
    /// <param name="points">Array of point coordinates (x0, y0, x1, y1, ...).</param>
    /// <param name="brush">Brush for stroke.</param>
    /// <param name="strokeWidth">Width of stroke.</param>
    /// <param name="closed">Whether to close the polygon.</param>
    public void DrawPolygon(float[] points, NativeBrush brush, float strokeWidth = 1.0f, bool closed = true, int lineJoin = 0, float miterLimit = 10.0f)
    {
        ThrowIfDisposed();
        if (brush == null || !brush.IsValid || points == null || points.Length < 4) return;
        NativeMethods.DrawPolygon(_handle, points, points.Length / 2, brush.Handle, strokeWidth, closed ? 1 : 0, lineJoin, miterLimit);
    }

    /// <summary>
    /// Fills a path with native bezier curve support.
    /// </summary>
    public void FillPath(float startX, float startY, float[] commands, NativeBrush brush, int fillRule = 0)
    {
        ThrowIfDisposed();
        if (brush == null || !brush.IsValid || commands == null || commands.Length == 0) return;
        NativeMethods.FillPath(_handle, startX, startY, commands, commands.Length, brush.Handle, fillRule);
    }

    /// <summary>
    /// Strokes a path with native bezier curve support.
    /// lineCap: 0 = Butt, 1 = Square, 2 = Round.
    /// </summary>
    public void StrokePath(float startX, float startY, float[] commands, NativeBrush brush, float strokeWidth = 1.0f, bool closed = true, int lineJoin = 0, float miterLimit = 10.0f, int lineCap = 0,
        float[]? dashPattern = null, float dashOffset = 0.0f)
    {
        ThrowIfDisposed();
        if (brush == null || !brush.IsValid || commands == null || commands.Length == 0) return;
        NativeMethods.StrokePath(_handle, startX, startY, commands, commands.Length, brush.Handle, strokeWidth, closed ? 1 : 0, lineJoin, miterLimit, lineCap,
            dashPattern, dashPattern?.Length ?? 0, dashOffset);
    }

    /// <summary>
    /// Draws a content area border: fills rect with bottom-only rounded corners,
    /// strokes U-shape (left + bottom + right, no top) with native D2D arcs.
    /// </summary>
    public void DrawContentBorder(float x, float y, float width, float height,
        float blRadius, float brRadius,
        NativeBrush? fillBrush, NativeBrush? strokeBrush, float strokeWidth = 1.0f)
    {
        ThrowIfDisposed();
        var fillHandle = (fillBrush != null && fillBrush.IsValid) ? fillBrush.Handle : 0;
        var strokeHandle = (strokeBrush != null && strokeBrush.IsValid) ? strokeBrush.Handle : 0;
        if (fillHandle == 0 && strokeHandle == 0) return;
        NativeMethods.DrawContentBorder(_handle, x, y, width, height, blRadius, brRadius, fillHandle, strokeHandle, strokeWidth);
    }

    /// <summary>
    /// Draws text.
    /// </summary>
    public void DrawText(string text, NativeTextFormat format, float x, float y, float width, float height, NativeBrush brush)
    {
        ThrowIfDisposed();
        if (string.IsNullOrEmpty(text) || format == null || !format.IsValid || brush == null || !brush.IsValid) return;

        // Hard guard against unexpected re-entrant draw recursion from native/interop paths.
        // Normal rendering should never exceed a very small depth on a single thread.
        if (_drawTextDepth > 8)
        {
            return;
        }

        _drawTextDepth++;
        try
        {
            unsafe
            {
                fixed (char* textPtr = text)
                {
                    NativeMethods.DrawTextRaw(_handle, textPtr, text.Length, format.Handle, x, y, width, height, brush.Handle);
                }
            }
        }
        finally
        {
            _drawTextDepth--;
        }
    }

    /// <summary>
    /// Pushes a transform matrix.
    /// </summary>
    public void PushTransform(float[] matrix)
    {
        ThrowIfDisposed();
        if (matrix == null || matrix.Length < 6) return;
        NativeMethods.PushTransform(_handle, matrix);
    }

    /// <summary>
    /// Pops the current transform.
    /// </summary>
    public void PopTransform()
    {
        ThrowIfDisposed();
        NativeMethods.PopTransform(_handle);
    }

    /// <summary>
    /// Pushes a clip rectangle (PER_PRIMITIVE anti-aliasing).
    /// </summary>
    public void PushClip(float x, float y, float width, float height)
    {
        ThrowIfDisposed();
        NativeMethods.PushClip(_handle, x, y, width, height);
    }

    /// <summary>
    /// Pushes a clip rectangle with ALIASED anti-aliasing (hard pixel boundary).
    /// Used for dirty region clips where semi-transparent edges cause artifacts.
    /// </summary>
    public void PushClipAliased(float x, float y, float width, float height)
    {
        ThrowIfDisposed();
        NativeMethods.PushClipAliased(_handle, x, y, width, height);
    }

    /// <summary>
    /// Pushes a rounded rectangle clip using a geometry mask layer.
    /// </summary>
    public void PushRoundedRectClip(float x, float y, float width, float height, float rx, float ry)
    {
        ThrowIfDisposed();
        NativeMethods.PushRoundedRectClip(_handle, x, y, width, height, rx, ry);
    }

    /// <summary>
    /// Pops the current clip.
    /// </summary>
    public void PopClip()
    {
        ThrowIfDisposed();
        NativeMethods.PopClip(_handle);
    }

    /// <summary>
    /// Punches a transparent rectangular hole in the current render target.
    /// </summary>
    public void PunchTransparentRect(float x, float y, float width, float height)
    {
        ThrowIfDisposed();
        NativeMethods.PunchTransparentRect(_handle, x, y, width, height);
    }

    /// <summary>
    /// Pushes an opacity value.
    /// </summary>
    public void PushOpacity(float opacity)
    {
        ThrowIfDisposed();
        NativeMethods.PushOpacity(_handle, opacity);
    }

    /// <summary>
    /// Pops the current opacity.
    /// </summary>
    public void PopOpacity()
    {
        ThrowIfDisposed();
        NativeMethods.PopOpacity(_handle);
    }

    /// <summary>
    /// Sets the current shape type for SDF rect rendering.
    /// </summary>
    /// <param name="type">0 = RoundedRect, 1 = SuperEllipse.</param>
    /// <param name="n">SuperEllipse exponent (e.g. 4.0 for squircle).</param>
    public void SetShapeType(int type, float n)
    {
        ThrowIfDisposed();
        NativeMethods.SetShapeType(_handle, type, n);
    }

    /// <summary>
    /// Sets whether VSync is enabled.
    /// When disabled, Present returns immediately for faster frame updates during resize.
    /// </summary>
    /// <param name="enabled">True to enable VSync, false to disable.</param>
    public void SetVSyncEnabled(bool enabled)
    {
        ThrowIfDisposed();
        _native.SetVSyncEnabled(_handle, enabled);
    }

    /// <summary>
    /// Sets the DPI for the render target.
    /// D2D will use this to map DIP coordinates to physical pixels.
    /// </summary>
    /// <param name="dpiX">Horizontal DPI (96 = 100% scaling).</param>
    /// <param name="dpiY">Vertical DPI (96 = 100% scaling).</param>
    public void SetDpi(float dpiX, float dpiY)
    {
        ThrowIfDisposed();
        _dpiX = dpiX;
        _dpiY = dpiY;
        NativeMethods.RenderTargetSetDpi(_handle, dpiX, dpiY);
    }

    /// <summary>
    /// Adds a dirty rectangle for partial rendering optimization.
    /// The native layer uses this to clip D2D drawing and for Present1 dirty rects.
    /// </summary>
    public void AddDirtyRect(float x, float y, float width, float height)
    {
        ThrowIfDisposed();
        NativeMethods.RenderTargetAddDirtyRect(_handle, x, y, width, height);
    }

    /// <summary>
    /// Marks the entire render target as needing full redraw.
    /// </summary>
    public void SetFullInvalidation()
    {
        ThrowIfDisposed();
        _native.SetFullInvalidation(_handle);
    }

    /// <summary>
    /// Attempts to create a composition visual node suitable for WebView composition hosting.
    /// </summary>
    /// <param name="visualTarget">Returns the native visual pointer (IUnknown* on Windows).</param>
    /// <returns>True when a visual was created; false when unsupported or unavailable.</returns>
    public bool TryCreateWebViewCompositionVisual(out nint visualTarget)
    {
        ThrowIfDisposed();
        visualTarget = nint.Zero;

        var resultCode = NativeMethods.RenderTargetCreateWebViewVisual(_handle, out var target);
        if (resultCode == (int)JaliumResult.Ok && target != nint.Zero)
        {
            visualTarget = target;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Destroys a composition visual previously created by <see cref="TryCreateWebViewCompositionVisual"/>.
    /// </summary>
    /// <param name="visualTarget">The native visual pointer.</param>
    public void DestroyWebViewCompositionVisual(nint visualTarget)
    {
        ThrowIfDisposed();
        if (visualTarget == nint.Zero)
        {
            return;
        }

        var resultCode = NativeMethods.RenderTargetDestroyWebViewVisual(_handle, visualTarget);
        ThrowIfNativeFailure("DestroyWebViewVisual", resultCode);
    }

    /// <summary>
    /// Updates the placement and visible clip of a composition visual created for WebView hosting.
    /// </summary>
    public void SetWebViewCompositionVisualPlacement(nint visualTarget, Rectangle bounds, System.Drawing.Point contentOffset)
    {
        ThrowIfDisposed();
        if (visualTarget == nint.Zero)
        {
            return;
        }

        var resultCode = NativeMethods.RenderTargetSetWebViewVisualPlacement(
            _handle,
            visualTarget,
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            contentOffset.X,
            contentOffset.Y);
        ThrowIfNativeFailure("SetWebViewVisualPlacement", resultCode);
    }

    /// <summary>
    /// Draws a bitmap.
    /// </summary>
    /// <param name="bitmap">The bitmap to draw.</param>
    /// <param name="x">The x coordinate.</param>
    /// <param name="y">The y coordinate.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="opacity">The opacity (0-1).</param>
    public void DrawBitmap(NativeBitmap bitmap, float x, float y, float width, float height, float opacity = 1.0f)
    {
        ThrowIfDisposed();
        if (bitmap == null || !bitmap.IsValid) return;
        NativeMethods.DrawBitmap(_handle, bitmap.Handle, x, y, width, height, opacity);
    }

    /// <summary>
    /// Draws a backdrop filter effect.
    /// </summary>
    /// <param name="x">The x coordinate.</param>
    /// <param name="y">The y coordinate.</param>
    /// <param name="width">The width of the filter area.</param>
    /// <param name="height">The height of the filter area.</param>
    /// <param name="backdropFilter">The CSS-style backdrop filter string.</param>
    /// <param name="material">The material type string.</param>
    /// <param name="materialTint">The material tint color string.</param>
    /// <param name="materialTintOpacity">The material tint opacity.</param>
    /// <param name="materialBlurRadius">The material blur radius.</param>
    /// <param name="cornerRadiusTL">Top-left corner radius.</param>
    /// <param name="cornerRadiusTR">Top-right corner radius.</param>
    /// <param name="cornerRadiusBR">Bottom-right corner radius.</param>
    /// <param name="cornerRadiusBL">Bottom-left corner radius.</param>
    public void DrawBackdropFilter(
        float x, float y, float width, float height,
        string? backdropFilter,
        string? material,
        string? materialTint,
        float materialTintOpacity,
        float materialBlurRadius,
        float cornerRadiusTL,
        float cornerRadiusTR,
        float cornerRadiusBR,
        float cornerRadiusBL)
    {
        ThrowIfDisposed();
        NativeMethods.DrawBackdropFilter(
            _handle,
            x, y, width, height,
            backdropFilter ?? string.Empty,
            material ?? string.Empty,
            materialTint ?? string.Empty,
            materialTintOpacity,
            materialBlurRadius,
            cornerRadiusTL,
            cornerRadiusTR,
            cornerRadiusBR,
            cornerRadiusBL);
    }

    /// <summary>
    /// Draws a glowing border highlight effect for DevTools element inspection.
    /// </summary>
    /// <param name="x">The x coordinate of the element.</param>
    /// <param name="y">The y coordinate of the element.</param>
    /// <param name="width">The width of the element.</param>
    /// <param name="height">The height of the element.</param>
    /// <param name="animationPhase">Animation phase (0.0 - 1.0).</param>
    /// <param name="glowColorR">Glow color red component (0-1).</param>
    /// <param name="glowColorG">Glow color green component (0-1).</param>
    /// <param name="glowColorB">Glow color blue component (0-1).</param>
    /// <param name="strokeWidth">Width of the glowing stroke.</param>
    /// <param name="trailLength">Length of the trailing glow (0.0 - 1.0 of perimeter).</param>
    /// <param name="dimOpacity">Opacity of the dimmed area outside (0-1).</param>
    /// <param name="screenWidth">Total screen/window width for dimming.</param>
    /// <param name="screenHeight">Total screen/window height for dimming.</param>
    public void DrawGlowingBorderHighlight(
        float x, float y, float width, float height,
        float animationPhase,
        float glowColorR, float glowColorG, float glowColorB,
        float strokeWidth,
        float trailLength,
        float dimOpacity,
        float screenWidth, float screenHeight)
    {
        ThrowIfDisposed();
        NativeMethods.DrawGlowingBorderHighlight(
            _handle,
            x, y, width, height,
            animationPhase,
            glowColorR, glowColorG, glowColorB,
            strokeWidth,
            trailLength,
            dimOpacity,
            screenWidth, screenHeight);
    }

    /// <summary>
    /// Draws a glowing border transition effect between two elements.
    /// </summary>
    public void DrawGlowingBorderTransition(
        float fromX, float fromY, float fromWidth, float fromHeight,
        float toX, float toY, float toWidth, float toHeight,
        float headProgress, float tailProgress,
        float animationPhase,
        float glowColorR, float glowColorG, float glowColorB,
        float strokeWidth,
        float trailLength,
        float dimOpacity,
        float screenWidth, float screenHeight)
    {
        ThrowIfDisposed();
        NativeMethods.DrawGlowingBorderTransition(
            _handle,
            fromX, fromY, fromWidth, fromHeight,
            toX, toY, toWidth, toHeight,
            headProgress, tailProgress,
            animationPhase,
            glowColorR, glowColorG, glowColorB,
            strokeWidth,
            trailLength,
            dimOpacity,
            screenWidth, screenHeight);
    }

    /// <summary>
    /// Draws a ripple effect expanding from element border.
    /// Used after transition animation completes, before rotation starts.
    /// </summary>
    /// <param name="x">The x coordinate of the element.</param>
    /// <param name="y">The y coordinate of the element.</param>
    /// <param name="width">The width of the element.</param>
    /// <param name="height">The height of the element.</param>
    /// <param name="rippleProgress">Ripple expansion progress (0.0 - 1.0).</param>
    /// <param name="glowColorR">Glow color red component (0-1).</param>
    /// <param name="glowColorG">Glow color green component (0-1).</param>
    /// <param name="glowColorB">Glow color blue component (0-1).</param>
    /// <param name="strokeWidth">Base stroke width.</param>
    /// <param name="dimOpacity">Opacity of the dimmed area outside (0-1).</param>
    /// <param name="screenWidth">Total screen/window width for dimming.</param>
    /// <param name="screenHeight">Total screen/window height for dimming.</param>
    public void DrawRippleEffect(
        float x, float y, float width, float height,
        float rippleProgress,
        float glowColorR, float glowColorG, float glowColorB,
        float strokeWidth,
        float dimOpacity,
        float screenWidth, float screenHeight)
    {
        ThrowIfDisposed();
        NativeMethods.DrawRippleEffect(
            _handle,
            x, y, width, height,
            rippleProgress,
            glowColorR, glowColorG, glowColorB,
            strokeWidth,
            dimOpacity,
            screenWidth, screenHeight);
    }

    /// <summary>
    /// Captures the desktop area at the specified screen coordinates.
    /// The captured content is cached internally for use by DrawDesktopBackdrop.
    /// </summary>
    /// <param name="screenX">Screen X coordinate.</param>
    /// <param name="screenY">Screen Y coordinate.</param>
    /// <param name="width">Width to capture.</param>
    /// <param name="height">Height to capture.</param>
    public void CaptureDesktopArea(int screenX, int screenY, int width, int height)
    {
        ThrowIfDisposed();
        if (width <= 0 || height <= 0) return;
        NativeMethods.CaptureDesktopArea(_handle, screenX, screenY, width, height);
    }

    /// <summary>
    /// Draws the cached desktop capture with Gaussian blur and tint overlay.
    /// Must call CaptureDesktopArea first.
    /// </summary>
    public void DrawDesktopBackdrop(
        float x, float y, float width, float height,
        float blurRadius,
        float tintR, float tintG, float tintB, float tintOpacity,
        float noiseIntensity = 0f, float saturation = 1f)
    {
        ThrowIfDisposed();
        NativeMethods.DrawDesktopBackdrop(
            _handle, x, y, width, height,
            blurRadius, tintR, tintG, tintB, tintOpacity,
            noiseIntensity, saturation);
    }

    /// <summary>
    /// Begins capturing content into an offscreen bitmap for transition shader effects.
    /// </summary>
    /// <param name="slot">0 = old content, 1 = new content.</param>
    /// <param name="x">X position (in DIPs).</param>
    /// <param name="y">Y position (in DIPs).</param>
    /// <param name="w">Width (in DIPs).</param>
    /// <param name="h">Height (in DIPs).</param>
    public void BeginTransitionCapture(int slot, float x, float y, float w, float h)
    {
        ThrowIfDisposed();
        NativeMethods.TransitionBeginCapture(_handle, slot, x, y, w, h);
    }

    /// <summary>
    /// Ends capturing content for a transition slot and restores the main render target.
    /// </summary>
    /// <param name="slot">0 = old content, 1 = new content.</param>
    public void EndTransitionCapture(int slot)
    {
        ThrowIfDisposed();
        NativeMethods.TransitionEndCapture(_handle, slot);
    }

    /// <summary>
    /// Draws the transition shader effect blending old and new content bitmaps.
    /// </summary>
    /// <param name="x">X position (in DIPs).</param>
    /// <param name="y">Y position (in DIPs).</param>
    /// <param name="w">Width (in DIPs).</param>
    /// <param name="h">Height (in DIPs).</param>
    /// <param name="progress">Transition progress (0.0 - 1.0).</param>
    /// <param name="mode">Shader mode index (0-9).</param>
    public void DrawTransitionShader(float x, float y, float w, float h, float progress, int mode, float cornerRadius = 0f)
    {
        ThrowIfDisposed();
        NativeMethods.DrawTransitionShader(_handle, x, y, w, h, progress, mode, cornerRadius);
    }

    /// <summary>
    /// Draws a previously captured transition bitmap to the current render target.
    /// </summary>
    public void DrawCapturedTransition(int slot, float x, float y, float w, float h, float opacity)
    {
        ThrowIfDisposed();
        NativeMethods.DrawCapturedTransition(_handle, slot, x, y, w, h, opacity);
    }

    // ========================================================================
    // Element Effect Capture & Rendering
    // ========================================================================

    /// <summary>
    /// Begins capturing element content into an offscreen bitmap for effect processing.
    /// </summary>
    public void BeginEffectCapture(float x, float y, float w, float h)
    {
        ThrowIfDisposed();
        NativeMethods.EffectBeginCapture(_handle, x, y, w, h);
    }

    /// <summary>
    /// Ends capturing element content and restores the main render target.
    /// </summary>
    public void EndEffectCapture()
    {
        ThrowIfDisposed();
        NativeMethods.EffectEndCapture(_handle);
    }

    /// <summary>
    /// Applies a Gaussian blur effect to the captured element content and draws it.
    /// </summary>
    public void DrawBlurEffect(float x, float y, float w, float h, float radius,
        float uvOffsetX = 0, float uvOffsetY = 0)
    {
        ThrowIfDisposed();
        NativeMethods.DrawBlurEffect(_handle, x, y, w, h, radius, uvOffsetX, uvOffsetY);
    }

    /// <summary>
    /// Applies a drop shadow effect to the captured element content and draws it.
    /// </summary>
    public void DrawDropShadowEffect(float x, float y, float w, float h,
        float blurRadius, float offsetX, float offsetY,
        float r, float g, float b, float a,
        float uvOffsetX = 0, float uvOffsetY = 0,
        float cornerTL = 0, float cornerTR = 0, float cornerBR = 0, float cornerBL = 0)
    {
        ThrowIfDisposed();
        NativeMethods.DrawDropShadowEffect(_handle, x, y, w, h,
            blurRadius, offsetX, offsetY, r, g, b, a,
            uvOffsetX, uvOffsetY,
            cornerTL, cornerTR, cornerBR, cornerBL);
    }

    public void DrawOuterGlowEffect(float x, float y, float w, float h,
        float glowSize, float r, float g, float b, float a, float intensity,
        float uvOffsetX = 0, float uvOffsetY = 0,
        float cornerTL = 0, float cornerTR = 0, float cornerBR = 0, float cornerBL = 0)
    {
        ThrowIfDisposed();
        NativeMethods.DrawOuterGlowEffect(_handle, x, y, w, h,
            glowSize, r, g, b, a, intensity, uvOffsetX, uvOffsetY,
            cornerTL, cornerTR, cornerBR, cornerBL);
    }

    public void DrawInnerShadowEffect(float x, float y, float w, float h,
        float blurRadius, float offsetX, float offsetY,
        float r, float g, float b, float a,
        float uvOffsetX = 0, float uvOffsetY = 0,
        float cornerTL = 0, float cornerTR = 0, float cornerBR = 0, float cornerBL = 0)
    {
        ThrowIfDisposed();
        NativeMethods.DrawInnerShadowEffect(_handle, x, y, w, h,
            blurRadius, offsetX, offsetY, r, g, b, a, uvOffsetX, uvOffsetY,
            cornerTL, cornerTR, cornerBR, cornerBL);
    }

    public void DrawColorMatrixEffect(float x, float y, float w, float h,
        ReadOnlySpan<float> matrix)
    {
        ThrowIfDisposed();
        NativeMethods.DrawColorMatrixEffect(_handle, x, y, w, h, matrix);
    }

    public void DrawEmbossEffect(float x, float y, float w, float h,
        float amount, float lightDirX, float lightDirY, float relief)
    {
        ThrowIfDisposed();
        NativeMethods.DrawEmbossEffect(_handle, x, y, w, h,
            amount, lightDirX, lightDirY, relief);
    }

    /// <summary>
    /// Applies a custom pixel shader effect to the captured element content and draws it.
    /// </summary>
    public void DrawShaderEffect(float x, float y, float w, float h,
        byte[] shaderBytecode, float[] constants)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(shaderBytecode);
        ArgumentNullException.ThrowIfNull(constants);

        NativeMethods.DrawShaderEffect(_handle, x, y, w, h,
            shaderBytecode, (uint)shaderBytecode.Length,
            constants, (uint)constants.Length);
    }

    /// <summary>
    /// Draws a liquid glass effect with SDF-based refraction, highlight, and inner shadow.
    /// </summary>
    public unsafe void DrawLiquidGlass(
        float x, float y, float width, float height,
        float cornerRadius,
        float blurRadius = 8f,
        float refractionAmount = 60f,
        float chromaticAberration = 0f,
        float tintR = 0.08f, float tintG = 0.08f, float tintB = 0.08f,
        float tintOpacity = 0.3f,
        float lightX = -1f, float lightY = -1f,
        float highlightBoost = 0f,
        int shapeType = 0,
        float shapeExponent = 4f,
        int neighborCount = 0,
        float fusionRadius = 30f,
        ReadOnlySpan<float> neighborData = default)
    {
        ThrowIfDisposed();
        if (neighborCount > 0 && neighborData.Length < neighborCount * 5)
            throw new ArgumentException("neighborData too small for neighborCount");
        fixed (float* pNeighbor = neighborData)
        {
            NativeMethods.DrawLiquidGlass(
                _handle, x, y, width, height,
                cornerRadius, blurRadius,
                refractionAmount, chromaticAberration,
                tintR, tintG, tintB, tintOpacity,
                lightX, lightY, highlightBoost,
                shapeType, shapeExponent,
                neighborCount, fusionRadius, (nint)pNeighbor);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void ThrowIfNativeFailure(string stage, int resultCode)
    {
        if (resultCode == (int)JaliumResult.Ok)
        {
            return;
        }

        ThrowRenderPipelineException(stage, resultCode);
    }

    private void ThrowRenderPipelineException(string stage, int resultCode)
    {
        // RenderTarget creation can fail with a null handle while context last-error is still OK.
        // Treat this as a transient resource-creation failure so upper layers can recover/retry.
        bool nullHandleWithOkError = stage == "Create" && resultCode == (int)JaliumResult.Ok;
        int normalizedCode = nullHandleWithOkError
            ? (int)JaliumResult.ResourceCreationFailed
            : resultCode;

        JaliumResult mapped = JaliumResultMapper.FromCode(normalizedCode);
        throw new RenderPipelineException(
            stage: stage,
            result: mapped,
            resultCode: normalizedCode,
            hwnd: _hwnd,
            width: Width,
            height: Height,
            dpiX: _dpiX,
            dpiY: _dpiY,
            backend: _backend.ToString(),
            details: nullHandleWithOkError
                ? "RenderTarget creation returned null handle while context last-error was OK."
                : null);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_isDrawing)
        {
            try { _ = _native.EndDraw(_handle); } catch { }
            _isDrawing = false;
        }

        if (_handle != nint.Zero)
        {
            _native.Destroy(_handle);
            _handle = nint.Zero;
        }

        ReleaseOwnerContextReference();
        GC.SuppressFinalize(this);
    }

    ~RenderTarget()
    {
        if (_handle != nint.Zero)
        {
            NativeMethods.RenderTargetDestroy(_handle);
        }
        _isDrawing = false;
        _disposed = true;
        _handle = nint.Zero;
    }

    private void ReleaseOwnerContextReference()
    {
        if (Interlocked.Exchange(ref _ownerContextReleased, 1) != 0)
        {
            return;
        }

        _ownerContext?.UnregisterRenderTarget();
    }
}

internal interface IRenderTargetNative
{
    nint CreateForSurface(nint context, NativeSurfaceDescriptor surface, int width, int height);
    nint CreateForCompositionSurface(nint context, NativeSurfaceDescriptor surface, int width, int height);
    int GetContextLastError(nint context);
    int Resize(nint renderTarget, int width, int height);
    int BeginDraw(nint renderTarget);
    int EndDraw(nint renderTarget);
    void SetVSyncEnabled(nint renderTarget, bool enabled);
    void SetFullInvalidation(nint renderTarget);
    bool SupportsPartialPresentation(nint renderTarget);
    void Destroy(nint renderTarget);
}

internal sealed class DefaultRenderTargetNative : IRenderTargetNative
{
    internal static readonly DefaultRenderTargetNative Instance = new();

    private DefaultRenderTargetNative()
    {
    }

    public nint CreateForSurface(nint context, NativeSurfaceDescriptor surface, int width, int height)
        => NativeMethods.RenderTargetCreateForSurface(context, in surface, width, height);

    public nint CreateForCompositionSurface(nint context, NativeSurfaceDescriptor surface, int width, int height)
        => NativeMethods.RenderTargetCreateForCompositionSurface(context, in surface, width, height);

    public int GetContextLastError(nint context)
        => NativeMethods.ContextGetLastError(context);

    public int Resize(nint renderTarget, int width, int height)
        => NativeMethods.RenderTargetResize(renderTarget, width, height);

    public int BeginDraw(nint renderTarget)
        => NativeMethods.RenderTargetBeginDraw(renderTarget);

    public int EndDraw(nint renderTarget)
        => NativeMethods.RenderTargetEndDraw(renderTarget);

    public void SetVSyncEnabled(nint renderTarget, bool enabled)
        => NativeMethods.RenderTargetSetVSync(renderTarget, enabled ? 1 : 0);

    public void SetFullInvalidation(nint renderTarget)
        => NativeMethods.RenderTargetSetFullInvalidation(renderTarget);

    public bool SupportsPartialPresentation(nint renderTarget)
        => NativeMethods.RenderTargetSupportsPartialPresentation(renderTarget) != 0;

    public void Destroy(nint renderTarget)
        => NativeMethods.RenderTargetDestroy(renderTarget);
}

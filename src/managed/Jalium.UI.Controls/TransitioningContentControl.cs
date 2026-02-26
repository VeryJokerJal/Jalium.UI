using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls;

/// <summary>
/// A ContentControl that plays transition animations when its content changes.
/// During a transition, both the old content (as an overlay) and the new content
/// are rendered simultaneously. The transition controls their opacity, transforms, and clips.
///
/// Rendering approach: During transitions, normal visual child rendering is suppressed
/// (VisualChildrenCount returns 0). Both old and new content are rendered manually in
/// OnPostRender with full control over Offset, Transform, Opacity, and Clip.
/// This is necessary because the framework's render pipeline uses IOffsetDrawingContext.Offset
/// for positioning (not RenderTransform), so we need direct DrawingContext control.
/// </summary>
public sealed class TransitioningContentControl : ContentControl, TransitionHost
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the Transition dependency property.
    /// </summary>
    public static readonly DependencyProperty TransitionProperty =
        DependencyProperty.Register(nameof(Transition), typeof(ContentTransition), typeof(TransitioningContentControl),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the TransitionMode dependency property.
    /// Provides a shortcut to set common transitions without creating a ContentTransition instance.
    /// </summary>
    public static readonly DependencyProperty TransitionModeProperty =
        DependencyProperty.Register(nameof(TransitionMode), typeof(TransitionMode?), typeof(TransitioningContentControl),
            new PropertyMetadata(null));

    /// <summary>
    /// Gets or sets the custom transition to use when content changes.
    /// Takes precedence over <see cref="TransitionMode"/>.
    /// </summary>
    public ContentTransition? Transition
    {
        get => (ContentTransition?)GetValue(TransitionProperty);
        set => SetValue(TransitionProperty, value);
    }

    /// <summary>
    /// Gets or sets the transition mode. Used when <see cref="Transition"/> is null.
    /// </summary>
    public new TransitionMode? TransitionMode
    {
        get => (TransitionMode?)GetValue(TransitionModeProperty);
        set => SetValue(TransitionModeProperty, value);
    }

    #endregion

    #region Fields

    private UIElement? _outgoingElement;
    private DispatcherTimer? _activeTransition;
    private bool _isTransitioning;

    // Overlay state (old content)
    private double _overlayOpacity = 1.0;
    private Transform? _overlayTransform;
    private Geometry? _overlayClip;

    // Second overlay (for split effects like DoorOpen)
    private ImageSource? _overlayImage2;
    private double _overlay2Opacity = 1.0;
    private Transform? _overlay2Transform;
    private Geometry? _overlay2Clip;

    // New content state
    private double _newContentOpacity = 1.0;
    private Transform? _newContentTransform;
    private Geometry? _newContentClip;

    // Particle system
    private TransitionParticle[]? _activeParticles;
    private bool _particleBitmapCaptured;

    // GPU shader transition
    private int _gpuShaderMode = -1;
    private double _gpuShaderProgress;

    #endregion

    #region Constructor

    public TransitioningContentControl()
    {
        ClipToBounds = true;
    }

    #endregion

    #region Content Change Override

    /// <inheritdoc />
    protected override void OnContentChanged(object? oldContent, object? newContent)
    {
        var transition = GetEffectiveTransition();
        var oldElement = ContentElement;

        if (transition != null && oldElement != null && newContent != null)
        {
            // Cancel any in-progress transition immediately
            CancelTransition();

            // Keep reference to old element before base swaps it out
            _outgoingElement = oldElement;

            // Let base handle the actual content swap
            base.OnContentChanged(oldContent, newContent);

            var newElement = ContentElement;
            if (newElement == null)
            {
                // New content didn't produce an element, skip transition
                _outgoingElement = null;
                return;
            }

            // Ensure new content is laid out before starting transition
            if (RenderSize.Width > 0 && RenderSize.Height > 0)
            {
                newElement.Measure(RenderSize);
                newElement.Arrange(new Rect(0, 0, RenderSize.Width, RenderSize.Height));
            }

            // Start transition
            _isTransitioning = true;
            _overlayOpacity = 1.0;
            _overlayTransform = null;
            _overlayClip = null;
            _newContentOpacity = 1.0;
            _newContentTransform = null;
            _newContentClip = null;
            _overlayImage2 = null;
            _overlay2Opacity = 1.0;
            _overlay2Transform = null;
            _overlay2Clip = null;
            _activeParticles = null;
            _particleBitmapCaptured = false;
            _gpuShaderMode = -1;
            _gpuShaderProgress = 0.0;

            _activeTransition = transition.Run(
                this,
                null, // oldSnapshot not used in element-based approach
                newElement,
                RenderSize.IsEmpty ? new Size(ActualWidth, ActualHeight) : RenderSize,
                OnTransitionCompleted);

            InvalidateVisual();
        }
        else
        {
            // No transition - normal swap
            CancelTransition();
            base.OnContentChanged(oldContent, newContent);
        }
    }

    #endregion

    #region Visual Children

    /// <inheritdoc />
    public override int VisualChildrenCount
    {
        get
        {
            // During transitions, suppress normal child rendering.
            // Both old and new content are rendered manually in OnPostRender
            // to allow full control over positioning and transforms.
            if (_isTransitioning)
                return 0;
            return base.VisualChildrenCount;
        }
    }

    /// <inheritdoc />
    public override Visual? GetVisualChild(int index)
    {
        if (_isTransitioning)
            throw new ArgumentOutOfRangeException(nameof(index));
        return base.GetVisualChild(index);
    }

    #endregion

    #region Rendering

    /// <summary>
    /// Gets the visual offset for an element (VisualBounds position + RenderOffset).
    /// This replicates what Visual.Render does when positioning child elements.
    /// </summary>
    private static (double X, double Y) GetElementOffset(UIElement element)
    {
        var vb = element.VisualBounds;
        var ro = element.RenderOffset;
        return (vb.X + ro.X, vb.Y + ro.Y);
    }

    /// <inheritdoc />
    protected override void OnPostRender(object drawingContext)
    {
        if (_isTransitioning && drawingContext is DrawingContext dc)
        {
            // Push our own clip (since normal ClipToBounds clip was already popped before OnPostRender)
            var bounds = GetTransitionBounds();
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, bounds.Width, bounds.Height)));

            if (_gpuShaderMode >= 0 && dc is RenderTargetDrawingContext rtdc)
            {
                // GPU shader path: capture old+new content to offscreen bitmaps, blend with shader
                RenderWithGpuShader(rtdc, bounds);
            }
            else
            {
                // Managed fallback: opacity/transform/clip based rendering
                RenderNewContent(dc);
                RenderOverlay(dc);
            }

            dc.Pop(); // clip
        }
        base.OnPostRender(drawingContext);
    }

    /// <summary>
    /// Renders the transition using GPU shader effects.
    /// Captures old and new content into offscreen bitmaps, then blends with the shader.
    /// </summary>
    private void RenderWithGpuShader(RenderTargetDrawingContext rtdc, Size bounds)
    {
        var rect = new Rect(0, 0, bounds.Width, bounds.Height);

        // Capture old content to offscreen bitmap 0
        rtdc.BeginTransitionCapture(0, rect);
        RenderContentForCapture(_outgoingElement, rtdc);
        rtdc.EndTransitionCapture(0);

        // Capture new content to offscreen bitmap 1
        rtdc.BeginTransitionCapture(1, rect);
        RenderContentForCapture(ContentElement, rtdc);
        rtdc.EndTransitionCapture(1);

        // Blend with GPU shader
        rtdc.DrawTransitionShader(rect, (float)_gpuShaderProgress, _gpuShaderMode);
    }

    /// <summary>
    /// Renders a content element for GPU shader capture.
    /// </summary>
    private void RenderContentForCapture(UIElement? element, DrawingContext dc)
    {
        if (element == null) return;

        var (ofsX, ofsY) = GetElementOffset(element);
        dc.PushTransform(new TranslateTransform { X = ofsX, Y = ofsY });
        element.Render(dc);
        dc.Pop();
    }

    /// <summary>
    /// Renders the new content element with transition transforms applied.
    /// </summary>
    private void RenderNewContent(DrawingContext dc)
    {
        var element = ContentElement;
        if (element == null) return;

        var pushCount = 0;

        // Apply new content opacity
        if (_newContentOpacity < 1.0)
        {
            dc.PushOpacity(_newContentOpacity);
            pushCount++;
        }

        // Apply new content transform (in transition control's coordinate space)
        if (_newContentTransform != null)
        {
            dc.PushTransform(_newContentTransform);
            pushCount++;
        }

        // Apply new content clip
        if (_newContentClip != null)
        {
            dc.PushClip(_newContentClip);
            pushCount++;
        }

        // Apply element's natural layout position
        var (ofsX, ofsY) = GetElementOffset(element);
        dc.PushTransform(new TranslateTransform { X = ofsX, Y = ofsY });
        pushCount++;

        element.Render(dc);

        for (int i = 0; i < pushCount; i++)
            dc.Pop();
    }

    /// <summary>
    /// Renders the old content overlay with transition transforms applied.
    /// </summary>
    private void RenderOverlay(DrawingContext dc)
    {
        // Render the outgoing element with overlay transforms applied
        if (_outgoingElement != null && _activeParticles == null)
        {
            var pushedCount = 0;

            if (_overlayOpacity < 1.0)
            {
                dc.PushOpacity(_overlayOpacity);
                pushedCount++;
            }

            if (_overlayTransform != null)
            {
                dc.PushTransform(_overlayTransform);
                pushedCount++;
            }

            if (_overlayClip != null)
            {
                dc.PushClip(_overlayClip);
                pushedCount++;
            }

            // Apply element's natural layout position
            var (ofsX, ofsY) = GetElementOffset(_outgoingElement);
            dc.PushTransform(new TranslateTransform { X = ofsX, Y = ofsY });
            pushedCount++;

            _outgoingElement.Render(dc);

            for (int i = 0; i < pushedCount; i++)
                dc.Pop();
        }

        // Render second overlay if needed (for split effects)
        if (_overlayImage2 != null)
        {
            var pushedCount = 0;

            if (_overlay2Opacity < 1.0)
            {
                dc.PushOpacity(_overlay2Opacity);
                pushedCount++;
            }

            if (_overlay2Transform != null)
            {
                dc.PushTransform(_overlay2Transform);
                pushedCount++;
            }

            if (_overlay2Clip != null)
            {
                dc.PushClip(_overlay2Clip);
                pushedCount++;
            }

            var bounds = GetTransitionBounds();
            dc.DrawImage(_overlayImage2, new Rect(0, 0, bounds.Width, bounds.Height));

            for (int i = 0; i < pushedCount; i++)
                dc.Pop();
        }

        // Render particles
        if (_activeParticles != null && _outgoingElement != null)
        {
            RenderParticles(dc);
        }
    }

    private void RenderParticles(DrawingContext dc)
    {
        if (_activeParticles == null || _outgoingElement == null) return;

        // GPU fast path: capture old content to offscreen bitmap once,
        // then draw each particle from the cached bitmap instead of
        // re-rendering the entire visual tree per particle.
        if (dc is RenderTargetDrawingContext rtdc)
        {
            var bounds = GetTransitionBounds();
            var rect = new Rect(0, 0, bounds.Width, bounds.Height);

            // Capture old content to GPU bitmap on first frame only
            if (!_particleBitmapCaptured)
            {
                rtdc.BeginTransitionCapture(0, rect);
                RenderContentForCapture(_outgoingElement, rtdc);
                rtdc.EndTransitionCapture(0);
                _particleBitmapCaptured = true;
            }

            // Draw each particle from the cached bitmap
            foreach (ref var p in _activeParticles.AsSpan())
            {
                if (p.Opacity <= 0.001) continue;

                var centerX = p.SourceRect.X + p.SourceRect.Width / 2;
                var centerY = p.SourceRect.Y + p.SourceRect.Height / 2;

                dc.PushOpacity(p.Opacity);

                var transform = new TransformGroup();
                transform.Add(new TranslateTransform { X = -centerX, Y = -centerY });
                if (Math.Abs(p.Scale - 1.0) > 0.001)
                    transform.Add(new ScaleTransform { ScaleX = p.Scale, ScaleY = p.Scale });
                if (Math.Abs(p.Rotation) > 0.1)
                    transform.Add(new RotateTransform { Angle = p.Rotation });
                transform.Add(new TranslateTransform { X = p.X + centerX, Y = p.Y + centerY });

                dc.PushTransform(transform);
                dc.PushClip(new RectangleGeometry(p.SourceRect));

                // Draw from the captured bitmap instead of re-rendering the element
                rtdc.DrawCapturedTransition(0, rect, (float)1.0);

                dc.Pop(); // clip
                dc.Pop(); // transform
                dc.Pop(); // opacity
            }
            return;
        }

        // Fallback: software path (slower, re-renders element per particle)
        var (elemOfsX, elemOfsY) = GetElementOffset(_outgoingElement);
        foreach (ref var p in _activeParticles.AsSpan())
        {
            if (p.Opacity <= 0.001) continue;

            var centerX = p.SourceRect.X + p.SourceRect.Width / 2;
            var centerY = p.SourceRect.Y + p.SourceRect.Height / 2;

            dc.PushOpacity(p.Opacity);

            var transform = new TransformGroup();
            transform.Add(new TranslateTransform { X = -centerX, Y = -centerY });
            if (Math.Abs(p.Scale - 1.0) > 0.001)
                transform.Add(new ScaleTransform { ScaleX = p.Scale, ScaleY = p.Scale });
            if (Math.Abs(p.Rotation) > 0.1)
                transform.Add(new RotateTransform { Angle = p.Rotation });
            transform.Add(new TranslateTransform { X = p.X + centerX, Y = p.Y + centerY });

            dc.PushTransform(transform);
            dc.PushClip(new RectangleGeometry(p.SourceRect));
            dc.PushTransform(new TranslateTransform { X = elemOfsX, Y = elemOfsY });

            _outgoingElement.Render(dc);

            dc.Pop(); // element offset
            dc.Pop(); // clip
            dc.Pop(); // transform
            dc.Pop(); // opacity
        }
    }

    #endregion

    #region TransitionHost Implementation

    double TransitionHost.OverlayOpacity
    {
        get => _overlayOpacity;
        set
        {
            _overlayOpacity = value;
            InvalidateVisual();
        }
    }

    Transform? TransitionHost.OverlayTransform
    {
        get => _overlayTransform;
        set
        {
            _overlayTransform = value;
            InvalidateVisual();
        }
    }

    Geometry? TransitionHost.OverlayClip
    {
        get => _overlayClip;
        set
        {
            _overlayClip = value;
            InvalidateVisual();
        }
    }

    ImageSource? TransitionHost.OverlayImage
    {
        get => null; // Not used in element-based approach
        set { }
    }

    ImageSource? TransitionHost.OverlayImage2
    {
        get => _overlayImage2;
        set
        {
            _overlayImage2 = value;
            InvalidateVisual();
        }
    }

    double TransitionHost.Overlay2Opacity
    {
        get => _overlay2Opacity;
        set
        {
            _overlay2Opacity = value;
            InvalidateVisual();
        }
    }

    Transform? TransitionHost.Overlay2Transform
    {
        get => _overlay2Transform;
        set
        {
            _overlay2Transform = value;
            InvalidateVisual();
        }
    }

    Geometry? TransitionHost.Overlay2Clip
    {
        get => _overlay2Clip;
        set
        {
            _overlay2Clip = value;
            InvalidateVisual();
        }
    }

    double TransitionHost.NewContentOpacity
    {
        get => _newContentOpacity;
        set
        {
            _newContentOpacity = value;
            InvalidateVisual();
        }
    }

    Transform? TransitionHost.NewContentTransform
    {
        get => _newContentTransform;
        set
        {
            _newContentTransform = value;
            InvalidateVisual();
        }
    }

    Geometry? TransitionHost.NewContentClip
    {
        get => _newContentClip;
        set
        {
            _newContentClip = value;
            InvalidateVisual();
        }
    }

    private Size GetTransitionBounds() =>
        RenderSize.IsEmpty ? new Size(ActualWidth, ActualHeight) : RenderSize;

    Size TransitionHost.TransitionBounds => GetTransitionBounds();

    TransitionParticle[]? TransitionHost.ActiveParticles
    {
        get => _activeParticles;
        set
        {
            _activeParticles = value;
            InvalidateVisual();
        }
    }

    int TransitionHost.GpuShaderMode
    {
        get => _gpuShaderMode;
        set => _gpuShaderMode = value;
    }

    double TransitionHost.GpuShaderProgress
    {
        get => _gpuShaderProgress;
        set => _gpuShaderProgress = value;
    }

    void TransitionHost.InvalidateTransitionVisual()
    {
        InvalidateVisual();
    }

    #endregion

    #region Transition Lifecycle

    private ContentTransition? GetEffectiveTransition()
    {
        return Transition ?? CreateTransitionFromMode(TransitionMode);
    }

    private void OnTransitionCompleted()
    {
        _isTransitioning = false;
        _activeTransition = null;
        _outgoingElement = null;
        _activeParticles = null;
        _particleBitmapCaptured = false;

        // Reset overlay state
        _overlayOpacity = 1.0;
        _overlayTransform = null;
        _overlayClip = null;
        _overlayImage2 = null;
        _overlay2Opacity = 1.0;
        _overlay2Transform = null;
        _overlay2Clip = null;

        // Reset new content state
        _newContentOpacity = 1.0;
        _newContentTransform = null;
        _newContentClip = null;

        // Reset GPU shader state
        _gpuShaderMode = -1;
        _gpuShaderProgress = 0.0;

        InvalidateVisual();
    }

    private void CancelTransition()
    {
        if (_activeTransition != null)
        {
            _activeTransition.Stop();
            _activeTransition = null;
        }

        if (_isTransitioning)
        {
            OnTransitionCompleted();
        }
    }

    internal static ContentTransition? CreateTransitionFromMode(TransitionMode? mode)
    {
        if (mode == null) return null;

        return mode.Value switch
        {
            Media.Animation.TransitionMode.Crossfade => new CrossfadeTransition(),

            Media.Animation.TransitionMode.SlideLeft => new SlideTransition { Direction = SlideDirection.Left },
            Media.Animation.TransitionMode.SlideRight => new SlideTransition { Direction = SlideDirection.Right },
            Media.Animation.TransitionMode.SlideUp => new SlideTransition { Direction = SlideDirection.Up },
            Media.Animation.TransitionMode.SlideDown => new SlideTransition { Direction = SlideDirection.Down },

            Media.Animation.TransitionMode.ZoomIn => new ZoomTransition { Mode = ZoomMode.ZoomIn },
            Media.Animation.TransitionMode.ZoomOut => new ZoomTransition { Mode = ZoomMode.ZoomOut },

            Media.Animation.TransitionMode.FlipHorizontal => new FlipTransition { Axis = FlipAxis.Horizontal },
            Media.Animation.TransitionMode.FlipVertical => new FlipTransition { Axis = FlipAxis.Vertical },
            Media.Animation.TransitionMode.CubeRotate => new CubeRotateTransition(),
            Media.Animation.TransitionMode.DoorOpen => new DoorOpenTransition(),
            Media.Animation.TransitionMode.Carousel => new CarouselTransition(),

            Media.Animation.TransitionMode.WipeLeft => new WipeTransition { Direction = SlideDirection.Left },
            Media.Animation.TransitionMode.WipeRight => new WipeTransition { Direction = SlideDirection.Right },
            Media.Animation.TransitionMode.WipeUp => new WipeTransition { Direction = SlideDirection.Up },
            Media.Animation.TransitionMode.WipeDown => new WipeTransition { Direction = SlideDirection.Down },
            Media.Animation.TransitionMode.WipeDiagonal => new WipeDiagonalTransition(),
            Media.Animation.TransitionMode.IrisReveal => new IrisRevealTransition(),
            Media.Animation.TransitionMode.BlindsHorizontal => new BlindsRevealTransition { Orientation = BlindsOrientation.Horizontal },
            Media.Animation.TransitionMode.BlindsVertical => new BlindsRevealTransition { Orientation = BlindsOrientation.Vertical },

            Media.Animation.TransitionMode.RippleReveal => new ShaderTransition { ShaderMode = ShaderTransitionMode.RippleReveal },
            Media.Animation.TransitionMode.ClockWipe => new ShaderTransition { ShaderMode = ShaderTransitionMode.ClockWipe },
            Media.Animation.TransitionMode.Dissolve => new ShaderTransition { ShaderMode = ShaderTransitionMode.Dissolve },
            Media.Animation.TransitionMode.Pixelate => new ShaderTransition { ShaderMode = ShaderTransitionMode.Pixelate },
            Media.Animation.TransitionMode.Glitch => new ShaderTransition { ShaderMode = ShaderTransitionMode.Glitch },
            Media.Animation.TransitionMode.ChromaticSplit => new ShaderTransition { ShaderMode = ShaderTransitionMode.ChromaticSplit },
            Media.Animation.TransitionMode.LiquidMorph => new ShaderTransition { ShaderMode = ShaderTransitionMode.LiquidMorph },
            Media.Animation.TransitionMode.WaveDistortion => new ShaderTransition { ShaderMode = ShaderTransitionMode.WaveDistortion },
            Media.Animation.TransitionMode.WindBlow => new ShaderTransition { ShaderMode = ShaderTransitionMode.WindBlow },
            Media.Animation.TransitionMode.ThermalFade => new ShaderTransition { ShaderMode = ShaderTransitionMode.ThermalFade },

            Media.Animation.TransitionMode.Shatter => new ShatterTransition(),
            Media.Animation.TransitionMode.ParticleDissolve => new ParticleDissolveTransition(),
            Media.Animation.TransitionMode.FallingTiles => new FallingTilesTransition(),
            Media.Animation.TransitionMode.Vortex => new VortexTransition(),

            Media.Animation.TransitionMode.TypewriterReveal => new TypewriterRevealTransition(),
            Media.Animation.TransitionMode.MatrixRain => new MatrixRainTransition(),
            Media.Animation.TransitionMode.SketchReveal => new SketchRevealTransition(),
            Media.Animation.TransitionMode.GlitchSlice => new GlitchSliceTransition(),

            _ => null,
        };
    }

    #endregion
}

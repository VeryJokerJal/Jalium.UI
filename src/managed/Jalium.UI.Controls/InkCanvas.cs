using System.Buffers;
using System.Collections.Specialized;
using Jalium.UI.Controls.Ink;
using Jalium.UI.Controls.Ink.Shaders;
using Jalium.UI.Input;
using Jalium.UI.Input.StylusPlugIns;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using InkStylusPoint = Jalium.UI.Input.StylusPoint;
using InkStylusPointCollection = Jalium.UI.Input.StylusPointCollection;
using InputStylusPoints = Jalium.UI.Input.StylusPointCollection;

namespace Jalium.UI.Controls;

/// <summary>
/// Provides an area for ink collection and display.
/// </summary>
public class InkCanvas : FrameworkElement
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.InkCanvasAutomationPeer(this);
    }

    #region Private Fields

    private InkStylusPointCollection? _currentPoints;
    private Stroke? _currentStroke;
    private bool _isDrawing;
    private readonly InkPresenter _dynamicInkPresenter = new();
    private DynamicRenderer _dynamicRenderer;
    private readonly InkCollectionStylusPlugIn _inkCollectionStylusPlugIn;
    // RTS background-thread preview plugin. Owns the realtime stylus stroke
    // accumulation that used to happen on the UI thread inside
    // InkCollectionStylusPlugIn / DynamicRenderer — packets are appended on
    // the Jalium.RTS thread, then a Processed callback on the UI thread
    // invalidates the canvas and (on StylusUp) commits the stroke into
    // Strokes. See RealTimeInkPreviewStylusPlugIn.cs for the threading model.
    private readonly RealTimeInkPreviewStylusPlugIn _realTimePreviewPlugIn;

    // Committed strokes are rendered into a dedicated child visual so the
    // system-level retained-mode cache (Visual.RenderCacheHost) can replay
    // them as a single immutable Drawing on frames where only the in-progress
    // stroke (mouse / touch / stylus preview) changes. Without this split,
    // every new point on the active stroke would invalidate the entire
    // InkCanvas, forcing the recorder to walk all N committed strokes again.
    private readonly InkCanvasCommittedLayer _committedLayer;

    // GPU-resident offscreen bitmap that holds every committed stroke as
    // pixels painted by brush shaders. The committed layer blits this
    // bitmap each frame — constant cost per frame regardless of stroke
    // count. Lazily created on first OnRender once a RenderContext is
    // available, resized on layout changes.
    internal InkLayerBitmap? _inkLayer;

    // Secondary offscreen bitmap dedicated to active (in-progress) strokes.
    // Each frame its contents are cleared and every active stroke is
    // re-dispatched into it, then the bitmap is blitted over the committed
    // layer. This keeps the active preview pixel-identical to the eventual
    // commit — exactly the same brush shader produces both.
    private InkLayerBitmap? _previewInkLayer;

    // Cache of compiled brush shader handles, keyed by BrushShader.ShaderKey.
    // Built lazily as strokes of each brush type are committed. Cleared on
    // context change (device lost) or InkCanvas dispose.
    private readonly Dictionary<string, BrushShaderHandle> _shaderHandles = new();

    // RenderContext the _inkLayer + _shaderHandles were created against.
    // If this changes (device lost + recreate) we drop everything and
    // rebuild on the next render.
    private RenderContext? _inkLayerContext;

    // ── GPU ink-layer brush-dispatch failure recovery ──────────────────
    // A native brush dispatch can refuse a stroke with a non-zero return code.
    // The ONLY self-healing class is a Vulkan device-generation mismatch:
    //   -6  the bitmap's generation is device-lost-latched, or
    //   -7  the shader's pipeline was baked on a different generation than the
    //       bitmap (a second window on a different VkDevice swapped the
    //       process-global brush pipeline, so a lazily-compiled shader landed
    //       on the new generation while this canvas's layer is still on the
    //       old one — and _shaderHandles caches by ShaderKey with no knowledge
    //       of the native pipeline swap, so the mismatch is permanent).
    // Rebuilding _inkLayer + _previewInkLayer + every _shaderHandle together
    // re-pairs them on the currently-registered pipeline (the native backend
    // hands the rebuilt bitmap and the rebuilt shaders the SAME cached brush
    // pipeline), so the next dispatch succeeds and committed ink is restored by
    // replaying Strokes. We deliberately do NOT rebuild for any other code:
    // all D3D12 codes (where -6/-7 instead mean a *transient* upload-buffer OOM,
    // indistinguishable from a device-removed symptom that the window's own
    // device-lost recovery already handles out-of-band) and Vulkan's transient
    // -3/-4/-5 must be left to recover on their own — a full teardown+replay
    // there would amplify a one-frame hiccup into a storm. _inkRebuildPending
    // defers the rebuild to the next EnsureInkLayer so we never tear resources
    // down mid-OnRender / mid-replay; if that rebuild still hits a generational
    // failure during its own replay we latch _inkRebuildSuppressed and fall back
    // to the CPU ink path until the context instance itself is replaced (real
    // device-lost recovery), which clears the latch.
    private bool _inkRebuildPending;
    private bool _inkRebuildSuppressed;
    private int _lastLoggedInkDispatchRc;

    // ── Test seam (production: both null) ──────────────────────────────────
    // Lets the GPU-ink failure-recovery state machine be unit-tested without a
    // real Vulkan device — the only backend whose dispatch -6/-7 are
    // deterministic generational failures. _inkNativeOpsOverride substitutes the
    // native ink/brush ops (so DispatchBrush can return a scripted rc and layer/
    // shader construction succeeds headlessly); _inkBackendOverride makes
    // HandleInkDispatchFailure classify failures as if the context were that
    // backend. Both null in production → the real native path is used unchanged.
    internal Jalium.UI.Interop.IInkNativeOps? _inkNativeOpsOverride;
    internal RenderBackend? _inkBackendOverride;

    // Scratch buffer rented from the array pool to marshal stroke points
    // into BrushStrokePoint[] before P/Invoke. Grows on demand; never
    // shrinks (one InkCanvas has a stable upper bound on stroke size).
    private BrushStrokePoint[]? _strokePointsScratch;

    // Seconds since InkCanvas construction — fed into BrushConstants.TimeSeconds
    // so HLSL brushes can animate (airbrush particles drifting, watercolor
    // edge shimmer, etc.). Reset on context change.
    private readonly long _creationTicks = Environment.TickCount64;
    private float AnimationTime => (float)((Environment.TickCount64 - _creationTicks) / 1000.0);

    /// <summary>
    /// Tracks per-touch-pointer active strokes for multi-touch drawing.
    /// Key = touch pointer ID, Value = active stroke data.
    /// </summary>
    private readonly Dictionary<int, TouchStrokeSession> _activeTouchStrokes = new();

    /// <summary>
    /// Minimum distance (in pixels) between consecutive points to avoid jitter.
    /// </summary>
    private const double MinPointDistance = 2.0;

    /// <summary>
    /// Upper bound on the number of resampled points uploaded to the GPU
    /// brush polyline buffer. <c>SdfPolyline</c> in the brush HLSL preamble
    /// is O(N) per pixel (two passes), so an unbounded resample on a
    /// long stroke would tank fill-rate. 2048 covers a freehand stroke
    /// that spans the canvas at ~1 px step and still keeps the per-pixel
    /// shader cost in the GPU's noise floor.
    /// </summary>
    private const int MaxResampledStrokePoints = 2048;

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the Background dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty BackgroundProperty =
        DependencyProperty.Register(nameof(Background), typeof(Brush), typeof(InkCanvas),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the Strokes dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty StrokesProperty =
        DependencyProperty.Register(nameof(Strokes), typeof(StrokeCollection), typeof(InkCanvas),
            new PropertyMetadata(null, OnStrokesChanged));

    /// <summary>
    /// Identifies the DefaultDrawingAttributes dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty DefaultDrawingAttributesProperty =
        DependencyProperty.Register(nameof(DefaultDrawingAttributes), typeof(DrawingAttributes), typeof(InkCanvas),
            new PropertyMetadata(null, OnDefaultDrawingAttributesChanged));

    /// <summary>
    /// Identifies the EditingMode dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty EditingModeProperty =
        DependencyProperty.Register(nameof(EditingMode), typeof(InkCanvasEditingMode), typeof(InkCanvas),
            new PropertyMetadata(InkCanvasEditingMode.Ink, OnEditingModeChanged));

    /// <summary>
    /// Identifies the EraserDiameter dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty EraserDiameterProperty =
        DependencyProperty.Register(nameof(EraserDiameter), typeof(double), typeof(InkCanvas),
            new PropertyMetadata(8.0));

    /// <summary>
    /// Identifies the DefaultStrokeTaperMode dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty DefaultStrokeTaperModeProperty =
        DependencyProperty.Register(nameof(DefaultStrokeTaperMode), typeof(StrokeTaperMode), typeof(InkCanvas),
            new PropertyMetadata(StrokeTaperMode.None));

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="InkCanvas"/> class.
    /// </summary>
    public InkCanvas()
    {
        // The committed-strokes layer must exist before any DP setter that
        // could fire OnStrokesChanged / OnVisualPropertyChanged — those
        // callbacks dereference _committedLayer to invalidate it.
        _committedLayer = new InkCanvasCommittedLayer(this);
        AddVisualChild(_committedLayer);

        Strokes = new StrokeCollection();
        DefaultDrawingAttributes = new DrawingAttributes();

        _dynamicRenderer = new DynamicRenderer
        {
            DrawingAttributes = DefaultDrawingAttributes.Clone()
        };
        _dynamicRenderer.SetInkPresenter(_dynamicInkPresenter);

        _inkCollectionStylusPlugIn = new InkCollectionStylusPlugIn(this);
        _realTimePreviewPlugIn = new RealTimeInkPreviewStylusPlugIn(this);
        // Order matters: the RTS-capable preview plug-in goes first so the
        // background-thread bucket processes it before any UI-thread plug-in
        // partition runs. RealTimeStylus.PartitionPlugIns splits by
        // IsRealTimeCapable, so the visible append-then-DynamicRenderer order
        // in this list is what surface-collection-order users would expect.
        StylusPlugIns.Add(_realTimePreviewPlugIn);
        StylusPlugIns.Add(_dynamicRenderer);
        StylusPlugIns.Add(_inkCollectionStylusPlugIn);

        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownHandler));
        AddHandler(MouseMoveEvent, new MouseEventHandler(OnMouseMoveHandler));
        AddHandler(MouseUpEvent, new MouseButtonEventHandler(OnMouseUpHandler));
        AddHandler(MouseLeaveEvent, new MouseEventHandler(OnMouseLeaveHandler));

        AddHandler(PreviewStylusDownEvent, new Input.StylusDownEventHandler((s, e) => OnPreviewStylusInputHandler(s, e)));
        AddHandler(PreviewStylusMoveEvent, new Input.StylusEventHandler(OnPreviewStylusInputHandler));
        AddHandler(PreviewStylusUpEvent, new Input.StylusEventHandler(OnPreviewStylusInputHandler));

        // Touch event handlers — provide a direct fallback path for touch input.
        // This allows multi-touch drawing even when touch → stylus promotion is not
        // available or when the stylus plugin pipeline does not consume the event.
        AddHandler(TouchDownEvent, new TouchEventHandler(OnTouchDownHandler));
        AddHandler(TouchMoveEvent, new TouchEventHandler(OnTouchMoveHandler));
        AddHandler(TouchUpEvent, new TouchEventHandler(OnTouchUpHandler));

        // Release GPU resources when this InkCanvas leaves the visual tree.
        // Without this, an ink layer + compiled shader PSOs linger until GC
        // runs the finalizer, which can delay reclaim by seconds in debug
        // builds and keep a device-lost reference alive across recovery.
        Unloaded += (_, _) => DisposeInkLayerResources();
    }

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the background brush for the InkCanvas.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? Background
    {
        get => (Brush?)GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the collection of strokes displayed on this InkCanvas.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public StrokeCollection Strokes
    {
        get => (StrokeCollection?)GetValue(StrokesProperty) ?? new StrokeCollection();
        set => SetValue(StrokesProperty, value);
    }

    /// <summary>
    /// Gets or sets the default drawing attributes for new strokes.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public DrawingAttributes DefaultDrawingAttributes
    {
        get => (DrawingAttributes?)GetValue(DefaultDrawingAttributesProperty) ?? new DrawingAttributes();
        set => SetValue(DefaultDrawingAttributesProperty, value);
    }

    /// <summary>
    /// Gets or sets the dynamic renderer used for real-time stylus preview.
    /// </summary>
    public DynamicRenderer DynamicRenderer
    {
        get => _dynamicRenderer;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (ReferenceEquals(_dynamicRenderer, value))
            {
                return;
            }

            _dynamicRenderer.SetInkPresenter(null);
            StylusPlugIns.Remove(_dynamicRenderer);

            _dynamicRenderer = value;
            _dynamicRenderer.DrawingAttributes = DefaultDrawingAttributes.Clone();
            _dynamicRenderer.SetInkPresenter(_dynamicInkPresenter);
            StylusPlugIns.Insert(0, _dynamicRenderer);
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Gets or sets the editing mode for this InkCanvas.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public InkCanvasEditingMode EditingMode
    {
        get => (InkCanvasEditingMode)(GetValue(EditingModeProperty) ?? InkCanvasEditingMode.Ink);
        set => SetValue(EditingModeProperty, value);
    }

    /// <summary>
    /// Gets or sets the diameter of the eraser.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public double EraserDiameter
    {
        get => (double)GetValue(EraserDiameterProperty)!;
        set => SetValue(EraserDiameterProperty, value);
    }

    /// <summary>
    /// Gets or sets the default taper mode for new strokes.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public StrokeTaperMode DefaultStrokeTaperMode
    {
        get => (StrokeTaperMode)(GetValue(DefaultStrokeTaperModeProperty) ?? StrokeTaperMode.None);
        set => SetValue(DefaultStrokeTaperModeProperty, value);
    }

    #endregion

    #region Events

    /// <summary>
    /// Occurs when a new stroke has been collected.
    /// </summary>
    public event EventHandler<InkCanvasStrokeCollectedEventArgs>? StrokeCollected;

    /// <summary>
    /// Occurs when a stroke is about to be erased.
    /// </summary>
    public event EventHandler<InkCanvasStrokeErasingEventArgs>? StrokeErasing;

    /// <summary>
    /// Occurs when the strokes collection changes.
    /// </summary>
    public event EventHandler? StrokesChanged;

    /// <summary>
    /// Occurs when the editing mode changes.
    /// </summary>
    public event EventHandler<RoutedEventArgs>? EditingModeChanged;

    #endregion

    #region Public Methods

    /// <summary>
    /// Clears all strokes from this InkCanvas.
    /// </summary>
    public void ClearStrokes()
    {
        Strokes.Clear();
    }

    #endregion

    #region Layout

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        // Clamp infinite dimensions to zero to avoid layout crashes
        // when InkCanvas is inside unconstrained containers (e.g. ScrollViewer).
        var size = new Size(
            double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
        _committedLayer.Measure(size);
        return size;
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        _committedLayer.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
        return finalSize;
    }

    #endregion

    #region Rendering

    /// <inheritdoc/>
    protected override void OnRender(DrawingContext drawingContext)
    {
        // Background and committed strokes are rendered by _committedLayer
        // (a child visual). Active content (current mouse stroke, multi-touch
        // strokes, stylus preview) must paint AFTER the committed layer so it
        // sits on top — render order is parent.OnRender → children.Render →
        // parent.OnPostRender, so we draw the active overlay in OnPostRender.
    }

    /// <inheritdoc/>
    protected override void OnPostRender(DrawingContext drawingContext)
    {
        var dc = drawingContext;

        // Shader-based active preview. The preview bitmap is fully rebuilt
        // every frame (cleared + re-dispatched) so the user sees the exact
        // same pixel shader output that a commit would bake into the
        // committed layer. Falls back to the legacy CPU path when there's
        // no RTDC (test probes / recorders) or the pipeline is still
        // bootstrapping.
        if (dc is Jalium.UI.Interop.RenderTargetDrawingContext rtdc
            && _previewInkLayer is { IsValid: true })
        {
            _previewInkLayer.Clear();

            // Mouse active stroke. Eraser strokes are skipped here — they
            // can't meaningfully paint a transparent preview bitmap; a CPU
            // cursor ring is drawn below instead to show drag position.
            if (_currentStroke != null && _currentPoints != null && _currentPoints.Count >= 2
                && !IsEraserStroke(_currentStroke))
            {
                DispatchStrokeToInkLayer(_currentStroke, _previewInkLayer);
            }

            // Multi-touch active strokes (eraser not exposed via touch today)
            foreach (var session in _activeTouchStrokes.Values)
            {
                if (session.Points.Count >= 2 && !IsEraserStroke(session.Stroke))
                    DispatchStrokeToInkLayer(session.Stroke, _previewInkLayer);
            }

            // Real-time stylus preview — accumulated on the Jalium.RTS thread
            // by RealTimeInkPreviewStylusPlugIn. Each session owns a thread-safe
            // point buffer; snapshot it on the UI thread, wrap in a transient
            // Stroke, and dispatch through the same brush-shader path as mouse
            // / touch. Eraser sessions are *both* dispatched to the preview
            // layer (so the user sees the cursor cap) AND incrementally
            // applied to the committed layer below — same as the mouse path.
            var rtsSessions = _realTimePreviewPlugIn.SnapshotSessions();
            for (int i = 0; i < rtsSessions.Length; i++)
            {
                var s = rtsSessions[i];
                if (s.Attributes is null) continue;
                var pts = s.SnapshotPoints();
                if (pts.Length < 2) continue;
                if (s.IsEraser)
                {
                    // Eraser: paint into committed layer (idempotent erase
                    // pass) and also paint the cursor ring below.
                    DispatchStrokePointsToInkLayer(pts, s.Attributes, _inkLayer);
                }
                else
                {
                    DispatchStrokePointsToInkLayer(pts, s.Attributes, _previewInkLayer);
                }
            }

            // Legacy DynamicRenderer preview — only consulted when the RTS
            // plug-in has no active session (e.g. RTS disabled in a test).
            if (rtsSessions.Length == 0)
            {
                var stylusStroke = _dynamicRenderer.CurrentPreviewStroke;
                if (stylusStroke != null && !IsEraserStroke(stylusStroke))
                    DispatchStrokeToInkLayer(stylusStroke, _previewInkLayer);
            }

            rtdc.BlitInkLayer(_previewInkLayer, 0, 0, 1.0f);

            // Eraser cursor ring — drawn on top of the main RT (not the
            // preview bitmap) so it sits above already-committed ink. The
            // ink underneath has already been erased in place by the
            // incremental DispatchStrokeToInkLayer calls in ContinueDrawing.
            if (_currentStroke != null && IsEraserStroke(_currentStroke)
                && _currentPoints != null && _currentPoints.Count > 0)
            {
                var last = _currentPoints[_currentPoints.Count - 1];
                var radius = EraserDiameter * 0.5;
                var ringBrush = new Media.SolidColorBrush(
                    Media.Color.FromArgb(160, 128, 128, 128));
                var ringPen = new Media.Pen(ringBrush, 1.5);
                dc.DrawEllipse(null, ringPen, new Point(last.X, last.Y), radius, radius);
            }
            // RTS-session eraser cursor: mirror the mouse path so the user
            // sees a ring under the pen tip during a stylus EraseByPoint drag.
            for (int i = 0; i < rtsSessions.Length; i++)
            {
                var s = rtsSessions[i];
                if (!s.IsEraser || s.Attributes is null) continue;
                var pts = s.SnapshotPoints();
                if (pts.Length == 0) continue;
                var last = pts[pts.Length - 1];
                var radius = EraserDiameter * 0.5;
                var ringBrush = new Media.SolidColorBrush(
                    Media.Color.FromArgb(160, 128, 128, 128));
                var ringPen = new Media.Pen(ringBrush, 1.5);
                dc.DrawEllipse(null, ringPen, new Point(last.X, last.Y), radius, radius);
            }
            return;
        }

        // Fallback CPU preview (non-RTDC contexts)
        _currentStroke?.Draw(dc);
        foreach (var session in _activeTouchStrokes.Values)
            session.Stroke.Draw(dc);
        // RTS sessions on the CPU fallback: synthesize a transient Stroke
        // from the snapshot points and draw it through the legacy CPU path.
        var rtsSessionsCpu = _realTimePreviewPlugIn.SnapshotSessions();
        for (int i = 0; i < rtsSessionsCpu.Length; i++)
        {
            var s = rtsSessionsCpu[i];
            if (s.Attributes is null) continue;
            var pts = s.SnapshotPoints();
            if (pts.Length < 2) continue;
            var coll = new InkStylusPointCollection(pts);
            new Stroke(coll, s.Attributes).Draw(dc);
        }
        // Legacy DynamicRenderer preview — only consulted when the RTS plug-in
        // has no active session, mirroring the RTDC branch above. Both plug-ins
        // accumulate the same stylus stream, so drawing both here would render
        // the in-flight stroke twice.
        if (rtsSessionsCpu.Length == 0)
            _dynamicRenderer.DrawPreview(dc);
    }

    #endregion

    #region Input Handling

    private void OnMouseDownHandler(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        var position = e.GetPosition(this);

        switch (EditingMode)
        {
            case InkCanvasEditingMode.Ink:
                StartDrawing(position);
                break;

            case InkCanvasEditingMode.EraseByStroke:
                // Whole-stroke erase: hit-test + remove matched strokes.
                EraseStrokesAt(position);
                break;

            case InkCanvasEditingMode.EraseByPoint:
                // Per-point erase: drive the same active-stroke pipeline as
                // Ink, but with the eraser brush shader attached. The Erase
                // blend mode subtracts from the committed ink layer as the
                // drag progresses.
                StartDrawing(position, BuildEraserAttributes());
                break;
        }

        e.Handled = true;
    }

    private void OnMouseMoveHandler(object sender, MouseEventArgs e)
    {
        var position = e.GetPosition(this);

        // Ink + EraseByPoint both go through the active-stroke pipeline —
        // ContinueDrawing inspects the stroke's brush and dispatches the
        // eraser shader to the committed layer incrementally.
        if (_isDrawing &&
            (EditingMode == InkCanvasEditingMode.Ink
             || EditingMode == InkCanvasEditingMode.EraseByPoint))
        {
            ContinueDrawing(position);
            e.Handled = true;
        }
        else if (e.LeftButton == MouseButtonState.Pressed
              && EditingMode == InkCanvasEditingMode.EraseByStroke)
        {
            EraseStrokesAt(position);
            e.Handled = true;
        }
    }

    private void OnMouseUpHandler(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        if (_isDrawing &&
            (EditingMode == InkCanvasEditingMode.Ink
             || EditingMode == InkCanvasEditingMode.EraseByPoint))
        {
            FinishDrawing();
            e.Handled = true;
        }
    }

    private void OnMouseLeaveHandler(object sender, MouseEventArgs e)
    {
        if (_isDrawing && EditingMode == InkCanvasEditingMode.Ink)
        {
            FinishDrawing();
        }
    }

    private void OnPreviewStylusInputHandler(object sender, Input.StylusEventArgs e)
    {
        if (EditingMode is InkCanvasEditingMode.Ink or InkCanvasEditingMode.EraseByStroke or InkCanvasEditingMode.EraseByPoint)
        {
            e.Handled = true;
        }
    }

    #region Touch Input

    private void OnTouchDownHandler(object sender, TouchEventArgs e)
    {
        var touchDevice = e.TouchDevice;
        var position = touchDevice.GetTouchPoint(this).Position;
        float pressure = GetTouchPressure(e);

        switch (EditingMode)
        {
            case InkCanvasEditingMode.Ink:
                StartTouchDrawing(touchDevice.Id, position, pressure);
                touchDevice.Capture(this);
                break;

            case InkCanvasEditingMode.EraseByStroke:
                // Whole-stroke erase by touch: hit-test + remove.
                EraseStrokesAt(position);
                touchDevice.Capture(this);
                break;

            case InkCanvasEditingMode.EraseByPoint:
                // Per-point erase by touch: same pipeline as Ink but with
                // an eraser brush shader attached.
                StartTouchDrawing(touchDevice.Id, position, pressure, BuildEraserAttributes());
                touchDevice.Capture(this);
                break;
        }

        e.Handled = true;
    }

    private void OnTouchMoveHandler(object sender, TouchEventArgs e)
    {
        var touchDevice = e.TouchDevice;
        var position = touchDevice.GetTouchPoint(this).Position;
        float pressure = GetTouchPressure(e);

        switch (EditingMode)
        {
            case InkCanvasEditingMode.Ink:
            case InkCanvasEditingMode.EraseByPoint:
                ContinueTouchDrawing(touchDevice.Id, position, pressure);
                break;

            case InkCanvasEditingMode.EraseByStroke:
                EraseStrokesAt(position);
                break;
        }

        e.Handled = true;
    }

    private void OnTouchUpHandler(object sender, TouchEventArgs e)
    {
        var touchDevice = e.TouchDevice;
        var position = touchDevice.GetTouchPoint(this).Position;

        switch (EditingMode)
        {
            case InkCanvasEditingMode.Ink:
            case InkCanvasEditingMode.EraseByPoint:
                FinishTouchDrawing(touchDevice.Id);
                break;

            case InkCanvasEditingMode.EraseByStroke:
                EraseStrokesAt(position);
                break;
        }

        touchDevice.Capture(null);
        e.Handled = true;
    }

    private void StartTouchDrawing(int pointerId, Point position, float pressure, DrawingAttributes? overrideAttributes = null)
    {
        var points = new InkStylusPointCollection();
        points.Add(new InkStylusPoint(position.X, position.Y, pressure));

        var attrs = overrideAttributes ?? DefaultDrawingAttributes.Clone();
        var stroke = new Stroke(points, attrs)
        {
            TaperMode = DefaultStrokeTaperMode
        };

        _activeTouchStrokes[pointerId] = new TouchStrokeSession(points, stroke);
        InvalidatePoint(position, stroke.DrawingAttributes);
        // Eraser fires an immediate dispatch so the first cap shows up
        // under the finger before any move event arrives.
        if (IsEraserStroke(stroke))
            DispatchStrokeToInkLayer(stroke);
    }

    private void ContinueTouchDrawing(int pointerId, Point position, float pressure)
    {
        if (!_activeTouchStrokes.TryGetValue(pointerId, out var session))
            return;

        var points = session.Points;
        Point lastPoint = position;
        if (points.Count > 0)
        {
            var lastSp = points[points.Count - 1];
            lastPoint = new Point(lastSp.X, lastSp.Y);
            var dx = position.X - lastPoint.X;
            var dy = position.Y - lastPoint.Y;
            if (dx * dx + dy * dy < MinPointDistance * MinPointDistance)
                return;
        }

        points.Add(new InkStylusPoint(position.X, position.Y, pressure));
        InvalidateActiveStroke(session.Stroke);

        // Eraser incremental: write to committed layer during drag so the
        // touched ink fades in real time, mirroring the mouse path.
        if (IsEraserStroke(session.Stroke))
            DispatchStrokeToInkLayer(session.Stroke);
    }

    private void FinishTouchDrawing(int pointerId)
    {
        if (!_activeTouchStrokes.TryGetValue(pointerId, out var session))
            return;

        _activeTouchStrokes.Remove(pointerId);

        if (session.Points.Count > 0)
        {
            var finishedBounds = session.Stroke.GetBounds();
            Strokes.Add(session.Stroke);
            OnStrokeCollected(new InkCanvasStrokeCollectedEventArgs(session.Stroke));
            InvalidateVisual(finishedBounds);
        }
    }

    /// <summary>
    /// Extracts pressure from a touch event. Returns the device pressure
    /// if available, otherwise the default pressure.
    /// </summary>
    private static float GetTouchPressure(TouchEventArgs e)
    {
        // TouchPoint itself doesn't carry pressure in the base API.
        // When touch → stylus promotion is active, the stylus pipeline handles pressure.
        // For the direct touch path, use default pressure.
        return InkStylusPoint.DefaultPressure;
    }

    /// <summary>
    /// Represents an active multi-touch stroke drawing session.
    /// </summary>
    private sealed class TouchStrokeSession
    {
        public InkStylusPointCollection Points { get; }
        public Stroke Stroke { get; }

        public TouchStrokeSession(InkStylusPointCollection points, Stroke stroke)
        {
            Points = points;
            Stroke = stroke;
        }
    }

    #endregion

    #endregion

    #region Drawing Logic

    /// <summary>
    /// Starts an active stroke. <paramref name="overrideAttributes"/>
    /// lets the caller swap in a different brush for the drag — used by
    /// the EraseByPoint editing mode to drive an eraser shader stroke
    /// through the same Start/Continue/Finish path as Ink.
    /// </summary>
    private void StartDrawing(Point position, DrawingAttributes? overrideAttributes = null)
    {
        _currentPoints = new InkStylusPointCollection();
        _currentPoints.Add(new InkStylusPoint(position.X, position.Y));
        var attrs = overrideAttributes ?? DefaultDrawingAttributes.Clone();
        _currentStroke = new Stroke(_currentPoints, attrs);
        _currentStroke.TaperMode = DefaultStrokeTaperMode;
        _isDrawing = true;

        CaptureMouse();
        InvalidatePoint(position, attrs);
    }

    private void ContinueDrawing(Point position)
    {
        if (_currentPoints == null || _currentPoints.Count == 0)
            return;

        // Check minimum distance from the last point to avoid jitter
        var lastPointStruct = _currentPoints[_currentPoints.Count - 1];
        var lastPoint = new Point(lastPointStruct.X, lastPointStruct.Y);
        var dx = position.X - lastPoint.X;
        var dy = position.Y - lastPoint.Y;
        var distanceSquared = dx * dx + dy * dy;

        if (distanceSquared < MinPointDistance * MinPointDistance)
            return;

        _currentPoints.Add(new InkStylusPoint(position.X, position.Y));
        if (_currentStroke is null) return;

        InvalidateActiveStroke(_currentStroke);

        // Eraser: dispatch to the committed ink layer on every move so the
        // user sees pixels fade during drag. The Erase blend mode is
        // idempotent (erasing an already-transparent pixel is a no-op),
        // so redispatching the ever-growing full stroke per move is safe
        // if wasteful — only newly-reached pixels actually change state.
        // On FinishDrawing we still append the stroke to Strokes so undo
        // replays are correct; the Add handler's re-dispatch is another
        // idempotent pass.
        if (IsEraserStroke(_currentStroke))
            DispatchStrokeToInkLayer(_currentStroke);
    }

    /// <summary>
    /// True when <paramref name="stroke"/>'s brush is the built-in eraser
    /// shader — triggers the incremental "paint-into-committed-layer"
    /// path during drag.
    /// </summary>
    private static bool IsEraserStroke(Stroke stroke)
        => stroke?.DrawingAttributes?.BrushShader is Jalium.UI.Controls.Ink.Shaders.EraserBrushShader;

    /// <summary>
    /// Builds a fresh eraser DrawingAttributes: zero-based copy of the
    /// canvas defaults, width = EraserDiameter, brush = eraser shader.
    /// Used by EraseByPoint mode when translating a mouse drag into a
    /// stroke recorded on the Strokes collection.
    /// </summary>
    private DrawingAttributes BuildEraserAttributes()
        => new()
        {
            Width          = EraserDiameter,
            Height         = EraserDiameter,
            Color          = Media.Colors.Black,
            FitToCurve     = true,
            IgnorePressure = true,
            BrushShader    = Jalium.UI.Controls.Ink.Shaders.EraserBrushShader.Instance,
        };

    /// <summary>
    /// UI-thread snapshot of the active Ink-mode brush. Called once per stylus
    /// session by <see cref="RealTimeInkPreviewStylusPlugIn"/>'s Down Processed
    /// callback so the RTS background thread never has to touch the canvas DPs.
    /// </summary>
    internal DrawingAttributes BuildInkAttributesForRtsPreview() => DefaultDrawingAttributes.Clone();

    /// <summary>
    /// UI-thread snapshot of the eraser brush — counterpart to
    /// <see cref="BuildInkAttributesForRtsPreview"/> for the EraseByPoint
    /// editing mode.
    /// </summary>
    internal DrawingAttributes BuildEraserAttributesForRtsPreview() => BuildEraserAttributes();

    /// <summary>
    /// UI-thread entry point fired from the RTS plug-in's Processed callbacks.
    /// Invalidates the preview region so <see cref="OnPostRender"/> re-blits
    /// the per-frame preview bitmap with the latest snapshot points.
    /// </summary>
    internal void NotifyRealTimePreviewInvalidate(RealTimePreviewSession session)
    {
        if (session is null) return;
        var bounds = session.SnapshotBounds();
        // Inflate by half the stroke radius (clamped at 2 px) so the bitmap
        // rebuild covers the full stroke cap, not just the centerline bbox.
        if (session.Attributes is { } attrs)
        {
            double pad = Math.Max(2.0, Math.Max(attrs.Width, attrs.Height) * 0.5 + 2.0);
            if (!bounds.IsEmpty)
            {
                bounds = new Rect(bounds.X - pad, bounds.Y - pad,
                    bounds.Width + pad * 2, bounds.Height + pad * 2);
            }
        }
        if (bounds.IsEmpty)
            InvalidateVisual();
        else
            InvalidateVisual(bounds);
    }

    /// <summary>
    /// UI-thread entry point fired from the RTS plug-in's StylusUp Processed
    /// callback. Materialises the accumulated points as a real
    /// <see cref="Stroke"/> + appends it to <see cref="Strokes"/>; the
    /// StrokeCollection change handler picks it up and bakes the stroke into
    /// the committed ink-layer bitmap.
    /// </summary>
    internal void CommitRealTimePreviewSession(RealTimePreviewSession session)
    {
        if (session is null || session.Attributes is null) return;
        var pts = session.SnapshotPoints();
        if (pts.Length == 0) return;

        var coll = new InkStylusPointCollection(pts);
        var stroke = new Stroke(coll, session.Attributes)
        {
            TaperMode = DefaultStrokeTaperMode
        };
        var finishedBounds = stroke.GetBounds();
        Strokes.Add(stroke);
        OnStrokeCollected(new InkCanvasStrokeCollectedEventArgs(stroke));
        InvalidateVisual(finishedBounds);
    }

    private void FinishDrawing()
    {
        if (_currentStroke == null || _currentPoints == null)
        {
            _isDrawing = false;
            ReleaseMouseCapture();
            return;
        }

        // Only add stroke if it has at least one point
        if (_currentPoints.Count > 0)
        {
            // Capture bounds before the stroke is handed off to the committed
            // collection so the active layer can repaint exactly the region
            // the in-progress stroke last occupied (vs invalidating the whole
            // InkCanvas).
            var finishedBounds = _currentStroke.GetBounds();
            Strokes.Add(_currentStroke);
            OnStrokeCollected(new InkCanvasStrokeCollectedEventArgs(_currentStroke));
            InvalidateVisual(finishedBounds);
        }

        _currentStroke = null;
        _currentPoints = null;
        _isDrawing = false;

        ReleaseMouseCapture();
        // No invalidate needed here: the active overlay region was already
        // queued above (or there was nothing to draw at all). The committed
        // layer's invalidation is driven by the Strokes.Add() callback.
    }

    /// <summary>
    /// Invalidates the visual around a single point (stroke start / tap).
    /// </summary>
    private void InvalidatePoint(Point point, DrawingAttributes attributes)
    {
        var pad = Math.Max(attributes.Width, attributes.Height) / 2.0 + 4.0;
        InvalidateVisual(new Rect(point.X - pad, point.Y - pad, pad * 2, pad * 2));
    }

    /// <summary>
    /// Invalidates the current active stroke's full bounds. The shader
    /// preview pipeline clears + re-dispatches the whole preview bitmap
    /// each frame, so the dirty region has to cover every pixel the
    /// previous frame's blit touched as well as the new one — which is
    /// the full stroke bbox, not just the new segment. Calling this on
    /// every point append keeps the preview tight to the actual active
    /// region without paying for a whole-canvas repaint.
    /// </summary>
    private void InvalidateActiveStroke(Stroke stroke)
    {
        if (stroke is null) return;
        var bounds = stroke.GetBounds();
        if (bounds.IsEmpty)
            InvalidateVisual();
        else
            InvalidateVisual(bounds);
    }

    /// <summary>Legacy helper kept for single-segment invalidation callers.</summary>
    private void InvalidateSegment(Point a, Point b, DrawingAttributes attributes)
    {
        var pad = Math.Max(attributes.Width, attributes.Height) / 2.0 + 4.0;
        var minX = Math.Min(a.X, b.X) - pad;
        var minY = Math.Min(a.Y, b.Y) - pad;
        var maxX = Math.Max(a.X, b.X) + pad;
        var maxY = Math.Max(a.Y, b.Y) + pad;
        InvalidateVisual(new Rect(minX, minY, maxX - minX, maxY - minY));
    }

    #endregion

    #region Erasing Logic

    private void EraseStrokesAt(Point position)
    {
        var hitStrokes = Strokes.HitTest(position, EraserDiameter);

        foreach (var stroke in hitStrokes)
        {
            var erasingArgs = new InkCanvasStrokeErasingEventArgs(stroke);
            OnStrokeErasing(erasingArgs);

            if (!erasingArgs.Cancel)
            {
                Strokes.Remove(stroke);
            }
        }

        if (hitStrokes.Count > 0)
        {
            InvalidateVisual();
        }
    }

    private void EraseStrokesAt(InputStylusPoints points)
    {
        foreach (var point in points)
        {
            EraseStrokesAt(new Point(point.X, point.Y));
        }
    }

    private void CommitStylusStroke(InkStylusPointCollection points, DrawingAttributes? overrideAttributes = null)
    {
        if (points.Count == 0)
        {
            return;
        }

        var attrs = overrideAttributes ?? DefaultDrawingAttributes.Clone();
        var stroke = new Stroke(points, attrs)
        {
            TaperMode = DefaultStrokeTaperMode
        };

        Strokes.Add(stroke);
        OnStrokeCollected(new InkCanvasStrokeCollectedEventArgs(stroke));
        InvalidateVisual();
    }

    /// <summary>
    /// Dispatches an incremental eraser stroke onto the committed ink layer
    /// during an active stylus / touch / mouse drag. Wrapped for the stylus
    /// plugin and the touch handler, which both need the same "erase as you
    /// drag" behaviour as the mouse EraseByPoint path.
    /// </summary>
    internal void IncrementalEraserDispatch(Stroke stroke)
    {
        if (stroke is null) return;
        if (!IsEraserStroke(stroke)) return;
        DispatchStrokeToInkLayer(stroke);
    }

    #endregion

    #region Event Handlers

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is InkCanvas canvas)
        {
            // Background lives on the committed layer; only that layer needs
            // to re-record. Invalidating the InkCanvas itself would force the
            // active-stroke recording to re-emit too.
            canvas._committedLayer.InvalidateVisual();
        }
    }

    private static void OnStrokesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not InkCanvas canvas)
            return;

        if (e.OldValue is StrokeCollection oldCollection)
        {
            oldCollection.CollectionChanged -= canvas.OnStrokesCollectionChanged;
        }

        if (e.NewValue is StrokeCollection newCollection)
        {
            newCollection.CollectionChanged += canvas.OnStrokesCollectionChanged;
        }

        canvas._committedLayer.InvalidateVisual();
        canvas.StrokesChanged?.Invoke(canvas, EventArgs.Empty);
    }

    private void OnStrokesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Mirror the collection change onto the GPU ink-layer bitmap:
        //   Add    → dispatch just the new strokes (cheap, incremental)
        //   Remove / Replace / Reset → wipe and re-dispatch everything
        //     (brush shaders write into the persistent bitmap with no
        //     per-stroke record of what they touched — undo has to replay
        //     from the managed Strokes collection)
        // The dispatch is a no-op when _inkLayer hasn't been initialized yet
        // (first render hasn't happened); the committed layer's OnRender
        // path does a full re-dispatch on init, catching up to the current
        // Strokes state.
        if (_inkLayer != null && _inkLayer.IsValid)
        {
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
            {
                foreach (Stroke s in e.NewItems)
                    DispatchStrokeToInkLayer(s);
            }
            else if (e.Action == NotifyCollectionChangedAction.Remove
                  || e.Action == NotifyCollectionChangedAction.Replace
                  || e.Action == NotifyCollectionChangedAction.Reset)
            {
                _inkLayer.Clear();
                foreach (Stroke s in Strokes)
                    DispatchStrokeToInkLayer(s);
            }
            // Move / no-op: ignore.
        }

        // For Add / Remove / Replace we know exactly which strokes changed,
        // so we can scope the invalidation to their union bounds. A Reset
        // (raised by Stroke.Invalidated → StrokeCollection.OnStrokeInvalidated
        // and by Strokes.Clear()) doesn't carry per-item info, so we have to
        // fall back to a full repaint.
        Rect dirty = Rect.Empty;
        bool fullInvalidate = e.Action == NotifyCollectionChangedAction.Reset;

        if (!fullInvalidate)
        {
            if (e.NewItems != null)
            {
                foreach (Stroke s in e.NewItems)
                    dirty = dirty.IsEmpty ? s.GetBounds() : dirty.Union(s.GetBounds());
            }
            if (e.OldItems != null)
            {
                foreach (Stroke s in e.OldItems)
                    dirty = dirty.IsEmpty ? s.GetBounds() : dirty.Union(s.GetBounds());
            }
        }

        if (fullInvalidate || dirty.IsEmpty)
            _committedLayer.InvalidateVisual();
        else
            _committedLayer.InvalidateVisual(dirty);

        StrokesChanged?.Invoke(this, EventArgs.Empty);
    }

    // ────────────────────────────────────────────────────────────────────
    //  GPU ink-layer bitmap + brush-shader pipeline
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Lazy-creates the GPU ink-layer bitmap against the provided context,
    /// or rebuilds it when the context / canvas size changes. Called from
    /// the committed child layer's <see cref="InkCanvasCommittedLayer.OnRender"/>
    /// — that's the earliest point at which a valid RenderContext is in
    /// scope. Re-dispatches every committed stroke on first initialization
    /// so the bitmap catches up to the current Strokes collection.
    /// </summary>
    internal void EnsureInkLayer(RenderContext context)
    {
        int width  = Math.Max(1, (int)Math.Ceiling(ActualWidth));
        int height = Math.Max(1, (int)Math.Ceiling(ActualHeight));

        // Context changed (e.g. device-lost reconstitution) — drop every
        // handle bound to the old context. A brand-new context instance is a
        // healthy device, so clear any dispatch-failure latches and allow the
        // GPU ink path to be tried again from scratch.
        if (!ReferenceEquals(context, _inkLayerContext))
        {
            DisposeInkLayerResources();
            _inkLayerContext = context;
            _inkRebuildPending = false;
            _inkRebuildSuppressed = false;
        }

        // A previous generational rebuild attempt failed to self-heal even
        // after replaying onto a freshly-built layer — stop touching the GPU
        // ink resources entirely and let the committed / preview layers render
        // through their CPU fallback (Strokes.Draw) until the context is
        // replaced. Prevents a per-frame Dispose+rebuild storm.
        if (_inkRebuildSuppressed)
            return;

        // A brush dispatch reported a Vulkan device-generation mismatch (-6/-7)
        // on an earlier frame. Drop the stale-generation layer + shader handles
        // now — on the render-pass thread, between frames, never mid-dispatch —
        // so the rebuild below re-pairs the bitmap and every shader on the
        // currently-registered pipeline. attemptingGenerationRebuild lets the
        // post-replay self-check below tell whether the rebuild actually healed
        // the mismatch.
        bool attemptingGenerationRebuild = false;
        if (_inkRebuildPending)
        {
            _inkRebuildPending = false;
            attemptingGenerationRebuild = true;
            DisposeInkLayerResources();
        }

        if (_inkLayer == null || !_inkLayer.IsValid)
        {
            // Backends that don't implement the brush-shader pipeline
            // (currently Vulkan) return 0 from the native allocation call;
            // surface that as a quiet InkLayerBitmap construction failure
            // and fall back to the legacy CPU raster path in OnRender.
            try
            {
                _inkLayer        = CreateInkLayerBitmap(context, width, height);
                _previewInkLayer = CreateInkLayerBitmap(context, width, height);
            }
            catch (InvalidOperationException)
            {
                // Dispose a partially constructed pair deterministically: the
                // committed layer can succeed while the preview layer throws,
                // and an undisposed native layer pins its whole device
                // generation until the finalizer runs.
                _inkLayer?.Dispose();
                _inkLayer = null;
                _previewInkLayer = null;
                return;
            }

            // Warm up the built-in shader cache so the first click on any
            // brush type doesn't pay a 10-50 ms D3DCompile cost mid-drag.
            // Compilation is one-off per (context, shader key); subsequent
            // InkCanvas instances on the same context share nothing today
            // but could if a process-wide registry were added later.
            PrecompileBuiltInShaders();

            foreach (var s in Strokes)
                DispatchStrokeToInkLayer(s);

            // If this was a generation-mismatch rebuild and the replay STILL
            // tripped a generational failure (a dispatch above re-set
            // _inkRebuildPending), the freshly-built layer and shaders did not
            // land on the same pipeline — self-healing is not possible right
            // now (e.g. the registered generation shifted again mid-rebuild).
            // Give up on the GPU ink path to avoid a rebuild storm; the CPU
            // fallback keeps committed ink visible until the context is
            // replaced (which clears _inkRebuildSuppressed).
            if (attemptingGenerationRebuild && _inkRebuildPending)
            {
                _inkRebuildSuppressed = true;
                _inkRebuildPending = false;
                DisposeInkLayerResources();
                System.Diagnostics.Debug.WriteLine(
                    "[InkCanvas] GPU ink-layer rebuild did not resolve the device-generation mismatch; falling back to CPU ink rendering until the render context is replaced.");
            }
            return;
        }

        // Size changed — resize + re-dispatch everything.
        if (_inkLayer.Width != width || _inkLayer.Height != height)
        {
            // Mirror the construction catch above: a resize fails
            // deterministically when the GPU device was lost between frames
            // (the native layer refuses to touch a lost device), and that is
            // exactly when InkCanvas sizes tend to change (GPU switches move
            // windows across displays/DPIs). Letting the exception escape
            // OnRender would crash the frame instead of letting the window's
            // device-lost recovery rebuild the context; dropping the layers
            // here lets the next frame rebuild them on the recovered context
            // — or fall back to CPU ink through the construction catch.
            try
            {
                _inkLayer.Resize(width, height);
                _previewInkLayer?.Resize(width, height);
            }
            catch (InvalidOperationException)
            {
                DisposeInkLayerResources();
                return;
            }
            _inkLayer.Clear();
            _previewInkLayer?.Clear();
            foreach (var s in Strokes)
                DispatchStrokeToInkLayer(s);

            // If a dispatch during this resize-replay tripped a Vulkan
            // generational failure (HandleInkDispatchFailure set
            // _inkRebuildPending), the layer was just Cleared but is still
            // valid — so the committed layer would blit it EMPTY for this one
            // frame before the pending rebuild runs next frame. Drop the layers
            // now so committed ink falls straight to the CPU path this frame
            // instead of flashing blank; the pending flag still drives the
            // full rebuild on the next EnsureInkLayer. (Unlike the init branch,
            // the rebuild here is never in-progress — a pending-driven rebuild
            // disposes the layer first and so takes the init branch, not this
            // one — so there is nothing to latch as suppressed yet.)
            if (_inkRebuildPending)
                DisposeInkLayerResources();
        }
    }

    private void DisposeInkLayerResources()
    {
        foreach (var h in _shaderHandles.Values)
            h.Dispose();
        _shaderHandles.Clear();
        _inkLayer?.Dispose();
        _inkLayer = null;
        _previewInkLayer?.Dispose();
        _previewInkLayer = null;
    }

    /// <summary>
    /// Constructs an <see cref="InkLayerBitmap"/> through the test seam when one
    /// is installed (<see cref="_inkNativeOpsOverride"/>), otherwise the
    /// production native path. Centralizes the two init-branch allocations so
    /// the failure-recovery rebuild reconstructs seam-backed layers under test.
    /// </summary>
    private InkLayerBitmap CreateInkLayerBitmap(RenderContext context, int width, int height)
        => _inkNativeOpsOverride is { } ops
            ? new InkLayerBitmap(context, width, height, ops)
            : new InkLayerBitmap(context, width, height);

    /// <summary>
    /// Walks the registry + eraser singleton and compiles all built-in
    /// shaders upfront. Safe to call repeatedly — AcquireShaderHandle
    /// short-circuits when the key is already cached.
    /// </summary>
    private void PrecompileBuiltInShaders()
    {
        foreach (BrushType type in Enum.GetValues<BrushType>())
            AcquireShaderHandle(BrushShaderRegistry.GetBuiltIn(type));
        AcquireShaderHandle(Jalium.UI.Controls.Ink.Shaders.EraserBrushShader.Instance);
    }

    /// <summary>
    /// Compiles (or returns the cached) brush shader handle for the given
    /// <paramref name="shader"/>. Returns null on compile failure.
    /// </summary>
    private BrushShaderHandle? AcquireShaderHandle(BrushShader shader)
    {
        if (_inkLayerContext is null) return null;
        if (_shaderHandles.TryGetValue(shader.ShaderKey, out var cached) && cached.IsValid)
            return cached;

        var fresh = _inkNativeOpsOverride is { } ops
            ? BrushShaderHandle.Create(
                _inkLayerContext, shader.ShaderKey, shader.BrushMainHlsl, (int)shader.BlendMode, ops)
            : BrushShaderHandle.Create(
                _inkLayerContext, shader.ShaderKey, shader.BrushMainHlsl, (int)shader.BlendMode);
        if (fresh != null)
            _shaderHandles[shader.ShaderKey] = fresh;
        return fresh;
    }

    /// <summary>
    /// Encodes <paramref name="stroke"/> into a BrushStrokePoint array +
    /// BrushConstantsNative cbuffer and dispatches the appropriate brush
    /// shader onto <paramref name="target"/> (defaults to the committed
    /// ink layer). Silently no-ops when the GPU pipeline isn't ready —
    /// the active CPU preview path still shows the stroke visually, just
    /// without the baked-in pixel-shader effect.
    /// </summary>
    /// <summary>
    /// Pure-points overload used by the RTS preview pipeline — there is no
    /// live <see cref="Stroke"/> object yet (it's only constructed at commit
    /// time, on the UI thread). Wraps the points in a transient
    /// <see cref="Stroke"/> + delegates so a single brush dispatch path
    /// covers both the live-stroke and the snapshot-points caller.
    /// </summary>
    private void DispatchStrokePointsToInkLayer(InkStylusPoint[] points, DrawingAttributes attrs, InkLayerBitmap? target)
    {
        if (points is null || points.Length < 2 || attrs is null) return;
        var coll = new InkStylusPointCollection(points);
        var transient = new Stroke(coll, attrs);
        DispatchStrokeToInkLayer(transient, target);
    }

    private void DispatchStrokeToInkLayer(Stroke stroke, InkLayerBitmap? target = null)
    {
        target ??= _inkLayer;
        if (target is null || !target.IsValid) return;
        if (stroke is null) return;

        int rawCount = stroke.StylusPoints.Count;
        if (rawCount < 2) return;

        var attrs = stroke.DrawingAttributes;
        var shader = attrs.BrushShader ?? BrushShaderRegistry.GetBuiltIn(attrs.BrushType);
        var handle = AcquireShaderHandle(shader);
        if (handle is null) return;

        // Marshal stylus points into the native-layout array (16 B each).
        // The GPU brush HLSL (SdfPolyline) treats consecutive points as a
        // straight segment with no curve fitting, so raw input — typically
        // 5–15 px apart at fast drag speeds — would render as a visible
        // polygon. When FitToCurve is enabled we Catmull-Rom-resample the
        // polyline at sub-stroke-radius density before upload so the
        // segment SDFs blend into a smooth curve. Eraser strokes go through
        // the same pipeline and benefit identically.
        int count;
        bool resample = attrs.FitToCurve && rawCount >= 3;
        if (resample)
        {
            // Step ≈ half the brush radius (clamped at 1 px). Below this,
            // adjacent segment SDFs are within the 1-px AA falloff so the
            // polyline reads as a continuous curve. Wider brushes get a
            // larger step — visual tolerance scales with stroke width.
            double stepSize = Math.Max(1.0, Math.Min(attrs.Width, attrs.Height) * 0.5);

            // EnumerateSmoothedPath emits (steps+1) samples per raw segment
            // and shares endpoints between segments. Worst case is
            // ≈ rawCount + arcLength/stepSize, but the absolute ceiling
            // keeps SdfPolyline's per-pixel loop bounded. Rent the full
            // ceiling up front so the inner loop never has to grow + copy.
            if (_strokePointsScratch is null || _strokePointsScratch.Length < MaxResampledStrokePoints)
                _strokePointsScratch = ArrayPool<BrushStrokePoint>.Shared.Rent(MaxResampledStrokePoints);

            int collected = 0;
            foreach (var smoothPt in stroke.EnumerateSmoothedPath(stepSize))
            {
                if (collected >= MaxResampledStrokePoints) break;
                _strokePointsScratch[collected++] = new BrushStrokePoint(
                    (float)smoothPt.X, (float)smoothPt.Y, (float)smoothPt.Pressure);
            }
            count = collected;
            if (count < 2) return;
        }
        else
        {
            if (_strokePointsScratch is null || _strokePointsScratch.Length < rawCount)
                _strokePointsScratch = ArrayPool<BrushStrokePoint>.Shared.Rent(Math.Max(rawCount, 64));
            for (int i = 0; i < rawCount; i++)
            {
                var sp = stroke.StylusPoints[i];
                _strokePointsScratch[i] = new BrushStrokePoint(
                    (float)sp.X, (float)sp.Y, sp.PressureFactor);
            }
            count = rawCount;
        }
        var scratch = _strokePointsScratch!;

        // Build the 80-byte BrushConstants cbuffer. Color is premultiplied
        // here so SourceOver-blend shaders can output `StrokeColor * cov`
        // directly.
        var bounds = stroke.GetBounds();
        float r = attrs.Color.R / 255f;
        float g = attrs.Color.G / 255f;
        float b = attrs.Color.B / 255f;
        float a = attrs.Color.A / 255f;
        var constants = new BrushConstantsNative
        {
            ColorR         = r * a,
            ColorG         = g * a,
            ColorB         = b * a,
            ColorA         = a,
            StrokeWidth    = (float)attrs.Width,
            StrokeHeight   = (float)attrs.Height,
            TimeSeconds    = AnimationTime,
            RandomSeed     = unchecked((uint)stroke.GetHashCode()),
            BBoxMinX       = (float)bounds.X,
            BBoxMinY       = (float)bounds.Y,
            BBoxMaxX       = (float)(bounds.X + bounds.Width),
            BBoxMaxY       = (float)(bounds.Y + bounds.Height),
            PointCount     = (uint)count,
            TaperMode      = (uint)stroke.TaperMode,
            IgnorePressure = attrs.IgnorePressure ? 1u : 0u,
            FitToCurve     = attrs.FitToCurve    ? 1u : 0u,
        };

        // Pack user-defined ExtraParameters into a contiguous float4 blob
        // (HLSL cbuffer rules: each top-level field consumes a 16-byte
        // register, so one BrushShaderParameter → one float4 slot). Caller
        // HLSL reads `cbuffer UserParams : register(b1) { float4 P0; ... };`.
        var extras = shader.ExtraParameters;
        int rc;
        if (extras is { Count: > 0 })
        {
            int extraByteLen = extras.Count * 16;
            Span<byte> extraBytes = extraByteLen <= 256
                ? stackalloc byte[extraByteLen]
                : new byte[extraByteLen];
            for (int i = 0; i < extras.Count; i++)
            {
                var p = extras[i];
                int off = i * 16;
                System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(extraBytes[(off +  0)..], p.X);
                System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(extraBytes[(off +  4)..], p.Y);
                System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(extraBytes[(off +  8)..], p.Z);
                System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(extraBytes[(off + 12)..], p.W);
            }
            rc = target.DispatchBrush(handle, scratch.AsSpan(0, count), in constants, extraBytes);
        }
        else
        {
            rc = target.DispatchBrush(handle, scratch.AsSpan(0, count), in constants);
        }

        if (rc != 0)
            HandleInkDispatchFailure(rc);
        else if (_lastLoggedInkDispatchRc != 0)
            _lastLoggedInkDispatchRc = 0;
    }

    /// <summary>
    /// Reacts to a non-zero return code from a native brush dispatch
    /// (<see cref="InkLayerBitmap.DispatchBrush"/>). Only Vulkan's deterministic
    /// device-generation failures — <c>-6</c> (the bitmap's generation is
    /// device-lost-latched) and <c>-7</c> (the shader's pipeline was baked on a
    /// different generation than the bitmap) — are self-healing, and they heal
    /// by rebuilding the whole GPU ink-resource set so the bitmap and all shader
    /// handles re-pair on the currently-registered pipeline. The rebuild is
    /// deferred to the next <see cref="EnsureInkLayer"/> so resources are never
    /// torn down mid-dispatch / mid-replay. Every other outcome (all D3D12 codes
    /// — where <c>-6/-7</c> are transient upload-buffer OOM, not a generation
    /// mismatch — and Vulkan's transient <c>-3/-4/-5</c>) is logged once per
    /// distinct code and left to recover on its own; forcing a teardown there
    /// would amplify a momentary failure into a full rebuild storm.
    /// </summary>
    private void HandleInkDispatchFailure(int rc)
    {
        var backend = _inkBackendOverride ?? _inkLayerContext?.Backend;
        bool generational = backend == RenderBackend.Vulkan && (rc == -6 || rc == -7);

        if (generational)
        {
            // Already scheduled a rebuild, or already gave up — nothing to add
            // (also collapses a whole frame's worth of failed strokes into one
            // scheduled rebuild + one log line).
            if (_inkRebuildPending || _inkRebuildSuppressed)
                return;

            _inkRebuildPending = true;
            System.Diagnostics.Debug.WriteLine(
                $"[InkCanvas] Vulkan ink brush dispatch failed rc={rc}; scheduling GPU ink-layer rebuild on the current device generation.");
            // Schedule a frame so EnsureInkLayer runs and performs the rebuild.
            InvalidateVisual();
        }
        else if (_lastLoggedInkDispatchRc != rc)
        {
            // Transient / non-generational: warn once per distinct code (reset
            // on the next success) so a per-stroke failure can't spam the log,
            // and keep the existing layer — the next dispatch usually succeeds.
            _lastLoggedInkDispatchRc = rc;
            System.Diagnostics.Debug.WriteLine(
                $"[InkCanvas] ink brush dispatch returned rc={rc} (transient/non-generational); stroke not baked into the GPU ink layer this frame.");
        }
    }

    private static void OnDefaultDrawingAttributesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is InkCanvas canvas && canvas._dynamicRenderer != null)
        {
            canvas._dynamicRenderer.DrawingAttributes = canvas.DefaultDrawingAttributes.Clone();
        }
    }

    private static void OnEditingModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is InkCanvas canvas)
        {
            // Cancel any current drawing operation
            if (canvas._isDrawing)
            {
                canvas._currentStroke = null;
                canvas._currentPoints = null;
                canvas._isDrawing = false;
                canvas.ReleaseMouseCapture();
                canvas.InvalidateVisual();
            }

            // Cancel any active touch drawing sessions.
            canvas._activeTouchStrokes.Clear();

            canvas._dynamicRenderer?.Reset();
            canvas._inkCollectionStylusPlugIn?.Reset();
            // Drop any in-flight RTS preview sessions — a mid-stroke editing-mode
            // change must not leave a stale Ink session bleeding into the next
            // editing mode (eraser, select, etc.).
            canvas._realTimePreviewPlugIn?.Reset();
            canvas.EditingModeChanged?.Invoke(canvas, new RoutedEventArgs());
        }
    }

    private sealed class InkCollectionStylusPlugIn : StylusPlugIn
    {
        private readonly InkCanvas _owner;

        public InkCollectionStylusPlugIn(InkCanvas owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        public void Reset() { }

        // This plug-in now only owns the whole-stroke erase path. Ink and
        // EraseByPoint are handled by RealTimeInkPreviewStylusPlugIn on the
        // RTS background thread (which then commits on the UI thread via
        // InkCanvas.CommitRealTimePreviewSession). Keeping the two modes
        // here would double-commit each stroke.
        protected override bool IsActiveForInput(RawStylusInput rawStylusInput)
        {
            return _owner.EditingMode is InkCanvasEditingMode.EraseByStroke;
        }

        protected override void OnStylusDown(RawStylusInput rawStylusInput)
        {
            var points = rawStylusInput.GetStylusPoints();
            _owner.EraseStrokesAt(points);
            rawStylusInput.NotifyWhenProcessed(this);
        }

        protected override void OnStylusMove(RawStylusInput rawStylusInput)
        {
            var points = rawStylusInput.GetStylusPoints();
            _owner.EraseStrokesAt(points);
            rawStylusInput.NotifyWhenProcessed(this);
        }

        protected override void OnStylusUp(RawStylusInput rawStylusInput)
        {
            var points = rawStylusInput.GetStylusPoints();
            _owner.EraseStrokesAt(points);
            rawStylusInput.NotifyWhenProcessed(this);
        }

        protected override void OnStylusDownProcessed(RawStylusInput rawStylusInput) => _owner.InvalidateVisual();
        protected override void OnStylusMoveProcessed(RawStylusInput rawStylusInput) => _owner.InvalidateVisual();
        protected override void OnStylusUpProcessed(RawStylusInput rawStylusInput) => _owner.InvalidateVisual();

    }

    #endregion

    #region Event Raisers

    /// <summary>
    /// Raises the <see cref="StrokeCollected"/> event.
    /// </summary>
    protected void OnStrokeCollected(InkCanvasStrokeCollectedEventArgs e)
    {
        StrokeCollected?.Invoke(this, e);
    }

    /// <summary>
    /// Raises the <see cref="StrokeErasing"/> event.
    /// </summary>
    protected void OnStrokeErasing(InkCanvasStrokeErasingEventArgs e)
    {
        StrokeErasing?.Invoke(this, e);
    }

    #endregion

    /// <summary>
    /// Internal child visual that owns rendering of the InkCanvas background
    /// and committed strokes. Splitting this off the InkCanvas itself lets
    /// the system retained-mode cache replay the (potentially hundreds of)
    /// committed-stroke draw commands as a single immutable Drawing on every
    /// frame where only the in-progress stroke (mouse / touch / stylus
    /// preview) changes. Without the split, every new active-stroke point
    /// would invalidate the InkCanvas, force the recorder to walk all N
    /// committed strokes again, and emit N path/ellipse commands per frame.
    /// </summary>
    private sealed class InkCanvasCommittedLayer : FrameworkElement
    {
        private readonly InkCanvas _owner;

        public InkCanvasCommittedLayer(InkCanvas owner)
        {
            _owner = owner;
            // Input is handled by the InkCanvas itself; this overlay must not
            // intercept hit-testing or it would steal stylus/mouse events.
            IsHitTestVisible = false;
        }

        /// <summary>
        /// Opt out of the system retained-mode cache.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Particle-brush strokes (Airbrush / Crayon / Pencil / Oil /
        /// Watercolor) rely on the <c>rtdc is RenderTargetDrawingContext</c>
        /// check inside <c>Stroke.DrawCore</c> to route through the native
        /// <c>BeginEllipseBatch</c> / <c>EndEllipseBatch</c> fast path —
        /// thousands of particles become a single native call with raw
        /// float data, not thousands of independent SDF instances. The
        /// retained-mode cache substitutes a <see cref="DrawingRecorder"/>
        /// for the live RTDC, which fails the type check and forces the
        /// DrawingGroup fallback: every particle becomes a
        /// <c>GeometryDrawing</c> recorded as a <c>DrawEllipse</c> command,
        /// then replayed as an independent SDF instance. A single airbrush
        /// stroke emits thousands of particles, so two strokes easily
        /// overflow the 262144-entry per-frame instance buffer and the
        /// downstream triangle buffer offset gets pushed past the 48 MB
        /// resource end — D3D12 raises <c>SET_VERTEX_BUFFERS_INVALID</c>
        /// (#726) and the device is removed.
        /// </para>
        /// <para>
        /// Opting out makes this layer always re-record in immediate mode
        /// against the live RTDC. The ink overlay still benefits from the
        /// architectural split (active-stroke invalidations don't churn
        /// the committed-stroke work each frame the way the monolithic
        /// InkCanvas used to) but each per-frame stroke draw gets the
        /// batched native path particle brushes need.
        /// </para>
        /// </remarks>
        protected override bool ParticipatesInRenderCache => false;

        protected override Size MeasureOverride(Size availableSize)
        {
            return new Size(
                double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width,
                double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
        }

        protected override Size ArrangeOverride(Size finalSize) => finalSize;

        protected override void OnRender(DrawingContext drawingContext)
        {
            var dc = drawingContext;

            var background = _owner.Background;
            if (background != null)
            {
                dc.DrawRectangle(background, null, new Rect(0, 0, ActualWidth, ActualHeight));
            }

            // Prefer the GPU ink-layer path when a live RTDC is available:
            // lazy-create the offscreen bitmap + compile any brush shaders
            // we haven't seen, then blit the bitmap over the background.
            // Fallback: if RTDC is unavailable (test probes, recorders, …)
            // every stroke is painted via the legacy CPU path.
            if (dc is Jalium.UI.Interop.RenderTargetDrawingContext rtdc)
            {
                _owner.EnsureInkLayer(rtdc.Context);
                if (_owner._inkLayer is { IsValid: true } layer)
                {
                    rtdc.BlitInkLayer(layer, 0, 0, 1.0f);
                    return;
                }
            }

            // Fallback CPU raster (non-RTDC contexts only)
            _owner.Strokes?.Draw(dc);
        }
    }
}

/// <summary>
/// Provides data for the <see cref="InkCanvas.StrokeCollected"/> event.
/// </summary>
public sealed class InkCanvasStrokeCollectedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InkCanvasStrokeCollectedEventArgs"/> class.
    /// </summary>
    /// <param name="stroke">The stroke that was collected.</param>
    public InkCanvasStrokeCollectedEventArgs(Stroke stroke)
    {
        Stroke = stroke;
    }

    /// <summary>
    /// Gets the stroke that was collected.
    /// </summary>
    public Stroke Stroke { get; }
}

/// <summary>
/// Provides data for the <see cref="InkCanvas.StrokeErasing"/> event.
/// </summary>
public sealed class InkCanvasStrokeErasingEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InkCanvasStrokeErasingEventArgs"/> class.
    /// </summary>
    /// <param name="stroke">The stroke that is about to be erased.</param>
    public InkCanvasStrokeErasingEventArgs(Stroke stroke)
    {
        Stroke = stroke;
    }

    /// <summary>
    /// Gets the stroke that is about to be erased.
    /// </summary>
    public Stroke Stroke { get; }

    /// <summary>
    /// Gets or sets a value indicating whether to cancel the erase operation.
    /// </summary>
    public bool Cancel { get; set; }
}

/// <summary>
/// Provides data for the <see cref="InkCanvas.SelectionMoving"/> and <see cref="InkCanvas.SelectionResizing"/> events.
/// </summary>
public sealed class InkCanvasSelectionEditingEventArgs : EventArgs
{
    public InkCanvasSelectionEditingEventArgs(Rect oldRectangle, Rect newRectangle)
    {
        OldRectangle = oldRectangle;
        NewRectangle = newRectangle;
    }

    /// <summary>Gets the bounds of the selection before the editing operation.</summary>
    public Rect OldRectangle { get; }

    /// <summary>Gets or sets the bounds of the selection after the editing operation.</summary>
    public Rect NewRectangle { get; set; }

    /// <summary>Gets or sets a value indicating whether to cancel the operation.</summary>
    public bool Cancel { get; set; }
}

/// <summary>
/// Provides data for the <see cref="InkCanvas.Gesture"/> event.
/// </summary>
public sealed class InkCanvasGestureEventArgs : RoutedEventArgs
{
    public InkCanvasGestureEventArgs(StrokeCollection strokes, IReadOnlyList<GestureRecognitionResult> gestureRecognitionResults)
    {
        Strokes = strokes;
        GestureRecognitionResults = gestureRecognitionResults;
    }

    /// <summary>Gets the strokes that represent the gesture.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public StrokeCollection Strokes { get; }

    /// <summary>Gets the recognition results for the gesture.</summary>
    public IReadOnlyList<GestureRecognitionResult> GestureRecognitionResults { get; }

    /// <summary>Gets or sets a value indicating whether the event should be canceled.</summary>
    public bool Cancel { get; set; }
}

/// <summary>
/// Contains information about a gesture recognition result.
/// </summary>
public sealed class GestureRecognitionResult
{
    public GestureRecognitionResult(InkCanvasGesture applicationGesture, RecognitionConfidence recognitionConfidence)
    {
        ApplicationGesture = applicationGesture;
        RecognitionConfidence = recognitionConfidence;
    }

    /// <summary>Gets the recognized gesture.</summary>
    public InkCanvasGesture ApplicationGesture { get; }

    /// <summary>Gets the confidence level of the recognition.</summary>
    public RecognitionConfidence RecognitionConfidence { get; }
}

/// <summary>
/// Specifies application gestures.
/// </summary>
public enum InkCanvasGesture
{
    NoGesture = 0,
    Tap,
    DoubleTap,
    RightTap,
    Drag,
    RightDrag,
    ScratchOut,
    Circle,
    Check,
    Curlicue,
    DoubleCurlicue,
    Triangle,
    Square,
    Star,
    ArrowUp,
    ArrowDown,
    ArrowLeft,
    ArrowRight,
    Up,
    Down,
    Left,
    Right,
    UpDown,
    DownUp,
    LeftRight,
    RightLeft,
    UpLeftLong,
    UpRightLong,
    DownLeftLong,
    DownRightLong,
    UpLeft,
    UpRight,
    DownLeft,
    DownRight,
    LeftUp,
    LeftDown,
    RightUp,
    RightDown,
    Exclamation
}

/// <summary>
/// Specifies the level of confidence for a recognition result.
/// </summary>
public enum RecognitionConfidence
{
    Strong,
    Intermediate,
    Poor
}

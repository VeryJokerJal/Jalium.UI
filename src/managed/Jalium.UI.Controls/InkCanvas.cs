using System.Buffers;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Jalium.UI.Ink;
using Jalium.UI.Ink.Shaders;
using Jalium.UI.Input;
using Jalium.UI.Input.StylusPlugIns;
using Jalium.UI.Interop;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using InkStylusPoint = Jalium.UI.Input.StylusPoint;
using InkStylusPointCollection = Jalium.UI.Input.StylusPointCollection;
using InputStylusPoints = Jalium.UI.Input.StylusPointCollection;

namespace Jalium.UI.Controls;

/// <summary>
/// Provides an area for ink collection and display.
/// </summary>
public class InkCanvas : FrameworkElement, IAddChild
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.InkCanvasAutomationPeer(this);
    }

    #region Private Fields

    private InkStylusPointCollection? _currentPoints;
    private Stroke? _currentStroke;
    private bool _isDrawing;
    private StrokeCollection _selectedStrokes = new();
    private List<UIElement> _selectedElements = new();
    private SelectionEditSession? _selectionEditSession;
    private readonly InkCanvasChildrenHost _childrenHost = new();
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
    // A native brush dispatch can refuse a stroke with a non-zero
    // InkDispatchResult code. The codes are backend-agnostic by contract
    // (every backend classifies its internal failures before returning), so
    // recovery here dispatches on the CODE ALONE — never on which backend
    // produced it. The ONLY self-healing class is
    // InkDispatchResult.StaleContext: the device generation behind the
    // handles is gone or inconsistent (device-lost latch, or the shader's
    // pipeline was baked on a different generation than the bitmap — e.g. a
    // second window on a different device swapped the process-global brush
    // pipeline, so a lazily-compiled shader landed on the new generation
    // while this canvas's layer is still on the old one; _shaderHandles
    // caches by ShaderKey with no knowledge of the native pipeline swap, so
    // the mismatch is permanent for these handles).
    // Rebuilding _inkLayer + _previewInkLayer + every _shaderHandle together
    // re-pairs them on the currently-registered pipeline (the native backend
    // hands the rebuilt bitmap and the rebuilt shaders the SAME cached brush
    // pipeline), so the next dispatch succeeds and committed ink is restored by
    // replaying Strokes. We deliberately do NOT rebuild for any other code:
    // InkDispatchResult.Transient (momentary upload/command failure — the
    // handles are still healthy and the next dispatch usually succeeds) and
    // the InvalidArg/InvalidState classes must be left to recover on their
    // own — a full teardown+replay there would amplify a one-frame hiccup
    // into a storm. _inkRebuildPending defers the rebuild to the next
    // EnsureInkLayer so we never tear resources down mid-OnRender /
    // mid-replay; if that rebuild still hits a stale-context failure during
    // its own replay we latch _inkRebuildSuppressed and fall back to the CPU
    // ink path until the context instance itself is replaced (real
    // device-lost recovery), which clears the latch.
    private bool _inkRebuildPending;
    private bool _inkRebuildSuppressed;
    private int _lastLoggedInkDispatchRc;

    // ── Test seam (production: null) ────────────────────────────────────────
    // Lets the GPU-ink failure-recovery state machine be unit-tested without a
    // real GPU device (stale-context failures cannot be coerced on demand and
    // CI has no ICD to produce them at all). _inkNativeOpsOverride substitutes
    // the native ink/brush ops (so DispatchBrush can return a scripted rc and
    // layer/shader construction succeeds headlessly). Null in production → the
    // real native path is used unchanged.
    internal Jalium.UI.Interop.IInkNativeOps? _inkNativeOpsOverride;

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

    private StylusPointDescription _defaultStylusPointDescription = new();
    private StylusShape _eraserShape = new RectangleStylusShape(8.0, 8.0);
    private InkCanvasClipboardFormat[] _preferredPasteFormats =
        [InkCanvasClipboardFormat.InkSerializedFormat];
    private ApplicationGesture[] _enabledGestures = [ApplicationGesture.AllGestures];
    private bool _moveEnabled = true;
    private bool _resizeEnabled = true;
    private bool _useCustomCursor;
    private bool _isStylusInverted;

    private static readonly object s_clipboardGate = new();
    private static InkCanvasClipboardPayload? s_clipboardPayload;

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
            new PropertyMetadata(InkCanvasEditingMode.Ink, OnEditingModeChanged), IsValidEditingMode);

    /// <summary>
    /// Identifies the editing mode used by the inverted end of a stylus.
    /// </summary>
    public static readonly DependencyProperty EditingModeInvertedProperty =
        DependencyProperty.Register(
            nameof(EditingModeInverted),
            typeof(InkCanvasEditingMode),
            typeof(InkCanvas),
            new PropertyMetadata(
                InkCanvasEditingMode.EraseByStroke,
                OnEditingModeInvertedPropertyChanged),
            IsValidEditingMode);

    private static readonly DependencyPropertyKey ActiveEditingModePropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(ActiveEditingMode),
            typeof(InkCanvasEditingMode),
            typeof(InkCanvas),
            new PropertyMetadata(InkCanvasEditingMode.Ink),
            IsValidEditingMode);

    /// <summary>
    /// Identifies the current editing mode, accounting for stylus inversion.
    /// </summary>
    public static readonly DependencyProperty ActiveEditingModeProperty =
        ActiveEditingModePropertyKey.DependencyProperty;

    /// <summary>Identifies the Left attached property.</summary>
    public static readonly DependencyProperty LeftProperty =
        DependencyProperty.RegisterAttached(
            "Left",
            typeof(double),
            typeof(InkCanvas),
            new PropertyMetadata(double.NaN, OnPositioningChanged),
            IsValidPositioningValue);

    /// <summary>Identifies the Top attached property.</summary>
    public static readonly DependencyProperty TopProperty =
        DependencyProperty.RegisterAttached(
            "Top",
            typeof(double),
            typeof(InkCanvas),
            new PropertyMetadata(double.NaN, OnPositioningChanged),
            IsValidPositioningValue);

    /// <summary>Identifies the Right attached property.</summary>
    public static readonly DependencyProperty RightProperty =
        DependencyProperty.RegisterAttached(
            "Right",
            typeof(double),
            typeof(InkCanvas),
            new PropertyMetadata(double.NaN, OnPositioningChanged),
            IsValidPositioningValue);

    /// <summary>Identifies the Bottom attached property.</summary>
    public static readonly DependencyProperty BottomProperty =
        DependencyProperty.RegisterAttached(
            "Bottom",
            typeof(double),
            typeof(InkCanvas),
            new PropertyMetadata(double.NaN, OnPositioningChanged),
            IsValidPositioningValue);

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
        AddVisualChild(_childrenHost);

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
        AddHandler(PreviewStylusInAirMoveEvent, new Input.StylusEventHandler(OnPreviewStylusStateHandler));
        AddHandler(PreviewStylusInRangeEvent, new Input.StylusEventHandler(OnPreviewStylusStateHandler));
        AddHandler(PreviewStylusOutOfRangeEvent, new Input.StylusEventHandler(OnPreviewStylusOutOfRangeHandler));

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
        Unloaded += OnInkCanvasUnloaded;
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
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            SetValue(StrokesProperty, value);
        }
    }

    /// <summary>
    /// Gets or sets the default drawing attributes for new strokes.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public DrawingAttributes DefaultDrawingAttributes
    {
        get => (DrawingAttributes?)GetValue(DefaultDrawingAttributesProperty) ?? new DrawingAttributes();
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            SetValue(DefaultDrawingAttributesProperty, value);
        }
    }

    /// <summary>
    /// Gets or sets the dynamic renderer used for real-time stylus preview.
    /// </summary>
    protected DynamicRenderer DynamicRenderer
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

    /// <summary>Gets the presenter used for dynamic stylus feedback.</summary>
    protected InkPresenter InkPresenter => _dynamicInkPresenter;

    /// <summary>Gets the visual children hosted by the ink surface.</summary>
    public UIElementCollection Children => _childrenHost.Children;

    /// <summary>
    /// Gets or sets the editing mode for this InkCanvas.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public InkCanvasEditingMode EditingMode
    {
        get => (InkCanvasEditingMode)(GetValue(EditingModeProperty) ?? InkCanvasEditingMode.Ink);
        set => SetValue(EditingModeProperty, value);
    }

    /// <summary>Gets the editing mode currently used for input.</summary>
    public InkCanvasEditingMode ActiveEditingMode =>
        (InkCanvasEditingMode)(GetValue(ActiveEditingModeProperty) ?? InkCanvasEditingMode.Ink);

    /// <summary>Gets or sets the editing mode used by an inverted stylus.</summary>
    public InkCanvasEditingMode EditingModeInverted
    {
        get => (InkCanvasEditingMode)(
            GetValue(EditingModeInvertedProperty) ?? InkCanvasEditingMode.EraseByStroke);
        set => SetValue(EditingModeInvertedProperty, value);
    }

    /// <summary>Gets or sets the stylus packet description used for new ink.</summary>
    public StylusPointDescription DefaultStylusPointDescription
    {
        get => _defaultStylusPointDescription;
        set => _defaultStylusPointDescription = value
            ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Gets or sets the shape used by point erasing.</summary>
    public StylusShape EraserShape
    {
        get => _eraserShape;
        set
        {
            _eraserShape = value ?? throw new ArgumentNullException(nameof(value));
            SetValue(EraserDiameterProperty, Math.Max(value.Width, value.Height));
        }
    }

    /// <summary>Gets whether ink gesture recognition is available.</summary>
    public bool IsGestureRecognizerAvailable => true;

    /// <summary>Gets or sets whether a selected item may be moved.</summary>
    public bool MoveEnabled
    {
        get => _moveEnabled;
        set => _moveEnabled = value;
    }

    /// <summary>Gets or sets whether a selected item may be resized.</summary>
    public bool ResizeEnabled
    {
        get => _resizeEnabled;
        set => _resizeEnabled = value;
    }

    /// <summary>Gets or sets whether application cursor selection overrides InkCanvas cursors.</summary>
    public bool UseCustomCursor
    {
        get => _useCustomCursor;
        set => _useCustomCursor = value;
    }

    /// <summary>Gets or sets the ordered clipboard formats considered during paste.</summary>
    public IEnumerable<InkCanvasClipboardFormat> PreferredPasteFormats
    {
        get => Array.AsReadOnly(_preferredPasteFormats);
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            var formats = new List<InkCanvasClipboardFormat>();
            foreach (InkCanvasClipboardFormat format in value)
            {
                if (!Enum.IsDefined(format))
                    throw new ArgumentException(
                        "Specified InkCanvasClipboardFormat is not valid.",
                        nameof(value));
                if (!formats.Contains(format))
                    formats.Add(format);
            }

            _preferredPasteFormats = formats.ToArray();
        }
    }

    /// <summary>
    /// Gets or sets the diameter of the eraser.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public double EraserDiameter
    {
        get => (double)GetValue(EraserDiameterProperty)!;
        set
        {
            if (!double.IsFinite(value) || value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value));
            SetValue(EraserDiameterProperty, value);
            _eraserShape = new RectangleStylusShape(value, value);
        }
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
    /// Identifies the <see cref="StrokeCollected"/> routed event.
    /// </summary>
    public static readonly RoutedEvent StrokeCollectedEvent =
        EventManager.RegisterRoutedEvent(
            nameof(StrokeCollected),
            RoutingStrategy.Bubble,
            typeof(InkCanvasStrokeCollectedEventHandler),
            typeof(InkCanvas));

    /// <summary>
    /// Identifies the <see cref="Gesture"/> routed event.
    /// </summary>
    public static readonly RoutedEvent GestureEvent =
        EventManager.RegisterRoutedEvent(
            nameof(Gesture),
            RoutingStrategy.Bubble,
            typeof(InkCanvasGestureEventHandler),
            typeof(InkCanvas));

    /// <summary>
    /// Identifies the <see cref="StrokeErased"/> routed event.
    /// </summary>
    public static readonly RoutedEvent StrokeErasedEvent =
        EventManager.RegisterRoutedEvent(
            nameof(StrokeErased),
            RoutingStrategy.Bubble,
            typeof(RoutedEventHandler),
            typeof(InkCanvas));

    /// <summary>
    /// Identifies the <see cref="EditingModeChanged"/> routed event.
    /// </summary>
    public static readonly RoutedEvent EditingModeChangedEvent =
        EventManager.RegisterRoutedEvent(
            nameof(EditingModeChanged),
            RoutingStrategy.Bubble,
            typeof(RoutedEventHandler),
            typeof(InkCanvas));

    /// <summary>Identifies the ActiveEditingModeChanged routed event.</summary>
    public static readonly RoutedEvent ActiveEditingModeChangedEvent =
        EventManager.RegisterRoutedEvent(
            nameof(ActiveEditingModeChanged),
            RoutingStrategy.Bubble,
            typeof(RoutedEventHandler),
            typeof(InkCanvas));

    /// <summary>Identifies the EditingModeInvertedChanged routed event.</summary>
    public static readonly RoutedEvent EditingModeInvertedChangedEvent =
        EventManager.RegisterRoutedEvent(
            nameof(EditingModeInvertedChanged),
            RoutingStrategy.Bubble,
            typeof(RoutedEventHandler),
            typeof(InkCanvas));

    /// <summary>
    /// Occurs when a new stroke has been collected.
    /// </summary>
    public event InkCanvasStrokeCollectedEventHandler StrokeCollected
    {
        add => AddHandler(StrokeCollectedEvent, value);
        remove => RemoveHandler(StrokeCollectedEvent, value);
    }

    /// <summary>
    /// Occurs when a stroke has been recognized as a gesture.
    /// </summary>
    public event InkCanvasGestureEventHandler Gesture
    {
        add => AddHandler(GestureEvent, value);
        remove => RemoveHandler(GestureEvent, value);
    }

    /// <summary>
    /// Occurs when a stroke is about to be erased.
    /// </summary>
    public event InkCanvasStrokeErasingEventHandler? StrokeErasing;

    /// <summary>
    /// Occurs after a stroke has been erased.
    /// </summary>
    public event RoutedEventHandler StrokeErased
    {
        add => AddHandler(StrokeErasedEvent, value);
        remove => RemoveHandler(StrokeErasedEvent, value);
    }

    /// <summary>
    /// Occurs when the <see cref="Strokes"/> collection is replaced.
    /// </summary>
    public event InkCanvasStrokesReplacedEventHandler? StrokesReplaced;

    /// <summary>
    /// Occurs before the programmatic selection changes.
    /// </summary>
    public event InkCanvasSelectionChangingEventHandler? SelectionChanging;

    /// <summary>
    /// Occurs after the selection changes.
    /// </summary>
    public event EventHandler? SelectionChanged;

    /// <summary>
    /// Occurs before a selection move is committed.
    /// </summary>
    public event InkCanvasSelectionEditingEventHandler? SelectionMoving;

    /// <summary>
    /// Occurs after a selection move is committed.
    /// </summary>
    public event EventHandler? SelectionMoved;

    /// <summary>
    /// Occurs before a selection resize is committed.
    /// </summary>
    public event InkCanvasSelectionEditingEventHandler? SelectionResizing;

    /// <summary>
    /// Occurs after a selection resize is committed.
    /// </summary>
    public event EventHandler? SelectionResized;

    /// <summary>
    /// Occurs when the strokes collection changes.
    /// </summary>
    public event EventHandler? StrokesChanged;

    /// <summary>
    /// Occurs when the editing mode changes.
    /// </summary>
    public event RoutedEventHandler EditingModeChanged
    {
        add => AddHandler(EditingModeChangedEvent, value);
        remove => RemoveHandler(EditingModeChangedEvent, value);
    }

    /// <summary>Occurs when stylus inversion changes the effective editing mode.</summary>
    public event RoutedEventHandler ActiveEditingModeChanged
    {
        add => AddHandler(ActiveEditingModeChangedEvent, value);
        remove => RemoveHandler(ActiveEditingModeChangedEvent, value);
    }

    /// <summary>Occurs when <see cref="EditingModeInverted"/> changes.</summary>
    public event RoutedEventHandler EditingModeInvertedChanged
    {
        add => AddHandler(EditingModeInvertedChangedEvent, value);
        remove => RemoveHandler(EditingModeInvertedChangedEvent, value);
    }

    /// <summary>Occurs when <see cref="DefaultDrawingAttributes"/> is replaced.</summary>
    public event DrawingAttributesReplacedEventHandler? DefaultDrawingAttributesReplaced;

    #endregion

    #region Public Methods

    public static double GetLeft(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (double)(element.GetValue(LeftProperty) ?? double.NaN);
    }

    public static void SetLeft(UIElement element, double length)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(LeftProperty, length);
    }

    public static double GetTop(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (double)(element.GetValue(TopProperty) ?? double.NaN);
    }

    public static void SetTop(UIElement element, double length)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(TopProperty, length);
    }

    public static double GetRight(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (double)(element.GetValue(RightProperty) ?? double.NaN);
    }

    public static void SetRight(UIElement element, double length)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(RightProperty, length);
    }

    public static double GetBottom(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (double)(element.GetValue(BottomProperty) ?? double.NaN);
    }

    public static void SetBottom(UIElement element, double length)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(BottomProperty, length);
    }

    /// <summary>Gets a snapshot of the gestures currently enabled for recognition.</summary>
    public ReadOnlyCollection<ApplicationGesture> GetEnabledGestures() =>
        Array.AsReadOnly((ApplicationGesture[])_enabledGestures.Clone());

    /// <summary>Replaces the set of gestures considered by the recognizer.</summary>
    public void SetEnabledGestures(IEnumerable<ApplicationGesture> applicationGestures)
    {
        _enabledGestures = InkGestureRecognizerCore.ValidateEnabledGestures(applicationGestures);
    }

    /// <summary>Returns whether this process currently has compatible ink data to paste.</summary>
    public bool CanPaste()
    {
        if (!_preferredPasteFormats.Contains(InkCanvasClipboardFormat.InkSerializedFormat))
            return false;

        lock (s_clipboardGate)
        {
            return s_clipboardPayload is { Strokes.Count: > 0 };
        }
    }

    /// <summary>Copies the selected ink to the process clipboard.</summary>
    public void CopySelection()
    {
        if (_selectedStrokes.Count == 0)
            return;

        var clone = _selectedStrokes.Clone();
        lock (s_clipboardGate)
        {
            s_clipboardPayload = new InkCanvasClipboardPayload(clone, clone.GetBounds());
        }
    }

    /// <summary>Copies and then removes the selected ink.</summary>
    public void CutSelection()
    {
        if (_selectedStrokes.Count == 0)
            return;

        CopySelection();
        var removed = _selectedStrokes.ToArray();
        Strokes.RemoveRange(removed);
        if (_selectedStrokes.Count != 0 || _selectedElements.Count != 0)
        {
            _selectedStrokes = new StrokeCollection();
            _selectedElements.Clear();
            OnSelectionChanged(EventArgs.Empty);
        }
    }

    /// <summary>Pastes ink at its original clipboard coordinates.</summary>
    public void Paste() => PasteCore(null);

    /// <summary>Pastes ink with its upper-left bound at <paramref name="point"/>.</summary>
    public void Paste(Point point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
            throw new ArgumentException("The paste point must contain finite coordinates.", nameof(point));
        PasteCore(point);
    }

    /// <summary>Gets the bounds occupied by all selected strokes and elements.</summary>
    public Rect GetSelectionBounds()
    {
        Rect bounds = _selectedStrokes.GetBounds();
        foreach (UIElement element in _selectedElements)
        {
            Rect elementBounds = GetElementBounds(element);
            if (!elementBounds.IsEmpty)
                bounds = bounds.IsEmpty ? elementBounds : Rect.Union(bounds, elementBounds);
        }

        return bounds;
    }

    /// <summary>Hit-tests the selection body and its eight resize handles.</summary>
    public InkCanvasSelectionHitResult HitTestSelection(Point point)
    {
        Rect bounds = GetSelectionBounds();
        if (bounds.IsEmpty)
            return InkCanvasSelectionHitResult.None;

        const double handleOffset = 5.0;
        const double hitRadius = 4.0;
        double centerX = bounds.X + bounds.Width * 0.5;
        double centerY = bounds.Y + bounds.Height * 0.5;

        if (IsHandleHit(point, bounds.Left - handleOffset, bounds.Top - handleOffset, hitRadius))
            return InkCanvasSelectionHitResult.TopLeft;
        if (IsHandleHit(point, centerX, bounds.Top - handleOffset, hitRadius))
            return InkCanvasSelectionHitResult.Top;
        if (IsHandleHit(point, bounds.Right + handleOffset, bounds.Top - handleOffset, hitRadius))
            return InkCanvasSelectionHitResult.TopRight;
        if (IsHandleHit(point, bounds.Right + handleOffset, centerY, hitRadius))
            return InkCanvasSelectionHitResult.Right;
        if (IsHandleHit(point, bounds.Right + handleOffset, bounds.Bottom + handleOffset, hitRadius))
            return InkCanvasSelectionHitResult.BottomRight;
        if (IsHandleHit(point, centerX, bounds.Bottom + handleOffset, hitRadius))
            return InkCanvasSelectionHitResult.Bottom;
        if (IsHandleHit(point, bounds.Left - handleOffset, bounds.Bottom + handleOffset, hitRadius))
            return InkCanvasSelectionHitResult.BottomLeft;
        if (IsHandleHit(point, bounds.Left - handleOffset, centerY, hitRadius))
            return InkCanvasSelectionHitResult.Left;

        return bounds.Contains(point)
            ? InkCanvasSelectionHitResult.Selection
            : InkCanvasSelectionHitResult.None;
    }

    /// <summary>
    /// Clears all strokes from this InkCanvas.
    /// </summary>
    public void ClearStrokes()
    {
        Strokes.Clear();
    }

    /// <summary>
    /// Gets a snapshot of the currently selected strokes.
    /// </summary>
    public StrokeCollection GetSelectedStrokes() => new(_selectedStrokes);

    /// <summary>
    /// Gets a read-only snapshot of the currently selected elements.
    /// </summary>
    public ReadOnlyCollection<UIElement> GetSelectedElements() =>
        new(_selectedElements.ToList());

    /// <summary>
    /// Selects the specified strokes and clears the element selection.
    /// </summary>
    public void Select(StrokeCollection selectedStrokes) =>
        Select(selectedStrokes, selectedElements: null);

    /// <summary>
    /// Selects the specified elements and clears the stroke selection.
    /// </summary>
    public void Select(IEnumerable<UIElement> selectedElements) =>
        Select(selectedStrokes: null, selectedElements: selectedElements);

    /// <summary>
    /// Selects the specified strokes and elements.
    /// </summary>
    public void Select(StrokeCollection? selectedStrokes, IEnumerable<UIElement>? selectedElements)
    {
        if (EditingMode != InkCanvasEditingMode.Select)
        {
            EditingMode = InkCanvasEditingMode.Select;
        }

        var validStrokes = ValidateSelectedStrokes(selectedStrokes);
        var validElements = ValidateSelectedElements(selectedElements);

        if (SelectionsAreEqual(validStrokes, validElements))
        {
            return;
        }

        var args = new InkCanvasSelectionChangingEventArgs(validStrokes, validElements);
        OnSelectionChanging(args);
        if (args.Cancel)
        {
            return;
        }

        if (args.StrokesChanged)
        {
            validStrokes = ValidateSelectedStrokes(args.GetSelectedStrokes());
        }

        if (args.ElementsChanged)
        {
            validElements = ValidateSelectedElements(args.GetSelectedElements());
        }

        if (SelectionsAreEqual(validStrokes, validElements))
        {
            return;
        }

        _selectedStrokes = new StrokeCollection(validStrokes);
        _selectedElements = validElements.ToList();
        OnSelectionChanged(EventArgs.Empty);
    }

    void IAddChild.AddChild(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value is not UIElement element)
            throw new ArgumentException("InkCanvas children must be UIElement instances.", nameof(value));
        Children.Add(element);
    }

    void IAddChild.AddText(string textData)
    {
        ArgumentNullException.ThrowIfNull(textData);
        if (!string.IsNullOrWhiteSpace(textData))
            throw new ArgumentException("InkCanvas does not accept text children.", nameof(textData));
    }

    private void PasteCore(Point? point)
    {
        if (!_preferredPasteFormats.Contains(InkCanvasClipboardFormat.InkSerializedFormat))
            return;

        StrokeCollection pasted;
        Rect sourceBounds;
        lock (s_clipboardGate)
        {
            if (s_clipboardPayload is null || s_clipboardPayload.Strokes.Count == 0)
                return;
            pasted = s_clipboardPayload.Strokes.Clone();
            sourceBounds = s_clipboardPayload.Bounds;
        }

        if (point is Point target && !sourceBounds.IsEmpty)
        {
            double offsetX = target.X - sourceBounds.X;
            double offsetY = target.Y - sourceBounds.Y;
            foreach (Stroke stroke in pasted)
                stroke.StylusPoints.Transform(1, 0, 0, 1, offsetX, offsetY);
        }

        foreach (Stroke stroke in pasted)
            Strokes.Add(stroke);
        Select(pasted);
    }

    private Rect GetElementBounds(UIElement element)
    {
        double x = 0;
        double y = 0;
        Visual? current = element;
        while (current is not null && !ReferenceEquals(current, this))
        {
            if (current is UIElement currentElement)
            {
                Rect visualBounds = currentElement.VisualBounds;
                x += visualBounds.X;
                y += visualBounds.Y;
            }
            current = current.VisualParent;
        }

        if (!ReferenceEquals(current, this))
            return Rect.Empty;

        return new Rect(x, y, element.RenderSize.Width, element.RenderSize.Height);
    }

    private static bool IsHandleHit(Point point, double centerX, double centerY, double radius) =>
        Math.Abs(point.X - centerX) <= radius && Math.Abs(point.Y - centerY) <= radius;

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
        _childrenHost.Measure(size);
        return size;
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        _committedLayer.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
        _childrenHost.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
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
                var radiusX = EraserShape.Width * 0.5;
                var radiusY = EraserShape.Height * 0.5;
                var ringBrush = new Media.SolidColorBrush(
                    Media.Color.FromArgb(160, 128, 128, 128));
                var ringPen = new Media.Pen(ringBrush, 1.5);
                dc.DrawEllipse(null, ringPen, new Point(last.X, last.Y), radiusX, radiusY);
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
                var radiusX = EraserShape.Width * 0.5;
                var radiusY = EraserShape.Height * 0.5;
                var ringBrush = new Media.SolidColorBrush(
                    Media.Color.FromArgb(160, 128, 128, 128));
                var ringPen = new Media.Pen(ringBrush, 1.5);
                dc.DrawEllipse(null, ringPen, new Point(last.X, last.Y), radiusX, radiusY);
            }
            DrawSelectionAdorner(dc);
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
        DrawSelectionAdorner(dc);
    }

    private void DrawSelectionAdorner(DrawingContext drawingContext)
    {
        if (EditingMode != InkCanvasEditingMode.Select)
            return;

        Rect bounds = _selectionEditSession is { } session
            ? GetEditedSelectionBounds(session, session.CurrentPoint)
            : GetSelectionBounds();
        if (bounds.IsEmpty)
            return;

        var accent = new SolidColorBrush(Color.FromArgb(220, 30, 120, 220));
        drawingContext.DrawRectangle(null, new Pen(accent, 1.0), bounds);
        if (!ResizeEnabled)
            return;

        const double offset = 5.0;
        const double size = 6.0;
        double centerX = bounds.X + bounds.Width * 0.5;
        double centerY = bounds.Y + bounds.Height * 0.5;
        DrawSelectionHandle(drawingContext, accent, bounds.Left - offset, bounds.Top - offset, size);
        DrawSelectionHandle(drawingContext, accent, centerX, bounds.Top - offset, size);
        DrawSelectionHandle(drawingContext, accent, bounds.Right + offset, bounds.Top - offset, size);
        DrawSelectionHandle(drawingContext, accent, bounds.Right + offset, centerY, size);
        DrawSelectionHandle(drawingContext, accent, bounds.Right + offset, bounds.Bottom + offset, size);
        DrawSelectionHandle(drawingContext, accent, centerX, bounds.Bottom + offset, size);
        DrawSelectionHandle(drawingContext, accent, bounds.Left - offset, bounds.Bottom + offset, size);
        DrawSelectionHandle(drawingContext, accent, bounds.Left - offset, centerY, size);
    }

    private static void DrawSelectionHandle(
        DrawingContext drawingContext,
        Brush brush,
        double centerX,
        double centerY,
        double size)
    {
        drawingContext.DrawRectangle(
            brush,
            null,
            new Rect(centerX - size * 0.5, centerY - size * 0.5, size, size));
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
            case InkCanvasEditingMode.GestureOnly:
            case InkCanvasEditingMode.InkAndGesture:
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

            case InkCanvasEditingMode.Select:
                StartSelectionEdit(position);
                break;
        }

        e.Handled = true;
    }

    private void OnMouseMoveHandler(object sender, MouseEventArgs e)
    {
        var position = e.GetPosition(this);

        if (_selectionEditSession is not null && e.LeftButton == MouseButtonState.Pressed)
        {
            _selectionEditSession.CurrentPoint = position;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        // Ink + EraseByPoint both go through the active-stroke pipeline —
        // ContinueDrawing inspects the stroke's brush and dispatches the
        // eraser shader to the committed layer incrementally.
        if (_isDrawing &&
            (EditingMode == InkCanvasEditingMode.Ink
             || EditingMode == InkCanvasEditingMode.GestureOnly
             || EditingMode == InkCanvasEditingMode.InkAndGesture
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

        if (_selectionEditSession is not null)
        {
            FinishSelectionEdit(e.GetPosition(this));
            e.Handled = true;
            return;
        }

        if (_isDrawing &&
            (EditingMode == InkCanvasEditingMode.Ink
             || EditingMode == InkCanvasEditingMode.GestureOnly
             || EditingMode == InkCanvasEditingMode.InkAndGesture
             || EditingMode == InkCanvasEditingMode.EraseByPoint))
        {
            FinishDrawing();
            e.Handled = true;
        }
    }

    private void OnMouseLeaveHandler(object sender, MouseEventArgs e)
    {
        if (_selectionEditSession is not null)
        {
            _selectionEditSession = null;
            ReleaseMouseCapture();
            InvalidateVisual();
        }

        if (_isDrawing && EditingMode is InkCanvasEditingMode.Ink
            or InkCanvasEditingMode.GestureOnly
            or InkCanvasEditingMode.InkAndGesture)
        {
            FinishDrawing();
        }
    }

    private void OnPreviewStylusInputHandler(object sender, Input.StylusEventArgs e)
    {
        UpdateActiveEditingMode(e.Inverted);
        if (ActiveEditingMode is InkCanvasEditingMode.Ink
            or InkCanvasEditingMode.InkAndGesture
            or InkCanvasEditingMode.GestureOnly
            or InkCanvasEditingMode.EraseByStroke
            or InkCanvasEditingMode.EraseByPoint)
        {
            e.Handled = true;
        }
    }

    private void OnPreviewStylusStateHandler(object sender, Input.StylusEventArgs e) =>
        UpdateActiveEditingMode(e.Inverted);

    private void OnPreviewStylusOutOfRangeHandler(object sender, Input.StylusEventArgs e) =>
        UpdateActiveEditingMode(inverted: false);

    #region Touch Input

    private void OnTouchDownHandler(object sender, TouchEventArgs e)
    {
        var touchDevice = e.TouchDevice;
        var position = touchDevice.GetTouchPoint(this).Position;
        float pressure = GetTouchPressure(e);

        switch (EditingMode)
        {
            case InkCanvasEditingMode.Ink:
            case InkCanvasEditingMode.GestureOnly:
            case InkCanvasEditingMode.InkAndGesture:
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
            case InkCanvasEditingMode.GestureOnly:
            case InkCanvasEditingMode.InkAndGesture:
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
            case InkCanvasEditingMode.GestureOnly:
            case InkCanvasEditingMode.InkAndGesture:
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
            CommitCollectedStroke(session.Stroke);
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
        => stroke?.DrawingAttributes?.BrushShader is Jalium.UI.Ink.Shaders.EraserBrushShader;

    /// <summary>
    /// Builds a fresh eraser DrawingAttributes: zero-based copy of the
    /// canvas defaults, width = EraserDiameter, brush = eraser shader.
    /// Used by EraseByPoint mode when translating a mouse drag into a
    /// stroke recorded on the Strokes collection.
    /// </summary>
    private DrawingAttributes BuildEraserAttributes()
        => new()
        {
            Width          = EraserShape.Width,
            Height         = EraserShape.Height,
            Color          = Media.Colors.Black,
            FitToCurve     = true,
            IgnorePressure = true,
            BrushShader    = Jalium.UI.Ink.Shaders.EraserBrushShader.Instance,
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
        CommitCollectedStroke(stroke);
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
            CommitCollectedStroke(_currentStroke);
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
        var hitStrokes = Strokes.HitTest(
            position,
            Math.Max(EraserShape.Width, EraserShape.Height));

        foreach (var stroke in hitStrokes)
        {
            var erasingArgs = new InkCanvasStrokeErasingEventArgs(stroke);
            OnStrokeErasing(erasingArgs);

            if (!erasingArgs.Cancel)
            {
                if (Strokes.Remove(stroke))
                {
                    OnStrokeErased(new RoutedEventArgs(StrokeErasedEvent, this));
                }
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

        CommitCollectedStroke(stroke);
        InvalidateVisual();
    }

    private void CommitCollectedStroke(Stroke stroke)
    {
        InkCanvasEditingMode mode = ActiveEditingMode;
        if (!IsEraserStroke(stroke)
            && mode is InkCanvasEditingMode.GestureOnly or InkCanvasEditingMode.InkAndGesture)
        {
            GestureRecognitionResult? result = RecognizeGesture(stroke);
            if (result is not null)
            {
                var gestureArgs = new InkCanvasGestureEventArgs(
                    new StrokeCollection { stroke },
                    new[] { result });
                OnGesture(gestureArgs);

                // Cancel=true means the candidate is ink, matching WPF's event contract.
                if (mode == InkCanvasEditingMode.GestureOnly || !gestureArgs.Cancel)
                    return;
            }
            else if (mode == InkCanvasEditingMode.GestureOnly)
            {
                return;
            }
        }

        Strokes.Add(stroke);
        OnStrokeCollected(new InkCanvasStrokeCollectedEventArgs(stroke));
    }

    private GestureRecognitionResult? RecognizeGesture(Stroke stroke)
    {
        return InkGestureRecognizerCore.Recognize(stroke, _enabledGestures);
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

    private StrokeCollection ValidateSelectedStrokes(StrokeCollection? selectedStrokes)
    {
        var validStrokes = new StrokeCollection();
        if (selectedStrokes is null)
        {
            return validStrokes;
        }

        foreach (var stroke in selectedStrokes)
        {
            if (Strokes.Contains(stroke) && !validStrokes.Contains(stroke))
            {
                validStrokes.Add(stroke);
            }
        }

        return validStrokes;
    }

    private List<UIElement> ValidateSelectedElements(IEnumerable<UIElement>? selectedElements)
    {
        var validElements = new List<UIElement>();
        if (selectedElements is null)
        {
            return validElements;
        }

        foreach (var element in selectedElements)
        {
            if (element is null || validElements.Contains(element) || ReferenceEquals(element, this))
            {
                continue;
            }

            for (Visual? ancestor = element.VisualParent; ancestor is not null; ancestor = ancestor.VisualParent)
            {
                if (ReferenceEquals(ancestor, this))
                {
                    validElements.Add(element);
                    break;
                }
            }
        }

        return validElements;
    }

    private bool SelectionsAreEqual(StrokeCollection strokes, IReadOnlyCollection<UIElement> elements)
    {
        if (_selectedStrokes.Count != strokes.Count || _selectedElements.Count != elements.Count)
        {
            return false;
        }

        return _selectedStrokes.All(strokes.Contains) && _selectedElements.All(elements.Contains);
    }

    private void StartSelectionEdit(Point point)
    {
        InkCanvasSelectionHitResult hit = HitTestSelection(point);
        if (hit == InkCanvasSelectionHitResult.None)
        {
            StrokeCollection hitStrokes = Strokes.HitTest(point, 6.0);
            if (hitStrokes.Count == 0)
            {
                Select(new StrokeCollection(), Array.Empty<UIElement>());
                return;
            }

            Select(new StrokeCollection { hitStrokes[hitStrokes.Count - 1] });
            hit = InkCanvasSelectionHitResult.Selection;
        }

        if ((hit == InkCanvasSelectionHitResult.Selection && !MoveEnabled)
            || (hit != InkCanvasSelectionHitResult.Selection && !ResizeEnabled))
        {
            return;
        }

        Rect bounds = GetSelectionBounds();
        if (bounds.IsEmpty)
            return;

        _selectionEditSession = new SelectionEditSession(hit, point, bounds);
        CaptureMouse();
    }

    private void FinishSelectionEdit(Point point)
    {
        SelectionEditSession? session = _selectionEditSession;
        _selectionEditSession = null;
        ReleaseMouseCapture();
        if (session is null)
            return;

        Rect newBounds = GetEditedSelectionBounds(session, point);
        if (newBounds == session.OriginalBounds)
        {
            InvalidateVisual();
            return;
        }

        var args = new InkCanvasSelectionEditingEventArgs(session.OriginalBounds, newBounds);
        bool moving = session.HitResult == InkCanvasSelectionHitResult.Selection;
        if (moving)
            OnSelectionMoving(args);
        else
            OnSelectionResizing(args);

        if (!args.Cancel && !args.NewRectangle.IsEmpty)
        {
            ApplySelectionBounds(session.OriginalBounds, args.NewRectangle, resize: !moving);
            if (moving)
                OnSelectionMoved(EventArgs.Empty);
            else
                OnSelectionResized(EventArgs.Empty);
        }

        InvalidateVisual();
    }

    private static Rect GetEditedSelectionBounds(SelectionEditSession session, Point point)
    {
        Rect old = session.OriginalBounds;
        double dx = point.X - session.StartPoint.X;
        double dy = point.Y - session.StartPoint.Y;
        if (session.HitResult == InkCanvasSelectionHitResult.Selection)
            return new Rect(old.X + dx, old.Y + dy, old.Width, old.Height);

        double left = old.Left;
        double top = old.Top;
        double right = old.Right;
        double bottom = old.Bottom;
        switch (session.HitResult)
        {
            case InkCanvasSelectionHitResult.TopLeft:
                left += dx;
                top += dy;
                break;
            case InkCanvasSelectionHitResult.Top:
                top += dy;
                break;
            case InkCanvasSelectionHitResult.TopRight:
                right += dx;
                top += dy;
                break;
            case InkCanvasSelectionHitResult.Right:
                right += dx;
                break;
            case InkCanvasSelectionHitResult.BottomRight:
                right += dx;
                bottom += dy;
                break;
            case InkCanvasSelectionHitResult.Bottom:
                bottom += dy;
                break;
            case InkCanvasSelectionHitResult.BottomLeft:
                left += dx;
                bottom += dy;
                break;
            case InkCanvasSelectionHitResult.Left:
                left += dx;
                break;
        }

        if (right - left < 1.0)
        {
            if (session.HitResult is InkCanvasSelectionHitResult.Left
                or InkCanvasSelectionHitResult.TopLeft
                or InkCanvasSelectionHitResult.BottomLeft)
                left = right - 1.0;
            else
                right = left + 1.0;
        }
        if (bottom - top < 1.0)
        {
            if (session.HitResult is InkCanvasSelectionHitResult.Top
                or InkCanvasSelectionHitResult.TopLeft
                or InkCanvasSelectionHitResult.TopRight)
                top = bottom - 1.0;
            else
                bottom = top + 1.0;
        }

        return new Rect(left, top, right - left, bottom - top);
    }

    private void ApplySelectionBounds(Rect oldBounds, Rect newBounds, bool resize)
    {
        double scaleX = resize && oldBounds.Width > double.Epsilon
            ? newBounds.Width / oldBounds.Width
            : 1.0;
        double scaleY = resize && oldBounds.Height > double.Epsilon
            ? newBounds.Height / oldBounds.Height
            : 1.0;
        double offsetX = newBounds.X - oldBounds.X * scaleX;
        double offsetY = newBounds.Y - oldBounds.Y * scaleY;

        foreach (Stroke stroke in _selectedStrokes)
            stroke.StylusPoints.Transform(scaleX, 0, 0, scaleY, offsetX, offsetY);

        foreach (UIElement element in _selectedElements)
        {
            Rect elementBounds = GetElementBounds(element);
            double newX = elementBounds.X * scaleX + offsetX;
            double newY = elementBounds.Y * scaleY + offsetY;
            SetLeft(element, newX);
            SetTop(element, newY);
            if (resize && element is FrameworkElement frameworkElement)
            {
                frameworkElement.Width = Math.Max(0, elementBounds.Width * scaleX);
                frameworkElement.Height = Math.Max(0, elementBounds.Height * scaleY);
            }
        }
    }

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
            ((INotifyCollectionChanged)oldCollection).CollectionChanged -= canvas.OnStrokesCollectionChanged;
        }

        if (e.NewValue is StrokeCollection newCollection)
        {
            ((INotifyCollectionChanged)newCollection).CollectionChanged += canvas.OnStrokesCollectionChanged;
        }

        if (e.OldValue is StrokeCollection previousStrokes &&
            e.NewValue is StrokeCollection replacementStrokes &&
            !ReferenceEquals(previousStrokes, replacementStrokes))
        {
            canvas._selectedStrokes = new StrokeCollection();
            canvas.OnStrokesReplaced(
                new InkCanvasStrokesReplacedEventArgs(replacementStrokes, previousStrokes));
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
                    dirty = dirty.IsEmpty ? s.GetBounds() : Rect.Union(dirty, s.GetBounds());
            }
            if (e.OldItems != null)
            {
                foreach (Stroke s in e.OldItems)
                    dirty = dirty.IsEmpty ? s.GetBounds() : Rect.Union(dirty, s.GetBounds());
            }
        }

        if (fullInvalidate || dirty.IsEmpty)
            _committedLayer.InvalidateVisual();
        else
            _committedLayer.InvalidateVisual(dirty);

        var validSelectedStrokes = ValidateSelectedStrokes(_selectedStrokes);
        if (validSelectedStrokes.Count != _selectedStrokes.Count)
        {
            _selectedStrokes = validSelectedStrokes;
            OnSelectionChanged(EventArgs.Empty);
        }

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

        // A brush dispatch reported a stale device generation
        // (InkDispatchResult.StaleContext) on an earlier frame. Drop the
        // stale-generation layer + shader handles now — on the render-pass
        // thread, between frames, never mid-dispatch — so the rebuild below
        // re-pairs the bitmap and every shader on the currently-registered
        // pipeline. attemptingGenerationRebuild lets the post-replay
        // self-check below tell whether the rebuild actually healed the
        // mismatch.
        bool attemptingGenerationRebuild = false;
        if (_inkRebuildPending)
        {
            _inkRebuildPending = false;
            attemptingGenerationRebuild = true;
            DisposeInkLayerResources();
        }

        if (_inkLayer == null || !_inkLayer.IsValid)
        {
            // Both backends (D3D12 and Vulkan) implement the full GPU
            // brush-shader ink pipeline; the native allocation only returns 0
            // on exceptional failure (device lost, DXC shader compiler
            // unavailable, extreme OOM). Surface that as a quiet
            // InkLayerBitmap construction failure and fall back to the legacy
            // CPU raster path in OnRender.
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

            // If a dispatch during this resize-replay tripped a stale-context
            // failure (HandleInkDispatchFailure set
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

    private void OnInkCanvasUnloaded(object? sender, RoutedEventArgs e)
    {
        // An Unloaded InkCanvas can be detached in the middle of a pointer
        // sequence. Drop every in-flight preview so queued RTS processed
        // callbacks cannot keep stale stroke buffers alive or commit them when
        // the same instance is attached again later.
        _realTimePreviewPlugIn.Reset();
        _dynamicRenderer.Reset();
        _inkCollectionStylusPlugIn.Reset();
        _activeTouchStrokes.Clear();
        _currentStroke = null;
        _currentPoints = null;
        _selectionEditSession = null;
        _isDrawing = false;

        DisposeInkLayerResources();
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
        AcquireShaderHandle(Jalium.UI.Ink.Shaders.EraserBrushShader.Instance);
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
    /// (<see cref="InkLayerBitmap.DispatchBrush"/>). The codes are
    /// backend-agnostic (<see cref="InkDispatchResult"/>), so recovery is
    /// picked from the code alone. Only
    /// <see cref="InkDispatchResult.StaleContext"/> — the device generation
    /// behind the handles is lost, or the bitmap and shader were baked on
    /// mismatched generations — is self-healing, and it heals by rebuilding
    /// the whole GPU ink-resource set so the bitmap and all shader handles
    /// re-pair on the currently-registered pipeline. The rebuild is deferred
    /// to the next <see cref="EnsureInkLayer"/> so resources are never torn
    /// down mid-dispatch / mid-replay. Every other outcome
    /// (<see cref="InkDispatchResult.Transient"/> and the invalid-arg/state
    /// classes) is logged once per distinct code and left to recover on its
    /// own — the handles are still healthy and the next dispatch usually
    /// succeeds; forcing a teardown there would amplify a momentary failure
    /// into a full rebuild storm.
    /// </summary>
    private void HandleInkDispatchFailure(int rc)
    {
        if (rc == InkDispatchResult.StaleContext)
        {
            // Already scheduled a rebuild, or already gave up — nothing to add
            // (also collapses a whole frame's worth of failed strokes into one
            // scheduled rebuild + one log line).
            if (_inkRebuildPending || _inkRebuildSuppressed)
                return;

            _inkRebuildPending = true;
            System.Diagnostics.Debug.WriteLine(
                $"[InkCanvas] ink brush dispatch reported a stale device generation (rc={rc}); scheduling GPU ink-layer rebuild on the current device generation.");
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
        if (d is not InkCanvas canvas)
            return;

        if (canvas._dynamicRenderer != null)
            canvas._dynamicRenderer.DrawingAttributes = canvas.DefaultDrawingAttributes.Clone();

        if (e.OldValue is DrawingAttributes previous
            && e.NewValue is DrawingAttributes replacement
            && !ReferenceEquals(previous, replacement))
        {
            canvas.OnDefaultDrawingAttributesReplaced(
                new DrawingAttributesReplacedEventArgs(replacement, previous));
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
            canvas._selectionEditSession = null;

            canvas._dynamicRenderer?.Reset();
            canvas._inkCollectionStylusPlugIn?.Reset();
            // Drop any in-flight RTS preview sessions — a mid-stroke editing-mode
            // change must not leave a stale Ink session bleeding into the next
            // editing mode (eraser, select, etc.).
            canvas._realTimePreviewPlugIn?.Reset();
            if (!canvas._isStylusInverted)
                canvas.UpdateActiveEditingMode(inverted: false);
            canvas.OnEditingModeChanged(
                new RoutedEventArgs(EditingModeChangedEvent, canvas));
        }
    }

    private static void OnEditingModeInvertedPropertyChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e)
    {
        if (d is not InkCanvas canvas)
            return;

        if (canvas._isStylusInverted)
            canvas.UpdateActiveEditingMode(inverted: true);
        canvas.OnEditingModeInvertedChanged(
            new RoutedEventArgs(EditingModeInvertedChangedEvent, canvas));
    }

    private static bool IsValidEditingMode(object? value) =>
        value is InkCanvasEditingMode mode && Enum.IsDefined(mode);

    private static bool IsValidPositioningValue(object? value) =>
        value is double length && (double.IsNaN(length) || double.IsFinite(length));

    private static void OnPositioningChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement { VisualParent: InkCanvasChildrenHost host })
            host.InvalidateArrange();
    }

    private void UpdateActiveEditingMode(bool inverted)
    {
        _isStylusInverted = inverted;
        InkCanvasEditingMode next = inverted ? EditingModeInverted : EditingMode;
        if (ActiveEditingMode == next)
            return;

        SetValue(ActiveEditingModePropertyKey, next);
        OnActiveEditingModeChanged(
            new RoutedEventArgs(ActiveEditingModeChangedEvent, this));
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
        protected override bool IsActiveForInputCore(RawStylusInput rawStylusInput)
        {
            return _owner.ActiveEditingMode is InkCanvasEditingMode.EraseByStroke;
        }

        protected override void OnStylusDown(RawStylusInput rawStylusInput)
        {
            var points = rawStylusInput.GetStylusPoints();
            _owner.EraseStrokesAt(points);
            rawStylusInput.NotifyWhenProcessed(rawStylusInput);
        }

        protected override void OnStylusMove(RawStylusInput rawStylusInput)
        {
            var points = rawStylusInput.GetStylusPoints();
            _owner.EraseStrokesAt(points);
            rawStylusInput.NotifyWhenProcessed(rawStylusInput);
        }

        protected override void OnStylusUp(RawStylusInput rawStylusInput)
        {
            var points = rawStylusInput.GetStylusPoints();
            _owner.EraseStrokesAt(points);
            rawStylusInput.NotifyWhenProcessed(rawStylusInput);
        }

        protected override void OnStylusDownProcessed(object callbackData, bool targetVerified) => _owner.InvalidateVisual();
        protected override void OnStylusMoveProcessed(object callbackData, bool targetVerified) => _owner.InvalidateVisual();
        protected override void OnStylusUpProcessed(object callbackData, bool targetVerified) => _owner.InvalidateVisual();

    }

    #endregion

    #region Event Raisers

    /// <summary>
    /// Raises the <see cref="StrokeCollected"/> event.
    /// </summary>
    protected virtual void OnStrokeCollected(InkCanvasStrokeCollectedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        RaiseEvent(e);
    }

    /// <summary>
    /// Raises the <see cref="Gesture"/> event.
    /// </summary>
    protected virtual void OnGesture(InkCanvasGestureEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        RaiseEvent(e);
    }

    /// <summary>
    /// Raises the <see cref="StrokeErasing"/> event.
    /// </summary>
    protected virtual void OnStrokeErasing(InkCanvasStrokeErasingEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        StrokeErasing?.Invoke(this, e);
    }

    /// <summary>
    /// Raises the <see cref="StrokeErased"/> event.
    /// </summary>
    protected virtual void OnStrokeErased(RoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        RaiseEvent(e);
    }

    /// <summary>
    /// Raises the <see cref="StrokesReplaced"/> event.
    /// </summary>
    protected virtual void OnStrokesReplaced(InkCanvasStrokesReplacedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        StrokesReplaced?.Invoke(this, e);
    }

    /// <summary>
    /// Raises the <see cref="EditingModeChanged"/> event.
    /// </summary>
    protected virtual void OnEditingModeChanged(RoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        RaiseEvent(e);
    }

    /// <summary>Raises <see cref="ActiveEditingModeChanged"/>.</summary>
    protected virtual void OnActiveEditingModeChanged(RoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        RaiseEvent(e);
    }

    /// <summary>Raises <see cref="EditingModeInvertedChanged"/>.</summary>
    protected virtual void OnEditingModeInvertedChanged(RoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        RaiseEvent(e);
    }

    /// <summary>Raises <see cref="DefaultDrawingAttributesReplaced"/>.</summary>
    protected virtual void OnDefaultDrawingAttributesReplaced(
        DrawingAttributesReplacedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        DefaultDrawingAttributesReplaced?.Invoke(this, e);
    }

    /// <summary>
    /// Raises the <see cref="SelectionChanging"/> event.
    /// </summary>
    protected virtual void OnSelectionChanging(InkCanvasSelectionChangingEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        SelectionChanging?.Invoke(this, e);
    }

    /// <summary>
    /// Raises the <see cref="SelectionChanged"/> event.
    /// </summary>
    protected virtual void OnSelectionChanged(EventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        SelectionChanged?.Invoke(this, e);
    }

    /// <summary>
    /// Raises the <see cref="SelectionMoving"/> event.
    /// </summary>
    protected virtual void OnSelectionMoving(InkCanvasSelectionEditingEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        SelectionMoving?.Invoke(this, e);
    }

    /// <summary>
    /// Raises the <see cref="SelectionMoved"/> event.
    /// </summary>
    protected virtual void OnSelectionMoved(EventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        SelectionMoved?.Invoke(this, e);
    }

    /// <summary>
    /// Raises the <see cref="SelectionResizing"/> event.
    /// </summary>
    protected virtual void OnSelectionResizing(InkCanvasSelectionEditingEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        SelectionResizing?.Invoke(this, e);
    }

    /// <summary>
    /// Raises the <see cref="SelectionResized"/> event.
    /// </summary>
    protected virtual void OnSelectionResized(EventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        SelectionResized?.Invoke(this, e);
    }

    #endregion

    private sealed class InkCanvasClipboardPayload
    {
        public InkCanvasClipboardPayload(StrokeCollection strokes, Rect bounds)
        {
            Strokes = strokes;
            Bounds = bounds;
        }

        public StrokeCollection Strokes { get; }

        public Rect Bounds { get; }
    }

    private sealed class SelectionEditSession
    {
        public SelectionEditSession(
            InkCanvasSelectionHitResult hitResult,
            Point startPoint,
            Rect originalBounds)
        {
            HitResult = hitResult;
            StartPoint = startPoint;
            CurrentPoint = startPoint;
            OriginalBounds = originalBounds;
        }

        public InkCanvasSelectionHitResult HitResult { get; }

        public Point StartPoint { get; }

        public Point CurrentPoint { get; set; }

        public Rect OriginalBounds { get; }
    }

    /// <summary>
    /// Hosts non-ink UI children and applies InkCanvas's own positioning attached
    /// properties. Keeping this layer separate preserves committed-ink caching while
    /// letting application content render and hit-test above the strokes.
    /// </summary>
    private sealed class InkCanvasChildrenHost : Panel
    {
        protected override Size MeasureOverride(Size availableSize)
        {
            double width = 0;
            double height = 0;
            var infinite = new Size(double.PositiveInfinity, double.PositiveInfinity);
            foreach (UIElement child in Children)
            {
                child.Measure(infinite);
                double left = GetLeft(child);
                double top = GetTop(child);
                if (double.IsNaN(left)) left = 0;
                if (double.IsNaN(top)) top = 0;
                width = Math.Max(width, Math.Max(0, left) + child.DesiredSize.Width);
                height = Math.Max(height, Math.Max(0, top) + child.DesiredSize.Height);
            }

            return new Size(width, height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            foreach (UIElement child in Children)
            {
                double left = GetLeft(child);
                double top = GetTop(child);
                double right = GetRight(child);
                double bottom = GetBottom(child);
                double width = child.DesiredSize.Width;
                double height = child.DesiredSize.Height;

                double x = !double.IsNaN(left)
                    ? left
                    : !double.IsNaN(right) ? finalSize.Width - right - width : 0;
                double y = !double.IsNaN(top)
                    ? top
                    : !double.IsNaN(bottom) ? finalSize.Height - bottom - height : 0;
                child.Arrange(new Rect(x, y, width, height));
            }

            return finalSize;
        }
    }

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
/// Represents the method that handles the <see cref="InkCanvas.StrokeCollected"/> event.
/// </summary>
public delegate void InkCanvasStrokeCollectedEventHandler(
    object sender,
    InkCanvasStrokeCollectedEventArgs e);

/// <summary>
/// Represents the method that handles the <see cref="InkCanvas.StrokesReplaced"/> event.
/// </summary>
public delegate void InkCanvasStrokesReplacedEventHandler(
    object sender,
    InkCanvasStrokesReplacedEventArgs e);

/// <summary>
/// Represents the method that handles the <see cref="InkCanvas.SelectionChanging"/> event.
/// </summary>
public delegate void InkCanvasSelectionChangingEventHandler(
    object sender,
    InkCanvasSelectionChangingEventArgs e);

/// <summary>
/// Represents the method that handles the selection editing events.
/// </summary>
public delegate void InkCanvasSelectionEditingEventHandler(
    object sender,
    InkCanvasSelectionEditingEventArgs e);

/// <summary>
/// Represents the method that handles the <see cref="InkCanvas.StrokeErasing"/> event.
/// </summary>
public delegate void InkCanvasStrokeErasingEventHandler(
    object sender,
    InkCanvasStrokeErasingEventArgs e);

/// <summary>
/// Represents the method that handles the <see cref="InkCanvas.Gesture"/> event.
/// </summary>
public delegate void InkCanvasGestureEventHandler(
    object sender,
    InkCanvasGestureEventArgs e);

/// <summary>
/// Provides data for the <see cref="InkCanvas.StrokeCollected"/> event.
/// </summary>
public class InkCanvasStrokeCollectedEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InkCanvasStrokeCollectedEventArgs"/> class.
    /// </summary>
    /// <param name="stroke">The stroke that was collected.</param>
    public InkCanvasStrokeCollectedEventArgs(Stroke stroke)
        : base(InkCanvas.StrokeCollectedEvent)
    {
        Stroke = stroke ?? throw new ArgumentNullException(nameof(stroke));
    }

    /// <summary>
    /// Gets the stroke that was collected.
    /// </summary>
    public Stroke Stroke { get; }

    protected override void InvokeEventHandler(Delegate handler, object target)
    {
        if (handler is InkCanvasStrokeCollectedEventHandler strokeCollectedHandler)
        {
            strokeCollectedHandler(target, this);
            return;
        }

        base.InvokeEventHandler(handler, target);
    }
}

/// <summary>
/// Provides data for the <see cref="InkCanvas.StrokesReplaced"/> event.
/// </summary>
public class InkCanvasStrokesReplacedEventArgs : EventArgs
{
    internal InkCanvasStrokesReplacedEventArgs(
        StrokeCollection newStrokes,
        StrokeCollection previousStrokes)
    {
        NewStrokes = newStrokes ?? throw new ArgumentNullException(nameof(newStrokes));
        PreviousStrokes = previousStrokes ?? throw new ArgumentNullException(nameof(previousStrokes));
    }

    /// <summary>Gets the replacement stroke collection.</summary>
    public StrokeCollection NewStrokes { get; }

    /// <summary>Gets the stroke collection that was replaced.</summary>
    public StrokeCollection PreviousStrokes { get; }
}

/// <summary>
/// Provides data for the <see cref="InkCanvas.SelectionChanging"/> event.
/// </summary>
public class InkCanvasSelectionChangingEventArgs : CancelEventArgs
{
    private StrokeCollection _strokes;
    private List<UIElement> _elements;

    internal InkCanvasSelectionChangingEventArgs(
        StrokeCollection selectedStrokes,
        IEnumerable<UIElement> selectedElements)
    {
        _strokes = selectedStrokes ?? throw new ArgumentNullException(nameof(selectedStrokes));
        ArgumentNullException.ThrowIfNull(selectedElements);
        _elements = new List<UIElement>(selectedElements);
    }

    internal bool StrokesChanged { get; private set; }

    internal bool ElementsChanged { get; private set; }

    /// <summary>Replaces the candidate selected elements.</summary>
    public void SetSelectedElements(IEnumerable<UIElement> selectedElements)
    {
        ArgumentNullException.ThrowIfNull(selectedElements);
        _elements = new List<UIElement>(selectedElements);
        ElementsChanged = true;
    }

    /// <summary>Gets the candidate selected elements.</summary>
    public ReadOnlyCollection<UIElement> GetSelectedElements() => new(_elements);

    /// <summary>Replaces the candidate selected strokes.</summary>
    public void SetSelectedStrokes(StrokeCollection selectedStrokes)
    {
        _strokes = selectedStrokes ?? throw new ArgumentNullException(nameof(selectedStrokes));
        StrokesChanged = true;
    }

    /// <summary>Gets a copy of the candidate selected strokes.</summary>
    public StrokeCollection GetSelectedStrokes() => new(_strokes);
}

/// <summary>
/// Provides data for the <see cref="InkCanvas.SelectionMoving"/> and
/// <see cref="InkCanvas.SelectionResizing"/> events.
/// </summary>
public class InkCanvasSelectionEditingEventArgs : CancelEventArgs
{
    internal InkCanvasSelectionEditingEventArgs(Rect oldRectangle, Rect newRectangle)
    {
        OldRectangle = oldRectangle;
        NewRectangle = newRectangle;
    }

    /// <summary>Gets the bounds of the selection before the editing operation.</summary>
    public Rect OldRectangle { get; }

    /// <summary>Gets or sets the bounds of the selection after the editing operation.</summary>
    public Rect NewRectangle { get; set; }
}

/// <summary>
/// Provides data for the <see cref="InkCanvas.StrokeErasing"/> event.
/// </summary>
public class InkCanvasStrokeErasingEventArgs : CancelEventArgs
{
    internal InkCanvasStrokeErasingEventArgs(Stroke stroke)
    {
        Stroke = stroke ?? throw new ArgumentNullException(nameof(stroke));
    }

    /// <summary>Gets the stroke that is about to be erased.</summary>
    public Stroke Stroke { get; }
}

/// <summary>
/// Provides data for the <see cref="InkCanvas.Gesture"/> event.
/// </summary>
public class InkCanvasGestureEventArgs : RoutedEventArgs
{
    private readonly ReadOnlyCollection<GestureRecognitionResult> _gestureRecognitionResults;

    public InkCanvasGestureEventArgs(
        StrokeCollection strokes,
        IEnumerable<GestureRecognitionResult> gestureRecognitionResults)
        : base(InkCanvas.GestureEvent)
    {
        Strokes = strokes ?? throw new ArgumentNullException(nameof(strokes));
        if (strokes.Count == 0)
        {
            throw new ArgumentException("The stroke collection cannot be empty.", nameof(strokes));
        }

        ArgumentNullException.ThrowIfNull(gestureRecognitionResults);
        var results = new List<GestureRecognitionResult>(gestureRecognitionResults);
        if (results.Count == 0)
        {
            throw new ArgumentException("The recognition result sequence cannot be empty.", nameof(gestureRecognitionResults));
        }

        _gestureRecognitionResults = new ReadOnlyCollection<GestureRecognitionResult>(results);
    }

    /// <summary>Gets the strokes that represent the gesture.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public StrokeCollection Strokes { get; }

    /// <summary>Gets the recognition results for the gesture.</summary>
    public IReadOnlyList<GestureRecognitionResult> GestureRecognitionResults =>
        _gestureRecognitionResults;

    /// <summary>Gets the recognition results for the gesture.</summary>
    public ReadOnlyCollection<GestureRecognitionResult> GetGestureRecognitionResults() =>
        _gestureRecognitionResults;

    /// <summary>
    /// Gets or sets a value indicating whether the event should be canceled.
    /// </summary>
    public bool Cancel { get; set; }

    protected override void InvokeEventHandler(Delegate handler, object target)
    {
        if (handler is InkCanvasGestureEventHandler gestureHandler)
        {
            gestureHandler(target, this);
            return;
        }

        base.InvokeEventHandler(handler, target);
    }
}

/// <summary>
/// Specifies legacy Jalium gesture names. New code should use
/// <see cref="ApplicationGesture"/>, whose values match WPF persistence identifiers.
/// </summary>
[Obsolete("Use Jalium.UI.Ink.ApplicationGesture.")]
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

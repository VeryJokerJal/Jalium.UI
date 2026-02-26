using System.Collections.Specialized;
using Jalium.UI.Controls.Ink;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Provides an area for ink collection and display.
/// </summary>
public sealed class InkCanvas : FrameworkElement
{
    #region Private Fields

    private StylusPointCollection? _currentPoints;
    private Stroke? _currentStroke;
    private bool _isDrawing;

    /// <summary>
    /// Minimum distance (in pixels) between consecutive points to avoid jitter.
    /// </summary>
    private const double MinPointDistance = 2.0;

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the Background dependency property.
    /// </summary>
    public static readonly DependencyProperty BackgroundProperty =
        DependencyProperty.Register(nameof(Background), typeof(Brush), typeof(InkCanvas),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the Strokes dependency property.
    /// </summary>
    public static readonly DependencyProperty StrokesProperty =
        DependencyProperty.Register(nameof(Strokes), typeof(StrokeCollection), typeof(InkCanvas),
            new PropertyMetadata(null, OnStrokesChanged));

    /// <summary>
    /// Identifies the DefaultDrawingAttributes dependency property.
    /// </summary>
    public static readonly DependencyProperty DefaultDrawingAttributesProperty =
        DependencyProperty.Register(nameof(DefaultDrawingAttributes), typeof(DrawingAttributes), typeof(InkCanvas),
            new PropertyMetadata(null, OnDefaultDrawingAttributesChanged));

    /// <summary>
    /// Identifies the EditingMode dependency property.
    /// </summary>
    public static readonly DependencyProperty EditingModeProperty =
        DependencyProperty.Register(nameof(EditingMode), typeof(InkCanvasEditingMode), typeof(InkCanvas),
            new PropertyMetadata(InkCanvasEditingMode.Ink, OnEditingModeChanged));

    /// <summary>
    /// Identifies the EraserDiameter dependency property.
    /// </summary>
    public static readonly DependencyProperty EraserDiameterProperty =
        DependencyProperty.Register(nameof(EraserDiameter), typeof(double), typeof(InkCanvas),
            new PropertyMetadata(8.0));

    /// <summary>
    /// Identifies the DefaultStrokeTaperMode dependency property.
    /// </summary>
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
        Strokes = new StrokeCollection();
        DefaultDrawingAttributes = new DrawingAttributes();

        // Reduce anti-aliasing for sharper strokes
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);

        AddHandler(MouseDownEvent, new RoutedEventHandler(OnMouseDownHandler));
        AddHandler(MouseMoveEvent, new RoutedEventHandler(OnMouseMoveHandler));
        AddHandler(MouseUpEvent, new RoutedEventHandler(OnMouseUpHandler));
        AddHandler(MouseLeaveEvent, new RoutedEventHandler(OnMouseLeaveHandler));
    }

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the background brush for the InkCanvas.
    /// </summary>
    public Brush? Background
    {
        get => (Brush?)GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the collection of strokes displayed on this InkCanvas.
    /// </summary>
    public StrokeCollection Strokes
    {
        get => (StrokeCollection?)GetValue(StrokesProperty) ?? new StrokeCollection();
        set => SetValue(StrokesProperty, value);
    }

    /// <summary>
    /// Gets or sets the default drawing attributes for new strokes.
    /// </summary>
    public DrawingAttributes DefaultDrawingAttributes
    {
        get => (DrawingAttributes?)GetValue(DefaultDrawingAttributesProperty) ?? new DrawingAttributes();
        set => SetValue(DefaultDrawingAttributesProperty, value);
    }

    /// <summary>
    /// Gets or sets the editing mode for this InkCanvas.
    /// </summary>
    public InkCanvasEditingMode EditingMode
    {
        get => (InkCanvasEditingMode)(GetValue(EditingModeProperty) ?? InkCanvasEditingMode.Ink);
        set => SetValue(EditingModeProperty, value);
    }

    /// <summary>
    /// Gets or sets the diameter of the eraser.
    /// </summary>
    public double EraserDiameter
    {
        get => (double)GetValue(EraserDiameterProperty)!;
        set => SetValue(EraserDiameterProperty, value);
    }

    /// <summary>
    /// Gets or sets the default taper mode for new strokes.
    /// </summary>
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
        return new Size(
            double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        return finalSize;
    }

    #endregion

    #region Rendering

    /// <inheritdoc/>
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc)
            return;

        // Draw background
        var background = Background;
        if (background != null)
        {
            dc.DrawRectangle(background, null, new Rect(0, 0, ActualWidth, ActualHeight));
        }

        // Draw all committed strokes
        Strokes?.Draw(dc);

        // Draw the current stroke being drawn
        _currentStroke?.Draw(dc);
    }

    #endregion

    #region Input Handling

    private void OnMouseDownHandler(object sender, RoutedEventArgs e)
    {
        if (e is not MouseButtonEventArgs args || args.ChangedButton != MouseButton.Left)
            return;

        var position = args.GetPosition(this);

        switch (EditingMode)
        {
            case InkCanvasEditingMode.Ink:
                StartDrawing(position);
                break;

            case InkCanvasEditingMode.EraseByStroke:
                EraseStrokesAt(position);
                break;

            case InkCanvasEditingMode.EraseByPoint:
                // EraseByPoint is more complex, treat like EraseByStroke for now
                EraseStrokesAt(position);
                break;
        }

        e.Handled = true;
    }

    private void OnMouseMoveHandler(object sender, RoutedEventArgs e)
    {
        if (e is not MouseEventArgs args)
            return;

        var position = args.GetPosition(this);

        if (_isDrawing && EditingMode == InkCanvasEditingMode.Ink)
        {
            ContinueDrawing(position);
            e.Handled = true;
        }
        else if (args.LeftButton == MouseButtonState.Pressed)
        {
            switch (EditingMode)
            {
                case InkCanvasEditingMode.EraseByStroke:
                case InkCanvasEditingMode.EraseByPoint:
                    EraseStrokesAt(position);
                    e.Handled = true;
                    break;
            }
        }
    }

    private void OnMouseUpHandler(object sender, RoutedEventArgs e)
    {
        if (e is not MouseButtonEventArgs args || args.ChangedButton != MouseButton.Left)
            return;

        if (_isDrawing && EditingMode == InkCanvasEditingMode.Ink)
        {
            FinishDrawing();
            e.Handled = true;
        }
    }

    private void OnMouseLeaveHandler(object sender, RoutedEventArgs e)
    {
        if (_isDrawing && EditingMode == InkCanvasEditingMode.Ink)
        {
            FinishDrawing();
        }
    }

    #endregion

    #region Drawing Logic

    private void StartDrawing(Point position)
    {
        _currentPoints = new StylusPointCollection();
        _currentPoints.Add(new StylusPoint(position.X, position.Y));
        _currentStroke = new Stroke(_currentPoints, DefaultDrawingAttributes.Clone());
        _currentStroke.TaperMode = DefaultStrokeTaperMode;
        _isDrawing = true;

        CaptureMouse();
        InvalidateVisual();
    }

    private void ContinueDrawing(Point position)
    {
        if (_currentPoints == null || _currentPoints.Count == 0)
            return;

        // Check minimum distance from the last point to avoid jitter
        var lastPoint = _currentPoints[_currentPoints.Count - 1];
        var dx = position.X - lastPoint.X;
        var dy = position.Y - lastPoint.Y;
        var distanceSquared = dx * dx + dy * dy;

        if (distanceSquared < MinPointDistance * MinPointDistance)
            return;

        _currentPoints.Add(new StylusPoint(position.X, position.Y));
        InvalidateVisual();
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
            Strokes.Add(_currentStroke);
            OnStrokeCollected(new InkCanvasStrokeCollectedEventArgs(_currentStroke));
        }

        _currentStroke = null;
        _currentPoints = null;
        _isDrawing = false;

        ReleaseMouseCapture();
        InvalidateVisual();
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

    #endregion

    #region Event Handlers

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is InkCanvas canvas)
        {
            canvas.InvalidateVisual();
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

        canvas.InvalidateVisual();
        canvas.StrokesChanged?.Invoke(canvas, EventArgs.Empty);
    }

    private void OnStrokesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        InvalidateVisual();
        StrokesChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void OnDefaultDrawingAttributesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // No immediate action needed - new attributes will be used for new strokes
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

            canvas.EditingModeChanged?.Invoke(canvas, new RoutedEventArgs());
        }
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

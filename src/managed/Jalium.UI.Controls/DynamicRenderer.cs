using Jalium.UI.Controls;
using Jalium.UI.Ink;
using Jalium.UI.Input;
using Jalium.UI.Media;
using InkStylusPoint = Jalium.UI.Input.StylusPoint;
using InkStylusPointCollection = Jalium.UI.Input.StylusPointCollection;

namespace Jalium.UI.Input.StylusPlugIns;

/// <summary>
/// Real-time stylus renderer that previews in-progress ink before stroke commit.
/// </summary>
public class DynamicRenderer : StylusPlugIn
{
    private readonly DrawingVisual _previewVisual = new();
    private DrawingAttributes _drawingAttributes = new();
    private InkStylusPointCollection? _previewPoints;
    private Stroke? _previewStroke;
    private InkPresenter? _inkPresenter;

    // NOTE: DynamicRenderer intentionally stays UI-thread (IsRealTimeCapable=false).
    // Its input hooks touch the visual tree (InkPresenter.AttachVisuals,
    // DrawingVisual.RenderOpen) which are not thread-safe. A future split could
    // collect points on the RTS thread and only render on the UI thread, but
    // that needs a non-trivial rewrite of the preview-stroke pipeline.

    /// <summary>
    /// Gets or sets the drawing attributes used for preview rendering.
    /// </summary>
    public DrawingAttributes DrawingAttributes
    {
        get => _drawingAttributes;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(_drawingAttributes, value))
                return;
            _drawingAttributes = value;
            OnDrawingAttributesReplaced();
        }
    }

    /// <summary>Gets the visual that contains the dynamic preview.</summary>
    public Visual RootVisual => _previewVisual;

    /// <summary>Gets the dispatcher that owns the renderer's visual state.</summary>
    protected Jalium.UI.Threading.Dispatcher GetDispatcher() =>
        Jalium.UI.Threading.Dispatcher.FromLegacy(
            Element?.Dispatcher ?? Jalium.UI.Dispatcher.GetForCurrentThread());

    /// <summary>Draws a generated ink geometry into the supplied drawing context.</summary>
    protected virtual void OnDraw(
        DrawingContext drawingContext,
        InkStylusPointCollection stylusPoints,
        Geometry geometry,
        Brush fillBrush)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        ArgumentNullException.ThrowIfNull(stylusPoints);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(fillBrush);
        drawingContext.DrawGeometry(fillBrush, null, geometry);
    }

    /// <summary>Rebuilds active preview state after drawing attributes are replaced.</summary>
    protected virtual void OnDrawingAttributesReplaced()
    {
        if (_previewPoints is not null)
            _previewStroke = new Stroke(_previewPoints, _drawingAttributes.Clone());
        RedrawPreviewVisual();
        Element?.InvalidateVisual();
    }

    /// <summary>Resets the preview to the points reported by a stylus device.</summary>
    public virtual void Reset(StylusDevice stylusDevice, InkStylusPointCollection stylusPoints)
    {
        ArgumentNullException.ThrowIfNull(stylusDevice);
        ArgumentNullException.ThrowIfNull(stylusPoints);
        ClearPreview();
        if (stylusPoints.Count != 0)
            StartPreview(stylusPoints);
        Element?.InvalidateVisual();
    }

    /// <summary>
    /// The in-progress stylus stroke being previewed, or null when no
    /// stylus session is active. Exposed so <see cref="InkCanvas"/> can
    /// dispatch the same pixel-shader brush over its preview bitmap —
    /// keeping stylus preview pixel-identical with the eventual commit.
    /// Returns null when there aren't yet enough points for a shader
    /// dispatch (minimum 2).
    /// </summary>
    internal Stroke? CurrentPreviewStroke
        => _previewStroke is { } s && s.StylusPoints.Count >= 2 ? s : null;

    internal void SetInkPresenter(InkPresenter? inkPresenter)
    {
        if (ReferenceEquals(_inkPresenter, inkPresenter))
        {
            return;
        }

        if (_inkPresenter != null)
        {
            _inkPresenter.DetachVisuals(_previewVisual);
        }

        _inkPresenter = inkPresenter;

        if (_inkPresenter != null && _previewStroke != null)
        {
            _inkPresenter.AttachVisuals(_previewVisual, DrawingAttributes);
        }
    }

    internal void DrawPreview(DrawingContext drawingContext)
    {
        _previewStroke?.Draw(drawingContext);
    }

    internal void Reset()
    {
        ClearPreview();
    }

    protected override void OnStylusDown(RawStylusInput rawStylusInput)
    {
        StartPreview(rawStylusInput.GetStylusPoints());
        rawStylusInput.NotifyWhenProcessed(rawStylusInput);
    }

    protected override void OnStylusMove(RawStylusInput rawStylusInput)
    {
        AppendPreview(rawStylusInput.GetStylusPoints());
        rawStylusInput.NotifyWhenProcessed(rawStylusInput);
    }

    protected override void OnStylusUp(RawStylusInput rawStylusInput)
    {
        AppendPreview(rawStylusInput.GetStylusPoints());
        ClearPreview();
        rawStylusInput.NotifyWhenProcessed(rawStylusInput);
    }

    protected override void OnStylusDownProcessed(object callbackData, bool targetVerified) => Element?.InvalidateVisual();
    protected override void OnStylusMoveProcessed(object callbackData, bool targetVerified) => Element?.InvalidateVisual();
    protected override void OnStylusUpProcessed(object callbackData, bool targetVerified) => Element?.InvalidateVisual();

    protected override void OnRemoved()
    {
        ClearPreview();
    }

    private void StartPreview(Jalium.UI.Input.StylusPointCollection inputPoints)
    {
        _previewPoints = ConvertToInkPoints(inputPoints);
        _previewStroke = new Stroke(_previewPoints, DrawingAttributes.Clone());

        if (_inkPresenter != null)
        {
            _inkPresenter.AttachVisuals(_previewVisual, DrawingAttributes);
        }

        RedrawPreviewVisual();
    }

    private void AppendPreview(Jalium.UI.Input.StylusPointCollection inputPoints)
    {
        if (_previewPoints == null)
        {
            return;
        }

        foreach (var point in inputPoints)
        {
            _previewPoints.Add(new InkStylusPoint(point.X, point.Y, point.PressureFactor));
        }

        RedrawPreviewVisual();
    }

    private void ClearPreview()
    {
        _previewPoints = null;
        _previewStroke = null;

        if (_inkPresenter != null)
        {
            _inkPresenter.DetachVisuals(_previewVisual);
        }
    }

    private void RedrawPreviewVisual()
    {
        using var drawingContext = _previewVisual.RenderOpen();
        if (_previewStroke is null || _previewPoints is null)
            return;

        if (_previewStroke.TryGetDynamicRendererDrawing(out Geometry geometry, out Brush fillBrush))
        {
            OnDraw(drawingContext, _previewPoints, geometry, fillBrush);
        }
        else
        {
            // Jalium's particle-based brush extensions cannot be represented by
            // WPF's single geometry/fill-brush callback contract. Retain their
            // specialized renderer while using OnDraw for every WPF-shaped path.
            _previewStroke.Draw(drawingContext);
        }
    }

    private static InkStylusPointCollection ConvertToInkPoints(Jalium.UI.Input.StylusPointCollection inputPoints)
    {
        var result = new InkStylusPointCollection(inputPoints.Count);
        foreach (var point in inputPoints)
        {
            result.Add(new InkStylusPoint(point.X, point.Y, point.PressureFactor));
        }

        return result;
    }
}

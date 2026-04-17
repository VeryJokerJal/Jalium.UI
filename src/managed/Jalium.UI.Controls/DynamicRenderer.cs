using Jalium.UI.Controls.Ink;
using Jalium.UI.Input;
using Jalium.UI.Input.StylusPlugIns;
using Jalium.UI.Media;
using InkStylusPoint = Jalium.UI.Input.StylusPoint;
using InkStylusPointCollection = Jalium.UI.Input.StylusPointCollection;

namespace Jalium.UI.Controls;

/// <summary>
/// Real-time stylus renderer that previews in-progress ink before stroke commit.
/// </summary>
public sealed class DynamicRenderer : StylusPlugIn
{
    private readonly DrawingVisual _previewVisual = new();
    private InkStylusPointCollection? _previewPoints;
    private Stroke? _previewStroke;
    private InkPresenter? _inkPresenter;

    /// <summary>
    /// Gets or sets the drawing attributes used for preview rendering.
    /// </summary>
    public DrawingAttributes DrawingAttributes { get; set; } = new();

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
        rawStylusInput.NotifyWhenProcessed(this);
    }

    protected override void OnStylusMove(RawStylusInput rawStylusInput)
    {
        AppendPreview(rawStylusInput.GetStylusPoints());
        rawStylusInput.NotifyWhenProcessed(this);
    }

    protected override void OnStylusUp(RawStylusInput rawStylusInput)
    {
        AppendPreview(rawStylusInput.GetStylusPoints());
        ClearPreview();
        rawStylusInput.NotifyWhenProcessed(this);
    }

    protected override void OnStylusDownProcessed(RawStylusInput rawStylusInput) => Element?.InvalidateVisual();
    protected override void OnStylusMoveProcessed(RawStylusInput rawStylusInput) => Element?.InvalidateVisual();
    protected override void OnStylusUpProcessed(RawStylusInput rawStylusInput) => Element?.InvalidateVisual();

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
        _previewStroke?.Draw(drawingContext);
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

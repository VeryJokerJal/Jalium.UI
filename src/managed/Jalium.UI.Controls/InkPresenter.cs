using Jalium.UI.Controls.Ink;
using Jalium.UI.Documents;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Renders ink on a surface. Used internally by InkCanvas to render strokes.
/// </summary>
public class InkPresenter : Decorator
{
    private readonly List<Visual> _attachedVisuals = [];

    /// <summary>
    /// Identifies the Strokes dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty StrokesProperty =
        DependencyProperty.Register(nameof(Strokes), typeof(StrokeCollection), typeof(InkPresenter),
            new PropertyMetadata(null, OnStrokesChanged));

    /// <summary>
    /// Gets or sets the strokes that the InkPresenter displays.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public StrokeCollection? Strokes
    {
        get => (StrokeCollection?)GetValue(StrokesProperty);
        set => SetValue(StrokesProperty, value);
    }

    /// <inheritdoc/>
    public override int VisualChildrenCount => base.VisualChildrenCount + _attachedVisuals.Count;

    /// <inheritdoc/>
    public override Visual? GetVisualChild(int index)
    {
        int baseCount = base.VisualChildrenCount;
        if (index < baseCount)
        {
            return base.GetVisualChild(index);
        }

        int attachedIndex = index - baseCount;
        if (attachedIndex >= 0 && attachedIndex < _attachedVisuals.Count)
        {
            return _attachedVisuals[attachedIndex];
        }

        throw new ArgumentOutOfRangeException(nameof(index));
    }

    private static void OnStrokesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is InkPresenter presenter)
            presenter.InvalidateVisual();
    }

    /// <summary>
    /// Attaches the visuals of a DrawingAttributes change to the element.
    /// </summary>
    public void AttachVisuals(Visual visual, DrawingAttributes drawingAttributes)
    {
        ArgumentNullException.ThrowIfNull(visual);
        ArgumentNullException.ThrowIfNull(drawingAttributes);

        if (_attachedVisuals.Contains(visual))
        {
            return;
        }

        AddVisualChild(visual);
        _attachedVisuals.Add(visual);
        InvalidateVisual();
    }

    /// <summary>
    /// Detaches the visuals of a DrawingAttributes change from the element.
    /// </summary>
    public void DetachVisuals(Visual visual)
    {
        ArgumentNullException.ThrowIfNull(visual);

        if (!_attachedVisuals.Remove(visual))
        {
            return;
        }

        RemoveVisualChild(visual);
        InvalidateVisual();
    }
}

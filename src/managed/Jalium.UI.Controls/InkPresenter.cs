using Jalium.UI.Controls.Ink;
using Jalium.UI.Documents;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Renders ink on a surface. Used internally by InkCanvas to render strokes.
/// </summary>
public sealed class InkPresenter : Decorator
{
    /// <summary>
    /// Identifies the Strokes dependency property.
    /// </summary>
    public static readonly DependencyProperty StrokesProperty =
        DependencyProperty.Register(nameof(Strokes), typeof(StrokeCollection), typeof(InkPresenter),
            new PropertyMetadata(null, OnStrokesChanged));

    /// <summary>
    /// Gets or sets the strokes that the InkPresenter displays.
    /// </summary>
    public StrokeCollection? Strokes
    {
        get => (StrokeCollection?)GetValue(StrokesProperty);
        set => SetValue(StrokesProperty, value);
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
        // Integration point for real-time ink rendering
    }

    /// <summary>
    /// Detaches the visuals of a DrawingAttributes change from the element.
    /// </summary>
    public void DetachVisuals(Visual visual)
    {
        // Remove real-time ink rendering visuals
    }
}

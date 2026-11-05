using Jalium.UI.Media;

namespace Jalium.UI.Controls.Primitives;

/// <summary>Renders and allocates the binary content area of a HexEditor template.</summary>
public sealed class HexEditorTextView : FrameworkElement
{
    private HexEditor? Owner
    {
        get
        {
            for (Visual? parent = ParentVisual; parent is not null; parent = parent.VisualParent)
                if (parent is HexEditor owner) return owner.OwnsTextView(this) ? owner : null;
            return null;
        }
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
        => Owner?.MeasureTextView(availableSize) ?? default;

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
        => Owner?.ArrangeTextView(finalSize) ?? finalSize;

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
        => Owner?.RenderTextView(drawingContext);
}

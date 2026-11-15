namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Stretches horizontal separator lines without making their end margins impose
/// an intrinsic width on the separator's content box.
/// </summary>
public sealed class SeparatorLinePanel : Panel
{
    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        double height = 0;
        foreach (UIElement child in Children)
        {
            child.Measure(availableSize);
            height = Math.Max(height, child.DesiredSize.Height);
        }
        return new Size(0, height);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (UIElement child in Children)
            child.Arrange(new Rect(finalSize));
        return finalSize;
    }
}

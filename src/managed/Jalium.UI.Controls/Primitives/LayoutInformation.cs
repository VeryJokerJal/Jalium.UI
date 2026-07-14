using Jalium.UI.Threading;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Provides static methods for querying layout information about elements.
/// </summary>
public static class LayoutInformation
{
    /// <summary>
    /// Gets the layout clip rectangle for the specified element.
    /// </summary>
    /// <param name="element">The element to query.</param>
    /// <returns>The layout clip rectangle, or an empty rect if no clipping is applied.</returns>
    public static Rect GetLayoutClip(FrameworkElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        // If ClipToBounds is true, return the render size
        if (element.ClipToBounds)
        {
            return new Rect(element.RenderSize);
        }

        return Rect.Empty;
    }

    /// <summary>
    /// Gets the layout slot (the space allocated by the parent) for the specified element.
    /// </summary>
    /// <param name="element">The element to query.</param>
    /// <returns>The layout slot rectangle.</returns>
    public static Rect GetLayoutSlot(FrameworkElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        // The layout slot is the final arrange rectangle given by the parent
        // This returns the render size at the origin since visual offset tracking
        // is not currently implemented
        return new Rect(element.RenderSize);
    }

    /// <summary>
    /// Gets the element that caused a layout exception.
    /// </summary>
    /// <param name="dispatcher">The dispatcher to query.</param>
    /// <returns>The element that caused the exception, or null.</returns>
    public static UIElement? GetLayoutExceptionElement(Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        return dispatcher.CoreDispatcher.LastLayoutExceptionElement;
    }

    /// <summary>
    /// Gets the actual size of the layout slot for the specified element.
    /// </summary>
    /// <param name="element">The element to query.</param>
    /// <returns>The size of the layout slot.</returns>
    public static Size GetLayoutSlotSize(FrameworkElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return element.RenderSize;
    }

    /// <summary>
    /// Gets the available size passed to the element during measure.
    /// </summary>
    /// <param name="element">The element to query.</param>
    /// <returns>The available size from the last measure pass.</returns>
    public static Size GetAvailableSize(FrameworkElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        // This would require storing the available size during MeasureOverride
        // For now, return the desired size as an approximation
        return element.DesiredSize;
    }

    /// <summary>
    /// Gets the margin of the specified element.
    /// </summary>
    /// <param name="element">The element to query.</param>
    /// <returns>The margin thickness.</returns>
    public static Thickness GetMargin(FrameworkElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return element.Margin;
    }

    /// <summary>
    /// Gets the offset of the element relative to its parent.
    /// </summary>
    /// <param name="element">The element to query.</param>
    /// <returns>The visual offset.</returns>
    public static Point GetVisualOffset(FrameworkElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        // Visual offset tracking is not currently implemented
        // Return origin point as default
        return new Point(0, 0);
    }

    /// <summary>
    /// Determines whether the element needs a new measure pass.
    /// </summary>
    /// <param name="element">The element to query.</param>
    /// <returns>True if measure is invalid; otherwise, false.</returns>
    public static bool IsMeasureValid(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return element.IsMeasureValid;
    }

    /// <summary>
    /// Determines whether the element needs a new arrange pass.
    /// </summary>
    /// <param name="element">The element to query.</param>
    /// <returns>True if arrange is invalid; otherwise, false.</returns>
    public static bool IsArrangeValid(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return element.IsArrangeValid;
    }

    /// <summary>
    /// Gets the final arranged bounds of an element within its parent's coordinate space.
    /// </summary>
    /// <param name="element">The element to query.</param>
    /// <returns>The bounds rectangle.</returns>
    public static Rect GetBounds(FrameworkElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        // Visual offset tracking is not currently implemented
        // Return bounds at origin with render size
        return new Rect(element.RenderSize);
    }
}

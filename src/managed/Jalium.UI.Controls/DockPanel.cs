namespace Jalium.UI.Controls;

/// <summary>
/// Defines an area where you can arrange child elements either horizontally or vertically, relative to each other.
/// </summary>
public class DockPanel : Panel
{
    #region Attached Properties

    /// <summary>
    /// Identifies the Dock attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty DockProperty =
        DependencyProperty.RegisterAttached("Dock", typeof(Dock), typeof(DockPanel),
            new PropertyMetadata(Dock.Left, OnDockChanged));

    /// <summary>
    /// Gets the value of the Dock attached property for a given element.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static Dock GetDock(UIElement element)
    {
        return (Dock)(element.GetValue(DockProperty) ?? Dock.Left);
    }

    /// <summary>
    /// Sets the value of the Dock attached property for a given element.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static void SetDock(UIElement element, Dock value)
    {
        element.SetValue(DockProperty, value);
    }

    private static void OnDockChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element)
        {
            var parent = element.VisualParent;
            if (parent is DockPanel dockPanel)
            {
                dockPanel.InvalidateMeasure();
            }
        }
    }

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the LastChildFill dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty LastChildFillProperty =
        DependencyProperty.Register(nameof(LastChildFill), typeof(bool), typeof(DockPanel),
            new PropertyMetadata(true, OnLastChildFillChanged));

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets whether the last child element stretches to fill the remaining available space.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public bool LastChildFill
    {
        get => (bool)GetValue(LastChildFillProperty)!;
        set => SetValue(LastChildFillProperty, value);
    }

    #endregion

    private static void OnLastChildFillChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DockPanel panel)
        {
            panel.InvalidateMeasure();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double usedWidth = 0;
        double usedHeight = 0;
        double maxWidth = 0;
        double maxHeight = 0;

        var children = Children;
        int lastIndex = children.Count - 1;

        for (int i = 0; i < children.Count; i++)
        {
            if (children[i] is not UIElement child)
                continue;

            var dock = GetDock(child);
            bool isLast = i == lastIndex && LastChildFill;

            Size childConstraint;
            if (isLast)
            {
                // Last child fills remaining space
                childConstraint = new Size(
                    Math.Max(0, availableSize.Width - usedWidth),
                    Math.Max(0, availableSize.Height - usedHeight));
            }
            else
            {
                switch (dock)
                {
                    case Dock.Left:
                    case Dock.Right:
                        childConstraint = new Size(
                            Math.Max(0, availableSize.Width - usedWidth),
                            Math.Max(0, availableSize.Height - usedHeight));
                        break;
                    default: // Top, Bottom
                        childConstraint = new Size(
                            Math.Max(0, availableSize.Width - usedWidth),
                            Math.Max(0, availableSize.Height - usedHeight));
                        break;
                }
            }

            if (child is FrameworkElement fe)
            {
                fe.Measure(childConstraint);
                var desiredSize = fe.DesiredSize;

                switch (dock)
                {
                    case Dock.Left:
                    case Dock.Right:
                        maxHeight = Math.Max(maxHeight, usedHeight + desiredSize.Height);
                        usedWidth += desiredSize.Width;
                        break;
                    case Dock.Top:
                    case Dock.Bottom:
                        maxWidth = Math.Max(maxWidth, usedWidth + desiredSize.Width);
                        usedHeight += desiredSize.Height;
                        break;
                }
            }
        }

        maxWidth = Math.Max(maxWidth, usedWidth);
        maxHeight = Math.Max(maxHeight, usedHeight);

        return new Size(
            double.IsInfinity(availableSize.Width) ? maxWidth : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? maxHeight : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double leftOffset = 0;
        double topOffset = 0;
        double rightRemaining = finalSize.Width;
        double bottomRemaining = finalSize.Height;

        var children = Children;
        int lastIndex = children.Count - 1;

        for (int i = 0; i < children.Count; i++)
        {
            if (children[i] is not UIElement child)
                continue;

            var dock = GetDock(child);
            bool isLast = i == lastIndex && LastChildFill;

            if (child is FrameworkElement fe)
            {
                Rect childRect;

                if (isLast)
                {
                    // Last child fills remaining space
                    childRect = new Rect(
                        leftOffset,
                        topOffset,
                        Math.Max(0, rightRemaining),
                        Math.Max(0, bottomRemaining));
                }
                else
                {
                    switch (dock)
                    {
                        case Dock.Left:
                            childRect = new Rect(
                                leftOffset,
                                topOffset,
                                fe.DesiredSize.Width,
                                bottomRemaining);
                            leftOffset += fe.DesiredSize.Width;
                            rightRemaining -= fe.DesiredSize.Width;
                            break;

                        case Dock.Right:
                            childRect = new Rect(
                                leftOffset + rightRemaining - fe.DesiredSize.Width,
                                topOffset,
                                fe.DesiredSize.Width,
                                bottomRemaining);
                            rightRemaining -= fe.DesiredSize.Width;
                            break;

                        case Dock.Top:
                            childRect = new Rect(
                                leftOffset,
                                topOffset,
                                rightRemaining,
                                fe.DesiredSize.Height);
                            topOffset += fe.DesiredSize.Height;
                            bottomRemaining -= fe.DesiredSize.Height;
                            break;

                        case Dock.Bottom:
                            childRect = new Rect(
                                leftOffset,
                                topOffset + bottomRemaining - fe.DesiredSize.Height,
                                rightRemaining,
                                fe.DesiredSize.Height);
                            bottomRemaining -= fe.DesiredSize.Height;
                            break;

                        default:
                            childRect = new Rect(leftOffset, topOffset, fe.DesiredSize.Width, fe.DesiredSize.Height);
                            break;
                    }
                }

                fe.Arrange(childRect);
                // Note: Do NOT call SetVisualBounds here - ArrangeCore already handles margin
            }
        }

        return finalSize;
    }
}

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
            new PropertyMetadata(true, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the Spacing dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty SpacingProperty =
        DependencyProperty.Register(nameof(Spacing), typeof(double), typeof(DockPanel),
            new PropertyMetadata(0.0, OnLayoutPropertyChanged));

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

    /// <summary>
    /// Gets or sets the uniform gap, in device-independent pixels, inserted between adjacent
    /// docked children along the axis of the preceding child's <see cref="Dock"/> direction.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double Spacing
    {
        get => (double)GetValue(SpacingProperty)!;
        set => SetValue(SpacingProperty, value);
    }

    private double EffectiveSpacing
    {
        get
        {
            var value = Spacing;
            return (double.IsNaN(value) || double.IsInfinity(value) || value < 0) ? 0 : value;
        }
    }

    #endregion

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DockPanel panel)
        {
            panel.InvalidateMeasure();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double parentWidth = 0;
        double parentHeight = 0;
        double accumulatedWidth = 0;
        double accumulatedHeight = 0;

        var children = Children;
        var spacing = EffectiveSpacing;
        Dock? previousDock = null;

        for (int i = 0; i < children.Count; i++)
        {
            if (children[i] is not FrameworkElement child)
                continue;

            // position:absolute children are out of flow: no dock slot, no spacing.
            if (IsCssAbsolute(child))
                continue;

            // Inject spacing along the axis of the preceding docked sibling so the next
            // child's constraint already accounts for the gap we will insert in Arrange.
            if (previousDock is Dock prev)
            {
                switch (prev)
                {
                    case Dock.Left:
                    case Dock.Right:
                        accumulatedWidth += spacing;
                        break;
                    case Dock.Top:
                    case Dock.Bottom:
                        accumulatedHeight += spacing;
                        break;
                }
            }

            var childConstraint = new Size(
                Math.Max(0, availableSize.Width - accumulatedWidth),
                Math.Max(0, availableSize.Height - accumulatedHeight));

            child.Measure(childConstraint);
            var childDesiredSize = child.DesiredSize;
            var childDock = GetDock(child);

            switch (childDock)
            {
                case Dock.Left:
                case Dock.Right:
                    parentHeight = Math.Max(parentHeight, accumulatedHeight + childDesiredSize.Height);
                    accumulatedWidth += childDesiredSize.Width;
                    break;
                case Dock.Top:
                case Dock.Bottom:
                    parentWidth = Math.Max(parentWidth, accumulatedWidth + childDesiredSize.Width);
                    accumulatedHeight += childDesiredSize.Height;
                    break;
            }

            previousDock = childDock;
        }

        parentWidth = Math.Max(parentWidth, accumulatedWidth);
        parentHeight = Math.Max(parentHeight, accumulatedHeight);

        MeasureCssAbsoluteChildren(availableSize);

        return new Size(parentWidth, parentHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double leftOffset = 0;
        double topOffset = 0;
        double rightRemaining = finalSize.Width;
        double bottomRemaining = finalSize.Height;

        var children = Children;
        bool lastChildFill = LastChildFill;
        var spacing = EffectiveSpacing;

        // Find the true last FrameworkElement index for LastChildFill
        int lastFeIndex = -1;
        if (lastChildFill)
        {
            for (int i = children.Count - 1; i >= 0; i--)
            {
                if (children[i] is FrameworkElement candidate && !IsCssAbsolute(candidate))
                {
                    lastFeIndex = i;
                    break;
                }
            }
        }

        Dock? previousDock = null;

        for (int i = 0; i < children.Count; i++)
        {
            if (children[i] is not FrameworkElement fe)
                continue;

            // position:absolute children are placed by the inset protocol below.
            if (IsCssAbsolute(fe))
                continue;

            // Consume spacing from the slot on the side of the previously docked sibling
            // so the current child (whether docked or LastChildFill) sits past the gap.
            if (previousDock is Dock prev && spacing > 0)
            {
                switch (prev)
                {
                    case Dock.Left:
                        leftOffset += spacing;
                        rightRemaining -= spacing;
                        break;
                    case Dock.Right:
                        rightRemaining -= spacing;
                        break;
                    case Dock.Top:
                        topOffset += spacing;
                        bottomRemaining -= spacing;
                        break;
                    case Dock.Bottom:
                        bottomRemaining -= spacing;
                        break;
                }
            }

            Rect childRect;

            if (i == lastFeIndex)
            {
                // Last child fills remaining space (already offset by any prior spacing).
                childRect = new Rect(
                    leftOffset,
                    topOffset,
                    Math.Max(0, rightRemaining),
                    Math.Max(0, bottomRemaining));
            }
            else
            {
                var dock = GetDock(fe);
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
                previousDock = dock;
            }

            fe.Arrange(childRect);
        }

        ArrangeCssAbsoluteChildren(finalSize);

        return finalSize;
    }
}

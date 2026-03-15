namespace Jalium.UI.Input;

/// <summary>
/// Provides logical and directional navigation between focusable elements.
/// </summary>
public static class KeyboardNavigation
{
    #region Attached Properties

    /// <summary>
    /// Identifies the TabIndex attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static readonly DependencyProperty TabIndexProperty =
        DependencyProperty.RegisterAttached("TabIndex", typeof(int), typeof(KeyboardNavigation),
            new PropertyMetadata(int.MaxValue));

    /// <summary>
    /// Identifies the IsTabStop attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static readonly DependencyProperty IsTabStopProperty =
        DependencyProperty.RegisterAttached("IsTabStop", typeof(bool), typeof(KeyboardNavigation),
            new PropertyMetadata(true));

    /// <summary>
    /// Identifies the TabNavigation attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static readonly DependencyProperty TabNavigationProperty =
        DependencyProperty.RegisterAttached("TabNavigation", typeof(KeyboardNavigationMode), typeof(KeyboardNavigation),
            new PropertyMetadata(KeyboardNavigationMode.Continue));

    /// <summary>
    /// Identifies the DirectionalNavigation attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static readonly DependencyProperty DirectionalNavigationProperty =
        DependencyProperty.RegisterAttached("DirectionalNavigation", typeof(KeyboardNavigationMode), typeof(KeyboardNavigation),
            new PropertyMetadata(KeyboardNavigationMode.Continue));

    /// <summary>
    /// Gets the tab index for the specified element.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static int GetTabIndex(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (int)(element.GetValue(TabIndexProperty) ?? int.MaxValue);
    }

    /// <summary>
    /// Sets the tab index for the specified element.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static void SetTabIndex(DependencyObject element, int value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(TabIndexProperty, value);
    }

    /// <summary>
    /// Gets whether the specified element is a tab stop.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static bool GetIsTabStop(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)(element.GetValue(IsTabStopProperty) ?? true);
    }

    /// <summary>
    /// Sets whether the specified element is a tab stop.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static void SetIsTabStop(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsTabStopProperty, value);
    }

    /// <summary>
    /// Gets the tab navigation mode for the specified element.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static KeyboardNavigationMode GetTabNavigation(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (KeyboardNavigationMode)(element.GetValue(TabNavigationProperty) ?? KeyboardNavigationMode.Continue);
    }

    /// <summary>
    /// Sets the tab navigation mode for the specified element.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static void SetTabNavigation(DependencyObject element, KeyboardNavigationMode value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(TabNavigationProperty, value);
    }

    /// <summary>
    /// Gets the directional navigation mode for the specified element.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static KeyboardNavigationMode GetDirectionalNavigation(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (KeyboardNavigationMode)(element.GetValue(DirectionalNavigationProperty) ?? KeyboardNavigationMode.Continue);
    }

    /// <summary>
    /// Sets the directional navigation mode for the specified element.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static void SetDirectionalNavigation(DependencyObject element, KeyboardNavigationMode value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(DirectionalNavigationProperty, value);
    }

    #endregion

    #region Navigation Methods

    /// <summary>
    /// Moves focus to the next element in the tab order.
    /// </summary>
    /// <param name="currentElement">The element that currently has focus.</param>
    /// <param name="reverse">True to move backward in the tab order.</param>
    /// <returns>True if focus was successfully moved; otherwise, false.</returns>
    public static bool MoveFocus(UIElement currentElement, bool reverse = false)
    {
        ArgumentNullException.ThrowIfNull(currentElement);
        if (!TryGetTabNavigationContext(currentElement, out var context))
        {
            return false;
        }

        var focusableElements = GetOrderedFocusableElements(context.ScopeRoot, NavigationPropertyKind.Tab);
        if (focusableElements.Count == 0)
        {
            return false;
        }

        int currentIndex = focusableElements.IndexOf(currentElement);
        if (currentIndex < 0)
        {
            return FocusElement(reverse ? focusableElements[^1] : focusableElements[0]);
        }

        var nextIndex = currentIndex + (reverse ? -1 : 1);
        if (nextIndex < 0 || nextIndex >= focusableElements.Count)
        {
            if (!context.Wraps)
            {
                return false;
            }

            nextIndex = reverse ? focusableElements.Count - 1 : 0;
        }

        return nextIndex == currentIndex
            ? false
            : FocusElement(focusableElements[nextIndex]);
    }

    /// <summary>
    /// Moves focus in the specified direction.
    /// </summary>
    public static bool MoveFocus(UIElement currentElement, FocusNavigationDirection direction)
    {
        ArgumentNullException.ThrowIfNull(currentElement);

        return direction switch
        {
            FocusNavigationDirection.Next => MoveFocus(currentElement, reverse: false),
            FocusNavigationDirection.Previous => MoveFocus(currentElement, reverse: true),
            FocusNavigationDirection.First => MoveFocusToFirst(currentElement),
            FocusNavigationDirection.Last => MoveFocusToLast(currentElement),
            FocusNavigationDirection.Left or
            FocusNavigationDirection.Right or
            FocusNavigationDirection.Up or
            FocusNavigationDirection.Down => MoveFocusDirectional(currentElement, direction),
            _ => false
        };
    }

    private static bool MoveFocusToFirst(UIElement currentElement)
    {
        if (!TryGetTabNavigationContext(currentElement, out var context))
        {
            return false;
        }

        var focusableElements = GetOrderedFocusableElements(context.ScopeRoot, NavigationPropertyKind.Tab);
        if (focusableElements.Count == 0)
        {
            return false;
        }

        return FocusElement(focusableElements[0]);
    }

    private static bool MoveFocusToLast(UIElement currentElement)
    {
        if (!TryGetTabNavigationContext(currentElement, out var context))
        {
            return false;
        }

        var focusableElements = GetOrderedFocusableElements(context.ScopeRoot, NavigationPropertyKind.Tab);
        if (focusableElements.Count == 0)
        {
            return false;
        }

        return FocusElement(focusableElements[^1]);
    }

    private static bool MoveFocusDirectional(UIElement currentElement, FocusNavigationDirection direction)
    {
        if (!TryGetDirectionalNavigationContext(currentElement, out var context))
        {
            return false;
        }

        var focusableElements = GetOrderedFocusableElements(context.ScopeRoot, NavigationPropertyKind.Directional);
        if (focusableElements.Count <= 1)
        {
            return false;
        }

        // Get current element bounds
        var currentBounds = currentElement.VisualBounds;
        var currentCenter = new Point(
            currentBounds.X + currentBounds.Width / 2,
            currentBounds.Y + currentBounds.Height / 2);

        UIElement? bestCandidate = null;
        double bestScore = double.MaxValue;

        foreach (var candidate in focusableElements)
        {
            if (candidate == currentElement) continue;

            var candidateBounds = candidate.VisualBounds;
            var candidateCenter = new Point(
                candidateBounds.X + candidateBounds.Width / 2,
                candidateBounds.Y + candidateBounds.Height / 2);

            // Check if candidate is in the correct direction
            bool isInDirection = direction switch
            {
                FocusNavigationDirection.Left => candidateCenter.X < currentCenter.X,
                FocusNavigationDirection.Right => candidateCenter.X > currentCenter.X,
                FocusNavigationDirection.Up => candidateCenter.Y < currentCenter.Y,
                FocusNavigationDirection.Down => candidateCenter.Y > currentCenter.Y,
                _ => false
            };

            if (!isInDirection) continue;

            // Calculate score (distance)
            double dx = candidateCenter.X - currentCenter.X;
            double dy = candidateCenter.Y - currentCenter.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);

            // Prefer elements that are more aligned in the navigation direction
            double alignment = direction switch
            {
                FocusNavigationDirection.Left or FocusNavigationDirection.Right => Math.Abs(dy),
                FocusNavigationDirection.Up or FocusNavigationDirection.Down => Math.Abs(dx),
                _ => 0
            };

            double score = distance + alignment * 2;

            if (score < bestScore)
            {
                bestScore = score;
                bestCandidate = candidate;
            }
        }

        if (bestCandidate != null)
        {
            return FocusElement(bestCandidate);
        }

        if (!context.Wraps)
        {
            return false;
        }

        var wrappedCandidate = GetWrappedDirectionalCandidate(currentElement, direction, focusableElements);
        return wrappedCandidate != null && FocusElement(wrappedCandidate);
    }

    private static UIElement? GetVisualRoot(UIElement element)
    {
        var current = element;
        while (current.VisualParent is UIElement parent)
        {
            current = parent;
        }
        return current;
    }

    private static bool TryGetTabNavigationContext(UIElement currentElement, out NavigationContext context)
    {
        return TryGetNavigationContext(currentElement, NavigationPropertyKind.Tab, out context);
    }

    private static bool TryGetDirectionalNavigationContext(UIElement currentElement, out NavigationContext context)
    {
        return TryGetNavigationContext(currentElement, NavigationPropertyKind.Directional, out context);
    }

    private static bool TryGetNavigationContext(UIElement currentElement, NavigationPropertyKind kind, out NavigationContext context)
    {
        var container = FindNavigationContainer(currentElement, kind);
        if (container == null)
        {
            var visualRoot = GetVisualRoot(currentElement);
            if (visualRoot == null)
            {
                context = default;
                return false;
            }

            context = new NavigationContext(visualRoot, false);
            return true;
        }

        var mode = GetNavigationMode(container, kind);
        if (mode == KeyboardNavigationMode.None)
        {
            context = default;
            return false;
        }

        context = new NavigationContext(container, mode == KeyboardNavigationMode.Cycle);
        return true;
    }

    private static UIElement? FindNavigationContainer(UIElement element, NavigationPropertyKind kind)
    {
        for (UIElement? current = element; current != null; current = current.VisualParent as UIElement)
        {
            if (GetNavigationMode(current, kind) != KeyboardNavigationMode.Continue)
            {
                return current;
            }
        }

        return null;
    }

    private static KeyboardNavigationMode GetNavigationMode(UIElement element, NavigationPropertyKind kind)
    {
        return kind == NavigationPropertyKind.Tab
            ? GetTabNavigation(element)
            : GetDirectionalNavigation(element);
    }

    private static List<UIElement> GetOrderedFocusableElements(UIElement root, NavigationPropertyKind kind)
    {
        var orderedElements = new List<(UIElement Element, int VisualOrder)>();
        var visualOrder = 0;
        CollectFocusableElements(root, root, kind, orderedElements, ref visualOrder);

        orderedElements.Sort((a, b) =>
        {
            var tabCompare = GetTabIndex(a.Element).CompareTo(GetTabIndex(b.Element));
            return tabCompare != 0 ? tabCompare : a.VisualOrder.CompareTo(b.VisualOrder);
        });

        var result = new List<UIElement>(orderedElements.Count);
        foreach (var (element, _) in orderedElements)
        {
            result.Add(element);
        }

        return result;
    }

    private static void CollectFocusableElements(
        UIElement element,
        UIElement traversalRoot,
        NavigationPropertyKind kind,
        List<(UIElement Element, int VisualOrder)> results,
        ref int visualOrder)
    {
        if (!element.IsEnabled || element.Visibility != Visibility.Visible)
        {
            return;
        }

        if (element.Focusable && GetIsTabStop(element))
        {
            results.Add((element, visualOrder++));
        }

        if (!ReferenceEquals(element, traversalRoot) && GetNavigationMode(element, kind) == KeyboardNavigationMode.None)
        {
            return;
        }

        // Recursively check children
        for (int i = 0; i < element.VisualChildrenCount; i++)
        {
            if (element.GetVisualChild(i) is UIElement child)
            {
                CollectFocusableElements(child, traversalRoot, kind, results, ref visualOrder);
            }
        }
    }

    private static UIElement? GetWrappedDirectionalCandidate(UIElement currentElement, FocusNavigationDirection direction, List<UIElement> focusableElements)
    {
        var currentBounds = currentElement.VisualBounds;
        var currentCenter = new Point(
            currentBounds.X + currentBounds.Width / 2,
            currentBounds.Y + currentBounds.Height / 2);

        UIElement? bestCandidate = null;
        double bestPrimary = direction switch
        {
            FocusNavigationDirection.Left or FocusNavigationDirection.Up => double.MinValue,
            _ => double.MaxValue
        };
        double bestAlignment = double.MaxValue;

        foreach (var candidate in focusableElements)
        {
            if (candidate == currentElement)
            {
                continue;
            }

            var bounds = candidate.VisualBounds;
            var center = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);

            double primary;
            double alignment;
            bool improves;

            switch (direction)
            {
                case FocusNavigationDirection.Left:
                    primary = center.X;
                    alignment = Math.Abs(center.Y - currentCenter.Y);
                    improves = primary > bestPrimary || (Math.Abs(primary - bestPrimary) < 0.001 && alignment < bestAlignment);
                    break;
                case FocusNavigationDirection.Right:
                    primary = center.X;
                    alignment = Math.Abs(center.Y - currentCenter.Y);
                    improves = primary < bestPrimary || (Math.Abs(primary - bestPrimary) < 0.001 && alignment < bestAlignment);
                    break;
                case FocusNavigationDirection.Up:
                    primary = center.Y;
                    alignment = Math.Abs(center.X - currentCenter.X);
                    improves = primary > bestPrimary || (Math.Abs(primary - bestPrimary) < 0.001 && alignment < bestAlignment);
                    break;
                case FocusNavigationDirection.Down:
                    primary = center.Y;
                    alignment = Math.Abs(center.X - currentCenter.X);
                    improves = primary < bestPrimary || (Math.Abs(primary - bestPrimary) < 0.001 && alignment < bestAlignment);
                    break;
                default:
                    return null;
            }

            if (!improves)
            {
                continue;
            }

            bestCandidate = candidate;
            bestPrimary = primary;
            bestAlignment = alignment;
        }

        return bestCandidate;
    }

    private static bool FocusElement(UIElement element)
    {
        if (element.Focusable && element.IsEnabled)
        {
            return element.Focus();
        }
        return false;
    }

    private readonly record struct NavigationContext(UIElement ScopeRoot, bool Wraps);

    private enum NavigationPropertyKind
    {
        Tab,
        Directional
    }

    #endregion
}

/// <summary>
/// Specifies the keyboard navigation mode.
/// </summary>
public enum KeyboardNavigationMode
{
    /// <summary>
    /// Tab navigation continues to the next element.
    /// </summary>
    Continue,

    /// <summary>
    /// Tab navigation cycles within the container.
    /// </summary>
    Cycle,

    /// <summary>
    /// Tab navigation stops when reaching the last element.
    /// </summary>
    Once,

    /// <summary>
    /// Tab navigation is contained within the element.
    /// </summary>
    Contained,

    /// <summary>
    /// Tab navigation is locally contained.
    /// </summary>
    Local,

    /// <summary>
    /// No tab navigation.
    /// </summary>
    None
}

// FocusNavigationDirection enum is defined in Jalium.UI.Core

namespace Jalium.UI;

/// <summary>
/// Provides methods for managing focus within the application.
/// </summary>
public static class FocusManager
{
    #region Dependency Properties (Attached)

    /// <summary>
    /// Identifies the IsFocusScope attached property.
    /// </summary>
    public static readonly DependencyProperty IsFocusScopeProperty =
        DependencyProperty.RegisterAttached("IsFocusScope", typeof(bool), typeof(FocusManager),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the FocusedElement attached property.
    /// </summary>
    public static readonly DependencyProperty FocusedElementProperty =
        DependencyProperty.RegisterAttached("FocusedElement", typeof(IInputElement), typeof(FocusManager),
            new PropertyMetadata(null, OnFocusedElementChanged));

    /// <summary>
    /// Gets the IsFocusScope value for an element.
    /// </summary>
    public static bool GetIsFocusScope(DependencyObject element) =>
        (bool)(element.GetValue(IsFocusScopeProperty) ?? false);

    /// <summary>
    /// Sets the IsFocusScope value for an element.
    /// </summary>
    public static void SetIsFocusScope(DependencyObject element, bool value) =>
        element.SetValue(IsFocusScopeProperty, value);

    /// <summary>
    /// Gets the focused element within the specified focus scope.
    /// </summary>
    public static IInputElement? GetFocusedElement(DependencyObject element) =>
        (IInputElement?)element.GetValue(FocusedElementProperty);

    /// <summary>
    /// Sets the focused element within the specified focus scope.
    /// </summary>
    public static void SetFocusedElement(DependencyObject element, IInputElement? value) =>
        element.SetValue(FocusedElementProperty, value);

    #endregion

    #region Routed Events

    /// <summary>
    /// Identifies the GotFocus routed event.
    /// </summary>
    public static readonly RoutedEvent GotFocusEvent =
        EventManager.RegisterRoutedEvent("GotFocus", RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(FocusManager));

    /// <summary>
    /// Identifies the LostFocus routed event.
    /// </summary>
    public static readonly RoutedEvent LostFocusEvent =
        EventManager.RegisterRoutedEvent("LostFocus", RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(FocusManager));

    /// <summary>
    /// Adds a handler for the GotFocus event.
    /// </summary>
    public static void AddGotFocusHandler(DependencyObject element, RoutedEventHandler handler)
    {
        if (element is UIElement uiElement)
        {
            uiElement.AddHandler(GotFocusEvent, handler);
        }
    }

    /// <summary>
    /// Removes a handler for the GotFocus event.
    /// </summary>
    public static void RemoveGotFocusHandler(DependencyObject element, RoutedEventHandler handler)
    {
        if (element is UIElement uiElement)
        {
            uiElement.RemoveHandler(GotFocusEvent, handler);
        }
    }

    /// <summary>
    /// Adds a handler for the LostFocus event.
    /// </summary>
    public static void AddLostFocusHandler(DependencyObject element, RoutedEventHandler handler)
    {
        if (element is UIElement uiElement)
        {
            uiElement.AddHandler(LostFocusEvent, handler);
        }
    }

    /// <summary>
    /// Removes a handler for the LostFocus event.
    /// </summary>
    public static void RemoveLostFocusHandler(DependencyObject element, RoutedEventHandler handler)
    {
        if (element is UIElement uiElement)
        {
            uiElement.RemoveHandler(LostFocusEvent, handler);
        }
    }

    #endregion

    #region Focus State

    private static IInputElement? _focusedElement;
    private static readonly Dictionary<DependencyObject, IInputElement?> _scopeFocusedElements = new();

    /// <summary>
    /// Gets the element that currently has keyboard focus.
    /// </summary>
    public static IInputElement? FocusedElement => _focusedElement;

    /// <summary>
    /// Sets focus to the specified element.
    /// </summary>
    /// <param name="element">The element to receive focus.</param>
    /// <returns>True if focus was successfully set.</returns>
    public static bool SetFocus(IInputElement? element)
    {
        if (element == null)
        {
            ClearFocus();
            return true;
        }

        if (element == _focusedElement)
            return true;

        // Check if element can receive focus
        if (element is UIElement uiElement)
        {
            if (!uiElement.Focusable || !uiElement.IsEnabled || uiElement.Visibility != Visibility.Visible)
                return false;
        }

        var oldFocused = _focusedElement;

        // Raise LostFocus on old element
        if (oldFocused is UIElement oldUiElement)
        {
            oldUiElement.RaiseEvent(new RoutedEventArgs(LostFocusEvent, oldUiElement));
            oldUiElement.IsFocused = false;
        }

        _focusedElement = element;

        // Raise GotFocus on new element
        if (element is UIElement newUiElement)
        {
            newUiElement.IsFocused = true;
            newUiElement.RaiseEvent(new RoutedEventArgs(GotFocusEvent, newUiElement));
        }

        // Update focus scope
        var focusScope = GetFocusScope(element as DependencyObject);
        if (focusScope != null)
        {
            SetFocusedElement(focusScope, element);
        }

        return true;
    }

    /// <summary>
    /// Clears focus from all elements.
    /// </summary>
    public static void ClearFocus()
    {
        if (_focusedElement is UIElement oldUiElement)
        {
            oldUiElement.RaiseEvent(new RoutedEventArgs(LostFocusEvent, oldUiElement));
            oldUiElement.IsFocused = false;
        }

        _focusedElement = null;
    }

    /// <summary>
    /// Gets the focus scope for the specified element.
    /// </summary>
    public static DependencyObject? GetFocusScope(DependencyObject? element)
    {
        while (element != null)
        {
            if (GetIsFocusScope(element))
                return element;

            element = (element as FrameworkElement)?.Parent ?? (element as Visual)?.VisualParent;
        }

        return null;
    }

    private static void OnFocusedElementChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is IInputElement newElement)
        {
            _scopeFocusedElements[d] = newElement;
        }
        else
        {
            _scopeFocusedElements.Remove(d);
        }
    }

    #endregion
}

/// <summary>
/// Provides focus navigation methods.
/// </summary>
public static class KeyboardNavigation
{
    #region Attached Properties

    /// <summary>
    /// Identifies the TabIndex attached property.
    /// </summary>
    public static readonly DependencyProperty TabIndexProperty =
        DependencyProperty.RegisterAttached("TabIndex", typeof(int), typeof(KeyboardNavigation),
            new PropertyMetadata(int.MaxValue));

    /// <summary>
    /// Identifies the IsTabStop attached property.
    /// </summary>
    public static readonly DependencyProperty IsTabStopProperty =
        DependencyProperty.RegisterAttached("IsTabStop", typeof(bool), typeof(KeyboardNavigation),
            new PropertyMetadata(true));

    /// <summary>
    /// Identifies the TabNavigation attached property.
    /// </summary>
    public static readonly DependencyProperty TabNavigationProperty =
        DependencyProperty.RegisterAttached("TabNavigation", typeof(KeyboardNavigationMode), typeof(KeyboardNavigation),
            new PropertyMetadata(KeyboardNavigationMode.Continue));

    /// <summary>
    /// Identifies the DirectionalNavigation attached property.
    /// </summary>
    public static readonly DependencyProperty DirectionalNavigationProperty =
        DependencyProperty.RegisterAttached("DirectionalNavigation", typeof(KeyboardNavigationMode), typeof(KeyboardNavigation),
            new PropertyMetadata(KeyboardNavigationMode.Continue));

    /// <summary>
    /// Gets the TabIndex for an element.
    /// </summary>
    public static int GetTabIndex(DependencyObject element) =>
        (int)(element.GetValue(TabIndexProperty) ?? int.MaxValue);

    /// <summary>
    /// Sets the TabIndex for an element.
    /// </summary>
    public static void SetTabIndex(DependencyObject element, int value) =>
        element.SetValue(TabIndexProperty, value);

    /// <summary>
    /// Gets the IsTabStop value for an element.
    /// </summary>
    public static bool GetIsTabStop(DependencyObject element) =>
        (bool)(element.GetValue(IsTabStopProperty) ?? true);

    /// <summary>
    /// Sets the IsTabStop value for an element.
    /// </summary>
    public static void SetIsTabStop(DependencyObject element, bool value) =>
        element.SetValue(IsTabStopProperty, value);

    /// <summary>
    /// Gets the TabNavigation mode for an element.
    /// </summary>
    public static KeyboardNavigationMode GetTabNavigation(DependencyObject element) =>
        (KeyboardNavigationMode)(element.GetValue(TabNavigationProperty) ?? KeyboardNavigationMode.Continue);

    /// <summary>
    /// Sets the TabNavigation mode for an element.
    /// </summary>
    public static void SetTabNavigation(DependencyObject element, KeyboardNavigationMode value) =>
        element.SetValue(TabNavigationProperty, value);

    /// <summary>
    /// Gets the DirectionalNavigation mode for an element.
    /// </summary>
    public static KeyboardNavigationMode GetDirectionalNavigation(DependencyObject element) =>
        (KeyboardNavigationMode)(element.GetValue(DirectionalNavigationProperty) ?? KeyboardNavigationMode.Continue);

    /// <summary>
    /// Sets the DirectionalNavigation mode for an element.
    /// </summary>
    public static void SetDirectionalNavigation(DependencyObject element, KeyboardNavigationMode value) =>
        element.SetValue(DirectionalNavigationProperty, value);

    #endregion

    #region Navigation Methods

    /// <summary>
    /// Moves focus in the specified direction.
    /// </summary>
    /// <param name="request">The navigation request.</param>
    /// <returns>True if focus was moved.</returns>
    public static bool MoveFocus(TraversalRequest request)
    {
        if (FocusManager.FocusedElement is not DependencyObject currentFocus)
            return false;

        var nextElement = FindNextFocusableElement(currentFocus, request.FocusNavigationDirection);
        if (nextElement != null)
        {
            return FocusManager.SetFocus(nextElement);
        }

        return false;
    }

    /// <summary>
    /// Gets the next focusable element.
    /// </summary>
    public static IInputElement? PredictFocus(DependencyObject currentElement, FocusNavigationDirection direction)
    {
        return FindNextFocusableElement(currentElement, direction);
    }

    private static IInputElement? FindNextFocusableElement(DependencyObject current, FocusNavigationDirection direction)
    {
        var focusScope = FocusManager.GetFocusScope(current);
        if (focusScope == null) return null;

        // Get all focusable elements in the scope
        var focusableElements = GetFocusableDescendants(focusScope).ToList();
        if (focusableElements.Count == 0) return null;

        var currentIndex = focusableElements.IndexOf(current as UIElement);

        switch (direction)
        {
            case FocusNavigationDirection.Next:
                return GetNextInTabOrder(focusableElements, currentIndex);

            case FocusNavigationDirection.Previous:
                return GetPreviousInTabOrder(focusableElements, currentIndex);

            case FocusNavigationDirection.First:
                return focusableElements.OrderBy(e => GetTabIndex(e)).FirstOrDefault();

            case FocusNavigationDirection.Last:
                return focusableElements.OrderByDescending(e => GetTabIndex(e)).FirstOrDefault();

            case FocusNavigationDirection.Up:
            case FocusNavigationDirection.Down:
            case FocusNavigationDirection.Left:
            case FocusNavigationDirection.Right:
                return FindDirectionalFocus(current, direction, focusableElements);

            default:
                return null;
        }
    }

    private static IInputElement? GetNextInTabOrder(List<UIElement> elements, int currentIndex)
    {
        if (elements.Count == 0) return null;

        // Sort by tab index
        var sorted = elements.OrderBy(e => GetTabIndex(e)).ThenBy(e => elements.IndexOf(e)).ToList();
        var current = currentIndex >= 0 ? elements[currentIndex] : null;
        var currentSortedIndex = current != null ? sorted.IndexOf(current) : -1;

        var nextIndex = (currentSortedIndex + 1) % sorted.Count;
        return sorted[nextIndex];
    }

    private static IInputElement? GetPreviousInTabOrder(List<UIElement> elements, int currentIndex)
    {
        if (elements.Count == 0) return null;

        var sorted = elements.OrderBy(e => GetTabIndex(e)).ThenBy(e => elements.IndexOf(e)).ToList();
        var current = currentIndex >= 0 ? elements[currentIndex] : null;
        var currentSortedIndex = current != null ? sorted.IndexOf(current) : 0;

        var prevIndex = (currentSortedIndex - 1 + sorted.Count) % sorted.Count;
        return sorted[prevIndex];
    }

    private static IInputElement? FindDirectionalFocus(DependencyObject current, FocusNavigationDirection direction, List<UIElement> candidates)
    {
        if (current is not UIElement currentElement) return null;

        var currentBounds = currentElement.VisualBounds;
        var currentCenter = new Point(currentBounds.X + currentBounds.Width / 2, currentBounds.Y + currentBounds.Height / 2);

        UIElement? bestCandidate = null;
        double bestScore = double.MaxValue;

        foreach (var candidate in candidates)
        {
            if (candidate == currentElement) continue;

            var candidateBounds = candidate.VisualBounds;
            var candidateCenter = new Point(candidateBounds.X + candidateBounds.Width / 2, candidateBounds.Y + candidateBounds.Height / 2);

            var dx = candidateCenter.X - currentCenter.X;
            var dy = candidateCenter.Y - currentCenter.Y;

            bool isInDirection = direction switch
            {
                FocusNavigationDirection.Up => dy < -5,
                FocusNavigationDirection.Down => dy > 5,
                FocusNavigationDirection.Left => dx < -5,
                FocusNavigationDirection.Right => dx > 5,
                _ => false
            };

            if (!isInDirection) continue;

            // Calculate score based on distance and alignment
            var distance = Math.Sqrt(dx * dx + dy * dy);
            var alignment = direction switch
            {
                FocusNavigationDirection.Up or FocusNavigationDirection.Down => Math.Abs(dx),
                FocusNavigationDirection.Left or FocusNavigationDirection.Right => Math.Abs(dy),
                _ => 0
            };

            var score = distance + alignment * 2; // Weight alignment more

            if (score < bestScore)
            {
                bestScore = score;
                bestCandidate = candidate;
            }
        }

        return bestCandidate;
    }

    private static IEnumerable<UIElement> GetFocusableDescendants(DependencyObject root)
    {
        if (root is UIElement element)
        {
            if (element.Focusable && GetIsTabStop(element) && element.IsEnabled && element.Visibility == Visibility.Visible)
            {
                yield return element;
            }

            for (var i = 0; i < element.VisualChildrenCount; i++)
            {
                var child = element.GetVisualChild(i);
                if (child is DependencyObject childDep)
                {
                    foreach (var descendant in GetFocusableDescendants(childDep))
                    {
                        yield return descendant;
                    }
                }
            }
        }
    }

    #endregion
}

/// <summary>
/// Specifies the direction for focus navigation.
/// </summary>
public enum FocusNavigationDirection
{
    /// <summary>
    /// Move to the next element in tab order.
    /// </summary>
    Next,

    /// <summary>
    /// Move to the previous element in tab order.
    /// </summary>
    Previous,

    /// <summary>
    /// Move to the first element.
    /// </summary>
    First,

    /// <summary>
    /// Move to the last element.
    /// </summary>
    Last,

    /// <summary>
    /// Move left.
    /// </summary>
    Left,

    /// <summary>
    /// Move right.
    /// </summary>
    Right,

    /// <summary>
    /// Move up.
    /// </summary>
    Up,

    /// <summary>
    /// Move down.
    /// </summary>
    Down
}

/// <summary>
/// Specifies keyboard navigation mode.
/// </summary>
public enum KeyboardNavigationMode
{
    /// <summary>
    /// Continue navigation to the next element.
    /// </summary>
    Continue,

    /// <summary>
    /// Navigate only once through the container.
    /// </summary>
    Once,

    /// <summary>
    /// Cycle navigation within the container.
    /// </summary>
    Cycle,

    /// <summary>
    /// Do not navigate into the container.
    /// </summary>
    None,

    /// <summary>
    /// Navigation is contained within the element.
    /// </summary>
    Contained,

    /// <summary>
    /// Navigation targets the element, not its children.
    /// </summary>
    Local
}

/// <summary>
/// Represents a focus traversal request.
/// </summary>
public sealed class TraversalRequest
{
    /// <summary>
    /// Gets the focus navigation direction.
    /// </summary>
    public FocusNavigationDirection FocusNavigationDirection { get; }

    /// <summary>
    /// Gets or sets whether to wrap around when reaching the end.
    /// </summary>
    public bool Wrapped { get; set; }

    /// <summary>
    /// Creates a new traversal request.
    /// </summary>
    public TraversalRequest(FocusNavigationDirection focusNavigationDirection)
    {
        FocusNavigationDirection = focusNavigationDirection;
    }
}

/// <summary>
/// Provides access modifier scope methods.
/// </summary>
public static class AccessKeyManager
{
    private static readonly Dictionary<UIElement, Dictionary<string, UIElement>> _accessKeyScopes = new();

    /// <summary>
    /// Registers an access key.
    /// </summary>
    public static void Register(string key, UIElement element)
    {
        var scope = FindAccessKeyScope(element);
        if (!_accessKeyScopes.TryGetValue(scope, out var keys))
        {
            keys = new Dictionary<string, UIElement>(StringComparer.OrdinalIgnoreCase);
            _accessKeyScopes[scope] = keys;
        }

        keys[key] = element;
    }

    /// <summary>
    /// Unregisters an access key.
    /// </summary>
    public static void Unregister(string key, UIElement element)
    {
        var scope = FindAccessKeyScope(element);
        if (_accessKeyScopes.TryGetValue(scope, out var keys))
        {
            keys.Remove(key);
        }
    }

    /// <summary>
    /// Processes an access key.
    /// </summary>
    public static bool ProcessKey(UIElement scope, string key)
    {
        if (_accessKeyScopes.TryGetValue(scope, out var keys) && keys.TryGetValue(key, out var element))
        {
            if (element is UIElement uiElement && uiElement.IsEnabled)
            {
                // Invoke the access key action
                uiElement.RaiseEvent(new AccessKeyEventArgs(AccessKeyPressedEvent, key));
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Identifies the AccessKeyPressed event.
    /// </summary>
    public static readonly RoutedEvent AccessKeyPressedEvent =
        EventManager.RegisterRoutedEvent("AccessKeyPressed", RoutingStrategy.Bubble,
            typeof(AccessKeyEventHandler), typeof(AccessKeyManager));

    private static UIElement FindAccessKeyScope(UIElement element)
    {
        var current = element;
        while (current != null)
        {
            if (FocusManager.GetIsFocusScope(current))
                return current;

            current = (current as FrameworkElement)?.Parent as UIElement;
        }

        return element;
    }
}

/// <summary>
/// Delegate for access key events.
/// </summary>
public delegate void AccessKeyEventHandler(object sender, AccessKeyEventArgs e);

/// <summary>
/// Provides data for access key events.
/// </summary>
public sealed class AccessKeyEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Gets the access key that was pressed.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Gets or sets whether the access key scope should be removed after the event.
    /// </summary>
    public bool IsMultiple { get; set; }

    /// <summary>
    /// Creates a new AccessKeyEventArgs.
    /// </summary>
    public AccessKeyEventArgs(RoutedEvent routedEvent, string key)
        : base(routedEvent)
    {
        Key = key;
    }
}

/// <summary>
/// Represents an input element that can receive focus.
/// </summary>
public interface IInputElement
{
    /// <summary>
    /// Gets whether the element has focus.
    /// </summary>
    bool IsFocused { get; }

    /// <summary>
    /// Gets whether the element has keyboard focus.
    /// </summary>
    bool IsKeyboardFocused { get; }

    /// <summary>
    /// Gets whether the element can receive focus.
    /// </summary>
    bool Focusable { get; }

    /// <summary>
    /// Sets focus to this element.
    /// </summary>
    bool Focus();
}

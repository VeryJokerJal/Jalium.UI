namespace Jalium.UI.Input;

/// <summary>
/// Manages access key (mnemonic) registrations and processes access key input.
/// Access keys allow users to activate controls using Alt+key combinations.
/// </summary>
public sealed class AccessKeyManager
{
    private static readonly AccessKeyManager _current = new();

    // Maps access key characters to their registered target elements (using WeakReferences to avoid leaks)
    private readonly Dictionary<string, List<WeakReference<IInputElement>>> _keyToElements = new(StringComparer.OrdinalIgnoreCase);

    private AccessKeyManager() { }

    /// <summary>
    /// Gets the AccessKeyManager for the current thread.
    /// </summary>
    public static AccessKeyManager Current => _current;

    #region Routed Events

    /// <summary>
    /// Identifies the AccessKeyPressed routed event.
    /// </summary>
    public static readonly RoutedEvent AccessKeyPressedEvent =
        EventManager.RegisterRoutedEvent("AccessKeyPressed", RoutingStrategy.Bubble,
            typeof(AccessKeyPressedEventHandler), typeof(AccessKeyManager));

    #endregion

    #region Public Methods

    /// <summary>
    /// Registers the specified access key for the given element.
    /// </summary>
    /// <param name="key">The access key character to register.</param>
    /// <param name="element">The element to associate with the access key.</param>
    public static void Register(string key, IInputElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentException.ThrowIfNullOrEmpty(key);

        var normalizedKey = NormalizeKey(key);
        var akm = Current;

        lock (akm._keyToElements)
        {
            if (!akm._keyToElements.TryGetValue(normalizedKey, out var elements))
            {
                elements = new List<WeakReference<IInputElement>>(1);
                akm._keyToElements[normalizedKey] = elements;
            }
            else
            {
                // Purge dead references
                PurgeDead(elements, null);
            }

            elements.Add(new WeakReference<IInputElement>(element));
        }
    }

    /// <summary>
    /// Unregisters the specified access key for the given element.
    /// </summary>
    /// <param name="key">The access key character to unregister.</param>
    /// <param name="element">The element to disassociate from the access key.</param>
    public static void Unregister(string key, IInputElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentException.ThrowIfNullOrEmpty(key);

        var normalizedKey = NormalizeKey(key);
        var akm = Current;

        lock (akm._keyToElements)
        {
            if (akm._keyToElements.TryGetValue(normalizedKey, out var elements))
            {
                PurgeDead(elements, element);

                if (elements.Count == 0)
                {
                    akm._keyToElements.Remove(normalizedKey);
                }
            }
        }
    }

    /// <summary>
    /// Determines whether the specified key is registered as an access key.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <returns>true if the key is registered; otherwise, false.</returns>
    public static bool IsKeyRegistered(string key)
    {
        if (string.IsNullOrEmpty(key))
            return false;

        var normalizedKey = NormalizeKey(key);
        var akm = Current;

        lock (akm._keyToElements)
        {
            if (akm._keyToElements.TryGetValue(normalizedKey, out var elements))
            {
                PurgeDead(elements, null);
                return elements.Count > 0;
            }
        }

        return false;
    }

    /// <summary>
    /// Processes the specified access key.
    /// </summary>
    /// <param name="key">The access key to process.</param>
    /// <param name="isMultiple">true if there are multiple elements registered for this key.</param>
    /// <returns>true if the key was processed; otherwise, false.</returns>
    public static bool ProcessKey(string key, bool isMultiple)
    {
        if (string.IsNullOrEmpty(key))
            return false;

        var normalizedKey = NormalizeKey(key);
        var akm = Current;
        IInputElement? target = null;

        lock (akm._keyToElements)
        {
            if (akm._keyToElements.TryGetValue(normalizedKey, out var elements))
            {
                PurgeDead(elements, null);

                if (elements.Count > 0)
                {
                    // Use the first alive element as the target
                    foreach (var weakRef in elements)
                    {
                        if (weakRef.TryGetTarget(out var element))
                        {
                            target = element;
                            break;
                        }
                    }
                }
            }
        }

        if (target is UIElement uiTarget)
        {
            var args = new AccessKeyPressedEventArgs(normalizedKey)
            {
                RoutedEvent = AccessKeyPressedEvent,
                Target = uiTarget,
            };
            uiTarget.RaiseEvent(args);
            return true;
        }

        return false;
    }

    #endregion

    #region Private Helpers

    private static string NormalizeKey(string key)
    {
        return key.ToUpperInvariant();
    }

    /// <summary>
    /// Removes dead WeakReferences and optionally a specific element from the list.
    /// </summary>
    private static void PurgeDead(List<WeakReference<IInputElement>> elements, IInputElement? elementToRemove)
    {
        for (int i = elements.Count - 1; i >= 0; i--)
        {
            if (!elements[i].TryGetTarget(out var target) || target == elementToRemove)
            {
                elements.RemoveAt(i);
            }
        }
    }

    #endregion
}

/// <summary>
/// Provides data for the AccessKeyPressed routed event.
/// </summary>
public sealed class AccessKeyPressedEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AccessKeyPressedEventArgs"/> class.
    /// </summary>
    /// <param name="key">The access key that was pressed.</param>
    public AccessKeyPressedEventArgs(string key)
    {
        Key = key ?? string.Empty;
    }

    /// <summary>
    /// Gets the access key that was pressed.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Gets or sets the target element for the access key.
    /// </summary>
    public UIElement? Target { get; set; }

    /// <summary>
    /// Gets or sets the scope element that limits access key lookup.
    /// </summary>
    public UIElement? Scope { get; set; }
}

/// <summary>
/// Delegate for handling AccessKeyPressed events.
/// </summary>
public delegate void AccessKeyPressedEventHandler(object sender, AccessKeyPressedEventArgs e);

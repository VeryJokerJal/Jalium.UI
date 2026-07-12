namespace Jalium.UI;

/// <summary>
/// Provides weak-event subscriptions for <see cref="UIElement.LostFocus"/>.
/// </summary>
public class LostFocusEventManager : WeakEventManager
{
    private LostFocusEventManager()
    {
    }

    /// <summary>Adds a weak listener for the source's logical focus loss.</summary>
    public static void AddListener(DependencyObject source, IWeakEventListener listener)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(listener);
        CurrentManager.ProtectedAddListener(source, listener);
    }

    /// <summary>Removes a weak listener for the source's logical focus loss.</summary>
    public static void RemoveListener(DependencyObject source, IWeakEventListener listener)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(listener);
        CurrentManager.ProtectedRemoveListener(source, listener);
    }

    /// <summary>Adds a weak handler for the source's logical focus loss.</summary>
    public static void AddHandler(DependencyObject source, EventHandler<RoutedEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(handler);
        CurrentManager.ProtectedAddHandler(source, handler);
    }

    /// <summary>Removes a weak handler for the source's logical focus loss.</summary>
    public static void RemoveHandler(DependencyObject source, EventHandler<RoutedEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(handler);
        CurrentManager.ProtectedRemoveHandler(source, handler);
    }

    /// <inheritdoc />
    protected override ListenerList NewListenerList() => new ListenerList<RoutedEventArgs>();

    /// <inheritdoc />
    protected override void StartListening(object source)
    {
        if (source is not UIElement element)
        {
            throw new ArgumentException(
                "The LostFocus event source must be a UIElement.",
                nameof(source));
        }

        element.LostFocus += OnLostFocus;
    }

    /// <inheritdoc />
    protected override void StopListening(object source)
    {
        if (source is UIElement element)
        {
            element.LostFocus -= OnLostFocus;
        }
    }

    private static LostFocusEventManager CurrentManager
    {
        get
        {
            var managerType = typeof(LostFocusEventManager);
            if (GetCurrentManager(managerType) is not LostFocusEventManager manager)
            {
                manager = new LostFocusEventManager();
                SetCurrentManager(managerType, manager);
            }

            return manager;
        }
    }

    private void OnLostFocus(object sender, RoutedEventArgs e) => DeliverEvent(sender, e);
}

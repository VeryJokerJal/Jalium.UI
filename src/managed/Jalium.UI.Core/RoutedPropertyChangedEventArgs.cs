namespace Jalium.UI;

/// <summary>Provides data for a routed property-value change.</summary>
public class RoutedPropertyChangedEventArgs<T> : RoutedEventArgs
{
    public RoutedPropertyChangedEventArgs(T oldValue, T newValue)
    {
        OldValue = oldValue;
        NewValue = newValue;
    }

    public RoutedPropertyChangedEventArgs(T oldValue, T newValue, RoutedEvent routedEvent)
        : base(routedEvent)
    {
        OldValue = oldValue;
        NewValue = newValue;
    }

    public T OldValue { get; }

    public T NewValue { get; }

    protected override void InvokeEventHandler(Delegate handler, object target)
    {
        if (handler is RoutedPropertyChangedEventHandler<T> typedHandler)
        {
            typedHandler(target, this);
            return;
        }

        base.InvokeEventHandler(handler, target);
    }
}

/// <summary>Represents the method that handles a routed property-value change.</summary>
public delegate void RoutedPropertyChangedEventHandler<T>(
    object sender,
    RoutedPropertyChangedEventArgs<T> e);

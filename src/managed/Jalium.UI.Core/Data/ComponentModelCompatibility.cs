using Jalium.UI;
using Jalium.UI.Data;

namespace System.ComponentModel;

/// <summary>Provides weak-event delivery for <see cref="ICollectionView.CurrentChanged"/>.</summary>
public class CurrentChangedEventManager : WeakEventManager
{
    private static CurrentChangedEventManager CurrentManager
    {
        get
        {
            var manager = (CurrentChangedEventManager?)GetCurrentManager(typeof(CurrentChangedEventManager));
            if (manager is null)
            {
                manager = new CurrentChangedEventManager();
                SetCurrentManager(typeof(CurrentChangedEventManager), manager);
            }

            return manager;
        }
    }

    private CurrentChangedEventManager()
    {
    }

    public static void AddListener(ICollectionView source, IWeakEventListener listener)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(listener);
        CurrentManager.ProtectedAddListener(source, listener);
    }

    public static void RemoveListener(ICollectionView source, IWeakEventListener listener)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(listener);
        CurrentManager.ProtectedRemoveListener(source, listener);
    }

    public static void AddHandler(ICollectionView source, EventHandler<EventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(handler);
        CurrentManager.ProtectedAddHandler(source, handler);
    }

    public static void RemoveHandler(ICollectionView source, EventHandler<EventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(handler);
        CurrentManager.ProtectedRemoveHandler(source, handler);
    }

    protected override ListenerList NewListenerList() => new ListenerList<EventArgs>();

    protected override void StartListening(object source) =>
        ((ICollectionView)source).CurrentChanged += OnCurrentChanged;

    protected override void StopListening(object source) =>
        ((ICollectionView)source).CurrentChanged -= OnCurrentChanged;

    private void OnCurrentChanged(object? sender, EventArgs args) => DeliverEvent(sender!, args);
}

/// <summary>Provides weak-event delivery for <see cref="ICollectionView.CurrentChanging"/>.</summary>
public class CurrentChangingEventManager : WeakEventManager
{
    private static CurrentChangingEventManager CurrentManager
    {
        get
        {
            var manager = (CurrentChangingEventManager?)GetCurrentManager(typeof(CurrentChangingEventManager));
            if (manager is null)
            {
                manager = new CurrentChangingEventManager();
                SetCurrentManager(typeof(CurrentChangingEventManager), manager);
            }

            return manager;
        }
    }

    private CurrentChangingEventManager()
    {
    }

    public static void AddListener(ICollectionView source, IWeakEventListener listener)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(listener);
        CurrentManager.ProtectedAddListener(source, listener);
    }

    public static void RemoveListener(ICollectionView source, IWeakEventListener listener)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(listener);
        CurrentManager.ProtectedRemoveListener(source, listener);
    }

    public static void AddHandler(
        ICollectionView source,
        EventHandler<Jalium.UI.Data.CurrentChangingEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(handler);
        CurrentManager.ProtectedAddHandler(source, handler);
    }

    public static void RemoveHandler(
        ICollectionView source,
        EventHandler<Jalium.UI.Data.CurrentChangingEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(handler);
        CurrentManager.ProtectedRemoveHandler(source, handler);
    }

    protected override ListenerList NewListenerList() =>
        new ListenerList<Jalium.UI.Data.CurrentChangingEventArgs>();

    protected override void StartListening(object source) =>
        ((ICollectionView)source).CurrentChanging += OnCurrentChanging;

    protected override void StopListening(object source) =>
        ((ICollectionView)source).CurrentChanging -= OnCurrentChanging;

    private void OnCurrentChanging(object? sender, Jalium.UI.Data.CurrentChangingEventArgs args) =>
        DeliverEvent(sender!, args);
}

/// <summary>Provides weak-event delivery for <see cref="INotifyDataErrorInfo.ErrorsChanged"/>.</summary>
public class ErrorsChangedEventManager : WeakEventManager
{
    private static ErrorsChangedEventManager CurrentManager
    {
        get
        {
            var manager = (ErrorsChangedEventManager?)GetCurrentManager(typeof(ErrorsChangedEventManager));
            if (manager is null)
            {
                manager = new ErrorsChangedEventManager();
                SetCurrentManager(typeof(ErrorsChangedEventManager), manager);
            }

            return manager;
        }
    }

    private ErrorsChangedEventManager()
    {
    }

    public static void AddHandler(
        INotifyDataErrorInfo source,
        EventHandler<DataErrorsChangedEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(handler);
        CurrentManager.ProtectedAddHandler(source, handler);
    }

    public static void RemoveHandler(
        INotifyDataErrorInfo source,
        EventHandler<DataErrorsChangedEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(handler);
        CurrentManager.ProtectedRemoveHandler(source, handler);
    }

    protected override ListenerList NewListenerList() => new ListenerList<DataErrorsChangedEventArgs>();

    protected override void StartListening(object source) =>
        ((INotifyDataErrorInfo)source).ErrorsChanged += OnErrorsChanged;

    protected override void StopListening(object source) =>
        ((INotifyDataErrorInfo)source).ErrorsChanged -= OnErrorsChanged;

    private void OnErrorsChanged(object? sender, DataErrorsChangedEventArgs args) =>
        DeliverEvent(sender!, args);
}

/// <summary>Specifies which properties a type descriptor should return.</summary>
[Flags]
public enum PropertyFilterOptions
{
    None = 0,
    Invalid = 1,
    SetValues = 2,
    UnsetValues = 4,
    Valid = 8,
    All = Invalid | SetValues | UnsetValues | Valid,
}

/// <summary>Associates a property-descriptor filter with a property or method.</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property)]
public sealed class PropertyFilterAttribute : Attribute
{
    public static readonly PropertyFilterAttribute Default = new(PropertyFilterOptions.All);

    public PropertyFilterAttribute(PropertyFilterOptions filter)
    {
        Filter = filter;
    }

    public PropertyFilterOptions Filter { get; }

    public override bool Equals(object? value) =>
        value is PropertyFilterAttribute attribute && attribute.Filter == Filter;

    public override int GetHashCode() => Filter.GetHashCode();

    public override bool Match(object? value) =>
        value is PropertyFilterAttribute attribute && (Filter & attribute.Filter) == Filter;
}

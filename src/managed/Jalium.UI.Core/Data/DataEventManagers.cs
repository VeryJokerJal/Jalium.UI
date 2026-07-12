using System.Runtime.Serialization;

namespace Jalium.UI.Data;

/// <summary>
/// Provides a WeakEventManager implementation for the DataChanged event.
/// </summary>
public sealed class DataChangedEventManager : WeakEventManager
{
    private static DataChangedEventManager CurrentManager
    {
        get
        {
            var manager = (DataChangedEventManager?)GetCurrentManager(typeof(DataChangedEventManager));
            if (manager == null)
            {
                manager = new DataChangedEventManager();
                SetCurrentManager(typeof(DataChangedEventManager), manager);
            }
            return manager;
        }
    }

    /// <summary>
    /// Adds a handler for the DataChanged event from the specified source.
    /// </summary>
    /// <param name="source">The provider that raises the event.</param>
    /// <param name="handler">The handler to add.</param>
    public static void AddHandler(DataSourceProvider source, EventHandler<EventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(handler);
        CurrentManager.ProtectedAddHandler(source, handler);
    }

    /// <summary>
    /// Removes a handler for the DataChanged event from the specified source.
    /// </summary>
    /// <param name="source">The provider that raises the event.</param>
    /// <param name="handler">The handler to remove.</param>
    public static void RemoveHandler(DataSourceProvider source, EventHandler<EventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(handler);
        CurrentManager.ProtectedRemoveHandler(source, handler);
    }

    /// <summary>
    /// Adds a weak listener for a <see cref="DataSourceProvider.DataChanged"/> event.
    /// </summary>
    public static void AddListener(DataSourceProvider source, IWeakEventListener listener)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(listener);
        CurrentManager.ProtectedAddListener(source, listener);
    }

    /// <summary>
    /// Removes a weak listener for a <see cref="DataSourceProvider.DataChanged"/> event.
    /// </summary>
    public static void RemoveListener(DataSourceProvider source, IWeakEventListener listener)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(listener);
        CurrentManager.ProtectedRemoveListener(source, listener);
    }

    /// <inheritdoc />
    protected override ListenerList NewListenerList() => new ListenerList<EventArgs>();

    /// <inheritdoc />
    protected override void StartListening(object source)
    {
        ((DataSourceProvider)source).DataChanged += OnDataChanged;
    }

    /// <inheritdoc />
    protected override void StopListening(object source)
    {
        ((DataSourceProvider)source).DataChanged -= OnDataChanged;
    }

    private void OnDataChanged(object? sender, EventArgs eventArgs)
    {
        if (sender is not null)
        {
            DeliverEvent(sender, eventArgs);
        }
    }
}

/// <summary>
/// Exception thrown when a binding value is unavailable.
/// </summary>
[Serializable]
public class ValueUnavailableException : SystemException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ValueUnavailableException"/> class.
    /// </summary>
    public ValueUnavailableException() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="ValueUnavailableException"/> class
    /// with the specified error message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public ValueUnavailableException(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="ValueUnavailableException"/> class
    /// with the specified error message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public ValueUnavailableException(string message, Exception innerException) : base(message, innerException) { }

#pragma warning disable SYSLIB0051 // Required for the WPF-compatible exception serialization contract.
    /// <summary>
    /// Initializes a serialized instance of the <see cref="ValueUnavailableException"/> class.
    /// </summary>
    protected ValueUnavailableException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
    }
#pragma warning restore SYSLIB0051
}

/// <summary>
/// Provides data for CollectionRegistering events.
/// </summary>
public sealed class CollectionRegisteringEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CollectionRegisteringEventArgs"/> class.
    /// </summary>
    /// <param name="collection">The collection being registered.</param>
    /// <param name="parent">The parent object, if any.</param>
    public CollectionRegisteringEventArgs(System.Collections.IEnumerable? collection, object? parent = null)
    {
        Collection = collection;
        Parent = parent;
    }

    /// <summary>
    /// Gets the collection being registered.
    /// </summary>
    public System.Collections.IEnumerable? Collection { get; }

    /// <summary>
    /// Gets the parent object of the collection, if any.
    /// </summary>
    public object? Parent { get; }
}

/// <summary>
/// Provides data for CollectionViewRegistering events.
/// </summary>
public sealed class CollectionViewRegisteringEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CollectionViewRegisteringEventArgs"/> class.
    /// </summary>
    /// <param name="collectionView">The collection view being registered.</param>
    public CollectionViewRegisteringEventArgs(CollectionView collectionView)
    {
        CollectionView = collectionView;
    }

    /// <summary>
    /// Gets the collection view being registered.
    /// </summary>
    public CollectionView CollectionView { get; }
}

/// <summary>
/// Attribute that identifies the value conversion for a binding converter.
/// Used to declare the source and target types that a value converter supports.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class ValueConversionAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ValueConversionAttribute"/> class.
    /// </summary>
    /// <param name="sourceType">The type this converter converts from.</param>
    /// <param name="targetType">The type this converter converts to.</param>
    public ValueConversionAttribute(Type sourceType, Type targetType)
    {
        SourceType = sourceType;
        TargetType = targetType;
    }

    /// <summary>
    /// Gets the type this converter converts from.
    /// </summary>
    public Type SourceType { get; }

    /// <summary>
    /// Gets the type this converter converts to.
    /// </summary>
    public Type TargetType { get; }

    /// <summary>
    /// Gets or sets the type of the parameter passed to the converter.
    /// </summary>
    public Type? ParameterType { get; set; }

    /// <inheritdoc />
    public override object TypeId => this;

    /// <inheritdoc />
    public override int GetHashCode() => SourceType.GetHashCode() + TargetType.GetHashCode();
}

/// <summary>
/// Delegate for collection synchronization.
/// Provides a callback mechanism for synchronizing access to a collection across threads.
/// </summary>
/// <param name="collection">The collection being synchronized.</param>
/// <param name="context">The synchronization context object (e.g., a lock object).</param>
/// <param name="accessMethod">The method that accesses the collection.</param>
/// <param name="writeAccess">true if the access is a write operation; false for read.</param>
public delegate void CollectionSynchronizationCallback(
    System.Collections.IEnumerable collection,
    object context,
    Action accessMethod,
    bool writeAccess);

/// <summary>
/// Delegate for filtering exceptions during UpdateSource operations.
/// Returns a replacement value or null to use the default error handling.
/// </summary>
/// <param name="bindExpression">The binding expression that encountered the error.</param>
/// <param name="exception">The exception that occurred.</param>
/// <returns>A replacement value, or null to use default error handling.</returns>
public delegate object? UpdateSourceExceptionFilterCallback(object bindExpression, Exception exception);

using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;
using Jalium.UI.Controls;
using Jalium.UI.Markup;
using Jalium.UI.Threading;

namespace Jalium.UI.Data;

/// <summary>
/// Wraps and creates an object that you can use as a binding source.
/// </summary>
public sealed class ObjectDataProvider : DataSourceProvider
{
    private object? _objectInstance;
    private Type? _objectType;
    private string? _methodName;
    private ParameterCollection? _methodParameters;
    private ParameterCollection? _constructorParameters;
    private bool _isAsynchronous;

    /// <summary>
    /// Gets or sets the object used as the binding source.
    /// </summary>
    public object? ObjectInstance
    {
        get => _objectInstance;
        set
        {
            _objectInstance = value;
            _objectType = null;
            OnPropertyChanged(nameof(ObjectInstance));
            if (!IsRefreshDeferred)
                Refresh();
        }
    }

    /// <summary>
    /// Gets or sets the type of object to create.
    /// </summary>
    public Type? ObjectType
    {
        get => _objectType;
        set
        {
            _objectType = value;
            _objectInstance = null;
            OnPropertyChanged(nameof(ObjectType));
            if (!IsRefreshDeferred)
                Refresh();
        }
    }

    /// <summary>
    /// Gets or sets the name of the method to call.
    /// </summary>
    public string? MethodName
    {
        get => _methodName;
        set
        {
            _methodName = value;
            OnPropertyChanged(nameof(MethodName));
            if (!IsRefreshDeferred)
                Refresh();
        }
    }

    /// <summary>
    /// Gets the list of parameters to pass to the method.
    /// </summary>
    public IList MethodParameters => _methodParameters ??= new ParameterCollection();

    /// <summary>
    /// Gets the list of parameters to pass to the constructor.
    /// </summary>
    public IList ConstructorParameters => _constructorParameters ??= new ParameterCollection();

    /// <summary>
    /// Gets or sets whether to perform object creation and method calls asynchronously.
    /// </summary>
    public bool IsAsynchronous
    {
        get => _isAsynchronous;
        set
        {
            _isAsynchronous = value;
            OnPropertyChanged(nameof(IsAsynchronous));
        }
    }

    /// <summary>Returns whether constructor parameters should be serialized.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeConstructorParameters() => _constructorParameters?.Count > 0;

    /// <summary>Returns whether method parameters should be serialized.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeMethodParameters() => _methodParameters?.Count > 0;

    /// <summary>Returns whether an object instance should be serialized.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeObjectInstance() => _objectInstance != null;

    /// <summary>Returns whether an object type should be serialized.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeObjectType() => _objectType != null;

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Override of DataSourceProvider.BeginQuery which is annotated for reflection-based data providers.")]
    protected override void BeginQuery()
    {
        if (IsAsynchronous)
        {
            Task.Run(() =>
            {
                var result = QueryWorker();
                OnQueryFinished(result);
            });
        }
        else
        {
            var result = QueryWorker();
            OnQueryFinished(result);
        }
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("ObjectDataProvider invokes user methods via reflection on the supplied ObjectType. Application is responsible for keeping the bound type trim-safe via DAM annotations or explicit registrations.")]
    private object? QueryWorker()
    {
        try
        {
            object? instance = _objectInstance;

            // Create instance if needed
            if (instance == null && _objectType != null)
            {
                var ctorParams = _constructorParameters?.ToArray() ?? Array.Empty<object?>();
                instance = Activator.CreateInstance(_objectType, ctorParams);
            }

            // Call method if specified
            if (!string.IsNullOrEmpty(_methodName) && instance != null)
            {
                var type = instance.GetType();
                var methodParams = _methodParameters?.ToArray() ?? Array.Empty<object?>();
                var paramTypes = methodParams.Select(p => p?.GetType() ?? typeof(object)).ToArray();

                var method = type.GetMethod(_methodName, paramTypes);
                method ??= type.GetMethod(_methodName);

                if (method != null)
                {
                    return method.Invoke(instance, methodParams);
                }
            }

            return instance;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}

/// <summary>
/// Enables access to XML data for data binding.
/// </summary>
public class XmlDataProvider : DataSourceProvider, IUriContext
{
    private Uri? _source;
    private XmlDocument? _document;
    private string? _xPath;
    private bool _isAsynchronous = true;
    private Uri? _baseUri;
    private XmlNamespaceManager? _xmlNamespaceManager;
    private readonly IXmlSerializable _xmlSerializer;

    /// <summary>Initializes an XML data provider.</summary>
    public XmlDataProvider()
    {
        _xmlSerializer = new XmlDocumentSerializer(this);
    }

    /// <summary>
    /// Gets or sets the URI of the XML data file.
    /// </summary>
    public Uri? Source
    {
        get => _source;
        set
        {
            _source = value;
            if (value != null)
            {
                bool hadDocument = _document != null;
                _document = null;
                if (hadDocument)
                {
                    OnPropertyChanged(nameof(Document));
                }
            }
            OnPropertyChanged(nameof(Source));
            if (!IsRefreshDeferred)
                Refresh();
        }
    }

    /// <summary>
    /// Gets or sets the XML document directly.
    /// </summary>
    public XmlDocument? Document
    {
        get => _document;
        set
        {
            _document = value;
            bool hadSource = _source != null;
            _source = null;
            if (hadSource)
            {
                OnPropertyChanged(nameof(Source));
            }
            OnPropertyChanged(nameof(Document));
            if (!IsRefreshDeferred)
                Refresh();
        }
    }

    /// <summary>Gets or sets the namespace manager used to evaluate XPath expressions.</summary>
    public XmlNamespaceManager? XmlNamespaceManager
    {
        get => _xmlNamespaceManager;
        set
        {
            if (ReferenceEquals(_xmlNamespaceManager, value))
            {
                return;
            }

            _xmlNamespaceManager = value;
            OnPropertyChanged(nameof(XmlNamespaceManager));
            if (!IsRefreshDeferred)
            {
                Refresh();
            }
        }
    }

    /// <summary>Gets the serializer used for inline XML content.</summary>
    public IXmlSerializable XmlSerializer => _xmlSerializer;

    /// <summary>Gets or sets the URI against which relative sources are resolved.</summary>
    protected virtual Uri? BaseUri
    {
        get => _baseUri;
        set => _baseUri = value;
    }

    Uri? IUriContext.BaseUri
    {
        get => BaseUri;
        set => BaseUri = value;
    }

    /// <summary>
    /// Gets or sets the XPath query used to generate the data collection.
    /// </summary>
    public string? XPath
    {
        get => _xPath;
        set
        {
            _xPath = value;
            OnPropertyChanged(nameof(XPath));
            if (!IsRefreshDeferred)
                Refresh();
        }
    }

    /// <summary>
    /// Gets or sets whether to perform data loading asynchronously.
    /// </summary>
    public bool IsAsynchronous
    {
        get => _isAsynchronous;
        set
        {
            _isAsynchronous = value;
            OnPropertyChanged(nameof(IsAsynchronous));
        }
    }

    /// <inheritdoc />
    protected override void EndInit()
    {
        base.EndInit();
    }

    /// <summary>Returns whether the source URI should be serialized.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeSource() => _source != null;

    /// <summary>Returns whether the XPath expression should be serialized.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeXPath() => !string.IsNullOrEmpty(_xPath);

    /// <summary>Returns whether inline XML should be serialized.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeXmlSerializer() => _document != null && _source == null;

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Override of DataSourceProvider.BeginQuery which is annotated for reflection-based data providers.")]
    protected override void BeginQuery()
    {
        if (IsAsynchronous)
        {
            Task.Run(() =>
            {
                var result = QueryWorker();
                OnQueryFinished(result);
            });
        }
        else
        {
            var result = QueryWorker();
            OnQueryFinished(result);
        }
    }

    private object? QueryWorker()
    {
        try
        {
            XmlDocument? doc = _document;

            // Load from source if needed
            if (doc == null && _source != null)
            {
                Uri resolvedSource = !_source.IsAbsoluteUri && _baseUri != null
                    ? new Uri(_baseUri, _source)
                    : _source;
                doc = new XmlDocument();
                if (resolvedSource.IsFile)
                {
                    doc.Load(resolvedSource.LocalPath);
                }
                else
                {
                    using var client = new System.Net.Http.HttpClient();
                    var content = client.GetStringAsync(resolvedSource).Result;
                    doc.LoadXml(content);
                }
            }

            if (doc == null)
                return null;

            // Apply XPath if specified
            if (!string.IsNullOrEmpty(_xPath))
            {
                return _xmlNamespaceManager == null
                    ? doc.SelectNodes(_xPath)
                    : doc.SelectNodes(_xPath, _xmlNamespaceManager);
            }

            return doc;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private sealed class XmlDocumentSerializer(XmlDataProvider provider) : IXmlSerializable
    {
        public XmlSchema? GetSchema() => null;

        public void ReadXml(XmlReader reader)
        {
            ArgumentNullException.ThrowIfNull(reader);
            XmlDocument document = new();
            XmlNode? node = document.ReadNode(reader);
            if (node is XmlDocument sourceDocument)
            {
                provider.Document = sourceDocument;
                return;
            }

            if (node != null)
            {
                document.AppendChild(node);
            }

            provider.Document = document;
        }

        public void WriteXml(XmlWriter writer)
        {
            ArgumentNullException.ThrowIfNull(writer);
            provider.Document?.DocumentElement?.WriteTo(writer);
        }
    }
}

/// <summary>
/// Base class for data source providers.
/// </summary>
public abstract class DataSourceProvider : INotifyPropertyChanged, ISupportInitialize
{
    private object? _data;
    private Exception? _error;
    private bool _isInitialLoadEnabled = true;
    private int _deferLevel;
    private bool _isInitializing;
    private bool _initialLoadCalled;

    /// <summary>Initializes a provider on the current dispatcher.</summary>
    protected DataSourceProvider()
    {
        Dispatcher = Jalium.UI.Threading.Dispatcher.CurrentDispatcher;
    }

    /// <summary>Gets or sets the dispatcher used to publish query results.</summary>
    protected Jalium.UI.Threading.Dispatcher Dispatcher { get; set; }

    /// <summary>
    /// Occurs when a property value changes.
    /// </summary>
    protected virtual event PropertyChangedEventHandler? PropertyChanged;

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => PropertyChanged += value;
        remove => PropertyChanged -= value;
    }

    /// <summary>
    /// Occurs when the data changes.
    /// </summary>
    public event EventHandler? DataChanged;

    /// <summary>
    /// Gets the underlying data object.
    /// </summary>
    public object? Data
    {
        get => _data;
        private set
        {
            _data = value;
            OnPropertyChanged(nameof(Data));
            DataChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Gets any error that occurred during data retrieval.
    /// </summary>
    public Exception? Error
    {
        get => _error;
        private set
        {
            _error = value;
            OnPropertyChanged(nameof(Error));
        }
    }

    /// <summary>
    /// Gets or sets whether the initial load is enabled.
    /// </summary>
    public bool IsInitialLoadEnabled
    {
        get => _isInitialLoadEnabled;
        set
        {
            _isInitialLoadEnabled = value;
            OnPropertyChanged(nameof(IsInitialLoadEnabled));
        }
    }

    /// <summary>
    /// Gets whether refresh is currently deferred.
    /// </summary>
    protected bool IsRefreshDeferred => _deferLevel > 0;

    /// <summary>
    /// Initiates a refresh of the data source.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "BeginQuery is an opt-in reflective extension point. Only ObjectDataProvider's override reflects on user types, and it already declares that contract at its own BeginQuery/QueryWorker: 'Application is responsible for keeping the bound type trim-safe via DAM annotations or explicit registrations.' XmlDataProvider's override performs no reflection. Refresh is the public driver invoked by property setters; whether reflection actually runs is determined by the concrete provider the consumer chose, so preservation remains the documented consumer responsibility at that boundary rather than a defect of this dispatch site.")]
    public void Refresh()
    {
        if (!IsRefreshDeferred && !_isInitializing)
        {
            BeginQuery();
        }
    }

    /// <summary>Performs the provider's initial query when initial loading is enabled.</summary>
    public void InitialLoad()
    {
        if (_initialLoadCalled || !IsInitialLoadEnabled)
        {
            return;
        }

        _initialLoadCalled = true;
        Refresh();
    }

    /// <summary>
    /// Enters a defer cycle that prevents refresh.
    /// </summary>
    public IDisposable DeferRefresh()
    {
        _deferLevel++;
        return new DeferHelper(this);
    }

    /// <summary>
    /// Called when the query begins. Implementations may use reflection on user-supplied types
    /// (see <see cref="ObjectDataProvider"/>); marked accordingly so callers / overrides can declare AOT contracts.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Some providers (e.g. ObjectDataProvider) reflectively invoke methods on user types.")]
    protected virtual void BeginQuery()
    {
    }

    /// <summary>Begins an initialization batch.</summary>
    protected virtual void BeginInit() => _isInitializing = true;

    /// <summary>Completes an initialization batch and performs the initial load.</summary>
    protected virtual void EndInit()
    {
        _isInitializing = false;
        InitialLoad();
    }

    void ISupportInitialize.BeginInit() => BeginInit();

    void ISupportInitialize.EndInit() => EndInit();

    /// <summary>
    /// Called when the query finishes.
    /// </summary>
    protected void OnQueryFinished(object? result)
    {
        OnQueryFinished(
            result is Exception ? null : result,
            result as Exception,
            null,
            null);
    }

    /// <summary>
    /// Publishes query completion on the provider dispatcher and then invokes optional completion work.
    /// </summary>
    protected virtual void OnQueryFinished(
        object? newData,
        Exception? error,
        DispatcherOperationCallback? completionWork,
        object? callbackArguments)
    {
        void Complete()
        {
            Error = error;
            Data = error == null ? newData : null;
            completionWork?.Invoke(callbackArguments);
        }

        if (Dispatcher.CheckAccess())
        {
            Complete();
        }
        else
        {
            Dispatcher.BeginInvoke(Threading.DispatcherPriority.DataBind, (Action)Complete);
        }
    }

    /// <summary>
    /// Raises the PropertyChanged event.
    /// </summary>
    protected virtual void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        PropertyChanged?.Invoke(this, e);
    }

    /// <summary>Raises <see cref="INotifyPropertyChanged.PropertyChanged"/> for a named property.</summary>
    protected void OnPropertyChanged(string propertyName) =>
        OnPropertyChanged(new PropertyChangedEventArgs(propertyName));

    private void EndDefer()
    {
        _deferLevel--;
        if (_deferLevel == 0)
        {
            Refresh();
        }
    }

    private class DeferHelper : IDisposable
    {
        private readonly DataSourceProvider _provider;
        private bool _disposed;

        public DeferHelper(DataSourceProvider provider)
        {
            _provider = provider;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _provider.EndDefer();
            }
        }
    }
}

/// <summary>
/// A collection of method or constructor parameters.
/// </summary>
public sealed class ParameterCollection : Collection<object?>
{
    /// <summary>
    /// Converts the collection to an array.
    /// </summary>
    public object?[] ToArray() => this.Cast<object?>().ToArray();
}

/// <summary>
/// Enables multiple collections to be treated as a single collection.
/// </summary>
public class CompositeCollection :
    IList,
    INotifyCollectionChanged,
    IWeakEventListener,
    Jalium.UI.ICollectionViewFactory,
    System.ComponentModel.ICollectionViewFactory
{
    private readonly List<object?> _collections;
    private readonly ObservableCollection<object?> _flattenedItems = new();
    private readonly HashSet<INotifyCollectionChanged> _subscribedCollections =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>Initializes an empty composite collection.</summary>
    public CompositeCollection()
        : this(0)
    {
    }

    /// <summary>Initializes an empty composite collection with the requested capacity.</summary>
    public CompositeCollection(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        _collections = new List<object?>(capacity);
    }

    /// <summary>
    /// Occurs when the collection changes.
    /// </summary>
    protected virtual event NotifyCollectionChangedEventHandler? CollectionChanged;

    event NotifyCollectionChangedEventHandler? INotifyCollectionChanged.CollectionChanged
    {
        add => CollectionChanged += value;
        remove => CollectionChanged -= value;
    }

    /// <inheritdoc />
    public object? this[int index]
    {
        get => _collections[index];
        set
        {
            object? oldValue = _collections[index];
            _collections[index] = value;
            ResetSubscriptionsAndFlattenedItems();
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Replace,
                value,
                oldValue,
                index));
        }
    }

    /// <inheritdoc />
    public int Count => _collections.Count;

    /// <inheritdoc />
    public bool IsFixedSize => false;

    /// <inheritdoc />
    public bool IsReadOnly => false;

    /// <inheritdoc />
    public bool IsSynchronized => false;

    /// <inheritdoc />
    public object SyncRoot => _collections;

    /// <summary>
    /// Adds a collection or item to the composite collection.
    /// </summary>
    public int Add(object? value)
    {
        _collections.Add(value);
        int index = _collections.Count - 1;
        ResetSubscriptionsAndFlattenedItems();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, value, index));
        return index;
    }

    /// <inheritdoc />
    public void Clear()
    {
        _collections.Clear();
        ResetSubscriptionsAndFlattenedItems();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    /// <inheritdoc />
    public bool Contains(object? value) => _collections.Contains(value);

    /// <inheritdoc />
    public void CopyTo(Array array, int index) => ((ICollection)_collections).CopyTo(array, index);

    /// <inheritdoc />
    public IEnumerator GetEnumerator() => _collections.GetEnumerator();

    /// <inheritdoc />
    public int IndexOf(object? value) => _collections.IndexOf(value);

    /// <inheritdoc />
    public void Insert(int index, object? value)
    {
        _collections.Insert(index, value);
        ResetSubscriptionsAndFlattenedItems();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, value, index));
    }

    /// <inheritdoc />
    public void Remove(object? value)
    {
        int index = _collections.IndexOf(value);
        if (index >= 0)
        {
            object? removed = _collections[index];
            _collections.RemoveAt(index);
            ResetSubscriptionsAndFlattenedItems();
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, removed, index));
        }
    }

    /// <inheritdoc />
    public void RemoveAt(int index)
    {
        var item = _collections[index];
        _collections.RemoveAt(index);
        ResetSubscriptionsAndFlattenedItems();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item, index));
    }

    private void ResetSubscriptionsAndFlattenedItems()
    {
        foreach (INotifyCollectionChanged source in _subscribedCollections)
        {
            CollectionChangedEventManager.RemoveListener(source, this);
        }

        _subscribedCollections.Clear();
        _flattenedItems.Clear();

        foreach (var item in _collections)
        {
            if (item is CollectionContainer container)
            {
                Subscribe(container);
                if (container.Collection is IEnumerable enumerable)
                {
                    foreach (var child in enumerable)
                    {
                        _flattenedItems.Add(child);
                    }
                }
            }
            else if (item is IEnumerable enumerable and not string)
            {
                if (item is INotifyCollectionChanged observable)
                {
                    Subscribe(observable);
                }

                foreach (var child in enumerable)
                {
                    _flattenedItems.Add(child);
                }
            }
            else
            {
                _flattenedItems.Add(item);
            }
        }
    }

    private void Subscribe(INotifyCollectionChanged source)
    {
        if (_subscribedCollections.Add(source))
        {
            CollectionChangedEventManager.AddListener(source, this);
        }
    }

    /// <summary>Handles weak notifications from contained collections.</summary>
    protected virtual bool ReceiveWeakEvent(Type managerType, object sender, EventArgs e)
    {
        if (managerType == typeof(CollectionChangedEventManager) && e is NotifyCollectionChangedEventArgs)
        {
            ResetSubscriptionsAndFlattenedItems();
            return true;
        }

        return false;
    }

    bool IWeakEventListener.ReceiveWeakEvent(Type managerType, object sender, EventArgs e) =>
        ReceiveWeakEvent(managerType, sender, e);

    private void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        CollectionChanged?.Invoke(this, e);
    }

    System.ComponentModel.ICollectionView System.ComponentModel.ICollectionViewFactory.CreateView() =>
        new CollectionView(_flattenedItems);

    ICollectionView Jalium.UI.ICollectionViewFactory.CreateView() =>
        new CollectionView(_flattenedItems);
}

/// <summary>
/// Holds an existing collection for use in CompositeCollection.
/// </summary>
public class CollectionContainer : DependencyObject, INotifyCollectionChanged, IWeakEventListener
{
    /// <summary>Identifies the <see cref="Collection"/> dependency property.</summary>
    public static readonly DependencyProperty CollectionProperty =
        DependencyProperty.Register(
            nameof(Collection),
            typeof(IEnumerable),
            typeof(CollectionContainer),
            new PropertyMetadata(null, OnCollectionPropertyChanged));

    /// <summary>
    /// Occurs when the collection changes.
    /// </summary>
    protected virtual event NotifyCollectionChangedEventHandler? CollectionChanged;

    event NotifyCollectionChangedEventHandler? INotifyCollectionChanged.CollectionChanged
    {
        add => CollectionChanged += value;
        remove => CollectionChanged -= value;
    }

    /// <summary>
    /// Gets or sets the collection.
    /// </summary>
    public IEnumerable? Collection
    {
        get => (IEnumerable?)GetValue(CollectionProperty);
        set => SetValue(CollectionProperty, value);
    }

    /// <summary>Returns whether the contained collection should be serialized.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeCollection() =>
        !ReferenceEquals(ReadLocalValue(CollectionProperty), DependencyProperty.UnsetValue) && Collection != null;

    /// <summary>Raises a change notification received from the contained collection.</summary>
    protected virtual void OnContainedCollectionChanged(NotifyCollectionChangedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        CollectionChanged?.Invoke(this, args);
    }

    /// <summary>Handles weak notifications from the contained collection.</summary>
    protected virtual bool ReceiveWeakEvent(Type managerType, object sender, EventArgs e)
    {
        if (managerType == typeof(CollectionChangedEventManager) && e is NotifyCollectionChangedEventArgs args)
        {
            OnContainedCollectionChanged(args);
            return true;
        }

        return false;
    }

    bool IWeakEventListener.ReceiveWeakEvent(Type managerType, object sender, EventArgs e) =>
        ReceiveWeakEvent(managerType, sender, e);

    private static void OnCollectionPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        CollectionContainer container = (CollectionContainer)d;
        if (e.OldValue is INotifyCollectionChanged oldCollection)
        {
            CollectionChangedEventManager.RemoveListener(oldCollection, container);
        }

        if (e.NewValue is INotifyCollectionChanged newCollection)
        {
            CollectionChangedEventManager.AddListener(newCollection, container);
        }

        container.OnContainedCollectionChanged(
            new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}

/// <summary>
/// Provides transactional binding group for validation across multiple bindings.
/// </summary>
public sealed class BindingGroup : DependencyObject
{
    private readonly Collection<BindingExpressionBase> _bindingExpressions = new();
    private readonly ArrayList _items = new();
    private readonly List<ValidationError> _validationErrors = new();
    private readonly Dictionary<object, Dictionary<string, object?>> _proposedValues = new();
    private bool _isDirty;
    private DependencyObject? _owner;

    /// <summary>
    /// Gets or sets the name of the binding group.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether the binding group should share proposed values.
    /// </summary>
    public bool SharesProposedValues { get; set; }

    /// <summary>
    /// Gets or sets whether changes can be committed.
    /// </summary>
    public bool CanRestoreValues { get; set; } = true;

    /// <summary>Gets or sets whether validation errors raise routed validation events.</summary>
    public bool NotifyOnValidationError { get; set; }

    /// <summary>Gets or sets whether notify-data-error validation participates in the group.</summary>
    public bool ValidatesOnNotifyDataError { get; set; } = true;

    /// <summary>Gets the element that owns this binding group.</summary>
    public DependencyObject? Owner => _owner;

    /// <summary>
    /// Gets whether the binding group has uncommitted changes.
    /// </summary>
    public bool IsDirty => _isDirty;

    /// <summary>
    /// Gets the binding expressions in this group.
    /// </summary>
    public Collection<BindingExpressionBase> BindingExpressions => _bindingExpressions;

    /// <summary>
    /// Gets the items in this binding group.
    /// </summary>
    public IList Items => _items;

    /// <summary>
    /// Gets the validation rules for this binding group.
    /// </summary>
    public Collection<ValidationRule> ValidationRules { get; } = new();

    /// <summary>Gets whether group validation has produced one or more errors.</summary>
    public bool HasValidationError =>
        _validationErrors.Count > 0 || _bindingExpressions.Any(static expression => expression.HasValidationError);

    /// <summary>Gets the current validation errors.</summary>
    public ReadOnlyCollection<ValidationError> ValidationErrors => _validationErrors
        .Concat(_bindingExpressions.SelectMany(static expression => expression.ValidationErrors))
        .ToList()
        .AsReadOnly();

    /// <summary>
    /// Adds a binding expression to the group.
    /// </summary>
    internal void AddBindingExpression(BindingExpressionBase expression)
    {
        if (!_bindingExpressions.Contains(expression))
        {
            _bindingExpressions.Add(expression);
        }
    }

    /// <summary>
    /// Removes a binding expression from the group.
    /// </summary>
    internal void RemoveBindingExpression(BindingExpressionBase expression)
    {
        _bindingExpressions.Remove(expression);
    }

    /// <summary>
    /// Begins an edit session.
    /// </summary>
    public void BeginEdit()
    {
        foreach (var item in Items)
        {
            if (item is IEditableObject editable)
            {
                editable.BeginEdit();
            }
        }
    }

    /// <summary>
    /// Commits all pending changes.
    /// </summary>
    public bool CommitEdit()
    {
        if (!ValidateWithoutUpdate())
            return false;

        foreach (var item in Items)
        {
            if (item is IEditableObject editable)
            {
                editable.EndEdit();
            }
        }

        _proposedValues.Clear();
        _isDirty = false;
        return true;
    }

    /// <summary>
    /// Cancels all pending changes.
    /// </summary>
    public void CancelEdit()
    {
        foreach (var item in Items)
        {
            if (item is IEditableObject editable)
            {
                editable.CancelEdit();
            }
        }

        _proposedValues.Clear();
        _isDirty = false;
    }

    /// <summary>
    /// Runs all validation rules without updating the source.
    /// </summary>
    public bool ValidateWithoutUpdate()
    {
        _validationErrors.Clear();
        foreach (var rule in ValidationRules)
        {
            var result = rule.Validate(this, System.Globalization.CultureInfo.CurrentCulture, this);
            if (!result.IsValid)
            {
                _validationErrors.Add(new ValidationError(rule, this, result.ErrorContent, null));
            }
        }
        return _validationErrors.Count == 0;
    }

    /// <summary>
    /// Updates all sources in the binding group.
    /// </summary>
    public bool UpdateSources()
    {
        if (!ValidateWithoutUpdate())
            return false;

        foreach (var expression in _bindingExpressions)
        {
            expression.UpdateSource();
        }

        return true;
    }

    /// <summary>
    /// Gets the proposed value for an item's property.
    /// </summary>
    public bool TryGetValue(object item, string propertyName, out object? value)
    {
        if (_proposedValues.TryGetValue(item, out var props) && props.TryGetValue(propertyName, out value))
        {
            return true;
        }
        value = null;
        return false;
    }

    /// <summary>Gets a proposed value, or the current source value when no proposal exists.</summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:RequiresUnreferencedCode",
        Justification = "BindingGroup.GetValue is an explicit runtime data-binding API over consumer model properties.")]
    public object? GetValue(object item, string propertyName)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrEmpty(propertyName);
        if (TryGetValue(item, propertyName, out object? value))
        {
            return value;
        }

        PropertyDescriptor? property = TypeDescriptor.GetProperties(item)[propertyName];
        if (property == null)
        {
            throw new ValueUnavailableException(
                $"Property '{propertyName}' is not available on '{item.GetType().FullName}'.");
        }

        return property.GetValue(item);
    }

    /// <summary>
    /// Sets the proposed value for an item's property.
    /// </summary>
    internal void SetProposedValue(object item, string propertyName, object? value)
    {
        if (!_proposedValues.TryGetValue(item, out var props))
        {
            props = new Dictionary<string, object?>();
            _proposedValues[item] = props;
        }
        props[propertyName] = value;
        _isDirty = true;
    }

    internal void SetOwner(DependencyObject? owner)
    {
        if (Owner != null && owner != null && !ReferenceEquals(Owner, owner))
        {
            throw new InvalidOperationException("A BindingGroup cannot be assigned to more than one owner.");
        }

        _owner = owner;
    }
}


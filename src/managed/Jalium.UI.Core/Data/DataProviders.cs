using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;
using Jalium.UI.Controls;

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
    public ParameterCollection MethodParameters => _methodParameters ??= new ParameterCollection();

    /// <summary>
    /// Gets the list of parameters to pass to the constructor.
    /// </summary>
    public ParameterCollection ConstructorParameters => _constructorParameters ??= new ParameterCollection();

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

    /// <inheritdoc />
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
public sealed class XmlDataProvider : DataSourceProvider
{
    private Uri? _source;
    private XDocument? _document;
    private string? _xPath;
    private bool _isAsynchronous = true;

    /// <summary>
    /// Gets or sets the URI of the XML data file.
    /// </summary>
    public Uri? Source
    {
        get => _source;
        set
        {
            _source = value;
            OnPropertyChanged(nameof(Source));
            if (!IsRefreshDeferred)
                Refresh();
        }
    }

    /// <summary>
    /// Gets or sets the XML document directly.
    /// </summary>
    public XDocument? Document
    {
        get => _document;
        set
        {
            _document = value;
            _source = null;
            OnPropertyChanged(nameof(Document));
            if (!IsRefreshDeferred)
                Refresh();
        }
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
            XDocument? doc = _document;

            // Load from source if needed
            if (doc == null && _source != null)
            {
                if (_source.IsFile)
                {
                    doc = XDocument.Load(_source.LocalPath);
                }
                else
                {
                    using var client = new System.Net.Http.HttpClient();
                    var content = client.GetStringAsync(_source).Result;
                    doc = XDocument.Parse(content);
                }
            }

            if (doc == null)
                return null;

            // Apply XPath if specified
            if (!string.IsNullOrEmpty(_xPath))
            {
                // Simple XPath support - for full XPath, would need XPathSelectElements
                var elements = doc.Descendants()
                    .Where(e => e.Name.LocalName == _xPath.TrimStart('/').Split('/').Last())
                    .ToList();
                return elements;
            }

            return doc;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}

/// <summary>
/// Base class for data source providers.
/// </summary>
public abstract class DataSourceProvider : INotifyPropertyChanged
{
    private object? _data;
    private Exception? _error;
    private bool _isInitialLoadEnabled = true;
    private int _deferLevel;

    /// <summary>
    /// Occurs when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

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
    public void Refresh()
    {
        if (!IsRefreshDeferred)
        {
            BeginQuery();
        }
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
    /// Called when the query begins.
    /// </summary>
    protected abstract void BeginQuery();

    /// <summary>
    /// Called when the query finishes.
    /// </summary>
    protected void OnQueryFinished(object? result)
    {
        if (result is Exception ex)
        {
            Error = ex;
            Data = null;
        }
        else
        {
            Error = null;
            Data = result;
        }
    }

    /// <summary>
    /// Raises the PropertyChanged event.
    /// </summary>
    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

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
    public object?[] ToArray() => this.ToArray();
}

/// <summary>
/// Enables multiple collections to be treated as a single collection.
/// </summary>
public sealed class CompositeCollection : IList, INotifyCollectionChanged
{
    private readonly List<object?> _collections = new();
    private readonly List<object?> _flattenedItems = new();

    /// <summary>
    /// Occurs when the collection changes.
    /// </summary>
    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    /// <inheritdoc />
    public object? this[int index]
    {
        get => _flattenedItems[index];
        set => throw new NotSupportedException("Cannot set items directly in CompositeCollection.");
    }

    /// <inheritdoc />
    public int Count => _flattenedItems.Count;

    /// <inheritdoc />
    public bool IsFixedSize => false;

    /// <inheritdoc />
    public bool IsReadOnly => true;

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
        RefreshFlattenedList();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        return _collections.Count - 1;
    }

    /// <inheritdoc />
    public void Clear()
    {
        foreach (var collection in _collections)
        {
            if (collection is INotifyCollectionChanged observable)
            {
                observable.CollectionChanged -= OnChildCollectionChanged;
            }
        }
        _collections.Clear();
        _flattenedItems.Clear();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    /// <inheritdoc />
    public bool Contains(object? value) => _flattenedItems.Contains(value);

    /// <inheritdoc />
    public void CopyTo(Array array, int index) => ((ICollection)_flattenedItems).CopyTo(array, index);

    /// <inheritdoc />
    public IEnumerator GetEnumerator() => _flattenedItems.GetEnumerator();

    /// <inheritdoc />
    public int IndexOf(object? value) => _flattenedItems.IndexOf(value);

    /// <inheritdoc />
    public void Insert(int index, object? value)
    {
        _collections.Insert(index, value);
        RefreshFlattenedList();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    /// <inheritdoc />
    public void Remove(object? value)
    {
        if (_collections.Remove(value))
        {
            if (value is INotifyCollectionChanged observable)
            {
                observable.CollectionChanged -= OnChildCollectionChanged;
            }
            RefreshFlattenedList();
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }

    /// <inheritdoc />
    public void RemoveAt(int index)
    {
        var item = _collections[index];
        if (item is INotifyCollectionChanged observable)
        {
            observable.CollectionChanged -= OnChildCollectionChanged;
        }
        _collections.RemoveAt(index);
        RefreshFlattenedList();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    private void RefreshFlattenedList()
    {
        _flattenedItems.Clear();

        foreach (var item in _collections)
        {
            if (item is CollectionContainer container)
            {
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

    private void OnChildCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshFlattenedList();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    private void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        CollectionChanged?.Invoke(this, e);
    }
}

/// <summary>
/// Holds an existing collection for use in CompositeCollection.
/// </summary>
public sealed class CollectionContainer : INotifyCollectionChanged
{
    private IEnumerable? _collection;

    /// <summary>
    /// Occurs when the collection changes.
    /// </summary>
    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    /// <summary>
    /// Gets or sets the collection.
    /// </summary>
    public IEnumerable? Collection
    {
        get => _collection;
        set
        {
            if (_collection is INotifyCollectionChanged oldObservable)
            {
                oldObservable.CollectionChanged -= OnCollectionChanged;
            }

            _collection = value;

            if (_collection is INotifyCollectionChanged newObservable)
            {
                newObservable.CollectionChanged += OnCollectionChanged;
            }

            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        CollectionChanged?.Invoke(this, e);
    }
}

/// <summary>
/// Provides transactional binding group for validation across multiple bindings.
/// </summary>
public sealed class BindingGroup : DependencyObject
{
    private readonly List<BindingExpressionBase> _bindingExpressions = new();
    private readonly Dictionary<object, Dictionary<string, object?>> _proposedValues = new();
    private bool _isDirty;

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

    /// <summary>
    /// Gets whether the binding group has uncommitted changes.
    /// </summary>
    public bool IsDirty => _isDirty;

    /// <summary>
    /// Gets the binding expressions in this group.
    /// </summary>
    public IReadOnlyList<BindingExpressionBase> BindingExpressions => _bindingExpressions;

    /// <summary>
    /// Gets the items in this binding group.
    /// </summary>
    public IList<object> Items { get; } = new List<object>();

    /// <summary>
    /// Gets the validation rules for this binding group.
    /// </summary>
    public Collection<ValidationRule> ValidationRules { get; } = new();

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
        foreach (var rule in ValidationRules)
        {
            var result = rule.Validate(Items, System.Globalization.CultureInfo.CurrentCulture);
            if (!result.IsValid)
            {
                return false;
            }
        }
        return true;
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
}


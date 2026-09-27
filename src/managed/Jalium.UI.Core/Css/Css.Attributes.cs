using System.Collections;
using System.Collections.Specialized;

namespace Jalium.UI.Styling;

public static partial class Css
{
    public static readonly DependencyProperty AttributesProperty = DependencyProperty.RegisterAttached(
        "Attributes", typeof(IDictionary<string, string>), typeof(Css),
        new PropertyMetadata(null, OnAttributesChanged));

    public static IDictionary<string, string> GetAttributes(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element.GetValue(AttributesProperty) is IDictionary<string, string> attributes) return attributes;
        var collection = new AttributeCollection();
        element.SetValue(AttributesProperty, collection);
        return collection;
    }

    public static void SetAttributes(DependencyObject element, IDictionary<string, string>? attributes)
        => element.SetValue(AttributesProperty, attributes);

    public static void SetAttribute(DependencyObject element, string name, string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var attributes = GetAttributes(element);
        if (value is null) attributes.Remove(name); else attributes[name] = value;
        if (attributes is not INotifyCollectionChanged && element is FrameworkElement or FrameworkContentElement)
            CssEngine.InvalidateSelectorDependents(CssNode.Get(element));
    }

    public static string? GetAttribute(DependencyObject element, string name)
        => element.GetValue(AttributesProperty) is IDictionary<string, string> attributes &&
            attributes.TryGetValue(name, out var value) ? value : null;

    /// <summary>Sets an attribute by expanded name. Namespace names are literal, case-sensitive strings.</summary>
    public static void SetAttribute(DependencyObject element,string namespaceUri,string name,string? value)
    {
        ArgumentNullException.ThrowIfNull(namespaceUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        SetAttribute(element,AttributeKey(namespaceUri,name),value);
    }

    /// <summary>Reads an explicit Css.Attributes value by expanded name.</summary>
    public static string? GetAttribute(DependencyObject element,string namespaceUri,string name)
    {
        ArgumentNullException.ThrowIfNull(namespaceUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return GetAttribute(element,AttributeKey(namespaceUri,name));
    }

    private static string AttributeKey(string namespaceUri,string name)
        => namespaceUri.Length==0 ? name : "\0"+namespaceUri.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)+":"+namespaceUri+name;

    internal static CssExpandedName AttributeName(string key)
    {
        if(key.Length>0 && key[0]=='\0' && key.IndexOf(':') is var colon && colon>1 &&
            int.TryParse(key.AsSpan(1,colon-1),out var length) && length>=0 && length<key.Length-colon)
            return new(key.Substring(colon+1,length),key[(colon+1+length)..]);
        return new(string.Empty,key);
    }

    internal static IEnumerable<(CssExpandedName Name,string Value)> AttributeValues(DependencyObject element,string? namespaceUri,string name)
    {
        if(element.GetValue(AttributesProperty) is not IDictionary<string,string> attributes) yield break;
        foreach(var pair in attributes)
        {
            var expanded=AttributeName(pair.Key);
            if(expanded.LocalName==name && (namespaceUri is null || namespaceUri==expanded.NamespaceUri)) yield return(expanded,pair.Value);
        }
    }

    private static void OnAttributesChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not (FrameworkElement or FrameworkContentElement)) return;
        var element = CssNode.Get(sender);
        var state = CssEngine.EnsureState(element);
        if (args.OldValue is INotifyCollectionChanged old && state.AttributesChanged is { } handler)
            old.CollectionChanged -= handler;
        if (args.NewValue is INotifyCollectionChanged current)
        {
            var weak=new WeakReference<CssNode>(element);
            NotifyCollectionChangedEventHandler? changed=null;
            changed=(_,_)=>
            {
                if(weak.TryGetTarget(out var target)) CssEngine.InvalidateSelectorDependents(target);
                else current.CollectionChanged-=changed;
            };
            state.AttributesChanged=changed;
            current.CollectionChanged+=changed;
        }
        CssEngine.InvalidateSelectorDependents(element);
    }

    private sealed class AttributeCollection : IDictionary<string, string>, INotifyCollectionChanged
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
        public event NotifyCollectionChangedEventHandler? CollectionChanged;
        private void Changed() => CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        public string this[string key]
        {
            get => _values[key];
            set { if (_values.TryGetValue(key, out var old) && old == value) return; _values[key] = value; Changed(); }
        }
        public ICollection<string> Keys => _values.Keys;
        public ICollection<string> Values => _values.Values;
        public int Count => _values.Count;
        public bool IsReadOnly => false;
        public void Add(string key, string value) { _values.Add(key, value); Changed(); }
        public void Add(KeyValuePair<string, string> item) => Add(item.Key, item.Value);
        public bool ContainsKey(string key) => _values.ContainsKey(key);
        public bool Remove(string key) { if (!_values.Remove(key)) return false; Changed(); return true; }
        public bool TryGetValue(string key, out string value) => _values.TryGetValue(key, out value!);
        public void Clear() { if (_values.Count == 0) return; _values.Clear(); Changed(); }
        public bool Contains(KeyValuePair<string, string> item) => ((ICollection<KeyValuePair<string, string>>)_values).Contains(item);
        public void CopyTo(KeyValuePair<string, string>[] array, int index) => ((ICollection<KeyValuePair<string, string>>)_values).CopyTo(array, index);
        public bool Remove(KeyValuePair<string, string> item) => Contains(item) && Remove(item.Key);
        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _values.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

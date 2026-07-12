using System.Collections;
using System.Globalization;
using System.Runtime.Serialization;
using Jalium.UI.Markup;

namespace Jalium.UI.Media;

/// <summary>Specifies whether ClearType is enabled for a rendered subtree.</summary>
public enum ClearTypeHint
{
    Auto = 0,
    Enabled = 1,
}

/// <summary>Specifies the color space used to interpolate gradient colors.</summary>
public enum ColorInterpolationMode
{
    ScRgbLinearInterpolation = 0,
    SRgbLinearInterpolation = 1,
}

/// <summary>Specifies the shape at the end of each dash in a dashed stroke.</summary>
public enum PenDashCap
{
    Flat = 0,
    Round = 2,
    Triangle = 3,
}

/// <summary>Disables automatic DPI-awareness configuration for an assembly.</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class DisableDpiAwarenessAttribute : Attribute
{
}

/// <summary>Indicates that the installed Windows Media Player version is unsupported.</summary>
[Serializable]
public class InvalidWmpVersionException : SystemException
{
    public InvalidWmpVersionException()
    {
    }

    public InvalidWmpVersionException(string? message)
        : base(message)
    {
    }

    public InvalidWmpVersionException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }

#pragma warning disable SYSLIB0051
    protected InvalidWmpVersionException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
    }
#pragma warning restore SYSLIB0051
}

/// <summary>Maps a Unicode range and optional language to a target font family.</summary>
public class FontFamilyMap
{
    private double _scale = 1.0;

    public FontFamilyMap()
    {
    }

    /// <summary>Gets or sets the Unicode range covered by this map.</summary>
    public string? Unicode { get; set; } = "0000-10ffff";

    /// <summary>Gets or sets the target font family name.</summary>
    public string? Target { get; set; }

    /// <summary>Gets or sets the em-size scale applied to the target family.</summary>
    public double Scale
    {
        get => _scale;
        set
        {
            if (double.IsNaN(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Scale cannot be NaN.");
            }

            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Scale must be greater than zero.");
            }

            _scale = value;
        }
    }

    /// <summary>Gets or sets the language to which this map applies.</summary>
    public XmlLanguage? Language { get; set; }
}

/// <summary>Provides the mutable font-family maps associated with a composite font.</summary>
public sealed class FontFamilyMapCollection : IList<FontFamilyMap>, IList
{
    private readonly List<FontFamilyMap> _items = new();

    internal FontFamilyMapCollection()
    {
    }

    public int Count => _items.Count;
    public bool IsReadOnly => false;

    public FontFamilyMap this[int index]
    {
        get => _items[index];
        set => _items[index] = Validate(value);
    }

    object? IList.this[int index]
    {
        get => this[index];
        set => this[index] = Cast(value);
    }

    bool ICollection<FontFamilyMap>.IsReadOnly => false;
    bool IList.IsReadOnly => false;
    bool IList.IsFixedSize => false;
    bool ICollection.IsSynchronized => false;
    object ICollection.SyncRoot => ((ICollection)_items).SyncRoot;

    public void Add(FontFamilyMap item) => _items.Add(Validate(item));

    int IList.Add(object? value)
    {
        Add(Cast(value));
        return Count - 1;
    }

    public void Clear() => _items.Clear();
    public bool Contains(FontFamilyMap item) => _items.Contains(item);
    bool IList.Contains(object? value) => value is FontFamilyMap map && Contains(map);
    public void CopyTo(FontFamilyMap[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
    void ICollection.CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);
    public IEnumerator<FontFamilyMap> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public int IndexOf(FontFamilyMap item) => _items.IndexOf(item);
    int IList.IndexOf(object? value) => value is FontFamilyMap map ? IndexOf(map) : -1;

    public void Insert(int index, FontFamilyMap item) => _items.Insert(index, Validate(item));
    void IList.Insert(int index, object? value) => Insert(index, Cast(value));
    public bool Remove(FontFamilyMap item) => _items.Remove(item);
    void IList.Remove(object? value)
    {
        if (value is FontFamilyMap map)
        {
            Remove(map);
        }
    }

    public void RemoveAt(int index) => _items.RemoveAt(index);

    private static FontFamilyMap Validate(FontFamilyMap item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (string.IsNullOrEmpty(item.Target))
        {
            throw new ArgumentException("Cannot add a font family map whose Target property is not set.", nameof(item));
        }

        return item;
    }

    private static FontFamilyMap Cast(object? value) =>
        value is FontFamilyMap map
            ? map
            : throw new ArgumentException($"Value must be a {nameof(FontFamilyMap)}.", nameof(value));
}

/// <summary>Maps language tags to localized font-family names.</summary>
public sealed class LanguageSpecificStringDictionary : IDictionary<XmlLanguage, string>, IDictionary
{
    private readonly Dictionary<XmlLanguage, string> _items;

    internal LanguageSpecificStringDictionary()
        : this(new Dictionary<XmlLanguage, string>())
    {
    }

    internal LanguageSpecificStringDictionary(IDictionary<XmlLanguage, string> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _items = new Dictionary<XmlLanguage, string>(source);
    }

    public int Count => _items.Count;
    public bool IsReadOnly => false;
    public ICollection<XmlLanguage> Keys => _items.Keys;
    public ICollection<string> Values => _items.Values;

    public string this[XmlLanguage key]
    {
        get => _items[key];
        set
        {
            ArgumentNullException.ThrowIfNull(key);
            _items[key] = value;
        }
    }

    object? IDictionary.this[object key]
    {
        get => key is XmlLanguage language && _items.TryGetValue(language, out string? value) ? value : null;
        set => this[CastKey(key)] = CastValue(value);
    }

    bool ICollection<KeyValuePair<XmlLanguage, string>>.IsReadOnly => false;
    bool IDictionary.IsReadOnly => false;
    bool IDictionary.IsFixedSize => false;
    ICollection IDictionary.Keys => (ICollection)_items.Keys;
    ICollection IDictionary.Values => (ICollection)_items.Values;
    bool ICollection.IsSynchronized => false;
    object ICollection.SyncRoot => ((ICollection)_items).SyncRoot;

    public void Add(XmlLanguage key, string value)
    {
        ArgumentNullException.ThrowIfNull(key);
        _items.Add(key, value);
    }

    public void Add(KeyValuePair<XmlLanguage, string> item) =>
        ((ICollection<KeyValuePair<XmlLanguage, string>>)_items).Add(item);

    void IDictionary.Add(object key, object? value) => Add(CastKey(key), CastValue(value));
    public void Clear() => _items.Clear();
    public bool Contains(KeyValuePair<XmlLanguage, string> item) =>
        ((ICollection<KeyValuePair<XmlLanguage, string>>)_items).Contains(item);
    bool IDictionary.Contains(object key) => key is XmlLanguage language && ContainsKey(language);
    public bool ContainsKey(XmlLanguage key) => _items.ContainsKey(key);
    public void CopyTo(KeyValuePair<XmlLanguage, string>[] array, int arrayIndex) =>
        ((ICollection<KeyValuePair<XmlLanguage, string>>)_items).CopyTo(array, arrayIndex);
    void ICollection.CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);
    public IEnumerator<KeyValuePair<XmlLanguage, string>> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    IDictionaryEnumerator IDictionary.GetEnumerator() => ((IDictionary)_items).GetEnumerator();
    public bool Remove(XmlLanguage key) => _items.Remove(key);
    public bool Remove(KeyValuePair<XmlLanguage, string> item) =>
        ((ICollection<KeyValuePair<XmlLanguage, string>>)_items).Remove(item);
    void IDictionary.Remove(object key)
    {
        if (key is XmlLanguage language)
        {
            Remove(language);
        }
    }

    public bool TryGetValue(XmlLanguage key, out string value) => _items.TryGetValue(key, out value!);

    private static XmlLanguage CastKey(object? key) =>
        key as XmlLanguage ?? throw new ArgumentException($"Key must be a {nameof(XmlLanguage)}.", nameof(key));

    private static string CastValue(object? value) =>
        value as string ?? (value is null ? null! : throw new ArgumentException("Value must be a string.", nameof(value)));
}

/// <summary>Represents an animatable collection of general transforms.</summary>
public sealed class GeneralTransformCollection : AnimatableCollection<GeneralTransform>
{
    public GeneralTransformCollection()
    {
    }

    public GeneralTransformCollection(int capacity)
        : base(capacity)
    {
    }

    public GeneralTransformCollection(IEnumerable<GeneralTransform> collection)
        : base(collection)
    {
    }

    public new GeneralTransformCollection Clone() => (GeneralTransformCollection)base.Clone();
    public new GeneralTransformCollection CloneCurrentValue() => (GeneralTransformCollection)base.CloneCurrentValue();
    public new Enumerator GetEnumerator() => new(this);

    protected override Freezable CreateInstanceCore() => new GeneralTransformCollection();

    public struct Enumerator : IEnumerator<GeneralTransform>
    {
        private IEnumerator<GeneralTransform>? _inner;

        internal Enumerator(GeneralTransformCollection collection)
        {
            _inner = ((IEnumerable<GeneralTransform>)collection).GetEnumerator();
        }

        public GeneralTransform Current =>
            _inner?.Current ?? throw new InvalidOperationException("The enumerator is not positioned on an item.");

        object IEnumerator.Current => Current;
        public bool MoveNext() => _inner?.MoveNext() ?? false;
        public void Reset() => _inner?.Reset();
        public void Dispose()
        {
            _inner?.Dispose();
            _inner = null;
        }
    }
}

/// <summary>Composes a sequence of general transforms.</summary>
public sealed class GeneralTransformGroup : GeneralTransform
{
    public static readonly DependencyProperty ChildrenProperty =
        DependencyProperty.Register(
            nameof(Children),
            typeof(GeneralTransformCollection),
            typeof(GeneralTransformGroup),
            new PropertyMetadata(null));

    public GeneralTransformGroup()
    {
        Children = new GeneralTransformCollection();
    }

    public GeneralTransformCollection Children
    {
        get => (GeneralTransformCollection?)GetValue(ChildrenProperty) ?? new GeneralTransformCollection();
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(Children, value))
            {
                return;
            }

            SetValue(ChildrenProperty, value);
            WritePostscript();
        }
    }

    public override GeneralTransform? Inverse
    {
        get
        {
            var inverse = new GeneralTransformGroup();
            for (int index = Children.Count - 1; index >= 0; index--)
            {
                GeneralTransform? childInverse = Children[index].Inverse;
                if (childInverse is null)
                {
                    return null;
                }

                inverse.Children.Add(childInverse);
            }

            return inverse;
        }
    }

    public override bool TryTransform(Point inPoint, out Point result)
    {
        result = inPoint;
        foreach (GeneralTransform transform in Children)
        {
            if (!transform.TryTransform(result, out Point transformed))
            {
                return false;
            }

            result = transformed;
        }

        return true;
    }

    public new GeneralTransformGroup Clone() => (GeneralTransformGroup)base.Clone();
    public new GeneralTransformGroup CloneCurrentValue() => (GeneralTransformGroup)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new GeneralTransformGroup();

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (ReferenceEquals(e.Property, ChildrenProperty))
        {
            OnFreezablePropertyChanged(e.OldValue as DependencyObject, e.NewValue as DependencyObject, ChildrenProperty);
        }
    }
}

/// <summary>Converts matrix strings to and from <see cref="Matrix"/> values.</summary>
public sealed class MatrixConverter : System.ComponentModel.TypeConverter
{
    public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override bool CanConvertTo(System.ComponentModel.ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    public override object ConvertFrom(System.ComponentModel.ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
        value is string text ? Matrix.Parse(text) : base.ConvertFrom(context, culture, value)!;

    public override object? ConvertTo(System.ComponentModel.ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType) =>
        destinationType == typeof(string) && value is Matrix matrix
            ? matrix.ToString(culture ?? CultureInfo.CurrentCulture)
            : base.ConvertTo(context, culture, value, destinationType);
}

/// <summary>Converts lists of doubles to and from XAML text.</summary>
public sealed class DoubleCollectionConverter : System.ComponentModel.TypeConverter
{
    public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override bool CanConvertTo(System.ComponentModel.ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    public override object ConvertFrom(System.ComponentModel.ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
        value is string text ? DoubleCollection.Parse(text) : base.ConvertFrom(context, culture, value)!;

    public override object? ConvertTo(System.ComponentModel.ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType) =>
        destinationType == typeof(string) && value is DoubleCollection collection
            ? string.Join(" ", collection.Select(item => item.ToString(culture ?? CultureInfo.CurrentCulture)))
            : base.ConvertTo(context, culture, value, destinationType);
}

/// <summary>Converts affine transforms to and from their matrix representation.</summary>
public sealed class TransformConverter : System.ComponentModel.TypeConverter
{
    public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override bool CanConvertTo(System.ComponentModel.ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    public override object ConvertFrom(System.ComponentModel.ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
        value is string text ? new MatrixTransform(Matrix.Parse(text)) : base.ConvertFrom(context, culture, value)!;

    public override object? ConvertTo(System.ComponentModel.ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType) =>
        destinationType == typeof(string) && value is Transform transform
            ? transform.Value.ToString(culture ?? CultureInfo.CurrentCulture)
            : base.ConvertTo(context, culture, value, destinationType);
}

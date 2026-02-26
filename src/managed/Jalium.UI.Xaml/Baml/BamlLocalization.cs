using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace Jalium.UI.Markup.Localizer;

/// <summary>
/// Errors that may be encountered by BamlLocalizer.
/// </summary>
public enum BamlLocalizerError
{
    /// <summary>
    /// More than one element has the same Uid value.
    /// </summary>
    DuplicateUid,

    /// <summary>
    /// The localized Baml contains more than one reference to the same element.
    /// </summary>
    DuplicateElement,

    /// <summary>
    /// The element's substitution contains incomplete child placeholders.
    /// </summary>
    IncompleteElementPlaceholder,

    /// <summary>
    /// The localization commenting XML does not have the correct format.
    /// </summary>
    InvalidCommentingXml,

    /// <summary>
    /// The localization commenting text contains invalid attributes.
    /// </summary>
    InvalidLocalizationAttributes,

    /// <summary>
    /// The localization commenting text contains invalid comments.
    /// </summary>
    InvalidLocalizationComments,

    /// <summary>
    /// The Uid does not correspond to any element in the Baml.
    /// </summary>
    InvalidUid,

    /// <summary>
    /// Child placeholders mismatch between substitution and source.
    /// </summary>
    MismatchedElements,

    /// <summary>
    /// The substitution to an element's content cannot be parsed as XML.
    /// The substitution will be applied as plain text.
    /// </summary>
    SubstitutionAsPlaintext,

    /// <summary>
    /// A child element does not have a Uid. It cannot be represented as a placeholder
    /// in the parent's content string.
    /// </summary>
    UidMissingOnChildElement,

    /// <summary>
    /// A formatting tag in the substitution is not recognized as a type of element.
    /// </summary>
    UnknownFormattingTag,
}

/// <summary>
/// Localization category of the string values of each localizable resource.
/// </summary>
public enum LocalizationCategory
{
    /// <summary>
    /// None. For items that don't need to have a category.
    /// </summary>
    None = 0,

    /// <summary>
    /// Descriptive text. Use for long pieces of text.
    /// </summary>
    Text,

    /// <summary>
    /// Title text. Use for one line of text.
    /// </summary>
    Title,

    /// <summary>
    /// Label text. Use for short text in labeling controls.
    /// </summary>
    Label,

    /// <summary>
    /// Button. For Button control and similar classes.
    /// </summary>
    Button,

    /// <summary>
    /// CheckBox. For CheckBox, CheckBoxItem and similar classes.
    /// </summary>
    CheckBox,

    /// <summary>
    /// ComboBox. For ComboBox, ComboBoxItem and similar classes.
    /// </summary>
    ComboBox,

    /// <summary>
    /// ListBox. For ListBox, ListBoxItem and similar classes.
    /// </summary>
    ListBox,

    /// <summary>
    /// Menu. For Menu, MenuItem and similar classes.
    /// </summary>
    Menu,

    /// <summary>
    /// RadioButton. For RadioButton, RadioButtonList and similar classes.
    /// </summary>
    RadioButton,

    /// <summary>
    /// ToolTip. For tooltip control and similar classes.
    /// </summary>
    ToolTip,

    /// <summary>
    /// Hyperlink. For hyperlink and similar classes.
    /// </summary>
    Hyperlink,

    /// <summary>
    /// TextFlow. For text panel and panels that can contain text.
    /// </summary>
    TextFlow,

    /// <summary>
    /// XML data.
    /// </summary>
    XmlData,

    /// <summary>
    /// Font related data, font name, font size, etc.
    /// </summary>
    Font,

    /// <summary>
    /// The category inherits from the parent node.
    /// </summary>
    Inherit,

    /// <summary>
    /// "Ignore" indicates that the value in BAML should be treated as if it did not exist.
    /// </summary>
    Ignore,

    /// <summary>
    /// "NeverLocalize" means that content is not localized. Content includes the subtree.
    /// </summary>
    NeverLocalize,
}

/// <summary>
/// Readability of the localized resource.
/// </summary>
public enum Readability
{
    /// <summary>
    /// Readability inherits from parent.
    /// </summary>
    Inherit = 0,

    /// <summary>
    /// Resource is readable.
    /// </summary>
    Readable = 1,

    /// <summary>
    /// Resource is not readable.
    /// </summary>
    Unreadable = 2,
}

/// <summary>
/// Modifiability of the localized resource.
/// </summary>
public enum Modifiability
{
    /// <summary>
    /// Modifiability inherits from parent.
    /// </summary>
    Inherit = 0,

    /// <summary>
    /// Resource is modifiable.
    /// </summary>
    Modifiable = 1,

    /// <summary>
    /// Resource is not modifiable.
    /// </summary>
    Unmodifiable = 2,
}

/// <summary>
/// Specifies the localization preferences for a class or property in BAML.
/// </summary>
[AttributeUsage(
    AttributeTargets.Class |
    AttributeTargets.Property |
    AttributeTargets.Field |
    AttributeTargets.Enum |
    AttributeTargets.Struct,
    AllowMultiple = false,
    Inherited = true)]
public sealed class LocalizabilityAttribute : Attribute
{
    private readonly LocalizationCategory _category;
    private Readability _readability = Readability.Readable;
    private Modifiability _modifiability = Modifiability.Modifiable;

    /// <summary>
    /// Constructs a LocalizabilityAttribute to describe the localizability of a property.
    /// </summary>
    /// <param name="category">The string category given to the item.</param>
    public LocalizabilityAttribute(LocalizationCategory category)
    {
        _category = category;
    }

    /// <summary>
    /// Gets the string category.
    /// </summary>
    public LocalizationCategory Category => _category;

    /// <summary>
    /// Gets or sets the readability of the attribute's targeted value.
    /// </summary>
    public Readability Readability
    {
        get => _readability;
        set => _readability = value;
    }

    /// <summary>
    /// Gets or sets the modifiability of the attribute's targeted value.
    /// </summary>
    public Modifiability Modifiability
    {
        get => _modifiability;
        set => _modifiability = value;
    }
}

/// <summary>
/// Key to BamlLocalizableResource.
/// </summary>
public class BamlLocalizableResourceKey : IEquatable<BamlLocalizableResourceKey>
{
    private readonly string _uid;
    private readonly string _className;
    private readonly string _propertyName;
    private readonly string? _assemblyName;

    /// <summary>
    /// Constructs a key to the BamlLocalizableResource. The key consists of uid, class name
    /// and property name, which are used to identify a localizable resource in BAML.
    /// </summary>
    /// <param name="uid">The unique id of the element (x:Uid in XAML).</param>
    /// <param name="className">Class name of the localizable resource.</param>
    /// <param name="propertyName">Property name of the localizable resource.</param>
    public BamlLocalizableResourceKey(string uid, string className, string propertyName)
        : this(uid, className, propertyName, null)
    {
    }

    internal BamlLocalizableResourceKey(string uid, string className, string propertyName, string? assemblyName)
    {
        ArgumentNullException.ThrowIfNull(uid);
        ArgumentNullException.ThrowIfNull(className);
        ArgumentNullException.ThrowIfNull(propertyName);

        _uid = uid;
        _className = className;
        _propertyName = propertyName;
        _assemblyName = assemblyName;
    }

    /// <summary>
    /// Gets the id of the element that has the localizable resource.
    /// </summary>
    public string Uid => _uid;

    /// <summary>
    /// Gets the class name of the localizable resource.
    /// </summary>
    public string ClassName => _className;

    /// <summary>
    /// Gets the property name of the localizable resource.
    /// </summary>
    public string PropertyName => _propertyName;

    /// <summary>
    /// Gets the name of the assembly that defines the type of the localizable resource.
    /// </summary>
    public string? AssemblyName => _assemblyName;

    /// <summary>
    /// Compares two BamlLocalizableResourceKey objects.
    /// </summary>
    public bool Equals(BamlLocalizableResourceKey? other)
    {
        if (other is null) return false;
        return _uid == other._uid
            && _className == other._className
            && _propertyName == other._propertyName;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return Equals(obj as BamlLocalizableResourceKey);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return _uid.GetHashCode()
             ^ _className.GetHashCode()
             ^ _propertyName.GetHashCode();
    }
}

/// <summary>
/// Localization resource in BAML.
/// </summary>
public class BamlLocalizableResource
{
    [Flags]
    private enum LocalizationFlags : byte
    {
        Readable = 1,
        Modifiable = 2,
    }

    private string? _content;
    private string? _comments;
    private LocalizationFlags _flags;
    private LocalizationCategory _category;

    /// <summary>
    /// Constructs a new empty BamlLocalizableResource.
    /// </summary>
    public BamlLocalizableResource()
        : this(null, null, LocalizationCategory.None, true, true)
    {
    }

    /// <summary>
    /// Constructs a new BamlLocalizableResource with the specified properties.
    /// </summary>
    public BamlLocalizableResource(
        string? content,
        string? comments,
        LocalizationCategory category,
        bool modifiable,
        bool readable)
    {
        _content = content;
        _comments = comments;
        _category = category;
        Modifiable = modifiable;
        Readable = readable;
    }

    /// <summary>
    /// Gets or sets the localizable value.
    /// </summary>
    public string? Content
    {
        get => _content;
        set => _content = value;
    }

    /// <summary>
    /// Gets or sets the localization comments.
    /// </summary>
    public string? Comments
    {
        get => _comments;
        set => _comments = value;
    }

    /// <summary>
    /// Gets or sets whether the resource is modifiable.
    /// </summary>
    public bool Modifiable
    {
        get => (_flags & LocalizationFlags.Modifiable) > 0;
        set
        {
            if (value) _flags |= LocalizationFlags.Modifiable;
            else _flags &= ~LocalizationFlags.Modifiable;
        }
    }

    /// <summary>
    /// Gets or sets whether the resource is readable for translation.
    /// </summary>
    public bool Readable
    {
        get => (_flags & LocalizationFlags.Readable) > 0;
        set
        {
            if (value) _flags |= LocalizationFlags.Readable;
            else _flags &= ~LocalizationFlags.Readable;
        }
    }

    /// <summary>
    /// Gets or sets the string category of the resource.
    /// </summary>
    public LocalizationCategory Category
    {
        get => _category;
        set => _category = value;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        if (obj is not BamlLocalizableResource other) return false;
        return _content == other._content
            && _comments == other._comments
            && _flags == other._flags
            && _category == other._category;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return (_content == null ? 0 : _content.GetHashCode())
             ^ (_comments == null ? 0 : _comments.GetHashCode())
             ^ (int)_flags
             ^ (int)_category;
    }
}

/// <summary>
/// The localizability information for an element.
/// </summary>
public class ElementLocalizability
{
    private string? _formattingTag;
    private LocalizabilityAttribute? _attribute;

    /// <summary>
    /// Default constructor.
    /// </summary>
    public ElementLocalizability()
    {
    }

    /// <summary>
    /// Constructor with formatting tag and localizability attribute.
    /// </summary>
    /// <param name="formattingTag">Formatting tag; a non-empty value indicates the class is formatted inline.</param>
    /// <param name="attribute">LocalizabilityAttribute for the class.</param>
    public ElementLocalizability(string? formattingTag, LocalizabilityAttribute? attribute)
    {
        _formattingTag = formattingTag;
        _attribute = attribute;
    }

    /// <summary>
    /// Gets or sets the formatting tag.
    /// </summary>
    public string? FormattingTag
    {
        get => _formattingTag;
        set => _formattingTag = value;
    }

    /// <summary>
    /// Gets or sets the LocalizabilityAttribute.
    /// </summary>
    public LocalizabilityAttribute? Attribute
    {
        get => _attribute;
        set => _attribute = value;
    }
}

/// <summary>
/// The EventArgs for the BamlLocalizer.ErrorNotify event.
/// </summary>
public class BamlLocalizerErrorNotifyEventArgs : EventArgs
{
    private readonly BamlLocalizableResourceKey _key;
    private readonly BamlLocalizerError _error;

    /// <summary>
    /// Constructs a new BamlLocalizerErrorNotifyEventArgs.
    /// </summary>
    public BamlLocalizerErrorNotifyEventArgs(BamlLocalizableResourceKey key, BamlLocalizerError error)
    {
        _key = key;
        _error = error;
    }

    /// <summary>
    /// Gets the key of the BamlLocalizableResource related to the error.
    /// </summary>
    public BamlLocalizableResourceKey Key => _key;

    /// <summary>
    /// Gets the error encountered by BamlLocalizer.
    /// </summary>
    public BamlLocalizerError Error => _error;
}

/// <summary>
/// Delegate for the BamlLocalizer.ErrorNotify event.
/// </summary>
public delegate void BamlLocalizerErrorNotifyEventHandler(object sender, BamlLocalizerErrorNotifyEventArgs e);

/// <summary>
/// Abstract base class for BAML localizability resolution.
/// Implemented by BAML localization API clients to provide localizability settings to BAML content.
/// </summary>
public abstract class BamlLocalizabilityResolver
{
    /// <summary>
    /// Obtains the localizability of an element and whether the element can be formatted inline.
    /// Called when extracting localizable resources from BAML.
    /// </summary>
    /// <param name="assembly">Full assembly name.</param>
    /// <param name="className">Full class name.</param>
    /// <returns>ElementLocalizability for the class.</returns>
    public abstract ElementLocalizability GetElementLocalizability(string assembly, string className);

    /// <summary>
    /// Obtains the localizability of a property.
    /// Called when extracting localizable resources from BAML.
    /// </summary>
    /// <param name="assembly">Full assembly name.</param>
    /// <param name="className">Full class name that contains the property definition.</param>
    /// <param name="property">Property name.</param>
    /// <returns>LocalizabilityAttribute for the property.</returns>
    public abstract LocalizabilityAttribute GetPropertyLocalizability(string assembly, string className, string property);

    /// <summary>
    /// Returns the full class name of a formatting tag that hasn't been encountered in BAML.
    /// Called when applying translations to the localized BAML.
    /// </summary>
    /// <param name="formattingTag">Formatting tag name.</param>
    /// <returns>Full name of the class that is formatted inline.</returns>
    public abstract string ResolveFormattingTagToClass(string formattingTag);

    /// <summary>
    /// Returns the full name of the assembly that contains the class definition.
    /// </summary>
    /// <param name="className">Full class name.</param>
    /// <returns>Full name of the assembly containing the class.</returns>
    public abstract string ResolveAssemblyFromClass(string className);
}

/// <summary>
/// Enumerator for BamlLocalizationDictionary.
/// </summary>
public sealed class BamlLocalizationDictionaryEnumerator : IDictionaryEnumerator
{
    private readonly IEnumerator _enumerator;

    internal BamlLocalizationDictionaryEnumerator(IEnumerator enumerator)
    {
        _enumerator = enumerator;
    }

    /// <summary>
    /// Moves to the next entry.
    /// </summary>
    public bool MoveNext() => _enumerator.MoveNext();

    /// <summary>
    /// Resets the enumerator.
    /// </summary>
    public void Reset() => _enumerator.Reset();

    /// <summary>
    /// Gets the current DictionaryEntry.
    /// </summary>
    public DictionaryEntry Entry => (DictionaryEntry)_enumerator.Current;

    /// <summary>
    /// Gets the key.
    /// </summary>
    public BamlLocalizableResourceKey Key => (BamlLocalizableResourceKey)Entry.Key;

    /// <summary>
    /// Gets the value.
    /// </summary>
    public BamlLocalizableResource Value => (BamlLocalizableResource)Entry.Value;

    /// <summary>
    /// Gets the current entry.
    /// </summary>
    public DictionaryEntry Current => Entry;

    object IEnumerator.Current => Current;
    object IDictionaryEnumerator.Key => Key;
    object? IDictionaryEnumerator.Value => Value;
}

/// <summary>
/// Dictionary that contains all localizable resources extracted from a BAML stream.
/// </summary>
public sealed class BamlLocalizationDictionary : IDictionary
{
    private readonly Dictionary<BamlLocalizableResourceKey, BamlLocalizableResource> _dictionary = new();
    private BamlLocalizableResourceKey? _rootElementKey;

    /// <summary>
    /// Constructs an empty BamlLocalizationDictionary.
    /// </summary>
    public BamlLocalizationDictionary()
    {
    }

    /// <summary>
    /// Gets whether the dictionary has a fixed size.
    /// </summary>
    public bool IsFixedSize => false;

    /// <summary>
    /// Gets whether the dictionary is read-only.
    /// </summary>
    public bool IsReadOnly => false;

    /// <summary>
    /// Gets the key to the root element if the root element is localizable; null otherwise.
    /// </summary>
    public BamlLocalizableResourceKey? RootElementKey => _rootElementKey;

    /// <summary>
    /// Gets the collection of keys.
    /// </summary>
    public ICollection Keys => ((IDictionary)_dictionary).Keys;

    /// <summary>
    /// Gets the collection of values.
    /// </summary>
    public ICollection Values => ((IDictionary)_dictionary).Values;

    /// <summary>
    /// Gets the number of localizable resources in the dictionary.
    /// </summary>
    public int Count => _dictionary.Count;

    /// <summary>
    /// Gets or sets a localizable resource by key.
    /// </summary>
    public BamlLocalizableResource this[BamlLocalizableResourceKey key]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(key);
            return _dictionary[key];
        }
        set
        {
            ArgumentNullException.ThrowIfNull(key);
            _dictionary[key] = value;
        }
    }

    /// <summary>
    /// Adds a localizable resource with the provided key.
    /// </summary>
    public void Add(BamlLocalizableResourceKey key, BamlLocalizableResource value)
    {
        ArgumentNullException.ThrowIfNull(key);
        _dictionary.Add(key, value);
    }

    /// <summary>
    /// Removes all resources from the dictionary.
    /// </summary>
    public void Clear() => _dictionary.Clear();

    /// <summary>
    /// Removes the localizable resource with the specified key.
    /// </summary>
    public void Remove(BamlLocalizableResourceKey key) => _dictionary.Remove(key);

    /// <summary>
    /// Determines whether the dictionary contains a resource with the specified key.
    /// </summary>
    public bool Contains(BamlLocalizableResourceKey key) => _dictionary.ContainsKey(key);

    /// <summary>
    /// Returns an enumerator for the dictionary.
    /// </summary>
    public BamlLocalizationDictionaryEnumerator GetEnumerator()
    {
        return new BamlLocalizationDictionaryEnumerator(((IDictionary)_dictionary).GetEnumerator());
    }

    /// <summary>
    /// Copies the dictionary's elements to a one-dimensional Array instance at the specified index.
    /// </summary>
    public void CopyTo(DictionaryEntry[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        ArgumentOutOfRangeException.ThrowIfNegative(arrayIndex);

        if (arrayIndex >= array.Length)
            throw new ArgumentException("arrayIndex is greater than or equal to array length.", nameof(arrayIndex));

        if (Count > (array.Length - arrayIndex))
            throw new ArgumentException("The number of elements exceeds the available array length.", nameof(arrayIndex));

        foreach (var pair in _dictionary)
        {
            array[arrayIndex++] = new DictionaryEntry(pair.Key, pair.Value);
        }
    }

    internal void SetRootElementKey(BamlLocalizableResourceKey key)
    {
        _rootElementKey = key;
    }

    #region IDictionary explicit interface implementation

    bool IDictionary.Contains(object key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return ((IDictionary)_dictionary).Contains(key);
    }

    void IDictionary.Add(object key, object? value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ((IDictionary)_dictionary).Add(key, value);
    }

    void IDictionary.Remove(object key)
    {
        ArgumentNullException.ThrowIfNull(key);
        ((IDictionary)_dictionary).Remove(key);
    }

    object? IDictionary.this[object key]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(key);
            return ((IDictionary)_dictionary)[key];
        }
        set
        {
            ArgumentNullException.ThrowIfNull(key);
            ((IDictionary)_dictionary)[key] = value;
        }
    }

    IDictionaryEnumerator IDictionary.GetEnumerator() => GetEnumerator();

    void ICollection.CopyTo(Array array, int index)
    {
        if (array != null && array.Rank != 1)
            throw new ArgumentException("Array cannot be multidimensional.", nameof(array));

        CopyTo((array as DictionaryEntry[])!, index);
    }

    int ICollection.Count => Count;

    object ICollection.SyncRoot => ((IDictionary)_dictionary).SyncRoot;

    bool ICollection.IsSynchronized => ((IDictionary)_dictionary).IsSynchronized;

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    #endregion
}

/// <summary>
/// BamlLocalizer class localizes a BAML stream.
/// </summary>
/// <remarks>
/// In Jalium.UI, BAML is not used (JALXAML uses its own binary format .uic).
/// This class is provided for WPF API compatibility only.
/// </remarks>
public class BamlLocalizer
{
    private readonly Stream _source;
    private readonly BamlLocalizabilityResolver? _resolver;
    private readonly TextReader? _comments;

    /// <summary>
    /// Constructs a BamlLocalizer with the specified source stream.
    /// </summary>
    /// <param name="source">Source BAML stream to be localized.</param>
    public BamlLocalizer(Stream source)
        : this(source, null)
    {
    }

    /// <summary>
    /// Constructs a BamlLocalizer with the specified source stream and localizability resolver.
    /// </summary>
    /// <param name="source">Source BAML stream to be localized.</param>
    /// <param name="resolver">Localizability resolver implemented by client.</param>
    public BamlLocalizer(Stream source, BamlLocalizabilityResolver? resolver)
        : this(source, resolver, null)
    {
    }

    /// <summary>
    /// Constructs a BamlLocalizer with the specified source stream, resolver, and comments reader.
    /// </summary>
    /// <param name="source">Source BAML stream to be localized.</param>
    /// <param name="resolver">Localizability resolver implemented by client.</param>
    /// <param name="comments">TextReader to read localization comments XML.</param>
    public BamlLocalizer(Stream source, BamlLocalizabilityResolver? resolver, TextReader? comments)
    {
        ArgumentNullException.ThrowIfNull(source);

        _source = source;
        _resolver = resolver;
        _comments = comments;
    }

    /// <summary>
    /// Raised by BamlLocalizer when it encounters any abnormal conditions.
    /// </summary>
    public event BamlLocalizerErrorNotifyEventHandler? ErrorNotify;

    /// <summary>
    /// Extracts localizable resources from the source BAML.
    /// </summary>
    /// <returns>Localizable resources returned in the form of BamlLocalizationDictionary.</returns>
    public BamlLocalizationDictionary ExtractResources()
    {
        // Stub implementation - Jalium.UI uses .uic bundles instead of BAML
        return new BamlLocalizationDictionary();
    }

    /// <summary>
    /// Updates the source BAML into a target stream with all the resource updates applied.
    /// </summary>
    /// <param name="target">Target stream.</param>
    /// <param name="updates">Resource updates to be applied when generating the localized BAML.</param>
    public void UpdateBaml(Stream target, BamlLocalizationDictionary updates)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(updates);

        // Stub implementation - Jalium.UI uses .uic bundles instead of BAML
    }

    /// <summary>
    /// Raises the ErrorNotify event.
    /// </summary>
    protected virtual void OnErrorNotify(BamlLocalizerErrorNotifyEventArgs e)
    {
        ErrorNotify?.Invoke(this, e);
    }
}

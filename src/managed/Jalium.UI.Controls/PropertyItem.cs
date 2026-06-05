using System.ComponentModel;
using System.Reflection;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a single property in a <see cref="PropertyGrid"/>, wrapping a
/// <see cref="PropertyInfo"/> or <see cref="PropertyDescriptor"/> and providing
/// change notification when the value is edited.
/// </summary>
public class PropertyItem : INotifyPropertyChanged
{
    private readonly object _sourceObject;
    private readonly PropertyInfo? _propertyInfo;
    private readonly PropertyDescriptor? _propertyDescriptor;

    /// <summary>
    /// Initializes a new instance from a <see cref="PropertyInfo"/>.
    /// </summary>
    public PropertyItem(object sourceObject, PropertyInfo propertyInfo)
    {
        _sourceObject = sourceObject ?? throw new ArgumentNullException(nameof(sourceObject));
        _propertyInfo = propertyInfo ?? throw new ArgumentNullException(nameof(propertyInfo));

        Name = propertyInfo.Name;
        PropertyType = propertyInfo.PropertyType;

        var displayAttr = propertyInfo.GetCustomAttribute<DisplayNameAttribute>();
        DisplayName = displayAttr != null && !string.IsNullOrEmpty(displayAttr.DisplayName)
            ? displayAttr.DisplayName
            : propertyInfo.Name;

        var categoryAttr = propertyInfo.GetCustomAttribute<CategoryAttribute>();
        Category = categoryAttr?.Category ?? "Misc";

        var descAttr = propertyInfo.GetCustomAttribute<DescriptionAttribute>();
        Description = descAttr?.Description;

        var readOnlyAttr = propertyInfo.GetCustomAttribute<ReadOnlyAttribute>();
        IsReadOnly = readOnlyAttr?.IsReadOnly == true || !propertyInfo.CanWrite;

        IsExpandable = !PropertyType.IsPrimitive
                       && PropertyType != typeof(string)
                       && PropertyType != typeof(decimal)
                       && !PropertyType.IsEnum
                       && PropertyType != typeof(DateTime)
                       && PropertyType != typeof(TimeSpan)
                       && PropertyType != typeof(Guid);

        SubProperties = new List<PropertyItem>();
    }

    /// <summary>
    /// Initializes a new instance from a <see cref="PropertyDescriptor"/>.
    /// </summary>
    public PropertyItem(object sourceObject, PropertyDescriptor propertyDescriptor)
    {
        _sourceObject = sourceObject ?? throw new ArgumentNullException(nameof(sourceObject));
        _propertyDescriptor = propertyDescriptor ?? throw new ArgumentNullException(nameof(propertyDescriptor));

        Name = propertyDescriptor.Name;
        PropertyType = propertyDescriptor.PropertyType;

        DisplayName = !string.IsNullOrEmpty(propertyDescriptor.DisplayName)
            ? propertyDescriptor.DisplayName
            : propertyDescriptor.Name;

        Category = propertyDescriptor.Category ?? "Misc";
        Description = propertyDescriptor.Description;
        IsReadOnly = propertyDescriptor.IsReadOnly;

        IsExpandable = !PropertyType.IsPrimitive
                       && PropertyType != typeof(string)
                       && PropertyType != typeof(decimal)
                       && !PropertyType.IsEnum
                       && PropertyType != typeof(DateTime)
                       && PropertyType != typeof(TimeSpan)
                       && PropertyType != typeof(Guid);

        SubProperties = new List<PropertyItem>();
    }

    /// <summary>
    /// Gets the property name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the display name shown in the property grid.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Gets the category this property belongs to.
    /// </summary>
    public string? Category { get; }

    /// <summary>
    /// Gets the description of the property.
    /// </summary>
    public string? Description { get; }

    /// <summary>
    /// Gets the CLR type of the property.
    /// </summary>
    public Type PropertyType { get; }

    /// <summary>
    /// Gets or sets the current value of the property on the source object.
    /// Setting this writes back to the source object and raises <see cref="PropertyChanged"/>.
    /// </summary>
    public object? Value
    {
        get
        {
            if (_propertyInfo != null)
                return _propertyInfo.GetValue(_sourceObject);
            return _propertyDescriptor?.GetValue(_sourceObject);
        }
        [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2072:RequiresUnreferencedCode",
            Justification = "ConvertValue requires DynamicallyAccessedMemberTypes.All on its targetType. The Type flows here from PropertyInfo.PropertyType / PropertyDescriptor.PropertyType, framework members that cannot carry DAM annotations, so the flow cannot be satisfied statically. PropertyGrid is a developer/inspector control; consumers binding it to user-defined types are responsible for preserving those property types' converters (e.g. via DynamicDependency or a linker descriptor) — the same documented AOT prerequisite already declared on ConvertValue.")]
        set
        {
            if (IsReadOnly)
                return;

            var oldValue = Value;
            if (Equals(oldValue, value))
                return;

            if (_propertyInfo != null)
            {
                var converted = ConvertValue(value, _propertyInfo.PropertyType);
                _propertyInfo.SetValue(_sourceObject, converted);
            }
            else if (_propertyDescriptor != null)
            {
                var converted = ConvertValue(value, _propertyDescriptor.PropertyType);
                _propertyDescriptor.SetValue(_sourceObject, converted);
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
        }
    }

    /// <summary>
    /// Gets a value indicating whether the property is read-only.
    /// </summary>
    public bool IsReadOnly { get; }

    /// <summary>
    /// Gets a value indicating whether this property has a complex type
    /// that can be expanded to show sub-properties.
    /// </summary>
    public bool IsExpandable { get; }

    /// <summary>
    /// Gets or sets the parent property when this is a sub-property.
    /// </summary>
    public PropertyItem? ParentProperty { get; set; }

    /// <summary>
    /// Gets the list of sub-properties for expandable properties.
    /// </summary>
    public List<PropertyItem> SubProperties { get; }

    /// <summary>
    /// Gets the source object that owns this property.
    /// </summary>
    internal object SourceObject => _sourceObject;

    /// <summary>
    /// Gets the underlying <see cref="PropertyInfo"/>, if any.
    /// </summary>
    internal PropertyInfo? PropertyInfo => _propertyInfo;

    /// <summary>
    /// Gets the underlying <see cref="PropertyDescriptor"/>, if any.
    /// </summary>
    internal PropertyDescriptor? PropertyDescriptor => _propertyDescriptor;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Populates <see cref="SubProperties"/> by reflecting on the current value.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075:RequiresUnreferencedCode",
        Justification = "GetProperties is called on currentValue.GetType(); the runtime Type from object.GetType() cannot carry DynamicallyAccessedMembers, so the PublicProperties requirement cannot be satisfied statically. PropertyGrid is a developer/inspector control: consumers that bind it to user-defined types must keep those types' public properties preserved (e.g. via DynamicDependency or a linker descriptor) — a documented prerequisite for using PropertyGrid under AOT, not a defect of this site.")]
    public void BuildSubProperties()
    {
        SubProperties.Clear();

        var currentValue = Value;
        if (currentValue == null || !IsExpandable)
            return;

        var type = currentValue.GetType();
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in properties)
        {
            var browsable = prop.GetCustomAttribute<BrowsableAttribute>();
            if (browsable?.Browsable == false)
                continue;

            if (prop.GetIndexParameters().Length > 0)
                continue;

            var subItem = new PropertyItem(currentValue, prop)
            {
                ParentProperty = this
            };

            SubProperties.Add(subItem);
        }
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "PropertyGrid is a developer/inspector control. Consumers that bind it to user-defined types must keep their property converters preserved (e.g. via DynamicDependency or PreserveCode) — that is a documented prerequisite for using PropertyGrid under AOT, not a defect of this site.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode",
        Justification = "Convert.ChangeType to a runtime-supplied Type may construct Nullable<T>; same opt-in caveat as above.")]
    private static object? ConvertValue(object? value, [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] Type targetType)
    {
        if (value == null)
            return null;

        var valueType = value.GetType();
        if (targetType.IsAssignableFrom(valueType))
            return value;

        if (value is string str)
        {
            var converter = TypeDescriptor.GetConverter(targetType);
            if (converter.CanConvertFrom(typeof(string)))
                return converter.ConvertFromString(str);
        }

        try
        {
            return Convert.ChangeType(value, targetType);
        }
        catch
        {
            return value;
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"{DisplayName} = {Value}";
}

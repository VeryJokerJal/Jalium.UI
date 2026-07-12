using System.Runtime.CompilerServices;

namespace Jalium.UI.Controls;

/// <summary>
/// Specifies the localization attributes for a class or class member.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class LocalizabilityAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LocalizabilityAttribute"/> class with a specified localization category.
    /// </summary>
    public LocalizabilityAttribute(LocalizationCategory category)
    {
        Category = category;
    }

    /// <summary>
    /// Gets the category value of the localization attribute.
    /// </summary>
    public LocalizationCategory Category { get; }

    /// <summary>
    /// Gets or sets the readability setting of the localization attribute's targeted value.
    /// </summary>
    public Readability Readability { get; set; } = Readability.Inherit;

    /// <summary>
    /// Gets or sets the modifiability setting of the localization attribute's targeted value.
    /// </summary>
    public Modifiability Modifiability { get; set; } = Modifiability.Inherit;
}

/// <summary>
/// Specifies the category of a localizable resource.
/// </summary>
public enum LocalizationCategory
{
    None = 0,
    Text = 1,
    Title = 2,
    Label = 3,
    Button = 4,
    CheckBox = 5,
    ComboBox = 6,
    ListBox = 7,
    Menu = 8,
    RadioButton = 9,
    ToolTip = 10,
    Hyperlink = 11,
    TextFlow = 12,
    XmlData = 13,
    Font = 14,
    Inherit = 15,
    Ignore = 16,
    NeverLocalize = 17
}

/// <summary>
/// Specifies the readability value of a localizable resource.
/// </summary>
public enum Readability
{
    Unreadable = 0,
    Readable = 1,
    Inherit = 2
}

/// <summary>
/// Specifies the modifiability of a localizable resource.
/// </summary>
public enum Modifiability
{
    Unmodifiable = 0,
    Modifiable = 1,
    Inherit = 2
}

/// <summary>
/// Provides attached properties for localization.
/// </summary>
public static class Localization
{
    private sealed class LocalizationValue
    {
        public string Value { get; set; } = string.Empty;
    }

    private static readonly ConditionalWeakTable<object, LocalizationValue> CommentsOnObjects = new();
    private static readonly ConditionalWeakTable<object, LocalizationValue> AttributesOnObjects = new();

    /// <summary>
    /// Identifies the Comments attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty CommentsProperty =
        DependencyProperty.RegisterAttached("Comments", typeof(string), typeof(Localization),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// Identifies the Attributes attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty AttributesProperty =
        DependencyProperty.RegisterAttached("Attributes", typeof(string), typeof(Localization),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// Gets the localization comments for the specified element.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static string GetComments(object element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return GetValue(element, CommentsProperty, CommentsOnObjects);
    }

    /// <summary>
    /// Sets the localization comments for the specified element.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static void SetComments(object element, string comments)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(comments);
        SetValue(element, CommentsProperty, CommentsOnObjects, comments);
    }

    /// <summary>
    /// Gets the localization attributes for the specified element.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static string GetAttributes(object element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return GetValue(element, AttributesProperty, AttributesOnObjects);
    }

    /// <summary>
    /// Sets the localization attributes for the specified element.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static void SetAttributes(object element, string attributes)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(attributes);
        SetValue(element, AttributesProperty, AttributesOnObjects, attributes);
    }

    private static string GetValue(
        object element,
        DependencyProperty property,
        ConditionalWeakTable<object, LocalizationValue> values)
    {
        if (element is DependencyObject dependencyObject)
        {
            return (string)(dependencyObject.GetValue(property) ?? string.Empty);
        }

        return values.TryGetValue(element, out LocalizationValue? value)
            ? value.Value
            : string.Empty;
    }

    private static void SetValue(
        object element,
        DependencyProperty property,
        ConditionalWeakTable<object, LocalizationValue> values,
        string value)
    {
        if (element is DependencyObject dependencyObject)
        {
            dependencyObject.SetValue(property, value);
            return;
        }

        values.GetValue(element, static _ => new LocalizationValue()).Value = value;
    }
}

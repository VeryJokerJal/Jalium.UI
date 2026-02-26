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
    /// <summary>
    /// Identifies the Comments attached property.
    /// </summary>
    public static readonly DependencyProperty CommentsProperty =
        DependencyProperty.RegisterAttached("Comments", typeof(string), typeof(Localization),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// Identifies the Attributes attached property.
    /// </summary>
    public static readonly DependencyProperty AttributesProperty =
        DependencyProperty.RegisterAttached("Attributes", typeof(string), typeof(Localization),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// Gets the localization comments for the specified element.
    /// </summary>
    public static string GetComments(DependencyObject element)
    {
        return (string)(element.GetValue(CommentsProperty) ?? string.Empty);
    }

    /// <summary>
    /// Sets the localization comments for the specified element.
    /// </summary>
    public static void SetComments(DependencyObject element, string value)
    {
        element.SetValue(CommentsProperty, value);
    }

    /// <summary>
    /// Gets the localization attributes for the specified element.
    /// </summary>
    public static string GetAttributes(DependencyObject element)
    {
        return (string)(element.GetValue(AttributesProperty) ?? string.Empty);
    }

    /// <summary>
    /// Sets the localization attributes for the specified element.
    /// </summary>
    public static void SetAttributes(DependencyObject element, string value)
    {
        element.SetValue(AttributesProperty, value);
    }
}

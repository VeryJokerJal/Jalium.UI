namespace Jalium.UI;

/// <summary>Specifies the localization category of a resource.</summary>
public enum LocalizationCategory
{
    None = 0,
    Text,
    Title,
    Label,
    Button,
    CheckBox,
    ComboBox,
    ListBox,
    Menu,
    RadioButton,
    ToolTip,
    Hyperlink,
    TextFlow,
    XmlData,
    Font,
    Inherit,
    Ignore,
    NeverLocalize,
}

/// <summary>Specifies whether a localized resource is readable.</summary>
public enum Readability
{
    Inherit = 0,
    Readable = 1,
    Unreadable = 2,
}

/// <summary>Specifies whether a localized resource is modifiable.</summary>
public enum Modifiability
{
    Inherit = 0,
    Modifiable = 1,
    Unmodifiable = 2,
}

/// <summary>Specifies localization preferences for a class or member.</summary>
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
    /// <summary>Initializes an attribute with the specified localization category.</summary>
    public LocalizabilityAttribute(LocalizationCategory category)
    {
        Category = category;
    }

    /// <summary>Gets the localization category.</summary>
    public LocalizationCategory Category { get; }

    /// <summary>Gets or sets whether the value is readable.</summary>
    public Readability Readability { get; set; } = Readability.Readable;

    /// <summary>Gets or sets whether the value is modifiable.</summary>
    public Modifiability Modifiability { get; set; } = Modifiability.Modifiable;
}

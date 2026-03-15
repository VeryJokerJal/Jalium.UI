namespace Jalium.UI;

/// <summary>
/// Categorizes dependency properties for DevTools inspection.
/// </summary>
public enum DevToolsPropertyCategory
{
    Framework,
    Layout,
    Appearance,
    Typography,
    Content,
    Items,
    Data,
    Input,
    Behavior,
    State,
    Other
}

/// <summary>
/// Marks a property, dependency property field, or attached property accessor with a DevTools category.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Method, Inherited = true)]
public sealed class DevToolsPropertyCategoryAttribute : Attribute
{
    public DevToolsPropertyCategoryAttribute(DevToolsPropertyCategory category)
    {
        Category = category;
    }

    /// <summary>
    /// Gets the DevTools category assigned to the annotated member.
    /// </summary>
    public DevToolsPropertyCategory Category { get; }
}

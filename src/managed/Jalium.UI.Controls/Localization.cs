using System.Runtime.CompilerServices;
using Jalium.UI.Controls;

namespace Jalium.UI;

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

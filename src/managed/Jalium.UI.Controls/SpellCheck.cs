namespace Jalium.UI.Controls;

/// <summary>
/// Provides real-time spell-checking functionality to text-editing controls such as TextBox and RichTextBox.
/// </summary>
public sealed class SpellCheck
{
    /// <summary>
    /// Identifies the IsEnabled attached dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(SpellCheck),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the SpellingReform attached dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty SpellingReformProperty =
        DependencyProperty.RegisterAttached("SpellingReform", typeof(SpellingReform), typeof(SpellCheck),
            new PropertyMetadata(SpellingReform.PreAndPostreform));

    /// <summary>
    /// Identifies the CustomDictionaries attached dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty CustomDictionariesProperty =
        DependencyProperty.RegisterAttached("CustomDictionaries", typeof(IList<Uri>), typeof(SpellCheck),
            new PropertyMetadata(null));

    /// <summary>Gets the IsEnabled value from the specified TextBox.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty)!;

    /// <summary>Sets the IsEnabled value on the specified TextBox.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    /// <summary>Gets the SpellingReform value from the specified element.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static SpellingReform GetSpellingReform(DependencyObject element) =>
        (SpellingReform)(element.GetValue(SpellingReformProperty) ?? SpellingReform.PreAndPostreform);

    /// <summary>Sets the SpellingReform value on the specified element.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static void SetSpellingReform(DependencyObject element, SpellingReform value) =>
        element.SetValue(SpellingReformProperty, value);

    /// <summary>Gets the custom dictionaries collection.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static IList<Uri>? GetCustomDictionaries(DependencyObject element) =>
        (IList<Uri>?)element.GetValue(CustomDictionariesProperty);

    /// <summary>Sets the custom dictionaries collection.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static void SetCustomDictionaries(DependencyObject element, IList<Uri>? value) =>
        element.SetValue(CustomDictionariesProperty, value);

    /// <summary>Gets or sets whether spell checking is enabled.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsEnabled { get; set; }
}

/// <summary>
/// Specifies the spelling reform rules used by the spell checker.
/// </summary>
public enum SpellingReform
{
    /// <summary>Use pre-reform and post-reform spellings.</summary>
    PreAndPostreform,

    /// <summary>Use pre-reform spellings only.</summary>
    Prereform,

    /// <summary>Use post-reform spellings only.</summary>
    Postreform
}

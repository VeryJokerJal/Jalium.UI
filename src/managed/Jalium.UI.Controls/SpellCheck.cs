using System.Collections;
using System.Globalization;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Controls;

/// <summary>
/// Provides spell-checking options for a <see cref="TextBoxBase"/>.
/// </summary>
public sealed class SpellCheck
{
    private readonly TextBoxBase _owner;

    internal SpellCheck(TextBoxBase owner)
    {
        _owner = owner;
    }

    /// <summary>
    /// Identifies the IsEnabled attached dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(SpellCheck),
            new PropertyMetadata(false, OnIsEnabledChanged));

    /// <summary>
    /// Identifies the SpellingReform attached dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty SpellingReformProperty =
        DependencyProperty.RegisterAttached(
            "SpellingReform",
            typeof(SpellingReform),
            typeof(SpellCheck),
            new PropertyMetadata(GetDefaultSpellingReform()));

    private static readonly DependencyPropertyKey CustomDictionariesPropertyKey =
        DependencyProperty.RegisterAttachedReadOnly(
            "CustomDictionaries",
            typeof(IList),
            typeof(SpellCheck),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the read-only CustomDictionaries attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty CustomDictionariesProperty =
        CustomDictionariesPropertyKey.DependencyProperty;

    /// <summary>
    /// Gets or sets whether spell checking is enabled for the associated control.
    /// </summary>
    public bool IsEnabled
    {
        get => GetIsEnabled(_owner);
        set => SetIsEnabled(_owner, value);
    }

    /// <summary>
    /// Gets or sets the spelling-reform mode for the associated control.
    /// </summary>
    public SpellingReform SpellingReform
    {
        get => GetSpellingReform(_owner);
        set => SetSpellingReform(_owner, value);
    }

    /// <summary>
    /// Gets the per-control collection of custom dictionary locations.
    /// </summary>
    public IList CustomDictionaries => GetCustomDictionaries(_owner);

    /// <summary>Gets whether spell checking is enabled for a text box.</summary>
    public static bool GetIsEnabled(TextBoxBase textBoxBase)
    {
        ArgumentNullException.ThrowIfNull(textBoxBase);
        return (bool)textBoxBase.GetValue(IsEnabledProperty)!;
    }

    /// <summary>Enables or disables spell checking for a text box.</summary>
    public static void SetIsEnabled(TextBoxBase textBoxBase, bool value)
    {
        ArgumentNullException.ThrowIfNull(textBoxBase);
        textBoxBase.SetValue(IsEnabledProperty, value);
    }

    /// <summary>Gets the spelling-reform mode for a text box.</summary>
    public static SpellingReform GetSpellingReform(TextBoxBase textBoxBase)
    {
        ArgumentNullException.ThrowIfNull(textBoxBase);
        return (SpellingReform)textBoxBase.GetValue(SpellingReformProperty)!;
    }

    /// <summary>Sets the spelling-reform mode for a text box.</summary>
    public static void SetSpellingReform(TextBoxBase textBoxBase, SpellingReform value)
    {
        ArgumentNullException.ThrowIfNull(textBoxBase);
        textBoxBase.SetValue(SpellingReformProperty, value);
    }

    /// <summary>Gets the custom dictionary collection for a text box.</summary>
    public static IList GetCustomDictionaries(TextBoxBase textBoxBase)
    {
        ArgumentNullException.ThrowIfNull(textBoxBase);

        if (textBoxBase.GetValue(CustomDictionariesProperty) is IList dictionaries)
        {
            return dictionaries;
        }

        dictionaries = new List<Uri>();
        textBoxBase.SetValue(CustomDictionariesPropertyKey, dictionaries);
        return dictionaries;
    }

    private static SpellingReform GetDefaultSpellingReform() =>
        string.Equals(
            CultureInfo.CurrentCulture.TwoLetterISOLanguageName,
            "de",
            StringComparison.OrdinalIgnoreCase)
            ? SpellingReform.Postreform
            : SpellingReform.PreAndPostreform;

    private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is TextBox textBox)
        {
            bool isEnabled = (bool)args.NewValue!;
            textBox.OnSpellCheckSettingChanged(isEnabled);
        }
        else if (dependencyObject is RichTextBox richTextBox)
        {
            richTextBox.OnSpellCheckSettingChanged((bool)args.NewValue!);
        }
    }
}

/// <summary>
/// Specifies the spelling-reform rules used by the spell checker.
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

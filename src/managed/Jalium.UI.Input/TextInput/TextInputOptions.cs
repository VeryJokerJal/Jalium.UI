namespace Jalium.UI.Input.TextInput;

/// <summary>
/// Describes the kind of content an editable element accepts. It is primarily
/// used to determine the shape of the on-screen (virtual) keyboard on mobile
/// platforms. The member set mirrors Avalonia's <c>TextInputContentType</c>,
/// with <see cref="Telephone"/> added because Avalonia has no dedicated phone
/// content type while Jalium exposes a distinct phone dial pad.
/// </summary>
public enum TextInputContentType
{
    /// <summary>Default keyboard for the user's configured input method.</summary>
    Normal = 0,

    /// <summary>Keyboard restricted to alphabetic characters.</summary>
    Alpha = 1,

    /// <summary>Numeric keypad capable of digits only (e.g. quantities).</summary>
    Digits = 2,

    /// <summary>Numeric keypad for entering a PIN (masked digits).</summary>
    Pin = 3,

    /// <summary>Numeric keypad including decimal separators and signs (e.g. currency amounts).</summary>
    Number = 4,

    /// <summary>Keyboard optimized for entering email addresses.</summary>
    Email = 5,

    /// <summary>Keyboard optimized for entering URLs.</summary>
    Url = 6,

    /// <summary>Keyboard optimized for entering a person's name.</summary>
    Name = 7,

    /// <summary>Keyboard for entering sensitive password data (input is masked).</summary>
    Password = 8,

    /// <summary>Keyboard suited for #tags and @mentions; may fall back to a normal keyboard.</summary>
    Social = 9,

    /// <summary>Keyboard optimized for entering search keywords.</summary>
    Search = 10,

    /// <summary>
    /// Telephone dial pad (digits plus phone symbols such as +, *, #). Jalium
    /// extension: Avalonia folds phone numbers into <see cref="Digits"/>.
    /// </summary>
    Telephone = 11,
}

/// <summary>
/// Determines what the Return key of the on-screen keyboard reads and how it
/// behaves. The member set mirrors Avalonia's <c>TextInputReturnKeyType</c>.
/// </summary>
public enum TextInputReturnKeyType
{
    /// <summary>Platform default. Jalium infers Next/Done from the focus chain.</summary>
    Default = 0,

    /// <summary>A literal return/newline key.</summary>
    Return = 1,

    /// <summary>A "Done" action that dismisses the keyboard.</summary>
    Done = 2,

    /// <summary>A "Go" action (e.g. navigate).</summary>
    Go = 3,

    /// <summary>A "Send" action (e.g. submit a message).</summary>
    Send = 4,

    /// <summary>A "Search" action.</summary>
    Search = 5,

    /// <summary>A "Next" action that advances focus to the next field.</summary>
    Next = 6,

    /// <summary>A "Previous" action that moves focus to the previous field.</summary>
    Previous = 7,
}

/// <summary>
/// Attached properties that hint the on-screen keyboard about the text an
/// element accepts. The surface mirrors Avalonia's
/// <c>Avalonia.Input.TextInput.TextInputOptions</c> so existing conventions map
/// across directly. On desktop platforms these hints are advisory; on Android
/// they drive the system soft keyboard's input type and Return key.
/// </summary>
public static class TextInputOptions
{
    /// <summary>Identifies the ContentType attached property.</summary>
    public static readonly DependencyProperty ContentTypeProperty =
        DependencyProperty.RegisterAttached(
            "ContentType",
            typeof(TextInputContentType),
            typeof(TextInputOptions),
            new PropertyMetadata(TextInputContentType.Normal));

    /// <summary>Identifies the ReturnKeyType attached property.</summary>
    public static readonly DependencyProperty ReturnKeyTypeProperty =
        DependencyProperty.RegisterAttached(
            "ReturnKeyType",
            typeof(TextInputReturnKeyType),
            typeof(TextInputOptions),
            new PropertyMetadata(TextInputReturnKeyType.Default));

    /// <summary>Identifies the Multiline attached property.</summary>
    public static readonly DependencyProperty MultilineProperty =
        DependencyProperty.RegisterAttached(
            "Multiline",
            typeof(bool),
            typeof(TextInputOptions),
            new PropertyMetadata(false));

    /// <summary>Identifies the AutoCapitalization attached property.</summary>
    public static readonly DependencyProperty AutoCapitalizationProperty =
        DependencyProperty.RegisterAttached(
            "AutoCapitalization",
            typeof(bool),
            typeof(TextInputOptions),
            new PropertyMetadata(false));

    /// <summary>Identifies the Lowercase attached property.</summary>
    public static readonly DependencyProperty LowercaseProperty =
        DependencyProperty.RegisterAttached(
            "Lowercase",
            typeof(bool),
            typeof(TextInputOptions),
            new PropertyMetadata(false));

    /// <summary>Identifies the Uppercase attached property.</summary>
    public static readonly DependencyProperty UppercaseProperty =
        DependencyProperty.RegisterAttached(
            "Uppercase",
            typeof(bool),
            typeof(TextInputOptions),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the IsSensitive attached property. Sensitive text (card
    /// numbers, secrets) should not be persisted or offered to suggestion
    /// engines.
    /// </summary>
    public static readonly DependencyProperty IsSensitiveProperty =
        DependencyProperty.RegisterAttached(
            "IsSensitive",
            typeof(bool),
            typeof(TextInputOptions),
            new PropertyMetadata(false));

    /// <summary>Identifies the ShowSuggestions attached property (default true).</summary>
    public static readonly DependencyProperty ShowSuggestionsProperty =
        DependencyProperty.RegisterAttached(
            "ShowSuggestions",
            typeof(bool),
            typeof(TextInputOptions),
            new PropertyMetadata(true));

    /// <summary>Gets the content-type hint for <paramref name="target"/>.</summary>
    public static TextInputContentType GetContentType(DependencyObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return (TextInputContentType)target.GetValue(ContentTypeProperty)!;
    }

    /// <summary>Sets the content-type hint for <paramref name="target"/>.</summary>
    public static void SetContentType(DependencyObject target, TextInputContentType value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(ContentTypeProperty, value);
    }

    /// <summary>Gets the Return key type for <paramref name="target"/>.</summary>
    public static TextInputReturnKeyType GetReturnKeyType(DependencyObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return (TextInputReturnKeyType)target.GetValue(ReturnKeyTypeProperty)!;
    }

    /// <summary>Sets the Return key type for <paramref name="target"/>.</summary>
    public static void SetReturnKeyType(DependencyObject target, TextInputReturnKeyType value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(ReturnKeyTypeProperty, value);
    }

    /// <summary>Gets whether <paramref name="target"/> accepts multiline text.</summary>
    public static bool GetMultiline(DependencyObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.GetValue(MultilineProperty) is true;
    }

    /// <summary>Sets whether <paramref name="target"/> accepts multiline text.</summary>
    public static void SetMultiline(DependencyObject target, bool value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(MultilineProperty, value);
    }

    /// <summary>Gets whether the keyboard should auto-capitalize sentences.</summary>
    public static bool GetAutoCapitalization(DependencyObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.GetValue(AutoCapitalizationProperty) is true;
    }

    /// <summary>Sets whether the keyboard should auto-capitalize sentences.</summary>
    public static void SetAutoCapitalization(DependencyObject target, bool value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(AutoCapitalizationProperty, value);
    }

    /// <summary>Gets whether the text is forced to lower case.</summary>
    public static bool GetLowercase(DependencyObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.GetValue(LowercaseProperty) is true;
    }

    /// <summary>Sets whether the text is forced to lower case.</summary>
    public static void SetLowercase(DependencyObject target, bool value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(LowercaseProperty, value);
    }

    /// <summary>Gets whether the text is forced to upper case.</summary>
    public static bool GetUppercase(DependencyObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.GetValue(UppercaseProperty) is true;
    }

    /// <summary>Sets whether the text is forced to upper case.</summary>
    public static void SetUppercase(DependencyObject target, bool value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(UppercaseProperty, value);
    }

    /// <summary>Gets whether the text is sensitive and must not be stored/suggested.</summary>
    public static bool GetIsSensitive(DependencyObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.GetValue(IsSensitiveProperty) is true;
    }

    /// <summary>Sets whether the text is sensitive and must not be stored/suggested.</summary>
    public static void SetIsSensitive(DependencyObject target, bool value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(IsSensitiveProperty, value);
    }

    /// <summary>Gets whether the on-screen keyboard should offer suggestions.</summary>
    public static bool GetShowSuggestions(DependencyObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.GetValue(ShowSuggestionsProperty) is true;
    }

    /// <summary>Sets whether the on-screen keyboard should offer suggestions.</summary>
    public static void SetShowSuggestions(DependencyObject target, bool value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(ShowSuggestionsProperty, value);
    }
}

using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Input.TextInput;

namespace Jalium.UI.Controls.Platform;

/// <summary>
/// Resolved on-screen keyboard hints for a focused editor. Produced by
/// <see cref="SoftKeyboardResolver"/> from (in priority order) an explicit
/// <see cref="TextInputOptions"/> hint, a WPF <see cref="InputScope"/>, and the
/// control type. The Return key is left raw here; the host window turns
/// <see cref="TextInputReturnKeyType.Default"/> into a concrete action using the
/// live focus chain.
/// </summary>
internal readonly record struct SoftKeyboardResolution(
    TextInputContentType ContentType,
    TextInputReturnKeyType ReturnKeyType,
    bool Multiline,
    bool ShowSuggestions,
    bool AutoCapitalization,
    bool Lowercase,
    bool Uppercase);

/// <summary>
/// Maps a focused editor's declared input semantics to a
/// <see cref="SoftKeyboardResolution"/>. This is the single place where the
/// otherwise inert <see cref="InputScope"/> / <see cref="TextInputOptions"/>
/// metadata and control-type defaults become an actual keyboard shape.
/// </summary>
internal static class SoftKeyboardResolver
{
    internal static SoftKeyboardResolution Resolve(DependencyObject target)
    {
        ArgumentNullException.ThrowIfNull(target);

        TextInputContentType contentType = ResolveContentType(target);

        bool multiline = TextInputOptions.GetMultiline(target) ||
            (target is TextBoxBase { AcceptsReturn: true });

        bool sensitive = TextInputOptions.GetIsSensitive(target) ||
            contentType is TextInputContentType.Password or TextInputContentType.Pin ||
            target is PasswordBox;

        // Suggestions/prediction are suppressed for anything sensitive so the
        // IME neither stores nor auto-completes secrets, matching requirement 2
        // (password fields) while leaving Chinese prediction on for plain text.
        bool showSuggestions = TextInputOptions.GetShowSuggestions(target) && !sensitive;

        return new SoftKeyboardResolution(
            contentType,
            TextInputOptions.GetReturnKeyType(target),
            multiline,
            showSuggestions,
            TextInputOptions.GetAutoCapitalization(target),
            TextInputOptions.GetLowercase(target),
            TextInputOptions.GetUppercase(target));
    }

    private static TextInputContentType ResolveContentType(DependencyObject target)
    {
        // 1) Explicit Avalonia-style hint wins.
        TextInputContentType explicitType = TextInputOptions.GetContentType(target);
        if (explicitType != TextInputContentType.Normal)
            return explicitType;

        // 2) WPF InputScope, if the focused element declares one.
        if (target.GetValue(FrameworkElement.InputScopeProperty) is InputScope scope &&
            TryMapInputScope(scope, out TextInputContentType scopedType))
        {
            return scopedType;
        }

        // 3) Control-type fallbacks so common controls "just work" with no markup.
        return target switch
        {
            PasswordBox => TextInputContentType.Password,
            NumberBox => TextInputContentType.Number,
            _ => TextInputContentType.Normal,
        };
    }

    private static bool TryMapInputScope(InputScope scope, out TextInputContentType contentType)
    {
        contentType = TextInputContentType.Normal;
        if (scope.Names.Count == 0 || scope.Names[0] is not InputScopeName name)
            return false;

        contentType = name.NameValue switch
        {
            InputScopeNameValue.Url or InputScopeNameValue.FullFilePath or
                InputScopeNameValue.FileName => TextInputContentType.Url,

            InputScopeNameValue.EmailUserName or
                InputScopeNameValue.EmailSmtpAddress => TextInputContentType.Email,

            InputScopeNameValue.Digits or InputScopeNameValue.PostalCode or
                InputScopeNameValue.NumberFullWidth => TextInputContentType.Digits,

            InputScopeNameValue.Number or InputScopeNameValue.CurrencyAmount or
                InputScopeNameValue.CurrencyAmountAndSymbol => TextInputContentType.Number,

            InputScopeNameValue.Password => TextInputContentType.Password,

            InputScopeNameValue.TelephoneNumber or InputScopeNameValue.TelephoneCountryCode or
                InputScopeNameValue.TelephoneAreaCode or
                InputScopeNameValue.TelephoneLocalNumber => TextInputContentType.Telephone,

            InputScopeNameValue.PersonalFullName or InputScopeNameValue.PersonalGivenName or
                InputScopeNameValue.PersonalMiddleName or InputScopeNameValue.PersonalSurname or
                InputScopeNameValue.PersonalNamePrefix or
                InputScopeNameValue.PersonalNameSuffix => TextInputContentType.Name,

            _ => TextInputContentType.Normal,
        };

        return contentType != TextInputContentType.Normal;
    }
}

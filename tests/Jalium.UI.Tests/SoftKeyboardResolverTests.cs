using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Input;
using Jalium.UI.Input.TextInput;
using Xunit;

namespace Jalium.UI.Tests;

/// <summary>
/// Covers <see cref="SoftKeyboardResolver"/>: the mapping from a focused editor's
/// declared input semantics (explicit <see cref="TextInputOptions"/>, WPF
/// <see cref="InputScope"/>, or control type) to an on-screen keyboard shape.
/// </summary>
[Collection("Application")]
public class SoftKeyboardResolverTests
{
    private static InputScope Scope(InputScopeNameValue value)
    {
        var scope = new InputScope();
        scope.Names.Add(new InputScopeName(value));
        return scope;
    }

    [Fact]
    public void PlainTextBox_ResolvesToNormalText()
    {
        var resolution = SoftKeyboardResolver.Resolve(new TextBox());

        Assert.Equal(TextInputContentType.Normal, resolution.ContentType);
        Assert.False(resolution.Multiline);
        Assert.True(resolution.ShowSuggestions);
    }

    [Fact]
    public void ExplicitContentType_Wins()
    {
        var box = new TextBox();
        TextInputOptions.SetContentType(box, TextInputContentType.Email);

        Assert.Equal(TextInputContentType.Email, SoftKeyboardResolver.Resolve(box).ContentType);
    }

    [Theory]
    [InlineData(InputScopeNameValue.EmailSmtpAddress, TextInputContentType.Email)]
    [InlineData(InputScopeNameValue.EmailUserName, TextInputContentType.Email)]
    [InlineData(InputScopeNameValue.Url, TextInputContentType.Url)]
    [InlineData(InputScopeNameValue.Digits, TextInputContentType.Digits)]
    [InlineData(InputScopeNameValue.Number, TextInputContentType.Number)]
    [InlineData(InputScopeNameValue.CurrencyAmount, TextInputContentType.Number)]
    [InlineData(InputScopeNameValue.TelephoneNumber, TextInputContentType.Telephone)]
    [InlineData(InputScopeNameValue.Password, TextInputContentType.Password)]
    [InlineData(InputScopeNameValue.PersonalFullName, TextInputContentType.Name)]
    public void InputScope_MapsToContentType(InputScopeNameValue scope, TextInputContentType expected)
    {
        var box = new TextBox();
        InputMethod.SetInputScope(box, Scope(scope));

        Assert.Equal(expected, SoftKeyboardResolver.Resolve(box).ContentType);
    }

    [Fact]
    public void ExplicitContentType_OverridesInputScope()
    {
        var box = new TextBox();
        InputMethod.SetInputScope(box, Scope(InputScopeNameValue.TelephoneNumber));
        TextInputOptions.SetContentType(box, TextInputContentType.Email);

        Assert.Equal(TextInputContentType.Email, SoftKeyboardResolver.Resolve(box).ContentType);
    }

    [Fact]
    public void PasswordBox_ResolvesToPasswordWithoutSuggestions()
    {
        var resolution = SoftKeyboardResolver.Resolve(new PasswordBox());

        Assert.Equal(TextInputContentType.Password, resolution.ContentType);
        Assert.False(resolution.ShowSuggestions);
    }

    [Fact]
    public void NumberBox_ResolvesToNumber()
    {
        Assert.Equal(TextInputContentType.Number, SoftKeyboardResolver.Resolve(new NumberBox()).ContentType);
    }

    [Fact]
    public void AcceptsReturn_ImpliesMultiline()
    {
        var box = new TextBox { AcceptsReturn = true };

        Assert.True(SoftKeyboardResolver.Resolve(box).Multiline);
    }

    [Fact]
    public void ExplicitMultiline_IsHonoured()
    {
        var box = new TextBox();
        TextInputOptions.SetMultiline(box, true);

        Assert.True(SoftKeyboardResolver.Resolve(box).Multiline);
    }

    [Fact]
    public void ReturnKeyType_PassesThrough()
    {
        var box = new TextBox();
        TextInputOptions.SetReturnKeyType(box, TextInputReturnKeyType.Search);

        Assert.Equal(TextInputReturnKeyType.Search, SoftKeyboardResolver.Resolve(box).ReturnKeyType);
    }

    [Fact]
    public void ShowSuggestionsFalse_DisablesSuggestions()
    {
        var box = new TextBox();
        TextInputOptions.SetShowSuggestions(box, false);

        Assert.False(SoftKeyboardResolver.Resolve(box).ShowSuggestions);
    }

    [Fact]
    public void SensitiveText_DisablesSuggestions()
    {
        var box = new TextBox();
        TextInputOptions.SetIsSensitive(box, true);

        Assert.False(SoftKeyboardResolver.Resolve(box).ShowSuggestions);
    }
}

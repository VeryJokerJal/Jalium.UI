using System.Collections;
using System.ComponentModel;
using System.Globalization;
using Jalium.UI.Markup;

namespace Jalium.UI.Input;

/// <summary>
/// Represents information related to the scope of data provided by an input method.
/// </summary>
[TypeConverter(typeof(InputScopeConverter))]
public sealed class InputScope
{
    private readonly ArrayList _names = [];
    private readonly ArrayList _phraseList = [];

    /// <summary>Gets the input-scope names associated with this scope.</summary>
    public IList Names => _names;

    /// <summary>Gets the phrase suggestions associated with this scope.</summary>
    public IList PhraseList => _phraseList;

    /// <summary>Gets or sets the regular expression used by the input processor.</summary>
    public string RegularExpression { get; set; } = string.Empty;

    /// <summary>Gets or sets the SRGS markup used by the input processor.</summary>
    public string SrgsMarkup { get; set; } = string.Empty;
}

/// <summary>Identifies a named input scope.</summary>
[TypeConverter(typeof(InputScopeNameConverter))]
public partial class InputScopeName : IAddChild
{
    public InputScopeName()
    {
    }

    public InputScopeName(InputScopeNameValue nameValue)
    {
        NameValue = nameValue;
    }

    public InputScopeNameValue NameValue { get; set; }

    public void AddChild(object value)
    {
        if (value is string text)
        {
            AddText(text);
            return;
        }

        throw new ArgumentException("InputScopeName only accepts text content.", nameof(value));
    }

    public void AddText(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!Enum.TryParse<InputScopeNameValue>(name, ignoreCase: true, out var value))
        {
            throw new ArgumentException($"'{name}' is not a valid input scope name.", nameof(name));
        }

        NameValue = value;
    }
}

/// <summary>Represents a phrase suggested to an input processor.</summary>
public partial class InputScopePhrase : IAddChild
{
    public InputScopePhrase()
    {
    }

    public InputScopePhrase(string name)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public string Name { get; set; } = string.Empty;

    public void AddChild(object value)
    {
        if (value is string text)
        {
            AddText(text);
            return;
        }

        throw new ArgumentException("InputScopePhrase only accepts text content.", nameof(value));
    }

    public void AddText(string name)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }
}

public enum InputScopeNameValue
{
    Default = 0,
    Url = 1,
    FullFilePath = 2,
    FileName = 3,
    EmailUserName = 4,
    EmailSmtpAddress = 5,
    LogOnName = 6,
    PersonalFullName = 7,
    PersonalNamePrefix = 8,
    PersonalGivenName = 9,
    PersonalMiddleName = 10,
    PersonalSurname = 11,
    PersonalNameSuffix = 12,
    PostalAddress = 13,
    PostalCode = 14,
    AddressStreet = 15,
    AddressStateOrProvince = 16,
    AddressCity = 17,
    AddressCountryName = 18,
    AddressCountryShortName = 19,
    CurrencyAmountAndSymbol = 20,
    CurrencyAmount = 21,
    Date = 22,
    DateMonth = 23,
    DateDay = 24,
    DateYear = 25,
    DateMonthName = 26,
    DateDayName = 27,
    Digits = 28,
    Number = 29,
    OneChar = 30,
    Password = 31,
    TelephoneNumber = 32,
    TelephoneCountryCode = 33,
    TelephoneAreaCode = 34,
    TelephoneLocalNumber = 35,
    Time = 36,
    TimeHour = 37,
    TimeMinorSec = 38,
    NumberFullWidth = 39,
    AlphanumericHalfWidth = 40,
    AlphanumericFullWidth = 41,
    CurrencyChinese = 42,
    Bopomofo = 43,
    Hiragana = 44,
    KatakanaHalfWidth = 45,
    KatakanaFullWidth = 46,
    Hanja = 47,
    PhraseList = -1,
    RegularExpression = -2,
    Srgs = -3,
    Xml = -4,
}

public sealed class InputScopeConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
        => destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string text && Enum.TryParse<InputScopeNameValue>(text, ignoreCase: true, out var result))
        {
            var scope = new InputScope();
            scope.Names.Add(new InputScopeName(result));
            return scope;
        }

        return base.ConvertFrom(context, culture, value);
    }

    public override object? ConvertTo(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object? value,
        Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);
        if (destinationType == typeof(string)
            && value is InputScope scope
            && scope.Names.Count == 1
            && scope.Names[0] is InputScopeName name)
        {
            return name.NameValue.ToString();
        }

        return base.ConvertTo(context, culture, value, destinationType);
    }
}

public sealed class InputScopeNameConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
        => destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string text && Enum.TryParse<InputScopeNameValue>(text, ignoreCase: true, out var result))
        {
            return new InputScopeName(result);
        }

        return base.ConvertFrom(context, culture, value);
    }

    public override object? ConvertTo(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object? value,
        Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);
        if (destinationType == typeof(string) && value is InputScopeName name)
        {
            return name.NameValue.ToString();
        }

        return base.ConvertTo(context, culture, value, destinationType);
    }
}

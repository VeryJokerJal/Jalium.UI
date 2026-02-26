using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Jalium.UI.Input;

/// <summary>
/// Represents information related to the scope of data provided by an input method.
/// </summary>
[TypeConverter(typeof(InputScopeConverter))]
public sealed class InputScope
{
    public Collection<InputScopeName> Names { get; } = new();
    public Collection<InputScopePhrase> PhraseList { get; } = new();
    public Collection<InputScopePhrase> RegularExpression { get; } = new();
    public string SrgsMarkup { get; set; } = string.Empty;
}

public sealed class InputScopeName
{
    public InputScopeName() { }
    public InputScopeName(InputScopeNameValue nameValue) { NameValue = nameValue; }
    public InputScopeNameValue NameValue { get; set; }
}

public sealed class InputScopePhrase
{
    public InputScopePhrase() { }
    public InputScopePhrase(string name) { Name = name; }
    public string Name { get; set; } = string.Empty;
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
    Xml = -4
}

public sealed class InputScopeConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object value)
    {
        if (value is string s && Enum.TryParse<InputScopeNameValue>(s, true, out var result))
        {
            var scope = new InputScope();
            scope.Names.Add(new InputScopeName(result));
            return scope;
        }
        return base.ConvertFrom(context, culture, value);
    }
}

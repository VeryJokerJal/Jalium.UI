using System.Collections.Concurrent;
using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using System.Globalization;
using System.Reflection;

namespace Jalium.UI.Markup;

/// <summary>Represents an RFC 3066/IETF language tag used by XAML.</summary>
[TypeConverter(typeof(XmlLanguageConverter))]
public class XmlLanguage
{
    private static readonly ConcurrentDictionary<string, XmlLanguage> s_cache =
        new(StringComparer.Ordinal);

    private CultureInfo? _equivalentCulture;
    private CultureInfo? _specificCulture;
    private bool _equivalentCultureFailed;

    private XmlLanguage(string lowerCaseTag)
    {
        IetfLanguageTag = lowerCaseTag;
    }

    /// <summary>Gets the empty language tag.</summary>
    public static XmlLanguage Empty => GetLanguage(string.Empty);

    /// <summary>Gets the normalized, lower-case IETF language tag.</summary>
    public string IetfLanguageTag { get; }

    /// <summary>Gets the interned language object for <paramref name="ietfLanguageTag"/>.</summary>
    public static XmlLanguage GetLanguage(string ietfLanguageTag)
    {
        ArgumentNullException.ThrowIfNull(ietfLanguageTag);

        for (var i = 0; i < ietfLanguageTag.Length; i++)
        {
            if (ietfLanguageTag[i] > 0x7f)
            {
                throw new ArgumentException("Language tags must contain ASCII characters only.", nameof(ietfLanguageTag));
            }
        }

        var normalized = ietfLanguageTag.ToLowerInvariant();
        ValidateTag(normalized, nameof(ietfLanguageTag));
        return s_cache.GetOrAdd(normalized, static tag => new XmlLanguage(tag));
    }

    /// <summary>Gets the registered culture exactly equivalent to this tag.</summary>
    public CultureInfo GetEquivalentCulture()
    {
        if (_equivalentCulture != null)
        {
            return _equivalentCulture;
        }

        var tag = string.Equals(IetfLanguageTag, "und", StringComparison.Ordinal)
            ? string.Empty
            : IetfLanguageTag;

        try
        {
            _equivalentCulture = CultureInfo.GetCultureInfoByIetfLanguageTag(tag);
            return _equivalentCulture;
        }
        catch (ArgumentException exception)
        {
            _equivalentCultureFailed = true;
            throw new InvalidOperationException($"No culture is registered for language tag '{tag}'.", exception);
        }
    }

    /// <summary>Gets the closest registered non-neutral culture for this tag.</summary>
    public CultureInfo GetSpecificCulture()
    {
        if (_specificCulture != null)
        {
            return _specificCulture;
        }

        if (IetfLanguageTag.Length == 0 || string.Equals(IetfLanguageTag, "und", StringComparison.Ordinal))
        {
            return _specificCulture = GetEquivalentCulture();
        }

        var compatible = GetCompatibleCulture();
        if (compatible.Equals(CultureInfo.InvariantCulture))
        {
            throw new InvalidOperationException($"No specific culture is registered for language tag '{IetfLanguageTag}'.");
        }

        if (!compatible.IsNeutralCulture)
        {
            return _specificCulture = compatible;
        }

        try
        {
            var specific = CultureInfo.CreateSpecificCulture(compatible.Name);
            return _specificCulture = CultureInfo.GetCultureInfoByIetfLanguageTag(specific.IetfLanguageTag);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                $"No specific culture is registered for language tag '{IetfLanguageTag}'.",
                exception);
        }
    }

    public override string ToString() => IetfLanguageTag;

    private CultureInfo GetCompatibleCulture()
    {
        if (!_equivalentCultureFailed)
        {
            try
            {
                return GetEquivalentCulture();
            }
            catch (InvalidOperationException)
            {
            }
        }

        var tag = IetfLanguageTag;
        while (tag.Length > 0)
        {
            var separator = tag.LastIndexOf('-');
            tag = separator < 0 ? string.Empty : tag[..separator];
            try
            {
                return CultureInfo.GetCultureInfoByIetfLanguageTag(tag);
            }
            catch (ArgumentException)
            {
            }
        }

        return CultureInfo.InvariantCulture;
    }

    private static void ValidateTag(string tag, string parameterName)
    {
        if (tag.Length == 0)
        {
            return;
        }

        if (tag[0] == '-' || tag[^1] == '-')
        {
            throw new ArgumentException($"'{tag}' is not a valid language tag.", parameterName);
        }

        var start = 0;
        var primary = true;
        while (start < tag.Length)
        {
            var separator = tag.IndexOf('-', start);
            var end = separator < 0 ? tag.Length : separator;
            var length = end - start;
            if (length is < 1 or > 8)
            {
                throw new ArgumentException($"'{tag}' is not a valid language tag.", parameterName);
            }

            for (var i = start; i < end; i++)
            {
                var character = tag[i];
                var valid = character is >= 'a' and <= 'z'
                    || (!primary && character is >= '0' and <= '9');
                if (!valid)
                {
                    throw new ArgumentException($"'{tag}' is not a valid language tag.", parameterName);
                }
            }

            if (separator < 0)
            {
                break;
            }

            start = separator + 1;
            primary = false;
        }
    }
}

/// <summary>Converts between strings and <see cref="XmlLanguage"/> instances.</summary>
public class XmlLanguageConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        => sourceType == typeof(string);

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
        => destinationType == typeof(string)
            || destinationType == typeof(InstanceDescriptor);

    public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        => value is string text
            ? XmlLanguage.GetLanguage(text)
            : throw GetConvertFromException(value);

    public override object ConvertTo(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object? value,
        Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);
        if (value is XmlLanguage language)
        {
            if (destinationType == typeof(string))
            {
                return language.IetfLanguageTag;
            }

            if (destinationType == typeof(InstanceDescriptor))
            {
                var method = typeof(XmlLanguage).GetMethod(
                    nameof(XmlLanguage.GetLanguage),
                    BindingFlags.Public | BindingFlags.Static,
                    binder: null,
                    [typeof(string)],
                    modifiers: null)!;
                return new InstanceDescriptor(method, new object[] { language.IetfLanguageTag });
            }
        }

        throw GetConvertToException(value, destinationType);
    }
}

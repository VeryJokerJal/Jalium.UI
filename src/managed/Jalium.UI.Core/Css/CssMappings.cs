using System.Globalization;
using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>A single (dependency property, value) assignment produced by an <see cref="ICssMultiPropertyConverter"/>.</summary>
public readonly struct CssPropertyAssignment
{
    public CssPropertyAssignment(DependencyProperty property, object? value)
    {
        ArgumentNullException.ThrowIfNull(property);
        Property = property;
        Value = value;
    }

    public DependencyProperty Property { get; }

    public object? Value { get; }
}

/// <summary>
/// Converts a CSS declaration's raw value text into a value for one dependency property.
/// Modeled on <c>Jalium.UI.Data.IValueConverter</c>, narrowed for CSS: the input is always
/// the declaration text, conversion is one-way, and the culture is always
/// <see cref="CultureInfo.InvariantCulture"/>.
/// </summary>
public interface ICssValueConverter
{
    /// <summary>
    /// Returns the converted value, or null to drop the declaration per the CSS error model
    /// (the rest of the block still applies). Exceptions are treated as null and do not
    /// propagate.
    /// <para><paramref name="targetType"/> is the target property's <c>PropertyType</c>
    /// whenever the property is known at conversion time: always for a fixed
    /// <see cref="DependencyProperty"/> registration, and for a by-name registration when
    /// <c>cacheByValue</c> is false (the property has already been resolved against the
    /// element's runtime type). A by-name registration with <c>cacheByValue: true</c>
    /// converts once at compile time, before any element exists — <paramref name="targetType"/>
    /// is then <c>typeof(object)</c>, because a single shared result cannot depend on which
    /// owner type the name later resolves against.</para>
    /// <para><paramref name="parameter"/> is the <c>converterParameter</c> given at
    /// registration. <paramref name="culture"/> is always
    /// <see cref="CultureInfo.InvariantCulture"/> — deliberately different from the binding
    /// engine's <c>CurrentCulture</c> default: style-sheet text is culture-invariant source
    /// code, and cached conversions are shared process-wide.</para>
    /// </summary>
    object? Convert(string rawValue, Type targetType, object? parameter, CultureInfo culture);
}

/// <summary>
/// An element-aware converter that turns one declaration into assignments for several
/// dependency properties. Note the arity is the inverse of
/// <c>Jalium.UI.Data.IMultiValueConverter</c>: one raw value in, many property assignments
/// out. Called on every application (the framework cannot prove purity); implementations
/// that want caching cache internally.
/// </summary>
public interface ICssMultiPropertyConverter
{
    /// <summary>
    /// Returns the assignments to apply, or null to drop the declaration with a one-shot
    /// diagnostic; an empty list is a successful no-op. Exceptions are treated as null and
    /// do not propagate. <paramref name="parameter"/> and <paramref name="culture"/> follow
    /// the <see cref="ICssValueConverter.Convert"/> contract.
    /// </summary>
    IReadOnlyList<CssPropertyAssignment>? Convert(
        string rawValue, FrameworkElement element, object? parameter, CultureInfo culture);
}

/// <summary>Common CSS value parsing helpers for custom converters (string in — no internal readers).</summary>
public static class CssValueParsing
{
    /// <summary>
    /// hex (#RGB/#RGBA/#RRGGBB/#RRGGBBAA, CSS trailing-alpha), named colors, transparent,
    /// rgb()/rgba()/hsl()/hsla() in legacy and modern syntax. currentcolor returns false
    /// (it needs element context, which this helper does not carry).
    /// </summary>
    public static bool TryParseColor(string text, out Color color)
    {
        ArgumentNullException.ThrowIfNull(text);
        var reader = new CssTokenReader(text);
        if (CssColorParser.TryParse(ref reader, out color, out var isCurrentColor) &&
            !isCurrentColor && reader.AtEnd)
        {
            return true;
        }

        color = default;
        return false;
    }

    /// <summary>Absolute lengths (px/pt/in/cm/mm/q/pc/unitless) to DIPs. em/rem/%/vw/vh return false.</summary>
    public static bool TryParseLength(string text, out double pixels)
    {
        ArgumentNullException.ThrowIfNull(text);
        var reader = new CssTokenReader(text);
        if (reader.TryReadLength(out var length) && reader.AtEnd && length.IsAbsolute)
        {
            pixels = length.ToPxAbsolute();
            return true;
        }

        pixels = 0;
        return false;
    }
}

/// <summary>
/// The public extension surface of the CSS property table. Registrations are global,
/// thread-safe, and later registrations win — including over built-in entries and the
/// Unsupported placeholders. There is no unregister: re-register to replace; already
/// compiled style sheets refresh automatically.
/// </summary>
public static class CssMappings
{
    /// <summary>
    /// Redirects <paramref name="cssName"/> to <paramref name="targetCssName"/> at compile
    /// time (e.g. a design-token name for a built-in property). The alias participates in
    /// the cascade as the target's longhand. Throws when the alias would create a cycle.
    /// </summary>
    public static void RegisterAlias(string cssName, string targetCssName)
    {
        ValidateName(cssName);
        ValidateName(targetCssName);
        CssPropertyRegistry.RegisterUserAlias(cssName.ToLowerInvariant(), targetCssName.ToLowerInvariant());
    }

    /// <summary>
    /// Registers a custom property mapped to a fixed dependency property. The converter
    /// receives <paramref name="property"/>.PropertyType as its target type and returns null
    /// for invalid values (declaration dropped). With <paramref name="cacheByValue"/>
    /// (default) the conversion runs once per distinct declaration text and the result is
    /// shared by every matched element — return frozen Freezables. Pass false to convert on
    /// every application. <paramref name="converterParameter"/> belongs to the registration,
    /// not the declaration — the same converter class can be registered under several css
    /// names with different parameters.
    /// </summary>
    public static void RegisterProperty(
        string cssName, DependencyProperty property, ICssValueConverter converter,
        object? converterParameter = null, bool cacheByValue = true)
    {
        ValidateName(cssName);
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(converter);
        var name = cssName.ToLowerInvariant();
        var parameter = converterParameter;
        CssPropertyRegistry.RegisterUser(new CssPropertyDescriptor
        {
            Name = name,
            Kind = CssPropertyKind.Longhand,
            Parse = (ref CssTokenReader reader, CssCompileContext _) =>
            {
                var raw = reader.Remaining.ToString();
                if (raw.Length == 0)
                {
                    return null;
                }

                if (cacheByValue)
                {
                    var converted = SafeConvert(converter, raw, property.PropertyType, parameter);
                    if (converted is null || !property.PropertyType.IsInstanceOfType(converted))
                    {
                        return null;
                    }

                    return new CssImmediateValue(property, converted);
                }

                return new CssUserLazyValue(name, property, null, converter, parameter, raw);
            },
        });
    }

    /// <summary>
    /// Registers a custom property whose target dependency property is resolved by name
    /// against each element's runtime type (for properties spread across owners, e.g.
    /// "Padding"/"Background"). With <paramref name="cacheByValue"/> (default) the conversion
    /// runs at compile time, before the property is resolved — the converter's target type is
    /// then typeof(object); pass false to convert per application with the resolved
    /// property's type.
    /// </summary>
    public static void RegisterProperty(
        string cssName, string dependencyPropertyName, ICssValueConverter converter,
        object? converterParameter = null, bool cacheByValue = true)
    {
        ValidateName(cssName);
        ArgumentException.ThrowIfNullOrEmpty(dependencyPropertyName);
        ArgumentNullException.ThrowIfNull(converter);
        var name = cssName.ToLowerInvariant();
        var parameter = converterParameter;
        CssPropertyRegistry.RegisterUser(new CssPropertyDescriptor
        {
            Name = name,
            Kind = CssPropertyKind.Longhand,
            Parse = (ref CssTokenReader reader, CssCompileContext _) =>
            {
                var raw = reader.Remaining.ToString();
                if (raw.Length == 0)
                {
                    return null;
                }

                if (cacheByValue)
                {
                    var converted = SafeConvert(converter, raw, typeof(object), parameter);
                    return converted is null ? null : new CssNamedValue(name, dependencyPropertyName, converted);
                }

                return new CssUserLazyValue(name, null, dependencyPropertyName, converter, parameter, raw);
            },
        });
    }

    /// <summary>
    /// Registers an element-aware converter that may produce assignments for several
    /// dependency properties from one declaration.
    /// </summary>
    public static void RegisterProperty(
        string cssName, ICssMultiPropertyConverter converter, object? converterParameter = null)
    {
        ValidateName(cssName);
        ArgumentNullException.ThrowIfNull(converter);
        var name = cssName.ToLowerInvariant();
        var parameter = converterParameter;
        CssPropertyRegistry.RegisterUser(new CssPropertyDescriptor
        {
            Name = name,
            Kind = CssPropertyKind.Longhand,
            Parse = (ref CssTokenReader reader, CssCompileContext _) =>
            {
                var raw = reader.Remaining.ToString();
                return raw.Length == 0 ? null : new CssUserMultiPropertyValue(name, converter, parameter, raw);
            },
        });
    }

    internal static object? SafeConvert(
        ICssValueConverter converter, string rawValue, Type targetType, object? parameter)
    {
        try
        {
            return converter.Convert(rawValue, targetType, parameter, CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    private static void ValidateName(string cssName)
    {
        ArgumentException.ThrowIfNullOrEmpty(cssName);
        foreach (var c in cssName)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_')
            {
                throw new ArgumentException($"'{cssName}' is not a valid CSS property name.", nameof(cssName));
            }
        }

        if (cssName[0] == '-' && cssName.Length > 1 && char.IsAsciiDigit(cssName[1]))
        {
            throw new ArgumentException($"'{cssName}' is not a valid CSS property name.", nameof(cssName));
        }
    }
}

/// <summary>Per-application user conversion (cacheByValue: false).</summary>
internal sealed class CssUserLazyValue : CssCompiledValue
{
    private readonly string _cssName;
    private readonly DependencyProperty? _property;
    private readonly string? _dpName;
    private readonly ICssValueConverter _converter;
    private readonly object? _parameter;
    private readonly string _rawValue;

    public CssUserLazyValue(
        string cssName, DependencyProperty? property, string? dpName,
        ICssValueConverter converter, object? parameter, string rawValue)
    {
        _cssName = cssName;
        _property = property;
        _dpName = dpName;
        _converter = converter;
        _parameter = parameter;
        _rawValue = rawValue;
    }

    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
    {
        var dp = _property ?? CssDependencyPropertyLookup.Find(context.Element.GetType(), _dpName!);
        if (dp is null)
        {
            CssDiagnostics.Report(
                _cssName, CssDiagnosticReason.TargetPropertyMissing, context.Element.GetType(),
                $"no dependency property '{_dpName}' on this element type; declaration skipped here");
            return false;
        }

        var converted = CssMappings.SafeConvert(_converter, _rawValue, dp.PropertyType, _parameter);
        if (converted is null || !dp.PropertyType.IsInstanceOfType(converted))
        {
            CssDiagnostics.Report(
                _cssName, CssDiagnosticReason.InvalidValue, context.Element.GetType(),
                "custom converter rejected the value; declaration skipped here");
            return false;
        }

        sink.Set(dp, converted);
        return true;
    }
}

/// <summary>Multi-property conversion: element context, several assignments, no framework caching.</summary>
internal sealed class CssUserMultiPropertyValue : CssCompiledValue
{
    private readonly string _cssName;
    private readonly ICssMultiPropertyConverter _converter;
    private readonly object? _parameter;
    private readonly string _rawValue;

    public CssUserMultiPropertyValue(
        string cssName, ICssMultiPropertyConverter converter, object? parameter, string rawValue)
    {
        _cssName = cssName;
        _converter = converter;
        _parameter = parameter;
        _rawValue = rawValue;
    }

    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
    {
        IReadOnlyList<CssPropertyAssignment>? assignments;
        try
        {
            assignments = _converter.Convert(
                _rawValue, context.Element, _parameter, CultureInfo.InvariantCulture);
        }
        catch
        {
            assignments = null;
        }

        if (assignments is null)
        {
            CssDiagnostics.Report(
                _cssName, CssDiagnosticReason.InvalidValue, context.Element.GetType(),
                "multi-property converter rejected the value; declaration skipped here");
            return false;
        }

        for (var i = 0; i < assignments.Count; i++)
        {
            sink.Set(assignments[i].Property, assignments[i].Value);
        }

        return true;
    }
}

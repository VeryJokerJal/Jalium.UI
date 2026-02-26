using System.ComponentModel;
using System.Globalization;

namespace Jalium.UI.Input;

/// <summary>
/// Converts a <see cref="Cursor"/> to and from a string representation.
/// Supports all standard cursor types defined in <see cref="Cursors"/>.
/// </summary>
public sealed class CursorConverter : TypeConverter
{
    private StandardValuesCollection? _standardValues;

    /// <summary>
    /// Returns whether this converter can convert from the specified source type.
    /// </summary>
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    /// <summary>
    /// Returns whether this converter can convert to the specified destination type.
    /// </summary>
    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
        => destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    /// <summary>
    /// Converts the given value to a <see cref="Cursor"/>.
    /// </summary>
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string text)
        {
            var trimmed = text.Trim();

            if (string.IsNullOrEmpty(trimmed))
                return null;

            // Match against known cursor type names
            if (Enum.TryParse<CursorType>(trimmed, true, out var cursorType))
            {
                return cursorType switch
                {
                    CursorType.Arrow => Cursors.Arrow,
                    CursorType.Cross => Cursors.Cross,
                    CursorType.Hand => Cursors.Hand,
                    CursorType.Help => Cursors.Help,
                    CursorType.IBeam => Cursors.IBeam,
                    CursorType.None => Cursors.None,
                    CursorType.Pen => Cursors.Pen,
                    CursorType.ScrollAll => Cursors.ScrollAll,
                    CursorType.ScrollE => Cursors.ScrollE,
                    CursorType.ScrollN => Cursors.ScrollN,
                    CursorType.ScrollNE => Cursors.ScrollNE,
                    CursorType.ScrollNW => Cursors.ScrollNW,
                    CursorType.ScrollS => Cursors.ScrollS,
                    CursorType.ScrollSE => Cursors.ScrollSE,
                    CursorType.ScrollSW => Cursors.ScrollSW,
                    CursorType.ScrollW => Cursors.ScrollW,
                    CursorType.SizeWE => Cursors.SizeWE,
                    CursorType.SizeNS => Cursors.SizeNS,
                    CursorType.SizeNWSE => Cursors.SizeNWSE,
                    CursorType.SizeNESW => Cursors.SizeNESW,
                    CursorType.SizeAll => Cursors.SizeAll,
                    CursorType.No => Cursors.No,
                    CursorType.Wait => Cursors.Wait,
                    CursorType.AppStarting => Cursors.AppStarting,
                    CursorType.UpArrow => Cursors.UpArrow,
                    _ => new Cursor(cursorType),
                };
            }
        }

        return base.ConvertFrom(context, culture, value);
    }

    /// <summary>
    /// Converts the given <see cref="Cursor"/> to its string representation.
    /// </summary>
    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        if (destinationType == typeof(string))
        {
            if (value is Cursor cursor)
                return cursor.ToString();

            return string.Empty;
        }

        return base.ConvertTo(context, culture, value, destinationType);
    }

    /// <summary>
    /// Returns a collection of standard cursor values for this type converter.
    /// </summary>
    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context)
    {
        if (_standardValues is null)
        {
            var values = new Cursor[]
            {
                Cursors.Arrow,
                Cursors.AppStarting,
                Cursors.Cross,
                Cursors.Hand,
                Cursors.Help,
                Cursors.IBeam,
                Cursors.No,
                Cursors.None,
                Cursors.Pen,
                Cursors.ScrollAll,
                Cursors.ScrollE,
                Cursors.ScrollN,
                Cursors.ScrollNE,
                Cursors.ScrollNW,
                Cursors.ScrollS,
                Cursors.ScrollSE,
                Cursors.ScrollSW,
                Cursors.ScrollW,
                Cursors.SizeAll,
                Cursors.SizeNESW,
                Cursors.SizeNS,
                Cursors.SizeNWSE,
                Cursors.SizeWE,
                Cursors.UpArrow,
                Cursors.Wait,
            };

            _standardValues = new StandardValuesCollection(values);
        }

        return _standardValues;
    }

    /// <summary>
    /// Returns whether this type converter supports a standard set of values that can be picked from a list.
    /// </summary>
    public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;
}

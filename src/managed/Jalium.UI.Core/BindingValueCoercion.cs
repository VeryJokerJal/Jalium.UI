using System.Globalization;
using Jalium.UI.Data;

namespace Jalium.UI;

internal static class BindingValueCoercion
{
    public static object? Coerce(object? value, Type targetType, CultureInfo culture)
    {
        if (ReferenceEquals(value, DependencyProperty.UnsetValue) ||
            ReferenceEquals(value, Binding.DoNothing))
        {
            return value;
        }

        if (value == null)
        {
            // For non-nullable value types (double, int, bool, etc.), return the
            // type's default rather than null to prevent NullReferenceException
            // when the DP getter unboxes the stored value. Uses a TypeCode switch
            // and known framework-type table so AOT/trim does not need to keep
            // any constructor reflectively (no Activator.CreateInstance).
            if (targetType.IsValueType && Nullable.GetUnderlyingType(targetType) == null)
                return DefaultValueTypeBox(targetType);
            return null;
        }

        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (underlyingType == typeof(object) || underlyingType.IsInstanceOfType(value))
            return value;

        if (underlyingType == typeof(string))
            return ConvertToString(value, culture);

        if (value is string stringValue)
        {
            if (string.IsNullOrWhiteSpace(stringValue) && Nullable.GetUnderlyingType(targetType) != null)
                return null;

            if (underlyingType.IsEnum && Enum.TryParse(underlyingType, stringValue, ignoreCase: true, out var enumValue))
                return enumValue;

            try
            {
                return System.Convert.ChangeType(stringValue, underlyingType, culture);
            }
            catch
            {
                return value;
            }
        }

        // Convert.ChangeType requires the target type to implement IConvertible — typically
        // primitives, decimal, DateTime and string. User reference types (Brush, Visibility
        // descendants, etc.) do not, and ChangeType raises InvalidCastException for every
        // call against them. The catch below swallows it, but Visual Studio's
        // "break on thrown CLR exceptions" flag still stops the debugger every time, which
        // pollutes the user's debugging session even though the binding is otherwise
        // healthy. Skip the call entirely when the target can't be Convert.ChangeType'd
        // and let the property setter's own type check decide whether to drop the value.
        if (!typeof(IConvertible).IsAssignableFrom(underlyingType))
        {
            return value;
        }

        try
        {
            return System.Convert.ChangeType(value, underlyingType, culture);
        }
        catch
        {
            return value;
        }
    }

    private static string ConvertToString(object value, CultureInfo culture)
    {
        if (value is string stringValue)
            return stringValue;

        if (value is IFormattable formattable)
            return formattable.ToString(null, culture) ?? string.Empty;

        return value.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Returns a boxed default for the given non-nullable value type without using reflection, or
    /// <c>null</c> for a value type this table does not know.
    /// </summary>
    /// <remarks>
    /// Covers all primitives via <see cref="Type.GetTypeCode"/>, enums via <see cref="Enum.ToObject(Type, long)"/>,
    /// and the framework structs Thickness/CornerRadius/Size/Point/Rect/TimeSpan/Guid/DateTimeOffset. It
    /// deliberately stays reflection-free (AOT/trim-safe), so value types NOT in the table — including the
    /// framework structs <c>Color</c>, <c>GridLength</c> and <c>Duration</c>, plus any user-defined struct —
    /// fall back to <c>null</c>. Therefore EVERY value-type consumer of this helper must either apply its own
    /// typed fallback (as <c>DependencyProperty.GetEffectiveDefaultValue</c> does via <c>Activator.CreateInstance</c>
    /// on the read path) OR be protected by a null-into-value-type backstop (as the DP write paths are, via
    /// <c>IsNullForNonNullableValueType</c>): a <c>null</c> returned here must never be pinned as a value-type
    /// property's effective value, or the generated CLR getter will unbox-crash at layout.
    /// </remarks>
    internal static object? DefaultValueTypeBox(Type targetType)
    {
        if (targetType.IsEnum)
            return Enum.ToObject(targetType, 0L);

        switch (Type.GetTypeCode(targetType))
        {
            case TypeCode.Boolean: return BoxedFalse;
            case TypeCode.Byte: return BoxedByteZero;
            case TypeCode.SByte: return BoxedSByteZero;
            case TypeCode.Char: return BoxedCharZero;
            case TypeCode.Int16: return BoxedShortZero;
            case TypeCode.UInt16: return BoxedUShortZero;
            case TypeCode.Int32: return BoxedIntZero;
            case TypeCode.UInt32: return BoxedUIntZero;
            case TypeCode.Int64: return BoxedLongZero;
            case TypeCode.UInt64: return BoxedULongZero;
            case TypeCode.Single: return BoxedFloatZero;
            case TypeCode.Double: return BoxedDoubleZero;
            case TypeCode.Decimal: return BoxedDecimalZero;
            case TypeCode.DateTime: return BoxedDateTimeDefault;
        }

        if (targetType == typeof(TimeSpan)) return BoxedTimeSpanZero;
        if (targetType == typeof(Guid)) return BoxedGuidEmpty;
        if (targetType == typeof(DateTimeOffset)) return BoxedDateTimeOffsetDefault;
        if (targetType == typeof(Thickness)) return BoxedThicknessDefault;
        if (targetType == typeof(CornerRadius)) return BoxedCornerRadiusDefault;
        if (targetType == typeof(Size)) return BoxedSizeEmpty;
        if (targetType == typeof(Point)) return BoxedPointZero;
        if (targetType == typeof(Rect)) return BoxedRectEmpty;

        // Unknown user-defined value type: framework cannot synthesize a default
        // without reflection. Return null and let downstream metadata defaults
        // or coercion callbacks supply a value if needed.
        return null;
    }

    private static readonly object BoxedFalse = false;
    private static readonly object BoxedByteZero = (byte)0;
    private static readonly object BoxedSByteZero = (sbyte)0;
    private static readonly object BoxedCharZero = '\0';
    private static readonly object BoxedShortZero = (short)0;
    private static readonly object BoxedUShortZero = (ushort)0;
    private static readonly object BoxedIntZero = 0;
    private static readonly object BoxedUIntZero = 0u;
    private static readonly object BoxedLongZero = 0L;
    private static readonly object BoxedULongZero = 0UL;
    private static readonly object BoxedFloatZero = 0f;
    private static readonly object BoxedDoubleZero = 0d;
    private static readonly object BoxedDecimalZero = 0m;
    private static readonly object BoxedDateTimeDefault = default(DateTime);
    private static readonly object BoxedTimeSpanZero = TimeSpan.Zero;
    private static readonly object BoxedGuidEmpty = Guid.Empty;
    private static readonly object BoxedDateTimeOffsetDefault = default(DateTimeOffset);
    private static readonly object BoxedThicknessDefault = default(Thickness);
    private static readonly object BoxedCornerRadiusDefault = default(CornerRadius);
    private static readonly object BoxedSizeEmpty = default(Size);
    private static readonly object BoxedPointZero = default(Point);
    private static readonly object BoxedRectEmpty = default(Rect);
}

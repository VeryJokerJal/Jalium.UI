using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Windows.Input;
using Jalium.UI.Markup;

namespace Jalium.UI.Input;

/// <summary>Converts command names used by markup into command instances.</summary>
public class CommandConverter : TypeConverter
{
    private static readonly Type[] s_knownCommandTypes =
    [
        typeof(ApplicationCommands),
        typeof(ComponentCommands),
        typeof(NavigationCommands),
    ];

    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    public override object? ConvertFrom(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object value)
    {
        if (value is not string text)
            return base.ConvertFrom(context, culture, value);

        text = text.Trim();
        if (text.Length == 0)
            return null;

        int separator = text.LastIndexOf('.');
        string? ownerName = separator < 0 ? null : text[..separator];
        string commandName = separator < 0 ? text : text[(separator + 1)..];

        foreach (Type ownerType in GetCandidateOwnerTypes(context, ownerName))
        {
            if (TryGetCommand(ownerType, commandName, out ICommand? command))
                return command;
        }

        throw new NotSupportedException($"Command '{text}' could not be resolved.");
    }

    public override object? ConvertTo(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object? value,
        Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);
        if (destinationType == typeof(string) && value is RoutedCommand routedCommand)
            return $"{routedCommand.OwnerType.Name}.{routedCommand.Name}";
        return base.ConvertTo(context, culture, value, destinationType);
    }

    private static IEnumerable<Type> GetCandidateOwnerTypes(
        ITypeDescriptorContext? context,
        string? ownerName)
    {
        if (ownerName is null)
        {
            if (context?.Instance is object instance)
                yield return instance as Type ?? instance.GetType();
            foreach (Type knownType in s_knownCommandTypes)
                yield return knownType;
            yield break;
        }

        foreach (Type knownType in s_knownCommandTypes)
        {
            if (string.Equals(ownerName, knownType.Name, StringComparison.Ordinal)
                || string.Equals(ownerName, knownType.FullName, StringComparison.Ordinal))
            {
                yield return knownType;
                yield break;
            }
        }

        if (context?.Instance is object contextInstance)
        {
            Type instanceType = contextInstance as Type ?? contextInstance.GetType();
            if (string.Equals(ownerName, instanceType.Name, StringComparison.Ordinal)
                || string.Equals(ownerName, instanceType.FullName, StringComparison.Ordinal))
            {
                yield return instanceType;
            }
        }
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = "CommandConverter intentionally resolves public static command members supplied by markup context.")]
    private static bool TryGetCommand(Type ownerType, string name, out ICommand? command)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        PropertyInfo? property = ownerType.GetProperty(name, flags);
        if (property?.CanRead == true && typeof(ICommand).IsAssignableFrom(property.PropertyType))
        {
            command = property.GetValue(null) as ICommand;
            return command is not null;
        }

        FieldInfo? field = ownerType.GetField(name, flags);
        if (field is not null && typeof(ICommand).IsAssignableFrom(field.FieldType))
        {
            command = field.GetValue(null) as ICommand;
            return command is not null;
        }

        command = null;
        return false;
    }
}

/// <summary>Converts <see cref="MouseAction"/> values to and from markup strings.</summary>
public class MouseActionConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    public override object? ConvertFrom(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object value)
    {
        if (value is string text)
        {
            text = text.Trim();
            if (text.Length == 0)
                return MouseAction.None;
            if (Enum.TryParse(text, ignoreCase: true, out MouseAction action) && Enum.IsDefined(action))
                return action;
            throw new InvalidEnumArgumentException(nameof(value), -1, typeof(MouseAction));
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
        if (destinationType == typeof(string) && value is MouseAction action && Enum.IsDefined(action))
            return action.ToString();
        return base.ConvertTo(context, culture, value, destinationType);
    }

    public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;
    public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => true;
    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context) =>
        new(Enum.GetValues<MouseAction>());
}

public class KeyValueSerializer : ValueSerializer
{
    private static readonly KeyConverter s_converter = new();

    public override bool CanConvertFromString(string value, IValueSerializerContext? context)
    {
        try
        {
            return value is not null && s_converter.ConvertFromInvariantString(value) is Key;
        }
        catch (Exception exception) when (exception is NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    public override bool CanConvertToString(object value, IValueSerializerContext? context) =>
        value is Key key && Enum.IsDefined(key);

    public override object ConvertFromString(string value, IValueSerializerContext? context) =>
        s_converter.ConvertFromInvariantString(value) ?? throw GetConvertFromException(value);

    public override string ConvertToString(object value, IValueSerializerContext? context) =>
        value is Key key && Enum.IsDefined(key)
            ? s_converter.ConvertToInvariantString(key) ?? throw GetConvertToException(value, typeof(string))
            : throw GetConvertToException(value, typeof(string));
}

public class ModifierKeysValueSerializer : ValueSerializer
{
    private static readonly ModifierKeysConverter s_converter = new();

    public override bool CanConvertFromString(string value, IValueSerializerContext? context)
    {
        try
        {
            return value is not null && s_converter.ConvertFromInvariantString(value) is ModifierKeys;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    public override bool CanConvertToString(object value, IValueSerializerContext? context) =>
        value is ModifierKeys modifiers && ModifierKeysConverter.IsDefinedModifierKeys(modifiers);

    public override object ConvertFromString(string value, IValueSerializerContext? context) =>
        s_converter.ConvertFromInvariantString(value) ?? throw GetConvertFromException(value);

    public override string ConvertToString(object value, IValueSerializerContext? context) =>
        value is ModifierKeys modifiers && ModifierKeysConverter.IsDefinedModifierKeys(modifiers)
            ? s_converter.ConvertToInvariantString(modifiers) ?? throw GetConvertToException(value, typeof(string))
            : throw GetConvertToException(value, typeof(string));
}

public class MouseActionValueSerializer : ValueSerializer
{
    private static readonly MouseActionConverter s_converter = new();

    public override bool CanConvertFromString(string value, IValueSerializerContext? context)
    {
        try
        {
            return value is not null && s_converter.ConvertFromInvariantString(value) is MouseAction;
        }
        catch (Exception exception) when (exception is NotSupportedException or InvalidEnumArgumentException)
        {
            return false;
        }
    }

    public override bool CanConvertToString(object value, IValueSerializerContext? context) =>
        value is MouseAction action && Enum.IsDefined(action);

    public override object ConvertFromString(string value, IValueSerializerContext? context) =>
        s_converter.ConvertFromInvariantString(value) ?? throw GetConvertFromException(value);

    public override string ConvertToString(object value, IValueSerializerContext? context) =>
        value is MouseAction action && Enum.IsDefined(action)
            ? s_converter.ConvertToInvariantString(action) ?? throw GetConvertToException(value, typeof(string))
            : throw GetConvertToException(value, typeof(string));
}

public class KeyGestureValueSerializer : ValueSerializer
{
    private static readonly KeyGestureConverter s_converter = new();

    public override bool CanConvertFromString(string value, IValueSerializerContext? context)
    {
        try
        {
            return value is not null && s_converter.ConvertFromInvariantString(value) is KeyGesture;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    public override bool CanConvertToString(object value, IValueSerializerContext? context) =>
        value is KeyGesture;

    public override object ConvertFromString(string value, IValueSerializerContext? context) =>
        s_converter.ConvertFromInvariantString(value) ?? throw GetConvertFromException(value);

    public override string ConvertToString(object value, IValueSerializerContext? context) =>
        value is KeyGesture gesture
            ? s_converter.ConvertToInvariantString(gesture) ?? throw GetConvertToException(value, typeof(string))
            : throw GetConvertToException(value, typeof(string));
}

public class MouseGestureValueSerializer : ValueSerializer
{
    private static readonly MouseGestureConverter s_converter = new();

    public override bool CanConvertFromString(string value, IValueSerializerContext? context)
    {
        try
        {
            return value is not null && s_converter.ConvertFromInvariantString(value) is MouseGesture;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    public override bool CanConvertToString(object value, IValueSerializerContext? context) =>
        value is MouseGesture;

    public override object ConvertFromString(string value, IValueSerializerContext? context) =>
        s_converter.ConvertFromInvariantString(value) ?? throw GetConvertFromException(value);

    public override string ConvertToString(object value, IValueSerializerContext? context) =>
        value is MouseGesture gesture
            ? s_converter.ConvertToInvariantString(gesture) ?? throw GetConvertToException(value, typeof(string))
            : throw GetConvertToException(value, typeof(string));
}

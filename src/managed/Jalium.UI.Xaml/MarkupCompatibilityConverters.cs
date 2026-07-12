using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using Jalium.UI.Xaml;
using ComponentModelTypeConverter = System.ComponentModel.TypeConverter;

namespace Jalium.UI.Markup;

public sealed class DependencyPropertyConverter : ComponentModelTypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
        => destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Converter resolves dependency-property owner fields named in XAML.")]
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is not string text) return base.ConvertFrom(context, culture, value);
        (Type? ownerType, string propertyName) = ResolveOwnerAndMember(text, context);
        if (ownerType is not null)
        {
            FieldInfo? field = ownerType.GetField(propertyName + "Property", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (field?.GetValue(null) is DependencyProperty property) return property;
        }
        throw GetConvertFromException(value);
    }

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
        => destinationType == typeof(string) && value is DependencyProperty property
            ? $"{property.OwnerType.Name}.{property.Name}"
            : base.ConvertTo(context, culture, value, destinationType);

    [UnconditionalSuppressMessage("Trimming", "IL2057", Justification = "The converter first uses IXamlTypeResolver/XamlTypeRegistry and retains Type.GetType only as the documented runtime fallback.")]
    internal static (Type? OwnerType, string MemberName) ResolveOwnerAndMember(string text, ITypeDescriptorContext? context)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        int separator = text.LastIndexOf('.');
        if (separator < 0) return (context?.PropertyDescriptor?.ComponentType, text);
        string ownerName = text[..separator];
        string memberName = text[(separator + 1)..];
        Type? owner = (context?.GetService(typeof(IXamlTypeResolver)) as IXamlTypeResolver)?.Resolve(ownerName)
            ?? XamlTypeRegistry.GetType(ownerName)
            ?? Type.GetType(ownerName, throwOnError: false);
        return (owner, memberName);
    }
}

public sealed class RoutedEventConverter : ComponentModelTypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? typeDescriptorContext, Type sourceType)
        => sourceType == typeof(string) || base.CanConvertFrom(typeDescriptorContext, sourceType);
    public override bool CanConvertTo(ITypeDescriptorContext? typeDescriptorContext, Type? destinationType)
        => destinationType == typeof(string) || base.CanConvertTo(typeDescriptorContext, destinationType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is not string text) return base.ConvertFrom(context, culture, value);
        (Type? owner, string eventName) = DependencyPropertyConverter.ResolveOwnerAndMember(text, context);
        RoutedEvent? routedEvent = owner is null
            ? EventManager.GetRoutedEvents().FirstOrDefault(item => item.Name == eventName)
            : EventManager.GetRoutedEventsForOwner(owner).FirstOrDefault(item => item.Name == eventName);
        return routedEvent ?? throw GetConvertFromException(value);
    }

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
        => destinationType == typeof(string) && value is RoutedEvent routedEvent
            ? $"{routedEvent.OwnerType.Name}.{routedEvent.Name}"
            : base.ConvertTo(context, culture, value, destinationType);
}

public sealed class EventSetterHandlerConverter : ComponentModelTypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
        => destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Event handler conversion resolves a user supplied handler method named in XAML.")]
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is not string methodName) return base.ConvertFrom(context, culture, value);
        object? target = (context?.GetService(typeof(IProvideValueTarget)) as IProvideValueTarget)?.TargetObject
            ?? context?.Instance;
        RoutedEvent? routedEvent = (context?.Instance as EventSetter)?.Event;
        if (target is null || routedEvent is null) throw GetConvertFromException(value);
        MethodInfo? method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return method is null ? throw GetConvertFromException(value) : Delegate.CreateDelegate(routedEvent.HandlerType, target, method);
    }

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
        => destinationType == typeof(string) && value is Delegate handler
            ? handler.Method.Name
            : base.ConvertTo(context, culture, value, destinationType);
}

public class NameReferenceConverter : ComponentModelTypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
        => destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is not string name) return base.ConvertFrom(context, culture, value);
        if (context?.GetService(typeof(Jalium.UI.Xaml.IXamlNameResolver)) is Jalium.UI.Xaml.IXamlNameResolver resolver)
            return resolver.Resolve(name) ?? throw GetConvertFromException(value);
        if (context?.GetService(typeof(INameScope)) is INameScope scope)
            return scope.FindName(name) ?? throw GetConvertFromException(value);
        throw GetConvertFromException(value);
    }

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        if (destinationType == typeof(string) && value is not null
            && context?.GetService(typeof(Jalium.UI.Xaml.IXamlNameProvider)) is Jalium.UI.Xaml.IXamlNameProvider provider)
            return provider.GetName(value) ?? throw GetConvertToException(value, destinationType);
        return base.ConvertTo(context, culture, value, destinationType);
    }
}

public class ResourceReferenceExpressionConverter : ExpressionConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) => sourceType == typeof(string);
    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) => destinationType == typeof(string);
    public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        => value is string key ? new DynamicResourceExtension(key) : throw GetConvertFromException(value);
    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
        => destinationType == typeof(string) && value is DynamicResourceExtension resource
            ? resource.ResourceKey?.ToString()
            : throw GetConvertToException(value, destinationType);
}

public sealed class SetterTriggerConditionValueConverter : ComponentModelTypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
        => destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Converter resolves the destination type explicitly supplied by the XAML writer context.")]
    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "The destination type is intentionally supplied at runtime by the XAML writer context.")]
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is not string text) return base.ConvertFrom(context, culture, value);
        Type? destinationType = (context?.GetService(typeof(IDestinationTypeProvider)) as IDestinationTypeProvider)?.GetDestinationType()
            ?? context?.PropertyDescriptor?.PropertyType;
        if (destinationType is null || destinationType == typeof(string)) return text;
        ComponentModelTypeConverter converter = TypeDescriptor.GetConverter(destinationType);
        return converter.CanConvertFrom(typeof(string)) ? converter.ConvertFrom(context, culture ?? CultureInfo.InvariantCulture, text) : throw GetConvertFromException(value);
    }

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
        => destinationType == typeof(string)
            ? Convert.ToString(value, culture ?? CultureInfo.InvariantCulture)
            : base.ConvertTo(context, culture, value, destinationType);
}

using System.Diagnostics.CodeAnalysis;

namespace Jalium.Extensions.DependencyInjection;

/// <summary>
/// Keyed-services TryAdd / Remove / Replace surface — partial of <see cref="ServiceCollectionDescriptorExtensions"/>
/// mirroring MS DI 8.0+ <c>ServiceCollectionDescriptorExtensions</c> keyed overloads.
/// </summary>
public static partial class ServiceCollectionDescriptorExtensions
{
    private static bool HasKeyedRegistration(IServiceCollection services, Type serviceType, object? serviceKey)
    {
        foreach (var d in services)
            if (d.IsKeyedService && d.ServiceType == serviceType && KeysMatch(d.ServiceKey, serviceKey)) return true;
        return false;
    }

    private static bool KeysMatch(object? a, object? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        return a.Equals(b);
    }

    // ── TryAddKeyedSingleton (7 overloads) ──────────────────────────────────
    public static void TryAddKeyedSingleton(this IServiceCollection services, Type serviceType, object? serviceKey, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type implementationType)
    { ArgumentNullException.ThrowIfNull(services); if (HasKeyedRegistration(services, serviceType, serviceKey)) return; services.Add(ServiceDescriptor.KeyedSingleton(serviceType, serviceKey, implementationType)); }

    public static void TryAddKeyedSingleton(this IServiceCollection services, Type serviceType, object? serviceKey, Func<IServiceProvider, object?, object> implementationFactory)
    { ArgumentNullException.ThrowIfNull(services); if (HasKeyedRegistration(services, serviceType, serviceKey)) return; services.Add(ServiceDescriptor.KeyedSingleton(serviceType, serviceKey, implementationFactory)); }

    public static void TryAddKeyedSingleton([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] this IServiceCollection services, Type serviceType, object? serviceKey)
    { ArgumentNullException.ThrowIfNull(services); if (HasKeyedRegistration(services, serviceType, serviceKey)) return; services.Add(ServiceDescriptor.KeyedSingleton(serviceType, serviceKey, serviceType)); }

    public static void TryAddKeyedSingleton<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TService>(this IServiceCollection services, object? serviceKey) where TService : class
    { if (HasKeyedRegistration(services, typeof(TService), serviceKey)) return; services.Add(ServiceDescriptor.KeyedSingleton(typeof(TService), serviceKey, typeof(TService))); }

    public static void TryAddKeyedSingleton<TService>(this IServiceCollection services, object? serviceKey, TService implementationInstance) where TService : class
    { ArgumentNullException.ThrowIfNull(implementationInstance); if (HasKeyedRegistration(services, typeof(TService), serviceKey)) return; services.Add(ServiceDescriptor.KeyedSingleton<TService>(serviceKey, implementationInstance)); }

    public static void TryAddKeyedSingleton<TService>(this IServiceCollection services, object? serviceKey, Func<IServiceProvider, object?, TService> implementationFactory) where TService : class
    { if (HasKeyedRegistration(services, typeof(TService), serviceKey)) return; services.Add(ServiceDescriptor.KeyedSingleton<TService>(serviceKey, implementationFactory)); }

    public static void TryAddKeyedSingleton<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(this IServiceCollection services, object? serviceKey)
        where TService : class where TImplementation : class, TService
    { if (HasKeyedRegistration(services, typeof(TService), serviceKey)) return; services.Add(ServiceDescriptor.KeyedSingleton<TService, TImplementation>(serviceKey)); }

    // ── TryAddKeyedScoped (6 overloads) ─────────────────────────────────────
    public static void TryAddKeyedScoped(this IServiceCollection services, Type serviceType, object? serviceKey, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type implementationType)
    { ArgumentNullException.ThrowIfNull(services); if (HasKeyedRegistration(services, serviceType, serviceKey)) return; services.Add(ServiceDescriptor.KeyedScoped(serviceType, serviceKey, implementationType)); }

    public static void TryAddKeyedScoped(this IServiceCollection services, Type serviceType, object? serviceKey, Func<IServiceProvider, object?, object> implementationFactory)
    { ArgumentNullException.ThrowIfNull(services); if (HasKeyedRegistration(services, serviceType, serviceKey)) return; services.Add(ServiceDescriptor.KeyedScoped(serviceType, serviceKey, implementationFactory)); }

    public static void TryAddKeyedScoped([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] this IServiceCollection services, Type serviceType, object? serviceKey)
    { ArgumentNullException.ThrowIfNull(services); if (HasKeyedRegistration(services, serviceType, serviceKey)) return; services.Add(ServiceDescriptor.KeyedScoped(serviceType, serviceKey, serviceType)); }

    public static void TryAddKeyedScoped<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TService>(this IServiceCollection services, object? serviceKey) where TService : class
    { if (HasKeyedRegistration(services, typeof(TService), serviceKey)) return; services.Add(ServiceDescriptor.KeyedScoped(typeof(TService), serviceKey, typeof(TService))); }

    public static void TryAddKeyedScoped<TService>(this IServiceCollection services, object? serviceKey, Func<IServiceProvider, object?, TService> implementationFactory) where TService : class
    { if (HasKeyedRegistration(services, typeof(TService), serviceKey)) return; services.Add(ServiceDescriptor.KeyedScoped<TService>(serviceKey, implementationFactory)); }

    public static void TryAddKeyedScoped<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(this IServiceCollection services, object? serviceKey)
        where TService : class where TImplementation : class, TService
    { if (HasKeyedRegistration(services, typeof(TService), serviceKey)) return; services.Add(ServiceDescriptor.KeyedScoped<TService, TImplementation>(serviceKey)); }

    // ── TryAddKeyedTransient (6 overloads) ──────────────────────────────────
    public static void TryAddKeyedTransient(this IServiceCollection services, Type serviceType, object? serviceKey, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type implementationType)
    { ArgumentNullException.ThrowIfNull(services); if (HasKeyedRegistration(services, serviceType, serviceKey)) return; services.Add(ServiceDescriptor.KeyedTransient(serviceType, serviceKey, implementationType)); }

    public static void TryAddKeyedTransient(this IServiceCollection services, Type serviceType, object? serviceKey, Func<IServiceProvider, object?, object> implementationFactory)
    { ArgumentNullException.ThrowIfNull(services); if (HasKeyedRegistration(services, serviceType, serviceKey)) return; services.Add(ServiceDescriptor.KeyedTransient(serviceType, serviceKey, implementationFactory)); }

    public static void TryAddKeyedTransient([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] this IServiceCollection services, Type serviceType, object? serviceKey)
    { ArgumentNullException.ThrowIfNull(services); if (HasKeyedRegistration(services, serviceType, serviceKey)) return; services.Add(ServiceDescriptor.KeyedTransient(serviceType, serviceKey, serviceType)); }

    public static void TryAddKeyedTransient<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TService>(this IServiceCollection services, object? serviceKey) where TService : class
    { if (HasKeyedRegistration(services, typeof(TService), serviceKey)) return; services.Add(ServiceDescriptor.KeyedTransient(typeof(TService), serviceKey, typeof(TService))); }

    public static void TryAddKeyedTransient<TService>(this IServiceCollection services, object? serviceKey, Func<IServiceProvider, object?, TService> implementationFactory) where TService : class
    { if (HasKeyedRegistration(services, typeof(TService), serviceKey)) return; services.Add(ServiceDescriptor.KeyedTransient<TService>(serviceKey, implementationFactory)); }

    public static void TryAddKeyedTransient<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(this IServiceCollection services, object? serviceKey)
        where TService : class where TImplementation : class, TService
    { if (HasKeyedRegistration(services, typeof(TService), serviceKey)) return; services.Add(ServiceDescriptor.KeyedTransient<TService, TImplementation>(serviceKey)); }

    // ── Remove / Replace — keyed variants ──────────────────────────────────
    public static IServiceCollection RemoveAllKeyed(this IServiceCollection collection, Type serviceType, object? serviceKey)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(serviceType);
        for (int i = collection.Count - 1; i >= 0; i--)
        {
            var d = collection[i];
            if (d.IsKeyedService && d.ServiceType == serviceType && KeysMatch(d.ServiceKey, serviceKey))
                collection.RemoveAt(i);
        }
        return collection;
    }

    public static IServiceCollection RemoveAllKeyed<T>(this IServiceCollection collection, object? serviceKey)
        => collection.RemoveAllKeyed(typeof(T), serviceKey);

    public static IServiceCollection ReplaceKeyed(this IServiceCollection collection, ServiceDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(descriptor);
        if (!descriptor.IsKeyedService)
            throw new ArgumentException("Descriptor is not a keyed registration.", nameof(descriptor));
        for (int i = 0; i < collection.Count; i++)
        {
            var d = collection[i];
            if (d.IsKeyedService && d.ServiceType == descriptor.ServiceType && KeysMatch(d.ServiceKey, descriptor.ServiceKey))
            {
                collection.RemoveAt(i);
                break;
            }
        }
        collection.Add(descriptor);
        return collection;
    }

    public static void TryAddEnumerableKeyed(this IServiceCollection services, ServiceDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(descriptor);
        if (!descriptor.IsKeyedService) { services.TryAddEnumerable(descriptor); return; }

        var implType = descriptor.GetImplementationType();
        foreach (var d in services)
        {
            if (d.IsKeyedService && d.ServiceType == descriptor.ServiceType
                && KeysMatch(d.ServiceKey, descriptor.ServiceKey)
                && d.GetImplementationType() == implType)
                return;
        }
        services.Add(descriptor);
    }
}

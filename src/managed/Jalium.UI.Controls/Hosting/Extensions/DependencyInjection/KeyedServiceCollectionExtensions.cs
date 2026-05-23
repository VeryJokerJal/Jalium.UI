using System.Diagnostics.CodeAnalysis;

namespace Jalium.Extensions.DependencyInjection;

/// <summary>
/// Keyed-services Add* surface — partial of <see cref="ServiceCollectionServiceExtensions"/>
/// mirroring MS DI 8.0+ <c>Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions</c>
/// keyed overloads.
/// </summary>
public static partial class ServiceCollectionServiceExtensions
{
    // ═══════════════════════════════════════════════════════════════════════════
    // AddKeyedSingleton — 9 overloads matching MS
    // ═══════════════════════════════════════════════════════════════════════════
    public static IServiceCollection AddKeyedSingleton(this IServiceCollection services, Type serviceType, object? serviceKey, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type implementationType)
    { ArgumentNullException.ThrowIfNull(services); ArgumentNullException.ThrowIfNull(serviceType); ArgumentNullException.ThrowIfNull(implementationType); services.Add(ServiceDescriptor.KeyedSingleton(serviceType, serviceKey, implementationType)); return services; }

    public static IServiceCollection AddKeyedSingleton(this IServiceCollection services, Type serviceType, object? serviceKey, Func<IServiceProvider, object?, object> implementationFactory)
    { ArgumentNullException.ThrowIfNull(services); ArgumentNullException.ThrowIfNull(implementationFactory); services.Add(ServiceDescriptor.KeyedSingleton(serviceType, serviceKey, implementationFactory)); return services; }

    public static IServiceCollection AddKeyedSingleton(this IServiceCollection services, Type serviceType, object? serviceKey, object implementationInstance)
    { ArgumentNullException.ThrowIfNull(services); ArgumentNullException.ThrowIfNull(implementationInstance); services.Add(ServiceDescriptor.KeyedSingleton(serviceType, serviceKey, implementationInstance)); return services; }

    public static IServiceCollection AddKeyedSingleton([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] this IServiceCollection services, Type serviceType, object? serviceKey)
    { ArgumentNullException.ThrowIfNull(services); services.Add(ServiceDescriptor.KeyedSingleton(serviceType, serviceKey, serviceType)); return services; }

    public static IServiceCollection AddKeyedSingleton<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TService>(this IServiceCollection services, object? serviceKey) where TService : class
    { services.Add(ServiceDescriptor.KeyedSingleton(typeof(TService), serviceKey, typeof(TService))); return services; }

    public static IServiceCollection AddKeyedSingleton<TService>(this IServiceCollection services, object? serviceKey, Func<IServiceProvider, object?, TService> implementationFactory) where TService : class
    { services.Add(ServiceDescriptor.KeyedSingleton<TService>(serviceKey, implementationFactory)); return services; }

    public static IServiceCollection AddKeyedSingleton<TService>(this IServiceCollection services, object? serviceKey, TService implementationInstance) where TService : class
    { ArgumentNullException.ThrowIfNull(implementationInstance); services.Add(ServiceDescriptor.KeyedSingleton<TService>(serviceKey, implementationInstance)); return services; }

    public static IServiceCollection AddKeyedSingleton<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(this IServiceCollection services, object? serviceKey)
        where TService : class where TImplementation : class, TService
    { services.Add(ServiceDescriptor.KeyedSingleton<TService, TImplementation>(serviceKey)); return services; }

    public static IServiceCollection AddKeyedSingleton<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(this IServiceCollection services, object? serviceKey, Func<IServiceProvider, object?, TImplementation> implementationFactory)
        where TService : class where TImplementation : class, TService
    { services.Add(ServiceDescriptor.KeyedSingleton<TService, TImplementation>(serviceKey, implementationFactory)); return services; }

    // ═══════════════════════════════════════════════════════════════════════════
    // AddKeyedScoped — 7 overloads
    // ═══════════════════════════════════════════════════════════════════════════
    public static IServiceCollection AddKeyedScoped(this IServiceCollection services, Type serviceType, object? serviceKey, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type implementationType)
    { ArgumentNullException.ThrowIfNull(services); ArgumentNullException.ThrowIfNull(serviceType); ArgumentNullException.ThrowIfNull(implementationType); services.Add(ServiceDescriptor.KeyedScoped(serviceType, serviceKey, implementationType)); return services; }

    public static IServiceCollection AddKeyedScoped(this IServiceCollection services, Type serviceType, object? serviceKey, Func<IServiceProvider, object?, object> implementationFactory)
    { ArgumentNullException.ThrowIfNull(services); ArgumentNullException.ThrowIfNull(implementationFactory); services.Add(ServiceDescriptor.KeyedScoped(serviceType, serviceKey, implementationFactory)); return services; }

    public static IServiceCollection AddKeyedScoped([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] this IServiceCollection services, Type serviceType, object? serviceKey)
    { ArgumentNullException.ThrowIfNull(services); services.Add(ServiceDescriptor.KeyedScoped(serviceType, serviceKey, serviceType)); return services; }

    public static IServiceCollection AddKeyedScoped<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TService>(this IServiceCollection services, object? serviceKey) where TService : class
    { services.Add(ServiceDescriptor.KeyedScoped(typeof(TService), serviceKey, typeof(TService))); return services; }

    public static IServiceCollection AddKeyedScoped<TService>(this IServiceCollection services, object? serviceKey, Func<IServiceProvider, object?, TService> implementationFactory) where TService : class
    { services.Add(ServiceDescriptor.KeyedScoped<TService>(serviceKey, implementationFactory)); return services; }

    public static IServiceCollection AddKeyedScoped<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(this IServiceCollection services, object? serviceKey)
        where TService : class where TImplementation : class, TService
    { services.Add(ServiceDescriptor.KeyedScoped<TService, TImplementation>(serviceKey)); return services; }

    public static IServiceCollection AddKeyedScoped<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(this IServiceCollection services, object? serviceKey, Func<IServiceProvider, object?, TImplementation> implementationFactory)
        where TService : class where TImplementation : class, TService
    { services.Add(ServiceDescriptor.KeyedScoped<TService, TImplementation>(serviceKey, implementationFactory)); return services; }

    // ═══════════════════════════════════════════════════════════════════════════
    // AddKeyedTransient — 7 overloads
    // ═══════════════════════════════════════════════════════════════════════════
    public static IServiceCollection AddKeyedTransient(this IServiceCollection services, Type serviceType, object? serviceKey, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type implementationType)
    { ArgumentNullException.ThrowIfNull(services); ArgumentNullException.ThrowIfNull(serviceType); ArgumentNullException.ThrowIfNull(implementationType); services.Add(ServiceDescriptor.KeyedTransient(serviceType, serviceKey, implementationType)); return services; }

    public static IServiceCollection AddKeyedTransient(this IServiceCollection services, Type serviceType, object? serviceKey, Func<IServiceProvider, object?, object> implementationFactory)
    { ArgumentNullException.ThrowIfNull(services); ArgumentNullException.ThrowIfNull(implementationFactory); services.Add(ServiceDescriptor.KeyedTransient(serviceType, serviceKey, implementationFactory)); return services; }

    public static IServiceCollection AddKeyedTransient([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] this IServiceCollection services, Type serviceType, object? serviceKey)
    { ArgumentNullException.ThrowIfNull(services); services.Add(ServiceDescriptor.KeyedTransient(serviceType, serviceKey, serviceType)); return services; }

    public static IServiceCollection AddKeyedTransient<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TService>(this IServiceCollection services, object? serviceKey) where TService : class
    { services.Add(ServiceDescriptor.KeyedTransient(typeof(TService), serviceKey, typeof(TService))); return services; }

    public static IServiceCollection AddKeyedTransient<TService>(this IServiceCollection services, object? serviceKey, Func<IServiceProvider, object?, TService> implementationFactory) where TService : class
    { services.Add(ServiceDescriptor.KeyedTransient<TService>(serviceKey, implementationFactory)); return services; }

    public static IServiceCollection AddKeyedTransient<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(this IServiceCollection services, object? serviceKey)
        where TService : class where TImplementation : class, TService
    { services.Add(ServiceDescriptor.KeyedTransient<TService, TImplementation>(serviceKey)); return services; }

    public static IServiceCollection AddKeyedTransient<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(this IServiceCollection services, object? serviceKey, Func<IServiceProvider, object?, TImplementation> implementationFactory)
        where TService : class where TImplementation : class, TService
    { services.Add(ServiceDescriptor.KeyedTransient<TService, TImplementation>(serviceKey, implementationFactory)); return services; }
}

/// <summary>
/// <see cref="IKeyedServiceProvider"/> resolution shortcuts — analogous to
/// <c>ServiceProviderServiceExtensions</c> but for keyed services.
/// </summary>
public static class ServiceProviderKeyedServiceExtensions
{
    public static T? GetKeyedService<T>(this IServiceProvider provider, object? serviceKey)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (provider is IKeyedServiceProvider ksp) return (T?)ksp.GetKeyedService(typeof(T), serviceKey);
        throw new InvalidOperationException("This service provider does not support keyed services.");
    }

    public static object? GetKeyedService(this IServiceProvider provider, Type serviceType, object? serviceKey)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(serviceType);
        if (provider is IKeyedServiceProvider ksp) return ksp.GetKeyedService(serviceType, serviceKey);
        throw new InvalidOperationException("This service provider does not support keyed services.");
    }

    public static T GetRequiredKeyedService<T>(this IServiceProvider provider, object? serviceKey) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (provider is IKeyedServiceProvider ksp) return (T)ksp.GetRequiredKeyedService(typeof(T), serviceKey);
        throw new InvalidOperationException("This service provider does not support keyed services.");
    }

    public static object GetRequiredKeyedService(this IServiceProvider provider, Type serviceType, object? serviceKey)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(serviceType);
        if (provider is IKeyedServiceProvider ksp) return ksp.GetRequiredKeyedService(serviceType, serviceKey);
        throw new InvalidOperationException("This service provider does not support keyed services.");
    }

    public static IEnumerable<T> GetKeyedServices<T>(this IServiceProvider provider, object? serviceKey)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var result = (IEnumerable<T>?)provider.GetKeyedService(typeof(IEnumerable<T>), serviceKey);
        return result ?? Array.Empty<T>();
    }

    public static IEnumerable<object?> GetKeyedServices(this IServiceProvider provider, Type serviceType, object? serviceKey)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(serviceType);
        var enumerableType = typeof(IEnumerable<>).MakeGenericType(serviceType);
        var result = provider.GetKeyedService(enumerableType, serviceKey);
        return ((System.Collections.IEnumerable?)result)?.Cast<object?>() ?? Array.Empty<object?>();
    }
}

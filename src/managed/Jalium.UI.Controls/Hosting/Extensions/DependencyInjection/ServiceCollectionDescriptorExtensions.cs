using System.Diagnostics.CodeAnalysis;

namespace Jalium.Extensions.DependencyInjection;

/// <summary>
/// Add* / TryAdd* / RemoveAll extensions on <see cref="IServiceCollection"/>. Mirrors MS
/// <c>Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions</c>
/// — interface contract and behavior are identical, only the namespace changes.
/// </summary>
public static partial class ServiceCollectionDescriptorExtensions
{
    public static IServiceCollection Add(this IServiceCollection collection, IEnumerable<ServiceDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(descriptors);
        foreach (var d in descriptors) collection.Add(d);
        return collection;
    }

    public static void TryAdd(this IServiceCollection collection, ServiceDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(descriptor);
        foreach (var d in collection) if (d.ServiceType == descriptor.ServiceType) return;
        collection.Add(descriptor);
    }

    public static void TryAdd(this IServiceCollection collection, IEnumerable<ServiceDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        foreach (var d in descriptors) collection.TryAdd(d);
    }

    public static void TryAddSingleton(this IServiceCollection services, Type service)
        => services.TryAdd(ServiceDescriptor.Singleton(service, service));

    public static void TryAddSingleton(this IServiceCollection services, Type service, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type implementationType)
        => services.TryAdd(ServiceDescriptor.Singleton(service, implementationType));

    public static void TryAddSingleton(this IServiceCollection services, Type service, Func<IServiceProvider, object> factory)
        => services.TryAdd(ServiceDescriptor.Singleton(service, factory));

    public static void TryAddSingleton<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TService>(this IServiceCollection services)
        where TService : class => services.TryAdd(ServiceDescriptor.Singleton(typeof(TService), typeof(TService)));

    public static void TryAddSingleton<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(this IServiceCollection services)
        where TService : class where TImplementation : class, TService
        => services.TryAdd(ServiceDescriptor.Singleton<TService, TImplementation>());

    public static void TryAddSingleton<TService>(this IServiceCollection services, TService instance) where TService : class
        => services.TryAdd(new ServiceDescriptor(typeof(TService), instance));

    public static void TryAddSingleton<TService>(this IServiceCollection services, Func<IServiceProvider, TService> factory) where TService : class
        => services.TryAdd(ServiceDescriptor.Singleton(typeof(TService), sp => factory(sp)));

    public static void TryAddTransient(this IServiceCollection services, Type service)
        => services.TryAdd(ServiceDescriptor.Transient(service, service));

    public static void TryAddTransient(this IServiceCollection services, Type service, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type implementationType)
        => services.TryAdd(ServiceDescriptor.Transient(service, implementationType));

    public static void TryAddTransient(this IServiceCollection services, Type service, Func<IServiceProvider, object> factory)
        => services.TryAdd(ServiceDescriptor.Transient(service, factory));

    public static void TryAddTransient<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TService>(this IServiceCollection services)
        where TService : class => services.TryAdd(ServiceDescriptor.Transient(typeof(TService), typeof(TService)));

    public static void TryAddTransient<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(this IServiceCollection services)
        where TService : class where TImplementation : class, TService
        => services.TryAdd(ServiceDescriptor.Transient<TService, TImplementation>());

    public static void TryAddTransient<TService>(this IServiceCollection services, Func<IServiceProvider, TService> factory) where TService : class
        => services.TryAdd(ServiceDescriptor.Transient(typeof(TService), sp => factory(sp)));

    public static void TryAddScoped(this IServiceCollection services, Type service)
        => services.TryAdd(ServiceDescriptor.Scoped(service, service));

    public static void TryAddScoped(this IServiceCollection services, Type service, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type implementationType)
        => services.TryAdd(ServiceDescriptor.Scoped(service, implementationType));

    public static void TryAddScoped(this IServiceCollection services, Type service, Func<IServiceProvider, object> factory)
        => services.TryAdd(ServiceDescriptor.Scoped(service, factory));

    public static void TryAddScoped<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TService>(this IServiceCollection services)
        where TService : class => services.TryAdd(ServiceDescriptor.Scoped(typeof(TService), typeof(TService)));

    public static void TryAddScoped<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(this IServiceCollection services)
        where TService : class where TImplementation : class, TService
        => services.TryAdd(ServiceDescriptor.Scoped<TService, TImplementation>());

    public static void TryAddScoped<TService>(this IServiceCollection services, Func<IServiceProvider, TService> factory) where TService : class
        => services.TryAdd(ServiceDescriptor.Scoped(typeof(TService), sp => factory(sp)));

    /// <summary>Adds <paramref name="descriptor"/> only if no descriptor with the same service+implementation pair already exists.</summary>
    public static void TryAddEnumerable(this IServiceCollection services, ServiceDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(descriptor);
        var implType = descriptor.GetImplementationType();
        foreach (var d in services)
        {
            if (d.ServiceType == descriptor.ServiceType && d.GetImplementationType() == implType) return;
        }
        services.Add(descriptor);
    }

    public static void TryAddEnumerable(this IServiceCollection services, IEnumerable<ServiceDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        foreach (var d in descriptors) services.TryAddEnumerable(d);
    }

    public static IServiceCollection RemoveAll(this IServiceCollection collection, Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(serviceType);
        for (int i = collection.Count - 1; i >= 0; i--)
            if (collection[i].ServiceType == serviceType) collection.RemoveAt(i);
        return collection;
    }

    public static IServiceCollection RemoveAll<T>(this IServiceCollection collection) => collection.RemoveAll(typeof(T));

    public static IServiceCollection Replace(this IServiceCollection collection, ServiceDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(descriptor);
        for (int i = 0; i < collection.Count; i++)
        {
            if (collection[i].ServiceType == descriptor.ServiceType)
            {
                collection.RemoveAt(i);
                break;
            }
        }
        collection.Add(descriptor);
        return collection;
    }
}

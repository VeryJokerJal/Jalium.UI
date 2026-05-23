using System.Diagnostics.CodeAnalysis;

namespace Jalium.Extensions.DependencyInjection;

/// <summary>
/// Describes a single service registration: service type, implementation/factory/instance, and lifetime.
/// </summary>
public sealed class ServiceDescriptor
{
    public ServiceDescriptor(
        Type serviceType,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type implementationType,
        ServiceLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentNullException.ThrowIfNull(implementationType);
        ServiceType = serviceType;
        ImplementationType = implementationType;
        Lifetime = lifetime;
    }

    public ServiceDescriptor(Type serviceType, object instance)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentNullException.ThrowIfNull(instance);
        ServiceType = serviceType;
        ImplementationInstance = instance;
        Lifetime = ServiceLifetime.Singleton;
    }

    public ServiceDescriptor(Type serviceType, Func<IServiceProvider, object> factory, ServiceLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentNullException.ThrowIfNull(factory);
        ServiceType = serviceType;
        ImplementationFactory = factory;
        Lifetime = lifetime;
    }

    // ── Keyed ctors ──────────────────────────────────────────────────────────
    public ServiceDescriptor(
        Type serviceType, object? serviceKey,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type implementationType,
        ServiceLifetime lifetime)
        : this(serviceType, implementationType, lifetime) { ServiceKey = serviceKey; IsKeyedService = true; }

    public ServiceDescriptor(Type serviceType, object? serviceKey, object instance)
        : this(serviceType, instance) { ServiceKey = serviceKey; IsKeyedService = true; }

    public ServiceDescriptor(Type serviceType, object? serviceKey, Func<IServiceProvider, object?, object> keyedFactory, ServiceLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentNullException.ThrowIfNull(keyedFactory);
        ServiceType = serviceType;
        ServiceKey = serviceKey;
        KeyedImplementationFactory = keyedFactory;
        Lifetime = lifetime;
        IsKeyedService = true;
    }

    public Type ServiceType { get; }
    public ServiceLifetime Lifetime { get; }

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    public Type? ImplementationType { get; }
    public object? ImplementationInstance { get; }
    public Func<IServiceProvider, object>? ImplementationFactory { get; }

    /// <summary><see langword="true"/> when this descriptor was registered with a key.</summary>
    public bool IsKeyedService { get; }
    /// <summary>The key value when <see cref="IsKeyedService"/> is <see langword="true"/>; <see langword="null"/> otherwise. <see cref="KeyedService.AnyKey"/> matches any key at resolution time.</summary>
    public object? ServiceKey { get; }
    public Func<IServiceProvider, object?, object>? KeyedImplementationFactory { get; }

    public Type GetImplementationType()
    {
        if (ImplementationType != null) return ImplementationType;
        if (ImplementationInstance != null) return ImplementationInstance.GetType();
        if (ImplementationFactory != null)
        {
            var typeArgs = ImplementationFactory.GetType().GenericTypeArguments;
            return typeArgs[1];
        }
        if (KeyedImplementationFactory != null)
        {
            var typeArgs = KeyedImplementationFactory.GetType().GenericTypeArguments;
            return typeArgs[2];
        }
        return ServiceType;
    }

    public static ServiceDescriptor Singleton<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>()
        where TService : class where TImplementation : class, TService
        => new(typeof(TService), typeof(TImplementation), ServiceLifetime.Singleton);

    public static ServiceDescriptor Singleton<TService>(TService instance) where TService : class
        => new(typeof(TService), instance);

    public static ServiceDescriptor Singleton<TService>(Func<IServiceProvider, TService> factory) where TService : class
        => new(typeof(TService), sp => factory(sp), ServiceLifetime.Singleton);

    public static ServiceDescriptor Singleton(Type service, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type implementationType)
        => new(service, implementationType, ServiceLifetime.Singleton);

    public static ServiceDescriptor Singleton(Type service, Func<IServiceProvider, object> factory)
        => new(service, factory, ServiceLifetime.Singleton);

    public static ServiceDescriptor Scoped<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>()
        where TService : class where TImplementation : class, TService
        => new(typeof(TService), typeof(TImplementation), ServiceLifetime.Scoped);

    public static ServiceDescriptor Scoped<TService>(Func<IServiceProvider, TService> factory) where TService : class
        => new(typeof(TService), sp => factory(sp), ServiceLifetime.Scoped);

    public static ServiceDescriptor Scoped(Type service, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type implementationType)
        => new(service, implementationType, ServiceLifetime.Scoped);

    public static ServiceDescriptor Scoped(Type service, Func<IServiceProvider, object> factory)
        => new(service, factory, ServiceLifetime.Scoped);

    public static ServiceDescriptor Transient<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>()
        where TService : class where TImplementation : class, TService
        => new(typeof(TService), typeof(TImplementation), ServiceLifetime.Transient);

    public static ServiceDescriptor Transient<TService>(Func<IServiceProvider, TService> factory) where TService : class
        => new(typeof(TService), sp => factory(sp), ServiceLifetime.Transient);

    public static ServiceDescriptor Transient(Type service, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type implementationType)
        => new(service, implementationType, ServiceLifetime.Transient);

    public static ServiceDescriptor Transient(Type service, Func<IServiceProvider, object> factory)
        => new(service, factory, ServiceLifetime.Transient);

    public static ServiceDescriptor Describe(Type service, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type implementationType, ServiceLifetime lifetime)
        => new(service, implementationType, lifetime);

    public static ServiceDescriptor Describe(Type service, Func<IServiceProvider, object> factory, ServiceLifetime lifetime)
        => new(service, factory, lifetime);

    // ── Keyed factory methods ────────────────────────────────────────────────
    public static ServiceDescriptor KeyedSingleton<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(object? serviceKey)
        where TService : class where TImplementation : class, TService
        => new(typeof(TService), serviceKey, typeof(TImplementation), ServiceLifetime.Singleton);

    public static ServiceDescriptor KeyedSingleton<TService>(object? serviceKey, TService instance) where TService : class
        => new(typeof(TService), serviceKey, instance);

    public static ServiceDescriptor KeyedSingleton<TService>(object? serviceKey, Func<IServiceProvider, object?, TService> factory) where TService : class
        => new(typeof(TService), serviceKey, (sp, key) => factory(sp, key), ServiceLifetime.Singleton);

    public static ServiceDescriptor KeyedSingleton(Type service, object? serviceKey, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type implementationType)
        => new(service, serviceKey, implementationType, ServiceLifetime.Singleton);

    public static ServiceDescriptor KeyedSingleton(Type service, object? serviceKey, Func<IServiceProvider, object?, object> factory)
        => new(service, serviceKey, factory, ServiceLifetime.Singleton);

    public static ServiceDescriptor KeyedSingleton(Type service, object? serviceKey, object instance)
        => new(service, serviceKey, instance);

    public static ServiceDescriptor KeyedSingleton<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(object? serviceKey, Func<IServiceProvider, object?, TImplementation> factory)
        where TService : class where TImplementation : class, TService
        => new(typeof(TService), serviceKey, (sp, key) => factory(sp, key), ServiceLifetime.Singleton);

    public static ServiceDescriptor KeyedScoped<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(object? serviceKey)
        where TService : class where TImplementation : class, TService
        => new(typeof(TService), serviceKey, typeof(TImplementation), ServiceLifetime.Scoped);

    public static ServiceDescriptor KeyedScoped<TService>(object? serviceKey, Func<IServiceProvider, object?, TService> factory) where TService : class
        => new(typeof(TService), serviceKey, (sp, key) => factory(sp, key), ServiceLifetime.Scoped);

    public static ServiceDescriptor KeyedScoped<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(object? serviceKey, Func<IServiceProvider, object?, TImplementation> factory)
        where TService : class where TImplementation : class, TService
        => new(typeof(TService), serviceKey, (sp, key) => factory(sp, key), ServiceLifetime.Scoped);

    public static ServiceDescriptor KeyedScoped(Type service, object? serviceKey, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type implementationType)
        => new(service, serviceKey, implementationType, ServiceLifetime.Scoped);

    public static ServiceDescriptor KeyedScoped(Type service, object? serviceKey, Func<IServiceProvider, object?, object> factory)
        => new(service, serviceKey, factory, ServiceLifetime.Scoped);

    public static ServiceDescriptor KeyedTransient<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(object? serviceKey)
        where TService : class where TImplementation : class, TService
        => new(typeof(TService), serviceKey, typeof(TImplementation), ServiceLifetime.Transient);

    public static ServiceDescriptor KeyedTransient<TService>(object? serviceKey, Func<IServiceProvider, object?, TService> factory) where TService : class
        => new(typeof(TService), serviceKey, (sp, key) => factory(sp, key), ServiceLifetime.Transient);

    public static ServiceDescriptor KeyedTransient<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(object? serviceKey, Func<IServiceProvider, object?, TImplementation> factory)
        where TService : class where TImplementation : class, TService
        => new(typeof(TService), serviceKey, (sp, key) => factory(sp, key), ServiceLifetime.Transient);

    public static ServiceDescriptor KeyedTransient(Type service, object? serviceKey, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type implementationType)
        => new(service, serviceKey, implementationType, ServiceLifetime.Transient);

    public static ServiceDescriptor KeyedTransient(Type service, object? serviceKey, Func<IServiceProvider, object?, object> factory)
        => new(service, serviceKey, factory, ServiceLifetime.Transient);

    public static ServiceDescriptor DescribeKeyed(Type service, object? serviceKey, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type implementationType, ServiceLifetime lifetime)
        => new(service, serviceKey, implementationType, lifetime);

    public override string ToString()
    {
        if (IsKeyedService)
            return $"ServiceType: {ServiceType.FullName} Key: {ServiceKey ?? "(null)"} Lifetime: {Lifetime} ImplementationType: {GetImplementationType().FullName}";
        return $"ServiceType: {ServiceType.FullName} Lifetime: {Lifetime} ImplementationType: {GetImplementationType().FullName}";
    }
}

/// <summary>Sentinel key value matching every key (used by <c>GetKeyedServices</c>).</summary>
public static class KeyedService
{
    /// <summary>Reference-equality sentinel; pass to <c>GetKeyedServices</c> to fetch every key for a service type.</summary>
    public static object AnyKey { get; } = new AnyKeySentinel();
    internal sealed class AnyKeySentinel { public override string ToString() => "(any)"; }
}

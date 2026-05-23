using System.Diagnostics.CodeAnalysis;

namespace Jalium.Extensions.DependencyInjection;

/// <summary>
/// AddSingleton / AddTransient / AddScoped extensions on <see cref="IServiceCollection"/>.
/// Signatures mirror the MS equivalents one-for-one.
/// </summary>
public static partial class ServiceCollectionServiceExtensions
{
    // ── Singleton ───────────────────────────────────────────────────────────
    public static IServiceCollection AddSingleton(this IServiceCollection services, Type service, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type implementationType)
    { services.Add(ServiceDescriptor.Singleton(service, implementationType)); return services; }

    public static IServiceCollection AddSingleton(this IServiceCollection services, Type service, Func<IServiceProvider, object> factory)
    { services.Add(ServiceDescriptor.Singleton(service, factory)); return services; }

    public static IServiceCollection AddSingleton(this IServiceCollection services, Type service, object instance)
    { ArgumentNullException.ThrowIfNull(instance); services.Add(new ServiceDescriptor(service, instance)); return services; }

    public static IServiceCollection AddSingleton([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] this IServiceCollection services, Type service)
    { services.Add(ServiceDescriptor.Singleton(service, service)); return services; }

    public static IServiceCollection AddSingleton<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(this IServiceCollection services)
        where TService : class where TImplementation : class, TService
    { services.Add(ServiceDescriptor.Singleton<TService, TImplementation>()); return services; }

    public static IServiceCollection AddSingleton<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TService>(this IServiceCollection services)
        where TService : class
    { services.Add(ServiceDescriptor.Singleton(typeof(TService), typeof(TService))); return services; }

    public static IServiceCollection AddSingleton<TService>(this IServiceCollection services, TService instance) where TService : class
    { ArgumentNullException.ThrowIfNull(instance); services.Add(new ServiceDescriptor(typeof(TService), instance)); return services; }

    public static IServiceCollection AddSingleton<TService>(this IServiceCollection services, Func<IServiceProvider, TService> factory) where TService : class
    { services.Add(ServiceDescriptor.Singleton(typeof(TService), sp => factory(sp))); return services; }

    public static IServiceCollection AddSingleton<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(this IServiceCollection services, Func<IServiceProvider, TImplementation> factory)
        where TService : class where TImplementation : class, TService
    { services.Add(ServiceDescriptor.Singleton(typeof(TService), sp => factory(sp))); return services; }

    // ── Transient ───────────────────────────────────────────────────────────
    public static IServiceCollection AddTransient(this IServiceCollection services, Type service, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type implementationType)
    { services.Add(ServiceDescriptor.Transient(service, implementationType)); return services; }

    public static IServiceCollection AddTransient(this IServiceCollection services, Type service, Func<IServiceProvider, object> factory)
    { services.Add(ServiceDescriptor.Transient(service, factory)); return services; }

    public static IServiceCollection AddTransient([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] this IServiceCollection services, Type service)
    { services.Add(ServiceDescriptor.Transient(service, service)); return services; }

    public static IServiceCollection AddTransient<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(this IServiceCollection services)
        where TService : class where TImplementation : class, TService
    { services.Add(ServiceDescriptor.Transient<TService, TImplementation>()); return services; }

    public static IServiceCollection AddTransient<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TService>(this IServiceCollection services)
        where TService : class
    { services.Add(ServiceDescriptor.Transient(typeof(TService), typeof(TService))); return services; }

    public static IServiceCollection AddTransient<TService>(this IServiceCollection services, Func<IServiceProvider, TService> factory) where TService : class
    { services.Add(ServiceDescriptor.Transient(typeof(TService), sp => factory(sp))); return services; }

    public static IServiceCollection AddTransient<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(this IServiceCollection services, Func<IServiceProvider, TImplementation> factory)
        where TService : class where TImplementation : class, TService
    { services.Add(ServiceDescriptor.Transient(typeof(TService), sp => factory(sp))); return services; }

    // ── Scoped ──────────────────────────────────────────────────────────────
    public static IServiceCollection AddScoped(this IServiceCollection services, Type service, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type implementationType)
    { services.Add(ServiceDescriptor.Scoped(service, implementationType)); return services; }

    public static IServiceCollection AddScoped(this IServiceCollection services, Type service, Func<IServiceProvider, object> factory)
    { services.Add(ServiceDescriptor.Scoped(service, factory)); return services; }

    public static IServiceCollection AddScoped([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] this IServiceCollection services, Type service)
    { services.Add(ServiceDescriptor.Scoped(service, service)); return services; }

    public static IServiceCollection AddScoped<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(this IServiceCollection services)
        where TService : class where TImplementation : class, TService
    { services.Add(ServiceDescriptor.Scoped<TService, TImplementation>()); return services; }

    public static IServiceCollection AddScoped<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TService>(this IServiceCollection services)
        where TService : class
    { services.Add(ServiceDescriptor.Scoped(typeof(TService), typeof(TService))); return services; }

    public static IServiceCollection AddScoped<TService>(this IServiceCollection services, Func<IServiceProvider, TService> factory) where TService : class
    { services.Add(ServiceDescriptor.Scoped(typeof(TService), sp => factory(sp))); return services; }

    public static IServiceCollection AddScoped<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(this IServiceCollection services, Func<IServiceProvider, TImplementation> factory)
        where TService : class where TImplementation : class, TService
    { services.Add(ServiceDescriptor.Scoped(typeof(TService), sp => factory(sp))); return services; }
}

/// <summary>
/// <see cref="IServiceProvider"/> resolution shortcuts (GetService&lt;T&gt;, GetRequiredService, GetServices, CreateScope).
/// Mirrors MS DI's <c>ServiceProviderServiceExtensions</c>.
/// </summary>
public static class ServiceProviderServiceExtensions
{
    public static T? GetService<T>(this IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return (T?)provider.GetService(typeof(T));
    }

    public static object GetRequiredService(this IServiceProvider provider, Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(serviceType);
        var s = provider.GetService(serviceType);
        if (s == null)
            throw new InvalidOperationException($"No service for type '{serviceType.FullName}' has been registered.");
        return s;
    }

    public static T GetRequiredService<T>(this IServiceProvider provider) where T : notnull
        => (T)provider.GetRequiredService(typeof(T));

    public static IEnumerable<T> GetServices<T>(this IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var result = (IEnumerable<T>?)provider.GetService(typeof(IEnumerable<T>));
        return result ?? Array.Empty<T>();
    }

    public static IEnumerable<object?> GetServices(this IServiceProvider provider, Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(serviceType);
        var enumerableType = typeof(IEnumerable<>).MakeGenericType(serviceType);
        var result = provider.GetService(enumerableType);
        return ((System.Collections.IEnumerable?)result)?.Cast<object?>() ?? Array.Empty<object?>();
    }

    public static IServiceScope CreateScope(this IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var factory = (IServiceScopeFactory)provider.GetRequiredService(typeof(IServiceScopeFactory));
        return factory.CreateScope();
    }

    public static AsyncServiceScope CreateAsyncScope(this IServiceProvider provider)
    {
        return new AsyncServiceScope(CreateScope(provider));
    }

    public static AsyncServiceScope CreateAsyncScope(this IServiceScopeFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return new AsyncServiceScope(factory.CreateScope());
    }
}

/// <summary>Wraps an <see cref="IServiceScope"/> to expose <see cref="IAsyncDisposable"/>.</summary>
public readonly struct AsyncServiceScope : IAsyncDisposable, IServiceScope
{
    private readonly IServiceScope _scope;
    public AsyncServiceScope(IServiceScope scope) { _scope = scope ?? throw new ArgumentNullException(nameof(scope)); }
    public IServiceProvider ServiceProvider => _scope.ServiceProvider;
    public void Dispose() => _scope.Dispose();
    public ValueTask DisposeAsync()
    {
        if (_scope is IAsyncDisposable ad) return ad.DisposeAsync();
        _scope.Dispose();
        return ValueTask.CompletedTask;
    }
}

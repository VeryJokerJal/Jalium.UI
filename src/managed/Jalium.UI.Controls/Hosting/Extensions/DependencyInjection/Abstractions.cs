using System.Diagnostics.CodeAnalysis;

namespace Jalium.Extensions.DependencyInjection;

/// <summary>Marker interface implemented by <see cref="IServiceProvider"/> instances that own a scope.</summary>
public interface IServiceScope : IDisposable
{
    IServiceProvider ServiceProvider { get; }
}

/// <summary>Factory for creating <see cref="IServiceScope"/> instances.</summary>
public interface IServiceScopeFactory
{
    IServiceScope CreateScope();
}

/// <summary>Resolves whether a service is registered without instantiating it.</summary>
public interface IServiceProviderIsService
{
    bool IsService(Type serviceType);
}

/// <summary>Resolves whether a keyed service is registered without instantiating it.</summary>
public interface IServiceProviderIsKeyedService : IServiceProviderIsService
{
    bool IsKeyedService(Type serviceType, object? serviceKey);
}

/// <summary>Provider surface for keyed-service resolution. Implemented by <see cref="ServiceProvider"/>.</summary>
public interface IKeyedServiceProvider : IServiceProvider
{
    object? GetKeyedService(Type serviceType, object? serviceKey);
    object GetRequiredKeyedService(Type serviceType, object? serviceKey);
}

/// <summary>Marker attribute that pulls a keyed service for a constructor/method parameter (<see cref="ActivatorUtilities"/>).</summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class FromKeyedServicesAttribute : Attribute
{
    public FromKeyedServicesAttribute(object key) { Key = key; }
    public object Key { get; }
}

/// <summary>Wraps a container builder. Default Jalium DI uses <see cref="IServiceCollection"/> directly.</summary>
public interface IServiceProviderFactory<TContainerBuilder> where TContainerBuilder : notnull
{
    TContainerBuilder CreateBuilder(IServiceCollection services);
    IServiceProvider CreateServiceProvider(TContainerBuilder containerBuilder);
}

/// <summary>Default factory that converts an <see cref="IServiceCollection"/> directly into a provider.</summary>
public sealed class DefaultServiceProviderFactory : IServiceProviderFactory<IServiceCollection>
{
    private readonly ServiceProviderOptions _options;
    public DefaultServiceProviderFactory() : this(ServiceProviderOptions.Default) { }
    public DefaultServiceProviderFactory(ServiceProviderOptions options) { _options = options; }
    public IServiceCollection CreateBuilder(IServiceCollection services) => services;
    public IServiceProvider CreateServiceProvider(IServiceCollection containerBuilder)
        => containerBuilder.BuildServiceProvider(_options);
}

/// <summary>Options controlling <see cref="ServiceProvider"/> behavior.</summary>
public sealed class ServiceProviderOptions
{
    internal static readonly ServiceProviderOptions Default = new();
    public bool ValidateScopes { get; set; }
    public bool ValidateOnBuild { get; set; }
}

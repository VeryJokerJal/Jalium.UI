namespace Jalium.Extensions.DependencyInjection;

/// <summary>
/// Specifies the lifetime of a service in an <see cref="IServiceCollection"/>.
/// </summary>
public enum ServiceLifetime
{
    /// <summary>A single instance is shared across the entire <see cref="IServiceProvider"/>.</summary>
    Singleton,
    /// <summary>A single instance is shared within a <see cref="IServiceScope"/>.</summary>
    Scoped,
    /// <summary>A new instance is created on every resolve.</summary>
    Transient,
}

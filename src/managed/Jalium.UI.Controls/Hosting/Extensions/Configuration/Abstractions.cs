using Jalium.Extensions.Primitives;

namespace Jalium.Extensions.Configuration;

/// <summary>Hierarchical key/value configuration tree.</summary>
public interface IConfiguration
{
    string? this[string key] { get; set; }
    IConfigurationSection GetSection(string key);
    IEnumerable<IConfigurationSection> GetChildren();
    IChangeToken GetReloadToken();
}

/// <summary>Read-only view of one node in the configuration tree.</summary>
public interface IConfigurationSection : IConfiguration
{
    string Key { get; }
    string Path { get; }
    string? Value { get; set; }
}

/// <summary>Root of the configuration tree — surfaces the list of providers and reload.</summary>
public interface IConfigurationRoot : IConfiguration
{
    IEnumerable<IConfigurationProvider> Providers { get; }
    void Reload();
}

/// <summary>Source description that knows how to instantiate an <see cref="IConfigurationProvider"/>.</summary>
public interface IConfigurationSource
{
    IConfigurationProvider Build(IConfigurationBuilder builder);
}

/// <summary>Reads keys from a backing store and exposes them under a flat key namespace.</summary>
public interface IConfigurationProvider
{
    bool TryGet(string key, out string? value);
    void Set(string key, string? value);
    IChangeToken GetReloadToken();
    void Load();
    IEnumerable<string> GetChildKeys(IEnumerable<string> earlierKeys, string? parentPath);
}

/// <summary>Builder used to compose configuration providers.</summary>
public interface IConfigurationBuilder
{
    IDictionary<string, object> Properties { get; }
    IList<IConfigurationSource> Sources { get; }
    IConfigurationBuilder Add(IConfigurationSource source);
    IConfigurationRoot Build();
}

/// <summary>Convenience surface combining <see cref="IConfigurationBuilder"/> + <see cref="IConfigurationRoot"/>.</summary>
public interface IConfigurationManager : IConfigurationBuilder, IConfiguration, IDisposable { }

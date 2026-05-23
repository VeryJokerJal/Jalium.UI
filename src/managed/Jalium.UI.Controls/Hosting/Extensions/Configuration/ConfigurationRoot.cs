using Jalium.Extensions.Primitives;

namespace Jalium.Extensions.Configuration;

/// <summary>Composite root that merges multiple providers — last-write wins on key collisions.</summary>
public sealed class ConfigurationRoot : IConfigurationRoot, IDisposable
{
    private readonly List<IConfigurationProvider> _providers;
    private readonly List<IDisposable> _changeTokenRegistrations = new();
    private ConfigurationReloadToken _changeToken = new();

    public ConfigurationRoot(IList<IConfigurationProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = new List<IConfigurationProvider>(providers);

        foreach (var p in _providers)
        {
            p.Load();
            _changeTokenRegistrations.Add(ChangeToken.OnChange(p.GetReloadToken, RaiseChanged));
        }
    }

    public IEnumerable<IConfigurationProvider> Providers => _providers;

    public string? this[string key]
    {
        get
        {
            for (int i = _providers.Count - 1; i >= 0; i--)
            {
                if (_providers[i].TryGet(key, out var v)) return v;
            }
            return null;
        }
        set
        {
            if (_providers.Count == 0) throw new InvalidOperationException("No configuration sources have been added.");
            foreach (var p in _providers) p.Set(key, value);
        }
    }

    public IConfigurationSection GetSection(string key) => new ConfigurationSection(this, key);

    public IEnumerable<IConfigurationSection> GetChildren() => GetChildrenImpl(null);

    internal IEnumerable<IConfigurationSection> GetChildrenImpl(string? path)
    {
        var keys = new List<string>();
        for (int i = _providers.Count - 1; i >= 0; i--)
        {
            foreach (var k in _providers[i].GetChildKeys(Array.Empty<string>(), path))
            {
                if (!keys.Contains(k, StringComparer.OrdinalIgnoreCase)) keys.Add(k);
            }
        }
        keys.Sort(ConfigurationKeyComparer.Instance);
        foreach (var k in keys)
        {
            yield return GetSection(path == null ? k : ConfigurationPath.Combine(path, k));
        }
    }

    public IChangeToken GetReloadToken() => _changeToken;

    public void Reload()
    {
        foreach (var p in _providers) p.Load();
        RaiseChanged();
    }

    private void RaiseChanged()
    {
        var previous = Interlocked.Exchange(ref _changeToken, new ConfigurationReloadToken());
        previous.OnReload();
    }

    public void Dispose()
    {
        foreach (var r in _changeTokenRegistrations) r.Dispose();
        _changeTokenRegistrations.Clear();
        foreach (var p in _providers)
        {
            if (p is IDisposable d) d.Dispose();
        }
    }
}

/// <summary>Sub-section view onto a <see cref="ConfigurationRoot"/>.</summary>
public sealed class ConfigurationSection : IConfigurationSection
{
    private readonly ConfigurationRoot _root;
    private readonly string _path;

    public ConfigurationSection(ConfigurationRoot root, string path)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _path = path ?? throw new ArgumentNullException(nameof(path));
    }

    public string Path => _path;
    public string Key => ConfigurationPath.GetSectionKey(_path)!;

    public string? Value
    {
        get => _root[_path];
        set => _root[_path] = value;
    }

    public string? this[string key]
    {
        get => _root[ConfigurationPath.Combine(_path, key)];
        set => _root[ConfigurationPath.Combine(_path, key)] = value;
    }

    public IConfigurationSection GetSection(string key) => _root.GetSection(ConfigurationPath.Combine(_path, key));
    public IEnumerable<IConfigurationSection> GetChildren() => _root.GetChildrenImpl(_path);
    public IChangeToken GetReloadToken() => _root.GetReloadToken();
}

/// <summary>Default mutable <see cref="IConfigurationBuilder"/>.</summary>
public sealed class ConfigurationBuilder : IConfigurationBuilder
{
    public IDictionary<string, object> Properties { get; } = new Dictionary<string, object>();
    public IList<IConfigurationSource> Sources { get; } = new List<IConfigurationSource>();

    public IConfigurationBuilder Add(IConfigurationSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Sources.Add(source);
        return this;
    }

    public IConfigurationRoot Build()
    {
        var providers = new List<IConfigurationProvider>();
        foreach (var s in Sources) providers.Add(s.Build(this));
        return new ConfigurationRoot(providers);
    }
}

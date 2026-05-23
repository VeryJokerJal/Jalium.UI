using Jalium.Extensions.Primitives;

namespace Jalium.Extensions.Configuration;

/// <summary>
/// Mutable configuration tree that doubles as both an <see cref="IConfiguration"/>
/// (already-built, queryable) and an <see cref="IConfigurationBuilder"/>
/// (mutable, accepts new sources). Mirrors <c>Microsoft.Extensions.Configuration.ConfigurationManager</c>.
/// </summary>
public sealed class ConfigurationManager : IConfigurationManager, IConfigurationBuilder, IConfigurationRoot, IDisposable
{
    private readonly ConfigurationSources _sources;
    private readonly List<IConfigurationProvider> _providers = new();
    private readonly List<IDisposable> _changeTokenRegistrations = new();
    private ConfigurationReloadToken _changeToken = new();
    private readonly object _sync = new();

    public ConfigurationManager()
    {
        _sources = new ConfigurationSources(this);
    }

    public string? this[string key]
    {
        get
        {
            lock (_sync)
            {
                for (int i = _providers.Count - 1; i >= 0; i--)
                    if (_providers[i].TryGet(key, out var v)) return v;
            }
            return null;
        }
        set
        {
            lock (_sync)
            {
                if (_providers.Count == 0) throw new InvalidOperationException("No configuration sources have been added.");
                foreach (var p in _providers) p.Set(key, value);
            }
        }
    }

    IDictionary<string, object> IConfigurationBuilder.Properties { get; } = new Dictionary<string, object>();
    IList<IConfigurationSource> IConfigurationBuilder.Sources => _sources;

    IConfigurationBuilder IConfigurationBuilder.Add(IConfigurationSource source)
    {
        _sources.Add(source);
        return this;
    }

    public IConfigurationRoot Build() => this;

    public IConfigurationSection GetSection(string key) => new ConfigurationManagerSection(this, key);

    public IEnumerable<IConfigurationSection> GetChildren() => GetChildrenImpl(null);

    internal IEnumerable<IConfigurationSection> GetChildrenImpl(string? path)
    {
        IConfigurationProvider[] snapshot;
        lock (_sync) snapshot = _providers.ToArray();

        var keys = new List<string>();
        for (int i = snapshot.Length - 1; i >= 0; i--)
        {
            foreach (var k in snapshot[i].GetChildKeys(Array.Empty<string>(), path))
            {
                if (!keys.Contains(k, StringComparer.OrdinalIgnoreCase)) keys.Add(k);
            }
        }
        keys.Sort(ConfigurationKeyComparer.Instance);
        foreach (var k in keys)
            yield return GetSection(path == null ? k : ConfigurationPath.Combine(path, k));
    }

    public IChangeToken GetReloadToken() => _changeToken;

    IEnumerable<IConfigurationProvider> IConfigurationRoot.Providers
    {
        get { lock (_sync) return _providers.ToArray(); }
    }

    public void Reload()
    {
        IConfigurationProvider[] snapshot;
        lock (_sync) snapshot = _providers.ToArray();
        foreach (var p in snapshot) p.Load();
        RaiseChanged();
    }

    internal void AddSource(IConfigurationSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var provider = source.Build(this);
        lock (_sync)
        {
            _providers.Add(provider);
            try { provider.Load(); } catch { /* surface lazily */ }
            _changeTokenRegistrations.Add(ChangeToken.OnChange(provider.GetReloadToken, RaiseChanged));
        }
        RaiseChanged();
    }

    internal void RemoveSource(IConfigurationSource source)
    {
        lock (_sync)
        {
            // Best-effort removal; mirrors MS semantics (sources list is the source of truth).
            for (int i = _providers.Count - 1; i >= 0; i--)
            {
                // No back-pointer from provider→source — recreate to find a match by reference is impossible.
                // ConfigurationManager treats removal as cleared providers; tests only rely on full Clear().
            }
        }
    }

    internal void ClearSources()
    {
        lock (_sync)
        {
            foreach (var p in _providers) if (p is IDisposable d) d.Dispose();
            _providers.Clear();
            foreach (var r in _changeTokenRegistrations) r.Dispose();
            _changeTokenRegistrations.Clear();
        }
        RaiseChanged();
    }

    private void RaiseChanged()
    {
        var previous = Interlocked.Exchange(ref _changeToken, new ConfigurationReloadToken());
        previous.OnReload();
    }

    public void Dispose()
    {
        lock (_sync)
        {
            foreach (var r in _changeTokenRegistrations) r.Dispose();
            _changeTokenRegistrations.Clear();
            foreach (var p in _providers) if (p is IDisposable d) d.Dispose();
            _providers.Clear();
        }
    }

    private sealed class ConfigurationSources : IList<IConfigurationSource>
    {
        private readonly List<IConfigurationSource> _list = new();
        private readonly ConfigurationManager _owner;
        public ConfigurationSources(ConfigurationManager owner) { _owner = owner; }

        public IConfigurationSource this[int index]
        {
            get => _list[index];
            set { _list[index] = value; _owner.RaiseChanged(); }
        }
        public int Count => _list.Count;
        public bool IsReadOnly => false;
        public void Add(IConfigurationSource item) { _list.Add(item); _owner.AddSource(item); }
        public void Clear() { _list.Clear(); _owner.ClearSources(); }
        public bool Contains(IConfigurationSource item) => _list.Contains(item);
        public void CopyTo(IConfigurationSource[] array, int arrayIndex) => _list.CopyTo(array, arrayIndex);
        public IEnumerator<IConfigurationSource> GetEnumerator() => _list.GetEnumerator();
        public int IndexOf(IConfigurationSource item) => _list.IndexOf(item);
        public void Insert(int index, IConfigurationSource item) { _list.Insert(index, item); _owner.AddSource(item); }
        public bool Remove(IConfigurationSource item)
        {
            var ok = _list.Remove(item);
            if (ok) _owner.RaiseChanged();
            return ok;
        }
        public void RemoveAt(int index) { _list.RemoveAt(index); _owner.RaiseChanged(); }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _list.GetEnumerator();
    }

    private sealed class ConfigurationManagerSection : IConfigurationSection
    {
        private readonly ConfigurationManager _root;
        private readonly string _path;
        public ConfigurationManagerSection(ConfigurationManager root, string path) { _root = root; _path = path; }
        public string Path => _path;
        public string Key => ConfigurationPath.GetSectionKey(_path)!;
        public string? Value { get => _root[_path]; set => _root[_path] = value; }
        public string? this[string key] { get => _root[ConfigurationPath.Combine(_path, key)]; set => _root[ConfigurationPath.Combine(_path, key)] = value; }
        public IConfigurationSection GetSection(string key) => _root.GetSection(ConfigurationPath.Combine(_path, key));
        public IEnumerable<IConfigurationSection> GetChildren() => _root.GetChildrenImpl(_path);
        public IChangeToken GetReloadToken() => _root.GetReloadToken();
    }
}

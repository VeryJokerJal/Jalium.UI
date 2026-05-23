namespace Jalium.Extensions.Configuration;

/// <summary>Source backed by an in-memory key/value sequence.</summary>
public sealed class MemoryConfigurationSource : IConfigurationSource
{
    public IEnumerable<KeyValuePair<string, string?>>? InitialData { get; set; }
    public IConfigurationProvider Build(IConfigurationBuilder builder) => new MemoryConfigurationProvider(this);
}

public sealed class MemoryConfigurationProvider : ConfigurationProvider, IEnumerable<KeyValuePair<string, string?>>
{
    public MemoryConfigurationProvider(MemoryConfigurationSource source)
    {
        if (source.InitialData != null)
            foreach (var kv in source.InitialData) Data[kv.Key] = kv.Value;
    }

    public void Add(string key, string? value) => Data[key] = value;
    public IEnumerator<KeyValuePair<string, string?>> GetEnumerator() => Data.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => Data.GetEnumerator();
}

public static class MemoryConfigurationBuilderExtensions
{
    public static IConfigurationBuilder AddInMemoryCollection(this IConfigurationBuilder builder)
        => builder.Add(new MemoryConfigurationSource());

    public static IConfigurationBuilder AddInMemoryCollection(this IConfigurationBuilder builder, IEnumerable<KeyValuePair<string, string?>>? initialData)
        => builder.Add(new MemoryConfigurationSource { InitialData = initialData });
}

/// <summary>Source that reads OS environment variables, optionally filtered by a prefix.</summary>
public sealed class EnvironmentVariablesConfigurationSource : IConfigurationSource
{
    public string? Prefix { get; set; }
    public IConfigurationProvider Build(IConfigurationBuilder builder) => new EnvironmentVariablesConfigurationProvider(Prefix);
}

public sealed class EnvironmentVariablesConfigurationProvider : ConfigurationProvider
{
    private const string MySqlServerPrefix = "MYSQLCONNSTR_";
    private const string SqlAzureServerPrefix = "SQLAZURECONNSTR_";
    private const string SqlServerPrefix = "SQLCONNSTR_";
    private const string CustomConnectionStringPrefix = "CUSTOMCONNSTR_";
    private readonly string _prefix;

    public EnvironmentVariablesConfigurationProvider() : this(string.Empty) { }
    public EnvironmentVariablesConfigurationProvider(string? prefix) { _prefix = prefix ?? string.Empty; }

    public override void Load()
    {
        Data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var env = System.Environment.GetEnvironmentVariables();
        foreach (var entry in env)
        {
            var kv = (System.Collections.DictionaryEntry)entry!;
            var key = (string)kv.Key;
            var value = kv.Value?.ToString();

            if (!string.IsNullOrEmpty(_prefix))
            {
                if (!key.StartsWith(_prefix, StringComparison.OrdinalIgnoreCase)) continue;
                key = key.Substring(_prefix.Length);
            }

            // MS-compatible: convert "__" (double underscore) to ":" key separator.
            key = key.Replace("__", ConfigurationPath.KeyDelimiter);
            Data[key] = value;
        }
        OnReload();
    }
}

public static class EnvironmentVariablesExtensions
{
    public static IConfigurationBuilder AddEnvironmentVariables(this IConfigurationBuilder builder)
        => builder.Add(new EnvironmentVariablesConfigurationSource());

    public static IConfigurationBuilder AddEnvironmentVariables(this IConfigurationBuilder builder, string? prefix)
        => builder.Add(new EnvironmentVariablesConfigurationSource { Prefix = prefix });

    public static IConfigurationBuilder AddEnvironmentVariables(this IConfigurationBuilder builder, Action<EnvironmentVariablesConfigurationSource>? configure)
    {
        var src = new EnvironmentVariablesConfigurationSource();
        configure?.Invoke(src);
        return builder.Add(src);
    }
}

/// <summary>Source wrapping an already-built <see cref="IConfiguration"/>.</summary>
public sealed class ChainedConfigurationSource : IConfigurationSource
{
    public IConfiguration Configuration { get; set; } = null!;
    public bool ShouldDisposeConfiguration { get; set; }
    public IConfigurationProvider Build(IConfigurationBuilder builder) => new ChainedConfigurationProvider(this);
}

public sealed class ChainedConfigurationProvider : IConfigurationProvider, IDisposable
{
    private readonly IConfiguration _config;
    private readonly bool _ownsConfig;

    public ChainedConfigurationProvider(ChainedConfigurationSource source)
    {
        _config = source.Configuration ?? throw new ArgumentNullException(nameof(source.Configuration));
        _ownsConfig = source.ShouldDisposeConfiguration;
    }

    public bool TryGet(string key, out string? value)
    {
        value = _config[key];
        return value != null;
    }

    public void Set(string key, string? value) => _config[key] = value;
    public Jalium.Extensions.Primitives.IChangeToken GetReloadToken() => _config.GetReloadToken();
    public void Load() { }

    public IEnumerable<string> GetChildKeys(IEnumerable<string> earlierKeys, string? parentPath)
    {
        var section = parentPath == null ? _config : _config.GetSection(parentPath);
        var keys = new List<string>();
        foreach (var c in section.GetChildren()) keys.Add(c.Key);
        keys.AddRange(earlierKeys);
        keys.Sort(ConfigurationKeyComparer.Instance);
        return keys;
    }

    public void Dispose()
    {
        if (_ownsConfig && _config is IDisposable d) d.Dispose();
    }
}

public static class ChainedBuilderExtensions
{
    public static IConfigurationBuilder AddConfiguration(this IConfigurationBuilder builder, IConfiguration configuration)
        => builder.AddConfiguration(configuration, shouldDisposeConfiguration: false);

    public static IConfigurationBuilder AddConfiguration(this IConfigurationBuilder builder, IConfiguration configuration, bool shouldDisposeConfiguration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return builder.Add(new ChainedConfigurationSource { Configuration = configuration, ShouldDisposeConfiguration = shouldDisposeConfiguration });
    }
}

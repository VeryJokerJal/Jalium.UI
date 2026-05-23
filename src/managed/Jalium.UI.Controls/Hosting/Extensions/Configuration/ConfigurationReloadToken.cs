using Jalium.Extensions.Primitives;

namespace Jalium.Extensions.Configuration;

/// <summary>
/// Single-shot <see cref="IChangeToken"/> used by providers to signal a reload.
/// Each provider holds a current token, fires it via <see cref="OnReload"/>, then
/// replaces itself with a fresh token so subsequent callbacks restart from "not changed".
/// </summary>
public sealed class ConfigurationReloadToken : IChangeToken
{
    private CancellationTokenSource _cts = new();
    public bool HasChanged => _cts.IsCancellationRequested;
    public bool ActiveChangeCallbacks => true;
    public IDisposable RegisterChangeCallback(Action<object?> callback, object? state) => _cts.Token.Register(callback, state);
    public void OnReload() => _cts.Cancel();
}

/// <summary>Base class implementing the shared <see cref="IConfigurationProvider"/> bookkeeping.</summary>
public abstract class ConfigurationProvider : IConfigurationProvider
{
    private ConfigurationReloadToken _reloadToken = new();
    protected IDictionary<string, string?> Data { get; set; } = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    public virtual bool TryGet(string key, out string? value) => Data.TryGetValue(key, out value);
    public virtual void Set(string key, string? value) => Data[key] = value;
    public virtual void Load() { }

    public IChangeToken GetReloadToken() => _reloadToken;

    protected void OnReload()
    {
        var previousToken = Interlocked.Exchange(ref _reloadToken, new ConfigurationReloadToken());
        previousToken.OnReload();
    }

    public virtual IEnumerable<string> GetChildKeys(IEnumerable<string> earlierKeys, string? parentPath)
    {
        var results = new List<string>();
        if (parentPath == null)
        {
            foreach (var kvp in Data) AddIfSegment(results, kvp.Key, 0);
        }
        else
        {
            var prefix = parentPath + ConfigurationPath.KeyDelimiter;
            foreach (var kvp in Data)
            {
                if (kvp.Key.Length > prefix.Length &&
                    kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    AddIfSegment(results, kvp.Key, prefix.Length);
                }
            }
        }
        results.AddRange(earlierKeys);
        results.Sort(ConfigurationKeyComparer.Instance);
        return results;
    }

    private static void AddIfSegment(List<string> dest, string key, int prefixLength)
    {
        var delim = key.IndexOf(':', prefixLength);
        var segment = delim < 0 ? key.Substring(prefixLength) : key.Substring(prefixLength, delim - prefixLength);
        if (!dest.Contains(segment, StringComparer.OrdinalIgnoreCase)) dest.Add(segment);
    }
}

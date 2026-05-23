using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Jalium.Extensions.DependencyInjection;
using Jalium.Extensions.Options;

namespace Jalium.Extensions.Logging;

/// <summary>
/// Default <see cref="ILoggerFactory"/>. Fans out every <see cref="ILogger.Log{TState}"/>
/// call to each registered <see cref="ILoggerProvider"/> and applies the per-provider
/// filter rules stored in <see cref="LoggerFilterOptions"/>.
/// </summary>
public sealed class LoggerFactory : ILoggerFactory
{
    private readonly List<ILoggerProvider> _providers = new();
    private readonly ConcurrentDictionary<string, Logger> _loggers = new(StringComparer.Ordinal);
    private LoggerFilterOptions _filter;
    private readonly LoggerExternalScopeProvider _scopeProvider = new();
    private bool _disposed;

    public LoggerFactory() : this(Array.Empty<ILoggerProvider>(), new LoggerFilterOptions()) { }
    public LoggerFactory(IEnumerable<ILoggerProvider> providers) : this(providers, new LoggerFilterOptions()) { }

    public LoggerFactory(IEnumerable<ILoggerProvider> providers, LoggerFilterOptions filter)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(filter);
        _filter = filter;
        foreach (var p in providers) AddProvider(p);
    }

    /// <summary>DI-friendly constructor — accepts the live <see cref="IOptionsMonitor{TOptions}"/> so the filter snapshot stays in sync.</summary>
    [RequiresUnreferencedCode("Reads IOptionsMonitor<LoggerFilterOptions>.")]
    public LoggerFactory(IEnumerable<ILoggerProvider> providers, IOptionsMonitor<LoggerFilterOptions> filterOption)
        : this(providers, filterOption.CurrentValue)
    {
        filterOption.OnChange((opts, _) =>
        {
            _filter = opts;
            foreach (var l in _loggers.Values) l.UpdateFilter(_filter, _providers);
        });
    }

    public ILogger CreateLogger(string categoryName)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LoggerFactory));
        return _loggers.GetOrAdd(categoryName, name => new Logger(name, _providers, _scopeProvider, _filter));
    }

    public void AddProvider(ILoggerProvider provider)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LoggerFactory));
        ArgumentNullException.ThrowIfNull(provider);
        _providers.Add(provider);
        if (provider is ISupportExternalScope ses) ses.SetScopeProvider(_scopeProvider);
        foreach (var l in _loggers.Values) l.UpdateFilter(_filter, _providers);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var p in _providers) { try { p.Dispose(); } catch { } }
        _providers.Clear();
        _loggers.Clear();
    }

    public static LoggerFactory Create(Action<ILoggingBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging(configure);
        var sp = services.BuildServiceProvider();
        return (LoggerFactory)sp.GetRequiredService<ILoggerFactory>();
    }
}

/// <summary>
/// Composite logger — owns one <see cref="LoggerInformation"/> per provider for the
/// category it was created with. Each call iterates the per-provider list, applies
/// the filter, and forwards.
/// </summary>
internal sealed class Logger : ILogger
{
    private readonly string _categoryName;
    private LoggerInformation[] _loggers;
    private readonly IExternalScopeProvider _scopeProvider;

    public Logger(string categoryName, IEnumerable<ILoggerProvider> providers, IExternalScopeProvider scopeProvider, LoggerFilterOptions filter)
    {
        _categoryName = categoryName;
        _scopeProvider = scopeProvider;
        _loggers = BuildLoggers(providers, filter);
    }

    public void UpdateFilter(LoggerFilterOptions filter, IEnumerable<ILoggerProvider> providers)
    {
        _loggers = BuildLoggers(providers, filter);
    }

    private LoggerInformation[] BuildLoggers(IEnumerable<ILoggerProvider> providers, LoggerFilterOptions filter)
    {
        var list = new List<LoggerInformation>();
        foreach (var p in providers)
        {
            var logger = p.CreateLogger(_categoryName);
            var providerName = p.GetType().FullName ?? p.GetType().Name;
            var (level, predicate) = ResolveFilter(filter, providerName, _categoryName);
            list.Add(new LoggerInformation(logger, level, predicate));
        }
        return list.ToArray();
    }

    private static (LogLevel?, Func<string?, string?, LogLevel, bool>?) ResolveFilter(LoggerFilterOptions options, string providerName, string categoryName)
    {
        // First, pick the most-specific rule.
        LoggerFilterRule? best = null;
        foreach (var rule in options.Rules)
        {
            if (!IsBetter(rule, best, providerName, categoryName)) continue;
            best = rule;
        }
        if (best != null) return (best.LogLevel ?? options.MinLevel, best.Filter);
        return (options.MinLevel, null);
    }

    private static bool IsBetter(LoggerFilterRule rule, LoggerFilterRule? current, string provider, string category)
    {
        if (rule.ProviderName != null && !string.Equals(rule.ProviderName, provider, StringComparison.OrdinalIgnoreCase)) return false;
        if (rule.CategoryName != null && !category.StartsWith(rule.CategoryName, StringComparison.OrdinalIgnoreCase)) return false;
        if (current == null) return true;
        // Longer category match wins; provider-specific beats wildcard.
        var newLen = rule.CategoryName?.Length ?? 0;
        var curLen = current.CategoryName?.Length ?? 0;
        if (rule.ProviderName != null && current.ProviderName == null) return true;
        if (rule.ProviderName == null && current.ProviderName != null) return false;
        return newLen > curLen;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var loggers = _loggers;
        for (int i = 0; i < loggers.Length; i++)
        {
            ref var info = ref loggers[i];
            if (info.MinLevel.HasValue && logLevel < info.MinLevel.Value) continue;
            if (info.Filter != null && !info.Filter(null, null, logLevel)) continue;
            if (!info.Logger.IsEnabled(logLevel)) continue;
            try { info.Logger.Log(logLevel, eventId, state, exception, formatter); }
            catch { /* never let a provider crash the pipeline */ }
        }
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        var loggers = _loggers;
        for (int i = 0; i < loggers.Length; i++)
        {
            ref var info = ref loggers[i];
            if (info.MinLevel.HasValue && logLevel < info.MinLevel.Value) continue;
            if (info.Filter != null && !info.Filter(null, null, logLevel)) continue;
            if (info.Logger.IsEnabled(logLevel)) return true;
        }
        return false;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => _scopeProvider.Push(state);
}

internal struct LoggerInformation
{
    public LoggerInformation(ILogger logger, LogLevel? minLevel, Func<string?, string?, LogLevel, bool>? filter)
    {
        Logger = logger; MinLevel = minLevel; Filter = filter;
    }
    public ILogger Logger;
    public LogLevel? MinLevel;
    public Func<string?, string?, LogLevel, bool>? Filter;
}

internal sealed class LoggerExternalScopeProvider : IExternalScopeProvider
{
    private readonly AsyncLocal<Scope?> _current = new();

    public void ForEachScope<TState>(Action<object?, TState> callback, TState state)
    {
        for (var scope = _current.Value; scope != null; scope = scope.Parent)
            callback(scope.State, state);
    }

    public IDisposable Push(object? state)
    {
        var parent = _current.Value;
        var scope = new Scope(this, state, parent);
        _current.Value = scope;
        return scope;
    }

    private sealed class Scope : IDisposable
    {
        private readonly LoggerExternalScopeProvider _owner;
        public object? State { get; }
        public Scope? Parent { get; }
        private bool _disposed;
        public Scope(LoggerExternalScopeProvider owner, object? state, Scope? parent) { _owner = owner; State = state; Parent = parent; }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _owner._current.Value = Parent;
        }
    }
}

public sealed class Logger<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] T> : ILogger<T>
{
    private readonly ILogger _inner;
    public Logger(ILoggerFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _inner = factory.CreateLogger(TypeNameHelper.GetTypeDisplayName(typeof(T), includeGenericParameters: false));
    }
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => _inner.Log(logLevel, eventId, state, exception, formatter);
    public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => _inner.BeginScope(state);
}

internal static class TypeNameHelper
{
    public static string GetTypeDisplayName(Type type, bool includeGenericParameters)
    {
        if (!type.IsGenericType) return type.FullName ?? type.Name;
        if (!includeGenericParameters)
        {
            var name = type.FullName ?? type.Name;
            var tick = name.IndexOf('`');
            return tick > 0 ? name.Substring(0, tick) : name;
        }
        return type.ToString();
    }
}

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Jalium.Extensions.DependencyInjection;
using Jalium.Extensions.Primitives;

namespace Jalium.Extensions.Options;

/// <summary>Concrete <see cref="IConfigureOptions{TOptions}"/> using a delegate.</summary>
public class ConfigureOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TOptions> : IConfigureOptions<TOptions>
    where TOptions : class
{
    public Action<TOptions>? Action { get; }
    public ConfigureOptions(Action<TOptions>? action) { Action = action; }
    public virtual void Configure(TOptions options) => Action?.Invoke(options);
}

public class ConfigureNamedOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TOptions> : IConfigureNamedOptions<TOptions>
    where TOptions : class
{
    public string? Name { get; }
    public Action<TOptions>? Action { get; }
    public ConfigureNamedOptions(string? name, Action<TOptions>? action) { Name = name; Action = action; }
    public virtual void Configure(string? name, TOptions options)
    {
        if (Name == null || Name == name) Action?.Invoke(options);
    }
    public void Configure(TOptions options) => Configure(global::Jalium.Extensions.Options.Options.DefaultName, options);
}

public class PostConfigureOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TOptions> : IPostConfigureOptions<TOptions>
    where TOptions : class
{
    public string? Name { get; }
    public Action<TOptions>? Action { get; }
    public PostConfigureOptions(string? name, Action<TOptions>? action) { Name = name; Action = action; }
    public virtual void PostConfigure(string? name, TOptions options)
    {
        if (Name == null || Name == name) Action?.Invoke(options);
    }
}

public class ValidateOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TOptions> : IValidateOptions<TOptions>
    where TOptions : class
{
    public string? Name { get; }
    public Func<TOptions, bool> Validation { get; }
    public string FailureMessage { get; }
    public ValidateOptions(string? name, Func<TOptions, bool> validation, string failureMessage)
    {
        Name = name; Validation = validation; FailureMessage = failureMessage;
    }
    public ValidateOptionsResult Validate(string? name, TOptions options)
    {
        if (Name != null && Name != name) return ValidateOptionsResult.Skip;
        return Validation(options) ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(FailureMessage);
    }
}

[RequiresUnreferencedCode("Default OptionsFactory uses Activator.CreateInstance.")]
public class OptionsFactory<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TOptions> : IOptionsFactory<TOptions>
    where TOptions : class, new()
{
    private readonly IEnumerable<IConfigureOptions<TOptions>> _setups;
    private readonly IEnumerable<IPostConfigureOptions<TOptions>> _postConfigures;
    private readonly IEnumerable<IValidateOptions<TOptions>>? _validations;

    public OptionsFactory(IEnumerable<IConfigureOptions<TOptions>> setups, IEnumerable<IPostConfigureOptions<TOptions>> postConfigures)
        : this(setups, postConfigures, null) { }

    public OptionsFactory(IEnumerable<IConfigureOptions<TOptions>> setups, IEnumerable<IPostConfigureOptions<TOptions>> postConfigures, IEnumerable<IValidateOptions<TOptions>>? validations)
    {
        _setups = setups;
        _postConfigures = postConfigures;
        _validations = validations;
    }

    public TOptions Create(string name)
    {
        var options = new TOptions();
        foreach (var s in _setups)
        {
            if (s is IConfigureNamedOptions<TOptions> n) n.Configure(name, options);
            else if (name == global::Jalium.Extensions.Options.Options.DefaultName) s.Configure(options);
        }
        foreach (var p in _postConfigures) p.PostConfigure(name, options);

        if (_validations != null)
        {
            var failures = new List<string>();
            foreach (var v in _validations)
            {
                var r = v.Validate(name, options);
                if (r.Failed && r.Failures != null) failures.AddRange(r.Failures);
            }
            if (failures.Count > 0) throw new OptionsValidationException(name, typeof(TOptions), failures);
        }
        return options;
    }
}

public class OptionsCache<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TOptions> : IOptionsMonitorCache<TOptions> where TOptions : class
{
    private readonly ConcurrentDictionary<string, Lazy<TOptions>> _cache = new(StringComparer.Ordinal);
    public TOptions GetOrAdd(string? name, Func<TOptions> createOptions)
    {
        name ??= global::Jalium.Extensions.Options.Options.DefaultName;
        return _cache.GetOrAdd(name, _ => new Lazy<TOptions>(createOptions)).Value;
    }
    public bool TryAdd(string? name, TOptions options)
    {
        name ??= global::Jalium.Extensions.Options.Options.DefaultName;
        return _cache.TryAdd(name, new Lazy<TOptions>(() => options));
    }
    public bool TryRemove(string? name)
    {
        name ??= global::Jalium.Extensions.Options.Options.DefaultName;
        return _cache.TryRemove(name, out _);
    }
    public void Clear() => _cache.Clear();
}

[RequiresUnreferencedCode("OptionsManager depends on OptionsFactory.")]
public sealed class OptionsManager<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TOptions> : IOptions<TOptions>, IOptionsSnapshot<TOptions>
    where TOptions : class, new()
{
    private readonly IOptionsFactory<TOptions> _factory;
    private readonly OptionsCache<TOptions> _cache = new();

    public OptionsManager(IOptionsFactory<TOptions> factory) { _factory = factory; }
    public TOptions Value => Get(global::Jalium.Extensions.Options.Options.DefaultName);
    public TOptions Get(string? name)
    {
        name ??= global::Jalium.Extensions.Options.Options.DefaultName;
        return _cache.GetOrAdd(name, () => _factory.Create(name));
    }
}

[RequiresUnreferencedCode("OptionsMonitor depends on OptionsFactory.")]
public sealed class OptionsMonitor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TOptions> : IOptionsMonitor<TOptions>, IDisposable
    where TOptions : class, new()
{
    private readonly IOptionsFactory<TOptions> _factory;
    private readonly IEnumerable<IOptionsChangeTokenSource<TOptions>> _sources;
    private readonly IOptionsMonitorCache<TOptions> _cache;
    private readonly List<IDisposable> _registrations = new();
    private event Action<TOptions, string>? Changed;

    public OptionsMonitor(IOptionsFactory<TOptions> factory, IEnumerable<IOptionsChangeTokenSource<TOptions>> sources, IOptionsMonitorCache<TOptions> cache)
    {
        _factory = factory;
        _sources = sources;
        _cache = cache;

        foreach (var src in _sources)
        {
            var name = src.Name ?? global::Jalium.Extensions.Options.Options.DefaultName;
            _registrations.Add(ChangeToken.OnChange(src.GetChangeToken, () =>
            {
                _cache.TryRemove(name);
                Changed?.Invoke(Get(name), name);
            }));
        }
    }

    public TOptions CurrentValue => Get(global::Jalium.Extensions.Options.Options.DefaultName);

    public TOptions Get(string? name)
    {
        name ??= global::Jalium.Extensions.Options.Options.DefaultName;
        return _cache.GetOrAdd(name, () => _factory.Create(name));
    }

    public IDisposable? OnChange(Action<TOptions, string> listener)
    {
        Changed += listener;
        return new ListenerSubscription(this, listener);
    }

    public void Dispose()
    {
        foreach (var r in _registrations) r.Dispose();
        _registrations.Clear();
    }

    private sealed class ListenerSubscription : IDisposable
    {
        private readonly OptionsMonitor<TOptions> _owner;
        private Action<TOptions, string>? _listener;
        public ListenerSubscription(OptionsMonitor<TOptions> owner, Action<TOptions, string> listener) { _owner = owner; _listener = listener; }
        public void Dispose()
        {
            if (_listener != null) { _owner.Changed -= _listener; _listener = null; }
        }
    }
}

using System.Diagnostics.CodeAnalysis;

namespace Jalium.Extensions.Options;

/// <summary>Singleton-scoped options accessor.</summary>
public interface IOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] out TOptions> where TOptions : class
{
    TOptions Value { get; }
}

/// <summary>Per-scope (scoped) options accessor — re-evaluates change-tokens at scope entry.</summary>
public interface IOptionsSnapshot<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] out TOptions> : IOptions<TOptions> where TOptions : class
{
    TOptions Get(string? name);
}

/// <summary>Live options accessor — fires <see cref="OnChange"/> whenever a source changes.</summary>
public interface IOptionsMonitor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] out TOptions>
{
    TOptions CurrentValue { get; }
    TOptions Get(string? name);
    IDisposable? OnChange(Action<TOptions, string> listener);
}

public interface IOptionsFactory<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TOptions> where TOptions : class
{
    TOptions Create(string name);
}

public interface IConfigureOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] in TOptions> where TOptions : class
{
    void Configure(TOptions options);
}

public interface IConfigureNamedOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] in TOptions> : IConfigureOptions<TOptions> where TOptions : class
{
    void Configure(string? name, TOptions options);
}

public interface IPostConfigureOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] in TOptions> where TOptions : class
{
    void PostConfigure(string? name, TOptions options);
}

public interface IValidateOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] in TOptions> where TOptions : class
{
    ValidateOptionsResult Validate(string? name, TOptions options);
}

public interface IOptionsMonitorCache<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TOptions> where TOptions : class
{
    TOptions GetOrAdd(string? name, Func<TOptions> createOptions);
    bool TryAdd(string? name, TOptions options);
    bool TryRemove(string? name);
    void Clear();
}

public interface IOptionsChangeTokenSource<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] out TOptions>
{
    string? Name { get; }
    Jalium.Extensions.Primitives.IChangeToken GetChangeToken();
}

public sealed class ValidateOptionsResult
{
    public static readonly ValidateOptionsResult Success = new() { Succeeded = true };
    public static readonly ValidateOptionsResult Skip = new() { Skipped = true };
    public bool Succeeded { get; init; }
    public bool Failed { get; init; }
    public bool Skipped { get; init; }
    public IEnumerable<string>? Failures { get; init; }
    public string? FailureMessage => Failures == null ? null : string.Join("; ", Failures);
    public static ValidateOptionsResult Fail(string message) => new() { Failed = true, Failures = new[] { message } };
    public static ValidateOptionsResult Fail(IEnumerable<string> messages) => new() { Failed = true, Failures = messages };
}

public sealed class OptionsValidationException : Exception
{
    public string OptionsName { get; }
    public Type OptionsType { get; }
    public IEnumerable<string> Failures { get; }
    public OptionsValidationException(string optionsName, Type optionsType, IEnumerable<string> failures)
        : base(string.Join("; ", failures))
    {
        OptionsName = optionsName;
        OptionsType = optionsType;
        Failures = failures;
    }
}

public static class Options
{
    public const string DefaultName = "";
    public static IOptions<TOptions> Create<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TOptions>(TOptions options) where TOptions : class
        => new OptionsWrapper<TOptions>(options);
}

public sealed class OptionsWrapper<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TOptions> : IOptions<TOptions> where TOptions : class
{
    public OptionsWrapper(TOptions value) { Value = value; }
    public TOptions Value { get; }
}

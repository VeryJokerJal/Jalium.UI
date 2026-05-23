using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using Jalium.Extensions.DependencyInjection;
using Jalium.Extensions.Hosting;

namespace Jalium.Extensions.Options;

/// <summary>
/// <see cref="IValidateOptions{TOptions}"/> implementation that runs every
/// <see cref="System.ComponentModel.DataAnnotations"/> validation attribute on the bound options.
/// Mirrors <c>Microsoft.Extensions.Options.DataAnnotations.DataAnnotationValidateOptions</c>.
/// </summary>
[RequiresUnreferencedCode("Uses reflection to walk every public property looking for ValidationAttribute.")]
public sealed class DataAnnotationValidateOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TOptions> : IValidateOptions<TOptions> where TOptions : class
{
    public string? Name { get; }
    public DataAnnotationValidateOptions(string? name) { Name = name; }

    public ValidateOptionsResult Validate(string? name, TOptions options)
    {
        if (Name != null && Name != name) return ValidateOptionsResult.Skip;
        ArgumentNullException.ThrowIfNull(options);

        var ctx = new ValidationContext(options);
        var results = new List<ValidationResult>();
        if (Validator.TryValidateObject(options, ctx, results, validateAllProperties: true)) return ValidateOptionsResult.Success;

        var failures = new List<string>();
        foreach (var r in results)
        {
            if (r == ValidationResult.Success || r.ErrorMessage == null) continue;
            failures.Add(r.MemberNames.Any() ? $"{string.Join(",", r.MemberNames)}: {r.ErrorMessage}" : r.ErrorMessage);
        }
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

public static class OptionsBuilderDataAnnotationsExtensions
{
    [RequiresUnreferencedCode("Uses DataAnnotationValidateOptions which walks every property via reflection.")]
    public static OptionsBuilder<TOptions> ValidateDataAnnotations<TOptions>(this OptionsBuilder<TOptions> optionsBuilder) where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        optionsBuilder.Services.AddSingleton<IValidateOptions<TOptions>>(new DataAnnotationValidateOptions<TOptions>(optionsBuilder.Name));
        return optionsBuilder;
    }
}

/// <summary>
/// <c>OptionsBuilder.ValidateOnStart()</c> — forces every registered <see cref="IValidateOptions{TOptions}"/>
/// to run during host start (rather than lazily on first <c>.Value</c> access).
/// </summary>
public static class OptionsBuilderExtensions
{
    [RequiresUnreferencedCode("Resolves IOptionsMonitor<TOptions> at host start, which evaluates IValidateOptions handlers.")]
    public static OptionsBuilder<TOptions> ValidateOnStart<TOptions>(this OptionsBuilder<TOptions> optionsBuilder) where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        optionsBuilder.Services.AddTransient<IStartupValidator>(sp => new StartupValidator<TOptions>(sp, optionsBuilder.Name));
        // Also ensure StartupValidatorHostedService is registered so it fires at IHostedService startup.
        optionsBuilder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, StartupValidatorHostedService>());
        return optionsBuilder;
    }
}

/// <summary>Resolves and triggers options materialization (running validators) at host startup.</summary>
public interface IStartupValidator
{
    void Validate();
}

[RequiresUnreferencedCode("Materializes IOptionsMonitor<TOptions>.")]
internal sealed class StartupValidator<TOptions> : IStartupValidator where TOptions : class
{
    private readonly IServiceProvider _services;
    private readonly string _name;
    public StartupValidator(IServiceProvider services, string name) { _services = services; _name = name; }
    public void Validate()
    {
        var monitor = _services.GetService<IOptionsMonitor<TOptions>>();
        if (monitor == null) return;
        // Accessing Get triggers IOptionsFactory.Create which runs every IValidateOptions.
        _ = monitor.Get(_name);
    }
}

internal sealed class StartupValidatorHostedService : IHostedService
{
    private readonly IEnumerable<IStartupValidator> _validators;
    public StartupValidatorHostedService(IEnumerable<IStartupValidator> validators) { _validators = validators; }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        foreach (var v in _validators)
        {
            try { v.Validate(); }
            catch (OptionsValidationException ex) { failures.Add(ex); }
            catch (Exception ex) { failures.Add(ex); }
        }
        if (failures.Count == 1) throw failures[0];
        if (failures.Count > 1) throw new AggregateException("One or more options validations failed during startup.", failures);
        return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public static class OptionsServiceCollectionStartExtensions
{
    /// <summary>Convenience wrapper around <c>AddOptions&lt;T&gt;().ValidateOnStart()</c>.</summary>
    [RequiresUnreferencedCode("Uses ValidateOnStart which depends on IOptionsMonitor reflection paths.")]
    public static OptionsBuilder<TOptions> AddOptionsWithValidateOnStart<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TOptions>(this IServiceCollection services)
        where TOptions : class => services.AddOptions<TOptions>().ValidateOnStart();

    [RequiresUnreferencedCode("Uses ValidateOnStart which depends on IOptionsMonitor reflection paths.")]
    public static OptionsBuilder<TOptions> AddOptionsWithValidateOnStart<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TOptions>(this IServiceCollection services, string? name)
        where TOptions : class => services.AddOptions<TOptions>(name).ValidateOnStart();
}

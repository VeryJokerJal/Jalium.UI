using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Jalium.UI.Controls;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jalium.UI.Hosting;

/// <summary>
/// Extension methods that wire the standard <see cref="IHostApplicationBuilder"/>
/// surface (<see cref="IServiceCollection"/>, <see cref="IConfiguration"/>,
/// <see cref="ILoggingBuilder"/>, <see cref="IHostEnvironment"/>,
/// <see cref="System.Diagnostics.Metrics.Meter"/>) into Jalium.UI-specific
/// concerns: MVVM view/viewmodel pairing, runtime options binding, frame-time
/// metrics, and structured logging helpers.
/// </summary>
public static class JaliumHostingExtensions
{
    // ── AppBuilder top-level shortcuts ───────────────────────────────────────

    /// <summary>
    /// Binds the <c>Jalium</c> configuration section to
    /// <see cref="JaliumRuntimeOptions"/> and registers it for consumption via
    /// <see cref="IOptions{TOptions}"/>. Call once during builder setup.
    /// </summary>
    public static AppBuilder ConfigureJalium(this AppBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddJaliumRuntimeOptions(builder.Configuration);
        return builder;
    }

    /// <summary>
    /// Binds the <c>Jalium</c> configuration section to
    /// <see cref="JaliumRuntimeOptions"/> and applies an additional in-code
    /// override callback.
    /// </summary>
    public static AppBuilder ConfigureJalium(this AppBuilder builder, Action<JaliumRuntimeOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        builder.Services.AddJaliumRuntimeOptions(builder.Configuration);
        builder.Services.Configure(configure);
        return builder;
    }

    // Feature-activation Use* extensions (UseDevTools / UseDebugHud /
    // UseDeveloperTools / UseJaliumMetrics) live on JaliumApp — see
    // JaliumAppExtensions. ASP.NET Core convention: Add*/Configure* before
    // Build, Use* after Build.

    // ── Services: MVVM / view resolution ────────────────────────────────────

    /// <summary>
    /// Registers <paramref name="viewType"/> with the DI container (transient)
    /// so <see cref="IViewFactory"/> can resolve it via constructor injection
    /// instead of falling back to <see cref="Activator.CreateInstance(Type)"/>.
    /// </summary>
    public static IServiceCollection AddView(
        this IServiceCollection services,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type viewType)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(viewType);

        services.EnsureViewInfrastructure();
        services.TryAddTransient(viewType);
        return services;
    }

    /// <summary>
    /// Registers <typeparamref name="TView"/> as a transient service so
    /// <see cref="IViewFactory"/> can construct it through DI.
    /// </summary>
    public static IServiceCollection AddView<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TView>(
        this IServiceCollection services)
        where TView : class
    {
        ArgumentNullException.ThrowIfNull(services);

        services.EnsureViewInfrastructure();
        services.TryAddTransient<TView>();
        return services;
    }

    /// <summary>
    /// Registers <typeparamref name="TView"/> as a transient service and pairs
    /// it with <typeparamref name="TViewModel"/>, so any
    /// <see cref="FrameworkElement"/> of type <typeparamref name="TView"/>
    /// created by <see cref="IViewFactory"/> or tagged with
    /// <see cref="ViewModelLocator.AutoWireViewModelProperty"/> receives an
    /// automatically-resolved <typeparamref name="TViewModel"/> as its
    /// <see cref="FrameworkElement.DataContext"/>.
    /// </summary>
    public static IServiceCollection AddView<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TView,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TViewModel>(
        this IServiceCollection services)
        where TView : class
        where TViewModel : class
    {
        ArgumentNullException.ThrowIfNull(services);

        var registry = services.GetOrCreateViewRegistry();
        registry.Register(typeof(TView), typeof(TViewModel));

        services.TryAddTransient<TView>();
        services.TryAddTransient<TViewModel>();
        services.TryAddSingleton<IViewFactory, ViewFactory>();

        return services;
    }

    /// <summary>
    /// Registers <typeparamref name="TViewModel"/> as a transient service
    /// (useful when a ViewModel is resolved by a view that constructor-injects
    /// it directly, without going through <c>AddView&lt;TView, TViewModel&gt;()</c>).
    /// </summary>
    public static IServiceCollection AddViewModel<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TViewModel>(
        this IServiceCollection services)
        where TViewModel : class
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddTransient<TViewModel>();
        return services;
    }

    /// <summary>
    /// Ensures <see cref="ViewRegistry"/> and <see cref="IViewFactory"/> are
    /// registered. Called by every <c>AddView*</c> overload, so users don't
    /// have to add the infrastructure manually.
    /// </summary>
    public static IServiceCollection AddJaliumMvvm(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.EnsureViewInfrastructure();
        return services;
    }

    // ── Convention-based bulk discovery (AddControllersWithViews-style) ─────

    /// <summary>
    /// Scans the calling assembly for Views (classes whose name ends with
    /// <c>Page</c>/<c>Window</c>/<c>UserControl</c>/<c>View</c> and that
    /// derive from <see cref="FrameworkElement"/>) and ViewModels (classes
    /// whose name ends with <c>ViewModel</c>), registers them with the
    /// container, and pairs them by name convention so
    /// <see cref="IViewFactory"/> can auto-wire the DataContext.
    /// </summary>
    /// <remarks>
    /// The Jalium.UI counterpart of ASP.NET Core's
    /// <c>AddControllersWithViews()</c> — one call replaces a long list of
    /// <c>AddView&lt;TView, TViewModel&gt;()</c>. Returns the service
    /// collection for fluent chaining. Name matching: <c>FooPage</c> pairs
    /// with <c>FooViewModel</c>, <c>MainWindow</c> pairs with
    /// <c>MainWindowViewModel</c>, etc.
    /// </remarks>
    [RequiresUnreferencedCode(
        "AddViewsAndViewModels uses reflection over Assembly.GetExportedTypes() and is not trim-safe; " +
        "use AddView<TView, TViewModel>() explicitly in trimmed/AOT applications.")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IServiceCollection AddViewsAndViewModels(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new ViewDiscoveryOptions();
        options.Assemblies.Add(Assembly.GetCallingAssembly());
        ViewDiscovery.Discover(services, options);
        return services;
    }

    /// <summary>
    /// Overload accepting explicit assemblies. Every assembly listed is
    /// scanned; duplicates are ignored.
    /// </summary>
    [RequiresUnreferencedCode(
        "AddViewsAndViewModels uses reflection; see the parameterless overload for details.")]
    public static IServiceCollection AddViewsAndViewModels(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assemblies);

        var options = new ViewDiscoveryOptions();
        foreach (var assembly in assemblies)
        {
            options.Assemblies.Add(assembly);
        }
        if (options.Assemblies.Count == 0)
        {
            options.Assemblies.Add(Assembly.GetCallingAssembly());
        }
        ViewDiscovery.Discover(services, options);
        return services;
    }

    /// <summary>
    /// Fully configurable overload — set custom suffixes, filters, lifetimes,
    /// or disable auto-pairing. Calling-assembly fallback still applies when
    /// <paramref name="configure"/> leaves <see cref="ViewDiscoveryOptions.Assemblies"/> empty.
    /// </summary>
    [RequiresUnreferencedCode(
        "AddViewsAndViewModels uses reflection; see the parameterless overload for details.")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IServiceCollection AddViewsAndViewModels(
        this IServiceCollection services,
        Action<ViewDiscoveryOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new ViewDiscoveryOptions();
        configure(options);
        if (options.Assemblies.Count == 0)
        {
            options.Assemblies.Add(Assembly.GetCallingAssembly());
        }
        ViewDiscovery.Discover(services, options);
        return services;
    }

    /// <summary>
    /// Scans and registers only View types (no ViewModel discovery / pairing).
    /// Useful when ViewModels live in a separate assembly and are registered
    /// manually.
    /// </summary>
    [RequiresUnreferencedCode(
        "AddViews uses reflection; use AddView<TView>() explicitly in trimmed/AOT applications.")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IServiceCollection AddViews(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new ViewDiscoveryOptions();
        options.Assemblies.Add(Assembly.GetCallingAssembly());
        ViewDiscovery.DiscoverViewsOnly(services, options);
        return services;
    }

    /// <summary>
    /// Overload of <see cref="AddViews(IServiceCollection)"/> with options callback.
    /// </summary>
    [RequiresUnreferencedCode(
        "AddViews uses reflection; use AddView<TView>() explicitly in trimmed/AOT applications.")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IServiceCollection AddViews(
        this IServiceCollection services,
        Action<ViewDiscoveryOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new ViewDiscoveryOptions();
        configure(options);
        if (options.Assemblies.Count == 0)
        {
            options.Assemblies.Add(Assembly.GetCallingAssembly());
        }
        ViewDiscovery.DiscoverViewsOnly(services, options);
        return services;
    }

    /// <summary>
    /// Scans and registers only ViewModel types. Pair symmetry with
    /// <see cref="AddViews(IServiceCollection)"/>; combine the two when Views
    /// and ViewModels live in separate assemblies.
    /// </summary>
    [RequiresUnreferencedCode(
        "AddViewModels uses reflection; use AddViewModel<T>() explicitly in trimmed/AOT applications.")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IServiceCollection AddViewModels(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new ViewDiscoveryOptions();
        options.Assemblies.Add(Assembly.GetCallingAssembly());
        ViewDiscovery.DiscoverViewModelsOnly(services, options);
        return services;
    }

    /// <summary>
    /// Overload of <see cref="AddViewModels(IServiceCollection)"/> with options callback.
    /// </summary>
    [RequiresUnreferencedCode(
        "AddViewModels uses reflection; use AddViewModel<T>() explicitly in trimmed/AOT applications.")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IServiceCollection AddViewModels(
        this IServiceCollection services,
        Action<ViewDiscoveryOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new ViewDiscoveryOptions();
        configure(options);
        if (options.Assemblies.Count == 0)
        {
            options.Assemblies.Add(Assembly.GetCallingAssembly());
        }
        ViewDiscovery.DiscoverViewModelsOnly(services, options);
        return services;
    }

    // ── AppBuilder fluent shortcuts ─────────────────────────────────────────

    /// <summary>
    /// Fluent <see cref="AppBuilder"/> wrapper over
    /// <see cref="AddViewsAndViewModels(IServiceCollection)"/>. Scans the
    /// calling assembly by default.
    /// </summary>
    [RequiresUnreferencedCode(
        "AddViewsAndViewModels uses reflection; see the IServiceCollection overloads for details.")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static AppBuilder AddViewsAndViewModels(this AppBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = new ViewDiscoveryOptions();
        options.Assemblies.Add(Assembly.GetCallingAssembly());
        ViewDiscovery.Discover(builder.Services, options);
        return builder;
    }

    /// <summary>
    /// Fluent <see cref="AppBuilder"/> wrapper accepting explicit assemblies.
    /// </summary>
    [RequiresUnreferencedCode(
        "AddViewsAndViewModels uses reflection; see the IServiceCollection overloads for details.")]
    public static AppBuilder AddViewsAndViewModels(this AppBuilder builder, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(assemblies);

        var options = new ViewDiscoveryOptions();
        foreach (var assembly in assemblies)
        {
            options.Assemblies.Add(assembly);
        }
        if (options.Assemblies.Count == 0)
        {
            options.Assemblies.Add(Assembly.GetCallingAssembly());
        }
        ViewDiscovery.Discover(builder.Services, options);
        return builder;
    }

    /// <summary>
    /// Fluent <see cref="AppBuilder"/> wrapper with configuration callback.
    /// </summary>
    [RequiresUnreferencedCode(
        "AddViewsAndViewModels uses reflection; see the IServiceCollection overloads for details.")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static AppBuilder AddViewsAndViewModels(this AppBuilder builder, Action<ViewDiscoveryOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new ViewDiscoveryOptions();
        configure(options);
        if (options.Assemblies.Count == 0)
        {
            options.Assemblies.Add(Assembly.GetCallingAssembly());
        }
        ViewDiscovery.Discover(builder.Services, options);
        return builder;
    }

    /// <summary>
    /// Ensures the <see cref="IMetricsBuilder"/> infrastructure is registered
    /// (idempotent). The <see cref="JaliumMeter"/> itself is always listenable
    /// once instantiated — this extension just guarantees that consumer code
    /// adding <see cref="IMetricsListener"/> implementations has a meter factory
    /// to latch onto.
    /// </summary>
    public static IServiceCollection AddJaliumMetrics(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddMetrics();
        return services;
    }

    /// <summary>
    /// Binds <see cref="JaliumRuntimeOptions"/> to the <c>Jalium</c> section of
    /// the supplied <paramref name="configuration"/>.
    /// </summary>
    public static IServiceCollection AddJaliumRuntimeOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(JaliumRuntimeOptions.SectionName);
        services.AddOptions<JaliumRuntimeOptions>().Bind(section);
        return services;
    }

    // ── Host environment helpers ────────────────────────────────────────────

    /// <summary>
    /// Name used by Jalium.UI tooling / design surfaces when the environment
    /// represents design-time preview (XAML editor, DevTools live edit).
    /// </summary>
    public const string DesignTimeEnvironmentName = "DesignTime";

    /// <summary>
    /// <see langword="true"/> when <see cref="IHostEnvironment.EnvironmentName"/>
    /// is <see cref="DesignTimeEnvironmentName"/>. Views can branch on this to
    /// show static preview data instead of hitting services that would fail
    /// at design-time.
    /// </summary>
    public static bool IsDesignTime(this IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return environment.IsEnvironment(DesignTimeEnvironmentName);
    }

    // ── Logging helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Resolves an <see cref="ILogger{TCategoryName}"/> from
    /// <see cref="Application.Services"/>. Returns <see langword="null"/> if
    /// the application has not been built through <see cref="AppBuilder"/>.
    /// </summary>
    public static ILogger<T>? GetLogger<T>(this Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        return application.Services?.GetService<ILogger<T>>();
    }

    /// <summary>
    /// Resolves a category-named <see cref="ILogger"/> from
    /// <see cref="Application.Services"/>.
    /// </summary>
    public static ILogger? GetLogger(this Application application, string categoryName)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(categoryName);

        var factory = application.Services?.GetService<ILoggerFactory>();
        return factory?.CreateLogger(categoryName);
    }

    /// <summary>
    /// Convenience accessor: resolves an <see cref="ILogger{TCategoryName}"/>
    /// using <typeparamref name="T"/> as the category, looking the factory up
    /// via <see cref="Application.Current"/>. Intended for controls and
    /// services that don't have a direct handle on <see cref="JaliumApp"/>.
    /// </summary>
    public static ILogger<T>? GetLogger<T>(this FrameworkElement _)
        => Application.Current?.GetLogger<T>();

    // ── Internal helpers ─────────────────────────────────────────────────────

    private static void EnsureViewInfrastructure(this IServiceCollection services)
    {
        services.GetOrCreateViewRegistry();
        services.TryAddSingleton<IViewFactory, ViewFactory>();
    }

    /// <summary>
    /// Returns the <see cref="ViewRegistry"/> singleton already registered in
    /// <paramref name="services"/>, or creates one and registers it. The
    /// instance is shared with the DI container — registrations added here
    /// are visible to <see cref="ViewFactory"/> after build.
    /// </summary>
    private static ViewRegistry GetOrCreateViewRegistry(this IServiceCollection services)
    {
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType == typeof(ViewRegistry) &&
                descriptor.ImplementationInstance is ViewRegistry existing)
            {
                return existing;
            }
        }

        var registry = new ViewRegistry();
        services.AddSingleton(registry);
        return registry;
    }
}

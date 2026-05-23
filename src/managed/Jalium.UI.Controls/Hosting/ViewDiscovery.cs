using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.Extensions.DependencyInjection;

namespace Jalium.UI.Hosting;

/// <summary>
/// Reflective scanner that turns a set of assemblies into DI registrations —
/// the moral equivalent of <c>AddControllersWithViews</c> in ASP.NET Core MVC.
/// </summary>
/// <remarks>
/// Scanning uses <see cref="Assembly.GetExportedTypes"/> and simple name-suffix
/// rules (see <see cref="ViewDiscoveryOptions"/>). The scan is <em>not</em>
/// trimmer/AOT friendly — consuming it from a trimmed app is allowed but the
/// types referenced must be preserved by the caller (typically via
/// <c>DynamicDependency</c> on the entry point or by keeping the project
/// non-trimmed at the entry layer). Use the explicit
/// <see cref="JaliumHostingExtensions.AddView{TView,TViewModel}"/> API instead
/// for trim-safe registration.
/// </remarks>
internal static class ViewDiscovery
{
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("ViewDiscovery enumerates exported types via Assembly.GetExportedTypes; see remarks for trim-safe alternatives.")]
    public static ViewDiscoveryResult Discover(IServiceCollection services, ViewDiscoveryOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.TryAddSingleton<ViewRegistry>();
        services.TryAddSingleton<IViewFactory, ViewFactory>();

        var registry = GetOrCreateRegistry(services);
        var result = new ViewDiscoveryResult();

        // Pass 1 — find all ViewModel types across requested assemblies so pass 2
        // can match views to VMs by name.
        var viewModelsByName = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var assembly in options.Assemblies.Distinct())
        {
            foreach (var type in GetExportedTypesSafe(assembly))
            {
                if (!IsCandidateConcrete(type))
                {
                    continue;
                }

                if (!type.Name.EndsWith(options.ViewModelSuffix, StringComparison.Ordinal))
                {
                    continue;
                }

                if (options.RequireNotifyingViewModel &&
                    !ImplementsAnyInterface(type, options.ViewModelNotifyInterfaces))
                {
                    continue;
                }

                if (options.ViewModelFilter != null && !options.ViewModelFilter(type))
                {
                    continue;
                }

                // Last-write wins for duplicates across assemblies — predictable
                // for the common case of one-VM-per-name.
                viewModelsByName[type.Name] = type;
            }
        }

        // Pass 2 — find View types and register + pair.
        var frameworkElementType = typeof(FrameworkElement);
        foreach (var assembly in options.Assemblies.Distinct())
        {
            foreach (var type in GetExportedTypesSafe(assembly))
            {
                if (!IsCandidateConcrete(type))
                {
                    continue;
                }

                if (!frameworkElementType.IsAssignableFrom(type))
                {
                    continue;
                }

                if (!TrySuffix(type.Name, options.ViewSuffixes, out var baseName))
                {
                    continue;
                }

                if (options.ViewFilter != null && !options.ViewFilter(type))
                {
                    continue;
                }

                services.TryAdd(new ServiceDescriptor(type, type, options.ViewLifetime));
                result.Views.Add(type);

                if (!options.AutoPair)
                {
                    continue;
                }

                // Matching order:
                //   1. Full-name pairing: keep the view suffix — MainWindow → MainWindowViewModel
                //   2. Short-name pairing: strip the view suffix — FooPage → FooViewModel
                // Full-name wins when both exist, because it's the stricter match.
                var fullName = type.Name + options.ViewModelSuffix;
                var shortName = baseName + options.ViewModelSuffix;

                Type? vmType = null;
                if (viewModelsByName.TryGetValue(fullName, out vmType) ||
                    viewModelsByName.TryGetValue(shortName, out vmType))
                {
                    registry.Register(type, vmType);
                    result.Pairings.Add(new KeyValuePair<Type, Type>(type, vmType));
                }
            }
        }

        // Register every discovered ViewModel even if unpaired — they may still
        // be constructor-injected into other services or resolved explicitly.
        foreach (var vm in viewModelsByName.Values)
        {
            services.TryAdd(new ServiceDescriptor(vm, vm, options.ViewModelLifetime));
            result.ViewModels.Add(vm);
        }

        return result;
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Defers to Discover which enumerates exported types via Assembly.GetExportedTypes.")]
    public static ViewDiscoveryResult DiscoverViewsOnly(IServiceCollection services, ViewDiscoveryOptions options)
    {
        var originalAutoPair = options.AutoPair;
        options.AutoPair = false;
        try
        {
            var result = Discover(services, options);
            result.ViewModels.Clear();
            return result;
        }
        finally
        {
            options.AutoPair = originalAutoPair;
        }
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("ViewDiscovery enumerates exported types via Assembly.GetExportedTypes.")]
    public static ViewDiscoveryResult DiscoverViewModelsOnly(IServiceCollection services, ViewDiscoveryOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        var result = new ViewDiscoveryResult();

        foreach (var assembly in options.Assemblies.Distinct())
        {
            foreach (var type in GetExportedTypesSafe(assembly))
            {
                if (!IsCandidateConcrete(type))
                {
                    continue;
                }

                if (!type.Name.EndsWith(options.ViewModelSuffix, StringComparison.Ordinal))
                {
                    continue;
                }

                if (options.RequireNotifyingViewModel &&
                    !ImplementsAnyInterface(type, options.ViewModelNotifyInterfaces))
                {
                    continue;
                }

                if (options.ViewModelFilter != null && !options.ViewModelFilter(type))
                {
                    continue;
                }

                services.TryAdd(new ServiceDescriptor(type, type, options.ViewModelLifetime));
                result.ViewModels.Add(type);
            }
        }

        return result;
    }

    private static bool ImplementsAnyInterface(Type type, List<Type> interfaces)
    {
        for (int i = 0; i < interfaces.Count; i++)
        {
            var iface = interfaces[i];
            if (iface != null && iface.IsAssignableFrom(type))
            {
                return true;
            }
        }
        return false;
    }

    private static ViewRegistry GetOrCreateRegistry(IServiceCollection services)
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

    private static bool IsCandidateConcrete(Type type)
    {
        return type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false };
    }

    private static bool TrySuffix(string name, List<string> suffixes, out string baseName)
    {
        foreach (var suffix in suffixes)
        {
            if (!string.IsNullOrEmpty(suffix) &&
                name.Length > suffix.Length &&
                name.EndsWith(suffix, StringComparison.Ordinal))
            {
                baseName = name[..^suffix.Length];
                return true;
            }
        }
        baseName = name;
        return false;
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Enumerates exported types via Assembly.GetExportedTypes; types may be trimmed.")]
    private static IEnumerable<Type> GetExportedTypesSafe(Assembly assembly)
    {
        try
        {
            return assembly.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // Some referenced assembly failed to load — use the types that did.
            return ex.Types.Where(t => t != null)!;
        }
        catch
        {
            return Array.Empty<Type>();
        }
    }
}

/// <summary>
/// Report returned by <see cref="ViewDiscovery.Discover"/> summarizing which
/// types were found and paired. Useful for logging at boot.
/// </summary>
public sealed class ViewDiscoveryResult
{
    /// <summary>All View types registered into the service collection.</summary>
    public List<Type> Views { get; } = new();

    /// <summary>All ViewModel types registered into the service collection.</summary>
    public List<Type> ViewModels { get; } = new();

    /// <summary>(View,ViewModel) pairs written into <see cref="ViewRegistry"/>.</summary>
    public List<KeyValuePair<Type, Type>> Pairings { get; } = new();
}

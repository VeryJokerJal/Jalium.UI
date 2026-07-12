using System.Collections.Concurrent;

namespace Jalium.UI.Markup;

/// <summary>
/// Registry of compile-time generated <see cref="Jalium.UI.ResourceDictionary"/> builders,
/// keyed by manifest resource name.
///
/// <para>
/// The SourceGenerator emits one entry per jalxaml ResourceDictionary file in a
/// <c>[ModuleInitializer]</c> so runtime lookups can short-circuit the embedded-resource +
/// XML reader path. <see cref="ThemeLoader"/> queries this registry first; only when the
/// lookup misses (e.g. document used a feature the SG could not lower) does it fall back
/// to <c>LoadResourceDictionaryFromPayload</c>.
/// </para>
///
/// <para>
/// The registry is process-wide and append-only. Keys are matched case-insensitively
/// because the runtime resource-name resolver normalises path separators inconsistently
/// across platforms; the SG always emits the canonical dot-separated form.
/// </para>
/// </summary>
public static class XamlPrebuiltDictionaryRegistry
{
    private const string LegacyControlsResourcePrefix = "Jalium.UI.Controls.";
    private const string ManagedResourcePrefix = "Jalium.UI.Managed.";

    /// <summary>
    /// Builder delegate signature. The runtime supplies an empty dictionary and a build
    /// context whose parent stack already includes the dictionary. The builder populates
    /// merged dictionaries / entries / inline values via <see cref="XamlBuilder"/> APIs.
    /// </summary>
    public delegate void DictionaryBuilder(ResourceDictionary target, XamlBuildContext context);

    /// <summary>
    /// Factory delegate for x:Class-derived ResourceDictionary types. The returned instance
    /// is the codebehind class (e.g. <c>AppTheme : ResourceDictionary</c>); its constructor
    /// has already invoked the SG-generated <c>InitializeComponent</c> so the dictionary
    /// is fully populated. Distinct from <see cref="DictionaryBuilder"/> because the runtime
    /// must surface the typed instance — the codebehind may expose extra members the
    /// containing app reads.
    /// </summary>
    public delegate ResourceDictionary DictionaryFactory();

    private static readonly ConcurrentDictionary<string, DictionaryBuilder> _builders =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, DictionaryFactory> _factories =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Register a compile-time generated dictionary builder. Called from the SG-emitted
    /// <c>[ModuleInitializer]</c> in each generated wrapper class. Last writer wins —
    /// which lets a downstream assembly override a framework-bundled dictionary by
    /// publishing a builder under the same resource name.
    /// </summary>
    public static void Register(string resourceName, DictionaryBuilder builder)
    {
        ArgumentException.ThrowIfNullOrEmpty(resourceName);
        ArgumentNullException.ThrowIfNull(builder);
        _builders[resourceName] = builder;
    }

    /// <summary>
    /// Register a compile-time generated factory for an x:Class-derived ResourceDictionary
    /// type. Used when the dictionary has codebehind (the SG already emits a partial
    /// <c>InitializeComponent</c> on the codebehind class); the factory simply
    /// <c>new T()</c>'s the type and returns it. <see cref="ThemeLoader"/> consults the
    /// factory registry before the builder registry — when both register a key the typed
    /// factory wins because it preserves the codebehind instance identity callers may
    /// rely on.
    /// </summary>
    public static void RegisterFactory(string resourceName, DictionaryFactory factory)
    {
        ArgumentException.ThrowIfNullOrEmpty(resourceName);
        ArgumentNullException.ThrowIfNull(factory);
        _factories[resourceName] = factory;
    }

    /// <summary>
    /// Try to resolve a registered builder for <paramref name="resourceName"/>. Returns
    /// false when no SG output is available; the caller then takes the legacy
    /// embedded-resource path.
    /// </summary>
    public static bool TryGet(string resourceName, out DictionaryBuilder? builder)
    {
        if (_builders.TryGetValue(resourceName, out builder))
        {
            return true;
        }

        return TryMapLegacyControlsResource(resourceName, out string managedName)
            && _builders.TryGetValue(managedName, out builder);
    }

    /// <summary>
    /// Try to resolve a registered factory for <paramref name="resourceName"/>. Returns
    /// false when no x:Class-typed factory is registered for this resource — caller may
    /// then try <see cref="TryGet"/> for an anonymous-dict builder.
    /// </summary>
    public static bool TryGetFactory(string resourceName, out DictionaryFactory? factory)
    {
        if (_factories.TryGetValue(resourceName, out factory))
        {
            return true;
        }

        return TryMapLegacyControlsResource(resourceName, out string managedName)
            && _factories.TryGetValue(managedName, out factory);
    }

    private static bool TryMapLegacyControlsResource(string resourceName, out string managedName)
    {
        if (resourceName.StartsWith(LegacyControlsResourcePrefix, StringComparison.OrdinalIgnoreCase))
        {
            managedName = ManagedResourcePrefix + resourceName[LegacyControlsResourcePrefix.Length..];
            return true;
        }

        managedName = string.Empty;
        return false;
    }

    /// <summary>
    /// Returns the number of registered builders. Diagnostic only — useful for verifying
    /// the SG output was wired up correctly during startup tracing.
    /// </summary>
    public static int Count => _builders.Count + _factories.Count;
}

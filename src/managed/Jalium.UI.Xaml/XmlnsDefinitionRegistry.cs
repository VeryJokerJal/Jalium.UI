using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Jalium.UI.Markup;

/// <summary>
/// Runtime registry that maps XML namespaces (as written in <c>xmlns</c> attributes in
/// JALXAML documents) to the CLR namespaces and assemblies that provide the corresponding types.
/// </summary>
/// <remarks>
/// <para>
/// The registry is populated automatically by scanning
/// <see cref="XmlnsDefinitionAttribute"/>, <see cref="XmlnsPrefixAttribute"/>, and
/// <see cref="XmlnsCompatibleWithAttribute"/> declarations on every loaded assembly and
/// on any assembly loaded after initialization. Consumers can also register mappings manually
/// via <see cref="AddXmlnsDefinition(string, string, Assembly)"/> for scenarios where the
/// assembly-level attribute cannot be used (dynamic assemblies, plug-ins that discover
/// types at runtime, tests).
/// </para>
/// <para>
/// A single XML namespace may resolve to multiple (CLR namespace, assembly) pairs. Type
/// resolution tries each pair in registration order, so earlier registrations win in case of
/// name clashes. Jalium framework assemblies are registered before user assemblies because
/// they are already loaded when this class runs its initial scan.
/// </para>
/// </remarks>
public static class XmlnsDefinitionRegistry
{
    /// <summary>
    /// An XML namespace mapping entry: the CLR namespace plus the assembly that contains the types.
    /// </summary>
    /// <param name="ClrNamespace">The CLR namespace (e.g. <c>Jalium.UI.Controls.Primitives</c>).</param>
    /// <param name="Assembly">The assembly that defines the CLR namespace.</param>
    public readonly record struct Mapping(string ClrNamespace, Assembly Assembly);

    private static readonly object _writeLock = new();
    private static readonly ConcurrentDictionary<string, ImmutableArray<Mapping>> _mappings = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, string> _preferredPrefixes = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, string> _compatibilityRedirects = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<Assembly, byte> _scannedAssemblies = new();
    private static int _initialized;

    /// <summary>
    /// Triggers the initial scan of every loaded assembly and subscribes to
    /// <see cref="AppDomain.AssemblyLoad"/> so that assemblies loaded later are scanned too.
    /// Safe to call repeatedly.
    /// </summary>
    public static void EnsureInitialized()
    {
        if (Interlocked.CompareExchange(ref _initialized, 1, 0) != 0)
        {
            return;
        }

        AppDomain.CurrentDomain.AssemblyLoad += static (_, args) => ScanAssembly(args.LoadedAssembly);

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            ScanAssembly(assembly);
        }
    }

    /// <summary>
    /// Scans the supplied assembly for <see cref="XmlnsDefinitionAttribute"/>,
    /// <see cref="XmlnsPrefixAttribute"/>, and <see cref="XmlnsCompatibleWithAttribute"/>
    /// declarations and installs the resulting mappings. No-op if the assembly has already
    /// been scanned.
    /// </summary>
    /// <param name="assembly">The assembly to scan.</param>
    public static void ScanAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        if (!_scannedAssemblies.TryAdd(assembly, 0))
        {
            return;
        }

        // Skip dynamic and reflection-only assemblies — GetCustomAttributes can fault on them.
        if (assembly.IsDynamic)
        {
            return;
        }

        XmlnsDefinitionAttribute[] defs;
        XmlnsPrefixAttribute[] prefixes;
        XmlnsCompatibleWithAttribute[] compats;

        try
        {
            defs = assembly.GetCustomAttributes<XmlnsDefinitionAttribute>().ToArray();
            prefixes = assembly.GetCustomAttributes<XmlnsPrefixAttribute>().ToArray();
            compats = assembly.GetCustomAttributes<XmlnsCompatibleWithAttribute>().ToArray();
        }
        catch
        {
            // Some assemblies (e.g. C++/CLI, load-context mismatches) throw when enumerating
            // attributes. Skip silently rather than bringing down XAML parsing.
            return;
        }

        foreach (var def in defs)
        {
            var target = ResolveTargetAssembly(assembly, def.AssemblyName) ?? assembly;
            AddXmlnsDefinitionInternal(def.XmlNamespace, def.ClrNamespace, target);
        }

        foreach (var prefix in prefixes)
        {
            _preferredPrefixes.TryAdd(prefix.XmlNamespace, prefix.Prefix);
        }

        foreach (var compat in compats)
        {
            _compatibilityRedirects.TryAdd(compat.OldNamespace, compat.NewNamespace);
        }
    }

    /// <summary>
    /// Registers a mapping between an XML namespace and a CLR namespace in the given assembly.
    /// Use this when assembly-level attributes cannot be applied (e.g. dynamic assemblies,
    /// tests, plug-ins loaded reflectively).
    /// </summary>
    public static void AddXmlnsDefinition(string xmlNamespace, string clrNamespace, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(xmlNamespace);
        ArgumentNullException.ThrowIfNull(clrNamespace);
        ArgumentNullException.ThrowIfNull(assembly);
        AddXmlnsDefinitionInternal(xmlNamespace, clrNamespace, assembly);
    }

    /// <summary>
    /// Registers a compatibility redirect so references to <paramref name="oldNamespace"/>
    /// are treated as if they targeted <paramref name="newNamespace"/>.
    /// </summary>
    public static void AddCompatibleNamespace(string oldNamespace, string newNamespace)
    {
        ArgumentNullException.ThrowIfNull(oldNamespace);
        ArgumentNullException.ThrowIfNull(newNamespace);
        _compatibilityRedirects[oldNamespace] = newNamespace;
    }

    /// <summary>
    /// Registers the preferred prefix for an XML namespace. Later calls overwrite earlier ones.
    /// </summary>
    public static void AddPreferredPrefix(string xmlNamespace, string prefix)
    {
        ArgumentNullException.ThrowIfNull(xmlNamespace);
        ArgumentNullException.ThrowIfNull(prefix);
        _preferredPrefixes[xmlNamespace] = prefix;
    }

    /// <summary>
    /// Gets every (CLR namespace, assembly) pair registered for <paramref name="xmlNamespace"/>,
    /// following any compatibility redirects. The returned snapshot is ordered by registration
    /// time — framework mappings come before user mappings.
    /// </summary>
    public static ImmutableArray<Mapping> GetMappings(string xmlNamespace)
    {
        ArgumentNullException.ThrowIfNull(xmlNamespace);
        EnsureInitialized();

        var effective = ResolveCompatibilityRedirect(xmlNamespace);
        return _mappings.TryGetValue(effective, out var mappings) ? mappings : ImmutableArray<Mapping>.Empty;
    }

    /// <summary>
    /// Returns the preferred prefix for <paramref name="xmlNamespace"/>, or <c>null</c> if none was registered.
    /// </summary>
    public static string? GetPreferredPrefix(string xmlNamespace)
    {
        ArgumentNullException.ThrowIfNull(xmlNamespace);
        EnsureInitialized();

        var effective = ResolveCompatibilityRedirect(xmlNamespace);
        return _preferredPrefixes.TryGetValue(effective, out var prefix) ? prefix : null;
    }

    /// <summary>
    /// Follows any registered compatibility redirects for <paramref name="xmlNamespace"/>.
    /// Returns the original namespace when no redirect is registered.
    /// </summary>
    public static string ResolveCompatibilityRedirect(string xmlNamespace)
    {
        ArgumentNullException.ThrowIfNull(xmlNamespace);

        // Follow chained redirects, guarding against cycles by capping the walk.
        var current = xmlNamespace;
        for (var i = 0; i < 8; i++)
        {
            if (!_compatibilityRedirects.TryGetValue(current, out var next) || next == current)
            {
                return current;
            }
            current = next;
        }
        return current;
    }

    /// <summary>
    /// Enumerates every XML namespace that has at least one registered mapping or compatibility redirect.
    /// Primarily intended for diagnostics and tooling.
    /// </summary>
    public static IEnumerable<string> EnumerateKnownXmlNamespaces()
    {
        EnsureInitialized();
        return _mappings.Keys.Concat(_compatibilityRedirects.Keys).Distinct(StringComparer.Ordinal);
    }

    private static void AddXmlnsDefinitionInternal(string xmlNamespace, string clrNamespace, Assembly assembly)
    {
        lock (_writeLock)
        {
            var mapping = new Mapping(clrNamespace, assembly);
            _mappings.AddOrUpdate(
                xmlNamespace,
                static (_, m) => ImmutableArray.Create(m),
                static (_, existing, m) => existing.Contains(m) ? existing : existing.Add(m),
                mapping);
        }
    }

    [UnconditionalSuppressMessage("AOT", "IL2026:RequiresUnreferencedCode",
        Justification = "Assembly names are matched by simple name; we never load new assemblies here.")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode",
        Justification = "No dynamic code generation — we only compare assembly simple names.")]
    private static Assembly? ResolveTargetAssembly(Assembly declaring, string? assemblyName)
    {
        if (string.IsNullOrEmpty(assemblyName))
        {
            return declaring;
        }

        if (string.Equals(declaring.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase))
        {
            return declaring;
        }

        foreach (var candidate in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (string.Equals(candidate.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }
}

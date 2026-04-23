using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
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

        // .NET 有两层 "看不见" 的机制会让声明了 [XmlnsDefinition] 的用户程序集
        // 对 registry 不可见:
        //   1) C# 编译器的 reference pruning:如果 consumer 代码没 touch 该程序集
        //      的任何类型,编译器会把 AssemblyRef 从最终 metadata 里剔除,
        //      GetReferencedAssemblies() 也看不到它,只靠传递引用 BFS 找不到。
        //   2) .NET runtime 的 lazy-load:即便 metadata 里有 AssemblyRef,运行时
        //      也只有在首次用到该程序集的类型时才会 Load。
        // 这两层结合意味着"仅靠 <ProjectReference> + [assembly: XmlnsDefinition]"
        // 的声明式用法无法让 user 控件被 XAML 识别。
        //
        // 根治策略:扫 entry exe 所在目录和所有已加载程序集所在目录的 *.dll,
        // 逐个通过默认 load context 做 Assembly.Load,让 AssemblyLoad 事件把它们
        // 带进 ScanAssembly。这正是 WPF 的做法。
        ForceLoadProbingDirectories();
        ForceLoadTransitiveReferences();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            ScanAssembly(assembly);
        }
    }

    [UnconditionalSuppressMessage("AOT", "IL2026:RequiresUnreferencedCode",
        Justification = "Probing-directory load is best-effort; failures are swallowed.")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode",
        Justification = "No dynamic code generation — only name-based Assembly.Load calls.")]
    private static void ForceLoadProbingDirectories()
    {
        // 候选目录:entry exe 目录 + 每个已加载程序集所在目录。
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var entryAsm = Assembly.GetEntryAssembly();
        TryAddDirectory(entryAsm, directories);

        foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
        {
            TryAddDirectory(loaded, directories);
        }

        var alreadyLoaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
        {
            var name = loaded.GetName().Name;
            if (name is not null)
            {
                alreadyLoaded.Add(name);
            }
        }

        foreach (var directory in directories)
        {
            IEnumerable<string> dlls;
            try
            {
                dlls = Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var dllPath in dlls)
            {
                AssemblyName name;
                try
                {
                    name = AssemblyName.GetAssemblyName(dllPath);
                }
                catch
                {
                    // 非托管 DLL / 损坏 / 权限问题 — 跳过。
                    continue;
                }

                if (name.Name is null || !alreadyLoaded.Add(name.Name))
                {
                    continue;
                }

                if (IsFrameworkAssembly(name.Name))
                {
                    continue;
                }

                try
                {
                    // 走默认 load context 的按名 Load,避免 LoadFrom 导致的 identity 冲突。
                    Assembly.Load(name);
                }
                catch
                {
                    // 依赖解析失败、TFM 不兼容等 — 跳过,不阻塞 XAML 解析。
                }
            }
        }
    }

    private static void TryAddDirectory(Assembly? assembly, HashSet<string> sink)
    {
        if (assembly is null || assembly.IsDynamic)
        {
            return;
        }

        string? location;
        try
        {
            location = assembly.Location;
        }
        catch
        {
            return;
        }

        if (string.IsNullOrEmpty(location))
        {
            return;
        }

        var directory = Path.GetDirectoryName(location);
        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
        {
            sink.Add(directory!);
        }
    }

    [UnconditionalSuppressMessage("AOT", "IL2026:RequiresUnreferencedCode",
        Justification = "Transitive load is best-effort; missing references are swallowed.")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode",
        Justification = "No dynamic code generation — only reference-based Assembly.Load calls.")]
    private static void ForceLoadTransitiveReferences()
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<Assembly>();

        foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (loaded.IsDynamic)
            {
                continue;
            }

            var name = loaded.GetName().Name;
            if (name is not null && visited.Add(name))
            {
                queue.Enqueue(loaded);
            }
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            AssemblyName[] references;
            try
            {
                references = current.GetReferencedAssemblies();
            }
            catch
            {
                continue;
            }

            foreach (var reference in references)
            {
                if (reference.Name is null || !visited.Add(reference.Name))
                {
                    continue;
                }

                if (IsFrameworkAssembly(reference.Name))
                {
                    continue;
                }

                try
                {
                    var loaded = Assembly.Load(reference);
                    queue.Enqueue(loaded);
                }
                catch
                {
                    // 找不到、签名不匹配、安全策略阻止等情况跳过,不阻塞 XAML 解析。
                }
            }
        }
    }

    private static bool IsFrameworkAssembly(string simpleName)
    {
        // BCL / runtime / 编译器内部程序集不可能声明 XmlnsDefinition,跳过显著降低启动开销。
        return simpleName.StartsWith("System.", StringComparison.Ordinal)
            || simpleName.StartsWith("Microsoft.", StringComparison.Ordinal)
            || simpleName.StartsWith("runtime.", StringComparison.Ordinal)
            || simpleName.Equals("mscorlib", StringComparison.Ordinal)
            || simpleName.Equals("System", StringComparison.Ordinal)
            || simpleName.Equals("netstandard", StringComparison.Ordinal)
            || simpleName.Equals("WindowsBase", StringComparison.Ordinal)
            || simpleName.Equals("PresentationCore", StringComparison.Ordinal)
            || simpleName.Equals("PresentationFramework", StringComparison.Ordinal);
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

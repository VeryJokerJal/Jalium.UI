using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;

namespace Jalium.UI.Tests;

public sealed class XamlTypeRegistryBuiltinCatalogTests
{
    private const long MaximumColdLookupAllocationBytes = 32 * 1024;

    private const DynamicallyAccessedMemberTypes ExpectedBuiltinDam =
        DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.PublicMethods |
        DynamicallyAccessedMemberTypes.NonPublicFields;

    [Fact]
    public void BuiltinCatalog_MatchesFrozenRevisedReleaseGolden()
    {
        using var runtime = IsolatedRegistryRuntime.Create();

        var entries = XamlTypeRegistryBuiltinsRevisedReleaseGolden.Entries;
        Assert.Equal(263, entries.Length);

        var names = entries.Select(static entry => entry.Name).ToArray();
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            names.OrderBy(static name => name, StringComparer.Ordinal).ToArray(),
            names);

        foreach (var entry in entries)
        {
            var resolved = runtime.GetTypeByName(entry.Name);
            Assert.NotNull(resolved);
            Assert.Equal(entry.Name, resolved!.Name);
            Assert.Equal(entry.FullName, resolved.FullName);
        }

        Assert.False(runtime.AreTypeResolutionCachesInitialized);
    }

    [Fact]
    public void BuiltinLookup_IsOrdinalAndUsesRuntimeTypeNames()
    {
        using var runtime = IsolatedRegistryRuntime.Create();

        Assert.Equal("System.String", runtime.GetTypeByName("String")?.FullName);
        Assert.Equal("Jalium.UI.Shapes.Path", runtime.GetTypeByName("Path")?.FullName);
        Assert.Equal("Jalium.UI.Styling.Css", runtime.GetTypeByName("Css")?.FullName);
        Assert.Equal(
            "Jalium.UI.Controls.Primitives.DataGridColumnHeader",
            runtime.GetTypeByName("DataGridColumnHeader")?.FullName);

        Assert.Null(runtime.GetTypeByName("string"));
        Assert.Null(runtime.GetTypeByName("path"));
        Assert.Null(runtime.GetTypeByName("BUTTON"));
        Assert.Null(runtime.GetTypeByName("DefinitelyNotABuiltinType_7A6C75D8"));
    }

    [Fact]
    public void ExplicitRegistration_LastWriteWinsAndOverridesBuiltin()
    {
        using var runtime = IsolatedRegistryRuntime.Create();

        Assert.Equal("Jalium.UI.Controls.Button", runtime.GetTypeByName("Button")?.FullName);

        runtime.RegisterExplicitType<Uri>("Button");
        Assert.Same(typeof(Uri), runtime.GetTypeByName("Button"));

        runtime.RegisterExplicitType<Version>("Button");
        Assert.Same(typeof(Version), runtime.GetTypeByName("Button"));
        Assert.False(runtime.AreTypeResolutionCachesInitialized);
    }

    [Theory]
    [InlineData("Button", "Jalium.UI.Controls.Button")]
    [InlineData("DefinitelyNotABuiltinType_7A6C75D8", null)]
    public void FirstBuiltinFallback_DoesNotAllocateWholeRuntimeTypeCatalog(
        string name,
        string? expectedFullName)
    {
        using var runtime = IsolatedRegistryRuntime.Create();

        // Initialize the explicit-registration holder and delegate path before measuring.
        // The remaining first fallback is the real cold builtin-name operation under test.
        runtime.RegisterExplicitType<Uri>("AllocationWarmup");
        Assert.Same(typeof(Uri), runtime.GetTypeByName("AllocationWarmup"));

        var before = GC.GetAllocatedBytesForCurrentThread();
        var resolved = runtime.GetTypeByName(name);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(expectedFullName, resolved?.FullName);
        Assert.InRange(allocated, 0, MaximumColdLookupAllocationBytes - 1);
    }

    [Fact]
    public void BuiltinResolvers_PreserveFullDamContract()
    {
        var registryType = typeof(Markup.XamlTypeRegistry);
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;

        var resolverMethods = registryType.GetMethods(flags)
            .Where(static method =>
                method.Name.StartsWith("Resolve", StringComparison.Ordinal) &&
                method.Name.EndsWith("BuiltinType", StringComparison.Ordinal))
            .OrderBy(static method => method.Name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(9, resolverMethods.Length);
        foreach (var method in resolverMethods)
        {
            var attribute = method.ReturnParameter.GetCustomAttribute<
                DynamicallyAccessedMembersAttribute>();
            Assert.NotNull(attribute);
            Assert.Equal(ExpectedBuiltinDam, attribute!.MemberTypes);
        }

        var publicGetType = registryType.GetMethod(
            "GetType",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: [typeof(string)],
            modifiers: null)!;
        var publicAttribute = publicGetType.ReturnParameter.GetCustomAttribute<
            DynamicallyAccessedMembersAttribute>();
        Assert.NotNull(publicAttribute);
        Assert.Equal(ExpectedBuiltinDam, publicAttribute!.MemberTypes);
    }

    [Fact]
    public void BuiltinCatalog_DoesNotRetainDictionaryOrInitializationSeam()
    {
        var registryType = typeof(Markup.XamlTypeRegistry);
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;

        Assert.Null(registryType.GetNestedType("BuiltinTypesHolder", BindingFlags.NonPublic));
        Assert.Null(registryType.GetMethod("InitializeTypes", flags));
        Assert.Null(registryType.GetProperty("IsBuiltinCatalogInitialized", flags));
    }

    private sealed class IsolatedRegistryRuntime : IDisposable
    {
        private const BindingFlags StaticFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        private readonly RegistryLoadContext _loadContext;
        private readonly Type _parserContextType;
        private readonly Func<string, Type?> _getTypeByName;
        private readonly MethodInfo _registerExplicitByName;

        private IsolatedRegistryRuntime(
            RegistryLoadContext loadContext,
            Assembly xamlAssembly)
        {
            _loadContext = loadContext;
            var registryType = xamlAssembly.GetType(
                "Jalium.UI.Markup.XamlTypeRegistry",
                throwOnError: true)!;
            _parserContextType = xamlAssembly.GetType(
                "Jalium.UI.Markup.XamlParserContext",
                throwOnError: true)!;
            _getTypeByName = (Func<string, Type?>)registryType.GetMethod(
                "GetType",
                StaticFlags,
                binder: null,
                types: [typeof(string)],
                modifiers: null)!
                .CreateDelegate(typeof(Func<string, Type?>));
            _registerExplicitByName = registryType.GetMethods(StaticFlags)
                .Single(static method =>
                    method.Name == "RegisterType" &&
                    method.IsGenericMethodDefinition &&
                    method.GetGenericArguments().Length == 1 &&
                    method.GetParameters() is [{ ParameterType: var parameterType }] &&
                    parameterType == typeof(string));
        }

        public static IsolatedRegistryRuntime Create()
        {
            var xamlPath = typeof(Markup.XamlTypeRegistry).Assembly.Location;
            var loadContext = new RegistryLoadContext(Path.GetDirectoryName(xamlPath)!);
            var xamlAssembly = loadContext.LoadFromAssemblyPath(xamlPath);
            return new IsolatedRegistryRuntime(loadContext, xamlAssembly);
        }

        public bool AreTypeResolutionCachesInitialized =>
            (bool)_parserContextType.GetProperty(
                "AreTypeResolutionCachesInitialized",
                StaticFlags)!
                .GetValue(null)!;

        public Type? GetTypeByName(string name) => _getTypeByName(name);

        public void RegisterExplicitType<T>(string name) =>
            _registerExplicitByName.MakeGenericMethod(typeof(T)).Invoke(null, [name]);

        public void Dispose() => _loadContext.Unload();
    }

    private sealed class RegistryLoadContext(string baseDirectory) : AssemblyLoadContext(
        name: $"XamlBuiltinRegistry_{Guid.NewGuid():N}",
        isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var simpleName = assemblyName.Name;
            if (simpleName == null ||
                !simpleName.StartsWith("Jalium.UI", StringComparison.Ordinal))
            {
                return null;
            }

            var candidate = Path.Combine(baseDirectory, simpleName + ".dll");
            return File.Exists(candidate) ? LoadFromAssemblyPath(candidate) : null;
        }
    }
}

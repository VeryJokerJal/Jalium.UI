extern alias sg;

using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using sg::Jalium.UI.Xaml.SourceGenerator;

namespace Jalium.UI.Tests;

public sealed class XamlTypeRegistryLazyInitializationTests
{
    [Fact]
    public void GeneratedDictionary_RegistersPrebuiltBuilderWithoutXamlTypeRegistryCalls()
    {
        const string jalxaml = """
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <Style TargetType="Button" />
            </ResourceDictionary>
            """;

        var projectDirectory = Path.Combine(Path.GetTempPath(), "JaliumRegistryGenerator");
        var sourcePath = Path.Combine(projectDirectory, "Themes", "LazyDictionary.jalxaml");
        var additionalText = new InMemoryAdditionalText(sourcePath, jalxaml);
        var optionsProvider = new TestAnalyzerConfigOptionsProvider(
            additionalText,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["build_property.MSBuildProjectDirectory"] = projectDirectory,
                ["build_property.RootNamespace"] = "GeneratedRegistry",
            },
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["build_metadata.AdditionalFiles.JalxamlSourceRelativePath"] =
                    "Themes\\LazyDictionary.jalxaml",
            });

        var compilation = CSharpCompilation.Create(
            assemblyName: $"GeneratedRegistry_{Guid.NewGuid():N}",
            syntaxTrees:
            [
                CSharpSyntaxTree.ParseText(
                    "namespace GeneratedRegistry { internal sealed class Marker { } }")
            ],
            references: GetMetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        IIncrementalGenerator generator = new JalxamlSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [generator.AsSourceGenerator()],
            additionalTexts: [additionalText],
            optionsProvider: optionsProvider);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics);

        Assert.DoesNotContain(
            generatorDiagnostics,
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(
            outputCompilation.GetDiagnostics(),
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var generatedSource = driver.GetRunResult().Results
            .SelectMany(static result => result.GeneratedSources)
            .Single(static source => source.HintName.StartsWith(
                "_GeneratedDict_",
                StringComparison.Ordinal))
            .SourceText
            .ToString();

        Assert.Contains(
            "XamlPrebuiltDictionaryRegistry.Register(\"GeneratedRegistry.Themes.LazyDictionary.jalxaml\", Build);",
            generatedSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "typeof(global::Jalium.UI.Controls.Button)",
            generatedSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "XamlTypeRegistry.",
            generatedSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void StartupExplicitAndFactoryRegistration_StayOffReflectionCaches()
    {
        using var runtime = IsolatedJaliumRuntime.Create();

        Assert.False(runtime.AreTypeResolutionCachesInitialized);

        runtime.RegisterStartupType("Probe.Startup", typeof(Uri));
        runtime.RegisterStartupUri("Views/Probe.jalxaml", typeof(Uri));
        runtime.RegisterExplicitType<Uri>("ProbeExplicit");
        runtime.RegisterAndQueryPrebuiltFactory("Probe.Generated.Dictionary.jalxaml");

        Assert.Same(typeof(Uri), runtime.GetStartupType("Probe.Startup"));
        Assert.Same(typeof(Uri), runtime.GetStartupTypeByUri("views/probe.jalxaml"));
        Assert.Same(typeof(Uri), runtime.GetTypeByName("ProbeExplicit"));
        Assert.False(runtime.AreTypeResolutionCachesInitialized);
    }

    [Fact]
    public void ExplicitRegistration_OverridesBuiltinBeforeAndAfterBuiltinLookup()
    {
        using var runtime = IsolatedJaliumRuntime.Create();

        runtime.RegisterExplicitType<Uri>("Button");

        Assert.Same(typeof(Uri), runtime.GetTypeByName("Button"));

        Assert.Equal(
            "Jalium.UI.Controls.TextBlock",
            runtime.GetTypeByName("TextBlock")?.FullName);
        Assert.Same(typeof(Uri), runtime.GetTypeByName("Button"));
        Assert.False(runtime.AreTypeResolutionCachesInitialized);
    }

    [Fact]
    public void PrecompiledBlankWindowAndTitleBar_DoNotNeedBuiltinNameCatalog()
    {
        using var runtime = IsolatedJaliumRuntime.Create();

        var window = runtime.CreateWindow();
        var titleBar = window.GetType().GetProperty("TitleBar")!.GetValue(window);

        Assert.NotNull(titleBar);
        Assert.Equal("Jalium.UI.Controls.TitleBar", titleBar!.GetType().FullName);
        Assert.False(runtime.AreTypeResolutionCachesInitialized);
    }

    [Fact]
    public void RuntimeXaml_ResolvesBuiltinAndInitializesResolutionCachesOnDemand()
    {
        using var runtime = IsolatedJaliumRuntime.Create();

        Assert.False(runtime.AreTypeResolutionCachesInitialized);

        var parsed = runtime.Parse("""
            <Button xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    Content="runtime" />
            """);

        Assert.Equal("Jalium.UI.Controls.Button", parsed.GetType().FullName);
        Assert.True(runtime.AreTypeResolutionCachesInitialized);
    }

    [Fact]
    public async Task ConcurrentRegistrationAndLookup_PreserveExplicitPrecedence()
    {
        using var runtime = IsolatedJaliumRuntime.Create();
        runtime.RegisterExplicitType<Uri>("Button");

        var tasks = Enumerable.Range(0, 32).Select(async worker =>
        {
            await Task.Yield();
            for (var iteration = 0; iteration < 64; iteration++)
            {
                var suffix = $"{worker}_{iteration}";
                var explicitName = "Probe_" + suffix;
                var startupName = "Probe.Startup." + suffix;
                var startupUri = "Views/Probe_" + suffix + ".jalxaml";

                runtime.RegisterExplicitType<Uri>(explicitName);
                runtime.RegisterStartupType(startupName, typeof(Uri));
                runtime.RegisterStartupUri(startupUri, typeof(Uri));

                Assert.Same(typeof(Uri), runtime.GetTypeByName(explicitName));
                Assert.Same(typeof(Uri), runtime.GetStartupType(startupName));
                Assert.Same(typeof(Uri), runtime.GetStartupTypeByUri(startupUri));
                Assert.Same(typeof(Uri), runtime.GetTypeByName("Button"));

                if ((iteration & 7) == 0)
                {
                    Assert.Equal(
                        "Jalium.UI.Controls.TextBlock",
                        runtime.GetTypeByName("TextBlock")?.FullName);
                }
            }
        });

        await Task.WhenAll(tasks);

        Assert.Same(typeof(Uri), runtime.GetTypeByName("Button"));
        Assert.False(runtime.AreTypeResolutionCachesInitialized);
    }

    private static IEnumerable<MetadataReference> GetMetadataReferences()
    {
        var trustedPlatformAssemblies =
            (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException(
                "The runtime did not expose trusted platform assemblies.");

        var jaliumAssemblies = new[]
        {
            typeof(ResourceDictionary).Assembly.Location,
            typeof(Controls.Button).Assembly.Location,
            typeof(Markup.XamlBuildContext).Assembly.Location,
            typeof(Markup.XamlPrebuiltDictionaryRegistry).Assembly.Location,
            typeof(Markup.XamlTypeRegistry).Assembly.Location,
        };

        return trustedPlatformAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Concat(jaliumAssemblies)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(static path => MetadataReference.CreateFromFile(path));
    }

    private sealed class InMemoryAdditionalText(string path, string content) : AdditionalText
    {
        private readonly SourceText _text = SourceText.From(content);

        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default) => _text;
    }

    private sealed class DictionaryAnalyzerConfigOptions(
        IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value) =>
            values.TryGetValue(key, out value!);
    }

    private sealed class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private static readonly AnalyzerConfigOptions Empty =
            new DictionaryAnalyzerConfigOptions(
                new Dictionary<string, string>(StringComparer.Ordinal));

        private readonly AdditionalText _additionalText;
        private readonly AnalyzerConfigOptions _fileOptions;

        public TestAnalyzerConfigOptionsProvider(
            AdditionalText additionalText,
            IReadOnlyDictionary<string, string> globalOptions,
            IReadOnlyDictionary<string, string> fileOptions)
        {
            _additionalText = additionalText;
            GlobalOptions = new DictionaryAnalyzerConfigOptions(globalOptions);
            _fileOptions = new DictionaryAnalyzerConfigOptions(fileOptions);
        }

        public override AnalyzerConfigOptions GlobalOptions { get; }

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => Empty;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) =>
            ReferenceEquals(textFile, _additionalText) ? _fileOptions : Empty;
    }

    private sealed class IsolatedJaliumRuntime : IDisposable
    {
        private const BindingFlags StaticFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        private readonly JaliumLoadContext _loadContext;
        private readonly Type _registryType;
        private readonly Type _readerType;
        private readonly Type _parserContextType;
        private readonly MethodInfo _getTypeByName;
        private readonly MethodInfo _registerExplicitByName;

        private IsolatedJaliumRuntime(
            JaliumLoadContext loadContext,
            Assembly xamlAssembly)
        {
            _loadContext = loadContext;
            _registryType = xamlAssembly.GetType(
                "Jalium.UI.Markup.XamlTypeRegistry",
                throwOnError: true)!;
            _readerType = xamlAssembly.GetType(
                "Jalium.UI.Markup.XamlReader",
                throwOnError: true)!;
            _parserContextType = xamlAssembly.GetType(
                "Jalium.UI.Markup.XamlParserContext",
                throwOnError: true)!;
            _getTypeByName = _registryType.GetMethod(
                "GetType",
                StaticFlags,
                binder: null,
                types: [typeof(string)],
                modifiers: null)!;
            _registerExplicitByName = _registryType.GetMethods(StaticFlags)
                .Single(static method =>
                    method.Name == "RegisterType" &&
                    method.IsGenericMethodDefinition &&
                    method.GetGenericArguments().Length == 1 &&
                    method.GetParameters() is [{ ParameterType: var parameterType }] &&
                    parameterType == typeof(string));
        }

        public static IsolatedJaliumRuntime Create()
        {
            var xamlPath = typeof(Markup.XamlTypeRegistry).Assembly.Location;
            var loadContext = new JaliumLoadContext(Path.GetDirectoryName(xamlPath)!);
            var xamlAssembly = loadContext.LoadFromAssemblyPath(xamlPath);
            return new IsolatedJaliumRuntime(loadContext, xamlAssembly);
        }

        public bool AreTypeResolutionCachesInitialized =>
            GetInternalBoolean(_parserContextType, "AreTypeResolutionCachesInitialized");

        public Type? GetTypeByName(string name) =>
            (Type?)_getTypeByName.Invoke(null, [name]);

        public void RegisterExplicitType<T>(string name) =>
            _registerExplicitByName.MakeGenericMethod(typeof(T)).Invoke(null, [name]);

        public void RegisterStartupType(string fullName, Type type) =>
            InvokeRegistry("RegisterStartupType", fullName, type);

        public Type? GetStartupType(string fullName) =>
            (Type?)InvokeRegistry("GetStartupType", fullName);

        public void RegisterStartupUri(string startupUri, Type type) =>
            InvokeRegistry("RegisterStartupUri", startupUri, type);

        public Type? GetStartupTypeByUri(string startupUri) =>
            (Type?)InvokeRegistry("GetStartupTypeByUri", startupUri);

        public object Parse(string xaml)
        {
            var parse = _readerType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(static method =>
                    method.Name == "Parse" &&
                    method.GetParameters() is [{ ParameterType: var parameterType }] &&
                    parameterType == typeof(string));
            return parse.Invoke(null, [xaml])!;
        }

        public object CreateWindow()
        {
            var managedAssembly = _loadContext.LoadFromAssemblyName(
                new AssemblyName("Jalium.UI.Managed"));
            var windowType = managedAssembly.GetType(
                "Jalium.UI.Window",
                throwOnError: true)!;
            return Activator.CreateInstance(windowType)!;
        }

        public void RegisterAndQueryPrebuiltFactory(string resourceName)
        {
            var coreAssembly = _loadContext.LoadFromAssemblyName(
                new AssemblyName("Jalium.UI.Core"));
            var prebuiltRegistry = coreAssembly.GetType(
                "Jalium.UI.Markup.XamlPrebuiltDictionaryRegistry",
                throwOnError: true)!;
            var factoryType = prebuiltRegistry.GetNestedType(
                "DictionaryFactory",
                BindingFlags.Public)!;
            var invoke = factoryType.GetMethod("Invoke")!;
            var constructor = invoke.ReturnType.GetConstructor(Type.EmptyTypes)!;
            var factory = System.Linq.Expressions.Expression.Lambda(
                factoryType,
                System.Linq.Expressions.Expression.New(constructor))
                .Compile();

            prebuiltRegistry.GetMethod("RegisterFactory", StaticFlags)!
                .Invoke(null, [resourceName, factory]);

            object?[] arguments = [resourceName, null];
            var found = (bool)prebuiltRegistry.GetMethod("TryGetFactory", StaticFlags)!
                .Invoke(null, arguments)!;
            Assert.True(found);
            Assert.NotNull(arguments[1]);
        }

        public void Dispose() => _loadContext.Unload();

        private object? InvokeRegistry(string methodName, params object?[] arguments) =>
            _registryType.GetMethod(methodName, StaticFlags)!.Invoke(null, arguments);

        private static bool GetInternalBoolean(Type type, string propertyName) =>
            (bool)type.GetProperty(propertyName, StaticFlags)!.GetValue(null)!;
    }

    private sealed class JaliumLoadContext(string baseDirectory) : AssemblyLoadContext(
        name: $"XamlRegistry_{Guid.NewGuid():N}",
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

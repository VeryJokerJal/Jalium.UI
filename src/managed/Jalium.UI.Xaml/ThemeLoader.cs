using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Microsoft.Extensions.DependencyInjection;

namespace Jalium.UI.Markup;

/// <summary>
/// Provides theme loading utilities for the Jalium.UI framework.
/// </summary>
public static class ThemeLoader
{
    private const string LegacyXamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    private const string JaliumMarkupNamespace = "https://schemas.jalium.dev/jalxaml/markup";
    private const string ComponentSeparator = ";component/";

    /// <summary>
    /// Module initializer that registers the XAML loader callback with ThemeManager.
    /// This runs automatically when the Jalium.UI.Xaml assembly is loaded,
    /// eliminating the need for reflection to bridge Controls and Xaml projects (AOT-safe).
    /// </summary>
    [ModuleInitializer]
    [SuppressMessage("Usage", "CA2255:The 'ModuleInitializer' attribute should not be used in libraries")]
    [SuppressMessage("Trimming", "IL2026:ModuleInitializer cannot declare RequiresUnreferencedCode by design.", Justification = "Module initializer is invoked by the runtime; downstream public callers (XamlReader.Load, ThemeManager.Initialize) carry the RUC contract.")]
    public static void Initialize()
    {
        ThemeManager.XamlLoader = LoadResourceDictionaryFromStream;
        ResourceDictionary.SourceLoader = LoadReferencedResourceDictionary;
        Application.StartupObjectLoader = LoadStartupObjectFromUri;
        Application.ComponentLoader = LoadComponentIntoExistingObject;
        Application.ComponentObjectLoader = LoadApplicationComponentFromUri;

        // Register AOT-safe type resolver for PropertyPath and other Core types
        TypeResolver.ResolveTypeByName = XamlTypeRegistry.GetType;

        // 把 TypeConverterRegistry 注入到 Core.Style 作为字符串值转换 fallback。
        // 这样 jalxaml 里写的 <Setter Property="Cursor" Value="Hand" /> 等字符串值，
        // Style.ConvertValueIfNeeded 内置 fast-path 没命中时会走完整的 TypeConverter
        // 管线（Brush / GridLength / IconElement 等），而不是把字符串原样塞进目标 DP
        // 让渲染层强转崩溃。
        Style.StringValueConverter = TypeConverterRegistry.ConvertValue;

        // NOTE on intentional restraint: this ModuleInitializer ONLY registers callbacks
        // (XamlLoader, SourceLoader, StartupObjectLoader, type resolver, value converter).
        // It MUST NOT call back into ThemeManager.Initialize here — even guarded by
        // "Application.Current != null && !IsInitialized". The CLR does not guarantee
        // ordering between sibling [ModuleInitializer] methods in the same assembly, so a
        // reentry from here may run before XamlBuilderInitializer.Register has wired
        // XamlBuilder.BeginComponentImpl, leading to "BeginComponentImpl has not been
        // registered" when LoadGenericTheme tries to materialize the prebuilt dictionary.
        // Theme loading is owned by Application.ctor → ThemeManager.Initialize, which calls
        // EnsureXamlLoaderRegistered first; that path is the single, ordered driver.
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Defers to LoadResourceDictionaryFromPayload which uses XamlReader (RUC).")]
    private static ResourceDictionary? LoadResourceDictionaryFromStream(
        Stream stream, string resourceName, Assembly sourceAssembly)
    {
        try
        {
            // Top-level entry from ThemeManager.LoadGenericTheme — same prebuilt-dictionary
            // shortcut as LoadReferencedResourceDictionary. When the SG emitted a builder
            // for this resource we skip the embedded-resource read entirely; the framework
            // theme dictionaries are the hot path here (Generic.jalxaml + 27 control
            // dictionaries).
            if (TryBuildFromPrebuiltRegistryByResourceName(resourceName, sourceAssembly, out var prebuilt))
            {
                return prebuilt;
            }

            // No prebuilt builder — fall back to streaming the manifest resource through
            // the runtime XAML reader. An empty/zero-length stream means the manifest
            // entry is missing AND no prebuilt builder matched, so theming will silently
            // degrade.
            if (stream.CanSeek && stream.Length == 0)
            {
                return null;
            }

            using var payloadStream = new MemoryStream();
            stream.CopyTo(payloadStream);
            return LoadResourceDictionaryFromPayload(payloadStream.ToArray(), resourceName, sourceAssembly, null);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Try to build a ResourceDictionary from <see cref="XamlPrebuiltDictionaryRegistry"/>
    /// keyed by a top-level resource name. Tries multiple spellings since the SG's
    /// registration uses the canonical manifest form (<c>{AssemblyName}.{path}.jalxaml</c>)
    /// while callers may pass other shapes (path-only, slash-separated, dot-separated).
    /// </summary>
    private static bool TryBuildFromPrebuiltRegistryByResourceName(string resourceName, Assembly sourceAssembly, out ResourceDictionary? dict)
    {
        var assemblyName = sourceAssembly.GetName().Name ?? string.Empty;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var normalizedSlash = resourceName.Replace('\\', '/').TrimStart('/');
        var dotted = normalizedSlash.Replace('/', '.');

        // CRITICAL: BaseUri must use the slash form so the URI parser's
        // relative-resolution treats path segments correctly. Generic.jalxaml then
        // resolves "<ResourceDictionary Source='DarkTheme.jalxaml' />" against
        //   resource:///Jalium.UI.Controls/Themes/Generic.jalxaml
        // → resource:///Jalium.UI.Controls/Themes/DarkTheme.jalxaml      (slash form keeps "Themes/")
        //
        // Using the dotted manifest name as a URI segment collapses the entire
        // "Themes/Generic.jalxaml" path into a single filename token; relative
        // resolution then strips it whole and the child becomes
        //   resource:///Jalium.UI.Controls/DarkTheme.jalxaml             (loses "Themes/")
        // which makes prebuilt-registry lookups for the child miss every spelling.
        var sourceUri = new Uri($"resource:///{assemblyName}/{normalizedSlash}", UriKind.Absolute);

        foreach (var candidate in new[]
        {
            resourceName,
            normalizedSlash,
            dotted,
            $"{assemblyName}.{normalizedSlash}",
            $"{assemblyName}.{dotted}",
        })
        {
            if (string.IsNullOrEmpty(candidate) || !seen.Add(candidate))
                continue;

            if (XamlPrebuiltDictionaryRegistry.TryGetFactory(candidate, out var factory) && factory != null)
            {
                var typed = factory();
                typed.Source = sourceUri;
                typed.BaseUri = sourceUri;
                typed.SourceAssembly = sourceAssembly;
                dict = typed;
                return true;
            }

            if (XamlPrebuiltDictionaryRegistry.TryGet(candidate, out var builder) && builder != null)
            {
                var built = new ResourceDictionary
                {
                    Source = sourceUri,
                    BaseUri = sourceUri,
                    SourceAssembly = sourceAssembly,
                };
                using (built.DeferNotifications())
                {
                    var ctx = XamlBuilder.BeginComponent(built, sourceUri, sourceAssembly);
                    builder(built, ctx);
                    XamlBuilder.EndComponent(built, ctx);
                }
                dict = built;
                return true;
            }
        }

        dict = null;
        return false;
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Defers to LoadResourceDictionaryFromPayload which uses XamlReader (RUC).")]
    private static ResourceDictionary? LoadReferencedResourceDictionary(
        ResourceDictionary owner,
        Uri sourceUri,
        Assembly? sourceAssembly)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(sourceUri);

        try
        {
            var sourceUriText = sourceUri.ToString();
            var assembly = sourceAssembly;
            var pathCandidates = new List<string>();

            if (TryParsePackComponentUri(sourceUriText, out var packAssemblyName, out var componentPath))
            {
                assembly = ResolveAssembly(packAssemblyName);
                if (assembly == null)
                {
                    LogResourceDictionaryLoadFailure(sourceUri, $"Pack component assembly '{packAssemblyName}' could not be loaded.");
                    return null;
                }

                pathCandidates.AddRange(BuildPathCandidates(componentPath));
            }
            else if (sourceUri.IsAbsoluteUri &&
                     sourceUri.Scheme.Equals("resource", StringComparison.OrdinalIgnoreCase))
            {
                var (resourceAssembly, resourcePath) = ParseResourceUri(sourceUri.AbsoluteUri);
                assembly = ResolveAssembly(resourceAssembly);
                if (assembly == null)
                {
                    LogResourceDictionaryLoadFailure(sourceUri, $"Resource assembly '{resourceAssembly}' could not be loaded.");
                    return null;
                }

                pathCandidates.AddRange(BuildPathCandidates(resourcePath));
            }
            else
            {
                if (assembly == null)
                {
                    LogResourceDictionaryLoadFailure(sourceUri, "Source assembly is null and URI is relative — no assembly to resolve embedded resource against.");
                    return null;
                }

                pathCandidates.AddRange(BuildPathCandidates(sourceUri.IsAbsoluteUri
                    ? sourceUri.AbsolutePath
                    : sourceUri.OriginalString));
            }

            if (assembly == null || pathCandidates.Count == 0)
            {
                LogResourceDictionaryLoadFailure(sourceUri, $"No path candidates resolved (assembly={assembly?.GetName().Name ?? "<null>"}).");
                return null;
            }

            // Compile-time generated dictionaries first. The SourceGenerator emits one
            // builder per prebuilt jalxaml resource and registers it in
            // XamlPrebuiltDictionaryRegistry under the canonical manifest name. When the
            // lookup hits, we skip the entire embedded-resource + XML reader path — at
            // startup this is the single biggest theme-loading saving (no XML lex / no
            // element-name → Type reflection / no per-attribute type-converter dispatch).
            var prebuilt = TryBuildFromPrebuiltRegistry(owner, sourceUri, assembly, pathCandidates);
            if (prebuilt != null)
            {
                return prebuilt;
            }

            var attemptedResourceNames = new List<string>();
            using var stream = TryOpenEmbeddedResource(assembly, pathCandidates, attemptedResourceNames, out var resolvedResourceName);
            if (stream == null || string.IsNullOrEmpty(resolvedResourceName))
            {
                LogResourceDictionaryLoadFailure(sourceUri,
                    $"Embedded resource not found in '{assembly.GetName().Name}'. Attempted: [{string.Join(", ", attemptedResourceNames)}].");
                return null;
            }

            using var payloadStream = new MemoryStream();
            stream.CopyTo(payloadStream);
            var result = LoadResourceDictionaryFromPayload(payloadStream.ToArray(), resolvedResourceName, assembly, sourceUri);
            return result;
        }
        catch (Exception ex)
        {
            LogResourceDictionaryLoadFailure(sourceUri, ex.ToString());
            return null;
        }
    }

    /// <summary>
    /// Iterate the SDK-style manifest-resource candidates and check the SG-emitted
    /// <see cref="XamlPrebuiltDictionaryRegistry"/>. Returns a fully-populated
    /// ResourceDictionary on the first hit (no XML reader, no embedded resource read);
    /// returns null when no candidate is registered, in which case the caller takes
    /// the legacy embedded-resource path.
    /// </summary>
    /// <remarks>
    /// We mirror the candidate-name fan-out used by <see cref="TryOpenEmbeddedResource"/>:
    /// each path is normalised to <c>foo/bar/baz.jalxaml</c>, then the registry is queried
    /// under both the raw form and the dotted form, with and without the assembly name
    /// prefix. The SG emits the canonical <c>{RootNamespace}.{dotted}</c> form; the rest
    /// are tried as a safety net for projects whose RootNamespace differs from
    /// AssemblyName.
    /// </remarks>
    private static ResourceDictionary? TryBuildFromPrebuiltRegistry(
        ResourceDictionary owner,
        Uri sourceUri,
        Assembly assembly,
        IReadOnlyList<string> pathCandidates)
    {
        var assemblyName = assembly.GetName().Name ?? string.Empty;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in pathCandidates)
        {
            var normalized = path.Replace('\\', '/').TrimStart('/');
            var dotted = normalized.Replace('/', '.');

            foreach (var candidate in new[]
            {
                $"{assemblyName}.{dotted}",
                dotted,
                $"{assemblyName}.{normalized}",
                normalized,
            })
            {
                if (!seen.Add(candidate))
                    continue;

                // Typed factory registration wins — the codebehind ctor has already populated
                // the returned instance via the SG-generated InitializeComponent. We only set
                // Source/BaseUri/SourceAssembly so downstream relative-URI resolution and
                // theme-cache invalidation see the right metadata; we never re-run a builder.
                if (XamlPrebuiltDictionaryRegistry.TryGetFactory(candidate, out var factory) && factory != null)
                {
                    var typedDict = factory();
                    typedDict.Source = sourceUri;
                    typedDict.BaseUri = sourceUri;
                    typedDict.SourceAssembly = assembly;
                    return typedDict;
                }

                if (XamlPrebuiltDictionaryRegistry.TryGet(candidate, out var builder) && builder != null)
                {
                    var dict = new ResourceDictionary
                    {
                        Source = sourceUri,
                        BaseUri = sourceUri,
                        SourceAssembly = assembly,
                    };

                    // Bulk-load: coalesce the dozens-to-hundreds of OnChangedForKey calls the
                    // builder triggers (one per Style.TargetType / Setter.Value insertion)
                    // into a single deferred notification when the dict is finally added to
                    // its parent's MergedDictionaries. Without this every individual Add
                    // bumps the global resource cache generation and re-fires Changed up the
                    // ancestor chain — turning a 60ms dict build into 600ms+ when N MergedDicts
                    // are nested. The scope only suppresses outbound events while the dict is
                    // still detached; consumers see a single coalesced "all keys changed"
                    // notification once the dict is published.
                    using (dict.DeferNotifications())
                    {
                        var ctx = XamlBuilder.BeginComponent(dict, sourceUri, assembly);
                        builder(dict, ctx);
                        XamlBuilder.EndComponent(dict, ctx);
                    }
                    return dict;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// 将 ResourceDictionary 加载失败的细节通过 Trace 暴露。静默吞匹配 WPF 行为
    /// （单个 Source 失败不应让整个字典爆炸），但开发期需要看到具体原因，否则
    /// 用户面对的就是"控件渲染空白、没有任何提示"的黑盒。
    /// </summary>
    private static void LogResourceDictionaryLoadFailure(Uri sourceUri, string detail)
    {
        var message = $"[Jalium.UI] ResourceDictionary 加载失败: '{sourceUri}'. {detail}";
        System.Diagnostics.Trace.WriteLine(message);
        System.Diagnostics.Debug.WriteLine(message);
    }

    /// <summary>
    /// Loads a ResourceDictionary from a stream containing XAML content.
    /// </summary>
    /// <param name="stream">The stream containing the XAML content.</param>
    /// <returns>The loaded ResourceDictionary, or null if loading failed.</returns>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Defers to XamlReader.Load which carries the same RUC contract.")]
    public static ResourceDictionary? LoadResourceDictionary(Stream stream)
    {
        try
        {
            return XamlReader.Load(stream) as ResourceDictionary;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Loads the Generic theme ResourceDictionary.
    /// </summary>
    /// <returns>The Generic theme ResourceDictionary, or null if loading failed.</returns>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Defers to LoadResourceDictionary which carries the same RUC contract.")]
    public static ResourceDictionary? LoadGenericTheme()
    {
        using var stream = ThemeManager.GetGenericThemeStream();
        if (stream == null)
            return null;

        return LoadResourceDictionary(stream);
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Loads a startup XAML object from a stream via XamlReader, which carries the RUC contract.")]
    private static object? LoadStartupObjectFromUri(Application? app, Uri startupUri)
    {
        ArgumentNullException.ThrowIfNull(startupUri);

        var startupUriText = startupUri.IsAbsoluteUri ? startupUri.AbsoluteUri : startupUri.OriginalString;
        if (string.IsNullOrWhiteSpace(startupUriText))
            return null;

        var appAssembly = app?.GetType().Assembly ?? Application.ResourceAssembly;
        var assembly = appAssembly;
        var pathCandidates = new List<string>();

        if (TryParsePackComponentUri(startupUriText, out var packAssemblyName, out var componentPath))
        {
            assembly = ResolveAssembly(packAssemblyName)
                ?? throw new InvalidOperationException(
                    $"StartupUri '{startupUriText}' references assembly '{packAssemblyName}', but it could not be loaded.");

            pathCandidates.AddRange(BuildPathCandidates(componentPath));
        }
        else if (startupUri.IsAbsoluteUri &&
                 startupUri.Scheme.Equals("resource", StringComparison.OrdinalIgnoreCase))
        {
            var (resourceAssembly, resourcePath) = ParseResourceUri(startupUri.AbsoluteUri);
            assembly = ResolveAssembly(resourceAssembly)
                ?? throw new InvalidOperationException(
                    $"StartupUri '{startupUriText}' references assembly '{resourceAssembly}', but it could not be loaded.");

            pathCandidates.AddRange(BuildPathCandidates(resourcePath));
        }
        else if (startupUriText.StartsWith("/", StringComparison.Ordinal))
        {
            pathCandidates.AddRange(BuildPathCandidates(startupUriText.TrimStart('/')));
        }
        else
        {
            pathCandidates.AddRange(BuildPathCandidates(startupUriText));
        }

        if (pathCandidates.Count == 0)
        {
            throw new InvalidOperationException($"StartupUri '{startupUriText}' is not a valid startup path.");
        }

        // Compile-time path: the SG registers (uri spelling → type) entries in its
        // ModuleInitializer for every code-behind jalxaml. When the StartupUri matches
        // one of those entries we skip the manifest-resource lookup entirely — the
        // codebehind ctor + SG-generated InitializeComponent already build the visual
        // tree. This is the preferred path now that .jalxaml is no longer embedded.
        foreach (var candidate in pathCandidates)
        {
            var registered = XamlTypeRegistry.GetStartupTypeByUri(candidate);
            if (registered != null)
            {
                return CreateStartupInstance(registered);
            }
        }

        var attemptedResourceNames = new List<string>();
        var stream = TryOpenEmbeddedResource(assembly, pathCandidates, attemptedResourceNames, out var resolvedResourceName);
        if (stream == null || string.IsNullOrEmpty(resolvedResourceName))
        {
            throw new XamlParseException(
                $"Cannot resolve StartupUri '{startupUriText}' in assembly '{assembly.GetName().Name}'. " +
                $"Candidates=[{string.Join(", ", attemptedResourceNames)}]. " +
                $"Hint: when EmbedJalxamlSources=false the SourceGenerator must have observed " +
                $"the file at compile time so it could register the StartupUri mapping. " +
                $"Verify the .jalxaml file is included in @(JalxamlPage) / @(JalxamlApplicationDefinition).");
        }

        using (stream)
        {
            using var payloadStream = new MemoryStream();
            stream.CopyTo(payloadStream);
            var payload = payloadStream.ToArray();

            var className = TryReadRootClassName(payload);
            if (!string.IsNullOrWhiteSpace(className))
            {
                var startupType = ResolveStartupType(className!, assembly, appAssembly, out var fromSourceGenerator);
                if (startupType == null)
                {
                    throw new InvalidOperationException(
                        $"StartupUri '{startupUriText}' declares x:Class '{className}', but the type could not be resolved.");
                }

                object instance;
                try
                {
                    instance = CreateStartupInstance(startupType);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"StartupUri '{startupUriText}' failed to instantiate startup type '{startupType.FullName}'.", ex);
                }

                // 按 WPF 约定,x:Class 的 codebehind ctor 已调用 SG 生成的 InitializeComponent() 完成
                // XAML 加载。再调一次 LoadComponent 会 double-load:Content/子控件会被重新创建并替换掉
                // 旧实例,用户在 ctor 里对旧 named element 做的事件订阅、DataContext、binding 全部失效。
                //
                // SG 注册过的 startup type 一定已经在 ctor 里加载过 XAML — fromSourceGenerator 就是这个
                // 权威信号。仅当类型不是 SG 注册的(裸 partial、第三方手写 x:Class)才退到反射探测
                // InitializeComponent 作为兜底;反射在 AOT trim 之后会失效,SG 路径必须直跳过。
                if (!fromSourceGenerator && !HasInitializeComponent(startupType))
                {
                    XamlReader.LoadComponent(instance, resolvedResourceName, assembly);
                }

                return instance;
            }

            using var parseStream = new MemoryStream(payload);
            return XamlReader.Load(parseStream, resolvedResourceName, assembly);
        }
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Loads a XAML component through XamlReader, which carries the RUC contract.")]
    private static object? LoadApplicationComponentFromUri(Uri resourceLocator)
        => LoadStartupObjectFromUri(Application.Current, resourceLocator);

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Loads XAML into an existing component through XamlReader, which carries the RUC contract.")]
    private static void LoadComponentIntoExistingObject(object component, Uri resourceLocator)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(resourceLocator);

        var uriText = resourceLocator.OriginalString;
        var assembly = component.GetType().Assembly;
        string resourcePath;

        if (TryParsePackComponentUri(uriText, out var assemblyName, out var componentPath))
        {
            assembly = ResolveAssembly(assemblyName)
                ?? throw new XamlParseException(
                    $"Component URI '{uriText}' references assembly '{assemblyName}', but it could not be loaded.");
            resourcePath = componentPath;
        }
        else
        {
            resourcePath = uriText.TrimStart('/');
        }

        var candidates = BuildPathCandidates(resourcePath).ToArray();
        var attemptedNames = new List<string>();
        using var probe = TryOpenEmbeddedResource(assembly, candidates, attemptedNames, out var resolvedResourceName);
        if (probe == null || string.IsNullOrEmpty(resolvedResourceName))
        {
            throw new XamlParseException(
                $"Cannot resolve component URI '{uriText}' in assembly '{assembly.GetName().Name}'. " +
                $"Candidates=[{string.Join(", ", attemptedNames)}].");
        }

        XamlReader.LoadComponent(component, resolvedResourceName, assembly);
    }

    private static object CreateStartupInstance(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)]
        Type startupType)
    {
        // WPF 原版 StartupUri 只用 Activator.CreateInstance,因为 WPF 没有 DI 集成。
        // Jalium.UI 通过 AppBuilder 挂载 Microsoft.Extensions.DependencyInjection,允许
        // StartupUri 目标类型声明 DI 构造函数(如 public MainWindow(IMessageService svc))。
        // Activator.CreateInstance 要求 public parameterless ctor,遇到 DI ctor 直接抛
        // MissingMethodException — 这正是 "Jalium.UI.Gallery.*.MainWindow" 黑屏/崩溃的根因。
        // ActivatorUtilities.CreateInstance 既能从 IServiceProvider 解析 DI 参数,也能处理
        // 无参 ctor,是 Activator.CreateInstance 的严格超集。有 Services 时统一走它,没有
        // (纯 Jalium.UI.Controls 直连、无 AppBuilder)则 fallback 保持兼容。
        var services = Application.Current?.Services;
        if (services != null)
        {
            return ActivatorUtilities.CreateInstance(services, startupType);
        }

        return Activator.CreateInstance(startupType)
            ?? throw new InvalidOperationException($"Failed to create startup type '{startupType.FullName}'.");
    }

    private static bool HasInitializeComponent(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)]
        Type type)
    {
        return type.GetMethod(
            "InitializeComponent",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null) != null;
    }

    private static IEnumerable<string> BuildPathCandidates(string path)
    {
        var trimmed = path.Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrWhiteSpace(trimmed))
            yield break;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (seen.Add(trimmed))
            yield return trimmed;

        if (trimmed.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
        {
            var jalxaml = trimmed.Substring(0, trimmed.Length - ".xaml".Length) + ".jalxaml";
            if (seen.Add(jalxaml))
                yield return jalxaml;
        }
        else if (!trimmed.EndsWith(".jalxaml", StringComparison.OrdinalIgnoreCase))
        {
            var jalxaml = $"{trimmed}.jalxaml";
            if (seen.Add(jalxaml))
                yield return jalxaml;
        }
    }

    private static Stream? TryOpenEmbeddedResource(
        Assembly assembly,
        IReadOnlyList<string> pathCandidates,
        List<string> attemptedNames,
        out string? resolvedResourceName)
    {
        var assemblyName = assembly.GetName().Name ?? string.Empty;
        var manifestNames = assembly.GetManifestResourceNames();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in pathCandidates)
        {
            var normalized = path.Replace('\\', '/').TrimStart('/');
            var dotted = normalized.Replace('/', '.');
            var fileName = normalized.Split('/').LastOrDefault() ?? normalized;

            foreach (var candidate in new[]
            {
                normalized,
                dotted,
                $"{assemblyName}.{normalized}",
                $"{assemblyName}.{dotted}"
            })
            {
                if (!seen.Add(candidate))
                    continue;

                attemptedNames.Add(candidate);
                var stream = assembly.GetManifestResourceStream(candidate);
                if (stream != null)
                {
                    resolvedResourceName = candidate;
                    return stream;
                }

                var exactIgnoreCase = manifestNames.FirstOrDefault(n => string.Equals(n, candidate, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(exactIgnoreCase) && seen.Add(exactIgnoreCase))
                {
                    attemptedNames.Add(exactIgnoreCase);
                    stream = assembly.GetManifestResourceStream(exactIgnoreCase);
                    if (stream != null)
                    {
                        resolvedResourceName = exactIgnoreCase;
                        return stream;
                    }
                }
            }

            var suffixes = new[]
            {
                $".{dotted}",
                $".{normalized}",
                $".{fileName}"
            };

            var suffixMatch = manifestNames.FirstOrDefault(n =>
                suffixes.Any(s => n.EndsWith(s, StringComparison.OrdinalIgnoreCase)));

            if (!string.IsNullOrEmpty(suffixMatch) && seen.Add(suffixMatch))
            {
                attemptedNames.Add(suffixMatch);
                var stream = assembly.GetManifestResourceStream(suffixMatch);
                if (stream != null)
                {
                    resolvedResourceName = suffixMatch;
                    return stream;
                }
            }
        }

        resolvedResourceName = null;
        return null;
    }

    private static string? TryReadRootClassName(byte[] payload)
    {
        using var memory = new MemoryStream(payload, writable: false);
        var settings = new XmlReaderSettings
        {
            IgnoreComments = true,
            IgnoreWhitespace = true,
            IgnoreProcessingInstructions = true
        };

        using var reader = XmlReader.Create(memory, settings);
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element)
                continue;

            var className = reader.GetAttribute("Class", LegacyXamlNamespace);
            if (!string.IsNullOrWhiteSpace(className))
                return className;

            className = reader.GetAttribute("Class", JaliumMarkupNamespace);
            if (!string.IsNullOrWhiteSpace(className))
                return className;

            if (reader.HasAttributes)
            {
                for (var i = 0; i < reader.AttributeCount; i++)
                {
                    reader.MoveToAttribute(i);
                    if (!string.Equals(reader.LocalName, "Class", StringComparison.Ordinal))
                        continue;

                    if (string.Equals(reader.Prefix, "x", StringComparison.Ordinal) ||
                        string.Equals(reader.NamespaceURI, LegacyXamlNamespace, StringComparison.Ordinal) ||
                        string.Equals(reader.NamespaceURI, JaliumMarkupNamespace, StringComparison.Ordinal))
                    {
                        var value = reader.Value;
                        reader.MoveToElement();
                        return value;
                    }
                }

                reader.MoveToElement();
            }

            break;
        }

        return null;
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Falls back to Assembly.GetType(string) which carries the RUC contract for trimmed types.")]
    private static Type? ResolveStartupType(string className, Assembly preferredAssembly, Assembly appAssembly, out bool fromSourceGenerator)
    {
        // AOT root: after trimming, Assembly.GetType(string) returns null for any x:Class type
        // that has no static reference in IL. The JALXAML source generator emits a
        // [ModuleInitializer] for every jalxaml file that calls XamlTypeRegistry.RegisterStartupType
        // with typeof(T) — that typeof reference keeps the trimmer honest AND gives us a
        // string-keyed lookup that survives AOT. Consult the registry before reflection.
        //
        // Registry hit doubles as the authoritative "this type has SG-emitted InitializeComponent"
        // signal: the same ModuleInitializer that registers the type is generated alongside the
        // InitializeComponent body, so registry presence ⇒ ctor already loaded the XAML. Reflection
        // probing for InitializeComponent fails after AOT trim (private method metadata pruned),
        // so callers must rely on this flag to avoid a double-load.
        var registered = XamlTypeRegistry.GetStartupType(className);
        if (registered != null)
        {
            fromSourceGenerator = true;
            return registered;
        }

        fromSourceGenerator = false;

        var startupType = preferredAssembly.GetType(className, throwOnError: false);
        if (startupType != null)
            return startupType;

        startupType = appAssembly.GetType(className, throwOnError: false);
        if (startupType != null)
            return startupType;

        // Type.GetType(string) is intentionally omitted: it carries IL2057 because the
        // string is dynamic, and the loop below already covers every assembly Type.GetType
        // would have searched. The registry path above is the AOT-safe fast path.
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            startupType = assembly.GetType(className, throwOnError: false);
            if (startupType != null)
                return startupType;
        }

        return null;
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Defers to ResolveStartupType and XamlReader.Load, both RUC.")]
    private static ResourceDictionary? LoadResourceDictionaryFromPayload(
        byte[] payload,
        string resourceName,
        Assembly assembly,
        Uri? sourceUri)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(resourceName);
        ArgumentNullException.ThrowIfNull(assembly);

        var className = TryReadRootClassName(payload);
        if (!string.IsNullOrWhiteSpace(className))
        {
            var dictionaryType = ResolveStartupType(className!, assembly, assembly, out _);
            if (dictionaryType != null &&
                typeof(ResourceDictionary).IsAssignableFrom(dictionaryType) &&
                Activator.CreateInstance(dictionaryType) is ResourceDictionary typedDictionary)
            {
                if (sourceUri != null)
                    typedDictionary.Source = sourceUri;

                return typedDictionary;
            }
        }

        using var parseStream = new MemoryStream(payload, writable: false);
        var dictionary = XamlReader.Load(parseStream, resourceName, assembly) as ResourceDictionary;
        if (dictionary != null && sourceUri != null)
            dictionary.Source = sourceUri;

        return dictionary;
    }

    private static Assembly? ResolveAssembly(string assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName))
            return null;

        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, assemblyName, StringComparison.Ordinal));
        if (loaded != null)
            return loaded;

        try
        {
            return Assembly.Load(new AssemblyName(assemblyName));
        }
        catch
        {
            return null;
        }
    }

    private static bool TryParsePackComponentUri(string uri, out string assemblyName, out string componentPath)
    {
        assemblyName = string.Empty;
        componentPath = string.Empty;

        var separatorIndex = uri.IndexOf(ComponentSeparator, StringComparison.OrdinalIgnoreCase);
        if (separatorIndex < 0)
            return false;

        var assemblyPart = uri.Substring(0, separatorIndex);
        var slash = assemblyPart.LastIndexOf('/');
        if (slash >= 0)
        {
            assemblyPart = assemblyPart[(slash + 1)..];
        }

        if (string.IsNullOrWhiteSpace(assemblyPart))
            return false;

        var path = uri.Substring(separatorIndex + ComponentSeparator.Length).TrimStart('/');
        if (string.IsNullOrWhiteSpace(path))
            return false;

        assemblyName = assemblyPart;
        componentPath = path;
        return true;
    }

    private static (string assemblyName, string resourcePath) ParseResourceUri(string uriText)
    {
        const string prefix = "resource:///";
        if (!uriText.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unsupported resource URI format: '{uriText}'.");
        }

        var path = uriText.Substring(prefix.Length);
        var slash = path.IndexOf('/');
        if (slash < 0)
        {
            return (path, string.Empty);
        }

        return (
            path.Substring(0, slash),
            path.Substring(slash + 1).TrimStart('/'));
    }
}

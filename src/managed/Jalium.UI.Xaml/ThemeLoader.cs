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

    [UnconditionalSuppressMessage("Trimming", "IL2072",
        Justification = "Startup types reachable via StartupUri are preserved by the source generator and XamlTypeRegistry.")]
    private static object CreateStartupInstance(Type startupType)
    {
        // 优先走 ActivatorUtilities(当 Application 由 AppBuilder 创建、IServiceProvider 已挂上时可用),
        // 使得 StartupUri 指向的 window 可以声明 DI ctor:e.g. public MainWindow(IMessageService msg)。
        // 找不到 service provider、或该 ctor 无 DI 参数时自动 fallback 回 Activator.CreateInstance,
        // 保证传统 parameterless-ctor 场景不回归。
        var services = Application.Current?.Services;
        if (services != null)
        {
            try
            {
                return ActivatorUtilities.CreateInstance(services, startupType);
            }
            catch
            {
                // ActivatorUtilities 找不到合适 ctor 或 ctor 抛异常时,继续 fallback。
            }
        }

        return Activator.CreateInstance(startupType)
            ?? throw new InvalidOperationException($"Failed to create startup type '{startupType.FullName}'.");
    }

    /// <summary>
    /// Module initializer that registers the XAML loader callback with ThemeManager.
    /// This runs automatically when the Jalium.UI.Xaml assembly is loaded,
    /// eliminating the need for reflection to bridge Controls and Xaml projects (AOT-safe).
    /// </summary>
    [ModuleInitializer]
    [SuppressMessage("Usage", "CA2255:The 'ModuleInitializer' attribute should not be used in libraries")]
    public static void Initialize()
    {
        // Register XamlReader.Load as the theme loader callback
        ThemeManager.XamlLoader = LoadResourceDictionaryFromStream;
        ResourceDictionary.SourceLoader = LoadReferencedResourceDictionary;
        Application.StartupObjectLoader = LoadStartupObjectFromUri;

        // Register AOT-safe type resolver for PropertyPath and other Core types
        TypeResolver.ResolveTypeByName = XamlTypeRegistry.GetType;

        // If Application was already created but theme not yet loaded, load now
        if (Application.Current != null && !ThemeManager.IsInitialized)
        {
            ThemeManager.Initialize(Application.Current);
        }
    }

    private static ResourceDictionary? LoadResourceDictionaryFromStream(
        Stream stream, string resourceName, Assembly sourceAssembly)
    {
        try
        {
            using var payloadStream = new MemoryStream();
            stream.CopyTo(payloadStream);
            return LoadResourceDictionaryFromPayload(payloadStream.ToArray(), resourceName, sourceAssembly, null);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static ResourceDictionary? LoadReferencedResourceDictionary(
        ResourceDictionary owner,
        Uri sourceUri,
        Assembly? sourceAssembly)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(sourceUri);

        Jalium.UI.Markup.JalxamlDiagnostics.Log(
            "ThemeLoader.LoadReferencedResourceDictionary: uri='{0}' sourceAssembly='{1}'",
            sourceUri, sourceAssembly?.GetName().Name ?? "<null>");

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
                    Jalium.UI.Markup.JalxamlDiagnostics.Log("  pack uri: failed to resolve assembly '{0}'", packAssemblyName);
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
                    Jalium.UI.Markup.JalxamlDiagnostics.Log("  resource uri: failed to resolve assembly '{0}'", resourceAssembly);
                    return null;
                }

                pathCandidates.AddRange(BuildPathCandidates(resourcePath));
            }
            else
            {
                if (assembly == null)
                {
                    Jalium.UI.Markup.JalxamlDiagnostics.Log("  relative uri but sourceAssembly is null — cannot resolve");
                    return null;
                }

                pathCandidates.AddRange(BuildPathCandidates(sourceUri.IsAbsoluteUri
                    ? sourceUri.AbsolutePath
                    : sourceUri.OriginalString));
            }

            if (assembly == null || pathCandidates.Count == 0)
            {
                Jalium.UI.Markup.JalxamlDiagnostics.Log(
                    "  assembly={0} candidates={1} — giving up",
                    assembly?.GetName().Name ?? "<null>", pathCandidates.Count);
                return null;
            }

            Jalium.UI.Markup.JalxamlDiagnostics.Log(
                "  probing '{0}' for [{1}]",
                assembly.GetName().Name!,
                string.Join(", ", pathCandidates));

            var attemptedResourceNames = new List<string>();
            using var stream = TryOpenEmbeddedResource(assembly, pathCandidates, attemptedResourceNames, out var resolvedResourceName);
            if (stream == null || string.IsNullOrEmpty(resolvedResourceName))
            {
                Jalium.UI.Markup.JalxamlDiagnostics.Log(
                    "  resource NOT FOUND. attempted=[{0}]",
                    string.Join(", ", attemptedResourceNames));
                return null;
            }

            Jalium.UI.Markup.JalxamlDiagnostics.Log("  resolved '{0}', parsing...", resolvedResourceName);
            using var payloadStream = new MemoryStream();
            stream.CopyTo(payloadStream);
            var result = LoadResourceDictionaryFromPayload(payloadStream.ToArray(), resolvedResourceName, assembly, sourceUri);
            Jalium.UI.Markup.JalxamlDiagnostics.Log(
                "  payload parsed: {0}",
                result != null ? $"OK ({result.GetType().Name})" : "NULL");
            return result;
        }
        catch (Exception ex)
        {
            Jalium.UI.Markup.JalxamlDiagnostics.Log(
                "  EXCEPTION: {0}: {1}",
                ex.GetType().Name, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Loads a ResourceDictionary from a stream containing XAML content.
    /// </summary>
    /// <param name="stream">The stream containing the XAML content.</param>
    /// <returns>The loaded ResourceDictionary, or null if loading failed.</returns>
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
    public static ResourceDictionary? LoadGenericTheme()
    {
        using var stream = ThemeManager.GetGenericThemeStream();
        if (stream == null)
            return null;

        return LoadResourceDictionary(stream);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "Startup types are registered in XamlTypeRegistry or preserved by the application")]
    [UnconditionalSuppressMessage("Trimming", "IL2072:Target parameter argument",
        Justification = "Startup types are registered in XamlTypeRegistry or preserved by the application")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode",
        Justification = "Startup types are registered in XamlTypeRegistry or preserved by the application")]
    private static object? LoadStartupObjectFromUri(Application app, Uri startupUri)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(startupUri);

        var startupUriText = startupUri.IsAbsoluteUri ? startupUri.AbsoluteUri : startupUri.OriginalString;
        if (string.IsNullOrWhiteSpace(startupUriText))
            return null;

        var appAssembly = app.GetType().Assembly;
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

        var attemptedResourceNames = new List<string>();
        var stream = TryOpenEmbeddedResource(assembly, pathCandidates, attemptedResourceNames, out var resolvedResourceName);
        if (stream == null || string.IsNullOrEmpty(resolvedResourceName))
        {
            throw new XamlParseException(
                $"Cannot resolve StartupUri '{startupUriText}' in assembly '{assembly.GetName().Name}'. " +
                $"Candidates=[{string.Join(", ", attemptedResourceNames)}].");
        }

        using (stream)
        {
            using var payloadStream = new MemoryStream();
            stream.CopyTo(payloadStream);
            var payload = payloadStream.ToArray();

            var className = TryReadRootClassName(payload);
            if (!string.IsNullOrWhiteSpace(className))
            {
                var startupType = ResolveStartupType(className!, assembly, appAssembly);
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

                XamlReader.LoadComponent(instance, resolvedResourceName, assembly);
                return instance;
            }

            using var parseStream = new MemoryStream(payload);
            return XamlReader.Load(parseStream, resolvedResourceName, assembly);
        }
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

    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "Startup types are registered in XamlTypeRegistry or preserved by the application")]
    [UnconditionalSuppressMessage("Trimming", "IL2072:Target parameter argument",
        Justification = "Startup types are registered in XamlTypeRegistry or preserved by the application")]
    private static Type? ResolveStartupType(string className, Assembly preferredAssembly, Assembly appAssembly)
    {
        var startupType = preferredAssembly.GetType(className, throwOnError: false);
        if (startupType != null)
            return startupType;

        startupType = appAssembly.GetType(className, throwOnError: false);
        if (startupType != null)
            return startupType;

        startupType = Type.GetType(className, throwOnError: false);
        if (startupType != null)
            return startupType;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            startupType = assembly.GetType(className, throwOnError: false);
            if (startupType != null)
                return startupType;
        }

        return null;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "ResourceDictionary types are preserved by XamlTypeRegistry and ILLink descriptors")]
    [UnconditionalSuppressMessage("Trimming", "IL2072:Target parameter argument",
        Justification = "ResourceDictionary types are preserved by XamlTypeRegistry and ILLink descriptors")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode",
        Justification = "ResourceDictionary types are preserved by XamlTypeRegistry and ILLink descriptors")]
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
            var dictionaryType = ResolveStartupType(className!, assembly, assembly);
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

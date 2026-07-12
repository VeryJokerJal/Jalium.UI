using System.Net;
using System.Reflection;
using Jalium.UI.Navigation;
using Jalium.UI.Resources;
using NavigationEventArgs = Jalium.UI.Navigation.NavigationEventArgs;
using NavigatingCancelEventArgs = Jalium.UI.Navigation.NavigatingCancelEventArgs;

namespace Jalium.UI;

public partial class Application
{
    private const string ComponentSeparator = ";component/";
    private static readonly CookieContainer s_cookieContainer = new();
    private static readonly HttpClient s_remoteClient = CreateRemoteClient();

    /// <summary>
    /// Runtime hook installed by Jalium.UI.Xaml for loading XAML into an existing object.
    /// It keeps the Controls assembly independent of the XAML runtime assembly.
    /// </summary>
    internal static Action<object, Uri>? ComponentLoader { get; set; }

    /// <summary>
    /// Runtime hook installed by Jalium.UI.Xaml for materializing a XAML root object.
    /// </summary>
    internal static Func<Uri, object?>? ComponentObjectLoader { get; set; }

    /// <summary>
    /// Occurs when navigation to a fragment is requested anywhere in the application.
    /// </summary>
    public event FragmentNavigationEventHandler? FragmentNavigation;

    /// <summary>
    /// Occurs when navigation content has finished loading.
    /// </summary>
    public event LoadCompletedEventHandler? LoadCompleted;

    /// <summary>
    /// Occurs after a navigator finds and displays its target content.
    /// </summary>
    public event NavigatedEventHandler? Navigated;

    /// <summary>
    /// Occurs before a navigator starts changing content.
    /// </summary>
    public event NavigatingCancelEventHandler? Navigating;

    /// <summary>
    /// Occurs when a navigation cannot be completed.
    /// </summary>
    public event NavigationFailedEventHandler? NavigationFailed;

    /// <summary>
    /// Occurs as navigation data is read.
    /// </summary>
    public event NavigationProgressEventHandler? NavigationProgress;

    /// <summary>
    /// Occurs when an in-progress navigation is stopped.
    /// </summary>
    public event NavigationStoppedEventHandler? NavigationStopped;

    /// <summary>
    /// Returns a stream for a loose application content file.
    /// </summary>
    public static StreamResourceInfo? GetContentStream(Uri uriContent)
    {
        ArgumentNullException.ThrowIfNull(uriContent);
        if (uriContent.IsAbsoluteUri)
        {
            throw new ArgumentException("Content URIs must be relative.", nameof(uriContent));
        }

        var relativePath = GetPathWithoutQueryOrFragment(uriContent.OriginalString);
        foreach (var baseDirectory in EnumerateContentBaseDirectories())
        {
            var fullPath = TryResolveLooseFile(baseDirectory, relativePath);
            if (fullPath != null && File.Exists(fullPath))
            {
                return new StreamResourceInfo(
                    new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read),
                    GetContentType(fullPath));
            }
        }

        return null;
    }

    /// <summary>
    /// Returns a stream for a resource embedded in the application or a referenced assembly.
    /// </summary>
    public static StreamResourceInfo GetResourceStream(Uri uriResource)
    {
        ArgumentNullException.ThrowIfNull(uriResource);
        if (uriResource.IsAbsoluteUri && !IsApplicationPackUri(uriResource))
        {
            throw new ArgumentException(
                "Resource URIs must be relative or use the pack://application:,,,/ form.",
                nameof(uriResource));
        }

        if (!TryResolveResourceUri(uriResource, out var assembly, out var resourcePath))
        {
            throw new IOException($"The resource URI '{uriResource}' could not be resolved.");
        }

        var stream = TryOpenManifestResource(assembly, resourcePath, out var resolvedName);
        if (stream == null)
        {
            throw new IOException(
                $"The resource '{resourcePath}' was not found in assembly '{assembly.GetName().Name}'.");
        }

        return new StreamResourceInfo(stream, GetContentType(resolvedName ?? resourcePath));
    }

    /// <summary>
    /// Returns a stream for a site-of-origin or remote resource.
    /// </summary>
    public static StreamResourceInfo? GetRemoteStream(Uri uriRemote)
    {
        ArgumentNullException.ThrowIfNull(uriRemote);

        if (!uriRemote.IsAbsoluteUri)
        {
            return GetContentStream(uriRemote);
        }

        if (IsSiteOfOriginPackUri(uriRemote))
        {
            var path = ExtractPackPath(uriRemote.OriginalString);
            return GetContentStream(new Uri(path, UriKind.Relative));
        }

        if (uriRemote.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException(
                "Remote URIs must be relative, HTTP(S), or use the pack://siteoforigin:,,,/ form.",
                nameof(uriRemote));
        }

        using var response = s_remoteClient.GetAsync(uriRemote, HttpCompletionOption.ResponseHeadersRead)
            .GetAwaiter()
            .GetResult();
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        using var responseStream = response.Content.ReadAsStream();
        var payload = new MemoryStream();
        responseStream.CopyTo(payload);
        payload.Position = 0;

        var contentType = response.Content.Headers.ContentType?.ToString();
        if (string.IsNullOrWhiteSpace(contentType))
        {
            contentType = GetContentType(uriRemote.AbsolutePath);
        }

        return new StreamResourceInfo(payload, contentType);
    }

    /// <summary>
    /// Loads XAML into an existing component instance.
    /// </summary>
    public static void LoadComponent(object component, Uri resourceLocator)
    {
        ArgumentNullException.ThrowIfNull(component);
        ValidateComponentUri(resourceLocator);

        var loader = ComponentLoader;
        if (loader == null)
        {
            throw new InvalidOperationException(
                "No XAML component loader is registered. Reference Jalium.UI.Xaml before calling LoadComponent.");
        }

        loader(component, resourceLocator);
    }

    /// <summary>
    /// Loads and returns the root object declared by a XAML resource.
    /// </summary>
    public static object LoadComponent(Uri resourceLocator)
    {
        ValidateComponentUri(resourceLocator);

        var loaded = ComponentObjectLoader?.Invoke(resourceLocator);
        if (loaded == null && Current != null && StartupObjectLoader != null)
        {
            loaded = StartupObjectLoader(Current, resourceLocator);
        }

        return loaded ?? throw new InvalidOperationException(
            $"No XAML component could be loaded from '{resourceLocator}'.");
    }

    /// <summary>
    /// Returns the cookies associated with an absolute URI.
    /// </summary>
    public static string GetCookie(Uri uri)
    {
        ValidateCookieUri(uri);
        return s_cookieContainer.GetCookieHeader(uri);
    }

    /// <summary>
    /// Stores one or more Set-Cookie values for an absolute URI.
    /// </summary>
    public static void SetCookie(Uri uri, string value)
    {
        ValidateCookieUri(uri);
        ArgumentNullException.ThrowIfNull(value);
        s_cookieContainer.SetCookies(uri, value);
    }

    /// <summary>Raises <see cref="FragmentNavigation"/>.</summary>
    protected virtual void OnFragmentNavigation(FragmentNavigationEventArgs e)
        => FragmentNavigation?.Invoke(this, e);

    /// <summary>Raises <see cref="LoadCompleted"/>.</summary>
    protected virtual void OnLoadCompleted(NavigationEventArgs e)
        => LoadCompleted?.Invoke(this, e);

    /// <summary>Raises <see cref="Navigated"/>.</summary>
    protected virtual void OnNavigated(NavigationEventArgs e)
        => Navigated?.Invoke(this, e);

    /// <summary>Raises <see cref="Navigating"/>.</summary>
    protected virtual void OnNavigating(NavigatingCancelEventArgs e)
        => Navigating?.Invoke(this, e);

    /// <summary>Raises <see cref="NavigationFailed"/>.</summary>
    protected virtual void OnNavigationFailed(NavigationFailedEventArgs e)
        => NavigationFailed?.Invoke(this, e);

    /// <summary>Raises <see cref="NavigationProgress"/>.</summary>
    protected virtual void OnNavigationProgress(NavigationProgressEventArgs e)
        => NavigationProgress?.Invoke(this, e);

    /// <summary>Raises <see cref="NavigationStopped"/>.</summary>
    protected virtual void OnNavigationStopped(NavigationEventArgs e)
        => NavigationStopped?.Invoke(this, e);

    internal void RaiseFragmentNavigation(FragmentNavigationEventArgs e) => OnFragmentNavigation(e);
    internal void RaiseLoadCompleted(NavigationEventArgs e) => OnLoadCompleted(e);
    internal void RaiseNavigated(NavigationEventArgs e) => OnNavigated(e);
    internal void RaiseNavigating(NavigatingCancelEventArgs e) => OnNavigating(e);
    internal void RaiseNavigationFailed(NavigationFailedEventArgs e) => OnNavigationFailed(e);
    internal void RaiseNavigationProgress(NavigationProgressEventArgs e) => OnNavigationProgress(e);
    internal void RaiseNavigationStopped(NavigationEventArgs e) => OnNavigationStopped(e);

    private static HttpClient CreateRemoteClient()
    {
        var handler = new HttpClientHandler
        {
            CookieContainer = s_cookieContainer,
            UseCookies = true
        };
        return new HttpClient(handler, disposeHandler: true);
    }

    private static void ValidateComponentUri(Uri resourceLocator)
    {
        ArgumentNullException.ThrowIfNull(resourceLocator);
        if (resourceLocator.IsAbsoluteUri)
        {
            throw new ArgumentException("Component resource locators must be relative.", nameof(resourceLocator));
        }
    }

    private static void ValidateCookieUri(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri)
        {
            throw new ArgumentException("Cookie URIs must be absolute.", nameof(uri));
        }
    }

    private static bool TryResolveResourceUri(Uri uri, out Assembly assembly, out string resourcePath)
    {
        var text = uri.OriginalString;
        if (uri.IsAbsoluteUri)
        {
            text = ExtractPackPath(text);
        }

        text = GetPathWithoutQueryOrFragment(text).TrimStart('/');
        var separator = text.IndexOf(ComponentSeparator, StringComparison.OrdinalIgnoreCase);
        if (separator >= 0)
        {
            var assemblyName = text[..separator].TrimStart('/');
            resourcePath = text[(separator + ComponentSeparator.Length)..];
            assembly = ResolveAssembly(assemblyName) ?? ResourceAssembly;
            return !string.IsNullOrWhiteSpace(resourcePath) &&
                   string.Equals(assembly.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase);
        }

        assembly = ResourceAssembly;
        resourcePath = text;
        return !string.IsNullOrWhiteSpace(resourcePath);
    }

    private static Assembly? ResolveAssembly(string assemblyName)
    {
        if (string.Equals(ResourceAssembly.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase))
        {
            return ResourceAssembly;
        }

        var loaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(candidate =>
            string.Equals(candidate.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase));
        if (loaded != null)
        {
            return loaded;
        }

        try
        {
            return Assembly.Load(new AssemblyName(assemblyName));
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (FileLoadException)
        {
            return null;
        }
    }

    private static Stream? TryOpenManifestResource(Assembly assembly, string resourcePath, out string? resolvedName)
    {
        var normalized = resourcePath.Replace('\\', '/').TrimStart('/');
        var dotted = normalized.Replace('/', '.');
        var assemblyName = assembly.GetName().Name ?? string.Empty;
        var paths = new List<string> { normalized };
        if (normalized.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
        {
            paths.Add(normalized[..^".xaml".Length] + ".jalxaml");
        }

        var manifestNames = assembly.GetManifestResourceNames();
        foreach (var path in paths)
        {
            var pathDotted = path.Replace('/', '.');
            foreach (var candidate in new[] { path, pathDotted, $"{assemblyName}.{path}", $"{assemblyName}.{pathDotted}" })
            {
                var stream = assembly.GetManifestResourceStream(candidate);
                if (stream != null)
                {
                    resolvedName = candidate;
                    return stream;
                }

                var actual = manifestNames.FirstOrDefault(name =>
                    string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase));
                if (actual != null)
                {
                    stream = assembly.GetManifestResourceStream(actual);
                    if (stream != null)
                    {
                        resolvedName = actual;
                        return stream;
                    }
                }
            }

            var suffix = $".{pathDotted}";
            var suffixMatch = manifestNames.FirstOrDefault(name =>
                name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
            if (suffixMatch != null)
            {
                var stream = assembly.GetManifestResourceStream(suffixMatch);
                if (stream != null)
                {
                    resolvedName = suffixMatch;
                    return stream;
                }
            }
        }

        resolvedName = null;
        return null;
    }

    private static IEnumerable<string> EnumerateContentBaseDirectories()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? resourceDirectory = null;
        try
        {
#pragma warning disable IL3000 // Empty in single-file apps; AppContext.BaseDirectory below remains the authoritative fallback.
            resourceDirectory = Path.GetDirectoryName(ResourceAssembly.Location);
#pragma warning restore IL3000
        }
        catch (NotSupportedException)
        {
            // Dynamic assemblies do not expose a physical location.
        }

        foreach (var directory in new[]
        {
            AppContext.BaseDirectory,
            resourceDirectory,
            Environment.CurrentDirectory
        })
        {
            if (!string.IsNullOrWhiteSpace(directory) && seen.Add(directory))
            {
                yield return directory;
            }
        }
    }

    private static string? TryResolveLooseFile(string baseDirectory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        var fullBase = Path.GetFullPath(baseDirectory);
        var candidate = Path.GetFullPath(Path.Combine(
            fullBase,
            Uri.UnescapeDataString(relativePath).Replace('/', Path.DirectorySeparatorChar)));
        var relative = Path.GetRelativePath(fullBase, candidate);
        return relative != ".." &&
               !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            ? candidate
            : null;
    }

    private static string GetPathWithoutQueryOrFragment(string uriText)
    {
        var end = uriText.Length;
        var query = uriText.IndexOf('?');
        if (query >= 0)
        {
            end = Math.Min(end, query);
        }

        var fragment = uriText.IndexOf('#');
        if (fragment >= 0)
        {
            end = Math.Min(end, fragment);
        }

        return uriText[..end];
    }

    private static bool IsApplicationPackUri(Uri uri)
        => uri.Scheme.Equals("pack", StringComparison.OrdinalIgnoreCase) &&
           uri.OriginalString.StartsWith("pack://application:,,,/", StringComparison.OrdinalIgnoreCase);

    private static bool IsSiteOfOriginPackUri(Uri uri)
        => uri.Scheme.Equals("pack", StringComparison.OrdinalIgnoreCase) &&
           uri.OriginalString.StartsWith("pack://siteoforigin:,,,/", StringComparison.OrdinalIgnoreCase);

    private static string ExtractPackPath(string text)
    {
        var marker = text.IndexOf(",,,/", StringComparison.Ordinal);
        return marker >= 0 ? text[(marker + 4)..] : text.TrimStart('/');
    }

    private static string GetContentType(string resourceName)
        => Path.GetExtension(GetPathWithoutQueryOrFragment(resourceName)).ToLowerInvariant() switch
        {
            ".bmp" => "image/bmp",
            ".css" => "text/css",
            ".gif" => "image/gif",
            ".htm" or ".html" => "text/html",
            ".ico" => "image/x-icon",
            ".jpeg" or ".jpg" => "image/jpeg",
            ".jalxaml" or ".xaml" => "application/xaml+xml",
            ".json" => "application/json",
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".svg" => "image/svg+xml",
            ".txt" => "text/plain",
            ".xml" => "application/xml",
            _ => "application/octet-stream"
        };
}

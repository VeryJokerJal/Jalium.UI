using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Jalium.Extensions.Primitives;

namespace Jalium.Extensions.FileProviders;

/// <summary><see cref="IFileProvider"/> that always reports "not found".</summary>
public sealed class NullFileProvider : IFileProvider
{
    public IFileInfo GetFileInfo(string subpath) => new NotFoundFileInfo(subpath);
    public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;
    public IChangeToken Watch(string filter) => NullChangeToken.Singleton;
}

/// <summary>Composes multiple <see cref="IFileProvider"/> — first match wins.</summary>
public sealed class CompositeFileProvider : IFileProvider
{
    private readonly IFileProvider[] _providers;

    public CompositeFileProvider(params IFileProvider[] fileProviders)
    {
        ArgumentNullException.ThrowIfNull(fileProviders);
        _providers = fileProviders;
    }

    public CompositeFileProvider(IEnumerable<IFileProvider> fileProviders)
    {
        ArgumentNullException.ThrowIfNull(fileProviders);
        _providers = fileProviders.ToArray();
    }

    public IEnumerable<IFileProvider> FileProviders => _providers;

    public IFileInfo GetFileInfo(string subpath)
    {
        foreach (var p in _providers)
        {
            var info = p.GetFileInfo(subpath);
            if (info != null && info.Exists) return info;
        }
        return new NotFoundFileInfo(subpath);
    }

    public IDirectoryContents GetDirectoryContents(string subpath)
    {
        var directories = new List<IDirectoryContents>();
        foreach (var p in _providers)
        {
            var dir = p.GetDirectoryContents(subpath);
            if (dir != null && dir.Exists) directories.Add(dir);
        }
        return directories.Count == 0 ? NotFoundDirectoryContents.Singleton : new CompositeDirectoryContents(directories);
    }

    public IChangeToken Watch(string filter)
    {
        var tokens = new List<IChangeToken>();
        foreach (var p in _providers)
        {
            var t = p.Watch(filter);
            if (t != null && !ReferenceEquals(t, NullChangeToken.Singleton)) tokens.Add(t);
        }
        return tokens.Count == 0 ? NullChangeToken.Singleton : new CompositeChangeToken(tokens);
    }

    private sealed class CompositeDirectoryContents : IDirectoryContents
    {
        private readonly List<IDirectoryContents> _inner;
        public CompositeDirectoryContents(List<IDirectoryContents> inner) { _inner = inner; }
        public bool Exists => _inner.Count > 0;
        public IEnumerator<IFileInfo> GetEnumerator()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dir in _inner)
                foreach (var f in dir)
                    if (seen.Add(f.Name)) yield return f;
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

/// <summary>Union <see cref="IChangeToken"/> — fires when any underlying token fires.</summary>
public sealed class CompositeChangeToken : IChangeToken
{
    private readonly IReadOnlyList<IChangeToken> _tokens;
    private readonly bool _hasActive;
    public CompositeChangeToken(IReadOnlyList<IChangeToken> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        _tokens = tokens;
        for (int i = 0; i < tokens.Count; i++) if (tokens[i].ActiveChangeCallbacks) { _hasActive = true; break; }
    }
    public bool HasChanged
    {
        get { for (int i = 0; i < _tokens.Count; i++) if (_tokens[i].HasChanged) return true; return false; }
    }
    public bool ActiveChangeCallbacks => _hasActive;
    public IDisposable RegisterChangeCallback(Action<object?> callback, object? state)
    {
        var regs = new List<IDisposable>(_tokens.Count);
        for (int i = 0; i < _tokens.Count; i++)
        {
            if (_tokens[i].ActiveChangeCallbacks)
                regs.Add(_tokens[i].RegisterChangeCallback(callback, state));
        }
        return new CompositeDisposable(regs);
    }
    private sealed class CompositeDisposable : IDisposable
    {
        private readonly List<IDisposable> _items;
        public CompositeDisposable(List<IDisposable> items) { _items = items; }
        public void Dispose() { foreach (var d in _items) { try { d.Dispose(); } catch { } } }
    }
}

/// <summary>
/// Reads files from an <see cref="Assembly"/>'s embedded resources. Each resource manifest name is mapped
/// to a subpath using <see cref="ResourceName.Sanitize"/> (dots → slashes, except final ext).
/// </summary>
public sealed class EmbeddedFileProvider : IFileProvider
{
    private readonly Assembly _assembly;
    private readonly string _baseNamespace;
    private readonly DateTimeOffset _lastModified;
    private readonly EmbeddedFile[] _files;

    public EmbeddedFileProvider(Assembly assembly) : this(assembly, assembly?.GetName().Name ?? string.Empty) { }

    public EmbeddedFileProvider(Assembly assembly, string baseNamespace)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        _assembly = assembly;
        _baseNamespace = string.IsNullOrEmpty(baseNamespace) ? string.Empty : baseNamespace + ".";
        _lastModified = TryGetAssemblyTimestamp(assembly);
        var resources = assembly.GetManifestResourceNames();
        _files = new EmbeddedFile[resources.Length];
        for (int i = 0; i < resources.Length; i++)
        {
            var name = resources[i];
            var subpath = ResourceName.ToSubpath(name, _baseNamespace);
            _files[i] = new EmbeddedFile(assembly, name, subpath, _lastModified);
        }
    }

    private static DateTimeOffset TryGetAssemblyTimestamp(Assembly asm)
    {
        try
        {
            var loc = asm.Location;
            if (!string.IsNullOrEmpty(loc) && File.Exists(loc)) return File.GetLastWriteTimeUtc(loc);
        }
        catch { }
        return DateTimeOffset.UtcNow;
    }

    public IFileInfo GetFileInfo(string subpath)
    {
        if (string.IsNullOrEmpty(subpath)) return new NotFoundFileInfo(string.Empty);
        var normalized = subpath.Replace('\\', '/').TrimStart('/');
        for (int i = 0; i < _files.Length; i++)
            if (string.Equals(_files[i].Name, normalized, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(_files[i].Subpath, normalized, StringComparison.OrdinalIgnoreCase))
                return _files[i];
        return new NotFoundFileInfo(subpath);
    }

    public IDirectoryContents GetDirectoryContents(string subpath)
    {
        var prefix = string.IsNullOrEmpty(subpath) ? string.Empty : subpath.Replace('\\', '/').TrimStart('/').TrimEnd('/') + "/";
        var entries = new List<IFileInfo>();
        foreach (var f in _files)
        {
            if (prefix.Length == 0 || f.Subpath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                entries.Add(f);
        }
        return entries.Count == 0 ? NotFoundDirectoryContents.Singleton : new EmbeddedDirectoryContents(entries);
    }

    public IChangeToken Watch(string filter) => NullChangeToken.Singleton; // Embedded resources are immutable.

    private sealed class EmbeddedDirectoryContents : IDirectoryContents
    {
        private readonly List<IFileInfo> _entries;
        public EmbeddedDirectoryContents(List<IFileInfo> entries) { _entries = entries; }
        public bool Exists => true;
        public IEnumerator<IFileInfo> GetEnumerator() => _entries.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _entries.GetEnumerator();
    }
}

internal sealed class EmbeddedFile : IFileInfo
{
    private readonly Assembly _assembly;
    public string ResourceName { get; }
    public string Subpath { get; }
    public string Name => Path.GetFileName(Subpath);
    public bool Exists => true;
    public bool IsDirectory => false;
    public long Length
    {
        get { using var s = _assembly.GetManifestResourceStream(ResourceName); return s?.Length ?? 0; }
    }
    public string? PhysicalPath => null;
    public DateTimeOffset LastModified { get; }

    public EmbeddedFile(Assembly assembly, string resourceName, string subpath, DateTimeOffset lastModified)
    {
        _assembly = assembly;
        ResourceName = resourceName;
        Subpath = subpath;
        LastModified = lastModified;
    }

    public Stream CreateReadStream()
    {
        var s = _assembly.GetManifestResourceStream(ResourceName)
            ?? throw new FileNotFoundException(ResourceName);
        return s;
    }
}

internal static class ResourceName
{
    /// <summary>
    /// Convert a manifest resource name like <c>"MyAsm.Folder.Sub.File.ext"</c> into a relative subpath
    /// <c>"Folder/Sub/File.ext"</c>. The last dot is treated as the file extension separator.
    /// </summary>
    public static string ToSubpath(string resourceName, string baseNamespace)
    {
        if (!string.IsNullOrEmpty(baseNamespace) && resourceName.StartsWith(baseNamespace, StringComparison.OrdinalIgnoreCase))
            resourceName = resourceName.Substring(baseNamespace.Length);

        var lastDot = resourceName.LastIndexOf('.');
        if (lastDot <= 0) return resourceName;
        var name = resourceName.Substring(0, lastDot).Replace('.', '/');
        var ext = resourceName.Substring(lastDot);
        return name + ext;
    }
}

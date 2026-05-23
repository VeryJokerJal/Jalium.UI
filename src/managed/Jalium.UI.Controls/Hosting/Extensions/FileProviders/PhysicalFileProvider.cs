using Jalium.Extensions.Primitives;

namespace Jalium.Extensions.FileProviders;

/// <summary>
/// <see cref="IFileProvider"/> rooted at a physical directory. Supports per-file
/// change notifications via <see cref="FileSystemWatcher"/>.
/// </summary>
public sealed class PhysicalFileProvider : IFileProvider, IDisposable
{
    private readonly string _root;
    private readonly Dictionary<string, PhysicalFileChangeToken> _activeTokens = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _watcher;
    private readonly object _watcherLock = new();
    private bool _disposed;

    public PhysicalFileProvider(string root)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        _root = Path.GetFullPath(root);
    }

    public string Root => _root;

    public IFileInfo GetFileInfo(string subpath)
    {
        if (string.IsNullOrEmpty(subpath)) return new NotFoundFileInfo(string.Empty);
        var fullPath = ResolvePath(subpath);
        if (fullPath == null) return new NotFoundFileInfo(subpath);
        var info = new FileInfo(fullPath);
        if (!info.Exists) return new NotFoundFileInfo(subpath);
        return new PhysicalFileInfo(info);
    }

    public IDirectoryContents GetDirectoryContents(string subpath)
    {
        var fullPath = subpath.Length == 0 ? _root : ResolvePath(subpath);
        if (fullPath == null || !Directory.Exists(fullPath)) return NotFoundDirectoryContents.Singleton;
        return new PhysicalDirectoryContents(fullPath);
    }

    public IChangeToken Watch(string filter)
    {
        if (string.IsNullOrEmpty(filter)) return NullChangeToken.Singleton;
        EnsureWatcher();
        lock (_activeTokens)
        {
            if (!_activeTokens.TryGetValue(filter, out var token) || token.HasChanged)
            {
                token = new PhysicalFileChangeToken();
                _activeTokens[filter] = token;
            }
            return token;
        }
    }

    private void EnsureWatcher()
    {
        if (_watcher != null) return;
        lock (_watcherLock)
        {
            if (_watcher != null) return;
            try
            {
                var w = new FileSystemWatcher(_root)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.DirectoryName,
                };
                w.Changed += OnChanged;
                w.Created += OnChanged;
                w.Deleted += OnChanged;
                w.Renamed += (s, e) => OnChanged(s, e);
                w.EnableRaisingEvents = true;
                _watcher = w;
            }
            catch
            {
                // Some environments (e.g. trimmed containers) lack inotify; reload-on-change becomes a no-op.
            }
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        PhysicalFileChangeToken[] snapshot;
        lock (_activeTokens)
        {
            // For simplicity fire every token (MS PhysicalFileProvider does precise matching;
            // we follow the safer over-fire strategy — consumers reload, harmless if false-positive).
            snapshot = _activeTokens.Values.ToArray();
            _activeTokens.Clear();
        }
        foreach (var t in snapshot) t.Fire();
    }

    private string? ResolvePath(string subpath)
    {
        if (subpath.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return null;
        var full = Path.GetFullPath(Path.Combine(_root, subpath));
        if (!full.StartsWith(_root, StringComparison.OrdinalIgnoreCase)) return null;
        return full;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _watcher?.Dispose();
        _watcher = null;
        lock (_activeTokens) _activeTokens.Clear();
    }

    private sealed class PhysicalFileInfo : IFileInfo
    {
        private readonly FileInfo _info;
        public PhysicalFileInfo(FileInfo info) { _info = info; }
        public bool Exists => _info.Exists;
        public long Length => _info.Length;
        public string? PhysicalPath => _info.FullName;
        public string Name => _info.Name;
        public DateTimeOffset LastModified => _info.LastWriteTimeUtc;
        public bool IsDirectory => false;
        public Stream CreateReadStream() => new FileStream(_info.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    }

    private sealed class PhysicalDirectoryContents : IDirectoryContents
    {
        private readonly string _path;
        public PhysicalDirectoryContents(string path) { _path = path; }
        public bool Exists => Directory.Exists(_path);
        public IEnumerator<IFileInfo> GetEnumerator()
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(_path))
            {
                if (Directory.Exists(entry)) yield return new PhysicalDirectoryInfo(new DirectoryInfo(entry));
                else yield return new PhysicalFileInfo(new FileInfo(entry));
            }
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class PhysicalDirectoryInfo : IFileInfo
    {
        private readonly DirectoryInfo _info;
        public PhysicalDirectoryInfo(DirectoryInfo info) { _info = info; }
        public bool Exists => _info.Exists;
        public long Length => -1;
        public string? PhysicalPath => _info.FullName;
        public string Name => _info.Name;
        public DateTimeOffset LastModified => _info.LastWriteTimeUtc;
        public bool IsDirectory => true;
        public Stream CreateReadStream() => throw new InvalidOperationException("Cannot read a directory.");
    }

    private sealed class PhysicalFileChangeToken : IChangeToken
    {
        private readonly CancellationTokenSource _cts = new();
        public bool HasChanged => _cts.IsCancellationRequested;
        public bool ActiveChangeCallbacks => true;
        public IDisposable RegisterChangeCallback(Action<object?> callback, object? state) => _cts.Token.Register(callback, state);
        public void Fire() { try { _cts.Cancel(); } catch { } }
    }
}

using Jalium.Extensions.Primitives;

namespace Jalium.Extensions.FileProviders;

/// <summary>Abstraction over filesystem-like resources used by configuration / asset loaders.</summary>
public interface IFileProvider
{
    IFileInfo GetFileInfo(string subpath);
    IDirectoryContents GetDirectoryContents(string subpath);
    IChangeToken Watch(string filter);
}

public interface IFileInfo
{
    bool Exists { get; }
    long Length { get; }
    string? PhysicalPath { get; }
    string Name { get; }
    DateTimeOffset LastModified { get; }
    bool IsDirectory { get; }
    Stream CreateReadStream();
}

public interface IDirectoryContents : IEnumerable<IFileInfo>
{
    bool Exists { get; }
}

public sealed class NullChangeToken : IChangeToken
{
    public static NullChangeToken Singleton { get; } = new();
    public bool HasChanged => false;
    public bool ActiveChangeCallbacks => false;
    public IDisposable RegisterChangeCallback(Action<object?> callback, object? state) => EmptyDisposable.Instance;
    private sealed class EmptyDisposable : IDisposable { public static readonly EmptyDisposable Instance = new(); public void Dispose() { } }
}

public sealed class NotFoundFileInfo : IFileInfo
{
    public NotFoundFileInfo(string name) { Name = name; }
    public bool Exists => false;
    public long Length => -1;
    public string? PhysicalPath => null;
    public string Name { get; }
    public DateTimeOffset LastModified => default;
    public bool IsDirectory => false;
    public Stream CreateReadStream() => throw new FileNotFoundException(Name);
}

public sealed class NotFoundDirectoryContents : IDirectoryContents
{
    public static NotFoundDirectoryContents Singleton { get; } = new();
    public bool Exists => false;
    public IEnumerator<IFileInfo> GetEnumerator() => Array.Empty<IFileInfo>().AsEnumerable().GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

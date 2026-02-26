namespace Jalium.UI.Gpu.Resources;

/// <summary>
/// 纹理管理器 - 管理纹理生命周期、渲染目标、SRV 创建
/// </summary>
public sealed class TextureManager : IDisposable
{
    private readonly IRenderBackendEx _backend;
    private readonly DescriptorHeapManager _descriptors;
    private readonly Dictionary<uint, TextureEntry> _textures = new();
    private uint _nextId;
    private bool _disposed;

    public TextureManager(IRenderBackendEx backend, DescriptorHeapManager descriptors)
    {
        _backend = backend;
        _descriptors = descriptors;
    }

    /// <summary>
    /// 加载纹理文件
    /// </summary>
    public TextureHandle LoadTexture(string path, TextureFormat format)
    {
        var id = _nextId++;
        var handle = _backend.LoadTexture(path, format);
        var srv = _descriptors.AllocateSrv(handle);

        _textures[id] = new TextureEntry
        {
            NativeHandle = handle,
            Srv = srv,
            Width = 0, // 实际尺寸从 native 获取
            Height = 0,
            Format = format,
            Usage = TextureUsage.ShaderResource,
            IsRenderTarget = false
        };

        return new TextureHandle(id);
    }

    /// <summary>
    /// 创建渲染目标纹理
    /// </summary>
    public TextureHandle CreateRenderTarget(int width, int height, TextureFormat format)
    {
        var id = _nextId++;
        var usage = TextureUsage.RenderTarget | TextureUsage.ShaderResource;
        var handle = _backend.CreateTexture2D(width, height, format, usage);
        var srv = _descriptors.AllocateSrv(handle);

        _textures[id] = new TextureEntry
        {
            NativeHandle = handle,
            Srv = srv,
            Width = width,
            Height = height,
            Format = format,
            Usage = usage,
            IsRenderTarget = true
        };

        return new TextureHandle(id);
    }

    /// <summary>
    /// 创建可读写纹理（用于 compute shader）
    /// </summary>
    public TextureHandle CreateReadWrite(int width, int height, TextureFormat format)
    {
        var id = _nextId++;
        var usage = TextureUsage.UnorderedAccess | TextureUsage.ShaderResource;
        var handle = _backend.CreateTexture2D(width, height, format, usage);
        var srv = _descriptors.AllocateSrv(handle);
        var uav = _descriptors.AllocateUav(handle);

        _textures[id] = new TextureEntry
        {
            NativeHandle = handle,
            Srv = srv,
            Uav = uav,
            Width = width,
            Height = height,
            Format = format,
            Usage = usage,
            IsRenderTarget = false
        };

        return new TextureHandle(id);
    }

    /// <summary>
    /// 创建字形图集纹理
    /// </summary>
    public TextureHandle CreateGlyphAtlas(string fontId, float fontSize, int width, int height)
    {
        var id = _nextId++;
        var handle = _backend.CreateGlyphAtlas(fontId, fontSize, width, height);
        var srv = _descriptors.AllocateSrv(handle);

        _textures[id] = new TextureEntry
        {
            NativeHandle = handle,
            Srv = srv,
            Width = width,
            Height = height,
            Format = TextureFormat.R8,
            Usage = TextureUsage.ShaderResource,
            IsRenderTarget = false
        };

        return new TextureHandle(id);
    }

    /// <summary>
    /// 获取纹理的 SRV
    /// </summary>
    public DescriptorHandle GetSrv(TextureHandle handle)
    {
        return _textures.TryGetValue(handle.Id, out var entry)
            ? entry.Srv
            : DescriptorHandle.Invalid;
    }

    /// <summary>
    /// 获取纹理的 UAV
    /// </summary>
    public DescriptorHandle GetUav(TextureHandle handle)
    {
        return _textures.TryGetValue(handle.Id, out var entry) && entry.Uav.IsValid
            ? entry.Uav
            : DescriptorHandle.Invalid;
    }

    /// <summary>
    /// 获取纹理的 native handle
    /// </summary>
    public nint GetNativeHandle(TextureHandle handle)
    {
        return _textures.TryGetValue(handle.Id, out var entry)
            ? entry.NativeHandle
            : nint.Zero;
    }

    /// <summary>
    /// 获取纹理尺寸
    /// </summary>
    public (int Width, int Height) GetSize(TextureHandle handle)
    {
        return _textures.TryGetValue(handle.Id, out var entry)
            ? (entry.Width, entry.Height)
            : (0, 0);
    }

    /// <summary>
    /// 调整渲染目标大小（窗口 resize 时）
    /// </summary>
    public void ResizeRenderTarget(TextureHandle handle, int newWidth, int newHeight)
    {
        if (!_textures.TryGetValue(handle.Id, out var entry) || !entry.IsRenderTarget)
            return;

        // 释放旧资源
        _descriptors.Free(entry.Srv);
        if (entry.Uav.IsValid) _descriptors.Free(entry.Uav);
        _backend.DestroyTexture(entry.NativeHandle);

        // 创建新资源
        var newHandle = _backend.CreateTexture2D(newWidth, newHeight, entry.Format, entry.Usage);
        var newSrv = _descriptors.AllocateSrv(newHandle);

        entry.NativeHandle = newHandle;
        entry.Srv = newSrv;
        entry.Width = newWidth;
        entry.Height = newHeight;

        _textures[handle.Id] = entry;
    }

    /// <summary>
    /// 销毁纹理
    /// </summary>
    public void Destroy(TextureHandle handle)
    {
        if (!_textures.Remove(handle.Id, out var entry))
            return;

        _descriptors.Free(entry.Srv);
        if (entry.Uav.IsValid) _descriptors.Free(entry.Uav);
        _backend.DestroyTexture(entry.NativeHandle);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var entry in _textures.Values)
        {
            _descriptors.Free(entry.Srv);
            if (entry.Uav.IsValid) _descriptors.Free(entry.Uav);
            _backend.DestroyTexture(entry.NativeHandle);
        }
        _textures.Clear();
    }
}

/// <summary>
/// 纹理句柄（轻量级标识符）
/// </summary>
public readonly struct TextureHandle : IEquatable<TextureHandle>
{
    public readonly uint Id;

    public TextureHandle(uint id) => Id = id;

    public static TextureHandle Invalid => new(uint.MaxValue);
    public bool IsValid => Id != uint.MaxValue;

    public bool Equals(TextureHandle other) => Id == other.Id;
    public override bool Equals(object? obj) => obj is TextureHandle other && Equals(other);
    public override int GetHashCode() => (int)Id;
}

/// <summary>
/// 纹理条目（内部追踪）
/// </summary>
internal struct TextureEntry
{
    public nint NativeHandle;
    public DescriptorHandle Srv;
    public DescriptorHandle Uav;
    public int Width;
    public int Height;
    public TextureFormat Format;
    public TextureUsage Usage;
    public bool IsRenderTarget;
}

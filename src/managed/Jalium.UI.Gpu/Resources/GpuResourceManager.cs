using System.Runtime.InteropServices;

namespace Jalium.UI.Gpu.Resources;

/// <summary>
/// GPU 资源管理器 - 帧环形缓冲区、上传堆、生命周期管理
/// </summary>
public sealed class GpuResourceManager : IDisposable
{
    /// <summary>
    /// 双缓冲帧数
    /// </summary>
    public const int FrameCount = 2;

    /// <summary>
    /// 默认上传堆大小 (4MB)
    /// </summary>
    public const int DefaultUploadHeapSize = 4 * 1024 * 1024;

    private readonly IRenderBackendEx _backend;
    private readonly FrameResources[] _frames;
    private int _currentFrameIndex;
    private ulong _fenceValue;
    private bool _disposed;

    // 静态资源追踪
    private readonly List<nint> _staticBuffers = new();
    private readonly List<nint> _staticTextures = new();

    public GpuResourceManager(IRenderBackendEx backend, int uploadHeapSize = DefaultUploadHeapSize)
    {
        _backend = backend;

        _frames = new FrameResources[FrameCount];
        for (int i = 0; i < FrameCount; i++)
        {
            _frames[i] = new FrameResources(backend, uploadHeapSize);
        }
    }

    /// <summary>
    /// 当前帧索引
    /// </summary>
    public int CurrentFrameIndex => _currentFrameIndex;

    /// <summary>
    /// 当前帧资源
    /// </summary>
    public FrameResources CurrentFrame => _frames[_currentFrameIndex];

    /// <summary>
    /// 开始新帧 - 轮转帧索引，等待并回收旧帧资源
    /// </summary>
    public void BeginFrame()
    {
        // 切换到下一帧
        _currentFrameIndex = (_currentFrameIndex + 1) % FrameCount;
        var frame = _frames[_currentFrameIndex];

        // 等待此帧的 GPU 工作完成
        if (frame.FenceValue > 0)
        {
            _backend.WaitForFence(frame.FenceValue);
        }

        // 重置帧资源
        frame.Reset();
    }

    /// <summary>
    /// 结束帧 - 发出 fence 信号
    /// </summary>
    public void EndFrame()
    {
        _fenceValue++;
        var frame = _frames[_currentFrameIndex];
        frame.FenceValue = _fenceValue;
        _backend.Signal();
    }

    /// <summary>
    /// 从当前帧的上传堆分配（用于每帧变化的数据：动画 uniform 等）
    /// </summary>
    public GpuAllocation AllocateUpload(int size, int alignment = 256)
    {
        return CurrentFrame.Allocate(size, alignment);
    }

    /// <summary>
    /// 创建静态缓冲区（不随帧变化的数据：顶点、索引等）
    /// </summary>
    public nint CreateStaticBuffer(ReadOnlySpan<byte> data, BufferUsage usage)
    {
        var buffer = _backend.CreateBuffer(data.Length, usage);

        // 通过上传堆传输数据
        _backend.UpdateBuffer(buffer, 0, data);

        _staticBuffers.Add(buffer);
        return buffer;
    }

    /// <summary>
    /// 创建静态纹理
    /// </summary>
    public nint CreateStaticTexture(int width, int height, TextureFormat format, TextureUsage usage)
    {
        var texture = _backend.CreateTexture2D(width, height, format, usage);
        _staticTextures.Add(texture);
        return texture;
    }

    /// <summary>
    /// 释放指定的静态缓冲区
    /// </summary>
    public void DestroyStaticBuffer(nint buffer)
    {
        _staticBuffers.Remove(buffer);
        _backend.DestroyBuffer(buffer);
    }

    /// <summary>
    /// 释放指定的静态纹理
    /// </summary>
    public void DestroyStaticTexture(nint texture)
    {
        _staticTextures.Remove(texture);
        _backend.DestroyTexture(texture);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // 等待所有 GPU 工作完成
        if (_fenceValue > 0)
        {
            _backend.WaitForFence(_fenceValue);
        }

        // 释放帧资源
        foreach (var frame in _frames)
        {
            frame.Dispose();
        }

        // 释放静态资源
        foreach (var buffer in _staticBuffers)
        {
            _backend.DestroyBuffer(buffer);
        }
        _staticBuffers.Clear();

        foreach (var texture in _staticTextures)
        {
            _backend.DestroyTexture(texture);
        }
        _staticTextures.Clear();
    }
}

/// <summary>
/// 帧资源 - 每帧独立的上传堆和临时资源
/// </summary>
public sealed class FrameResources : IDisposable
{
    private readonly IRenderBackendEx _backend;
    private readonly nint _uploadBuffer;
    private readonly int _uploadBufferSize;
    private int _uploadOffset;
    private nint _mappedPtr;

    // 临时资源（帧结束时回收）
    private readonly List<nint> _tempBuffers = new();
    private readonly List<nint> _tempTextures = new();

    /// <summary>
    /// 此帧的 fence 值
    /// </summary>
    public ulong FenceValue { get; set; }

    public FrameResources(IRenderBackendEx backend, int uploadBufferSize)
    {
        _backend = backend;
        _uploadBufferSize = uploadBufferSize;

        // 创建上传缓冲区
        _uploadBuffer = backend.CreateBuffer(uploadBufferSize, BufferUsage.Upload);
    }

    /// <summary>
    /// 从上传堆分配
    /// </summary>
    public GpuAllocation Allocate(int size, int alignment = 256)
    {
        // 对齐偏移量
        var alignedOffset = (_uploadOffset + alignment - 1) & ~(alignment - 1);

        if (alignedOffset + size > _uploadBufferSize)
        {
            throw new OutOfMemoryException(
                $"Upload heap exhausted. Requested {size} bytes at offset {alignedOffset}, heap size is {_uploadBufferSize}");
        }

        var allocation = new GpuAllocation(
            gpuAddress: _uploadBuffer,
            cpuAddress: _mappedPtr + alignedOffset,
            offset: alignedOffset,
            size: size);

        _uploadOffset = alignedOffset + size;
        return allocation;
    }

    /// <summary>
    /// 创建临时缓冲区（帧结束时自动回收）
    /// </summary>
    public nint CreateTempBuffer(int size, BufferUsage usage)
    {
        var buffer = _backend.CreateBuffer(size, usage);
        _tempBuffers.Add(buffer);
        return buffer;
    }

    /// <summary>
    /// 重置帧资源（复用上传堆，回收临时资源）
    /// </summary>
    public void Reset()
    {
        _uploadOffset = 0;

        foreach (var buffer in _tempBuffers)
        {
            _backend.DestroyBuffer(buffer);
        }
        _tempBuffers.Clear();

        foreach (var texture in _tempTextures)
        {
            _backend.DestroyTexture(texture);
        }
        _tempTextures.Clear();
    }

    public void Dispose()
    {
        Reset();
        _backend.DestroyBuffer(_uploadBuffer);
    }
}

/// <summary>
/// GPU 分配结果
/// </summary>
public readonly struct GpuAllocation
{
    /// <summary>
    /// GPU 缓冲区句柄
    /// </summary>
    public readonly nint GpuAddress;

    /// <summary>
    /// CPU 映射地址（用于直接写入）
    /// </summary>
    public readonly nint CpuAddress;

    /// <summary>
    /// 缓冲区内偏移
    /// </summary>
    public readonly int Offset;

    /// <summary>
    /// 分配大小
    /// </summary>
    public readonly int Size;

    public GpuAllocation(nint gpuAddress, nint cpuAddress, int offset, int size)
    {
        GpuAddress = gpuAddress;
        CpuAddress = cpuAddress;
        Offset = offset;
        Size = size;
    }

    /// <summary>
    /// 将数据写入此分配
    /// </summary>
    public unsafe void Write(ReadOnlySpan<byte> data)
    {
        if (CpuAddress == nint.Zero || data.Length > Size) return;

        fixed (byte* src = data)
        {
            Buffer.MemoryCopy(src, (void*)CpuAddress, Size, data.Length);
        }
    }
}

/// <summary>
/// 缓冲区用途
/// </summary>
public enum BufferUsage : byte
{
    Vertex,
    Index,
    Instance,
    Uniform,
    Storage,      // Structured Buffer (SSBO)
    Upload,       // CPU → GPU 传输
    Readback,     // GPU → CPU 回读
    Indirect      // 间接绘制参数
}

/// <summary>
/// 纹理用途
/// </summary>
[Flags]
public enum TextureUsage : byte
{
    ShaderResource = 1,
    RenderTarget = 2,
    UnorderedAccess = 4,
    DepthStencil = 8
}

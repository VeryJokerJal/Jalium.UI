namespace Jalium.UI.Gpu.Resources;

/// <summary>
/// 描述符堆管理器 - 管理 SRV/CBV/UAV/Sampler 描述符分配
/// </summary>
public sealed class DescriptorHeapManager : IDisposable
{
    private readonly IRenderBackendEx _backend;
    private readonly DescriptorPool _srvCbvUavPool;
    private readonly DescriptorPool _samplerPool;
    private bool _disposed;

    /// <summary>
    /// 默认 SRV/CBV/UAV 描述符数量
    /// </summary>
    public const int DefaultSrvCbvUavCount = 4096;

    /// <summary>
    /// 默认 Sampler 描述符数量
    /// </summary>
    public const int DefaultSamplerCount = 64;

    public DescriptorHeapManager(
        IRenderBackendEx backend,
        int srvCbvUavCount = DefaultSrvCbvUavCount,
        int samplerCount = DefaultSamplerCount)
    {
        _backend = backend;
        _srvCbvUavPool = new DescriptorPool(DescriptorType.SrvCbvUav, srvCbvUavCount);
        _samplerPool = new DescriptorPool(DescriptorType.Sampler, samplerCount);
    }

    /// <summary>
    /// 分配 Shader Resource View 描述符
    /// </summary>
    public DescriptorHandle AllocateSrv(nint resource)
    {
        var slot = _srvCbvUavPool.Allocate();
        var handle = new DescriptorHandle(slot, DescriptorType.SrvCbvUav);

        // 通过 backend 创建实际的 SRV
        _backend.CreateSrv(resource);

        return handle;
    }

    /// <summary>
    /// 分配 Constant Buffer View 描述符
    /// </summary>
    public DescriptorHandle AllocateCbv(nint buffer, int offset, int size)
    {
        var slot = _srvCbvUavPool.Allocate();
        var handle = new DescriptorHandle(slot, DescriptorType.SrvCbvUav);

        _backend.CreateCbv(buffer, offset, size);

        return handle;
    }

    /// <summary>
    /// 分配 Unordered Access View 描述符
    /// </summary>
    public DescriptorHandle AllocateUav(nint resource)
    {
        var slot = _srvCbvUavPool.Allocate();
        var handle = new DescriptorHandle(slot, DescriptorType.SrvCbvUav);

        _backend.CreateUav(resource);

        return handle;
    }

    /// <summary>
    /// 分配 Sampler 描述符
    /// </summary>
    public DescriptorHandle AllocateSampler(SamplerDesc desc)
    {
        var slot = _samplerPool.Allocate();
        return new DescriptorHandle(slot, DescriptorType.Sampler);
    }

    /// <summary>
    /// 释放描述符
    /// </summary>
    public void Free(DescriptorHandle handle)
    {
        if (handle.Type == DescriptorType.Sampler)
        {
            _samplerPool.Free(handle.HeapIndex);
        }
        else
        {
            _srvCbvUavPool.Free(handle.HeapIndex);
            _backend.FreeDescriptor(handle);
        }
    }

    /// <summary>
    /// 已分配的 SRV/CBV/UAV 数量
    /// </summary>
    public int AllocatedSrvCbvUavCount => _srvCbvUavPool.AllocatedCount;

    /// <summary>
    /// 已分配的 Sampler 数量
    /// </summary>
    public int AllocatedSamplerCount => _samplerPool.AllocatedCount;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _srvCbvUavPool.Dispose();
        _samplerPool.Dispose();
    }
}

/// <summary>
/// 描述符句柄
/// </summary>
public readonly struct DescriptorHandle : IEquatable<DescriptorHandle>
{
    /// <summary>
    /// 堆内索引
    /// </summary>
    public readonly uint HeapIndex;

    /// <summary>
    /// 描述符类型
    /// </summary>
    public readonly DescriptorType Type;

    /// <summary>
    /// 是否有效
    /// </summary>
    public bool IsValid => HeapIndex != uint.MaxValue;

    public DescriptorHandle(uint heapIndex, DescriptorType type)
    {
        HeapIndex = heapIndex;
        Type = type;
    }

    public static DescriptorHandle Invalid => new(uint.MaxValue, DescriptorType.SrvCbvUav);

    public bool Equals(DescriptorHandle other) => HeapIndex == other.HeapIndex && Type == other.Type;
    public override bool Equals(object? obj) => obj is DescriptorHandle other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(HeapIndex, Type);
}

/// <summary>
/// 描述符类型
/// </summary>
public enum DescriptorType : byte
{
    SrvCbvUav,
    Sampler,
    Rtv,
    Dsv
}

/// <summary>
/// 采样器描述符
/// </summary>
public readonly struct SamplerDesc
{
    public readonly SamplerFilter Filter;
    public readonly SamplerAddressMode AddressU;
    public readonly SamplerAddressMode AddressV;
    public readonly float MaxAnisotropy;

    public SamplerDesc(
        SamplerFilter filter = SamplerFilter.Linear,
        SamplerAddressMode addressU = SamplerAddressMode.Clamp,
        SamplerAddressMode addressV = SamplerAddressMode.Clamp,
        float maxAnisotropy = 1.0f)
    {
        Filter = filter;
        AddressU = addressU;
        AddressV = addressV;
        MaxAnisotropy = maxAnisotropy;
    }

    public static SamplerDesc LinearClamp => new(SamplerFilter.Linear, SamplerAddressMode.Clamp, SamplerAddressMode.Clamp);
    public static SamplerDesc PointClamp => new(SamplerFilter.Point, SamplerAddressMode.Clamp, SamplerAddressMode.Clamp);
    public static SamplerDesc LinearWrap => new(SamplerFilter.Linear, SamplerAddressMode.Wrap, SamplerAddressMode.Wrap);
    public static SamplerDesc Anisotropic => new(SamplerFilter.Anisotropic, SamplerAddressMode.Clamp, SamplerAddressMode.Clamp, 16f);
}

public enum SamplerFilter : byte
{
    Point,
    Linear,
    Anisotropic
}

public enum SamplerAddressMode : byte
{
    Wrap,
    Mirror,
    Clamp,
    Border
}

/// <summary>
/// 描述符池 - 简单的空闲列表分配器
/// </summary>
internal sealed class DescriptorPool : IDisposable
{
    private readonly DescriptorType _type;
    private readonly int _capacity;
    private readonly Stack<uint> _freeList;
    private int _allocatedCount;

    public int AllocatedCount => _allocatedCount;

    public DescriptorPool(DescriptorType type, int capacity)
    {
        _type = type;
        _capacity = capacity;
        _freeList = new Stack<uint>(capacity);

        // 反向压入空闲列表，使低索引先分配
        for (int i = capacity - 1; i >= 0; i--)
        {
            _freeList.Push((uint)i);
        }
    }

    public uint Allocate()
    {
        if (_freeList.Count == 0)
        {
            throw new InvalidOperationException(
                $"Descriptor heap exhausted. Type={_type}, Capacity={_capacity}");
        }

        _allocatedCount++;
        return _freeList.Pop();
    }

    public void Free(uint index)
    {
        _freeList.Push(index);
        _allocatedCount--;
    }

    public void Dispose()
    {
        _freeList.Clear();
    }
}

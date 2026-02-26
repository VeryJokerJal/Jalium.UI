namespace Jalium.UI.Gpu.Pipeline;

/// <summary>
/// PSO 缓存 - 按 PipelineStateDesc 哈希缓存已创建的 Pipeline State Object
/// </summary>
public sealed class PipelineCache : IDisposable
{
    private readonly Dictionary<ulong, nint> _cache = new();
    private readonly IRenderBackendEx _backend;
    private bool _disposed;

    public PipelineCache(IRenderBackendEx backend)
    {
        _backend = backend;
    }

    /// <summary>
    /// 获取或创建 PSO
    /// </summary>
    public nint GetOrCreate(PipelineStateDesc desc)
    {
        var hash = desc.GetHash();

        if (_cache.TryGetValue(hash, out var existing))
            return existing;

        var pso = _backend.CreatePipelineState(desc);
        _cache[hash] = pso;

        return pso;
    }

    /// <summary>
    /// 预创建所有预定义管线
    /// </summary>
    public void WarmUp()
    {
        GetOrCreate(UIPipelines.OpaqueRect);
        GetOrCreate(UIPipelines.TransparentRect);
        GetOrCreate(UIPipelines.Text);
        GetOrCreate(UIPipelines.Image);
        GetOrCreate(UIPipelines.Path);
        GetOrCreate(UIPipelines.GaussianBlurH);
        GetOrCreate(UIPipelines.GaussianBlurV);
        GetOrCreate(UIPipelines.BackdropFilter);
        GetOrCreate(UIPipelines.Composite);
    }

    /// <summary>
    /// 缓存条目数
    /// </summary>
    public int Count => _cache.Count;

    /// <summary>
    /// 清除所有缓存（触发 PSO 重建）
    /// </summary>
    public void Invalidate()
    {
        foreach (var pso in _cache.Values)
        {
            _backend.DestroyPipelineState(pso);
        }
        _cache.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Invalidate();
    }
}

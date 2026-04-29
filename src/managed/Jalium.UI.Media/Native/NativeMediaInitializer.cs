namespace Jalium.UI.Media.Native;

/// <summary>
/// 进程内一次性初始化 <c>jalium.native.media</c> 库。线程安全、幂等。
/// </summary>
internal static class NativeMediaInitializer
{
    private static readonly object _lock = new();
    private static bool _initialized;
    private static NativeMediaStatus _initStatus;

    /// <summary>
    /// 确保 <c>jalium_media_initialize</c> 至少被成功调用一次。
    /// 失败时抛出 <see cref="NativeMediaException"/>，调用方可以捕获并降级。
    /// </summary>
    public static void EnsureInitialized()
    {
        if (_initialized && _initStatus == NativeMediaStatus.Ok) return;

        lock (_lock)
        {
            if (_initialized && _initStatus == NativeMediaStatus.Ok) return;

            _initStatus = NativeMediaInterop.jalium_media_initialize();
            _initialized = true;

            if (_initStatus != NativeMediaStatus.Ok)
            {
                throw new NativeMediaException(_initStatus, "jalium_media_initialize");
            }
        }
    }

    /// <summary>
    /// 查询当前初始化状态（不触发 init）。
    /// </summary>
    public static NativeMediaStatus CurrentStatus
    {
        get
        {
            lock (_lock) return _initialized ? _initStatus : NativeMediaStatus.NotInitialized;
        }
    }
}

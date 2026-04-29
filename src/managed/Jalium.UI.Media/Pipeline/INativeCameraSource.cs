using Jalium.UI.Media.Imaging;

namespace Jalium.UI.Media.Pipeline;

/// <summary>
/// 平台原生摄像头采集抽象。Windows 由 Media Foundation Capture 实现、
/// Android 由 Camera2 NDK + <c>AImageReader</c> 实现。
/// </summary>
public interface INativeCameraSource : IDisposable
{
    /// <summary>
    /// 打开指定设备并启动采集。
    /// </summary>
    /// <param name="deviceId"><see cref="CameraDeviceInfo.Id"/>。</param>
    /// <param name="requestedWidth">请求的宽度（像素）。实现选择最接近的支持分辨率。</param>
    /// <param name="requestedHeight">请求的高度（像素）。</param>
    /// <param name="requestedFps">请求的帧率。</param>
    /// <param name="requestedFormat">请求的像素格式（默认 BGRA8）。</param>
    void Open(string deviceId, int requestedWidth, int requestedHeight, double requestedFps, NativePixelFormat requestedFormat = NativePixelFormat.Bgra8);

    /// <summary>
    /// 同步读取一帧。返回 false 表示流结束 / 设备关闭。
    /// </summary>
    bool TryReadFrame(out MediaFrame? frame);

    /// <summary>停止采集（设备保持已打开状态）。</summary>
    void Stop();
}

/// <summary>
/// <see cref="INativeCameraSource"/> 工厂。
/// </summary>
public interface INativeCameraSourceFactory
{
    /// <summary>枚举系统当前可见的所有摄像头设备。</summary>
    IReadOnlyList<CameraDeviceInfo> EnumerateDevices();

    /// <summary>创建一个未打开的采集源。</summary>
    INativeCameraSource Create(IMediaFramePool? framePool = null);
}

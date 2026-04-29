namespace Jalium.UI.Media.Native;

/// <summary>
/// 与原生 <c>jalium_media_status_t</c> 一一对应的状态码。
/// </summary>
public enum NativeMediaStatus
{
    /// <summary>成功。</summary>
    Ok = 0,

    /// <summary>非法参数。</summary>
    InvalidArgument = 1,

    /// <summary>内存不足。</summary>
    OutOfMemory = 2,

    /// <summary>I/O 错误。</summary>
    IoError = 3,

    /// <summary>不支持的容器或像素格式。</summary>
    UnsupportedFormat = 4,

    /// <summary>不支持的编解码器（H.265/VP9/AV1 在某些设备上无硬件解码）。</summary>
    UnsupportedCodec = 5,

    /// <summary>解码失败（数据损坏 / 不完整）。</summary>
    DecodeFailed = 6,

    /// <summary>到达流末尾。</summary>
    EndOfStream = 7,

    /// <summary>未初始化。<see cref="NativeMediaInterop.jalium_media_initialize"/> 未调用或失败。</summary>
    NotInitialized = 8,

    /// <summary>平台错误（HRESULT / AMediaResult 包装）。</summary>
    PlatformError = 9,

    /// <summary>找不到匹配设备。</summary>
    NoDevice = 10,

    /// <summary>权限被拒（Android 摄像头未授权）。</summary>
    PermissionDenied = 11,

    /// <summary>功能尚未实现（Commit 1 阶段所有解码 API 返回此值）。</summary>
    NotImplemented = 12,
}

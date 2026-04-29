namespace Jalium.UI.Media.Imaging;

/// <summary>
/// 共享 BGRA8 帧缓冲池。视频解码与摄像头采集走该池避免每帧 GC 分配。
/// </summary>
public interface IMediaFramePool
{
    /// <summary>
    /// 租用一个至少能容纳 <paramref name="width"/> × <paramref name="height"/> BGRA8 像素的 <see cref="MediaFrame"/>。
    /// 调用方必须 Dispose 以归还。
    /// </summary>
    MediaFrame Rent(int width, int height, int stride, TimeSpan presentationTime, NativePixelFormat format = NativePixelFormat.Bgra8);
}

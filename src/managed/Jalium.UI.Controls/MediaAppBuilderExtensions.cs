using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Native;
using Jalium.UI.Media.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Jalium.UI;

/// <summary>
/// AppBuilder 扩展：注册平台原生媒体管道（图像 / 视频 / 摄像头）。
/// </summary>
public static class MediaAppBuilderExtensions
{
    /// <summary>
    /// 注册平台原生媒体管道服务。
    /// </summary>
    /// <remarks>
    /// 注入：
    /// <list type="bullet">
    /// <item><see cref="IMediaFramePool"/> — BGRA 帧池（默认 <see cref="DefaultMediaFramePool"/>）</item>
    /// <item><see cref="INativeImageDecoder"/> — 静态图像解码器（WIC / AImageDecoder）</item>
    /// <item><see cref="INativeVideoDecoderFactory"/> — 视频解码器工厂（MF / MediaCodec）</item>
    /// <item><see cref="INativeCameraSourceFactory"/> — 摄像头采集工厂（MF Capture / Camera2 NDK）</item>
    /// </list>
    /// 实际平台分流由 <c>jalium.native.media</c> 库内部 <c>#ifdef _WIN32 / __ANDROID__</c> 完成。
    ///
    /// 同时把 <see cref="INativeImageDecoder"/> 注入 <see cref="BitmapImage"/> 静态访问点，
    /// 让所有 <c>BitmapImage.LoadFromBytes</c> 调用共享同一个原生解码器实例。
    /// </remarks>
    public static AppBuilder UseNativeMediaPipeline(this AppBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddSingleton<IMediaFramePool>(_ => DefaultMediaFramePool.Shared);
        builder.Services.TryAddSingleton<INativeImageDecoder, NativeImageDecoder>();
        builder.Services.TryAddSingleton<INativeVideoDecoderFactory, NativeVideoDecoderFactory>();
        builder.Services.TryAddSingleton<INativeCameraSourceFactory, NativeCameraSourceFactory>();

        // 把单例解码器注入静态字段，让所有 BitmapImage / MediaElement / MediaPlayer / CameraView
        // 共享同一组原生实例。
        builder.ConfigureApplication(_ =>
        {
            BitmapImage.SetDecoder(new NativeImageDecoder());
            var videoFactory = new NativeVideoDecoderFactory();
            MediaElement.SetVideoDecoderFactory(videoFactory);
            MediaPlayer.SetVideoDecoderFactory(videoFactory);
            CameraView.SetCameraFactory(new NativeCameraSourceFactory());
        });

        return builder;
    }
}

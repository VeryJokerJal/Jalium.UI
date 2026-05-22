namespace Jalium.UI.Media.Imaging;

/// <summary>
/// 平台原生图像解码器抽象。Windows 由 WIC 实现、Android 由 NDK <c>AImageDecoder</c>
/// (API 30+) 与 JNI <c>BitmapFactory</c> fallback (API 24-29) 实现。
/// </summary>
/// <remarks>
/// 实现禁止依赖 OpenCV、System.Drawing、SkiaSharp 等第三方库。
/// </remarks>
public interface INativeImageDecoder
{
    /// <summary>
    /// 从内存字节解码图像为 BGRA8 / RGBA8 像素。
    /// </summary>
    /// <param name="data">编码后的图像字节（PNG/JPEG/WebP/GIF/BMP 等）。</param>
    /// <param name="requestedFormat">请求的像素格式。实际格式可能不同，以返回值的 <see cref="DecodedImage.Format"/> 为准。</param>
    DecodedImage Decode(ReadOnlySpan<byte> data, NativePixelFormat requestedFormat = NativePixelFormat.Bgra8);

    /// <summary>
    /// 从流解码图像。流必须可读。实现内部需读完所有字节。
    /// </summary>
    DecodedImage Decode(Stream stream, NativePixelFormat requestedFormat = NativePixelFormat.Bgra8);

    /// <summary>
    /// 从文件路径解码图像。
    /// </summary>
    DecodedImage DecodeFile(string filePath, NativePixelFormat requestedFormat = NativePixelFormat.Bgra8);

    /// <summary>
    /// 仅读取图像尺寸而不解码像素，用于轻量级元数据探测。
    /// </summary>
    /// <returns>成功返回 true。失败时 <paramref name="width"/> 与 <paramref name="height"/> 置 0。</returns>
    bool TryReadDimensions(ReadOnlySpan<byte> data, out int width, out int height);

    /// <summary>
    /// 读取图像帧数。静态图像返回 1；动画 GIF / APNG / 动画 WebP 返回 &gt;1。
    /// 解码器未实现时默认返回 1（行为兼容静态图像）。
    /// </summary>
    int ReadFrameCount(ReadOnlySpan<byte> data) => 1;

    /// <summary>
    /// 解码指定帧并返回每帧延迟（毫秒）。<paramref name="frameIndex"/> = 0 与
    /// <see cref="Decode(ReadOnlySpan{byte}, NativePixelFormat)"/> 行为一致；解码器未实现多帧时,
    /// frameIndex &gt; 0 抛出 <see cref="ArgumentOutOfRangeException"/>。
    /// </summary>
    DecodedImageFrame DecodeFrame(ReadOnlySpan<byte> data, int frameIndex,
                                  NativePixelFormat requestedFormat = NativePixelFormat.Bgra8)
    {
        if (frameIndex != 0)
            throw new ArgumentOutOfRangeException(nameof(frameIndex),
                "Decoder does not support multi-frame images.");
        return new DecodedImageFrame(Decode(data, requestedFormat), delayMs: 0);
    }
}

/// <summary>
/// 解码后的单帧图像 + 帧延迟（用于动画 GIF / APNG / 动画 WebP）。
/// </summary>
public readonly struct DecodedImageFrame
{
    /// <summary>Initializes a new instance of the <see cref="DecodedImageFrame"/> struct.</summary>
    public DecodedImageFrame(DecodedImage image, int delayMs)
    {
        Image = image;
        DelayMs = delayMs;
    }

    /// <summary>解码后的帧像素数据。</summary>
    public DecodedImage Image { get; }

    /// <summary>帧延迟（毫秒）。静态图像或无延迟元数据时为 0。</summary>
    public int DelayMs { get; }
}

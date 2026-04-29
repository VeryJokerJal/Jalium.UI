namespace Jalium.UI.Media.Imaging;

/// <summary>
/// 不可变的解码图像数据：BGRA8 / RGBA8 紧凑像素 + 几何信息。
/// 由 <see cref="INativeImageDecoder"/> 实现产出。
/// </summary>
public readonly struct DecodedImage
{
    /// <summary>
    /// 初始化新的 <see cref="DecodedImage"/>。
    /// </summary>
    public DecodedImage(ReadOnlyMemory<byte> pixels, int width, int height, int stride, NativePixelFormat format)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfLessThan(stride, width * 4);

        Pixels = pixels;
        Width = width;
        Height = height;
        Stride = stride;
        Format = format;
    }

    /// <summary>
    /// 紧凑或带行间填充的像素数据。
    /// </summary>
    public ReadOnlyMemory<byte> Pixels { get; }

    /// <summary>
    /// 像素宽度。
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// 像素高度。
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// 行跨度（字节）。至少为 <c>Width * 4</c>。
    /// </summary>
    public int Stride { get; }

    /// <summary>
    /// 像素格式。
    /// </summary>
    public NativePixelFormat Format { get; }
}

/// <summary>
/// 跨平台像素格式枚举，与原生 <c>jalium_pixel_format_t</c> 一一对应。
/// </summary>
public enum NativePixelFormat
{
    /// <summary>BGRA8（默认；与 D3D12 swap-chain 一致）。</summary>
    Bgra8 = 0,

    /// <summary>RGBA8（Android Vulkan 在 Mali GPU 上常用）。</summary>
    Rgba8 = 1,
}

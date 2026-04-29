using System.Buffers;

namespace Jalium.UI.Media.Imaging;

/// <summary>
/// 默认 <see cref="IMediaFramePool"/> 实现，复用进程内 <see cref="ArrayPool{T}.Shared"/>。
/// </summary>
public sealed class DefaultMediaFramePool : IMediaFramePool
{
    /// <summary>共享的 <see cref="DefaultMediaFramePool"/> 实例。</summary>
    public static DefaultMediaFramePool Shared { get; } = new();

    /// <inheritdoc />
    public MediaFrame Rent(int width, int height, int stride, TimeSpan presentationTime, NativePixelFormat format = NativePixelFormat.Bgra8)
    {
        return new MediaFrame(ArrayPool<byte>.Shared, width, height, stride, presentationTime, format);
    }
}

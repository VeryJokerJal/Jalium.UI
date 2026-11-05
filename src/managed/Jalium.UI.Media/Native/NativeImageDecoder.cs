using System.Runtime.InteropServices;
using Jalium.UI.Media.Imaging;

namespace Jalium.UI.Media.Native;

/// <summary>
/// <see cref="INativeImageDecoder"/> 的默认实现：调用 <see cref="NativeMediaInterop"/>。
/// 真实的 WIC / AImageDecoder 解码逻辑在原生库中按 <c>#ifdef</c> 分流。
/// </summary>
public sealed partial class NativeImageDecoder : INativeImageDecoder
{
    /// <inheritdoc />
    public bool SupportsMimeType(string mimeType) => ImageMimeTypes.IsSupported(mimeType);

    /// <summary>初始化 <see cref="NativeImageDecoder"/>，确保原生库已 <c>jalium_media_initialize</c>。</summary>
    public NativeImageDecoder()
    {
        NativeMediaInitializer.EnsureInitialized();
    }

    /// <inheritdoc />
    public DecodedImage Decode(ReadOnlySpan<byte> data, NativePixelFormat requestedFormat = NativePixelFormat.Bgra8)
    {
        if (data.IsEmpty) throw new ArgumentException("Image data is empty.", nameof(data));

        NativeMediaInterop.NativeImage native;
        NativeMediaStatus status;
        unsafe
        {
            fixed (byte* ptr = data)
            {
                status = NativeMediaInterop.jalium_image_decode_memory(
                    ptr, (nuint)data.Length, NativeMediaInterop.ToNative(requestedFormat), out native);
            }
        }
        NativeMediaException.ThrowIfFailed(status, "jalium_image_decode_memory");

        return CopyAndFree(ref native);
    }

    /// <inheritdoc />
    public DecodedImage Decode(Stream stream, NativePixelFormat requestedFormat = NativePixelFormat.Bgra8)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // 原生 ABI 只接受连续内存块，先全部读入托管缓冲，再传指针。
        // 大文件场景可在后续 commit 引入 IMFByteStream / AMediaDataSource 直读。
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return Decode(ms.GetBuffer().AsSpan(0, (int)ms.Length), requestedFormat);
    }

    /// <inheritdoc />
    public DecodedImage DecodeFile(string filePath, NativePixelFormat requestedFormat = NativePixelFormat.Bgra8)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        var status = NativeMediaInterop.jalium_image_decode_file(
            filePath, NativeMediaInterop.ToNative(requestedFormat), out var native);
        NativeMediaException.ThrowIfFailed(status, "jalium_image_decode_file");

        return CopyAndFree(ref native);
    }

    /// <inheritdoc />
    public bool TryReadDimensions(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (data.IsEmpty) return false;

        NativeMediaStatus status;
        uint w, h;
        unsafe
        {
            fixed (byte* ptr = data)
            {
                status = NativeMediaInterop.jalium_image_read_dimensions(ptr, (nuint)data.Length, out w, out h);
            }
        }
        if (status != NativeMediaStatus.Ok) return false;
        if (w > int.MaxValue || h > int.MaxValue) return false;
        width = (int)w;
        height = (int)h;
        return true;
    }

    /// <inheritdoc />
    public int ReadFrameCount(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return 0;
        NativeMediaStatus status;
        uint count;
        unsafe
        {
            fixed (byte* ptr = data)
            {
                status = NativeMediaInterop.jalium_image_read_frame_count(ptr, (nuint)data.Length, out count);
            }
        }
        if (status != NativeMediaStatus.Ok) return 1;
        return Math.Max(1, (int)count);
    }

    /// <inheritdoc />
    public DecodedImageFrame DecodeFrame(ReadOnlySpan<byte> data, int frameIndex,
                                          NativePixelFormat requestedFormat = NativePixelFormat.Bgra8)
    {
        if (data.IsEmpty) throw new ArgumentException("Image data is empty.", nameof(data));
        if (frameIndex < 0) throw new ArgumentOutOfRangeException(nameof(frameIndex));

        NativeMediaInterop.NativeImage native;
        NativeMediaStatus status;
        uint delayMs;
        unsafe
        {
            fixed (byte* ptr = data)
            {
                status = NativeMediaInterop.jalium_image_decode_frame(
                    ptr, (nuint)data.Length, (uint)frameIndex,
                    NativeMediaInterop.ToNative(requestedFormat),
                    out native, out delayMs);
            }
        }
        NativeMediaException.ThrowIfFailed(status, "jalium_image_decode_frame");

        return new DecodedImageFrame(CopyAndFree(ref native), (int)delayMs);
    }

    private static DecodedImage CopyAndFree(ref NativeMediaInterop.NativeImage native)
    {
        try
        {
            if (native.Pixels == nint.Zero || native.Width == 0 || native.Height == 0)
            {
                throw new NativeMediaException(NativeMediaStatus.DecodeFailed, "jalium_image_decode (empty result)");
            }

            if (native.Width > int.MaxValue ||
                native.Height > int.MaxValue ||
                native.StrideBytes > int.MaxValue)
            {
                throw new NativeMediaException(
                    NativeMediaStatus.DecodeFailed,
                    "jalium_image_decode (dimensions exceed managed limits)");
            }

            int width = (int)native.Width;
            int height = (int)native.Height;
            int stride = (int)native.StrideBytes;
            int size;
            try
            {
                size = PixelBufferLayout.GetRequiredByteCount(width, height, stride);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw new NativeMediaException(
                    NativeMediaStatus.DecodeFailed,
                    $"jalium_image_decode (invalid pixel layout: {ex.Message})");
            }

            var buffer = new byte[size];
            unsafe
            {
                fixed (byte* dst = buffer)
                {
                    Buffer.MemoryCopy((void*)native.Pixels, dst, size, size);
                }
            }
            // buffer 是刚 new 出来的专属托管数组、恰好 size 长度，原生侧内存在 finally
            // 里立即释放——消费方可以直接接管，不必再拷一份全尺寸副本。
            return new DecodedImage(
                buffer,
                width,
                height,
                stride,
                NativeMediaInterop.FromNative(native.Format),
                bufferIsExclusive: true);
        }
        finally
        {
            NativeMediaInterop.jalium_image_free(ref native);
        }
    }
}

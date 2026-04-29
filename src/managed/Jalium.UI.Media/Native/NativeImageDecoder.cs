using System.Runtime.InteropServices;
using Jalium.UI.Media.Imaging;

namespace Jalium.UI.Media.Native;

/// <summary>
/// <see cref="INativeImageDecoder"/> 的默认实现：调用 <see cref="NativeMediaInterop"/>。
/// 真实的 WIC / AImageDecoder 解码逻辑在原生库中按 <c>#ifdef</c> 分流。
/// </summary>
public sealed class NativeImageDecoder : INativeImageDecoder
{
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
        width = (int)w;
        height = (int)h;
        return true;
    }

    private static DecodedImage CopyAndFree(ref NativeMediaInterop.NativeImage native)
    {
        try
        {
            if (native.Pixels == nint.Zero || native.Width == 0 || native.Height == 0)
            {
                throw new NativeMediaException(NativeMediaStatus.DecodeFailed, "jalium_image_decode (empty result)");
            }

            var size = checked((int)native.StrideBytes * (int)native.Height);
            var buffer = new byte[size];
            unsafe
            {
                fixed (byte* dst = buffer)
                {
                    Buffer.MemoryCopy((void*)native.Pixels, dst, size, size);
                }
            }
            return new DecodedImage(
                buffer,
                (int)native.Width,
                (int)native.Height,
                (int)native.StrideBytes,
                NativeMediaInterop.FromNative(native.Format));
        }
        finally
        {
            NativeMediaInterop.jalium_image_free(ref native);
        }
    }
}

using System.Runtime.InteropServices;
using Jalium.UI.Media.Imaging;

namespace Jalium.UI.Media.Native;

/// <summary>
/// 与原生 <c>jalium.native.media</c> 库的 P/Invoke 桥。Windows 加载
/// <c>jalium.native.media.dll</c>、Android 加载 <c>libjalium.native.media.so</c>。
/// </summary>
/// <remarks>
/// 所有 <c>out *_t</c> 缓冲由原生库分配，必须调用对应 <c>_free</c> / <c>_close</c> 释放。
/// 视频 / 摄像头帧缓冲跨 read_frame 复用，C# 在拿到下一帧前必须消费完。
/// </remarks>
internal static partial class NativeMediaInterop
{
    internal const string MediaLib = "jalium.native.media";

    // ----- 原生 struct（必须与 jalium_media.h 完全对齐）---------------------

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeImage
    {
        public uint Width;
        public uint Height;
        public uint StrideBytes;
        public int  Format;       // jalium_pixel_format_t
        public nint Pixels;       // uint8_t*
        public nint Reserved;     // void*
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeVideoInfo
    {
        public uint   Width;
        public uint   Height;
        public double DurationSeconds;
        public double FrameRate;
        public ulong  FrameCount;
        public int    ActiveCodec;   // jalium_video_codec_t
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeVideoFrame
    {
        public uint  Width;
        public uint  Height;
        public uint  StrideBytes;
        public int   Format;          // jalium_pixel_format_t
        public nint  Pixels;          // uint8_t*
        public long  PtsMicroseconds;
        public int   IsKeyframe;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeCameraFormat
    {
        public uint   Width;
        public uint   Height;
        public double Fps;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeCameraDevice
    {
        public nint Id;             // const char*  (UTF-8)
        public nint FriendlyName;   // const char*  (UTF-8)
        public int  Facing;         // jalium_camera_facing_t
        public uint FormatCount;
        public nint Formats;        // const jalium_camera_format_t*
    }

    // ----- 生命周期 ----------------------------------------------------------

    [LibraryImport(MediaLib, EntryPoint = "jalium_media_initialize")]
    internal static partial NativeMediaStatus jalium_media_initialize();

    [LibraryImport(MediaLib, EntryPoint = "jalium_media_shutdown")]
    internal static partial void jalium_media_shutdown();

    [LibraryImport(MediaLib, EntryPoint = "jalium_media_status_string")]
    internal static partial nint jalium_media_status_string(NativeMediaStatus status);

    [LibraryImport(MediaLib, EntryPoint = "jalium_media_supported_video_codecs")]
    internal static partial uint jalium_media_supported_video_codecs();

    // ----- 图像解码 ---------------------------------------------------------

    [LibraryImport(MediaLib, EntryPoint = "jalium_image_decode_memory")]
    internal static unsafe partial NativeMediaStatus jalium_image_decode_memory(
        byte* data,
        nuint size,
        int requestedFormat,
        out NativeImage outImage);

    [LibraryImport(MediaLib, EntryPoint = "jalium_image_decode_file", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial NativeMediaStatus jalium_image_decode_file(
        string utf8Path,
        int requestedFormat,
        out NativeImage outImage);

    [LibraryImport(MediaLib, EntryPoint = "jalium_image_read_dimensions")]
    internal static unsafe partial NativeMediaStatus jalium_image_read_dimensions(
        byte* data,
        nuint size,
        out uint width,
        out uint height);

    [LibraryImport(MediaLib, EntryPoint = "jalium_image_free")]
    internal static partial void jalium_image_free(ref NativeImage image);

    // ----- 视频解码 ---------------------------------------------------------

    [LibraryImport(MediaLib, EntryPoint = "jalium_video_decoder_open_file", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial NativeMediaStatus jalium_video_decoder_open_file(
        string utf8Path,
        int requestedFormat,
        out nint outDecoder);

    [LibraryImport(MediaLib, EntryPoint = "jalium_video_decoder_get_info")]
    internal static partial NativeMediaStatus jalium_video_decoder_get_info(
        nint decoder,
        out NativeVideoInfo outInfo);

    [LibraryImport(MediaLib, EntryPoint = "jalium_video_decoder_read_frame")]
    internal static partial NativeMediaStatus jalium_video_decoder_read_frame(
        nint decoder,
        out NativeVideoFrame outFrame);

    [LibraryImport(MediaLib, EntryPoint = "jalium_video_decoder_seek_microseconds")]
    internal static partial NativeMediaStatus jalium_video_decoder_seek_microseconds(
        nint decoder,
        long ptsMicroseconds);

    [LibraryImport(MediaLib, EntryPoint = "jalium_video_decoder_close")]
    internal static partial void jalium_video_decoder_close(nint decoder);

    // ----- 摄像头 -----------------------------------------------------------

    [LibraryImport(MediaLib, EntryPoint = "jalium_camera_enumerate")]
    internal static partial NativeMediaStatus jalium_camera_enumerate(
        out nint outDevices,
        out uint outCount);

    [LibraryImport(MediaLib, EntryPoint = "jalium_camera_devices_free")]
    internal static partial void jalium_camera_devices_free(nint devices, uint count);

    [LibraryImport(MediaLib, EntryPoint = "jalium_camera_open", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial NativeMediaStatus jalium_camera_open(
        string deviceId,
        uint requestedWidth,
        uint requestedHeight,
        double requestedFps,
        int requestedFormat,
        out nint outSource);

    [LibraryImport(MediaLib, EntryPoint = "jalium_camera_read_frame")]
    internal static partial NativeMediaStatus jalium_camera_read_frame(
        nint source,
        out NativeVideoFrame outFrame);

    [LibraryImport(MediaLib, EntryPoint = "jalium_camera_close")]
    internal static partial void jalium_camera_close(nint source);

    // ----- 工具 -------------------------------------------------------------

    /// <summary>读取 <see cref="jalium_media_status_string"/> 返回的 UTF-8 字符串。</summary>
    internal static string GetStatusString(NativeMediaStatus status)
    {
        try
        {
            var ptr = jalium_media_status_string(status);
            return ptr == nint.Zero ? "(null)" : Marshal.PtrToStringUTF8(ptr) ?? "(null)";
        }
        catch (DllNotFoundException)
        {
            return "(jalium.native.media not loaded)";
        }
        catch (EntryPointNotFoundException)
        {
            return "(jalium_media_status_string not exported)";
        }
    }

    /// <summary>把内部枚举转为原生 ABI int。</summary>
    internal static int ToNative(NativePixelFormat format) => (int)format;

    /// <summary>把原生 ABI int 转为内部枚举。</summary>
    internal static NativePixelFormat FromNative(int format)
        => format switch
        {
            0 => NativePixelFormat.Bgra8,
            1 => NativePixelFormat.Rgba8,
            _ => NativePixelFormat.Bgra8,
        };
}

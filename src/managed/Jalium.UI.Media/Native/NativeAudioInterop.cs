using System.Runtime.InteropServices;

namespace Jalium.UI.Media.Native;

/// <summary>
/// 与 <c>jalium.native.media</c> 库中音频部分(<c>jalium_audio.h</c>)的 P/Invoke 桥。
/// 与 <see cref="NativeMediaInterop"/> 共用同一个原生库——音频/视频/图像/摄像头
/// 全部由 <c>jalium.native.media.dll</c>(Windows)、<c>libjalium.native.media.so</c>
/// (Android/Linux)等单一二进制提供。
/// </summary>
/// <remarks>
/// <para>
/// 状态码复用 <see cref="NativeMediaStatus"/>;失败时统一交给
/// <see cref="NativeMediaException.ThrowIfFailed"/> 转抛。
/// </para>
/// <para>
/// 所有 <c>nint</c> 句柄由原生库分配,必须通过对应的 <c>*_close</c> 释放。
/// 解码器输出 PCM 是 caller-owned 的 <c>Span&lt;float&gt;</c>;设备 submit 走
/// lock-free ring buffer,不阻塞回调线程。
/// </para>
/// </remarks>
internal static partial class NativeAudioInterop
{
    internal const string MediaLib = NativeMediaInterop.MediaLib;

    // ----- 原生 struct(必须与 jalium_audio.h 完全对齐)-----------------------

    /// <summary>对应 <c>jalium_audio_info_t</c>。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeAudioInfo
    {
        public uint SampleRate;
        public uint Channels;
        public long DurationUs;
        public int  Codec;           // jalium_audio_codec_t
        public int  OutputFormat;    // jalium_audio_sample_format_t
    }

    /// <summary>对应 <c>jalium_audio_device_config_t</c>。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeAudioDeviceConfig
    {
        public uint SampleRate;
        public uint Channels;
        public int  SampleFormat;        // jalium_audio_sample_format_t
        public uint RingCapacityFrames;
        public nint OnPlaybackEnded;     // jalium_audio_playback_ended_fn (函数指针)
        public nint EndedUserData;       // void* — managed 侧塞 GCHandle.ToIntPtr
    }

    /// <summary>对应 <c>jalium_audio_stats_t</c>。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeAudioStats
    {
        public ulong Version;
        public ulong DecodersOpened;
        public ulong DecodersClosed;
        public ulong DevicesOpened;
        public ulong DevicesClosed;
        public ulong FramesDecodedWav;
        public ulong FramesDecodedFlac;
        public ulong FramesDecodedMp3;
        public ulong FramesDecodedOgg;
        public ulong FramesDecodedAac;
        public ulong FramesSubmitted;
        public ulong FramesPlayed;
        public ulong RingUnderruns;
        public ulong RingOverflows;
        public ulong CodecProbeFailures;
        public ulong PlatformAacFallbacks;

        // reserved[16]
        public ulong _reserved00; public ulong _reserved01; public ulong _reserved02; public ulong _reserved03;
        public ulong _reserved04; public ulong _reserved05; public ulong _reserved06; public ulong _reserved07;
        public ulong _reserved08; public ulong _reserved09; public ulong _reserved10; public ulong _reserved11;
        public ulong _reserved12; public ulong _reserved13; public ulong _reserved14; public ulong _reserved15;
    }

    /// <summary>对应 <c>jalium_audio_playback_ended_fn</c>(C 函数指针签名)。</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void PlaybackEndedNative(nint userData);

    // ----- 生命周期 ----------------------------------------------------------

    [LibraryImport(MediaLib, EntryPoint = "jalium_audio_initialize")]
    internal static partial NativeMediaStatus jalium_audio_initialize();

    [LibraryImport(MediaLib, EntryPoint = "jalium_audio_shutdown")]
    internal static partial void jalium_audio_shutdown();

    // ----- Decoder ----------------------------------------------------------

    [LibraryImport(MediaLib, EntryPoint = "jalium_audio_decoder_open_file", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial NativeMediaStatus jalium_audio_decoder_open_file(
        string utf8Path,
        int hint,
        out nint outDecoder);

    [LibraryImport(MediaLib, EntryPoint = "jalium_linux_audio_decoder_open_track", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial NativeMediaStatus jalium_linux_audio_decoder_open_track(
        string utf8PathOrUri,
        uint trackIndex,
        out nint outDecoder);

    [LibraryImport(MediaLib, EntryPoint = "jalium_audio_decoder_open_memory")]
    internal static unsafe partial NativeMediaStatus jalium_audio_decoder_open_memory(
        byte* data,
        nuint size,
        int hint,
        out nint outDecoder);

    [LibraryImport(MediaLib, EntryPoint = "jalium_audio_decoder_get_info")]
    internal static partial NativeMediaStatus jalium_audio_decoder_get_info(
        nint decoder,
        out NativeAudioInfo outInfo);

    [LibraryImport(MediaLib, EntryPoint = "jalium_audio_decoder_read_frames")]
    internal static unsafe partial NativeMediaStatus jalium_audio_decoder_read_frames(
        nint decoder,
        float* dst,
        uint frameCapacity,
        out uint framesRead);

    [LibraryImport(MediaLib, EntryPoint = "jalium_audio_decoder_seek_us")]
    internal static partial NativeMediaStatus jalium_audio_decoder_seek_us(
        nint decoder,
        long positionUs);

    [LibraryImport(MediaLib, EntryPoint = "jalium_audio_decoder_close")]
    internal static partial void jalium_audio_decoder_close(nint decoder);

    // ----- Playback device --------------------------------------------------

    [LibraryImport(MediaLib, EntryPoint = "jalium_audio_device_open")]
    internal static partial NativeMediaStatus jalium_audio_device_open(
        in NativeAudioDeviceConfig config,
        out nint outDevice);

    [LibraryImport(MediaLib, EntryPoint = "jalium_audio_device_start")]
    internal static partial NativeMediaStatus jalium_audio_device_start(nint device);

    [LibraryImport(MediaLib, EntryPoint = "jalium_audio_device_stop")]
    internal static partial NativeMediaStatus jalium_audio_device_stop(nint device);

    [LibraryImport(MediaLib, EntryPoint = "jalium_audio_device_submit")]
    internal static unsafe partial NativeMediaStatus jalium_audio_device_submit(
        nint device,
        float* pcm,
        uint frameCount,
        out uint framesWritten);

    [LibraryImport(MediaLib, EntryPoint = "jalium_audio_device_signal_end_of_stream")]
    internal static partial NativeMediaStatus jalium_audio_device_signal_end_of_stream(nint device);

    [LibraryImport(MediaLib, EntryPoint = "jalium_audio_device_flush")]
    internal static partial NativeMediaStatus jalium_audio_device_flush(nint device);

    [LibraryImport(MediaLib, EntryPoint = "jalium_audio_device_set_volume")]
    internal static partial NativeMediaStatus jalium_audio_device_set_volume(nint device, float linearVolume);

    [LibraryImport(MediaLib, EntryPoint = "jalium_audio_device_played_frames")]
    internal static partial ulong jalium_audio_device_played_frames(nint device);

    [LibraryImport(MediaLib, EntryPoint = "jalium_audio_device_close")]
    internal static partial void jalium_audio_device_close(nint device);

    // ----- Telemetry --------------------------------------------------------

    [LibraryImport(MediaLib, EntryPoint = "jalium_query_audio_stats")]
    internal static partial void jalium_query_audio_stats(out NativeAudioStats outStats);

    [LibraryImport(MediaLib, EntryPoint = "jalium_reset_audio_stats")]
    internal static partial void jalium_reset_audio_stats();
}

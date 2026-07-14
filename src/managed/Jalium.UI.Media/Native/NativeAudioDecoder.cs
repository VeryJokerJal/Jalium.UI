using Jalium.UI.Media.Pipeline;

namespace Jalium.UI.Media.Native;

/// <summary>
/// 通过 <c>jalium_audio_decoder_*</c> P/Invoke 实现的解码器。同一实例只允许
/// 由 <see cref="AudioPlayer"/> 的 pump worker 单线程访问。
/// </summary>
public sealed class NativeAudioDecoder : INativeAudioDecoder, INativeAudioTrackSelector
{
    private nint _handle;
    private int _sampleRate;
    private int _channels;
    private System.TimeSpan _duration;
    private SupportedAudioCodec _codec;
    private bool _disposed;
    private int _audioTrackIndex;

    /// <inheritdoc />
    public int AudioTrackIndex
    {
        get => _audioTrackIndex;
        set
        {
            if (value < 0) throw new System.ArgumentOutOfRangeException(nameof(value));
            if (_handle != nint.Zero)
                throw new System.InvalidOperationException("Set AudioTrackIndex before Open.");
            _audioTrackIndex = value;
        }
    }

    /// <inheritdoc />
    public int SampleRate => _sampleRate;

    /// <inheritdoc />
    public int Channels => _channels;

    /// <inheritdoc />
    public System.TimeSpan Duration => _duration;

    /// <inheritdoc />
    public SupportedAudioCodec Codec => _codec;

    /// <inheritdoc />
    public void Open(string utf8Path)
    {
        if (utf8Path is null) throw new System.ArgumentNullException(nameof(utf8Path));
        if (_disposed) throw new System.ObjectDisposedException(nameof(NativeAudioDecoder));

        NativeMediaInitializer.EnsureInitialized();
        EnsureAudioInitialized();

        CloseHandleLocked();

        NativeMediaStatus st;
        nint handle;
        if (OperatingSystem.IsLinux() && _audioTrackIndex > 0)
        {
            st = OpenLinuxTrack(utf8Path, out handle);
        }
        else
        {
            st = NativeAudioInterop.jalium_audio_decoder_open_file(
                utf8Path,
                (int)JaliumAudioCodec.Auto,
                out handle);
            // Built-in codecs cover local elementary audio files. GStreamer
            // handles remote URIs and richer containers when that probe fails.
            if (OperatingSystem.IsLinux() && st != NativeMediaStatus.Ok)
            {
                var linuxStatus = OpenLinuxTrack(utf8Path, out var linuxHandle);
                if (linuxStatus == NativeMediaStatus.Ok)
                {
                    st = linuxStatus;
                    handle = linuxHandle;
                }
            }
        }
        NativeMediaException.ThrowIfFailed(st, "jalium_audio_decoder_open_file");

        try
        {
            st = NativeAudioInterop.jalium_audio_decoder_get_info(handle, out var info);
            NativeMediaException.ThrowIfFailed(st, "jalium_audio_decoder_get_info");

            _handle = handle;
            _sampleRate = (int)info.SampleRate;
            _channels = (int)info.Channels;
            _duration = info.DurationUs <= 0
                ? System.TimeSpan.Zero
                : System.TimeSpan.FromTicks(info.DurationUs * (System.TimeSpan.TicksPerMillisecond / 1000));
            _codec = MapCodec((JaliumAudioCodec)info.Codec);
        }
        catch
        {
            NativeAudioInterop.jalium_audio_decoder_close(handle);
            throw;
        }
    }

    private NativeMediaStatus OpenLinuxTrack(string source, out nint handle)
    {
        try
        {
            return NativeAudioInterop.jalium_linux_audio_decoder_open_track(
                source, checked((uint)_audioTrackIndex), out handle);
        }
        catch (EntryPointNotFoundException)
        {
            handle = nint.Zero;
            return NativeMediaStatus.NotImplemented;
        }
    }

    /// <inheritdoc />
    public int ReadFrames(System.Span<float> dst)
    {
        if (_disposed) throw new System.ObjectDisposedException(nameof(NativeAudioDecoder));
        if (_handle == nint.Zero) throw new System.InvalidOperationException("Decoder is not open.");
        if (_channels <= 0) return 0;

        int frameCapacity = dst.Length / _channels;
        if (frameCapacity <= 0) return 0;

        uint framesRead;
        NativeMediaStatus st;
        unsafe
        {
            fixed (float* p = dst)
            {
                st = NativeAudioInterop.jalium_audio_decoder_read_frames(
                    _handle, p, (uint)frameCapacity, out framesRead);
            }
        }

        if (st == NativeMediaStatus.EndOfStream) return 0;
        NativeMediaException.ThrowIfFailed(st, "jalium_audio_decoder_read_frames");
        return (int)framesRead;
    }

    /// <inheritdoc />
    public void Seek(System.TimeSpan position)
    {
        if (_disposed) throw new System.ObjectDisposedException(nameof(NativeAudioDecoder));
        if (_handle == nint.Zero) throw new System.InvalidOperationException("Decoder is not open.");

        long us = position < System.TimeSpan.Zero ? 0L : (long)(position.TotalMilliseconds * 1000.0);
        var st = NativeAudioInterop.jalium_audio_decoder_seek_us(_handle, us);
        NativeMediaException.ThrowIfFailed(st, "jalium_audio_decoder_seek_us");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CloseHandleLocked();
    }

    private void CloseHandleLocked()
    {
        if (_handle != nint.Zero)
        {
            var h = _handle;
            _handle = nint.Zero;
            NativeAudioInterop.jalium_audio_decoder_close(h);
        }
        _sampleRate = 0;
        _channels = 0;
        _duration = System.TimeSpan.Zero;
        _codec = SupportedAudioCodec.Unknown;
    }

    // ---------------------------------------------------------------------------

    private static readonly object s_audioInitLock = new();
    private static NativeMediaStatus s_audioInitStatus = (NativeMediaStatus)(-1);

    internal static void EnsureAudioInitialized()
    {
        if (s_audioInitStatus == NativeMediaStatus.Ok) return;
        lock (s_audioInitLock)
        {
            if (s_audioInitStatus == NativeMediaStatus.Ok) return;
            s_audioInitStatus = NativeAudioInterop.jalium_audio_initialize();
            if (s_audioInitStatus != NativeMediaStatus.Ok)
            {
                throw new NativeMediaException(s_audioInitStatus, "jalium_audio_initialize");
            }
        }
    }

    private static SupportedAudioCodec MapCodec(JaliumAudioCodec native)
        => native switch
        {
            JaliumAudioCodec.Wav  => SupportedAudioCodec.Wav,
            JaliumAudioCodec.Flac => SupportedAudioCodec.Flac,
            JaliumAudioCodec.Mp3  => SupportedAudioCodec.Mp3,
            JaliumAudioCodec.Ogg  => SupportedAudioCodec.Ogg,
            JaliumAudioCodec.Aac  => SupportedAudioCodec.Aac,
            _ => SupportedAudioCodec.Unknown,
        };

    // 与 jalium_audio_codec_t 一一对应。
    private enum JaliumAudioCodec
    {
        Auto = 0,
        Wav  = 1,
        Flac = 2,
        Mp3  = 3,
        Ogg  = 4,
        Aac  = 5,
    }
}

/// <summary>
/// <see cref="NativeAudioDecoder"/> 工厂。
/// </summary>
public sealed class NativeAudioDecoderFactory : INativeAudioDecoderFactory
{
    /// <inheritdoc />
    public INativeAudioDecoder Create() => new NativeAudioDecoder();
}

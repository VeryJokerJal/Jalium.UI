using System.Runtime.InteropServices;
using Jalium.UI.Media.Pipeline;

namespace Jalium.UI.Media.Native;

/// <summary>Runtime media capabilities and stream discovery on Linux.</summary>
public static class NativeLinuxMedia
{
    /// <summary>Returns only features that loaded successfully in this process.</summary>
    public static LinuxMediaCapability GetCapabilities()
    {
        if (!OperatingSystem.IsLinux()) return LinuxMediaCapability.None;
        try
        {
            NativeMediaInitializer.EnsureInitialized();
            return (LinuxMediaCapability)NativeMediaInterop.jalium_linux_media_capabilities();
        }
        catch (DllNotFoundException)
        {
            return LinuxMediaCapability.None;
        }
        catch (EntryPointNotFoundException)
        {
            return LinuxMediaCapability.None;
        }
        catch (NativeMediaException)
        {
            return LinuxMediaCapability.None;
        }
    }

    /// <summary>Discovers embedded audio and subtitle streams.</summary>
    public static IReadOnlyList<MediaTrackInfo> DiscoverTracks(Uri source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException(
                "Native media track discovery is currently available on Linux.");
        }

        NativeMediaInitializer.EnsureInitialized();
        var path = source.IsFile ? source.LocalPath : source.AbsoluteUri;
        var status = NativeMediaInterop.jalium_media_discover_tracks(
            path, out var raw, out var count);
        NativeMediaException.ThrowIfFailed(status, "jalium_media_discover_tracks");
        if (raw == nint.Zero || count == 0) return Array.Empty<MediaTrackInfo>();

        try
        {
            var result = new MediaTrackInfo[checked((int)count)];
            var size = Marshal.SizeOf<NativeMediaInterop.NativeMediaTrackInfo>();
            for (var index = 0; index < result.Length; index++)
            {
                var native = Marshal.PtrToStructure<NativeMediaInterop.NativeMediaTrackInfo>(
                    raw + index * size);
                result[index] = new MediaTrackInfo(
                    (MediaTrackKind)native.Kind,
                    checked((int)native.Index),
                    Marshal.PtrToStringUTF8(native.Id) ?? string.Empty,
                    Marshal.PtrToStringUTF8(native.Label) ?? string.Empty,
                    Marshal.PtrToStringUTF8(native.Language) ?? string.Empty,
                    Marshal.PtrToStringUTF8(native.Codec) ?? string.Empty,
                    checked((int)native.Channels),
                    checked((int)native.SampleRate),
                    native.IsDefault != 0,
                    native.IsForced != 0);
            }
            return result;
        }
        finally
        {
            NativeMediaInterop.jalium_media_tracks_free(raw, count);
        }
    }
}

internal readonly record struct NativeSubtitleCue(
    string Text,
    TimeSpan Start,
    TimeSpan Duration);

internal sealed class NativeSubtitleDecoder : IDisposable
{
    private nint _handle;

    public void Open(Uri source, int trackIndex)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (trackIndex < 0) throw new ArgumentOutOfRangeException(nameof(trackIndex));
        DisposeHandle();
        NativeMediaInitializer.EnsureInitialized();
        var path = source.IsFile ? source.LocalPath : source.AbsoluteUri;
        var status = NativeMediaInterop.jalium_subtitle_decoder_open(
            path, checked((uint)trackIndex), out _handle);
        NativeMediaException.ThrowIfFailed(status, "jalium_subtitle_decoder_open");
    }

    public bool TryReadCue(out NativeSubtitleCue cue)
    {
        if (_handle == nint.Zero) throw new InvalidOperationException("Subtitle decoder is not open.");
        var status = NativeMediaInterop.jalium_subtitle_decoder_read_cue(
            _handle, out var native);
        if (status == NativeMediaStatus.EndOfStream)
        {
            cue = default;
            return false;
        }
        NativeMediaException.ThrowIfFailed(status, "jalium_subtitle_decoder_read_cue");
        cue = new NativeSubtitleCue(
            Marshal.PtrToStringUTF8(native.Utf8Text) ?? string.Empty,
            TimeSpan.FromMicroseconds(native.StartMicroseconds),
            TimeSpan.FromMicroseconds(Math.Max(0, native.DurationMicroseconds)));
        return true;
    }

    public void Seek(TimeSpan position)
    {
        if (_handle == nint.Zero) throw new InvalidOperationException("Subtitle decoder is not open.");
        var microseconds = position <= TimeSpan.Zero ? 0 : (long)position.TotalMicroseconds;
        var status = NativeMediaInterop.jalium_subtitle_decoder_seek_us(_handle, microseconds);
        NativeMediaException.ThrowIfFailed(status, "jalium_subtitle_decoder_seek_us");
    }

    public void Dispose()
    {
        DisposeHandle();
        GC.SuppressFinalize(this);
    }

    private void DisposeHandle()
    {
        if (_handle == nint.Zero) return;
        NativeMediaInterop.jalium_subtitle_decoder_close(_handle);
        _handle = nint.Zero;
    }
}

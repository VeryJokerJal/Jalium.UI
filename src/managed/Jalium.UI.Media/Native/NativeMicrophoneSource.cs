using System.Buffers;
using System.Runtime.InteropServices;
using Jalium.UI.Media.Pipeline;

namespace Jalium.UI.Media.Native;

/// <summary>Linux GStreamer microphone capture through the native media ABI.</summary>
public sealed class NativeMicrophoneSource : INativeMicrophoneSource
{
    private nint _handle;
    private bool _disposed;

    public NativeMicrophoneSource() => NativeMediaInitializer.EnsureInitialized();

    public void Open(string deviceId, int requestedSampleRate = 48000, int requestedChannels = 1)
    {
        ArgumentException.ThrowIfNullOrEmpty(deviceId);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (requestedSampleRate < 0) throw new ArgumentOutOfRangeException(nameof(requestedSampleRate));
        if (requestedChannels is < 0 or > 8) throw new ArgumentOutOfRangeException(nameof(requestedChannels));
        Stop();
        var status = NativeMediaInterop.jalium_microphone_open(
            deviceId,
            checked((uint)requestedSampleRate),
            checked((uint)requestedChannels),
            out _handle);
        NativeMediaException.ThrowIfFailed(status, "jalium_microphone_open");
    }

    public bool TryReadFrame(out MicrophoneFrame? frame)
    {
        frame = null;
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_handle == nint.Zero) throw new InvalidOperationException("Microphone is not open.");
        var status = NativeMediaInterop.jalium_microphone_read_frame(_handle, out var native);
        if (status == NativeMediaStatus.EndOfStream) return false;
        NativeMediaException.ThrowIfFailed(status, "jalium_microphone_read_frame");
        var sampleCount = checked((int)(native.FrameCount * native.Channels));
        if (sampleCount == 0 || native.Samples == nint.Zero) return false;

        var buffer = ArrayPool<float>.Shared.Rent(sampleCount);
        try
        {
            Marshal.Copy(native.Samples, buffer, 0, sampleCount);
            frame = new MicrophoneFrame(
                buffer,
                sampleCount,
                checked((int)native.SampleRate),
                checked((int)native.Channels),
                TimeSpan.FromMicroseconds(native.PtsMicroseconds));
            return true;
        }
        catch
        {
            ArrayPool<float>.Shared.Return(buffer);
            throw;
        }
    }

    public void Stop()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CloseHandle();
    }

    public void Dispose()
    {
        if (_disposed) return;
        CloseHandle();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void CloseHandle()
    {
        if (_handle == nint.Zero) return;
        NativeMediaInterop.jalium_microphone_close(_handle);
        _handle = nint.Zero;
    }
}

public sealed class NativeMicrophoneSourceFactory : INativeMicrophoneSourceFactory
{
    public NativeMicrophoneSourceFactory() => NativeMediaInitializer.EnsureInitialized();

    public IReadOnlyList<MicrophoneDeviceInfo> EnumerateDevices()
    {
        var status = NativeMediaInterop.jalium_microphone_enumerate(out var raw, out var count);
        NativeMediaException.ThrowIfFailed(status, "jalium_microphone_enumerate");
        if (raw == nint.Zero || count == 0) return Array.Empty<MicrophoneDeviceInfo>();
        try
        {
            var result = new MicrophoneDeviceInfo[checked((int)count)];
            var size = Marshal.SizeOf<NativeMediaInterop.NativeMicrophoneDevice>();
            for (var index = 0; index < result.Length; index++)
            {
                var native = Marshal.PtrToStructure<NativeMediaInterop.NativeMicrophoneDevice>(
                    raw + index * size);
                result[index] = new MicrophoneDeviceInfo(
                    Marshal.PtrToStringUTF8(native.Id) ?? string.Empty,
                    Marshal.PtrToStringUTF8(native.FriendlyName) ?? string.Empty);
            }
            return result;
        }
        finally
        {
            NativeMediaInterop.jalium_microphone_devices_free(raw, count);
        }
    }

    public INativeMicrophoneSource Create() => new NativeMicrophoneSource();
}

using System.Buffers;

namespace Jalium.UI.Media.Pipeline;

/// <summary>Information about an audio capture device.</summary>
public sealed record MicrophoneDeviceInfo(string Id, string FriendlyName);

/// <summary>A pooled block of interleaved floating-point microphone samples.</summary>
public sealed class MicrophoneFrame : IDisposable
{
    private float[]? _buffer;

    internal MicrophoneFrame(
        float[] buffer,
        int sampleCount,
        int sampleRate,
        int channels,
        TimeSpan presentationTime)
    {
        _buffer = buffer;
        SampleCount = sampleCount;
        SampleRate = sampleRate;
        Channels = channels;
        PresentationTime = presentationTime;
    }

    public int SampleCount { get; }
    public int FrameCount => Channels == 0 ? 0 : SampleCount / Channels;
    public int SampleRate { get; }
    public int Channels { get; }
    public TimeSpan PresentationTime { get; }
    public ReadOnlyMemory<float> Samples => _buffer?.AsMemory(0, SampleCount)
        ?? ReadOnlyMemory<float>.Empty;

    public void Dispose()
    {
        var buffer = Interlocked.Exchange(ref _buffer, null);
        if (buffer != null) ArrayPool<float>.Shared.Return(buffer);
    }
}

public interface INativeMicrophoneSource : IDisposable
{
    void Open(string deviceId, int requestedSampleRate = 48000, int requestedChannels = 1);
    bool TryReadFrame(out MicrophoneFrame? frame);
    void Stop();
}

public interface INativeMicrophoneSourceFactory
{
    IReadOnlyList<MicrophoneDeviceInfo> EnumerateDevices();
    INativeMicrophoneSource Create();
}

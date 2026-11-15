using System.Collections.Concurrent;
using Jalium.UI.Media;
using Jalium.UI.Media.Pipeline;

namespace Jalium.UI.Tests;

public sealed class AudioPlayerSessionTests
{
    [Fact]
    public async Task EndOfStreamThenStopAndPlayProducesAudioAgain()
    {
        using var file = new LocalSource();
        var decoder = new Decoder();
        var device = new Device();
        using var player = Create(decoder, device);
        player.Open(file.Uri);
        player.Play();
        await Until(() => player.State == NativePlaybackState.Ended);
        var first = device.Submissions;
        Assert.True(first > 0);
        player.Stop();
        player.Play();
        await Until(() => player.State == NativePlaybackState.Ended && device.Submissions > first);
        player.Dispose();
        await player.PendingCleanup.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, decoder.Disposals);
        Assert.Equal(1, device.Disposals);
    }

    [Fact]
    public async Task PlayAfterEndRewindsWithoutReopeningDevice()
    {
        using var file = new LocalSource();
        var decoder = new Decoder();
        var device = new Device();
        using var player = Create(decoder, device);
        player.Open(file.Uri);
        player.Play();
        await Until(() => player.State == NativePlaybackState.Ended);
        player.Play();
        await Until(() => decoder.Seeks == 1 && player.State == NativePlaybackState.Ended);
        Assert.Equal(TimeSpan.Zero, decoder.LastSeek);
        Assert.Equal(1, decoder.Opens);
        player.Dispose();
        await player.PendingCleanup.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SeekSerializesWithReadAndDropsPreSeekPcm()
    {
        using var file = new LocalSource();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var decoder = new Decoder
        {
            BeforeRead = call =>
            {
                if (call != 1) return;
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            }
        };
        var device = new Device { BlockSubmissions = true };
        using var player = Create(decoder, device);
        try
        {
            player.Open(file.Uri);
            player.Play();
            await Until(() => entered.IsSet);
            var seek = Task.Run(() => player.Seek(TimeSpan.FromSeconds(5)));
            await Task.Delay(40);
            Assert.Equal(0, decoder.Seeks);
            release.Set();
            await seek.WaitAsync(TimeSpan.FromSeconds(5));
            device.BlockSubmissions = false;
            await Until(() => player.State == NativePlaybackState.Ended);
            Assert.False(decoder.ConcurrentSeek);
            Assert.NotEmpty(device.Accepted);
            Assert.All(device.Accepted, value => Assert.Equal(0.8f, value));
        }
        finally
        {
            release.Set();
            player.Dispose();
            await player.PendingCleanup.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task RetiredBlockedReadDoesNotDisposeLiveDecoderOrTouchReplacement()
    {
        using var file = new LocalSource();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var old = new Decoder { BeforeRead = _ => { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException(); } };
        var next = new Decoder();
        var oldDevice = new Device();
        var nextDevice = new Device();
        using var player = new AudioPlayer(new DecoderFactory(old, next), new DeviceFactory(oldDevice, nextDevice));
        try
        {
            player.Open(file.Uri);
            player.Play();
            await Until(() => entered.IsSet);
            player.Close();
            Assert.Equal(0, old.Disposals);
            player.Open(file.Uri);
            player.Play();
            await Until(() => player.State == NativePlaybackState.Ended);
            release.Set();
            await Until(() => old.Disposals == 1);
            Assert.False(old.DisposedWhileReading);
            Assert.Empty(oldDevice.Accepted);
            Assert.Equal(2, next.Reads);
        }
        finally
        {
            release.Set();
            player.Dispose();
            await player.PendingCleanup.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task LateOldDeviceCompletionDoesNotEndReplacement()
    {
        using var file = new LocalSource();
        var first = new Device();
        var next = new Device();
        using var player = new AudioPlayer(new DecoderFactory(new Decoder(), new Decoder()), new DeviceFactory(first, next));
        player.Open(file.Uri);
        var stale = first.SnapshotCompletion();
        player.Close();
        player.Open(file.Uri);
        stale?.Invoke(first, EventArgs.Empty);
        await Task.Delay(50);
        Assert.Equal(NativePlaybackState.Stopped, player.State);
        player.Dispose();
        await player.PendingCleanup.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task DecodeFailureReportsOnceAndCleanupCompletes()
    {
        using var file = new LocalSource();
        var expected = new InvalidDataException("synthetic audio failure");
        var decoder = new Decoder { BeforeRead = _ => throw expected };
        var device = new Device();
        using var player = Create(decoder, device);
        var failures = new ConcurrentQueue<Exception>();
        player.MediaFailed += (_, e) => failures.Enqueue(e.ErrorException);
        player.Open(file.Uri);
        player.Play();
        await Until(() => !failures.IsEmpty);
        Assert.Same(expected, Assert.Single(failures));
        player.Dispose();
        await player.PendingCleanup.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(decoder.DisposedWhileReading);
    }

    private static AudioPlayer Create(Decoder decoder, Device device) => new(new DecoderFactory(decoder), new DeviceFactory(device));
    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("Audio test condition was not reached.");
            await Task.Delay(5);
        }
    }
    private sealed class DecoderFactory(params Decoder[] values) : INativeAudioDecoderFactory
    {
        private int _next;
        public INativeAudioDecoder Create() => values[Interlocked.Increment(ref _next) - 1];
    }
    private sealed class DeviceFactory(params Device[] values) : INativeAudioDeviceFactory
    {
        private int _next;
        public INativeAudioDevice Create(int sampleRate, int channels) => values[Interlocked.Increment(ref _next) - 1];
    }
    private sealed class Decoder : INativeAudioDecoder
    {
        public Action<int>? BeforeRead;
        public int Opens, Reads, Seeks, Disposals;
        private int _reading;
        private int _remaining = 1;
        private float _value = 0.2f;
        public bool ConcurrentSeek, DisposedWhileReading;
        public TimeSpan LastSeek;
        public int SampleRate => 48000;
        public int Channels => 2;
        public TimeSpan Duration => TimeSpan.FromSeconds(10);
        public SupportedAudioCodec Codec => SupportedAudioCodec.Wav;
        public void Open(string path) => Interlocked.Increment(ref Opens);
        public int ReadFrames(Span<float> dst)
        {
            Interlocked.Increment(ref _reading);
            try
            {
                int call = Interlocked.Increment(ref Reads);
                BeforeRead?.Invoke(call);
                if (_remaining-- <= 0) return 0;
                dst.Fill(_value);
                return dst.Length / Channels;
            }
            finally { Interlocked.Decrement(ref _reading); }
        }
        public void Seek(TimeSpan position)
        {
            ConcurrentSeek |= Volatile.Read(ref _reading) != 0;
            LastSeek = position;
            _remaining = 1;
            _value = 0.8f;
            Interlocked.Increment(ref Seeks);
        }
        public void Dispose()
        {
            DisposedWhileReading |= Volatile.Read(ref _reading) != 0;
            Interlocked.Increment(ref Disposals);
        }
    }
    private sealed class Device : INativeAudioDevice
    {
        public volatile bool BlockSubmissions;
        public ConcurrentQueue<float> Accepted = new();
        public int Disposals;
        public int Submissions;
        public TimeSpan Position => TimeSpan.Zero;
        public event EventHandler? PlaybackEnded;
        public EventHandler? SnapshotCompletion() => PlaybackEnded;
        public void Start() { }
        public void Stop() { }
        public void Flush() { while (Accepted.TryDequeue(out _)) { } }
        public void SetVolume(float value) { }
        public int Submit(ReadOnlySpan<float> pcm)
        {
            if (BlockSubmissions) return 0;
            Accepted.Enqueue(pcm[0]);
            Interlocked.Increment(ref Submissions);
            return pcm.Length / 2;
        }
        public void SignalEndOfStream() => PlaybackEnded?.Invoke(this, EventArgs.Empty);
        public void Dispose() => Interlocked.Increment(ref Disposals);
    }
    private sealed class LocalSource : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"jalium-audio-test-{Guid.NewGuid():N}.wav");
        public LocalSource() => File.WriteAllBytes(_path, []);
        public Uri Uri => new(_path);
        public void Dispose() => File.Delete(_path);
    }
}

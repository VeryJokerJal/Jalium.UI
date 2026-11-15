using System.Buffers;
using System.Collections.Concurrent;
using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Pipeline;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class MediaElementPlaybackSessionTests
{
    [Fact]
    public async Task CancelDuringPreciseDelayDisposesTheDequeuedFrame()
    {
        var pool = new TrackingArrayPool();
        var decoder = new ScriptedDecoder(call => call switch
        {
            1 => (true, Frame(pool, TimeSpan.FromSeconds(5))),
            _ => (false, null),
        });
        var presented = 0;
        using var session = Session(
            generation: 1,
            decoder,
            (_, _) =>
            {
                Interlocked.Increment(ref presented);
                return false;
            });

        session.Start();
        await WaitUntilAsync(
            () => session.FramesAwaitingPresentation == 1,
            "the render worker to own the delayed frame");

        session.Cancel();
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, Volatile.Read(ref presented));
        Assert.Equal(1, pool.ReturnCount);
    }

    [Fact]
    public async Task DecodeExceptionIsReportedOnceAndDoesNotMasqueradeAsEndOfStream()
    {
        var expected = new InvalidDataException("synthetic decode failure");
        var decoder = new ScriptedDecoder(_ => throw expected);
        var failure = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ended = 0;
        using var session = Session(
            generation: 7,
            decoder,
            (_, _) => false,
            failed: (_, exception) => failure.TrySetResult(exception),
            ended: _ => Interlocked.Increment(ref ended));

        session.Start();

        Assert.Same(expected, await failure.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, Volatile.Read(ref ended));
    }

    [Fact]
    public async Task EndOfStreamCompletesTheFrameProducerAndDrainsBeforeMediaEnded()
    {
        var pool = new TrackingArrayPool();
        var decoder = new ScriptedDecoder(call => call switch
        {
            1 => (true, Frame(pool, TimeSpan.Zero)),
            _ => (false, null),
        });
        var presented = 0;
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var session = Session(
            generation: 3,
            decoder,
            (_, frame) =>
            {
                frame.Dispose();
                Interlocked.Increment(ref presented);
                return true;
            },
            ended: _ => ended.TrySetResult());

        session.Start();

        await ended.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, Volatile.Read(ref presented));
        Assert.Equal(1, pool.ReturnCount);
    }

    [Fact]
    public async Task StaleGenerationCannotPublishOrEndTheReplacementSession()
    {
        var oldPool = new TrackingArrayPool();
        var newPool = new TrackingArrayPool();
        using var releaseOldDecoder = new ManualResetEventSlim(false);
        var oldReadEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldDecoder = new ScriptedDecoder(call =>
        {
            if (call != 1) return (false, null);
            oldReadEntered.TrySetResult();
            if (!releaseOldDecoder.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Old decoder was never released.");
            return (true, Frame(oldPool, TimeSpan.Zero));
        });
        var newDecoder = new ScriptedDecoder(call => call switch
        {
            1 => (true, Frame(newPool, TimeSpan.Zero)),
            _ => (false, null),
        });
        var currentGeneration = 1;
        var published = new ConcurrentQueue<int>();
        var acceptedEnded = 0;

        bool Present(MediaElementPlaybackSession owner, VideoFrame frame)
        {
            if (owner.Generation != Volatile.Read(ref currentGeneration))
                return false;
            published.Enqueue(owner.Generation);
            frame.Dispose();
            return true;
        }

        void Ended(MediaElementPlaybackSession owner)
        {
            if (owner.Generation == Volatile.Read(ref currentGeneration))
                Interlocked.Increment(ref acceptedEnded);
        }

        using var oldSession = Session(1, oldDecoder, Present, ended: Ended);
        oldSession.Start();
        await oldReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Volatile.Write(ref currentGeneration, 2);
        using var newSession = Session(2, newDecoder, Present, ended: Ended);
        newSession.Start();
        await newSession.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        releaseOldDecoder.Set();
        await oldSession.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal([2], published.ToArray());
        Assert.Equal(1, Volatile.Read(ref acceptedEnded));
        Assert.Equal(1, oldPool.ReturnCount);
        Assert.Equal(1, newPool.ReturnCount);
        Assert.Equal(2, oldDecoder.ReadCalls);
        Assert.Equal(2, newDecoder.ReadCalls);
    }

    [Fact]
    public void PausedSeekWaitsForTheOldWorkerAndDoesNotResumePlayback()
    {
        RunWithMainDispatcher(() =>
        {
            using var releaseRead = new ManualResetEventSlim(false);
            var readEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var decoder = new ScriptedDecoder(call =>
            {
                if (call != 1) return (false, null);
                readEntered.TrySetResult();
                if (!releaseRead.Wait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("Playback decoder was never released.");
                return (false, null);
            });
            var factory = new SingleDecoderFactory(decoder);
            using var factoryScope = new DecoderFactoryScope(factory);
            using var media = OpenManualMedia(factory);
            media.Measure(new Size(640, 360));
            media.Arrange(new Rect(0, 0, 640, 360));

            media.Play();
            PumpUntil(() => readEntered.Task.IsCompleted, "the playback decoder to enter TryReadFrame");
            media.Pause();
            media.Position = TimeSpan.FromSeconds(5);

            Assert.False(media.IsPlaying);
            Assert.Equal(1, decoder.SeekCount);
            Assert.Equal(TimeSpan.Zero, decoder.LastSeek);
            releaseRead.Set();
            PumpUntil(() => decoder.SeekCount == 2, "the paused seek to run after the old worker exited");

            Assert.False(media.IsPlaying);
            Assert.Equal(TimeSpan.FromSeconds(5), decoder.LastSeek);
            Assert.Equal(1, decoder.ReadCalls);
        });
    }

    [Fact]
    public void ManualUnloadAndSameResolvedSourceDoNotReopenTheDecoder()
    {
        RunWithMainDispatcher(() =>
        {
            var decoder = new ScriptedDecoder(_ => (false, null));
            var factory = new SingleDecoderFactory(decoder);
            using var factoryScope = new DecoderFactoryScope(factory);
            using var media = OpenManualMedia(factory);
            var source = media.Source!;

            media.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent, media));
            typeof(MediaElement)
                .GetMethod("OpenSource", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(media, [source, null]);
            PumpDispatcherFor(TimeSpan.FromMilliseconds(100));

            Assert.Equal(1, factory.CreateCount);
            Assert.Equal(1, decoder.OpenCount);
            Assert.False(media.IsPlaying);
        });
    }

    [Fact]
    public void OpenWithNoDecodableStreamRaisesFailureInsteadOfMediaOpened()
    {
        RunWithMainDispatcher(() =>
        {
            var expected = new InvalidDataException("synthetic invalid video");
            var decoder = new ScriptedDecoder(_ => (false, null), () => throw expected);
            using var factoryScope = new DecoderFactoryScope(new SingleDecoderFactory(decoder));
            using var media = new MediaElement { LoadedBehavior = MediaState.Manual };
            var opened = 0;
            Exception? failure = null;
            media.MediaOpened += (_, _) => opened++;
            media.MediaFailed += (_, e) => failure = e.ErrorException;
            media.Source = new Uri(Path.Combine(Path.GetTempPath(), $"missing-media-{Guid.NewGuid():N}.mp4"));
            PumpUntil(() => failure is not null, "invalid source failure");
            Assert.Same(expected, failure);
            Assert.Equal(0, opened);
            Assert.False(media.IsPlaying);
        });
    }

    [Fact]
    public void PlayDuringAsyncOpenKeepsTheExplicitPlayRequest()
    {
        RunWithMainDispatcher(() =>
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var pool = new TrackingArrayPool();
            var decoder = new ScriptedDecoder(
                call => call == 1 ? (true, Frame(pool, TimeSpan.FromSeconds(5))) : (false, null),
                () => { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException(); });
            using var factoryScope = new DecoderFactoryScope(new SingleDecoderFactory(decoder));
            using var media = new MediaElement { LoadedBehavior = MediaState.Manual };
            try
            {
                media.Measure(new Size(640, 360));
                media.Arrange(new Rect(0, 0, 640, 360));
                media.Source = new Uri(Path.Combine(Path.GetTempPath(), $"pending-media-{Guid.NewGuid():N}.mp4"));
                PumpUntil(() => entered.IsSet, "the asynchronous open");
                media.Play();
                release.Set();
                PumpUntil(() => media.IsPlaying && decoder.ReadCalls > 0, "explicit play after open");
                var closed = media.CloseAsync();
                PumpUntil(() => closed.IsCompleted, "pending-open media cleanup");
                Assert.True(closed.IsCompletedSuccessfully);
            }
            finally { release.Set(); }
        });
    }

    [Fact]
    public void StopDuringAsyncOpenOverridesLoadedPlayBehavior()
    {
        RunWithMainDispatcher(() =>
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var decoder = new ScriptedDecoder(_ => (false, null),
                () => { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException(); });
            using var factoryScope = new DecoderFactoryScope(new SingleDecoderFactory(decoder));
            using var media = new MediaElement { LoadedBehavior = MediaState.Play };
            var opened = false;
            media.MediaOpened += (_, _) => opened = true;
            try
            {
                media.Source = new Uri(Path.Combine(Path.GetTempPath(), $"stop-open-{Guid.NewGuid():N}.mp4"));
                PumpUntil(() => entered.IsSet, "the asynchronous open");
                media.Stop();
                release.Set();
                PumpUntil(() => opened, "MediaOpened after Stop");
                PumpDispatcherFor(TimeSpan.FromMilliseconds(100));
                Assert.False(media.IsPlaying);
                Assert.Equal(0, decoder.ReadCalls);
            }
            finally { release.Set(); }
        });
    }

    [Fact]
    public void CloseAsyncWaitsForTheReadBeforeDisposingItsDecoder()
    {
        RunWithMainDispatcher(() =>
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var decoder = new ScriptedDecoder(_ =>
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
                return (false, null);
            });
            var factory = new SingleDecoderFactory(decoder);
            using var factoryScope = new DecoderFactoryScope(factory);
            using var media = OpenManualMedia(factory);
            try
            {
                media.Measure(new Size(640, 360));
                media.Arrange(new Rect(0, 0, 640, 360));
                media.Play();
                PumpUntil(() => entered.IsSet, "the blocked decoder read");
                var closed = media.CloseAsync();
                Assert.False(closed.IsCompleted);
                Assert.Equal(0, decoder.DisposeCount);
                release.Set();
                PumpUntil(() => closed.IsCompleted, "native playback cleanup");
                Assert.True(closed.IsCompletedSuccessfully);
                Assert.Equal(1, decoder.DisposeCount);
            }
            finally { release.Set(); }
        });
    }

    private static MediaElementPlaybackSession Session(
        int generation,
        INativeVideoDecoder decoder,
        Func<MediaElementPlaybackSession, VideoFrame, bool> present,
        Action<MediaElementPlaybackSession, Exception>? failed = null,
        Action<MediaElementPlaybackSession>? ended = null)
        => new(new MediaElementPlaybackSessionOptions(
            generation,
            decoder,
            ReadAudioPosition: null,
            StartPosition: TimeSpan.Zero,
            FrameDelayMs: 1000.0 / 30.0,
            SpeedRatio: 1.0,
            Duration: TimeSpan.FromMinutes(1),
            GetRenderContextHandle: static () => nint.Zero,
            PresentFrame: present,
            Failed: failed,
            Ended: ended));

    private static MediaFrame Frame(ArrayPool<byte> pool, TimeSpan presentationTime)
        => new(pool, width: 2, height: 2, stride: 8, presentationTime, NativePixelFormat.Bgra8);

    private static MediaElement OpenManualMedia(SingleDecoderFactory factory)
    {
        var media = new MediaElement
        {
            LoadedBehavior = MediaState.Manual,
            UnloadedBehavior = MediaState.Manual,
        };
        var opened = 0;
        media.MediaOpened += (_, _) => Volatile.Write(ref opened, 1);
        media.Source = new Uri(Path.Combine(
            Path.GetTempPath(),
            $"Jalium.MediaElement.{Guid.NewGuid():N}.mp4"));
        PumpUntil(() => Volatile.Read(ref opened) != 0, "MediaElement.MediaOpened");
        Assert.Equal(1, factory.CreateCount);
        return media;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string description)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException($"Timed out waiting for {description}.");
            await Task.Delay(10).ConfigureAwait(false);
        }
    }

    private static void PumpUntil(Func<bool> condition, string description)
    {
        var dispatcher = Dispatcher.GetForCurrentThread();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            dispatcher.ProcessQueue();
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException($"Timed out waiting for {description}.");
            Thread.Sleep(5);
        }
        dispatcher.ProcessQueue();
    }

    private static void PumpDispatcherFor(TimeSpan duration)
    {
        var dispatcher = Dispatcher.GetForCurrentThread();
        var deadline = DateTime.UtcNow + duration;
        do
        {
            dispatcher.ProcessQueue();
            Thread.Sleep(5);
        }
        while (DateTime.UtcNow < deadline);
        dispatcher.ProcessQueue();
    }

    private static void RunWithMainDispatcher(Action action)
    {
        var dispatcher = Dispatcher.GetForCurrentThread();
        var coreType = typeof(MediaElement).Assembly.GetType("Jalium.UI.Threading.DispatcherCore")!;
        var mainField = coreType.GetField("_mainDispatcher", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previousMain = mainField.GetValue(null);
        try
        {
            Dispatcher.SetAsMainThread();
            action();
            dispatcher.ProcessQueue();
        }
        finally
        {
            dispatcher.ProcessQueue();
            mainField.SetValue(null, previousMain);
        }
    }

    private sealed class TrackingArrayPool : ArrayPool<byte>
    {
        private int _returns;
        public int ReturnCount => Volatile.Read(ref _returns);
        public override byte[] Rent(int minimumLength) => new byte[Math.Max(minimumLength, 16)];
        public override void Return(byte[] array, bool clearArray = false) => Interlocked.Increment(ref _returns);
    }

    private sealed class ScriptedDecoder(
        Func<int, (bool HasFrame, MediaFrame? Frame)> read,
        Action? open = null) : INativeVideoDecoder
    {
        private int _readCalls;
        private int _openCount;
        private int _disposeCount;
        private readonly ConcurrentQueue<TimeSpan> _seeks = new();

        public TimeSpan Duration => TimeSpan.FromMinutes(1);
        public double Fps => 30;
        public int Width => 640;
        public int Height => 360;
        public SupportedCodec ActiveVideoCodec => SupportedCodec.H264;
        public int ReadCalls => Volatile.Read(ref _readCalls);
        public int OpenCount => Volatile.Read(ref _openCount);
        public int DisposeCount => Volatile.Read(ref _disposeCount);
        public int SeekCount => _seeks.Count;
        public TimeSpan LastSeek => _seeks.LastOrDefault();

        public void Open(Uri source, NativePixelFormat requestedFormat = NativePixelFormat.Bgra8)
        {
            Interlocked.Increment(ref _openCount);
            open?.Invoke();
        }

        public bool TryReadFrame(out MediaFrame? frame)
        {
            var result = read(Interlocked.Increment(ref _readCalls));
            frame = result.Frame;
            return result.HasFrame;
        }

        public void Seek(TimeSpan position) => _seeks.Enqueue(position);
        public void Dispose() => Interlocked.Increment(ref _disposeCount);
    }

    private sealed class SingleDecoderFactory(ScriptedDecoder decoder) : INativeVideoDecoderFactory
    {
        private int _createCount;
        public int CreateCount => Volatile.Read(ref _createCount);

        public INativeVideoDecoder Create(IMediaFramePool? framePool = null)
        {
            Interlocked.Increment(ref _createCount);
            return decoder;
        }

        public SupportedCodec GetSupportedCodecs() => SupportedCodec.H264;
    }

    private sealed class DecoderFactoryScope : IDisposable
    {
        private static readonly FieldInfo FactoryField = typeof(MediaElement)
            .GetField("s_videoDecoderFactory", BindingFlags.Static | BindingFlags.NonPublic)!;
        private static readonly object FactoryLock = typeof(MediaElement)
            .GetField("s_factoryLock", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null)!;
        private readonly object? _previous;

        public DecoderFactoryScope(INativeVideoDecoderFactory factory)
        {
            lock (FactoryLock)
            {
                _previous = FactoryField.GetValue(null);
                FactoryField.SetValue(null, factory);
            }
        }

        public void Dispose()
        {
            lock (FactoryLock)
            {
                FactoryField.SetValue(null, _previous);
            }
        }
    }
}

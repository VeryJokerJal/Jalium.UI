using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Markup;
using Jalium.UI.Media.Animation;
using Jalium.UI.Media.Imaging;
using WpfD3DImage = Jalium.UI.Interop.D3DImage;
using WriteableBitmap = Jalium.UI.Media.Imaging.WriteableBitmap;
using Jalium.UI.Media.Native;
using Jalium.UI.Media.Pipeline;
using Jalium.UI.Threading;
using System;
using System.Buffers;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Jalium.UI.Controls;

#region 媒体状态枚举

public enum MediaState
{
    Manual = 0,
    Play = 1,
    Close = 2,
    Pause = 3,
    Stop = 4
}

#endregion

#region 媒体信息结构

public sealed class MediaInfo
{
    public bool HasVideo { get; set; }
    public bool HasAudio { get; set; }
    public int VideoWidth { get; set; }
    public int VideoHeight { get; set; }
    public double VideoFps { get; set; }
    public double Duration { get; set; }
    public int AudioSampleRate { get; set; } = 44100;
    public int AudioChannels { get; set; } = 2;
}

#endregion

#region 视频帧缓冲队列

/// <summary>
/// 一帧解码后的视频。<see cref="Pixels"/> 来自底层 <see cref="MediaFrame"/> 的池化 BGRA8 缓冲,
/// 调用方必须 <see cref="Dispose"/> 归还。
/// </summary>
/// <remarks>
/// <para>
/// Stage 0 优化:此前 VideoFrame 自己 <see cref="System.Buffers.ArrayPool{T}"/> 租一份 byte[] 然后
/// 把 MediaFrame.Pixels 拷过来,1080p@30fps 等于无意义地烧 ~240 MB/s 内存带宽。现在 VideoFrame
/// 直接接管 MediaFrame 的所有权,Pixels 转发到底层 buffer,Dispose 时把 MediaFrame 一并归还池。
/// </para>
/// <para>
/// 跨线程生命周期没变 —— 解码线程 new VideoFrame、render 线程 Dispose,中间 MediaFrame 一直活着,
/// 池 buffer 不会被复用,Span 一直安全。
/// </para>
/// </remarks>
public sealed class VideoFrame : IDisposable
{
    private MediaFrame? _mediaFrame;
    private NativeVideoSurface? _gpuSurface;

    public ReadOnlySpan<byte> Pixels =>
        _mediaFrame is null ? ReadOnlySpan<byte>.Empty : _mediaFrame.Pixels.Span;

    public int Width  => _gpuSurface?.PixelWidth  ?? _mediaFrame?.Width  ?? 0;
    public int Height => _gpuSurface?.PixelHeight ?? _mediaFrame?.Height ?? 0;
    public int Stride => _mediaFrame?.Stride ?? 0;
    public long TimestampMs { get; }
    public int FrameIndex { get; }

    /// <summary>
    /// Stage 3+ 路径:DXVA / MediaCodec / VTDecompressionSession 等硬件解码器直接给一个
    /// GPU-resident surface。非 null 时 MediaElement 跳过 BGRA staging 路径,
    /// 直接 bind <see cref="Jalium.UI.Interop.D3DImage"/> 让 framework 走 jalium_render_target_draw_video_surface。
    /// 当前 stage 1+2 decoder 全部返 null,所以这个字段长期为 null;stage 3 真填后自动生效。
    /// </summary>
    public NativeVideoSurface? GpuSurface => _gpuSurface;

    /// <summary>
    /// Transfers ownership of the GPU surface to the display slot.  The
    /// returned surface is no longer disposed with this frame.
    /// </summary>
    internal NativeVideoSurface? DetachGpuSurface()
    {
        var surface = _gpuSurface;
        _gpuSurface = null;
        return surface;
    }

    /// <summary>
    /// CPU 路径:VideoFrame 接管 <paramref name="mediaFrame"/> 的生命周期。
    /// </summary>
    public VideoFrame(MediaFrame mediaFrame, long timestampMs, int frameIndex)
    {
        _mediaFrame = mediaFrame ?? throw new ArgumentNullException(nameof(mediaFrame));
        TimestampMs = timestampMs;
        FrameIndex = frameIndex;
    }

    /// <summary>
    /// GPU 路径:VideoFrame 接管 <paramref name="gpuSurface"/> 的生命周期(Dispose 时归还 decoder 或 release native handle)。
    /// </summary>
    public VideoFrame(NativeVideoSurface gpuSurface, long timestampMs, int frameIndex)
    {
        _gpuSurface = gpuSurface ?? throw new ArgumentNullException(nameof(gpuSurface));
        TimestampMs = timestampMs;
        FrameIndex = frameIndex;
    }

    public void Dispose()
    {
        var mf = _mediaFrame;
        if (mf is not null)
        {
            _mediaFrame = null;
            mf.Dispose();
        }
        var gs = _gpuSurface;
        if (gs is not null)
        {
            _gpuSurface = null;
            gs.Dispose();
        }
    }
}

public sealed class VideoFrameBuffer : IDisposable
{
    private readonly BlockingCollection<VideoFrame> _frameQueue;
    private readonly int _maxSize;
    private int _disposed;

    public VideoFrameBuffer(int maxSize = 3)
    {
        _maxSize = maxSize;
        _frameQueue = new BlockingCollection<VideoFrame>(maxSize);
    }

    public bool TryAdd(VideoFrame frame, int timeoutMs = 0)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (Volatile.Read(ref _disposed) != 0 || _frameQueue.IsAddingCompleted)
            return false;

        if (timeoutMs > 0)
        {
            try
            {
                return _frameQueue.TryAdd(frame, timeoutMs);
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
        try
        {
            _frameQueue.Add(frame);
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public VideoFrame? Take(CancellationToken token)
    {
        try
        {
            return _frameQueue.Take(token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public bool TryTake(out VideoFrame? frame)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            frame = null;
            return false;
        }

        try
        {
            return _frameQueue.TryTake(out frame);
        }
        catch (ObjectDisposedException)
        {
            frame = null;
            return false;
        }
    }

    public void CompleteAdding()
    {
        if (Volatile.Read(ref _disposed) != 0 || _frameQueue.IsAddingCompleted)
            return;

        try { _frameQueue.CompleteAdding(); }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    public void Clear()
    {
        while (_frameQueue.TryTake(out var frame))
        {
            frame?.Dispose();
        }
    }

    public int Count
    {
        get
        {
            if (Volatile.Read(ref _disposed) != 0) return 0;
            try { return _frameQueue.Count; }
            catch (ObjectDisposedException) { return 0; }
        }
    }

    public bool IsCompleted
    {
        get
        {
            if (Volatile.Read(ref _disposed) != 0) return true;
            try { return _frameQueue.IsCompleted; }
            catch (ObjectDisposedException) { return true; }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { _frameQueue.CompleteAdding(); } catch { }
        Clear();
        _frameQueue.Dispose();
    }
}

#endregion

#region 播放会话

/// <summary>
/// Immutable inputs for one MediaElement playback generation.  Every worker loop
/// reads only these captured values; replacing fields on the owning control can no
/// longer redirect an older worker into a newer decoder, frame queue, clock, or
/// audio player.
/// </summary>
internal sealed record MediaElementPlaybackSessionOptions(
    int Generation,
    INativeVideoDecoder? Decoder,
    Func<TimeSpan>? ReadAudioPosition,
    TimeSpan StartPosition,
    double FrameDelayMs,
    double SpeedRatio,
    TimeSpan Duration,
    Func<nint> GetRenderContextHandle,
    Func<MediaElementPlaybackSession, VideoFrame, bool> PresentFrame,
    Action<MediaElementPlaybackSession, TimeSpan>? PositionChanged = null,
    Action<MediaElementPlaybackSession, Exception>? Failed = null,
    Action<MediaElementPlaybackSession>? Ended = null,
    Action<MediaElementPlaybackSession>? StatisticsChanged = null,
    Func<bool>? AudioHasEnded = null);

/// <summary>
/// Owns the decode, render, and audio-position workers for exactly one playback
/// generation.  Cancellation and EOF close the frame producer explicitly, and
/// all frame ownership transfers are paired with a guaranteed Dispose fallback.
/// </summary>
internal sealed class MediaElementPlaybackSession : IDisposable
{
    private readonly MediaElementPlaybackSessionOptions _options;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly VideoFrameBuffer _frames = new(3);
    private readonly AVSyncClock _clock = new();
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _started;
    private int _failed;
    private int _ended;
    private int _cleaned;
    private int _framesAwaitingPresentation;
    private TimeSpan _completedMediaTime;

    internal MediaElementPlaybackSession(MediaElementPlaybackSessionOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (options.Decoder is null && options.ReadAudioPosition is null)
            throw new ArgumentException("A playback session requires video or audio.", nameof(options));
        if (options.FrameDelayMs <= 0 || double.IsNaN(options.FrameDelayMs) || double.IsInfinity(options.FrameDelayMs))
            throw new ArgumentOutOfRangeException(nameof(options), "Frame delay must be finite and positive.");
        ArgumentNullException.ThrowIfNull(options.GetRenderContextHandle);
        ArgumentNullException.ThrowIfNull(options.PresentFrame);
        _clock.SpeedRatio = options.SpeedRatio;
    }

    internal int Generation => _options.Generation;
    internal INativeVideoDecoder? Decoder => _options.Decoder;
    internal Task Completion => _completion.Task;
    internal TimeSpan MediaTime => Volatile.Read(ref _cleaned) != 0 ? _completedMediaTime : _clock.GetMediaTime();
    internal double SpeedRatio
    {
        set => _clock.SpeedRatio = value;
    }
    internal int FramesRendered { get; private set; }
    internal int FramesDropped { get; private set; }
    internal int FramesLate { get; private set; }
    internal int FramesAwaitingPresentation => Volatile.Read(ref _framesAwaitingPresentation);

    internal void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("The playback session has already started.");
        try
        {
            _clock.Start(_options.StartPosition);
            var token = _cancellation.Token;
            var workers = new List<Task>(3);
            if (_options.Decoder is not null)
            {
                workers.Add(Task.Run(() => DecodeLoop(token)));
                workers.Add(Task.Run(() => RenderLoopAsync(token)));
            }

            if (_options.ReadAudioPosition is not null)
            {
                workers.Add(_options.Decoder is null
                    ? Task.Run(() => AudioOnlyPositionLoopAsync(token))
                    : Task.Run(() => AudioSyncLoop(token)));
            }

            _ = CompleteAsync(workers);
        }
        catch
        {
            Cleanup();
            _completion.TrySetResult();
            throw;
        }
    }

    internal void Cancel()
    {
        try { _cancellation.Cancel(); } catch (ObjectDisposedException) { }
        _frames.CompleteAdding();
    }

    public void Dispose()
    {
        Cancel();
        if (Volatile.Read(ref _started) == 0)
        {
            Cleanup();
            _completion.TrySetResult();
        }
    }

    private async Task CompleteAsync(IReadOnlyCollection<Task> workers)
    {
        try
        {
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
        finally
        {
            Cleanup();
            _completion.TrySetResult();
        }
    }

    private void DecodeLoop(CancellationToken token)
    {
        var decoder = _options.Decoder!;
        try
        {
            long frameIndex = 0;
            double startTimeMs = _options.StartPosition.TotalMilliseconds;
            while (!token.IsCancellationRequested)
            {
                VideoFrame? frame = null;
                if (decoder is INativeGpuVideoDecoder gpuDecoder && gpuDecoder.MayHaveGpuOutput)
                {
                    var result = gpuDecoder.TryReadGpuFrame(
                        _options.GetRenderContextHandle(),
                        out var directSurface,
                        out var directPts);
                    if (result == GpuVideoFrameReadResult.EndOfStream)
                        break;
                    if (result == GpuVideoFrameReadResult.FellBackToCpu)
                        continue;
                    if (result == GpuVideoFrameReadResult.Frame)
                    {
                        if (directSurface is null)
                            throw new InvalidDataException("The video decoder reported a GPU frame without a surface.");
                        double timestamp = directPts.TotalMilliseconds;
                        if (timestamp <= 0)
                            timestamp = startTimeMs + frameIndex * _options.FrameDelayMs;
                        frame = new VideoFrame(directSurface, (long)timestamp, checked((int)frameIndex));
                    }
                }

                if (frame is null)
                {
                    if (!decoder.TryReadFrame(out var mediaFrame))
                        break;
                    if (mediaFrame is null)
                        throw new InvalidDataException("The video decoder returned success without a frame.");

                    double pts = mediaFrame.PresentationTime.TotalMilliseconds;
                    if (pts <= 0)
                        pts = startTimeMs + frameIndex * _options.FrameDelayMs;

                    NativeVideoSurface? gpuSurface = null;
                    try
                    {
                        nint context = _options.GetRenderContextHandle();
                        if (context != nint.Zero)
                            gpuSurface = decoder.AcquireGpuSurface(context);
                    }
                    catch
                    {
                        // Optional legacy surface acquisition may fail independently
                        // of CPU decoding; retain the valid CPU frame as fallback.
                    }

                    if (gpuSurface is not null)
                    {
                        mediaFrame.Dispose();
                        frame = new VideoFrame(gpuSurface, (long)pts, checked((int)frameIndex));
                    }
                    else
                    {
                        frame = new VideoFrame(mediaFrame, (long)pts, checked((int)frameIndex));
                    }
                }

                if (token.IsCancellationRequested)
                {
                    frame.Dispose();
                    break;
                }

                if (!_frames.TryAdd(frame, 50))
                {
                    if (_frames.TryTake(out var oldFrame))
                        oldFrame?.Dispose();
                    if (!_frames.TryAdd(frame))
                        frame.Dispose();
                }

                frameIndex++;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
        finally
        {
            _frames.CompleteAdding();
        }
    }

    private async Task RenderLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                VideoFrame? frame = _frames.Take(token);
                if (frame is null)
                {
                    if (token.IsCancellationRequested)
                        return;
                    if (_frames.IsCompleted)
                        break;
                    continue;
                }

                bool transferred = false;
                Interlocked.Increment(ref _framesAwaitingPresentation);
                try
                {
                    TimeSpan delay = _clock.CalculateVideoDelay(frame.TimestampMs);
                    if (delay < TimeSpan.FromMilliseconds(-AVSyncClock.VideoCatchUpThresholdMs))
                    {
                        FramesDropped++;
                        NotifyStatisticsChanged();
                        continue;
                    }

                    if (delay < TimeSpan.Zero)
                    {
                        FramesLate++;
                        NotifyStatisticsChanged();
                    }
                    else if (delay > TimeSpan.FromMilliseconds(2))
                    {
                        do
                        {
                            await PreciseDelayAsync(TimeSpan.FromMilliseconds(Math.Min(10, delay.TotalMilliseconds)), token).ConfigureAwait(false);
                            delay = _clock.CalculateVideoDelay(frame.TimestampMs);
                        } while (delay > TimeSpan.FromMilliseconds(2));
                    }

                    token.ThrowIfCancellationRequested();
                    TimeSpan position = _clock.GetMediaTime();
                    _options.PositionChanged?.Invoke(this, position);
                    FramesRendered++;
                    NotifyStatisticsChanged();
                    transferred = _options.PresentFrame(this, frame);
                }
                finally
                {
                    Interlocked.Decrement(ref _framesAwaitingPresentation);
                    if (!transferred)
                        frame.Dispose();
                }
            }

            while (!token.IsCancellationRequested && _options.AudioHasEnded is { } ended && !ended())
                await Task.Delay(20, token).ConfigureAwait(false);
            if (!token.IsCancellationRequested && Volatile.Read(ref _failed) == 0)
                SignalEnded();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private void AudioSyncLoop(CancellationToken token)
    {
        try
        {
            if (token.WaitHandle.WaitOne(50)) return;
            while (!token.IsCancellationRequested)
            {
                if (_options.AudioHasEnded?.Invoke() == true) return;
                TimeSpan position = _options.ReadAudioPosition!();
                _clock.UpdateAudioPosition(position.TotalSeconds);
                _options.PositionChanged?.Invoke(this, position);
                if (token.WaitHandle.WaitOne(20)) break;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private async Task AudioOnlyPositionLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                TimeSpan position = _options.ReadAudioPosition!();
                _options.PositionChanged?.Invoke(this, position);
                if (_options.AudioHasEnded?.Invoke() == true ||
                    (_options.AudioHasEnded is null && _options.Duration > TimeSpan.Zero && position >= _options.Duration))
                {
                    SignalEnded();
                    return;
                }
                await Task.Delay(100, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private void Fail(Exception exception)
    {
        if (Interlocked.Exchange(ref _failed, 1) != 0) return;
        try { _options.Failed?.Invoke(this, exception); }
        catch { }
        Cancel();
    }

    private void SignalEnded()
    {
        if (Interlocked.Exchange(ref _ended, 1) != 0 || Volatile.Read(ref _failed) != 0)
            return;
        try { _options.Ended?.Invoke(this); }
        catch { }
        Cancel();
    }

    private void NotifyStatisticsChanged()
    {
        try { _options.StatisticsChanged?.Invoke(this); } catch { }
    }

    private void Cleanup()
    {
        _completedMediaTime = _clock.GetMediaTime();
        if (Interlocked.Exchange(ref _cleaned, 1) != 0) return;
        _frames.Dispose();
        _clock.Dispose();
        _cancellation.Dispose();
    }

    internal static async Task PreciseDelayAsync(TimeSpan delay, CancellationToken token)
    {
        if (delay <= TimeSpan.Zero) return;

        // Short cancellable slices let the caller re-read the A/V clock after
        // rate changes without burning a CPU core in a per-frame spin loop.
        await Task.Delay(delay, token).ConfigureAwait(false);
    }
}

#endregion

#region 音频流信息

public sealed class AudioStreamInfo
{
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public int BitsPerSample { get; set; }
}

#endregion


#region 同步时钟

public sealed class AVSyncClock : IDisposable
{
    // 使用高精度计时器
    private readonly Stopwatch _systemClock;
    private TimeSpan _baseMediaTime;
    private double _speedRatio = 1.0;
    private readonly object _lock = new();
    private bool _isRunning;
    private double _lastAudioPosition;

    // 音视频同步阈值（40ms 以内人耳无法感知）
    public const double MaxAllowedDriftMs = 40;
    public const double VideoCatchUpThresholdMs = 80;

    public AVSyncClock()
    {
        _systemClock = new Stopwatch();
    }

    public void UpdateAudioPosition(double positionSeconds)
    {
        lock (_lock)
        {
            _lastAudioPosition = positionSeconds;
            // 当音频位置领先时钟时，微调时钟基准
            var clockTime = GetMediaTime().TotalSeconds;
            var drift = positionSeconds - clockTime;

            // 如果漂移超过阈值，调整时钟基准
            if (Math.Abs(drift) > MaxAllowedDriftMs / 1000.0)
            {
                _baseMediaTime = TimeSpan.FromSeconds(positionSeconds);
                if (_isRunning)
                {
                    _systemClock.Restart();
                }
            }
        }
    }

    public void Start(TimeSpan startPosition)
    {
        lock (_lock)
        {
            _baseMediaTime = startPosition;
            _systemClock.Restart();
            _isRunning = true;
        }
    }

    public void Pause()
    {
        lock (_lock)
        {
            if (_isRunning)
            {
                // 暂停时，将已运行时间累加到基准时间
                _baseMediaTime += TimeSpan.FromTicks((long)(_systemClock.Elapsed.Ticks * _speedRatio));
                _systemClock.Stop();
                _isRunning = false;
            }
        }
    }

    public void Resume()
    {
        lock (_lock)
        {
            if (!_isRunning)
            {
                _systemClock.Restart();
                _isRunning = true;
            }
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _systemClock.Stop();
            _systemClock.Reset();
            _baseMediaTime = TimeSpan.Zero;
            _isRunning = false;
        }
    }

    public TimeSpan GetMediaTime()
    {
        lock (_lock)
        {
            if (!_isRunning)
                return _baseMediaTime;

            var elapsed = TimeSpan.FromTicks((long)(_systemClock.Elapsed.Ticks * _speedRatio));
            return _baseMediaTime + elapsed;
        }
    }

    public double SpeedRatio
    {
        get { lock (_lock) return _speedRatio; }
        set
        {
            if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
            lock (_lock)
            {
                if (_isRunning)
                {
                    _baseMediaTime = GetMediaTime();
                    _systemClock.Restart();
                }
                _speedRatio = Math.Clamp(value, 0.1, 10.0);
            }
        }
    }

    public TimeSpan CalculateVideoDelay(double framePresentationTimeMs)
    {
        lock (_lock)
        {
            var delay = framePresentationTimeMs - GetMediaTime().TotalMilliseconds;
            return TimeSpan.FromMilliseconds(delay / _speedRatio);
        }
    }

    public bool ShouldDropFrame(double framePresentationTimeMs)
    {
        var currentTime = GetMediaTime().TotalMilliseconds;
        var drift = currentTime - framePresentationTimeMs;
        return drift > VideoCatchUpThresholdMs;
    }

    public void Dispose()
    {
        Stop();
    }
}

#endregion

#region MediaElement 控件

public class MediaElement : FrameworkElement, IDisposable, IUriContext
{
    #region 私有字段

    private static INativeVideoDecoderFactory? s_videoDecoderFactory;
    private static readonly object s_factoryLock = new();

    /// <summary>
    /// 注入自定义 <see cref="INativeVideoDecoderFactory"/>。MediaAppBuilderExtensions
    /// 在注册原生媒体管道时自动调用；测试可手动设置 mock 工厂。
    /// </summary>
    public static void SetVideoDecoderFactory(INativeVideoDecoderFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        lock (s_factoryLock)
        {
            s_videoDecoderFactory = factory;
        }
    }

    private static INativeVideoDecoderFactory GetVideoDecoderFactory()
    {
        var f = Volatile.Read(ref s_videoDecoderFactory);
        if (f is not null) return f;
        lock (s_factoryLock)
        {
            s_videoDecoderFactory ??= new NativeVideoDecoderFactory();
            return s_videoDecoderFactory;
        }
    }

    private INativeVideoDecoder? _videoDecoder;
    private AudioPlayer? _audioManager;
    private string? _mediaPath;
    private Uri? _openedSource;
    private MediaClock? _clock;
    private Uri? _baseUri;
    private double _downloadProgress;
    private double _bufferingProgress;
    private TimeSpan _position;
    private TimeSpan _duration;
    private bool _isPlaying;
    private bool _isPaused;
    private bool _hasVideo;
    private bool _hasAudio;
    private MediaElementPlaybackSession? _playbackSession;
    private Task _playbackTail = Task.CompletedTask;
    private Task _resourceCleanupTail = Task.CompletedTask;
    private Task _openTask = Task.CompletedTask;
    private bool _isOpening;
    private bool _replayOnPlay;
    private MediaState? _requestedOpenState;
    private int _playbackGeneration;
    private int _pendingPlaybackStartGeneration;
    private CancellationTokenSource? _subtitleCts;
    private Task? _subtitleTask;
    private string? _subtitleText;
    private IReadOnlyList<MediaTrackInfo> _audioTracks = Array.Empty<MediaTrackInfo>();
    private IReadOnlyList<MediaTrackInfo> _subtitleTracks = Array.Empty<MediaTrackInfo>();
    private int _selectedAudioTrackIndex;
    private int _selectedSubtitleTrackIndex = -1;
    private int _trackSwitchGeneration;
    private bool _disposed;
    private readonly object _lock = new();

    private WriteableBitmap? _frameBitmap;
    // Stage 1+2 video pipeline:when the active backend supports video surfaces
    // (Software done; D3D12 / Vulkan land in stage 2+), MediaElement uploads frames
    // directly into _videoSurface and renders via _d3dImage, bypassing the
    // WriteableBitmap.BackBuffer copy hop. Falls back to _frameBitmap when
    // CreateVideoSurface returns null / throws (e.g. backend stub).
    private NativeVideoSurface? _videoSurface;
    private WpfD3DImage? _d3dImage;
    private bool _videoSurfacePathUnsupported;  // sticky:once create fails, don't keep retrying
    private readonly SolidColorBrush _backgroundBrush = new(Color.FromRgb(0, 0, 0));

    // OnRender 里这几处颜色都是编译期常量。字幕叠加层随视频每帧重跑（30~60fps），
    // 每帧新建画刷会往渲染后端那份按实例身份键控的原生画刷缓存里塞新条目并触发 LRU 淘汰。
    private static readonly SolidColorBrush s_placeholderTextBrush = new(Color.FromRgb(200, 200, 200));
    private static readonly SolidColorBrush s_subtitleForegroundBrush = new(Color.FromRgb(255, 255, 255));
    private static readonly SolidColorBrush s_subtitleBackgroundBrush = new(Color.FromArgb(180, 0, 0, 0));
    private int _videoWidth;
    private int _videoHeight;
    private double _videoFps;
    private double _frameDelayMs;

    private double _currentVolume = 1;
    private bool _isMuted;

    private double _displayScale = 1.0;
    private Size _arrangedSize;
    private bool _isArranged;
    private bool _pendingPlay;

    private int _framesRendered;
    private int _framesDropped;
    private int _framesLate;

    private int _targetWidth;
    private int _targetHeight;

    // 记录视频应该从什么时间戳开始解码（解决多次暂停后的时间戳漂移）
    private double _videoStartTimeMs;

    // 异步 OpenMedia 的代次序号 — 快速切换 Source 时让旧的后台探测完成回调直接作废，
    // 避免旧文件的元数据覆盖新文件的状态。
    private int _openMediaGeneration;

    // 解码线程将最新一帧 atomic 换入此槽位；UI 线程（OnRender 时）原子换出并归还 ArrayPool。
    // 不再走 Dispatcher.BeginInvoke 闭包传 frame — 否则 UI 线程偶尔卡顿就会让 8MB×N 帧
    // 堆积在 Dispatcher 队列里，100MB 视频可在数秒内膨胀到 1GB+。
    private VideoFrame? _pendingDisplayFrame;

    // 0 = 没有 pending 的 InvalidateVisual；1 = 已经 BeginInvoke 一个，重复触发被吞掉。
    // 解决高频 frame ready 时 InvalidateVisual delegate 在 Dispatcher 队列里堆积的问题。
    private int _renderRequestPending;

    #endregion

    #region 依赖属性

    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(nameof(Source), typeof(Uri), typeof(MediaElement),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
                OnSourceChanged));

    public static readonly DependencyProperty VolumeProperty =
        DependencyProperty.Register(nameof(Volume), typeof(double), typeof(MediaElement),
            new PropertyMetadata(1.0, OnVolumeChanged, CoerceVolume));

    public static readonly DependencyProperty BalanceProperty =
        DependencyProperty.Register(nameof(Balance), typeof(double), typeof(MediaElement),
            new PropertyMetadata(0.0, OnBalanceChanged, CoerceBalance));

    public static readonly DependencyProperty IsMutedProperty =
        DependencyProperty.Register(nameof(IsMuted), typeof(bool), typeof(MediaElement),
            new PropertyMetadata(false, OnIsMutedChanged));

    public static readonly DependencyProperty ScrubbingEnabledProperty =
        DependencyProperty.Register(nameof(ScrubbingEnabled), typeof(bool), typeof(MediaElement),
            new PropertyMetadata(false));

    public static readonly DependencyProperty StretchProperty =
        DependencyProperty.Register(nameof(Stretch), typeof(Stretch), typeof(MediaElement),
            new PropertyMetadata(Stretch.Uniform, OnStretchChanged));

    public static readonly DependencyProperty StretchDirectionProperty =
        DependencyProperty.Register(nameof(StretchDirection), typeof(StretchDirection), typeof(MediaElement),
            new PropertyMetadata(StretchDirection.Both, OnStretchChanged));

    public static readonly DependencyProperty LoadedBehaviorProperty =
        DependencyProperty.Register(nameof(LoadedBehavior), typeof(MediaState), typeof(MediaElement),
            new PropertyMetadata(MediaState.Play, OnLoadedBehaviorChanged));

    public static readonly DependencyProperty UnloadedBehaviorProperty =
        DependencyProperty.Register(nameof(UnloadedBehavior), typeof(MediaState), typeof(MediaElement),
            new PropertyMetadata(MediaState.Close));

    public static readonly DependencyProperty SpeedRatioProperty =
        DependencyProperty.Register(nameof(SpeedRatio), typeof(double), typeof(MediaElement),
            new PropertyMetadata(1.0, OnSpeedRatioChanged, CoerceSpeedRatio));

    #endregion

    #region 路由事件

    public static readonly RoutedEvent MediaOpenedEvent =
        EventManager.RegisterRoutedEvent(nameof(MediaOpened), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(MediaElement));

    public static readonly RoutedEvent MediaEndedEvent =
        EventManager.RegisterRoutedEvent(nameof(MediaEnded), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(MediaElement));

    public static readonly RoutedEvent MediaFailedEvent =
        EventManager.RegisterRoutedEvent(nameof(MediaFailed), RoutingStrategy.Bubble,
            typeof(EventHandler<ExceptionRoutedEventArgs>), typeof(MediaElement));

    public static readonly RoutedEvent BufferingStartedEvent =
        EventManager.RegisterRoutedEvent(nameof(BufferingStarted), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(MediaElement));

    public static readonly RoutedEvent BufferingEndedEvent =
        EventManager.RegisterRoutedEvent(nameof(BufferingEnded), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(MediaElement));

    public static readonly RoutedEvent ScriptCommandEvent =
        EventManager.RegisterRoutedEvent(nameof(ScriptCommand), RoutingStrategy.Bubble,
            typeof(EventHandler<MediaScriptCommandRoutedEventArgs>), typeof(MediaElement));

    public event RoutedEventHandler? MediaOpened
    {
        add => AddHandler(MediaOpenedEvent, value!);
        remove => RemoveHandler(MediaOpenedEvent, value!);
    }

    public event RoutedEventHandler? MediaEnded
    {
        add => AddHandler(MediaEndedEvent, value!);
        remove => RemoveHandler(MediaEndedEvent, value!);
    }

    public event EventHandler<ExceptionRoutedEventArgs>? MediaFailed
    {
        add => AddHandler(MediaFailedEvent, value!);
        remove => RemoveHandler(MediaFailedEvent, value!);
    }

    public event RoutedEventHandler? BufferingStarted
    {
        add => AddHandler(BufferingStartedEvent, value!);
        remove => RemoveHandler(BufferingStartedEvent, value!);
    }

    public event RoutedEventHandler? BufferingEnded
    {
        add => AddHandler(BufferingEndedEvent, value!);
        remove => RemoveHandler(BufferingEndedEvent, value!);
    }

    public event EventHandler<MediaScriptCommandRoutedEventArgs>? ScriptCommand
    {
        add => AddHandler(ScriptCommandEvent, value!);
        remove => RemoveHandler(ScriptCommandEvent, value!);
    }

    /// <summary>Raises the <see cref="BufferingStarted"/> event. For internal use by media backends.</summary>
    protected virtual void OnBufferingStarted()
    {
        IsBuffering = true;
        Volatile.Write(ref _bufferingProgress, 0.0);
        RaiseEvent(new RoutedEventArgs(BufferingStartedEvent, this));
    }

    /// <summary>Raises the <see cref="BufferingEnded"/> event. For internal use by media backends.</summary>
    protected virtual void OnBufferingEnded()
    {
        IsBuffering = false;
        Volatile.Write(ref _bufferingProgress, 1.0);
        RaiseEvent(new RoutedEventArgs(BufferingEndedEvent, this));
    }

    /// <summary>Raises the <see cref="ScriptCommand"/> event. For internal use by media backends.</summary>
    protected virtual void OnScriptCommand(MediaScriptCommandEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        RaiseEvent(new MediaScriptCommandRoutedEventArgs(
            ScriptCommandEvent,
            this,
            e.ParameterType,
            e.ParameterValue));
    }

    #endregion

    #region CLR 属性

    public Uri? Source
    {
        get => (Uri?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>Gets or sets the media clock that controls playback.</summary>
    public MediaClock? Clock
    {
        get => _clock;
        set => SetClock(value);
    }

    Uri? IUriContext.BaseUri
    {
        get => _baseUri;
        set => SetBaseUri(value);
    }

    public double Volume
    {
        get => (double)GetValue(VolumeProperty)!;
        set => SetValue(VolumeProperty, value);
    }

    public double Balance
    {
        get => (double)GetValue(BalanceProperty)!;
        set => SetValue(BalanceProperty, value);
    }

    public bool IsMuted
    {
        get => (bool)GetValue(IsMutedProperty)!;
        set => SetValue(IsMutedProperty, value);
    }

    public bool ScrubbingEnabled
    {
        get => (bool)GetValue(ScrubbingEnabledProperty)!;
        set => SetValue(ScrubbingEnabledProperty, value);
    }

    public Stretch Stretch
    {
        get => (Stretch)GetValue(StretchProperty)!;
        set => SetValue(StretchProperty, value);
    }

    public StretchDirection StretchDirection
    {
        get => (StretchDirection)GetValue(StretchDirectionProperty)!;
        set => SetValue(StretchDirectionProperty, value);
    }

    public MediaState LoadedBehavior
    {
        get => (MediaState)GetValue(LoadedBehaviorProperty)!;
        set => SetValue(LoadedBehaviorProperty, value);
    }

    public MediaState UnloadedBehavior
    {
        get => (MediaState)GetValue(UnloadedBehaviorProperty)!;
        set => SetValue(UnloadedBehaviorProperty, value);
    }

    public double SpeedRatio
    {
        get => (double)GetValue(SpeedRatioProperty)!;
        set => SetValue(SpeedRatioProperty, value);
    }

    public TimeSpan Position
    {
        get => _position;
        set
        {
            if (_position != value)
            {
                SeekToPosition(value);
            }
        }
    }

    public Jalium.UI.Duration NaturalDuration => _duration == TimeSpan.Zero
        ? Jalium.UI.Duration.Automatic
        : new Jalium.UI.Duration(_duration);

    public int NaturalVideoWidth => _videoWidth;
    public int NaturalVideoHeight => _videoHeight;
    public bool HasAudio => _hasAudio;
    public bool HasVideo => _hasVideo;
    public double BufferingProgress => Volatile.Read(ref _bufferingProgress);
    public double DownloadProgress => Volatile.Read(ref _downloadProgress);
    public bool IsBuffering { get; private set; }
    public bool CanPause { get; private set; } = true;
    public bool IsPlaying => _isPlaying;
    public string SyncStats => $"Frames: {_framesRendered} Dropped: {_framesDropped} Late: {_framesLate}";
    public IReadOnlyList<MediaTrackInfo> AudioTracks => _audioTracks;
    public IReadOnlyList<MediaTrackInfo> SubtitleTracks => _subtitleTracks;
    public Exception? TrackDiscoveryError { get; private set; }
    public Exception? SubtitleError { get; private set; }

    /// <summary>Gets or selects the zero-based embedded audio stream.</summary>
    public int SelectedAudioTrackIndex
    {
        get => _selectedAudioTrackIndex;
        set
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
            if (_audioTracks.Count > 0 && value >= _audioTracks.Count)
                throw new ArgumentOutOfRangeException(nameof(value));
            if (_selectedAudioTrackIndex == value) return;
            var previousTrack = _selectedAudioTrackIndex;
            _selectedAudioTrackIndex = value;
            SwitchAudioTrack(previousTrack);
        }
    }

    /// <summary>
    /// Gets or selects the zero-based embedded subtitle stream; -1 disables
    /// subtitle rendering.
    /// </summary>
    public int SelectedSubtitleTrackIndex
    {
        get => _selectedSubtitleTrackIndex;
        set
        {
            if (value < -1) throw new ArgumentOutOfRangeException(nameof(value));
            if (value >= _subtitleTracks.Count && value != -1)
                throw new ArgumentOutOfRangeException(nameof(value));
            if (_selectedSubtitleTrackIndex == value) return;
            _selectedSubtitleTrackIndex = value;
            RestartSubtitlePlayback();
        }
    }

    #endregion

    #region 构造函数与析构函数

    public MediaElement()
    {
        Unloaded += OnUnloaded;
        _audioManager = new AudioPlayer();
    }

    /// <inheritdoc />
    protected override AutomationPeer? OnCreateAutomationPeer() =>
        new MediaElementAutomationPeer(this);

    ~MediaElement()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;
        ++_openMediaGeneration;
        ++_trackSwitchGeneration;
        DetachClock(_clock);
        _clock = null;
        var playbackTail = StopPlayback();
        DropPendingDisplayFrame();
        var audioManager = _audioManager;
        _audioManager = null;
        DisposeAfterPlayback(playbackTail, audioManager);
        _d3dImage?.SetBackBuffer((NativeVideoSurface?)null);
        _d3dImage?.Dispose();
        _videoSurface?.Dispose();
        _openedSource = null;
        _d3dImage = null;
        _videoSurface = null;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ApplyUnloadedBehavior();
    }

    #endregion

    #region 公共方法

    public void Play()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_isOpening) { _requestedOpenState = MediaState.Play; return; }
        if (_isPlaying) return;
        if (!_hasVideo && !_hasAudio && _clock is null)
        {
            if (Source is { } source)
            {
                OpenSource(source, _baseUri);
                if (_isOpening) _requestedOpenState = MediaState.Play;
            }
            return;
        }

        if (_replayOnPlay)
        {
            _position = TimeSpan.Zero;
            _replayOnPlay = false;
        }

        if (_isPaused)
        {
            ResumePlayback();
        }
        else
        {
            _isPlaying = true;
            StartPlaybackInternal();
        }
    }

    public void Pause()
    {
        if (_isOpening) { _requestedOpenState = MediaState.Pause; return; }
        if (!_isPlaying || _isPaused) return;

        _isPlaying = false;
        _isPaused = true;
        PausePlayback();
    }

    public void Stop()
    {
        if (_isOpening) _requestedOpenState = MediaState.Stop;
        _replayOnPlay = false;
        _isPlaying = false;
        _isPaused = false;
        _pendingPlay = false;
        StopPlayback();
        _position = TimeSpan.Zero;
        _videoStartTimeMs = 0;
        InvalidateVisual();
    }

    public void Close()
    {
        _isPlaying = false;
        _isPaused = false;
        _pendingPlay = false;
        CloseMedia();
    }

    /// <summary>Closes media and waits until its native decoders, audio pumps and pending opens are retired.</summary>
    public async Task CloseAsync()
    {
        Close();
        Task opening;
        lock (_lock) opening = _openTask;
        await opening.ConfigureAwait(false);
        Task cleanup;
        lock (_lock) cleanup = Task.WhenAll(_playbackTail, _resourceCleanupTail);
        await cleanup.ConfigureAwait(false);
    }

    #endregion

    #region 布局与渲染

    protected override Size MeasureOverride(Size availableSize)
    {
        if ((!_hasVideo && !_isOpening) || _videoWidth <= 0 || _videoHeight <= 0)
        {
            return new Size(0, 0);
        }

        var naturalSize = new Size(_videoWidth, _videoHeight);
        return ComputeScaledSize(availableSize, naturalSize);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _arrangedSize = finalSize;
        _isArranged = finalSize.Width > 0 && finalSize.Height > 0;

        UpdateDisplayScale(finalSize);

        if (_isArranged && _pendingPlay)
        {
            _pendingPlay = false;
            Dispatcher.MainDispatcher?.BeginInvoke(() =>
            {
                if (_isPlaying)
                {
                    StartPlaybackWithSize(_arrangedSize);
                }
            });
        }

        return finalSize;
    }

    private Size ComputeScaledSize(Size availableSize, Size contentSize)
    {
        if (contentSize.Width <= 0 || contentSize.Height <= 0)
            return new Size(0, 0);

        var scaleX = 1.0;
        var scaleY = 1.0;

        var isWidthInfinite = double.IsInfinity(availableSize.Width);
        var isHeightInfinite = double.IsInfinity(availableSize.Height);

        if (Stretch != Stretch.None && (!isWidthInfinite || !isHeightInfinite))
        {
            scaleX = availableSize.Width / contentSize.Width;
            scaleY = availableSize.Height / contentSize.Height;

            if (isWidthInfinite) scaleX = scaleY;
            else if (isHeightInfinite) scaleY = scaleX;
            else
            {
                switch (Stretch)
                {
                    case Stretch.Uniform:
                        scaleX = scaleY = Math.Min(scaleX, scaleY);
                        break;
                    case Stretch.UniformToFill:
                        scaleX = scaleY = Math.Max(scaleX, scaleY);
                        break;
                }
            }

            switch (StretchDirection)
            {
                case StretchDirection.UpOnly:
                    scaleX = Math.Max(1.0, scaleX);
                    scaleY = Math.Max(1.0, scaleY);
                    break;
                case StretchDirection.DownOnly:
                    scaleX = Math.Min(1.0, scaleX);
                    scaleY = Math.Min(1.0, scaleY);
                    break;
            }
        }

        scaleX = Math.Max(0.01, scaleX);
        scaleY = Math.Max(0.01, scaleY);

        return new Size(contentSize.Width * scaleX, contentSize.Height * scaleY);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var dc = drawingContext;

        var rect = new Rect(RenderSize);

        if (!_hasVideo && _hasAudio)
        {
            dc.DrawRectangle(_backgroundBrush, null, rect);

            var text = new FormattedText("Audio Only", "Segoe UI", 14)
            {
                Foreground = s_placeholderTextBrush
            };
            dc.DrawText(text, new Point((rect.Width - text.Width) / 2, (rect.Height - text.Height) / 2));
            return;
        }

        dc.DrawRectangle(_backgroundBrush, null, rect);

        // Stage 1+2 fast path:D3DImage wraps a NativeVideoSurface and the
        // framework dispatches DrawImage(D3DImage) straight to
        // jalium_render_target_draw_video_surface — no WriteableBitmap, no
        // managed back buffer copy.
        var surfaceImage = _d3dImage;
        if (surfaceImage != null && surfaceImage.IsFrontBufferAvailable && surfaceImage.NativeHandle != nint.Zero && _hasVideo)
        {
            var frameSize = new Size(surfaceImage.PixelWidth, surfaceImage.PixelHeight);
            var scaledSize = ComputeScaledSize(rect.Size, frameSize);
            if (scaledSize.Width > 0 && scaledSize.Height > 0)
            {
                var x = (rect.Width - scaledSize.Width) / 2;
                var y = (rect.Height - scaledSize.Height) / 2;
                dc.DrawImage(surfaceImage, new Rect(x, y, scaledSize.Width, scaledSize.Height), BitmapScalingMode.Linear);
            }
            DrawSubtitleOverlay(dc, rect);
            return;
        }

        var bitmap = _frameBitmap;
        if (bitmap != null && _hasVideo)
        {
            var frameSize = new Size(bitmap.Width, bitmap.Height);
            var scaledSize = ComputeScaledSize(rect.Size, frameSize);

            if (scaledSize.Width > 0 && scaledSize.Height > 0)
            {
                var x = (rect.Width - scaledSize.Width) / 2;
                var y = (rect.Height - scaledSize.Height) / 2;

                // 视频帧每帧 ContentRevision 自增 — Linear 采样比 HighQuality 便宜得多，
                // 在 30+fps 重绘时是关键热路径（HighQuality 默认走各向异性 + mipmap）。
                dc.DrawImage(bitmap, new Rect(x, y, scaledSize.Width, scaledSize.Height), BitmapScalingMode.Linear);
            }
        }
        else if (_hasVideo)
        {
            var text = new FormattedText("Loading...", "Segoe UI", 14)
            {
                Foreground = s_placeholderTextBrush
            };
            dc.DrawText(text, new Point((rect.Width - text.Width) / 2, (rect.Height - text.Height) / 2));
        }
        DrawSubtitleOverlay(dc, rect);
    }

    private void DrawSubtitleOverlay(DrawingContext drawingContext, Rect bounds)
    {
        var subtitle = _subtitleText;
        if (string.IsNullOrWhiteSpace(subtitle) || bounds.Width <= 40 || bounds.Height <= 20)
            return;

        var text = new FormattedText(subtitle, "Sans", 24)
        {
            Foreground = s_subtitleForegroundBrush,
            MaxTextWidth = Math.Max(1, bounds.Width - 48),
            TextAlignment = TextAlignment.Center,
        };
        var x = 24.0;
        var y = Math.Max(8.0, bounds.Height - text.Height - 32.0);
        var backgroundWidth = Math.Min(bounds.Width - 32.0, text.Width + 16.0);
        var background = new Rect(
            (bounds.Width - backgroundWidth) / 2.0,
            y - 6.0,
            backgroundWidth,
            text.Height + 12.0);
        drawingContext.DrawRectangle(s_subtitleBackgroundBrush, null, background);
        drawingContext.DrawText(text, new Point(x, y));
    }

    #endregion

    #region 属性变更处理

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MediaElement media)
        {
            media.OnSourceChanged((Uri?)e.OldValue, (Uri?)e.NewValue);
        }
    }

    private void OnSourceChanged(Uri? oldSource, Uri? newSource)
    {
        if (_clock != null)
        {
            throw new InvalidOperationException("Source cannot be changed while Clock is set.");
        }

        if (newSource != null)
        {
            OpenSource(newSource, _baseUri);
        }
        else
        {
            _mediaPath = null;
            CloseMedia();
        }
    }

    private void SetBaseUri(Uri? value)
    {
        if (Equals(_baseUri, value))
        {
            return;
        }

        _baseUri = value;

        if (_clock?.Timeline.Source is { IsAbsoluteUri: false } clockSource)
        {
            var timelineBaseUri = ((IUriContext)_clock.Timeline).BaseUri;
            if (timelineBaseUri == null)
            {
                OpenSource(clockSource, _baseUri);
            }
        }
        else if (_clock == null && Source is { IsAbsoluteUri: false } source)
        {
            OpenSource(source, _baseUri);
        }
    }

    private void SetClock(MediaClock? value)
    {
        if (ReferenceEquals(_clock, value))
        {
            return;
        }

        DetachClock(_clock);
        CloseMedia();
        _mediaPath = null;
        _clock = value;
        AttachClock(value);

        if (value?.Timeline.Source is { } clockSource)
        {
            var timelineBaseUri = ((IUriContext)value.Timeline).BaseUri;
            OpenSource(clockSource, timelineBaseUri ?? _baseUri);
        }
        else if (value == null && Source is { } source)
        {
            OpenSource(source, _baseUri);
        }
        else if (value != null)
        {
            ApplyClockState();
        }
    }

    private void AttachClock(MediaClock? clock)
    {
        if (clock == null)
        {
            return;
        }

        clock.CurrentStateInvalidated += OnClockStateInvalidated;
        clock.CurrentTimeInvalidated += OnClockTimeInvalidated;
        clock.CurrentGlobalSpeedInvalidated += OnClockGlobalSpeedInvalidated;
    }

    private void DetachClock(MediaClock? clock)
    {
        if (clock == null)
        {
            return;
        }

        clock.CurrentStateInvalidated -= OnClockStateInvalidated;
        clock.CurrentTimeInvalidated -= OnClockTimeInvalidated;
        clock.CurrentGlobalSpeedInvalidated -= OnClockGlobalSpeedInvalidated;
    }

    private void OnClockStateInvalidated(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, _clock))
        {
            DispatchClockUpdate(ApplyClockState);
        }
    }

    private void OnClockTimeInvalidated(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, _clock))
        {
            DispatchClockUpdate(ApplyClockTime);
        }
    }

    private void OnClockGlobalSpeedInvalidated(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, _clock))
        {
            DispatchClockUpdate(ApplyClockState);
        }
    }

    private void DispatchClockUpdate(Action update)
    {
        if (CheckAccess())
        {
            update();
        }
        else
        {
            Dispatcher.BeginInvoke(update);
        }
    }

    private void ApplyClockTime()
    {
        if (_clock?.CurrentTime is { } currentTime && currentTime != _position)
        {
            Position = currentTime;
        }
    }

    private void ApplyClockState()
    {
        var clock = _clock;
        if (clock == null)
        {
            return;
        }

        ApplyClockTime();
        SpeedRatio = clock.Timeline.SpeedRatio;

        switch (clock.CurrentState)
        {
            case ClockState.Active when clock.IsPaused:
            case ClockState.Filling:
                if (_isPlaying)
                {
                    Pause();
                }
                else
                {
                    _isPlaying = false;
                    _isPaused = true;
                }
                break;
            case ClockState.Active:
                Play();
                break;
            case ClockState.Stopped:
                Stop();
                break;
        }
    }

    private void OpenSource(Uri source, Uri? baseUri)
    {
        var resolvedSource = ResolveSourceUri(source, baseUri);
        if (_openedSource is not null && _openedSource.Equals(resolvedSource))
        {
            // Reparenting a Manual MediaElement (for example an F11 fullscreen
            // hand-off) can replay the same resolved Source through the property
            // pipeline.  Keep the live decoder/session instead of probing and
            // reopening identical media.
            _mediaPath = resolvedSource.IsFile ? resolvedSource.LocalPath : resolvedSource.AbsoluteUri;
            return;
        }

        _openedSource = resolvedSource;
        _mediaPath = resolvedSource.IsFile ? resolvedSource.LocalPath : resolvedSource.AbsoluteUri;
        Volatile.Write(ref _downloadProgress, 0.0);
        OpenMedia(_mediaPath);
    }

    internal static Uri ResolveSourceUri(Uri source, Uri? baseUri)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.IsAbsoluteUri)
        {
            return source;
        }

        if (baseUri?.IsAbsoluteUri == true)
        {
            return new Uri(baseUri, source);
        }

        var path = source.OriginalString;
        if (baseUri != null)
        {
            path = Path.Combine(baseUri.OriginalString, path);
        }

        return new Uri(Path.GetFullPath(path), UriKind.Absolute);
    }

    private static void OnVolumeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MediaElement media)
        {
            media.SetVolumeInternal((double)e.NewValue!);
        }
    }

    private static object CoerceVolume(DependencyObject d, object? value)
    {
        var volume = (double)(value ?? 0.5);
        return Math.Clamp(volume, 0.0, 1.0);
    }

    private static void OnBalanceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MediaElement media && media._audioManager != null)
        {
            media._audioManager.Balance = (double)e.NewValue!;
        }
    }

    private static object CoerceBalance(DependencyObject d, object? value)
    {
        var balance = (double)(value ?? 0.0);
        return Math.Clamp(balance, -1.0, 1.0);
    }

    private static void OnIsMutedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MediaElement media)
        {
            media.SetMutedInternal((bool)e.NewValue!);
        }
    }

    private static void OnStretchChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MediaElement media)
        {
            media.InvalidateMeasure();
            media.InvalidateVisual();
        }
    }

    private static void OnLoadedBehaviorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MediaElement media)
        {
            media.ApplyLoadedBehavior((MediaState)e.NewValue!);
        }
    }

    private static void OnSpeedRatioChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MediaElement media)
        {
            var ratio = (double)e.NewValue!;
            MediaElementPlaybackSession? session;
            lock (media._lock)
            {
                session = media._playbackSession;
            }
            if (session is not null) session.SpeedRatio = ratio;
            if (media._audioManager != null)
            {
                media._audioManager.SpeedRatio = ratio;
            }
        }
    }

    private static object CoerceSpeedRatio(DependencyObject d, object? value)
    {
        var ratio = (double)(value ?? 1.0);
        return Math.Clamp(ratio, 0.1, 10.0);
    }

    #endregion

    #region 媒体打开与初始化

    /// <summary>
    /// 启动异步媒体打开。UI 线程不做任何 IO — native video probe（IMFSourceReader 创建）+
    /// audio engine 初始化都搬到 ThreadPool，完成后回 UI 线程更新状态并按 LoadedBehavior 启动播放。
    /// 期间 _hasVideo/_hasAudio 为 false，OnRender 显示"Loading..."而不阻塞 WM_PAINT。
    /// </summary>
    private void OpenMedia(string source)
    {
        // 立即停止旧播放并清掉显示中的帧；但**不**重置 _hasVideo / _videoWidth / _videoHeight,
        // 否则 MeasureOverride 会立即返回 (0,0)，触发 layout 塌陷让父容器闪黑。
        // 这些字段在 probe 完成的 BeginInvoke 中一次性更新到新视频尺寸。
        // StopPlayback 已经把 _videoDecoder 设 null 并放后台 dispose — 不需要再手动 Dispose。
        StopPlayback();
        _isOpening = true;
        _requestedOpenState = null;
        _isPaused = false;
        _replayOnPlay = false;
        _position = TimeSpan.Zero;
        _duration = TimeSpan.Zero;
        _hasVideo = false;
        _hasAudio = false;
        _d3dImage?.SetBackBuffer((NativeVideoSurface?)null);
        _videoSurface?.Dispose();
        _videoSurface = null;
        _videoSurfacePathUnsupported = false;
        _frameBitmap = null;
        DropPendingDisplayFrame();
        InvalidateVisual();

        var generation = ++_openMediaGeneration;
        ++_trackSwitchGeneration;
        var dispatcher = Dispatcher.MainDispatcher;
        var requestedAudioTrack = _selectedAudioTrackIndex;
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock) _openTask = _openTask.IsCompleted ? opened.Task : Task.WhenAll(_openTask, opened.Task);
        TrackDiscoveryError = null;
        _audioTracks = Array.Empty<MediaTrackInfo>();
        _subtitleTracks = Array.Empty<MediaTrackInfo>();

        Task.Run(() =>
        {
            INativeVideoDecoder? probe = null;
            var probeHasVideo = false;
            var probeWidth = 0;
            var probeHeight = 0;
            var probeFps = 30.0;
            var probeDuration = 0.0;
            var probeHasAudio = false;
            var audioDurationSeconds = 0.0;
            Exception? probeError = null;
            Exception? videoError = null;
            Exception? audioError = null;
            Exception? trackDiscoveryError = null;
            IReadOnlyList<MediaTrackInfo> discoveredAudio = Array.Empty<MediaTrackInfo>();
            IReadOnlyList<MediaTrackInfo> discoveredSubtitles = Array.Empty<MediaTrackInfo>();
            AudioPlayer? openedAudio = null;

            try
            {
                if (OperatingSystem.IsLinux())
                {
                    try
                    {
                        var tracks = NativeLinuxMedia.DiscoverTracks(BuildSourceUri(source));
                        discoveredAudio = tracks
                            .Where(track => track.Kind == MediaTrackKind.Audio)
                            .OrderBy(track => track.Index)
                            .ToArray();
                        discoveredSubtitles = tracks
                            .Where(track => track.Kind == MediaTrackKind.Subtitle)
                            .OrderBy(track => track.Index)
                            .ToArray();
                    }
                    catch (Exception ex)
                    {
                        trackDiscoveryError = ex;
                    }
                }

                probe = GetVideoDecoderFactory().Create();
                try
                {
                    probe.Open(BuildSourceUri(source));
                    probeHasVideo = probe.Width > 0 && probe.Height > 0;
                    probeWidth = probe.Width;
                    probeHeight = probe.Height;
                    probeFps = probe.Fps > 0 ? probe.Fps : 30.0;
                    probeDuration = probe.Duration.TotalSeconds;
                }
                catch (Exception exception)
                {
                    videoError = exception;
                    probe.Dispose();
                    probe = null;
                }

                // Open each candidate on an isolated player. Source changes
                // can overlap while native probing runs; sharing
                // _audioManager here let an older generation close or reopen
                // the newer source after it had won the generation check.
                var audioCandidate = new AudioPlayer();
                EventHandler<AudioPlayerErrorEventArgs> captureAudioError = (_, e) => audioError = e.ErrorException;
                audioCandidate.MediaFailed += captureAudioError;
                try
                {
                    audioCandidate.AudioTrackIndex = discoveredAudio.Count == 0
                        ? requestedAudioTrack
                        : Math.Min(requestedAudioTrack, discoveredAudio.Count - 1);
                    audioCandidate.Open(BuildSourceUri(source));
                    if (audioCandidate.HasAudio)
                    {
                        probeHasAudio = true;
                        audioDurationSeconds =
                            audioCandidate.NaturalDuration?.TotalSeconds ?? 0;
                        audioCandidate.Stop();
                        openedAudio = audioCandidate;
                        audioCandidate = null!;
                    }
                }
                catch (Exception exception)
                {
                    audioError = exception;
                }
                finally
                {
                    (audioCandidate ?? openedAudio)!.MediaFailed -= captureAudioError;
                    audioCandidate?.Dispose();
                }
                if (!probeHasVideo && !probeHasAudio)
                    probeError = videoError ?? audioError ?? new InvalidDataException("No decodable audio or video stream was found.");
            }
            catch (Exception ex)
            {
                probeError = ex;
                probe?.Dispose();
                probe = null;
                openedAudio?.Dispose();
                openedAudio = null;
            }

            var capturedDecoder = probe;
            var capturedAudio = openedAudio;
            if (dispatcher is null || _disposed || generation != Volatile.Read(ref _openMediaGeneration))
            {
                DisposeAfterPlayback(Task.CompletedTask, capturedDecoder);
                DisposeAfterPlayback(Task.CompletedTask, capturedAudio);
                opened.TrySetResult();
                return;
            }
            dispatcher?.BeginInvoke(() =>
            {
                try
                {
                // 期间用户切换了 Source — 旧探测结果作废。
                if (_disposed || generation != _openMediaGeneration)
                {
                    DisposeAfterPlayback(Task.CompletedTask, capturedDecoder);
                    DisposeAfterPlayback(Task.CompletedTask, capturedAudio);
                    return;
                }

                if (probeError != null)
                {
                    _isOpening = false;
                    _openedSource = null;
                    DisposeAfterPlayback(Task.CompletedTask, capturedDecoder);
                    DisposeAfterPlayback(Task.CompletedTask, capturedAudio);
                    RaiseMediaFailed(probeError);
                    return;
                }

                lock (_lock)
                {
                    _videoDecoder = capturedDecoder;
                }
                var previousAudio = _audioManager;
                _audioManager = capturedAudio ?? new AudioPlayer();
                _audioManager.MediaFailed += OnAudioPlaybackFailed;
                if (previousAudio != null &&
                    !ReferenceEquals(previousAudio, _audioManager))
                {
                    previousAudio.MediaFailed -= OnAudioPlaybackFailed;
                    DisposeAfterPlayback(CapturePlaybackTail(), previousAudio);
                }
                TrackDiscoveryError = trackDiscoveryError;
                _audioTracks = discoveredAudio;
                _subtitleTracks = discoveredSubtitles;
                if (_audioTracks.Count > 0 && _selectedAudioTrackIndex >= _audioTracks.Count)
                    _selectedAudioTrackIndex = 0;
                if (_subtitleTracks.Count == 0)
                    _selectedSubtitleTrackIndex = -1;
                else if (_selectedSubtitleTrackIndex >= _subtitleTracks.Count)
                    _selectedSubtitleTrackIndex = -1;
                _hasVideo = probeHasVideo;
                _hasAudio = probeHasAudio;
                _videoWidth = probeWidth;
                _videoHeight = probeHeight;
                _videoFps = probeFps;

                var totalDuration = Math.Max(probeDuration, audioDurationSeconds);
                _duration = TimeSpan.FromSeconds(totalDuration);
                _frameDelayMs = 1000.0 / _videoFps;
                _videoStartTimeMs = 0;

                if (_hasVideo)
                {
                    UpdateDisplayScale(RenderSize);
                    InvalidateMeasure();
                }

                Volatile.Write(ref _downloadProgress, 1.0);
                _isOpening = false;
                var playbackGeneration = _playbackGeneration;
                RaiseMediaOpened();
                if (_disposed || generation != _openMediaGeneration || playbackGeneration != _playbackGeneration || _isPlaying)
                    return;
                if (_clock != null)
                {
                    ApplyClockState();
                }
                else
                {
                    ApplyLoadedBehavior(_requestedOpenState ?? LoadedBehavior);
                }
                _requestedOpenState = null;
                }
                finally { opened.TrySetResult(); }
            });
        });
    }

    private static Uri BuildSourceUri(string source)
    {
        if (string.IsNullOrEmpty(source))
            throw new ArgumentException("Source path is empty.", nameof(source));

        if (Uri.TryCreate(source, UriKind.Absolute, out var absolute))
            return absolute;

        var fullPath = Path.GetFullPath(source);
        return new Uri(fullPath, UriKind.Absolute);
    }

    #endregion

    #region 播放控制

    private readonly record struct PlaybackOperationSnapshot(
        string? MediaPath,
        INativeVideoDecoder? Decoder,
        AudioPlayer? Audio,
        TimeSpan Position,
        TimeSpan Duration,
        bool HasVideo,
        bool HasAudio,
        double FrameDelayMs);

    private void StartPlaybackInternal()
    {
        if (string.IsNullOrEmpty(_mediaPath))
            return;

        // arrange-size 只在需要渲染视频帧时是硬要求。Audio-only 媒体(Gallery
        // Audio Player demo 把 <MediaElement Width="0" Height="0"/> 当后台播放器
        // 用)永远等不到非零 arrange size,过去会卡在 _pendingPlay = true 上播
        // 不出声。这里把检查收紧到"有视频内容时才要求 size"。
        if (_hasVideo && (!_isArranged || _arrangedSize.Width <= 0 || _arrangedSize.Height <= 0))
        {
            _pendingPlay = true;
            return;
        }

        // probe 完成后既无 video 也无 audio:没有可启动的播放路径,直接返回。
        if (!_hasVideo && !_hasAudio)
        {
            return;
        }

        StartPlaybackWithSize(_arrangedSize);
    }

    private void StartPlaybackWithSize(Size targetSize)
    {
        _framesRendered = 0;
        _framesDropped = 0;
        _framesLate = 0;

        var (videoWidth, videoHeight) = CalculateVideoRenderSize(targetSize);
        _targetWidth = videoWidth;
        _targetHeight = videoHeight;

        // 首次播放或Stop后，从0开始
        _videoStartTimeMs = _position.TotalMilliseconds;

        if (_hasVideo && (videoWidth <= 0 || videoHeight <= 0))
            return;

        EnqueuePlaybackOperation(
            marksPlaybackStart: true,
            (generation, snapshot) => StartPlaybackOperationAsync(
                generation,
                snapshot,
                videoWidth,
                videoHeight));
    }

    /// <summary>
    /// Resume is queued behind the previous immutable session.  The decoder is
    /// not sought until every old worker that captured it has exited.
    /// </summary>
    private void ResumePlayback()
    {
        _videoStartTimeMs = _position.TotalMilliseconds;
        _isPlaying = true;
        _isPaused = false;
        StartPlaybackInternal();
    }

    /// <summary>
    /// 在 UI 线程瞬间完成 Pause — 不再 task.Wait。decode/render task 看到 cancel 后自然退出，
    /// 它们持有的资源由 task 自身或下次 OpenMedia/StopPlayback 清理。
    /// </summary>
    private void PausePlayback()
    {
        MediaElementPlaybackSession? session;
        lock (_lock)
        {
            ++_playbackGeneration;
            _pendingPlaybackStartGeneration = 0;
            session = _playbackSession;
            _playbackSession = null;
        }

        if (session is not null)
            _position = session.MediaTime;

        _audioManager?.Pause();
        session?.Cancel();
        DropPendingDisplayFrame();
        StopSubtitlePlayback();
    }

    /// <summary>
    /// Detaches the current generation synchronously, then retires its decoder
    /// only after all already-admitted playback operations and worker loops end.
    /// </summary>
    private Task StopPlayback()
    {
        MediaElementPlaybackSession? session;
        INativeVideoDecoder? decoder;
        Task tail;
        lock (_lock)
        {
            ++_playbackGeneration;
            _pendingPlaybackStartGeneration = 0;
            session = _playbackSession;
            _playbackSession = null;
            decoder = _videoDecoder;
            _videoDecoder = null;
            tail = _playbackTail;
        }

        session?.Cancel();
        _isPlaying = false;
        _pendingPlay = false;
        DropPendingDisplayFrame();
        _audioManager?.Stop();
        StopSubtitlePlayback();

        DisposeAfterPlayback(tail, decoder);
        return tail;
    }

    private void CloseMedia()
    {
        _isOpening = false;
        _requestedOpenState = null;
        _replayOnPlay = false;
        ++_openMediaGeneration;
        ++_trackSwitchGeneration;
        var tail = StopPlayback();
        _openedSource = null;

        // Detach the player before closing it. A subsequent OpenMedia uses an
        // independent candidate, so a slow native close from this generation
        // can never tear down the next source.
        var audioMgr = _audioManager;
        _audioManager = new AudioPlayer();
        DisposeAfterPlayback(tail, audioMgr);
        DropPendingDisplayFrame();
        _d3dImage?.SetBackBuffer((NativeVideoSurface?)null);
        _videoSurface?.Dispose();
        _videoSurface = null;

        _frameBitmap = null;
        _position = TimeSpan.Zero;
        _duration = TimeSpan.Zero;
        _hasVideo = false;
        _hasAudio = false;
        _audioTracks = Array.Empty<MediaTrackInfo>();
        _subtitleTracks = Array.Empty<MediaTrackInfo>();
        TrackDiscoveryError = null;
        _subtitleText = null;
        _videoWidth = 0;
        _videoHeight = 0;
        _isArranged = false;
        _arrangedSize = default;
        _pendingPlay = false;
        _isPaused = false;
        _videoStartTimeMs = 0;
        IsBuffering = false;
        Volatile.Write(ref _bufferingProgress, 0.0);
        Volatile.Write(ref _downloadProgress, 0.0);

        InvalidateMeasure();
        InvalidateVisual();
    }

    private int EnqueuePlaybackOperation(
        bool marksPlaybackStart,
        Func<int, PlaybackOperationSnapshot, Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var launchGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task previous;
        Task queued;
        PlaybackOperationSnapshot snapshot;
        int generation;

        lock (_lock)
        {
            if (_disposed ||
                (marksPlaybackStart &&
                 (_playbackSession is not null || _pendingPlaybackStartGeneration != 0)))
            {
                return 0;
            }

            generation = ++_playbackGeneration;
            if (marksPlaybackStart)
                _pendingPlaybackStartGeneration = generation;
            previous = _playbackTail.IsCompleted
                ? Task.CompletedTask
                : _playbackTail;
            snapshot = new PlaybackOperationSnapshot(
                _mediaPath,
                _videoDecoder,
                _audioManager,
                _position,
                _duration,
                _hasVideo,
                _hasAudio,
                _frameDelayMs > 0 ? _frameDelayMs : 1000.0 / 30.0);

            queued = Task.Run(async () =>
            {
                await launchGate.Task.ConfigureAwait(false);
                await ObservePlaybackTaskAsync(previous).ConfigureAwait(false);
                try
                {
                    await operation(generation, snapshot).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    ReportPlaybackOperationFailure(generation, exception);
                }
                finally
                {
                    if (marksPlaybackStart)
                    {
                        lock (_lock)
                        {
                            if (_pendingPlaybackStartGeneration == generation)
                                _pendingPlaybackStartGeneration = 0;
                        }
                    }
                }
            });
            _playbackTail = queued;
        }

        launchGate.TrySetResult();
        return generation;
    }

    private async Task StartPlaybackOperationAsync(
        int generation,
        PlaybackOperationSnapshot snapshot,
        int videoWidth,
        int videoHeight)
    {
        if (!IsPlaybackOperationCurrent(generation, snapshot.MediaPath, requirePlaying: true))
            return;

        INativeVideoDecoder? decoder = snapshot.Decoder;
        bool createdDecoder = false;
        bool adoptedDecoder = false;
        try
        {
            if (snapshot.HasVideo && decoder is null)
            {
                if (string.IsNullOrEmpty(snapshot.MediaPath))
                    throw new InvalidOperationException("The video source is unavailable.");
                decoder = GetVideoDecoderFactory().Create();
                createdDecoder = true;
                decoder.Open(BuildSourceUri(snapshot.MediaPath));
            }

            if (!IsPlaybackOperationCurrent(generation, snapshot.MediaPath, requirePlaying: true))
                return;

            if (decoder is not null)
                decoder.Seek(snapshot.Position);
            if (snapshot.HasAudio && snapshot.Audio is not null)
                snapshot.Audio.Seek(snapshot.Position);

            await InvokeOnUiAsync(() =>
            {
                Func<TimeSpan>? readAudioPosition = snapshot.Audio is not null && snapshot.HasAudio
                    ? () => snapshot.Audio.Position
                    : null;
                var session = new MediaElementPlaybackSession(
                    new MediaElementPlaybackSessionOptions(
                        generation,
                        snapshot.HasVideo ? decoder : null,
                        readAudioPosition,
                        snapshot.Position,
                        snapshot.FrameDelayMs,
                        SpeedRatio,
                        snapshot.Duration,
                        static () => RenderContext.GetOrCreateCurrent().Handle,
                        PresentPlaybackFrame,
                        OnPlaybackPositionChanged,
                        OnPlaybackSessionFailed,
                        OnPlaybackSessionEnded,
                        OnPlaybackStatisticsChanged,
                        snapshot.HasAudio && snapshot.Audio is not null
                            ? () => snapshot.Audio.State == NativePlaybackState.Ended
                            : null));

                try
                {
                    lock (_lock)
                    {
                        if (_disposed ||
                            _playbackGeneration != generation ||
                            !_isPlaying ||
                            !string.Equals(_mediaPath, snapshot.MediaPath, StringComparison.Ordinal))
                        {
                            session.Dispose();
                            return;
                        }

                        if (createdDecoder)
                        {
                            _videoDecoder = decoder;
                            adoptedDecoder = true;
                        }

                        _playbackSession = session;
                        _playbackTail = _playbackTail.IsCompleted
                            ? session.Completion
                            : Task.WhenAll(_playbackTail, session.Completion);

                        if (snapshot.Audio is not null && snapshot.HasAudio)
                        {
                            snapshot.Audio.Volume = _currentVolume;
                            snapshot.Audio.IsMuted = _isMuted;
                            snapshot.Audio.Balance = Balance;
                            snapshot.Audio.SpeedRatio = SpeedRatio;
                            snapshot.Audio.Play();
                        }

                        session.Start();
                    }
                }
                catch
                {
                    lock (_lock)
                    {
                        if (ReferenceEquals(_playbackSession, session))
                            _playbackSession = null;
                    }
                    try { snapshot.Audio?.Stop(); } catch { }
                    session.Dispose();
                    throw;
                }

                StartSubtitlePlayback();
            }).ConfigureAwait(false);
        }
        finally
        {
            if (createdDecoder && !adoptedDecoder)
            {
                try { decoder?.Dispose(); } catch { }
            }
        }
    }

    private void QueueSeekWithoutPlayback(TimeSpan position)
    {
        bool scrub = ScrubbingEnabled;
        EnqueuePlaybackOperation(
            marksPlaybackStart: false,
            async (generation, snapshot) =>
            {
                if (!IsPlaybackOperationCurrent(generation, snapshot.MediaPath, requirePlaying: false))
                    return;
                snapshot.Decoder?.Seek(position);
                if (snapshot.HasAudio)
                    snapshot.Audio?.Seek(position);
                if (!scrub || snapshot.Decoder is null || !snapshot.HasVideo) return;
                if (!snapshot.Decoder.TryReadFrame(out var mediaFrame) || mediaFrame is null) return;
                using var frame = new VideoFrame(mediaFrame, (long)mediaFrame.PresentationTime.TotalMilliseconds, 0);
                await InvokeOnUiAsync(() =>
                {
                    if (!IsPlaybackOperationCurrent(generation, snapshot.MediaPath, requirePlaying: false) || _isPlaying) return;
                    UpdateFrameBitmap(frame);
                }).ConfigureAwait(false);
            });
    }

    private bool PresentPlaybackFrame(MediaElementPlaybackSession session, VideoFrame frame)
    {
        VideoFrame? oldPending;
        lock (_lock)
        {
            if (_disposed ||
                !ReferenceEquals(_playbackSession, session) ||
                _playbackGeneration != session.Generation ||
                !_isPlaying)
            {
                return false;
            }
            oldPending = Interlocked.Exchange(ref _pendingDisplayFrame, frame);
        }
        oldPending?.Dispose();
        RequestUiRefresh();
        return true;
    }

    private void OnPlaybackPositionChanged(MediaElementPlaybackSession session, TimeSpan position)
    {
        lock (_lock)
        {
            if (_disposed ||
                !ReferenceEquals(_playbackSession, session) ||
                _playbackGeneration != session.Generation)
            {
                return;
            }
            _position = position;
        }
    }

    private void OnPlaybackStatisticsChanged(MediaElementPlaybackSession session)
    {
        lock (_lock)
        {
            if (_disposed ||
                !ReferenceEquals(_playbackSession, session) ||
                _playbackGeneration != session.Generation)
            {
                return;
            }
            _framesRendered = session.FramesRendered;
            _framesDropped = session.FramesDropped;
            _framesLate = session.FramesLate;
        }
    }

    private void OnPlaybackSessionFailed(MediaElementPlaybackSession session, Exception exception)
    {
        PostPlaybackCallback(() =>
        {
            if (!IsCurrentPlaybackSession(session)) return;
            _isPlaying = false;
            _isPaused = false;
            StopPlayback();
            RaiseMediaFailed(exception);
        });
    }

    private void OnPlaybackSessionEnded(MediaElementPlaybackSession session)
    {
        PostPlaybackCallback(() =>
        {
            lock (_lock)
            {
                if (!ReferenceEquals(_playbackSession, session) ||
                    _playbackGeneration != session.Generation)
                {
                    return;
                }
                ++_playbackGeneration;
                _playbackSession = null;
                _pendingPlaybackStartGeneration = 0;
            }

            _isPlaying = false;
            _isPaused = false;
            if (_duration > TimeSpan.Zero) _position = _duration;
            _replayOnPlay = true;
            _audioManager?.Stop();
            StopSubtitlePlayback();
            RaiseMediaEnded();
        });
    }

    private bool IsCurrentPlaybackSession(MediaElementPlaybackSession session)
    {
        lock (_lock)
        {
            return !_disposed &&
                   ReferenceEquals(_playbackSession, session) &&
                   _playbackGeneration == session.Generation;
        }
    }

    private bool IsPlaybackOperationCurrent(
        int generation,
        string? mediaPath,
        bool requirePlaying)
    {
        lock (_lock)
        {
            return !_disposed &&
                   _playbackGeneration == generation &&
                   string.Equals(_mediaPath, mediaPath, StringComparison.Ordinal) &&
                   (!requirePlaying || _isPlaying);
        }
    }

    private void ReportPlaybackOperationFailure(int generation, Exception exception)
    {
        PostPlaybackCallback(() =>
        {
            lock (_lock)
            {
                if (_disposed || _playbackGeneration != generation)
                    return;
            }
            _isPlaying = false;
            _isPaused = false;
            StopPlayback();
            RaiseMediaFailed(exception);
        });
    }

    private void PostPlaybackCallback(Action callback)
    {
        var dispatcher = Dispatcher.MainDispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            callback();
            return;
        }
        dispatcher.BeginInvoke(callback);
    }

    private static Task InvokeOnUiAsync(Action callback)
    {
        var dispatcher = Dispatcher.MainDispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            callback();
            return Task.CompletedTask;
        }
        return dispatcher.InvokeAsync(callback).Task;
    }

    private static async Task ObservePlaybackTaskAsync(Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch { }
    }

    private void DisposeAfterPlayback(Task playbackTail, IDisposable? resource)
    {
        if (resource is null) return;
        if (resource is AudioPlayer audio) audio.MediaFailed -= OnAudioPlaybackFailed;
        var cleanup = Task.Run(async () =>
        {
            await ObservePlaybackTaskAsync(playbackTail).ConfigureAwait(false);
            try { resource.Dispose(); } catch { }
            if (resource is AudioPlayer player) await player.PendingCleanup.ConfigureAwait(false);
        });
        lock (_lock) _resourceCleanupTail = _resourceCleanupTail.IsCompleted ? cleanup : Task.WhenAll(_resourceCleanupTail, cleanup);
    }

    private void OnAudioPlaybackFailed(object? sender, AudioPlayerErrorEventArgs e)
    {
        PostPlaybackCallback(() =>
        {
            if (_disposed || !ReferenceEquals(sender, _audioManager)) return;
            StopPlayback();
            RaiseMediaFailed(e.ErrorException);
        });
    }

    private Task CapturePlaybackTail()
    {
        lock (_lock) return _playbackTail;
    }

    private (int width, int height) CalculateVideoRenderSize(Size availableSize)
    {
        if (_videoWidth <= 0 || _videoHeight <= 0)
            return (0, 0);

        var naturalSize = new Size(_videoWidth, _videoHeight);
        var scaledSize = ComputeScaledSize(availableSize, naturalSize);

        var targetWidth = (int)(scaledSize.Width + 0.5);
        var targetHeight = (int)(scaledSize.Height + 0.5);

        targetWidth = Math.Max(1, targetWidth);
        targetHeight = Math.Max(1, targetHeight);

        targetWidth = (targetWidth / 2) * 2;
        targetHeight = (targetHeight / 2) * 2;

        return (targetWidth, targetHeight);
    }

    #endregion

    #region 播放循环

    /// <summary>
    /// 节流的 UI 重绘请求 — 同一时刻 Dispatcher 队列里最多一个 ApplyPendingFrame delegate。
    /// 视频帧更新极频繁（30+fps），不节流会让 UI 线程偶尔卡顿时队列积累上千个 delegate。
    /// </summary>
    private void RequestUiRefresh()
    {
        if (Interlocked.Exchange(ref _renderRequestPending, 1) == 0)
        {
            Dispatcher.MainDispatcher?.BeginInvoke(ApplyPendingFrame);
        }
    }

    /// <summary>
    /// UI 线程：从 atomic 槽位取最新一帧，写入 WriteableBitmap，归还 ArrayPool。
    /// 同时间最多有一个 frame 在槽位里，老的早已被解码线程换出并 Dispose。
    /// </summary>
    private void ApplyPendingFrame()
    {
        Volatile.Write(ref _renderRequestPending, 0);

        var pending = Interlocked.Exchange(ref _pendingDisplayFrame, null);
        if (pending == null) return;

        try
        {
            UpdateFrameBitmap(pending);
        }
        catch (Exception exception)
        {
            StopPlayback();
            RaiseMediaFailed(exception);
        }
        finally
        {
            pending.Dispose();
        }
    }

    private void DropPendingDisplayFrame()
    {
        var pending = Interlocked.Exchange(ref _pendingDisplayFrame, null);
        pending?.Dispose();
    }

    #endregion

    #region 帧渲染

    /// <summary>
    /// UI 线程：把解码出的 BGRA8 帧拷贝到唯一的 <see cref="WriteableBitmap"/>，并触发重绘。
    /// </summary>
    /// <remarks>
    /// 关键路径只有一次拷贝（Span → WriteableBitmap.BackBuffer），WriteableBitmap 的 ContentRevision
    /// 自增让 <c>RenderTargetDrawingContext._bitmapCache</c> 检测到脏并重新上传 GPU 纹理。
    /// 不再做 BMP 编解码也不再创建新 BitmapImage 实例 — 视频帧通过同一个 ImageSource 引用持续刷新。
    /// </remarks>
    private void UpdateFrameBitmap(VideoFrame frame)
    {
        var width = frame.Width;
        var height = frame.Height;
        if (width <= 0 || height <= 0 || width > 8192 || height > 8192)
            throw new InvalidDataException("The decoded video frame dimensions are invalid or unsupported.");

        // ── Stage 3 fast path:decoder 已给 GPU-resident surface(DXVA / MediaCodec /
        //   AVAssetReader 等硬件解码)。绕开所有 CPU staging,直接把 surface bind 到
        //   D3DImage 让 framework 走 jalium_render_target_draw_video_surface。
        //   VideoFrame.Dispose 时 surface 会被还给 decoder ring(stage 3 真填后)或
        //   release native handle(stage 3 骨架期)。
        if (frame.GpuSurface is not null)
        {
            // D3DImage keeps only a caller-owned reference. Transfer the
            // surface out of VideoFrame before ApplyPendingFrame disposes the
            // frame, and retain it until a later surface replaces it. Without
            // this transfer the native VkImage was destroyed immediately
            // after binding, leaving D3DImage with a disposed handle.
            var gpu = frame.DetachGpuSurface();
            if (gpu is null) return;
            var previousSurface = _videoSurface;
            // 旧 NativeVideoSurface 退出 D3DImage 引用(下一次 dispose 时归还 decoder)。
            _d3dImage ??= new WpfD3DImage();
            _d3dImage.SetBackBuffer(gpu);
            _videoSurface = gpu;
            // 旧 BGRA staging surface 在 GPU 路径下用不上,释放 GPU 内存。
            if (previousSurface is not null &&
                !ReferenceEquals(previousSurface, gpu))
            {
                previousSurface.Dispose();
            }
            InvalidateVisual();
            return;
        }

        var pixels = frame.Pixels;
        if (pixels.IsEmpty) throw new InvalidDataException("The decoded video frame has no pixels.");

        // ── Stage 1+2 path:NativeVideoSurface direct GPU staging ──
        // Software backend already supports this end-to-end as of stage 2;
        // D3D12 / Vulkan implementations land in subsequent PRs. While
        // CreateBgra8 throws (backend stub), _videoSurfacePathUnsupported
        // sticks so we don't keep retrying every frame.
        if (!_videoSurfacePathUnsupported &&
            TryUpdateVideoSurface(pixels, width, height, frame.Stride))
        {
            InvalidateVisual();
            return;
        }

        // ── Fallback:legacy WriteableBitmap path ──
        var needsNewBitmap = _frameBitmap is null ||
                             _frameBitmap.PixelWidth != width ||
                             _frameBitmap.PixelHeight != height;

        if (needsNewBitmap)
        {
            try
            {
                _frameBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            }
            catch
            {
                _frameBitmap = null;
                throw;
            }
        }

        try
        {
            _frameBitmap!.WritePixels(new Int32Rect(0, 0, width, height), pixels, frame.Stride);
            InvalidateVisual();
        }
        catch (Exception exception) { throw new InvalidOperationException("Video frame upload failed.", exception); }
    }

    /// <summary>
    /// Stage 1+2 path. Returns true when the frame was successfully uploaded
    /// into <see cref="_videoSurface"/>;false signals the caller to fall back
    /// to the WriteableBitmap path. On a thrown <see cref="NativeMediaException"/>
    /// (typically <see cref="NativeMediaStatus.NotImplemented"/> from a backend
    /// without video surfaces wired yet) the path is permanently disabled for
    /// this MediaElement instance to avoid retrying every frame.
    /// </summary>
    private bool TryUpdateVideoSurface(ReadOnlySpan<byte> pixels, int width, int height, int srcStride)
    {
        bool needsRebuild = _videoSurface is null ||
                            _videoSurface.IsDisposed ||
                            _videoSurface.Kind != NativeVideoSurfaceKind.Bgra8Cpu ||
                            _videoSurface.PixelWidth != width ||
                            _videoSurface.PixelHeight != height;

        if (needsRebuild)
        {
            _videoSurface?.Dispose();
            _videoSurface = null;
            _d3dImage?.SetBackBuffer((NativeVideoSurface?)null);
            try
            {
                var ctx = RenderContext.GetOrCreateCurrent();
                _videoSurface = NativeVideoSurface.CreateBgra8(ctx.Handle, width, height);
                _d3dImage ??= new WpfD3DImage();
                _d3dImage.SetBackBuffer(_videoSurface);
            }
            catch (NativeMediaException)
            {
                _videoSurfacePathUnsupported = true;
                _videoSurface = null;
                _d3dImage = null;
                return false;
            }
            catch
            {
                // Any other failure (OOM, context unavailable) — fall through.
                _videoSurface = null;
                _d3dImage = null;
                return false;
            }
        }

        try
        {
            using var locked = _videoSurface!.Lock();
            int dstStride = locked.Stride;
            int rowBytes = width * 4;
            if (srcStride == dstStride && srcStride == rowBytes)
            {
                // Tightly packed source — single contiguous copy.
                pixels.Slice(0, rowBytes * height).CopyTo(locked.Pixels);
            }
            else
            {
                // Row-by-row copy to honor differing strides.
                for (int y = 0; y < height; y++)
                {
                    pixels.Slice(y * srcStride, rowBytes)
                          .CopyTo(locked.Pixels.Slice(y * dstStride, rowBytes));
                }
            }
            return true;
        }
        catch
        {
            // Lock failed mid-stream — invalidate this surface and try again
            // next frame (rebuild path).
            _videoSurface?.Dispose();
            _videoSurface = null;
            _d3dImage?.SetBackBuffer((NativeVideoSurface?)null);
            return false;
        }
    }

    private void UpdateDisplayScale(Size availableSize)
    {
        if (!_hasVideo || _videoWidth <= 0 || _videoHeight <= 0) return;

        var displayWidth = availableSize.Width > 0 ? availableSize.Width : _videoWidth;
        var displayHeight = availableSize.Height > 0 ? availableSize.Height : _videoHeight;

        var scaleX = displayWidth / _videoWidth;
        var scaleY = displayHeight / _videoHeight;

        switch (Stretch)
        {
            case Stretch.None:
                _displayScale = 1.0;
                break;
            case Stretch.Fill:
                _displayScale = Math.Max(scaleX, scaleY);
                break;
            case Stretch.Uniform:
                _displayScale = Math.Min(scaleX, scaleY);
                break;
            case Stretch.UniformToFill:
                _displayScale = Math.Max(scaleX, scaleY);
                break;
        }

        switch (StretchDirection)
        {
            case StretchDirection.UpOnly:
                _displayScale = Math.Max(1.0, _displayScale);
                break;
            case StretchDirection.DownOnly:
                _displayScale = Math.Min(1.0, _displayScale);
                break;
        }

        _displayScale = Math.Clamp(_displayScale, 0.1, 4.0);
    }

    #endregion

    #region 跳转

    /// <summary>
    /// 不阻塞 UI 线程的 Seek。AudioPlayer.Seek 走 SoundFlow 的内部 SoundPlayerBase.Seek,
    /// 不再重建 stream/data provider,但仍放后台执行以避免任何潜在阻塞 UI 调度的开销。
    /// </summary>
    private void SeekToPosition(TimeSpan position)
    {
        position = position < TimeSpan.Zero ? TimeSpan.Zero : position;
        if (_duration > TimeSpan.Zero && position > _duration) position = _duration;
        _replayOnPlay = false;
        var wasPlaying = _isPlaying;
        var wasPaused = _isPaused;

        // Retire the current immutable session but retain its decoder.  A queued
        // seek/start operation waits for that session before touching the decoder.
        _isPlaying = false;
        PausePlayback();

        _position = position;
        _videoStartTimeMs = position.TotalMilliseconds;

        if (wasPlaying)
        {
            _isPlaying = true;
            _isPaused = false;
            StartPlaybackInternal();
        }
        else
        {
            _isPlaying = false;
            _isPaused = wasPaused;
            QueueSeekWithoutPlayback(position);
        }
    }

    #endregion

    #region Track selection and subtitles

    private void SwitchAudioTrack(int previousTrack)
    {
        var path = _mediaPath;
        if (string.IsNullOrEmpty(path) || _audioManager == null || !_hasAudio) return;

        var generation = ++_trackSwitchGeneration;
        var position = _position;
        var resumePlaying = _isPlaying;
        var remainPaused = _isPaused;
        var selectedTrack = _selectedAudioTrackIndex;
        var volume = _currentVolume;
        var muted = _isMuted;
        var balance = Balance;
        var speedRatio = SpeedRatio;
        var dispatcher = Dispatcher.MainDispatcher;
        _isPlaying = false;
        StopPlayback();

        Task.Run(() =>
        {
            Exception? failure = null;
            AudioPlayer? candidate = null;
            try
            {
                candidate = new AudioPlayer
                {
                    AudioTrackIndex = selectedTrack,
                    Volume = volume,
                    IsMuted = muted,
                    Balance = balance,
                    SpeedRatio = speedRatio,
                };
                candidate.Open(BuildSourceUri(path));
                if (!candidate.HasAudio)
                    throw new InvalidOperationException("The selected audio track did not produce audio.");
                if (position > TimeSpan.Zero) candidate.Seek(position);
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            if (dispatcher is null)
            {
                candidate?.Dispose();
                return;
            }
            dispatcher.BeginInvoke(() =>
            {
                if (generation != _trackSwitchGeneration ||
                    !string.Equals(path, _mediaPath, StringComparison.Ordinal))
                {
                    candidate?.Dispose();
                    return;
                }
                if (failure != null)
                {
                    candidate?.Dispose();
                    _selectedAudioTrackIndex = previousTrack;
                    _position = position;
                    if (resumePlaying)
                    {
                        _isPlaying = true;
                        _isPaused = false;
                        StartPlaybackInternal();
                    }
                    else
                    {
                        _isPlaying = false;
                        _isPaused = remainPaused;
                    }
                    RaiseMediaFailed(failure);
                    return;
                }

                var previousAudio = _audioManager;
                _audioManager = candidate;
                candidate = null;
                if (previousAudio != null)
                {
                    DisposeAfterPlayback(CapturePlaybackTail(), previousAudio);
                }
                _hasAudio = true;
                _position = position;
                _videoStartTimeMs = position.TotalMilliseconds;
                if (resumePlaying)
                {
                    _isPlaying = true;
                    _isPaused = false;
                    StartPlaybackInternal();
                }
                else
                {
                    _isPlaying = false;
                    _isPaused = remainPaused;
                }
            });
        });
    }

    private void RestartSubtitlePlayback()
    {
        StopSubtitlePlayback();
        if (_isPlaying) StartSubtitlePlayback();
    }

    private void StartSubtitlePlayback()
    {
        StopSubtitlePlayback();
        if (!OperatingSystem.IsLinux() || !_isPlaying ||
            _selectedSubtitleTrackIndex < 0 || string.IsNullOrEmpty(_mediaPath)) return;

        SubtitleError = null;
        var source = BuildSourceUri(_mediaPath);
        var track = _selectedSubtitleTrackIndex;
        var cts = new CancellationTokenSource();
        _subtitleCts = cts;
        _subtitleTask = Task.Run(() => SubtitleLoop(source, track, cts.Token), cts.Token);
    }

    private void StopSubtitlePlayback()
    {
        var cts = _subtitleCts;
        _subtitleCts = null;
        _subtitleTask = null;
        try { cts?.Cancel(); } catch { }
        cts?.Dispose();
        if (_subtitleText != null)
        {
            _subtitleText = null;
            InvalidateVisual();
        }
    }

    private async Task SubtitleLoop(Uri source, int trackIndex, CancellationToken token)
    {
        try
        {
            using var decoder = new NativeSubtitleDecoder();
            decoder.Open(source, trackIndex);
            var initialPosition = GetPlaybackPosition();
            if (initialPosition > TimeSpan.Zero) decoder.Seek(initialPosition);

            while (!token.IsCancellationRequested && decoder.TryReadCue(out var cue))
            {
                var end = cue.Start + (cue.Duration > TimeSpan.Zero
                    ? cue.Duration : TimeSpan.FromSeconds(3));
                var current = GetPlaybackPosition();
                if (end <= current) continue;

                while (!token.IsCancellationRequested &&
                       (current = GetPlaybackPosition()) < cue.Start)
                {
                    var wait = cue.Start - current;
                    await Task.Delay(
                        wait > TimeSpan.FromMilliseconds(100)
                            ? TimeSpan.FromMilliseconds(100) : wait,
                        token).ConfigureAwait(false);
                }
                if (token.IsCancellationRequested) break;
                PostSubtitleText(cue.Text, token);

                while (!token.IsCancellationRequested && GetPlaybackPosition() < end)
                {
                    await Task.Delay(50, token).ConfigureAwait(false);
                }
                PostSubtitleText(null, token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Dispatcher.MainDispatcher?.BeginInvoke(() =>
            {
                if (token.IsCancellationRequested) return;
                SubtitleError = ex;
                _subtitleText = null;
                InvalidateVisual();
            });
        }
    }

    private TimeSpan GetPlaybackPosition()
    {
        if (_hasAudio && _audioManager != null)
        {
            try { return _audioManager.Position; } catch { }
        }
        lock (_lock)
        {
            return _playbackSession?.MediaTime ?? _position;
        }
    }

    private void PostSubtitleText(string? text, CancellationToken token)
    {
        Dispatcher.MainDispatcher?.BeginInvoke(() =>
        {
            if (token.IsCancellationRequested) return;
            _subtitleText = text;
            InvalidateVisual();
        });
    }

    #endregion

    #region 音量与静音

    private void SetVolumeInternal(double volume)
    {
        _currentVolume = volume;
        if (_audioManager != null) _audioManager.Volume = volume;
    }

    private void SetMutedInternal(bool isMuted)
    {
        _isMuted = isMuted;
        if (_audioManager != null) _audioManager.IsMuted = isMuted;
    }

    #endregion

    #region 行为应用

    private void ApplyLoadedBehavior(MediaState state)
    {
        switch (state)
        {
            case MediaState.Play:
                _isPlaying = true;
                _isPaused = false;
                StartPlaybackInternal();
                break;
            case MediaState.Pause:
                _isPlaying = false;
                _isPaused = true;
                PausePlayback();
                break;
            case MediaState.Stop:
                _isPlaying = false;
                _isPaused = false;
                StopPlayback();
                break;
            case MediaState.Close:
                CloseMedia();
                break;
        }
    }

    private void ApplyUnloadedBehavior()
    {
        switch (UnloadedBehavior)
        {
            case MediaState.Close:
                CloseMedia();
                break;
            case MediaState.Stop:
                Stop();
                break;
            case MediaState.Pause:
                Pause();
                break;
        }
    }

    #endregion

    #region 事件触发

    private void RaiseMediaOpened()
    {
        RaiseEvent(new RoutedEventArgs(MediaOpenedEvent, this));
    }

    private void RaiseMediaEnded()
    {
        _isPlaying = false;
        _isPaused = false;
        RaiseEvent(new RoutedEventArgs(MediaEndedEvent, this));
    }

    private void RaiseMediaFailed(Exception exception)
    {
        _isPlaying = false;
        _isPaused = false;
        RaiseEvent(new ExceptionRoutedEventArgs(MediaFailedEvent, this, exception));
    }

    #endregion
}

#endregion

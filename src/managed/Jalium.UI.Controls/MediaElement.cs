using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Native;
using Jalium.UI.Media.Pipeline;
using Jalium.UI.Threading;
using SoundFlow.Enums;
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
    Manual,
    Play,
    Pause,
    Stop,
    Close
}

#endregion

#region 事件参数

public sealed class MediaFailedEventArgs : RoutedEventArgs
{
    public Exception Exception { get; }
    public string ErrorMessage => Exception.Message;

    public MediaFailedEventArgs(RoutedEvent routedEvent, object source, Exception exception)
        : base(routedEvent, source)
    {
        Exception = exception;
    }

    public MediaFailedEventArgs(Exception exception)
    {
        Exception = exception;
    }
}

public sealed class MediaScriptCommandEventArgs : EventArgs
{
    public string ParameterType { get; }
    public string ParameterValue { get; }

    public MediaScriptCommandEventArgs(string parameterType, string parameterValue)
    {
        ParameterType = parameterType;
        ParameterValue = parameterValue;
    }
}

#endregion

#region Duration 结构

public struct Duration
{
    private readonly TimeSpan _timeSpan;
    private readonly DurationType _type;

    public static Duration Automatic => new(DurationType.Automatic);
    public static Duration Forever => new(DurationType.Forever);

    private Duration(DurationType type)
    {
        _type = type;
        _timeSpan = TimeSpan.Zero;
    }

    public Duration(TimeSpan timeSpan)
    {
        _type = DurationType.TimeSpan;
        _timeSpan = timeSpan;
    }

    public bool HasTimeSpan => _type == DurationType.TimeSpan;

    public TimeSpan TimeSpan => _type == DurationType.TimeSpan
        ? _timeSpan
        : throw new InvalidOperationException("Duration does not have a TimeSpan value.");

    public static implicit operator Duration(TimeSpan timeSpan) => new(timeSpan);

    private enum DurationType
    {
        Automatic,
        TimeSpan,
        Forever
    }
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
/// 池化的解码视频帧。<see cref="Pixels"/> 是从 <see cref="ArrayPool{Byte}.Shared"/> 租用的 BGRA8
/// 缓冲，调用方必须 <see cref="Dispose"/> 归还，否则会让池被逐渐抽空。
/// </summary>
public sealed class VideoFrame : IDisposable
{
    private byte[]? _buffer;
    private readonly int _length;

    public ReadOnlySpan<byte> Pixels =>
        _buffer is null
            ? ReadOnlySpan<byte>.Empty
            : _buffer.AsSpan(0, _length);

    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }
    public long TimestampMs { get; }
    public int FrameIndex { get; }

    public VideoFrame(int width, int height, int stride, long timestampMs, int frameIndex)
    {
        Width = width;
        Height = height;
        Stride = stride;
        TimestampMs = timestampMs;
        FrameIndex = frameIndex;
        _length = checked(stride * height);
        _buffer = ArrayPool<byte>.Shared.Rent(_length);
    }

    /// <summary>把源像素拷贝到内部缓冲。源 stride 必须 == <see cref="Stride"/>。</summary>
    internal void CopyFrom(ReadOnlySpan<byte> source)
    {
        if (_buffer is null) throw new ObjectDisposedException(nameof(VideoFrame));
        if (source.Length < _length)
            throw new ArgumentException("Source span is smaller than the frame buffer.", nameof(source));
        source.Slice(0, _length).CopyTo(_buffer);
    }

    public void Dispose()
    {
        var buffer = _buffer;
        if (buffer is null) return;
        _buffer = null;
        ArrayPool<byte>.Shared.Return(buffer, clearArray: false);
    }
}

public sealed class VideoFrameBuffer : IDisposable
{
    private readonly BlockingCollection<VideoFrame> _frameQueue;
    private readonly int _maxSize;

    public VideoFrameBuffer(int maxSize = 3)
    {
        _maxSize = maxSize;
        _frameQueue = new BlockingCollection<VideoFrame>(maxSize);
    }

    public bool TryAdd(VideoFrame frame, int timeoutMs = 0)
    {
        if (timeoutMs > 0)
        {
            return _frameQueue.TryAdd(frame, timeoutMs);
        }
        try
        {
            _frameQueue.Add(frame);
            return true;
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
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public bool TryTake(out VideoFrame? frame)
    {
        return _frameQueue.TryTake(out frame);
    }

    public void Clear()
    {
        while (_frameQueue.TryTake(out var frame))
        {
            frame?.Dispose();
        }
    }

    public int Count => _frameQueue.Count;

    public void Dispose()
    {
        Clear();
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
                _baseMediaTime += TimeSpan.FromTicks((long)(_systemClock.Elapsed.Ticks / _speedRatio));
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

            var elapsed = TimeSpan.FromTicks((long)(_systemClock.Elapsed.Ticks / _speedRatio));
            return _baseMediaTime + elapsed;
        }
    }

    public double SpeedRatio
    {
        get => _speedRatio;
        set => _speedRatio = Math.Clamp(value, 0.1, 10.0);
    }

    public TimeSpan CalculateVideoDelay(double framePresentationTimeMs)
    {
        var currentTime = GetMediaTime().TotalMilliseconds;
        var delay = framePresentationTimeMs - currentTime;
        return TimeSpan.FromMilliseconds(delay);
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

public class MediaElement : FrameworkElement, IDisposable
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
    private VideoFrameBuffer? _frameBuffer;
    private string? _mediaPath;
    private TimeSpan _position;
    private TimeSpan _duration;
    private bool _isPlaying;
    private bool _isPaused;
    private bool _hasVideo;
    private bool _hasAudio;
    private CancellationTokenSource? _playbackCts;
    private Task? _videoDecodeTask;
    private Task? _videoRenderTask;
    private Task? _audioSyncTask;
    private readonly object _lock = new();

    private WriteableBitmap? _frameBitmap;
    private readonly SolidColorBrush _backgroundBrush = new(Color.FromRgb(0, 0, 0));
    private int _videoWidth;
    private int _videoHeight;
    private double _videoFps;
    private double _frameDelayMs;

    private double _currentVolume = 1;
    private bool _isMuted;

    private AVSyncClock? _syncClock;

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
            new PropertyMetadata(null, OnSourceChanged));

    public static readonly DependencyProperty VolumeProperty =
        DependencyProperty.Register(nameof(Volume), typeof(double), typeof(MediaElement),
            new PropertyMetadata(1, OnVolumeChanged, CoerceVolume));

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
            typeof(EventHandler<MediaFailedEventArgs>), typeof(MediaElement));

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

    public event EventHandler<MediaFailedEventArgs>? MediaFailed
    {
        add => AddHandler(MediaFailedEvent, value!);
        remove => RemoveHandler(MediaFailedEvent, value!);
    }

    public event RoutedEventHandler? BufferingStarted;
    public event RoutedEventHandler? BufferingEnded;
    public event EventHandler<MediaScriptCommandEventArgs>? ScriptCommand;

    /// <summary>Raises the <see cref="BufferingStarted"/> event. For internal use by media backends.</summary>
    protected virtual void OnBufferingStarted() => BufferingStarted?.Invoke(this, new RoutedEventArgs());

    /// <summary>Raises the <see cref="BufferingEnded"/> event. For internal use by media backends.</summary>
    protected virtual void OnBufferingEnded() => BufferingEnded?.Invoke(this, new RoutedEventArgs());

    /// <summary>Raises the <see cref="ScriptCommand"/> event. For internal use by media backends.</summary>
    protected virtual void OnScriptCommand(MediaScriptCommandEventArgs e) => ScriptCommand?.Invoke(this, e);

    #endregion

    #region CLR 属性

    public Uri? Source
    {
        get => (Uri?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
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

    public Duration NaturalDuration => _duration == TimeSpan.Zero
        ? Duration.Automatic
        : new Duration(_duration);

    public int NaturalVideoWidth => _videoWidth;
    public int NaturalVideoHeight => _videoHeight;
    public bool HasAudio => _hasAudio;
    public bool HasVideo => _hasVideo;
    public double BufferingProgress => 0.0;
    public bool IsBuffering { get; private set; }
    public bool CanPause { get; private set; } = true;
    public bool IsPlaying => _isPlaying;
    public string SyncStats => $"Frames: {_framesRendered} Dropped: {_framesDropped} Late: {_framesLate}";

    #endregion

    #region 构造函数与析构函数

    public MediaElement()
    {
        Unloaded += OnUnloaded;
        _audioManager = new AudioPlayer();
    }

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
        StopPlayback();
        _syncClock?.Dispose();
        _videoDecoder?.Dispose();
        _frameBuffer?.Dispose();
        _audioManager?.Dispose();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ApplyUnloadedBehavior();
    }

    #endregion

    #region 公共方法

    public void Play()
    {
        if (_isPlaying) return;

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
        if (!_isPlaying || _isPaused) return;

        _isPlaying = false;
        _isPaused = true;
        PausePlayback();
    }

    public void Stop()
    {
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

    #endregion

    #region 布局与渲染

    protected override Size MeasureOverride(Size availableSize)
    {
        if (!_hasVideo || _videoWidth <= 0 || _videoHeight <= 0)
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

    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc)
            return;

        var rect = new Rect(RenderSize);

        if (!_hasVideo && _hasAudio)
        {
            dc.DrawRectangle(_backgroundBrush, null, rect);

            var text = new FormattedText("Audio Only", "Segoe UI", 14)
            {
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200))
            };
            dc.DrawText(text, new Point((rect.Width - text.Width) / 2, (rect.Height - text.Height) / 2));
            return;
        }

        dc.DrawRectangle(_backgroundBrush, null, rect);

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
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200))
            };
            dc.DrawText(text, new Point((rect.Width - text.Width) / 2, (rect.Height - text.Height) / 2));
        }
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
        if (newSource != null)
        {
            var path = newSource.IsFile ? newSource.LocalPath : newSource.ToString();
            _mediaPath = path;
            OpenMedia(path);
        }
        else
        {
            _mediaPath = null;
            CloseMedia();
        }
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

    private static void OnBalanceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) { }

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
            if (media._syncClock != null)
            {
                media._syncClock.SpeedRatio = ratio;
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
        _frameBitmap = null;
        DropPendingDisplayFrame();
        InvalidateVisual();

        var generation = ++_openMediaGeneration;
        var dispatcher = Dispatcher.MainDispatcher;

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

            try
            {
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
                catch
                {
                    probe.Dispose();
                    probe = null;
                }

                // audio probe — Open/Stop 序列只为知道是否有 audio track + 拿 duration。
                // 完整启动 audio device 在后台线程完成,UI 线程不感知。
                var audioMgr = _audioManager;
                if (audioMgr != null)
                {
                    try
                    {
                        audioMgr.Close();
                        audioMgr.Open(BuildSourceUri(source));
                        if (audioMgr.HasAudio)
                        {
                            probeHasAudio = true;
                            audioDurationSeconds = audioMgr.NaturalDuration?.TotalSeconds ?? 0;
                            audioMgr.Stop();
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                probeError = ex;
                probe?.Dispose();
                probe = null;
            }

            var capturedDecoder = probe;
            dispatcher?.BeginInvoke(() =>
            {
                // 期间用户切换了 Source — 旧探测结果作废。
                if (generation != _openMediaGeneration)
                {
                    capturedDecoder?.Dispose();
                    return;
                }

                if (probeError != null)
                {
                    RaiseMediaFailed(probeError);
                    return;
                }

                _videoDecoder = capturedDecoder;
                _hasVideo = probeHasVideo;
                _hasAudio = probeHasAudio;
                _videoWidth = probeWidth;
                _videoHeight = probeHeight;
                _videoFps = probeFps;

                var totalDuration = probeDuration;
                if (totalDuration <= 0 && audioDurationSeconds > 0)
                {
                    totalDuration = audioDurationSeconds;
                }
                _duration = TimeSpan.FromSeconds(totalDuration);
                _frameDelayMs = 1000.0 / _videoFps;
                _videoStartTimeMs = 0;

                _syncClock?.Dispose();
                _syncClock = new AVSyncClock();
                _syncClock.SpeedRatio = SpeedRatio;

                _frameBuffer?.Dispose();
                _frameBuffer = new VideoFrameBuffer(3);

                if (_hasVideo)
                {
                    UpdateDisplayScale(RenderSize);
                    InvalidateMeasure();
                }

                RaiseMediaOpened();
                ApplyLoadedBehavior(LoadedBehavior);
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

    private void StartPlaybackInternal()
    {
        if (string.IsNullOrEmpty(_mediaPath))
            return;

        if (!_isArranged || _arrangedSize.Width <= 0 || _arrangedSize.Height <= 0)
        {
            _pendingPlay = true;
            return;
        }

        StartPlaybackWithSize(_arrangedSize);
    }

    private void StartPlaybackWithSize(Size targetSize)
    {
        _playbackCts = new CancellationTokenSource();
        var token = _playbackCts.Token;

        _framesRendered = 0;
        _framesDropped = 0;
        _framesLate = 0;

        var (videoWidth, videoHeight) = CalculateVideoRenderSize(targetSize);
        _targetWidth = videoWidth;
        _targetHeight = videoHeight;

        // 首次播放或Stop后，从0开始
        _videoStartTimeMs = _position.TotalMilliseconds;

        if (_hasAudio && _audioManager != null)
        {
            _audioManager.Volume = _currentVolume;
            _audioManager.IsMuted = _isMuted;

            if (_position > TimeSpan.Zero)
                _audioManager.Seek(_position);

            _audioManager.Play();
            // 音频启动延迟
            Thread.Sleep(1);
        }

        _syncClock?.Start(_position);

        if (_hasAudio)
        {
            _audioSyncTask = Task.Run(() => AudioSyncLoop(token), token);
        }

        if (_hasVideo && videoWidth > 0 && videoHeight > 0)
        {
            StartVideoPlayback(token, videoWidth, videoHeight);
        }

        if (!_hasVideo && _hasAudio)
        {
            _videoRenderTask = Task.Run(() => AudioOnlyPositionLoop(token), token);
        }
    }

    private void StartVideoPlayback(CancellationToken token, int videoWidth, int videoHeight)
    {
        try
        {
            EnsureVideoDecoderForPlayback();
        }
        catch (Exception ex)
        {
            RaiseMediaFailed(ex);
            return;
        }

        if (_position > TimeSpan.Zero)
        {
            try { _videoDecoder!.Seek(_position); }
            catch { }
        }

        _videoDecodeTask = Task.Run(() => VideoDecodeLoop(token, videoWidth, videoHeight), token);
        _videoRenderTask = Task.Run(() => VideoRenderLoop(token), token);
    }

    private void EnsureVideoDecoderForPlayback()
    {
        if (_videoDecoder is null)
        {
            _videoDecoder = GetVideoDecoderFactory().Create();
            _videoDecoder.Open(BuildSourceUri(_mediaPath!));
        }
    }

    /// <summary>
    /// 不阻塞 UI 线程的 Resume — 旧 task 的退出 + decoder Seek + 启动新 task 全部
    /// 在 ThreadPool 上完成，UI 线程几个字段赋值就返回。
    /// IMFSourceReader 支持 SetCurrentPosition 后继续 ReadSample — 不再每次重建 decoder。
    /// </summary>
    private void ResumePlayback()
    {
        var oldCts = _playbackCts;
        var oldDecodeTask = _videoDecodeTask;
        var oldRenderTask = _videoRenderTask;
        var oldAudioTask = _audioSyncTask;

        var newCts = new CancellationTokenSource();
        var token = newCts.Token;
        _playbackCts = newCts;
        _videoDecodeTask = null;
        _videoRenderTask = null;
        _audioSyncTask = null;
        _videoStartTimeMs = _position.TotalMilliseconds;
        _isPlaying = true;
        _isPaused = false;

        // audio.Play 是 lock 包裹的轻量调用 — 安全在 UI 线程上做。
        if (_hasAudio && _audioManager != null)
        {
            _audioManager.Play();
        }
        _syncClock?.Resume();
        _frameBuffer?.Clear();

        var resumeWidth = _targetWidth;
        var resumeHeight = _targetHeight;
        var resumePosition = _position;
        var hasVideo = _hasVideo;
        var hasAudio = _hasAudio;

        Task.Run(async () =>
        {
            try { oldCts?.Cancel(); } catch { }
            try { if (oldDecodeTask != null) await oldDecodeTask.ConfigureAwait(false); } catch { }
            try { if (oldRenderTask != null) await oldRenderTask.ConfigureAwait(false); } catch { }
            try { if (oldAudioTask != null) await oldAudioTask.ConfigureAwait(false); } catch { }
            try { oldCts?.Dispose(); } catch { }

            if (token.IsCancellationRequested) return;

            // 老 task 已退，_videoDecoder 现在是单线程访问。Seek 安全。
            try
            {
                _videoDecoder?.Seek(resumePosition);
            }
            catch
            {
                return;
            }

            if (token.IsCancellationRequested) return;

            if (hasVideo && resumeWidth > 0 && resumeHeight > 0 && _videoDecoder != null)
            {
                _videoDecodeTask = Task.Run(() => VideoDecodeLoop(token, resumeWidth, resumeHeight), token);
                _videoRenderTask = Task.Run(() => VideoRenderLoop(token), token);
            }
            if (hasAudio)
            {
                _audioSyncTask = Task.Run(() => AudioSyncLoop(token), token);
            }
        });
    }

    /// <summary>
    /// 在 UI 线程瞬间完成 Pause — 不再 task.Wait。decode/render task 看到 cancel 后自然退出，
    /// 它们持有的资源由 task 自身或下次 OpenMedia/StopPlayback 清理。
    /// </summary>
    private void PausePlayback()
    {
        _syncClock?.Pause();
        if (_syncClock != null)
        {
            _position = _syncClock.GetMediaTime();
        }

        // audio 的 Pause 是 lock 包裹的 SoundPlayer.Pause — 几个微秒级别，安全在 UI 线程。
        _audioManager?.Pause();

        // cancel token 让 VideoRenderLoop 从 BlockingCollection.Take 立即醒来。
        _playbackCts?.Cancel();
    }

    /// <summary>
    /// 不阻塞 UI 线程。把 decoder / 老 task 的 dispose 工作扔到 ThreadPool。
    /// </summary>
    private void StopPlayback()
    {
        var oldCts = _playbackCts;
        var oldDecodeTask = _videoDecodeTask;
        var oldRenderTask = _videoRenderTask;
        var oldAudioTask = _audioSyncTask;
        var oldDecoder = _videoDecoder;

        _playbackCts = null;
        _videoDecodeTask = null;
        _videoRenderTask = null;
        _audioSyncTask = null;
        _videoDecoder = null;
        _isPlaying = false;
        _pendingPlay = false;
        _syncClock?.Stop();
        _frameBuffer?.Clear();
        DropPendingDisplayFrame();
        _audioManager?.Stop();

        if (oldCts == null && oldDecoder == null) return;

        try { oldCts?.Cancel(); } catch { }

        if (oldDecodeTask == null && oldRenderTask == null && oldAudioTask == null)
        {
            try { oldCts?.Dispose(); } catch { }
            try { oldDecoder?.Dispose(); } catch { }
            return;
        }

        // 后台等老 task 退出再 dispose 资源；UI 线程立即返回。
        Task.Run(async () =>
        {
            try { if (oldDecodeTask != null) await oldDecodeTask.ConfigureAwait(false); } catch { }
            try { if (oldRenderTask != null) await oldRenderTask.ConfigureAwait(false); } catch { }
            try { if (oldAudioTask != null) await oldAudioTask.ConfigureAwait(false); } catch { }
            try { oldCts?.Dispose(); } catch { }
            try { oldDecoder?.Dispose(); } catch { }
        });
    }

    private void CloseMedia()
    {
        StopPlayback();

        // _audioManager.Close 会停止 device 并 dispose stream — 100ms+，移到后台。
        var audioMgr = _audioManager;
        if (audioMgr != null)
        {
            Task.Run(() =>
            {
                try { audioMgr.Close(); } catch { }
            });
        }

        _frameBuffer?.Dispose();
        _frameBuffer = null;
        DropPendingDisplayFrame();

        _frameBitmap = null;
        _position = TimeSpan.Zero;
        _duration = TimeSpan.Zero;
        _hasVideo = false;
        _hasAudio = false;
        _videoWidth = 0;
        _videoHeight = 0;
        _isArranged = false;
        _arrangedSize = Size.Empty;
        _pendingPlay = false;
        _isPaused = false;
        _videoStartTimeMs = 0;

        InvalidateMeasure();
        InvalidateVisual();
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
    /// 视频解码循环 — 使用 <see cref="INativeVideoDecoder"/> (Windows MF / Android MediaCodec)
    /// 直出 BGRA8 帧。GPU 采样器在 OnRender 时承担缩放，CPU 不再做 resize。
    /// </summary>
    private void VideoDecodeLoop(CancellationToken token, int targetWidth, int targetHeight)
    {
        if (_videoDecoder is null) return;

        try
        {
            var frameIndex = 0L;
            var startTimeMs = _videoStartTimeMs;

            while (!token.IsCancellationRequested && _isPlaying)
            {
                MediaFrame? mediaFrame = null;
                bool hasFrame;
                try
                {
                    hasFrame = _videoDecoder.TryReadFrame(out mediaFrame);
                }
                catch
                {
                    break;
                }

                if (!hasFrame || mediaFrame is null) break;

                using (mediaFrame)
                {
                    var pts = mediaFrame.PresentationTime.TotalMilliseconds;
                    if (pts <= 0)
                    {
                        // 容器没给 PTS — 退化为按 fps 推算
                        pts = startTimeMs + frameIndex * _frameDelayMs;
                    }

                    var frame = new VideoFrame(
                        mediaFrame.Width,
                        mediaFrame.Height,
                        mediaFrame.Stride,
                        (long)pts,
                        (int)frameIndex);
                    frame.CopyFrom(mediaFrame.Pixels.Span);

                    if (!_frameBuffer!.TryAdd(frame, 50))
                    {
                        if (_frameBuffer.TryTake(out var oldFrame))
                        {
                            oldFrame?.Dispose();
                        }
                        if (!_frameBuffer.TryAdd(frame))
                        {
                            // 缓冲在我们丢弃旧帧后被关闭 — 归还当前帧避免泄漏。
                            frame.Dispose();
                        }
                    }

                    frameIndex++;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    private async Task VideoRenderLoop(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && _isPlaying)
            {
                var frame = _frameBuffer?.Take(token);
                if (frame == null) continue;

                var delay = _syncClock!.CalculateVideoDelay(frame.TimestampMs);

                // 如果帧时间戳落后太多，直接丢弃，避免追赶造成卡顿
                if (delay < TimeSpan.FromMilliseconds(-AVSyncClock.VideoCatchUpThresholdMs))
                {
                    _framesDropped++;
                    frame.Dispose();
                    continue;
                }

                // 如果延迟很小，直接渲染，不等待
                if (delay > TimeSpan.FromMilliseconds(2))
                {
                    await PreciseDelay(delay, token);
                }

                _position = _syncClock.GetMediaTime();
                _framesRendered++;

                // 把当前帧 atomic 换入显示槽，旧帧立即归还 ArrayPool。
                // 这样无论 UI 线程多卡，内存上限永远是 _frameBuffer (3) + 槽位 (1) = 4 帧。
                var oldPending = Interlocked.Exchange(ref _pendingDisplayFrame, frame);
                oldPending?.Dispose();

                RequestUiRefresh();
            }
        }
        catch (OperationCanceledException) { }
        catch { }
    }

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

    private void AudioSyncLoop(CancellationToken token)
    {
        try
        {
            // 减少初始延迟
            Thread.Sleep(50);

            while (!token.IsCancellationRequested && _isPlaying)
            {
                if (_audioManager != null && _syncClock != null)
                {
                    // 使用音频实际位置更新同步时钟
                    var audioPos = _audioManager.Position.TotalSeconds;
                    _syncClock.UpdateAudioPosition(audioPos);
                    _position = _audioManager.Position;
                }

                // 降低同步频率，减少CPU占用
                Thread.Sleep(20);
            }
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    private async Task AudioOnlyPositionLoop(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && _isPlaying)
            {
                if (_audioManager != null)
                {
                    _position = _audioManager.Position;
                }

                if (_duration > TimeSpan.Zero && _position >= _duration)
                {
                    Dispatcher.MainDispatcher?.BeginInvoke(() => RaiseMediaEnded());
                    break;
                }

                try
                {
                    await Task.Delay(100, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    #endregion

    #region 辅助方法

    private async Task PreciseDelay(TimeSpan delay, CancellationToken token)
    {
        if (delay <= TimeSpan.Zero) return;

        const int taskDelayThresholdMs = 16; // 降低到一帧的时间

        if (delay.TotalMilliseconds > taskDelayThresholdMs)
        {
            var remaining = delay - TimeSpan.FromMilliseconds(taskDelayThresholdMs);
            try
            {
                await Task.Delay(remaining, token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }

            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < delay - remaining && !token.IsCancellationRequested)
            {
                Thread.SpinWait(1);
            }
        }
        else
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < delay && !token.IsCancellationRequested)
            {
                Thread.SpinWait(1);
            }
        }
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
        if (width <= 0 || height <= 0) return;
        if (width > 8192 || height > 8192) return;

        var pixels = frame.Pixels;
        if (pixels.IsEmpty) return;

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
                return;
            }
        }

        try
        {
            _frameBitmap!.WritePixels(new Int32Rect(0, 0, width, height), pixels, frame.Stride);
            InvalidateVisual();
        }
        catch { }
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
        var wasPlaying = _isPlaying;
        var wasPaused = _isPaused;

        if (_isPlaying)
        {
            StopPlayback();
        }

        _position = position;
        _videoStartTimeMs = position.TotalMilliseconds;

        var willResume = wasPlaying || wasPaused;
        var audioMgr = _audioManager;
        var dispatcher = Dispatcher.MainDispatcher;

        Task.Run(() =>
        {
            try { audioMgr?.Seek(position); } catch { }

            if (!willResume) return;

            dispatcher?.BeginInvoke(() =>
            {
                _isPlaying = true;
                _isPaused = false;
                StartPlaybackInternal();
            });
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
        RaiseEvent(new MediaFailedEventArgs(MediaFailedEvent, this, exception));
    }

    #endregion
}

#endregion

using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using Jalium.UI.Threading;
using OpenCvSharp;
using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Codecs.FFMpeg;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Providers;
using SoundFlow.Structs;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
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

#region 文件日志记录器

public static class FileLogger
{
    private static readonly object _lock = new();
    private static string _logPath = Path.Combine(AppContext.BaseDirectory, "mediaplayer.log");

    public static void Log(string message)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var logMessage = $"[{timestamp}] {message}";

        lock (_lock)
        {
            try
            {
                File.AppendAllText(_logPath, logMessage + Environment.NewLine);
            }
            catch { }
        }

        Debug.WriteLine(logMessage);
    }

    public static void LogError(string message, Exception? ex = null)
    {
        var logMessage = $"[ERROR] {message}";
        if (ex != null)
        {
            logMessage += $" - Exception: {ex.GetType().Name}: {ex.Message}";
        }
        Log(logMessage);
    }
}

#endregion

#region 视频帧缓冲队列

public sealed class VideoFrame : IDisposable
{
    public byte[] Data { get; }
    public int Width { get; }
    public int Height { get; }
    public long TimestampMs { get; }  // 绝对时间戳（从视频开始计算的毫秒数）
    public int FrameIndex { get; }

    public VideoFrame(byte[] data, int width, int height, long timestampMs, int frameIndex)
    {
        Data = data;
        Width = width;
        Height = height;
        TimestampMs = timestampMs;
        FrameIndex = frameIndex;
    }

    public void Dispose() { }
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

#region 音频管理器

public sealed class AudioPlaybackManager : IDisposable
{
    private MiniAudioEngine? _audioEngine;
    private AudioPlaybackDevice? _playbackDevice;
    private SoundPlayer? _soundPlayer;
    private StreamDataProvider? _dataProvider;
    private FileStream? _fileStream;
    private readonly object _lock = new();
    private bool _disposed;
    private double _currentVolume = 1.0;
    private bool _isMuted;
    private string? _currentFilePath;

    // 使用音频引擎内部时间，而不是自己计算
    private TimeSpan _basePosition;
    private DateTime _lastPlayTime;
    private bool _isActuallyPlaying;

    private const int FORCED_SAMPLE_RATE = 44100;
    private const int FORCED_CHANNELS = 2;
    private const SampleFormat FORCED_FORMAT = SampleFormat.F32;

    public TimeSpan CurrentPosition
    {
        get
        {
            lock (_lock)
            {
                if (_soundPlayer == null) return TimeSpan.Zero;

                // 优先使用 SoundPlayer 的内部位置（如果有）
                // 否则基于已播放时间计算
                if (_isActuallyPlaying && _soundPlayer.State == PlaybackState.Playing)
                {
                    var elapsed = DateTime.Now - _lastPlayTime;
                    return _basePosition + elapsed;
                }
                return _basePosition;
            }
        }
    }

    public TimeSpan Duration { get; private set; }

    public AudioPlaybackManager()
    {
        InitializeEngineAndCodecs();
    }

    private void InitializeEngineAndCodecs()
    {
        try
        {
            _audioEngine = new MiniAudioEngine();
            FileLogger.Log($"AudioPlaybackManager: Engine initialized");

            var factory = new FFmpegCodecFactory();
            TryRegisterFactory(factory);
        }
        catch (Exception ex)
        {
            FileLogger.LogError("Failed to initialize engine and codecs", ex);
        }
    }

    private void TryRegisterFactory(FFmpegCodecFactory factory)
    {
        try
        {
            var engineType = typeof(AudioEngine);
            var registerMethod = engineType.GetMethod("RegisterCodecFactory",
                BindingFlags.Public | BindingFlags.Instance);

            if (registerMethod != null && _audioEngine != null)
            {
                registerMethod.Invoke(_audioEngine, new object[] { factory });
                FileLogger.Log("FFmpeg codec factory registered");
                return;
            }
        }
        catch (Exception ex)
        {
            FileLogger.LogError("Error in TryRegisterFactory", ex);
        }
    }

    private bool EnsureEngineInitialized()
    {
        try
        {
            if (_audioEngine == null)
            {
                InitializeEngineAndCodecs();
            }
            return _audioEngine != null;
        }
        catch (Exception ex)
        {
            FileLogger.LogError("Failed to ensure engine initialization", ex);
            return false;
        }
    }

    public bool OpenAndPlay(string filePath)
    {
        lock (_lock)
        {
            try
            {
                Close();

                if (!EnsureEngineInitialized())
                    return false;

                _currentFilePath = filePath;
                Duration = TimeSpan.Zero;

                var defaultDevice = _audioEngine!.PlaybackDevices.FirstOrDefault(x => x.IsDefault);
                if (defaultDevice.Name == null)
                {
                    defaultDevice = _audioEngine.PlaybackDevices.FirstOrDefault();
                }

                if (string.IsNullOrEmpty(defaultDevice.Name))
                {
                    FileLogger.LogError("No playback device found");
                    return false;
                }

                var forcedFormat = new AudioFormat
                {
                    SampleRate = FORCED_SAMPLE_RATE,
                    Channels = FORCED_CHANNELS,
                    Format = FORCED_FORMAT
                };

                _playbackDevice = _audioEngine.InitializePlaybackDevice(defaultDevice, forcedFormat);
                _fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    bufferSize: 262144, useAsync: true);

                _dataProvider = new StreamDataProvider(_audioEngine, forcedFormat, _fileStream);
                _soundPlayer = new SoundPlayer(_audioEngine, forcedFormat, _dataProvider);

                UpdateVolume();
                _playbackDevice.MasterMixer.AddComponent(_soundPlayer);

                _basePosition = TimeSpan.Zero;
                _isActuallyPlaying = false;

                FileLogger.Log($"Audio opened: {filePath}");
                return true;
            }
            catch (Exception ex)
            {
                FileLogger.LogError($"Failed to open audio: {filePath}", ex);
                Cleanup();
                return false;
            }
        }
    }

    public bool Play()
    {
        lock (_lock)
        {
            if (_soundPlayer == null || _playbackDevice == null) return false;

            try
            {
                if (_soundPlayer.State == PlaybackState.Playing)
                    return true;

                if (_soundPlayer.State == PlaybackState.Paused)
                {
                    // 恢复播放时，更新基准时间
                    _soundPlayer.Play();
                    _lastPlayTime = DateTime.Now;
                    _isActuallyPlaying = true;
                    FileLogger.Log($"Audio resumed from {_basePosition}");
                    return true;
                }

                _playbackDevice.Start();
                _soundPlayer.Play();
                _lastPlayTime = DateTime.Now;
                _isActuallyPlaying = true;

                FileLogger.Log("Audio started");
                return true;
            }
            catch (Exception ex)
            {
                FileLogger.LogError("Audio play failed", ex);
                return false;
            }
        }
    }

    public bool Pause()
    {
        lock (_lock)
        {
            if (_soundPlayer == null) return false;

            try
            {
                if (_soundPlayer.State == PlaybackState.Playing)
                {
                    _soundPlayer.Pause();
                    // 暂停时，累加已播放时间到基准位置
                    var elapsed = DateTime.Now - _lastPlayTime;
                    _basePosition += elapsed;
                    _isActuallyPlaying = false;
                    FileLogger.Log($"Audio paused at {_basePosition}");
                }
                return true;
            }
            catch (Exception ex)
            {
                FileLogger.LogError("Audio pause failed", ex);
                return false;
            }
        }
    }

    public bool Stop()
    {
        lock (_lock)
        {
            if (_soundPlayer == null) return false;

            try
            {
                _soundPlayer.Stop();
                _basePosition = TimeSpan.Zero;
                _isActuallyPlaying = false;
                FileLogger.Log("Audio stopped");
                return true;
            }
            catch (Exception ex)
            {
                FileLogger.LogError("Audio stop failed", ex);
                return false;
            }
        }
    }

    public bool Seek(TimeSpan position)
    {
        lock (_lock)
        {
            if (_audioEngine == null || _playbackDevice == null || string.IsNullOrEmpty(_currentFilePath))
                return false;

            try
            {
                FileLogger.Log($"Audio seeking to {position.TotalSeconds:F2}s");

                // Seek 时重置内部位置状态
                _soundPlayer?.Stop();
                _playbackDevice.MasterMixer.RemoveComponent(_soundPlayer!);
                _dataProvider?.Dispose();
                _fileStream?.Dispose();

                _fileStream = new FileStream(_currentFilePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    bufferSize: 262144, useAsync: true);

                var forcedFormat = new AudioFormat
                {
                    SampleRate = FORCED_SAMPLE_RATE,
                    Channels = FORCED_CHANNELS,
                    Format = FORCED_FORMAT
                };

                _dataProvider = new StreamDataProvider(_audioEngine, forcedFormat, _fileStream);
                _soundPlayer = new SoundPlayer(_audioEngine, forcedFormat, _dataProvider);

                UpdateVolume();
                _playbackDevice.MasterMixer.AddComponent(_soundPlayer);

                // 更新基准位置为 Seek 目标位置
                _basePosition = position;

                // 如果正在播放，继续播放
                if (_isActuallyPlaying)
                {
                    _lastPlayTime = DateTime.Now;
                    _soundPlayer.Play();
                }

                FileLogger.Log($"Audio seek completed");
                return true;
            }
            catch (Exception ex)
            {
                FileLogger.LogError($"Audio seek failed", ex);
                return false;
            }
        }
    }

    public void SetVolume(double volume)
    {
        _currentVolume = Math.Clamp(volume, 0.0, 1.0);
        UpdateVolume();
    }

    public void SetMuted(bool muted)
    {
        _isMuted = muted;
        UpdateVolume();
    }

    private void UpdateVolume()
    {
        lock (_lock)
        {
            if (_soundPlayer != null)
            {
                _soundPlayer.Volume = _isMuted ? 0.0f : (float)_currentVolume;
            }
        }
    }

    public PlaybackState PlaybackState => _soundPlayer?.State ?? PlaybackState.Stopped;

    private void Cleanup()
    {
        if (_soundPlayer != null)
        {
            try
            {
                _playbackDevice?.MasterMixer.RemoveComponent(_soundPlayer);
                _soundPlayer.Stop();
                _soundPlayer.Dispose();
            }
            catch { }
            _soundPlayer = null;
        }

        _dataProvider?.Dispose();
        _dataProvider = null;
        _fileStream?.Dispose();
        _fileStream = null;

        if (_playbackDevice != null)
        {
            try
            {
                _playbackDevice.Stop();
                _playbackDevice.Dispose();
            }
            catch { }
            _playbackDevice = null;
        }
    }

    public void Close()
    {
        lock (_lock)
        {
            Cleanup();
            _basePosition = TimeSpan.Zero;
            _isActuallyPlaying = false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Close();
        _audioEngine?.Dispose();
        _audioEngine = null;
    }
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
            FileLogger.Log($"AVSyncClock started at {startPosition.TotalSeconds:F3}s");
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
                FileLogger.Log($"AVSyncClock paused at {_baseMediaTime.TotalSeconds:F3}s");
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
                FileLogger.Log($"AVSyncClock resumed, base time {_baseMediaTime.TotalSeconds:F3}s");
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

    private VideoCapture? _videoCapture;
    private AudioPlaybackManager? _audioManager;
    private VideoFrameBuffer? _frameBuffer;
    private string? _mediaPath;
    private TimeSpan _position;
    private TimeSpan _duration;
    private bool _isPlaying;
    private bool _isPaused;
    private bool _hasVideo;
    private bool _hasAudio;
    private double _bufferingProgress;
    private CancellationTokenSource? _playbackCts;
    private Task? _videoDecodeTask;
    private Task? _videoRenderTask;
    private Task? _audioSyncTask;
    private readonly object _lock = new();

    private WriteableBitmap? _frameBitmap;
    private ImageSource? _frameImage;
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
    public double BufferingProgress => _bufferingProgress;
    public bool IsBuffering { get; private set; }
    public bool CanPause { get; private set; } = true;
    public bool IsPlaying => _isPlaying;
    public string SyncStats => $"Frames: {_framesRendered} Dropped: {_framesDropped} Late: {_framesLate}";

    #endregion

    #region 构造函数与析构函数

    public MediaElement()
    {
        Unloaded += OnUnloaded;
        _audioManager = new AudioPlaybackManager();
        FileLogger.Log("MediaElement created");
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
        _videoCapture?.Dispose();
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

        FileLogger.Log("Play() called");

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

        FileLogger.Log("Pause() called");
        _isPlaying = false;
        _isPaused = true;
        PausePlayback();
    }

    public void Stop()
    {
        FileLogger.Log("Stop() called");
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
        FileLogger.Log("Close() called");
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

        var imageSourceToDraw = _frameImage as ImageSource ?? _frameBitmap as ImageSource;
        if (imageSourceToDraw != null && _hasVideo)
        {
            var frameSize = new Size(imageSourceToDraw.Width, imageSourceToDraw.Height);
            var scaledSize = ComputeScaledSize(rect.Size, frameSize);

            if (scaledSize.Width > 0 && scaledSize.Height > 0)
            {
                var x = (rect.Width - scaledSize.Width) / 2;
                var y = (rect.Height - scaledSize.Height) / 2;

                dc.DrawImage(imageSourceToDraw, new Rect(x, y, scaledSize.Width, scaledSize.Height));
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
            FileLogger.Log($"OnSourceChanged: {path}");
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

    private void OpenMedia(string source)
    {
        FileLogger.Log($"OpenMedia: {source}");

        try
        {
            StopPlayback();

            var hasVideo = false;
            var hasAudio = false;
            var videoWidth = 0;
            var videoHeight = 0;
            var videoFps = 30.0;
            var duration = 0.0;

            _videoCapture?.Dispose();
            _videoCapture = new VideoCapture(source);

            if (_videoCapture.IsOpened())
            {
                var videoInfo = GetVideoInfo(_videoCapture);
                hasVideo = videoInfo.HasVideo;
                videoWidth = videoInfo.VideoWidth;
                videoHeight = videoInfo.VideoHeight;
                videoFps = videoInfo.VideoFps;
                duration = videoInfo.Duration;

                FileLogger.Log($"Video: {videoWidth}x{videoHeight}@{videoFps}fps, {duration}s");
            }
            else
            {
                _videoCapture.Dispose();
                _videoCapture = null;
            }

            if (_audioManager != null)
            {
                _audioManager.Close();
                if (_audioManager.OpenAndPlay(source))
                {
                    hasAudio = true;
                    var audioDuration = _audioManager.Duration.TotalSeconds;
                    if (duration <= 0 && audioDuration > 0)
                        duration = audioDuration;
                    _audioManager.Stop();
                }
            }

            _hasVideo = hasVideo;
            _hasAudio = hasAudio;
            _videoWidth = videoWidth;
            _videoHeight = videoHeight;
            _videoFps = videoFps > 0 ? videoFps : 30.0;
            _duration = TimeSpan.FromSeconds(duration);
            _frameDelayMs = 1000.0 / _videoFps;
            _videoStartTimeMs = 0;

            _syncClock?.Dispose();
            _syncClock = new AVSyncClock();
            _syncClock.SpeedRatio = SpeedRatio;

            _frameBuffer?.Dispose();
            _frameBuffer = new VideoFrameBuffer(3); // 减小缓冲大小，降低延迟

            if (_hasVideo)
            {
                UpdateDisplayScale(RenderSize);
                InvalidateMeasure();
            }

            RaiseMediaOpened();
            ApplyLoadedBehavior(LoadedBehavior);
        }
        catch (Exception ex)
        {
            FileLogger.LogError("OpenMedia failed", ex);
            RaiseMediaFailed(ex);
        }
    }

    private MediaInfo GetVideoInfo(VideoCapture capture)
    {
        var info = new MediaInfo();

        info.VideoWidth = (int)capture.Get(VideoCaptureProperties.FrameWidth);
        info.VideoHeight = (int)capture.Get(VideoCaptureProperties.FrameHeight);
        info.VideoFps = capture.Get(VideoCaptureProperties.Fps);
        info.HasVideo = info.VideoWidth > 0 && info.VideoHeight > 0;

        var frameCount = capture.Get(VideoCaptureProperties.FrameCount);
        if (info.VideoFps > 0 && frameCount > 0)
        {
            info.Duration = frameCount / info.VideoFps;
        }

        if (info.VideoFps <= 0)
            info.VideoFps = 30.0;

        return info;
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
            _audioManager.SetVolume(_currentVolume);
            _audioManager.SetMuted(_isMuted);

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
        _videoCapture?.Dispose();
        _videoCapture = new VideoCapture(_mediaPath!);

        if (!_videoCapture.IsOpened())
        {
            RaiseMediaFailed(new InvalidOperationException("Failed to open video"));
            return;
        }

        // 定位到正确位置
        if (_position > TimeSpan.Zero)
        {
            var framePos = _position.TotalSeconds * _videoFps;
            _videoCapture.Set(VideoCaptureProperties.PosFrames, framePos);
        }

        _videoDecodeTask = Task.Run(() => VideoDecodeLoop(token, videoWidth, videoHeight), token);
        _videoRenderTask = Task.Run(() => VideoRenderLoop(token), token);
    }

    /// <summary>
    /// Resume时保持时间戳连续性，避免音视频错位
    /// </summary>
    private void ResumePlayback()
    {
        FileLogger.Log($"ResumePlayback: position={_position.TotalSeconds:F3}s");

        // 先确保之前的任务完全停止，避免资源冲突
        _playbackCts?.Cancel();
        try
        {
            _videoDecodeTask?.Wait(TimeSpan.FromMilliseconds(500));
            _videoRenderTask?.Wait(TimeSpan.FromMilliseconds(500));
        }
        catch { }

        _playbackCts = new CancellationTokenSource();
        var token = _playbackCts.Token;

        // 保持当前位置作为视频起始时间戳，不要累加！
        // 这样解码出的帧时间戳 = _position + (当前帧 - 起始帧) * frameDelay
        _videoStartTimeMs = _position.TotalMilliseconds;

        // 先启动音频，再启动视频，确保音频先运行
        if (_hasAudio && _audioManager != null)
        {
            _audioManager.Play();
        }

        // 同步时钟恢复，保持时间连续性
        _syncClock?.Resume();

        // 清空旧帧缓冲，避免显示旧帧造成卡顿感
        _frameBuffer?.Clear();

        if (_hasVideo && _targetWidth > 0 && _targetHeight > 0)
        {
            // 重新打开视频并定位到当前位置
            _videoCapture?.Dispose();
            _videoCapture = new VideoCapture(_mediaPath!);

            if (_videoCapture.IsOpened())
            {
                var framePos = _position.TotalSeconds * _videoFps;
                _videoCapture.Set(VideoCaptureProperties.PosFrames, framePos);

                FileLogger.Log($"Resume video at frame {framePos:F0}, timeMs={_videoStartTimeMs:F1}");

                _videoDecodeTask = Task.Run(() => VideoDecodeLoop(token, _targetWidth, _targetHeight), token);
                _videoRenderTask = Task.Run(() => VideoRenderLoop(token), token);
            }
        }

        if (_hasAudio)
        {
            _audioSyncTask = Task.Run(() => AudioSyncLoop(token), token);
        }

        _isPlaying = true;
        _isPaused = false;
    }

    private void PausePlayback()
    {
        // 先暂停时钟，确保时间停止在正确位置
        _syncClock?.Pause();
        _audioManager?.Pause();

        _playbackCts?.Cancel();

        try
        {
            // 减少等待时间，提高响应速度
            _videoDecodeTask?.Wait(TimeSpan.FromMilliseconds(500));
            _videoRenderTask?.Wait(TimeSpan.FromMilliseconds(500));
            _audioSyncTask?.Wait(TimeSpan.FromMilliseconds(200));
        }
        catch { }

        // 更新位置到暂停时的准确时间
        if (_syncClock != null)
        {
            _position = _syncClock.GetMediaTime();
        }
    }

    private void StopPlayback()
    {
        _playbackCts?.Cancel();

        try
        {
            _videoDecodeTask?.Wait(TimeSpan.FromSeconds(1));
            _videoRenderTask?.Wait(TimeSpan.FromSeconds(1));
            _audioSyncTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch { }

        _playbackCts?.Dispose();
        _playbackCts = null;
        _videoDecodeTask = null;
        _videoRenderTask = null;
        _audioSyncTask = null;
        _isPlaying = false;
        _pendingPlay = false;

        _syncClock?.Stop();
        _frameBuffer?.Clear();

        _audioManager?.Stop();
        _videoCapture?.Dispose();
        _videoCapture = null;
    }

    private void CloseMedia()
    {
        StopPlayback();

        _videoCapture?.Dispose();
        _videoCapture = null;
        _audioManager?.Close();
        _frameBuffer?.Dispose();
        _frameBuffer = null;

        _frameBitmap = null;
        _frameImage = null;
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
    /// 视频解码循环 - 使用绝对时间戳，确保暂停后继续播放时间正确
    /// </summary>
    private void VideoDecodeLoop(CancellationToken token, int targetWidth, int targetHeight)
    {
        // 获取当前视频位置（帧号）
        var currentFramePos = _videoCapture?.Get(VideoCaptureProperties.PosFrames) ?? 0;
        var startFrameIndex = (long)currentFramePos;

        FileLogger.Log($"VideoDecodeLoop: startFrame={startFrameIndex}, startTimeMs={_videoStartTimeMs:F1}");

        try
        {
            var frameIndex = startFrameIndex;

            using var mat = new Mat();

            while (!token.IsCancellationRequested && _isPlaying)
            {
                if (_videoCapture == null || !_videoCapture.Read(mat) || mat.Empty())
                {
                    FileLogger.Log("VideoDecodeLoop: End of stream");
                    break;
                }

                // 计算绝对时间戳：基于起始时间 + 相对偏移
                // 这样无论Resume多少次，时间戳都是正确的绝对时间
                var relativeFrameIndex = frameIndex - startFrameIndex;
                var framePts = _videoStartTimeMs + (relativeFrameIndex * _frameDelayMs);

                Mat processedMat;
                if (mat.Width != targetWidth || mat.Height != targetHeight)
                {
                    using var resizedMat = mat.Resize(new OpenCvSharp.Size(targetWidth, targetHeight));
                    processedMat = resizedMat.Clone();
                }
                else
                {
                    processedMat = mat.Clone();
                }

                var frameData = MatToBgraBytes(processedMat, targetWidth, targetHeight);
                processedMat.Dispose();

                var frame = new VideoFrame(frameData, targetWidth, targetHeight, (long)framePts, (int)frameIndex);

                // 使用非阻塞添加，避免解码线程卡住
                if (!_frameBuffer!.TryAdd(frame, 50))
                {
                    // 如果缓冲满，丢弃最旧的帧
                    if (_frameBuffer.TryTake(out var oldFrame))
                    {
                        oldFrame?.Dispose();
                    }
                    _frameBuffer.TryAdd(frame);
                }

                frameIndex++;
            }
        }
        catch (OperationCanceledException)
        {
            FileLogger.Log("VideoDecodeLoop cancelled");
        }
        catch (Exception ex)
        {
            FileLogger.LogError("VideoDecodeLoop error", ex);
        }
    }

    private async Task VideoRenderLoop(CancellationToken token)
    {
        FileLogger.Log("VideoRenderLoop started");

        try
        {
            var firstFrame = true;

            while (!token.IsCancellationRequested && _isPlaying)
            {
                var frame = _frameBuffer?.Take(token);
                if (frame == null) continue;

                var delay = _syncClock!.CalculateVideoDelay(frame.TimestampMs);

                // 如果帧时间戳落后太多，直接丢弃，避免追赶造成卡顿
                if (delay < TimeSpan.FromMilliseconds(-AVSyncClock.VideoCatchUpThresholdMs))
                {
                    _framesDropped++;
                    if (_framesDropped % 30 == 0)
                    {
                        FileLogger.Log($"Dropped {_framesDropped} frames, current drift: {-delay.TotalMilliseconds:F1}ms");
                    }
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

                Dispatcher.MainDispatcher?.BeginInvoke(() =>
                {
                    UpdateFrameBitmap(frame.Data, frame.Width, frame.Height);
                });

                if (firstFrame)
                {
                    FileLogger.Log($"First frame rendered: PTS={frame.TimestampMs:F1}ms, drift={delay.TotalMilliseconds:F1}ms");
                    firstFrame = false;
                }

                frame.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
            FileLogger.Log("VideoRenderLoop cancelled");
        }
        catch (Exception ex)
        {
            FileLogger.LogError("VideoRenderLoop error", ex);
        }
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
                    var audioPos = _audioManager.CurrentPosition.TotalSeconds;
                    _syncClock.UpdateAudioPosition(audioPos);
                    _position = _audioManager.CurrentPosition;
                }

                // 降低同步频率，减少CPU占用
                Thread.Sleep(20);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            FileLogger.LogError("AudioSyncLoop error", ex);
        }
    }

    private async Task AudioOnlyPositionLoop(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && _isPlaying)
            {
                if (_audioManager != null)
                {
                    _position = _audioManager.CurrentPosition;
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

    private byte[] MatToBgraBytes(Mat mat, int width, int height)
    {
        var pixelCount = width * height;
        var bgraData = new byte[pixelCount * 4];

        using var continuousMat = mat.IsContinuous() ? mat : mat.Clone();

        using var bgrMat = continuousMat.Type() == MatType.CV_8UC3
            ? continuousMat
            : continuousMat.CvtColor(ColorConversionCodes.RGBA2BGR);

        unsafe
        {
            byte* srcPtr = bgrMat.DataPointer;

            for (int i = 0; i < pixelCount; i++)
            {
                int srcIdx = i * 3;
                int dstIdx = i * 4;

                bgraData[dstIdx + 0] = srcPtr[srcIdx + 0];
                bgraData[dstIdx + 1] = srcPtr[srcIdx + 1];
                bgraData[dstIdx + 2] = srcPtr[srcIdx + 2];
                bgraData[dstIdx + 3] = 255;
            }
        }

        return bgraData;
    }

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

    private void UpdateFrameBitmap(byte[] pixelData, int width, int height)
    {
        if (width <= 0 || height <= 0 || pixelData == null || pixelData.Length == 0)
            return;

        var expectedLength = width * height * 4;
        if (pixelData.Length < expectedLength)
            return;

        var needsNewBitmap = _frameBitmap == null ||
                            _frameBitmap.Width != width ||
                            _frameBitmap.Height != height;

        if (needsNewBitmap)
        {
            if (width > 8192 || height > 8192)
                return;

            try
            {
                _frameBitmap = new WriteableBitmap(
                    width, height,
                    96, 96,
                    PixelFormats.Bgra32, null);
            }
            catch
            {
                _frameBitmap = null;
                return;
            }
        }

        try
        {
            var stride = width * 4;
            _frameBitmap!.WritePixels(
                new Int32Rect(0, 0, width, height),
                pixelData,
                stride,
                0);

            try
            {
                var bmp = EncodeBmp32TopDown(pixelData, width, height);
                _frameImage = BitmapImage.FromBytes(bmp);
            }
            catch { }

            InvalidateVisual();
        }
        catch { }
    }

    private static byte[] EncodeBmp32TopDown(byte[] bgraData, int width, int height)
    {
        if (bgraData == null) throw new ArgumentNullException(nameof(bgraData));

        var stride = width * 4;
        var pixelDataSize = (uint)(stride * height);
        const int fileHeaderSize = 14;
        const int infoHeaderSize = 40;
        var bfOffBits = fileHeaderSize + infoHeaderSize;
        var bfSize = bfOffBits + pixelDataSize;

        using var ms = new MemoryStream((int)bfSize);
        using var bw = new BinaryWriter(ms);

        bw.Write((ushort)0x4D42);
        bw.Write((uint)bfSize);
        bw.Write((ushort)0);
        bw.Write((ushort)0);
        bw.Write((uint)bfOffBits);

        bw.Write((uint)infoHeaderSize);
        bw.Write((int)width);
        bw.Write((int)(-height));
        bw.Write((ushort)1);
        bw.Write((ushort)32);
        bw.Write((uint)0);
        bw.Write((uint)pixelDataSize);
        bw.Write((int)3780);
        bw.Write((int)3780);
        bw.Write((uint)0);
        bw.Write((uint)0);

        bw.Write(bgraData, 0, bgraData.Length);
        bw.Flush();
        return ms.ToArray();
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

    private void SeekToPosition(TimeSpan position)
    {
        FileLogger.Log($"SeekToPosition: {position.TotalSeconds:F3}s");

        var wasPlaying = _isPlaying;
        var wasPaused = _isPaused;

        if (_isPlaying)
        {
            StopPlayback();
        }

        _position = position;
        _videoStartTimeMs = position.TotalMilliseconds;  // Seek时更新时间基准

        if (_audioManager != null)
        {
            _audioManager.Seek(position);
        }

        if (wasPlaying || wasPaused)
        {
            _isPlaying = true;
            _isPaused = false;
            StartPlaybackInternal();
        }
    }

    #endregion

    #region 音量与静音

    private void SetVolumeInternal(double volume)
    {
        _currentVolume = volume;
        _audioManager?.SetVolume(volume);
    }

    private void SetMutedInternal(bool isMuted)
    {
        _isMuted = isMuted;
        _audioManager?.SetMuted(isMuted);
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
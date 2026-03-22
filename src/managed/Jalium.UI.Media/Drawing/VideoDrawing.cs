using OpenCvSharp;
using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Codecs.FFMpeg;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Providers;
using SoundFlow.Structs;
using System.Diagnostics;
using System.Reflection;

namespace Jalium.UI.Media;

/// <summary>
/// Plays a media file. If the media is a video file, the VideoDrawing draws it to the specified rectangle.
/// </summary>
public sealed class VideoDrawing : Drawing
{
    /// <summary>
    /// Initializes a new instance of the <see cref="VideoDrawing"/> class.
    /// </summary>
    public VideoDrawing()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="VideoDrawing"/> class
    /// with the specified media player and destination rectangle.
    /// </summary>
    /// <param name="player">The media player that plays the video.</param>
    /// <param name="rect">The region in which to draw the video.</param>
    public VideoDrawing(MediaPlayer? player, Rect rect)
    {
        Player = player;
        Rect = rect;
    }

    /// <summary>
    /// Gets or sets the MediaPlayer that plays the video.
    /// </summary>
    public MediaPlayer? Player { get; set; }

    /// <summary>
    /// Gets or sets the region in which to draw the video.
    /// </summary>
    public Rect Rect { get; set; }

    /// <inheritdoc />
    public override Rect Bounds => Rect;

    /// <inheritdoc />
    public override void RenderTo(DrawingContext context)
    {
        if (Player == null || Rect.IsEmpty)
            return;

        var frame = Player.CurrentFrame;
        if (frame != null)
        {
            context.DrawImage(frame, Rect);
        }
    }
}

/// <summary>
/// Non-visual media player that handles audio/video playback and exposes the current video frame.
/// Uses OpenCvSharp for video decoding and SoundFlow + FFmpeg for audio playback.
/// </summary>
public sealed class MediaPlayer : IDisposable
{
    private VideoCapture? _videoCapture;
    private MiniAudioEngine? _audioEngine;
    private SoundPlayer? _soundPlayer;
    private StreamDataProvider? _dataProvider;
    private FileStream? _fileStream;
    private AudioPlaybackDevice? _playbackDevice;

    private CancellationTokenSource? _playbackCts;
    private Task? _videoDecodeTask;
    private Task? _audioSyncTask;

    private int _videoWidth;
    private int _videoHeight;
    private double _videoFps;
    private double _frameDelayMs;
    private TimeSpan _duration;
    private bool _isPlaying;
    private TimeSpan _position;
    private readonly object _lock = new();
    private bool _disposed;

    private double _currentVolume = 0.5;
    private bool _isMuted;
    private double _speedRatio = 1.0;
    private readonly Stopwatch _playbackClock = new();
    private TimeSpan _clockBaseTime;

    /// <summary>
    /// Gets or sets the source URI for the media.
    /// </summary>
    public Uri? Source { get; set; }

    /// <summary>
    /// Gets or sets the volume level (0.0 to 1.0).
    /// </summary>
    public double Volume
    {
        get => _currentVolume;
        set
        {
            _currentVolume = Math.Clamp(value, 0.0, 1.0);
            UpdateAudioVolume();
        }
    }

    /// <summary>
    /// Gets or sets the balance between left and right speakers (-1.0 to 1.0).
    /// </summary>
    public double Balance { get; set; }

    /// <summary>
    /// Gets or sets whether the media is muted.
    /// </summary>
    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            _isMuted = value;
            UpdateAudioVolume();
        }
    }

    /// <summary>
    /// Gets or sets the speed ratio for playback.
    /// </summary>
    public double SpeedRatio
    {
        get => _speedRatio;
        set => _speedRatio = Math.Clamp(value, 0.1, 10.0);
    }

    /// <summary>
    /// Gets or sets the current position in the media.
    /// </summary>
    public TimeSpan Position
    {
        get => _position;
        set
        {
            if (_position != value)
            {
                _position = value;
                Seek(value);
            }
        }
    }

    /// <summary>
    /// Gets the natural duration of the media.
    /// </summary>
    public TimeSpan? NaturalDuration { get; private set; }

    /// <summary>
    /// Gets the natural width of the video.
    /// </summary>
    public int NaturalVideoWidth => _videoWidth;

    /// <summary>
    /// Gets the natural height of the video.
    /// </summary>
    public int NaturalVideoHeight => _videoHeight;

    /// <summary>
    /// Gets a value indicating whether the media has audio.
    /// </summary>
    public bool HasAudio { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the media has video.
    /// </summary>
    public bool HasVideo { get; private set; }

    /// <summary>
    /// Gets the current video frame as an ImageSource, or null if no frame is available.
    /// </summary>
    public ImageSource? CurrentFrame { get; private set; }

    /// <summary>
    /// Occurs when the media is opened.
    /// </summary>
    public event EventHandler? MediaOpened;

    /// <summary>
    /// Occurs when media playback ends.
    /// </summary>
    public event EventHandler? MediaEnded;

    /// <summary>
    /// Occurs when an error is encountered.
    /// </summary>
    public event EventHandler<ExceptionEventArgs>? MediaFailed;

    /// <summary>
    /// Occurs when a new video frame is available.
    /// </summary>
    public event EventHandler? FrameReady;

    /// <summary>
    /// Opens the specified URI for playback.
    /// </summary>
    /// <param name="source">The URI of the media to open.</param>
    public void Open(Uri source)
    {
        Source = source;
        var path = source.IsFile ? source.LocalPath : source.ToString();

        try
        {
            StopInternal();

            // 视频探测
            _videoCapture?.Dispose();
            _videoCapture = new VideoCapture(path);

            if (_videoCapture.IsOpened())
            {
                _videoWidth = (int)_videoCapture.Get(VideoCaptureProperties.FrameWidth);
                _videoHeight = (int)_videoCapture.Get(VideoCaptureProperties.FrameHeight);
                _videoFps = _videoCapture.Get(VideoCaptureProperties.Fps);
                HasVideo = _videoWidth > 0 && _videoHeight > 0;

                var frameCount = _videoCapture.Get(VideoCaptureProperties.FrameCount);
                if (_videoFps > 0 && frameCount > 0)
                {
                    _duration = TimeSpan.FromSeconds(frameCount / _videoFps);
                    NaturalDuration = _duration;
                }

                if (_videoFps <= 0) _videoFps = 30.0;
                _frameDelayMs = 1000.0 / _videoFps;
            }
            else
            {
                _videoCapture.Dispose();
                _videoCapture = null;
            }

            // 音频探测
            HasAudio = TryInitializeAudio(path);

            MediaOpened?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            MediaFailed?.Invoke(this, new ExceptionEventArgs(ex));
        }
    }

    /// <summary>
    /// Starts or resumes playback.
    /// </summary>
    public void Play()
    {
        if (_isPlaying) return;

        _isPlaying = true;
        _playbackCts = new CancellationTokenSource();
        var token = _playbackCts.Token;

        _clockBaseTime = _position;
        _playbackClock.Restart();

        // 启动音频
        if (HasAudio)
        {
            try
            {
                _playbackDevice?.Start();
                _soundPlayer?.Play();
            }
            catch { }
        }

        // 启动视频解码线程
        if (HasVideo)
        {
            _videoDecodeTask = Task.Run(() => DecodeLoop(token), token);
        }
        else if (HasAudio)
        {
            // 纯音频 - 跟踪位置
            _audioSyncTask = Task.Run(() => AudioPositionLoop(token), token);
        }
    }

    /// <summary>
    /// Pauses playback.
    /// </summary>
    public void Pause()
    {
        if (!_isPlaying) return;

        _isPlaying = false;
        _playbackClock.Stop();
        _position = GetMediaTime();

        try { _soundPlayer?.Pause(); } catch { }
    }

    /// <summary>
    /// Stops playback and resets position to the beginning.
    /// </summary>
    public void Stop()
    {
        _isPlaying = false;
        StopInternal();
        _position = TimeSpan.Zero;
        CurrentFrame = null;
    }

    /// <summary>
    /// Closes the media player and releases resources.
    /// </summary>
    public void Close()
    {
        Stop();

        _videoCapture?.Dispose();
        _videoCapture = null;
        CleanupAudio();

        _videoWidth = 0;
        _videoHeight = 0;
        HasVideo = false;
        HasAudio = false;
        NaturalDuration = null;
        Source = null;
        CurrentFrame = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Close();
        _audioEngine?.Dispose();
        _audioEngine = null;
    }

    #region 内部实现

    private TimeSpan GetMediaTime()
    {
        if (!_playbackClock.IsRunning)
            return _clockBaseTime;
        return _clockBaseTime + TimeSpan.FromTicks((long)(_playbackClock.Elapsed.Ticks / _speedRatio));
    }

    private void Seek(TimeSpan position)
    {
        var wasPlaying = _isPlaying;
        StopInternal();

        _position = position;

        if (_videoCapture != null && HasVideo)
        {
            var framePos = position.TotalSeconds * _videoFps;
            _videoCapture.Set(VideoCaptureProperties.PosFrames, framePos);
        }

        if (wasPlaying)
        {
            _isPlaying = true;
            Play();
        }
    }

    private void StopInternal()
    {
        _playbackCts?.Cancel();

        try
        {
            _videoDecodeTask?.Wait(TimeSpan.FromSeconds(2));
            _audioSyncTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch { }

        _playbackCts?.Dispose();
        _playbackCts = null;
        _videoDecodeTask = null;
        _audioSyncTask = null;
        _playbackClock.Stop();
        _playbackClock.Reset();

        try
        {
            _soundPlayer?.Stop();
            _playbackDevice?.Stop();
        }
        catch { }
    }

    private void DecodeLoop(CancellationToken token)
    {
        try
        {
            using var mat = new Mat();

            while (!token.IsCancellationRequested && _isPlaying)
            {
                if (_videoCapture == null || !_videoCapture.Read(mat) || mat.Empty())
                {
                    // 播放结束
                    _isPlaying = false;
                    MediaEnded?.Invoke(this, EventArgs.Empty);
                    break;
                }

                // 转换为 BGRA 像素
                var width = mat.Width;
                var height = mat.Height;
                var frameData = MatToBgraBytes(mat, width, height);

                // 创建 ImageSource
                var stride = width * 4;
                CurrentFrame = BitmapImage.FromPixels(frameData, width, height, stride);
                FrameReady?.Invoke(this, EventArgs.Empty);

                // 更新位置
                _position = GetMediaTime();

                // 帧延迟（按速度比）
                var delayMs = _frameDelayMs / _speedRatio;
                if (delayMs > 1)
                {
                    PreciseDelay(TimeSpan.FromMilliseconds(delayMs), token);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            MediaFailed?.Invoke(this, new ExceptionEventArgs(ex));
        }
    }

    private void AudioPositionLoop(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && _isPlaying)
            {
                _position = GetMediaTime();

                if (_duration > TimeSpan.Zero && _position >= _duration)
                {
                    _isPlaying = false;
                    MediaEnded?.Invoke(this, EventArgs.Empty);
                    break;
                }

                Thread.Sleep(50);
            }
        }
        catch (OperationCanceledException) { }
    }

    private static byte[] MatToBgraBytes(Mat mat, int width, int height)
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

                bgraData[dstIdx + 0] = srcPtr[srcIdx + 0]; // B
                bgraData[dstIdx + 1] = srcPtr[srcIdx + 1]; // G
                bgraData[dstIdx + 2] = srcPtr[srcIdx + 2]; // R
                bgraData[dstIdx + 3] = 255;                // A
            }
        }

        return bgraData;
    }

    private static void PreciseDelay(TimeSpan delay, CancellationToken token)
    {
        if (delay <= TimeSpan.Zero) return;

        if (delay.TotalMilliseconds > 20)
        {
            try
            {
                Task.Delay(delay - TimeSpan.FromMilliseconds(15), token).Wait(token);
            }
            catch (OperationCanceledException) { return; }

            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < TimeSpan.FromMilliseconds(15) && !token.IsCancellationRequested)
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

    #region 音频管理

    private bool TryInitializeAudio(string filePath)
    {
        try
        {
            if (_audioEngine == null)
            {
                _audioEngine = new MiniAudioEngine();
                var factory = new FFmpegCodecFactory();

                // 注册编解码器
                var registerMethod = typeof(AudioEngine).GetMethod("RegisterCodecFactory",
                    BindingFlags.Public | BindingFlags.Instance);
                if (registerMethod != null)
                {
                    registerMethod.Invoke(_audioEngine, new object[] { factory });
                }
                else
                {
                    var codecManagerProp = typeof(AudioEngine).GetProperty("CodecManager",
                        BindingFlags.Public | BindingFlags.Instance);
                    if (codecManagerProp != null)
                    {
                        var codecManager = codecManagerProp.GetValue(_audioEngine);
                        var addMethod = codecManager?.GetType().GetMethod("AddFactory",
                            BindingFlags.Public | BindingFlags.Instance);
                        addMethod?.Invoke(codecManager, new object[] { factory });
                    }
                }
            }

            var format = AudioFormat.Dvd;
            var defaultDevice = _audioEngine.PlaybackDevices.FirstOrDefault(x => x.IsDefault);
            _playbackDevice = _audioEngine.InitializePlaybackDevice(defaultDevice, format);

            _fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 262144, useAsync: true);
            _dataProvider = new StreamDataProvider(_audioEngine, format, _fileStream);
            _soundPlayer = new SoundPlayer(_audioEngine, format, _dataProvider);

            UpdateAudioVolume();
            _playbackDevice.MasterMixer.AddComponent(_soundPlayer);

            // 探测成功后先停掉，等 Play() 再启动
            _soundPlayer.Stop();

            return true;
        }
        catch
        {
            CleanupAudio();
            return false;
        }
    }

    private void CleanupAudio()
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
        _playbackDevice?.Stop();
        _playbackDevice?.Dispose();
        _playbackDevice = null;
    }

    private void UpdateAudioVolume()
    {
        lock (_lock)
        {
            if (_soundPlayer != null)
            {
                _soundPlayer.Volume = _isMuted ? 0.0f : (float)_currentVolume;
            }
        }
    }

    #endregion
}

/// <summary>
/// Provides data for media failure events.
/// </summary>
public sealed class ExceptionEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExceptionEventArgs"/> class.
    /// </summary>
    /// <param name="exception">The exception that caused the failure.</param>
    public ExceptionEventArgs(Exception exception)
    {
        ErrorException = exception;
    }

    /// <summary>
    /// Gets the exception that caused the failure.
    /// </summary>
    public Exception ErrorException { get; }
}

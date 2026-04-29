using System.Diagnostics;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Native;
using Jalium.UI.Media.Pipeline;

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
/// 非视觉媒体播放器:视频解码走 <see cref="INativeVideoDecoder"/>(Windows MF / Android MediaCodec),
/// 音频走 <see cref="AudioPlayer"/>(SoundFlow + MiniAudio + FFmpeg)。
/// </summary>
public sealed class MediaPlayer : IDisposable
{
    private static INativeVideoDecoderFactory? s_videoDecoderFactory;
    private static readonly object s_factoryLock = new();

    /// <summary>
    /// 注入自定义 <see cref="INativeVideoDecoderFactory"/>。
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
    private readonly AudioPlayer _audioPlayer = new();

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
    private bool _disposed;

    private double _currentVolume = 0.5;
    private bool _isMuted;
    private double _speedRatio = 1.0;
    private readonly Stopwatch _playbackClock = new();
    private TimeSpan _clockBaseTime;
    private WriteableBitmap? _frameBitmap;

    /// <summary>Gets or sets the source URI for the media.</summary>
    public Uri? Source { get; set; }

    /// <summary>Gets or sets the volume level (0.0 to 1.0).</summary>
    public double Volume
    {
        get => _currentVolume;
        set
        {
            _currentVolume = Math.Clamp(value, 0.0, 1.0);
            _audioPlayer.Volume = _currentVolume;
        }
    }

    /// <summary>Gets or sets the balance between left and right speakers (-1.0 to 1.0).</summary>
    public double Balance
    {
        get => _audioPlayer.Balance;
        set => _audioPlayer.Balance = value;
    }

    /// <summary>Gets or sets whether the media is muted.</summary>
    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            _isMuted = value;
            _audioPlayer.IsMuted = value;
        }
    }

    /// <summary>Gets or sets the speed ratio for playback.</summary>
    public double SpeedRatio
    {
        get => _speedRatio;
        set
        {
            _speedRatio = Math.Clamp(value, 0.1, 10.0);
            _audioPlayer.SpeedRatio = _speedRatio;
        }
    }

    /// <summary>Gets or sets the current position in the media.</summary>
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

    /// <summary>Gets the natural duration of the media.</summary>
    public TimeSpan? NaturalDuration { get; private set; }

    /// <summary>Gets the natural width of the video.</summary>
    public int NaturalVideoWidth => _videoWidth;

    /// <summary>Gets the natural height of the video.</summary>
    public int NaturalVideoHeight => _videoHeight;

    /// <summary>Gets a value indicating whether the media has audio.</summary>
    public bool HasAudio { get; private set; }

    /// <summary>Gets a value indicating whether the media has video.</summary>
    public bool HasVideo { get; private set; }

    /// <summary>Gets the active video codec (or <see cref="SupportedCodec.None"/> if no video).</summary>
    public SupportedCodec ActiveVideoCodec => _videoDecoder?.ActiveVideoCodec ?? SupportedCodec.None;

    /// <summary>Gets the current video frame as an ImageSource, or null if no frame is available.</summary>
    public ImageSource? CurrentFrame { get; private set; }

    /// <summary>Occurs when the media is opened.</summary>
    public event EventHandler? MediaOpened;

    /// <summary>Occurs when media playback ends.</summary>
    public event EventHandler? MediaEnded;

    /// <summary>Occurs when an error is encountered.</summary>
    public event EventHandler<ExceptionEventArgs>? MediaFailed;

    /// <summary>Occurs when a new video frame is available.</summary>
    public event EventHandler? FrameReady;

    /// <summary>Opens the specified URI for playback.</summary>
    public void Open(Uri source)
    {
        Source = source;

        try
        {
            StopInternal();

            // 视频探测
            _videoDecoder?.Dispose();
            _videoDecoder = null;

            try
            {
                var decoder = GetVideoDecoderFactory().Create();
                decoder.Open(source);
                _videoDecoder = decoder;
                _videoWidth = decoder.Width;
                _videoHeight = decoder.Height;
                _videoFps = decoder.Fps > 0 ? decoder.Fps : 30.0;
                HasVideo = _videoWidth > 0 && _videoHeight > 0;

                if (decoder.Duration > TimeSpan.Zero)
                {
                    _duration = decoder.Duration;
                    NaturalDuration = _duration;
                }

                _frameDelayMs = 1000.0 / _videoFps;
            }
            catch (Exception ex)
            {
                // 视频解码器打不开,可能是纯音频文件 — 继续探测音频。
                System.Diagnostics.Debug.WriteLine($"Native video decoder open failed: {ex.Message}");
                _videoDecoder?.Dispose();
                _videoDecoder = null;
                HasVideo = false;
            }

            // 音频探测 — 委托给 AudioPlayer。失败时 AudioPlayer 自己 raise MediaFailed。
            try
            {
                _audioPlayer.Close();
                _audioPlayer.Open(source);
                HasAudio = _audioPlayer.HasAudio;
                _audioPlayer.Volume = _currentVolume;
                _audioPlayer.IsMuted = _isMuted;
                _audioPlayer.SpeedRatio = _speedRatio;

                if (HasAudio && NaturalDuration is null && _audioPlayer.NaturalDuration is { } audioDur)
                {
                    _duration = audioDur;
                    NaturalDuration = _duration;
                }
            }
            catch (Exception audioEx)
            {
                System.Diagnostics.Debug.WriteLine($"AudioPlayer.Open failed: {audioEx.Message}");
                HasAudio = false;
            }

            MediaOpened?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            MediaFailed?.Invoke(this, new ExceptionEventArgs(ex));
        }
    }

    /// <summary>Starts or resumes playback.</summary>
    public void Play()
    {
        if (_isPlaying) return;

        _isPlaying = true;
        _playbackCts = new CancellationTokenSource();
        var token = _playbackCts.Token;

        _clockBaseTime = _position;
        _playbackClock.Restart();

        if (HasAudio)
        {
            try { _audioPlayer.Play(); } catch { }
        }

        if (HasVideo)
        {
            _videoDecodeTask = Task.Run(() => DecodeLoop(token), token);
        }
        else if (HasAudio)
        {
            _audioSyncTask = Task.Run(() => AudioPositionLoop(token), token);
        }
    }

    /// <summary>Pauses playback.</summary>
    public void Pause()
    {
        if (!_isPlaying) return;

        _isPlaying = false;
        _playbackClock.Stop();
        _position = GetMediaTime();

        try { _audioPlayer.Pause(); } catch { }
    }

    /// <summary>Stops playback and resets position to the beginning.</summary>
    public void Stop()
    {
        _isPlaying = false;
        StopInternal();
        _position = TimeSpan.Zero;
        CurrentFrame = null;
    }

    /// <summary>Closes the media player and releases resources.</summary>
    public void Close()
    {
        Stop();

        _videoDecoder?.Dispose();
        _videoDecoder = null;
        _audioPlayer.Close();

        _videoWidth = 0;
        _videoHeight = 0;
        HasVideo = false;
        HasAudio = false;
        NaturalDuration = null;
        Source = null;
        CurrentFrame = null;
        _frameBitmap = null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Close();
        _audioPlayer.Dispose();
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

        if (_videoDecoder != null && HasVideo)
        {
            try { _videoDecoder.Seek(position); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Seek failed: {ex.Message}"); }
        }

        if (HasAudio)
        {
            try { _audioPlayer.Seek(position); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Audio seek failed: {ex.Message}"); }
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

        try { _audioPlayer.Stop(); } catch { }
    }

    private void DecodeLoop(CancellationToken token)
    {
        if (_videoDecoder is null) return;

        try
        {
            while (!token.IsCancellationRequested && _isPlaying)
            {
                MediaFrame? mediaFrame;
                bool hasFrame;
                try
                {
                    hasFrame = _videoDecoder.TryReadFrame(out mediaFrame);
                }
                catch (Exception ex)
                {
                    MediaFailed?.Invoke(this, new ExceptionEventArgs(ex));
                    break;
                }

                if (!hasFrame || mediaFrame is null)
                {
                    _isPlaying = false;
                    MediaEnded?.Invoke(this, EventArgs.Empty);
                    break;
                }

                using (mediaFrame)
                {
                    var width = mediaFrame.Width;
                    var height = mediaFrame.Height;
                    var stride = mediaFrame.Stride;

                    // 复用同一个 WriteableBitmap，让 ContentRevision 触发 GPU cache 失效。
                    // 每帧 new BitmapImage 会让 RenderTargetDrawingContext._bitmapCache 永远 miss，
                    // 1080p 30fps 下变成每秒 8MB×30 = 240MB GPU 上传 + 240MB GC。
                    var bitmap = _frameBitmap;
                    if (bitmap is null || bitmap.PixelWidth != width || bitmap.PixelHeight != height)
                    {
                        bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
                        _frameBitmap = bitmap;
                    }

                    bitmap.WritePixels(new Int32Rect(0, 0, width, height), mediaFrame.Pixels.Span, stride);
                    CurrentFrame = bitmap;
                    FrameReady?.Invoke(this, EventArgs.Empty);
                }

                _position = GetMediaTime();

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
}

/// <summary>Provides data for media failure events.</summary>
public sealed class ExceptionEventArgs : EventArgs
{
    /// <summary>Initializes a new instance of the <see cref="ExceptionEventArgs"/> class.</summary>
    public ExceptionEventArgs(Exception exception)
    {
        ErrorException = exception;
    }

    /// <summary>Gets the exception that caused the failure.</summary>
    public Exception ErrorException { get; }
}

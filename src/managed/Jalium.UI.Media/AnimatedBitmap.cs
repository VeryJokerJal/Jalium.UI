using Jalium.UI.Media.Imaging;
using Jalium.UI.Threading;

namespace Jalium.UI.Media;

/// <summary>
/// Specifies how an <see cref="AnimatedBitmap"/> behaves when it finishes its
/// last frame.
/// </summary>
public enum AnimatedBitmapRepeatBehavior
{
    /// <summary>Loop forever (the default — matches browser behaviour for animated GIFs).</summary>
    Forever,

    /// <summary>Play the animation once and stop on the last frame.</summary>
    Once,

    /// <summary>Stop after the first frame (effectively a static image).</summary>
    None,
}

/// <summary>
/// A multi-frame bitmap source (animated GIF / APNG / animated WebP). The
/// platform image decoder is asked once for the frame count + per-frame delay
/// metadata, all frames are decoded into <see cref="BitmapImage"/> instances,
/// and a <see cref="DispatcherTimer"/> advances <see cref="CurrentFrame"/> on
/// the UI thread. Subscribers (the <c>Image</c> control) drive their visual
/// invalidation off <see cref="FrameChanged"/>.
/// </summary>
public sealed class AnimatedBitmap : ImageSource, IDisposable
{
    /// <inheritdoc />
    public override ImageMetadata? Metadata => CurrentFrame?.Metadata;

    private BitmapImage[] _frames = Array.Empty<BitmapImage>();
    private int[] _delaysMs = Array.Empty<int>();
    private int _currentIndex;
    private DispatcherTimer? _timer;
    private bool _isPlaying;
    private AnimatedBitmapRepeatBehavior _repeatBehavior = AnimatedBitmapRepeatBehavior.Forever;
    private double _width;
    private double _height;

    /// <summary>Raised after <see cref="CurrentFrame"/> changes — the Image
    /// control hooks this to call <c>InvalidateVisual</c>.</summary>
    public event EventHandler? FrameChanged;

    /// <summary>Raised once all frames have been decoded.</summary>
    public event EventHandler? LoadCompleted;

    /// <inheritdoc />
    public override double Width => _width;

    /// <inheritdoc />
    public override double Height => _height;

    /// <inheritdoc />
    public override nint NativeHandle => CurrentFrame?.NativeHandle ?? nint.Zero;

    /// <summary>Gets the frame currently displayed.</summary>
    public BitmapImage? CurrentFrame
        => _frames.Length == 0 ? null : _frames[Math.Clamp(_currentIndex, 0, _frames.Length - 1)];

    /// <summary>Gets all decoded frames (read-only view).</summary>
    public IReadOnlyList<BitmapImage> Frames => _frames;

    /// <summary>Gets the delay between consecutive frames in milliseconds.</summary>
    public IReadOnlyList<int> FrameDelays => _delaysMs;

    /// <summary>Number of frames in the animation.</summary>
    public int FrameCount => _frames.Length;

    /// <summary>Index of the currently-displayed frame.</summary>
    public int CurrentFrameIndex
    {
        get => _currentIndex;
        set
        {
            if (_frames.Length == 0)
                return;
            int clamped = Math.Clamp(value, 0, _frames.Length - 1);
            if (clamped != _currentIndex)
            {
                _currentIndex = clamped;
                RescheduleTimer();
                FrameChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>Whether the timer is currently advancing frames.</summary>
    public bool IsPlaying => _isPlaying;

    /// <summary>Repeat behaviour after the last frame.</summary>
    public AnimatedBitmapRepeatBehavior RepeatBehavior
    {
        get => _repeatBehavior;
        set => _repeatBehavior = value;
    }

    /// <summary>
    /// Creates an <see cref="AnimatedBitmap"/> from a file path. Decodes every
    /// frame synchronously; for large GIFs prefer the async byte-array overload.
    /// </summary>
    public static AnimatedBitmap FromFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        var bytes = System.IO.File.ReadAllBytes(filePath);
        return FromBytes(bytes);
    }

    /// <summary>
    /// Creates an <see cref="AnimatedBitmap"/> from an encoded byte array
    /// (GIF / APNG / animated WebP).
    /// </summary>
    public static AnimatedBitmap FromBytes(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0) throw new ArgumentException("Image data is empty.", nameof(data));

        var anim = new AnimatedBitmap();
        anim.LoadFromBytes(data);
        return anim;
    }

    /// <summary>
    /// Tries to create an <see cref="AnimatedBitmap"/> from <paramref name="data"/>;
    /// returns <c>null</c> when the input has only one frame so callers can fall
    /// back to <see cref="BitmapImage"/>.
    /// </summary>
    public static AnimatedBitmap? TryFromBytes(byte[] data)
    {
        var anim = FromBytes(data);
        if (anim.FrameCount <= 1)
        {
            anim.Dispose();
            return null;
        }
        return anim;
    }

    private void LoadFromBytes(byte[] data)
    {
        var decoder = GetDecoder();
        int frameCount = decoder.ReadFrameCount(data);
        if (frameCount <= 0)
            frameCount = 1;

        var frames = new BitmapImage[frameCount];
        var delays = new int[frameCount];

        for (int i = 0; i < frameCount; i++)
        {
            var frame = decoder.DecodeFrame(data, i);
            frames[i] = BitmapImage.FromDecodedImage(frame.Image);
            delays[i] = frame.DelayMs > 0 ? frame.DelayMs : 100; // sane default for sources lacking delay metadata
        }

        _frames = frames;
        _delaysMs = delays;
        _width = frames[0].Width;
        _height = frames[0].Height;
        _currentIndex = 0;

        LoadCompleted?.Invoke(this, EventArgs.Empty);

        if (_frames.Length > 1)
        {
            Play();
        }
    }

    /// <summary>Starts (or resumes) the animation timer on the current dispatcher.</summary>
    public void Play()
    {
        if (_isPlaying || _frames.Length <= 1)
            return;

        _isPlaying = true;
        EnsureTimer();
        ScheduleNextTick();
    }

    /// <summary>Pauses the animation. Resume with <see cref="Play"/>.</summary>
    public void Pause()
    {
        _isPlaying = false;
        _timer?.Stop();
    }

    /// <summary>Stops the animation and rewinds to frame 0.</summary>
    public void Stop()
    {
        Pause();
        if (_currentIndex != 0)
        {
            _currentIndex = 0;
            FrameChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void EnsureTimer()
    {
        if (_timer != null)
            return;

        _timer = new DispatcherTimer(DispatcherPriority.Background);
        _timer.Tick += OnTimerTick;
    }

    private void ScheduleNextTick()
    {
        if (_timer == null || _frames.Length <= 1)
            return;

        int delay = _delaysMs[_currentIndex];
        if (delay < 20) delay = 100;
        _timer.Interval = TimeSpan.FromMilliseconds(delay);
        _timer.Start();
    }

    private void RescheduleTimer()
    {
        if (_isPlaying)
        {
            _timer?.Stop();
            ScheduleNextTick();
        }
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (!_isPlaying || _frames.Length <= 1)
        {
            _timer?.Stop();
            return;
        }

        int next = _currentIndex + 1;
        if (next >= _frames.Length)
        {
            switch (_repeatBehavior)
            {
                case AnimatedBitmapRepeatBehavior.Forever:
                    next = 0;
                    break;
                case AnimatedBitmapRepeatBehavior.Once:
                case AnimatedBitmapRepeatBehavior.None:
                    _timer?.Stop();
                    _isPlaying = false;
                    return;
            }
        }

        _currentIndex = next;
        ScheduleNextTick();
        FrameChanged?.Invoke(this, EventArgs.Empty);
    }

    private static INativeImageDecoder GetDecoder()
    {
        // Reuse the exact decoder BitmapImage uses (honoring any SetDecoder
        // injection) so frame-count probing and per-frame decoding never
        // disagree about whether a payload is animated.
        return BitmapImage.ResolveDecoder();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Pause();
        _timer = null;
        foreach (var frame in _frames)
        {
            frame.Dispose();
        }
        _frames = Array.Empty<BitmapImage>();
        _delaysMs = Array.Empty<int>();
    }
}

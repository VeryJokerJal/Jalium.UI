using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Specifies the load behavior for the MediaElement.
/// </summary>
//public enum MediaState
//{
//    /// <summary>
//    /// The media is in manual control mode (uses Play, Pause, Stop methods).
//    /// </summary>
//    Manual,

//    /// <summary>
//    /// The media is playing.
//    /// </summary>
//    Play,

//    /// <summary>
//    /// The media is paused.
//    /// </summary>
//    Pause,

//    /// <summary>
//    /// The media is stopped.
//    /// </summary>
//    Stop,

//    /// <summary>
//    /// The media is closed.
//    /// </summary>
//    Close
//}

/// <summary>
/// Represents a control that contains audio and/or video.
/// </summary>
public class MediaElement : FrameworkElement
{
    private Uri? _currentSource;
    private TimeSpan _position;
    private TimeSpan _duration;
    private bool _isPlaying;
    private bool _hasVideo;
    private bool _hasAudio;
    private double _downloadProgress;
    private double _bufferingProgress;

    #region Dependency Properties

    /// <summary>
    /// Identifies the Source dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(nameof(Source), typeof(Uri), typeof(MediaElement),
            new PropertyMetadata(null, OnSourceChanged));

    /// <summary>
    /// Identifies the Volume dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty VolumeProperty =
        DependencyProperty.Register(nameof(Volume), typeof(double), typeof(MediaElement),
            new PropertyMetadata(0.5, OnVolumeChanged, CoerceVolume));

    /// <summary>
    /// Identifies the Balance dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty BalanceProperty =
        DependencyProperty.Register(nameof(Balance), typeof(double), typeof(MediaElement),
            new PropertyMetadata(0.0, OnBalanceChanged, CoerceBalance));

    /// <summary>
    /// Identifies the IsMuted dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsMutedProperty =
        DependencyProperty.Register(nameof(IsMuted), typeof(bool), typeof(MediaElement),
            new PropertyMetadata(false, OnIsMutedChanged));

    /// <summary>
    /// Identifies the ScrubbingEnabled dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty ScrubbingEnabledProperty =
        DependencyProperty.Register(nameof(ScrubbingEnabled), typeof(bool), typeof(MediaElement),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the Stretch dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty StretchProperty =
        DependencyProperty.Register(nameof(Stretch), typeof(Stretch), typeof(MediaElement),
            new PropertyMetadata(Stretch.Uniform, OnStretchChanged));

    /// <summary>
    /// Identifies the StretchDirection dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty StretchDirectionProperty =
        DependencyProperty.Register(nameof(StretchDirection), typeof(StretchDirection), typeof(MediaElement),
            new PropertyMetadata(StretchDirection.Both, OnStretchChanged));

    /// <summary>
    /// Identifies the LoadedBehavior dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty LoadedBehaviorProperty =
        DependencyProperty.Register(nameof(LoadedBehavior), typeof(MediaState), typeof(MediaElement),
            new PropertyMetadata(MediaState.Play, OnLoadedBehaviorChanged));

    /// <summary>
    /// Identifies the UnloadedBehavior dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty UnloadedBehaviorProperty =
        DependencyProperty.Register(nameof(UnloadedBehavior), typeof(MediaState), typeof(MediaElement),
            new PropertyMetadata(MediaState.Close));

    /// <summary>
    /// Identifies the SpeedRatio dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty SpeedRatioProperty =
        DependencyProperty.Register(nameof(SpeedRatio), typeof(double), typeof(MediaElement),
            new PropertyMetadata(1.0, OnSpeedRatioChanged, CoerceSpeedRatio));

    #endregion

    #region Events

    /// <summary>
    /// Occurs when media loading has finished.
    /// </summary>
    public event RoutedEventHandler? MediaOpened;

    /// <summary>
    /// Occurs when the media has ended.
    /// </summary>
    public event RoutedEventHandler? MediaEnded;

    /// <summary>
    /// Occurs when an error is encountered.
    /// </summary>
    public event EventHandler<MediaFailedEventArgs>? MediaFailed;

    /// <summary>
    /// Occurs when buffering has started.
    /// </summary>
    public event RoutedEventHandler? BufferingStarted;

    /// <summary>
    /// Occurs when buffering has ended.
    /// </summary>
    public event RoutedEventHandler? BufferingEnded;

    /// <summary>
    /// Occurs when a script command is encountered in the media.
    /// </summary>
    public event EventHandler<MediaScriptCommandEventArgs>? ScriptCommand;

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets a media source on the MediaElement.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public Uri? Source
    {
        get => (Uri?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>
    /// Gets or sets the media's volume.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public double Volume
    {
        get => (double)GetValue(VolumeProperty)!;
        set => SetValue(VolumeProperty, value);
    }

    /// <summary>
    /// Gets or sets a ratio of volume across speakers.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public double Balance
    {
        get => (double)GetValue(BalanceProperty)!;
        set => SetValue(BalanceProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the audio is muted.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsMuted
    {
        get => (bool)GetValue(IsMutedProperty)!;
        set => SetValue(IsMutedProperty, value);
    }

    /// <summary>
    /// Gets or sets a value that indicates whether the MediaElement will update frames
    /// for seek operations while paused.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public bool ScrubbingEnabled
    {
        get => (bool)GetValue(ScrubbingEnabledProperty)!;
        set => SetValue(ScrubbingEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets a Stretch value that describes how the media fills the destination rectangle.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public Stretch Stretch
    {
        get => (Stretch)GetValue(StretchProperty)!;
        set => SetValue(StretchProperty, value);
    }

    /// <summary>
    /// Gets or sets a value that determines the restrictions on scaling that are applied to the video.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public StretchDirection StretchDirection
    {
        get => (StretchDirection)GetValue(StretchDirectionProperty)!;
        set => SetValue(StretchDirectionProperty, value);
    }

    /// <summary>
    /// Gets or sets the load behavior MediaState for the media.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public MediaState LoadedBehavior
    {
        get => (MediaState)GetValue(LoadedBehaviorProperty)!;
        set => SetValue(LoadedBehaviorProperty, value);
    }

    /// <summary>
    /// Gets or sets the unload behavior MediaState for the media.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public MediaState UnloadedBehavior
    {
        get => (MediaState)GetValue(UnloadedBehaviorProperty)!;
        set => SetValue(UnloadedBehaviorProperty, value);
    }

    /// <summary>
    /// Gets or sets the speed ratio of the media.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public double SpeedRatio
    {
        get => (double)GetValue(SpeedRatioProperty)!;
        set => SetValue(SpeedRatioProperty, value);
    }

    /// <summary>
    /// Gets or sets the current position of progress through the media's playback time.
    /// </summary>
    public TimeSpan Position
    {
        get => _position;
        set
        {
            if (_position != value)
            {
                _position = value;
                SeekToPosition(value);
            }
        }
    }

    /// <summary>
    /// Gets the duration of the media.
    /// </summary>
    public Duration NaturalDuration => _duration == TimeSpan.Zero
        ? Duration.Automatic
        : new Duration(_duration);

    /// <summary>
    /// Gets the width of the video.
    /// </summary>
    public int NaturalVideoWidth { get; private set; }

    /// <summary>
    /// Gets the height of the video.
    /// </summary>
    public int NaturalVideoHeight { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the media has audio.
    /// </summary>
    public bool HasAudio => _hasAudio;

    /// <summary>
    /// Gets a value indicating whether the media has video.
    /// </summary>
    public bool HasVideo => _hasVideo;

    /// <summary>
    /// Gets a value indicating the percentage of buffering progress made.
    /// </summary>
    public double BufferingProgress => _bufferingProgress;

    /// <summary>
    /// Gets a percentage value indicating the amount of download completed for content located on a remote server.
    /// </summary>
    public double DownloadProgress => _downloadProgress;

    /// <summary>
    /// Gets a value indicating whether the media is buffering.
    /// </summary>
    public bool IsBuffering { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the media can be paused.
    /// </summary>
    public bool CanPause { get; private set; } = true;

    #endregion

    #region Public Methods

    /// <summary>
    /// Plays media from the current position.
    /// </summary>
    public void Play()
    {
        if (LoadedBehavior == MediaState.Manual)
        {
            _isPlaying = true;
            PlayInternal();
        }
    }

    /// <summary>
    /// Pauses media at the current position.
    /// </summary>
    public void Pause()
    {
        if (LoadedBehavior == MediaState.Manual)
        {
            _isPlaying = false;
            PauseInternal();
        }
    }

    /// <summary>
    /// Stops and resets media to be played from the beginning.
    /// </summary>
    public void Stop()
    {
        if (LoadedBehavior == MediaState.Manual)
        {
            _isPlaying = false;
            StopInternal();
        }
    }

    /// <summary>
    /// Closes the media.
    /// </summary>
    public void Close()
    {
        _isPlaying = false;
        CloseInternal();
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (!_hasVideo || NaturalVideoWidth == 0 || NaturalVideoHeight == 0)
        {
            return new Size(0, 0);
        }

        var naturalSize = new Size(NaturalVideoWidth, NaturalVideoHeight);
        return ComputeScaledSize(availableSize, naturalSize);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        return finalSize;
    }

    private Size ComputeScaledSize(Size availableSize, Size contentSize)
    {
        var scaleX = 1.0;
        var scaleY = 1.0;

        var isWidthInfinite = double.IsInfinity(availableSize.Width);
        var isHeightInfinite = double.IsInfinity(availableSize.Height);

        if (Stretch != Stretch.None && (!isWidthInfinite || !isHeightInfinite))
        {
            scaleX = contentSize.Width > 0 ? availableSize.Width / contentSize.Width : 0;
            scaleY = contentSize.Height > 0 ? availableSize.Height / contentSize.Height : 0;

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

        return new Size(contentSize.Width * scaleX, contentSize.Height * scaleY);
    }

    #endregion

    #region Property Changed Handlers

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MediaElement media)
        {
            media.OnSourceChanged((Uri?)e.OldValue, (Uri?)e.NewValue);
        }
    }

    private void OnSourceChanged(Uri? oldSource, Uri? newSource)
    {
        _currentSource = newSource;
        if (newSource != null)
        {
            OpenMedia(newSource);
        }
        else
        {
            CloseInternal();
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

    private static void OnBalanceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MediaElement media)
        {
            media.SetBalanceInternal((double)e.NewValue!);
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
            media.SetSpeedRatioInternal((double)e.NewValue!);
        }
    }

    private static object CoerceSpeedRatio(DependencyObject d, object? value)
    {
        var ratio = (double)(value ?? 1.0);
        return Math.Clamp(ratio, 0.1, 10.0);
    }

    #endregion

    #region Internal Methods (Platform Implementation Hooks)

    /// <summary>
    /// Opens and prepares media for playback.
    /// </summary>
    protected void OpenMedia(Uri source)
    {
        // Platform-specific implementation
        // This would integrate with Media Foundation, FFmpeg, etc.
        OnMediaOpened();
    }

    /// <summary>
    /// Plays the media internally.
    /// </summary>
    protected void PlayInternal()
    {
        // Platform-specific implementation
    }

    /// <summary>
    /// Pauses the media internally.
    /// </summary>
    protected void PauseInternal()
    {
        // Platform-specific implementation
    }

    /// <summary>
    /// Stops the media internally.
    /// </summary>
    protected void StopInternal()
    {
        _position = TimeSpan.Zero;
        // Platform-specific implementation
    }

    /// <summary>
    /// Closes the media internally.
    /// </summary>
    protected void CloseInternal()
    {
        _position = TimeSpan.Zero;
        _duration = TimeSpan.Zero;
        _hasVideo = false;
        _hasAudio = false;
        NaturalVideoWidth = 0;
        NaturalVideoHeight = 0;
        // Platform-specific implementation
    }

    /// <summary>
    /// Seeks to the specified position.
    /// </summary>
    protected void SeekToPosition(TimeSpan position)
    {
        // Platform-specific implementation
    }

    /// <summary>
    /// Sets the volume internally.
    /// </summary>
    protected void SetVolumeInternal(double volume)
    {
        // Platform-specific implementation
    }

    /// <summary>
    /// Sets the balance internally.
    /// </summary>
    protected void SetBalanceInternal(double balance)
    {
        // Platform-specific implementation
    }

    /// <summary>
    /// Sets the muted state internally.
    /// </summary>
    protected void SetMutedInternal(bool isMuted)
    {
        // Platform-specific implementation
    }

    /// <summary>
    /// Sets the speed ratio internally.
    /// </summary>
    protected void SetSpeedRatioInternal(double speedRatio)
    {
        // Platform-specific implementation
    }

    /// <summary>
    /// Applies the loaded behavior.
    /// </summary>
    protected void ApplyLoadedBehavior(MediaState state)
    {
        switch (state)
        {
            case MediaState.Play:
                PlayInternal();
                break;
            case MediaState.Pause:
                PauseInternal();
                break;
            case MediaState.Stop:
                StopInternal();
                break;
            case MediaState.Close:
                CloseInternal();
                break;
        }
    }

    #endregion

    #region Event Raising Methods

    /// <summary>
    /// Raises the MediaOpened event.
    /// </summary>
    protected void OnMediaOpened()
    {
        MediaOpened?.Invoke(this, new RoutedEventArgs());
    }

    /// <summary>
    /// Raises the MediaEnded event.
    /// </summary>
    protected void OnMediaEnded()
    {
        _isPlaying = false;
        MediaEnded?.Invoke(this, new RoutedEventArgs());
    }

    /// <summary>
    /// Raises the MediaFailed event.
    /// </summary>
    protected void OnMediaFailed(Exception exception)
    {
        _isPlaying = false;
        //MediaFailed?.Invoke(this, new MediaFailedEventArgs(exception));
    }

    /// <summary>
    /// Raises the BufferingStarted event.
    /// </summary>
    protected void OnBufferingStarted()
    {
        IsBuffering = true;
        BufferingStarted?.Invoke(this, new RoutedEventArgs());
    }

    /// <summary>
    /// Raises the BufferingEnded event.
    /// </summary>
    protected void OnBufferingEnded()
    {
        IsBuffering = false;
        BufferingEnded?.Invoke(this, new RoutedEventArgs());
    }

    #endregion
}

/// <summary>
/// Event arguments for media failure events.
/// </summary>
//public sealed class MediaFailedEventArgs : EventArgs
//{
//    /// <summary>
//    /// Gets the exception that caused the failure.
//    /// </summary>
//    public Exception Exception { get; }

//    /// <summary>
//    /// Gets the error message.
//    /// </summary>
//    public string ErrorMessage => Exception.Message;

//    /// <summary>
//    /// Initializes a new instance of the MediaFailedEventArgs class.
//    /// </summary>
//    public MediaFailedEventArgs(Exception exception)
//    {
//        Exception = exception;
//    }
//}

/// <summary>
/// Event arguments for media script command events.
/// </summary>
public sealed class MediaScriptCommandEventArgs : EventArgs
{
    /// <summary>
    /// Gets the type of script command.
    /// </summary>
    public string ParameterType { get; }

    /// <summary>
    /// Gets the script command parameter.
    /// </summary>
    public string ParameterValue { get; }

    /// <summary>
    /// Initializes a new instance of the MediaScriptCommandEventArgs class.
    /// </summary>
    public MediaScriptCommandEventArgs(string parameterType, string parameterValue)
    {
        ParameterType = parameterType;
        ParameterValue = parameterValue;
    }
}

/// <summary>
/// Represents the duration of time a Timeline is active.
/// </summary>
public struct Duration
{
    private readonly TimeSpan _timeSpan;
    private readonly DurationType _type;

    /// <summary>
    /// Gets an automatic duration.
    /// </summary>
    public static Duration Automatic => new(DurationType.Automatic);

    /// <summary>
    /// Gets a forever duration.
    /// </summary>
    public static Duration Forever => new(DurationType.Forever);

    private Duration(DurationType type)
    {
        _type = type;
        _timeSpan = TimeSpan.Zero;
    }

    /// <summary>
    /// Initializes a new instance of the Duration structure.
    /// </summary>
    public Duration(TimeSpan timeSpan)
    {
        _type = DurationType.TimeSpan;
        _timeSpan = timeSpan;
    }

    /// <summary>
    /// Gets a value indicating whether this Duration represents a time span.
    /// </summary>
    public bool HasTimeSpan => _type == DurationType.TimeSpan;

    /// <summary>
    /// Gets the time span represented by this Duration.
    /// </summary>
    public TimeSpan TimeSpan => _type == DurationType.TimeSpan
        ? _timeSpan
        : throw new InvalidOperationException("Duration does not have a TimeSpan value.");

    /// <summary>
    /// Implicitly converts a TimeSpan to a Duration.
    /// </summary>
    public static implicit operator Duration(TimeSpan timeSpan) => new(timeSpan);

    private enum DurationType
    {
        Automatic,
        TimeSpan,
        Forever
    }
}
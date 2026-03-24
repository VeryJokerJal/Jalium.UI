using Jalium.UI.Media.Effects;
using Jalium.UI.Threading;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Specifies the type of transition animation to use when content changes.
/// </summary>
public enum TransitionMode
{
    // Basic (1-5)
    Crossfade,
    SlideLeft,
    SlideRight,
    SlideUp,
    SlideDown,
    ZoomIn,
    ZoomOut,

    // 3D Simulation (6-10)
    FlipHorizontal,
    FlipVertical,
    CubeRotate,
    DoorOpen,
    Carousel,

    // Mask/Wipe (11-16)
    RippleReveal,
    WipeLeft,
    WipeRight,
    WipeUp,
    WipeDown,
    WipeDiagonal,
    IrisReveal,
    ClockWipe,
    BlindsHorizontal,
    BlindsVertical,

    // Shader Effects (17-23)
    Dissolve,
    Pixelate,
    Glitch,
    ChromaticSplit,
    LiquidMorph,
    WaveDistortion,
    WindBlow,

    // Particle/Fragment (24-27)
    Shatter,
    ParticleDissolve,
    FallingTiles,
    Vortex,

    // Stylized (28-32)
    TypewriterReveal,
    MatrixRain,
    SketchReveal,
    ThermalFade,
    GlitchSlice,
}

/// <summary>
/// Defines the contract for content transition animations.
/// Transition implementations control the visual state of old/new content
/// through the <see cref="TransitionHost"/> interface.
/// </summary>
public abstract class ContentTransition : DependencyObject
{
    /// <summary>
    /// Identifies the Duration dependency property.
    /// </summary>
    public static readonly DependencyProperty DurationProperty =
        DependencyProperty.Register(nameof(Duration), typeof(Duration), typeof(ContentTransition),
            new PropertyMetadata(new Duration(TimeSpan.FromMilliseconds(400))));

    /// <summary>
    /// Identifies the EasingFunction dependency property.
    /// </summary>
    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(ContentTransition),
            new PropertyMetadata(null));

    /// <summary>
    /// Gets or sets the duration of the transition.
    /// </summary>
    public Duration Duration
    {
        get => (Duration)(GetValue(DurationProperty) ?? new Duration(TimeSpan.FromMilliseconds(400)));
        set => SetValue(DurationProperty, value);
    }

    /// <summary>
    /// Gets or sets the easing function applied to the transition progress.
    /// When null, a default CubicEase(EaseInOut) is used.
    /// </summary>
    public IEasingFunction? EasingFunction
    {
        get => (IEasingFunction?)GetValue(EasingFunctionProperty);
        set => SetValue(EasingFunctionProperty, value);
    }

    /// <summary>
    /// Gets whether this transition requires capturing the old content as a bitmap snapshot.
    /// Most transitions need this (default true).
    /// </summary>
    public virtual bool RequiresOldContentSnapshot => true;

    /// <summary>
    /// Starts the transition animation.
    /// </summary>
    /// <param name="host">The transition host providing rendering control.</param>
    /// <param name="oldSnapshot">Bitmap snapshot of the old content, may be null.</param>
    /// <param name="newContent">The new content element.</param>
    /// <param name="bounds">The size of the transition area.</param>
    /// <param name="onComplete">Callback invoked when the transition finishes.</param>
    /// <returns>A DispatcherTimer that can be stopped to cancel the transition, or null.</returns>
    public abstract DispatcherTimer? Run(
        TransitionHost host,
        ImageSource? oldSnapshot,
        UIElement? newContent,
        Size bounds,
        Action onComplete);

    /// <summary>
    /// Gets the effective easing function, falling back to CubicEase(EaseInOut) if none set.
    /// </summary>
    protected IEasingFunction EffectiveEasing =>
        EasingFunction ?? DefaultEasing;

    /// <summary>
    /// Gets the duration in milliseconds.
    /// </summary>
    protected double DurationMs =>
        Duration.HasTimeSpan ? Duration.TimeSpan.TotalMilliseconds : 400.0;

    /// <summary>
    /// Creates a DispatcherTimer that drives the transition animation frame-by-frame.
    /// </summary>
    /// <param name="durationMs">Total duration in milliseconds.</param>
    /// <param name="easing">Easing function to apply.</param>
    /// <param name="onFrame">Called each frame with eased progress (0→1).</param>
    /// <param name="onComplete">Called when the animation finishes.</param>
    /// <returns>The started DispatcherTimer.</returns>
    protected static DispatcherTimer CreateFrameTimer(
        double durationMs,
        IEasingFunction easing,
        Action<double> onFrame,
        Action onComplete)
    {
        var startTime = Environment.TickCount64;
        var timer = new DispatcherTimer { Interval = CompositionTarget.FrameInterval };
        timer.Tick += (_, _) =>
        {
            try
            {
                var elapsed = Environment.TickCount64 - startTime;
                var rawProgress = durationMs > 0 ? Math.Min(1.0, elapsed / durationMs) : 1.0;
                var easedProgress = easing.Ease(rawProgress);

                onFrame(easedProgress);

                if (rawProgress >= 1.0)
                {
                    timer.Stop();
                    onComplete();
                }
            }
            catch (Exception ex)
            {
                timer.Stop();
                System.Diagnostics.Debug.WriteLine($"[ContentTransition] Animation frame error: {ex}");
            }
        };
        timer.Start();
        return timer;
    }

    private static readonly CubicEase DefaultEasing = new() { EasingMode = EasingMode.EaseInOut };
}

/// <summary>
/// Interface implemented by TransitioningContentControl to allow transitions
/// to control the rendering of old and new content during animation.
/// </summary>
public interface TransitionHost
{
    /// <summary>
    /// Gets or sets the opacity of the old content overlay (0.0 - 1.0).
    /// </summary>
    double OverlayOpacity { get; set; }

    /// <summary>
    /// Gets or sets the transform applied to the old content overlay.
    /// </summary>
    Transform? OverlayTransform { get; set; }

    /// <summary>
    /// Gets or sets the clip geometry for the old content overlay.
    /// </summary>
    Geometry? OverlayClip { get; set; }

    /// <summary>
    /// Gets or sets the image source for the old content overlay.
    /// </summary>
    ImageSource? OverlayImage { get; set; }

    /// <summary>
    /// Gets or sets an additional overlay image (used for split effects like DoorOpen).
    /// </summary>
    ImageSource? OverlayImage2 { get; set; }

    /// <summary>
    /// Gets or sets the opacity of the second overlay.
    /// </summary>
    double Overlay2Opacity { get; set; }

    /// <summary>
    /// Gets or sets the transform for the second overlay.
    /// </summary>
    Transform? Overlay2Transform { get; set; }

    /// <summary>
    /// Gets or sets the clip for the second overlay.
    /// </summary>
    Geometry? Overlay2Clip { get; set; }

    /// <summary>
    /// Gets or sets the opacity of the new content (0.0 - 1.0).
    /// </summary>
    double NewContentOpacity { get; set; }

    /// <summary>
    /// Gets or sets the transform applied to the new content.
    /// </summary>
    Transform? NewContentTransform { get; set; }

    /// <summary>
    /// Gets or sets the clip geometry for the new content.
    /// </summary>
    Geometry? NewContentClip { get; set; }

    /// <summary>
    /// Gets the size of the transition area.
    /// </summary>
    Size TransitionBounds { get; }

    /// <summary>
    /// Gets or sets the particle array for particle-based transitions.
    /// When set, particles are rendered in OnPostRender instead of the overlay image.
    /// </summary>
    TransitionParticle[]? ActiveParticles { get; set; }

    /// <summary>
    /// Gets or sets the GPU shader mode index for shader-based transitions.
    /// -1 means no GPU shader is active; 0-9 maps to shader effect modes.
    /// </summary>
    int GpuShaderMode { get; set; }

    /// <summary>
    /// Gets or sets the GPU shader progress (0.0 - 1.0).
    /// Only used when GpuShaderMode >= 0.
    /// </summary>
    double GpuShaderProgress { get; set; }

    /// <summary>
    /// Requests a visual update of the transition layer.
    /// </summary>
    void InvalidateTransitionVisual();
}

/// <summary>
/// Represents a single particle/fragment in a particle-based transition.
/// </summary>
public struct TransitionParticle
{
    /// <summary>Source rectangle in the original bitmap.</summary>
    public Rect SourceRect;

    /// <summary>Current X position.</summary>
    public double X;

    /// <summary>Current Y position.</summary>
    public double Y;

    /// <summary>X velocity (pixels per second).</summary>
    public double VelocityX;

    /// <summary>Y velocity (pixels per second).</summary>
    public double VelocityY;

    /// <summary>Current rotation angle in degrees.</summary>
    public double Rotation;

    /// <summary>Angular velocity (degrees per second).</summary>
    public double AngularVelocity;

    /// <summary>Current opacity (0-1).</summary>
    public double Opacity;

    /// <summary>Current scale factor.</summary>
    public double Scale;

    /// <summary>Startup delay as a fraction of total progress (0-1).</summary>
    public double Delay;
}

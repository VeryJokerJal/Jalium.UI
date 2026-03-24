using Jalium.UI.Media.Effects;
using Jalium.UI.Threading;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Identifies the shader-based transition mode.
/// </summary>
public enum ShaderTransitionMode
{
    Dissolve,
    Pixelate,
    Glitch,
    ChromaticSplit,
    LiquidMorph,
    WaveDistortion,
    WindBlow,
    RippleReveal,
    ClockWipe,
    ThermalFade,
}

/// <summary>
/// Transition that uses a pixel shader to blend between old and new content.
/// The shader receives both textures and a progress value.
/// Since the framework renders via DrawingContext (not direct texture access),
/// this transition simulates the shader effect by computing per-frame visual state
/// that approximates the shader behavior.
/// </summary>
/// <remarks>
/// For full GPU shader support, the <see cref="TransitionEffect"/> system provides
/// proper HLSL shaders. This class provides fallback approximations using the managed
/// rendering pipeline (Opacity, Transform, Clip) that visually resemble the shader effects.
/// When the rendering pipeline supports applying ShaderEffect to composited layers,
/// this class can delegate to the actual GPU effects.
/// </remarks>
public sealed class ShaderTransition : ContentTransition
{
    /// <summary>
    /// Gets or sets the shader transition mode.
    /// </summary>
    public ShaderTransitionMode ShaderMode { get; set; } = ShaderTransitionMode.Dissolve;

    /// <inheritdoc />
    public override DispatcherTimer? Run(
        TransitionHost host,
        ImageSource? oldSnapshot,
        UIElement? newContent,
        Size bounds,
        Action onComplete)
    {
        // Use GPU shader rendering path: set the mode and let OnPostRender handle capture + blend
        host.GpuShaderMode = (int)ShaderMode;
        host.GpuShaderProgress = 0.0;

        // Hide the overlay and new content — the GPU shader renders everything
        host.OverlayOpacity = 0.0;
        host.NewContentOpacity = 0.0;

        return CreateFrameTimer(DurationMs, EffectiveEasing, progress =>
        {
            try
            {
                host.GpuShaderProgress = progress;
                host.InvalidateTransitionVisual();
            }
            catch
            {
                host.GpuShaderMode = -1;
                host.GpuShaderProgress = 0.0;
                throw;
            }
        }, () =>
        {
            // Reset GPU shader mode and restore visibility
            host.GpuShaderMode = -1;
            host.GpuShaderProgress = 0.0;
            host.NewContentOpacity = 1.0;
            onComplete();
        });
    }

    /// <summary>
    /// Dissolve: simulated with random-block opacity transitions.
    /// Approximated by crossfade with noise-like staggered opacity.
    /// </summary>
    private DispatcherTimer RunDissolve(TransitionHost host, Size bounds, Action onComplete)
    {
        host.NewContentOpacity = 0.0;
        host.OverlayOpacity = 1.0;

        return CreateFrameTimer(DurationMs, EffectiveEasing, progress =>
        {
            // Crossfade with accelerated mid-section for dissolve feel
            var dissolveProgress = Math.Pow(progress, 0.8);
            host.OverlayOpacity = 1.0 - dissolveProgress;
            host.NewContentOpacity = dissolveProgress;
        }, onComplete);
    }

    /// <summary>
    /// Pixelate: approximated by scaling down and up.
    /// Old content scales down (mosaic effect), then new content scales up.
    /// </summary>
    private DispatcherTimer RunPixelate(TransitionHost host, Size bounds, Action onComplete)
    {
        var centerX = bounds.Width / 2;
        var centerY = bounds.Height / 2;

        host.NewContentOpacity = 0.0;

        return CreateFrameTimer(DurationMs, EffectiveEasing, progress =>
        {
            // Scale down to simulate pixelation, then scale up
            var scaleFactor = progress < 0.5
                ? 1.0 - progress * 1.4  // Shrink to ~0.3
                : 0.3 + (progress - 0.5) * 1.4; // Grow back to 1.0

            scaleFactor = Math.Clamp(scaleFactor, 0.1, 1.0);

            if (progress < 0.5)
            {
                host.OverlayTransform = new ScaleTransform
                {
                    ScaleX = scaleFactor, ScaleY = scaleFactor,
                    CenterX = centerX, CenterY = centerY,
                };
                host.OverlayOpacity = 1.0;
                host.NewContentOpacity = 0.0;
            }
            else
            {
                host.OverlayOpacity = 0.0;
                host.NewContentOpacity = 1.0;
                host.NewContentTransform = new ScaleTransform
                {
                    ScaleX = scaleFactor, ScaleY = scaleFactor,
                    CenterX = centerX, CenterY = centerY,
                };
            }
        }, onComplete);
    }

    /// <summary>
    /// Glitch: rapid random offset + opacity flashing.
    /// </summary>
    private DispatcherTimer RunGlitch(TransitionHost host, Size bounds, Action onComplete)
    {
        var rng = new Random(42);
        var frameCount = 0;

        host.NewContentOpacity = 0.0;

        return CreateFrameTimer(DurationMs, EffectiveEasing, progress =>
        {
            frameCount++;
            var intensity = Math.Sin(progress * Math.PI); // Peak at midpoint

            // Random horizontal displacement
            var offsetX = (rng.NextDouble() - 0.5) * bounds.Width * 0.1 * intensity;
            var offsetY = (rng.NextDouble() - 0.5) * bounds.Height * 0.02 * intensity;

            // Flash between old and new
            var showNew = rng.NextDouble() < progress;

            if (showNew || progress > 0.8)
            {
                host.OverlayOpacity = frameCount % 3 == 0 ? 0.3 * (1.0 - progress) : 0.0;
                host.NewContentOpacity = 1.0;
                host.NewContentTransform = new TranslateTransform
                {
                    X = offsetX * 0.3,
                    Y = offsetY * 0.3,
                };
            }
            else
            {
                host.OverlayOpacity = 1.0;
                host.NewContentOpacity = frameCount % 4 == 0 ? progress : 0.0;
                host.OverlayTransform = new TranslateTransform
                {
                    X = offsetX,
                    Y = offsetY,
                };
            }
        }, onComplete);
    }

    /// <summary>
    /// ChromaticSplit: overlay slides with offset + crossfade.
    /// </summary>
    private DispatcherTimer RunChromaticSplit(TransitionHost host, Size bounds, Action onComplete)
    {
        host.NewContentOpacity = 0.0;

        return CreateFrameTimer(DurationMs, EffectiveEasing, progress =>
        {
            var spread = (1.0 - progress) * bounds.Width * 0.05;

            host.OverlayTransform = new TranslateTransform { X = spread, Y = spread * 0.3 };
            host.OverlayOpacity = 1.0 - progress;

            host.NewContentTransform = new TranslateTransform { X = -spread * 0.5, Y = -spread * 0.2 };
            host.NewContentOpacity = progress;
        }, onComplete);
    }

    /// <summary>
    /// LiquidMorph: sinusoidal offset + crossfade.
    /// </summary>
    private DispatcherTimer RunLiquidMorph(TransitionHost host, Size bounds, Action onComplete)
    {
        var centerX = bounds.Width / 2;
        var centerY = bounds.Height / 2;

        host.NewContentOpacity = 0.0;

        return CreateFrameTimer(DurationMs, EffectiveEasing, progress =>
        {
            var time = progress * Math.PI * 2;
            var strength = Math.Sin(progress * Math.PI) * 0.08;
            var offsetX = Math.Sin(time) * bounds.Width * strength;
            var offsetY = Math.Cos(time * 1.3) * bounds.Height * strength;

            var scale = 1.0 - Math.Sin(progress * Math.PI) * 0.05;

            host.OverlayTransform = new TransformGroup
            {
                Children =
                {
                    new ScaleTransform { ScaleX = scale, ScaleY = scale, CenterX = centerX, CenterY = centerY },
                    new TranslateTransform { X = offsetX, Y = offsetY },
                }
            };
            host.OverlayOpacity = 1.0 - progress;

            host.NewContentTransform = new TransformGroup
            {
                Children =
                {
                    new ScaleTransform { ScaleX = scale, ScaleY = scale, CenterX = centerX, CenterY = centerY },
                    new TranslateTransform { X = -offsetX * 0.5, Y = -offsetY * 0.5 },
                }
            };
            host.NewContentOpacity = progress;
        }, onComplete);
    }

    /// <summary>
    /// WaveDistortion: sinusoidal horizontal offset + crossfade.
    /// </summary>
    private DispatcherTimer RunWaveDistortion(TransitionHost host, Size bounds, Action onComplete)
    {
        host.NewContentOpacity = 0.0;

        return CreateFrameTimer(DurationMs, EffectiveEasing, progress =>
        {
            var amplitude = Math.Sin(progress * Math.PI) * bounds.Width * 0.08;
            var wave = Math.Sin(progress * Math.PI * 4) * amplitude;

            host.OverlayTransform = new TranslateTransform { X = wave };
            host.OverlayOpacity = 1.0 - progress;

            host.NewContentTransform = new TranslateTransform { X = -wave * 0.5 };
            host.NewContentOpacity = progress;
        }, onComplete);
    }

    /// <summary>
    /// WindBlow: progressive slide-out with fade.
    /// </summary>
    private DispatcherTimer RunWindBlow(TransitionHost host, Size bounds, Action onComplete)
    {
        host.NewContentOpacity = 1.0;

        return CreateFrameTimer(DurationMs, EffectiveEasing, progress =>
        {
            // Old content "blows" to the right
            var blowDistance = bounds.Width * progress * 1.2;
            var blowUp = Math.Sin(progress * Math.PI * 3) * bounds.Height * 0.05;

            host.OverlayTransform = new TranslateTransform { X = blowDistance, Y = -blowUp };
            host.OverlayOpacity = Math.Max(0, 1.0 - progress * 1.5);
        }, onComplete);
    }

    /// <summary>
    /// RippleReveal: expanding circle clip with slight scale pulse.
    /// </summary>
    private DispatcherTimer RunRippleReveal(TransitionHost host, Size bounds, Action onComplete)
    {
        var centerX = bounds.Width / 2;
        var centerY = bounds.Height / 2;
        var maxRadius = Math.Sqrt(centerX * centerX + centerY * centerY);

        host.NewContentOpacity = 1.0;

        return CreateFrameTimer(DurationMs, EffectiveEasing, progress =>
        {
            var radius = maxRadius * progress * 1.2;

            // New content revealed through expanding circle
            host.NewContentClip = new EllipseGeometry
            {
                Center = new Point(centerX, centerY),
                RadiusX = radius,
                RadiusY = radius,
            };

            // Ripple effect: slight scale pulse on old content
            var ripple = Math.Sin(progress * Math.PI * 3) * 0.02 * (1.0 - progress);
            host.OverlayTransform = new ScaleTransform
            {
                ScaleX = 1.0 + ripple,
                ScaleY = 1.0 + ripple,
                CenterX = centerX,
                CenterY = centerY,
            };
        }, onComplete);
    }

    /// <summary>
    /// ClockWipe: pie-shaped clip that sweeps clockwise.
    /// Approximated using PathGeometry with arc segments.
    /// </summary>
    private DispatcherTimer RunClockWipe(TransitionHost host, Size bounds, Action onComplete)
    {
        var centerX = bounds.Width / 2;
        var centerY = bounds.Height / 2;
        var maxRadius = Math.Sqrt(bounds.Width * bounds.Width + bounds.Height * bounds.Height);

        host.NewContentOpacity = 1.0;

        return CreateFrameTimer(DurationMs, EffectiveEasing, progress =>
        {
            // Build a pie-shaped clip for the new content
            // Sweep from 12 o'clock clockwise
            var sweepAngle = progress * 360.0;

            if (sweepAngle >= 359.5)
            {
                // Full circle - remove clip
                host.NewContentClip = null;
                host.OverlayOpacity = 0.0;
                return;
            }

            // Build pie slice path
            var startAngleRad = -Math.PI / 2; // 12 o'clock
            var endAngleRad = startAngleRad + sweepAngle * Math.PI / 180.0;

            var startX = centerX + Math.Cos(startAngleRad) * maxRadius;
            var startY = centerY + Math.Sin(startAngleRad) * maxRadius;
            var endX = centerX + Math.Cos(endAngleRad) * maxRadius;
            var endY = centerY + Math.Sin(endAngleRad) * maxRadius;

            var figure = new PathFigure { StartPoint = new Point(centerX, centerY), IsClosed = true };
            figure.Segments.Add(new LineSegment { Point = new Point(startX, startY) });

            // For large arcs, add intermediate points to approximate the arc
            var isLargeArc = sweepAngle > 180;
            var steps = Math.Max(2, (int)(sweepAngle / 30));

            for (int i = 1; i <= steps; i++)
            {
                var t = (double)i / steps;
                var angle = startAngleRad + (endAngleRad - startAngleRad) * t;
                var px = centerX + Math.Cos(angle) * maxRadius;
                var py = centerY + Math.Sin(angle) * maxRadius;
                figure.Segments.Add(new LineSegment { Point = new Point(px, py) });
            }

            var path = new PathGeometry { FillRule = FillRule.Nonzero };
            path.Figures.Add(figure);
            host.NewContentClip = path;
        }, onComplete);
    }

    /// <summary>
    /// ThermalFade: simulated with color tinting via overlay opacity.
    /// Phase 1: old content fades with warm tint.
    /// Phase 2: new content appears.
    /// </summary>
    private DispatcherTimer RunThermalFade(TransitionHost host, Size bounds, Action onComplete)
    {
        host.NewContentOpacity = 0.0;

        return CreateFrameTimer(DurationMs, EffectiveEasing, progress =>
        {
            if (progress < 0.5)
            {
                // Phase 1: old content with increasing "glow" effect via opacity
                host.OverlayOpacity = 1.0;
                host.NewContentOpacity = 0.0;

                // Scale up slightly for "heat expansion" feel
                var expansion = progress * 0.06;
                var centerX = bounds.Width / 2;
                var centerY = bounds.Height / 2;
                host.OverlayTransform = new ScaleTransform
                {
                    ScaleX = 1.0 + expansion,
                    ScaleY = 1.0 + expansion,
                    CenterX = centerX,
                    CenterY = centerY,
                };
            }
            else
            {
                // Phase 2: crossfade to new content
                var phase2 = (progress - 0.5) * 2.0;
                host.OverlayOpacity = 1.0 - phase2;
                host.NewContentOpacity = phase2;
                host.OverlayTransform = null;
            }
        }, onComplete);
    }
}

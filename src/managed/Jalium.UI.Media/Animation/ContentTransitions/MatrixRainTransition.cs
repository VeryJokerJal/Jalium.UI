using Jalium.UI.Threading;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Transition that overlays a "Matrix rain" effect (falling green characters)
/// while cross-fading between old and new content.
/// The rain intensifies during the transition midpoint, then clears.
/// </summary>
/// <remarks>
/// Since the managed rendering pipeline uses DrawingContext, the actual character rain
/// is approximated by animating vertical strips of the overlay with staggered timing.
/// For full character rendering, a custom OnPostRender with DrawText would be needed.
/// </remarks>
public sealed class MatrixRainTransition : ContentTransition
{
    /// <summary>
    /// Gets or sets the number of rain columns (default 12).
    /// </summary>
    public int RainColumns { get; set; } = 12;

    /// <inheritdoc />
    public override DispatcherTimer? Run(
        TransitionHost host,
        ImageSource? oldSnapshot,
        UIElement? newContent,
        Size bounds,
        Action onComplete)
    {
        var durationMs = DurationMs;
        var width = bounds.Width;
        var height = bounds.Height;
        var colCount = Math.Max(4, RainColumns);
        var colWidth = width / colCount;

        var rng = new Random(99);
        // Each column has a random speed and start delay
        var columnSpeeds = new double[colCount];
        var columnDelays = new double[colCount];
        for (int i = 0; i < colCount; i++)
        {
            columnSpeeds[i] = 0.8 + rng.NextDouble() * 1.2;
            columnDelays[i] = rng.NextDouble() * 0.3;
        }

        // Use particles to simulate rain columns falling over old content
        var particles = new TransitionParticle[colCount];
        for (int i = 0; i < colCount; i++)
        {
            particles[i] = new TransitionParticle
            {
                SourceRect = new Rect(i * colWidth, 0, colWidth, height),
                X = 0,
                Y = -height, // Start above
                VelocityX = 0,
                VelocityY = height * columnSpeeds[i],
                Rotation = 0,
                AngularVelocity = 0,
                Opacity = 1.0,
                Scale = 1.0,
                Delay = columnDelays[i],
            };
        }

        host.NewContentOpacity = 0.0;
        host.ActiveParticles = particles;
        double previousProgress = 0d;

        var easing = EffectiveEasing;
        return CreateFrameTimer(durationMs, easing, (rawProgress, easedProgress) =>
        {

            // Crossfade timing
            host.NewContentOpacity = Math.Min(1.0, rawProgress * 1.5);

            // Update rain columns
            for (int i = 0; i < particles.Length; i++)
            {
                ref var p = ref particles[i];
                var localProgress = p.Delay >= 1.0 ? 1.0 : Math.Clamp((rawProgress - p.Delay) / (1.0 - p.Delay), 0.0, 1.0);
                if (localProgress <= 0) continue;
                double dt = GetParticleFrameSeconds(previousProgress, rawProgress, p.Delay, durationMs);

                // Column falls down
                p.Y += p.VelocityY * dt;

                // When column passes bottom, it wraps around (for continuous rain effect)
                if (p.Y > height * 0.5)
                {
                    p.Y = -height * 0.5;
                    p.VelocityY *= 0.8; // Slow down each wrap
                }

                // Fade based on overall progress
                p.Opacity = Math.Max(0, 1.0 - rawProgress);
            }

            previousProgress = rawProgress;
            host.ActiveParticles = particles;
            host.InvalidateTransitionVisual();

        }, () =>
        {
            host.ActiveParticles = null;
            onComplete();
        });
    }
}

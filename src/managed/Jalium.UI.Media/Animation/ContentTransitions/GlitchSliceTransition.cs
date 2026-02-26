using Jalium.UI.Threading;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Transition that splits the old content into horizontal slices that randomly
/// slide left and right. During the sliding, content switches from old to new.
/// Slices then return to their original positions.
/// </summary>
public sealed class GlitchSliceTransition : ContentTransition
{
    /// <summary>
    /// Gets or sets the number of horizontal slices (default 10).
    /// </summary>
    public int SliceCount { get; set; } = 10;

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
        var sliceCount = Math.Max(3, SliceCount);
        var sliceHeight = height / sliceCount;

        var rng = new Random(51);

        // Generate random offsets and timing for each slice
        var maxOffsets = new double[sliceCount];
        var speeds = new double[sliceCount];
        var switchTimes = new double[sliceCount]; // Progress at which slice switches content

        for (int i = 0; i < sliceCount; i++)
        {
            // Random max displacement: 5-30% of width, alternating direction
            maxOffsets[i] = (rng.NextDouble() * 0.25 + 0.05) * width * (i % 2 == 0 ? 1 : -1);
            speeds[i] = 0.8 + rng.NextDouble() * 0.8;
            switchTimes[i] = 0.35 + rng.NextDouble() * 0.3; // Switch content between 35%-65%
        }

        // Use particles for slice management
        var particles = new TransitionParticle[sliceCount];
        for (int i = 0; i < sliceCount; i++)
        {
            particles[i] = new TransitionParticle
            {
                SourceRect = new Rect(0, i * sliceHeight, width, Math.Min(sliceHeight, height - i * sliceHeight)),
                X = 0, Y = 0,
                VelocityX = 0, VelocityY = 0,
                Rotation = 0,
                AngularVelocity = 0,
                Opacity = 1.0,
                Scale = 1.0,
                Delay = 0,
            };
        }

        host.NewContentOpacity = 0.0;
        host.OverlayOpacity = 0.0; // We use particles instead
        host.ActiveParticles = particles;

        var easing = EffectiveEasing;
        var startTime = Environment.TickCount64;
        var timer = new DispatcherTimer { Interval = CompositionTarget.FrameInterval };
        timer.Tick += (_, _) =>
        {
            var elapsed = Environment.TickCount64 - startTime;
            var rawProgress = durationMs > 0 ? Math.Min(1.0, elapsed / durationMs) : 1.0;

            for (int i = 0; i < sliceCount; i++)
            {
                ref var p = ref particles[i];
                var t = rawProgress * speeds[i];

                // Displacement curve: 0 → max → 0
                // Quick out, hold, quick back
                double displacement;
                if (t < 0.2)
                    displacement = maxOffsets[i] * (t / 0.2); // Slide out
                else if (t < 0.7)
                    displacement = maxOffsets[i]; // Hold
                else if (t < 1.0)
                    displacement = maxOffsets[i] * (1.0 - (t - 0.7) / 0.3); // Slide back
                else
                    displacement = 0;

                // Add jitter for glitch feel
                var jitter = Math.Sin(rawProgress * 50 + i * 7) * 3.0 *
                    Math.Sin(rawProgress * Math.PI);

                p.X = displacement + jitter;

                // Opacity flicker during peak displacement
                var flickerIntensity = Math.Sin(rawProgress * Math.PI);
                if (rng.NextDouble() < flickerIntensity * 0.3 && rawProgress < 0.8)
                    p.Opacity = 0.7 + rng.NextDouble() * 0.3;
                else
                    p.Opacity = rawProgress > switchTimes[i] ? Math.Max(0, 1.0 - (rawProgress - switchTimes[i]) * 3) : 1.0;
            }

            // Show new content progressively
            host.NewContentOpacity = Math.Min(1.0, rawProgress * 1.8);

            host.ActiveParticles = particles;
            host.InvalidateTransitionVisual();

            if (rawProgress >= 1.0)
            {
                timer.Stop();
                host.ActiveParticles = null;
                onComplete();
            }
        };
        timer.Start();
        return timer;
    }
}

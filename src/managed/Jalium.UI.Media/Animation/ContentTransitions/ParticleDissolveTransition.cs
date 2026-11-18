using Jalium.UI.Threading;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Transition that dissolves old content into small particles that float upward
/// and fade out. Particles are triggered row by row from top to bottom.
/// </summary>
public sealed class ParticleDissolveTransition : ContentTransition
{
    /// <summary>
    /// Gets or sets the particle size in pixels (default 16).
    /// </summary>
    public int ParticleSize { get; set; } = 16;

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
        var size = Math.Max(4, ParticleSize);

        var cols = Math.Max(1, (int)Math.Ceiling(width / size));
        var rows = Math.Max(1, (int)Math.Ceiling(height / size));
        var rng = new Random(31);
        var particles = new TransitionParticle[cols * rows];

        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                var idx = row * cols + col;
                var x = col * size;
                var y = row * size;
                var pw = Math.Min(size, width - x);
                var ph = Math.Min(size, height - y);

                // Stagger delay: top rows dissolve first
                var rowDelay = (double)row / rows * 0.5;
                var randomDelay = rng.NextDouble() * 0.1;

                particles[idx] = new TransitionParticle
                {
                    SourceRect = new Rect(x, y, pw, ph),
                    X = 0, Y = 0,
                    VelocityX = (rng.NextDouble() - 0.5) * 30,
                    VelocityY = -40 - rng.NextDouble() * 60, // Float upward
                    Rotation = 0,
                    AngularVelocity = (rng.NextDouble() - 0.5) * 90,
                    Opacity = 1.0,
                    Scale = 1.0,
                    Delay = rowDelay + randomDelay,
                };
            }
        }

        host.NewContentOpacity = 0.0;
        host.ActiveParticles = particles;
        double previousProgress = 0d;

        return CreateFrameTimer(durationMs, EffectiveEasing, (rawProgress, easedProgress) =>
        {

            host.NewContentOpacity = Math.Min(1.0, rawProgress * 1.5);

            for (int i = 0; i < particles.Length; i++)
            {
                ref var p = ref particles[i];
                var localProgress = p.Delay < 1.0
                    ? Math.Max(0, (rawProgress - p.Delay) / (1.0 - p.Delay))
                    : 1.0;
                if (localProgress <= 0) continue;
                double dt = GetParticleFrameSeconds(previousProgress, rawProgress, p.Delay, durationMs);

                p.X += p.VelocityX * dt;
                p.Y += p.VelocityY * dt;
                p.Rotation += p.AngularVelocity * dt;
                p.Opacity = Math.Max(0, 1.0 - localProgress * 1.2);
                p.Scale = Math.Max(0.01, 1.0 - localProgress * 0.8);
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

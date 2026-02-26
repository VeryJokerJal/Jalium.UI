using Jalium.UI.Threading;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Transition that splits old content into vertical columns (tiles) that fall
/// downward with rotation and bounce. Columns fall in sequence from left to right.
/// </summary>
public sealed class FallingTilesTransition : ContentTransition
{
    /// <summary>
    /// Gets or sets the number of columns (default 8).
    /// </summary>
    public int ColumnCount { get; set; } = 8;

    /// <summary>
    /// Gets or sets gravity in pixels/sec^2 (default 1200).
    /// </summary>
    public double Gravity { get; set; } = 1200.0;

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
        var colCount = Math.Max(2, ColumnCount);
        var gravity = Gravity;

        var colWidth = width / colCount;
        var particles = new TransitionParticle[colCount];
        var rng = new Random(7);

        for (int i = 0; i < colCount; i++)
        {
            var delay = (double)i / colCount * 0.4 + rng.NextDouble() * 0.05;

            particles[i] = new TransitionParticle
            {
                SourceRect = new Rect(i * colWidth, 0, Math.Min(colWidth, width - i * colWidth), height),
                X = 0, Y = 0,
                VelocityX = 0,
                VelocityY = 0,
                Rotation = 0,
                AngularVelocity = (rng.NextDouble() - 0.5) * 60 + (i % 2 == 0 ? 15 : -15),
                Opacity = 1.0,
                Scale = 1.0,
                Delay = delay,
            };
        }

        host.NewContentOpacity = 0.0;
        host.ActiveParticles = particles;

        var startTime = Environment.TickCount64;
        var timer = new DispatcherTimer { Interval = CompositionTarget.FrameInterval };
        timer.Tick += (_, _) =>
        {
            var elapsed = Environment.TickCount64 - startTime;
            var rawProgress = durationMs > 0 ? Math.Min(1.0, elapsed / durationMs) : 1.0;

            host.NewContentOpacity = Math.Min(1.0, rawProgress * 2.0);

            var dt = 1.0 / 60.0;
            for (int i = 0; i < particles.Length; i++)
            {
                ref var p = ref particles[i];
                var localProgress = p.Delay < 1.0
                    ? Math.Max(0, (rawProgress - p.Delay) / (1.0 - p.Delay))
                    : 1.0;
                if (localProgress <= 0) continue;

                // Apply gravity
                p.VelocityY += gravity * dt;
                p.Y += p.VelocityY * dt;
                p.Rotation += p.AngularVelocity * dt;

                // Bounce off bottom edge
                var bottomLimit = height * 1.5;
                if (p.Y > bottomLimit)
                {
                    p.Y = bottomLimit;
                    p.VelocityY = -p.VelocityY * 0.4; // Damped bounce
                    p.AngularVelocity *= 0.6;
                }

                p.Opacity = Math.Max(0, 1.0 - localProgress);
            }

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

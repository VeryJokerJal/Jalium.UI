using Jalium.UI.Threading;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Transition that shatters old content into irregular fragments that fly apart
/// with random velocities, rotation, and gravity. New content is revealed underneath.
/// </summary>
public sealed class ShatterTransition : ContentTransition
{
    /// <summary>
    /// Gets or sets the grid columns for fragment generation (default 6).
    /// </summary>
    public int Columns { get; set; } = 6;

    /// <summary>
    /// Gets or sets the grid rows for fragment generation (default 6).
    /// </summary>
    public int Rows { get; set; } = 6;

    /// <summary>
    /// Gets or sets the gravity acceleration in pixels/sec^2 (default 800).
    /// </summary>
    public double Gravity { get; set; } = 800.0;

    /// <inheritdoc />
    public override DispatcherTimer? Run(
        TransitionHost host,
        ImageSource? oldSnapshot,
        UIElement? newContent,
        Size bounds,
        Action onComplete)
    {
        var easing = EffectiveEasing;
        var durationMs = DurationMs;
        var width = bounds.Width;
        var height = bounds.Height;
        var cols = Math.Max(2, Columns);
        var rows = Math.Max(2, Rows);
        var gravity = Gravity;

        // Generate particles from grid with random offsets
        var rng = new Random(17);
        var cellW = width / cols;
        var cellH = height / rows;
        var particles = new TransitionParticle[cols * rows];

        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                var idx = row * cols + col;
                var x = col * cellW;
                var y = row * cellH;

                // Add random jitter to make fragments irregular
                var jitterX = (rng.NextDouble() - 0.5) * cellW * 0.3;
                var jitterY = (rng.NextDouble() - 0.5) * cellH * 0.3;
                var jitterW = cellW + (rng.NextDouble() - 0.5) * cellW * 0.2;
                var jitterH = cellH + (rng.NextDouble() - 0.5) * cellH * 0.2;

                particles[idx] = new TransitionParticle
                {
                    SourceRect = new Rect(
                        Math.Max(0, x + jitterX),
                        Math.Max(0, y + jitterY),
                        Math.Min(jitterW, width - x),
                        Math.Min(jitterH, height - y)),
                    X = 0, Y = 0,
                    VelocityX = (rng.NextDouble() - 0.5) * 600,
                    VelocityY = -200 - rng.NextDouble() * 300, // Initial upward burst
                    Rotation = 0,
                    AngularVelocity = (rng.NextDouble() - 0.5) * 720,
                    Opacity = 1.0,
                    Scale = 1.0,
                    Delay = rng.NextDouble() * 0.15, // Slight stagger
                };
            }
        }

        host.NewContentOpacity = 0.0;
        host.ActiveParticles = particles;

        var startTime = Environment.TickCount64;
        var timer = new DispatcherTimer { Interval = CompositionTarget.FrameInterval };
        timer.Tick += (_, _) =>
        {
            var elapsed = Environment.TickCount64 - startTime;
            var rawProgress = durationMs > 0 ? Math.Min(1.0, elapsed / durationMs) : 1.0;

            // Show new content early
            host.NewContentOpacity = Math.Min(1.0, rawProgress * 2.0);

            // Update each particle with physics
            var dt = Math.Max(1.0 / 240.0, Math.Min(1.0 / 15.0, CompositionTarget.FrameInterval.TotalSeconds));
            if (dt <= 0) dt = 1.0 / 60.0;
            for (int i = 0; i < particles.Length; i++)
            {
                ref var p = ref particles[i];
                var localProgress = p.Delay >= 1.0 ? 1.0 : Math.Clamp((rawProgress - p.Delay) / (1.0 - p.Delay), 0.0, 1.0);
                if (localProgress <= 0) continue;

                p.VelocityY += gravity * dt;
                p.X += p.VelocityX * dt;
                p.Y += p.VelocityY * dt;
                p.Rotation += p.AngularVelocity * dt;
                p.Opacity = Math.Max(0, 1.0 - localProgress * 1.5);
                p.Scale = Math.Max(0.1, 1.0 - localProgress * 0.5);
            }

            host.ActiveParticles = particles; // Trigger re-render
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

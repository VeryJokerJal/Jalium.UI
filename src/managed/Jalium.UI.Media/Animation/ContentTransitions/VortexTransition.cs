using Jalium.UI.Threading;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Transition that spirals old content fragments into the center like a vortex/drain.
/// Fragments rotate around the center while shrinking and converging.
/// New content expands from the center.
/// </summary>
public sealed class VortexTransition : ContentTransition
{
    /// <summary>
    /// Gets or sets the grid size for fragment generation (default 6).
    /// </summary>
    public int GridSize { get; set; } = 6;

    /// <summary>
    /// Gets or sets the number of rotations during the vortex (default 2).
    /// </summary>
    public double Rotations { get; set; } = 2.0;

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
        var centerX = width / 2;
        var centerY = height / 2;
        var grid = Math.Max(2, GridSize);
        var totalRotation = Rotations * 360.0;

        var cellW = width / grid;
        var cellH = height / grid;
        var rng = new Random(13);
        var particles = new TransitionParticle[grid * grid];

        // Store initial positions for polar interpolation
        var initialAngles = new double[particles.Length];
        var initialDistances = new double[particles.Length];

        for (int row = 0; row < grid; row++)
        {
            for (int col = 0; col < grid; col++)
            {
                var idx = row * grid + col;
                var x = col * cellW;
                var y = row * cellH;
                var pw = Math.Min(cellW, width - x);
                var ph = Math.Min(cellH, height - y);

                // Fragment center relative to vortex center
                var fragCenterX = x + pw / 2 - centerX;
                var fragCenterY = y + ph / 2 - centerY;

                initialAngles[idx] = Math.Atan2(fragCenterY, fragCenterX);
                initialDistances[idx] = Math.Sqrt(fragCenterX * fragCenterX + fragCenterY * fragCenterY);

                particles[idx] = new TransitionParticle
                {
                    SourceRect = new Rect(x, y, pw, ph),
                    X = 0, Y = 0,
                    VelocityX = 0, VelocityY = 0,
                    Rotation = 0,
                    AngularVelocity = 0,
                    Opacity = 1.0,
                    Scale = 1.0,
                    Delay = rng.NextDouble() * 0.1,
                };
            }
        }

        host.NewContentOpacity = 0.0;
        host.ActiveParticles = particles;

        var easing = EffectiveEasing;
        var startTime = Environment.TickCount64;
        var timer = new DispatcherTimer { Interval = CompositionTarget.FrameInterval };
        timer.Tick += (_, _) =>
        {
            var elapsed = Environment.TickCount64 - startTime;
            var rawProgress = durationMs > 0 ? Math.Min(1.0, elapsed / durationMs) : 1.0;
            var easedProgress = easing.Ease(rawProgress);

            // New content expands from center
            host.NewContentOpacity = Math.Min(1.0, rawProgress * 1.5);
            if (rawProgress < 0.7)
            {
                var newScale = 0.5 + rawProgress * 0.5 / 0.7;
                host.NewContentTransform = new ScaleTransform
                {
                    ScaleX = newScale, ScaleY = newScale,
                    CenterX = centerX, CenterY = centerY,
                };
            }
            else
            {
                host.NewContentTransform = null;
            }

            // Update vortex particles
            for (int i = 0; i < particles.Length; i++)
            {
                ref var p = ref particles[i];
                var localProgress = p.Delay >= 1.0 ? 1.0 : Math.Clamp((easedProgress - p.Delay) / (1.0 - p.Delay), 0.0, 1.0);
                if (localProgress <= 0) continue;

                // Spiral inward: distance shrinks, angle increases
                var angle = initialAngles[i] + totalRotation * localProgress * Math.PI / 180.0;
                var distance = initialDistances[i] * (1.0 - localProgress);

                // New position relative to original position
                var newX = Math.Cos(angle) * distance + centerX - (p.SourceRect.X + p.SourceRect.Width / 2);
                var newY = Math.Sin(angle) * distance + centerY - (p.SourceRect.Y + p.SourceRect.Height / 2);

                p.X = newX;
                p.Y = newY;
                p.Rotation = totalRotation * localProgress;
                p.Scale = Math.Max(0.01, 1.0 - localProgress);
                p.Opacity = Math.Max(0, 1.0 - localProgress * 1.3);
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

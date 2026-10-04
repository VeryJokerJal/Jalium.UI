namespace Jalium.UI.Styling;

internal enum CssAnimationDirection : byte
{
    Normal,
    Reverse,
    Alternate,
    AlternateReverse,
}

internal enum CssAnimationFillMode : byte
{
    None,
    Forwards,
    Backwards,
    Both,
}

/// <summary>One animation's position before per-keyframe easing is applied.</summary>
internal readonly record struct CssAnimationSample(
    bool Applies, bool Finished, bool DuringDelay, double Progress);

/// <summary>Turns elapsed time into a cycle position, including delays, repeats and fill.</summary>
internal readonly record struct CssAnimationTiming(
    double DurationMilliseconds,
    double DelayMilliseconds,
    double IterationCount,
    CssAnimationDirection Direction,
    CssAnimationFillMode FillMode)
{
    internal CssAnimationSample Sample(double elapsedMilliseconds)
    {
        if (!double.IsFinite(elapsedMilliseconds) ||
            !double.IsFinite(DurationMilliseconds) || DurationMilliseconds < 0 ||
            !double.IsFinite(DelayMilliseconds) ||
            double.IsNaN(IterationCount) || IterationCount < 0)
            throw new ArgumentOutOfRangeException(nameof(elapsedMilliseconds), "Invalid CSS animation timing input.");

        var activeTime = elapsedMilliseconds - DelayMilliseconds;
        if (activeTime < 0)
        {
            var backwards = FillMode is CssAnimationFillMode.Backwards or CssAnimationFillMode.Both;
            return new CssAnimationSample(backwards, false, true,
                IsReversed(0) ? 1 : 0);
        }

        var activeDuration = DurationMilliseconds == 0 ? 0 : DurationMilliseconds * IterationCount;
        if (DurationMilliseconds == 0 || IterationCount == 0 || activeTime >= activeDuration)
        {
            var forwards = FillMode is CssAnimationFillMode.Forwards or CssAnimationFillMode.Both;
            return new CssAnimationSample(forwards, true, false, FinalProgress());
        }

        var cycle = activeTime / DurationMilliseconds;
        var iteration = Math.Floor(cycle);
        return new CssAnimationSample(true, false, false,
            DirectedProgress(iteration, cycle - iteration));
    }

    private double FinalProgress()
    {
        if (IterationCount == 0) return DirectedProgress(0, 0);
        if (double.IsPositiveInfinity(IterationCount)) return DirectedProgress(0, 1);
        var whole = Math.Floor(IterationCount);
        var fractional = IterationCount - whole;
        return fractional == 0
            ? DirectedProgress(whole - 1, 1)
            : DirectedProgress(whole, fractional);
    }

    private double DirectedProgress(double iteration, double progress)
        => IsReversed(iteration) ? 1 - progress : progress;

    private bool IsReversed(double iteration) => Direction switch
    {
        CssAnimationDirection.Reverse => true,
        CssAnimationDirection.Alternate => iteration % 2 != 0,
        CssAnimationDirection.AlternateReverse => iteration % 2 == 0,
        _ => false,
    };
}

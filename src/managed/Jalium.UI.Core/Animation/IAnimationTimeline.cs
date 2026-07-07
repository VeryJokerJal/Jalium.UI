namespace Jalium.UI;

/// <summary>
/// Represents the fill behavior for an animation timeline.
/// Mirrors FillBehavior from Jalium.UI.Media.Animation.
/// </summary>
public enum AnimationFillBehavior
{
    /// <summary>
    /// The timeline holds its final value after its active period ends.
    /// </summary>
    HoldEnd,

    /// <summary>
    /// The timeline stops when its active period ends and the property reverts to its base value.
    /// </summary>
    Stop
}

/// <summary>
/// Specifies the reference point of an <see cref="IAnimationClock.Seek"/> offset.
/// Core-side counterpart of the seek origins used by Storyboard/clock seeking.
/// </summary>
public enum AnimationSeekOrigin
{
    /// <summary>
    /// The offset is measured forward from the begin time of the timeline.
    /// </summary>
    BeginTime,

    /// <summary>
    /// The offset is measured backward from the end of the timeline's duration.
    /// </summary>
    Duration
}

/// <summary>
/// Interface for animation timelines. Implemented by AnimationTimeline in Jalium.UI.Media.
/// </summary>
public interface IAnimationTimeline
{
    /// <summary>
    /// Gets the type of value that this animation produces.
    /// </summary>
    Type TargetPropertyType { get; }

    /// <summary>
    /// Gets the fill behavior for this animation.
    /// </summary>
    AnimationFillBehavior AnimationFillBehavior { get; }

    /// <summary>
    /// Gets the current animated value.
    /// </summary>
    /// <param name="defaultOriginValue">The default origin value.</param>
    /// <param name="defaultDestinationValue">The default destination value.</param>
    /// <param name="clock">The animation clock.</param>
    /// <returns>The current animated value.</returns>
    object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, IAnimationClock clock);

    /// <summary>
    /// Creates a clock for this timeline.
    /// </summary>
    /// <returns>A new animation clock.</returns>
    IAnimationClock CreateClock();
}

/// <summary>
/// Interface for animation clocks. Implemented by AnimationClock in Jalium.UI.Media.
/// </summary>
public interface IAnimationClock
{
    /// <summary>
    /// Gets the current progress of the animation (0.0 to 1.0).
    /// </summary>
    double CurrentProgress { get; }

    /// <summary>
    /// Gets whether this clock is currently running.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Gets the timeline associated with this clock.
    /// </summary>
    IAnimationTimeline? Timeline { get; }

    /// <summary>
    /// Occurs when the animation completes.
    /// </summary>
    event EventHandler? Completed;

    /// <summary>
    /// Starts the animation.
    /// </summary>
    void Begin();

    /// <summary>
    /// Starts the animation at an explicit start timestamp
    /// (<see cref="System.Diagnostics.Stopwatch"/> ticks), so a clock whose start
    /// was deferred to the first rendered frame begins exactly at that frame's
    /// unified timestamp. The default implementation ignores the timestamp and
    /// falls back to <see cref="Begin"/>, keeping external implementations working.
    /// </summary>
    /// <param name="frameTimestamp">The frame timestamp in Stopwatch ticks.</param>
    void BeginAt(long frameTimestamp) => Begin();

    /// <summary>
    /// Stops the animation.
    /// </summary>
    void Stop();

    /// <summary>
    /// Pauses the animation.
    /// </summary>
    void Pause();

    /// <summary>
    /// Resumes a paused animation.
    /// </summary>
    void Resume();

    /// <summary>
    /// Updates the animation progress. Called each frame.
    /// </summary>
    void Tick();

    /// <summary>
    /// Updates the animation progress using the unified frame timestamp
    /// (<see cref="System.Diagnostics.Stopwatch"/> ticks) sampled once per frame
    /// by the animation manager, so every clock ticked in a frame shares the
    /// same time base. The default implementation ignores the timestamp and
    /// falls back to <see cref="Tick()"/>, keeping external implementations working.
    /// </summary>
    /// <param name="frameTimestamp">The frame timestamp in Stopwatch ticks.</param>
    void Tick(long frameTimestamp) => Tick();

    /// <summary>
    /// Gets whether the clock is paused. A paused clock stays registered with
    /// the animation manager but must not advance. The default implementation
    /// reports never-paused.
    /// </summary>
    bool IsPaused => false;

    /// <summary>
    /// Gets whether the clock has finished its active period. The default
    /// implementation derives completion from <see cref="IsRunning"/>.
    /// </summary>
    bool IsCompleted => !IsRunning;

    /// <summary>
    /// Moves the clock to a new position in time. The default implementation is
    /// a no-op; clocks that support seeking implement this explicitly.
    /// </summary>
    /// <param name="offset">The seek offset.</param>
    /// <param name="origin">Whether <paramref name="offset"/> is measured from
    /// the begin time or backward from the end of the duration.</param>
    void Seek(TimeSpan offset, AnimationSeekOrigin origin)
    {
    }
}

/// <summary>
/// Termination bookkeeping callback from an element-driven animation entry back
/// to the storyboard that owns its clock. A storyboard-driven clock stopped from
/// the element side (detach stop, container recycling, handoff replacement,
/// explicit stop) never fires Completed, so the element must settle it here —
/// otherwise the storyboard would wait forever in its static active set and pin
/// the recycled target subtree.
/// </summary>
internal interface IStoryboardClockOwner
{
    /// <summary>
    /// Records that <paramref name="clock"/> was terminated from the element
    /// side and will never complete naturally. Implementations must tolerate a
    /// clock that has already settled (naturally or terminated) — the call is
    /// idempotent per clock.
    /// </summary>
    void NotifyClockTerminated(IAnimationClock clock);

    /// <summary>
    /// Records that <paramref name="clock"/> was replaced by a sibling clock of
    /// the same storyboard targeting the same (target, property) — the common
    /// "two child animations drive one property in sequence" pattern. The clock
    /// settles like a natural completion and does NOT suppress the storyboard's
    /// Completed event, unlike <see cref="NotifyClockTerminated"/>. Idempotent
    /// per clock.
    /// </summary>
    void NotifyClockSuperseded(IAnimationClock clock);
}

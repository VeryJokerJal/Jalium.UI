using Jalium.UI.Media.Animation;

namespace Jalium.UI.Controls;

/// <summary>
/// Specifies the hand-off behavior when a new animation affects an existing one.
/// </summary>
public enum HandoffBehavior
{
    /// <summary>
    /// New animations replace existing animations on the properties to which they are applied.
    /// </summary>
    SnapshotAndReplace = 0,

    /// <summary>
    /// New animations are combined with existing animations.
    /// </summary>
    Compose = 1
}

/// <summary>
/// A trigger action that begins a Storyboard and distributes its animations to their targeted objects and properties.
/// </summary>
[ContentProperty("Storyboard")]
public sealed class BeginStoryboard : TriggerAction
{
    /// <summary>
    /// Gets or sets the Storyboard that this BeginStoryboard starts.
    /// </summary>
    public Storyboard? Storyboard { get; set; }

    /// <summary>
    /// Gets or sets the proper hand-off behavior to start an animation clock in this storyboard.
    /// </summary>
    public HandoffBehavior HandoffBehavior { get; set; } = HandoffBehavior.SnapshotAndReplace;

    /// <summary>
    /// Gets or sets the name of the BeginStoryboard object, used by ControllableStoryboardAction to reference this.
    /// </summary>
    public string? Name { get; set; }

    /// <inheritdoc />
    internal override void Invoke(FrameworkElement? element)
    {
        Storyboard?.Begin(element);
    }
}

/// <summary>
/// Abstract base class for trigger actions that control a Storyboard started by a corresponding BeginStoryboard.
/// </summary>
public abstract class ControllableStoryboardAction : TriggerAction
{
    /// <summary>
    /// Gets or sets the Name of the BeginStoryboard that began the Storyboard to control.
    /// </summary>
    public string? BeginStoryboardName { get; set; }

    /// <inheritdoc />
    internal override void Invoke(FrameworkElement? element)
    {
        // In a full implementation, this would resolve the BeginStoryboard by name
        // and get its Storyboard to perform the control action.
    }

    /// <summary>
    /// Performs the controllable action on the specified storyboard.
    /// </summary>
    internal abstract void Invoke(FrameworkElement? element, Storyboard storyboard);
}

/// <summary>
/// A trigger action that pauses a Storyboard.
/// </summary>
public sealed class PauseStoryboard : ControllableStoryboardAction
{
    /// <inheritdoc />
    internal override void Invoke(FrameworkElement? element, Storyboard storyboard)
    {
        storyboard.Pause();
    }
}

/// <summary>
/// A trigger action that resumes a paused Storyboard.
/// </summary>
public sealed class ResumeStoryboard : ControllableStoryboardAction
{
    /// <inheritdoc />
    internal override void Invoke(FrameworkElement? element, Storyboard storyboard)
    {
        storyboard.Resume();
    }
}

/// <summary>
/// A trigger action that stops a Storyboard.
/// </summary>
public sealed class StopStoryboard : ControllableStoryboardAction
{
    /// <inheritdoc />
    internal override void Invoke(FrameworkElement? element, Storyboard storyboard)
    {
        storyboard.Stop();
    }
}

/// <summary>
/// A trigger action that seeks a Storyboard to a specified position.
/// </summary>
public sealed class SeekStoryboard : ControllableStoryboardAction
{
    /// <summary>
    /// Gets or sets the amount of time to seek.
    /// </summary>
    public TimeSpan Offset { get; set; }

    /// <summary>
    /// Gets or sets the origin from which to seek.
    /// </summary>
    public TimeSeekOrigin Origin { get; set; } = TimeSeekOrigin.BeginTime;

    /// <inheritdoc />
    internal override void Invoke(FrameworkElement? element, Storyboard storyboard)
    {
        // Storyboard seek would go here when full seek support is implemented
    }
}

/// <summary>
/// Specifies the origin from which a seek operation is performed.
/// </summary>
public enum TimeSeekOrigin
{
    /// <summary>
    /// The offset is relative to the beginning of the storyboard.
    /// </summary>
    BeginTime = 0,

    /// <summary>
    /// The offset is relative to the duration of the storyboard.
    /// </summary>
    Duration = 1
}

/// <summary>
/// A trigger action that removes a Storyboard.
/// </summary>
public sealed class RemoveStoryboard : ControllableStoryboardAction
{
    /// <inheritdoc />
    internal override void Invoke(FrameworkElement? element, Storyboard storyboard)
    {
        storyboard.Stop();
    }
}

/// <summary>
/// A trigger action that changes the speed of a Storyboard.
/// </summary>
public sealed class SetStoryboardSpeedRatio : ControllableStoryboardAction
{
    /// <summary>
    /// Gets or sets the new SpeedRatio for the Storyboard.
    /// </summary>
    public double SpeedRatio { get; set; } = 1.0;

    /// <inheritdoc />
    internal override void Invoke(FrameworkElement? element, Storyboard storyboard)
    {
        storyboard.SpeedRatio = SpeedRatio;
    }
}

/// <summary>
/// A trigger action that advances a Storyboard to its fill period.
/// </summary>
public sealed class SkipStoryboardToFill : ControllableStoryboardAction
{
    /// <inheritdoc />
    internal override void Invoke(FrameworkElement? element, Storyboard storyboard)
    {
        storyboard.Stop();
    }
}

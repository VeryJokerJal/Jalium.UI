namespace Jalium.UI.Input;

/// <summary>Describes the lifecycle of a scrolling gesture or its native momentum.</summary>
[Flags]
public enum MouseWheelPhase
{
    None = 0,
    Began = 1,
    Stationary = 2,
    Changed = 4,
    Ended = 8,
    Cancelled = 16,
    MayBegin = 32
}

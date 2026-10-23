namespace Jalium.UI.Controls.Platform;

/// <summary>Optional native support for indirect manipulation gestures.</summary>
internal interface IPlatformGestureInput
{
    bool SupportsIndirectGestures { get; }
}

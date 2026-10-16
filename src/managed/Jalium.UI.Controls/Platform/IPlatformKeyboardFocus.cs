namespace Jalium.UI.Controls.Platform;

/// <summary>Acquires a window's input responder without foreground activation.</summary>
internal interface IPlatformKeyboardFocus
{
    bool TrySetKeyboardFocus();
}

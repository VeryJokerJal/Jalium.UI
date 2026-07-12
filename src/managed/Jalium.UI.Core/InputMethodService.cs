namespace Jalium.UI;

/// <summary>
/// Bridges input-method state into Core without introducing a Core-to-Input
/// assembly reference cycle.
/// </summary>
internal static class InputMethodService
{
    internal static Func<IInputElement, bool>? IsInputMethodEnabledResolver { get; set; }

    internal static bool GetIsInputMethodEnabled(IInputElement element)
    {
        return IsInputMethodEnabledResolver?.Invoke(element) ?? true;
    }
}

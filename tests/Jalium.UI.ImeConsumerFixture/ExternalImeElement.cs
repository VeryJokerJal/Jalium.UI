using Jalium.UI;

namespace Jalium.UI.ImeConsumerFixture;

/// <summary>
/// Compile-time consumer that proves an unrelated assembly can opt a custom
/// element into the public IME extension contract through Jalium.UI.Core.
/// </summary>
public sealed class ExternalImeElement : FrameworkElement, IImeSupport
{
    public bool TryGetImeSurroundingText(out ImeSurroundingTextSnapshot snapshot)
    {
        snapshot = new ImeSurroundingTextSnapshot("external", 8, 8);
        return true;
    }

    public Point GetImeCaretPosition() => default;

    public void OnImeCompositionStart()
    {
    }

    public void OnImeCompositionUpdate(string compositionString, int cursorPosition)
    {
    }

    public void OnImeCompositionEnd(string? resultString)
    {
    }
}

using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI;

public partial class Window
{
    private bool _queryingMacOSPopupKeyboard;

    internal bool CanMacOSPopupReceiveKeyboardInput()
    {
        if (_queryingMacOSPopupKeyboard || !IsMacOSPopupHost ||
            _macOSHostedPopupRoot is not { } root || _macOSHostedPopup?.Child is Controls.ToolTip) return false;
        _queryingMacOSPopupKeyboard = true;
        try
        {
            var current = CaptureMacOSAccessibilityLifetime(root);
            if (!current()) return false;
            var pending = new Stack<Visual>();
            var visited = new HashSet<Visual>();
            pending.Push(root);
            while (pending.TryPop(out var visual))
            {
                if (!current()) return false;
                if (!visited.Add(visual)) continue;
                if (visual is UIElement element)
                {
                    if (element.Visibility != Visibility.Visible || CssDisplayProperties.IsExitInert(element)) continue;
                    // AppKit delivers recognized trackpad gestures to the key
                    // window. A gesture surface can be non-focusable; allowing
                    // its host to become key does not focus that element.
                    bool ReceivesKeyInput() => element.Focusable ||
                        _platformWindow is Controls.Platform.IPlatformGestureInput { SupportsIndirectGestures: true } &&
                        _macOSHostedPopup?.IsHitTestVisible == true && element.IsHitTestVisible && element.IsManipulationEnabled;
                    if (ReceivesKeyInput() && element.IsEnabled && element.IsVisible)
                    {
                        var candidate = CaptureMacOSAccessibilityLifetime(element);
                        // IsEnabledCore may run user code. Recheck the captured
                        // target after those reads, including a detached child.
                        if (ReceivesKeyInput() && element.IsEnabled && current() && candidate() &&
                            IsMacOSKeyboardInputTargetEligible(element)) return true;
                    }
                }
                // Use visual eligibility, without invoking automation providers or
                // changing focus. CSS visibility can be overridden by descendants.
                int count = visual.InternalVisualChildrenCount;
                if (!current()) return false;
                for (int index = count - 1; index >= 0; index--)
                {
                    var child = visual.InternalGetVisualChild(index);
                    if (!current()) return false;
                    if (child != null) pending.Push(child);
                }
            }
            return false;
        }
        finally { _queryingMacOSPopupKeyboard = false; }
    }
}

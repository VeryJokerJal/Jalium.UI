using System.Diagnostics;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Input;

namespace Jalium.UI.Controls.Automation.MacOS;

internal sealed unsafe partial class MacOSAccessibilityTree
{
    private static long s_focusRequestSequence;
    private readonly record struct FocusRequest(long Sequence, long Revision, long IntentRevision,
        IFocusProvider? Provider, IInputElement? Focus)
    {
        internal bool HasCurrentIntent => Sequence == s_focusRequestSequence && Revision == Keyboard.FocusRevision &&
            IntentRevision == FocusService.FocusIntentRevision && ReferenceEquals(Provider, FocusService.Provider);
        internal bool IsCurrent
        {
            get
            {
                if (!HasCurrentIntent) return false;
                var focus = FocusService.FocusedElement;
                return HasCurrentIntent && ReferenceEquals(Focus, focus);
            }
        }
    }

    private static FocusRequest CaptureFocusRequest() =>
        new(Interlocked.Increment(ref s_focusRequestSequence), Keyboard.FocusRevision,
            FocusService.FocusIntentRevision, FocusService.Provider, FocusService.FocusedElement);

    private bool RequestFocus(AutomationPeer peer, Func<bool> lifetime, FocusRequest request, Func<bool, bool> activationLifetime)
    {
        bool IsCurrent() => request.IsCurrent && lifetime() && request.HasCurrentIntent;
        if (!IsCurrent() || !activationLifetime(false)) return false;
        // Renderer-neutral trees have no AppKit surface to select. Preserve
        // their synchronous provider contract without requesting activation.
        if (_window.Handle == 0)
        {
            peer.SetFocus();
            return lifetime() && peer.HasKeyboardFocus();
        }
        if (_window.Dispatcher.HasShutdownStarted) return false;
        // Activation handlers can open modal loops. Finish the native AX
        // callback before asking AppKit to select this physical window.
        _window.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                if (!IsCurrent() || !activationLifetime(false)) return;
                if (!_window.ActivateForMacOSAccessibilityFocus() || !IsCurrent() || !activationLifetime(true)) return;
                if (!peer.IsKeyboardFocusable() || !IsCurrent() || !activationLifetime(true)) return;
                peer.SetFocus();
            }
            catch (Exception error)
            {
                Debug.WriteLine($"[macOS accessibility Focus] {error}");
            }
        });
        return true;
    }
}

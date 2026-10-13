using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Controls;

namespace Jalium.UI;

public partial class Window
{
    // A Metal backdrop samples pixels painted before the panel. A retained
    // partial scene contains last frame's finished panel outside the damage
    // region, so sampling its blur/refraction halo would filter it twice.
    // Repaint visible backdrop scenes in full; idle frames are still skipped.
    internal static bool RequiresFullReplayForBackdrop(RenderBackend backend, Visual root)
    {
        if (backend != RenderBackend.Metal)
            return false;

        // A deep visible subtree still participates in retained rendering.
        // Walk it without a recursion cutoff that could miss its backdrop.
        var pending = new Stack<Visual>();
        pending.Push(root);
        while (pending.TryPop(out var visual))
        {
            if (visual is UIElement element)
            {
                if (element.Visibility != Visibility.Visible || element.Opacity <= 0)
                    continue;
                if (element.BackdropEffect is { HasEffect: true })
                    return true;
                if (element is Border { LiquidGlass: true })
                    return true;
            }
            for (var i = visual.InternalVisualChildrenCount - 1; i >= 0; i--)
                if (visual.InternalGetVisualChild(i) is { } child)
                    pending.Push(child);
        }
        return false;
    }
}

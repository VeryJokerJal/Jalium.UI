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
        if (!root.HasBackdropReplayCandidates)
            return false;

        // Preserve deep and mutable effects without scanning ordinary branches.
        // Candidate counts change on effect/ownership updates, not on scrolling.
        var pending = new Stack<Visual>();
        pending.Push(root);
        while (pending.TryPop(out var visual))
        {
            if (visual is UIElement element)
            {
                if (element.Visibility != Visibility.Visible || element.Opacity <= 0)
                    continue;
                if(Styling.CssCompositingProperties.Mix(element)!=CssBlendMode.Normal)return true;
                if (backend == RenderBackend.Metal && !visual.RendersBackdropEffectInTemplate &&
                    element.BackdropEffect is { HasEffect: true })
                    return true;
                if (backend == RenderBackend.Metal && element is Border { LiquidGlass: true })
                    return true;
            }
            for (var i = visual.InternalVisualChildrenCount - 1; i >= 0; i--)
                if (visual.InternalGetVisualChild(i) is { HasBackdropReplayCandidates: true } child)
                    pending.Push(child);
        }
        return false;
    }
}

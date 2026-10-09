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

        return HasBackdrop(root, 0);

        static bool HasBackdrop(Visual visual, int depth)
        {
            if (depth > 256)
                return false;
            if (visual is UIElement element)
            {
                if (element.Visibility != Visibility.Visible || element.Opacity <= 0)
                    return false;
                if (element.BackdropEffect is { HasEffect: true })
                    return true;
                if (element is Border { LiquidGlass: true })
                    return true;
            }
            for (var i = 0; i < visual.InternalVisualChildrenCount; i++)
                if (visual.InternalGetVisualChild(i) is { } child && HasBackdrop(child, depth + 1))
                    return true;
            return false;
        }
    }
}

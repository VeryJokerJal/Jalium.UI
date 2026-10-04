namespace Jalium.UI;

public partial class FrameworkElement
{
    // Font arrival changes text metrics without changing native property values.
    // Controls with retained line/measurement caches clear those through this hook.
    internal virtual void OnFontResourcesChanged()
    {
        InvalidateMeasure();
        InvalidateVisual();
    }
}

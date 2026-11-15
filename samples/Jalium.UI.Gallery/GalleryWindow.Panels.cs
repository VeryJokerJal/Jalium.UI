using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Documents;
using Jalium.UI.Media;

namespace Jalium.UI.Gallery;

internal static partial class GalleryWindow
{
    public static UIElement BuildPanelsSection() => new GallerySamplePanelsSection();
}

/// <summary>Layout Panels examples with replaceable default templates.</summary>
public class GallerySamplePanelsSection : GallerySampleHeader
{
    public GallerySamplePanelsSection() => DefaultStyleKey = typeof(GallerySamplePanelsSection);
}

/// <summary>A labeled color cell used to compare panel layouts.</summary>
public class GallerySamplePanelCell : ContentControl
{
    public GallerySamplePanelCell() { GallerySampleTheme.EnsureRegistered(); DefaultStyleKey = typeof(GallerySamplePanelCell); }
}

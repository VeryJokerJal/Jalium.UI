using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Documents;
using Jalium.UI.Media;

namespace Jalium.UI.Gallery;

internal static partial class GalleryWindow
{
    public static UIElement BuildFlyoutsSection() => new GallerySampleFlyoutsSection();
}

/// <summary>Menus &amp; Flyouts examples with replaceable default templates.</summary>
[TemplatePart(Name = "PART_Swipe", Type = typeof(SwipeControl))]
public class GallerySampleFlyoutsSection : GallerySampleHeader
{
    public GallerySampleFlyoutsSection() => DefaultStyleKey = typeof(GallerySampleFlyoutsSection);
}

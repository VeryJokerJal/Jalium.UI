using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Documents;
using Jalium.UI.Media;

namespace Jalium.UI.Gallery;

internal static partial class GalleryWindow
{
    public static UIElement BuildNavigationSection() => new GallerySampleNavigationSection();
}

/// <summary>Navigation &amp; Chrome examples with replaceable default templates.</summary>
[TemplatePart(Name = "PART_Navigation", Type = typeof(NavigationView))]
[TemplatePart(Name = "PART_Tabs", Type = typeof(TabControl))]
public class GallerySampleNavigationSection : GallerySampleHeader
{
    public GallerySampleNavigationSection() => DefaultStyleKey = typeof(GallerySampleNavigationSection);
}

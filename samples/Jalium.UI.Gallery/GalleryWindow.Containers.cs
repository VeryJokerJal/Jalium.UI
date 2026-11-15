using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Documents;
using Jalium.UI.Media;

namespace Jalium.UI.Gallery;

internal static partial class GalleryWindow
{
    public static UIElement BuildContainersSection() => new GallerySampleContainersSection();
}

/// <summary>Layout &amp; Containers examples with replaceable default templates.</summary>
[TemplatePart(Name = "PART_Expander", Type = typeof(Expander))]
[TemplatePart(Name = "PART_ExampleScroller", Type = typeof(ScrollViewer))]
public class GallerySampleContainersSection : GallerySampleHeader
{
    public GallerySampleContainersSection() => DefaultStyleKey = typeof(GallerySampleContainersSection);
}

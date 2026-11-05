using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;

namespace Jalium.UI.Gallery;

internal static partial class GalleryWindow
{
    public static UIElement BuildStatusSection() => new GallerySampleStatusSection();
}

/// <summary>Progress, notification and separator examples with replaceable chrome.</summary>
[TemplatePart(Name = "PART_Progress", Type = typeof(ProgressBar))]
public class GallerySampleStatusSection : GallerySampleHeader
{
    public GallerySampleStatusSection() => DefaultStyleKey = typeof(GallerySampleStatusSection);
}

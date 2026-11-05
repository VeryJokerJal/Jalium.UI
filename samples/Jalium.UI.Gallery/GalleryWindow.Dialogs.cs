using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Documents;
using Jalium.UI.Media;

namespace Jalium.UI.Gallery;

internal static partial class GalleryWindow
{
    public static UIElement BuildDialogsSection() => new GallerySampleDialogsSection();
}

/// <summary>Dialogs &amp; Notifications examples with replaceable default templates.</summary>
public class GallerySampleDialogsSection : GallerySampleHeader
{
    public GallerySampleDialogsSection() => DefaultStyleKey = typeof(GallerySampleDialogsSection);
}

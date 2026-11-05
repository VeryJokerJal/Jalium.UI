using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Gallery;

internal static partial class GalleryWindow
{
    public static UIElement BuildButtonsSection() => new GallerySampleButtonsSection();
}

/// <summary>Reusable command-control examples. The full default layout lives in the sample theme.</summary>
[TemplatePart(Name = "PART_StandardButton", Type = typeof(Button))]
[TemplatePart(Name = "PART_DefaultButton", Type = typeof(Button))]
[TemplatePart(Name = "PART_DisabledButton", Type = typeof(Button))]
[TemplatePart(Name = "PART_ToggleOff", Type = typeof(ToggleButton))]
[TemplatePart(Name = "PART_ToggleOn", Type = typeof(ToggleButton))]
[TemplatePart(Name = "PART_RepeatButton", Type = typeof(RepeatButton))]
[TemplatePart(Name = "PART_SplitButton", Type = typeof(SplitButton))]
[TemplatePart(Name = "PART_HyperlinkButton", Type = typeof(HyperlinkButton))]
[TemplatePart(Name = "PART_AppBarAdd", Type = typeof(AppBarButton))]
[TemplatePart(Name = "PART_AppBarEdit", Type = typeof(AppBarButton))]
[TemplatePart(Name = "PART_AppBarPin", Type = typeof(AppBarToggleButton))]
public class GallerySampleButtonsSection : GallerySampleHeader
{
    public GallerySampleButtonsSection() => DefaultStyleKey = typeof(GallerySampleButtonsSection);
}

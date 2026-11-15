using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;

namespace Jalium.UI.Gallery;

internal static partial class GalleryWindow
{
    public static UIElement BuildSelectionSection() => new GallerySampleSelectionSection();
}

/// <summary>Independent checkbox, radio, switch and range examples.</summary>
[TemplatePart(Name = "PART_RadioLow", Type = typeof(RadioButton))]
[TemplatePart(Name = "PART_RadioMedium", Type = typeof(RadioButton))]
[TemplatePart(Name = "PART_RadioHigh", Type = typeof(RadioButton))]
[TemplatePart(Name = "PART_Slider", Type = typeof(Slider))]
[TemplatePart(Name = "PART_RangeSlider", Type = typeof(RangeSlider))]
public class GallerySampleSelectionSection : GallerySampleHeader
{
    public static readonly DependencyProperty RadioGroupNameProperty = DependencyProperty.Register(nameof(RadioGroupName), typeof(string), typeof(GallerySampleSelectionSection), new PropertyMetadata(string.Empty));
    public string RadioGroupName { get => (string)GetValue(RadioGroupNameProperty)!; set => SetValue(RadioGroupNameProperty, value); }
    public GallerySampleSelectionSection()
    {
        DefaultStyleKey = typeof(GallerySampleSelectionSection);
        SetCurrentValue(RadioGroupNameProperty, "GalleryPriority-" + Guid.NewGuid().ToString("N"));
    }
}

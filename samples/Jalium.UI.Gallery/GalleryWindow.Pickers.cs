using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;

namespace Jalium.UI.Gallery;

internal static partial class GalleryWindow
{
    public static UIElement BuildPickersSection() => new GallerySamplePickersSection();
}

/// <summary>Deterministic date/time/color examples; values are externally bindable.</summary>
[TemplatePart(Name = "PART_Calendar", Type = typeof(Calendar))]
[TemplatePart(Name = "PART_DatePicker", Type = typeof(DatePicker))]
[TemplatePart(Name = "PART_TimePicker", Type = typeof(TimePicker))]
[TemplatePart(Name = "PART_ColorPicker", Type = typeof(ColorPicker))]
public class GallerySamplePickersSection : GallerySampleHeader
{
    public static readonly DependencyProperty DisplayDateProperty = DependencyProperty.Register(nameof(DisplayDate), typeof(DateTime), typeof(GallerySamplePickersSection), new PropertyMetadata(new DateTime(2026, 6, 15)));
    public static readonly DependencyProperty SampleDateProperty = DependencyProperty.Register(nameof(SampleDate), typeof(DateTime?), typeof(GallerySamplePickersSection), new PropertyMetadata((DateTime?)new DateTime(2026, 6, 15)));
    public static readonly DependencyProperty SampleTimeProperty = DependencyProperty.Register(nameof(SampleTime), typeof(TimeSpan?), typeof(GallerySamplePickersSection), new PropertyMetadata((TimeSpan?)new TimeSpan(14, 30, 0)));
    public static readonly DependencyProperty SampleColorProperty = DependencyProperty.Register(nameof(SampleColor), typeof(Color), typeof(GallerySamplePickersSection), new PropertyMetadata(Color.FromRgb(0x4F, 0x8A, 0xF7)));
    public DateTime? SampleDate { get => (DateTime?)GetValue(SampleDateProperty); set => SetValue(SampleDateProperty, value); }
    public DateTime DisplayDate { get => (DateTime)GetValue(DisplayDateProperty)!; set => SetValue(DisplayDateProperty, value); }
    public TimeSpan? SampleTime { get => (TimeSpan?)GetValue(SampleTimeProperty); set => SetValue(SampleTimeProperty, value); }
    public Color SampleColor { get => (Color)GetValue(SampleColorProperty)!; set => SetValue(SampleColorProperty, value); }
    public GallerySamplePickersSection() => DefaultStyleKey = typeof(GallerySamplePickersSection);
}

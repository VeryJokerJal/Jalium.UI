using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Gallery;

/// <summary>
/// Gallery section for date, time and color selection controls. Follows the
/// per-category section pattern established by <c>GalleryWindow.Buttons.cs</c>.
/// All demos are static (no popups are opened): the pickers are shown in their
/// closed inline form with a pre-selected value.
/// </summary>
internal static partial class GalleryWindow
{
    // Fixed sample instants so the captured screenshot is deterministic
    // (never DateTime.Today / DateTime.Now).
    private static readonly DateTime PickerSampleDate = new(2026, 6, 15);
    private static readonly TimeSpan PickerSampleTime = new(14, 30, 0);

    public static UIElement BuildPickersSection() => Section(
        "Date, Time & Color",
        "Calendar and date/time selection, plus the color picker.",
        Card("Calendar", new Calendar
        {
            SelectedDate = PickerSampleDate,
            DisplayDate = PickerSampleDate,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        }, width: 300),
        Card("DatePicker", new DatePicker
        {
            SelectedDate = PickerSampleDate,
            Width = 240,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        }, width: 300),
        Card("TimePicker", new TimePicker
        {
            SelectedTime = PickerSampleTime,
            Width = 200,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        }, width: 300),
        Card("ColorPicker", new ColorPicker
        {
            Color = Color.FromRgb(0x4F, 0x8A, 0xF7),
            Width = 220,
            Height = 300,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        }, width: 260));
}

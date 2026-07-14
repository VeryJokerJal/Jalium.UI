using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Gallery;

/// <summary>
/// Gallery section for boolean and ranged selection controls: checkboxes,
/// radio buttons, toggle switches and sliders. Follows the per-category
/// section pattern established by <c>GalleryWindow.Buttons.cs</c>.
/// </summary>
internal static partial class GalleryWindow
{
    public static UIElement BuildSelectionSection() => Section(
        "Selection & Toggles",
        "Boolean and ranged input: checkboxes, radio buttons, switches and sliders.",
        Card("CheckBox", CheckBoxVariants()),
        Card("RadioButton", RadioButtonGroup()),
        Card("ToggleSwitch", ToggleSwitchVariants()),
        Card("Slider", SliderDemo(), width: 300),
        Card("RangeSlider", RangeSliderDemo(), width: 300));

    private static UIElement CheckBoxVariants()
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10 };
        stack.Children.Add(new CheckBox
        {
            Content = "Checked",
            IsChecked = true,
            HorizontalAlignment = HorizontalAlignment.Left,
        });
        stack.Children.Add(new CheckBox
        {
            Content = "Unchecked",
            IsChecked = false,
            HorizontalAlignment = HorizontalAlignment.Left,
        });
        stack.Children.Add(new CheckBox
        {
            Content = "Indeterminate",
            IsThreeState = true,
            IsChecked = null,
            HorizontalAlignment = HorizontalAlignment.Left,
        });
        return stack;
    }

    private static UIElement RadioButtonGroup()
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10 };
        stack.Children.Add(new RadioButton
        {
            Content = "Low",
            GroupName = "GalleryPriority",
            HorizontalAlignment = HorizontalAlignment.Left,
        });
        stack.Children.Add(new RadioButton
        {
            Content = "Medium",
            GroupName = "GalleryPriority",
            IsChecked = true,
            HorizontalAlignment = HorizontalAlignment.Left,
        });
        stack.Children.Add(new RadioButton
        {
            Content = "High",
            GroupName = "GalleryPriority",
            HorizontalAlignment = HorizontalAlignment.Left,
        });
        return stack;
    }

    private static UIElement ToggleSwitchVariants()
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 14 };
        stack.Children.Add(new ToggleSwitch
        {
            Header = "Wi-Fi",
            IsOn = true,
            OnContent = "On",
            OffContent = "Off",
            HorizontalAlignment = HorizontalAlignment.Left,
        });
        stack.Children.Add(new ToggleSwitch
        {
            Header = "Bluetooth",
            IsOn = false,
            OnContent = "On",
            OffContent = "Off",
            HorizontalAlignment = HorizontalAlignment.Left,
        });
        return stack;
    }

    private static UIElement SliderDemo()
    {
        return new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Value = 65,
            Orientation = Orientation.Horizontal,
            TickFrequency = 10,
            IsSnapToTickEnabled = true,
            Width = 220,
            Height = 28,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private static UIElement RangeSliderDemo()
    {
        return new RangeSlider
        {
            Minimum = 0,
            Maximum = 100,
            RangeStart = 25,
            RangeEnd = 75,
            Orientation = Orientation.Horizontal,
            TickFrequency = 10,
            Width = 220,
            Height = 28,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }
}

using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;

namespace Jalium.UI.Gallery;

/// <summary>
/// Gallery section for click-to-invoke command controls. This file is the
/// "golden reference" for the per-category section pattern: every other
/// <c>GalleryWindow.&lt;Category&gt;.cs</c> file follows the same shape —
/// a single <c>public static UIElement Build&lt;Category&gt;Section()</c> that
/// returns <see cref="GalleryWindow.Section"/> wrapping one <see cref="GalleryWindow.Card"/>
/// per control.
/// </summary>
internal static partial class GalleryWindow
{
    public static UIElement BuildButtonsSection() => Section(
        "Buttons & Commands",
        "Click-to-invoke controls: standard, toggle, repeat, split, hyperlink and app-bar buttons.",
        Card("Button", ButtonVariants()),
        Card("ToggleButton", ToggleButtonVariants()),
        Card("RepeatButton", new RepeatButton
        {
            Content = "Hold to repeat",
            Padding = new Thickness(16, 8, 16, 8),
            HorizontalAlignment = HorizontalAlignment.Left,
        }),
        Card("SplitButton", new SplitButton
        {
            Content = "Split action",
            Padding = new Thickness(16, 8, 16, 8),
            HorizontalAlignment = HorizontalAlignment.Left,
        }),
        Card("HyperlinkButton", new HyperlinkButton
        {
            Content = "Visit jalium.dev",
            NavigateUri = new Uri("https://jalium.dev"),
            HorizontalAlignment = HorizontalAlignment.Left,
        }),
        Card("AppBarButton", AppBarButtons()));

    private static UIElement ButtonVariants()
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8 };
        stack.Children.Add(new Button
        {
            Content = "Standard",
            Padding = new Thickness(16, 8, 16, 8),
            HorizontalAlignment = HorizontalAlignment.Left,
        });
        stack.Children.Add(new Button
        {
            Content = "Default (accent)",
            IsDefault = true,
            Padding = new Thickness(16, 8, 16, 8),
            HorizontalAlignment = HorizontalAlignment.Left,
        });
        stack.Children.Add(new Button
        {
            Content = "Disabled",
            IsEnabled = false,
            Padding = new Thickness(16, 8, 16, 8),
            HorizontalAlignment = HorizontalAlignment.Left,
        });
        return stack;
    }

    private static UIElement ToggleButtonVariants()
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8 };
        stack.Children.Add(new ToggleButton
        {
            Content = "Toggle (off)",
            Padding = new Thickness(16, 8, 16, 8),
            HorizontalAlignment = HorizontalAlignment.Left,
        });
        stack.Children.Add(new ToggleButton
        {
            Content = "Toggle (on)",
            IsChecked = true,
            Padding = new Thickness(16, 8, 16, 8),
            HorizontalAlignment = HorizontalAlignment.Left,
        });
        return stack;
    }

    private static UIElement AppBarButtons()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, HorizontalAlignment = HorizontalAlignment.Left };

        var add = new AppBarButton { Label = "Add" };
        add.SetIconGlyph(""); // Segoe MDL2 "Add"

        var edit = new AppBarButton { Label = "Edit" };
        edit.SetIconGlyph(""); // "Edit"

        var pin = new AppBarToggleButton { Label = "Pin", IsChecked = true };

        row.Children.Add(add);
        row.Children.Add(edit);
        row.Children.Add(pin);
        return row;
    }
}

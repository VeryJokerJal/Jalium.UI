using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Gallery;

internal static partial class GalleryWindow
{
    public static UIElement BuildTextInputSection() => Section(
        "Text Input", "Single/multi-line, masked, numeric, auto-complete and combo entry.",
        Card("TextBox", BuildTextBoxes(), width: 300),
        Card("PasswordBox", new PasswordBox
        {
            Password = "S3cr3t!",
            Width = 200,
            HorizontalAlignment = HorizontalAlignment.Left
        }),
        Card("NumberBox", new NumberBox
        {
            Value = 42,
            Minimum = 0,
            Maximum = 100,
            SmallChange = 1,
            Width = 200,
            HorizontalAlignment = HorizontalAlignment.Left
        }),
        Card("AutoCompleteBox", new AutoCompleteBox
        {
            Text = "Cascadia",
            PlaceholderText = "Type a font...",
            Width = 200,
            HorizontalAlignment = HorizontalAlignment.Left
        }),
        Card("ComboBox", BuildComboBox()));

    private static UIElement BuildTextBoxes()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        panel.Children.Add(new TextBox
        {
            Text = "Single line entry",
            Width = 240,
            HorizontalAlignment = HorizontalAlignment.Left
        });

        panel.Children.Add(new TextBox
        {
            Text = "Multi-line text box.\nIt accepts Enter for new lines\nand wraps long paragraphs onto the next visual row.",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinLines = 3,
            MaxLines = 5,
            Width = 240,
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left
        });

        return panel;
    }

    private static UIElement BuildComboBox()
    {
        var combo = new ComboBox
        {
            Width = 200,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        combo.Items.Add("Apple");
        combo.Items.Add("Banana");
        combo.Items.Add("Cherry");
        combo.Items.Add("Dragon fruit");
        combo.SelectedIndex = 1;

        return combo;
    }
}

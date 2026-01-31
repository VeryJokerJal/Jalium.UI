using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Gallery.Theme;
using Jalium.UI.Media;

namespace Jalium.UI.Gallery.Views;

public partial class ComboBoxPage : Page
{
    public ComboBoxPage()
    {
        InitializeComponent();
        CreateContent();
    }

    private void CreateContent()
    {
        if (ContentPanel == null) return;

        // Title
        var title = new TextBlock
        {
            Text = "ComboBox",
            FontSize = GalleryTheme.FontSizeTitle,
            FontWeight = FontWeights.SemiBold,
            Foreground = GalleryTheme.TextPrimaryBrush,
            Margin = new Thickness(0, 0, 0, 8)
        };
        ContentPanel.Children.Add(title);

        // Description
        var description = new TextBlock
        {
            Text = "A dropdown control that presents a list of options for selection.",
            FontSize = GalleryTheme.FontSizeBody,
            Foreground = GalleryTheme.TextSecondaryBrush,
            Margin = new Thickness(0, 0, 0, 24),
            TextWrapping = TextWrapping.Wrap
        };
        ContentPanel.Children.Add(description);

        // Basic ComboBox
        AddSection("Basic ComboBox", "A simple ComboBox with string items.");

        var basicComboBox = new ComboBox
        {
            PlaceholderText = "Select a fruit",
            Width = 200,
            Margin = new Thickness(0, 0, 0, 16)
        };
        basicComboBox.Items.Add("Apple");
        basicComboBox.Items.Add("Banana");
        basicComboBox.Items.Add("Cherry");
        basicComboBox.Items.Add("Orange");
        basicComboBox.Items.Add("Grape");
        ContentPanel.Children.Add(basicComboBox);

        // ComboBox with pre-selected item
        AddSection("Pre-selected Item", "A ComboBox with an item already selected.");

        var selectedComboBox = new ComboBox
        {
            PlaceholderText = "Select a color",
            Width = 200,
            Margin = new Thickness(0, 0, 0, 16)
        };
        selectedComboBox.Items.Add("Red");
        selectedComboBox.Items.Add("Green");
        selectedComboBox.Items.Add("Blue");
        selectedComboBox.Items.Add("Yellow");
        selectedComboBox.SelectedIndex = 2; // Blue
        ContentPanel.Children.Add(selectedComboBox);

        // ComboBox with selection event
        AddSection("Selection Changed Event", "Displays the selected item in a TextBlock.");

        var eventStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 16)
        };

        var eventComboBox = new ComboBox
        {
            PlaceholderText = "Select a day",
            Width = 200,
            Margin = new Thickness(0, 0, 16, 0)
        };
        eventComboBox.Items.Add("Monday");
        eventComboBox.Items.Add("Tuesday");
        eventComboBox.Items.Add("Wednesday");
        eventComboBox.Items.Add("Thursday");
        eventComboBox.Items.Add("Friday");
        eventComboBox.Items.Add("Saturday");
        eventComboBox.Items.Add("Sunday");

        var resultText = new TextBlock
        {
            Text = "Selected: (none)",
            Foreground = GalleryTheme.TextSecondaryBrush,
            VerticalAlignment = VerticalAlignment.Center
        };

        eventComboBox.SelectionChanged += (s, e) =>
        {
            var selected = eventComboBox.SelectedItem?.ToString() ?? "(none)";
            resultText.Text = $"Selected: {selected}";
        };

        eventStack.Children.Add(eventComboBox);
        eventStack.Children.Add(resultText);
        ContentPanel.Children.Add(eventStack);
    }

    private void AddSection(string titleText, string descriptionText)
    {
        if (ContentPanel == null) return;

        var sectionTitle = new TextBlock
        {
            Text = titleText,
            FontSize = GalleryTheme.FontSizeSubtitle,
            FontWeight = FontWeights.SemiBold,
            Foreground = GalleryTheme.TextPrimaryBrush,
            Margin = new Thickness(0, 16, 0, 4)
        };
        ContentPanel.Children.Add(sectionTitle);

        var sectionDesc = new TextBlock
        {
            Text = descriptionText,
            FontSize = GalleryTheme.FontSizeBody,
            Foreground = GalleryTheme.TextTertiaryBrush,
            Margin = new Thickness(0, 0, 0, 12)
        };
        ContentPanel.Children.Add(sectionDesc);
    }
}

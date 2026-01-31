using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Gallery.Theme;
using Jalium.UI.Media;

namespace Jalium.UI.Gallery.Views;

public partial class WrapPanelPage : Page
{
    public WrapPanelPage()
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
            Text = "WrapPanel",
            FontSize = GalleryTheme.FontSizeTitle,
            FontWeight = FontWeights.SemiBold,
            Foreground = GalleryTheme.TextPrimaryBrush,
            Margin = new Thickness(0, 0, 0, 8)
        };
        ContentPanel.Children.Add(title);

        // Description
        var description = new TextBlock
        {
            Text = "A panel that positions child elements sequentially, wrapping to the next line when reaching the edge.",
            FontSize = GalleryTheme.FontSizeBody,
            Foreground = GalleryTheme.TextSecondaryBrush,
            Margin = new Thickness(0, 0, 0, 24),
            TextWrapping = TextWrapping.Wrap
        };
        ContentPanel.Children.Add(description);

        // Horizontal WrapPanel
        AddSection("Horizontal WrapPanel", "Items flow from left to right, wrapping to the next row.");

        var horizontalContainer = new Border
        {
            Width = 400,
            Background = GalleryTheme.BackgroundDarkBrush,
            BorderBrush = GalleryTheme.BorderDefaultBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 24)
        };

        var horizontalWrapPanel = new WrapPanel
        {
            Orientation = Orientation.Horizontal
        };

        var colors = new[]
        {
            Color.FromRgb(0, 120, 212),
            Color.FromRgb(76, 175, 80),
            Color.FromRgb(255, 152, 0),
            Color.FromRgb(156, 39, 176),
            Color.FromRgb(33, 150, 243),
            Color.FromRgb(244, 67, 54),
            Color.FromRgb(0, 150, 136),
            Color.FromRgb(255, 87, 34)
        };

        for (int i = 0; i < 12; i++)
        {
            var color = colors[i % colors.Length];
            var item = CreateWrapItem($"Item {i + 1}", color, 80 + (i % 3) * 20, 40);
            horizontalWrapPanel.Children.Add(item);
        }

        horizontalContainer.Child = horizontalWrapPanel;
        ContentPanel.Children.Add(horizontalContainer);

        // Vertical WrapPanel
        AddSection("Vertical WrapPanel", "Items flow from top to bottom, wrapping to the next column.");

        var verticalContainer = new Border
        {
            Width = 400,
            Height = 150,
            Background = GalleryTheme.BackgroundDarkBrush,
            BorderBrush = GalleryTheme.BorderDefaultBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 24)
        };

        var verticalWrapPanel = new WrapPanel
        {
            Orientation = Orientation.Vertical
        };

        for (int i = 0; i < 10; i++)
        {
            var color = colors[i % colors.Length];
            var item = CreateWrapItem($"V{i + 1}", color, 60, 30 + (i % 3) * 10);
            verticalWrapPanel.Children.Add(item);
        }

        verticalContainer.Child = verticalWrapPanel;
        ContentPanel.Children.Add(verticalContainer);

        // Fixed Item Size
        AddSection("Fixed Item Size", "Using ItemWidth and ItemHeight for uniform sizing.");

        var fixedContainer = new Border
        {
            Width = 400,
            Background = GalleryTheme.BackgroundDarkBrush,
            BorderBrush = GalleryTheme.BorderDefaultBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 24)
        };

        var fixedWrapPanel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            ItemWidth = 80,
            ItemHeight = 80
        };

        for (int i = 0; i < 8; i++)
        {
            var color = colors[i % colors.Length];
            var item = CreateWrapItem($"{i + 1}", color, 60, 60);
            fixedWrapPanel.Children.Add(item);
        }

        fixedContainer.Child = fixedWrapPanel;
        ContentPanel.Children.Add(fixedContainer);

        // Tag Cloud Example
        AddSection("Tag Cloud Example", "A practical use case for WrapPanel.");

        var tagContainer = new Border
        {
            Width = 400,
            Background = GalleryTheme.BackgroundLightBrush,
            BorderBrush = GalleryTheme.BorderDefaultBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(8)
        };

        var tagWrapPanel = new WrapPanel
        {
            Orientation = Orientation.Horizontal
        };

        var tags = new[] { "C#", "WPF", "XAML", "UI Framework", ".NET", "Windows", "Desktop", "Controls", "Layout", "Styling" };
        foreach (var tag in tags)
        {
            var tagBorder = new Border
            {
                Background = GalleryTheme.AccentPrimaryBrush,
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(4)
            };

            var tagText = new TextBlock
            {
                Text = tag,
                Foreground = new SolidColorBrush(Color.White),
                FontSize = 12
            };

            tagBorder.Child = tagText;
            tagWrapPanel.Children.Add(tagBorder);
        }

        tagContainer.Child = tagWrapPanel;
        ContentPanel.Children.Add(tagContainer);
    }

    private Border CreateWrapItem(string text, Color color, double width, double height)
    {
        var border = new Border
        {
            Width = width,
            Height = height,
            Background = new SolidColorBrush(color),
            Margin = new Thickness(4),
            CornerRadius = new CornerRadius(4)
        };

        var textBlock = new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.White),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        border.Child = textBlock;
        return border;
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

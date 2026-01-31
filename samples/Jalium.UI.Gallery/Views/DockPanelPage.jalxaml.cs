using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Gallery.Theme;
using Jalium.UI.Media;

namespace Jalium.UI.Gallery.Views;

public partial class DockPanelPage : Page
{
    public DockPanelPage()
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
            Text = "DockPanel",
            FontSize = GalleryTheme.FontSizeTitle,
            FontWeight = FontWeights.SemiBold,
            Foreground = GalleryTheme.TextPrimaryBrush,
            Margin = new Thickness(0, 0, 0, 8)
        };
        ContentPanel.Children.Add(title);

        // Description
        var description = new TextBlock
        {
            Text = "A panel that positions child elements to the edges (top, bottom, left, right) or fills the remaining space.",
            FontSize = GalleryTheme.FontSizeBody,
            Foreground = GalleryTheme.TextSecondaryBrush,
            Margin = new Thickness(0, 0, 0, 24),
            TextWrapping = TextWrapping.Wrap
        };
        ContentPanel.Children.Add(description);

        // Basic DockPanel
        AddSection("Basic DockPanel", "Elements docked to different edges with the last child filling the center.");

        var basicContainer = new Border
        {
            Width = 400,
            Height = 250,
            Background = GalleryTheme.BackgroundDarkBrush,
            Margin = new Thickness(0, 0, 0, 24)
        };

        var basicDockPanel = new DockPanel();

        // Top
        var topBorder = CreateColoredBorder("Top", Color.FromRgb(0, 120, 212), 50);
        DockPanel.SetDock(topBorder, Dock.Top);
        basicDockPanel.Children.Add(topBorder);

        // Bottom
        var bottomBorder = CreateColoredBorder("Bottom", Color.FromRgb(76, 175, 80), 50);
        DockPanel.SetDock(bottomBorder, Dock.Bottom);
        basicDockPanel.Children.Add(bottomBorder);

        // Left
        var leftBorder = CreateColoredBorder("Left", Color.FromRgb(255, 152, 0), 80);
        DockPanel.SetDock(leftBorder, Dock.Left);
        basicDockPanel.Children.Add(leftBorder);

        // Right
        var rightBorder = CreateColoredBorder("Right", Color.FromRgb(156, 39, 176), 80);
        DockPanel.SetDock(rightBorder, Dock.Right);
        basicDockPanel.Children.Add(rightBorder);

        // Fill (Center)
        var centerBorder = CreateColoredBorder("Fill", Color.FromRgb(33, 150, 243));
        basicDockPanel.Children.Add(centerBorder);

        basicContainer.Child = basicDockPanel;
        ContentPanel.Children.Add(basicContainer);

        // LastChildFill = false
        AddSection("LastChildFill = false", "When disabled, the last child does not fill the remaining space.");

        var noFillContainer = new Border
        {
            Width = 400,
            Height = 150,
            Background = GalleryTheme.BackgroundDarkBrush,
            Margin = new Thickness(0, 0, 0, 24)
        };

        var noFillDockPanel = new DockPanel
        {
            LastChildFill = false
        };

        var noFillTop = CreateColoredBorder("Top", Color.FromRgb(0, 120, 212), 40);
        DockPanel.SetDock(noFillTop, Dock.Top);
        noFillDockPanel.Children.Add(noFillTop);

        var noFillLeft = CreateColoredBorder("Left", Color.FromRgb(255, 152, 0), 100);
        DockPanel.SetDock(noFillLeft, Dock.Left);
        noFillDockPanel.Children.Add(noFillLeft);

        var noFillRight = CreateColoredBorder("Right", Color.FromRgb(156, 39, 176), 100);
        DockPanel.SetDock(noFillRight, Dock.Right);
        noFillDockPanel.Children.Add(noFillRight);

        noFillContainer.Child = noFillDockPanel;
        ContentPanel.Children.Add(noFillContainer);

        // App Layout Example
        AddSection("Application Layout", "A typical application layout with header, sidebar, and content area.");

        var appContainer = new Border
        {
            Width = 400,
            Height = 250,
            Background = GalleryTheme.BackgroundDarkBrush
        };

        var appDockPanel = new DockPanel();

        // Header
        var header = new Border
        {
            Height = 40,
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
            Child = new TextBlock
            {
                Text = "Application Header",
                Foreground = new SolidColorBrush(Color.White),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            }
        };
        DockPanel.SetDock(header, Dock.Top);
        appDockPanel.Children.Add(header);

        // Sidebar
        var sidebar = new Border
        {
            Width = 120,
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
            Child = new TextBlock
            {
                Text = "Sidebar",
                Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                Margin = new Thickness(12, 12, 0, 0)
            }
        };
        DockPanel.SetDock(sidebar, Dock.Left);
        appDockPanel.Children.Add(sidebar);

        // Content
        var content = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
            Child = new TextBlock
            {
                Text = "Main Content Area",
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        appDockPanel.Children.Add(content);

        appContainer.Child = appDockPanel;
        ContentPanel.Children.Add(appContainer);
    }

    private Border CreateColoredBorder(string text, Color color, double? size = null)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(color),
            Child = new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.White),
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        if (size.HasValue)
        {
            border.MinHeight = size.Value;
            border.MinWidth = size.Value;
        }

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

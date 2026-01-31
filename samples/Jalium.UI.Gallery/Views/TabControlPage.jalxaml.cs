using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Gallery.Theme;
using Jalium.UI.Media;

namespace Jalium.UI.Gallery.Views;

public partial class TabControlPage : Page
{
    public TabControlPage()
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
            Text = "TabControl",
            FontSize = GalleryTheme.FontSizeTitle,
            FontWeight = FontWeights.SemiBold,
            Foreground = GalleryTheme.TextPrimaryBrush,
            Margin = new Thickness(0, 0, 0, 8)
        };
        ContentPanel.Children.Add(title);

        // Description
        var description = new TextBlock
        {
            Text = "A control that contains multiple items sharing the same space with a tab strip for navigation.",
            FontSize = GalleryTheme.FontSizeBody,
            Foreground = GalleryTheme.TextSecondaryBrush,
            Margin = new Thickness(0, 0, 0, 24),
            TextWrapping = TextWrapping.Wrap
        };
        ContentPanel.Children.Add(description);

        // Basic TabControl
        AddSection("Basic TabControl", "A simple TabControl with multiple tabs.");

        var basicTabControl = new TabControl
        {
            Width = 400,
            Height = 200,
            Margin = new Thickness(0, 0, 0, 24)
        };

        var tab1 = new TabItem { Header = "Home" };
        tab1.Content = CreateTabContent("Welcome to the Home tab!", GalleryTheme.AccentPrimaryBrush);
        basicTabControl.Items.Add(tab1);

        var tab2 = new TabItem { Header = "Profile" };
        tab2.Content = CreateTabContent("Your profile information goes here.", new SolidColorBrush(Color.FromRgb(76, 175, 80)));
        basicTabControl.Items.Add(tab2);

        var tab3 = new TabItem { Header = "Settings" };
        tab3.Content = CreateTabContent("Application settings and preferences.", new SolidColorBrush(Color.FromRgb(255, 152, 0)));
        basicTabControl.Items.Add(tab3);

        ContentPanel.Children.Add(basicTabControl);

        // TabControl with Bottom placement
        AddSection("Bottom Tab Placement", "Tabs positioned at the bottom of the control.");

        var bottomTabControl = new TabControl
        {
            Width = 400,
            Height = 200,
            TabStripPlacement = Dock.Bottom,
            Margin = new Thickness(0, 0, 0, 24)
        };

        var bottomTab1 = new TabItem { Header = "Tab 1" };
        bottomTab1.Content = CreateTabContent("Content for Tab 1", GalleryTheme.AccentPrimaryBrush);
        bottomTabControl.Items.Add(bottomTab1);

        var bottomTab2 = new TabItem { Header = "Tab 2" };
        bottomTab2.Content = CreateTabContent("Content for Tab 2", new SolidColorBrush(Color.FromRgb(156, 39, 176)));
        bottomTabControl.Items.Add(bottomTab2);

        ContentPanel.Children.Add(bottomTabControl);

        // Selection changed event
        AddSection("Selection Changed Event", "Responds to tab selection changes.");

        var eventStack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(0, 0, 0, 16)
        };

        var resultText = new TextBlock
        {
            Text = "Selected tab: Home",
            Foreground = GalleryTheme.TextSecondaryBrush,
            Margin = new Thickness(0, 0, 0, 8)
        };
        eventStack.Children.Add(resultText);

        var eventTabControl = new TabControl
        {
            Width = 400,
            Height = 150
        };

        var eventTab1 = new TabItem { Header = "Home" };
        eventTab1.Content = CreateTabContent("Home content", GalleryTheme.AccentPrimaryBrush);
        eventTabControl.Items.Add(eventTab1);

        var eventTab2 = new TabItem { Header = "Documents" };
        eventTab2.Content = CreateTabContent("Documents content", new SolidColorBrush(Color.FromRgb(33, 150, 243)));
        eventTabControl.Items.Add(eventTab2);

        var eventTab3 = new TabItem { Header = "Downloads" };
        eventTab3.Content = CreateTabContent("Downloads content", new SolidColorBrush(Color.FromRgb(0, 150, 136)));
        eventTabControl.Items.Add(eventTab3);

        eventTabControl.SelectionChanged += (s, e) =>
        {
            if (eventTabControl.SelectedItem is TabItem selectedTab)
            {
                resultText.Text = $"Selected tab: {selectedTab.Header}";
            }
        };

        eventStack.Children.Add(eventTabControl);
        ContentPanel.Children.Add(eventStack);
    }

    private Border CreateTabContent(string text, Brush accentColor)
    {
        var border = new Border
        {
            Background = GalleryTheme.BackgroundLightBrush,
            Padding = new Thickness(16)
        };

        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var indicator = new Border
        {
            Width = 40,
            Height = 4,
            Background = accentColor,
            Margin = new Thickness(0, 0, 0, 12),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        stack.Children.Add(indicator);

        var contentText = new TextBlock
        {
            Text = text,
            Foreground = GalleryTheme.TextPrimaryBrush,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        stack.Children.Add(contentText);

        border.Child = stack;
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

using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Gallery.Theme;
using Jalium.UI.Media;

namespace Jalium.UI.Gallery.Views;

public partial class TreeViewPage : Page
{
    public TreeViewPage()
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
            Text = "TreeView",
            FontSize = GalleryTheme.FontSizeTitle,
            FontWeight = FontWeights.SemiBold,
            Foreground = GalleryTheme.TextPrimaryBrush,
            Margin = new Thickness(0, 0, 0, 8)
        };
        ContentPanel.Children.Add(title);

        // Description
        var description = new TextBlock
        {
            Text = "A hierarchical control that displays data in a tree structure with expandable and collapsible nodes.",
            FontSize = GalleryTheme.FontSizeBody,
            Foreground = GalleryTheme.TextSecondaryBrush,
            Margin = new Thickness(0, 0, 0, 24),
            TextWrapping = TextWrapping.Wrap
        };
        ContentPanel.Children.Add(description);

        // Basic TreeView
        AddSection("Basic TreeView", "A simple TreeView with hierarchical items.");

        var basicTreeView = CreateFileSystemTreeView();
        basicTreeView.Width = 300;
        basicTreeView.Height = 250;
        basicTreeView.Margin = new Thickness(0, 0, 0, 24);
        ContentPanel.Children.Add(basicTreeView);

        // TreeView with selection
        AddSection("Selection Event", "Displays the selected item when a node is clicked.");

        var eventStack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(0, 0, 0, 16)
        };

        var resultText = new TextBlock
        {
            Text = "Selected: (none)",
            Foreground = GalleryTheme.TextSecondaryBrush,
            Margin = new Thickness(0, 0, 0, 8)
        };
        eventStack.Children.Add(resultText);

        var eventTreeView = CreateCategoryTreeView();
        eventTreeView.Width = 300;
        eventTreeView.Height = 200;

        eventTreeView.SelectedItemChanged += (s, e) =>
        {
            if (e.NewValue is TreeViewItem item)
            {
                resultText.Text = $"Selected: {item.Header}";
            }
        };

        eventStack.Children.Add(eventTreeView);
        ContentPanel.Children.Add(eventStack);

        // Pre-expanded TreeView
        AddSection("Pre-expanded Nodes", "A TreeView with some nodes already expanded.");

        var expandedTreeView = CreateExpandedTreeView();
        expandedTreeView.Width = 300;
        expandedTreeView.Height = 200;
        ContentPanel.Children.Add(expandedTreeView);
    }

    private TreeView CreateFileSystemTreeView()
    {
        var treeView = new TreeView();

        var documents = new TreeViewItem { Header = "Documents" };

        var workFolder = new TreeViewItem { Header = "Work" };
        workFolder.Items.Add(new TreeViewItem { Header = "Report.docx" });
        workFolder.Items.Add(new TreeViewItem { Header = "Presentation.pptx" });
        workFolder.Items.Add(new TreeViewItem { Header = "Budget.xlsx" });
        documents.Items.Add(workFolder);

        var personalFolder = new TreeViewItem { Header = "Personal" };
        personalFolder.Items.Add(new TreeViewItem { Header = "Notes.txt" });
        personalFolder.Items.Add(new TreeViewItem { Header = "Photos" });
        documents.Items.Add(personalFolder);

        treeView.Items.Add(documents);

        var downloads = new TreeViewItem { Header = "Downloads" };
        downloads.Items.Add(new TreeViewItem { Header = "installer.exe" });
        downloads.Items.Add(new TreeViewItem { Header = "archive.zip" });
        treeView.Items.Add(downloads);

        var desktop = new TreeViewItem { Header = "Desktop" };
        desktop.Items.Add(new TreeViewItem { Header = "Shortcut.lnk" });
        treeView.Items.Add(desktop);

        return treeView;
    }

    private TreeView CreateCategoryTreeView()
    {
        var treeView = new TreeView();

        var animals = new TreeViewItem { Header = "Animals" };

        var mammals = new TreeViewItem { Header = "Mammals" };
        mammals.Items.Add(new TreeViewItem { Header = "Dog" });
        mammals.Items.Add(new TreeViewItem { Header = "Cat" });
        mammals.Items.Add(new TreeViewItem { Header = "Elephant" });
        animals.Items.Add(mammals);

        var birds = new TreeViewItem { Header = "Birds" };
        birds.Items.Add(new TreeViewItem { Header = "Eagle" });
        birds.Items.Add(new TreeViewItem { Header = "Sparrow" });
        animals.Items.Add(birds);

        treeView.Items.Add(animals);

        var plants = new TreeViewItem { Header = "Plants" };
        plants.Items.Add(new TreeViewItem { Header = "Trees" });
        plants.Items.Add(new TreeViewItem { Header = "Flowers" });
        plants.Items.Add(new TreeViewItem { Header = "Grass" });
        treeView.Items.Add(plants);

        return treeView;
    }

    private TreeView CreateExpandedTreeView()
    {
        var treeView = new TreeView();

        var root = new TreeViewItem { Header = "Project", IsExpanded = true };

        var src = new TreeViewItem { Header = "src", IsExpanded = true };
        src.Items.Add(new TreeViewItem { Header = "App.cs" });
        src.Items.Add(new TreeViewItem { Header = "MainWindow.cs" });

        var components = new TreeViewItem { Header = "Components" };
        components.Items.Add(new TreeViewItem { Header = "Button.cs" });
        components.Items.Add(new TreeViewItem { Header = "TextBox.cs" });
        src.Items.Add(components);

        root.Items.Add(src);

        var tests = new TreeViewItem { Header = "tests" };
        tests.Items.Add(new TreeViewItem { Header = "UnitTests.cs" });
        root.Items.Add(tests);

        root.Items.Add(new TreeViewItem { Header = "README.md" });

        treeView.Items.Add(root);

        return treeView;
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

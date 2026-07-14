using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Gallery;

/// <summary>
/// Gallery section for collection / data-presentation controls: list boxes,
/// list views, data grids and trees over tiny in-memory sample data. Follows
/// the per-category section pattern established by <c>GalleryWindow.Buttons.cs</c>.
/// </summary>
internal static partial class GalleryWindow
{
    public static UIElement BuildDataControlsSection() => Section(
        "Collections & Data",
        "Lists, grids and trees over tiny in-memory sample data.",
        Card("ListBox", ListBoxDemo()),
        Card("ListView", ListViewDemo()),
        Card("DataGrid", DataGridDemo(), width: 480),
        Card("TreeView", TreeViewDemo()),
        Card("TreeDataGrid", Placeholder("TreeDataGrid", "Hierarchical, expandable rows combined with grid columns.")));

    private static UIElement ListBoxDemo()
    {
        var listBox = new ListBox
        {
            SelectedIndex = 1,
            Height = 150,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        listBox.Items.Add("Inbox");
        listBox.Items.Add("Drafts");
        listBox.Items.Add("Sent");
        listBox.Items.Add("Archive");
        listBox.Items.Add("Trash");
        return listBox;
    }

    private static UIElement ListViewDemo()
    {
        var listView = new ListView
        {
            SelectedIndex = 0,
            Height = 150,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        listView.Items.Add("Design review");
        listView.Items.Add("Sprint planning");
        listView.Items.Add("Release notes");
        listView.Items.Add("Retrospective");
        return listView;
    }

    private static UIElement DataGridDemo()
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = true,
            ItemsSource = new[]
            {
                new EmployeeRecord { Name = "Ava Chen",     Role = "Engineer", Years = 4 },
                new EmployeeRecord { Name = "Liam Patel",   Role = "Designer", Years = 6 },
                new EmployeeRecord { Name = "Noah Kim",     Role = "Manager",  Years = 9 },
                new EmployeeRecord { Name = "Mia Rossi",    Role = "Engineer", Years = 2 },
            },
            SelectedIndex = 0,
            Width = 452,
            Height = 190,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        return grid;
    }

    private static UIElement TreeViewDemo()
    {
        var tree = new TreeView
        {
            Height = 170,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var solution = new TreeViewItem { Header = "Solution 'Jalium.UI'", IsExpanded = true };
        solution.Items.Add(new TreeViewItem { Header = "Jalium.UI.Core" });
        solution.Items.Add(new TreeViewItem { Header = "Jalium.UI.Controls" });
        solution.Items.Add(new TreeViewItem { Header = "Jalium.UI.Media" });

        var samples = new TreeViewItem { Header = "Samples", IsExpanded = true };
        samples.Items.Add(new TreeViewItem { Header = "Jalium.UI.Gallery" });
        samples.Items.Add(new TreeViewItem { Header = "Jalium.UI.Demo" });

        tree.Items.Add(solution);
        tree.Items.Add(samples);
        return tree;
    }

    /// <summary>
    /// Tiny sample row type for the <see cref="DataGrid"/> demo. Its public
    /// properties drive the auto-generated columns (Name, Role, Years).
    /// </summary>
    private sealed class EmployeeRecord
    {
        public string Name { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public int Years { get; set; }
    }
}

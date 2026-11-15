using System.Collections;
using Jalium.UI.Controls;
using Jalium.UI.Ink;
using Jalium.UI.Media;

namespace Jalium.UI.Gallery;

internal static partial class GalleryWindow
{
    public static UIElement BuildDataControlsSection() => SectionFromCards("Collections & Data", "Lists, grids and trees over tiny in-memory sample data.", CreateDataControlCards());
    private static DeferredCard[] CreateDataControlCards() =>
    [
        new("Gallery.Card.DataControls.ListBox", "ListBox", static () => new GallerySampleListBoxDemo()),
        new("Gallery.Card.DataControls.ListView", "ListView", static () => new GallerySampleListViewDemo()),
        new("Gallery.Card.DataControls.DataGrid", "DataGrid", static () => new GallerySampleDataGridDemo(), Width: 480),
        new("Gallery.Card.DataControls.TreeView", "TreeView", static () => new GallerySampleTreeDemo()),
        new("Gallery.Card.DataControls.TreeDataGrid", "TreeDataGrid", static () => Placeholder("TreeDataGrid", "Hierarchical, expandable rows combined with grid columns.")),
    ];
}

[TemplatePart(Name = "PART_List", Type = typeof(ListBox))]
public class GallerySampleListBoxDemo : GallerySampleDemo
{
    public static readonly DependencyProperty EntriesProperty = DependencyProperty.Register(nameof(Entries), typeof(IEnumerable), typeof(GallerySampleListBoxDemo), new PropertyMetadata(null));
    public IEnumerable Entries { get => (IEnumerable)GetValue(EntriesProperty)!; set => SetValue(EntriesProperty, value); }
    public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.Register(nameof(SelectedIndex), typeof(int), typeof(GallerySampleListBoxDemo), new PropertyMetadata(1));
    public int SelectedIndex { get => (int)GetValue(SelectedIndexProperty)!; set => SetValue(SelectedIndexProperty, value); }
    public GallerySampleListBoxDemo() { SetCurrentValue(EntriesProperty, new[] { "Inbox", "Drafts", "Sent", "Archive", "Trash" }); DefaultStyleKey = typeof(GallerySampleListBoxDemo); }
}
[TemplatePart(Name = "PART_List", Type = typeof(ListView))]
public class GallerySampleListViewDemo : GallerySampleDemo
{
    public static readonly DependencyProperty EntriesProperty = DependencyProperty.Register(nameof(Entries), typeof(IEnumerable), typeof(GallerySampleListViewDemo), new PropertyMetadata(null));
    public IEnumerable Entries { get => (IEnumerable)GetValue(EntriesProperty)!; set => SetValue(EntriesProperty, value); }
    public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.Register(nameof(SelectedIndex), typeof(int), typeof(GallerySampleListViewDemo), new PropertyMetadata(0));
    public int SelectedIndex { get => (int)GetValue(SelectedIndexProperty)!; set => SetValue(SelectedIndexProperty, value); }
    public GallerySampleListViewDemo() { SetCurrentValue(EntriesProperty, new[] { "Design review", "Sprint planning", "Release notes", "Retrospective" }); DefaultStyleKey = typeof(GallerySampleListViewDemo); }
}
[TemplatePart(Name = "PART_Grid", Type = typeof(DataGrid))]
public class GallerySampleDataGridDemo : GallerySampleDemo
{
    public static readonly DependencyProperty RowsProperty = DependencyProperty.Register(nameof(Rows), typeof(IEnumerable), typeof(GallerySampleDataGridDemo), new PropertyMetadata(null));
    public IEnumerable Rows { get => (IEnumerable)GetValue(RowsProperty)!; set => SetValue(RowsProperty, value); }
    public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.Register(nameof(SelectedIndex), typeof(int), typeof(GallerySampleDataGridDemo), new PropertyMetadata(0));
    public int SelectedIndex { get => (int)GetValue(SelectedIndexProperty)!; set => SetValue(SelectedIndexProperty, value); }
    public GallerySampleDataGridDemo() { SetCurrentValue(RowsProperty, new GallerySampleEmployee[]
        {
            new() { Name = "Ava Chen", Role = "Engineer", Years = 4 }, new() { Name = "Liam Patel", Role = "Designer", Years = 6 },
            new() { Name = "Noah Kim", Role = "Manager", Years = 9 }, new() { Name = "Mia Rossi", Role = "Engineer", Years = 2 },
        }); DefaultStyleKey = typeof(GallerySampleDataGridDemo); }
}

public sealed class GallerySampleEmployee
{
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public int Years { get; set; }
}
[TemplatePart(Name = "PART_Tree", Type = typeof(TreeView))]
public class GallerySampleTreeDemo : GallerySampleDemo
{
    public GallerySampleTreeDemo() { DefaultStyleKey = typeof(GallerySampleTreeDemo);  }
}

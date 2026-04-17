using Jalium.UI.Controls;
using Jalium.UI.Data;

namespace Jalium.UI.Tests;

public class TreeDataGridTests
{
    private class FileItem
    {
        public string Name { get; set; } = "";
        public long Size { get; set; }
        public List<FileItem> Children { get; set; } = new();
    }

    private static List<FileItem> CreateTestData()
    {
        return new List<FileItem>
        {
            new FileItem
            {
                Name = "src",
                Size = 0,
                Children = new List<FileItem>
                {
                    new FileItem { Name = "main.cs", Size = 1024 },
                    new FileItem
                    {
                        Name = "utils",
                        Size = 0,
                        Children = new List<FileItem>
                        {
                            new FileItem { Name = "helper.cs", Size = 512 },
                            new FileItem { Name = "config.cs", Size = 256 }
                        }
                    }
                }
            },
            new FileItem { Name = "readme.md", Size = 2048 },
            new FileItem
            {
                Name = "tests",
                Size = 0,
                Children = new List<FileItem>
                {
                    new FileItem { Name = "test1.cs", Size = 768 }
                }
            }
        };
    }

    [Fact]
    public void TreeDataGrid_InitialLoad_ShowsRootItems()
    {
        var grid = new TreeDataGrid
        {
            ChildrenPropertyPath = "Children"
        };
        grid.ItemsSource = CreateTestData();

        Assert.Equal(3, grid.FlattenedCount);
        Assert.Equal("src", ((FileItem)grid.GetItemAt(0)!).Name);
        Assert.Equal("readme.md", ((FileItem)grid.GetItemAt(1)!).Name);
        Assert.Equal("tests", ((FileItem)grid.GetItemAt(2)!).Name);
    }

    [Fact]
    public void TreeDataGrid_InitialLoad_RootNodesHaveLevel0()
    {
        var grid = new TreeDataGrid
        {
            ChildrenPropertyPath = "Children"
        };
        grid.ItemsSource = CreateTestData();

        Assert.Equal(0, grid.GetLevel(0));
        Assert.Equal(0, grid.GetLevel(1));
        Assert.Equal(0, grid.GetLevel(2));
    }

    [Fact]
    public void TreeDataGrid_InitialLoad_NodesAreCollapsed()
    {
        var grid = new TreeDataGrid
        {
            ChildrenPropertyPath = "Children"
        };
        grid.ItemsSource = CreateTestData();

        Assert.False(grid.IsExpanded(0));
        Assert.False(grid.IsExpanded(1));
        Assert.False(grid.IsExpanded(2));
    }

    [Fact]
    public void TreeDataGrid_ChildrenSelector_Works()
    {
        var grid = new TreeDataGrid
        {
            ChildrenSelector = item => ((FileItem)item).Children
        };
        grid.ItemsSource = CreateTestData();

        Assert.Equal(3, grid.FlattenedCount);
    }

    [Fact]
    public void TreeDataGrid_HasChildrenSelector_ShowsExpander()
    {
        var grid = new TreeDataGrid
        {
            ChildrenSelector = item => ((FileItem)item).Children,
            HasChildrenSelector = item => ((FileItem)item).Children.Count > 0
        };
        grid.ItemsSource = CreateTestData();

        // "src" has children
        Assert.Equal(3, grid.FlattenedCount);
        // "readme.md" doesn't have children (empty list, but HasChildrenSelector checks Count > 0)
    }

    [Fact]
    public void TreeDataGrid_ExpandAll_ShowsAllNodes()
    {
        var grid = new TreeDataGrid
        {
            ChildrenPropertyPath = "Children"
        };
        grid.ItemsSource = CreateTestData();

        grid.ExpandAll();

        // Root: src, readme.md, tests
        // src children: main.cs, utils
        // utils children: helper.cs, config.cs
        // tests children: test1.cs
        Assert.Equal(8, grid.FlattenedCount);
    }

    [Fact]
    public void TreeDataGrid_ExpandAll_CorrectLevels()
    {
        var grid = new TreeDataGrid
        {
            ChildrenPropertyPath = "Children"
        };
        grid.ItemsSource = CreateTestData();

        grid.ExpandAll();

        // src(0), main.cs(1), utils(1), helper.cs(2), config.cs(2), readme.md(0), tests(0), test1.cs(1)
        Assert.Equal(0, grid.GetLevel(0)); // src
        Assert.Equal(1, grid.GetLevel(1)); // main.cs
        Assert.Equal(1, grid.GetLevel(2)); // utils
        Assert.Equal(2, grid.GetLevel(3)); // helper.cs
        Assert.Equal(2, grid.GetLevel(4)); // config.cs
        Assert.Equal(0, grid.GetLevel(5)); // readme.md
        Assert.Equal(0, grid.GetLevel(6)); // tests
        Assert.Equal(1, grid.GetLevel(7)); // test1.cs
    }

    [Fact]
    public void TreeDataGrid_CollapseAll_ShowsOnlyRoots()
    {
        var grid = new TreeDataGrid
        {
            ChildrenPropertyPath = "Children"
        };
        grid.ItemsSource = CreateTestData();

        grid.ExpandAll();
        Assert.Equal(8, grid.FlattenedCount);

        grid.CollapseAll();
        Assert.Equal(3, grid.FlattenedCount);
    }

    [Fact]
    public void TreeDataGrid_Selection_Single_SelectsOneItem()
    {
        var grid = new TreeDataGrid
        {
            ChildrenPropertyPath = "Children",
            SelectionMode = DataGridSelectionMode.Single
        };
        grid.ItemsSource = CreateTestData();

        grid.SelectedIndex = 1;

        Assert.NotNull(grid.SelectedItem);
        Assert.Equal("readme.md", ((FileItem)grid.SelectedItem!).Name);
        Assert.Equal(1, grid.SelectedIndex);
    }

    [Fact]
    public void TreeDataGrid_Selection_SelectedIndexMinusOne_ClearsSelection()
    {
        var grid = new TreeDataGrid
        {
            ChildrenPropertyPath = "Children"
        };
        grid.ItemsSource = CreateTestData();

        grid.SelectedIndex = 1;
        Assert.NotNull(grid.SelectedItem);

        grid.SelectedIndex = -1;
        Assert.Null(grid.SelectedItem);
    }

    [Fact]
    public void TreeDataGrid_Columns_CanBeAdded()
    {
        var grid = new TreeDataGrid
        {
            ChildrenPropertyPath = "Children"
        };

        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Name",
            Binding = new Jalium.UI.Data.Binding("Name"),
            Width = 200
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Size",
            Binding = new Jalium.UI.Data.Binding("Size"),
            Width = 100
        });

        Assert.Equal(2, grid.Columns.Count);
    }

    [Fact]
    public void TreeDataGrid_NullItemsSource_HasZeroItems()
    {
        var grid = new TreeDataGrid
        {
            ChildrenPropertyPath = "Children"
        };

        Assert.Equal(0, grid.FlattenedCount);
    }

    [Fact]
    public void TreeDataGrid_EmptyItemsSource_HasZeroItems()
    {
        var grid = new TreeDataGrid
        {
            ChildrenPropertyPath = "Children"
        };
        grid.ItemsSource = new List<FileItem>();

        Assert.Equal(0, grid.FlattenedCount);
    }

    [Fact]
    public void TreeDataGrid_GetItemAt_OutOfRange_ReturnsNull()
    {
        var grid = new TreeDataGrid
        {
            ChildrenPropertyPath = "Children"
        };
        grid.ItemsSource = CreateTestData();

        Assert.Null(grid.GetItemAt(-1));
        Assert.Null(grid.GetItemAt(100));
    }

    [Fact]
    public void TreeDataGrid_GetLevel_OutOfRange_ReturnsMinusOne()
    {
        var grid = new TreeDataGrid
        {
            ChildrenPropertyPath = "Children"
        };
        grid.ItemsSource = CreateTestData();

        Assert.Equal(-1, grid.GetLevel(-1));
        Assert.Equal(-1, grid.GetLevel(100));
    }

    [Fact]
    public void TreeDataGrid_DefaultProperties()
    {
        var grid = new TreeDataGrid();

        Assert.Equal(DataGridSelectionMode.Extended, grid.SelectionMode);
        Assert.True(grid.CanUserSortColumns);
        Assert.True(grid.CanUserResizeColumns);
        Assert.True(grid.CanUserReorderColumns);
        Assert.True(grid.EnableRowVirtualization);
        Assert.False(grid.EnableColumnVirtualization);
        Assert.False(grid.IsReadOnly);
        Assert.Equal(16.0, grid.IndentSize);
        Assert.Equal(0, grid.TreeColumnIndex);
        Assert.Equal(-1, grid.SelectedIndex);
        Assert.Null(grid.SelectedItem);
        Assert.Null(grid.ItemsSource);
        Assert.NotNull(grid.Columns);
        Assert.Empty(grid.Columns);
    }

    [Fact]
    public void TreeDataGrid_LeafNodes_HaveNoChildren()
    {
        var data = new List<FileItem>
        {
            new FileItem { Name = "leaf.txt", Size = 100, Children = new List<FileItem>() }
        };

        var grid = new TreeDataGrid
        {
            ChildrenPropertyPath = "Children"
        };
        grid.ItemsSource = data;

        Assert.Equal(1, grid.FlattenedCount);
        Assert.False(grid.IsExpanded(0));
    }

    [Fact]
    public void TreeDataGrid_SelectAll_SelectsAllVisibleNodes()
    {
        var grid = new TreeDataGrid
        {
            ChildrenPropertyPath = "Children",
            SelectionMode = DataGridSelectionMode.Extended
        };
        grid.ItemsSource = CreateTestData();

        grid.SelectAll();

        Assert.Equal(3, grid.SelectedItems.Count);
    }

    [Fact]
    public void TreeDataGrid_UnselectAll_ClearsSelection()
    {
        var grid = new TreeDataGrid
        {
            ChildrenPropertyPath = "Children",
            SelectionMode = DataGridSelectionMode.Extended
        };
        grid.ItemsSource = CreateTestData();

        grid.SelectAll();
        Assert.Equal(3, grid.SelectedItems.Count);

        grid.UnselectAll();
        Assert.Empty(grid.SelectedItems);
        Assert.Null(grid.SelectedItem);
        Assert.Equal(-1, grid.SelectedIndex);
    }
}

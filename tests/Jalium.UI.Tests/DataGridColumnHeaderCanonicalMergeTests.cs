using System.ComponentModel;
using System.Reflection;
using Jalium.UI;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class DataGridColumnHeaderCanonicalMergeTests
{
    [Fact]
    public void PublicSurface_ExportsOnlyTheWpfCanonicalHeaderType()
    {
        var assembly = typeof(DataGrid).Assembly;
        var canonical = typeof(DataGridColumnHeader);

        Assert.Null(assembly.GetType("Jalium.UI.Controls.DataGridColumnHeader", throwOnError: false));
        Assert.DoesNotContain(assembly.GetExportedTypes(),
            type => type.FullName == "Jalium.UI.Controls.DataGridColumnHeader");
        Assert.Equal("Jalium.UI.Controls.Primitives.DataGridColumnHeader", canonical.FullName);
        Assert.Equal(typeof(ButtonBase), canonical.BaseType);
        Assert.NotNull(canonical.GetConstructor(Type.EmptyTypes));

        foreach (var propertyName in new[] { "CanUserSort", "Column", "DisplayIndex", "IsFrozen", "SortDirection" })
        {
            var property = canonical.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)!;
            Assert.True(property.GetMethod!.IsPublic);
            Assert.Null(property.SetMethod);
        }

        foreach (var propertyName in new[] { "SeparatorBrush", "SeparatorVisibility" })
        {
            var property = canonical.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)!;
            Assert.True(property.GetMethod!.IsPublic);
            Assert.True(property.SetMethod!.IsPublic);
        }

        Assert.Null(canonical.GetProperty("CanUserResize", BindingFlags.Public | BindingFlags.Instance));
        Assert.Null(canonical.GetProperty("CanUserReorder", BindingFlags.Public | BindingFlags.Instance));

        foreach (var fieldName in new[]
                 {
                     "CanUserSortProperty", "DisplayIndexProperty", "IsFrozenProperty",
                     "SeparatorBrushProperty", "SeparatorVisibilityProperty", "SortDirectionProperty"
                 })
        {
            var field = canonical.GetField(fieldName, BindingFlags.Public | BindingFlags.Static)!;
            Assert.Equal(typeof(DependencyProperty), field.FieldType);
        }

        var applyTemplate = canonical.GetMethod(nameof(DataGridColumnHeader.OnApplyTemplate),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!;
        Assert.True(applyTemplate.IsVirtual);
    }

    [Fact]
    public void XamlReader_ResolvesTheShortNameToTheCanonicalHeader()
    {
        const string xaml = """
            <DataGridColumnHeader xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                  Content="Name" />
            """;

        var header = Assert.IsType<DataGridColumnHeader>(XamlReader.Parse(xaml));
        Assert.Equal("Name", header.Content);
    }

    [Fact]
    public void RealizedHeader_KeepsColumnStateAutomationAndResizeBehavior()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        _ = new Application();

        try
        {
            var column = new DataGridTextColumn
            {
                Header = "Name",
                Width = 120,
                MinWidth = 40,
                MaxWidth = 200,
                CanUserSort = false
            };
            var grid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserResizeColumns = true,
                CanUserReorderColumns = true,
                FrozenColumnCount = 1,
                Width = 320,
                Height = 160
            };
            grid.Columns.Add(column);
            grid.ItemsSource = new[] { new { Name = "Alice" } };

            var host = new StackPanel { Width = 360, Height = 200 };
            host.Children.Add(grid);
            host.Measure(new Size(360, 200));
            host.Arrange(new Rect(0, 0, 360, 200));

            var headersHost = Assert.IsType<StackPanel>(grid.FindName("PART_ColumnHeadersHost"));
            var header = Assert.IsType<DataGridColumnHeader>(Assert.Single(headersHost.Children));

            Assert.Same(column, header.Column);
            Assert.False(header.CanUserSort);
            Assert.Equal(column.DisplayIndex, header.DisplayIndex);
            Assert.True(header.IsFrozen);
            Assert.Equal(column.SortDirection, header.SortDirection);

            var peer = Assert.IsType<DataGridColumnHeaderAutomationPeer>(header.GetAutomationPeer());
            Assert.Equal("Name", peer.GetName());

            header.BeginResize(0);
            header.UpdateResize(30);
            header.EndResize();

            Assert.Equal(150, column.Width.DisplayValue);
            Assert.Equal(150, header.Width);
            Assert.Same(header, headersHost.Children[0]);

            column.SortDirection = ListSortDirection.Ascending;
            Assert.Equal(ListSortDirection.Ascending, header.SortDirection);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private static void ResetApplicationState()
    {
        typeof(Application).GetField("_current", BindingFlags.NonPublic | BindingFlags.Static)?.SetValue(null, null);
        typeof(ThemeManager).GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static)?.Invoke(null, null);
    }
}

using System.Collections;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Documents;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

public sealed class LayeredNamespaceFacetWpfParityTests
{
    [Fact]
    public void UIElement_IsConcreteAndPubliclyInstantiable()
    {
        Assert.False(typeof(UIElement).IsAbstract);
        ConstructorInfo constructor = typeof(UIElement).GetConstructor(Type.EmptyTypes)
            ?? throw new InvalidOperationException("UIElement's public parameterless constructor was not found.");
        Assert.True(constructor.IsPublic);
        Assert.IsType<UIElement>(constructor.Invoke(null));
    }

    [Fact]
    public void ContextMenu_UsesCanonicalMenuBaseHierarchy()
    {
        Assert.Equal(typeof(MenuBase), typeof(ContextMenu).BaseType);
        Assert.IsAssignableFrom<MenuBase>(new ContextMenu());
    }

    [Fact]
    public void GridSplitter_UsesThumbDragContract()
    {
        Assert.Equal(typeof(Thumb), typeof(GridSplitter).BaseType);
        Assert.True(typeof(Thumb).IsAssignableFrom(typeof(GridSplitter)));
        Assert.Equal(typeof(Thumb), typeof(GridSplitter).GetMethod(nameof(Thumb.CancelDrag))!.DeclaringType);
        Assert.Equal(typeof(Thumb), typeof(GridSplitter).GetProperty(nameof(Thumb.IsDragging))!.DeclaringType);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        var splitter = new GridSplitter
        {
            ResizeDirection = GridResizeDirection.Columns,
            ResizeBehavior = GridResizeBehavior.CurrentAndNext,
        };
        Grid.SetColumn(splitter, 0);
        grid.Children.Add(splitter);
        grid.Measure(new Size(200, 40));
        grid.Arrange(new Rect(0, 0, 200, 40));

        splitter.RaiseEvent(new DragStartedEventArgs(0, 0)
        {
            RoutedEvent = Thumb.DragStartedEvent,
            Source = splitter,
        });
        splitter.RaiseEvent(new DragDeltaEventArgs(10, 0)
        {
            RoutedEvent = Thumb.DragDeltaEvent,
            Source = splitter,
        });
        splitter.RaiseEvent(new DragCompletedEventArgs(10, 0, canceled: false)
        {
            RoutedEvent = Thumb.DragCompletedEvent,
            Source = splitter,
        });

        Assert.True(grid.ColumnDefinitions[0].Width.IsStar);
        Assert.True(grid.ColumnDefinitions[1].Width.IsStar);
        Assert.Equal(110, grid.ColumnDefinitions[0].Width.Value, 6);
        Assert.Equal(90, grid.ColumnDefinitions[1].Width.Value, 6);
    }

    [Fact]
    public void Image_UsesFrameworkElementAndCanonicalUriContract()
    {
        Type imageType = typeof(Jalium.UI.Controls.Image);
        Assert.Equal(typeof(FrameworkElement), imageType.BaseType);
        Assert.Contains(typeof(IUriContext), imageType.GetInterfaces());
        Assert.DoesNotContain(typeof(IReclaimableResource), imageType.GetInterfaces());
        Assert.NotNull(imageType.GetMethod(
            "OnRender",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
    }

    [Fact]
    public void DataGrid_OverridesSharedSelectionHook()
    {
        MethodInfo method = typeof(DataGrid).GetMethod(
            "OnSelectionChanged",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            ?? throw new InvalidOperationException("DataGrid.OnSelectionChanged was not found.");

        Assert.True(method.IsFamily);
        Assert.True(method.IsVirtual);
        Assert.Equal(typeof(ItemsControl), method.GetBaseDefinition().DeclaringType);
        Assert.Equal("e", Assert.Single(method.GetParameters()).Name);
    }

    [Fact]
    public void GridDefinitionCollections_AreReplaceableAndTransferOwnership()
    {
        Assert.True(typeof(Grid).GetProperty(nameof(Grid.RowDefinitions))!.CanWrite);
        Assert.True(typeof(Grid).GetProperty(nameof(Grid.ColumnDefinitions))!.CanWrite);
        Assert.Equal(typeof(object), typeof(RowDefinitionCollection).BaseType);
        Assert.Equal(typeof(object), typeof(ColumnDefinitionCollection).BaseType);
        Assert.Contains(typeof(IList<RowDefinition>), typeof(RowDefinitionCollection).GetInterfaces());
        Assert.Contains(typeof(IList<ColumnDefinition>), typeof(ColumnDefinitionCollection).GetInterfaces());
        Assert.Contains(typeof(IList), typeof(RowDefinitionCollection).GetInterfaces());
        Assert.Contains(typeof(IList), typeof(ColumnDefinitionCollection).GetInterfaces());
        Assert.Null(typeof(RowDefinitionCollection).GetConstructor(Type.EmptyTypes));
        Assert.Null(typeof(ColumnDefinitionCollection).GetConstructor(Type.EmptyTypes));

        var donor = new Grid();
        var rows = donor.RowDefinitions;
        var columns = donor.ColumnDefinitions;
        rows.Add(new RowDefinition());
        columns.Add(new ColumnDefinition());
        donor.RowDefinitions = null!;
        donor.ColumnDefinitions = null!;

        var first = new Grid
        {
            RowDefinitions = rows,
            ColumnDefinitions = columns,
        };

        Assert.Same(rows, first.RowDefinitions);
        Assert.Same(columns, first.ColumnDefinitions);
        Assert.Throws<ArgumentException>(() => rows.Add(rows[0]));
        Assert.Throws<ArgumentException>(() => columns.Add(columns[0]));
        Assert.Throws<ArgumentException>(() => new Grid { RowDefinitions = rows });
        Assert.Throws<ArgumentException>(() => new Grid { ColumnDefinitions = columns });

        first.RowDefinitions = null!;
        first.ColumnDefinitions = null!;

        var second = new Grid
        {
            RowDefinitions = rows,
            ColumnDefinitions = columns,
        };
        Assert.Same(rows, second.RowDefinitions);
        Assert.Same(columns, second.ColumnDefinitions);
        Assert.NotSame(rows, first.RowDefinitions);
        Assert.NotSame(columns, first.ColumnDefinitions);

        rows.Add(new RowDefinition());
        rows.Add(new RowDefinition());
        columns.Add(new ColumnDefinition());
        columns.Add(new ColumnDefinition());
        rows.RemoveRange(1, 2);
        columns.RemoveRange(1, 2);
        Assert.Single(rows);
        Assert.Single(columns);
    }

    [Fact]
    public void FlowDocument_ExposesCanonicalPaginatorAndViewerUsesInternalBridge()
    {
        Type type = typeof(FlowDocument);
        Assert.Contains(typeof(Jalium.UI.Documents.IDocumentPaginatorSource), type.GetInterfaces());
        Assert.DoesNotContain(typeof(Jalium.UI.Controls.Primitives.IDocumentPaginatorSource), type.GetInterfaces());

        var document = new FlowDocument();
        var viewer = new FlowDocumentPageViewer { Document = document };
        Assert.Same(
            document.ViewerPaginator,
            ((DocumentViewerBase)viewer).Document!.DocumentPaginator);
    }

    [Fact]
    public void InlineCollection_UsesTextElementCollectionAndNonGenericCollectionContracts()
    {
        Assert.Equal(typeof(TextElementCollection<Inline>), typeof(InlineCollection).BaseType);
        var collection = new InlineCollection(new Paragraph());
        Assert.IsAssignableFrom<IList>(collection);
        Assert.IsAssignableFrom<ICollection>(collection);
        Assert.IsAssignableFrom<IEnumerable>(collection);
    }
}

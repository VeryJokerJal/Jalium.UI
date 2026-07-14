using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Data;
using Jalium.UI.Markup;
using Xunit;

namespace Jalium.UI.Tests;

public sealed class GridViewSurfaceParityTests
{
    [Fact]
    public void GridViewIsExtensibleContentHostWithStableStyleKeys()
    {
        Assert.False(typeof(GridView).IsSealed);
        Assert.Contains(typeof(Jalium.UI.Markup.IAddChild), typeof(GridView).GetInterfaces());

        Assert.Same(GridView.GridViewStyleKey, GridView.GridViewStyleKey);
        Assert.Same(GridView.GridViewItemContainerStyleKey, GridView.GridViewItemContainerStyleKey);
        Assert.Same(GridView.GridViewScrollViewerStyleKey, GridView.GridViewScrollViewerStyleKey);

        var view = new TestGridView();
        var column = new GridViewColumn { Header = "Name" };
        view.AddPublic(column);

        Assert.Same(column, Assert.Single(view.Columns));
        Assert.Contains("Columns.Count:1", view.ToString(), StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => view.AddTextPublic("not a column"));
    }

    [Fact]
    public void AttachedColumnCollectionIsPreparedAndClearedWithContainer()
    {
        var view = new TestGridView();
        view.Columns.Add(new GridViewColumn());
        var item = new ListViewItem();

        view.PreparePublic(item);
        Assert.Same(view.Columns, GridView.GetColumnCollection(item));

        view.ClearPublic(item);
        Assert.Null(GridView.GetColumnCollection(item));
        Assert.Throws<ArgumentNullException>(() => GridView.GetColumnCollection(null!));
        Assert.Throws<ArgumentNullException>(() => GridView.SetColumnCollection(null!, view.Columns));
    }

    [Fact]
    public void HeaderPresenterUsesCanonicalNamespaceAndHeaderPrecedence()
    {
        Assert.Equal(typeof(GridViewRowPresenterBase), typeof(GridViewHeaderRowPresenter).BaseType);

        var column = new GridViewColumn { Header = "Name", Width = 84 };
        var columns = new GridViewColumnCollection { column };
        var presenter = new GridViewHeaderRowPresenter
        {
            Columns = columns,
            ColumnHeaderStringFormat = "[{0}]",
            ColumnHeaderToolTip = "tip"
        };

        presenter.Measure(new Size(500, 100));

        var header = Assert.IsType<GridViewColumnHeader>(presenter.GetVisualChild(0));
        Assert.Equal("[Name]", header.Content);
        Assert.Equal("tip", header.ToolTip);
        Assert.Same(column, header.Column);
        Assert.Equal(84, header.Width);
        Assert.Same(GridView.ColumnHeaderStringFormatProperty, GridViewHeaderRowPresenter.ColumnHeaderStringFormatProperty);
    }

    [Fact]
    public void HeaderPresenterKeepsGeneratedHeaderWhileAutoWidthIsMeasured()
    {
        var column = new GridViewColumn { Header = "Name" };
        var presenter = new GridViewHeaderRowPresenter
        {
            Columns = new GridViewColumnCollection { column }
        };
        var generatedHeader = presenter.GetVisualChild(0);

        presenter.Measure(new Size(400, 100));

        Assert.Same(generatedHeader, presenter.GetVisualChild(0));
        Assert.True(column.ActualWidth >= 120);
    }

    [Fact]
    public void RowPresenterBuildsCellsFromCanonicalColumns()
    {
        Assert.Equal(typeof(GridViewRowPresenterBase), typeof(GridViewRowPresenter).BaseType);

        var presenter = new GridViewRowPresenter
        {
            Columns = new GridViewColumnCollection
            {
                new() { Header = "Name", Width = 100, DisplayMemberBinding = new Binding(nameof(Row.Name)) }
            },
            Content = new Row { Name = "Ada" }
        };

        presenter.Measure(new Size(500, 100));

        var cell = Assert.IsType<TextBlock>(presenter.GetVisualChild(0));
        Assert.Equal("Ada", cell.Text);
        Assert.Contains("Content:", presenter.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RowPresenterUsesCellTemplateSelectorWhenNoExplicitTemplateExists()
    {
        var template = new DataTemplate();
        template.SetVisualTree(() => new Border());
        var selector = new FixedTemplateSelector(template);
        var row = new Row { Name = "Ada" };
        var presenter = new GridViewRowPresenter
        {
            Columns = new GridViewColumnCollection
            {
                new() { CellTemplateSelector = selector }
            },
            Content = row
        };

        var cell = Assert.IsType<ContentPresenter>(presenter.GetVisualChild(0));

        Assert.Same(row, cell.Content);
        Assert.IsType<Border>(cell.GetVisualChild(0));
        Assert.Equal(1, selector.CallCount);
    }

    [Fact]
    public void ColumnHeaderIsAButtonAndRaisesClick()
    {
        Assert.Equal(typeof(ButtonBase), typeof(GridViewColumnHeader).BaseType);
        Assert.NotNull(GridViewColumnHeader.RoleProperty);

        var header = new TestHeader();
        var clicked = false;
        header.Click += (_, _) => clicked = true;

        header.ClickPublic();

        Assert.True(clicked);
        Assert.Equal(GridViewColumnHeaderRole.Normal, header.Role);
    }

    private sealed class TestGridView : GridView
    {
        public void AddPublic(object value) => AddChild(value);
        public void AddTextPublic(string value) => AddText(value);
        public void PreparePublic(ListViewItem item) => PrepareItem(item);
        public void ClearPublic(ListViewItem item) => ClearItem(item);
    }

    private sealed class TestHeader : GridViewColumnHeader
    {
        public void ClickPublic() => OnClick();
    }

    private sealed class Row
    {
        public string Name { get; init; } = string.Empty;
    }

    private sealed class FixedTemplateSelector(DataTemplate template) : Jalium.UI.Controls.DataTemplateSelector
    {
        public int CallCount { get; private set; }

        public override DataTemplate? SelectTemplate(object? item, DependencyObject container)
        {
            CallCount++;
            return template;
        }
    }
}

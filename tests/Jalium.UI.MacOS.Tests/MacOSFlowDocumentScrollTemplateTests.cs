using System.ComponentModel;
using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;
[Collection("macOS Window globals")]
public sealed class MacOSFlowDocumentScrollTemplateTests
{
    [Fact]
    public void ConstructorRetainsSemanticContentWithoutCreatingAFrame()
    {
        var viewer = new FlowDocumentScrollViewer();
        Assert.Equal(0, viewer.VisualChildrenCount);
        Assert.Same(viewer, Assert.IsAssignableFrom<FrameworkElement>(viewer.DocumentView).LogicalParentInternal);
        viewer.Style = DefaultStyle();
        Layout(viewer);
        Assert.Same(viewer.DocumentView, Host(viewer).Content);
        viewer.Background = Brushes.Navy;
        viewer.Padding = new Thickness(0);
        viewer.BorderThickness = new Thickness(0);
        viewer.CornerRadius = new CornerRadius(0);
        var root = Assert.IsType<Border>(viewer.FindName("PART_Root"));
        Assert.Same(Brushes.Navy, root.Background);
        Assert.Equal(new Thickness(0), root.Padding);
        Assert.Equal(new Thickness(0), root.BorderThickness);
    }

    [Fact]
    public void DefaultToolAndFindSurfacesExecuteRealCommands()
    {
        var viewer = Create();
        viewer.IsToolBarVisible = true;
        Layout(viewer);
        var toolbar = Assert.IsType<ToolBar>(viewer.FindName("PART_ToolBar"));
        Assert.Equal(Visibility.Visible, toolbar.Visibility);
        var zoomIn = toolbar.Items.OfType<Button>().Single(x => Equals(x.Content, "+"));
        Assert.Same(viewer, zoomIn.CommandTarget);
        Assert.Equal("100%", toolbar.Items.OfType<TextBlock>().Single().Text);
        zoomIn.PerformClick();
        Assert.Equal(110, viewer.Zoom);
        var find = toolbar.Items.OfType<Button>().Single(x => Equals(x.Content, "Find"));
        Assert.Same(viewer, find.CommandTarget);
        Assert.True(find.IsEnabled, $"Find disabled: ownerEnabled={viewer.IsEnabled}, document={viewer.Document != null}, command={find.Command?.GetType().Name}, canExecute={(find.Command as RoutedCommand)?.CanExecute(null, viewer)}");
        find.PerformClick();
        Assert.True(viewer.IsFindToolBarVisible, "Find click did not open surface before layout");
        Layout(viewer);
        Assert.True(viewer.IsFindToolBarVisible);
        Assert.Equal("FindOpen", VisualStateManager.GetCurrentStateName(viewer, "FindStates"));
        var input = Assert.IsType<TextBox>(viewer.FindName("PART_FindTextBox"));
        input.Text = "needle";
        Assert.Equal("needle", viewer.SearchText);
        input.RaiseEvent(new KeyEventArgs(UIElement.PreviewKeyDownEvent, Key.Enter, ModifierKeys.None, true, false, 0));
        Assert.Equal("needle", viewer.Selection!.Text);
        input.RaiseEvent(new KeyEventArgs(UIElement.PreviewKeyDownEvent, Key.Escape, ModifierKeys.None, true, false, 0));
        Assert.False(viewer.IsFindToolBarVisible);
        Assert.Equal(Visibility.Collapsed, Assert.IsType<Grid>(viewer.FindName("PART_FindBar")).Visibility);
    }

    [Fact]
    public void RetemplateKeepsContentSelectionZoomAndScrollAndRetiresTheOldHost()
    {
        var viewer = Create();
        viewer.Zoom = 120;
        viewer.Find("needle");
        Layout(viewer);
        var content = viewer.DocumentView;
        var selection = viewer.Selection;
        var old = Host(viewer);
        old.ScrollToBottom();
        Layout(viewer);
        double offset = old.VerticalOffset;
        Assert.True(offset > 0);
        viewer.Template = Blank();
        viewer.ApplyTemplate();
        Layout(viewer);
        Assert.Null(old.Content);
        viewer.ClearValue(Control.TemplateProperty);
        viewer.ApplyTemplate();
        Layout(viewer);
        var current = Host(viewer);
        Assert.NotSame(old, current);
        Assert.Same(content, current.Content);
        Assert.Same(selection, viewer.Selection);
        Assert.Equal(120, viewer.Zoom);
        Assert.Equal(offset, current.VerticalOffset, 2);
        old.Measure(new Size(100, 100));
        old.Arrange(new Rect(0, 0, 100, 100));
        Assert.Null(old.Content);
        Assert.Same(content, current.Content);
    }

    [Fact]
    public void RetiredFindInputCannotCloseTheCurrentFindSurface()
    {
        var viewer = Create();
        Layout(viewer);
        viewer.Find();
        var old = Assert.IsType<TextBox>(viewer.FindName("PART_FindTextBox"));
        viewer.Template = Blank();
        viewer.ApplyTemplate();
        viewer.ClearValue(Control.TemplateProperty);
        viewer.ApplyTemplate();
        Layout(viewer);
        old.RaiseEvent(new KeyEventArgs(UIElement.PreviewKeyDownEvent, Key.Escape, ModifierKeys.None, true, false, 0));
        Assert.True(viewer.IsFindToolBarVisible);
        Assert.NotSame(old, viewer.FindName("PART_FindTextBox"));
    }

    [Fact]
    public void MissingPartsKeepDocumentOperationsAndEmptyDisabledStates()
    {
        var viewer = Create();
        viewer.Template = Blank();
        Layout(viewer);
        Assert.Null(viewer.FindName("PART_ContentHost"));
        Assert.True(viewer.Find("needle"));
        Assert.Equal("needle", viewer.Selection!.Text);
        Assert.Null(viewer.DocumentView.VisualParent);
        viewer.ClearValue(Control.TemplateProperty);
        Layout(viewer);
        Assert.Equal("Ready", VisualStateManager.GetCurrentStateName(viewer, "DocumentStates"));
        viewer.Find();
        Assert.True(viewer.IsFindToolBarVisible);
        viewer.Document = null;
        Assert.False(viewer.IsFindToolBarVisible);
        Assert.Equal("Empty", VisualStateManager.GetCurrentStateName(viewer, "DocumentStates"));
        viewer.IsEnabled = false;
        Assert.Equal("Disabled", VisualStateManager.GetCurrentStateName(viewer, "CommonStates"));
    }

    [Fact]
    public void InternalFindAndZoomOperationsKeepCallerBindings()
    {
        var viewer = Create();
        var model = new Model();
        BindingOperations.SetBinding(viewer, FlowDocumentScrollViewer.SearchTextProperty, new Binding(nameof(Model.Query)) { Source = model, Mode = BindingMode.TwoWay });
        BindingOperations.SetBinding(viewer, FlowDocumentScrollViewer.ZoomProperty, new Binding(nameof(Model.Zoom)) { Source = model, Mode = BindingMode.TwoWay });
        Assert.True(viewer.Find("needle"));
        viewer.IncreaseZoom();
        Assert.NotNull(BindingOperations.GetBindingExpression(viewer, FlowDocumentScrollViewer.SearchTextProperty));
        Assert.NotNull(BindingOperations.GetBindingExpression(viewer, FlowDocumentScrollViewer.ZoomProperty));
        model.Query = "other";
        model.Zoom = 130;
        Assert.Equal("other", viewer.SearchText);
        Assert.Equal(130, viewer.Zoom);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(110)]
    [InlineData(140)]
    public void ZoomedScrollExtentKeepsTheLastSourceLineInsideTheViewport(double zoom)
    {
        var viewer = Create();
        viewer.Zoom = zoom;
        Layout(viewer);
        var host = Host(viewer);
        host.ScrollToBottom();
        Layout(viewer);
        var renderer = Assert.IsType<FlowDocumentPresentation>(viewer.DocumentView);
        var last = renderer.Lines.Last();
        var point = renderer.TransformToVisual(host).Transform(new Point(0, last.Top));
        Assert.InRange(point.Y, -0.5, host.ViewportHeight);
    }

    private sealed class Model : INotifyPropertyChanged
    {
        private string _query = "";
        private double _zoom = 100;
        public string Query
        {
            get => _query;
            set
            {
                _query = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Query)));
            }
        }

        public double Zoom
        {
            get => _zoom;
            set
            {
                _zoom = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Zoom)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private static FlowDocumentScrollViewer Create() => new()
    {
        Style = DefaultStyle(),
        Document = FlowDocument.FromText("needle " + string.Join(' ', Enumerable.Repeat("segment", 300)))
    };
    private static Style DefaultStyle() => (Style)MenuFlyoutTemplateTestHelper.Themes.Value[typeof(FlowDocumentScrollViewer)];
    private static ScrollViewer Host(FlowDocumentScrollViewer viewer) => Assert.IsType<ScrollViewer>(viewer.FindName("PART_ContentHost"));
    private static ControlTemplate Blank()
    {
        var template = new ControlTemplate(typeof(FlowDocumentScrollViewer));
        template.SetVisualTree(() => new Border());
        return template;
    }

    private static void Layout(UIElement element)
    {
        for (var i = 0; i < 4; i++)
        {
            element.Measure(new Size(320, 240));
            element.Arrange(new Rect(0, 0, 320, 240));
        }
    }
}

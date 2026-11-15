using System.ComponentModel;
using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Documents;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;
[Collection("macOS Window globals")]
public sealed class MacOSDocumentPageReaderTemplateTests
{
    public MacOSDocumentPageReaderTemplateTests() => FlowDocumentTemplateTestHelper.Register();
    [Fact]
    public void PageViewerUsesTemplateContentAndRetainsTheMasterPage()
    {
        var viewer = new FlowDocumentPageViewer
        {
            Document = Document()
        };
        Assert.Equal(0, viewer.VisualChildrenCount);
        Assert.Same(viewer, viewer.PageView.LogicalParentInternal);
        viewer.Style = Style<FlowDocumentPageViewer>();
        Layout(viewer);
        var old = Assert.IsType<ContentPresenter>(viewer.FindName("PART_PagePresenter"));
        Assert.Same(viewer.PageView, old.Content);
        Assert.True(viewer.PageViewWidth > 0);
        viewer.NextPage();
        viewer.Zoom = 120;
        int page = viewer.MasterPageNumber;
        var retained = viewer.PageView;
        viewer.Template = Blank<FlowDocumentPageViewer>();
        viewer.ApplyTemplate();
        Assert.Null(old.Content);
        viewer.ClearValue(Control.TemplateProperty);
        Layout(viewer);
        var current = Assert.IsType<ContentPresenter>(viewer.FindName("PART_PagePresenter"));
        Assert.Same(retained, current.Content);
        Assert.Equal(page, viewer.MasterPageNumber);
        Assert.Equal(120, viewer.Zoom);
        old.Measure(new Size(100, 100));
        old.Arrange(new Rect(0, 0, 100, 100));
        Assert.Same(retained, current.Content);
        Assert.Null(old.Content);
    }

    [Fact]
    public void PageFindInputSearchesOriginalTextAndRetiredInputCannotCloseIt()
    {
        var viewer = new FlowDocumentPageViewer
        {
            Document = Document(),
            Style = Style<FlowDocumentPageViewer>()
        };
        Layout(viewer);
        viewer.Find();
        Layout(viewer);
        var old = Assert.IsType<TextBox>(viewer.FindName("PART_FindTextBox"));
        old.Text = "needle";
        Preview(old, Key.Enter);
        Assert.Equal("needle", viewer.Selection!.Text);
        viewer.Template = Blank<FlowDocumentPageViewer>();
        viewer.ApplyTemplate();
        viewer.ClearValue(Control.TemplateProperty);
        Layout(viewer);
        Preview(old, Key.Escape);
        Assert.True(viewer.IsFindToolBarVisible);
        Preview(Assert.IsType<TextBox>(viewer.FindName("PART_FindTextBox")), Key.Escape);
        Assert.False(viewer.IsFindToolBarVisible);
    }

    [Fact]
    public void ReaderModeCommandsUseRetainedModelsAndTheDefaultPresenter()
    {
        var reader = new FlowDocumentReader
        {
            Document = Document()
        };
        Assert.Equal(0, reader.VisualChildrenCount);
        reader.Style = Style<FlowDocumentReader>();
        Layout(reader);
        var presenter = Assert.IsType<ContentPresenter>(reader.FindName("PART_ViewerPresenter"));
        var first = Assert.IsType<FlowDocumentPageViewer>(reader.ActiveViewer);
        Assert.Same(first, presenter.Content);
        Assert.Same(reader, first.LogicalParentInternal);
        var toolbar = Assert.IsType<WrapPanel>(reader.FindName("PART_ToolBar"));
        var two = toolbar.Children.OfType<RadioButton>().Single(x => Equals(x.Content, "Two pages"));
        Assert.Same(reader, two.CommandTarget);
        two.PerformClick();
        Layout(reader);
        Assert.Equal(FlowDocumentReaderViewingMode.TwoPage, reader.ViewingMode);
        Assert.True(reader.IsTwoPageMode);
        Assert.IsType<DocumentViewer>(presenter.Content);
        var scroll = toolbar.Children.OfType<RadioButton>().Single(x => Equals(x.Content, "Scroll"));
        scroll.PerformClick();
        Layout(reader);
        Assert.True(reader.IsScrollMode);
        Assert.IsType<FlowDocumentScrollViewer>(presenter.Content);
        reader.SwitchViewingMode(FlowDocumentReaderViewingMode.Page);
        Layout(reader);
        Assert.Same(first, reader.ActiveViewer);
        Assert.Same(first, presenter.Content);
        Assert.True(reader.IsPageMode);
        Assert.Equal("Page", VisualStateManager.GetCurrentStateName(reader, "ViewingModeStates"));
    }

    [Theory]
    [InlineData(480)]
    [InlineData(320)]
    public void NarrowReaderKeepsEveryHeaderActionVisibleAndUsable(double width)
    {
        var reader = new FlowDocumentReader
        {
            Document = Document(),
            Style = Style<FlowDocumentReader>()
        };
        reader.ApplyTemplate();
        var header = Assert.IsType<WrapPanel>(reader.FindName("PART_ToolBar"));
        foreach (var control in header.Children.OfType<Control>())
            control.Style = (Style)MenuFlyoutTemplateTestHelper.Themes.Value[control.GetType()];
        for (int i = 0; i < 4; i++)
        {
            reader.Measure(new Size(width, 700));
            reader.Arrange(new Rect(0, 0, width, 700));
        }

        Assert.True(header.Children.OfType<FrameworkElement>().Max(child => child.TransformToVisual(header).Transform(new Point()).Y) > 0);
        foreach (var child in header.Children.OfType<FrameworkElement>())
        {
            var point = child.TransformToVisual(header).Transform(new Point());
            Assert.Equal(Visibility.Visible, child.Visibility);
            Assert.True(child.RenderSize.Width > 0 && child.RenderSize.Height > 0);
            Assert.InRange(point.X, 0, header.RenderSize.Width);
            Assert.True(point.X + child.RenderSize.Width <= header.RenderSize.Width + 0.1);
            Assert.True(point.Y + child.RenderSize.Height <= header.RenderSize.Height + 0.1);
        }

        var buttons = header.Children.OfType<Button>().ToArray();
        var find = buttons.Single(button => Equals(button.Content, "Find"));
        Assert.True(find.IsEnabled);
        find.PerformClick();
        Assert.True(reader.IsFindToolBarVisible);
        double zoom = reader.Zoom;
        buttons.Single(button => Equals(button.Content, "+")).PerformClick();
        Assert.True(reader.Zoom > zoom);
        buttons.Single(button => Equals(button.Content, "−")).PerformClick();
        Assert.Equal(zoom, reader.Zoom);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisabledDocumentSurfaceHasVisibleFeedbackAndRestoresOnEnable(bool reading)
    {
        Control viewer = reading ? new FlowDocumentReader
        {
            Document = Document(),
            Style = Style<FlowDocumentReader>()
        }

        : new FlowDocumentPageViewer
        {
            Document = Document(),
            Style = Style<FlowDocumentPageViewer>()
        };
        Layout(viewer);
        var root = Assert.IsType<Border>(viewer.FindName("PART_Root"));
        viewer.IsEnabled = false;
        Assert.Equal(0.5, root.Opacity);
        Assert.Equal("Disabled", VisualStateManager.GetCurrentStateName(viewer, "CommonStates"));
        if (viewer is FlowDocumentReader reader)
        {
            double zoom = reader.Zoom;
            var header = Assert.IsType<WrapPanel>(reader.FindName("PART_ToolBar"));
            var button = header.Children.OfType<Button>().Single(child => Equals(child.Content, "+"));
            Assert.False(button.IsEnabled);
            button.PerformClick();
            Assert.Equal(zoom, reader.Zoom);
        }

        viewer.IsEnabled = true;
        Assert.Equal(1.0, root.Opacity);
        Assert.Equal("Normal", VisualStateManager.GetCurrentStateName(viewer, "CommonStates"));
    }

    [Fact]
    public void ReaderRetemplateAndMissingPartsRetainPageAndSelection()
    {
        var reader = new FlowDocumentReader
        {
            Document = Document(),
            Style = Style<FlowDocumentReader>()
        };
        Layout(reader);
        reader.NextPage();
        int page = reader.PageNumber;
        Assert.True(reader.Find("needle"));
        var selection = reader.Selection;
        var active = reader.ActiveViewer;
        var old = Assert.IsType<ContentPresenter>(reader.FindName("PART_ViewerPresenter"));
        reader.Template = Blank<FlowDocumentReader>();
        reader.ApplyTemplate();
        Assert.Null(old.Content);
        Assert.Same(active, reader.ActiveViewer);
        reader.ClearValue(Control.TemplateProperty);
        Layout(reader);
        Assert.Same(active, Assert.IsType<ContentPresenter>(reader.FindName("PART_ViewerPresenter")).Content);
        Assert.Same(selection, reader.Selection);
        old.Measure(new Size(100, 100));
        old.Arrange(new Rect(0, 0, 100, 100));
        Assert.Null(old.Content);
        reader.Document = null;
        Assert.Equal("Empty", VisualStateManager.GetCurrentStateName(reader, "DocumentStates"));
    }

    [Fact]
    public void ReaderFindAvailabilityAndViewFallbackUpdateVisibleState()
    {
        var reader = new FlowDocumentReader
        {
            Document = Document(),
            Style = Style<FlowDocumentReader>()
        };
        Layout(reader);
        reader.Find();
        Assert.True(reader.IsFindToolBarVisible);
        var input = Assert.IsType<TextBox>(reader.FindName("PART_FindTextBox"));
        input.Text = "needle";
        Preview(input, Key.Enter);
        Assert.Equal("needle", reader.Selection!.Text);
        reader.IsFindEnabled = false;
        Assert.False(reader.IsFindToolBarVisible);
        reader.SwitchViewingMode(FlowDocumentReaderViewingMode.TwoPage);
        reader.IsTwoPageViewEnabled = false;
        Assert.Equal(FlowDocumentReaderViewingMode.Page, reader.ViewingMode);
        Assert.True(reader.IsPageMode);
        reader.IsEnabled = false;
        Assert.Equal("Disabled", VisualStateManager.GetCurrentStateName(reader, "CommonStates"));
    }

    [Fact]
    public void ReaderFocusKeepsChildSelectionVisibleWhileUsingItsOwnFindBar()
    {
        var reader = new FocusReader
        {
            Document = Document(),
            Style = Style<FlowDocumentReader>()
        };
        Layout(reader);
        Assert.False(reader.IsInactiveSelectionHighlightEnabled);
        reader.SetFocusWithin(true);
        Assert.True(Assert.IsType<FlowDocumentPageViewer>(reader.ActiveViewer).IsInactiveSelectionHighlightEnabled);
        reader.SwitchViewingMode(FlowDocumentReaderViewingMode.Scroll);
        Assert.True(Assert.IsType<FlowDocumentScrollViewer>(reader.ActiveViewer).IsInactiveSelectionHighlightEnabled);
        reader.SetFocusWithin(false);
        Assert.False(Assert.IsType<FlowDocumentScrollViewer>(reader.ActiveViewer).IsInactiveSelectionHighlightEnabled);
    }

    [Fact]
    public void ReaderOperationsAndAvailabilityFallbackPreserveCallerBindings()
    {
        var reader = new FlowDocumentReader
        {
            Document = Document(),
            Style = Style<FlowDocumentReader>()
        };
        var model = new BoundModel();
        BindingOperations.SetBinding(reader, FlowDocumentReader.ViewingModeProperty, new Binding(nameof(BoundModel.Mode)) { Source = model, Mode = BindingMode.TwoWay });
        BindingOperations.SetBinding(reader, FlowDocumentReader.ZoomProperty, new Binding(nameof(BoundModel.Zoom)) { Source = model, Mode = BindingMode.TwoWay });
        BindingOperations.SetBinding(reader, FlowDocumentReader.SearchTextProperty, new Binding(nameof(BoundModel.Query)) { Source = model, Mode = BindingMode.TwoWay });
        reader.SwitchViewingMode(FlowDocumentReaderViewingMode.Scroll);
        reader.Find("needle");
        reader.IncreaseZoom();
        reader.IsScrollViewEnabled = false;
        Assert.NotNull(BindingOperations.GetBindingExpression(reader, FlowDocumentReader.ViewingModeProperty));
        Assert.NotNull(BindingOperations.GetBindingExpression(reader, FlowDocumentReader.ZoomProperty));
        Assert.NotNull(BindingOperations.GetBindingExpression(reader, FlowDocumentReader.SearchTextProperty));
        model.Mode = FlowDocumentReaderViewingMode.TwoPage;
        model.Zoom = 130;
        model.Query = "Section";
        Assert.Equal(model.Mode, reader.ViewingMode);
        Assert.Equal(model.Zoom, reader.Zoom);
        Assert.Equal(model.Query, reader.SearchText);
    }

    private sealed class BoundModel : INotifyPropertyChanged
    {
        private FlowDocumentReaderViewingMode _mode;
        private double _zoom = 100;
        private string _query = "";
        public FlowDocumentReaderViewingMode Mode
        {
            get => _mode;
            set
            {
                _mode = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Mode)));
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

        public string Query
        {
            get => _query;
            set
            {
                _query = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Query)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed class FocusReader : FlowDocumentReader
    {
        internal void SetFocusWithin(bool value) => OnIsKeyboardFocusWithinChanged(value);
    }

    private static FlowDocument Document()
    {
        var document = FlowDocument.FromText(string.Join("\n\n", Enumerable.Range(0, 20).Select(i => $"Section {i} needle {i} " + new string ('x', 180))));
        document.PageWidth = 240;
        document.PageHeight = 200;
        document.PagePadding = new Thickness(8);
        document.FontSize = 14;
        return document;
    }

    private static Style Style<T>()
        where T : Control => (Style)MenuFlyoutTemplateTestHelper.Themes.Value[typeof(T)];
    private static ControlTemplate Blank<T>()
        where T : Control
    {
        var template = new ControlTemplate(typeof(T));
        template.SetVisualTree(() => new Border());
        return template;
    }

    private static void Preview(UIElement target, Key key) => target.RaiseEvent(new KeyEventArgs(UIElement.PreviewKeyDownEvent, key, ModifierKeys.None, true, false, 0));
    private static void Layout(UIElement target)
    {
        for (int i = 0; i < 4; i++)
        {
            target.Measure(new Size(1000, 700));
            target.Arrange(new Rect(0, 0, 1000, 700));
        }
    }
}

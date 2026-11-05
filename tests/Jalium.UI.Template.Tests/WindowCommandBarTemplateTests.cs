using System.ComponentModel;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Data;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class WindowCommandBarTemplateTests
{
    private static readonly Lazy<ResourceDictionary> Defaults = new(() =>
    {
        Jalium.UI.Markup.ThemeLoader.Initialize();
        using var stream = new MemoryStream();
        return ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
    });

    [Fact]
    public void DefaultTemplateOwnsFrameAndOriginalCommandControls()
    {
        var left = new Button { Width = 120, Content = "Workspace" };
        var right = new Button { Width = 80, Content = "Tools" };
        var bar = Create(); bar.LeftCommands = left; bar.RightCommands = right; Layout(bar);
        Assert.IsType<Border>(bar.GetVisualChild(0));
        Assert.Same(left, Assert.IsType<ContentPresenter>(bar.FindName("PART_LeftCommandsHost")).Content);
        Assert.Same(right, Assert.IsType<ContentPresenter>(bar.FindName("PART_RightCommandsHost")).Content);
        Assert.True(right.TransformToAncestor(bar).X >= 300);
        Assert.False(bar.HasLocalValue(UIElement.ClipToBoundsProperty));
        Assert.Equal("HasLeftCommands", VisualStateManager.GetCurrentStateName(bar, "LeftCommandStates"));
    }

    [Fact]
    public void TitleAndAvailabilityStatesFollowDependencyProperties()
    {
        var bar = Create(); bar.Title = "Window title"; Layout(bar);
        var text = Assert.IsType<TextBlock>(bar.FindName("PART_TitleText"));
        Assert.False(text.Focusable); Assert.False(text.IsHitTestVisible);
        Assert.Equal(Visibility.Collapsed, text.Visibility);
        bar.IsTitleVisible = true; Layout(bar);
        Assert.Equal(Visibility.Visible, text.Visibility); Assert.Equal("Window title", text.Text);
        bar.Title = "Renamed"; Assert.Equal("Renamed", text.Text);
        Assert.Equal("TitleVisible", VisualStateManager.GetCurrentStateName(bar, "TitleStates"));
        bar.RightCommands = new Button(); Assert.Equal("HasRightCommands", VisualStateManager.GetCurrentStateName(bar, "RightCommandStates"));
        bar.IsEnabled = false; Assert.Equal("Disabled", VisualStateManager.GetCurrentStateName(bar, "CommonStates"));
    }

    [Fact]
    public void RetiredPresentersReleaseControlsAndCannotReattachThem()
    {
        var left = new Button { Content = "Search" }; var right = new Button { Content = "Actions" };
        var bar = Create(); bar.LeftCommands = left; bar.RightCommands = right; Layout(bar);
        var oldLeft = Assert.IsType<ContentPresenter>(bar.FindName("PART_LeftCommandsHost"));
        var oldRight = Assert.IsType<ContentPresenter>(bar.FindName("PART_RightCommandsHost"));
        var blank = new ControlTemplate(typeof(WindowCommandBar)); blank.SetVisualTree(() => new Border());
        bar.Template = blank; Layout(bar);
        Assert.Null(oldLeft.Content); Assert.Null(oldRight.Content);
        Assert.Null(left.VisualParent); Assert.Null(right.VisualParent);
        bar.ClearValue(Control.TemplateProperty); Layout(bar);
        var current = Assert.IsType<ContentPresenter>(bar.FindName("PART_LeftCommandsHost"));
        Assert.Same(left, current.Content); Assert.NotSame(oldLeft, current);
        oldLeft.InvalidateMeasure(); oldLeft.Measure(new Size(200, 40));
        Assert.Same(current, left.VisualParent); Assert.Null(oldLeft.Content);
    }

    [Fact]
    public void CommandBindingSurvivesTemplateReplacementAndModelUpdates()
    {
        var model = new Model { Content = new Button { Content = "First" } }; var bar = Create();
        BindingOperations.SetBinding(bar, WindowCommandBar.LeftCommandsProperty, new Binding(nameof(Model.Content)) { Source = model });
        Layout(bar);
        var blank = new ControlTemplate(typeof(WindowCommandBar)); blank.SetVisualTree(() => new Border());
        bar.Template = blank; Layout(bar); model.Content = new Button { Content = "Second" };
        bar.ClearValue(Control.TemplateProperty); Layout(bar);
        Assert.Same(model.Content, Assert.IsType<ContentPresenter>(bar.FindName("PART_LeftCommandsHost")).Content);
        Assert.NotNull(BindingOperations.GetBindingExpression(bar, WindowCommandBar.LeftCommandsProperty));
    }

    [Fact]
    public void LeadingNativeInsetAndCssBoxAreAllocatedByTheTemplate()
    {
        var bar = Create(); bar.Padding = new Thickness(80, 0, 0, 0); Layout(bar);
        var left = Assert.IsType<ContentPresenter>(bar.FindName("PART_LeftCommandsHost"));
        Assert.Equal(80, left.TransformToAncestor(bar).X);
        // An explicit native inset has higher precedence than CSS padding.
        bar.ClearValue(Control.PaddingProperty);
        Css.SetStyle(bar, "border:3px solid lime;padding:12px"); Layout(bar);
        Assert.Equal(new Point(15, 15), left.TransformToAncestor(bar));
        var frame = Assert.IsType<Border>(bar.FindName("PART_Border"));
        Assert.Equal(370, Assert.IsType<Grid>(frame.Child).RenderSize.Width);
    }

    [Fact]
    public void FramePaletteFollowsScopedResourcesAndLocalBrushes()
    {
        var bar = Create(); bar.Resources["TitleBarBackground"] = Brushes.Red; Layout(bar);
        var border = Assert.IsType<Border>(bar.FindName("PART_Border")); Assert.Same(Brushes.Red, border.Background);
        bar.Resources["TitleBarBackground"] = Brushes.Blue; Layout(bar); Assert.Same(Brushes.Blue, border.Background);
        bar.Background = Brushes.Green; bar.Resources["TitleBarBackground"] = Brushes.Red; Layout(bar);
        Assert.Same(Brushes.Green, border.Background);
    }

    private static WindowCommandBar Create()
    {
        var bar = new WindowCommandBar(); bar.Resources.MergedDictionaries.Add(Defaults.Value); return bar;
    }
    private static void Layout(FrameworkElement element)
    {
        element.InvalidateMeasure(); element.Measure(new Size(400, 48));
        element.Arrange(new Rect(0, 0, 400, 48)); element.UpdateLayout();
    }
    private sealed class Model : INotifyPropertyChanged
    {
        private object? _content;
        public object? Content { get => _content; set { _content = value; PropertyChanged?.Invoke(this, new(nameof(Content))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}

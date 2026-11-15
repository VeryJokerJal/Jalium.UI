using System.ComponentModel;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Data;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class ScrollBarTemplateTests
{
    private static readonly Lazy<ResourceDictionary> Defaults = new(() =>
    {
        Jalium.UI.Markup.ThemeLoader.Initialize();
        using var stream = new MemoryStream();
        return ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
    });

    [Fact]
    public void DefaultTemplateOwnsAllPartsAndStyles()
    {
        var bar = Create();
        Assert.Single(Enumerable.Range(0, bar.VisualChildrenCount));
        var track = Assert.IsType<Track>(bar.FindName("PART_Track"));
        Assert.Same(track, bar.Track);
        Assert.IsType<Thumb>(track.Thumb);
        Assert.NotNull(track.Thumb!.Template);
        Assert.NotNull(track.DecreaseRepeatButton!.Style);
        Assert.NotNull(track.IncreaseRepeatButton!.Style);
        Assert.NotNull(Assert.IsType<RepeatButton>(bar.FindName("PART_LineDecreaseButton")).Style);
        Assert.Equal(16, bar.DesiredSize.Width);
    }

    [Fact]
    public void LinePageAndCommandsPreserveTwoWayValueBinding()
    {
        var bar = Create();
        var model = new Model { Value = 40 };
        BindingOperations.SetBinding(bar, RangeBase.ValueProperty, new Binding(nameof(Model.Value))
        { Source = model, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        var binding = BindingOperations.GetBindingExpression(bar, RangeBase.ValueProperty);
        var line = Assert.IsType<RepeatButton>(bar.FindName("PART_LineIncreaseButton"));
        line.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.Equal(41, model.Value);
        bar.Track!.IncreaseRepeatButton!.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.Equal(51, model.Value);
        ScrollBar.ScrollToEndCommand.Execute(null, bar);
        Assert.Equal(100, model.Value);
        Assert.Same(binding, BindingOperations.GetBindingExpression(bar, RangeBase.ValueProperty));
        model.Value = 12;
        Assert.Equal(12, bar.Value);
        Assert.Equal(12, bar.Track.Value);
    }

    [Fact]
    public void ReplacementTemplateRetiresAllPartsAndCanOmitTrack()
    {
        var bar = Create();
        var line = Assert.IsType<RepeatButton>(bar.FindName("PART_LineIncreaseButton"));
        var page = bar.Track!.IncreaseRepeatButton!;
        var thumb = bar.Track.Thumb!;
        var template = new ControlTemplate(typeof(ScrollBar));
        template.SetVisualTree(() => new Border { Name = "CustomRoot" });
        bar.Template = template;
        Layout(bar);
        Assert.Null(bar.Track);
        Assert.IsType<Border>(bar.GetVisualChild(0));
        line.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        page.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        thumb.RaiseEvent(new DragDeltaEventArgs(0, 100) { RoutedEvent = Thumb.DragDeltaEvent });
        Assert.Equal(0, bar.Value);
        bar.ClearValue(Control.TemplateProperty);
        Layout(bar);
        Assert.NotNull(bar.Track);
        Assert.NotSame(page, bar.Track!.IncreaseRepeatButton);
        bar.Track.IncreaseRepeatButton!.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.Equal(10, bar.Value);
    }

    [Fact]
    public void OrientationChangesAreTemplateBoundAndUpdateDirections()
    {
        var bar = Create();
        bar.Orientation = Orientation.Horizontal;
        Layout(bar, 240, 16);
        Assert.Equal(Orientation.Horizontal, bar.Track!.Orientation);
        Assert.Equal("Left", bar.DecreaseDirection);
        Assert.Equal("Right", bar.IncreaseDirection);
        Assert.Equal("Right", Assert.IsType<RepeatButton>(bar.FindName("PART_LineIncreaseButton")).Tag);
        Assert.Equal(16, bar.DesiredSize.Height);
        Assert.Equal(16, bar.Track.RenderSize.Height);
    }

    [Fact]
    public void DisabledStateDimmingBelongsToTheTemplateRoot()
    {
        var bar = Create();
        var root = Assert.IsType<Grid>(bar.GetVisualChild(0));
        Assert.Equal(1, root.Opacity);
        bar.IsEnabled = false;
        Assert.Equal(.45, root.Opacity);
        bar.IsEnabled = true;
        Assert.Equal(1, root.Opacity);
    }

    [Fact]
    public void OverlayPresentationAndRetirementKeepAuthoredPartBindings()
    {
        var bar = Create();
        var track = bar.Track!;
        var thumb = track.Thumb!;
        var model = new Model { Value = .65 };
        BindingOperations.SetBinding(track, UIElement.OpacityProperty, new Binding(nameof(Model.Value)) { Source = model });
        var expression = BindingOperations.GetBindingExpression(track, UIElement.OpacityProperty);
        var overlay = typeof(ScrollBar).GetProperty("IsOverlayStyle", BindingFlags.NonPublic | BindingFlags.Instance)!;
        overlay.SetValue(bar, true);
        Assert.Same(expression, BindingOperations.GetBindingExpression(track, UIElement.OpacityProperty));
        overlay.SetValue(bar, false);
        Assert.Equal(.65, track.Opacity);
        model.Value = .8;
        Assert.Equal(.8, track.Opacity);
        overlay.SetValue(bar, true);
        var replacement = new ControlTemplate(typeof(ScrollBar)); replacement.SetVisualTree(() => new Border());
        bar.Template = replacement; Layout(bar);
        Assert.Same(expression, BindingOperations.GetBindingExpression(track, UIElement.OpacityProperty));
        Assert.Equal(.8, track.Opacity);
        model.Value = .4;
        Assert.Equal(.4, track.Opacity);
    }

    [Fact]
    public void FocusedKeyboardNavigationUsesOrientationAndPreservesValueBinding()
    {
        Keyboard.Initialize();
        var bar = Create();
        var model = new Model { Value = 40 };
        BindingOperations.SetBinding(bar, RangeBase.ValueProperty, new Binding(nameof(Model.Value))
        { Source = model, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        var expression = BindingOperations.GetBindingExpression(bar, RangeBase.ValueProperty);
        Assert.True(bar.Focus());
        Assert.True(Press(bar, Key.Down).Handled); Assert.Equal(41, model.Value);
        Assert.False(Press(bar, Key.Left).Handled); Assert.Equal(41, model.Value);
        Press(bar, Key.PageDown); Assert.Equal(51, model.Value);
        Press(bar, Key.Home); Assert.Equal(0, model.Value);
        Press(bar, Key.End); Assert.Equal(100, model.Value);
        bar.Orientation = Orientation.Horizontal;
        Press(bar, Key.Left); Assert.Equal(99, model.Value);
        Press(bar, Key.Right); Assert.Equal(100, model.Value);
        bar.IsEnabled = false;
        Press(bar, Key.Home); Assert.Equal(100, model.Value);
        Assert.Same(expression, BindingOperations.GetBindingExpression(bar, RangeBase.ValueProperty));
    }
    private static KeyEventArgs Press(UIElement target, Key key)
    {
        var args = (KeyEventArgs)Activator.CreateInstance(typeof(KeyEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
            null, new object[] { UIElement.KeyDownEvent, key, ModifierKeys.None, true, false, 0 }, null)!;
        target.RaiseEvent(args); return args;
    }

    [Fact]
    public void NonAnimatedLineArrowsKeepTemplateFillAndFollowForeground()
    {
        var bar = Create();
        var button = Assert.IsType<RepeatButton>(bar.FindName("PART_LineDecreaseButton"));
        button.ApplyTemplate();
        var arrow = Assert.IsType<Jalium.UI.Shapes.Path>(button.FindName("PART_Arrow"));
        var fillExpression = BindingOperations.GetBindingExpressionBase(arrow, Jalium.UI.Shapes.Shape.FillProperty);
        Assert.NotNull(fillExpression);
        Assert.False(button.UseScrollBarArrowAnimation);
        Assert.False(arrow.HasLocalValue(Jalium.UI.Shapes.Shape.FillProperty));
        Assert.False(arrow.HasLocalValue(UIElement.RenderTransformProperty));
        button.Foreground = Brushes.Blue;
        Assert.Same(Brushes.Blue, arrow.Fill);
        button.Foreground = Brushes.Red;
        Assert.Same(Brushes.Red, arrow.Fill);
        button.UseScrollBarArrowAnimation = true;
        button.UseScrollBarArrowAnimation = false;
        Assert.Same(Brushes.Red, button.Foreground);
        Assert.Same(fillExpression, BindingOperations.GetBindingExpressionBase(arrow, Jalium.UI.Shapes.Shape.FillProperty));
        Assert.Same(Brushes.Red, arrow.Fill);
        Assert.False(arrow.HasLocalValue(Jalium.UI.Shapes.Shape.FillProperty));
        Assert.False(arrow.HasLocalValue(UIElement.RenderTransformProperty));
    }

    private static ScrollBar Create()
    {
        var bar = new ScrollBar { Maximum = 100, ViewportSize = 20 };
        bar.Resources.MergedDictionaries.Add(Defaults.Value);
        typeof(ScrollBar).GetProperty("UseMacOSScrollBarBehavior", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(bar, false);
        typeof(ScrollBar).GetProperty("IsOverlayStyle", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(bar, false);
        Layout(bar);
        return bar;
    }
    private static void Layout(UIElement element, double width = 16, double height = 240)
    { element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout(); }
    private sealed class Model : INotifyPropertyChanged
    {
        private double _value;
        public double Value { get => _value; set { _value = value; PropertyChanged?.Invoke(this, new(nameof(Value))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}

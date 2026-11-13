using System.ComponentModel;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Data;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class NumberBoxTemplateTests
{
    [Fact]
    public void RangeChangesRefreshTextAndPreserveTheBoundBaseValue()
    {
        var model = new Model { Value = 5000 };
        var box = Create();
        BindingOperations.SetBinding(box, NumberBox.ValueProperty, new Binding(nameof(Model.Value)) { Source = model, Mode = BindingMode.TwoWay });
        var binding = BindingOperations.GetBindingExpression(box, NumberBox.ValueProperty);
        box.Maximum = 1000;
        Assert.Equal(1000, box.Value); Assert.Equal("1000", box.Text);
        Assert.Same(binding, BindingOperations.GetBindingExpression(box, NumberBox.ValueProperty));
        box.Maximum = 10000;
        Assert.Equal(5000, box.Value); Assert.Equal("5000", box.Text);
        model.Value = 50; box.Minimum = 100;
        Assert.Equal(100, box.Value); Assert.Equal("100", box.Text);
        box.Minimum = 0;
        Assert.Equal(50, box.Value); Assert.Equal("50", box.Text);
    }

    [Fact]
    public void CursorLiteralsUseThePlatformCursorConverter()
    {
        var button = Assert.IsType<Button>(Jalium.UI.Markup.XamlReader.Parse(
            "<Button xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Cursor='Arrow' />"));
        Assert.Same(Cursors.Arrow, button.Cursor);
    }

    private static readonly Lazy<ResourceDictionary> Defaults = new(() =>
    {
        Jalium.UI.Markup.ThemeLoader.Initialize();
        using var stream = new MemoryStream();
        return ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
    });

    [Fact]
    public void KeyboardStepsSurviveBlurAndKeepTheValueBinding()
    {
        Keyboard.Initialize();
        var model = new Model { Value = 43 };
        var box = Create();
        BindingOperations.SetBinding(box, NumberBox.ValueProperty, new Binding(nameof(Model.Value))
        { Source = model, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        var binding = BindingOperations.GetBindingExpression(box, NumberBox.ValueProperty);
        Assert.True(box.Focus());
        Press(box, Key.Up);
        Assert.Equal(44, box.Value);
        Assert.Equal("44", box.Text);
        Assert.True(new Button().Focus());
        Assert.Equal(44, box.Value);
        Assert.Equal(44, model.Value);
        Assert.Same(binding, BindingOperations.GetBindingExpression(box, NumberBox.ValueProperty));
        model.Value = 50;
        Assert.Equal("50", box.Text);
    }

    [Fact]
    public void StepsCommitPendingTextAndExpressionValuesWithoutReplacingBindings()
    {
        Keyboard.Initialize();
        var box = Create();
        box.Value = 10;
        box.AcceptsExpression = true;
        Assert.True(box.Focus());
        box.Text = "2+3";
        box.StepUp();
        Assert.Equal(6, box.Value);
        Assert.Equal("6", box.Text);
        Press(box, Key.PageUp);
        Assert.Equal(6 + box.LargeChange, box.Value);
        var value = box.Value;
        Assert.True(new Button().Focus());
        Assert.Equal(value, box.Value);
    }

    [Fact]
    public void HiddenAndCompactModesUseTemplateStatesAndReadOnlyDisablesStepping()
    {
        Keyboard.Initialize();
        var box = Create();
        var host = Assert.IsType<Grid>(box.FindName("PART_SpinButtonsHost"));
        var input = Assert.IsType<Grid>(box.FindName("PART_ContentHost"));
        box.SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden;
        Assert.Equal(Visibility.Collapsed, host.Visibility);
        Assert.Equal(2, Grid.GetColumnSpan(input));
        box.SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline;
        Assert.Equal(Visibility.Visible, host.Visibility);
        Assert.Equal(1, Grid.GetColumnSpan(input));
        box.SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact;
        Assert.Equal(Visibility.Collapsed, host.Visibility);
        Assert.True(box.Focus());
        Assert.Equal(Visibility.Visible, host.Visibility);
        box.IsReadOnly = true;
        var up = Assert.IsType<RepeatButton>(box.FindName("PART_UpSpinButton"));
        Assert.False(up.IsEnabled);
        var value = box.Value;
        box.StepUp();
        Press(box, Key.Up);
        Assert.Equal(value, box.Value);
        box.IsReadOnly = false;
        Assert.True(up.IsEnabled);
        Assert.True(new Button().Focus());
        Assert.Equal(Visibility.Collapsed, host.Visibility);
    }

    [Fact]
    public void ACustomTemplateOwnsItsButtonGeometryAndRetiredButtonsCannotChangeTheValue()
    {
        var box = new NumberBox { Value = 10 };
        RepeatButton? up = null;
        var template = new ControlTemplate(typeof(NumberBox));
        template.SetVisualTree(() =>
        {
            up = new RepeatButton { Name = "PART_UpSpinButton", CornerRadius = new CornerRadius(25), BorderThickness = new Thickness(4, 2, 1, 3) };
            var root = new StackPanel();
            root.Children.Add(new Grid { Name = "PART_ContentHost" });
            root.Children.Add(up);
            return root;
        });
        box.Template = template;
        Layout(box);
        Assert.Equal(new CornerRadius(25), up!.CornerRadius);
        Assert.Equal(new Thickness(4, 2, 1, 3), up.BorderThickness);
        box.CornerRadius = new CornerRadius(40);
        Assert.Equal(new CornerRadius(25), up.CornerRadius);
        up.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, up));
        Assert.Equal(11, box.Value);
        box.Template = null;
        up.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, up));
        Assert.Equal(11, box.Value);
    }

    private static NumberBox Create()
    {
        var box = new NumberBox { Width = 240 };
        box.Resources.MergedDictionaries.Add(Defaults.Value);
        Layout(box);
        return box;
    }

    [Fact]
    public void HeaderHasItsOwnAutoRowAndEmptyContentLeavesNoGap()
    {
        var box = Create();
        var header = Assert.IsType<ContentPresenter>(box.FindName("PART_HeaderPresenter"));
        var border = Assert.IsType<Border>(box.FindName("OuterBorder"));
        Assert.False(box.HasHeader);
        Assert.Equal(Visibility.Collapsed, header.Visibility);
        Assert.Equal(0, border.TransformToVisual(box).Transform(new Point()).Y);
        box.Header = "Amount";
        Layout(box);
        Assert.True(box.HasHeader);
        Assert.Equal("Amount", header.Content);
        Assert.Equal("HeaderVisible", VisualStateManager.GetCurrentStateName(box, "HeaderStates"));
        double headerBottom = header.TransformToVisual(box).Transform(new Point(0, header.RenderSize.Height)).Y;
        double inputTop = border.TransformToVisual(box).Transform(new Point()).Y;
        Assert.True(inputTop >= headerBottom + 6, $"Header ends {headerBottom}; input starts {inputTop}");
        box.Header = string.Empty;
        Layout(box);
        Assert.False(box.HasHeader);
        Assert.Equal(Visibility.Collapsed, header.Visibility);
        Assert.Equal(0, border.TransformToVisual(box).Transform(new Point()).Y);
    }

    [Fact]
    public void HeaderSupportsVisualContentTemplatesSelectorsAndFormatting()
    {
        var box = Create();
        var header = Assert.IsType<ContentPresenter>(box.FindName("PART_HeaderPresenter"));
        var content = new TextBlock { Text = "Visual header", Foreground = Brushes.Blue };
        box.Header = content; Layout(box);
        Assert.Same(content, header.Content);
        Assert.True(content.RenderSize.Width > 0);
        var peer = new NumberBoxAutomationPeer(box);
        Assert.Equal("Visual header", peer.GetName());
        var template = new DataTemplate();
        template.SetVisualTree(() => new TextBlock { Text = "Templated header" });
        box.HeaderTemplate = template;
        box.Header = new object(); Layout(box);
        Assert.Same(template, header.ContentTemplate);
        Assert.Equal("Templated header", peer.GetName());
        Assert.Same(box.Header, header.Content);
        box.HeaderTemplate = null;
        var selector = new HeaderSelector(template);
        box.HeaderTemplateSelector = selector; Layout(box);
        Assert.Same(selector, header.ContentTemplateSelector);
        Assert.True(selector.Calls > 0);
        box.HeaderTemplateSelector = null;
        box.Header = 42;
        box.HeaderStringFormat = "Amount {0}"; Layout(box);
        Assert.Equal("Amount {0}", header.ContentStringFormat);
        Assert.Equal("Amount 42", peer.GetName());
        AutomationProperties.SetName(box, "Explicit numeric label");
        Assert.Equal("Explicit numeric label", peer.GetName());
    }

    [Fact]
    public void CustomTemplateCanReplaceHeaderWithoutAnAdditionalControlDrawing()
    {
        var box = new HeaderDrawingProbe { Header = "Retained caption" };
        var template = new ControlTemplate(typeof(NumberBox));
        template.SetVisualTree(() => new Border { Background = Brushes.Blue });
        box.Template = template; Layout(box);
        Assert.Null(box.FindName("PART_HeaderPresenter"));
        var drawing = new DrawingGroup();
        using (var context = drawing.Open()) box.RenderOwnContent(context);
        Assert.Empty(drawing.Children);
        Assert.True(box.HasHeader);
    }

    [Fact]
    public void ReadOnlyAndDisabledAutomationCannotChangeTheNumericValue()
    {
        var box = Create();
        box.Value = 12;
        var peer = new NumberBoxAutomationPeer(box);
        box.IsReadOnly = true;
        Assert.True(peer.IsReadOnly);
        Assert.Throws<InvalidOperationException>(() => peer.SetValue(20));
        Assert.Equal(12, box.Value);
        box.IsReadOnly = false;
        peer.SetValue(20);
        Assert.Equal(20, box.Value);
        box.IsEnabled = false;
        Assert.True(peer.IsReadOnly);
        Assert.Throws<InvalidOperationException>(() => peer.SetValue(30));
    }

    [Fact]
    public void LongHeaderWrapsWithinTheInputWidthAndKeepsAnIndependentRow()
    {
        var box = Create();
        box.Width = 180;
        box.Header = "Invoice total including tax and additional processing charges for this purchase";
        box.Measure(new Size(180, double.PositiveInfinity));
        box.Arrange(new Rect(0, 0, 180, box.DesiredSize.Height));
        var header = Assert.IsType<ContentPresenter>(box.FindName("PART_HeaderPresenter"));
        var input = Assert.IsType<Border>(box.FindName("OuterBorder"));
        Assert.True(header.RenderSize.Height > box.FontSize * 2);
        Assert.True(header.RenderSize.Width <= 180);
        Assert.True(input.TransformToVisual(box).Transform(new Point()).Y >= header.RenderSize.Height + 6);
        Assert.True(box.RenderSize.Height >= input.TransformToVisual(box).Transform(new Point(0, input.RenderSize.Height)).Y);
    }

    private sealed class HeaderDrawingProbe : NumberBox
    {
        internal void RenderOwnContent(DrawingContext context) => OnRender(context);
    }

    private sealed class HeaderSelector(DataTemplate template) : DataTemplateSelector
    {
        internal int Calls;
        public override DataTemplate? SelectTemplate(object? item, DependencyObject? container)
        { Calls++; return template; }
    }
    private static void Layout(UIElement element)
    {
        element.Measure(new Size(240, 100)); element.Arrange(new Rect(0, 0, 240, 100)); element.UpdateLayout();
    }
    private static void Press(UIElement target, Key key)
        => target.RaiseEvent((KeyEventArgs)Activator.CreateInstance(typeof(KeyEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
            null, new object[] { UIElement.KeyDownEvent, key, ModifierKeys.None, true, false, 0 }, null)!);
    private sealed class Model : INotifyPropertyChanged
    {
        private double _value;
        public double Value { get => _value; set { _value = value; PropertyChanged?.Invoke(this, new(nameof(Value))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}

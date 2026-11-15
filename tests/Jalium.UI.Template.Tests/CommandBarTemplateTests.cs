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
public sealed class CommandBarTemplateTests
{
    private static readonly Lazy<ResourceDictionary> Defaults = new(() =>
    {
        Jalium.UI.Markup.ThemeLoader.Initialize();
        using var stream = new MemoryStream();
        var resources = ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
        ThemeManager.RegisterControlThemes(new ResourceDictionary
        {
            [typeof(AppBarButton)] = resources[typeof(AppBarButton)],
            [typeof(AppBarToggleButton)] = resources[typeof(AppBarToggleButton)],
        });
        return resources;
    });

    [Fact]
    public void CommandsAndOpenStateBeforeATemplateDoNotCreateAVisualTree()
    {
        var bar = new CommandBar();
        bar.PrimaryCommands.Add(new AppBarButton { Label = "New" });
        bar.SecondaryCommands.Add(new AppBarButton { Label = "Settings" });
        bar.IsOpen = true;
        Assert.Equal(0, VisualTreeHelper.GetChildrenCount(bar));
        Assert.True(bar.IsOverflowOpen);
        var template = new ControlTemplate(typeof(CommandBar));
        template.SetVisualTree(() => new Border());
        bar.Template = template;
        Layout(bar);
        Assert.True(bar.IsOpen);
        bar.IsOpen = false;
        Assert.Null(bar.FindName("PART_MoreButton"));
    }

    [Fact]
    public void DefaultTemplateProjectsCommandsAndTracksOverflowAvailability()
    {
        var bar = Create();
        Assert.Same(bar.PrimaryCommands, Part<ItemsControl>(bar, "PART_PrimaryItemsControl").ItemsSource);
        Assert.Same(bar.SecondaryCommands, Part<ItemsControl>(bar, "PART_SecondaryItemsControl").ItemsSource);
        var more = Part<Button>(bar, "PART_MoreButton");
        Assert.Equal(Visibility.Collapsed, more.Visibility);
        bar.SecondaryCommands.Add(new AppBarButton { Label = "Settings" });
        Assert.True(bar.HasSecondaryCommands);
        Assert.True(bar.IsOverflowButtonVisible);
        Assert.Equal(Visibility.Visible, more.Visibility);
        bar.SecondaryCommands.Clear();
        Assert.Equal(Visibility.Collapsed, more.Visibility);
        bar.OverflowButtonVisibility = CommandBarOverflowButtonVisibility.Visible;
        Assert.Equal(Visibility.Visible, more.Visibility);
        bar.OverflowButtonVisibility = CommandBarOverflowButtonVisibility.Collapsed;
        Assert.False(bar.IsOverflowButtonVisible);
    }

    [Fact]
    public void MoreButtonPreservesTheOpenBindingAndEventsArePaired()
    {
        var model = new Model();
        var bar = Create();
        bar.SecondaryCommands.Add(new AppBarButton { Label = "Settings" });
        bar.SetBinding(CommandBar.IsOpenProperty, new Binding(nameof(Model.Open)) { Source = model });
        var events = new List<string>();
        bar.Opening += (_, _) => events.Add("opening");
        bar.Opened += (_, _) => events.Add("opened");
        bar.Closing += (_, _) => events.Add("closing");
        bar.Closed += (_, _) => events.Add("closed");
        Click(Part<Button>(bar, "PART_MoreButton"));
        Assert.True(model.Open);
        Assert.Equal("Open", VisualStateManager.GetCurrentStateName(bar, "OpenStates"));
        Click(Part<Button>(bar, "PART_MoreButton"));
        Assert.False(model.Open);
        Assert.Equal(new[] { "opening", "opened", "closing", "closed" }, events);
        Assert.NotNull(bar.GetBindingExpression(CommandBar.IsOpenProperty));
    }

    [Fact]
    public void MinimalAndHiddenModesUseTemplateStatesAndRecoverWhenOpened()
    {
        var bar = Create();
        bar.PrimaryCommands.Add(new AppBarButton { Label = "New" });
        bar.ClosedDisplayMode = CommandBarClosedDisplayMode.Minimal;
        Layout(bar);
        Assert.True(bar.IsOverflowButtonVisible);
        Assert.Equal(Visibility.Collapsed, Part<ItemsControl>(bar, "PART_PrimaryItemsControl").Visibility);
        bar.IsOpen = true;
        Assert.Equal(Visibility.Visible, Part<ItemsControl>(bar, "PART_PrimaryItemsControl").Visibility);
        Assert.False(bar.IsOverflowOpen); // Minimal mode can expand primary commands without an empty popup.
        bar.IsOpen = false;
        bar.SecondaryCommands.Add(new AppBarButton { Label = "Settings" });
        bar.ClosedDisplayMode = CommandBarClosedDisplayMode.Hidden;
        Layout(bar);
        Assert.Equal(0, bar.DesiredSize.Height);
        bar.IsOpen = true;
        Layout(bar);
        Assert.Equal("Expanded", VisualStateManager.GetCurrentStateName(bar, "DisplayStates"));
        Assert.True(bar.DesiredSize.Height >= 48);
    }

    [Fact]
    public void LabelLayoutUsesInheritedTemplateValuesAndKeepsConsumerBindings()
    {
        var model = new Model();
        var bar = Create();
        var command = new AppBarButton { Label = "Save" };
        command.SetBinding(AppBarButton.IsCompactProperty, new Binding(nameof(Model.Compact)) { Source = model });
        bar.PrimaryCommands.Add(command);
        bar.DefaultLabelPosition = CommandBarDefaultLabelPosition.Right;
        Layout(bar);
        Assert.Equal(CommandBarDefaultLabelPosition.Right, CommandBar.GetLabelPosition(Part<ItemsControl>(bar, "PART_PrimaryItemsControl")));
        Assert.Equal(CommandBarDefaultLabelPosition.Right, CommandBar.GetLabelPosition(command));
        var rightTrigger = Assert.Single(command.Template!.Triggers.OfType<Trigger>(), trigger => trigger.Value?.ToString() == "Right");
        Assert.Same(CommandBar.LabelPositionProperty, rightTrigger.Property);
        Assert.Equal(Orientation.Horizontal, Part<StackPanel>(command, "PART_ContentPanel").Orientation);
        bar.DefaultLabelPosition = CommandBarDefaultLabelPosition.Collapsed;
        Layout(bar);
        Assert.Equal(Visibility.Collapsed, Part<TextBlock>(command, "LabelText").Visibility);
        Assert.False(model.Compact);
        Assert.NotNull(command.GetBindingExpression(AppBarButton.IsCompactProperty));
        bar.PrimaryCommands.Remove(command);
        Layout(bar);
        var newHost = new StackPanel();
        newHost.Children.Add(command);
        Layout(newHost);
        Assert.Equal(CommandBarDefaultLabelPosition.Bottom, CommandBar.GetLabelPosition(command));
        Assert.Equal(Visibility.Visible, Part<TextBlock>(command, "LabelText").Visibility);
        Assert.NotNull(command.GetBindingExpression(AppBarButton.IsCompactProperty));
    }

    [Fact]
    public void BackgroundAndOverflowOverrideFlowThroughTemplateBindings()
    {
        var bar = Create();
        var border = Part<ContentControl>(bar, "PART_OverflowBorder");
        bar.Background = Brushes.Red;
        Assert.Same(Brushes.Red, border.Background);
        bar.OverflowBackground = Brushes.Blue;
        Assert.Same(Brushes.Blue, border.Background);
        bar.Background = Brushes.Green;
        Assert.Same(Brushes.Blue, border.Background);
        bar.OverflowBackground = null;
        Assert.Same(Brushes.Green, border.Background);
    }

    [Fact]
    public void ReplacingATemplateRetiresOldHostsAndMoreButtons()
    {
        var bar = Create();
        var primary = new AppBarButton { Label = "New" };
        bar.PrimaryCommands.Add(primary);
        bar.SecondaryCommands.Add(new AppBarButton { Label = "Settings" });
        Layout(bar);
        var oldPrimary = Part<ItemsControl>(bar, "PART_PrimaryItemsControl");
        var oldSecondary = Part<ItemsControl>(bar, "PART_SecondaryItemsControl");
        var oldMore = Part<Button>(bar, "PART_MoreButton");
        var template = bar.Template;
        bar.Template = null;
        Assert.Null(oldPrimary.ItemsSource);
        Assert.Null(oldSecondary.ItemsSource);
        Click(oldMore);
        Assert.False(bar.IsOpen);
        bar.Template = template;
        Layout(bar);
        Assert.Same(bar.PrimaryCommands, Part<ItemsControl>(bar, "PART_PrimaryItemsControl").ItemsSource);
        Assert.NotSame(oldMore, Part<Button>(bar, "PART_MoreButton"));
        Assert.True(primary.IsVisible);
    }

    [Fact]
    public void PrimaryArrowNavigationSkipsSeparatorsAndDisabledCommands()
    {
        var bar = Create();
        var first = new AppBarButton { Label = "First" };
        var last = new AppBarButton { Label = "Last" };
        bar.PrimaryCommands.Add(first);
        bar.PrimaryCommands.Add(new AppBarSeparator());
        bar.PrimaryCommands.Add(new AppBarButton { Label = "Disabled", IsEnabled = false });
        bar.PrimaryCommands.Add(last);
        Layout(bar);
        Assert.True(first.Focus());
        Key(first, Jalium.UI.Input.Key.Right);
        Assert.Same(last, Keyboard.FocusedElement);
        Key(last, Jalium.UI.Input.Key.Home);
        Assert.Same(first, Keyboard.FocusedElement);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OverflowCommandsRespectStickyAndEscapeReturnsFocus(bool sticky)
    {
        var bar = Create();
        bar.IsSticky = sticky;
        var command = new AppBarButton { Label = "Settings" };
        bar.SecondaryCommands.Add(command);
        var window = new DisplayedWindow { Width = 600, Height = 300, Content = bar };
        try
        {
            Layout(window);
            var more = Part<Button>(bar, "PART_MoreButton");
            var popup = Part<Popup>(bar, "PART_OverflowPopup");
            popup.ShouldConstrainToRootBounds = true;
            Assert.True(more.Focus());
            Click(more);
            Layout(window);
            Pump(bar);
            Assert.True(popup.IsOpen);
            Assert.True(command.IsKeyboardFocused);
            Click(command);
            Assert.Equal(sticky, bar.IsOpen);
            if (sticky) Key(command, Jalium.UI.Input.Key.Escape);
            Pump(bar);
            Assert.False(bar.IsOpen);
            Assert.Same(more, Keyboard.FocusedElement);
        }
        finally { bar.IsOpen = false; window.Close(); }
    }

    [Fact]
    public void OverflowArrowNavigationPrecedesScrollViewerAndSkipsDisabledCommands()
    {
        var bar = Create();
        var first = new AppBarButton { Label = "First" };
        var last = new AppBarButton { Label = "Last" };
        bar.SecondaryCommands.Add(first);
        bar.SecondaryCommands.Add(new AppBarSeparator());
        bar.SecondaryCommands.Add(new AppBarButton { Label = "Disabled", IsEnabled = false });
        bar.SecondaryCommands.Add(last);
        var window = new DisplayedWindow { Content = bar };
        try
        {
            Layout(window);
            Part<Popup>(bar, "PART_OverflowPopup").ShouldConstrainToRootBounds = true;
            bar.IsOpen = true;
            Layout(window);
            Pump(bar);
            Assert.Same(first, Keyboard.FocusedElement);
            var surface = Part<ContentControl>(bar, "PART_OverflowBorder");
            Assert.InRange(first.ActualWidth, surface.ActualWidth - 11, surface.ActualWidth - 9);
            Assert.Equal(HorizontalAlignment.Left, Part<StackPanel>(first, "PART_ContentPanel").HorizontalAlignment);
            Key(first, Jalium.UI.Input.Key.Down);
            Assert.Same(last, Keyboard.FocusedElement);
            Key(last, Jalium.UI.Input.Key.Down);
            Assert.Same(first, Keyboard.FocusedElement);
            Key(first, Jalium.UI.Input.Key.End);
            Assert.Same(last, Keyboard.FocusedElement);
            Key(last, Jalium.UI.Input.Key.Home);
            Assert.Same(first, Keyboard.FocusedElement);
        }
        finally { bar.IsOpen = false; window.Close(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IconOnlyAppBarCommandsExposeLabelsAndRetainTheirStandardPatterns(bool toggle)
    {
        Control command = toggle
            ? new AppBarToggleButton { Label = "Save", IsCompact = true }
            : new AppBarButton { Label = "Save", IsCompact = true };
        var peer = Jalium.UI.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(command)!;
        Assert.Equal("Save", peer.GetName());
        Assert.NotNull(peer.GetPattern(toggle ? Jalium.UI.Automation.Peers.PatternInterface.Toggle : Jalium.UI.Automation.Peers.PatternInterface.Invoke));
        Jalium.UI.Automation.AutomationProperties.SetName(command, "Store document");
        Assert.Equal("Store document", peer.GetName());
        command.ClearValue(Jalium.UI.Automation.AutomationProperties.NameProperty);
        if (toggle) ((AppBarToggleButton)command).Label = "Save changes";
        else ((AppBarButton)command).Label = "Save changes";
        Assert.Equal("Save changes", peer.GetName());
    }

    [Fact]
    public void AppBarDefaultTemplatesRemainStyleValuesAndRecoverAfterConsumerStyles()
    {
        var button = new AppBarButton { Label = "Save" };
        button.Resources.MergedDictionaries.Add(Defaults.Value);
        Layout(button);
        Assert.False(button.HasLocalValue(Control.TemplateProperty));
        var original = button.Template;
        var custom = new ControlTemplate(typeof(AppBarButton));
        custom.SetVisualTree(() => new Border());
        var style = new Style(typeof(AppBarButton));
        style.Setters.Add(new Setter(Control.TemplateProperty, custom));
        button.Style = style;
        Layout(button);
        Assert.Same(custom, button.Template);
        button.Style = null;
        Layout(button);
        Assert.Same(original, button.Template);
        Assert.False(button.HasLocalValue(Control.TemplateProperty));
    }

    [Fact]
    public void AnOpeningHandlerCanReverseTheRequestWithoutAStaleOpenedEvent()
    {
        var bar = Create();
        bar.SecondaryCommands.Add(new AppBarButton { Label = "Settings" });
        int opened = 0;
        bar.Opening += (_, _) => bar.IsOpen = false;
        bar.Opened += (_, _) => opened++;
        bar.IsOpen = true;
        Assert.False(bar.IsOpen);
        Assert.Equal(0, opened);
        Assert.Equal("Closed", VisualStateManager.GetCurrentStateName(bar, "OpenStates"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AttachedSettersKeepTheirOwnerWhenTheTargetHasASameNamedProperty(bool inTrigger)
    {
        var element = new CollisionControl();
        var style = new Style(typeof(CollisionControl));
        if (inTrigger)
        {
            var trigger = new Trigger { Property = UIElement.IsEnabledProperty, Value = true };
            trigger.Setters.Add(new Setter(AttachedOwner.ValueProperty, 42));
            style.Triggers.Add(trigger);
        }
        else style.Setters.Add(new Setter(AttachedOwner.ValueProperty, 42));
        element.Style = style;
        Layout(element);
        Assert.Equal(42, element.GetValue(AttachedOwner.ValueProperty));
        Assert.Equal(0, element.GetValue(CollisionControl.ValueProperty));
        element.Style = null;
        Assert.Equal(0, element.GetValue(AttachedOwner.ValueProperty));
    }

    [Fact]
    public void AnAncestorBindingRetargetsOnReuseAndUsesItsFallbackWhenTheAncestorIsMissing()
    {
        var first = new Grid { Width = 150 };
        var second = new Grid { Width = 250 };
        var child = new Border();
        child.SetBinding(FrameworkElement.WidthProperty, new Binding(nameof(FrameworkElement.Width))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor) { AncestorType = typeof(Grid) },
            FallbackValue = 37.0,
        });
        Assert.Equal(37, child.Width);
        first.Children.Add(child);
        Assert.Equal(150, child.Width);
        first.Children.Remove(child);
        second.Children.Add(child);
        Assert.Equal(250, child.Width);
        first.Width = 300;
        Assert.Equal(250, child.Width);
        second.Children.Remove(child);
        new StackPanel().Children.Add(child);
        Assert.Equal(37, child.Width);
        second.Width = 400;
        Assert.Equal(37, child.Width);
    }

    private static CommandBar Create()
    {
        var bar = new CommandBar();
        bar.Resources.MergedDictionaries.Add(Defaults.Value);
        Layout(bar);
        return bar;
    }
    private static T Part<T>(FrameworkElement owner, string name) where T : class => Assert.IsAssignableFrom<T>(owner.FindName(name));
    private static void Layout(FrameworkElement control)
    {
        control.Measure(new Size(600, 300));
        control.Arrange(new Rect(0, 0, 600, 300));
        control.UpdateLayout();
    }
    private static void Click(ButtonBase button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
    private static void Key(UIElement target, Key key)
    {
        var source = new Source { RootVisual = target };
        var preview = new KeyEventArgs(Keyboard.PrimaryDevice, source, 1, key)
            { RoutedEvent = UIElement.PreviewKeyDownEvent, Source = target };
        target.RaiseEvent(preview);
        if (!preview.Handled)
            target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 1, key)
                { RoutedEvent = UIElement.KeyDownEvent, Source = target });
    }
    private static void Pump(DependencyObject owner) => typeof(Jalium.UI.Threading.Dispatcher)
        .GetMethod("ProcessQueue", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner.Dispatcher, null);
    private sealed class Source : PresentationSource
    {
        public override Visual? RootVisual { get; set; }
        public override bool IsDisposed => false;
        protected override CompositionTarget? GetCompositionTargetCore() => null;
    }
    private sealed class DisplayedWindow : Window
    {
        public DisplayedWindow()
        {
            if (OperatingSystem.IsMacOS()) typeof(Window).GetMethod("SetMacOSVisibilityForDisplay", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(this, new object[] { Visibility.Visible });
        }
    }
    private sealed class Model : INotifyPropertyChanged
    {
        private bool _open;
        public bool Open { get => _open; set { _open = value; PropertyChanged?.Invoke(this, new(nameof(Open))); } }
        public bool Compact { get; set; }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
    private sealed class CollisionControl : Control
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register("Value", typeof(int), typeof(CollisionControl), new PropertyMetadata(0));
    }
    private static class AttachedOwner
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.RegisterAttached("Value", typeof(int), typeof(AttachedOwner), new PropertyMetadata(0));
    }
}

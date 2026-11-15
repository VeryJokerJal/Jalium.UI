using System.ComponentModel;
using System.Reflection;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;
[Collection("macOS Window globals")]
public sealed class MacOSMenuFlyoutItemTemplateTests
{
    [Fact]
    public void AncestorTypographyUpdatesTheTemplateBeforeMeasurement()
    {
        var item = Create(new MenuFlyoutItem { Text = "Inherited", KeyboardAcceleratorTextOverride = "Ctrl+O" });
        var host = new Grid();
        host.Children.Add(item);
        host.SetValue(Control.FontSizeProperty, 28.0);
        host.SetValue(Control.FontWeightProperty, FontWeights.Bold);
        host.SetValue(Control.FontFamilyProperty, new FontFamily(FrameworkElement.DefaultFontFamilyName));
        Layout(host, 60);
        var text = Assert.IsType<TextBlock>(item.FindName("PART_Text"));
        Assert.Equal(28, text.FontSize);
        Assert.Equal(24, Assert.IsType<TextBlock>(item.FindName("PART_Accelerator")).FontSize);
        Assert.Equal(FontWeights.Bold, text.FontWeight);
        Assert.Equal(item.FontFamily.Source, text.FontFamily.Source);
    }

    [Fact]
    public void ForegroundChangesInvalidateCachedVisualIconContent()
    {
        var item = Create(new MenuFlyoutItem { Icon = new FontIcon { Glyph = "A" } });
        var presenter = Assert.IsType<MenuIconPresenter>(item.FindName("PART_Icon"));
        var visual = Assert.IsType<FontIcon>(presenter.VisualIcon);
        visual.ClearRenderDirty();
        presenter.Foreground = Brushes.Orange;
        Assert.True(visual.IsRenderDirty);
        Assert.Same(visual, presenter.VisualIcon);
    }

    [Fact]
    public void DefaultTemplateProvidesRealLabelAcceleratorIconAndStates()
    {
        var item = Create(new MenuFlyoutItem { Text = "Open", KeyboardAcceleratorTextOverride = "Ctrl+O", Icon = "X" });
        Assert.Equal("Open", Assert.IsType<TextBlock>(item.FindName("PART_Text")).Text);
        var accelerator = Assert.IsType<TextBlock>(item.FindName("PART_Accelerator"));
        Assert.Equal("Ctrl+O", accelerator.Text);
        Assert.Equal(Visibility.Visible, accelerator.Visibility);
        Assert.IsType<MenuIconPresenter>(item.FindName("PART_Icon"));
        Assert.False(item.HasSubmenu);
        Assert.Equal("Normal", VisualStateManager.GetCurrentStateName(item, "CommonStates"));
        item.IsEnabled = false;
        Assert.Equal("Disabled", VisualStateManager.GetCurrentStateName(item, "CommonStates"));
        item.KeyboardAcceleratorTextOverride = "";
        Assert.False(item.HasAccelerator);
        Assert.Equal(Visibility.Collapsed, accelerator.Visibility);
    }

    [Fact]
    public void ToggleUsesTemplatePathAndKeepsItsBindingAcrossInvocationAndRetemplate()
    {
        var source = new CheckSource();
        var item = new ToggleMenuFlyoutItem
        {
            Text = "Toolbar"
        };
        BindingOperations.SetBinding(item, ToggleMenuFlyoutItem.IsCheckedProperty, new Binding(nameof(CheckSource.Value)) { Source = source, Mode = BindingMode.TwoWay });
        Create(item);
        var original = item.Template;
        ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(item)).Invoke();
        Assert.True(source.Value);
        Assert.True(item.IsItemChecked);
        Assert.Equal(Visibility.Visible, Assert.IsType<Jalium.UI.Shapes.Path>(item.FindName("PART_Check")).Visibility);
        Assert.Equal("Checked", VisualStateManager.GetCurrentStateName(item, "CheckStates"));
        var blank = new ControlTemplate(typeof(MenuFlyoutItem));
        blank.SetVisualTree(() => new Border());
        item.Template = blank;
        item.ApplyTemplate();
        item.Template = original;
        item.ApplyTemplate();
        Layout(item);
        Assert.True(item.IsItemChecked);
        source.Value = false;
        Assert.False(item.IsItemChecked);
        Assert.NotNull(BindingOperations.GetBindingExpression(item, ToggleMenuFlyoutItem.IsCheckedProperty));
    }

    [Fact]
    public void CallerForegroundAndBackgroundRemainEffectiveInDisabledState()
    {
        var item = Create(new MenuFlyoutItem { Text = "Authored", Foreground = Brushes.Navy, Background = Brushes.Orange });
        item.IsEnabled = false;
        Assert.Same(Brushes.Navy, Assert.IsType<TextBlock>(item.FindName("PART_Text")).Foreground);
        Assert.Same(Brushes.Orange, Assert.IsType<Border>(item.FindName("PART_Root")).Background);
        var styled = new Style(typeof(MenuFlyoutItem))
        {
            BasedOn = (Style)MenuFlyoutTemplateTestHelper.Themes.Value[typeof(MenuFlyoutItem)]
        };
        styled.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.DarkBlue));
        item.ClearValue(Control.ForegroundProperty);
        item.Style = styled;
        Assert.Same(Brushes.DarkBlue, Assert.IsType<TextBlock>(item.FindName("PART_Text")).Foreground);
    }

    [Fact]
    public void SubmenuArrowAndHighlightFollowOpenStateWithoutManualDrawing()
    {
        var item = Create(new MenuFlyoutSubItem { Text = "Nested" });
        item.Items.Add(new MenuFlyoutItem { Text = "Action" });
        Assert.True(item.HasSubmenu);
        Assert.Equal(Visibility.Visible, Assert.IsType<Jalium.UI.Shapes.Path>(item.FindName("PART_Arrow")).Visibility);
        item.ShowSubMenu();
        Assert.True(item.IsItemHighlighted);
        Assert.True(item.ShowHighlight);
        Assert.Equal("SubmenuOpen", VisualStateManager.GetCurrentStateName(item, "CommonStates"));
        item.HideSubMenu();
        Assert.False(item.IsItemHighlighted);
    }

    [Fact]
    public void FontsAndTinyLayoutStayFiniteAndMissingPartsKeepBehavior()
    {
        var item = Create(new MenuFlyoutItem { Text = "Open", KeyboardAcceleratorTextOverride = "Ctrl+O", MinHeight = 0 });
        item.FontSize = 0;
        Layout(item, 12);
        Assert.Equal(0, Assert.IsType<TextBlock>(item.FindName("PART_Text")).FontSize);
        Assert.Equal(0, Assert.IsType<TextBlock>(item.FindName("PART_Accelerator")).FontSize);
        item.FontSize = 28;
        Layout(item, 60);
        Assert.Equal(28, Assert.IsType<TextBlock>(item.FindName("PART_Text")).FontSize);
        Assert.Equal(24, item.AcceleratorFontSize);
        int clicks = 0;
        item.Click += (_, _) => clicks++;
        var blank = new ControlTemplate(typeof(MenuFlyoutItem));
        blank.SetVisualTree(() => new Border());
        item.Template = blank;
        item.ApplyTemplate();
        ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(item)).Invoke();
        Assert.Equal(1, clicks);
        Layout(item, 1);
        Assert.True(double.IsFinite(item.DesiredSize.Height));
    }

    [Fact]
    public void IconSourceChangesAndUnloadReleaseRetiredVisuals()
    {
        var source = new FontIconSource
        {
            Glyph = "A"
        };
        var item = Create(new MenuFlyoutItem { Icon = source });
        var presenter = Assert.IsType<MenuIconPresenter>(item.FindName("PART_Icon"));
        var first = Assert.IsType<FontIcon>(presenter.VisualIcon);
        source.Glyph = "B";
        Assert.Equal("B", Assert.IsType<FontIcon>(presenter.VisualIcon).Glyph);
        Assert.NotSame(first, presenter.VisualIcon);
        typeof(FrameworkElement).GetMethod("SetLoadedState", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(item, new object[] { true });
        typeof(FrameworkElement).GetMethod("SetLoadedState", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(item, new object[] { false });
        Assert.Null(presenter.VisualIcon);
        source.Glyph = "C";
        Assert.Null(presenter.VisualIcon);
        typeof(FrameworkElement).GetMethod("SetLoadedState", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(item, new object[] { true });
        Assert.Equal("C", Assert.IsType<FontIcon>(presenter.VisualIcon).Glyph);
    }

    [Fact]
    public void SeparatorIsThemedAndRemainsNonInteractive()
    {
        var separator = new MenuFlyoutSeparator
        {
            Foreground = Brushes.Navy
        };
        MenuFlyoutTemplateTestHelper.Apply(separator);
        Layout(separator, 20);
        Assert.False(separator.Focusable);
        Assert.False(separator.IsHitTestVisible);
        var line = Assert.IsType<Border>(separator.FindName("PART_Line"));
        Assert.Equal(1, line.Height);
        Assert.Same(Brushes.Navy, line.Background);
        Assert.Null(typeof(MenuFlyoutSeparator).GetMethod("OnRender", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
    }

    private static T Create<T>(T item)
        where T : MenuFlyoutItem
    {
        MenuFlyoutTemplateTestHelper.Apply(item);
        Layout(item);
        return item;
    }

    private static void Layout(UIElement item, double height = 40)
    {
        for (int i = 0; i < 3; i++)
        {
            item.Measure(new Size(280, height));
            item.Arrange(new Rect(0, 0, 280, height));
            item.UpdateLayout();
        }
    }

    private sealed class CheckSource : INotifyPropertyChanged
    {
        private bool _value;
        public bool Value
        {
            get => _value;
            set
            {
                _value = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}

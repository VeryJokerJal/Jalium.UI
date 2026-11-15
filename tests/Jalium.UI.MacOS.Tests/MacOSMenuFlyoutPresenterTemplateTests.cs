using System.ComponentModel;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Data;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;
[Collection("macOS Window globals")]
public sealed class MacOSMenuFlyoutPresenterTemplateTests
{
    [Fact]
    public void PresenterStyleDependencyPropertyUpdatesAnExistingPresenterAndKeepsItsBinding()
    {
        _ = Presenter(new MenuFlyout());
        Style WithPadding(double value)
        {
            var style = new Style(typeof(MenuFlyoutPresenter))
            {
                BasedOn = (Style)Defaults.Value[typeof(MenuFlyoutPresenter)]
            };
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(value)));
            return style;
        }

        var source = new StyleSource
        {
            Value = WithPadding(2)
        };
        var flyout = new MenuFlyout();
        BindingOperations.SetBinding(flyout, MenuFlyout.MenuFlyoutPresenterStyleProperty, new Binding(nameof(StyleSource.Value)) { Source = source });
        var presenter = Assert.IsType<MenuFlyoutPresenter>(typeof(MenuFlyout).GetMethod("CreatePresenter", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(flyout, null));
        Layout(presenter);
        Assert.Equal(new Thickness(2), presenter.Padding);
        source.Value = WithPadding(10);
        Layout(presenter);
        Assert.Equal(new Thickness(10), Assert.IsType<Border>(presenter.FindName("PART_PopupBorder")).Padding);
        Assert.NotNull(BindingOperations.GetBindingExpression(flyout, MenuFlyout.MenuFlyoutPresenterStyleProperty));
        source.Value = null;
        Layout(presenter);
        Assert.Equal(new Thickness(4), presenter.Padding);
    }

    [Fact]
    public void DefaultTemplateOwnsChromeAndOriginalContainers()
    {
        var(flyout, presenter) = Create();
        var border = Assert.IsType<Border>(presenter.FindName("PART_PopupBorder"));
        var host = Host(presenter);
        Assert.Same(host, border.Child);
        Assert.Equal(new Thickness(4), border.Padding);
        Assert.Equal(new Thickness(1), border.BorderThickness);
        Assert.Equal(new CornerRadius(8), border.CornerRadius);
        Assert.Equal(16, host.Items.Count);
        Assert.Same(flyout.Items[0], host.Items[0]);
        Assert.True(host.IsOverflowing);
        Assert.Equal("Normal", VisualStateManager.GetCurrentStateName(presenter, "CommonStates"));
        presenter.IsEnabled = false;
        Assert.Equal("Disabled", VisualStateManager.GetCurrentStateName(presenter, "CommonStates"));
        Assert.Equal(.55, border.Opacity);
    }

    [Fact]
    public void ExplicitZeroChromeAndCallerBindingsRemainEffective()
    {
        var(_, presenter) = Create();
        var source = new PaddingSource
        {
            Value = new Thickness(9)
        };
        BindingOperations.SetBinding(presenter, Control.PaddingProperty, new Binding(nameof(PaddingSource.Value)) { Source = source });
        presenter.BorderThickness = new Thickness(0);
        presenter.CornerRadius = new CornerRadius(0);
        presenter.Background = Brushes.Navy;
        Layout(presenter);
        var border = Assert.IsType<Border>(presenter.FindName("PART_PopupBorder"));
        Assert.Equal(new Thickness(9), border.Padding);
        source.Value = new Thickness(0);
        Layout(presenter);
        Assert.Equal(new Thickness(0), border.Padding);
        Assert.Equal(new Thickness(0), border.BorderThickness);
        Assert.Equal(new CornerRadius(0), border.CornerRadius);
        Assert.Same(Brushes.Navy, border.Background);
        Assert.NotNull(BindingOperations.GetBindingExpression(presenter, Control.PaddingProperty));
        Assert.Equal(new Thickness(0), presenter.GetChrome().Padding);
    }

    [Fact]
    public void RetemplateRestoresScrollAndRetiresOldHost()
    {
        var(flyout, presenter) = Create();
        var old = Host(presenter);
        var down = Assert.IsType<RepeatButton>(old.FindName("PART_ScrollDownButton"));
        down.PerformClick();
        Layout(presenter);
        double offset = old.VerticalOffset;
        Assert.True(offset > 0);
        var original = presenter.Template;
        presenter.Template = Blank();
        presenter.ApplyTemplate();
        presenter.Template = original;
        presenter.ApplyTemplate();
        Layout(presenter);
        var current = Host(presenter);
        Assert.NotSame(old, current);
        Assert.Empty(old.Items);
        Assert.Equal(offset, current.VerticalOffset, 5);
        down.PerformClick();
        old.Measure(new Size(220, 120));
        Layout(presenter);
        Assert.Equal(offset, current.VerticalOffset, 5);
        Assert.Same(current.ItemsHostPanel, flyout.Items[0].VisualParent);
    }

    [Fact]
    public void UnloadedPresenterStopsObservingUntilLoadedAgain()
    {
        var(flyout, presenter) = Create();
        SetLoaded(presenter, true);
        SetLoaded(presenter, false);
        var item = flyout.Items[0];
        var otherFlyout = new MenuFlyout();
        otherFlyout.Items.Add(item);
        var other = Presenter(otherFlyout);
        var owner = item.VisualParent;
        flyout.Items.Add(new MenuFlyoutItem { Text = "Deferred", Height = 32 });
        Layout(presenter);
        Assert.Same(owner, item.VisualParent);
        Assert.Equal(16, Host(presenter).Items.Count);
        SetLoaded(presenter, true);
        Layout(presenter);
        Assert.Equal(17, Host(presenter).Items.Count);
        Assert.Same(Host(presenter).ItemsHostPanel, item.VisualParent);
        GC.KeepAlive(other);
    }

    [Fact]
    public void ObservableChangesAndMissingPartsKeepTheSourceCollection()
    {
        var(flyout, presenter) = Create();
        var item = flyout.Items[4];
        flyout.Items.RemoveAt(0);
        ((System.Collections.ObjectModel.ObservableCollection<Control>)flyout.Items).Move(3, 0);
        Layout(presenter);
        Assert.Same(item, Host(presenter).Items[0]);
        var original = presenter.Template;
        presenter.Template = Blank();
        presenter.ApplyTemplate();
        flyout.Items.Add(new MenuFlyoutItem { Text = "Without parts" });
        Layout(presenter);
        Assert.Equal(16, flyout.Items.Count);
        presenter.Template = original;
        presenter.ApplyTemplate();
        Layout(presenter);
        Assert.Equal(flyout.Items.Cast<UIElement>(), Host(presenter).Items);
    }

    [Fact]
    public void NestedChromeFindsPresenterThroughCustomTemplateWrappers()
    {
        var flyout = new MenuFlyout();
        var nested = new MenuFlyoutSubItem
        {
            Text = "Nested"
        };
        nested.Items.Add(new MenuFlyoutItem { Text = "Action" });
        flyout.Items.Add(nested);
        var presenter = Presenter(flyout);
        presenter.Padding = new Thickness(7);
        presenter.CornerRadius = new CornerRadius(11);
        presenter.Background = Brushes.Navy;
        var template = new ControlTemplate(typeof(MenuFlyoutPresenter));
        template.SetVisualTree(() => new Grid { Children = { new Grid { Children = { new MenuPopupScrollHost { Name = "PART_ScrollHost" } } } } });
        presenter.Template = template;
        Layout(presenter);
        nested.ShowSubMenu();
        var frame = Assert.IsType<Border>(nested.SubMenuFrame);
        Assert.Equal(new Thickness(7), frame.Padding);
        Assert.Equal(new CornerRadius(11), frame.CornerRadius);
        Assert.Same(Brushes.Navy, frame.Background);
        nested.HideSubMenu();
    }

    [Fact]
    public void NegativePaddingAndTinyViewportKeepFiniteGeometry()
    {
        var(_, presenter) = Create();
        presenter.Padding = new Thickness(-30);
        Layout(presenter, 12);
        Assert.True(double.IsFinite(presenter.DesiredSize.Width));
        Assert.True(double.IsFinite(presenter.DesiredSize.Height));
        Assert.True(presenter.DesiredSize.Width >= 0 && presenter.DesiredSize.Height >= 0);
    }

    private static readonly Lazy<ResourceDictionary> Defaults = new(() =>
    {
        ThemeLoader.Initialize();
        using var stream = new MemoryStream();
        return ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
    });
    private static (MenuFlyout, MenuFlyoutPresenter) Create()
    {
        var flyout = new MenuFlyout();
        for (int index = 0; index < 16; index++)
            flyout.Items.Add(new MenuFlyoutItem { Text = $"Item {index}", Height = 32 });
        return (flyout, Presenter(flyout));
    }

    private static MenuFlyoutPresenter Presenter(MenuFlyout flyout)
    {
        var scoped = new ResourceDictionary();
        foreach (var type in new[]
        {
            typeof(MenuFlyoutPresenter),
            typeof(MenuPopupScrollHost),
            typeof(RepeatButton),
            typeof(MenuFlyoutItem)
        }

        )
            if (Defaults.Value.TryGetValue(type, out var style))
                scoped[type] = style;
        ThemeManager.RegisterControlThemes(scoped);
        var presenter = new MenuFlyoutPresenter(flyout)
        {
            Style = (Style)Defaults.Value[typeof(MenuFlyoutPresenter)]
        };
        presenter.Resources["ScrollBarLineButtonStyle"] = Defaults.Value["ScrollBarLineButtonStyle"];
        Layout(presenter);
        return presenter;
    }

    private static ControlTemplate Blank()
    {
        var t = new ControlTemplate(typeof(MenuFlyoutPresenter));
        t.SetVisualTree(() => new Border());
        return t;
    }

    private static MenuPopupScrollHost Host(MenuFlyoutPresenter presenter) => Assert.IsType<MenuPopupScrollHost>(presenter.FindName("PART_ScrollHost"));
    private static void SetLoaded(FrameworkElement host, bool value) => typeof(FrameworkElement).GetMethod("SetLoadedState", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, new object[] { value });
    private static void Layout(UIElement element, double height = 140)
    {
        for (int pass = 0; pass < 4; pass++)
        {
            element.Measure(new Size(240, height));
            element.Arrange(new Rect(0, 0, 240, height));
            element.UpdateLayout();
        }
    }

    private sealed class PaddingSource : INotifyPropertyChanged
    {
        private Thickness _value;
        public Thickness Value
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

    private sealed class StyleSource : INotifyPropertyChanged
    {
        private Style? _value;
        public Style? Value
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

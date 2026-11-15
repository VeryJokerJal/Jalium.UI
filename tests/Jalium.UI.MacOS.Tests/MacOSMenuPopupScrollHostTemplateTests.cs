using System.ComponentModel;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Data;
using Jalium.UI.Input;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;
[Collection("macOS Window globals")]
public sealed class MacOSMenuPopupScrollHostTemplateTests
{
    [Fact]
    public void DefaultPartsOwnOverflowAndKeepOriginalLogicalParents()
    {
        var host = Create();
        var item = new Button
        {
            Content = "Owned",
            Height = 32
        };
        var owner = new StackPanel
        {
            Children =
            {
                item
            }
        };
        host.Items[0] = item;
        Layout(host);
        Assert.Same(owner, item.Parent);
        Assert.Same(host.ItemsHostPanel, item.VisualParent);
        Assert.True(host.IsOverflowing);
        Assert.False(host.CanScrollUp);
        Assert.True(host.CanScrollDown);
        Assert.Equal(18, host.EffectiveScrollButtonHeight);
        Assert.Equal(84, Viewer(host).ViewportHeight, 1);
        Assert.Equal("AtStart", VisualStateManager.GetCurrentStateName(host, "ScrollStates"));
        Assert.Equal(16, host.ItemsHostPanel!.Children.Count);
    }

    [Fact]
    public void RetiredPartsCannotScrollCurrentTemplateAndOffsetReturns()
    {
        var host = Create();
        var oldViewer = Viewer(host);
        var oldButton = Down(host);
        var original = host.Template;
        oldButton.PerformClick();
        Layout(host);
        var offset = host.VerticalOffset;
        Assert.True(offset > 0);
        var blank = new ControlTemplate(typeof(MenuPopupScrollHost));
        blank.SetVisualTree(() => new Border());
        host.Template = blank;
        host.ApplyTemplate();
        host.Template = original;
        host.ApplyTemplate();
        Layout(host);
        Assert.NotSame(oldViewer, Viewer(host));
        Assert.Equal(offset, host.VerticalOffset, 5);
        oldButton.PerformClick();
        oldViewer.ScrollToVerticalOffset(200);
        Layout(host);
        Assert.Equal(offset, host.VerticalOffset, 5);
        Down(host).PerformClick();
        Layout(host);
        Assert.True(host.VerticalOffset > offset);
    }

    [Fact]
    public void SuspendedHostAndOldMeasurementsCannotStealContainers()
    {
        var host = Create();
        var shared = host.Items[0];
        SetLoaded(host, true);
        SetLoaded(host, false);
        var other = Create(0);
        other.Items.Add(shared);
        Layout(other);
        var current = shared.VisualParent;
        host.Items.Add(new Button { Content = "Deferred", Height = 32 });
        host.InvalidateMeasure();
        Layout(host);
        Assert.Same(current, shared.VisualParent);
        Assert.False(host.IsOverflowing);
        var ignored = Wheel(-1);
        host.RaiseEvent(ignored);
        Assert.False(ignored.Handled);
        SetLoaded(host, true);
        Layout(host);
        Assert.Same(host.ItemsHostPanel, shared.VisualParent);
        Assert.Equal(17, host.ItemsHostPanel!.Children.Count);
    }

    [Fact]
    public void IncrementalAndBatchedItemsPreserveUnchangedContainers()
    {
        var host = Create();
        var kept = host.Items[3];
        SetLoaded(host, true);
        int unloaded = 0;
        ((FrameworkElement)kept).Unloaded += (_, _) => unloaded++;
        host.Items.RemoveAt(0);
        host.Items.Add(new Button { Content = "New", Height = 32 });
        Layout(host);
        Assert.Equal(0, unloaded);
        Assert.Same(host.ItemsHostPanel, kept.VisualParent);
        host.Items.BeginBatchUpdate();
        host.Items.Add(new Button { Content = "Batch", Height = 32 });
        host.Items.Move(0, 1);
        host.Items.EndBatchUpdate();
        Layout(host);
        Assert.Same(kept, host.Items[2]);
        Assert.Same(host.ItemsHostPanel, kept.VisualParent);
        Assert.Throws<ArgumentException>(() => host.Items.Add(kept));
        Assert.Throws<InvalidOperationException>(() => host.Items.EndBatchUpdate());
    }

    [Fact]
    public void TinyAndGrowingViewportsRespectButtonHeightBinding()
    {
        var host = Create();
        var height = new HeightSource
        {
            Value = 24
        };
        BindingOperations.SetBinding(host, MenuPopupScrollHost.ScrollButtonHeightProperty, new Binding(nameof(HeightSource.Value)) { Source = height });
        Layout(host);
        Assert.Equal(24, host.EffectiveScrollButtonHeight);
        Layout(host, 12);
        Assert.InRange(host.EffectiveScrollButtonHeight, 0, 6);
        Assert.InRange(Down(host).ActualHeight, 0, 6.1);
        Assert.True(Viewer(host).ActualHeight >= 0);
        height.Value = 20;
        Layout(host, 800);
        Assert.False(host.IsOverflowing);
        Assert.Equal(0, host.EffectiveScrollButtonHeight);
        Assert.Equal(20, host.ScrollButtonHeight);
        Assert.NotNull(BindingOperations.GetBindingExpression(host, MenuPopupScrollHost.ScrollButtonHeightProperty));
        Assert.Throws<ArgumentException>(() => host.ScrollButtonHeight = double.NaN);
    }

    [Fact]
    public void MissingPartsRetainItemsAndDisabledHostsIgnoreWheel()
    {
        var host = Create();
        var original = host.Template;
        host.IsEnabled = false;
        var blocked = Wheel(-1);
        host.RaiseEvent(blocked);
        Assert.False(blocked.Handled);
        var blank = new ControlTemplate(typeof(MenuPopupScrollHost));
        blank.SetVisualTree(() => new Border());
        host.Template = blank;
        host.ApplyTemplate();
        Layout(host);
        Assert.Equal(16, host.Items.Count);
        Assert.False(host.IsOverflowing);
        host.IsEnabled = true;
        host.Template = original;
        host.ApplyTemplate();
        Layout(host);
        Assert.True(host.IsOverflowing);
    }

    [Fact]
    public void NaturalPopupSizeProbesKeepTheFiniteViewportOffset()
    {
        var host = Create();
        Down(host).PerformClick();
        Layout(host);
        var offset = host.VerticalOffset;
        Assert.True(offset > 0);
        for (int index = 0; index < 3; index++)
        {
            host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Layout(host);
            Assert.Equal(offset, host.VerticalOffset, 5);
            Assert.Equal(84, Viewer(host).ViewportHeight, 1);
            Assert.True(host.CanScrollUp);
        }

        Layout(host, 1000);
        Assert.False(host.IsOverflowing);
        Assert.Equal(0, host.VerticalOffset);
    }

    private static readonly Lazy<ResourceDictionary> Defaults = new(() =>
    {
        ThemeLoader.Initialize();
        using var stream = new MemoryStream();
        return ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
    });
    private static MenuPopupScrollHost Create(int count = 16)
    {
        var scoped = new ResourceDictionary();
        foreach (var type in new[]
        {
            typeof(MenuPopupScrollHost),
            typeof(Button),
            typeof(RepeatButton),
            typeof(MenuFlyoutItem)
        }

        )
            if (Defaults.Value.TryGetValue(type, out var style))
                scoped[type] = style;
        ThemeManager.RegisterControlThemes(scoped);
        var host = new MenuPopupScrollHost
        {
            Style = (Style)Defaults.Value[typeof(MenuPopupScrollHost)]
        };
        host.Resources["ScrollBarLineButtonStyle"] = Defaults.Value["ScrollBarLineButtonStyle"];
        host.Items.BeginBatchUpdate();
        for (int index = 0; index < count; index++)
            host.Items.Add(new Button { Content = index.ToString(), Height = 32, MinHeight = 0 });
        host.Items.EndBatchUpdate();
        Layout(host);
        return host;
    }

    private static ScrollViewer Viewer(MenuPopupScrollHost host) => Assert.IsAssignableFrom<ScrollViewer>(host.FindName("PART_ScrollViewer"));
    private static RepeatButton Down(MenuPopupScrollHost host) => Assert.IsType<RepeatButton>(host.FindName("PART_ScrollDownButton"));
    private static void Layout(UIElement host, double height = 120)
    {
        for (int pass = 0; pass < 4; pass++)
        {
            host.Measure(new Size(220, height));
            host.Arrange(new Rect(0, 0, 220, height));
            host.UpdateLayout();
        }
    }

    private static void SetLoaded(FrameworkElement host, bool loaded) => typeof(FrameworkElement).GetMethod("SetLoadedState", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, new object[] { loaded });
    private static MouseWheelEventArgs Wheel(double vertical) => new(UIElement.MouseWheelEvent, new Point(4, 4), 0, vertical, true, MouseButtonState.Released, MouseButtonState.Released, MouseButtonState.Released, MouseButtonState.Released, MouseButtonState.Released, ModifierKeys.None, 1);
    private sealed class HeightSource : INotifyPropertyChanged
    {
        private double _value;
        public double Value
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

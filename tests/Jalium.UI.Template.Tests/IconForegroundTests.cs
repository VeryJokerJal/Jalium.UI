using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public sealed class IconForegroundTests
{
    [Fact]
    public void MutatingAnInheritedForegroundInvalidatesTheDrawingIcon()
    {
        var brush = new SolidColorBrush(Color.FromRgb(16, 24, 39));
        var icon = new ProbeIcon();
        var host = new ProbeHost(icon) { Foreground = brush };
        Assert.Same(brush, icon.Resolve());
        var dirty = typeof(Visual).GetField("_isRenderDirty", BindingFlags.Instance | BindingFlags.NonPublic)!;
        dirty.SetValue(icon, false);

        brush.Color = Color.FromRgb(244, 247, 247);

        Assert.True((bool)dirty.GetValue(icon)!);
        GC.KeepAlive(host);
    }

    [Fact]
    public void IconsInheritForegroundAndKeepLocalOverridesWhenTheParentChanges()
    {
        var icon = new ProbeIcon();
        var host = new ProbeHost(icon) { Foreground = Brushes.Red };
        Assert.Same(Brushes.Red, icon.Foreground);
        host.Foreground = Brushes.Blue;
        Assert.Same(Brushes.Blue, icon.Foreground);
        icon.Foreground = Brushes.Green;
        host.Foreground = Brushes.Black;
        Assert.Same(Brushes.Green, icon.Resolve());
        icon.ClearValue(IconElement.ForegroundProperty);
        Assert.Same(Brushes.Black, icon.Foreground);
    }

    [Fact]
    public void AnUnparentedIconResolvesItsScopedThemeForeground()
    {
        var icon = new ProbeIcon();
        icon.Resources["TextPrimary"] = Brushes.Blue;
        Assert.Same(Brushes.Blue, icon.Resolve());
    }

    private sealed class ProbeIcon : IconElement
    {
        public Brush Resolve() => GetEffectiveForeground();
    }

    private sealed class ProbeHost : Control
    {
        private readonly Visual _child;
        public ProbeHost(Visual child) { _child = child; AddVisualChild(child); }
        protected override int VisualChildrenCount => 1;
        protected override Visual? GetVisualChild(int index) => index == 0 ? _child : throw new ArgumentOutOfRangeException(nameof(index));
    }
}

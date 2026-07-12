using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class IconElementThemeTests
{
    private static void ResetApplicationState()
    {
        var currentField = typeof(Application).GetField("_current",
            BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);

        var resetMethod = typeof(ThemeManager).GetMethod("Reset",
            BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);
    }

    [Fact]
    public void IconElement_ShouldResolveThemeForeground_WhenNoLocalOrParentForegroundExists()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var expected = Assert.IsAssignableFrom<Brush>(app.Resources["TextPrimary"]);
            var icon = new TestIconElement();
            var host = new StackPanel { Width = 120, Height = 80 };
            host.Children.Add(icon);

            host.Measure(new Size(120, 80));
            host.Arrange(new Rect(0, 0, 120, 80));

            Assert.Same(expected, icon.ResolveForegroundForTest());
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void IconElement_ShouldPreferParentControlForeground()
    {
        var parentBrush = new SolidColorBrush(Color.FromRgb(12, 34, 56));
        var icon = new TestIconElement();
        var host = new TestIconHost(icon)
        {
            Foreground = parentBrush
        };

        host.Measure(new Size(120, 80));
        host.Arrange(new Rect(0, 0, 120, 80));

        Assert.Same(parentBrush, icon.ResolveForegroundForTest());
    }

    [Fact]
    public void IconElement_ShouldPreferLocalForeground()
    {
        var parentBrush = new SolidColorBrush(Color.FromRgb(12, 34, 56));
        var localBrush = new SolidColorBrush(Color.FromRgb(78, 90, 12));
        var icon = new TestIconElement
        {
            Foreground = localBrush
        };
        var host = new TestIconHost(icon)
        {
            Foreground = parentBrush
        };

        host.Measure(new Size(120, 80));
        host.Arrange(new Rect(0, 0, 120, 80));

        Assert.Same(localBrush, icon.ResolveForegroundForTest());
    }

    private sealed class TestIconElement : IconElement
    {
        internal Brush ResolveForegroundForTest() => GetEffectiveForeground();
    }

    private sealed class TestIconHost : Control
    {
        private readonly TestIconElement _child;

        internal TestIconHost(TestIconElement child)
        {
            _child = child;
            AddVisualChild(_child);
        }

        protected override int VisualChildrenCount => 1;

        protected override Visual? GetVisualChild(int index)
        {
            return index == 0 ? _child : throw new ArgumentOutOfRangeException(nameof(index));
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            _child.Measure(availableSize);
            return _child.DesiredSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            _child.Arrange(new Rect(finalSize));
            return finalSize;
        }
    }
}

using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssPointerEventsTests
{
    [Fact]
    public void GrammarAcceptsOnlyAutoAndNone()
    {
        Assert.True(Css.Supports("pointer-events", "auto"));
        Assert.True(Css.Supports("pointer-events", "none"));
        Assert.False(Css.Supports("pointer-events", "visiblePainted"));
        Assert.False(Css.Supports("pointer-events", "garbage"));
        Assert.False(Css.Supports("pointer-events", "auto none"));
    }

    [Fact]
    public void NoneExcludesTheBoxButAnAutoDescendantCanReceiveInput()
    {
        var back = new Border { Width = 40, Height = 30, Background = Brushes.Blue };
        var child = new Border { Width = 20, Height = 20, Background = Brushes.Red,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var front = new Border { Width = 40, Height = 30, Background = Brushes.Green, Child = child };
        var root = new Grid { Width = 40, Height = 30 };
        root.Children.Add(back);
        root.Children.Add(front);
        Css.SetStyle(front, "pointer-events:none");
        Layout(root);

        Assert.True(front.IsHitTestVisible);
        Assert.Same(back, Hit(root, 10, 10)); // child inherits none
        Assert.Same(back, Hit(root, 30, 10)); // front itself is excluded
        Assert.Same(back, root.InputHitTest(new Point(10, 10)));

        front.IsHitTestVisible = true;
        Assert.Same(child, Hit(root, 10, 10)); // the native override is inherited by the child
        front.ClearValue(UIElement.IsHitTestVisibleProperty);
        Assert.Same(back, Hit(root, 10, 10));

        Css.SetStyle(child, "pointer-events:auto");
        Assert.Same(child, Hit(root, 10, 10));
        Assert.Same(back, Hit(root, 30, 10));

        front.IsHitTestVisible = true;
        Assert.Same(front, Hit(root, 30, 10)); // native local value wins over CSS on this box
        front.ClearValue(UIElement.IsHitTestVisibleProperty);
        Assert.Same(back, Hit(root, 30, 10));

        front.IsHitTestVisible = false;
        Assert.Same(back, Hit(root, 10, 10)); // native local value gates the subtree
        front.ClearValue(UIElement.IsHitTestVisibleProperty);
        Assert.Same(child, Hit(root, 10, 10));
    }

    [Fact]
    public void CssWideKeywordsAndRemovalFollowInheritedValues()
    {
        var parent = new Border { Width = 40, Height = 30 };
        var child = new Border { Width = 20, Height = 20, Background = Brushes.Red };
        parent.Child = child;
        Css.SetStyle(parent, "pointer-events:none");
        Layout(parent);
        Assert.Null(parent.HitTest(new Point(10, 10)));

        Css.SetStyle(child, "pointer-events:initial");
        Assert.Same(child, parent.HitTest(new Point(10, 10))?.VisualHit);
        Css.SetStyle(child, "pointer-events:unset");
        Assert.Null(parent.HitTest(new Point(10, 10)));
        Css.SetStyle(child, "pointer-events:inherit");
        Assert.Null(parent.HitTest(new Point(10, 10)));
        Css.SetStyle(parent, "pointer-events:auto");
        Assert.Same(child, parent.HitTest(new Point(10, 10))?.VisualHit);
        Css.SetStyle(parent, "pointer-events:none");
        Assert.Null(parent.HitTest(new Point(10, 10)));
        Css.SetStyle(child, "pointer-events:auto");
        Assert.Same(child, parent.HitTest(new Point(10, 10))?.VisualHit);
        Css.SetStyle(child, "");
        Assert.Null(parent.HitTest(new Point(10, 10)));

        Css.SetStyle(parent, "pointer-events:auto");
        Assert.Same(child, parent.HitTest(new Point(10, 10))?.VisualHit);
    }

    [Fact]
    public void RoundedBorderDoesNotSynthesizeAHitWhenCssExcludesIt()
    {
        var back = new Border { Width = 40, Height = 30, Background = Brushes.Blue };
        var front = new Border { Width = 40, Height = 30, Background = Brushes.Red };
        var root = new Grid { Width = 40, Height = 30 };
        root.Children.Add(back);
        root.Children.Add(front);
        Css.SetStyle(front, "border-radius:10px;pointer-events:none");
        Layout(root);
        Assert.Same(back, Hit(root, 20, 15));
    }

    [Fact]
    public void WindowMemoRechecksTheSamePointAfterPointerEventsChanges()
    {
        var back = new Border { Width = 40, Height = 30, Background = Brushes.Blue };
        var front = new Border { Width = 40, Height = 30, Background = Brushes.Red };
        var root = new Grid { Width = 40, Height = 30 };
        root.Children.Add(back);
        root.Children.Add(front);
        var window = new Window { TitleBarStyle = WindowTitleBarStyle.Native,
            Width = 40, Height = 30, Content = root };
        Layout(window);
        Assert.Same(front, WindowHit(window, 10, 10));
        Css.SetStyle(front, "pointer-events:none");
        Assert.Same(back, WindowHit(window, 10, 10));
        front.IsHitTestVisible = true;
        Assert.Same(front, WindowHit(window, 10, 10));
        front.ClearValue(UIElement.IsHitTestVisibleProperty);
        Assert.Same(back, WindowHit(window, 10, 10));
        Css.SetStyle(front, "pointer-events:auto");
        Assert.Same(front, WindowHit(window, 10, 10));

        var child = new Border { Width = 20, Height = 20, Background = Brushes.Green };
        front.Child = child;
        Css.SetStyle(front, "pointer-events:none");
        Layout(window);
        Assert.Same(back, WindowHit(window, 10, 10));
        Css.SetStyle(child, "pointer-events:auto");
        Assert.Same(child, WindowHit(window, 10, 10));
        Css.SetStyle(child, "");
        Assert.Same(back, WindowHit(window, 10, 10));
    }

    private static UIElement? Hit(FrameworkElement root, double x, double y)
        => root.HitTest(new Point(x, y))?.VisualHit as UIElement;

    private static UIElement? WindowHit(Window window, double x, double y)
    {
        var method = typeof(Window).GetMethod("HitTestElement", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (UIElement?)method.Invoke(window, [new Point(x, y), "pointer-events-test"]);
    }

    private static void Layout(FrameworkElement element)
    {
        element.Measure(new Size(40, 30));
        element.Arrange(new Rect(0, 0, 40, 30));
    }
}

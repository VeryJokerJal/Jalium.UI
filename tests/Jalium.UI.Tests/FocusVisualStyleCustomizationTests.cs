using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

/// <summary>
/// Customizing the keyboard focus ring through styles: a <c>FocusVisualStyle</c> supplied by
/// a Style setter, an opt-out through <c>{x:Null}</c>, a ring style derived from the theme
/// default that only changes brush/thickness, and a style change while the element is focused
/// must all be reflected by the adorner — never the theme default lingering on.
/// </summary>
[Collection("Application")]
public class FocusVisualStyleCustomizationTests
{
    [Fact]
    public void FocusVisualStyle_FromStyleSetter_IsWhatTheRingUses()
    {
        ResetApplicationState();
        var app = new Application();
        try
        {
            var custom = CreateRingStyle(Brushes.Red, 5);
            var button = new Button { Content = "Go", Width = 80, Height = 32 };
            var style = new Style(typeof(Button));
            style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, custom));
            button.Style = style;

            var window = CreateWindow(button);
            ArrangeWindow(window);

            Assert.Same(custom, button.ResolveFocusVisualStyle());

            SetShowFocusCues(true);
            Assert.True(button.Focus());
            ArrangeWindow(window);

            var ring = GetRing(window, button);
            Assert.Same(custom, ring.Host.Style);
            var border = FindDescendant<Border>(ring.Host);
            Assert.NotNull(border);
            Assert.Same(Brushes.Red, border!.BorderBrush);
            Assert.Equal(new Thickness(5), border.BorderThickness);
        }
        finally
        {
            SetShowFocusCues(false);
            ResetApplicationState();
        }
    }

    [Fact]
    public void FocusVisualStyle_NullFromStyleSetter_SuppressesRing()
    {
        ResetApplicationState();
        var app = new Application();
        try
        {
            var button = new Button { Content = "Go", Width = 80, Height = 32 };
            var style = new Style(typeof(Button));
            style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
            button.Style = style;

            var window = CreateWindow(button);
            ArrangeWindow(window);

            Assert.Null(button.ResolveFocusVisualStyle());

            SetShowFocusCues(true);
            Assert.True(button.Focus());
            ArrangeWindow(window);

            Assert.Null(window.AdornerLayer!.GetAdorners(button));
        }
        finally
        {
            SetShowFocusCues(false);
            ResetApplicationState();
        }
    }

    [Fact]
    public void FocusVisualStyle_BasedOnThemeDefault_BrushAndThicknessSettersRecolorTheRing()
    {
        ResetApplicationState();
        var app = new Application();
        try
        {
            var themeDefault = Assert.IsType<Style>(app.Resources[FrameworkElement.DefaultFocusVisualStyleKey]);
            var derived = new Style(typeof(Control), themeDefault);
            derived.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Red));
            derived.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));

            var button = new Button { Content = "Go", Width = 80, Height = 32, FocusVisualStyle = derived };
            var window = CreateWindow(button);
            ArrangeWindow(window);

            SetShowFocusCues(true);
            Assert.True(button.Focus());
            ArrangeWindow(window);

            var ring = GetRing(window, button);
            var border = FindDescendant<Border>(ring.Host);
            Assert.NotNull(border);
            Assert.Same(Brushes.Red, border!.BorderBrush);
            Assert.Equal(new Thickness(1), border.BorderThickness);
        }
        finally
        {
            SetShowFocusCues(false);
            ResetApplicationState();
        }
    }

    [Fact]
    public void FocusVisualStyle_CornerRadiusSetter_WinsOverTheMirroredControlCornerRadius()
    {
        ResetApplicationState();
        var app = new Application();
        try
        {
            var themeDefault = Assert.IsType<Style>(app.Resources[FrameworkElement.DefaultFocusVisualStyleKey]);
            var derived = new Style(typeof(Control), themeDefault);
            derived.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(12)));

            var button = new Button { Content = "Go", Width = 80, Height = 32, CornerRadius = new CornerRadius(2), FocusVisualStyle = derived };
            var window = CreateWindow(button);
            ArrangeWindow(window);

            SetShowFocusCues(true);
            Assert.True(button.Focus());
            ArrangeWindow(window);

            var ring = GetRing(window, button);
            Assert.Equal(new CornerRadius(12), ring.Host.CornerRadius);
            var border = FindDescendant<Border>(ring.Host);
            Assert.NotNull(border);
            Assert.Equal(new CornerRadius(12), border!.CornerRadius);

            // Without a setter the ring keeps mirroring the control's rounding.
            var plain = new Button { Content = "Go", Width = 80, Height = 32, CornerRadius = new CornerRadius(7) };
            ((StackPanel)button.VisualParent!).Children.Add(plain);
            ArrangeWindow(window);
            Assert.True(plain.Focus());
            ArrangeWindow(window);
            Assert.Equal(new CornerRadius(7), GetRing(window, plain).Host.CornerRadius);
        }
        finally
        {
            SetShowFocusCues(false);
            ResetApplicationState();
        }
    }

    [Fact]
    public void FocusVisualStyle_ChangedWhileFocused_RingIsRebuiltWithTheNewStyle()
    {
        ResetApplicationState();
        var app = new Application();
        try
        {
            var button = new Button { Content = "Go", Width = 80, Height = 32 };
            var window = CreateWindow(button);
            ArrangeWindow(window);

            SetShowFocusCues(true);
            Assert.True(button.Focus());
            ArrangeWindow(window);

            var themeDefault = Assert.IsType<Style>(app.Resources[FrameworkElement.DefaultFocusVisualStyleKey]);
            Assert.Same(themeDefault, GetRing(window, button).Host.Style);

            var custom = CreateRingStyle(Brushes.Blue, 2);
            button.FocusVisualStyle = custom;
            ArrangeWindow(window);
            Assert.Same(custom, GetRing(window, button).Host.Style);

            button.FocusVisualStyle = null;
            ArrangeWindow(window);
            Assert.Null(window.AdornerLayer!.GetAdorners(button));

            var another = CreateRingStyle(Brushes.Red, 1);
            button.FocusVisualStyle = another;
            ArrangeWindow(window);
            Assert.Same(another, GetRing(window, button).Host.Style);
        }
        finally
        {
            SetShowFocusCues(false);
            ResetApplicationState();
        }
    }

    [Fact]
    public void FocusVisualStyle_NullSetterAppliedAfterFocus_RemovesTheDefaultRing()
    {
        ResetApplicationState();
        var app = new Application();
        try
        {
            var button = new Button { Content = "Go", Width = 80, Height = 32 };
            var window = CreateWindow(button);
            ArrangeWindow(window);

            SetShowFocusCues(true);
            Assert.True(button.Focus());
            ArrangeWindow(window);
            Assert.NotNull(window.AdornerLayer!.GetAdorners(button));

            // {x:Null} does not move the effective value (null → null), so only the style-stack
            // change can carry the news; the ring drawn at focus time must still go away.
            var style = new Style(typeof(Button));
            style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
            button.Style = style;
            ArrangeWindow(window);

            Assert.Null(button.ResolveFocusVisualStyle());
            Assert.Null(window.AdornerLayer!.GetAdorners(button));

            // Removing that style hands the theme default back.
            button.Style = null;
            ArrangeWindow(window);
            Assert.NotNull(window.AdornerLayer!.GetAdorners(button));
        }
        finally
        {
            SetShowFocusCues(false);
            ResetApplicationState();
        }
    }

    [Fact]
    public void FocusedBeforeAttach_RingAppearsOnceTheElementIsInAWindow()
    {
        ResetApplicationState();
        var app = new Application();
        try
        {
            SetShowFocusCues(true);

            var button = new Button { Content = "Go", Width = 80, Height = 32 };
            Assert.True(button.Focus());

            // No adorner layer above the element yet — nothing could be drawn.
            var window = CreateWindow(button);
            ArrangeWindow(window);
            Assert.Null(window.AdornerLayer!.GetAdorners(button));

            // The deferred focus pass sees focus survive the attach and puts the ring in place.
            Jalium.UI.Threading.Dispatcher.CurrentDispatcher.ProcessQueue();
            ArrangeWindow(window);
            Assert.NotNull(window.AdornerLayer!.GetAdorners(button));
        }
        finally
        {
            SetShowFocusCues(false);
            ResetApplicationState();
        }
    }

    [Fact]
    public void FocusVisualStyle_StyleAppliedAfterFocus_RingFollowsTheSetter()
    {
        ResetApplicationState();
        var app = new Application();
        try
        {
            var button = new Button { Content = "Go", Width = 80, Height = 32 };
            var window = CreateWindow(button);
            ArrangeWindow(window);

            SetShowFocusCues(true);
            Assert.True(button.Focus());
            ArrangeWindow(window);
            Assert.NotNull(window.AdornerLayer!.GetAdorners(button));

            var custom = CreateRingStyle(Brushes.Red, 5);
            var style = new Style(typeof(Button));
            style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, custom));
            button.Style = style;
            ArrangeWindow(window);

            Assert.Same(custom, GetRing(window, button).Host.Style);
        }
        finally
        {
            SetShowFocusCues(false);
            ResetApplicationState();
        }
    }

    [Fact]
    public void DefaultFocusVisualStyle_OverriddenInApplicationResources_IsPickedUp()
    {
        ResetApplicationState();
        var app = new Application();
        try
        {
            var custom = CreateRingStyle(Brushes.Red, 5);
            app.Resources[FrameworkElement.DefaultFocusVisualStyleKey] = custom;

            var button = new Button { Content = "Go", Width = 80, Height = 32 };
            var window = CreateWindow(button);
            ArrangeWindow(window);

            Assert.Same(custom, button.ResolveFocusVisualStyle());

            SetShowFocusCues(true);
            Assert.True(button.Focus());
            ArrangeWindow(window);
            Assert.Same(custom, GetRing(window, button).Host.Style);
        }
        finally
        {
            SetShowFocusCues(false);
            ResetApplicationState();
        }
    }

    [Fact]
    public void FocusRing_FollowsScrolling_AndIsClippedOutsideTheViewport()
    {
        ResetApplicationState();
        var app = new Application();
        try
        {
            var button = new Button { Width = 80, Height = 32, Content = "Go" };
            var content = new Canvas { Width = 300, Height = 600 };
            Canvas.SetTop(button, 60);
            content.Children.Add(button);
            var viewer = new ScrollViewer
            {
                Content = content, Width = 200, Height = 120,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                IsScrollInertiaEnabled = false,
            };
            var window = CreateWindow(viewer);
            ArrangeWindow(window);
            SetShowFocusCues(true);
            Assert.True(button.Focus());
            ArrangeWindow(window);
            var ring = GetRing(window, button);
            double originalTop = ring.VisualBounds.Y;

            viewer.ScrollToVerticalOffset(40);
            Assert.False(window.AdornerLayer!.IsArrangeValid);
            window.UpdateLayout();
            Assert.Equal(originalTop - 40, ring.VisualBounds.Y, 6);
            Assert.Equal(button.TranslatePoint(new Point(0, 0), window.AdornerLayer).Y, ring.VisualBounds.Y, 6);

            viewer.ScrollToVerticalOffset(80);
            window.UpdateLayout();
            var partialClip = Assert.IsAssignableFrom<Geometry>(ring.GetLayoutClip());
            var visibleRing = Rect.Intersect(new Rect(ring.RenderSize), partialClip.Bounds);
            Assert.InRange(visibleRing.Height, 0.001, ring.RenderSize.Height - 0.001);
            Assert.True(partialClip.FillContains(new Point(visibleRing.X + visibleRing.Width / 2,
                visibleRing.Y + visibleRing.Height / 2)));

            viewer.ScrollToVerticalOffset(200);
            window.UpdateLayout();
            var clip = Assert.IsAssignableFrom<Geometry>(ring.GetLayoutClip());
            Assert.True(Rect.Intersect(new Rect(ring.RenderSize), clip.Bounds).IsEmpty);
            Assert.False(clip.FillContains(new Point(ring.RenderSize.Width / 2, ring.RenderSize.Height / 2)));
            Assert.True(button.IsKeyboardFocused);

            viewer.ScrollToVerticalOffset(0);
            window.UpdateLayout();
            Assert.Equal(originalTop, ring.VisualBounds.Y, 6);
            var restoredClip = Assert.IsAssignableFrom<Geometry>(ring.GetLayoutClip());
            Assert.Equal(new Rect(ring.RenderSize), Rect.Intersect(new Rect(ring.RenderSize), restoredClip.Bounds));
            Assert.True(restoredClip.FillContains(new Point(ring.RenderSize.Width / 2, ring.RenderSize.Height / 2)));
        }
        finally
        {
            SetShowFocusCues(false);
            ResetApplicationState();
        }
    }

    private static Style CreateRingStyle(Brush brush, double thickness)
    {
        var template = new ControlTemplate(typeof(Control));
        template.SetVisualTree(() => new Border
        {
            BorderBrush = brush,
            BorderThickness = new Thickness(thickness),
            IsHitTestVisible = false,
        });

        var style = new Style(typeof(Control));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        return style;
    }

    private static FocusVisualAdorner GetRing(Window window, UIElement element)
    {
        var adorners = window.AdornerLayer!.GetAdorners(element);
        Assert.NotNull(adorners);
        return Assert.IsType<FocusVisualAdorner>(Assert.Single(adorners!));
    }

    private static T? FindDescendant<T>(Visual root) where T : Visual
    {
        for (var i = 0; i < root.InternalVisualChildrenCount; i++)
        {
            var child = root.InternalGetVisualChild(i);
            if (child is T match)
                return match;
            if (child is not null && FindDescendant<T>(child) is { } nested)
                return nested;
        }

        return null;
    }

    private static Window CreateWindow(UIElement content)
    {
        var panel = new StackPanel();
        panel.Children.Add(content);
        return new Window
        {
            TitleBarStyle = WindowTitleBarStyle.Native,
            Width = 320,
            Height = 240,
            Content = panel,
        };
    }

    private static void ArrangeWindow(Window window)
    {
        window.Measure(new Size(window.Width, window.Height));
        window.Arrange(new Rect(0, 0, window.Width, window.Height));
    }

    private static void SetShowFocusCues(bool value)
    {
        var method = typeof(FocusVisualManager).GetMethod(
            "SetShowFocusCues",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(null, new object[] { value });
    }

    private static void ResetApplicationState()
    {
        Keyboard.Initialize();
        Keyboard.ClearFocus();
        Jalium.UI.Threading.Dispatcher.CurrentDispatcher.ProcessQueue();

        var currentField = typeof(Application).GetField("_current",
            BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);

        var resetMethod = typeof(ThemeManager).GetMethod("Reset",
            BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);
    }
}

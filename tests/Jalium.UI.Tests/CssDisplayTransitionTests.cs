using System.Diagnostics;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssDisplayTransitionTests
{
    [Fact]
    public void ExitKeepsBoxAndPaintUntilEndButImmediatelyStopsInput()
    {
        var disappearing = new Border { Width = 40, Height = 20, Background = Brushes.Red };
        var following = new Border { Width = 40, Height = 20, Background = Brushes.Blue };
        var host = new StackPanel { Width = 40, Height = 40 };
        host.Children.Add(disappearing);
        host.Children.Add(following);
        var previous = UIElement.AutomaticTransitionsEnabledProvider;
        try
        {
            UIElement.AutomaticTransitionsEnabledProvider = static () => true;
            Css.SetStyle(disappearing,
                "display:block;opacity:1;transition:display 1s linear allow-discrete,opacity 1s linear");
            Layout(host);
            Assert.Equal(20, following.VisualBounds.Y);

            Css.SetStyle(disappearing,
                "display:none;opacity:0;transition:display 1s linear allow-discrete,opacity 1s linear");
            Layout(host);
            Assert.True(disappearing.HasAutomaticTransition(CssDisplayProperties.SpecificationProperty));
            Assert.Equal(Visibility.Visible, disappearing.Visibility);
            Assert.Equal(20, following.VisualBounds.Y);
            Assert.True(CssDisplayProperties.IsExitInert(disappearing));
            Assert.Null(VisualTreeHelper.HitTest(disappearing, new Point(10, 10)));
            Assert.Same(disappearing, VisualTreeHelper.GetChild(host, 0));
            Assert.NotSame(disappearing,
                VisualTreeHelper.HitTest(host, new Point(10, 10))?.VisualHit);
            Assert.NotSame(disappearing, host.InputHitTest(new Point(10, 10)));
            Assert.InRange(Red(CssBorderRadiusTests.Pixels(host, 40, 40), 10, 10, 40), 250, 255);

            var start = Stopwatch.GetTimestamp();
            Jalium.UI.Animation.AnimationManager.ProcessFrame(start);
            Jalium.UI.Animation.AnimationManager.ProcessFrame(start + Stopwatch.Frequency / 2);
            Layout(host);
            Assert.Equal(Visibility.Visible, disappearing.Visibility);
            Assert.Equal(20, following.VisualBounds.Y);
            Assert.InRange(disappearing.Opacity, .45, .55);

            Jalium.UI.Animation.AnimationManager.ProcessFrame(start + Stopwatch.Frequency);
            Layout(host);
            Assert.Equal(Visibility.Collapsed, disappearing.Visibility);
            Assert.Equal(0, following.VisualBounds.Y);
            Assert.InRange(Blue(CssBorderRadiusTests.Pixels(host, 40, 40), 10, 10, 40), 250, 255);
        }
        finally
        {
            disappearing.StopAutomaticTransition(CssDisplayProperties.SpecificationProperty,
                clearAnimatedValue: true);
            disappearing.StopAutomaticTransition(UIElement.OpacityProperty, clearAnimatedValue: true);
            UIElement.AutomaticTransitionsEnabledProvider = previous;
        }
    }

    [Fact]
    public void StartingDisplayNoneEntersWithOpacityFade()
    {
        var target = new Border { Width = 40, Height = 20, Background = Brushes.Red };
        Css.SetClass(target, "card off");
        var host = new StackPanel { Width = 40, Height = 40 };
        host.Children.Add(target);
        var previous = UIElement.AutomaticTransitionsEnabledProvider;
        try
        {
            UIElement.AutomaticTransitionsEnabledProvider = static () => true;
            Css.SetStyleSheet(host,
                ".card{display:block;opacity:1;transition:display 1s linear allow-discrete,opacity 1s linear}" +
                ".card.off{display:none;opacity:0}" +
                "@starting-style{.card{display:none;opacity:0}}");
            Layout(host);
            Assert.Equal(Visibility.Collapsed, target.Visibility);

            Css.SetClass(target, "card");
            Layout(host);
            Assert.True(target.HasAutomaticTransition(CssDisplayProperties.SpecificationProperty));
            Assert.True(target.HasAutomaticTransition(UIElement.OpacityProperty));
            Assert.Equal(Visibility.Visible, target.Visibility);
            Assert.Equal(20, target.ActualHeight);
            Assert.Equal(0, target.Opacity, 4);

            var start = Stopwatch.GetTimestamp();
            Jalium.UI.Animation.AnimationManager.ProcessFrame(start);
            Jalium.UI.Animation.AnimationManager.ProcessFrame(start + Stopwatch.Frequency / 2);
            Layout(host);
            Assert.Equal(Visibility.Visible, target.Visibility);
            Assert.InRange(target.Opacity, .45, .55);
        }
        finally
        {
            target.StopAutomaticTransition(CssDisplayProperties.SpecificationProperty,
                clearAnimatedValue: true);
            target.StopAutomaticTransition(UIElement.OpacityProperty, clearAnimatedValue: true);
            UIElement.AutomaticTransitionsEnabledProvider = previous;
        }
    }

    [Fact]
    public void OtherDisplayModesSwitchAtEasedMidpoint()
    {
        var target = new Grid { Width = 40, Height = 40 };
        var first = new Border { Width = 10, Height = 10 };
        var second = new Border { Width = 10, Height = 10 };
        target.Children.Add(first);
        target.Children.Add(second);
        var host = new Grid { Width = 40, Height = 40 };
        host.Children.Add(target);
        var previous = UIElement.AutomaticTransitionsEnabledProvider;
        try
        {
            UIElement.AutomaticTransitionsEnabledProvider = static () => true;
            Css.SetStyle(target, "display:block;transition:display 1s linear allow-discrete");
            Layout(host);
            Assert.Equal(10, second.VisualBounds.Y);

            Css.SetStyle(target, "display:flex;transition:display 1s linear allow-discrete");
            var start = Stopwatch.GetTimestamp();
            Jalium.UI.Animation.AnimationManager.ProcessFrame(start);
            Jalium.UI.Animation.AnimationManager.ProcessFrame(start + Stopwatch.Frequency / 4);
            Layout(host);
            Assert.Equal(10, second.VisualBounds.Y);

            Jalium.UI.Animation.AnimationManager.ProcessFrame(start + Stopwatch.Frequency / 2);
            Layout(host);
            Assert.Equal(10, second.VisualBounds.X);
        }
        finally
        {
            target.StopAutomaticTransition(CssDisplayProperties.SpecificationProperty,
                clearAnimatedValue: true);
            UIElement.AutomaticTransitionsEnabledProvider = previous;
        }
    }

    [Fact]
    public void NormalBehaviorCollapsesDisplayImmediately()
    {
        var target = new Border { Width = 40, Height = 20 };
        var host = new StackPanel { Width = 40, Height = 40 };
        host.Children.Add(target);
        var previous = UIElement.AutomaticTransitionsEnabledProvider;
        try
        {
            UIElement.AutomaticTransitionsEnabledProvider = static () => true;
            Css.SetStyle(target, "display:block;transition:display 1s linear");
            Layout(host);
            Css.SetStyle(target, "display:none;transition:display 1s linear");
            Layout(host);
            Assert.False(target.HasAutomaticTransition(CssDisplayProperties.SpecificationProperty));
            Assert.Equal(Visibility.Collapsed, target.Visibility);
        }
        finally
        {
            target.StopAutomaticTransition(CssDisplayProperties.SpecificationProperty,
                clearAnimatedValue: true);
            UIElement.AutomaticTransitionsEnabledProvider = previous;
        }
    }

    [Fact]
    public void ReversingExitRestoresHitTestingAndCancelsCollapse()
    {
        var target = new Border { Width = 40, Height = 20, Background = Brushes.Red };
        var host = new StackPanel { Width = 40, Height = 40 };
        host.Children.Add(target);
        var previous = UIElement.AutomaticTransitionsEnabledProvider;
        try
        {
            UIElement.AutomaticTransitionsEnabledProvider = static () => true;
            Css.SetStyle(target, "display:block;transition:display 1s linear allow-discrete");
            Layout(host);
            Css.SetStyle(target, "display:none;transition:display 1s linear allow-discrete");
            Layout(host);
            Assert.True(CssDisplayProperties.IsExitInert(target));

            Css.SetStyle(target, "display:block;transition:display 1s linear allow-discrete");
            Layout(host);
            Assert.False(CssDisplayProperties.IsExitInert(target));
            Assert.Equal(Visibility.Visible, target.Visibility);
            Assert.Same(target, host.InputHitTest(new Point(10, 10)));
            Assert.False(target.HasAutomaticTransition(CssDisplayProperties.SpecificationProperty));
        }
        finally
        {
            target.StopAutomaticTransition(CssDisplayProperties.SpecificationProperty,
                clearAnimatedValue: true);
            UIElement.AutomaticTransitionsEnabledProvider = previous;
        }
    }

    [Fact]
    public void NativeVisibilityTakesOverAnActiveDisplayTransition()
    {
        var target = new Border { Width = 40, Height = 20, Background = Brushes.Red,
            Visibility = Visibility.Visible };
        var host = new StackPanel { Width = 40, Height = 40 };
        host.Children.Add(target);
        var previous = UIElement.AutomaticTransitionsEnabledProvider;
        try
        {
            UIElement.AutomaticTransitionsEnabledProvider = static () => true;
            Css.SetStyle(target, "display:block;transition:display 1s linear allow-discrete");
            Layout(host);
            Css.SetStyle(target, "display:none;transition:display 1s linear allow-discrete");
            Layout(host);
            Assert.True(target.HasAutomaticTransition(CssDisplayProperties.SpecificationProperty));
            Assert.False(CssDisplayProperties.IsExitInert(target));
            Assert.Same(target, host.InputHitTest(new Point(10, 10)));

            target.Visibility = Visibility.Collapsed;
            Layout(host);
            Assert.Equal(Visibility.Collapsed, target.Visibility);
            target.ClearValue(UIElement.VisibilityProperty);
            Layout(host);
            Assert.Equal(Visibility.Visible, target.Visibility);
            Assert.True(CssDisplayProperties.IsExitInert(target));
        }
        finally
        {
            target.StopAutomaticTransition(CssDisplayProperties.SpecificationProperty,
                clearAnimatedValue: true);
            UIElement.AutomaticTransitionsEnabledProvider = previous;
        }
    }

    [Fact]
    public void ExitReleasesMouseCaptureAndRejectsNewFocus()
    {
        var target = new Border { Width = 40, Height = 20, Background = Brushes.Red,
            Focusable = true };
        var host = new StackPanel { Width = 40, Height = 40 };
        host.Children.Add(target);
        var previousTransitions = UIElement.AutomaticTransitionsEnabledProvider;
        var previousFocus = FocusService.Provider;
        try
        {
            UIElement.AutomaticTransitionsEnabledProvider = static () => true;
            FocusService.Provider = new TestFocusProvider();
            Css.SetStyle(target, "display:block;transition:display 1s linear allow-discrete");
            Layout(host);
            Assert.True(target.CaptureMouse());
            Assert.True(target.Focus());

            Css.SetStyle(target, "display:none;transition:display 1s linear allow-discrete");
            Layout(host);
            Assert.False(target.IsMouseCaptured);
            Assert.False(target.CaptureMouse());
            Assert.False(target.Focus());
        }
        finally
        {
            target.ReleaseMouseCapture();
            target.StopAutomaticTransition(CssDisplayProperties.SpecificationProperty,
                clearAnimatedValue: true);
            FocusService.Provider = previousFocus;
            UIElement.AutomaticTransitionsEnabledProvider = previousTransitions;
        }
    }

    [Fact]
    public void ExitReleasesDescendantPointerCapturesAndRejectsNewTouchCapture()
    {
        var child = new Border { Width = 40, Height = 20, Background = Brushes.Red };
        var target = new StackPanel { Width = 40, Height = 20 };
        target.Children.Add(child);
        var host = new Grid { Width = 40, Height = 40 };
        host.Children.Add(target);
        var previousTransitions = UIElement.AutomaticTransitionsEnabledProvider;
        var previousStylus = Tablet.CurrentStylusDevice;
        var stylus = new PointerStylusDevice(93401, "CSS display exit");
        const int touchId = 93402;
        var touch = Touch.RegisterTouchPoint(touchId, Point.Zero, child);
        try
        {
            UIElement.AutomaticTransitionsEnabledProvider = static () => true;
            Tablet.CurrentStylusDevice = stylus;
            stylus.UpdateState(Point.Zero, new StylusPointCollection(), inAir: false,
                inverted: false, inRange: true, barrelPressed: false, eraserPressed: false,
                directlyOver: child);
            Css.SetStyle(target, "display:block;transition:display 1s linear allow-discrete");
            Layout(host);
            Assert.True(child.CaptureMouse());
            Assert.True(child.CaptureStylus());
            Assert.True(child.CaptureTouch(touch));

            Css.SetStyle(target, "display:none;transition:display 1s linear allow-discrete");
            Layout(host);
            Assert.False(child.IsMouseCaptured);
            Assert.False(child.IsStylusCaptured);
            Assert.False(child.AreAnyTouchesCaptured);
            Assert.Null(stylus.Captured);
            Assert.Null(touch.Captured);
            Assert.False(child.CaptureMouse());
            Assert.False(child.CaptureStylus());
            Assert.False(child.CaptureTouch(touch));
            Assert.False(stylus.Capture(child));
            Assert.False(touch.Capture(child));
        }
        finally
        {
            child.ReleaseMouseCapture();
            child.ReleaseStylusCapture();
            child.ReleaseAllTouchCaptures();
            stylus.Capture(null);
            Touch.UnregisterTouchPoint(touchId);
            Tablet.CurrentStylusDevice = previousStylus;
            target.StopAutomaticTransition(CssDisplayProperties.SpecificationProperty,
                clearAnimatedValue: true);
            UIElement.AutomaticTransitionsEnabledProvider = previousTransitions;
        }
    }

    [Fact]
    public void CompletedExitAllowsStartingStyleOnNextEntry()
    {
        var target = new Border { Width = 40, Height = 20, Background = Brushes.Red };
        Css.SetClass(target, "card");
        var host = new StackPanel { Width = 40, Height = 40 };
        host.Children.Add(target);
        var previous = UIElement.AutomaticTransitionsEnabledProvider;
        try
        {
            UIElement.AutomaticTransitionsEnabledProvider = static () => true;
            Css.SetStyleSheet(host,
                ".card{display:block;opacity:1;transition:display 1s linear allow-discrete,opacity 1s linear}" +
                ".card.off{display:none;opacity:0}" +
                "@starting-style{.card{display:none;opacity:0}}");
            Layout(host);
            var first = Stopwatch.GetTimestamp();
            Jalium.UI.Animation.AnimationManager.ProcessFrame(first);
            Jalium.UI.Animation.AnimationManager.ProcessFrame(first + Stopwatch.Frequency);
            Layout(host);
            Assert.Equal(Visibility.Visible, target.Visibility);

            Css.SetClass(target, "card off");
            Layout(host);
            var second = Stopwatch.GetTimestamp();
            Jalium.UI.Animation.AnimationManager.ProcessFrame(second);
            Jalium.UI.Animation.AnimationManager.ProcessFrame(second + Stopwatch.Frequency);
            Layout(host);
            Assert.Equal(Visibility.Collapsed, target.Visibility);
            Assert.False(target.CssRuntimeState!.HadRenderedStyle);

            Css.SetClass(target, "card");
            Layout(host);
            Assert.True(target.HasAutomaticTransition(CssDisplayProperties.SpecificationProperty));
            Assert.True(target.HasAutomaticTransition(UIElement.OpacityProperty));
            Assert.Equal(Visibility.Visible, target.Visibility);
            Assert.Equal(0, target.Opacity, 4);
        }
        finally
        {
            target.StopAutomaticTransition(CssDisplayProperties.SpecificationProperty,
                clearAnimatedValue: true);
            target.StopAutomaticTransition(UIElement.OpacityProperty, clearAnimatedValue: true);
            UIElement.AutomaticTransitionsEnabledProvider = previous;
        }
    }

    private sealed class TestFocusProvider : IFocusProvider
    {
        public IInputElement? FocusedElement { get; private set; }
        public IInputElement? Focus(IInputElement? element) => FocusedElement = element;
        public void ClearFocus() => FocusedElement = null;
        public bool MoveFocus(UIElement element, FocusNavigationDirection direction) => false;
    }

    private static void Layout(FrameworkElement root)
        => CssBorderRadiusTests.Layout(root, 40, 40);

    private static byte Red(byte[] pixels, int x, int y, int width) =>
        pixels[((y * width) + x) * 4 + 2];
    private static byte Blue(byte[] pixels, int x, int y, int width) =>
        pixels[((y * width) + x) * 4];
}

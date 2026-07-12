using System.ComponentModel;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Input.StylusPlugIns;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using Jalium.UI.Media.Effects;

namespace Jalium.UI.Tests;

[Collection(InputElementContractCollection.CollectionName)]
public sealed class UIElementWpfParityTests
{
    [Fact]
    public void CoreSurface_UsesWpfCompatibleTypesAndSignatures()
    {
        var type = typeof(UIElement);

        Assert.False(type.IsAbstract);
        Assert.Equal(typeof(void), type.GetMethod(
            "ArrangeCore",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(Rect)],
            modifiers: null)!.ReturnType);
        Assert.NotNull(type.GetMethod("OnGotMouseCapture", BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, types: [typeof(MouseEventArgs)], modifiers: null));
        Assert.NotNull(type.GetMethod("OnTouchDown", BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, types: [typeof(TouchEventArgs)], modifiers: null));
        Assert.Equal(typeof(TouchEventHandler), type.GetEvent(nameof(UIElement.TouchDown))!.EventHandlerType);
        Assert.Equal(typeof(TouchEventHandler), UIElement.TouchDownEvent.HandlerType);
        Assert.Equal("Jalium.UI.Input", typeof(FocusNavigationDirection).Namespace);
        Assert.Equal("Jalium.UI.Input", typeof(KeyboardFocusChangedEventArgs).Namespace);
        Assert.Equal("Jalium.UI.Media", typeof(HitTestResult).Namespace);
        Assert.Equal(typeof(QueryCursorEventHandler), UIElement.QueryCursorEvent.HandlerType);
        Assert.Equal(typeof(Effect), type.GetProperty(nameof(UIElement.Effect))!.PropertyType);
        Assert.Equal(typeof(CacheMode), type.GetProperty(nameof(UIElement.CacheMode))!.PropertyType);
        Assert.Same(type.Assembly, typeof(Effect).Assembly);
        Assert.Same(type.Assembly, typeof(CacheMode).Assembly);
        Assert.Same(type.Assembly, typeof(BitmapSource).Assembly);

        var frameworkMoveFocus = typeof(FrameworkElement).GetMethod(
            nameof(FrameworkElement.MoveFocus),
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: [typeof(TraversalRequest)],
            modifiers: null);
        Assert.NotNull(frameworkMoveFocus);
        Assert.True(frameworkMoveFocus!.IsFinal);
    }

    [Fact]
    public void AnimationAndStylusPlugInSurface_UsesExactWpfContracts()
    {
        const BindingFlags publicDeclared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        const BindingFlags nonPublicDeclared = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        Type type = typeof(UIElement);

        Assert.Same(type.Assembly, typeof(AnimationTimeline).Assembly);
        Assert.Same(type.Assembly, typeof(AnimationClock).Assembly);
        Assert.NotNull(type.GetMethod(
            nameof(UIElement.ApplyAnimationClock),
            publicDeclared,
            binder: null,
            types: [typeof(DependencyProperty), typeof(AnimationClock)],
            modifiers: null));
        Assert.NotNull(type.GetMethod(
            nameof(UIElement.ApplyAnimationClock),
            publicDeclared,
            binder: null,
            types: [typeof(DependencyProperty), typeof(AnimationClock), typeof(Jalium.UI.Media.Animation.HandoffBehavior)],
            modifiers: null));
        Assert.NotNull(type.GetMethod(
            nameof(UIElement.BeginAnimation),
            publicDeclared,
            binder: null,
            types: [typeof(DependencyProperty), typeof(AnimationTimeline)],
            modifiers: null));
        Assert.NotNull(type.GetMethod(
            nameof(UIElement.BeginAnimation),
            publicDeclared,
            binder: null,
            types: [typeof(DependencyProperty), typeof(AnimationTimeline), typeof(Jalium.UI.Media.Animation.HandoffBehavior)],
            modifiers: null));

        PropertyInfo stylusPlugIns = type.GetProperty("StylusPlugIns", nonPublicDeclared)!;
        Assert.NotNull(stylusPlugIns);
        Assert.Equal(typeof(StylusPlugInCollection), stylusPlugIns.PropertyType);
        Assert.True(stylusPlugIns.GetMethod!.IsFamily);

        var element = new UIElement();
        var animation = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromSeconds(1),
        };
        var clock = new AnimationClock(animation);

        element.ApplyAnimationClock(UIElement.OpacityProperty, clock);
        Assert.True(element.HasAnimation(UIElement.OpacityProperty));
        Assert.True(element.HasAnimatedProperties);

        element.ApplyAnimationClock(UIElement.OpacityProperty, null);
        Assert.False(element.HasAnimation(UIElement.OpacityProperty));
        Assert.False(element.HasAnimatedProperties);
    }

    [Fact]
    public void ArrangeAndVisibility_UpdateConcreteStateAndReadOnlyDependencyProperties()
    {
        var parent = new VisualProbe();
        var child = new VisualProbe();
        parent.Add(child);

        child.Arrange(new Rect(2, 3, 40, 24));
        Assert.Equal(new Size(40, 24), child.RenderSize);
        Assert.True(child.IsVisible);
        Assert.True((bool)child.GetValue(UIElement.IsVisibleProperty)!);

        parent.Visibility = Visibility.Collapsed;
        Assert.False(parent.IsVisible);
        Assert.False(child.IsVisible);
        Assert.False((bool)child.GetValue(UIElement.IsVisibleProperty)!);

        Assert.Throws<InvalidOperationException>(() =>
            child.SetValue(UIElement.IsVisibleProperty, true));
    }

    [Fact]
    public void MouseStylusAndTouchState_SynchronizeReadOnlyDependencyProperties()
    {
        var parent = new VisualProbe();
        var child = new VisualProbe();
        parent.Add(child);
        var previousStylus = Tablet.CurrentStylusDevice;
        var stylus = new PointerStylusDevice(90731, "UIElement parity stylus");
        const int touchId = 90732;
        var touch = Touch.RegisterTouchPoint(touchId, Point.Zero, child);

        try
        {
            UIElement.SetMouseDirectlyOverElement(child);
            Assert.True((bool)child.GetValue(UIElement.IsMouseDirectlyOverProperty)!);

            Assert.True(child.CaptureMouse());
            Assert.True((bool)child.GetValue(UIElement.IsMouseCapturedProperty)!);
            Assert.True((bool)parent.GetValue(UIElement.IsMouseCaptureWithinProperty)!);

            Tablet.CurrentStylusDevice = stylus;
            stylus.UpdateState(Point.Zero, new StylusPointCollection(), inAir: false,
                inverted: false, inRange: true, barrelPressed: false, eraserPressed: false,
                directlyOver: child);
            Assert.True((bool)child.GetValue(UIElement.IsStylusDirectlyOverProperty)!);
            Assert.True((bool)parent.GetValue(UIElement.IsStylusOverProperty)!);

            Assert.True(child.CaptureStylus());
            Assert.True((bool)child.GetValue(UIElement.IsStylusCapturedProperty)!);
            Assert.True((bool)parent.GetValue(UIElement.IsStylusCaptureWithinProperty)!);

            child.AddOverTouchInternal(touch);
            child.AddDirectlyOverTouchInternal(touch);
            Assert.True(child.CaptureTouch(touch));
            Assert.True((bool)child.GetValue(UIElement.AreAnyTouchesCapturedProperty)!);
            Assert.True((bool)parent.GetValue(UIElement.AreAnyTouchesCapturedWithinProperty)!);
            Assert.True((bool)child.GetValue(UIElement.AreAnyTouchesDirectlyOverProperty)!);
            Assert.True((bool)parent.GetValue(UIElement.AreAnyTouchesOverProperty)!);
        }
        finally
        {
            child.ReleaseAllTouchCaptures();
            child.RemoveDirectlyOverTouchInternal(touch);
            child.RemoveOverTouchInternal(touch);
            Touch.UnregisterTouchPoint(touchId);
            child.ReleaseStylusCapture();
            stylus.Capture(null);
            Tablet.CurrentStylusDevice = previousStylus;
            child.ReleaseMouseCapture();
            UIElement.SetMouseDirectlyOverElement(null);
        }
    }

    [Fact]
    public void TouchClassHook_ReceivesTouchEventArgs()
    {
        const int touchId = 90733;
        var probe = new TouchProbe();
        var touch = Touch.RegisterTouchPoint(touchId, new Point(4, 5), probe);
        try
        {
            var args = new TouchEventArgs(touch, 19)
            {
                RoutedEvent = UIElement.TouchDownEvent,
                Source = probe,
            };

            probe.RaiseEvent(args);

            Assert.Equal(1, probe.TouchDownCount);
            Assert.Same(args, probe.LastTouchArgs);
        }
        finally
        {
            Touch.UnregisterTouchPoint(touchId);
        }
    }

    [Fact]
    public void InputMethodAndFocusTraversal_AreFunctionalAndSideEffectFreeWhenPredicting()
    {
        Keyboard.Initialize();
        Keyboard.ClearFocus();
        var panel = new StackPanel();
        var first = new FrameworkElement { Focusable = true };
        var second = new FrameworkElement { Focusable = true };
        panel.Children.Add(first);
        panel.Children.Add(second);

        InputMethod.SetIsInputMethodEnabled(first, false);
        Assert.False(first.IsInputMethodEnabled);
        Assert.True(second.IsInputMethodEnabled);

        Assert.Same(second, first.PredictFocus(FocusNavigationDirection.Next));
        Assert.Null(Keyboard.FocusedElement);

        var request = new TraversalRequest(FocusNavigationDirection.Next);
        Assert.True(first.MoveFocus(request));
        Assert.Same(second, Keyboard.FocusedElement);
        Assert.Throws<InvalidEnumArgumentException>(() =>
            new TraversalRequest((FocusNavigationDirection)99));

        Keyboard.ClearFocus();
    }

    [Fact]
    public void CursorAccessKeyAndExactFocusHooks_UseCoreRoutedInfrastructure()
    {
        var probe = new InputHookProbe { Cursor = Cursors.Cross };
        probe.QueryCursor += (_, e) => e.Cursor = Cursors.Wait;

        Assert.Same(Cursors.Wait, FrameworkElement.ResolveEffectiveCursor(probe));

        AccessKeyManager.Register("K", probe);
        try
        {
            Assert.True(AccessKeyManager.ProcessKey("k", isMultiple: false));
            Assert.Equal("K", probe.LastAccessKey);
        }
        finally
        {
            AccessKeyManager.Unregister("K", probe);
        }

        probe.UpdateIsKeyboardFocused(true);
        Assert.Equal(1, probe.KeyboardFocusPropertyChanges);
        Assert.True(probe.IsKeyboardFocused);
    }

    [Fact]
    public void GeometryHitTestAndRenderResources_HaveFunctionalCoreImplementations()
    {
        var probe = new InputHookProbe();
        probe.Arrange(new Rect(0, 0, 40, 30));

        var inside = probe.GeometryHit(new RectangleGeometry(new Rect(4, 5, 10, 8)));
        var overlapping = probe.GeometryHit(new RectangleGeometry(new Rect(35, 25, 10, 10)));
        var outside = probe.GeometryHit(new RectangleGeometry(new Rect(60, 60, 5, 5)));

        Assert.Equal(IntersectionDetail.FullyContains, inside.IntersectionDetail);
        Assert.Equal(IntersectionDetail.Intersects, overlapping.IntersectionDetail);
        Assert.Equal(IntersectionDetail.Empty, outside.IntersectionDetail);

        var effect = new ProbeEffect();
        var cache = new ProbeCacheMode();
        probe.Effect = effect;
        probe.CacheMode = cache;
        Assert.Same(effect, probe.GetValue(UIElement.EffectProperty));
        Assert.Same(cache, probe.GetValue(UIElement.CacheModeProperty));
        cache.NotifyChanged();
    }

    private sealed class VisualProbe : UIElement
    {
        public void Add(UIElement child) => AddVisualChild(child);
    }

    private sealed class TouchProbe : UIElement
    {
        public int TouchDownCount { get; private set; }
        public TouchEventArgs? LastTouchArgs { get; private set; }

        protected override void OnTouchDown(TouchEventArgs e)
        {
            TouchDownCount++;
            LastTouchArgs = e;
            base.OnTouchDown(e);
        }
    }

    private sealed class InputHookProbe : FrameworkElement
    {
        public string? LastAccessKey { get; private set; }
        public int KeyboardFocusPropertyChanges { get; private set; }

        public GeometryHitTestResult GeometryHit(Geometry geometry)
            => HitTestCore(new GeometryHitTestParameters(geometry))!;

        protected override void OnAccessKey(AccessKeyEventArgs e)
        {
            LastAccessKey = e.Key;
            base.OnAccessKey(e);
        }

        protected override void OnIsKeyboardFocusedChanged(DependencyPropertyChangedEventArgs e)
        {
            KeyboardFocusPropertyChanges++;
            base.OnIsKeyboardFocusedChanged(e);
        }
    }

    private sealed class ProbeEffect : Effect
    {
        public override bool HasEffect => true;
        public override EffectType EffectType => EffectType.Blur;
    }

    private sealed class ProbeCacheMode : CacheMode
    {
        public void NotifyChanged() => OnChanged();
        protected override Freezable CreateInstanceCore() => new ProbeCacheMode();
    }
}

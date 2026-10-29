using System.Reflection;
using System.Runtime.CompilerServices;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class InputComponentLifecycleTests
{
    private const int LifecycleRounds = 8;
    private static readonly BindingFlags InstanceNonPublic = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly FieldInfo RenderingField = typeof(CompositionTarget)
        .GetField("Rendering", BindingFlags.Static | BindingFlags.NonPublic)!;

    [Fact]
    public void TitleBar_ReplacingTemplateRepeatedly_RetiresOldButtonsAndRoutesEachClickOnce()
    {
        var titleBar = new TitleBar();
        var retiredParts = new List<TitleBarTemplateParts>();
        var minimizeClicks = 0;
        var maximizeClicks = 0;
        var closeClicks = 0;
        titleBar.MinimizeClicked += (_, _) => minimizeClicks++;
        titleBar.MaximizeRestoreClicked += (_, _) => maximizeClicks++;
        titleBar.CloseClicked += (_, _) => closeClicks++;

        for (var round = 1; round <= LifecycleRounds; round++)
        {
            var parts = new TitleBarTemplateParts();
            titleBar.Template = CreateTitleBarTemplate(parts);
            titleBar.OnApplyTemplate();
            titleBar.OnApplyTemplate();

            foreach (var retired in retiredParts)
            {
                RaiseTitleBarClicks(retired);
            }

            Assert.Equal(round - 1, minimizeClicks);
            Assert.Equal(round - 1, maximizeClicks);
            Assert.Equal(round - 1, closeClicks);

            RaiseTitleBarClicks(parts);

            Assert.Equal(round, minimizeClicks);
            Assert.Equal(round, maximizeClicks);
            Assert.Equal(round, closeClicks);

            titleBar.Template = null;
            RaiseTitleBarClicks(parts);

            Assert.Equal(round, minimizeClicks);
            Assert.Equal(round, maximizeClicks);
            Assert.Equal(round, closeClicks);
            retiredParts.Add(parts);
        }
    }

    [Fact]
    public void ScrollViewer_RepeatedDetachStopsFrameTimer_AndReattachKeepsOneSubscription()
    {
        var root = new Grid();
        var viewer = CreateScrollableViewer();
        DispatcherTimer? smoothTimer = null;

        try
        {
            for (var round = 0; round < LifecycleRounds; round++)
            {
                root.Children.Add(viewer);

                var offsetBeforeInput = viewer.VerticalOffset;
                viewer.RaiseEvent(CreateMouseWheel(timestamp: round + 1));

                smoothTimer ??= GetPrivateField<DispatcherTimer>(viewer, "_smoothScrollTimer");
                var expectedDelta = ScrollViewer.ComputeMouseWheelDelta(
                    wheelDelta: -120,
                    lineStep: ScrollViewer.LineScrollAmount,
                    pageStep: 100);
                var expectedOffset = offsetBeforeInput + expectedDelta;
                var renderingHandlers = CaptureActiveRenderingHandlers(smoothTimer);

                Assert.True(smoothTimer.IsEnabled);
                Assert.True(GetPrivateField<bool>(viewer, "_isSmoothScrolling"));
                Assert.Equal(
                    expectedOffset,
                    GetPrivateField<double>(viewer, "_smoothTargetY"),
                    precision: 3);
                Assert.NotEmpty(renderingHandlers);
                Assert.All(
                    renderingHandlers,
                    handler => Assert.Equal(1, CountRenderingSubscriptions(handler)));

                root.Children.Remove(viewer);

                Assert.False(smoothTimer.IsEnabled);
                Assert.False(GetPrivateField<bool>(viewer, "_isSmoothScrolling"));
                Assert.Equal(expectedOffset, viewer.VerticalOffset, precision: 3);
                Assert.All(
                    renderingHandlers,
                    handler => Assert.Equal(0, CountRenderingSubscriptions(handler)));
            }
        }
        finally
        {
            root.Children.Remove(viewer);
            smoothTimer?.Stop();
        }
    }

    [Fact]
    public void DetachedScrollViewer_ActiveFrameTimerDoesNotKeepViewerAlive()
    {
        var (viewerReference, timerReference) = CreateDetachedViewerReferences();
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        do
        {
            Dispatcher.GetForCurrentThread().ProcessQueue();
            ForceFullCollection();
            if (!viewerReference.IsAlive && !timerReference.IsAlive) break;
            // Disposal cannot join a thread-pool timer callback that already
            // entered. Allow that old generation to return before checking roots.
            Thread.Sleep(10);
        } while (deadline.ElapsedMilliseconds < 2000);

        var leakedTimer = timerReference.Target as DispatcherTimer;
        try
        {
            Assert.False(viewerReference.IsAlive, LifecycleRootDiagnostic.Find(viewerReference.Target, Dispatcher.GetForCurrentThread()));
            Assert.False(timerReference.IsAlive);
        }
        finally
        {
            // Keep a pre-fix failure from retaining a global Rendering subscription
            // and contaminating later tests in the Application collection.
            leakedTimer?.Stop();
        }
    }

    [RequiresWindowsBackendFact(Jalium.UI.Interop.RenderBackend.Software)]
    public void WindowContentReplacement_ClearsHoverAndCancelsThumbCaptureEveryRound()
    {
        Keyboard.Initialize();
        UIElement.ForceReleaseMouseCapture();
        Keyboard.ClearFocus();

        RenderContextEmptyWindowTests.DrainAllContexts();
        _ = Jalium.UI.Interop.RenderContext.GetOrCreateCurrent(Jalium.UI.Interop.RenderBackend.Software);
        var window = new Window
        {
            Width = 120,
            Height = 80,
            TitleBarStyle = WindowTitleBarStyle.Native
        };
        var dispatcher = GetInputDispatcher(window);
        var retiredPages = new List<WeakReference>();

        try
        {
            window.Show();
            for (var round = 0; round < LifecycleRounds; round++)
            {
                retiredPages.Add(RunWindowContentRound(window, dispatcher, round));
            }

            Dispatcher.GetForCurrentThread().ProcessQueue();
            // Logical focus intentionally remembers the last focused element in
            // the scope. Release that application-owned choice before checking
            // whether the input dispatcher or renderer still roots a retired page.
            FocusManager.SetFocusedElement(window, null);
            ForceFullCollection();

            Assert.All(retiredPages, page => Assert.False(page.IsAlive, LifecycleRootDiagnostic.Find(page.Target, window)));
            Assert.Null(dispatcher.LastMouseOverElement);
            Assert.Null(UIElement.MouseDirectlyOverElement);
            Assert.Null(Mouse.Captured);
        }
        finally
        {
            dispatcher.HandleMouseLeave();
            UIElement.ForceReleaseMouseCapture();
            Keyboard.ClearFocus();
            window.Content = null;
            window.Close();
            RenderContextEmptyWindowTests.DrainAllContexts();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RunWindowContentRound(
        Window window,
        WindowInputDispatcher dispatcher,
        int round)
    {
        var thumb = new Thumb
        {
            Width = 120,
            Height = 80,
            ShowGrip = false,
            Background = new SolidColorBrush(Color.FromRgb(80, 80, 80))
        };
        var mouseEnterCount = 0;
        var mouseMoveCount = 0;
        var mouseLeaveCount = 0;
        var dragStartedCount = 0;
        var dragCompletedCount = 0;
        var dragWasCanceled = false;
        thumb.MouseEnter += (_, _) => mouseEnterCount++;
        thumb.MouseMove += (_, _) => mouseMoveCount++;
        thumb.MouseLeave += (_, _) => mouseLeaveCount++;
        thumb.DragStarted += (_, _) => dragStartedCount++;
        thumb.DragCompleted += (_, e) =>
        {
            dragCompletedCount++;
            dragWasCanceled = e.Canceled;
        };

        window.Content = thumb;
        ArrangeWindow(window);
        Assert.Same(thumb, HitTestWindow(window, new Point(10, 10)));

        dispatcher.HandleMouseMove(
            new Point(10, 10),
            MouseButtonStates.AllReleased,
            ModifierKeys.None,
            timestamp: round * 10 + 1);

        Assert.Equal(1, mouseEnterCount);
        Assert.Equal(1, mouseMoveCount);
        Assert.True(thumb.IsMouseOver);

        dispatcher.HandleMouseDown(
            MouseButton.Left,
            new Point(10, 10),
            MouseButtonStates.AllReleased with { Left = MouseButtonState.Pressed },
            ModifierKeys.None,
            clickCount: 1,
            timestamp: round * 10 + 2);

        Assert.Equal(1, dragStartedCount);
        Assert.True(thumb.IsDragging);
        Assert.True(thumb.IsMouseCaptured);

        window.Content = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 30))
        };
        ArrangeWindow(window);
        Dispatcher.GetForCurrentThread().ProcessQueue();

        // The dirty-region transaction retains old elements until their previous
        // pixels have been erased. Complete that real frame before testing roots.
        window.ForceRenderFrame();
        Assert.Equal(1, mouseLeaveCount);
        Assert.False(thumb.IsMouseOver);
        Assert.False(thumb.IsDragging);
        Assert.False(thumb.IsMouseCaptured);
        Assert.Equal(1, dragCompletedCount);
        Assert.True(dragWasCanceled);
        Assert.Null(dispatcher.LastMouseOverElement);
        Assert.Null(UIElement.MouseDirectlyOverElement);
        Assert.Null(Mouse.Captured);

        var reference = new WeakReference(thumb);
        GC.KeepAlive(thumb);
        return reference;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Viewer, WeakReference Timer) CreateDetachedViewerReferences()
    {
        var root = new Grid();
        var viewer = CreateScrollableViewer();
        root.Children.Add(viewer);
        viewer.RaiseEvent(CreateMouseWheel(timestamp: 1));

        var timer = GetPrivateField<DispatcherTimer>(viewer, "_smoothScrollTimer");
        root.Children.Remove(viewer);

        var viewerReference = new WeakReference(viewer);
        var timerReference = new WeakReference(timer);
        GC.KeepAlive(root);
        GC.KeepAlive(viewer);
        GC.KeepAlive(timer);
        return (viewerReference, timerReference);
    }

    private static ScrollViewer CreateScrollableViewer()
    {
        var viewer = new ScrollViewer
        {
            IsScrollInertiaEnabled = true,
            ScrollInertiaDurationMs = 3000,
            IsScrollBarAutoHideEnabled = false
        };
        viewer.Arrange(new Rect(0, 0, 240, 140));

        SetPrivateField(viewer, "_extentHeight", 2000.0);
        SetPrivateField(viewer, "_viewportHeight", 100.0);
        SetPrivateField(viewer, "_extentWidth", 200.0);
        SetPrivateField(viewer, "_viewportWidth", 100.0);
        SetPrivateField(viewer, "_verticalOffset", 0.0);
        SetPrivateField(viewer, "_horizontalOffset", 0.0);
        SetPrivateField(viewer, "_requestedVerticalOffset", 0.0);
        SetPrivateField(viewer, "_requestedHorizontalOffset", 0.0);
        SetPrivateField(viewer, "_smoothTargetX", 0.0);
        SetPrivateField(viewer, "_smoothTargetY", 0.0);
        return viewer;
    }

    private static MouseWheelEventArgs CreateMouseWheel(int timestamp)
    {
        return new MouseWheelEventArgs(
            UIElement.MouseWheelEvent,
            new Point(8, 8),
            delta: -120,
            leftButton: MouseButtonState.Released,
            middleButton: MouseButtonState.Released,
            rightButton: MouseButtonState.Released,
            xButton1: MouseButtonState.Released,
            xButton2: MouseButtonState.Released,
            modifiers: ModifierKeys.None,
            timestamp: timestamp);
    }

    private static ControlTemplate CreateTitleBarTemplate(TitleBarTemplateParts parts)
    {
        var template = new ControlTemplate(typeof(TitleBar));
        template.SetVisualTree(() =>
        {
            var panel = new StackPanel();
            parts.Minimize = new TitleBarButton { Name = "PART_MinimizeButton" };
            parts.Maximize = new TitleBarButton { Name = "PART_MaximizeButton" };
            parts.Close = new TitleBarButton { Name = "PART_CloseButton" };
            panel.Children.Add(parts.Minimize);
            panel.Children.Add(parts.Maximize);
            panel.Children.Add(parts.Close);
            return panel;
        });
        return template;
    }

    private static void RaiseTitleBarClicks(TitleBarTemplateParts parts)
    {
        Assert.NotNull(parts.Minimize);
        Assert.NotNull(parts.Maximize);
        Assert.NotNull(parts.Close);
        parts.Minimize!.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, parts.Minimize));
        parts.Maximize!.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, parts.Maximize));
        parts.Close!.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, parts.Close));
    }

    private static WindowInputDispatcher GetInputDispatcher(Window window)
    {
        var field = typeof(Window).GetField("_inputDispatcher", InstanceNonPublic);
        Assert.NotNull(field);
        return Assert.IsType<WindowInputDispatcher>(field!.GetValue(window));
    }

    private static UIElement? HitTestWindow(Window window, Point point)
    {
        var method = typeof(Window).GetMethod(
            "HitTestElement",
            InstanceNonPublic,
            binder: null,
            types: [typeof(Point), typeof(string)],
            modifiers: null);
        Assert.NotNull(method);
        return method!.Invoke(window, new object?[] { point, "input-component-lifecycle" }) as UIElement;
    }

    private static void ArrangeWindow(Window window)
    {
        var size = new Size(window.Width, window.Height);
        window.Measure(size);
        window.Arrange(new Rect(0, 0, size.Width, size.Height));
    }

    private static EventHandler[] CaptureActiveRenderingHandlers(DispatcherTimer timer)
    {
        var generationField = typeof(DispatcherTimer).GetField("_activeGeneration", InstanceNonPublic);
        Assert.NotNull(generationField);
        var generation = generationField!.GetValue(timer);
        Assert.NotNull(generation);

        var handlerField = generation!.GetType().GetField(
            "RenderingHandler",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(handlerField);
        var registration = Assert.IsAssignableFrom<Delegate>(handlerField!.GetValue(generation));
        return registration.GetInvocationList().OfType<EventHandler>().ToArray();
    }

    private static int CountRenderingSubscriptions(EventHandler expectedHandler)
    {
        return (RenderingField.GetValue(null) as Delegate)?.GetInvocationList()
            .OfType<EventHandler>()
            .Count(handler => handler == expectedHandler) ?? 0;
    }

    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, InstanceNonPublic);
        Assert.NotNull(field);
        return Assert.IsType<T>(field!.GetValue(instance));
    }

    private static void SetPrivateField(object instance, string fieldName, object value)
    {
        var field = instance.GetType().GetField(fieldName, InstanceNonPublic);
        Assert.NotNull(field);
        field!.SetValue(instance, value);
    }

    private static void ForceFullCollection()
    {
        for (var pass = 0; pass < 3; pass++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    private sealed class TitleBarTemplateParts
    {
        public TitleBarButton? Minimize { get; set; }
        public TitleBarButton? Maximize { get; set; }
        public TitleBarButton? Close { get; set; }
    }
}

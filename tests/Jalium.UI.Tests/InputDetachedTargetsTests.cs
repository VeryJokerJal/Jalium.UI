using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class InputDetachedTargetsTests
{
    [Fact]
    public void ReplacingContent_ReleasesDetachedHoverPressedCaptureAndPointerState()
    {
        ResetInputState();
        Window? window = null;

        try
        {
            var (oldRoot, oldLeaf) = CreateTree();
            var (newRoot, _) = CreateTree();
            window = CreateWindow(oldRoot);
            WindowInputDispatcher dispatcher = GetInputDispatcher(window);

            int mouseLeaveCount = 0;
            int lostCaptureCount = 0;
            List<uint> canceledPointers = [];
            oldLeaf.AddHandler(
                UIElement.MouseLeaveEvent,
                new RoutedEventHandler((_, _) => mouseLeaveCount++));
            oldLeaf.LostMouseCapture += (_, _) => lostCaptureCount++;
            oldLeaf.AddHandler(
                UIElement.PointerCancelEvent,
                new PointerCancelEventHandler((_, args) => canceledPointers.Add(args.Pointer.PointerId)));

            ArmMouseInteraction(dispatcher, oldLeaf, timestamp: 10);

            Assert.True(oldLeaf.IsMouseOver);
            Assert.True(oldLeaf.IsPressed);
            Assert.True(oldLeaf.IsMouseCaptured);
            Assert.Same(oldLeaf, dispatcher.ActivePointerTargets[WindowInputDispatcher.MousePointerId]);

            window.Content = newRoot;

            Assert.False(oldLeaf.IsMouseOver);
            Assert.False(oldLeaf.IsPressed);
            Assert.False(oldLeaf.IsMouseCaptured);
            Assert.False(oldRoot.IsPressed);
            Assert.False(window.IsPressed);
            Assert.Null(Mouse.Captured);
            Assert.Null(Mouse.DirectlyOver);
            Assert.Null(dispatcher.LastMouseOverElement);
            Assert.False(dispatcher.ActivePointerTargets.ContainsKey(WindowInputDispatcher.MousePointerId));
            Assert.False(dispatcher.LastPointerPoints.ContainsKey(WindowInputDispatcher.MousePointerId));
            Assert.Equal(1, mouseLeaveCount);
            Assert.Equal(1, lostCaptureCount);
            Assert.Equal(new[] { WindowInputDispatcher.MousePointerId }, canceledPointers);
        }
        finally
        {
            window?.Close();
            ResetInputState();
        }
    }

    [Fact]
    public void ReplacingContent_ReleasesDetachedTouchCaptureHoverAndPointerState()
    {
        ResetInputState();
        Window? window = null;

        try
        {
            const uint PointerId = 701;
            var (oldRoot, oldLeaf) = CreateTree();
            var (newRoot, _) = CreateTree();
            window = CreateWindow(oldRoot);
            WindowInputDispatcher dispatcher = GetInputDispatcher(window);

            int touchLeaveCount = 0;
            int lostTouchCaptureCount = 0;
            int pointerCancelCount = 0;
            oldLeaf.AddHandler(
                UIElement.PreviewTouchDownEvent,
                new TouchEventHandler((_, args) => args.Handled = true));
            oldLeaf.AddHandler(
                UIElement.TouchLeaveEvent,
                new TouchEventHandler((_, _) => touchLeaveCount++));
            oldLeaf.AddHandler(
                UIElement.LostTouchCaptureEvent,
                new TouchEventHandler((_, _) => lostTouchCaptureCount++));
            oldLeaf.AddHandler(
                UIElement.PointerCancelEvent,
                new PointerCancelEventHandler((_, _) => pointerCancelCount++));

            // The real pointer entry point resolves capture before hit testing. Use
            // it only to target the initial contact, then release it so the lifecycle
            // assertion below is exclusively about touch/pointer state.
            Assert.True(oldLeaf.CaptureMouse());
            PointerInputData touchDown = CreateTouchInput(PointerId, new Point(10, 10), inContact: true);
            dispatcher.HandlePointerInput(touchDown, isDown: true, isUp: false, timestamp: 70);
            oldLeaf.ReleaseMouseCapture();

            TouchDevice device = Touch.GetDevice(unchecked((int)PointerId))
                ?? throw new InvalidOperationException("Touch device was not registered.");
            Assert.True(oldLeaf.CaptureTouch(device));
            Assert.True(oldLeaf.AreAnyTouchesOver);
            Assert.True(oldLeaf.AreAnyTouchesDirectlyOver);
            Assert.True(oldLeaf.AreAnyTouchesCaptured);
            Assert.Same(oldLeaf, UIElement.GetTouchCapture(unchecked((int)PointerId)));

            window.Content = newRoot;

            Assert.Null(Touch.GetDevice(unchecked((int)PointerId)));
            Assert.Null(UIElement.GetTouchCapture(unchecked((int)PointerId)));
            Assert.False(oldLeaf.AreAnyTouchesOver);
            Assert.False(oldLeaf.AreAnyTouchesDirectlyOver);
            Assert.False(oldLeaf.AreAnyTouchesCaptured);
            Assert.False(oldRoot.AreAnyTouchesOver);
            Assert.False(window.AreAnyTouchesOver);
            Assert.False(dispatcher.ActivePointerTargets.ContainsKey(PointerId));
            Assert.False(dispatcher.LastPointerPoints.ContainsKey(PointerId));
            Assert.Equal(1, touchLeaveCount);
            Assert.Equal(1, lostTouchCaptureCount);
            Assert.Equal(1, pointerCancelCount);
        }
        finally
        {
            window?.Close();
            ResetInputState();
        }
    }

    [Fact]
    public void ReplacingContent_AfterPointerCancel_CleansStaleTouchOverWithoutSecondCancel()
    {
        ResetInputState();
        Window? window = null;

        try
        {
            const uint PointerId = 702;
            var (oldRoot, oldLeaf) = CreateTree();
            var (newRoot, _) = CreateTree();
            window = CreateWindow(oldRoot);
            WindowInputDispatcher dispatcher = GetInputDispatcher(window);

            int touchLeaveCount = 0;
            int pointerCancelCount = 0;
            oldLeaf.AddHandler(
                UIElement.PreviewTouchDownEvent,
                new TouchEventHandler((_, args) => args.Handled = true));
            oldLeaf.AddHandler(
                UIElement.TouchLeaveEvent,
                new TouchEventHandler((_, _) => touchLeaveCount++));
            oldLeaf.AddHandler(
                UIElement.PointerCancelEvent,
                new PointerCancelEventHandler((_, _) => pointerCancelCount++));

            Assert.True(oldLeaf.CaptureMouse());
            PointerInputData touchDown = CreateTouchInput(PointerId, new Point(11, 11), inContact: true);
            dispatcher.HandlePointerInput(touchDown, isDown: true, isUp: false, timestamp: 80);
            oldLeaf.ReleaseMouseCapture();

            dispatcher.HandlePointerCancel(touchDown, timestamp: 81);

            Assert.Equal(1, pointerCancelCount);
            Assert.Null(Touch.GetDevice(unchecked((int)PointerId)));

            window.Content = newRoot;

            Assert.Equal(1, pointerCancelCount);
            Assert.Equal(1, touchLeaveCount);
            Assert.False(oldLeaf.AreAnyTouchesOver);
            Assert.False(oldLeaf.AreAnyTouchesDirectlyOver);
            Assert.False(oldRoot.AreAnyTouchesOver);
            Assert.False(window.AreAnyTouchesOver);
        }
        finally
        {
            window?.Close();
            ResetInputState();
        }
    }

    [Fact]
    public void ReplacementTree_ContinuesReceivingInputAfterDetachedCleanup()
    {
        ResetInputState();
        Window? window = null;

        try
        {
            var (oldRoot, oldLeaf) = CreateTree();
            var (newRoot, newLeaf) = CreateTree();
            window = CreateWindow(oldRoot);
            WindowInputDispatcher dispatcher = GetInputDispatcher(window);

            ArmMouseInteraction(dispatcher, oldLeaf, timestamp: 20);
            window.Content = newRoot;
            Layout(window);

            int mouseDownCount = 0;
            int pointerDownCount = 0;
            newLeaf.AddHandler(
                UIElement.MouseDownEvent,
                new MouseButtonEventHandler((_, _) => mouseDownCount++));
            newLeaf.AddHandler(
                UIElement.PointerDownEvent,
                new PointerDownEventHandler((_, _) => pointerDownCount++));

            Assert.True(newLeaf.CaptureMouse());
            dispatcher.UpdateMouseOverState(newLeaf, timestamp: 30);
            dispatcher.HandleMouseDown(
                MouseButton.Left,
                new Point(8, 8),
                PressedButtons,
                ModifierKeys.None,
                clickCount: 1,
                timestamp: 31);

            Assert.Equal(1, mouseDownCount);
            Assert.Equal(1, pointerDownCount);
            Assert.True(newLeaf.IsMouseCaptured);
            Assert.True(newLeaf.IsMouseOver);
            Assert.True(newLeaf.IsPressed);
            Assert.Same(newLeaf, dispatcher.LastMouseOverElement);
            Assert.Same(newLeaf, dispatcher.ActivePointerTargets[WindowInputDispatcher.MousePointerId]);
        }
        finally
        {
            window?.Close();
            ResetInputState();
        }
    }

    [Fact]
    public void ReplacingAndClosingOneWindow_PreservesAnotherWindowCapture()
    {
        ResetInputState();
        Window? firstWindow = null;
        Window? secondWindow = null;

        try
        {
            var (firstRoot, firstLeaf) = CreateTree();
            var (replacementRoot, _) = CreateTree();
            var (secondRoot, secondLeaf) = CreateTree();
            firstWindow = CreateWindow(firstRoot);
            secondWindow = CreateWindow(secondRoot);

            WindowInputDispatcher firstDispatcher = GetInputDispatcher(firstWindow);
            firstDispatcher.UpdateMouseOverState(firstLeaf, timestamp: 40);
            Assert.True(secondLeaf.CaptureMouse());

            firstWindow.Content = replacementRoot;

            Assert.Same(secondLeaf, Mouse.Captured);
            Assert.True(secondLeaf.IsMouseCaptured);

            firstWindow.Close();

            Assert.Same(secondLeaf, Mouse.Captured);
            Assert.True(secondLeaf.IsMouseCaptured);
        }
        finally
        {
            secondWindow?.Close();
            firstWindow?.Close();
            ResetInputState();
        }
    }

    [Fact]
    public void RepeatedSubtreeDetach_IsIdempotentAndRaisesCancellationOnce()
    {
        ResetInputState();
        Window? window = null;

        try
        {
            var (oldRoot, oldLeaf) = CreateTree();
            var (newRoot, _) = CreateTree();
            window = CreateWindow(oldRoot);
            WindowInputDispatcher dispatcher = GetInputDispatcher(window);

            int mouseLeaveCount = 0;
            int pointerCancelCount = 0;
            int lostCaptureCount = 0;
            oldLeaf.AddHandler(
                UIElement.MouseLeaveEvent,
                new RoutedEventHandler((_, _) => mouseLeaveCount++));
            oldLeaf.AddHandler(
                UIElement.PointerCancelEvent,
                new RoutedEventHandler((_, _) => pointerCancelCount++));
            oldLeaf.LostMouseCapture += (_, _) => lostCaptureCount++;

            ArmMouseInteraction(dispatcher, oldLeaf, timestamp: 50);
            window.Content = newRoot;

            dispatcher.HandleSubtreeDetached(oldRoot);
            dispatcher.HandleSubtreeDetached(oldRoot);

            Assert.Equal(1, mouseLeaveCount);
            Assert.Equal(1, pointerCancelCount);
            Assert.Equal(1, lostCaptureCount);
            Assert.False(oldLeaf.IsMouseOver);
            Assert.False(oldLeaf.IsPressed);
            Assert.False(oldLeaf.IsMouseCaptured);
        }
        finally
        {
            window?.Close();
            ResetInputState();
        }
    }

    [Fact]
    public void LostCaptureReentry_CanEstablishReplacementTreeState()
    {
        ResetInputState();
        Window? window = null;

        try
        {
            var (oldRoot, oldLeaf) = CreateTree();
            var (newRoot, newLeaf) = CreateTree();
            window = CreateWindow(oldRoot);
            WindowInputDispatcher dispatcher = GetInputDispatcher(window);

            int lostCaptureCount = 0;
            oldLeaf.LostMouseCapture += (_, _) =>
            {
                lostCaptureCount++;
                Layout(window);
                Assert.True(newLeaf.CaptureMouse());
                dispatcher.UpdateMouseOverState(newLeaf, timestamp: 61);
                dispatcher.HandleMouseDown(
                    MouseButton.Left,
                    new Point(12, 12),
                    PressedButtons,
                    ModifierKeys.None,
                    clickCount: 1,
                    timestamp: 62);
            };

            ArmMouseInteraction(dispatcher, oldLeaf, timestamp: 60);

            window.Content = newRoot;

            Assert.Equal(1, lostCaptureCount);
            Assert.Same(newLeaf, Mouse.Captured);
            Assert.Same(newLeaf, Mouse.DirectlyOver);
            Assert.Same(newLeaf, dispatcher.LastMouseOverElement);
            Assert.Same(newLeaf, dispatcher.ActivePointerTargets[WindowInputDispatcher.MousePointerId]);
            Assert.True(newLeaf.IsMouseCaptured);
            Assert.True(newLeaf.IsMouseOver);
            Assert.True(newLeaf.IsPressed);
            Assert.False(oldLeaf.IsMouseCaptured);
            Assert.False(oldLeaf.IsMouseOver);
            Assert.False(oldLeaf.IsPressed);
        }
        finally
        {
            window?.Close();
            ResetInputState();
        }
    }

    [Fact]
    public void ReplacingContent_StopsDetachedManipulationInertiaOnce()
    {
        ResetInputState();
        Window? window = null;

        try
        {
            var (oldRoot, oldLeaf) = CreateTree();
            var (newRoot, _) = CreateTree();
            window = CreateWindow(oldRoot);
            WindowInputDispatcher dispatcher = GetInputDispatcher(window);

            int completedCount = 0;
            bool? completedWasInertial = null;
            oldLeaf.AddHandler(
                UIElement.ManipulationCompletedEvent,
                new ManipulationCompletedEventHandler((_, args) =>
                {
                    completedCount++;
                    completedWasInertial = args.IsInertial;
                }));

            ManipulationInertiaProcessor processor = TrackRunningInertia(dispatcher, oldLeaf);
            Assert.True(processor.IsRunning);

            window.Content = newRoot;

            Assert.False(processor.IsRunning);
            Assert.Equal(1, completedCount);
            Assert.Equal(true, completedWasInertial);

            dispatcher.HandleSubtreeDetached(oldRoot);

            Assert.Equal(1, completedCount);
        }
        finally
        {
            window?.Close();
            ResetInputState();
        }
    }

    private static readonly MouseButtonStates PressedButtons =
        MouseButtonStates.AllReleased.WithButton(MouseButton.Left, MouseButtonState.Pressed);

    [Fact]
    public void NestedPageReplacement_RevalidatesInputWithoutChangingWindowContent()
    {
        ResetInputState();
        var (oldRoot, oldLeaf) = CreateTree();
        var (newRoot, _) = CreateTree();
        var pageHost = new ContentControl { Content = oldRoot };
        var window = CreateWindow(pageHost);
        try
        {
            var dispatcher = GetInputDispatcher(window);
            ArmMouseInteraction(dispatcher, oldLeaf, 100);
            pageHost.Content = newRoot;
            Dispatcher.GetForCurrentThread().ProcessQueue();
            Assert.Same(pageHost, window.Content);
            Assert.Null(Mouse.Captured);
            Assert.Null(dispatcher.LastMouseOverElement);
            Assert.False(dispatcher.ActivePointerTargets.ContainsKey(WindowInputDispatcher.MousePointerId));
            Assert.False(oldLeaf.IsMouseOver);
        }
        finally { window.Close(); ResetInputState(); }
    }

    [Fact]
    public void SameBatchReparent_PreservesInputOwnedByTheStillAttachedSubtree()
    {
        ResetInputState();
        var (root, leaf) = CreateTree();
        var pageHost = new ContentControl { Content = root };
        var window = CreateWindow(pageHost);
        try
        {
            var dispatcher = GetInputDispatcher(window);
            ArmMouseInteraction(dispatcher, leaf, 110);
            pageHost.Content = null;
            pageHost.Content = root;
            Dispatcher.GetForCurrentThread().ProcessQueue();
            Assert.Same(leaf, Mouse.Captured);
            Assert.Same(leaf, dispatcher.LastMouseOverElement);
            Assert.Same(leaf, dispatcher.ActivePointerTargets[WindowInputDispatcher.MousePointerId]);
        }
        finally { window.Close(); ResetInputState(); }
    }

    [Fact]
    public void OperationPostedNestedPump_DoesNotPublishCompletedDetachOperation()
    {
        ResetInputState();
        var (root, leaf) = CreateTree();
        var pageHost = new ContentControl { Content = root };
        var window = CreateWindow(pageHost);
        var dispatcher = Dispatcher.GetForCurrentThread();
        int ownerThread = Environment.CurrentManagedThreadId;
        bool nestedPumpRan = false;
        DispatcherHookEventHandler handler = (_, e) =>
        {
            if (nestedPumpRan ||
                Environment.CurrentManagedThreadId != ownerThread ||
                e.Operation.Priority != DispatcherPriority.Input)
            {
                return;
            }

            nestedPumpRan = true;
            dispatcher.ProcessQueue();
        };

        try
        {
            var inputDispatcher = GetInputDispatcher(window);
            ArmMouseInteraction(inputDispatcher, leaf, 120);
            dispatcher.Hooks.OperationPosted += handler;

            pageHost.Content = null;
            pageHost.Content = root;

            dispatcher.Hooks.OperationPosted -= handler;
            Assert.True(nestedPumpRan);
            Assert.Null(ReadWindowField<object?>(window, "_inputDetachOperation"));
            Assert.True(ReadWindowField<bool>(window, "_inputDetachFrameFallbackPending"));
            InvokeWindowMethod(window, "ProcessPendingInputDetachesForFrame");

            Assert.Null(ReadWindowField<object?>(window, "_inputDetachOperation"));
            Assert.Null(ReadWindowField<object?>(window, "_inputDetachAttempt"));
            Assert.Same(leaf, Mouse.Captured);
            Assert.Same(leaf, inputDispatcher.LastMouseOverElement);
            Assert.Same(
                leaf,
                inputDispatcher.ActivePointerTargets[WindowInputDispatcher.MousePointerId]);

            // The completed operation from the nested pump must not poison future
            // detach scheduling once the subtree really leaves the window.
            pageHost.Content = null;
            dispatcher.ProcessQueue();
            Assert.Null(Mouse.Captured);
            Assert.Null(inputDispatcher.LastMouseOverElement);
            Assert.False(inputDispatcher.ActivePointerTargets.ContainsKey(WindowInputDispatcher.MousePointerId));
        }
        finally
        {
            dispatcher.Hooks.OperationPosted -= handler;
            window.Close();
            ResetInputState();
        }
    }

    [Fact]
    public void OperationPostedAlwaysAborts_FrameClockFallbackCompletesDirtyState()
    {
        ResetInputState();
        var (root, leaf) = CreateTree();
        var pageHost = new ContentControl { Content = root };
        var window = CreateWindow(pageHost);
        var dispatcher = Dispatcher.GetForCurrentThread();
        int abortedPosts = 0;
        int abortFailed = 0;
        DispatcherHookEventHandler handler = (_, e) =>
        {
            if (e.Operation.Priority != DispatcherPriority.Input)
                return;

            Interlocked.Increment(ref abortedPosts);
            if (!e.Operation.Abort())
                Volatile.Write(ref abortFailed, 1);
        };

        try
        {
            var inputDispatcher = GetInputDispatcher(window);
            ArmMouseInteraction(inputDispatcher, leaf, 130);
            dispatcher.Hooks.OperationPosted += handler;

            pageHost.Content = null;

            Assert.Null(ReadWindowField<object?>(window, "_inputDetachOperation"));
            Assert.Null(ReadWindowField<object?>(window, "_inputDetachAttempt"));

            Assert.Equal(1, Volatile.Read(ref abortedPosts));
            Assert.Equal(0, Volatile.Read(ref abortFailed));
            Assert.NotNull(ReadWindowField<object?>(window, "_pendingInputDetachRoots"));
            Assert.True(ReadWindowField<bool>(window, "_inputDetachFrameFallbackPending"));
            Assert.Same(leaf, Mouse.Captured);

            // Dispatcher hooks may abort every queued operation. The frame clock is
            // an independent final-state boundary and must finish the original detach
            // without waiting for another tree mutation or reposting indefinitely.
            InvokeWindowMethod(window, "ProcessPendingInputDetachesForFrame");

            Assert.Equal(1, Volatile.Read(ref abortedPosts));
            Assert.Null(Mouse.Captured);
            Assert.Null(inputDispatcher.LastMouseOverElement);
            Assert.False(inputDispatcher.ActivePointerTargets.ContainsKey(WindowInputDispatcher.MousePointerId));
            Assert.Null(ReadWindowField<object?>(window, "_pendingInputDetachRoots"));
            Assert.False(ReadWindowField<bool>(window, "_inputDetachFrameFallbackPending"));
        }
        finally
        {
            dispatcher.Hooks.OperationPosted -= handler;
            window.Close();
            ResetInputState();
        }
    }

    private static PointerInputData CreateTouchInput(uint pointerId, Point position, bool inContact)
    {
        PointerPointProperties properties = new()
        {
            IsPrimary = true,
            PointerUpdateKind = inContact
                ? PointerUpdateKind.LeftButtonPressed
                : PointerUpdateKind.LeftButtonReleased,
        };
        PointerPoint point = new(
            pointerId,
            position,
            PointerDeviceType.Touch,
            inContact,
            properties,
            timestamp: 70,
            frameId: 1);
        return new PointerInputData(
            pointerId,
            PointerInputKind.Touch,
            point,
            position,
            ModifierKeys.None,
            IsInRange: true,
            IsCanceled: false,
            new StylusPointCollection([new StylusPoint(position.X, position.Y)]));
    }

    private static void ArmMouseInteraction(
        WindowInputDispatcher dispatcher,
        UIElement target,
        int timestamp)
    {
        dispatcher.UpdateMouseOverState(target, timestamp);
        Assert.True(target.CaptureMouse());
        dispatcher.HandleMouseDown(
            MouseButton.Left,
            new Point(6, 6),
            PressedButtons,
            ModifierKeys.None,
            clickCount: 1,
            timestamp: timestamp + 1);
    }

    private static ManipulationInertiaProcessor TrackRunningInertia(
        WindowInputDispatcher dispatcher,
        UIElement target)
    {
        Type sessionType = typeof(WindowInputDispatcher).GetNestedType(
            "PointerManipulationSession",
            BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("PointerManipulationSession was not found.");
        ConstructorInfo constructor = sessionType.GetConstructors(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single();
        object session = constructor.Invoke(
        [
            target,
            Point.Zero,
            0,
            ManipulationModes.All,
            false,
            null,
        ]);

        ManipulationDelta cumulative = new() { Scale = new Vector(1, 1) };
        ManipulationInertiaProcessor processor = new(
            target,
            Point.Zero,
            cumulative,
            Dispatcher.CurrentDispatcher);
        Assert.True(processor.Start(
            linearVelocity: new Vector(1, 0),
            angularVelocity: 0,
            expansionVelocity: Vector.Zero,
            translationBehavior: null,
            rotationBehavior: null,
            expansionBehavior: null));

        sessionType.GetProperty("InertiaProcessor")!.SetValue(session, processor);
        MethodInfo trackMethod = typeof(WindowInputDispatcher).GetMethod(
            "TrackInertialManipulation",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("TrackInertialManipulation was not found.");
        trackMethod.Invoke(dispatcher, [session, processor]);
        return processor;
    }

    private static (Grid Root, Border Leaf) CreateTree()
    {
        Border leaf = new()
        {
            Width = 64,
            Height = 48,
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        Grid root = new()
        {
            Width = 120,
            Height = 90,
        };
        root.Children.Add(leaf);
        return (root, leaf);
    }

    private static Window CreateWindow(UIElement content)
    {
        Window window = new()
        {
            TitleBarStyle = WindowTitleBarStyle.Native,
            Width = 120,
            Height = 90,
            Content = content,
        };
        Layout(window);
        return window;
    }

    private static void Layout(Window window)
    {
        window.Measure(new Size(120, 90));
        window.Arrange(new Rect(0, 0, 120, 90));
    }

    private static WindowInputDispatcher GetInputDispatcher(Window window)
    {
        FieldInfo field = typeof(Window).GetField(
            "_inputDispatcher",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Window input dispatcher was not found.");
        return (WindowInputDispatcher)(field.GetValue(window)
            ?? throw new InvalidOperationException("Window input dispatcher was null."));
    }

    private static T ReadWindowField<T>(Window window, string name)
    {
        FieldInfo field = typeof(Window).GetField(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Window field '{name}' was not found.");
        return (T)field.GetValue(window)!;
    }

    private static void InvokeWindowMethod(Window window, string name)
    {
        MethodInfo method = typeof(Window).GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Window method '{name}' was not found.");
        method.Invoke(window, null);
    }

    private static void ResetInputState()
    {
        Keyboard.Initialize();
        Keyboard.ClearFocus();
        UIElement.ForceReleaseMouseCapture();
        UIElement.ForceReleaseAllTouchCaptures();
        foreach (TouchDevice device in Touch.ActiveDevices.ToArray())
            Touch.UnregisterTouchPoint(device.Id);

        StylusDevice? stylusDevice = Tablet.CurrentStylusDevice;
        stylusDevice?.Capture(null);
        Tablet.CurrentStylusDevice = null;
        UIElement.SetStylusDirectlyOverElement(null);
        Mouse.OnMouseLeaveWindow();
    }
}

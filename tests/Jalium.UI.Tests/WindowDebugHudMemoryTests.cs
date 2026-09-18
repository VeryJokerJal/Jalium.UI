using System.Diagnostics;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Hosting;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class WindowDebugHudMemoryTests
{
    private static readonly FieldInfo s_debugHudOverlayField =
        typeof(Window).GetField(
            "_debugHudOverlay",
            BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Window._debugHudOverlay was not found.");

    private static readonly FieldInfo s_debugHudField =
        typeof(Window).GetField(
            "_debugHud",
            BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Window._debugHud was not found.");

    private static readonly FieldInfo s_debugHudIntervalStopwatchField =
        s_debugHudField.FieldType.GetField(
            "_intervalSw",
            BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Window.RenderDebugHud._intervalSw was not found.");

    private static readonly FieldInfo s_handleField =
        typeof(Window).GetField(
            "<Handle>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Window.Handle backing field was not found.");

    private static readonly MethodInfo s_onNativeDestroyedMethod =
        typeof(Window).GetMethod(
            "OnNativeDestroyed",
            BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Window.OnNativeDestroyed was not found.");

    private static readonly MethodInfo s_cleanupApplicationMethod =
        typeof(Application).GetMethod(
            "Cleanup",
            BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Application.Cleanup was not found.");

    [Fact]
    public void F3OptIn_LazilyCreatesOverlay_AndReusesItWhileHidden()
    {
        Assert.Null(Application.Current);

        using JaliumApp app = AppBuilder.CreateBuilder(
            new AppBuilderSettings { DisableDefaults = true }).Build();
        Application application = app.Application;
        var window = new Window();

        try
        {
            var host = (IInputDispatcherHost)window;
            var dispatcher = new WindowInputDispatcher(host);

            Assert.False(host.CanToggleDebugHud);
            Assert.False(host.DebugHudEnabled);
            Assert.Null(GetDebugHudOverlay(window));

            Assert.False(dispatcher.HandleKeyDown(
                Key.F3,
                ModifierKeys.None,
                isRepeat: false,
                timestamp: 1));
            Assert.False(host.DebugHudEnabled);
            Assert.Null(GetDebugHudOverlay(window));

            app.UseDebugHud();
            Assert.True(host.CanToggleDebugHud);

            Assert.True(dispatcher.HandleKeyDown(
                Key.F3,
                ModifierKeys.None,
                isRepeat: false,
                timestamp: 2));
            Assert.True(host.DebugHudEnabled);

            DebugHudOverlay firstOverlay = Assert.IsType<DebugHudOverlay>(
                GetDebugHudOverlay(window));
            Assert.Equal(Visibility.Visible, firstOverlay.Visibility);
            Assert.True(window.OverlayLayer.Children.Contains(firstOverlay));
            Assert.True(window.CurrentEmptyRenderingDemand.HasFlag(
                Window.EmptyRenderingDemand.DeveloperOverlay));

            Assert.True(dispatcher.HandleKeyDown(
                Key.F3,
                ModifierKeys.None,
                isRepeat: false,
                timestamp: 3));
            Assert.False(host.DebugHudEnabled);
            Assert.Equal(Visibility.Collapsed, firstOverlay.Visibility);

            Assert.True(dispatcher.HandleKeyDown(
                Key.F3,
                ModifierKeys.None,
                isRepeat: false,
                timestamp: 4));
            Assert.True(host.DebugHudEnabled);
            Assert.Same(firstOverlay, GetDebugHudOverlay(window));
            Assert.Equal(Visibility.Visible, firstOverlay.Visibility);
        }
        finally
        {
            window.Close();
            if (ReferenceEquals(Application.Current, application))
            {
                s_cleanupApplicationMethod.Invoke(application, null);
            }
        }
    }

    [Fact]
    public void Close_ReleasesCreatedDebugHudOverlay()
    {
        Assert.Null(Application.Current);

        using JaliumApp app = AppBuilder.CreateBuilder(
            new AppBuilderSettings { DisableDefaults = true }).Build();
        Application application = app.Application;
        var window = new Window();

        try
        {
            var host = (IInputDispatcherHost)window;
            var dispatcher = new WindowInputDispatcher(host);

            app.UseDebugHud();
            Assert.True(dispatcher.HandleKeyDown(
                Key.F3,
                ModifierKeys.None,
                isRepeat: false,
                timestamp: 1));

            DebugHudOverlay overlay = Assert.IsType<DebugHudOverlay>(
                GetDebugHudOverlay(window));
            Assert.True(window.OverlayLayer.Children.Contains(overlay));
            Assert.True(GetDebugHudIntervalStopwatch(window).IsRunning);

            bool closingSawLiveHud = false;
            bool closedSawReleasedHud = false;
            window.Closing += (_, _) =>
            {
                closingSawLiveHud =
                    ReferenceEquals(GetDebugHudOverlay(window), overlay) &&
                    window.OverlayLayer.Children.Contains(overlay) &&
                    host.DebugHudEnabled;
            };
            window.Closed += (_, _) =>
                closedSawReleasedHud = IsDebugHudReleased(window, overlay, host);

            window.Close();

            Assert.True(closingSawLiveHud);
            Assert.True(closedSawReleasedHud);
            AssertDebugHudReleased(window, overlay, host);

            host.DebugHudEnabled = true;
            host.DebugHudOverlayVisibility = Visibility.Visible;
            Assert.False(host.DebugHudEnabled);
            Assert.Null(GetDebugHudOverlay(window));
        }
        finally
        {
            window.Close();
            if (ReferenceEquals(Application.Current, application))
            {
                s_cleanupApplicationMethod.Invoke(application, null);
            }
        }
    }

    [Fact]
    public void NativeDestroy_ReleasesCreatedDebugHudOverlay_AndIsIdempotent()
    {
        Assert.Null(Application.Current);

        using JaliumApp app = AppBuilder.CreateBuilder(
            new AppBuilderSettings { DisableDefaults = true }).Build();
        Application application = app.Application;
        var window = new Window();

        try
        {
            var host = (IInputDispatcherHost)window;
            var dispatcher = new WindowInputDispatcher(host);

            app.UseDebugHud();
            Assert.True(dispatcher.HandleKeyDown(
                Key.F3,
                ModifierKeys.None,
                isRepeat: false,
                timestamp: 1));

            DebugHudOverlay overlay = Assert.IsType<DebugHudOverlay>(
                GetDebugHudOverlay(window));
            var hwnd = new nint(0xD15);
            s_handleField.SetValue(window, hwnd);

            int closedCount = 0;
            window.Closed += (_, _) => closedCount++;

            s_onNativeDestroyedMethod.Invoke(window, [hwnd]);

            Assert.Equal(nint.Zero, window.Handle);
            Assert.Equal(1, closedCount);
            AssertDebugHudReleased(window, overlay, host);

            s_onNativeDestroyedMethod.Invoke(window, [hwnd]);
            Assert.Equal(1, closedCount);
            AssertDebugHudReleased(window, overlay, host);
        }
        finally
        {
            window.Close();
            if (ReferenceEquals(Application.Current, application))
            {
                s_cleanupApplicationMethod.Invoke(application, null);
            }
        }
    }

    private static object? GetDebugHudOverlay(Window window)
        => s_debugHudOverlayField.GetValue(window);

    private static Stopwatch GetDebugHudIntervalStopwatch(Window window)
    {
        object debugHud = s_debugHudField.GetValue(window)
            ?? throw new InvalidOperationException("Window._debugHud was null.");
        return (Stopwatch)(s_debugHudIntervalStopwatchField.GetValue(debugHud)
            ?? throw new InvalidOperationException("Window.RenderDebugHud._intervalSw was null."));
    }

    private static bool IsDebugHudReleased(
        Window window,
        DebugHudOverlay overlay,
        IInputDispatcherHost host)
        => GetDebugHudOverlay(window) is null &&
           !window.OverlayLayer.Children.Contains(overlay) &&
           !window.CurrentEmptyRenderingDemand.HasFlag(
               Window.EmptyRenderingDemand.DeveloperOverlay) &&
           !host.DebugHudEnabled &&
           !host.CanToggleDebugHud &&
           window.OverlayLayer.RenderingDemanded is null &&
           !GetDebugHudIntervalStopwatch(window).IsRunning;

    private static void AssertDebugHudReleased(
        Window window,
        DebugHudOverlay overlay,
        IInputDispatcherHost host)
    {
        bool referenceCleared = GetDebugHudOverlay(window) is null;
        bool childRemoved = !window.OverlayLayer.Children.Contains(overlay);
        bool demandReleased = !window.CurrentEmptyRenderingDemand.HasFlag(
            Window.EmptyRenderingDemand.DeveloperOverlay);
        bool stateDisabled = !host.DebugHudEnabled;
        bool toggleDisabled = !host.CanToggleDebugHud;
        bool overlayCallbackReleased = window.OverlayLayer.RenderingDemanded is null;
        bool intervalStopwatchStopped = !GetDebugHudIntervalStopwatch(window).IsRunning;

        Assert.True(
            referenceCleared && childRemoved && demandReleased && stateDisabled &&
            toggleDisabled && overlayCallbackReleased && intervalStopwatchStopped,
            $"HUD close cleanup incomplete: referenceCleared={referenceCleared}, " +
            $"childRemoved={childRemoved}, demandReleased={demandReleased}, " +
            $"stateDisabled={stateDisabled}, toggleDisabled={toggleDisabled}, " +
            $"overlayCallbackReleased={overlayCallbackReleased}, " +
            $"intervalStopwatchStopped={intervalStopwatchStopped}.");
    }
}

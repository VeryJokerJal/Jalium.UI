using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Threading;
using Jalium.UI;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;
using LeXtudio.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace Jalium.UI.ShaderDemo;

internal static class JaliumDevFlowAgentExtensions
{
    public static JaliumDevFlowAgentService AddJaliumDevFlowAgent(this Application app, AgentOptions? options = null)
    {
        options ??= new AgentOptions();
        DevFlowAgentPortResolver.ApplyDefaultPort(options);

        var service = new JaliumDevFlowAgentService(options);
        try
        {
            service.Start();
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            var fallbackPort = GetFreePort();
            if (fallbackPort > 0 && fallbackPort != options.Port)
            {
                options.Port = fallbackPort;
                service = new JaliumDevFlowAgentService(options);
                service.Start();
            }
            else
            {
                throw;
            }
        }

        return service;
    }

    private static int GetFreePort()
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        catch
        {
            return 0;
        }
    }
}

internal sealed class JaliumDevFlowAgentService : DevFlowAgentServiceBase
{
    private readonly JaliumVisualTreeWalker _treeWalker = new();

    public JaliumDevFlowAgentService(AgentOptions? options = null)
        : base(options)
    {
    }

    protected override string AgentId => "LeXtudio.DevFlow.Agent";
    protected override string AgentName => "LeXtudio DevFlow Agent";
    protected override string FrameworkName => "jalium";

    protected override object GetCapabilities() => new
    {
        screenshots = false,
        elementScreenshots = false,
        selectorScreenshots = false,
        tap = true,
        scroll = false,
        drag = true,
        structuredErrors = true,
        appTheme = false,
        webview = false,
        webviewCdp = false,
        multiWindow = false
    };

    protected override Task<object?> GetThemeAsync() => Task.FromResult<object?>(null);
    protected override Task<object?> SetThemeAsync(string theme) => Task.FromResult<object?>(null);

    protected override Task<string?> GetApplicationNameAsync()
        => Task.FromResult(Application.Current?.GetType().Name);

    protected override Task<List<ElementInfo>> BuildTreeAsync()
    {
        if (Application.Current == null)
            return Task.FromResult(new List<ElementInfo>());

        List<ElementInfo>? result = null;
        Dispatcher.MainDispatcher?.Invoke(() => result = _treeWalker.WalkTree());
        return Task.FromResult(result ?? new List<ElementInfo>());
    }

    protected override Task<ElementInfo?> FindElementAsync(string id)
    {
        if (Application.Current == null)
            return Task.FromResult<ElementInfo?>(null);

        ElementInfo? result = null;
        Dispatcher.MainDispatcher?.Invoke(() => result = _treeWalker.FindElementById(id));
        return Task.FromResult(result);
    }

    protected override Task<List<ElementInfo>> QueryElementsAsync(string? type = null, string? automationId = null, string? text = null)
    {
        if (Application.Current == null)
            return Task.FromResult(new List<ElementInfo>());

        List<ElementInfo> result = new();
        Dispatcher.MainDispatcher?.Invoke(() =>
        {
            var roots = _treeWalker.WalkTree();
            var all = new List<ElementInfo>();
            foreach (var root in roots)
                Flatten(root, all);

            result = all.Where(e =>
                    (string.IsNullOrWhiteSpace(type) || string.Equals(e.Type, type, StringComparison.OrdinalIgnoreCase))
                    && (string.IsNullOrWhiteSpace(automationId) || string.Equals(e.AutomationId, automationId, StringComparison.OrdinalIgnoreCase))
                    && (string.IsNullOrWhiteSpace(text) || (e.Text?.Contains(text, StringComparison.OrdinalIgnoreCase) == true)))
                .ToList();
        });

        return Task.FromResult(result);
    }

    protected override Task<byte[]?> CaptureScreenshotAsync(string? elementId = null, string? selector = null)
        => Task.FromResult<byte[]?>(null);

    protected override Task<bool> TryTapAsync(string elementId)
    {
        if (Application.Current == null)
            return Task.FromResult(false);

        var result = false;
        Dispatcher.MainDispatcher?.Invoke(() =>
        {
            var target = _treeWalker.FindElementObjectById(elementId);
            if (target == null)
                return;

            result = TryTapTarget(target);
        });

        return Task.FromResult(result);
    }

    protected override Task<bool> TryScrollAsync(string elementId, double deltaX, double deltaY)
        => Task.FromResult(false);

    protected override Task<bool> TryFillAsync(string elementId, string text)
    {
        if (Application.Current == null)
            return Task.FromResult(false);

        var result = false;
        Dispatcher.MainDispatcher?.Invoke(() =>
        {
            var target = _treeWalker.FindElementObjectById(elementId);
            if (target == null)
                return;

            result = TrySetTextValue(target, text);
        });

        return Task.FromResult(result);
    }

    protected override Task<bool> TryClearAsync(string elementId)
        => TryFillAsync(elementId, string.Empty);

    protected override Task<bool> TryFocusAsync(string elementId)
    {
        if (Application.Current == null)
            return Task.FromResult(false);

        var result = false;
        Dispatcher.MainDispatcher?.Invoke(() =>
        {
            var target = _treeWalker.FindElementObjectById(elementId);
            if (target is UIElement element)
            {
                result = element.Focus();
            }
        });

        return Task.FromResult(result);
    }

    protected override Task<object?> TryKeyAsync(string? elementId, string? key, string? text)
    {
        if (Application.Current == null)
            return Task.FromResult<object?>(null);

        object? result = null;
        Dispatcher.MainDispatcher?.Invoke(() =>
        {
            if (string.IsNullOrWhiteSpace(elementId))
            {
                result = CreateSuccessResult(simulationMode: "semantic", elementId: null, key: key, text: text);
                return;
            }

            var target = _treeWalker.FindElementObjectById(elementId);
            if (target == null)
                return;

            if (!string.IsNullOrWhiteSpace(text) && TrySetTextValue(target, text))
            {
                result = CreateSuccessResult(simulationMode: "property", elementId: elementId, key: key, text: text);
                return;
            }

            if (target is UIElement element && element.Focus())
            {
                result = CreateSuccessResult(simulationMode: "semantic", elementId: elementId, key: key, text: text);
            }
        });

        return Task.FromResult(result);
    }

    protected override Task<object?> TryDragResponseAsync(DragRequest request)
    {
        if (Application.Current == null)
            return Task.FromResult<object?>(null);

        object? result = null;
        Dispatcher.MainDispatcher?.Invoke(() =>
        {
            var target = ResolveDragTarget(request);
            if (target is not Slider slider || !slider.IsEnabled)
                return;

            var nativeOnly = IsNativeOnlyDragEnabled();
            var oldValue = slider.Value;
            if (OperatingSystem.IsMacOS() && IsNativeDragEnabled() && TryApplyNativeMacDrag(slider, request, out var nativeDx, out var nativeDy))
            {
                if (Math.Abs(slider.Value - oldValue) > 1e-9)
                {
                    result = CreateSuccessResult(
                        simulationMode: "native",
                        elementId: slider.Name,
                        deltaX: nativeDx,
                        deltaY: nativeDy);
                    return;
                }

                if (nativeOnly)
                {
                    Console.Error.WriteLine("[DevFlow drag] native-only mode enabled; native mouse drag did not change slider value.");
                    return;
                }
            }

            if (nativeOnly)
            {
                Console.Error.WriteLine("[DevFlow drag] native-only mode enabled; semantic fallback is disabled.");
                return;
            }

            if (TryApplySliderDrag(slider, request, out var appliedDx, out var appliedDy))
            {
                result = CreateSuccessResult(
                    simulationMode: "semantic",
                    elementId: slider.Name,
                    deltaX: appliedDx,
                    deltaY: appliedDy);
            }
        });

        return Task.FromResult(result);
    }

    protected override Task<bool> TryBackAsync() => Task.FromResult(false);

    private object? ResolveDragTarget(DragRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.FromId))
        {
            var from = _treeWalker.FindElementObjectById(request.FromId!);
            if (from != null)
                return from;
        }

        if (!string.IsNullOrWhiteSpace(request.ToId))
        {
            var to = _treeWalker.FindElementObjectById(request.ToId!);
            if (to != null)
                return to;
        }

        return null;
    }

    private static bool TryApplySliderDrag(Slider slider, DragRequest request, out double appliedDx, out double appliedDy)
    {
        appliedDx = 0;
        appliedDy = 0;

        var range = slider.Maximum - slider.Minimum;
        if (range <= 0)
            return false;
        double dx;
        double dy;
        if (request.ToX.HasValue && request.FromX.HasValue)
        {
            dx = request.ToX.Value - request.FromX.Value;
            dy = (request.ToY ?? request.FromY ?? 0) - (request.FromY ?? 0);
        }
        else
        {
            dx = request.Dx ?? 0;
            dy = request.Dy ?? 0;
        }

        var oldValue = slider.Value;
        var trackTravel = slider.Orientation == Orientation.Horizontal
            ? Math.Max(1.0, slider.ActualWidth - 16.0)
            : Math.Max(1.0, slider.ActualHeight - 16.0);

        var deltaRatio = slider.Orientation == Orientation.Horizontal
            ? dx / trackTravel
            : -dy / trackTravel;

        var next = oldValue + deltaRatio * range;
        if (slider.IsSnapToTickEnabled && slider.TickFrequency > 0)
        {
            next = Math.Round(next / slider.TickFrequency) * slider.TickFrequency;
        }

        next = Math.Clamp(next, slider.Minimum, slider.Maximum);
        if (Math.Abs(next - oldValue) < 1e-9)
            return false;

        slider.Value = next;
        appliedDx = dx;
        appliedDy = dy;
        return true;
    }

    private static bool TryApplyNativeMacDrag(Slider slider, DragRequest request, out double appliedDx, out double appliedDy)
    {
        appliedDx = 0;
        appliedDy = 0;

        var window = Application.Current?.MainWindow;
        if (window == null)
            return false;

        if (slider.TransformToVisual(window) is not GeneralTransform transform)
            return false;

        var thumbCenter = transform.Transform(GetSliderThumbCenterLocal(slider));

        var dx = request.Dx ?? 0;
        var dy = request.Dy ?? 0;
        if (request.ToX.HasValue && request.FromX.HasValue)
        {
            dx = request.ToX.Value - request.FromX.Value;
            dy = (request.ToY ?? request.FromY ?? 0) - (request.FromY ?? 0);
        }

        var fromLocal = request.FromX.HasValue && request.FromY.HasValue && !request.Global
            ? new Point(request.FromX.Value, request.FromY.Value)
            : thumbCenter;
        var toLocal = request.ToX.HasValue && request.ToY.HasValue && !request.Global
            ? new Point(request.ToX.Value, request.ToY.Value)
            : new Point(fromLocal.X + dx, fromLocal.Y + dy);

        var fromScreen = request.Global && request.FromX.HasValue && request.FromY.HasValue
            ? new CGPoint(request.FromX.Value, request.FromY.Value)
            : ConvertClientPointToScreen(window, fromLocal);
        var toScreen = request.Global && request.ToX.HasValue && request.ToY.HasValue
            ? new CGPoint(request.ToX.Value, request.ToY.Value)
            : ConvertClientPointToScreen(window, toLocal);

        if (IsMouseDebugEnabled())
        {
            var sliderOrigin = transform.Transform(new Point(0, 0));
            Console.Error.WriteLine(
                $"[DevFlow drag geometry] sliderOrigin=({sliderOrigin.X:F1},{sliderOrigin.Y:F1}) size=({slider.ActualWidth:F1},{slider.ActualHeight:F1}) " +
                $"thumb=({thumbCenter.X:F1},{thumbCenter.Y:F1}) fromLocal=({fromLocal.X:F1},{fromLocal.Y:F1}) toLocal=({toLocal.X:F1},{toLocal.Y:F1})");
            Console.Error.WriteLine(
                $"[DevFlow drag geometry] windowLeftTop=({window.Left:F1},{window.Top:F1}) windowSize=({window.ActualWidth:F1},{window.ActualHeight:F1}) " +
                $"fromScreen=({fromScreen.X:F1},{fromScreen.Y:F1}) toScreen=({toScreen.X:F1},{toScreen.Y:F1})");
        }

        if (!PostNativeMacDrag(fromScreen, toScreen, request.Steps ?? 16))
            return false;

        appliedDx = toLocal.X - fromLocal.X;
        appliedDy = toLocal.Y - fromLocal.Y;
        return true;
    }

    private static Point GetSliderThumbCenterLocal(Slider slider)
    {
        const double thumbSize = 16.0;

        var min = slider.Minimum;
        var max = slider.Maximum;
        var range = max - min;
        var ratio = range > 0
            ? Math.Clamp((slider.Value - min) / range, 0.0, 1.0)
            : 0.0;

        if (slider.Orientation == Orientation.Horizontal)
        {
            var travel = Math.Max(1.0, slider.ActualWidth - thumbSize);
            var x = (thumbSize / 2.0) + (ratio * travel);
            var y = slider.ActualHeight / 2.0;
            return new Point(x, y);
        }

        var verticalTravel = Math.Max(1.0, slider.ActualHeight - thumbSize);
        var centerFromBottom = (thumbSize / 2.0) + (ratio * verticalTravel);
        var yFromTop = slider.ActualHeight - centerFromBottom;
        var xCenter = slider.ActualWidth / 2.0;
        return new Point(xCenter, yFromTop);
    }

    private static CGPoint ConvertClientPointToScreen(Window window, Point local)
    {
        double left;
        double bottom;
        double clientHeight;
        if (!TryGetWindowGeometryFromNative(window, out left, out bottom, out clientHeight))
        {
            left = double.IsNaN(window.Left) ? 0 : window.Left;
            bottom = double.IsNaN(window.Top) ? 0 : window.Top;
            clientHeight = window.ActualHeight > 0 ? window.ActualHeight : window.Height;
            if (double.IsNaN(clientHeight) || clientHeight <= 0)
                clientHeight = 0;
        }

        // Convert top-origin client coordinates to global bottom-left space for Quartz event posting.
        var globalX = left + local.X;
        var globalYFromBottom = bottom + (clientHeight - local.Y);

        // Quartz mouse APIs expect top-left screen coordinates.
        var mainDisplay = CGMainDisplayID();
        var bounds = CGDisplayBounds(mainDisplay);
        var globalYFromTop = bounds.Height - globalYFromBottom;
        return new CGPoint(globalX, globalYFromTop);
    }

    private static bool TryGetWindowGeometryFromNative(Window window, out double left, out double bottom, out double clientHeight)
    {
        left = 0;
        bottom = 0;
        clientHeight = 0;

        try
        {
            var platformWindowField = typeof(Window).GetField("_platformWindow", BindingFlags.Instance | BindingFlags.NonPublic);
            var platformWindow = platformWindowField?.GetValue(window);
            if (platformWindow == null)
                return false;

            var handleField = platformWindow.GetType().GetField("_handle", BindingFlags.Instance | BindingFlags.NonPublic);
            if (handleField?.GetValue(platformWindow) is not nint nativeWindowHandle || nativeWindowHandle == nint.Zero)
                return false;

            var nativeMethodsType = Type.GetType("Jalium.UI.Interop.NativeMethods, Jalium.UI.Interop", throwOnError: false);
            if (nativeMethodsType == null)
            {
                if (IsMouseDebugEnabled())
                    Console.Error.WriteLine("[DevFlow drag geometry] failed to resolve Jalium.UI.Interop.NativeMethods.");
                return false;
            }

            var getPosition = nativeMethodsType.GetMethod("WindowGetPosition", BindingFlags.Static | BindingFlags.NonPublic);
            var getClientSize = nativeMethodsType.GetMethod("WindowGetClientSize", BindingFlags.Static | BindingFlags.NonPublic);
            if (getPosition == null || getClientSize == null)
            {
                if (IsMouseDebugEnabled())
                    Console.Error.WriteLine("[DevFlow drag geometry] failed to resolve WindowGetPosition/WindowGetClientSize.");
                return false;
            }

            object?[] positionArgs = { nativeWindowHandle, 0, 0 };
            getPosition.Invoke(null, positionArgs);

            object?[] sizeArgs = { nativeWindowHandle, 0, 0 };
            getClientSize.Invoke(null, sizeArgs);

            left = Convert.ToDouble(positionArgs[1]);
            bottom = Convert.ToDouble(positionArgs[2]);
            clientHeight = Convert.ToDouble(sizeArgs[2]);
            return clientHeight > 0;
        }
        catch (Exception ex)
        {
            if (IsMouseDebugEnabled())
                Console.Error.WriteLine($"[DevFlow drag geometry] native geometry reflection failed: {ex.Message}");
            return false;
        }
    }

    private static bool PostNativeMacDrag(CGPoint from, CGPoint to, int steps)
    {
        try
        {
            if (!CGPreflightPostEventAccess())
                return false;

            var effectiveSteps = Math.Clamp(steps, 1, 120);
            var debug = IsMouseDebugEnabled();

            if (debug)
                LogMouseComparison("before-drag", from, to);

            CGWarpMouseCursorPosition(from);
            PostMouseEvent(kCGEventMouseMoved, from);
            if (debug)
                LogMouseComparison("after-warp-start", from, to);

            PostMouseEvent(kCGEventLeftMouseDown, from);
            if (debug)
                LogMouseComparison("after-mouse-down", from, to);

            for (var i = 1; i <= effectiveSteps; i++)
            {
                var t = i / (double)effectiveSteps;
                var p = new CGPoint(
                    from.X + (to.X - from.X) * t,
                    from.Y + (to.Y - from.Y) * t);
                PostMouseEvent(kCGEventLeftMouseDragged, p);
                if (debug && (i == 1 || i == effectiveSteps || i % 4 == 0))
                    LogMouseComparison($"drag-step-{i}", p, to);

                Thread.Sleep(4);
            }

            PostMouseEvent(kCGEventLeftMouseUp, to);
            if (debug)
                LogMouseComparison("after-mouse-up", to, to);

            CGWarpMouseCursorPosition(to);
            if (debug)
                LogMouseComparison("after-warp-end", to, to);

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void PostMouseEvent(uint eventType, CGPoint point)
    {
        nint evt = CGEventCreateMouseEvent(nint.Zero, eventType, point, 0);
        if (evt == nint.Zero)
            return;

        CGEventPost(kCGHIDEventTap, evt);
        CFRelease(evt);
    }

    private static bool IsMouseDebugEnabled()
        => string.Equals(Environment.GetEnvironmentVariable("JALIUM_DEVFLOW_MOUSE_DEBUG"), "1", StringComparison.Ordinal);

    private static bool IsNativeDragEnabled()
        => string.Equals(Environment.GetEnvironmentVariable("JALIUM_DEVFLOW_ENABLE_NATIVE_DRAG"), "1", StringComparison.Ordinal);

    private static bool IsNativeOnlyDragEnabled()
        => string.Equals(Environment.GetEnvironmentVariable("JALIUM_DEVFLOW_NATIVE_ONLY"), "1", StringComparison.Ordinal);

    private static void LogMouseComparison(string stage, CGPoint target, CGPoint finalTarget)
    {
        if (!TryGetCurrentMousePosition(out var actual))
        {
            Console.Error.WriteLine($"[DevFlow drag] {stage}: target=({target.X:F1}, {target.Y:F1}) actual=(n/a)");
            return;
        }

        var deltaX = actual.X - target.X;
        var deltaY = actual.Y - target.Y;
        var finalDeltaX = actual.X - finalTarget.X;
        var finalDeltaY = actual.Y - finalTarget.Y;

        Console.Error.WriteLine(
            $"[DevFlow drag] {stage}: target=({target.X:F1}, {target.Y:F1}) actual=({actual.X:F1}, {actual.Y:F1}) " +
            $"delta=({deltaX:F1}, {deltaY:F1}) finalDelta=({finalDeltaX:F1}, {finalDeltaY:F1})");
    }

    private static bool TryGetCurrentMousePosition(out CGPoint point)
    {
        point = default;
        nint evt = CGEventCreate(nint.Zero);
        if (evt == nint.Zero)
            return false;

        try
        {
            point = CGEventGetLocation(evt);
            return true;
        }
        finally
        {
            CFRelease(evt);
        }
    }

    private const uint kCGHIDEventTap = 0;
    private const uint kCGEventLeftMouseDown = 1;
    private const uint kCGEventLeftMouseUp = 2;
    private const uint kCGEventMouseMoved = 5;
    private const uint kCGEventLeftMouseDragged = 6;

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CGPoint
    {
        public CGPoint(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; }
        public double Y { get; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CGSize
    {
        public double Width { get; init; }
        public double Height { get; init; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CGRect
    {
        public CGPoint Origin { get; init; }
        public CGSize Size { get; init; }
        public double Width => Size.Width;
        public double Height => Size.Height;
    }

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static extern uint CGMainDisplayID();

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static extern CGRect CGDisplayBounds(uint display);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static extern int CGWarpMouseCursorPosition(CGPoint point);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static extern nint CGEventCreateMouseEvent(nint source, uint mouseType, CGPoint mouseCursorPosition, uint mouseButton);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static extern void CGEventPost(uint tap, nint @event);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static extern nint CGEventCreate(nint source);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static extern CGPoint CGEventGetLocation(nint @event);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CGPreflightPostEventAccess();

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(nint cf);

    private static bool TryTapTarget(object target)
    {
        if (target is ButtonBase button)
        {
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
            return true;
        }

        if (target is UIElement element)
        {
            return element.Focus();
        }

        return false;
    }

    private static bool TrySetTextValue(object target, string text)
    {
        if (target == null)
            return false;

        var stringValue = text;
        var type = target.GetType();

        var prop = type.GetProperty("Text", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
            ?? type.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (prop == null || !prop.CanWrite)
            return false;

        if (prop.PropertyType == typeof(string))
        {
            prop.SetValue(target, stringValue);
            return true;
        }

        if (prop.PropertyType == typeof(int) && int.TryParse(stringValue, out var intValue))
        {
            prop.SetValue(target, intValue);
            return true;
        }

        if (prop.PropertyType == typeof(double) && double.TryParse(stringValue, out var doubleValue))
        {
            prop.SetValue(target, doubleValue);
            return true;
        }

        return false;
    }

    private static void Flatten(ElementInfo root, List<ElementInfo> result)
    {
        result.Add(root);
        if (root.Children == null)
            return;

        foreach (var child in root.Children)
            Flatten(child, result);
    }
}

internal sealed class JaliumVisualTreeWalker : IVisualTreeWalker
{
    public List<ElementInfo> WalkTree()
    {
        if (Application.Current == null)
            return new List<ElementInfo>();

        var result = new List<ElementInfo>();
        var window = Application.Current.MainWindow;
        if (window != null)
        {
            var info = CreateElementInfo(window, null, 0);
            if (info != null)
                result.Add(info);
        }

        return result;
    }

    public ElementInfo? FindElementById(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        foreach (var root in WalkTree())
        {
            var found = FindElementById(root, id);
            if (found != null)
                return found;
        }

        return null;
    }

    public object? FindElementObjectById(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || Application.Current == null)
            return null;

        var window = Application.Current.MainWindow;
        if (window == null)
            return null;

        return FindElementObjectById(window, id);
    }

    private ElementInfo? CreateElementInfo(object element, string? parentId, int siblingIndex)
    {
        if (element == null)
            return null;

        var id = GetElementId(element) ?? CreateGeneratedId(parentId, element.GetType().Name, siblingIndex);
        var isVisible = element is UIElement ui ? ui.Visibility == Visibility.Visible : true;
        var isEnabled = element is UIElement uie ? uie.IsEnabled : true;
        var isFocused = element is UIElement uif ? uif.IsFocused : false;
        var opacity = element is UIElement uio ? uio.Opacity : 1.0;

        var elementInfo = new ElementInfo
        {
            Id = id,
            ParentId = parentId,
            Type = element.GetType().Name,
            FullType = element.GetType().FullName ?? string.Empty,
            Framework = "jalium",
            AutomationId = element is DependencyObject dependencyObject ? AutomationProperties.GetAutomationId(dependencyObject) : null,
            Text = GetElementText(element),
            IsVisible = isVisible,
            IsEnabled = isEnabled,
            IsFocused = isFocused,
            Opacity = opacity,
            NativeType = element.GetType().FullName,
            FrameworkProperties = GetFrameworkProperties(element)
        };

        if (element is DependencyObject dep)
        {
            var childCount = VisualTreeHelper.GetChildrenCount(dep);
            if (childCount > 0)
            {
                elementInfo.Children = new List<ElementInfo>();
                for (var i = 0; i < childCount; i++)
                {
                    var child = VisualTreeHelper.GetChild(dep, i);
                    if (child == null)
                        continue;

                    var childInfo = CreateElementInfo(child, elementInfo.Id, i);
                    if (childInfo != null)
                        elementInfo.Children.Add(childInfo);
                }
            }
        }

        return elementInfo;
    }

    private static object? FindElementObjectById(object element, string id)
    {
        if (string.Equals(GetElementId(element), id, StringComparison.OrdinalIgnoreCase))
            return element;

        if (element is not DependencyObject dep)
            return null;

        var count = VisualTreeHelper.GetChildrenCount(dep);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(dep, i);
            if (child == null) continue;
            var found = FindElementObjectById(child, id);
            if (found != null)
                return found;
        }

        return null;
    }

    private static ElementInfo? FindElementById(ElementInfo element, string id)
    {
        if (string.Equals(element.Id, id, StringComparison.OrdinalIgnoreCase))
            return element;

        if (element.Children != null)
        {
            foreach (var child in element.Children)
            {
                var found = FindElementById(child, id);
                if (found != null)
                    return found;
            }
        }

        return null;
    }

    private static string? GetElementId(object element)
    {
        if (element is DependencyObject dep)
        {
            var automationId = AutomationProperties.GetAutomationId(dep);
            if (!string.IsNullOrWhiteSpace(automationId))
                return automationId;
        }

        if (element is FrameworkElement fe)
        {
            if (!string.IsNullOrWhiteSpace(fe.Name))
                return fe.Name;

            var tag = fe.Tag as string;
            if (!string.IsNullOrWhiteSpace(tag))
                return tag;
        }

        return null;
    }

    private static string GetElementText(object element)
    {
        var text = GetStringProperty(element, "Text");
        if (!string.IsNullOrWhiteSpace(text)) return text;

        var content = GetPropertyValue(element, "Content");
        if (content is string s && !string.IsNullOrWhiteSpace(s)) return s;

        var header = GetStringProperty(element, "Header");
        if (!string.IsNullOrWhiteSpace(header)) return header;

        return string.Empty;
    }

    private static readonly string[] s_brushPropertyNames =
    [
        "Background", "Foreground", "BorderBrush", "Fill", "Stroke"
    ];

    private static Dictionary<string, string?> GetFrameworkProperties(object element)
    {
        var props = new Dictionary<string, string?>();

        if (element is FrameworkElement fe)
        {
            props["name"] = string.IsNullOrWhiteSpace(fe.Name) ? null : fe.Name;
            props["tag"] = fe.Tag as string;
        }

        if (element is DependencyObject dep)
        {
            var aid = AutomationProperties.GetAutomationId(dep);
            if (!string.IsNullOrWhiteSpace(aid))
                props["automationId"] = aid;
        }

        foreach (var brushProp in s_brushPropertyNames)
        {
            var brush = GetPropertyValue(element, brushProp);
            if (brush != null)
                props[char.ToLowerInvariant(brushProp[0]) + brushProp[1..]] = BrushToString(brush);
        }

        return props;
    }

    private static string? BrushToString(object brush)
    {
        var colorProp = brush.GetType().GetProperty("Color", BindingFlags.Public | BindingFlags.Instance);
        if (colorProp != null)
        {
            var color = colorProp.GetValue(brush);
            if (color != null)
            {
                var a = GetColorChannel(color, "A");
                var r = GetColorChannel(color, "R");
                var g = GetColorChannel(color, "G");
                var b = GetColorChannel(color, "B");
                if (a.HasValue && r.HasValue && g.HasValue && b.HasValue)
                {
                    return a.Value == 255
                        ? $"#{r.Value:X2}{g.Value:X2}{b.Value:X2}"
                        : $"#{a.Value:X2}{r.Value:X2}{g.Value:X2}{b.Value:X2}";
                }
            }
        }

        return brush.GetType().Name;
    }

    private static byte? GetColorChannel(object color, string channel)
    {
        var prop = color.GetType().GetProperty(channel, BindingFlags.Public | BindingFlags.Instance);
        if (prop == null) return null;
        var val = prop.GetValue(color);
        return val is byte b ? b : null;
    }

    private static string CreateGeneratedId(string? parentId, string typeName, int siblingIndex)
        => parentId == null ? $"{typeName}[{siblingIndex}]" : $"{parentId}/{typeName}[{siblingIndex}]";

    private static string? GetStringProperty(object target, string name)
        => GetPropertyValue(target, name) as string;

    private static object? GetPropertyValue(object target, string name)
    {
        var prop = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        return prop?.GetValue(target);
    }
}

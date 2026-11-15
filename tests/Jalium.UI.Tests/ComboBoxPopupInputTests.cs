using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Data;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class ComboBoxPopupInputTests
{
    [Fact]
    public void DataContextArrivingAfterBindings_PreservesInitialSelectedItem()
    {
        var comboBox = new ComboBox();
        comboBox.SetBinding(
            ItemsControl.ItemsSourceProperty,
            new Binding(nameof(InitialSelectionSource.Items)));
        comboBox.SetBinding(
            Selector.SelectedItemProperty,
            new Binding(nameof(InitialSelectionSource.SelectedItem)) { Mode = BindingMode.TwoWay });

        var source = new InitialSelectionSource();
        comboBox.DataContext = source;

        Assert.Same(source.Items, comboBox.ItemsSource);
        Assert.Equal("xUnit v3", comboBox.SelectedItem);
        Assert.Equal(0, comboBox.SelectedIndex);
        Assert.Equal("xUnit v3", comboBox.SelectionBoxItem);
    }

    [Fact]
    public void PopupWindow_MouseHoverAndClick_SelectsComboBoxItem()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var comboBox = new ComboBox
            {
                Width = 220,
                ItemsSource = new[] { "xUnit v3", "xUnit v2", "NUnit", "MSTest", "TUnit", "Fixie" },
                SelectedItem = "xUnit v3",
            };

            var host = new StackPanel { Width = 400, Height = 300 };
            host.Children.Add(comboBox);
            host.Measure(new Size(400, 300));
            host.Arrange(new Rect(0, 0, 400, 300));
            comboBox.IsDropDownOpen = true;

            var popup = Assert.IsType<Popup>(FindDescendant<Popup>(comboBox));
            var popupChild = Assert.IsAssignableFrom<FrameworkElement>(popup.Child);
            popupChild.Measure(new Size(220, double.PositiveInfinity));

            var popupRoot = new PopupRoot(popup, popupChild, isLightDismiss: false);
            using var popupWindow = new PopupWindow(new Window { Width = 400, Height = 300 }, popupRoot);
            var popupSize = new Size(220, popupChild.DesiredSize.Height);
            popupWindow.Measure(popupSize);
            popupWindow.Arrange(new Rect(0, 0, popupSize.Width, popupSize.Height));
            popupWindow.SetVisualBounds(new Rect(0, 0, popupSize.Width, popupSize.Height));
            if (!OperatingSystem.IsWindows())
                typeof(PopupWindow).GetField("_platformWindow", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(popupWindow, new InputPlatformWindow((int)popupSize.Width, (int)popupSize.Height));

            var itemsHost = Assert.IsAssignableFrom<Panel>(
                typeof(ItemsControl)
                    .GetProperty("ItemsHost", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(comboBox));
            var third = Assert.IsType<ComboBoxItem>(itemsHost.Children[2]);
            Assert.Same(third, comboBox.ItemContainerGenerator.ContainerFromIndex(2));
            Assert.Equal(2, comboBox.ItemContainerGenerator.IndexFromContainer(third));
            Assert.Equal("NUnit", comboBox.ItemContainerGenerator.ItemFromContainer(third));
            var position = third.TranslatePoint(
                new Point(third.RenderSize.Width / 2, third.RenderSize.Height / 2),
                popupWindow);

            InvokeMousePointerUpdate(popupWindow, position);
            Assert.True(third.IsMouseOver);
            Assert.True(third.IsHighlighted);

            InvokeMouse(popupWindow, "OnMouseButtonDown", 1, position, MouseButton.Left);
            InvokeMouse(popupWindow, "OnMouseButtonUp", 0, position, MouseButton.Left);

            Assert.Equal("NUnit", comboBox.SelectedItem);
            Assert.Equal("NUnit", comboBox.SelectionBoxItem);
            Assert.False(comboBox.IsDropDownOpen);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private static void InvokeMouse(
        PopupWindow popupWindow,
        string methodName,
        int buttonFlags,
        Point position,
        MouseButton? button = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            var type = methodName switch
            {
                "OnMouseButtonDown" => PlatformEventType.MouseDown,
                "OnMouseButtonUp" => PlatformEventType.MouseUp,
                _ => throw new InvalidOperationException($"Unsupported popup mouse method '{methodName}'."),
            };
            InvokePlatformMouse(popupWindow, type, position, button ?? MouseButton.Left);
            return;
        }
        var parameterTypes = button.HasValue
            ? new[] { typeof(MouseButton), typeof(nint), typeof(nint), typeof(ModifierKeys?), typeof(int) }
            : new[] { typeof(nint), typeof(nint), typeof(ModifierKeys?), typeof(bool) };
        var method = typeof(PopupWindow).GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            types: parameterTypes,
            modifiers: null)
            ?? throw new InvalidOperationException($"Popup mouse method '{methodName}' was not found.");
        var packedPoint = PackClientPoint(position);

        if (button is { } changedButton)
        {
            method.Invoke(popupWindow, [changedButton, (nint)buttonFlags, packedPoint, null, 1]);
        }
        else
        {
            method.Invoke(popupWindow, [(nint)buttonFlags, packedPoint, null, false]);
        }
    }

    private static void InvokeMousePointerUpdate(PopupWindow popupWindow, Point position)
    {
        if (!OperatingSystem.IsWindows())
        {
            // Keep the WM_POINTER promotion regression on Windows, and cover
            // the native platform event route used by Linux/macOS popups.
            InvokePlatformMouse(popupWindow, PlatformEventType.MouseMove, position);
            return;
        }
        var properties = new PointerPointProperties
        {
            IsPrimary = true,
            PointerUpdateKind = PointerUpdateKind.Other,
        };
        var point = new PointerPoint(
            1,
            position,
            PointerDeviceType.Mouse,
            isInContact: false,
            properties,
            (ulong)Environment.TickCount,
            frameId: 0);
        var data = new PointerInputData(
            1,
            PointerInputKind.Mouse,
            point,
            position,
            ModifierKeys.None,
            IsInRange: true,
            IsCanceled: false,
            new StylusPointCollection([new StylusPoint(position.X, position.Y)]));
        var method = typeof(PopupWindow).GetMethod(
            "DispatchMousePointerUpdate",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Popup mouse-pointer update bridge was not found.");

        method.Invoke(popupWindow, [data]);
    }

    private static void InvokePlatformMouse(PopupWindow popupWindow, PlatformEventType type, Point position,
        MouseButton button = MouseButton.Left)
    {
        var parent = (Window)typeof(PopupWindow).GetField("_parentWindow", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(popupWindow)!;
        double scale = parent.DpiScale > 0 ? parent.DpiScale : 1;
        int nativeButton = button switch { MouseButton.Right => 1, MouseButton.Middle => 2,
            MouseButton.XButton1 => 3, MouseButton.XButton2 => 4, _ => 0 };
        typeof(PopupWindow).GetMethod("OnPlatformEvent", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(popupWindow, [new PlatformEvent { Type = type, MouseX = (float)(position.X * scale),
                MouseY = (float)(position.Y * scale), Button = nativeButton, ClickCount = 1 }]);
    }

    private static nint PackClientPoint(Point position)
    {
        var x = unchecked((ushort)(short)Math.Round(position.X));
        var y = unchecked((ushort)(short)Math.Round(position.Y));
        return (nint)(x | (y << 16));
    }

    private static T? FindDescendant<T>(Visual root) where T : Visual
    {
        if (root is T match)
        {
            return match;
        }

        for (var index = 0; index < root.VisualChildrenCount; index++)
        {
            if (root.GetVisualChild(index) is { } child && FindDescendant<T>(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    // The input fixture is arranged without showing a native surface. Supply
    // the platform lifetime required by OnPlatformEvent, while keeping native
    // creation and presentation in the separate host/protocol tests.
    private sealed class InputPlatformWindow(int width, int height) : IPlatformWindow
    {
        public nint NativeHandle => 0;
        public NativeSurfaceDescriptor GetSurface() => default;
        public void Show() { }
        public void Hide() { }
        public void Close() { }
        public void SetTitle(string title) { }
        public void Resize(int width, int height) { }
        public void Move(int x, int y) { }
        public int GetWidth() => width;
        public int GetHeight() => height;
        public void GetPosition(out int x, out int y) { x = y = 0; }
        public void SetMinMaxSize(int minWidth, int minHeight, int maxWidth, int maxHeight) { }
        public bool BeginMoveDrag() => false;
        public bool BeginResizeDrag(int edge) => false;
        public bool SetIcon(uint[]? pixels, int width, int height) => false;
        public bool SetTopmost(bool topmost) => false;
        public bool SetEnabled(bool enabled) => true;
        public bool SetOpacity(double opacity) => false;
        public bool SetShowInTaskbar(bool show) => false;
        public bool SetResizable(bool resizable) => false;
        public bool SetDecorated(bool decorated) => false;
        public bool SetOwner(nint owner) => false;
        public bool Activate() => false;
        public bool ShowSystemMenu(int x, int y) => false;
        public void SetState(WindowState state) { }
        public WindowState GetState() => WindowState.Normal;
        public void Invalidate() { }
        public float GetDpiScale() => 1;
        public int GetMonitorRefreshRate() => 60;
        public void SetCursor(int shape) { }
        public void UpdateImeContext(PlatformImeContext context) { }
        public void SetEventHandler(Action<PlatformEvent>? handler) { }
        public void Dispose() { }
    }

    private static void ResetApplicationState()
    {
        typeof(Application)
            .GetField("_current", BindingFlags.NonPublic | BindingFlags.Static)?
            .SetValue(null, null);
        typeof(ThemeManager)
            .GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static)?
            .Invoke(null, null);
    }

    private sealed class InitialSelectionSource
    {
        public IReadOnlyList<string> Items { get; } =
            ["xUnit v3", "xUnit v2", "NUnit", "MSTest", "TUnit", "Fixie"];

        public string SelectedItem { get; set; } = "xUnit v3";
    }
}

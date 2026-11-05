using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Input;

internal static class WindowKeyboardMenuProbe
{
    internal static void Attach(Window window, TextBox editor, Action<bool, string> check)
    {
        var source = new Geometry { Width = window.Width, Height = window.Height,
            Left = window.Left, Top = window.Top, State = window.WindowState };
        var properties = new[] { Window.WidthProperty, Window.HeightProperty, Window.LeftProperty,
            Window.TopProperty, Window.WindowStateProperty };
        var names = new[] { nameof(Geometry.Width), nameof(Geometry.Height), nameof(Geometry.Left),
            nameof(Geometry.Top), nameof(Geometry.State) };
        var bindings = properties.Select((property, i) => window.SetBinding(property,
            new Binding(names[i]) { Source = source, Mode = BindingMode.TwoWay })).ToArray();
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        window.PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.None || e.Key is not Key.F7 and not Key.F8 and not Key.F9) return;
            e.Handled = true;
            if (e.Key == Key.F8)
            {
                window.ResizeMode = window.ResizeMode == ResizeMode.NoResize ? ResizeMode.CanResize : ResizeMode.NoResize;
                return;
            }
            if (e.Key == Key.F9)
            {
                Console.WriteLine("[window-menu-open] requested");
                typeof(Window).GetMethod("ShowFallbackSystemMenu", fields)!.Invoke(window,
                    [new Point(Math.Max(0, window.Width - 8), Math.Max(0, window.Height - 8))]);
                var opened = (ContextMenu?)typeof(Window).GetField("_fallbackSystemMenu", fields)!.GetValue(window);
                var popup = opened == null ? null : typeof(ContextMenu).GetField("_popup", fields)!.GetValue(opened);
                Console.WriteLine("[window-menu-open] " + JsonSerializer.Serialize(new { open = opened?.IsOpen,
                    popupOpen = popup?.GetType().GetProperty("IsOpen")?.GetValue(popup),
                    external = popup?.GetType().GetField("_isUsingExternalWindow", fields)?.GetValue(popup) }));
                if (opened != null) opened.Closed += (_, _) => Console.WriteLine("[window-menu-open] closed");
                return;
            }
            var menu = (ContextMenu?)typeof(Window).GetField("_fallbackSystemMenu", fields)!.GetValue(window);
            var platform = typeof(Window).GetField("_platformWindow", fields)!.GetValue(window);
            bool bound = properties.Select((property, i) => ReferenceEquals(bindings[i],
                BindingOperations.GetBindingExpressionBase(window, property))).All(value => value);
            bool synced = Equal(source.Width, window.Width) && Equal(source.Height, window.Height) &&
                Equal(source.Left, window.Left) && Equal(source.Top, window.Top) && source.State == window.WindowState;
            check(bound, "窗口菜单保留位置、尺寸和状态绑定");
            check(synced, "窗口菜单同步位置、尺寸和状态绑定源");
            var record = new { menu = menu?.IsOpen == true, focus = ReferenceEquals(Keyboard.FocusedElement, editor)
                    ? "editor" : Keyboard.FocusedElement is MenuItem item ? item.Header?.ToString() : Keyboard.FocusedElement?.GetType().Name,
                state = window.WindowState.ToString(), mode = window.SizeToContent.ToString(),
                left = window.Left, top = window.Top, width = window.Width, height = window.Height,
                resizable = window.ResizeMode.ToString(), bound, synced,
                text = editor.Text,
                operations = platform?.GetType().GetProperty("KeyboardOperations")?.GetValue(platform)?.ToString() };
            Console.WriteLine("[window-menu] " + JsonSerializer.Serialize(record,
                new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals }));
        };
    }

    private static bool Equal(double left, double right) => left.Equals(right) || Math.Abs(left - right) < 0.000001;
    private sealed class Geometry
    {
        public double Width { get; set; }
        public double Height { get; set; }
        public double Left { get; set; }
        public double Top { get; set; }
        public WindowState State { get; set; }
    }
}

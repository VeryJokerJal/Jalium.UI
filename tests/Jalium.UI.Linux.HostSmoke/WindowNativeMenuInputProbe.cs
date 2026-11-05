using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;

internal static class WindowNativeMenuInputProbe
{
    internal static void Attach(Window window, TextBox editor)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object Platform() => typeof(Window).GetField("_platformWindow", flags)!.GetValue(window)!;
        bool Send() => (bool)Platform().GetType().GetMethod("ShowSystemMenu")!.Invoke(Platform(), [16, 24])!;
        void Report(string source, bool sent)
        {
            uint serial = 0;
            int x = 0, y = 0;
            if (sent)
            {
                nint handle = (nint)Platform().GetType().GetField("_handle", flags)!.GetValue(Platform())!;
                _ = LastMenu(handle, out x, out y, out serial);
            }
            Console.WriteLine("[window-native-menu] " + JsonSerializer.Serialize(new
            {
                source, sent, serial, x, y, text = editor.Text,
                focus = ReferenceEquals(Keyboard.FocusedElement, editor) ? "editor" : Keyboard.FocusedElement?.GetType().Name,
            }));
        }
        window.PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.None || e.Key is not Key.F2 and not Key.F3 and not Key.F4) return;
            e.Handled = true;
            if (e.Key == Key.F2) Report("keyboard", Send());
            else if (e.Key == Key.F3)
                window.Dispatcher.BeginInvoke(() => Report("queued", Send()));
            else
            {
                bool sent = false;
                var thread = new Thread(() => sent = Send());
                thread.Start();
                thread.Join();
                Report("background", sent);
            }
        };
        window.PreviewMouseUp += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Right) return;
            e.Handled = true;
            Report("pointer", Send());
        };
    }

    [DllImport("jalium.native.platform", EntryPoint = "jalium_test_wayland_get_last_system_menu", CallingConvention = CallingConvention.Cdecl)]
    private static extern int LastMenu(nint window, out int x, out int y, out uint serial);
}

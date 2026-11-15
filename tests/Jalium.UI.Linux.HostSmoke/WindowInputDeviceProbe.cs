using System.Runtime.InteropServices;
using System.Reflection;
using System.Text.Json;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Threading;

internal static class WindowInputDeviceProbe
{
    internal static void Attach(Window window, TextBox editor, Button button, bool watchSeat = false)
    {
        int repeats = 0, enters = 0, leaves = 0, clicks = 0;
        bool pending = false;
        button.MouseEnter += (_, _) => enters++;
        button.MouseLeave += (_, _) => leaves++;
        button.Click += (_, _) => clicks++;
        void Report(string phase)
        {
            var center = button.TranslatePoint(new Point(button.RenderSize.Width / 2, button.RenderSize.Height / 2), window);
            Console.WriteLine("[window-devices] " + JsonSerializer.Serialize(new
            {
                phase, caps = GetCapabilities(), text = editor.Text, repeats, enters, leaves, clicks,
                hover = button.IsMouseOver, pressed = button.IsPressed,
                capture = Mouse.Captured?.GetType().Name, leftButton = Mouse.LeftButton.ToString(),
                nativeA = (GetKeyState(0x41) & 0x8000) != 0,
                nativeControl = (GetKeyState(0x11) & 0x8000) != 0,
                focus = ReferenceEquals(Keyboard.FocusedElement, editor) ? "editor" : Keyboard.FocusedElement?.GetType().Name,
                width = window.Width, height = window.Height, buttonX = center.X, buttonY = center.Y,
            }));
        }
        if (watchSeat)
        {
            var observer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            observer.Tick += (_, _) =>
            {
                if (window.Handle == nint.Zero) { observer.Stop(); return; }
                Report("seat-watch");
            };
            observer.Start();
        }
        window.PreviewKeyDown += (_, e) =>
        {
            if (watchSeat && e.Key == Key.F8)
            {
                e.Handled = true;
                window.Close();
                return;
            }
            if (e.Key == Key.A && e.IsRepeat) repeats++;
            if (e.Key == Key.F7)
            {
                e.Handled = true;
                Report("snapshot");
                return;
            }
            if (e.Key == Key.F12)
            {
                e.Handled = true;
                editor.Focus();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                object platform = typeof(Window).GetField("_platformWindow", flags)!.GetValue(window)!;
                nint handle = (nint)platform.GetType().GetField("_handle", flags)!.GetValue(platform)!;
                if (Preedit(handle, "拼音未提交") != 0) throw new InvalidOperationException("Wayland text-input protocol is required.");
                Report("preedit");
                return;
            }
            if (e.Key is not Key.F10 and not Key.F11 || pending) return;
            e.Handled = true;
            bool keyboard = e.Key == Key.F10;
            uint original = GetCapabilities();
            if ((original & 3u) != 3u) throw new InvalidOperationException("A real Wayland pointer and keyboard seat is required.");
            pending = true;
            int phase = 0;
            string name = keyboard ? "keyboard" : "pointer";
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            timer.Tick += (_, _) =>
            {
                if (window.Handle == nint.Zero) { timer.Stop(); return; }
                if (phase == 0)
                {
                    Report(name + "-before-removal");
                    _ = SetCapabilities(original & ~(keyboard ? 2u : 1u));
                    Report(name + "-removed");
                }
                else if (phase < 3) Report(name + "-absent-" + phase);
                else if (phase == 3)
                {
                    _ = SetCapabilities(original);
                    Report(name + "-restoring");
                }
                else
                {
                    timer.Stop();
                    pending = false;
                    Report(name + "-restored");
                }
                phase++;
            };
            timer.Start();
            Report(name + "-armed");
        };
    }

    [DllImport("jalium.native.platform", EntryPoint = "jalium_test_wayland_get_seat_capabilities", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint GetCapabilities();
    [DllImport("jalium.native.platform", EntryPoint = "jalium_test_wayland_inject_seat_capabilities", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SetCapabilities(uint capabilities);
    [DllImport("jalium.native.platform", EntryPoint = "jalium_input_get_key_state", CallingConvention = CallingConvention.Cdecl)]
    private static extern short GetKeyState(int key);
    [DllImport("jalium.native.platform", EntryPoint = "jalium_test_wayland_inject_preedit", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Preedit(nint window, [MarshalAs(UnmanagedType.LPUTF8Str)] string text);
}

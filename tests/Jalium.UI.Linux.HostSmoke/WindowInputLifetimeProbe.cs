using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Threading;

internal static class WindowInputLifetimeProbe
{
    internal static void Attach(Window window, TextBox first, StackPanel content, TextBlock instructions, Action<string> status)
    {
        var second = new TextBox { Text = "新编辑器内容", MinHeight = 36, Margin = new Thickness(0, 0, 0, 16) };
        int index = content.Children.IndexOf(first) + 1;
        content.Children.Insert(index, new TextBlock { Text = "回调后的输入目标", Margin = new Thickness(0, 0, 0, 8) });
        content.Children.Insert(index + 1, second);
        Jalium.UI.Automation.AutomationProperties.SetName(second, "回调后的输入目标");
        instructions.Text = "F2 检查预编辑切换，F3 检查提交切换，F4 正常提交，F5 隐藏恢复；F12 预编辑，F6 提交，F8 窄窗口。";
        string mode = "idle";
        int starts = 0, updates = 0, ends = 0;
        void Report(string phase) => Console.WriteLine("[window-lifetime] " + JsonSerializer.Serialize(new
        {
            phase, mode, first = first.Text, second = second.Text,
            focus = ReferenceEquals(Keyboard.FocusedElement, first) ? "first" :
                ReferenceEquals(Keyboard.FocusedElement, second) ? "second" : Keyboard.FocusedElement?.GetType().Name,
            composing = InputMethod.IsComposing, starts, updates, ends,
            visible = window.Visibility == Visibility.Visible, width = window.Width, height = window.Height,
        }));
        void Reset(string next)
        {
            mode = "idle";
            InputMethod.CancelComposition();
            first.Text = "旧编辑器内容"; second.Text = "新编辑器内容";
            first.Focus(); first.CaretIndex = first.Text.Length;
            starts = updates = ends = 0;
            mode = next;
            Report("armed");
        }
        nint Handle()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            object platform = typeof(Window).GetField("_platformWindow", flags)!.GetValue(window)!;
            return (nint)platform.GetType().GetField("_handle", flags)!.GetValue(platform)!;
        }
        EventHandler started = (_, _) =>
        {
            starts++;
            if (mode == "start-focus") { mode = "done"; second.Focus(); Report("start-focus"); }
            else if (mode == "start-hide")
            {
                mode = "done";
                window.Hide(); Report("hidden");
                var restore = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                restore.Tick += (_, _) =>
                {
                    restore.Stop(); window.Show(); window.Activate(); second.Focus(); Report("shown");
                };
                restore.Start();
            }
        };
        EventHandler<CompositionEventArgs> updated = (_, _) => updates++;
        EventHandler<CompositionResultEventArgs> ended = (_, args) =>
        {
            ends++;
            if (mode == "commit-focus" && args.Result != null)
            {
                mode = "done"; second.Focus(); Report("ending-focus");
            }
        };
        InputMethod.CompositionStarted += started;
        InputMethod.CompositionUpdated += updated;
        InputMethod.CompositionEnded += ended;
        window.Closed += (_, _) =>
        {
            InputMethod.CompositionStarted -= started;
            InputMethod.CompositionUpdated -= updated;
            InputMethod.CompositionEnded -= ended;
        };
        second.GotKeyboardFocus += (_, _) => status("focus: 回调后的输入目标");
        window.PreviewKeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.F2: Reset("start-focus"); break;
                case Key.F3: Reset("commit-focus"); break;
                case Key.F4: Reset("control"); break;
                case Key.F5: Reset("start-hide"); break;
                case Key.F6:
                    if (Commit(Handle(), "提交") != 0) throw new InvalidOperationException("Text-input commit is required.");
                    Report("committed"); break;
                case Key.F7: Report("snapshot"); break;
                case Key.F8:
                    window.Width = 380; window.Height = 360;
                    second.Focus(); second.BringIntoView(); Report("narrow"); break;
                case Key.F9: window.Close(); break;
                case Key.F12:
                    if (Preedit(Handle(), "拼音未提交") != 0) throw new InvalidOperationException("Text-input preedit is required.");
                    Report("preedit"); break;
                default: return;
            }
            e.Handled = true;
        };
    }

    [DllImport("jalium.native.platform", EntryPoint = "jalium_test_wayland_inject_preedit", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Preedit(nint window, [MarshalAs(UnmanagedType.LPUTF8Str)] string text);
    [DllImport("jalium.native.platform", EntryPoint = "jalium_test_wayland_inject_commit", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Commit(nint window, [MarshalAs(UnmanagedType.LPUTF8Str)] string text);
}

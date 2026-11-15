using System.Text.Json;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;

internal static class WindowHoverProbe
{
    internal static void Attach(Window window, TextBox editor, IReadOnlyList<Button> buttons, TextBlock instructions)
    {
        instructions.Text = "悬停禁用操作查看说明；移出窗口应取消提示。F7 记录悬停与焦点，F8 切换窄窗口。";
        var owner = buttons[0];
        var first = new TextBlock { Text = "禁用操作", Margin = new Thickness(8, 0, 16, 0) };
        var second = new TextBlock { Text = "悬停查看说明", Margin = new Thickness(0, 0, 8, 0) };
        var parts = new StackPanel { Orientation = Orientation.Horizontal };
        parts.Children.Add(first);
        parts.Children.Add(second);
        owner.Content = parts;
        owner.IsEnabled = false;
        var hint = new ToolTip { Content = "操作暂不可用 · 中文说明", InitialShowDelay = 400 };
        owner.ToolTip = hint;
        ToolTipService.SetShowOnDisabled(owner, true);

        buttons[1].ToolTip = new ToolTip { Content = "此提示已停用", InitialShowDelay = 400 };
        ToolTipService.SetIsEnabled(buttons[1], false);
        buttons[2].ToolTip = new ToolTip { Content = "正常操作说明", InitialShowDelay = 400 };

        int enters = 0, leaves = 0, openings = 0, closings = 0;
        owner.MouseEnter += (_, _) => enters++;
        owner.MouseLeave += (_, _) => leaves++;
        hint.Opened += (_, _) => { openings++; Console.WriteLine("[window-hover-event] opened"); };
        hint.Closed += (_, _) => { closings++; Console.WriteLine("[window-hover-event] closed"); };

        Point Center(FrameworkElement element) => element.TranslatePoint(
            new Point(element.RenderSize.Width / 2, element.RenderSize.Height / 2), window);
        window.PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.None) return;
            if (e.Key == Key.F8)
            {
                bool narrow = window.Width > 500;
                window.Width = narrow ? 360 : 720;
                window.Height = narrow ? 320 : 560;
                e.Handled = true;
                return;
            }
            if (e.Key != Key.F7) return;
            e.Handled = true;
            var a = Center(first);
            var b = Center(second);
            var suppressed = Center(buttons[1]);
            var enabled = Center(buttons[2]);
            Console.WriteLine("[window-hover] " + JsonSerializer.Serialize(new
            {
                open = hint.IsOpen, enters, leaves, openings, closings,
                disabledHover = owner.IsMouseOver, active = window.IsActive,
                focus = ReferenceEquals(Keyboard.FocusedElement, editor) ? "editor" : Keyboard.FocusedElement?.GetType().Name,
                text = editor.Text, width = window.Width, height = window.Height,
                first = new { x = a.X, y = a.Y }, second = new { x = b.X, y = b.Y },
                suppressed = new { x = suppressed.X, y = suppressed.Y },
                enabled = new { x = enabled.X, y = enabled.Y },
                suppressedOpen = (buttons[1].ToolTip as ToolTip)?.IsOpen,
                enabledOpen = (buttons[2].ToolTip as ToolTip)?.IsOpen,
            }));
        };
    }
}

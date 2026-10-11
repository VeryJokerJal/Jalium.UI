using AppKit;
using CoreGraphics;
using Foundation;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using ObjCRuntime;
using System.Diagnostics;

namespace Jalium.UI.MacOS;

// These cases drive only the owned native view. Actual foreground keyboard and
// accessibility acceptance uses the interactive observer and computer-use tools.
internal static class WindowMessageBoxChecks
{
    private const string Prefix = "--window-message-box-case=";
    private const int Count = 23;

    internal static int RunAll()
    {
        int passed = 0;
        for (int index = 0; index < Count; index++)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            start.ArgumentList.Add(Prefix + index);
            using var process = Process.Start(start)!;
            if (!process.WaitForExit(20_000))
            {
                process.Kill(); process.WaitForExit();
                Console.Error.WriteLine($"FAIL {index}: MessageBox timed out");
            }
            else if (process.ExitCode == 0) passed++;
        }
        Console.WriteLine($"macOS MessageBox host checks: {passed}/{Count} passed");
        return passed == Count ? 0 : 1;
    }

    internal static int RunCase(string argument)
    {
        if (!int.TryParse(argument.AsSpan(Prefix.Length), out int index) || (uint)index >= Count) return 2;
        var application = NSApplication.SharedApplication;
        application.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        using var host = new ValidationDelegate();
        application.Delegate = host; application.FinishLaunching();
        using (var launch = NSNotification.FromName(NSApplication.DidFinishLaunchingNotification, application))
            host.DidFinishLaunching(launch);
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        return Run(index);
    }

    internal static int Observe()
    {
        var application = NSApplication.SharedApplication;
        application.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        using var host = new ValidationDelegate();
        application.Delegate = host; application.FinishLaunching();
        using (var launch = NSNotification.FromName(NSApplication.DidFinishLaunchingNotification, application))
            host.DidFinishLaunching(launch);
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        var editor = new TextBox { Text = "消息框返回后继续编辑🙂", Height = 40 };
        AutomationProperties.SetName(editor, "消息框返回后的编辑框");
        var status = new TextBlock { Text = "打开消息框，按提示验证键盘和关闭行为。", TextWrapping = TextWrapping.Wrap, FontSize = 15 };
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "消息框选择与返回", FontSize = 22 });
        panel.Children.Add(status); panel.Children.Add(editor);
        var owner = new Window { Title = "Jalium MessageBox v150", Width = 700, Height = 650,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, TitleBarStyle = WindowTitleBarStyle.Native,
            Content = panel, Background = new SolidColorBrush(Color.FromRgb(0x1c, 0x20, 0x26)) };
        Application.Current!.MainWindow = owner;
        int actions = 0; bool closed = false;
        owner.Closed += (_, _) => closed = true;
        void Add(string label, string message, MessageBoxButton buttons, MessageBoxResult result, MessageBoxOptions options = MessageBoxOptions.None)
        {
            var button = new Button { Content = label, Height = 40, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 390 };
            button.Click += (_, _) =>
            {
                actions++; Console.WriteLine($"MESSAGE OPEN: action={actions}; buttons={buttons}; default={result}; options={options}");
                var returned = MessageBox.Show(owner, message, label + " v150", buttons, MessageBoxImage.Information, result, options);
                status.Text = $"{label} → {returned}"; owner.UpdateLayout();
                Console.WriteLine($"MESSAGE RETURN: action={actions}; result={returned}; enabled={owner.IsEnabled}; focus={FocusedName()}; text={editor.Text}");
            };
            panel.Children.Add(button);
        }
        Add("OK：按 Escape", "这条信息已显示。按 Escape 结束，再回到编辑框。", MessageBoxButton.OK, MessageBoxResult.OK);
        Add("确认取消：默认 Cancel，按 Return", "默认按钮是 Cancel。按 Return 应返回 Cancel。", MessageBoxButton.OKCancel, MessageBoxResult.Cancel);
        Add("Yes/No：尝试 Escape 与关闭", "Escape 与关闭都不能代替选择。请尝试后，明确选择 Yes 或 No。", MessageBoxButton.YesNo, MessageBoxResult.No);
        Add("Yes/No/Cancel：默认 No，按 Return", "默认按钮是 No。按 Return 应返回 No。", MessageBoxButton.YesNoCancel, MessageBoxResult.No);
        Add("长文本：滚动至末尾", LongText(), MessageBoxButton.OKCancel, MessageBoxResult.OK);
        Add("右对齐与从右向左", "مرحبا بالعالم\n文本方向与对齐独立设置。\n短行🙂", MessageBoxButton.OKCancel, MessageBoxResult.Cancel,
            MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading);
        var finish = new Button { Content = "关闭验收", Height = 40, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 390 };
        finish.Click += (_, _) => owner.Close(); panel.Children.Add(finish);
        owner.Show(); owner.UpdateLayout(); editor.Focus();
        var watch = Stopwatch.StartNew(); string? last = null;
        using var timer = NSTimer.CreateRepeatingTimer(.2, _ =>
        {
            var dialog = Application.Current!.Windows.Cast<Window>().FirstOrDefault(x => !ReferenceEquals(x, owner));
            if (dialog != null) dialog.UpdateLayout();
            string state = $"active={application.Active}; key={application.KeyWindow?.Title}; ownerEnabled={owner.IsEnabled}; dialog={dialog?.Title}; " +
                $"focus={FocusedName()}; size={dialog?.ActualWidth}x{dialog?.ActualHeight}; close={dialog?.IsShowCloseButton}";
            if (state != last) { last = state; Console.WriteLine("MESSAGE STATE: " + state); }
            if (!closed && watch.Elapsed.TotalSeconds < 900) return;
            if (!closed)
            {
                if (dialog != null) Buttons(dialog).Last().PerformClick();
                owner.Close();
            }
            NativeMethods.PlatformQuit(0);
        });
        NSRunLoop.Main.AddTimer(timer, NSRunLoopMode.Common);
        NSRunLoop.Main.AddTimer(timer, NSRunLoopMode.ModalPanel);
        application.Run(); timer.Invalidate();
        Console.WriteLine($"MESSAGE COMPLETE: actions={actions}; closed={closed}; text={editor.Text}");
        return 0;
    }

    private static int Run(int index)
    {
        var editor = new TextBox { Text = "owned message return", Height = 40 };
        var owner = new Window { Title = "Owned MessageBox owner", Width = 640, Height = 440, Content = editor,
            TitleBarStyle = WindowTitleBarStyle.Native };
        Application.Current!.MainWindow = owner;
        owner.Show(); owner.UpdateLayout(); editor.Focus();
        if (index == 22)
        {
            try
            {
                var view = Runtime.GetNSObject<NSView>(owner.Handle)!;
                var native = view.Window!;
                var size = view.Bounds.Size;
                for (int flags = 0; flags < 8; flags++)
                {
                    owner.IsShowCloseButton = (flags & 1) != 0;
                    owner.IsShowMinimizeButton = (flags & 2) != 0;
                    owner.IsShowMaximizeButton = (flags & 4) != 0;
                    owner.UpdateLayout();
                    Require((native.StandardWindowButton(NSWindowButton.CloseButton)?.Enabled == true) == owner.IsShowCloseButton,
                        "live native close visibility ignored");
                    Require((native.StandardWindowButton(NSWindowButton.MiniaturizeButton)?.Enabled == true) == owner.IsShowMinimizeButton,
                        "live native minimize visibility ignored");
                    Require((native.StandardWindowButton(NSWindowButton.ZoomButton)?.Enabled == true) == owner.IsShowMaximizeButton,
                        "live native zoom visibility ignored");
                    Require(Math.Abs((double)(view.Bounds.Width - size.Width)) < 1 &&
                        Math.Abs((double)(view.Bounds.Height - size.Height)) < 1, "caption toggle changed client dimensions");
                    Require(ReferenceEquals(Keyboard.FocusedElement, editor) && native.FirstResponder?.Handle == view.Handle,
                        "caption toggle lost keyboard or native responder focus");
                }
                owner.IsShowCloseButton = false;
                owner.Close(); Require(owner.Handle == nint.Zero, "hidden native close blocked programmatic teardown");
                Console.WriteLine("PASS 22: eight live native caption combinations retained client size and focus; programmatic close succeeded");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine($"FAIL 22: {error}"); return 1; }
            finally { owner.Close(); }
        }
        MessageBoxButton buttons = index switch
        {
            <= 2 => MessageBoxButton.OK,
            3 or 4 or 5 or 14 or 19 or 20 or 21 => MessageBoxButton.OKCancel,
            6 or 7 or 8 or 9 or 15 or 16 or 17 or 18 => MessageBoxButton.YesNo,
            _ => MessageBoxButton.YesNoCancel
        };
        MessageBoxResult defaultResult = index switch
        {
            4 or 12 => MessageBoxResult.Cancel,
            8 or 11 => MessageBoxResult.No,
            9 => MessageBoxResult.Cancel, // Not present in Yes/No: fall back to Yes.
            _ => MessageBoxResult.None
        };
        MessageBoxResult expected = index switch
        {
            <= 2 => MessageBoxResult.OK,
            5 or 19 or 20 or 21 => MessageBoxResult.OK,
            7 or 9 or 16 or 18 => MessageBoxResult.Yes,
            6 or 8 or 11 or 15 or 17 => MessageBoxResult.No,
            _ => MessageBoxResult.Cancel
        };
        string caption = $"Owned MessageBox {index}";
        Exception? failure = null; Window? observed = null; int closed = 0, phase = 0; bool acted = false;
        var watch = Stopwatch.StartNew();
        EventHandler<System.ComponentModel.CancelEventArgs> veto = (_, e) => e.Cancel = true;
        if (index == 18) owner.Closing += veto;
        using var timer = NSTimer.CreateRepeatingTimer(.1, _ =>
        {
            try
            {
                observed ??= Application.Current!.Windows.Cast<Window>().FirstOrDefault(x => x.Title == caption);
                if (observed == null || observed.Handle == nint.Zero || watch.Elapsed.TotalSeconds < .5) return;
                observed.UpdateLayout();
                if (phase == 0) { observed.Closed += (_, _) => closed++; phase = 1; }
                if (watch.Elapsed.TotalSeconds > 8) throw new InvalidOperationException("owned message did not complete");
                if (acted) return;
                acted = true;
                var choices = Buttons(observed).ToArray();
                Require(!owner.IsEnabled, "modal owner remained enabled");
                if (index is 0 or 3 or 10) Send(observed, 0x35, "\u001b");
                else if (index is 1 or 4 or 8 or 9 or 11 or 12) Send(observed, 0x24, "\r");
                else if (index is 2 or 13) observed.Close();
                else if (index == 6)
                {
                    Send(observed, 0x35, "\u001b");
                    Require(observed.Handle != nint.Zero, "Escape dismissed Yes/No");
                    choices[1].PerformClick();
                }
                else if (index is 7 or 16)
                {
                    if (index == 16)
                    {
                        observed.TitleBarStyle = WindowTitleBarStyle.Native; observed.UpdateLayout();
                        var native = Runtime.GetNSObject<NSView>(observed.Handle)!.Window!;
                        Require(native.StandardWindowButton(NSWindowButton.CloseButton)?.Enabled != true, "native Yes/No close button enabled");
                        native.PerformClose(null);
                    }
                    else observed.Close();
                    Require(observed.Handle != nint.Zero, "close dismissed Yes/No");
                    choices[0].PerformClick();
                }
                else if (index == 14)
                {
                    choices[0].Focus(); Send(observed, 0x30, "\t"); Send(observed, 0x31, " ");
                }
                else if (index == 15) { choices[1].Focus(); Send(observed, 0x31, " "); }
                else if (index == 17) owner.Close();
                else if (index == 18)
                {
                    owner.Close(); Require(owner.Handle != nint.Zero && observed.Handle != nint.Zero, "veto closed owner or dialog");
                    owner.Closing -= veto; choices[0].PerformClick();
                }
                else if (index == 19)
                {
                    var text = Descendants(observed.Content as Visual).OfType<TextBlock>().Single(x => x.Text.StartsWith("مرحبا"));
                    Require(text.TextAlignment == TextAlignment.Right && text.FlowDirection == FlowDirection.RightToLeft, "message options ignored");
                    choices[0].PerformClick();
                }
                else if (index == 20)
                {
                    var scroll = Descendants(observed.Content as Visual).OfType<ScrollViewer>().Single();
                    Require(scroll.ExtentHeight > scroll.ViewportHeight && scroll.ViewportHeight > 0, "long message not scrollable");
                    Require(observed.ActualHeight < SystemParameters.WorkArea.Height, "long message exceeds screen work area");
                    scroll.Focus(); Send(observed, 0x79, "\uF72D"); // Page Down.
                    Require(scroll.VerticalOffset > 0, "keyboard did not scroll long message");
                    choices[0].PerformClick();
                }
                else choices[0].PerformClick();
                Console.WriteLine($"MESSAGE ACTION {index}: phase={phase}; focus={FocusedName()}; dialog={observed.Handle}");
            }
            catch (Exception error)
            {
                failure = error;
                if (observed != null && observed.Handle != nint.Zero) Buttons(observed).Last().PerformClick();
                else if (owner.Handle != nint.Zero) owner.Close();
            }
        });
        NSRunLoop.Main.AddTimer(timer, NSRunLoopMode.Common);
        NSRunLoop.Main.AddTimer(timer, NSRunLoopMode.ModalPanel);
        try
        {
            MessageBoxOptions options = index == 19 ? MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading : MessageBoxOptions.None;
            string message = index == 20 ? LongText() : index == 19 ? "مرحبا بالعالم\n短行🙂" : "Owned selection 中🙂";
            MessageBoxResult result = MessageBoxResult.None;
            if (index == 21)
            {
                bool completed = false;
                NSApplication.SharedApplication.BeginInvokeOnMainThread(() =>
                {
                    result = MessageBox.Show(owner, message, caption, buttons, MessageBoxImage.Information, defaultResult, options);
                    completed = true;
                });
                while (!completed && watch.Elapsed.TotalSeconds < 10) NativeMethods.PlatformPollEvents();
                Require(completed, "main queue MessageBox did not return");
            }
            else result = MessageBox.Show(owner, message, caption, buttons, MessageBoxImage.Information, defaultResult, options);
            if (failure != null) throw failure;
            Require(acted && observed != null && observed.Handle == nint.Zero && closed == 1, "dialog teardown was incomplete or repeated");
            Require(result == expected, $"expected {expected}, got {result}");
            if (index != 17)
            {
                Require(owner.IsEnabled, "modal owner not restored");
                Require(editor.Text == "owned message return", "message changed owner text");
                Require(editor.Focus(), "owner editor focus not restored");
            }
            else Require(owner.Handle == nint.Zero, "owner close left a window");
            Console.WriteLine($"PASS {index}: buttons={buttons}; default={defaultResult}; result={result}; closed={closed}");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine($"FAIL {index}: {error}"); return 1; }
        finally
        {
            timer.Invalidate(); owner.Closing -= veto;
            if (observed?.Handle != nint.Zero && observed != null) Buttons(observed).Last().PerformClick();
            owner.Close();
        }
    }

    private static string LongText() => string.Join("\n", Enumerable.Range(1, 100).Select(n => $"第 {n} 行：保存记录与中文🙂，请检查自动换行和最后一行。")) + "\n最后一行：完整可读。";
    private static string FocusedName() => Keyboard.FocusedElement is Button button ? button.Content?.ToString() ?? "" : Keyboard.FocusedElement?.GetType().Name ?? "none";
    private static IEnumerable<Button> Buttons(Window dialog) => Descendants(dialog.Content as Visual).OfType<Button>();
    private static IEnumerable<Visual> Descendants(Visual? root)
    {
        if (root == null) yield break;
        yield return root;
        for (int i = 0; i < root.VisualChildrenCount; i++)
            foreach (var child in Descendants(root.GetVisualChild(i))) yield return child;
    }
    private static void Send(Window dialog, ushort code, string text)
    {
        var view = Runtime.GetNSObject<NSView>(dialog.Handle)!;
        using var down = NSEvent.KeyEvent(NSEventType.KeyDown, CGPoint.Empty, 0, 1, view.Window!.WindowNumber, null, text, text, false, code)!;
        using var up = NSEvent.KeyEvent(NSEventType.KeyUp, CGPoint.Empty, 0, 2, view.Window.WindowNumber, null, text, text, false, code)!;
        view.KeyDown(down); view.KeyUp(up);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class ValidationDelegate : JaliumMacApplicationDelegate
    {
        private bool _started;
        public override void DidFinishLaunching(NSNotification notification)
        {
            if (_started) return; _started = true; base.DidFinishLaunching(notification);
        }
        protected override JaliumApp CreateHostedApp() => AppBuilder.CreateBuilder(new AppBuilderSettings { DisableDefaults = true }).Build()
            .UseApplication(new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown });
    }
}

using AppKit;
using CoreGraphics;
using Foundation;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Threading;
using ObjCRuntime;
using System.Diagnostics;

namespace Jalium.UI.MacOS;

// Native sheets, dispatcher pumping, owner closure and confirmation protocol.
// Real file acceptance and multi-selection use the desktop tool separately.
internal static class WindowFileDialogValidationChecks
{
    private const int Count = 18;
    private const string Prefix = "--window-file-dialog-validation-case=";
    internal static int RunAll()
    {
        int passed = 0;
        for (int index = 0; index < Count; index++)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            start.Environment.Remove("JALIUM_MACOS_FILE_DIALOG_OBSERVE_SECONDS");
            start.ArgumentList.Add(Prefix + index);
            using var process = Process.Start(start)!;
            if (!process.WaitForExit(20_000))
            {
                process.Kill(); process.WaitForExit();
                Console.Error.WriteLine($"FAIL {index}: file dialog validation timed out");
            }
            else if (process.ExitCode == 0) passed++;
        }
        Console.WriteLine($"macOS Window file dialog validation checks: {passed}/{Count} passed");
        return passed == Count ? 0 : 1;
    }

    internal static int RunCase(string argument)
    {
        if (!int.TryParse(argument.AsSpan(Prefix.Length), out int index) || (uint)index >= Count) return 2;
        int behavior = index switch { 11 or 15 => 0, 12 or 16 => 5, 13 or 17 => 10, 14 => 1, _ => index };
        var application = NSApplication.SharedApplication;
        application.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        using var host = new ValidationDelegate();
        application.Delegate = host;
        application.FinishLaunching();
        if (PlatformFileDialogs.Show == null)
        {
            using var launch = NSNotification.FromName(NSApplication.DidFinishLaunchingNotification, application);
            host.DidFinishLaunching(launch);
        }
        string root = Path.Combine(Environment.GetEnvironmentVariable("JALIUM_MACOS_FILE_DIALOG_ROOT") ?? Path.GetTempPath(),
            "jalium-file-dialog-validation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "chosen.txt"), "owned selection");
        File.WriteAllText(Path.Combine(root, "chosen.png"), "owned selection");
        Directory.CreateDirectory(Path.Combine(root, "folder-a"));
        Directory.CreateDirectory(Path.Combine(root, "folder-b"));
        Window? window = null;
        try
        {
            int.TryParse(Environment.GetEnvironmentVariable("JALIUM_MACOS_FILE_DIALOG_OBSERVE_SECONDS"), out int seconds);
            if (seconds > 0) return Observe(root, seconds);
            RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
            NSWindow? nativeOwner = null;
            if (behavior != 2)
            {
                window = new Window { Title = "Jalium File Dialog Validation", Width = 640, Height = 480,
                    TitleBarStyle = behavior == 1 ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native,
                    Content = new TextBox { Text = "Owner remains editable after cancellation" } };
                Application.Current!.MainWindow = window;
                window.Show();
                nativeOwner = Runtime.GetNSObject<NSView>(window.Handle)!.Window!;
            }
            var dialog = new Microsoft.Win32.SaveFileDialog { InitialDirectory = root,
                FileName = "original.png", Filter = "Images|*.png|Text|*.txt", FilterIndex = 1 };
            if (index == 7)
            {
                using var existing = new NSWindow(new CGRect(0, 0, 200, 120), NSWindowStyle.Titled, NSBackingStore.Buffered, false);
                using var guard = CancelUnexpectedModal();
                nativeOwner!.BeginSheet(existing, _ => { });
                try { ExpectInvalidOwner(() => dialog.ShowDialog(window!)); }
                finally { guard.Invalidate(); nativeOwner.EndSheet(existing); existing.OrderOut(null); }
                Console.WriteLine($"PASS {index}: occupied sheet owner rejects a nested file panel");
                return 0;
            }
            if (index == 8)
            {
                window!.Hide();
                using var guard = CancelUnexpectedModal();
                try { ExpectInvalidOwner(() => dialog.ShowDialog(window)); }
                finally { guard.Invalidate(); }
                Console.WriteLine($"PASS {index}: hidden owner is rejected without entering a wait");
                return 0;
            }

            int fileOk = 0;
            dialog.FileOk += (_, args) =>
            {
                fileOk++;
                Require(dialog.FileName == Path.Combine(root, "candidate.txt"), "FileOk did not expose the candidate path");
                if (behavior == 10) window!.Close();
                if (behavior is 5 or 10) throw new InvalidOperationException("owned validation failure");
                if (index == 6)
                {
                    ExpectInvalidOwner(() => dialog.ShowDialog(window!));
                    args.Cancel = true;
                }
                else args.Cancel = fileOk == 1;
            };
            bool dispatched = false, queued = false;
            Exception? failure = null;
            NSSavePanel? observed = null;
            var watch = Stopwatch.StartNew();
            if (index == 9) Dispatcher.CurrentDispatcher.InvokeAsync(() => queued = true);
            using var timer = NSTimer.CreateRepeatingTimer(.1, tick =>
            {
                try
                {
                    observed ??= nativeOwner?.AttachedSheet as NSSavePanel ?? application.ModalWindow as NSSavePanel;
                    if (watch.Elapsed.TotalSeconds > 8) throw new InvalidOperationException("native file panel did not complete");
                    if (dispatched || observed == null || watch.Elapsed.TotalSeconds < 1 || !observed.IsVisible) return;
                    Require(behavior == 2 ? observed.SheetParent == null : observed.SheetParent?.Handle == nativeOwner!.Handle,
                        "file panel did not attach to the requested owner");
                    if (index == 9) Require(queued, "sheet wait did not service queued managed work");
                    dispatched = true;
                    if (index == 3) { window!.Close(); return; }
                    if (behavior is 4 or 5 or 6 or 10)
                    {
                        Require(observed.Delegate is NSObject delegateObject && delegateObject.RespondsToSelector(new Selector("panel:validateURL:error:")),
                            "native confirmation was not connected to FileOk");
                        using var candidate = NSUrl.FromFilename(Path.Combine(root, "candidate.txt"));
                        Require(!observed.Delegate!.ValidateUrl(observed, candidate, out _), "canceled confirmation was accepted");
                        Require(fileOk == 1 && dialog.FileName == "original.png" && dialog.FilterIndex == 1,
                            "canceled or exceptional validation changed published state");
                        if (behavior is 5 or 10) return; // The provider must cancel and rethrow the callback failure.
                        Require(observed.IsVisible && observed.SheetParent != null, "canceled confirmation closed the sheet");
                        if (index == 4)
                        {
                            Require(observed.Delegate.ValidateUrl(observed, candidate, out _), "retry could not pass validation");
                            Require(fileOk == 2, "retry did not raise one fresh FileOk");
                        }
                    }
                    observed.Cancel(observed);
                }
                catch (Exception error)
                {
                    failure = error;
                    Console.Error.WriteLine($"TIMER FAILURE {index}: {error.Message}");
                    if (observed != null) observed.Cancel(observed);
                    else application.StopModalWithCode((nint)NSModalResponse.Cancel);
                }
            });
            NSRunLoop.Main.AddTimer(timer, NSRunLoopMode.Common);
            NSRunLoop.Main.AddTimer(timer, NSRunLoopMode.ModalPanel);
            bool? result = null;
            bool rethrown = false;
            void Show()
            {
                try
                {
                    result = behavior == 1
                        ? new Microsoft.Win32.OpenFileDialog { InitialDirectory = root }.ShowDialog(window!)
                        : behavior == 2 ? dialog.ShowDialog(nint.Zero) : dialog.ShowDialog(window!);
                }
                catch (InvalidOperationException error) when (behavior is 5 or 10 && error.Message == "owned validation failure")
                {
                    result = false;
                    rethrown = true;
                }
            }
            if (index >= 11)
            {
                bool completed = false;
                Exception? showFailure = null;
                Action dispatch = () =>
                {
                    try { Show(); }
                    catch (Exception error) { showFailure = error; }
                    finally { completed = true; }
                };
                if (index >= 14) application.BeginInvokeOnMainThread(dispatch);
                else Dispatcher.CurrentDispatcher.InvokeAsync(dispatch);
                // Deliver the wake through the native main queue, matching AX
                // button activation. Do not manually drain the managed queue.
                while (!completed && watch.Elapsed.TotalSeconds < 12)
                    NSRunLoop.Main.RunUntil(NSDate.FromTimeIntervalSinceNow(.01));
                Require(completed, "main-queue dialog did not return");
                if (showFailure != null) throw showFailure;
            }
            else Show();
            timer.Invalidate();
            if (behavior == 10) NSRunLoop.Main.RunUntil(NSDate.FromTimeIntervalSinceNow(.1));
            if (failure != null) throw failure;
            Require(behavior is not (5 or 10) || rethrown, "FileOk exception was swallowed");
            Require(dispatched && observed != null, "native sheet was not observed");
            Require(result == false && dialog.FileName == "original.png" && dialog.FilterIndex == 1,
                "cancellation changed the original dialog state");
            Require(observed!.SheetParent == null && !observed.IsVisible, "completed panel remained attached or visible");
            if (behavior is 3 or 10) Require(window!.Handle == 0, "owner close did not release its native handle");
            Console.WriteLine($"PASS {index}: native owner, confirmation protocol and cancellation; fileOk={fileOk}; queued={queued}; mainQueueEntry={index >= 11}; nativeQueueEntry={index >= 14}");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine($"FAIL {index}: {error}"); return 1; }
        finally
        {
            if (window?.Handle != 0) window?.Close();
            Directory.Delete(root, true);
            using var notification = NSNotification.FromName(NSApplication.WillTerminateNotification, application);
            host.WillTerminate(notification);
            application.Delegate = null!;
        }
    }

    private static void ExpectInvalidOwner(Func<bool?> action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("invalid or reentrant owner entered another file panel");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static NSTimer CancelUnexpectedModal()
    {
        var timer = NSTimer.CreateRepeatingTimer(1.2, _ =>
        {
            if (NSApplication.SharedApplication.ModalWindow is NSSavePanel { IsVisible: true } panel) panel.Cancel(panel);
        });
        NSRunLoop.Main.AddTimer(timer, NSRunLoopMode.Common);
        NSRunLoop.Main.AddTimer(timer, NSRunLoopMode.ModalPanel);
        return timer;
    }

    private static int Observe(string root, int seconds)
    {
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        var status = new TextBlock { Text = "首次确认会被取消；修改后再次确认。", FontSize = 15,
            TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White };
        var editor = new TextBox { Text = "返回后继续输入🙂", Height = 40 };
        AutomationProperties.SetName(editor, "对话框返回后的编辑框");
        var stack = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
        stack.Children.Add(new TextBlock { Text = "窗口归属与取消校验", FontSize = 22, Foreground = Brushes.White });
        stack.Children.Add(status); stack.Children.Add(editor);
        var window = new Window { Title = "Jalium File Dialog v145", Width = 720, Height = 640, MinWidth = 600, MinHeight = 560,
            TitleBarStyle = WindowTitleBarStyle.Native, WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = new SolidColorBrush(Color.FromRgb(0x1c, 0x20, 0x26)), Content = new ScrollViewer { Content = stack } };
        Application.Current!.MainWindow = window;
        bool closed = false; int actions = 0;
        window.Closed += (_, _) => closed = true;
        void Report(Microsoft.Win32.FileDialog dialog, bool? accepted, string label)
        {
            if (accepted == true && dialog is Microsoft.Win32.SaveFileDialog)
            {
                Require(dialog.FileName.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal), "save left owned directory");
                File.WriteAllText(dialog.FileName, "owned validation save");
            }
            status.Text = $"{label}：{(accepted == true ? "已确认" : "已取消")}；文件 {Path.GetFileName(dialog.FileName)}；类型 {dialog.FilterIndex}。";
            Console.WriteLine($"OBSERVE RESULT {++actions}: {status.Text}; paths={string.Join(';', dialog.FileNames)}");
        }
        void Button(string title, Action action)
        {
            var button = new Button { Content = title, Height = 40, Width = 350, HorizontalAlignment = HorizontalAlignment.Left };
            button.Click += (_, _) => { action(); window.UpdateLayout(); };
            stack.Children.Add(button);
        }
        void Save(bool cancelAlways)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { InitialDirectory = root, FileName = "拒绝", DefaultExt = "png",
                Filter = "PNG 图片|*.png|文本|*.txt|所有文件|*.*", FilterIndex = 1, Title = "首次取消确认后继续修改" };
            int count = 0;
            dialog.FileOk += (_, args) =>
            {
                count++;
                args.Cancel = cancelAlways || count == 1;
                Console.WriteLine($"OBSERVE FILEOK: count={count}; cancel={args.Cancel}; name={Path.GetFileName(dialog.FileName)}; filter={dialog.FilterIndex}");
                status.Text = args.Cancel ? "校验已取消；请在同一面板修改文件名，再确认。" : "校验已通过。";
            };
            Report(dialog, dialog.ShowDialog(window), cancelAlways ? "拒绝后取消" : "拒绝后重试");
        }
        Button("保存：取消校验后修改并重试", () => Save(false));
        Button("保存：取消校验后退出", () => Save(true));
        Button("打开：多选并重试确认", () =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { InitialDirectory = root, Multiselect = true, FileName = "previous.txt" };
            int count = 0;
            dialog.FileOk += (_, args) =>
            {
                args.Cancel = ++count == 1;
                Console.WriteLine($"OBSERVE OPEN FILEOK: count={count}; cancel={args.Cancel}; paths={string.Join(';', dialog.FileNames)}");
            };
            Report(dialog, dialog.ShowDialog(window), "多选打开");
        });
        Button("文件夹：取消校验后重试", () =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = root, Multiselect = true, FolderName = "previous" };
            int count = 0;
            dialog.FolderOk += (_, args) =>
            {
                args.Cancel = ++count == 1;
                Console.WriteLine($"OBSERVE FOLDEROK: count={count}; cancel={args.Cancel}; paths={string.Join(';', dialog.FolderNames)}");
            };
            bool? accepted = dialog.ShowDialog(window);
            status.Text = $"文件夹：{(accepted == true ? "已确认" : "已取消")}；{string.Join(';', dialog.FolderNames.Select(Path.GetFileName))}。";
            Console.WriteLine($"OBSERVE RESULT {++actions}: {status.Text}; paths={string.Join(';', dialog.FolderNames)}");
        });
        Button("结束验证", window.Close);
        try
        {
            window.Show(); editor.Focus();
            Console.WriteLine($"OBSERVE READY: root={root}; window={window.Handle}");
            var elapsed = Stopwatch.StartNew();
            while (!closed && elapsed.Elapsed.TotalSeconds < seconds)
            {
                NativeMethods.PlatformPollEvents(); Dispatcher.CurrentDispatcher.ProcessQueue(); window.UpdateLayout();
                NSRunLoop.Main.RunUntil(NSDate.FromTimeIntervalSinceNow(.01));
            }
            Console.WriteLine($"OBSERVE COMPLETE: actions={actions}; text={editor.Text}; closed={closed}");
            return 0;
        }
        finally { if (!closed) window.Close(); }
    }

    private sealed class ValidationDelegate : JaliumMacApplicationDelegate
    {
        private bool _started;
        public override void DidFinishLaunching(NSNotification notification)
        {
            if (_started) return;
            _started = true; base.DidFinishLaunching(notification);
        }
        protected override JaliumApp CreateHostedApp() => AppBuilder.CreateBuilder(new AppBuilderSettings { DisableDefaults = true }).Build()
            .UseApplication(new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown });
    }
}

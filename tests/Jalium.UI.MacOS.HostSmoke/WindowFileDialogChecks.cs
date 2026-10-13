using AppKit;
using Foundation;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Threading;
using System.Diagnostics;

namespace Jalium.UI.MacOS;

// These cases exercise the registered provider, native panel configuration,
// filename delegate protocol and modal cancellation with fixture-owned files.
// Acceptance, overwrite prompts and keyboard/AX checks use the desktop tool.
internal static class WindowFileDialogChecks
{
    private const int Count = 15;
    private static readonly (string Name, string Pattern)[] Filters =
        [("PNG 图片", "*.png"), ("文本", "*.txt"), ("所有文件", "*.*")];

    internal static int RunAll()
    {
        int passed = 0;
        for (int index = 0; index < Count; index++)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            start.Environment.Remove("JALIUM_MACOS_FILE_DIALOG_OBSERVE_SECONDS");
            start.ArgumentList.Add($"--window-file-dialog-case={index}");
            using var process = Process.Start(start)!;
            if (!process.WaitForExit(25_000))
            {
                process.Kill(); process.WaitForExit();
                Console.Error.WriteLine($"FAIL {index}: file dialog case timed out");
            }
            else if (process.ExitCode == 0) passed++;
        }
        Console.WriteLine($"macOS Window file dialog host checks: {passed}/{Count} passed");
        return passed == Count ? 0 : 1;
    }

    internal static int RunCase(string argument)
    {
        if (!int.TryParse(argument.AsSpan("--window-file-dialog-case=".Length), out int index) || (uint)index >= Count) return 2;
        var application = NSApplication.SharedApplication;
        int.TryParse(Environment.GetEnvironmentVariable("JALIUM_MACOS_FILE_DIALOG_OBSERVE_SECONDS"), out int seconds);
        // Remote file panels require an application that can present UI.
        application.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        using var host = new FileDialogTestDelegate();
        application.Delegate = host;
        application.FinishLaunching();
        if (PlatformFileDialogs.Show == null)
        {
            using var launch = NSNotification.FromName(NSApplication.DidFinishLaunchingNotification, application);
            host.DidFinishLaunching(launch);
        }
        string root = Path.Combine(Environment.GetEnvironmentVariable("JALIUM_MACOS_FILE_DIALOG_ROOT") ?? Path.GetTempPath(),
            "jalium-file-dialog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "chosen.png"), "owned png placeholder");
            File.WriteAllText(Path.Combine(root, "chosen.txt"), "owned text");
            File.WriteAllText(Path.Combine(root, "chosen.custom"), "owned custom");
            File.WriteAllText(Path.Combine(root, "chosen.tar.gz"), "owned archive");
            File.WriteAllText(Path.Combine(root, "chosen.gz"), "owned compressed file");
            if (seconds > 0) return Observe(root, seconds);
            var options = new PlatformFileDialogOptions(
                index < 6 || index >= 12, index == 11, false, "Jalium File Dialog Contract", root,
                index switch { 4 => "note.custom", 6 => "chosen.png", 7 => "chosen.txt", 8 => "chosen.custom", _ => "note" },
                index == 5 ? "zip" : "txt", index != 2, true, true,
                index is 0 or 6 ? [] : index is 5 or 9 ? [("归档", "*.tar.gz")]
                    : index == 10 ? [("文件名", "chosen.?xt")] : Filters,
                index is 1 or 7 ? 2 : index is 3 or 8 ? 3 : 1);
            NSSavePanel? observed = null;
            string? confirmedFilename = null;
            Exception? failure = null;
            bool firstTick = true;
            bool reopening = false;
            var watch = Stopwatch.StartNew();
            using var timer = NSTimer.CreateRepeatingTimer(0.1, _ =>
            {
                try
                {
                    if (firstTick)
                    {
                        firstTick = false;
                        Console.WriteLine($"TIMER {index}: modal={application.ModalWindow?.GetType().FullName}");
                    }
                    observed ??= application.ModalWindow as NSSavePanel;
                    if (observed == null)
                    {
                        if (watch.Elapsed.TotalSeconds > 8) throw new InvalidOperationException("AppKit did not expose the owned modal panel");
                        return;
                    }
                    if (watch.Elapsed.TotalSeconds > 8) throw new InvalidOperationException("AppKit did not confirm the owned file name");
                    // NSSavePanel becomes modal while its remote view service is
                    // still starting. Cancelling in that phase opens a service-
                    // failure alert instead of exercising normal cancellation.
                    if (watch.Elapsed.TotalSeconds < 1 || !observed.IsVisible) return;
                    if (options.Save || index is 6 or 8 or 11)
                        Require(observed.AllowedContentTypes.Length == 0, "default/all-files/folder options restricted native content types");
                    if (index is 7 or 9 or 10)
                    {
                        Require(observed.Delegate != null, "native open panel has no exact file-name filter");
                        using var matching = NSUrl.FromFilename(Path.Combine(root, index == 9 ? "chosen.tar.gz" : "chosen.txt"));
                        using var other = NSUrl.FromFilename(Path.Combine(root, index == 9 ? "chosen.gz" : "chosen.png"));
                        using var directory = NSUrl.FromFilename(root);
                        Require(observed.Delegate!.ShouldEnableUrl(observed, matching), "matching file was disabled");
                        Require(!observed.Delegate.ShouldEnableUrl(observed, other), "nonmatching file was enabled");
                        Require(observed.Delegate.ShouldEnableUrl(observed, directory), "filter prevented directory navigation");
                    }
                    if (index >= 12)
                    {
                        var selector = observed.AccessoryView as NSPopUpButton;
                        Require(selector != null, "multiple file filters lost their native selector");
                        Require((int)selector!.IndexOfSelectedItem == 0, "panel reused a cancelled filter selection");
                        if (!reopening)
                        {
                            selector.SelectItem(1);
                            Require(application.SendAction(selector.Action!, selector.Target, selector), "native filter action was not dispatched");
                        }
                    }
                    if (options.Save && index != 13 && (index != 14 || reopening))
                    {
                        Require(observed.Delegate != null, "save filename customization was deferred until after native confirmation");
                        Require(observed.Delegate!.UserEnteredFilename(observed, options.FileName!, false) == options.FileName,
                            "unconfirmed filename was rewritten");
                        confirmedFilename = observed.Delegate.UserEnteredFilename(observed, options.FileName!, true);
                    }
                    observed.Cancel(observed);
                }
                catch (Exception error)
                {
                    Console.Error.WriteLine($"TIMER FAILURE {index}: {error.Message}");
                    failure = error;
                    if (observed != null) observed.Cancel(observed);
                    else application.StopModalWithCode((nint)NSModalResponse.Cancel);
                }
            });
            NSRunLoop.Main.AddTimer(timer, NSRunLoopMode.Common);
            NSRunLoop.Main.AddTimer(timer, NSRunLoopMode.ModalPanel);
            Console.WriteLine($"READY {index}: registered native provider; owned files at {root}");
            var paths = PlatformFileDialogs.Show!(options);
            if (index == 14 && failure == null)
            {
                Require(paths == null && options.SelectedFilterIndex == 1, "cancelled first panel changed its published filter");
                observed = null; firstTick = true; reopening = true; watch.Restart();
                paths = PlatformFileDialogs.Show!(options);
            }
            timer.Invalidate();
            if (failure != null) throw failure;
            Require(observed != null, "provider did not run an AppKit panel");
            Require(paths == null, "cancelled panel returned a selection");
            Require(options.SelectedFilterIndex == options.FilterIndex, "cancellation published a changed filter");
            if (options.Save && index != 13)
            {
                string expected = index switch
                {
                    2 => "note", 4 => "note.custom", 5 => "note.tar.gz", 14 => "note.png", _ => "note.txt"
                };
                Require(confirmedFilename == expected, $"unexpected confirmation filename: {confirmedFilename}");
            }
            Console.WriteLine($"PASS {index}: native panel configuration, filename protocol and cancellation");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"FAIL {index}: {error}");
            return 1;
        }
        finally
        {
            Directory.Delete(root, true);
            using var notification = NSNotification.FromName(NSApplication.WillTerminateNotification, application);
            host.WillTerminate(notification);
            application.Delegate = null!;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static int Observe(string root, int seconds)
    {
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        var result = new TextBlock { Text = "请选择一项检查。", FontSize = 16, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap };
        var editor = new TextBox { Text = "返回后继续输入🙂", Height = 40 };
        AutomationProperties.SetName(editor, "对话框返回后的编辑框");
        var stack = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
        stack.Children.Add(new TextBlock { Text = "原生文件对话框", FontSize = 22, Foreground = Brushes.White });
        stack.Children.Add(new TextBlock { Text = "检查文件类型、后缀与取消后重开。文件只保存在本次验证目录。", FontSize = 15,
            Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(result); stack.Children.Add(editor);
        var window = new Window { Title = "Jalium File Dialog v144", Width = 720, Height = 660,
            MinWidth = 600, MinHeight = 580, TitleBarStyle = WindowTitleBarStyle.Native,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = new SolidColorBrush(Color.FromRgb(0x1c, 0x20, 0x26)),
            Content = new ScrollViewer { Content = stack } };
        Application.Current!.MainWindow = window;
        bool closed = false; int actions = 0, fileOk = 0;
        window.Closed += (_, _) => closed = true;
        var persistent = new Microsoft.Win32.SaveFileDialog
        {
            Title = "选择保存类型", InitialDirectory = root, FileName = "记录🙂", DefaultExt = "png",
            Filter = "PNG 图片|*.png|文本|*.txt|所有文件|*.*", FilterIndex = 1
        };
        persistent.FileOk += (_, _) => fileOk++;
        void Report(Microsoft.Win32.FileDialog dialog, bool? accepted, string label)
        {
            if (accepted == true)
            {
                Require(Path.GetFullPath(dialog.FileName).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal),
                    "selection left the owned validation directory");
                if (dialog is Microsoft.Win32.SaveFileDialog) File.WriteAllText(dialog.FileName, "owned validation save");
            }
            result.Text = $"{label}：{(accepted == true ? "已确认" : "已取消")}；文件 {Path.GetFileName(dialog.FileName)}；类型 {dialog.FilterIndex}。";
            Console.WriteLine($"OBSERVE RESULT {++actions}: {result.Text}; fileOk={fileOk}; path={dialog.FileName}");
        }
        void Button(string label, Action action)
        {
            var button = new Button { Content = label, Height = 40, Width = 350, HorizontalAlignment = HorizontalAlignment.Left };
            button.Click += (_, _) => { action(); window.UpdateLayout(); };
            stack.Children.Add(button);
        }
        Button("保存并切换文件类型", () => Report(persistent, persistent.ShowDialog(window), "保存类型"));
        Button("保存无后缀文件", () =>
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { Title = "保留无后缀文件名", InitialDirectory = root,
                FileName = "无后缀", Filter = "文本|*.txt", DefaultExt = "txt", AddExtension = false };
            Report(dialog, dialog.ShowDialog(window), "无后缀保存");
        });
        Button("保存所有文件并使用默认后缀", () =>
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { Title = "使用默认后缀", InitialDirectory = root,
                FileName = "全部文件", Filter = "所有文件|*.*", DefaultExt = ".txt" };
            Report(dialog, dialog.ShowDialog(window), "默认后缀");
        });
        Button("打开文件并查看全部类型", () =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = "不限制打开文件类型", InitialDirectory = root, DefaultExt = ".txt" };
            Report(dialog, dialog.ShowDialog(window), "打开文件");
        });
        string existing = Path.Combine(root, "已有文件.txt"); File.WriteAllText(existing, "owned original");
        Button("检查已有文件的覆盖提示", () =>
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { Title = "检查覆盖确认", InitialDirectory = root,
                FileName = "已有文件", DefaultExt = "txt" };
            Report(dialog, dialog.ShowDialog(window), "覆盖检查");
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
            Console.WriteLine($"OBSERVE COMPLETE: actions={actions}; fileOk={fileOk}; text={editor.Text}; closed={closed}");
            return 0;
        }
        finally { if (!closed) window.Close(); }
    }

    private sealed class FileDialogTestDelegate : JaliumMacApplicationDelegate
    {
        private bool _started;
        public override void DidFinishLaunching(NSNotification notification)
        {
            if (_started) return;
            _started = true;
            base.DidFinishLaunching(notification);
        }

        protected override JaliumApp CreateHostedApp()
            => AppBuilder.CreateBuilder(new AppBuilderSettings { DisableDefaults = true }).Build()
                .UseApplication(new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown });
    }
}

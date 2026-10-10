using AppKit;
using Foundation;
using CoreGraphics;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Threading;
using ObjCRuntime;
using System.Diagnostics;

namespace Jalium.UI.MacOS;

// Native configuration and delegate protocol checks. Real checkbox, shortcut
// navigation, hidden-item selection and acceptance are verified through CUA.
internal static class WindowFileDialogOptionsChecks
{
    private const int Count = 28;
    private const string Prefix = "--window-file-dialog-options-case=";
    internal static int RunAll()
    {
        int passed = 0;
        for (int index = 0; index < Count; index++)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            start.Environment.Remove("JALIUM_MACOS_FILE_DIALOG_OPTIONS_OBSERVE_SECONDS");
            start.ArgumentList.Add(Prefix + index);
            using var process = Process.Start(start)!;
            if (!process.WaitForExit(20_000)) { process.Kill(); process.WaitForExit(); Console.Error.WriteLine($"FAIL {index}: options timed out"); }
            else if (process.ExitCode == 0) passed++;
        }
        Console.WriteLine($"macOS Window file dialog options checks: {passed}/{Count} passed");
        return passed == Count ? 0 : 1;
    }

    internal static int RunCase(string argument)
    {
        if (!int.TryParse(argument.AsSpan(Prefix.Length), out int index) || (uint)index >= Count) return 2;
        var application = NSApplication.SharedApplication;
        application.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        using var host = new ValidationDelegate(); application.Delegate = host; application.FinishLaunching();
        if (PlatformFileDialogs.Show == null)
        {
            using var launch = NSNotification.FromName(NSApplication.DidFinishLaunchingNotification, application); host.DidFinishLaunching(launch);
        }
        string root = Path.Combine(Environment.GetEnvironmentVariable("JALIUM_MACOS_FILE_DIALOG_ROOT") ?? Path.GetTempPath(),
            "jalium-file-dialog-options-" + Guid.NewGuid().ToString("N"));
        string inside = Path.Combine(root, "inside"), outside = root + "-outside";
        Directory.CreateDirectory(inside); Directory.CreateDirectory(outside);
        string file = Path.Combine(inside, "chosen.txt"), other = Path.Combine(outside, "chosen.txt");
        File.WriteAllText(file, "owned selection"); File.WriteAllText(other, "owned selection");
        File.WriteAllText(Path.Combine(inside, ".隐藏🙂.txt"), "owned hidden selection");
        Directory.CreateDirectory(Path.Combine(inside, ".hidden-folder"));
        Window? window = null;
        try
        {
            int.TryParse(Environment.GetEnvironmentVariable("JALIUM_MACOS_FILE_DIALOG_OPTIONS_OBSERVE_SECONDS"), out int seconds);
            if (seconds > 0) return Observe(root, inside, outside, seconds);
            RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
            window = new Window { Title = "Jalium File Dialog Options", Width = 640, Height = 480,
                TitleBarStyle = WindowTitleBarStyle.Native, Content = new TextBox { Text = "Owned option validation" } };
            Application.Current!.MainWindow = window; window.Show();
            var owner = Runtime.GetNSObject<NSView>(window.Handle)!.Window!;
            Microsoft.Win32.CommonItemDialog dialog = index is 1 or 14 ? new Microsoft.Win32.OpenFolderDialog { FolderName = "original" }
                : index is 2 or 19 or 21 or 22 or 26 ? new Microsoft.Win32.SaveFileDialog { FileName = index == 26 ? "导航记录🙂.txt" : "original.txt", Filter = "Text|*.txt|All|*.*" }
                : new Microsoft.Win32.OpenFileDialog { FileName = "original.txt", ShowReadOnly = index is 10 or 11 or 12 or 13 or 24,
                    ReadOnlyChecked = index is 10 or 18 };
            if (index == 24) ((Microsoft.Win32.FileDialog)dialog).Filter = "Text|*.txt|All|*.*";
            dialog.InitialDirectory = inside;
            dialog.ShowHiddenItems = index is 0 or 1 or 2;
            if (index is 4 or 5 or 6 or 7 or 8 or 14 or 19 or 21 or 22 or 24 or 25 or 26 or 27) dialog.RootDirectory = root;
            if (index == 4) { dialog.InitialDirectory = outside; dialog.DefaultDirectory = inside; }
            if (index == 15) { dialog.InitialDirectory = root + "-absent"; dialog.DefaultDirectory = inside; }
            if (index == 16) dialog.DefaultDirectory = outside;
            if (index == 17) { dialog.InitialDirectory = string.Empty; ((Microsoft.Win32.FileDialog)dialog).FileName = file; dialog.DefaultDirectory = outside; }
            if (index is 8 or 21 or 24 or 26 or 27)
            {
                dialog.CustomPlaces.Add(new Microsoft.Win32.FileDialogCustomPlace(inside));
                dialog.CustomPlaces.Add(new Microsoft.Win32.FileDialogCustomPlace(inside));
                dialog.CustomPlaces.Add(new Microsoft.Win32.FileDialogCustomPlace(outside));
                dialog.CustomPlaces.Add(new Microsoft.Win32.FileDialogCustomPlace(root + "-absent"));
                dialog.CustomPlaces.Add(new Microsoft.Win32.FileDialogCustomPlace(Guid.NewGuid()));
            }
            if (index == 9) dialog.CustomPlaces.Add(Microsoft.Win32.FileDialogCustomPlaces.Documents);
            int confirmations = 0;
            if (dialog is Microsoft.Win32.FileDialog fileDialog)
                fileDialog.FileOk += (_, args) =>
                {
                    confirmations++;
                    if (index is 11 or 12 or 13)
                    {
                        Require(((Microsoft.Win32.OpenFileDialog)dialog).ReadOnlyChecked, "FileOk did not publish read-only");
                        if (index == 12) throw new InvalidOperationException("owned read-only validation failure");
                        args.Cancel = index == 11;
                    }
                };
            if (dialog is Microsoft.Win32.OpenFolderDialog folderDialog) folderDialog.FolderOk += (_, _) => confirmations++;
            NSSavePanel? observed = null; Exception? failure = null; bool dispatched = false; int phase = 0; nint navigationPanel = 0;
            var watch = Stopwatch.StartNew();
            using var timer = NSTimer.CreateRepeatingTimer(.1, tick =>
            {
                try
                {
                    observed = owner.AttachedSheet as NSSavePanel ?? observed;
                    if (watch.Elapsed.TotalSeconds > 8) throw new InvalidOperationException("native options did not complete");
                    if (observed == null || !observed.IsVisible || watch.Elapsed.TotalSeconds < .6) return;
                    if (phase == 1)
                    {
                        Require(observed.Handle != navigationPanel, "navigation reused the previous native panel");
                        Require(observed.DirectoryUrl?.Path == root, "navigation did not return to root");
                        if (index is 24 or 26)
                        {
                            var retained = Descendants(observed.AccessoryView).ToArray();
                            Require(retained.OfType<NSPopUpButton>().First(popup => popup.ItemTitles().FirstOrDefault() == "Text").IndexOfSelectedItem == 1,
                                "navigation reset the selected filter");
                            if (index == 26) Require(observed.NameFieldStringValue == "导航记录🙂.txt", "navigation reset the save name");
                            else
                            {
                                Require(observed.ShowsHiddenFiles,
                                    "navigation reset hidden items");
                                Require(retained.OfType<NSButton>().First(button => button.Title == "只读打开").State == NSCellStateValue.On,
                                    "navigation reset read-only");
                                var place = retained.OfType<NSPopUpButton>().First(popup => popup.ItemTitles().FirstOrDefault() == "位置快捷方式");
                                place.SelectItem(2);
                                Require(NSApplication.SharedApplication.SendAction(place.Action!, place.Target!, place), "custom shortcut did not dispatch");
                                Require(place.IndexOfSelectedItem == 0, "custom shortcut did not reset");
                                navigationPanel = observed.Handle;
                                phase = 4; return;
                            }
                        }
                        phase = 2; dispatched = true; observed.Cancel(observed); return;
                    }
                    if (phase == 4)
                    {
                        Require(observed.Handle != navigationPanel, "custom navigation reused the previous native panel");
                        Require(observed.DirectoryUrl?.Path == inside, "custom shortcut did not navigate after reopening");
                        Require(observed.ShowsHiddenFiles,
                            "custom navigation reset hidden items");
                        phase = 2; dispatched = true; observed.Cancel(observed); return;
                    }
                    if (phase == 3)
                    {
                        Require(observed.DirectoryUrl?.Path == inside, "stale directory callback replaced the current valid directory");
                        phase = 2; dispatched = true; observed.Cancel(observed); return;
                    }
                    if (dispatched) return;
                    Require(observed.SheetParent?.Handle == owner.Handle, "panel owner missing");
                    var controls = Descendants(observed.AccessoryView).ToArray();
                    var readOnly = controls.OfType<NSButton>().FirstOrDefault(button => button.Title == "只读打开");
                    var shortcuts = controls.OfType<NSPopUpButton>().FirstOrDefault(popup => popup.ItemTitles().FirstOrDefault() == "位置快捷方式");
                    if (index <= 3 || index == 20) Require(observed.ShowsHiddenFiles == (index <= 2), "hidden option was ignored");
                    if (index is 4 or 15 or 16 or 17 or 23) Require(observed.DirectoryUrl?.Path == inside, $"directory precedence was ignored: {observed.DirectoryUrl?.Path}; expected {inside}");
                    if (index is 5 or 7 or 14 or 22)
                    {
                        using var own = NSUrl.FromFilename(index == 14 ? inside : file);
                        using var external = NSUrl.FromFilename(index == 14 ? outside : other);
                        Require(observed.Delegate != null, "delegate missing");
                        if (index != 22)
                        {
                            Require(observed.Delegate!.ShouldEnableUrl(observed, own), "inside item was disabled");
                            Require(!observed.Delegate!.ShouldEnableUrl(observed, external), "outside item was enabled");
                        }
                        if (index is 7 or 14 or 22)
                        {
                            Require(!observed.Delegate!.ValidateUrl(observed, external, out var error), "outside candidate was accepted");
                            Require(error != null && confirmations == 0, "outside candidate reached confirmation or lacked explanation");
                            error?.Dispose();
                        }
                    }
                    if (index is 6 or 19)
                    {
                        using var external = NSUrl.FromFilename(outside);
                        observed.Delegate!.DidChangeToDirectory(observed, external);
                        navigationPanel = observed.Handle;
                        phase = 1; return;
                    }
                    if (index == 25)
                    {
                        using var stale = NSUrl.FromFilename(outside);
                        using var current = NSUrl.FromFilename(inside);
                        observed.Delegate!.DidChangeToDirectory(observed, stale);
                        observed.Delegate.DidChangeToDirectory(observed, current);
                        phase = 3; return;
                    }
                    if (index is 8 or 21 or 24 or 26 or 27)
                    {
                        Require(shortcuts != null && shortcuts.ItemTitles().SequenceEqual(new[] { "位置快捷方式", Path.GetFileName(root), "inside" }), "places were ignored or not filtered");
                        if (index == 21) Require(controls.OfType<NSPopUpButton>().Count() == 2, "filter and places did not coexist");
                    }
                    if (index is 24 or 26 or 27)
                    {
                        if (index is 24 or 26)
                        {
                            var filter = controls.OfType<NSPopUpButton>().First(popup => popup.ItemTitles().FirstOrDefault() == "Text");
                            filter.SelectItem(1);
                            Require(NSApplication.SharedApplication.SendAction(filter.Action!, filter.Target!, filter), "filter did not dispatch before navigation");
                            if (index == 24)
                            {
                                readOnly!.State = NSCellStateValue.On;
                                observed.ShowsHiddenFiles = true;
                            }
                        }
                        shortcuts!.SelectItem(1);
                        Require(NSApplication.SharedApplication.SendAction(shortcuts.Action!, shortcuts.Target!, shortcuts), "root shortcut did not dispatch");
                        Require(shortcuts.IndexOfSelectedItem == 0, "root shortcut did not reset");
                        navigationPanel = observed.Handle;
                        if (index == 24)
                        {
                            using var previous = NSUrl.FromFilename(file);
                            Require(!observed.Delegate!.ValidateUrl(observed, previous, out _) && confirmations == 0,
                                "navigation confirmed the previous directory's selection");
                        }
                        if (index == 27) { dispatched = true; window.Close(); return; }
                        phase = 1; return;
                    }
                    if (index == 9) Require(shortcuts != null && shortcuts.ItemTitles().Contains(Path.GetFileName(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments))), "known place was ignored");
                    if (index is 10 or 11 or 12 or 13)
                    {
                        Require(readOnly != null, "read-only accessory missing");
                        if (index == 10) Require(readOnly!.State == NSCellStateValue.On, "initial read-only choice missing");
                        else
                        {
                            readOnly!.State = NSCellStateValue.On;
                            using var candidate = NSUrl.FromFilename(file);
                            Require(observed.Delegate!.ValidateUrl(observed, candidate, out _) == (index == 13), "read-only confirmation result mismatch");
                            Require(confirmations == 1, "read-only confirmation count mismatch");
                            Require(((Microsoft.Win32.OpenFileDialog)dialog).ReadOnlyChecked == (index == 13), "read-only veto/exception did not restore public state");
                            if (index == 12) { dispatched = true; return; }
                            Require(observed.IsVisible, "read-only veto closed the panel");
                        }
                    }
                    if (index == 18) Require(readOnly == null && ((Microsoft.Win32.OpenFileDialog)dialog).ReadOnlyChecked, "hidden read-only choice changed");
                    dispatched = true; observed.Cancel(observed);
                }
                catch (Exception error)
                {
                    failure = error; Console.Error.WriteLine($"TIMER FAILURE {index}: {error.Message}");
                    observed?.Cancel(observed);
                }
            });
            NSRunLoop.Main.AddTimer(timer, NSRunLoopMode.Common); NSRunLoop.Main.AddTimer(timer, NSRunLoopMode.ModalPanel);
            bool? result = null; bool rethrown = false;
            try { result = dialog.ShowDialog(window); }
            catch (InvalidOperationException error) when (index == 12 && error.Message == "owned read-only validation failure") { result = false; rethrown = true; }
            if (index == 23 && failure == null)
            {
                Require(dispatched && result == false && observed?.SheetParent == null, "first remembered session did not cancel");
                observed = null; dispatched = false; watch.Restart();
                dialog.InitialDirectory = string.Empty; dialog.DefaultDirectory = outside;
                ((Microsoft.Win32.FileDialog)dialog).FileName = string.Empty;
                result = dialog.ShowDialog(window);
            }
            timer.Invalidate();
            if (failure != null) throw failure;
            Require(dispatched && result == false, "panel did not cancel cleanly");
            Require(index != 12 || rethrown, "read-only event exception was swallowed");
            if (dialog is Microsoft.Win32.OpenFileDialog selected)
                Require(selected.ReadOnlyChecked == (index is 10 or 18), "cancellation did not restore initial read-only");
            Require(observed?.SheetParent == null && observed?.IsVisible == false, "sheet cleanup failed");
            Console.WriteLine($"PASS {index}: native option, navigation and read-only contract; confirmations={confirmations}"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine($"FAIL {index}: {error}"); return 1; }
        finally
        {
            if (window?.Handle != 0) window?.Close();
            Directory.Delete(root, true); Directory.Delete(outside, true);
            using var notification = NSNotification.FromName(NSApplication.WillTerminateNotification, application);
            host.WillTerminate(notification); application.Delegate = null!;
        }
    }

    private static IEnumerable<NSView> Descendants(NSView? view)
    {
        if (view == null) yield break;
        yield return view;
        foreach (var child in view.Subviews) foreach (var descendant in Descendants(child)) yield return descendant;
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private static int Observe(string root, string inside, string outside, int seconds)
    {
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        var status = new TextBlock { Text = "选中隐藏文件，勾选只读；首次确认取消后再试。", FontSize = 15, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White };
        var editor = new TextBox { Text = "对话框返回后继续编辑🙂", Height = 40 };
        AutomationProperties.SetName(editor, "文件选项返回后的编辑框");
        var stack = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
        stack.Children.Add(new TextBlock { Text = "文件范围与只读选择", FontSize = 22, Foreground = Brushes.White });
        stack.Children.Add(status); stack.Children.Add(editor);
        var window = new Window { Title = "Jalium File Options v147", Width = 720, Height = 640, MinWidth = 600, MinHeight = 560,
            TitleBarStyle = WindowTitleBarStyle.Native, WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = new SolidColorBrush(Color.FromRgb(0x1c, 0x20, 0x26)), Content = new ScrollViewer { Content = stack } };
        Application.Current!.MainWindow = window; bool closed = false; int actions = 0;
        window.Closed += (_, _) => closed = true;
        void Button(string title, Action action)
        {
            var button = new Button { Content = title, Height = 40, Width = 350, HorizontalAlignment = HorizontalAlignment.Left };
            button.Click += (_, _) => { action(); window.UpdateLayout(); }; stack.Children.Add(button);
        }
        void Open(bool cancelPanel)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { RootDirectory = root, InitialDirectory = outside, DefaultDirectory = inside,
                ShowHiddenItems = true, ShowReadOnly = true, FileName = "original.txt", Multiselect = true,
                Filter = "文本|*.txt|所有文件|*.*" };
            dialog.CustomPlaces.Add(new Microsoft.Win32.FileDialogCustomPlace(inside));
            dialog.CustomPlaces.Add(new Microsoft.Win32.FileDialogCustomPlace(outside));
            int count = 0;
            dialog.FileOk += (_, args) =>
            {
                args.Cancel = ++count == 1 || cancelPanel;
                Console.WriteLine($"OPTIONS OPEN FILEOK: count={count}; cancel={args.Cancel}; readOnly={dialog.ReadOnlyChecked}; paths={string.Join(';', dialog.FileNames)}");
            };
            bool? accepted = dialog.ShowDialog(window);
            status.Text = $"打开：{(accepted == true ? "已确认" : "已取消")}；只读 {dialog.ReadOnlyChecked}；{string.Join(';', dialog.SafeFileNames)}。";
            Console.WriteLine($"OPTIONS RESULT {++actions}: {status.Text}; paths={string.Join(';', dialog.FileNames)}");
        }
        Button("打开：隐藏文件与只读重试", () => Open(false));
        Button("打开：改变只读后取消", () => Open(true));
        Button("文件夹：指定范围与快捷位置", () =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { RootDirectory = root, InitialDirectory = outside, ShowHiddenItems = true, Multiselect = true };
            dialog.CustomPlaces.Add(new Microsoft.Win32.FileDialogCustomPlace(inside));
            bool? accepted = dialog.ShowDialog(window);
            status.Text = $"文件夹：{(accepted == true ? "已确认" : "已取消")}；{string.Join(';', dialog.SafeFolderNames)}。";
            Console.WriteLine($"OPTIONS RESULT {++actions}: {status.Text}; paths={string.Join(';', dialog.FolderNames)}");
        });
        Button("保存：指定范围与快捷位置", () =>
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { RootDirectory = root, InitialDirectory = outside, DefaultDirectory = inside,
                FileName = "范围记录🙂", Filter = "文本|*.txt|所有文件|*.*", ShowHiddenItems = true };
            dialog.CustomPlaces.Add(new Microsoft.Win32.FileDialogCustomPlace(inside));
            bool? accepted = dialog.ShowDialog(window);
            if (accepted == true)
            {
                Require(dialog.FileName.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal), "save left owned root");
                File.WriteAllText(dialog.FileName, "owned option save");
            }
            status.Text = $"保存：{(accepted == true ? "已确认" : "已取消")}；{dialog.SafeFileName}。";
            Console.WriteLine($"OPTIONS RESULT {++actions}: {status.Text}; paths={dialog.FileName}");
        });
        Button("结束验证", window.Close);
        try
        {
            window.Show(); editor.Focus(); Console.WriteLine($"OPTIONS READY: root={root}; inside={inside}; outside={outside}");
            var watch = Stopwatch.StartNew();
            var application = NSApplication.SharedApplication;
            string? lastState = null;
            using var timer = NSTimer.CreateRepeatingTimer(.02, tick =>
            {
                Dispatcher.CurrentDispatcher.ProcessQueue(); window.UpdateLayout();
                string state = $"running={application.Running}; active={application.Active}; key={application.KeyWindow?.Title}; modal={application.ModalWindow?.Title}";
                if (state != lastState) { lastState = state; Console.WriteLine($"OPTIONS APPLICATION: {state}"); }
                if (!closed && watch.Elapsed.TotalSeconds < seconds) return;
                if (!closed) window.Close();
                NativeMethods.PlatformQuit(0);
            });
            NSRunLoop.Main.AddTimer(timer, NSRunLoopMode.Common);
            NSRunLoop.Main.AddTimer(timer, NSRunLoopMode.ModalPanel);
            // AppKit activation, menu tracking and asynchronous panel callbacks
            // require the real application loop, not a nextEvent/sendEvent poll.
            application.Run();
            timer.Invalidate();
            Console.WriteLine($"OPTIONS COMPLETE: actions={actions}; text={editor.Text}; closed={closed}"); return 0;
        }
        finally { if (!closed) window.Close(); }
    }
    private sealed class ValidationDelegate : JaliumMacApplicationDelegate
    {
        private bool _started;
        public override void DidFinishLaunching(NSNotification notification) { if (_started) return; _started = true; base.DidFinishLaunching(notification); }
        protected override JaliumApp CreateHostedApp() => AppBuilder.CreateBuilder(new AppBuilderSettings { DisableDefaults = true }).Build()
            .UseApplication(new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown });
    }
}

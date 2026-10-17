using AppKit;
using Foundation;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using ObjCRuntime;
using System.Diagnostics;
using System.Text.Json;

namespace Jalium.UI.MacOS;

// Opt-in acceptance host. It never confirms, selects or edits a panel itself;
// eng/apple/verify-file-panel-acceptance.py drives a separate system AX client.
internal static class WindowFilePanelAcceptanceChecks
{
    private const string Prefix = "--window-file-panel-acceptance-case=";
    internal const int Count = 12;

    internal static int RunCase(string argument)
    {
        if (!int.TryParse(argument.AsSpan(Prefix.Length), out int index) || (uint)index >= Count) return 2;
        string output = Environment.GetEnvironmentVariable("JALIUM_FILE_PANEL_OUTPUT")
            ?? throw new InvalidOperationException("JALIUM_FILE_PANEL_OUTPUT is required.");
        Directory.CreateDirectory(output);
        string root = Path.Combine(output, "owned-files");
        Directory.CreateDirectory(root);
        string[] files = ["甲🙂.txt", "乙中文.txt", "丙重选.txt"];
        foreach (string name in files) File.WriteAllText(Path.Combine(root, name), "owned acceptance data");

        int kind = index / 2;
        bool save = kind is 0 or 1 or 4;
        bool validationRetry = kind is 1 or 3;
        bool cancelReopen = kind is 4 or 5;
        var app = NSApplication.SharedApplication;
        app.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        using var host = new ValidationDelegate();
        app.Delegate = host; app.FinishLaunching();
        using (var launch = NSNotification.FromName(NSApplication.DidFinishLaunchingNotification, app))
            host.DidFinishLaunching(launch);
        RenderContext.GetOrCreateCurrent(RenderBackend.Software, forceReplace: true);

        var checks = new List<string>();
        var failures = new List<string>();
        void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks.Add(message); Console.WriteLine("PASS " + message);
        }
        var editor = new TextBox { Text = "面板返回后继续编辑🙂" };
        var window = new Window
        {
            Title = "Jalium File Panel Acceptance", Width = 640, Height = 440,
            TitleBarStyle = index % 2 == 0 ? WindowTitleBarStyle.Native : WindowTitleBarStyle.Custom,
            Content = editor,
        };
        Application.Current!.MainWindow = window;
        Microsoft.Win32.FileDialog dialog = save
            ? new Microsoft.Win32.SaveFileDialog { FileName = "原始名字🙂.txt" }
            : new Microsoft.Win32.OpenFileDialog { Multiselect = true };
        dialog.InitialDirectory = root; dialog.Filter = "文本|*.txt"; dialog.FilterIndex = 1;
        string originalName = dialog.FileName ?? string.Empty;
        string[] originalNames = dialog.FileNames.ToArray();
        string[] ExpectedNames(bool changed) => save
            ? [changed ? "修改之后🙂.txt" : "原始名字🙂.txt"]
            : changed ? [files[1], files[2]] : [files[0], files[1]];
        void RequirePaths(string[] paths, string[] expected, string message)
        {
            Require(paths.All(path => Path.GetDirectoryName(path) == root) &&
                paths.Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal)
                    .SequenceEqual(expected.OrderBy(name => name, StringComparer.Ordinal)), message);
        }

        NSSavePanel? panel = null;
        int confirmations = 0, requests = 0, attempt = 0;
        bool? accepted = null;
        bool finished = false;
        Exception? failure = null;
        var watch = Stopwatch.StartNew();
        double attemptStarted = 0;
        void PublishExternal(string phase, string intent, bool changed)
        {
            ++requests;
            string path = Path.Combine(output, "external-ready.json");
            string json = JsonSerializer.Serialize(new
            {
                phase, intent, processId = Environment.ProcessId, index, confirmations, requests,
                expectedNames = ExpectedNames(changed), save, root,
            });
            File.WriteAllText(path + ".tmp", json); File.Move(path + ".tmp", path, true);
            Console.WriteLine($"EXTERNAL AX READY {phase}: own={Environment.ProcessId}; " +
                $"front={NSWorkspace.SharedWorkspace.FrontmostApplication?.ProcessIdentifier}; " +
                $"active={app.Active}; running={app.Running}");
        }
        dialog.FileOk += (_, args) =>
        {
            ++confirmations;
            RequirePaths(dialog.FileNames, ExpectedNames(validationRetry && confirmations == 2),
                "FileOk sees exactly the current native selection in the owned directory");
            args.Cancel = validationRetry && confirmations == 1;
        };
        using var timer = NSTimer.CreateRepeatingTimer(.1, _ =>
        {
            try
            {
                if (watch.Elapsed.TotalSeconds > 8)
                    throw new InvalidOperationException("native sheet acceptance exceeded its original budget");
                if (!window.IsVisible) return;
                var owner = Runtime.GetNSObject<NSView>(window.Handle)!.Window!;
                panel ??= owner.AttachedSheet as NSSavePanel;
                if (panel == null || !panel.IsVisible || watch.Elapsed.TotalSeconds - attemptStarted < 1) return;
                if (requests == attempt)
                {
                    Require(panel.SheetParent?.Handle == owner.Handle,
                        "native panel remains a sheet of the requested owner");
                    PublishExternal(attempt == 0 ? "first" : "reopen",
                        cancelReopen && attempt == 0 ? "cancel" : "confirm", changed: false);
                }
                else if (validationRetry && confirmations == 1 && requests == 1)
                {
                    Require(accepted == null && panel.IsVisible && panel.SheetParent?.Handle == owner.Handle,
                        "FileOk cancellation keeps the same native sheet open");
                    Require((dialog.FileName ?? string.Empty) == originalName &&
                        dialog.FileNames.SequenceEqual(originalNames) && dialog.FilterIndex == 1,
                        "canceled validation restores the previously published dialog state");
                    PublishExternal("retry", "confirm", changed: true);
                }
            }
            catch (Exception error)
            {
                failure = error; panel?.Cancel(panel);
                if (app.ModalWindow != null) app.StopModalWithCode((nint)NSModalResponse.Cancel);
            }
        });
        NSRunLoop.Main.AddTimer(timer, NSRunLoopMode.Common);
        NSRunLoop.Main.AddTimer(timer, NSRunLoopMode.ModalPanel);
        try
        {
            window.Show(); editor.Focus();
            app.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    accepted = dialog.ShowDialog(window);
                    if (failure != null) throw failure;
                    if (cancelReopen)
                    {
                        Require(accepted == false && confirmations == 0,
                            "native Cancel returns false without FileOk");
                        Require(panel != null && panel.SheetParent == null && !panel.IsVisible,
                            "canceled panel detaches before returning");
                        Require((dialog.FileName ?? string.Empty) == originalName &&
                            dialog.FileNames.SequenceEqual(originalNames) && dialog.FilterIndex == 1,
                            "native cancellation preserves published file and filter state");
                        panel = null; attempt = 1; attemptStarted = watch.Elapsed.TotalSeconds;
                        accepted = dialog.ShowDialog(window);
                    }
                }
                catch (Exception error) { failure = error; }
                finally
                {
                    finished = true; app.Stop(app);
                    using var wake = NSEvent.OtherEvent(NSEventType.ApplicationDefined, default, 0, 0, 0, null, 0, 0, 0);
                    app.PostEvent(wake, false);
                }
            });
            app.Run(); timer.Invalidate();
            if (failure != null) throw failure;
            Require(finished && accepted == true, "native confirmation completes ShowDialog with acceptance");
            Require(confirmations == (validationRetry ? 2 : 1) &&
                requests == (validationRetry || cancelReopen ? 2 : 1),
                "confirmation and cancellation counts match the external action protocol");
            Require(panel != null && panel.SheetParent == null && !panel.IsVisible,
                "accepted panel detaches and closes before returning");
            RequirePaths(dialog.FileNames, ExpectedNames(validationRetry),
                "accepted selection contains no stale canceled file");
            if (save) Require(!File.Exists(dialog.FileName), "choosing a save path does not write a file");
            else Require(dialog.FileNames.All(File.Exists), "accepted open selections remain real owned files");
            editor.Text = "确认完成后仍可编辑🙂";
            Require(editor.Focus() && editor.Text == "确认完成后仍可编辑🙂",
                "owner editor remains usable after acceptance");
            return 0;
        }
        catch (Exception error)
        {
            failures.Add(error.ToString()); Console.Error.WriteLine("FAIL " + error); return 1;
        }
        finally
        {
            timer.Invalidate(); if (window.Handle != 0) window.Close();
            File.WriteAllText(Path.Combine(output, "host-result.json"), JsonSerializer.Serialize(new
            {
                index, kind, checks, failures, confirmations, requests, finished, accepted,
                physicalInput = false, backend = "Software", processId = Environment.ProcessId,
                actualMainLoop = true, externalAccessibility = true,
            }));
            Directory.Delete(root, true);
            using var notification = NSNotification.FromName(NSApplication.WillTerminateNotification, app);
            host.WillTerminate(notification); app.Delegate = null!;
        }
    }

    private sealed class ValidationDelegate : JaliumMacApplicationDelegate
    {
        private bool _started;
        public override void DidFinishLaunching(NSNotification notification)
        {
            if (_started) return;
            _started = true; base.DidFinishLaunching(notification);
        }
        protected override JaliumApp CreateHostedApp() => AppBuilder.CreateBuilder(new AppBuilderSettings
            { DisableDefaults = true }).Build().UseApplication(new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown });
    }
}

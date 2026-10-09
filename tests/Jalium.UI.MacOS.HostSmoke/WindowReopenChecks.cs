using AppKit;
using Foundation;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using ObjCRuntime;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace Jalium.UI.MacOS;

/// <summary>Reopen delegate contracts with real windows and prohibited app activation; no Dock UI acceptance.</summary>
[SupportedOSPlatform("macos15.0")]
internal static class WindowReopenChecks
{
    private static readonly string[] Names =
    [
        "Hidden main window reopens without default untitled handling",
        "Disabled main window yields to an enabled independent window",
        "Closed main window yields to a live independent window",
        "Reveal callback closing the main window restores another window",
        "Existing visible native window preserves normal AppKit reopen handling",
        "Only disabled windows remain hidden without activation requests",
        "Miniaturized windows reopen despite AppKit visible-windows flag",
        "Nested reopen callback is contained without duplicate activation",
        "Stopped host ignores reopen requests",
        "Closing main window yields to a live independent window",
        "Miniaturized modal dialog reopens above its disabled visible owner"
    ];

    internal static int RunAll()
    {
        int failures = 0;
        for (int index = 0; index < Names.Length; index++)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            start.ArgumentList.Add($"--window-reopen-case={index}");
            using var process = Process.Start(start)!;
            if (!process.WaitForExit(30_000))
            {
                process.Kill();
                failures++;
                Console.Error.WriteLine($"FAIL: reopen case {index} timed out");
            }
            else if (process.ExitCode != 0) failures++;
        }
        Console.WriteLine($"macOS Window reopen host checks: {Names.Length - failures}/{Names.Length} passed");
        return failures == 0 ? 0 : 1;
    }

    internal static int RunCase(string argument)
    {
        if (!int.TryParse(argument.AsSpan("--window-reopen-case=".Length), out int index) || index < 0 || index >= Names.Length)
            return 2;
        JaliumMacApplication.Initialize();
        var nativeApplication = NSApplication.SharedApplication;
        nativeApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        var main = CreateWindow("Reopen main window");
        var other = CreateWindow("Reopen independent window");
        _ = new Application { MainWindow = main, ShutdownMode = ShutdownMode.OnExplicitShutdown };
        using var host = new MenuTestDelegate();
        try
        {
            main.Show();
            other.Show();
            var mainHandle = main.Handle;
            var otherHandle = other.Handle;
            other.Hide();
            if (index is not 6 and not 10) main.Hide();
            switch (index)
            {
                case 0:
                    Require(!host.ApplicationShouldHandleReopen(nativeApplication, false), "handled reopen permits default untitled behavior");
                    Require(IsVisible(main) && main.Handle == mainHandle && main.ActivationRequests == 1,
                        "main window was not restored once with the original handle");
                    Require(!IsVisible(other) && other.ActivationRequests == 0, "reopening main also revealed another window");
                    break;
                case 1:
                    main.IsEnabled = false;
                    host.ApplicationShouldHandleReopen(nativeApplication, false);
                    Require(!IsVisible(main) && main.Visibility == Visibility.Hidden && main.ActivationRequests == 0,
                        "disabled main window was revealed or activated");
                    Require(IsVisible(other) && other.Handle == otherHandle && other.ActivationRequests == 1,
                        "enabled independent window was not restored");
                    break;
                case 2:
                    main.Close();
                    host.ApplicationShouldHandleReopen(nativeApplication, false);
                    Require(IsVisible(other) && other.Handle == otherHandle && other.ActivationRequests == 1,
                        "closed main prevented reopening the independent window");
                    break;
                case 3:
                    main.OnReveal = main.Close;
                    host.ApplicationShouldHandleReopen(nativeApplication, false);
                    Require(main.Handle == 0 && IsVisible(other) && other.ActivationRequests == 1,
                        "main closing during reveal prevented the next candidate from reopening");
                    break;
                case 4:
                    main.Show();
                    Require(host.ApplicationShouldHandleReopen(nativeApplication, true), "visible windows lost normal AppKit handling");
                    Require(main.ActivationRequests == 0 && other.ActivationRequests == 0 && !IsVisible(other),
                        "visible-window reopen unexpectedly revealed or activated a hidden candidate");
                    break;
                case 5:
                    main.IsEnabled = false;
                    other.IsEnabled = false;
                    Require(host.ApplicationShouldHandleReopen(nativeApplication, false), "no-candidate reopen lost normal AppKit handling");
                    Require(main.Visibility == Visibility.Hidden && other.Visibility == Visibility.Hidden &&
                        main.ActivationRequests == 0 && other.ActivationRequests == 0,
                        "disabled candidates were revealed or activated");
                    break;
                case 6:
                    main.WindowState = WindowState.Maximized;
                    main.WindowState = WindowState.Minimized;
                    Require(WaitUntil(() => Native(main)?.IsMiniaturized == true), "native miniaturization did not finish");
                    host.ApplicationShouldHandleReopen(nativeApplication, true);
                    Require(WaitUntil(() => IsVisible(main) && Native(main)?.IsMiniaturized == false),
                        "miniaturized main was not restored when AppKit reports visible windows");
                    Require(main.ActivationRequests == 1 && main.WindowState == WindowState.Maximized &&
                        main.RestoreBounds.Width == 320 && main.RestoreBounds.Height == 240,
                        "reopen lost the pre-minimize state or normal RestoreBounds");
                    break;
                case 7:
                    bool nestedResult = true;
                    main.BeforeActivate = () => nestedResult = host.ApplicationShouldHandleReopen(nativeApplication, false);
                    host.ApplicationShouldHandleReopen(nativeApplication, false);
                    Require(!nestedResult && main.ActivationRequests == 1 && IsVisible(main),
                        "nested reopen entered window activation again");
                    break;
                case 8:
                    host.Dispose();
                    Require(!host.ApplicationShouldHandleReopen(nativeApplication, false), "stopped host permits default reopen behavior");
                    Require(main.ActivationRequests == 0 && other.ActivationRequests == 0 &&
                        main.Visibility == Visibility.Hidden && other.Visibility == Visibility.Hidden,
                        "stopped host revealed or activated a window");
                    break;
                case 9:
                    main.Closing += (_, _) => host.ApplicationShouldHandleReopen(nativeApplication, false);
                    main.Close();
                    Require(main.ActivationRequests == 0 && IsVisible(other) && other.ActivationRequests == 1,
                        "closing main window was selected instead of the live independent window");
                    break;
                case 10:
                    var dialog = CreateWindow("Reopen modal dialog");
                    dialog.Owner = main;
                    dialog.Shown += (_, _) =>
                    {
                        Require(!main.IsEnabled && !other.IsEnabled && dialog.IsEnabled && IsVisible(main),
                            "modal setup did not disable the still visible owner and independent window");
                        dialog.WindowState = WindowState.Minimized;
                        Require(WaitUntil(() => Native(dialog)?.IsMiniaturized == true), "modal miniaturization did not finish");
                        host.ApplicationShouldHandleReopen(nativeApplication, true);
                        Require(WaitUntil(() => IsVisible(dialog) && Native(dialog)?.IsMiniaturized == false),
                            "disabled visible owner prevented the minimized modal dialog from reopening");
                        Require(!main.IsEnabled && !other.IsEnabled && main.ActivationRequests == 0 &&
                            other.ActivationRequests == 0 && dialog.ActivationRequests == 1 && dialog.IsModal,
                            "reopening modal dialog changed owner enablement or activated another window");
                        dialog.DialogResult = true;
                    };
                    try { Require(dialog.ShowDialog() == true, "reopened modal dialog did not close with its accepted result"); }
                    finally { dialog.Close(); }
                    Require(main.IsEnabled && other.IsEnabled, "modal close did not restore prior window enablement");
                    break;
            }
            Console.WriteLine($"PASS: {Names[index]}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAIL: {Names[index]}: {exception}");
            return 1;
        }
        finally { main.Close(); other.Close(); }
    }

    private static ReopenWindow CreateWindow(string title) => new()
    {
        Title = title, Width = 320, Height = 240,
        TitleBarStyle = WindowTitleBarStyle.Native, ShowActivated = false,
        Background = new SolidColorBrush(Color.FromRgb(0xf5, 0xfa, 0xfc)),
        Content = new TextBlock { Text = title, Margin = new Thickness(16) }
    };

    private static NSWindow? Native(Window window) => window.Handle == 0 ? null : Runtime.GetNSObject<NSView>(window.Handle)?.Window;
    private static bool IsVisible(Window window) => window.Visibility == Visibility.Visible && Native(window)?.IsVisible == true;
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static bool WaitUntil(Func<bool> condition)
    {
        var timeout = Stopwatch.StartNew();
        while (!condition() && timeout.Elapsed < TimeSpan.FromSeconds(8))
        {
            NativeMethods.PlatformPollEvents();
            NSRunLoop.Main.RunUntil(NSDate.FromTimeIntervalSinceNow(0.01));
        }
        return condition();
    }

    private sealed class ReopenWindow : Window
    {
        internal int ActivationRequests;
        internal Action? BeforeActivate;
        internal Action? OnReveal;
        public override bool Activate()
        {
            ActivationRequests++;
            var callback = BeforeActivate;
            BeforeActivate = null;
            callback?.Invoke();
            return base.Activate();
        }
        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.Property == VisibilityProperty && Visibility == Visibility.Visible)
            {
                var callback = OnReveal;
                OnReveal = null;
                callback?.Invoke();
            }
        }
    }
}

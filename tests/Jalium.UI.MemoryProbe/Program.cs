using System.Diagnostics;
using System.Globalization;
using System.Text;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Threading;

namespace Jalium.UI.MemoryProbe;

internal static partial class Program
{
    private const int ExitSuccess = 0;
    private const int ExitFailure = 1;
    private const int ExitUsage = 2;
    private const int ExitNativeGuardFailure = 3;

    private static readonly List<Window> OpenWindows = [];
    private static readonly HashSet<Window> ShownWindows = [];

    private static ProbeOptions _options = null!;
    private static Application _application = null!;
    private static Window _mainWindow = null!;
    private static DispatcherTimer? _actionTimer;
    private static Stopwatch? _actionClock;
    private static int _nextActionIndex;
    private static int _nextWindowNumber;
    private static bool _initialPopulationStarted;
    private static bool _readyReported;
    private static bool _shuttingDown;

    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        string inheritedTrimPolicy =
            Environment.GetEnvironmentVariable("JALIUM_WORKING_SET_TRIM") ?? "<unset>";

        // The probe always measures the framework without working-set trimming.
        // This happens before Application construction, where the policy is read.
        Environment.SetEnvironmentVariable("JALIUM_WORKING_SET_TRIM", "off");

        try
        {
            _options = ProbeOptions.Parse(args);
            if (_options.ShowHelp)
            {
                Console.WriteLine(ProbeOptions.Usage);
                return ExitSuccess;
            }
        }
        catch (ArgumentException ex)
        {
            WriteError("USAGE", ex.Message);
            Console.Error.WriteLine(ProbeOptions.Usage);
            return ExitUsage;
        }

        Marker(
            $"START pid={Environment.ProcessId} size=800x600 windows={_options.WindowCount} " +
            $"inputLifecycleRounds={_options.InputLifecycleRounds} " +
            $"titlebar={(_options.NativeTitleBar ? "native" : "default-custom")} " +
            $"JALIUM_WORKING_SET_TRIM=off inheritedTrim={QuoteForMarker(inheritedTrimPolicy)}");

        try
        {
            _application = new Application
            {
                // Lifecycle actions may temporarily replace the main window.
                // The probe explicitly exits when its final window closes.
                ShutdownMode = ShutdownMode.OnExplicitShutdown,
            };

            // A core-only payload silently skips the default theme when Xaml is
            // absent. Reject that configuration instead of reporting it as the
            // memory cost of the complete desktop framework. Contains checks the
            // resource key without realizing a deferred control style.
            if (_application.Resources.MergedDictionaries.Count < 3 ||
                !_application.Resources.Contains(typeof(Button)))
            {
                throw new InvalidOperationException("The complete desktop theme was not loaded.");
            }
            Marker($"THEME loaded=true dictionaries={_application.Resources.MergedDictionaries.Count}");

            _mainWindow = CreateWindow();
            _application.MainWindow = _mainWindow;

            int exitCode = _application.Run(_mainWindow, args);
            Marker($"EXIT code={exitCode}");
            return exitCode;
        }
        catch (Exception ex)
        {
            WriteError("UNHANDLED", ex.ToString());
            return ExitFailure;
        }
        finally
        {
            _actionTimer?.Stop();
        }
    }

    private static Window CreateWindow()
    {
        int windowNumber = Interlocked.Increment(ref _nextWindowNumber);
        var window = new Window
        {
            Title = $"Jalium.UI Memory Probe #{windowNumber}",
            Width = 800,
            Height = 600,
        };

        // Leave the framework default untouched for the baseline. This switch is
        // the only title-bar override made by the probe.
        if (_options.NativeTitleBar)
        {
            window.TitleBarStyle = WindowTitleBarStyle.Native;
        }

        window.Shown += OnWindowShown;
        window.Closed += OnWindowClosed;
        OpenWindows.Add(window);
        TrackLifetimeDiagnostics(window);
        return window;
    }

    private static void OnWindowShown(object? sender, EventArgs e)
    {
        if (sender is not Window window)
        {
            return;
        }

        ShownWindows.Add(window);
        Marker(
            $"WINDOW_SHOWN handle=0x{window.Handle:x} title={QuoteForMarker(window.Title)} " +
            $"visible={ShownWindows.Count} tracked={OpenWindows.Count} " +
            $"surface={window.RenderTarget?.Width}x{window.RenderTarget?.Height} " +
            $"backend={window.CurrentRenderBackend}");

        if (ReferenceEquals(window, _mainWindow) && !_initialPopulationStarted)
        {
            _initialPopulationStarted = true;
            for (int i = 1; i < _options.WindowCount; i++)
            {
                CreateWindow().Show();
            }
        }

        TryReportReady();
    }

    private static void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not Window window)
        {
            return;
        }

        ReleaseFeatureResourcesForWindow(window);
        ShownWindows.Remove(window);
        OpenWindows.Remove(window);
        Marker(
            $"WINDOW_CLOSED title={QuoteForMarker(window.Title)} remaining={OpenWindows.Count}");

        if (!_shuttingDown && OpenWindows.Count == 0)
        {
            _shuttingDown = true;
            _application.Shutdown(ExitSuccess);
        }
    }

    private static void TryReportReady()
    {
        if (_readyReported || !_initialPopulationStarted)
        {
            return;
        }

        if (OpenWindows.Count != _options.WindowCount ||
            ShownWindows.Count != _options.WindowCount ||
            OpenWindows.Any(static window => window.Handle == nint.Zero))
        {
            return;
        }

        try
        {
            VerifyNativeModuleOrigins();
        }
        catch (Exception ex)
        {
            WriteError("NATIVE_GUARD", ex.ToString());
            Shutdown(ExitNativeGuardFailure);
            return;
        }

        _readyReported = true;
        Marker(
            $"READY pid={Environment.ProcessId} windows={OpenWindows.Count} " +
            $"handles={string.Join(',', OpenWindows.Select(static window => $"0x{window.Handle:x}"))}");
        ReportLifetimeDiagnostics("ready");

        StartScheduledActionsIfNeeded();
    }

    private static void VerifyNativeModuleOrigins()
    {
        string payloadDirectory = Path.GetFullPath(AppContext.BaseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var loadedNativeModules = new List<string>();

        using var process = Process.GetCurrentProcess();
        foreach (ProcessModule module in process.Modules)
        {
            string moduleName = Path.GetFileName(module.FileName);
            if (!moduleName.StartsWith("jalium.native.", StringComparison.OrdinalIgnoreCase) ||
                !moduleName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string actualPath = Path.GetFullPath(module.FileName);
            string expectedPath = Path.GetFullPath(Path.Combine(payloadDirectory, moduleName));
            if (!File.Exists(expectedPath))
            {
                throw new FileNotFoundException(
                    $"Loaded native module '{moduleName}' from '{actualPath}', but the probe payload " +
                    $"does not contain the expected sibling '{expectedPath}'.",
                    expectedPath);
            }

            if (!string.Equals(actualPath, expectedPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Native module origin mismatch for '{moduleName}': actual='{actualPath}', " +
                    $"expected='{expectedPath}'.");
            }

            loadedNativeModules.Add(moduleName);
        }

        if (loadedNativeModules.Count == 0)
        {
            throw new InvalidOperationException(
                $"No Jalium native module was visible in process {Environment.ProcessId} after all windows were shown. " +
                $"Payload directory: '{payloadDirectory}'.");
        }

        loadedNativeModules.Sort(StringComparer.OrdinalIgnoreCase);
        Marker(
            $"NATIVE_GUARD_OK directory={QuoteForMarker(payloadDirectory)} " +
            $"modules={QuoteForMarker(string.Join(',', loadedNativeModules))}");
    }

    private static void StartScheduledActionsIfNeeded()
    {
        if (_options.Actions.Count == 0 &&
            _options.ExitAfterMilliseconds is null &&
            _options.InputLifecycleRounds == 0)
        {
            return;
        }

        _actionClock = Stopwatch.StartNew();
        _actionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50),
        };
        _actionTimer.Tick += OnActionTimerTick;
        _actionTimer.Start();
    }

    private static void OnActionTimerTick(object? sender, EventArgs e)
    {
        if (_actionClock is null || _shuttingDown)
        {
            return;
        }

        try
        {
            long elapsedMilliseconds = _actionClock.ElapsedMilliseconds;
            if (_options.InputLifecycleRounds > 0)
            {
                // Keep the diagnostic on this probe's one existing scheduler. The
                // nested type is not initialized by the ordinary empty-window path.
                InputLifecycleDiagnostics.OnTimerTick(elapsedMilliseconds);
                return;
            }

            AdvancePendingFeatureVerification(elapsedMilliseconds);
            ReportPeriodicLifetimeDiagnostics(elapsedMilliseconds);

            while (!HasPendingFeatureVerification &&
                   _nextActionIndex < _options.Actions.Count &&
                   elapsedMilliseconds >= _options.Actions[_nextActionIndex].DelayMilliseconds)
            {
                ScheduledProbeAction action = _options.Actions[_nextActionIndex++];
                ExecuteAction(action);
                AdvancePendingFeatureVerification(elapsedMilliseconds);
            }

            if (_options.ExitAfterMilliseconds is int exitAfter &&
                elapsedMilliseconds >= exitAfter &&
                !HasPendingFeatureVerification)
            {
                VerifyFeatureExerciseCompletedIfRequested();
                Marker($"ACTION exit elapsedMs={elapsedMilliseconds}");
                Shutdown(ExitSuccess);
                return;
            }

            if (_nextActionIndex >= _options.Actions.Count &&
                _options.ExitAfterMilliseconds is null &&
                !HasPendingFeatureVerification)
            {
                VerifyFeatureExerciseCompletedIfRequested();
                _actionTimer?.Stop();
            }
        }
        catch (Exception ex)
        {
            WriteError("ACTION", ex.ToString());
            Shutdown(ExitFailure);
        }
    }

    private static void ExecuteAction(ScheduledProbeAction action)
    {
        switch (action.Kind)
        {
            case ProbeActionKind.Create:
                CreateAndShowWindows(action.Count);
                Marker($"ACTION create requested={action.Count} windows={OpenWindows.Count}");
                break;

            case ProbeActionKind.Close:
                CloseWindowsKeepingOne(action.Count);
                break;

            case ProbeActionKind.Rebuild:
                RebuildWindows(action.Count);
                break;

            case ProbeActionKind.Feature:
                AttachAndExerciseFeatureContent();
                break;

            case ProbeActionKind.Blank:
                ReturnFeatureWindowToBlank();
                break;

            case ProbeActionKind.Theme:
                SwitchFeatureTheme();
                break;

            case ProbeActionKind.Resize:
                ResizeFeatureWindow();
                break;

            default:
                throw new InvalidOperationException($"Unsupported action kind: {action.Kind}.");
        }
    }

    private static List<Window> CreateAndShowWindows(int count)
    {
        var created = new List<Window>(count);
        for (int i = 0; i < count; i++)
        {
            Window window = CreateWindow();
            created.Add(window);
            window.Show();
        }

        return created;
    }

    private static void CloseWindowsKeepingOne(int requestedCount)
    {
        int closableCount = Math.Min(requestedCount, Math.Max(0, OpenWindows.Count - 1));
        if (closableCount == 0)
        {
            Marker(
                $"ACTION close requested={requestedCount} closed=0 windows={OpenWindows.Count} reason=keep-one-visible");
            return;
        }

        Window survivor = _application.MainWindow is { } main && OpenWindows.Contains(main)
            ? main
            : OpenWindows[0];

        Window[] toClose = OpenWindows
            .Where(window => !ReferenceEquals(window, survivor))
            .Reverse()
            .Take(closableCount)
            .ToArray();

        foreach (Window window in toClose)
        {
            window.Close();
        }

        Marker(
            $"ACTION close requested={requestedCount} closed={toClose.Length} windows={OpenWindows.Count}");
    }

    private static void RebuildWindows(int replacementCount)
    {
        Window[] oldWindows = OpenWindows.ToArray();

        // Show the replacement set before closing the old one so the process never
        // enters a hidden-window state during a rebuild trigger.
        List<Window> replacements = CreateAndShowWindows(replacementCount);
        _mainWindow = replacements[0];
        _application.MainWindow = _mainWindow;

        foreach (Window oldWindow in oldWindows)
        {
            oldWindow.Close();
        }

        Marker(
            $"ACTION rebuild old={oldWindows.Length} replacement={replacementCount} windows={OpenWindows.Count}");
    }

    private static void Shutdown(int exitCode)
    {
        if (_shuttingDown)
        {
            return;
        }

        _shuttingDown = true;
        _actionTimer?.Stop();
        CancelPendingFeatureVerification();

        foreach (Window window in OpenWindows.ToArray())
        {
            window.Close();
        }

        _application.Shutdown(exitCode);
    }

    private static void Marker(string message)
    {
        Console.WriteLine("## PROBE " + message);
        Console.Out.Flush();
    }

    private static void WriteError(string category, string message)
    {
        Console.Error.WriteLine($"## PROBE ERROR {category} {message}");
        Console.Error.Flush();
    }

    private static string QuoteForMarker(string? value)
    {
        value ??= string.Empty;
        return '"' + value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal) + '"';
    }

    private enum ProbeActionKind
    {
        Create,
        Close,
        Rebuild,
        Feature,
        Blank,
        Theme,
        Resize,
    }

    private sealed record ScheduledProbeAction(
        int DelayMilliseconds,
        ProbeActionKind Kind,
        int Count);

    private sealed class ProbeOptions
    {
        public const string Usage =
            "Jalium.UI.MemoryProbe [options]\n" +
            "  --windows <count>                 Initial visible window count (default: 1).\n" +
            "  --native-titlebar                Use WindowTitleBarStyle.Native.\n" +
            "  --action <delay:kind[:count]>     Trigger create, close, rebuild, feature, blank, theme, or resize after READY.\n" +
            "                                      Example: --action 5000:create:2\n" +
            "  --feature-trigger <delay-ms>      Attach and exercise real styled content after READY.\n" +
            "  --exercise-features              Empty -> feature -> theme/resize -> blank -> feature -> theme/resize.\n" +
            "  --exercise-features-return-blank  Exercise two feature generations, then verify automatic Software restoration.\n" +
            "  --exercise-window-lifecycle       Shorthand for create/close/rebuild at 3/6/9 s.\n" +
            "  --lifetime-diagnostics            Log passive GC counters and weak window references; no forced collection.\n" +
            "  --input-lifecycle-rounds <count>  Diagnose 30+ real Window/input/resize/feature teardown cycles.\n" +
            "  --exit-after-ms <milliseconds>    Close all windows and exit after READY.\n" +
            "  --help                            Show this help text.\n" +
            "\n" +
            "Every window is a real, visible Jalium Application+Window. The default path keeps Content null at 800x600;\n" +
            "feature actions add content only inside their explicit measured scenario.";

        public int WindowCount { get; private set; } = 1;
        public bool NativeTitleBar { get; private set; }
        public bool ShowHelp { get; private set; }
        public bool ExerciseFeatures { get; private set; }
        public bool ExerciseFeaturesReturnToBlank { get; private set; }
        public bool LifetimeDiagnostics { get; private set; }
        public int InputLifecycleRounds { get; private set; }
        public int? ExitAfterMilliseconds { get; private set; }
        public List<ScheduledProbeAction> Actions { get; } = [];

        public static ProbeOptions Parse(string[] args)
        {
            var options = new ProbeOptions();
            bool exerciseLifecycle = false;
            bool exerciseFeatures = false;

            for (int i = 0; i < args.Length; i++)
            {
                string argument = args[i];
                switch (argument.ToLowerInvariant())
                {
                    case "--help":
                    case "-h":
                    case "/?":
                        options.ShowHelp = true;
                        break;

                    case "--native-titlebar":
                        options.NativeTitleBar = true;
                        break;

                    case "--windows":
                    case "--window-count":
                        options.WindowCount = ParsePositiveInt(
                            ReadValue(args, ref i, argument),
                            argument,
                            maximum: 64);
                        break;

                    case "--action":
                        options.Actions.Add(ParseAction(ReadValue(args, ref i, argument)));
                        break;

                    case "--feature-trigger":
                        options.Actions.Add(ParseFeatureTrigger(ReadValue(args, ref i, argument)));
                        break;

                    case "--exercise-features":
                        exerciseFeatures = true;
                        options.ExerciseFeatures = true;
                        break;

                    case "--exercise-features-return-blank":
                        exerciseFeatures = true;
                        options.ExerciseFeatures = true;
                        options.ExerciseFeaturesReturnToBlank = true;
                        break;

                    case "--exercise-window-lifecycle":
                        exerciseLifecycle = true;
                        break;

                    case "--lifetime-diagnostics":
                        options.LifetimeDiagnostics = true;
                        break;

                    case "--input-lifecycle-rounds":
                    {
                        int rounds = ParsePositiveInt(
                            ReadValue(args, ref i, argument),
                            argument,
                            maximum: 1_000);
                        if (rounds < 30)
                        {
                            throw new ArgumentException(
                                "Option '--input-lifecycle-rounds' must be at least 30 so the run includes " +
                                "six Feature/Blank verification cycles.");
                        }

                        options.InputLifecycleRounds = rounds;
                        break;
                    }

                    case "--exit-after-ms":
                        options.ExitAfterMilliseconds = ParsePositiveInt(
                            ReadValue(args, ref i, argument),
                            argument,
                            maximum: int.MaxValue);
                        break;

                    default:
                        throw new ArgumentException($"Unknown option '{argument}'.");
                }
            }

            if (exerciseLifecycle)
            {
                options.Actions.Add(new ScheduledProbeAction(3_000, ProbeActionKind.Create, options.WindowCount));
                options.Actions.Add(new ScheduledProbeAction(6_000, ProbeActionKind.Close, options.WindowCount));
                options.Actions.Add(new ScheduledProbeAction(9_000, ProbeActionKind.Rebuild, options.WindowCount));
            }

            if (exerciseFeatures)
            {
                // Delays are relative to READY. The first two seconds intentionally leave
                // the normally visible main window empty so the lightweight baseline is
                // observable before any feature object is constructed.
                options.Actions.Add(new ScheduledProbeAction(2_000, ProbeActionKind.Feature, 1));
                options.Actions.Add(new ScheduledProbeAction(4_000, ProbeActionKind.Theme, 1));
                options.Actions.Add(new ScheduledProbeAction(6_000, ProbeActionKind.Resize, 1));
                options.Actions.Add(new ScheduledProbeAction(8_000, ProbeActionKind.Blank, 1));
                options.Actions.Add(new ScheduledProbeAction(10_000, ProbeActionKind.Feature, 1));
                options.Actions.Add(new ScheduledProbeAction(12_000, ProbeActionKind.Theme, 1));
                options.Actions.Add(new ScheduledProbeAction(14_000, ProbeActionKind.Resize, 1));
                if (options.ExerciseFeaturesReturnToBlank)
                {
                    options.Actions.Add(new ScheduledProbeAction(16_000, ProbeActionKind.Blank, 1));
                }
            }

            options.Actions.Sort(static (left, right) =>
                left.DelayMilliseconds.CompareTo(right.DelayMilliseconds));

            if (options.InputLifecycleRounds > 0)
            {
                if (options.WindowCount != 1)
                {
                    throw new ArgumentException(
                        "--input-lifecycle-rounds requires exactly one persistent main window.");
                }

                if (options.Actions.Count != 0 ||
                    options.ExitAfterMilliseconds is not null ||
                    options.LifetimeDiagnostics)
                {
                    throw new ArgumentException(
                        "--input-lifecycle-rounds is an independent diagnostic mode and cannot be combined " +
                        "with scheduled actions, feature/lifecycle shorthands, --exit-after-ms, or --lifetime-diagnostics.");
                }
            }

            return options;
        }

        private static ScheduledProbeAction ParseAction(string value)
        {
            string[] parts = value.Split(':', StringSplitOptions.TrimEntries);
            if (parts.Length is < 2 or > 3)
            {
                throw new ArgumentException(
                    $"Invalid action '{value}'. Expected <delay-ms>:<create|close|rebuild|feature|blank|theme|resize>[:count].");
            }

            int delay = ParseNonNegativeInt(parts[0], "action delay", maximum: int.MaxValue);
            ProbeActionKind kind = parts[1].ToLowerInvariant() switch
            {
                "create" => ProbeActionKind.Create,
                "close" => ProbeActionKind.Close,
                "rebuild" => ProbeActionKind.Rebuild,
                "feature" => ProbeActionKind.Feature,
                "blank" => ProbeActionKind.Blank,
                "theme" => ProbeActionKind.Theme,
                "resize" => ProbeActionKind.Resize,
                _ => throw new ArgumentException(
                    $"Invalid action kind '{parts[1]}'. Expected create, close, rebuild, feature, blank, theme, or resize."),
            };

            bool supportsCount = kind is ProbeActionKind.Create or ProbeActionKind.Close or ProbeActionKind.Rebuild;
            if (!supportsCount && parts.Length == 3)
            {
                throw new ArgumentException(
                    $"Action kind '{parts[1]}' operates on the visible main window and does not accept a count.");
            }

            int count = parts.Length == 3
                ? ParsePositiveInt(parts[2], "action count", maximum: 64)
                : 1;

            return new ScheduledProbeAction(delay, kind, count);
        }

        private static ScheduledProbeAction ParseFeatureTrigger(string value)
        {
            string[] parts = value.Split(':', StringSplitOptions.TrimEntries);
            if (parts.Length > 2 ||
                (parts.Length == 2 && !string.Equals(parts[1], "feature", StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException(
                    $"Invalid feature trigger '{value}'. Expected <delay-ms> or <delay-ms>:feature.");
            }

            int delay = ParseNonNegativeInt(parts[0], "feature trigger delay", maximum: int.MaxValue);
            return new ScheduledProbeAction(delay, ProbeActionKind.Feature, 1);
        }

        private static string ReadValue(string[] args, ref int index, string option)
        {
            if (++index >= args.Length)
            {
                throw new ArgumentException($"Option '{option}' requires a value.");
            }

            return args[index];
        }

        private static int ParsePositiveInt(string value, string option, int maximum)
        {
            int parsed = ParseNonNegativeInt(value, option, maximum);
            if (parsed == 0)
            {
                throw new ArgumentException($"Option '{option}' must be greater than zero.");
            }

            return parsed;
        }

        private static int ParseNonNegativeInt(string value, string option, int maximum)
        {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) ||
                parsed < 0 ||
                parsed > maximum)
            {
                throw new ArgumentException(
                    $"Option '{option}' must be an integer from 0 through {maximum.ToString(CultureInfo.InvariantCulture)}.");
            }

            return parsed;
        }
    }
}

using System.Runtime.InteropServices;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Effects;
using Jalium.UI.Media.Imaging;

namespace Jalium.UI.MemoryProbe;

internal static partial class Program
{
    // These fields are deliberately zero-initialized. The default empty-window path
    // never constructs a feature scene, delegate, timer, task, or backing pixel buffer.
    private static FeatureScene? _featureScene;
    private static PendingFeatureVerification? _pendingFeatureVerification;
    private static object? _firstButtonTemplate;
    private static object? _firstTextBoxTemplate;
    private static object? _firstCheckBoxTemplate;
    private static int _featureGeneration;
    private static int _featureBlankCount;
    private static int _featureThemeSwitchCount;
    private static int _featureResizeCount;
    private static int _applicationResourceChangeCount;
    private static bool _featureApplicationHookInstalled;
    private static bool _featureExerciseReported;

    private static bool HasPendingFeatureVerification => _pendingFeatureVerification != null;

    private static void AttachAndExerciseFeatureContent()
    {
        Window window = GetVisibleFeatureWindow();
        if (_featureScene != null)
        {
            throw new InvalidOperationException(
                "Feature content is already attached. Run a blank action before attaching it again.");
        }

        EnsureFeatureApplicationHook();
        RequireFeature(ThemeManager.IsInitialized, "The complete theme must be initialized before feature content is created.");

        int generation = ++_featureGeneration;
        long frameBaseline = window.FrameHistory.TotalFrames;
        var backendBefore = window.CurrentRenderBackend;
        FeatureScene scene = FeatureScene.Create(window, generation);
        _featureScene = scene;

        window.Content = scene.Root;
        window.UpdateLayout();
        scene.EnsureTemplatesAndValidateLayout();

        bool templateReuse = RememberOrVerifyThemeTemplates(scene, generation);

        scene.InteractionSource = "scripted-setup";
        scene.ActionButton.PerformClick();
        scene.FeatureCheckBox.PerformClick();
        scene.Editor.Text = $"Feature generation {generation}: routed text change";
        scene.InteractionSource = "external-os-input";
        scene.RefreshInteractionStatus("awaiting real mouse/keyboard input");

        RequireFeature(scene.ClickCount == 1, $"Button.Click fired {scene.ClickCount} times; expected exactly once.");
        RequireFeature(scene.CheckedCount == 1, $"CheckBox.Checked fired {scene.CheckedCount} times; expected exactly once.");
        RequireFeature(scene.TextChangedCount == 1, $"TextBox.TextChanged fired {scene.TextChangedCount} times; expected exactly once.");
        RequireFeature(scene.FeatureCheckBox.IsChecked == true, "CheckBox did not enter the checked state.");

        window.UpdateLayout();
        scene.EnsureTemplatesAndValidateLayout();

        BeginFeatureVerification(
            kind: "feature",
            window,
            frameBaseline,
            frameTransitions: 2,
            stateReady: () =>
                ReferenceEquals(window.Content, scene.Root) &&
                scene.ClickCount == 1 &&
                scene.CheckedCount == 1 &&
                scene.TextChangedCount == 1,
            validate: () => scene.EnsureTemplatesAndValidateLayout(),
            betweenFrames: () =>
            {
                scene.ConfirmationMutationCount++;
                scene.RefreshInteractionStatus(
                    $"render confirmation frame {scene.ConfirmationMutationCount}");
                window.UpdateLayout();
            },
            successDetails: () =>
                $"generation={generation} templateReuse={templateReuse.ToString().ToLowerInvariant()} " +
                $"click={scene.ClickCount} checked={scene.CheckedCount} textChanged={scene.TextChangedCount} " +
                $"image={scene.Bitmap.PixelWidth}x{scene.Bitmap.PixelHeight} backend={window.CurrentRenderBackend}");

        window.ForceRenderFrame();

        Marker(
            $"ACTION feature generation={generation} visible=true backendBefore={backendBefore} " +
            $"backendAfter={window.CurrentRenderBackend} click={scene.ClickCount} checked={scene.CheckedCount} " +
            $"textChanged={scene.TextChangedCount} templateReuse={templateReuse.ToString().ToLowerInvariant()}");
    }

    private static void ReturnFeatureWindowToBlank()
    {
        Window window = GetVisibleFeatureWindow();
        FeatureScene scene = _featureScene ??
            throw new InvalidOperationException("The blank action requires attached feature content.");

        long frameBaseline = window.FrameHistory.TotalFrames;
        int generation = scene.Generation;

        BeginFeatureVerification(
            kind: "blank",
            window,
            frameBaseline,
            frameTransitions: 1,
            stateReady: () => window.Content == null && window.Handle != nint.Zero && ShownWindows.Contains(window),
            validate: () =>
            {
                RequireFeature(window.Content == null, "Window.Content was not cleared by the blank action.");
                RequireFeature(window.Handle != nint.Zero && ShownWindows.Contains(window),
                    "The feature window stopped being normally visible while returning to blank.");
            },
            betweenFrames: null,
            successDetails: () =>
                $"generation={generation} content=null visible=true backend={window.CurrentRenderBackend}");

        window.Content = null;
        _featureScene = null;
        scene.Dispose();
        _featureBlankCount++;
        window.UpdateLayout();
        window.ForceRenderFrame();

        Marker(
            $"ACTION blank generation={generation} content=null visible=true backend={window.CurrentRenderBackend}");
    }

    private static void SwitchFeatureTheme()
    {
        Window window = GetVisibleFeatureWindow();
        EnsureFeatureApplicationHook();

        ThemeVariant previousTheme = ThemeManager.CurrentTheme;
        ThemeVariant nextTheme = previousTheme == ThemeVariant.Dark
            ? ThemeVariant.Light
            : ThemeVariant.Dark;
        int applicationEventsBefore = _applicationResourceChangeCount;
        FeatureScene? scene = _featureScene;
        int sceneEventsBefore = scene?.ResourceChangeCount ?? 0;
        object? buttonTemplate = scene?.ActionButton.Template;
        object? textBoxTemplate = scene?.Editor.Template;
        object? checkBoxTemplate = scene?.FeatureCheckBox.Template;
        long frameBaseline = window.FrameHistory.TotalFrames;

        BeginFeatureVerification(
            kind: "theme",
            window,
            frameBaseline,
            frameTransitions: 1,
            stateReady: () =>
                ThemeManager.CurrentTheme == nextTheme &&
                _applicationResourceChangeCount > applicationEventsBefore &&
                (scene == null || scene.ResourceChangeCount > sceneEventsBefore),
            validate: () =>
            {
                RequireFeature(ThemeManager.CurrentTheme == nextTheme,
                    $"Theme switch ended on {ThemeManager.CurrentTheme}; expected {nextTheme}.");
                RequireFeature(_applicationResourceChangeCount > applicationEventsBefore,
                    "Application.ResourcesChanged did not fire for the theme switch.");

                if (scene != null)
                {
                    RequireFeature(scene.ResourceChangeCount > sceneEventsBefore,
                        "The attached feature tree did not receive ResourcesChanged for the theme switch.");
                    scene.EnsureTemplatesAndValidateLayout();
                    RequireFeature(ReferenceEquals(buttonTemplate, scene.ActionButton.Template),
                        "The Button template was rebuilt instead of reused across a palette-only theme switch.");
                    RequireFeature(ReferenceEquals(textBoxTemplate, scene.Editor.Template),
                        "The TextBox template was rebuilt instead of reused across a palette-only theme switch.");
                    RequireFeature(ReferenceEquals(checkBoxTemplate, scene.FeatureCheckBox.Template),
                        "The CheckBox template was rebuilt instead of reused across a palette-only theme switch.");
                }
            },
            betweenFrames: null,
            successDetails: () =>
                $"old={previousTheme} new={nextTheme} " +
                $"applicationResourceEvents={_applicationResourceChangeCount - applicationEventsBefore} " +
                $"treeResourceEvents={(scene?.ResourceChangeCount ?? sceneEventsBefore) - sceneEventsBefore}");

        ThemeManager.ApplyTheme(nextTheme);
        window.UpdateLayout();
        window.ForceRenderFrame();
        _featureThemeSwitchCount++;

        Marker(
            $"ACTION theme old={previousTheme} new={nextTheme} " +
            $"applicationResourceEvents={_applicationResourceChangeCount - applicationEventsBefore} " +
            $"treeResourceEvents={(scene?.ResourceChangeCount ?? sceneEventsBefore) - sceneEventsBefore}");
    }

    private static void ResizeFeatureWindow()
    {
        Window window = GetVisibleFeatureWindow();
        int resizeOrdinal = ++_featureResizeCount;
        double targetWidth = resizeOrdinal % 2 == 1 ? 960 : 840;
        double targetHeight = resizeOrdinal % 2 == 1 ? 700 : 640;
        double oldWidth = window.Width;
        double oldHeight = window.Height;
        int oldSurfaceWidth = window.RenderTarget?.Width ?? 0;
        int oldSurfaceHeight = window.RenderTarget?.Height ?? 0;
        RequireFeature(oldSurfaceWidth > 0 && oldSurfaceHeight > 0,
            "Cannot resize a feature window without an initialized native render surface.");
        RequireFeature(GetWindowRect(window.Handle, out NativeRect oldWindowRect),
            $"GetWindowRect failed before resize (Win32 error {Marshal.GetLastWin32Error()}).");

        double scaleX = oldSurfaceWidth / Math.Max(1.0, oldWidth);
        double scaleY = oldSurfaceHeight / Math.Max(1.0, oldHeight);
        int nonClientWidth = Math.Max(0, oldWindowRect.Width - oldSurfaceWidth);
        int nonClientHeight = Math.Max(0, oldWindowRect.Height - oldSurfaceHeight);
        int targetOuterWidth = checked((int)Math.Round(targetWidth * scaleX) + nonClientWidth);
        int targetOuterHeight = checked((int)Math.Round(targetHeight * scaleY) + nonClientHeight);
        int sizeChangedEvents = 0;
        long frameBaseline = window.FrameHistory.TotalFrames;

        SizeChangedEventHandler sizeChangedHandler = (_, _) => sizeChangedEvents++;
        window.SizeChanged += sizeChangedHandler;

        BeginFeatureVerification(
            kind: "resize",
            window,
            frameBaseline,
            frameTransitions: 1,
            stateReady: () =>
                sizeChangedEvents > 0 &&
                NearlyEqual(window.Width, targetWidth) &&
                NearlyEqual(window.Height, targetHeight) &&
                window.RenderTarget is { Width: > 0, Height: > 0 },
            validate: () =>
            {
                RequireFeature(sizeChangedEvents > 0, "Window.SizeChanged did not fire for the resize action.");
                RequireFeature(NearlyEqual(window.Width, targetWidth) && NearlyEqual(window.Height, targetHeight),
                    $"Window resize ended at {window.Width:F0}x{window.Height:F0}; expected {targetWidth:F0}x{targetHeight:F0}.");
                RequireFeature(window.RenderTarget is { Width: > 0, Height: > 0 },
                    "The resized window has no valid render surface dimensions.");
                RequireFeature(
                    window.RenderTarget!.Width != oldSurfaceWidth || window.RenderTarget.Height != oldSurfaceHeight,
                    "The native render surface dimensions did not change during the resize action.");
                _featureScene?.EnsureTemplatesAndValidateLayout();
            },
            betweenFrames: null,
            successDetails: () =>
                $"old={oldWidth:F0}x{oldHeight:F0} requested={targetWidth:F0}x{targetHeight:F0} " +
                $"actual={window.Width:F0}x{window.Height:F0} events={sizeChangedEvents} " +
                $"surface={window.RenderTarget?.Width}x{window.RenderTarget?.Height}",
            cleanup: () => window.SizeChanged -= sizeChangedHandler);

        bool resized = SetWindowPos(
            window.Handle,
            nint.Zero,
            0,
            0,
            targetOuterWidth,
            targetOuterHeight,
            SwpNoMove | SwpNoZOrder | SwpNoActivate);
        RequireFeature(resized,
            $"SetWindowPos failed during resize (Win32 error {Marshal.GetLastWin32Error()}).");
        window.UpdateLayout();
        window.ForceRenderFrame();

        Marker(
            $"ACTION resize old={oldWidth:F0}x{oldHeight:F0} requested={targetWidth:F0}x{targetHeight:F0} " +
            $"logical={window.Width:F0}x{window.Height:F0} events={sizeChangedEvents} " +
            $"surface={window.RenderTarget?.Width}x{window.RenderTarget?.Height}");
    }

    private static Window GetVisibleFeatureWindow()
    {
        Window window = _mainWindow;
        RequireFeature(OpenWindows.Contains(window), "The main feature window is not tracked as open.");
        RequireFeature(ShownWindows.Contains(window), "The main feature window has not raised Shown.");
        RequireFeature(window.Handle != nint.Zero, "The main feature window has no native handle.");
        return window;
    }

    private static void EnsureFeatureApplicationHook()
    {
        if (_featureApplicationHookInstalled)
        {
            return;
        }

        _application.ResourcesChanged += OnFeatureApplicationResourcesChanged;
        _featureApplicationHookInstalled = true;
    }

    private static void OnFeatureApplicationResourcesChanged(object? sender, EventArgs e)
        => _applicationResourceChangeCount++;

    private static bool RememberOrVerifyThemeTemplates(FeatureScene scene, int generation)
    {
        object buttonTemplate = scene.ActionButton.Template!;
        object textBoxTemplate = scene.Editor.Template!;
        object checkBoxTemplate = scene.FeatureCheckBox.Template!;

        if (generation == 1)
        {
            _firstButtonTemplate = buttonTemplate;
            _firstTextBoxTemplate = textBoxTemplate;
            _firstCheckBoxTemplate = checkBoxTemplate;
            return true;
        }

        bool reused =
            ReferenceEquals(_firstButtonTemplate, buttonTemplate) &&
            ReferenceEquals(_firstTextBoxTemplate, textBoxTemplate) &&
            ReferenceEquals(_firstCheckBoxTemplate, checkBoxTemplate);
        RequireFeature(reused,
            "A second feature tree did not reuse the initialized Button/TextBox/CheckBox theme templates.");
        return true;
    }

    private static void BeginFeatureVerification(
        string kind,
        Window window,
        long frameBaseline,
        int frameTransitions,
        Func<bool> stateReady,
        Action validate,
        Action? betweenFrames,
        Func<string> successDetails,
        Action? cleanup = null)
    {
        if (_pendingFeatureVerification != null)
        {
            throw new InvalidOperationException(
                $"Cannot start feature verification '{kind}' while '{_pendingFeatureVerification.Kind}' is pending.");
        }

        long elapsedMilliseconds = _actionClock?.ElapsedMilliseconds ?? 0;
        _pendingFeatureVerification = new PendingFeatureVerification(
            kind,
            window,
            frameBaseline,
            frameTransitions,
            elapsedMilliseconds + 5_000,
            stateReady,
            validate,
            betweenFrames,
            successDetails,
            cleanup);
    }

    private static void AdvancePendingFeatureVerification(long elapsedMilliseconds)
    {
        PendingFeatureVerification? pending = _pendingFeatureVerification;
        if (pending == null)
        {
            return;
        }

        if (pending.Window.Handle == nint.Zero)
        {
            FailPendingFeatureVerification(
                pending,
                $"Window closed while feature verification '{pending.Kind}' was pending.");
        }

        long currentFrames = pending.Window.FrameHistory.TotalFrames;
        bool stateReady = pending.StateReady();
        if (currentFrames > pending.FrameBaseline && stateReady)
        {
            pending.Validate();

            if (pending.RemainingFrameTransitions > 1)
            {
                pending.RemainingFrameTransitions--;
                pending.FrameBaseline = currentFrames;
                pending.BetweenFrames?.Invoke();
                pending.Window.ForceRenderFrame();
                return;
            }

            _pendingFeatureVerification = null;
            pending.Cleanup?.Invoke();
            Marker(
                $"FEATURE_VERIFY kind={pending.Kind} frameDelta={currentFrames - pending.InitialFrameBaseline} " +
                pending.SuccessDetails());
            return;
        }

        if (elapsedMilliseconds >= pending.DeadlineMilliseconds)
        {
            string state = stateReady ? "ready" : "not-ready";
            FailPendingFeatureVerification(
                pending,
                $"Timed out waiting for completed Present(s) for '{pending.Kind}': " +
                $"frames={currentFrames - pending.InitialFrameBaseline}, state={state}, " +
                $"remainingTransitions={pending.RemainingFrameTransitions}.");
        }
    }

    private static void FailPendingFeatureVerification(
        PendingFeatureVerification pending,
        string message)
    {
        _pendingFeatureVerification = null;
        pending.Cleanup?.Invoke();
        throw new InvalidOperationException(message);
    }

    private static void CancelPendingFeatureVerification()
    {
        PendingFeatureVerification? pending = _pendingFeatureVerification;
        _pendingFeatureVerification = null;
        pending?.Cleanup?.Invoke();

        if (_featureApplicationHookInstalled)
        {
            _application.ResourcesChanged -= OnFeatureApplicationResourcesChanged;
            _featureApplicationHookInstalled = false;
        }
    }

    private static void ReleaseFeatureResourcesForWindow(Window window)
    {
        if (_featureScene is not { } scene || !ReferenceEquals(scene.Window, window))
        {
            return;
        }

        _featureScene = null;
        scene.Dispose();
    }

    private static void VerifyFeatureExerciseCompletedIfRequested()
    {
        if (!_options.ExerciseFeatures || _featureExerciseReported)
        {
            return;
        }

        RequireFeature(!HasPendingFeatureVerification, "The feature exercise still has a pending render verification.");
        RequireFeature(_featureGeneration >= 2,
            $"The feature exercise attached content {_featureGeneration} time(s); expected at least two.");
        RequireFeature(_featureBlankCount >= 1,
            "The feature exercise never returned the visible window to Content=null.");
        RequireFeature(_featureThemeSwitchCount >= 2,
            $"The feature exercise switched theme {_featureThemeSwitchCount} time(s); expected at least two.");
        RequireFeature(_featureResizeCount >= 2,
            $"The feature exercise resized the window {_featureResizeCount} time(s); expected at least two.");
        if (_options.ExerciseFeaturesReturnToBlank)
        {
            RequireFeature(_featureBlankCount >= 2 && _featureScene == null && _mainWindow.Content == null,
                "The feature exercise did not release both feature trees and return to Content=null.");
            RequireFeature(_mainWindow.Handle != nint.Zero && ShownWindows.Contains(_mainWindow),
                "The restored empty window must remain normally visible.");
            RequireFeature(_mainWindow.CurrentRenderBackend == RenderBackend.Software,
                "The automatic empty window did not restore its Software render target.");
        }
        else
        {
            RequireFeature(_featureScene is { } finalScene && ReferenceEquals(_mainWindow.Content, finalScene.Root),
                "The feature exercise did not finish with the second feature tree attached.");
            _featureScene!.EnsureTemplatesAndValidateLayout();
        }

        _featureExerciseReported = true;
        Marker(
            $"FEATURE_EXERCISE_OK generations={_featureGeneration} blanks={_featureBlankCount} " +
            $"themes={_featureThemeSwitchCount} resizes={_featureResizeCount} " +
            $"finalBackend={_mainWindow.CurrentRenderBackend} totalFrames={_mainWindow.FrameHistory.TotalFrames}");
    }

    private static bool NearlyEqual(double left, double right)
        => Math.Abs(left - right) < 0.5;

    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint hWnd,
        nint hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hWnd, out NativeRect rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    private static void RequireFeature(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class PendingFeatureVerification
    {
        public PendingFeatureVerification(
            string kind,
            Window window,
            long frameBaseline,
            int remainingFrameTransitions,
            long deadlineMilliseconds,
            Func<bool> stateReady,
            Action validate,
            Action? betweenFrames,
            Func<string> successDetails,
            Action? cleanup)
        {
            Kind = kind;
            Window = window;
            InitialFrameBaseline = frameBaseline;
            FrameBaseline = frameBaseline;
            RemainingFrameTransitions = remainingFrameTransitions;
            DeadlineMilliseconds = deadlineMilliseconds;
            StateReady = stateReady;
            Validate = validate;
            BetweenFrames = betweenFrames;
            SuccessDetails = successDetails;
            Cleanup = cleanup;
        }

        public string Kind { get; }
        public Window Window { get; }
        public long InitialFrameBaseline { get; }
        public long FrameBaseline { get; set; }
        public int RemainingFrameTransitions { get; set; }
        public long DeadlineMilliseconds { get; }
        public Func<bool> StateReady { get; }
        public Action Validate { get; }
        public Action? BetweenFrames { get; }
        public Func<string> SuccessDetails { get; }
        public Action? Cleanup { get; }
    }

    private sealed class FeatureScene : IDisposable
    {
        private FeatureScene(
            Window window,
            int generation,
            Border root,
            Button actionButton,
            TextBox editor,
            CheckBox featureCheckBox,
            Image featureImage,
            BitmapImage bitmap,
            Border blurVisual,
            Border stencilVisual,
            TextBlock status)
        {
            Window = window;
            Generation = generation;
            Root = root;
            ActionButton = actionButton;
            Editor = editor;
            FeatureCheckBox = featureCheckBox;
            FeatureImage = featureImage;
            Bitmap = bitmap;
            BlurVisual = blurVisual;
            StencilVisual = stencilVisual;
            Status = status;

            ActionButton.Click += OnActionButtonClick;
            FeatureCheckBox.Checked += OnFeatureCheckBoxChecked;
            FeatureCheckBox.Unchecked += OnFeatureCheckBoxUnchecked;
            Editor.TextChanged += OnEditorTextChanged;
            Root.ResourcesChanged += OnRootResourcesChanged;
        }

        public Window Window { get; }
        public int Generation { get; }
        public Border Root { get; }
        public Button ActionButton { get; }
        public TextBox Editor { get; }
        public CheckBox FeatureCheckBox { get; }
        public Image FeatureImage { get; }
        public BitmapImage Bitmap { get; }
        public Border BlurVisual { get; }
        public Border StencilVisual { get; }
        public TextBlock Status { get; }
        public int ClickCount { get; private set; }
        public int CheckedCount { get; private set; }
        public int UncheckedCount { get; private set; }
        public int TextChangedCount { get; private set; }
        public int ResourceChangeCount { get; private set; }
        public int ConfirmationMutationCount { get; set; }
        public string InteractionSource { get; set; } = "external-os-input";

        public static FeatureScene Create(Window window, int generation)
        {
            BitmapImage bitmap = CreateFeatureBitmap();

            var actionButton = new Button
            {
                Content = "Trigger routed Button.Click",
                Width = 280,
                Height = 44,
                Margin = new Thickness(0, 0, 0, 10),
                HorizontalAlignment = HorizontalAlignment.Left,
            };

            var editor = new TextBox
            {
                Text = "Feature input before routed change",
                Width = 280,
                Height = 42,
                Margin = new Thickness(0, 0, 0, 10),
                HorizontalAlignment = HorizontalAlignment.Left,
            };

            var featureCheckBox = new CheckBox
            {
                Content = "Exercise checked-state routing",
                Width = 280,
                Height = 36,
                Margin = new Thickness(0, 0, 0, 12),
                HorizontalAlignment = HorizontalAlignment.Left,
            };

            var status = new TextBlock
            {
                Text = $"Generation {generation} waiting for events",
                FontSize = 15,
                Margin = new Thickness(0, 0, 0, 14),
                TextWrapping = TextWrapping.Wrap,
            };
            status.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");

            var controlsColumn = new StackPanel
            {
                Width = 300,
                Margin = new Thickness(0, 0, 18, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            controlsColumn.Children.Add(ThemedLabel(new TextBlock
            {
                Text = "Implicitly styled controls",
                FontSize = 20,
                Margin = new Thickness(0, 0, 0, 14),
            }));
            controlsColumn.Children.Add(actionButton);
            controlsColumn.Children.Add(editor);
            controlsColumn.Children.Add(featureCheckBox);
            controlsColumn.Children.Add(status);

            var featureImage = new Image
            {
                Source = bitmap,
                Width = 92,
                Height = 92,
                Stretch = Stretch.Uniform,
            };

            var imageHost = new Border
            {
                Width = 112,
                Height = 112,
                Margin = new Thickness(0, 0, 12, 0),
                Padding = new Thickness(10),
                CornerRadius = new CornerRadius(18),
                Background = new SolidColorBrush(Color.FromRgb(0x18, 0x2A, 0x3A)),
                Effect = new DropShadowEffect
                {
                    BlurRadius = 12,
                    ShadowDepth = 5,
                    Opacity = 0.65,
                },
                Child = featureImage,
            };

            var blurVisual = new Border
            {
                Width = 112,
                Height = 82,
                Margin = new Thickness(0, 15, 12, 15),
                CornerRadius = new CornerRadius(18),
                Background = new SolidColorBrush(Color.FromRgb(0x2E, 0x86, 0xAB)),
                Effect = new BlurEffect(5.0),
                Child = new TextBlock
                {
                    Text = "blur",
                    FontSize = 22,
                    Foreground = new SolidColorBrush(Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };

            var clippedChild = new Border
            {
                Width = 150,
                Height = 106,
                Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xB7, 0x03)),
                RenderTransform = new RotateTransform(14),
                RenderTransformOrigin = new Point(0.5, 0.5),
                Child = new TextBlock
                {
                    Text = "stencil clip",
                    FontSize = 18,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x13, 0x20, 0x2A)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };

            var stencilVisual = new Border
            {
                Width = 112,
                Height = 82,
                Margin = new Thickness(0, 15, 0, 15),
                BorderThickness = new Thickness(3),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x8E, 0xCA, 0xE6)),
                Background = new SolidColorBrush(Color.FromRgb(0x02, 0x30, 0x47)),
                CornerRadius = new CornerRadius(30),
                Shape = BorderShape.SuperEllipse,
                SuperEllipseN = 4.0,
                ClipToBounds = true,
                Child = clippedChild,
            };

            var visualRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            visualRow.Children.Add(imageHost);
            visualRow.Children.Add(blurVisual);
            visualRow.Children.Add(stencilVisual);

            var visualColumn = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            visualColumn.Children.Add(ThemedLabel(new TextBlock
            {
                Text = "Text, pixels, blur and a superellipse stencil clip",
                FontSize = 20,
                Margin = new Thickness(0, 0, 0, 14),
            }));
            visualColumn.Children.Add(visualRow);
            visualColumn.Children.Add(ThemedLabel(new TextBlock
            {
                Text = "The rotated amber child extends beyond the superellipse and must be clipped by the renderer.",
                Width = 372,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 14, 0, 0),
            }, secondary: true));

            var body = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            body.Children.Add(controlsColumn);
            body.Children.Add(visualColumn);

            var content = new StackPanel
            {
                Margin = new Thickness(22),
            };
            content.Children.Add(ThemedLabel(new TextBlock
            {
                Text = "Jalium.UI feature activation probe",
                FontSize = 28,
                Margin = new Thickness(0, 0, 0, 8),
            }));
            content.Children.Add(ThemedLabel(new TextBlock
            {
                Text = "This scene is constructed only by an explicit feature action and stays in a normally visible window.",
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 22),
            }, secondary: true));
            content.Children.Add(body);

            var root = new Border
            {
                Child = content,
            };
            root.SetResourceReference(Border.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");

            return new FeatureScene(
                window,
                generation,
                root,
                actionButton,
                editor,
                featureCheckBox,
                featureImage,
                bitmap,
                blurVisual,
                stencilVisual,
                status);
        }

        private static TextBlock ThemedLabel(TextBlock label, bool secondary = false)
        {
            label.SetResourceReference(TextBlock.ForegroundProperty,
                secondary ? "TextFillColorSecondaryBrush" : "TextFillColorPrimaryBrush");
            return label;
        }

        public void EnsureTemplatesAndValidateLayout()
        {
            _ = ActionButton.ApplyTemplate();
            _ = Editor.ApplyTemplate();
            _ = FeatureCheckBox.ApplyTemplate();

            RequireFeature(ActionButton.Template != null, "The default Button theme style did not provide a template.");
            RequireFeature(Editor.Template != null, "The default TextBox theme style did not provide a template.");
            RequireFeature(FeatureCheckBox.Template != null, "The default CheckBox theme style did not provide a template.");
            RequireFeature(Root.ActualWidth > 0 && Root.ActualHeight > 0,
                "The feature root did not receive a non-empty layout slot.");
            RequireFeature(ActionButton.ActualWidth > 0 && ActionButton.ActualHeight > 0,
                "The styled Button did not complete layout.");
            RequireFeature(Editor.ActualWidth > 0 && Editor.ActualHeight > 0,
                "The styled TextBox did not complete layout.");
            RequireFeature(FeatureCheckBox.ActualWidth > 0 && FeatureCheckBox.ActualHeight > 0,
                "The styled CheckBox did not complete layout.");
            RequireFeature(FeatureImage.ActualWidth > 0 && FeatureImage.ActualHeight > 0,
                "The Image did not complete layout.");
            RequireFeature(Bitmap.PixelWidth == 48 && Bitmap.PixelHeight == 48,
                $"The in-memory image reported {Bitmap.PixelWidth}x{Bitmap.PixelHeight}; expected 48x48.");
            RequireFeature(Bitmap.RawPixelData is { Length: >= 9_216 },
                "The in-memory image no longer has the expected BGRA pixel payload.");
            RequireFeature(BlurVisual.Effect is BlurEffect { HasEffect: true },
                "The blur visual is not backed by an active BlurEffect.");
            RequireFeature(
                StencilVisual.ClipToBounds &&
                StencilVisual.Shape == BorderShape.SuperEllipse &&
                StencilVisual.CornerRadius.TopLeft > 0,
                "The stencil visual lost its superellipse geometry clip.");
            RequireFeature(ReferenceEquals(Window.Content, Root),
                "The feature root is no longer attached to the expected window.");
            RequireFeature(Window.RenderTarget is { Width: > 0, Height: > 0 },
                "The feature window has no render surface.");
        }

        public void Dispose()
        {
            ActionButton.Click -= OnActionButtonClick;
            FeatureCheckBox.Checked -= OnFeatureCheckBoxChecked;
            FeatureCheckBox.Unchecked -= OnFeatureCheckBoxUnchecked;
            Editor.TextChanged -= OnEditorTextChanged;
            Root.ResourcesChanged -= OnRootResourcesChanged;
            FeatureImage.Source = null;
            Bitmap.Dispose();
        }

        public void RefreshInteractionStatus(string detail)
        {
            Status.Text =
                $"B{ClickCount} C{CheckedCount} U{UncheckedCount} T{TextChangedCount} | {detail}";
        }

        private void OnActionButtonClick(object sender, RoutedEventArgs e)
        {
            ClickCount++;
            RefreshInteractionStatus($"{VisibleInputSource} Button.Click");
            Marker(
                $"INPUT_EVENT source={InteractionSource} generation={Generation} kind=button " +
                $"button={ClickCount} checked={CheckedCount} unchecked={UncheckedCount} text={TextChangedCount}");
        }

        private void OnFeatureCheckBoxChecked(object sender, RoutedEventArgs e)
        {
            CheckedCount++;
            RefreshInteractionStatus($"{VisibleInputSource} CheckBox.Checked");
            Marker(
                $"INPUT_EVENT source={InteractionSource} generation={Generation} kind=checkbox-checked " +
                $"button={ClickCount} checked={CheckedCount} unchecked={UncheckedCount} text={TextChangedCount}");
        }

        private void OnFeatureCheckBoxUnchecked(object sender, RoutedEventArgs e)
        {
            UncheckedCount++;
            RefreshInteractionStatus($"{VisibleInputSource} CheckBox.Unchecked");
            Marker(
                $"INPUT_EVENT source={InteractionSource} generation={Generation} kind=checkbox-unchecked " +
                $"button={ClickCount} checked={CheckedCount} unchecked={UncheckedCount} text={TextChangedCount}");
        }

        private void OnEditorTextChanged(object sender, TextChangedEventArgs e)
        {
            TextChangedCount++;
            RefreshInteractionStatus(
                $"{VisibleInputSource} TextChanged len={Editor.Text?.Length ?? 0}");
            Marker(
                $"INPUT_EVENT source={InteractionSource} generation={Generation} kind=text-changed " +
                $"button={ClickCount} checked={CheckedCount} unchecked={UncheckedCount} " +
                $"text={TextChangedCount} length={Editor.Text?.Length ?? 0}");
        }

        private string VisibleInputSource =>
            InteractionSource == "external-os-input" ? "OS" : "script";

        private void OnRootResourcesChanged(object? sender, EventArgs e)
            => ResourceChangeCount++;

        private static BitmapImage CreateFeatureBitmap()
        {
            const int width = 48;
            const int height = 48;
            const int stride = width * 4;
            var pixels = new byte[stride * height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool accentCell = ((x / 8) + (y / 8)) % 2 == 0;
                    int offset = y * stride + x * 4;
                    pixels[offset + 0] = accentCell ? (byte)0x36 : (byte)0xE0;
                    pixels[offset + 1] = accentCell ? (byte)0xD1 : (byte)0x82;
                    pixels[offset + 2] = accentCell ? (byte)0x5A : (byte)0x28;
                    pixels[offset + 3] = 0xFF;
                }
            }

            return BitmapImage.FromPixels(pixels, width, height, stride);
        }
    }
}

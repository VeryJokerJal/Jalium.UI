using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Diagnostics;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.MemoryProbe;

internal static partial class Program
{
    // Everything in this nested type is initialized only after the explicit
    // --input-lifecycle-rounds mode reaches its first scheduler tick. The normal
    // empty-window probe therefore does not allocate these lists/buffers or add a
    // timer/listener of its own.
    private static class InputLifecycleDiagnostics
    {
        // Suppress beforefieldinit: the CLR must not realize reflection caches,
        // weak-reference storage, or frame buffers until the explicit mode calls
        // OnTimerTick for the first time.
        static InputLifecycleDiagnostics()
        {
        }

        private const int ShowHideCyclesPerRound = 4;
        private const int InputBatchesPerRound = 3;
        private const int InputPointsPerBatch = 24;
        private const int ResizeUpdatesPerRound = 12;
        private const int QuietMinimumMilliseconds = 600;
        private const int FinalQuietMinimumMilliseconds = 2_000;
        private const int FeatureEveryRounds = 5;
        private const long NoProgressTimeoutMilliseconds = 10_000;
        private const uint WmMouseMove = 0x0200;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;

        private static readonly BindingFlags StaticFields =
            BindingFlags.Static | BindingFlags.NonPublic;
        private static readonly BindingFlags InstanceFields =
            BindingFlags.Instance | BindingFlags.NonPublic;

        private static readonly FieldInfo FrameStartingField = RequireField(
            typeof(CompositionTarget),
            "FrameStarting",
            StaticFields);
        private static readonly FieldInfo RenderingField = RequireField(
            typeof(CompositionTarget),
            "Rendering",
            StaticFields);
        private static readonly FieldInfo RenderableWindowCountField = RequireField(
            typeof(CompositionTarget),
            "_renderableWindowCount",
            StaticFields);
        private static readonly FieldInfo CompositionSubscriberCountField = RequireField(
            typeof(CompositionTarget),
            "_subscriberCount",
            StaticFields);
        private static readonly FieldInfo SuccessfulPresentCountField = RequireField(
            typeof(Window),
            "_successfulPresentCount",
            InstanceFields);

        private static readonly List<WeakReference<Window>> ClosedWindows = [];
        private static readonly FrameHistory.Sample[] FrameSamples =
            new FrameHistory.Sample[FrameHistory.Capacity];

        private static Phase _phase;
        private static bool _initialized;
        private static bool _failureReported;
        private static bool _completed;
        private static int _round;
        private static int _roundsCompleted;
        private static int _featureCyclesCompleted;
        private static int _resizePresentsVerified;
        private static int _inputBatchesCompleted;
        private static long _startedMilliseconds;
        private static long _lastProgressMilliseconds;
        private static long _overallDeadlineMilliseconds;
        private static long _roundStartedMilliseconds;
        private static long _quietStartedMilliseconds;
        private static long _startingWorkingSetBytes;
        private static long _startingPrivateMemoryBytes;
        private static long _startingManagedBytes;
        private static long _startingAllocatedBytes;

        private static int _initialFrameStartingSubscribers;
        private static int _initialRenderingSubscribers;
        private static int _initialRenderableWindows;
        private static int _initialCompositionSubscribers;
        private static int? _settledRenderingSubscribersAfterFeature;
        private static int? _settledCompositionSubscribersAfterFeature;
        private static Dictionary<string, int> _initialRenderingOwners = null!;
        private static Dictionary<string, int>? _settledRenderingOwnersAfterFeature;

        private static Window? _temporaryWindow;
        private static WeakReference<Window>? _currentClosedWindow;
        private static FrameHistory? _closedFrameHistory;
        private static long _closedFrameCount;
        private static int _closedPreviewMouseMoveCount;
        private static int _closedMouseMoveCount;
        private static int _closedPointerMoveCount;
        private static int _closedPointerMovedCount;
        private static int _closedSizeChangedCount;
        private static int _closedExternalPreviewMouseMoveCount;
        private static int _closedExternalMouseMoveCount;
        private static int _closedExternalPointerMoveCount;
        private static int _closedExternalPointerMovedCount;
        private static int _closedExternalSizeChangedCount;

        private static int _showHideCyclesCompleted;
        private static int _inputBatch;
        private static int _roundSyntheticMessages;
        private static int _previewMouseMoveCount;
        private static int _mouseMoveCount;
        private static int _pointerMoveCount;
        private static int _pointerMovedCount;
        private static int _sizeChangedCount;
        private static int _externalPreviewMouseMoveCount;
        private static int _externalMouseMoveCount;
        private static int _externalPointerMoveCount;
        private static int _externalPointerMovedCount;
        private static int _externalSizeChangedCount;
        private static int _shownCount;
        private static int _closedCount;
        private static int _quietTicks;
        private static bool _roundRanFeatureCycle;
        private static bool _sendingSyntheticInput;
        private static bool _issuingResizeBurst;

        private static int _batchPreviewBaseline;
        private static int _batchMouseBaseline;
        private static int _batchPointerMoveBaseline;
        private static int _batchPointerMovedBaseline;

        private static int _expectedClientWidth;
        private static int _expectedClientHeight;
        private static int _resizeSizeChangedBaseline;
        private static long _resizeFrameBaseline;
        private static long _resizePresentBaseline;
        private static long _resizeIssuedTimestamp;

        internal static void OnTimerTick(long elapsedMilliseconds)
        {
            if (_completed)
            {
                return;
            }

            try
            {
                if (!_initialized)
                {
                    Initialize(elapsedMilliseconds);
                }

                VerifyDeadlines(elapsedMilliseconds);

                switch (_phase)
                {
                    case Phase.BeginRound:
                        BeginRound(elapsedMilliseconds);
                        break;
                    case Phase.ShowHide:
                        ExerciseShowHide(elapsedMilliseconds);
                        break;
                    case Phase.SendInput:
                        SendSyntheticInputBatch(elapsedMilliseconds);
                        break;
                    case Phase.ValidateInput:
                        ValidateSyntheticInputBatch(elapsedMilliseconds);
                        break;
                    case Phase.ResizeBurst:
                        ExecuteResizeBurst(elapsedMilliseconds);
                        break;
                    case Phase.AwaitResizePresent:
                        AwaitResizePresent(elapsedMilliseconds);
                        break;
                    case Phase.FeatureAttach:
                        BeginFeatureAttach(elapsedMilliseconds);
                        break;
                    case Phase.AwaitFeatureAttach:
                        AwaitFeatureAttach(elapsedMilliseconds);
                        break;
                    case Phase.FeatureTheme:
                        BeginFeatureTheme(elapsedMilliseconds);
                        break;
                    case Phase.AwaitFeatureTheme:
                        AwaitFeatureTheme(elapsedMilliseconds);
                        break;
                    case Phase.FeatureBlank:
                        BeginFeatureBlank(elapsedMilliseconds);
                        break;
                    case Phase.AwaitFeatureBlank:
                        AwaitFeatureBlank(elapsedMilliseconds);
                        break;
                    case Phase.RecordRound:
                        RecordActiveRound(elapsedMilliseconds);
                        break;
                    case Phase.CloseWindow:
                        CloseTemporaryWindow(elapsedMilliseconds);
                        break;
                    case Phase.Quiet:
                        VerifyQuietPhase(elapsedMilliseconds);
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"Unknown input lifecycle phase '{_phase}'.");
                }
            }
            catch (Exception ex)
            {
                if (!_failureReported)
                {
                    _failureReported = true;
                    Marker(
                        $"INPUT_LIFECYCLE_FAILED round={_round} phase={_phase} " +
                        $"elapsedMs={elapsedMilliseconds} message={QuoteForMarker(ex.Message)}");
                }

                throw;
            }
        }

        private static void Initialize(long elapsedMilliseconds)
        {
            Require(_options.InputLifecycleRounds > 0,
                "Input lifecycle diagnostics were initialized without a positive round count.");
            Require(_mainWindow.Handle != nint.Zero && OpenWindows.Contains(_mainWindow),
                "The persistent main window is not open at input lifecycle initialization.");
            Require(ShownWindows.Contains(_mainWindow),
                "The persistent main window is not shown at input lifecycle initialization.");

            _initialFrameStartingSubscribers = CountEventSubscribers(FrameStartingField);
            _initialRenderingSubscribers = CountEventSubscribers(RenderingField);
            _initialRenderableWindows = ReadInt32(RenderableWindowCountField);
            _initialCompositionSubscribers = ReadInt32(CompositionSubscriberCountField);
            _initialRenderingOwners = CaptureRenderingOwners();

            Require(CountFrameStartingSubscriptions(_mainWindow) == 1,
                "The persistent main window must own exactly one FrameStarting subscription.");
            Require(_initialRenderableWindows == 1,
                $"Expected exactly one renderable main window, found {_initialRenderableWindows}.");

            ProcessSnapshot start = CaptureProcessSnapshot();
            _startingWorkingSetBytes = start.WorkingSetBytes;
            _startingPrivateMemoryBytes = start.PrivateMemoryBytes;
            _startingManagedBytes = start.ManagedBytes;
            _startingAllocatedBytes = start.AllocatedBytes;

            _startedMilliseconds = elapsedMilliseconds;
            _lastProgressMilliseconds = elapsedMilliseconds;
            _overallDeadlineMilliseconds = checked(
                elapsedMilliseconds + 30_000L + (_options.InputLifecycleRounds * 10_000L));
            _phase = Phase.BeginRound;
            _initialized = true;

            Marker(
                $"INPUT_LIFECYCLE_START rounds={_options.InputLifecycleRounds} " +
                $"showHideCycles={ShowHideCyclesPerRound} inputBatches={InputBatchesPerRound} " +
                $"inputPointsPerBatch={InputPointsPerBatch} resizeUpdates={ResizeUpdatesPerRound} " +
                $"quietMinimumMs={QuietMinimumMilliseconds} " +
                $"finalQuietMinimumMs={FinalQuietMinimumMilliseconds} " +
                $"featureEveryRounds={FeatureEveryRounds} " +
                $"frameStarting={_initialFrameStartingSubscribers} " +
                $"rendering={_initialRenderingSubscribers} " +
                $"renderableWindows={_initialRenderableWindows} " +
                $"compositionSubscribers={_initialCompositionSubscribers} " +
                $"renderingOwners={FormatOwnerCounts(_initialRenderingOwners)} " +
                "inputKind=synthetic-client-WM_MOUSEMOVE forcedGc=false trim=false hiddenWindow=false");
        }

        private static void BeginRound(long elapsedMilliseconds)
        {
            _round++;
            _roundStartedMilliseconds = elapsedMilliseconds;
            _showHideCyclesCompleted = 0;
            _inputBatch = 0;
            _roundSyntheticMessages = 0;
            _previewMouseMoveCount = 0;
            _mouseMoveCount = 0;
            _pointerMoveCount = 0;
            _pointerMovedCount = 0;
            _sizeChangedCount = 0;
            _externalPreviewMouseMoveCount = 0;
            _externalMouseMoveCount = 0;
            _externalPointerMoveCount = 0;
            _externalPointerMovedCount = 0;
            _externalSizeChangedCount = 0;
            _shownCount = 0;
            _closedCount = 0;
            _quietTicks = 0;
            _roundRanFeatureCycle = false;
            _currentClosedWindow = null;
            _closedFrameHistory = null;
            _sendingSyntheticInput = false;
            _issuingResizeBurst = false;

            Window window = CreateWindow();
            window.Title = $"Jalium.UI Input Lifecycle #{_round}";
            window.Width = 640;
            window.Height = 420;
            window.ShowActivated = false;
            // These handlers intentionally remain attached to the closed Window.
            // They are static, so the instance-side delegate fields cannot root the
            // Window; retaining them lets the quiet phase catch any delayed callback
            // that incorrectly arrives after native/managed teardown.
            window.PreviewMouseMove += OnPreviewMouseMove;
            window.MouseMove += OnMouseMove;
            window.PointerMove += OnPointerMove;
            window.PointerMoved += OnPointerMoved;
            window.SizeChanged += OnSizeChanged;
            window.Shown += OnTemporaryWindowShown;
            window.Closed += OnTemporaryWindowClosed;
            _temporaryWindow = window;

            window.Show();

            Require(window.Handle != nint.Zero,
                $"Round {_round}: temporary Window.Show did not create an HWND.");
            Require(_shownCount == 1,
                $"Round {_round}: initial Show raised Shown {_shownCount} times; expected exactly once.");
            Require(CountFrameStartingSubscriptions(window) == 1,
                $"Round {_round}: initial Show did not leave exactly one FrameStarting subscription.");
            Require(CountFrameStartingSubscriptions(_mainWindow) == 1,
                $"Round {_round}: the main window no longer owns exactly one FrameStarting subscription.");
            Require(CountEventSubscribers(FrameStartingField) == _initialFrameStartingSubscribers + 1,
                $"Round {_round}: total FrameStarting subscriptions did not increase by exactly one after Show.");
            Require(ReadInt32(RenderableWindowCountField) == _initialRenderableWindows + 1,
                $"Round {_round}: rendering-window count did not increase by exactly one after Show.");

            Marker(
                $"INPUT_LIFECYCLE_PROGRESS round={_round} phase=shown hwnd=0x{window.Handle:x} " +
                $"frameStarting={CountEventSubscribers(FrameStartingField)} " +
                $"renderableWindows={ReadInt32(RenderableWindowCountField)}");
            Advance(Phase.ShowHide, elapsedMilliseconds);
        }

        private static void ExerciseShowHide(long elapsedMilliseconds)
        {
            Window window = GetTemporaryWindow();

            // Show twice while already visible, then perform a real Hide/Show pair.
            // The duplicate Show is intentional: a subscription bug used to add one
            // global FrameStarting callback for every call.
            int shownBefore = _shownCount;
            window.Show();
            window.Show();
            Require(_shownCount == shownBefore + 2,
                $"Round {_round}: two duplicate Show calls raised {_shownCount - shownBefore} Shown callbacks; " +
                "expected exactly two under the existing Window.Show contract.");
            Require(CountFrameStartingSubscriptions(window) == 1,
                $"Round {_round}: duplicate Show accumulated FrameStarting subscriptions.");
            Require(CountEventSubscribers(FrameStartingField) == _initialFrameStartingSubscribers + 1,
                $"Round {_round}: duplicate Show changed the total FrameStarting subscription count.");

            window.Hide();
            Require(window.Handle != nint.Zero,
                $"Round {_round}: Hide unexpectedly destroyed the temporary HWND.");
            Require(CountFrameStartingSubscriptions(window) == 1,
                $"Round {_round}: Hide changed the temporary window's FrameStarting subscription count.");
            Require(CountEventSubscribers(FrameStartingField) == _initialFrameStartingSubscribers + 1,
                $"Round {_round}: Hide changed the total FrameStarting subscription count.");
            Require(ReadInt32(RenderableWindowCountField) == _initialRenderableWindows,
                $"Round {_round}: rendering-window count did not return to the main-window baseline after Hide.");

            window.Show();
            _showHideCyclesCompleted++;
            int expectedShown = 1 + (3 * _showHideCyclesCompleted);
            Require(_shownCount == expectedShown,
                $"Round {_round}: Show/Hide cycle {_showHideCyclesCompleted} produced {_shownCount} Shown callbacks; " +
                $"expected {expectedShown}.");
            Require(CountFrameStartingSubscriptions(window) == 1,
                $"Round {_round}: Show/Hide cycle {_showHideCyclesCompleted} did not leave one FrameStarting subscription.");
            Require(CountEventSubscribers(FrameStartingField) == _initialFrameStartingSubscribers + 1,
                $"Round {_round}: Show/Hide cycle {_showHideCyclesCompleted} changed the total " +
                "FrameStarting subscription count.");
            Require(ReadInt32(RenderableWindowCountField) == _initialRenderableWindows + 1,
                $"Round {_round}: rendering-window count did not restore after Show.");

            if (_showHideCyclesCompleted < ShowHideCyclesPerRound)
            {
                Advance(Phase.ShowHide, elapsedMilliseconds);
                return;
            }

            Advance(Phase.SendInput, elapsedMilliseconds);
        }

        private static void SendSyntheticInputBatch(long elapsedMilliseconds)
        {
            Window window = GetTemporaryWindow();
            Require(window.Handle != nint.Zero,
                $"Round {_round}: cannot send a synthetic client mouse batch without an HWND.");
            Require(GetClientRect(window.Handle, out NativeRect clientRect),
                $"Round {_round}: GetClientRect failed before synthetic input (Win32 {Marshal.GetLastWin32Error()}).");
            Require(clientRect.Width > 96 && clientRect.Height > 128,
                $"Round {_round}: client area {clientRect.Width}x{clientRect.Height} is too small for fixed input points.");

            _batchPreviewBaseline = _previewMouseMoveCount;
            _batchMouseBaseline = _mouseMoveCount;
            _batchPointerMoveBaseline = _pointerMoveCount;
            _batchPointerMovedBaseline = _pointerMovedCount;

            Require(!_sendingSyntheticInput,
                $"Round {_round}: a synthetic input batch was already active.");
            _sendingSyntheticInput = true;
            try
            {
                for (int point = 0; point < InputPointsPerBatch; point++)
                {
                    int usableWidth = clientRect.Width - 96;
                    int usableHeight = clientRect.Height - 128;
                    int x = 48 + ((_inputBatch * 31 + point * 37) % usableWidth);
                    int y = 96 + ((_inputBatch * 29 + point * 23) % usableHeight);
                    _ = SendMessageW(window.Handle, WmMouseMove, nint.Zero, PackClientPoint(x, y));
                }
            }
            finally
            {
                _sendingSyntheticInput = false;
            }

            _roundSyntheticMessages += InputPointsPerBatch;
            Advance(Phase.ValidateInput, elapsedMilliseconds);
        }

        private static void ValidateSyntheticInputBatch(long elapsedMilliseconds)
        {
            int previewDelta = _previewMouseMoveCount - _batchPreviewBaseline;
            int mouseDelta = _mouseMoveCount - _batchMouseBaseline;
            int pointerMoveDelta = _pointerMoveCount - _batchPointerMoveBaseline;
            int pointerMovedDelta = _pointerMovedCount - _batchPointerMovedBaseline;

            Require(previewDelta == InputPointsPerBatch,
                $"Round {_round} batch {_inputBatch + 1}: PreviewMouseMove handled {previewDelta}; " +
                $"expected exactly {InputPointsPerBatch} synthetic messages.");
            Require(mouseDelta == InputPointsPerBatch,
                $"Round {_round} batch {_inputBatch + 1}: MouseMove handled {mouseDelta}; " +
                $"expected exactly {InputPointsPerBatch} synthetic messages.");
            Require(pointerMoveDelta == InputPointsPerBatch,
                $"Round {_round} batch {_inputBatch + 1}: PointerMove handled {pointerMoveDelta}; " +
                $"expected exactly {InputPointsPerBatch} synthetic messages.");
            Require(pointerMovedDelta == InputPointsPerBatch,
                $"Round {_round} batch {_inputBatch + 1}: PointerMoved handled {pointerMovedDelta}; " +
                $"expected exactly {InputPointsPerBatch} synthetic messages.");

            _inputBatch++;
            _inputBatchesCompleted++;
            int expectedCumulative = _inputBatch * InputPointsPerBatch;
            Require(
                _previewMouseMoveCount == expectedCumulative &&
                _mouseMoveCount == expectedCumulative &&
                _pointerMoveCount == expectedCumulative &&
                _pointerMovedCount == expectedCumulative,
                $"Round {_round}: routed input totals multiplied or dropped after batch {_inputBatch}. " +
                $"preview={_previewMouseMoveCount} mouse={_mouseMoveCount} " +
                $"pointerMove={_pointerMoveCount} pointerMoved={_pointerMovedCount} " +
                $"expected={expectedCumulative}.");

            Marker(
                $"INPUT_LIFECYCLE_INPUT round={_round} batch={_inputBatch} " +
                $"points={InputPointsPerBatch} preview={previewDelta} mouse={mouseDelta} " +
                $"pointerMove={pointerMoveDelta} pointerMoved={pointerMovedDelta} " +
                $"externalPreview={_externalPreviewMouseMoveCount} externalMouse={_externalMouseMoveCount} " +
                $"externalPointerMove={_externalPointerMoveCount} externalPointerMoved={_externalPointerMovedCount} " +
                "kind=synthetic-client-WM_MOUSEMOVE");

            Advance(
                _inputBatch < InputBatchesPerRound ? Phase.SendInput : Phase.ResizeBurst,
                elapsedMilliseconds);
        }

        private static void ExecuteResizeBurst(long elapsedMilliseconds)
        {
            Window window = GetTemporaryWindow();
            Require(GetClientRect(window.Handle, out NativeRect initialClient),
                $"Round {_round}: GetClientRect failed before resize (Win32 {Marshal.GetLastWin32Error()}).");
            Require(GetWindowRect(window.Handle, out NativeRect initialWindow),
                $"Round {_round}: GetWindowRect failed before resize (Win32 {Marshal.GetLastWin32Error()}).");

            int nonClientWidth = Math.Max(0, initialWindow.Width - initialClient.Width);
            int nonClientHeight = Math.Max(0, initialWindow.Height - initialClient.Height);
            _expectedClientWidth = initialClient.Width + 72 + ((_round % 3) * 16);
            _expectedClientHeight = initialClient.Height + 48 + ((_round % 2) * 24);
            _resizeSizeChangedBaseline = _sizeChangedCount;

            Require(!_issuingResizeBurst,
                $"Round {_round}: a SetWindowPos resize burst was already active.");
            _issuingResizeBurst = true;
            try
            {
                for (int update = 1; update <= ResizeUpdatesPerRound; update++)
                {
                    int clientWidth = initialClient.Width +
                        ((_expectedClientWidth - initialClient.Width) * update / ResizeUpdatesPerRound);
                    int clientHeight = initialClient.Height +
                        ((_expectedClientHeight - initialClient.Height) * update / ResizeUpdatesPerRound);

                    if (update == ResizeUpdatesPerRound)
                    {
                        // Baseline immediately before the final real resize. A successful
                        // verification therefore needs a Present completed after the user
                        // equivalent of "stopping" at the final size.
                        _resizeFrameBaseline = window.FrameHistory.TotalFrames;
                        _resizePresentBaseline = ReadInt64(SuccessfulPresentCountField, window);
                        _resizeIssuedTimestamp = Stopwatch.GetTimestamp();
                    }

                    bool resized = SetWindowPos(
                        window.Handle,
                        nint.Zero,
                        0,
                        0,
                        clientWidth + nonClientWidth,
                        clientHeight + nonClientHeight,
                        SwpNoMove | SwpNoZOrder | SwpNoActivate);
                    Require(resized,
                        $"Round {_round}: SetWindowPos update {update}/{ResizeUpdatesPerRound} failed " +
                        $"(Win32 {Marshal.GetLastWin32Error()}).");
                }
            }
            finally
            {
                _issuingResizeBurst = false;
            }

            Require(GetClientRect(window.Handle, out NativeRect finalClient),
                $"Round {_round}: GetClientRect failed after resize (Win32 {Marshal.GetLastWin32Error()}).");
            Require(
                finalClient.Width == _expectedClientWidth &&
                finalClient.Height == _expectedClientHeight,
                $"Round {_round}: final client size is {finalClient.Width}x{finalClient.Height}; " +
                $"expected {_expectedClientWidth}x{_expectedClientHeight}.");
            int resizeCallbacks = _sizeChangedCount - _resizeSizeChangedBaseline;
            Require(resizeCallbacks == ResizeUpdatesPerRound,
                $"Round {_round}: the real SetWindowPos burst raised {resizeCallbacks} SizeChanged callbacks; " +
                $"expected exactly {ResizeUpdatesPerRound}.");

            Advance(Phase.AwaitResizePresent, elapsedMilliseconds);
        }

        private static void AwaitResizePresent(long elapsedMilliseconds)
        {
            Window window = GetTemporaryWindow();
            if (!GetClientRect(window.Handle, out NativeRect clientRect))
            {
                throw new InvalidOperationException(
                    $"Round {_round}: GetClientRect failed while awaiting final resize Present " +
                    $"(Win32 {Marshal.GetLastWin32Error()}).");
            }

            long frameCount = window.FrameHistory.TotalFrames;
            long successfulPresents = ReadInt64(SuccessfulPresentCountField, window);
            long latestFrameTimestamp = GetLatestFrameTimestamp(window.FrameHistory);
            bool finalClientSize =
                clientRect.Width == _expectedClientWidth &&
                clientRect.Height == _expectedClientHeight;
            bool finalRenderTargetSize =
                window.RenderTarget is { } target &&
                target.Width == _expectedClientWidth &&
                target.Height == _expectedClientHeight;
            bool realPresentCompleted =
                frameCount > _resizeFrameBaseline &&
                successfulPresents > _resizePresentBaseline &&
                latestFrameTimestamp >= _resizeIssuedTimestamp;

            if (!finalClientSize || !finalRenderTargetSize || !realPresentCompleted)
            {
                return;
            }

            _resizePresentsVerified++;
            Marker(
                $"INPUT_LIFECYCLE_RESIZE round={_round} updates={ResizeUpdatesPerRound} " +
                $"client={clientRect.Width}x{clientRect.Height} " +
                $"renderTarget={window.RenderTarget!.Width}x{window.RenderTarget.Height} " +
                $"frameDelta={frameCount - _resizeFrameBaseline} " +
                $"presentDelta={successfulPresents - _resizePresentBaseline} " +
                "frameHistoryPresent=true forceRenderFrame=false");

            bool exerciseFeature = _round % FeatureEveryRounds == 0;
            Advance(exerciseFeature ? Phase.FeatureAttach : Phase.RecordRound, elapsedMilliseconds);
        }

        private static void BeginFeatureAttach(long elapsedMilliseconds)
        {
            Require(!HasPendingFeatureVerification,
                $"Round {_round}: a feature verification was already pending before attach.");
            Require(_featureScene == null && _mainWindow.Content == null,
                $"Round {_round}: the persistent main window was not blank before feature attach.");
            AttachAndExerciseFeatureContent();
            _roundRanFeatureCycle = true;
            Advance(Phase.AwaitFeatureAttach, elapsedMilliseconds);
        }

        private static void AwaitFeatureAttach(long elapsedMilliseconds)
        {
            AdvancePendingFeatureVerification(elapsedMilliseconds);
            if (HasPendingFeatureVerification)
            {
                return;
            }

            Require(_featureScene != null && ReferenceEquals(_mainWindow.Content, _featureScene.Root),
                $"Round {_round}: feature verification completed without an attached feature scene.");
            Advance(Phase.FeatureTheme, elapsedMilliseconds);
        }

        private static void BeginFeatureTheme(long elapsedMilliseconds)
        {
            SwitchFeatureTheme();
            Advance(Phase.AwaitFeatureTheme, elapsedMilliseconds);
        }

        private static void AwaitFeatureTheme(long elapsedMilliseconds)
        {
            AdvancePendingFeatureVerification(elapsedMilliseconds);
            if (HasPendingFeatureVerification)
            {
                return;
            }

            Advance(Phase.FeatureBlank, elapsedMilliseconds);
        }

        private static void BeginFeatureBlank(long elapsedMilliseconds)
        {
            ReturnFeatureWindowToBlank();
            Advance(Phase.AwaitFeatureBlank, elapsedMilliseconds);
        }

        private static void AwaitFeatureBlank(long elapsedMilliseconds)
        {
            AdvancePendingFeatureVerification(elapsedMilliseconds);
            if (HasPendingFeatureVerification)
            {
                return;
            }

            Require(_featureScene == null && _mainWindow.Content == null,
                $"Round {_round}: Feature/Blank round-trip did not restore Content=null.");
            _featureCyclesCompleted++;
            Marker(
                $"INPUT_LIFECYCLE_FEATURE round={_round} cycle={_featureCyclesCompleted} " +
                $"featureGenerations={_featureGeneration} blanks={_featureBlankCount} " +
                $"themes={_featureThemeSwitchCount} mainFrames={_mainWindow.FrameHistory.TotalFrames}");
            Advance(Phase.RecordRound, elapsedMilliseconds);
        }

        private static void RecordActiveRound(long elapsedMilliseconds)
        {
            Window window = GetTemporaryWindow();
            int expectedInput = InputBatchesPerRound * InputPointsPerBatch;
            Require(
                _previewMouseMoveCount == expectedInput &&
                _mouseMoveCount == expectedInput &&
                _pointerMoveCount == expectedInput &&
                _pointerMovedCount == expectedInput,
                $"Round {_round}: input callback totals changed after their validated batches.");
            Require(CountFrameStartingSubscriptions(window) == 1,
                $"Round {_round}: temporary window no longer owns exactly one FrameStarting subscription before Close.");
            Require(CountFrameStartingSubscriptions(_mainWindow) == 1,
                $"Round {_round}: main window no longer owns exactly one FrameStarting subscription before Close.");
            Require(CountEventSubscribers(FrameStartingField) == _initialFrameStartingSubscribers + 1,
                $"Round {_round}: total FrameStarting subscriptions changed before Close.");
            Require(_shownCount == 1 + (3 * ShowHideCyclesPerRound) && _closedCount == 0,
                $"Round {_round}: lifecycle callback totals changed before Close: " +
                $"shown={_shownCount}, closed={_closedCount}.");

            ReportMetric("round", elapsedMilliseconds);
            Advance(Phase.CloseWindow, elapsedMilliseconds);
        }

        private static void CloseTemporaryWindow(long elapsedMilliseconds)
        {
            Window window = GetTemporaryWindow();
            FrameHistory history = window.FrameHistory;
            var weak = new WeakReference<Window>(window);

            window.Close();

            Require(_closedCount == 1,
                $"Round {_round}: Close raised Closed {_closedCount} times; expected exactly once.");
            Require(_shownCount == 1 + (3 * ShowHideCyclesPerRound),
                $"Round {_round}: Close changed the Shown callback count to {_shownCount}.");
            Require(window.Handle == nint.Zero,
                $"Round {_round}: closed temporary window retained HWND 0x{window.Handle:x}.");
            Require(window.RenderTarget == null,
                $"Round {_round}: closed temporary window retained a RenderTarget.");
            Require(CountFrameStartingSubscriptions(window) == 0,
                $"Round {_round}: closed temporary window retained a FrameStarting subscription.");
            Require(CountFrameStartingSubscriptions(_mainWindow) == 1,
                $"Round {_round}: main window FrameStarting subscription changed after temporary Close.");
            Require(CountEventSubscribers(FrameStartingField) == _initialFrameStartingSubscribers,
                $"Round {_round}: total FrameStarting subscriptions did not return to baseline after Close.");
            Require(ReadInt32(RenderableWindowCountField) == _initialRenderableWindows,
                $"Round {_round}: rendering-window count did not return to baseline after Close.");

            _closedFrameHistory = history;
            _closedFrameCount = history.TotalFrames;
            _closedPreviewMouseMoveCount = _previewMouseMoveCount;
            _closedMouseMoveCount = _mouseMoveCount;
            _closedPointerMoveCount = _pointerMoveCount;
            _closedPointerMovedCount = _pointerMovedCount;
            _closedSizeChangedCount = _sizeChangedCount;
            _closedExternalPreviewMouseMoveCount = _externalPreviewMouseMoveCount;
            _closedExternalMouseMoveCount = _externalMouseMoveCount;
            _closedExternalPointerMoveCount = _externalPointerMoveCount;
            _closedExternalPointerMovedCount = _externalPointerMovedCount;
            _closedExternalSizeChangedCount = _externalSizeChangedCount;
            _currentClosedWindow = weak;
            ClosedWindows.Add(weak);
            _temporaryWindow = null;
            _quietTicks = 0;
            _quietStartedMilliseconds = elapsedMilliseconds;

            Advance(Phase.Quiet, elapsedMilliseconds);
        }

        private static void VerifyQuietPhase(long elapsedMilliseconds)
        {
            FrameHistory history = _closedFrameHistory ??
                throw new InvalidOperationException($"Round {_round}: closed FrameHistory was not retained for quiet verification.");

            Require(history.TotalFrames == _closedFrameCount,
                $"Round {_round}: a closed window produced another frame during quiet: " +
                $"before={_closedFrameCount}, after={history.TotalFrames}.");
            Require(
                _previewMouseMoveCount == _closedPreviewMouseMoveCount &&
                _mouseMoveCount == _closedMouseMoveCount &&
                _pointerMoveCount == _closedPointerMoveCount &&
                _pointerMovedCount == _closedPointerMovedCount &&
                _sizeChangedCount == _closedSizeChangedCount &&
                _externalPreviewMouseMoveCount == _closedExternalPreviewMouseMoveCount &&
                _externalMouseMoveCount == _closedExternalMouseMoveCount &&
                _externalPointerMoveCount == _closedExternalPointerMoveCount &&
                _externalPointerMovedCount == _closedExternalPointerMovedCount &&
                _externalSizeChangedCount == _closedExternalSizeChangedCount &&
                _shownCount == 1 + (3 * ShowHideCyclesPerRound) &&
                _closedCount == 1,
                $"Round {_round}: a closed window received lifecycle/input/resize callbacks during quiet.");
            Require(CountFrameStartingSubscriptions(_mainWindow) == 1,
                $"Round {_round}: main window does not own exactly one FrameStarting subscription during quiet.");
            Require(CountEventSubscribers(FrameStartingField) == _initialFrameStartingSubscribers,
                $"Round {_round}: total FrameStarting subscriber count changed during quiet.");
            Require(ReadInt32(RenderableWindowCountField) == _initialRenderableWindows,
                $"Round {_round}: rendering-window count changed during quiet.");

            if (_currentClosedWindow is { } current && current.TryGetTarget(out Window? closedWindow))
            {
                Require(closedWindow.Handle == nint.Zero && closedWindow.RenderTarget == null,
                    $"Round {_round}: passively observed closed window resurrected native resources.");
                Require(CountFrameStartingSubscriptions(closedWindow) == 0,
                    $"Round {_round}: passively observed closed window regained a FrameStarting subscription.");
            }

            _quietTicks++;
            int requiredQuietMilliseconds = _round == _options.InputLifecycleRounds
                ? FinalQuietMinimumMilliseconds
                : QuietMinimumMilliseconds;
            long quietElapsedMilliseconds = elapsedMilliseconds - _quietStartedMilliseconds;
            if (quietElapsedMilliseconds < requiredQuietMilliseconds)
            {
                Advance(Phase.Quiet, elapsedMilliseconds);
                return;
            }

            ValidateSettledCompositionState();

            _roundsCompleted++;
            ReportMetric("quiet", elapsedMilliseconds);

            _currentClosedWindow = null;
            _closedFrameHistory = null;

            if (_roundsCompleted == _options.InputLifecycleRounds)
            {
                Complete(elapsedMilliseconds);
                return;
            }

            Advance(Phase.BeginRound, elapsedMilliseconds);
        }

        private static void Complete(long elapsedMilliseconds)
        {
            int expectedFeatureCycles = _options.InputLifecycleRounds / FeatureEveryRounds;
            Require(_roundsCompleted == _options.InputLifecycleRounds,
                $"Completed {_roundsCompleted} rounds; expected {_options.InputLifecycleRounds}.");
            Require(_featureCyclesCompleted == expectedFeatureCycles,
                $"Completed {_featureCyclesCompleted} Feature/Blank cycles; expected {expectedFeatureCycles}.");
            if (_options.InputLifecycleRounds >= 30)
            {
                Require(_featureCyclesCompleted >= 6,
                    "A 30+ round diagnostic must complete at least six Feature/Blank verification cycles.");
            }

            Require(_resizePresentsVerified == _options.InputLifecycleRounds,
                $"Verified {_resizePresentsVerified} final resize Presents; expected {_options.InputLifecycleRounds}.");
            Require(_inputBatchesCompleted == _options.InputLifecycleRounds * InputBatchesPerRound,
                $"Verified {_inputBatchesCompleted} input batches; expected " +
                $"{_options.InputLifecycleRounds * InputBatchesPerRound}.");
            Require(CountFrameStartingSubscriptions(_mainWindow) == 1,
                "The main window did not retain exactly one FrameStarting subscription at completion.");
            Require(CountEventSubscribers(FrameStartingField) == _initialFrameStartingSubscribers,
                "FrameStarting subscriptions did not finish at their initial baseline.");
            Require(ReadInt32(RenderableWindowCountField) == _initialRenderableWindows,
                "Rendering-window count did not finish at its initial baseline.");

            ProcessSnapshot finish = CaptureProcessSnapshot();
            (int weakAlive, int weakDead) = CountWeakWindows();
            int expectedRendering =
                _settledRenderingSubscribersAfterFeature ?? _initialRenderingSubscribers;
            int expectedComposition =
                _settledCompositionSubscribersAfterFeature ?? _initialCompositionSubscribers;
            Dictionary<string, int> expectedRenderingOwners =
                _settledRenderingOwnersAfterFeature ?? _initialRenderingOwners;
            Require(CountEventSubscribers(RenderingField) == expectedRendering,
                "Rendering subscriptions did not finish at their settled blank-state baseline.");
            Require(ReadInt32(CompositionSubscriberCountField) == expectedComposition,
                "CompositionTarget subscriber count did not finish at its settled blank-state baseline.");
            Require(OwnerCountsEqual(CaptureRenderingOwners(), expectedRenderingOwners),
                "CompositionTarget.Rendering owners did not finish at their settled blank-state baseline.");

            _completed = true;
            Marker(
                $"INPUT_LIFECYCLE_OK rounds={_roundsCompleted} featureCycles={_featureCyclesCompleted} " +
                $"inputBatches={_inputBatchesCompleted} syntheticMessages=" +
                $"{_roundsCompleted * InputBatchesPerRound * InputPointsPerBatch} " +
                $"resizePresents={_resizePresentsVerified} weakAlive={weakAlive} weakDead={weakDead} " +
                $"frameStarting={CountEventSubscribers(FrameStartingField)} " +
                $"rendering={CountEventSubscribers(RenderingField)} " +
                $"compositionSubscribers={ReadInt32(CompositionSubscriberCountField)} " +
                $"renderingOwners={FormatOwnerCounts(CaptureRenderingOwners())} " +
                $"renderableWindows={ReadInt32(RenderableWindowCountField)} " +
                $"workingSetStart={_startingWorkingSetBytes} workingSetEnd={finish.WorkingSetBytes} " +
                $"workingSetDelta={finish.WorkingSetBytes - _startingWorkingSetBytes} " +
                $"privateStart={_startingPrivateMemoryBytes} privateEnd={finish.PrivateMemoryBytes} " +
                $"privateDelta={finish.PrivateMemoryBytes - _startingPrivateMemoryBytes} " +
                $"managedStart={_startingManagedBytes} managedEnd={finish.ManagedBytes} " +
                $"managedDelta={finish.ManagedBytes - _startingManagedBytes} " +
                $"allocatedStart={_startingAllocatedBytes} allocatedEnd={finish.AllocatedBytes} " +
                $"allocatedDelta={finish.AllocatedBytes - _startingAllocatedBytes} " +
                $"elapsedMs={elapsedMilliseconds - _startedMilliseconds} forcedGc=false trim=false");

            Shutdown(ExitSuccess);
        }

        private static void ReportMetric(string stage, long elapsedMilliseconds)
        {
            ProcessSnapshot process = CaptureProcessSnapshot();
            var gcInfo = GC.GetGCMemoryInfo();
            (int retiredContexts, int retiredPins) = CountRetiredContexts();
            (int weakAlive, int weakDead) = CountWeakWindows();
            Window? window = _temporaryWindow;
            long temporaryFrames = window?.FrameHistory.TotalFrames ??
                _closedFrameHistory?.TotalFrames ?? 0;
            int tempFrameStarting = window != null
                ? CountFrameStartingSubscriptions(window)
                : TryCountCurrentClosedFrameStartingSubscriptions();
            int clientWidth = 0;
            int clientHeight = 0;
            long quietElapsedMilliseconds = string.Equals(stage, "quiet", StringComparison.Ordinal)
                ? Math.Max(0, elapsedMilliseconds - _quietStartedMilliseconds)
                : 0;
            if (window is { Handle: not 0 } && GetClientRect(window.Handle, out NativeRect client))
            {
                clientWidth = client.Width;
                clientHeight = client.Height;
            }

            Marker(
                $"INPUT_LIFECYCLE_METRIC round={_round} stage={stage} " +
                $"elapsedMs={elapsedMilliseconds} roundElapsedMs={elapsedMilliseconds - _roundStartedMilliseconds} " +
                $"quietElapsedMs={quietElapsedMilliseconds} " +
                $"quietChecks={_quietTicks} " +
                $"workingSetBytes={process.WorkingSetBytes} privateBytes={process.PrivateMemoryBytes} " +
                $"managedBytes={process.ManagedBytes} allocatedBytes={process.AllocatedBytes} " +
                $"lastGcHeapBytes={gcInfo.HeapSizeBytes} lastGcCommittedBytes={gcInfo.TotalCommittedBytes} " +
                $"lastGcFragmentedBytes={gcInfo.FragmentedBytes} " +
                $"gen0={process.Gen0} gen1={process.Gen1} gen2={process.Gen2} " +
                $"threads={process.ThreadCount} handles={process.HandleCount} " +
                $"mainBackend={_mainWindow.CurrentRenderBackend} " +
                $"currentContextBackend={RenderContext.Current?.Backend.ToString() ?? "None"} " +
                $"retiredContexts={retiredContexts} retiredResourcePins={retiredPins} " +
                $"openWindows={OpenWindows.Count} shownWindows={ShownWindows.Count} " +
                $"frameStarting={CountEventSubscribers(FrameStartingField)} " +
                $"mainFrameStarting={CountFrameStartingSubscriptions(_mainWindow)} " +
                $"tempFrameStarting={tempFrameStarting} " +
                $"rendering={CountEventSubscribers(RenderingField)} " +
                $"renderingOwners={FormatOwnerCounts(CaptureRenderingOwners())} " +
                $"renderableWindows={ReadInt32(RenderableWindowCountField)} " +
                $"compositionSubscribers={ReadInt32(CompositionSubscriberCountField)} " +
                $"mainFrames={_mainWindow.FrameHistory.TotalFrames} tempFrames={temporaryFrames} " +
                $"clientWidth={clientWidth} clientHeight={clientHeight} " +
                $"renderTargetWidth={window?.RenderTarget?.Width ?? 0} " +
                $"renderTargetHeight={window?.RenderTarget?.Height ?? 0} " +
                $"previewMouseMove={_previewMouseMoveCount} mouseMove={_mouseMoveCount} " +
                $"pointerMove={_pointerMoveCount} pointerMoved={_pointerMovedCount} " +
                $"externalPreviewMouseMove={_externalPreviewMouseMoveCount} " +
                $"externalMouseMove={_externalMouseMoveCount} " +
                $"externalPointerMove={_externalPointerMoveCount} " +
                $"externalPointerMoved={_externalPointerMovedCount} " +
                $"externalSizeChanged={_externalSizeChangedCount} " +
                $"sizeChanged={_sizeChangedCount} shown={_shownCount} closed={_closedCount} " +
                $"syntheticMessages={_roundSyntheticMessages} weakAlive={weakAlive} weakDead={weakDead}");
        }

        private static (int Count, int Pins) CountRetiredContexts()
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            object gate = typeof(RenderContext).GetField("s_sync", flags)!.GetValue(null)!;
            var retiredField = typeof(RenderContext).GetField("_retiredContexts", flags)!;
            var pinsField = typeof(RenderContext).GetField("_activeRenderTargetCount",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            lock (gate)
            {
                int count = 0, pins = 0;
                foreach (RenderContext context in (System.Collections.IEnumerable)retiredField.GetValue(null)!)
                {
                    count++;
                    pins += (int)pinsField.GetValue(context)!;
                }
                return (count, pins);
            }
        }

        private static void ValidateSettledCompositionState()
        {
            int renderingSubscribers = CountEventSubscribers(RenderingField);
            int compositionSubscribers = ReadInt32(CompositionSubscriberCountField);
            Dictionary<string, int> renderingOwners = CaptureRenderingOwners();

            if (_settledRenderingOwnersAfterFeature != null)
            {
                int settledRendering = _settledRenderingSubscribersAfterFeature ??
                    throw new InvalidOperationException(
                        "Settled Rendering owners exist without a settled delegate count.");
                int settledComposition = _settledCompositionSubscribersAfterFeature ??
                    throw new InvalidOperationException(
                        "Settled Rendering owners exist without a settled CompositionTarget subscriber count.");
                Require(renderingSubscribers == settledRendering,
                    $"Round {_round}: CompositionTarget.Rendering subscriptions accumulated after the " +
                    $"first feature cycle: actual={renderingSubscribers}, " +
                    $"expected={settledRendering}.");
                Require(compositionSubscribers == settledComposition,
                    $"Round {_round}: CompositionTarget subscriber count accumulated after the first " +
                    $"feature cycle: actual={compositionSubscribers}, " +
                    $"expected={settledComposition}.");
                Require(OwnerCountsEqual(renderingOwners, _settledRenderingOwnersAfterFeature),
                    $"Round {_round}: CompositionTarget.Rendering owner set changed after the first feature cycle. " +
                    $"actual={FormatOwnerCounts(renderingOwners)} " +
                    $"expected={FormatOwnerCounts(_settledRenderingOwnersAfterFeature)}.");
                return;
            }

            if (!_roundRanFeatureCycle)
            {
                Require(renderingSubscribers == _initialRenderingSubscribers,
                    $"Round {_round}: CompositionTarget.Rendering subscriptions changed before any feature cycle: " +
                    $"actual={renderingSubscribers}, expected={_initialRenderingSubscribers}.");
                Require(compositionSubscribers == _initialCompositionSubscribers,
                    $"Round {_round}: CompositionTarget subscriber count changed before any feature cycle: " +
                    $"actual={compositionSubscribers}, expected={_initialCompositionSubscribers}.");
                Require(OwnerCountsEqual(renderingOwners, _initialRenderingOwners),
                    $"Round {_round}: CompositionTarget.Rendering owner set changed before any feature cycle. " +
                    $"actual={FormatOwnerCounts(renderingOwners)} " +
                    $"expected={FormatOwnerCounts(_initialRenderingOwners)}.");
                return;
            }

            // A feature may initialize an application-lifetime rendering service.
            // Accept that first blank-state baseline only when the concrete added
            // delegate owners explain the change, no baseline owner disappeared, and
            // both scalar counts never moved backwards. Every later round then has to
            // match this exact owner multiset and both exact scalar counts.
            Dictionary<string, int> addedOwners = SubtractOwnerCounts(
                renderingOwners,
                _initialRenderingOwners);
            Dictionary<string, int> removedOwners = SubtractOwnerCounts(
                _initialRenderingOwners,
                renderingOwners);
            int renderingDelta = renderingSubscribers - _initialRenderingSubscribers;
            int compositionDelta = compositionSubscribers - _initialCompositionSubscribers;

            Require(removedOwners.Count == 0,
                $"Round {_round}: first feature cycle removed baseline Rendering owners: " +
                FormatOwnerCounts(removedOwners));
            Require(renderingDelta >= 0,
                $"Round {_round}: Rendering subscriber count dropped from " +
                $"{_initialRenderingSubscribers} to {renderingSubscribers}.");
            Require(compositionDelta >= 0,
                $"Round {_round}: CompositionTarget subscriber count dropped from " +
                $"{_initialCompositionSubscribers} to {compositionSubscribers}.");
            Require(renderingDelta == SumOwnerCounts(addedOwners),
                $"Round {_round}: added Rendering owners do not explain the delegate-count delta. " +
                $"delta={renderingDelta}, added={FormatOwnerCounts(addedOwners)}.");
            Require(compositionDelta <= renderingDelta,
                $"Round {_round}: CompositionTarget subscriber-count growth ({compositionDelta}) exceeds " +
                $"the concrete added Rendering delegate count ({renderingDelta}).");
            Require(
                (renderingDelta == 0 && compositionDelta == 0) || addedOwners.Count > 0,
                $"Round {_round}: the first feature cycle changed composition subscriptions without " +
                "a concrete added Rendering owner.");

            _settledRenderingSubscribersAfterFeature = renderingSubscribers;
            _settledCompositionSubscribersAfterFeature = compositionSubscribers;
            _settledRenderingOwnersAfterFeature = renderingOwners;
            Marker(
                $"INPUT_LIFECYCLE_RENDERING_BASELINE round={_round} " +
                $"initialRendering={_initialRenderingSubscribers} settledRendering={renderingSubscribers} " +
                $"initialComposition={_initialCompositionSubscribers} " +
                $"settledComposition={compositionSubscribers} " +
                $"addedOwners={FormatOwnerCounts(addedOwners)} " +
                $"removedOwners={FormatOwnerCounts(removedOwners)} " +
                $"settledOwners={FormatOwnerCounts(renderingOwners)}");
        }

        private static Dictionary<string, int> CaptureRenderingOwners()
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (Delegate callback in
                (RenderingField.GetValue(null) as Delegate)?.GetInvocationList() ?? Array.Empty<Delegate>())
            {
                string targetType = callback.Target?.GetType().FullName ?? "<static>";
                string declaringType = callback.Method.DeclaringType?.FullName ?? "<unknown>";
                string owner = $"{targetType}|{declaringType}.{callback.Method.Name}";
                counts.TryGetValue(owner, out int current);
                counts[owner] = current + 1;
            }

            return counts;
        }

        private static Dictionary<string, int> SubtractOwnerCounts(
            IReadOnlyDictionary<string, int> left,
            IReadOnlyDictionary<string, int> right)
        {
            var difference = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach ((string owner, int leftCount) in left)
            {
                right.TryGetValue(owner, out int rightCount);
                int delta = leftCount - rightCount;
                if (delta > 0)
                {
                    difference[owner] = delta;
                }
            }

            return difference;
        }

        private static bool OwnerCountsEqual(
            IReadOnlyDictionary<string, int> left,
            IReadOnlyDictionary<string, int> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            foreach ((string owner, int count) in left)
            {
                if (!right.TryGetValue(owner, out int otherCount) || otherCount != count)
                {
                    return false;
                }
            }

            return true;
        }

        private static int SumOwnerCounts(IReadOnlyDictionary<string, int> owners)
        {
            int sum = 0;
            foreach (int count in owners.Values)
            {
                sum = checked(sum + count);
            }

            return sum;
        }

        private static string FormatOwnerCounts(IReadOnlyDictionary<string, int> owners)
        {
            if (owners.Count == 0)
            {
                return "<none>";
            }

            return string.Join(
                ";",
                owners
                    .OrderBy(static entry => entry.Key, StringComparer.Ordinal)
                    .Select(static entry =>
                        $"{Uri.EscapeDataString(entry.Key)}:{entry.Value}"));
        }

        private static void VerifyDeadlines(long elapsedMilliseconds)
        {
            if (elapsedMilliseconds > _overallDeadlineMilliseconds)
            {
                throw new TimeoutException(
                    $"Input lifecycle overall deadline expired after " +
                    $"{elapsedMilliseconds - _startedMilliseconds}ms; " +
                    $"limit={_overallDeadlineMilliseconds - _startedMilliseconds}ms.");
            }

            if (elapsedMilliseconds - _lastProgressMilliseconds > NoProgressTimeoutMilliseconds)
            {
                throw new TimeoutException(
                    $"Round {_round} made no progress for " +
                    $"{elapsedMilliseconds - _lastProgressMilliseconds}ms in phase {_phase}; " +
                    $"limit={NoProgressTimeoutMilliseconds}ms.");
            }
        }

        private static void Advance(Phase next, long elapsedMilliseconds)
        {
            _phase = next;
            _lastProgressMilliseconds = elapsedMilliseconds;
        }

        private static Window GetTemporaryWindow()
            => _temporaryWindow ?? throw new InvalidOperationException(
                $"Round {_round}: phase {_phase} has no active temporary window.");

        private static int CountEventSubscribers(FieldInfo field)
            => (field.GetValue(null) as Delegate)?.GetInvocationList().Length ?? 0;

        private static int CountFrameStartingSubscriptions(Window window)
            => (FrameStartingField.GetValue(null) as Delegate)?.GetInvocationList()
                .Count(callback => ReferenceEquals(callback.Target, window)) ?? 0;

        private static int TryCountCurrentClosedFrameStartingSubscriptions()
        {
            if (_currentClosedWindow is { } current && current.TryGetTarget(out Window? window))
            {
                return CountFrameStartingSubscriptions(window);
            }

            return 0;
        }

        private static long GetLatestFrameTimestamp(FrameHistory history)
        {
            int count = history.CopyTo(FrameSamples);
            return count == 0 ? 0 : FrameSamples[count - 1].TimestampTicks;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static (int Alive, int Dead) CountWeakWindows()
        {
            int alive = 0;
            foreach (WeakReference<Window> reference in ClosedWindows)
            {
                if (reference.TryGetTarget(out _))
                {
                    alive++;
                }
            }

            return (alive, ClosedWindows.Count - alive);
        }

        private static ProcessSnapshot CaptureProcessSnapshot()
        {
            using Process process = Process.GetCurrentProcess();
            process.Refresh();
            return new ProcessSnapshot(
                process.WorkingSet64,
                process.PrivateMemorySize64,
                GC.GetTotalMemory(forceFullCollection: false),
                GC.GetTotalAllocatedBytes(precise: false),
                GC.CollectionCount(0),
                GC.CollectionCount(1),
                GC.CollectionCount(2),
                process.Threads.Count,
                process.HandleCount);
        }

        private static FieldInfo RequireField(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.NonPublicFields)] Type type,
            string name,
            BindingFlags flags)
            => type.GetField(name, flags) ?? throw new InvalidOperationException(
                $"Required diagnostic field {type.FullName}.{name} was not found.");

        private static int ReadInt32(FieldInfo field)
            => field.GetValue(null) is int value
                ? value
                : throw new InvalidOperationException($"Field {field.Name} did not contain Int32.");

        private static long ReadInt64(FieldInfo field, object instance)
            => field.GetValue(instance) is long value
                ? value
                : throw new InvalidOperationException($"Field {field.Name} did not contain Int64.");

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static nint PackClientPoint(int x, int y)
        {
            long packed = (ushort)x | ((long)(ushort)y << 16);
            return (nint)packed;
        }

        private static void OnPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_sendingSyntheticInput)
                _previewMouseMoveCount++;
            else
                _externalPreviewMouseMoveCount++;
        }

        private static void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_sendingSyntheticInput)
                _mouseMoveCount++;
            else
                _externalMouseMoveCount++;
        }

        private static void OnPointerMove(object sender, RoutedEventArgs e)
        {
            if (_sendingSyntheticInput)
                _pointerMoveCount++;
            else
                _externalPointerMoveCount++;
        }

        private static void OnPointerMoved(object sender, RoutedEventArgs e)
        {
            if (_sendingSyntheticInput)
                _pointerMovedCount++;
            else
                _externalPointerMovedCount++;
        }

        private static void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_issuingResizeBurst)
                _sizeChangedCount++;
            else
                _externalSizeChangedCount++;
        }

        private static void OnTemporaryWindowShown(object? sender, EventArgs e)
            => _shownCount++;

        private static void OnTemporaryWindowClosed(object? sender, EventArgs e)
            => _closedCount++;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(nint hWnd, out NativeRect rect);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(nint hWnd, out NativeRect rect);

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

        [DllImport("user32.dll")]
        private static extern nint SendMessageW(nint hWnd, uint message, nint wParam, nint lParam);

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

        private readonly record struct ProcessSnapshot(
            long WorkingSetBytes,
            long PrivateMemoryBytes,
            long ManagedBytes,
            long AllocatedBytes,
            int Gen0,
            int Gen1,
            int Gen2,
            int ThreadCount,
            int HandleCount);

        private enum Phase
        {
            BeginRound,
            ShowHide,
            SendInput,
            ValidateInput,
            ResizeBurst,
            AwaitResizePresent,
            FeatureAttach,
            AwaitFeatureAttach,
            FeatureTheme,
            AwaitFeatureTheme,
            FeatureBlank,
            AwaitFeatureBlank,
            RecordRound,
            CloseWindow,
            Quiet,
        }
    }
}

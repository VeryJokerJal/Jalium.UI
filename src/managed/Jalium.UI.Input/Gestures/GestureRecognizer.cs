using System.Diagnostics;
using Jalium.UI.Threading;

// This is the cross-platform pointer gesture engine used by Jalium internals.
// It intentionally is not a public WPF compatibility surface: WPF manipulation
// contracts live in Jalium.UI.Input, while ink gesture recognition lives in
// Jalium.UI.Ink.
namespace Jalium.UI.Input.Internal.Gestures;

/// <summary>
/// Specifies the types of gestures that can be recognized.
/// </summary>
[Flags]
internal enum GestureSettings
{
    None = 0,
    Tap = 1 << 0,
    DoubleTap = 1 << 1,
    Hold = 1 << 2,
    HoldWithMouse = 1 << 3,
    RightTap = 1 << 4,
    Drag = 1 << 5,
    CrossSlide = 1 << 6,
    ManipulationTranslateX = 1 << 7,
    ManipulationTranslateY = 1 << 8,
    ManipulationTranslateRailsX = 1 << 9,
    ManipulationTranslateRailsY = 1 << 10,
    ManipulationRotate = 1 << 11,
    ManipulationScale = 1 << 12,
    ManipulationTranslateInertia = 1 << 13,
    ManipulationRotateInertia = 1 << 14,
    ManipulationScaleInertia = 1 << 15,
    ManipulationAll = ManipulationTranslateX | ManipulationTranslateY |
                      ManipulationRotate | ManipulationScale |
                      ManipulationTranslateInertia | ManipulationRotateInertia |
                      ManipulationScaleInertia
}

/// <summary>
/// Recognises tap/double-tap/hold/right-tap/drag and multi-finger manipulation
/// gestures from a stream of pointer events. Hold and double-tap timing rely on
/// a <see cref="DispatcherTimer"/> on the dispatcher passed to the constructor
/// (defaults to the current thread's dispatcher). Tests can drive timing with
/// <see cref="AdvanceClockForTesting"/>.
/// </summary>
internal sealed class GestureRecognizer
{
    // Public-tunable thresholds. WPF / WinUI defaults.
    public static int TapTimeoutMs { get; set; } = 300;
    public static int DoubleTapTimeoutMs { get; set; } = 500;
    public static int HoldThresholdMs { get; set; } = 500;
    public static double TapDistanceThresholdDips { get; set; } = 8.0;
    public static double DoubleTapDistanceThresholdDips { get; set; } = 16.0;
    public static double DragThresholdDips { get; set; } = 8.0;

    // Per-pointer state.
    private sealed class PointerState
    {
        public PointerPoint Down { get; }
        public Point DownPosition => Down.Position;
        public Point LastPosition { get; set; }
        public long DownTicks { get; }
        public bool HoldFired { get; set; }
        public bool DragFired { get; set; }
        public bool Moved { get; set; }
        public PointerState(PointerPoint down, long nowTicks)
        {
            Down = down;
            LastPosition = down.Position;
            DownTicks = nowTicks;
        }
    }

    private readonly Dictionary<uint, PointerState> _active = new();
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _holdTimer;

    // Double-tap tracking: timestamp + position of the most recent Tap that was fired.
    private long _lastTapTicks;
    private Point _lastTapPosition;
    private PointerDeviceType _lastTapDeviceType;

    private Point _manipulationOrigin;
    private Point _lastCentroid;
    private double _lastSpread;
    private double _lastAngle;
    private bool _isManipulating;

    // Test injection — overrides Environment.TickCount64 for deterministic timing.
    private long _testClockOffsetTicks;

    private GestureSettings _gestureSettings = GestureSettings.None;

    #region Events

    public event EventHandler<TappedEventArgs>? Tapped;
    public event EventHandler<TappedEventArgs>? DoubleTapped;
    public event EventHandler<HoldingEventArgs>? Holding;
    public event EventHandler<RightTappedEventArgs>? RightTapped;
    public event EventHandler<DraggingEventArgs>? Dragging;
    public event EventHandler<ManipulationStartedEventArgs>? ManipulationStarted;
    public event EventHandler<ManipulationDeltaEventArgs>? ManipulationDelta;
    public event EventHandler<ManipulationCompletedEventArgs>? ManipulationCompleted;
    public event EventHandler<ManipulationInertiaStartingEventArgs>? ManipulationInertiaStarting;

    #endregion

    #region Properties

    public GestureSettings GestureSettings
    {
        get => _gestureSettings;
        set => _gestureSettings = value;
    }

    public bool InertiaTranslationDisplacement { get; set; }
    public float InertiaRotationAngle { get; set; }
    public float InertiaExpansion { get; set; }
    public bool IsActive => _isManipulating;
    public bool IsInertial { get; private set; }
    public Point? PivotCenter { get; set; }
    public float PivotRadius { get; set; }
    public bool AutoProcessInertia { get; set; } = true;

    #endregion

    public GestureRecognizer()
        : this(Dispatcher.CurrentDispatcher) { }

    public GestureRecognizer(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _holdTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(HoldThresholdMs), DispatcherPriority.Input, OnHoldTimerTick, _dispatcher)
        {
            IsEnabled = false
        };
    }

    private long Now() => Environment.TickCount64 + _testClockOffsetTicks;

    /// <summary>
    /// Test-only: advances the recogniser's internal clock by <paramref name="ms"/> and
    /// fires the hold timer once if its threshold has elapsed.
    /// </summary>
    internal void AdvanceClockForTesting(long ms)
    {
        _testClockOffsetTicks += ms;
        OnHoldTimerTick(this, EventArgs.Empty);
    }

    #region Methods

    public void ProcessDownEvent(PointerPoint value)
    {
        ArgumentNullException.ThrowIfNull(value);
        long now = Now();
        var state = new PointerState(value, now);
        _active[value.PointerId] = state;

        // Mouse/pen right-button down: synthesize RightTapped on up. We track via state.
        // We don't fire RightTapped here — wait for up to be classified as Tap not Drag.

        if (_active.Count == 1)
        {
            _manipulationOrigin = value.Position;
        }

        // Re-arm the hold timer for the first contact only (single-finger hold).
        if ((_gestureSettings & GestureSettings.Hold) != 0 && _active.Count == 1)
        {
            _holdTimer.Interval = TimeSpan.FromMilliseconds(HoldThresholdMs);
            _holdTimer.IsEnabled = false;
            _holdTimer.IsEnabled = true;
        }

        if (CanStartManipulation())
        {
            UpdateManipulationBaseline();
            if (!_isManipulating)
            {
                StartManipulation(value.Position);
            }
        }
    }

    public void ProcessMoveEvents(IList<PointerPoint> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        foreach (var point in values)
        {
            if (!_active.TryGetValue(point.PointerId, out var state)) continue;
            state.LastPosition = point.Position;
            double dx = point.Position.X - state.DownPosition.X;
            double dy = point.Position.Y - state.DownPosition.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance > TapDistanceThresholdDips)
            {
                state.Moved = true;
            }

            // Once movement exceeds the drag threshold, the Drag gesture takes over
            // and Hold is cancelled.
            if (!state.DragFired && distance > DragThresholdDips && (_gestureSettings & GestureSettings.Drag) != 0)
            {
                state.DragFired = true;
                Dragging?.Invoke(this, new DraggingEventArgs(point.PointerDeviceType, point.Position, DraggingState.Started));
                if (state.HoldFired)
                {
                    // Hold turned into a drag — cancel Hold.
                    Holding?.Invoke(this, new HoldingEventArgs(point.PointerDeviceType, point.Position, HoldingState.Canceled));
                    state.HoldFired = false;
                }
                _holdTimer.IsEnabled = false;
            }
            else if (state.DragFired)
            {
                Dragging?.Invoke(this, new DraggingEventArgs(point.PointerDeviceType, point.Position, DraggingState.Continuing));
            }
        }

        if (_isManipulating)
        {
            ProcessManipulationDelta();
        }
    }

    public void ProcessUpEvent(PointerPoint value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!_active.TryGetValue(value.PointerId, out var state))
        {
            return;
        }
        _active.Remove(value.PointerId);

        long now = Now();
        long elapsed = now - state.DownTicks;
        bool wasShortTap = !state.Moved && elapsed <= TapTimeoutMs;

        // Drag completion takes priority — no Tap/RightTap fires after drag.
        if (state.DragFired)
        {
            Dragging?.Invoke(this, new DraggingEventArgs(value.PointerDeviceType, value.Position, DraggingState.Completed));
        }
        else if (state.HoldFired)
        {
            Holding?.Invoke(this, new HoldingEventArgs(value.PointerDeviceType, value.Position, HoldingState.Completed));
            // A completed hold with touch is equivalent to a right-tap (touch context-menu trigger).
            if ((_gestureSettings & GestureSettings.RightTap) != 0 && value.PointerDeviceType == PointerDeviceType.Touch)
            {
                RightTapped?.Invoke(this, new RightTappedEventArgs(value.PointerDeviceType, value.Position));
            }
        }
        else if (wasShortTap)
        {
            // Distinguish right-tap (pen barrel / mouse right button) from regular tap.
            bool isRight = value.Properties.IsRightButtonPressed
                           || value.Properties.IsBarrelButtonPressed
                           || (value.PointerDeviceType == PointerDeviceType.Mouse &&
                               value.Properties.PointerUpdateKind == PointerUpdateKind.RightButtonReleased);
            if (isRight && (_gestureSettings & GestureSettings.RightTap) != 0)
            {
                RightTapped?.Invoke(this, new RightTappedEventArgs(value.PointerDeviceType, value.Position));
            }
            else if ((_gestureSettings & GestureSettings.Tap) != 0)
            {
                // Double-tap?
                double ddx = value.Position.X - _lastTapPosition.X;
                double ddy = value.Position.Y - _lastTapPosition.Y;
                double doubleDist = Math.Sqrt(ddx * ddx + ddy * ddy);
                bool isDouble = (_gestureSettings & GestureSettings.DoubleTap) != 0
                                && _lastTapTicks > 0
                                && now - _lastTapTicks <= DoubleTapTimeoutMs
                                && doubleDist <= DoubleTapDistanceThresholdDips
                                && _lastTapDeviceType == value.PointerDeviceType;
                if (isDouble)
                {
                    DoubleTapped?.Invoke(this, new TappedEventArgs(value.PointerDeviceType, value.Position, tapCount: 2));
                    _lastTapTicks = 0; // consume the previous tap
                }
                else
                {
                    Tapped?.Invoke(this, new TappedEventArgs(value.PointerDeviceType, value.Position));
                    _lastTapTicks = now;
                    _lastTapPosition = value.Position;
                    _lastTapDeviceType = value.PointerDeviceType;
                }
            }
        }

        if (_active.Count == 0)
        {
            _holdTimer.IsEnabled = false;
            if (_isManipulating)
            {
                CompleteManipulation();
            }
        }
    }

    public void ProcessMouseWheelEvent(PointerPoint value, bool isShiftKeyDown, bool isControlKeyDown)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (isControlKeyDown && (_gestureSettings & GestureSettings.ManipulationScale) != 0)
        {
            float delta = value.Properties.MouseWheelDelta / 120.0f;
            float scale = delta > 0 ? 1.1f : 0.9f;
            RaiseManipulationDelta(new ManipulationDelta(Point.Zero, 0, scale));
        }
    }

    /// <summary>
    /// Cancels any active gesture (e.g. window deactivation, focus loss).
    /// </summary>
    public void CompleteGesture()
    {
        if (_isManipulating)
        {
            CompleteManipulation();
        }
        _active.Clear();
        _holdTimer.IsEnabled = false;
    }

    /// <summary>
    /// Drives the inertia phase. The recogniser does not own its own inertia loop —
    /// host code should drive the manipulation events; this method is preserved for
    /// API surface compatibility and currently exposes a no-op (real inertia is in
    /// <c>ManipulationInertiaProcessor</c> in Jalium.UI.Controls).
    /// </summary>
    public void ProcessInertia()
    {
        // The shared exponential-decay inertia engine lives in Jalium.UI.Controls /
        // ManipulationInertiaProcessor. The recogniser surfaces the InertiaStarting
        // event; consumers wire the InertiaProcessor themselves.
    }

    #endregion

    #region Private Methods

    private void OnHoldTimerTick(object? sender, EventArgs e)
    {
        _holdTimer.IsEnabled = false;
        if (_active.Count != 1) return;
        long now = Now();
        var first = _active.Values.First();
        if (first.HoldFired || first.DragFired || first.Moved) return;
        if (now - first.DownTicks < HoldThresholdMs) return;
        if ((_gestureSettings & GestureSettings.Hold) == 0) return;
        first.HoldFired = true;
        Holding?.Invoke(this, new HoldingEventArgs(first.Down.PointerDeviceType, first.LastPosition, HoldingState.Started));
    }

    private bool CanStartManipulation()
        => (_gestureSettings & GestureSettings.ManipulationAll) != 0;

    private void StartManipulation(Point position)
    {
        _isManipulating = true;
        _manipulationOrigin = position;
        ManipulationStarted?.Invoke(this, new ManipulationStartedEventArgs(position));
    }

    private void UpdateManipulationBaseline()
    {
        _lastCentroid = ComputeCentroid();
        _lastSpread = ComputeSpread(_lastCentroid);
        _lastAngle = ComputeAngle(_lastCentroid);
    }

    private void ProcessManipulationDelta()
    {
        if (_active.Count == 0) return;
        Point centroid = ComputeCentroid();
        var translation = new Point(centroid.X - _lastCentroid.X, centroid.Y - _lastCentroid.Y);

        float scale = 1.0f;
        float rotation = 0.0f;
        if (_active.Count >= 2)
        {
            double spread = ComputeSpread(centroid);
            if (_lastSpread > 0.0001)
            {
                if ((_gestureSettings & GestureSettings.ManipulationScale) != 0)
                {
                    scale = (float)(spread / _lastSpread);
                }
            }
            double angle = ComputeAngle(centroid);
            if ((_gestureSettings & GestureSettings.ManipulationRotate) != 0)
            {
                rotation = (float)WrapAngle(angle - _lastAngle);
            }
            _lastSpread = spread;
            _lastAngle = angle;
        }

        _lastCentroid = centroid;
        var delta = new ManipulationDelta(translation, rotation, scale);
        RaiseManipulationDelta(delta);
    }

    private Point ComputeCentroid()
    {
        if (_active.Count == 0) return _manipulationOrigin;
        double sx = 0, sy = 0;
        foreach (var s in _active.Values) { sx += s.LastPosition.X; sy += s.LastPosition.Y; }
        return new Point(sx / _active.Count, sy / _active.Count);
    }

    private double ComputeSpread(Point centroid)
    {
        if (_active.Count == 0) return 0;
        double sum = 0;
        foreach (var s in _active.Values)
        {
            double dx = s.LastPosition.X - centroid.X;
            double dy = s.LastPosition.Y - centroid.Y;
            sum += Math.Sqrt(dx * dx + dy * dy);
        }
        return sum / _active.Count;
    }

    private double ComputeAngle(Point centroid)
    {
        foreach (var s in _active.Values)
        {
            return Math.Atan2(s.LastPosition.Y - centroid.Y, s.LastPosition.X - centroid.X) * (180.0 / Math.PI);
        }
        return 0;
    }

    private static double WrapAngle(double deg)
    {
        while (deg > 180) deg -= 360;
        while (deg <= -180) deg += 360;
        return deg;
    }

    private void RaiseManipulationDelta(ManipulationDelta delta)
    {
        var args = new ManipulationDeltaEventArgs(
            _manipulationOrigin,
            delta,
            new ManipulationDelta(Point.Zero, 0, 1),
            new ManipulationVelocities(Point.Zero, 0, 0),
            false);
        ManipulationDelta?.Invoke(this, args);
    }

    private void CompleteManipulation()
    {
        _isManipulating = false;
        var args = new ManipulationCompletedEventArgs(
            _manipulationOrigin,
            new ManipulationDelta(Point.Zero, 0, 1),
            new ManipulationVelocities(Point.Zero, 0, 0),
            false);
        ManipulationCompleted?.Invoke(this, args);

        // Surface the InertiaStarting hook so subscribers can attach an InertiaProcessor.
        ManipulationInertiaStarting?.Invoke(this, new ManipulationInertiaStartingEventArgs(
            _manipulationOrigin,
            new ManipulationDelta(Point.Zero, 0, 1),
            new ManipulationVelocities(Point.Zero, 0, 0)));
    }

    #endregion
}

/// <summary>Represents changes in manipulation.</summary>
internal readonly struct ManipulationDelta
{
    public Point Translation { get; }
    public float Rotation { get; }
    public float Scale { get; }
    public float Expansion { get; }
    public ManipulationDelta(Point translation, float rotation, float scale, float expansion = 0)
    {
        Translation = translation;
        Rotation = rotation;
        Scale = scale;
        Expansion = expansion;
    }
}

/// <summary>Represents manipulation velocities.</summary>
internal readonly struct ManipulationVelocities
{
    public Point Linear { get; }
    public float Angular { get; }
    public float Expansion { get; }
    public ManipulationVelocities(Point linear, float angular, float expansion)
    {
        Linear = linear;
        Angular = angular;
        Expansion = expansion;
    }
}

#region Event Args

internal sealed class TappedEventArgs : EventArgs
{
    public PointerDeviceType PointerDeviceType { get; }
    public Point Position { get; }
    public uint TapCount { get; }
    public TappedEventArgs(PointerDeviceType deviceType, Point position, uint tapCount = 1)
    {
        PointerDeviceType = deviceType;
        Position = position;
        TapCount = tapCount;
    }
}

internal sealed class RightTappedEventArgs : EventArgs
{
    public PointerDeviceType PointerDeviceType { get; }
    public Point Position { get; }
    public RightTappedEventArgs(PointerDeviceType deviceType, Point position)
    {
        PointerDeviceType = deviceType;
        Position = position;
    }
}

internal enum HoldingState { Started, Completed, Canceled }

internal sealed class HoldingEventArgs : EventArgs
{
    public PointerDeviceType PointerDeviceType { get; }
    public Point Position { get; }
    public HoldingState HoldingState { get; }
    public HoldingEventArgs(PointerDeviceType deviceType, Point position, HoldingState state)
    {
        PointerDeviceType = deviceType;
        Position = position;
        HoldingState = state;
    }
}

internal enum DraggingState { Started, Continuing, Completed }

internal sealed class DraggingEventArgs : EventArgs
{
    public PointerDeviceType PointerDeviceType { get; }
    public Point Position { get; }
    public DraggingState DraggingState { get; }
    public DraggingEventArgs(PointerDeviceType deviceType, Point position, DraggingState state)
    {
        PointerDeviceType = deviceType;
        Position = position;
        DraggingState = state;
    }
}

internal sealed class ManipulationStartedEventArgs : EventArgs
{
    public Point Position { get; }
    public ManipulationStartedEventArgs(Point position) { Position = position; }
}

internal sealed class ManipulationDeltaEventArgs : EventArgs
{
    public Point Position { get; }
    public ManipulationDelta Delta { get; }
    public ManipulationDelta Cumulative { get; }
    public ManipulationVelocities Velocities { get; }
    public bool IsInertial { get; }
    public bool Complete { get; set; }
    public ManipulationDeltaEventArgs(Point position, ManipulationDelta delta, ManipulationDelta cumulative, ManipulationVelocities velocities, bool isInertial)
    {
        Position = position; Delta = delta; Cumulative = cumulative; Velocities = velocities; IsInertial = isInertial;
    }
}

internal sealed class ManipulationCompletedEventArgs : EventArgs
{
    public Point Position { get; }
    public ManipulationDelta Cumulative { get; }
    public ManipulationVelocities Velocities { get; }
    public bool IsInertial { get; }
    public ManipulationCompletedEventArgs(Point position, ManipulationDelta cumulative, ManipulationVelocities velocities, bool isInertial)
    {
        Position = position; Cumulative = cumulative; Velocities = velocities; IsInertial = isInertial;
    }
}

internal sealed class ManipulationInertiaStartingEventArgs : EventArgs
{
    public Point Position { get; }
    public ManipulationDelta Cumulative { get; }
    public ManipulationVelocities Velocities { get; }
    public InertiaTranslationBehavior? TranslationBehavior { get; set; }
    public InertiaRotationBehavior? RotationBehavior { get; set; }
    public InertiaExpansionBehavior? ExpansionBehavior { get; set; }
    public ManipulationInertiaStartingEventArgs(Point position, ManipulationDelta cumulative, ManipulationVelocities velocities)
    {
        Position = position; Cumulative = cumulative; Velocities = velocities;
    }
}

internal sealed class InertiaTranslationBehavior
{
    public double DesiredDisplacement { get; set; }
    public double DesiredDeceleration { get; set; }
}

internal sealed class InertiaRotationBehavior
{
    public double DesiredRotation { get; set; }
    public double DesiredDeceleration { get; set; }
}

internal sealed class InertiaExpansionBehavior
{
    public double DesiredExpansion { get; set; }
    public double DesiredDeceleration { get; set; }
}

#endregion

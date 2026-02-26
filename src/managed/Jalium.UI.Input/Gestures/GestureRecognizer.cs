namespace Jalium.UI.Input.Gestures;

/// <summary>
/// Specifies the types of gestures that can be recognized.
/// </summary>
[Flags]
public enum GestureSettings
{
    /// <summary>
    /// No gestures.
    /// </summary>
    None = 0,

    /// <summary>
    /// Tap gesture.
    /// </summary>
    Tap = 1 << 0,

    /// <summary>
    /// Double tap gesture.
    /// </summary>
    DoubleTap = 1 << 1,

    /// <summary>
    /// Hold gesture.
    /// </summary>
    Hold = 1 << 2,

    /// <summary>
    /// Hold with mouse gesture.
    /// </summary>
    HoldWithMouse = 1 << 3,

    /// <summary>
    /// Right tap gesture (context menu).
    /// </summary>
    RightTap = 1 << 4,

    /// <summary>
    /// Drag gesture.
    /// </summary>
    Drag = 1 << 5,

    /// <summary>
    /// Cross slide gesture.
    /// </summary>
    CrossSlide = 1 << 6,

    /// <summary>
    /// Manipulation translate X.
    /// </summary>
    ManipulationTranslateX = 1 << 7,

    /// <summary>
    /// Manipulation translate Y.
    /// </summary>
    ManipulationTranslateY = 1 << 8,

    /// <summary>
    /// Manipulation translate with rails.
    /// </summary>
    ManipulationTranslateRailsX = 1 << 9,

    /// <summary>
    /// Manipulation translate with rails.
    /// </summary>
    ManipulationTranslateRailsY = 1 << 10,

    /// <summary>
    /// Manipulation rotate.
    /// </summary>
    ManipulationRotate = 1 << 11,

    /// <summary>
    /// Manipulation scale.
    /// </summary>
    ManipulationScale = 1 << 12,

    /// <summary>
    /// Manipulation with inertia.
    /// </summary>
    ManipulationTranslateInertia = 1 << 13,

    /// <summary>
    /// Manipulation rotate with inertia.
    /// </summary>
    ManipulationRotateInertia = 1 << 14,

    /// <summary>
    /// Manipulation scale with inertia.
    /// </summary>
    ManipulationScaleInertia = 1 << 15,

    /// <summary>
    /// All manipulation gestures.
    /// </summary>
    ManipulationAll = ManipulationTranslateX | ManipulationTranslateY |
                      ManipulationRotate | ManipulationScale |
                      ManipulationTranslateInertia | ManipulationRotateInertia |
                      ManipulationScaleInertia
}

/// <summary>
/// Provides gesture and manipulation recognition.
/// </summary>
public sealed class GestureRecognizer
{
    private GestureSettings _gestureSettings = GestureSettings.None;
    private readonly List<PointerPoint> _activePointers = new();
    private Point _manipulationOrigin;
    private bool _isManipulating;

    #region Events

    /// <summary>
    /// Occurs when a tap gesture is recognized.
    /// </summary>
    public event EventHandler<TappedEventArgs>? Tapped;

    /// <summary>
    /// Occurs when a double tap gesture is recognized.
    /// </summary>
    public event EventHandler<TappedEventArgs>? DoubleTapped;

    /// <summary>
    /// Occurs when a hold gesture is recognized.
    /// </summary>
    public event EventHandler<HoldingEventArgs>? Holding;

    /// <summary>
    /// Occurs when a right tap gesture is recognized.
    /// </summary>
    public event EventHandler<RightTappedEventArgs>? RightTapped;

    /// <summary>
    /// Occurs when dragging starts.
    /// </summary>
    public event EventHandler<DraggingEventArgs>? Dragging;

    /// <summary>
    /// Occurs when manipulation starts.
    /// </summary>
    public event EventHandler<ManipulationStartedEventArgs>? ManipulationStarted;

    /// <summary>
    /// Occurs during manipulation.
    /// </summary>
    public event EventHandler<ManipulationDeltaEventArgs>? ManipulationDelta;

    /// <summary>
    /// Occurs when manipulation completes.
    /// </summary>
    public event EventHandler<ManipulationCompletedEventArgs>? ManipulationCompleted;

    /// <summary>
    /// Occurs during inertia.
    /// </summary>
    public event EventHandler<ManipulationInertiaStartingEventArgs>? ManipulationInertiaStarting;

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the gesture settings.
    /// </summary>
    public GestureSettings GestureSettings
    {
        get => _gestureSettings;
        set => _gestureSettings = value;
    }

    /// <summary>
    /// Gets or sets a value indicating whether inertia is enabled.
    /// </summary>
    public bool InertiaTranslationDisplacement { get; set; }

    /// <summary>
    /// Gets or sets the inertia rotation angle.
    /// </summary>
    public float InertiaRotationAngle { get; set; }

    /// <summary>
    /// Gets or sets the inertia expansion.
    /// </summary>
    public float InertiaExpansion { get; set; }

    /// <summary>
    /// Gets a value indicating whether a manipulation is in progress.
    /// </summary>
    public bool IsActive => _isManipulating;

    /// <summary>
    /// Gets a value indicating whether inertia is in progress.
    /// </summary>
    public bool IsInertial { get; private set; }

    /// <summary>
    /// Gets or sets the pivot center for rotation.
    /// </summary>
    public Point? PivotCenter { get; set; }

    /// <summary>
    /// Gets or sets the pivot radius.
    /// </summary>
    public float PivotRadius { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether auto-processing is enabled.
    /// </summary>
    public bool AutoProcessInertia { get; set; } = true;

    #endregion

    #region Methods

    /// <summary>
    /// Processes pointer down events.
    /// </summary>
    public void ProcessDownEvent(PointerPoint value)
    {
        _activePointers.Add(value);

        if (_activePointers.Count == 1)
        {
            _manipulationOrigin = value.Position;
        }

        if (CanStartManipulation())
        {
            StartManipulation(value.Position);
        }
    }

    /// <summary>
    /// Processes pointer move events.
    /// </summary>
    public void ProcessMoveEvents(IList<PointerPoint> values)
    {
        foreach (var point in values)
        {
            UpdatePointer(point);
        }

        if (_isManipulating)
        {
            ProcessManipulationDelta();
        }
    }

    /// <summary>
    /// Processes pointer up events.
    /// </summary>
    public void ProcessUpEvent(PointerPoint value)
    {
        RemovePointer(value.PointerId);

        if (_activePointers.Count == 0 && _isManipulating)
        {
            CompleteManipulation();
        }
    }

    /// <summary>
    /// Processes mouse wheel events.
    /// </summary>
    public void ProcessMouseWheelEvent(PointerPoint value, bool isShiftKeyDown, bool isControlKeyDown)
    {
        // Handle zoom or scroll based on modifiers
        if (isControlKeyDown && (_gestureSettings & GestureSettings.ManipulationScale) != 0)
        {
            // Zoom
            var delta = value.Properties.MouseWheelDelta / 120.0f;
            var scale = delta > 0 ? 1.1f : 0.9f;
            RaiseManipulationDelta(new ManipulationDelta(Point.Zero, 0, scale));
        }
    }

    /// <summary>
    /// Processes inertia.
    /// </summary>
    public void ProcessInertia()
    {
        // Process inertia physics
        if (IsInertial)
        {
            // Calculate decay and update position/rotation/scale
        }
    }

    /// <summary>
    /// Completes the current gesture or manipulation.
    /// </summary>
    public void CompleteGesture()
    {
        if (_isManipulating)
        {
            CompleteManipulation();
        }

        _activePointers.Clear();
    }

    #endregion

    #region Private Methods

    private bool CanStartManipulation()
    {
        return (_gestureSettings & GestureSettings.ManipulationAll) != 0;
    }

    private void StartManipulation(Point position)
    {
        _isManipulating = true;
        _manipulationOrigin = position;
        ManipulationStarted?.Invoke(this, new ManipulationStartedEventArgs(position));
    }

    private void ProcessManipulationDelta()
    {
        if (_activePointers.Count == 0) return;

        var currentPosition = _activePointers[0].Position;
        var translation = new Point(
            currentPosition.X - _manipulationOrigin.X,
            currentPosition.Y - _manipulationOrigin.Y);

        var scale = 1.0f;
        var rotation = 0.0f;

        // Calculate pinch-to-zoom and rotation for multi-touch
        if (_activePointers.Count >= 2)
        {
            var p1 = _activePointers[0].Position;
            var p2 = _activePointers[1].Position;
            // Calculate scale and rotation from two-point gestures
        }

        var delta = new ManipulationDelta(translation, rotation, scale);
        RaiseManipulationDelta(delta);
    }

    private void RaiseManipulationDelta(ManipulationDelta delta)
    {
        var args = new ManipulationDeltaEventArgs(
            _manipulationOrigin,
            delta,
            new ManipulationDelta(Point.Zero, 0, 1), // Cumulative
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
    }

    private void UpdatePointer(PointerPoint point)
    {
        for (int i = 0; i < _activePointers.Count; i++)
        {
            if (_activePointers[i].PointerId == point.PointerId)
            {
                _activePointers[i] = point;
                return;
            }
        }
    }

    private void RemovePointer(uint pointerId)
    {
        _activePointers.RemoveAll(p => p.PointerId == pointerId);
    }

    #endregion
}

/// <summary>
/// Represents changes in manipulation.
/// </summary>
public struct ManipulationDelta
{
    /// <summary>
    /// Gets the translation delta.
    /// </summary>
    public Point Translation { get; }

    /// <summary>
    /// Gets the rotation delta in degrees.
    /// </summary>
    public float Rotation { get; }

    /// <summary>
    /// Gets the scale delta.
    /// </summary>
    public float Scale { get; }

    /// <summary>
    /// Gets the expansion delta.
    /// </summary>
    public float Expansion { get; }

    /// <summary>
    /// Initializes a new instance of the ManipulationDelta struct.
    /// </summary>
    public ManipulationDelta(Point translation, float rotation, float scale, float expansion = 0)
    {
        Translation = translation;
        Rotation = rotation;
        Scale = scale;
        Expansion = expansion;
    }
}

/// <summary>
/// Represents manipulation velocities.
/// </summary>
public struct ManipulationVelocities
{
    /// <summary>
    /// Gets the linear velocity.
    /// </summary>
    public Point Linear { get; }

    /// <summary>
    /// Gets the angular velocity in degrees per millisecond.
    /// </summary>
    public float Angular { get; }

    /// <summary>
    /// Gets the expansion velocity.
    /// </summary>
    public float Expansion { get; }

    /// <summary>
    /// Initializes a new instance of the ManipulationVelocities struct.
    /// </summary>
    public ManipulationVelocities(Point linear, float angular, float expansion)
    {
        Linear = linear;
        Angular = angular;
        Expansion = expansion;
    }
}

#region Event Args

/// <summary>
/// Event arguments for tap events.
/// </summary>
public sealed class TappedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the pointer device type.
    /// </summary>
    public PointerDeviceType PointerDeviceType { get; }

    /// <summary>
    /// Gets the position.
    /// </summary>
    public Point Position { get; }

    /// <summary>
    /// Gets the tap count.
    /// </summary>
    public uint TapCount { get; }

    /// <summary>
    /// Initializes a new instance of the TappedEventArgs class.
    /// </summary>
    public TappedEventArgs(PointerDeviceType deviceType, Point position, uint tapCount = 1)
    {
        PointerDeviceType = deviceType;
        Position = position;
        TapCount = tapCount;
    }
}

/// <summary>
/// Event arguments for right tap events.
/// </summary>
public sealed class RightTappedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the pointer device type.
    /// </summary>
    public PointerDeviceType PointerDeviceType { get; }

    /// <summary>
    /// Gets the position.
    /// </summary>
    public Point Position { get; }

    /// <summary>
    /// Initializes a new instance of the RightTappedEventArgs class.
    /// </summary>
    public RightTappedEventArgs(PointerDeviceType deviceType, Point position)
    {
        PointerDeviceType = deviceType;
        Position = position;
    }
}

/// <summary>
/// Specifies the holding state.
/// </summary>
public enum HoldingState
{
    /// <summary>
    /// Holding started.
    /// </summary>
    Started,

    /// <summary>
    /// Holding completed.
    /// </summary>
    Completed,

    /// <summary>
    /// Holding cancelled.
    /// </summary>
    Canceled
}

/// <summary>
/// Event arguments for holding events.
/// </summary>
public sealed class HoldingEventArgs : EventArgs
{
    /// <summary>
    /// Gets the pointer device type.
    /// </summary>
    public PointerDeviceType PointerDeviceType { get; }

    /// <summary>
    /// Gets the position.
    /// </summary>
    public Point Position { get; }

    /// <summary>
    /// Gets the holding state.
    /// </summary>
    public HoldingState HoldingState { get; }

    /// <summary>
    /// Initializes a new instance of the HoldingEventArgs class.
    /// </summary>
    public HoldingEventArgs(PointerDeviceType deviceType, Point position, HoldingState state)
    {
        PointerDeviceType = deviceType;
        Position = position;
        HoldingState = state;
    }
}

/// <summary>
/// Specifies the dragging state.
/// </summary>
public enum DraggingState
{
    /// <summary>
    /// Dragging started.
    /// </summary>
    Started,

    /// <summary>
    /// Dragging in progress.
    /// </summary>
    Continuing,

    /// <summary>
    /// Dragging completed.
    /// </summary>
    Completed
}

/// <summary>
/// Event arguments for dragging events.
/// </summary>
public sealed class DraggingEventArgs : EventArgs
{
    /// <summary>
    /// Gets the pointer device type.
    /// </summary>
    public PointerDeviceType PointerDeviceType { get; }

    /// <summary>
    /// Gets the position.
    /// </summary>
    public Point Position { get; }

    /// <summary>
    /// Gets the dragging state.
    /// </summary>
    public DraggingState DraggingState { get; }

    /// <summary>
    /// Initializes a new instance of the DraggingEventArgs class.
    /// </summary>
    public DraggingEventArgs(PointerDeviceType deviceType, Point position, DraggingState state)
    {
        PointerDeviceType = deviceType;
        Position = position;
        DraggingState = state;
    }
}

/// <summary>
/// Event arguments for manipulation started events.
/// </summary>
public sealed class ManipulationStartedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the origin point.
    /// </summary>
    public Point Position { get; }

    /// <summary>
    /// Initializes a new instance of the ManipulationStartedEventArgs class.
    /// </summary>
    public ManipulationStartedEventArgs(Point position)
    {
        Position = position;
    }
}

/// <summary>
/// Event arguments for manipulation delta events.
/// </summary>
public sealed class ManipulationDeltaEventArgs : EventArgs
{
    /// <summary>
    /// Gets the origin point.
    /// </summary>
    public Point Position { get; }

    /// <summary>
    /// Gets the delta since the last event.
    /// </summary>
    public ManipulationDelta Delta { get; }

    /// <summary>
    /// Gets the cumulative delta since manipulation started.
    /// </summary>
    public ManipulationDelta Cumulative { get; }

    /// <summary>
    /// Gets the current velocities.
    /// </summary>
    public ManipulationVelocities Velocities { get; }

    /// <summary>
    /// Gets a value indicating whether this is an inertia event.
    /// </summary>
    public bool IsInertial { get; }

    /// <summary>
    /// Gets or sets a value indicating whether to complete the manipulation.
    /// </summary>
    public bool Complete { get; set; }

    /// <summary>
    /// Initializes a new instance of the ManipulationDeltaEventArgs class.
    /// </summary>
    public ManipulationDeltaEventArgs(
        Point position,
        ManipulationDelta delta,
        ManipulationDelta cumulative,
        ManipulationVelocities velocities,
        bool isInertial)
    {
        Position = position;
        Delta = delta;
        Cumulative = cumulative;
        Velocities = velocities;
        IsInertial = isInertial;
    }
}

/// <summary>
/// Event arguments for manipulation completed events.
/// </summary>
public sealed class ManipulationCompletedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the origin point.
    /// </summary>
    public Point Position { get; }

    /// <summary>
    /// Gets the cumulative delta.
    /// </summary>
    public ManipulationDelta Cumulative { get; }

    /// <summary>
    /// Gets the final velocities.
    /// </summary>
    public ManipulationVelocities Velocities { get; }

    /// <summary>
    /// Gets a value indicating whether inertia occurred.
    /// </summary>
    public bool IsInertial { get; }

    /// <summary>
    /// Initializes a new instance of the ManipulationCompletedEventArgs class.
    /// </summary>
    public ManipulationCompletedEventArgs(
        Point position,
        ManipulationDelta cumulative,
        ManipulationVelocities velocities,
        bool isInertial)
    {
        Position = position;
        Cumulative = cumulative;
        Velocities = velocities;
        IsInertial = isInertial;
    }
}

/// <summary>
/// Event arguments for manipulation inertia starting events.
/// </summary>
public sealed class ManipulationInertiaStartingEventArgs : EventArgs
{
    /// <summary>
    /// Gets the origin point.
    /// </summary>
    public Point Position { get; }

    /// <summary>
    /// Gets the cumulative delta.
    /// </summary>
    public ManipulationDelta Cumulative { get; }

    /// <summary>
    /// Gets the initial velocities.
    /// </summary>
    public ManipulationVelocities Velocities { get; }

    /// <summary>
    /// Gets or sets the translation behavior during inertia.
    /// </summary>
    public InertiaTranslationBehavior? TranslationBehavior { get; set; }

    /// <summary>
    /// Gets or sets the rotation behavior during inertia.
    /// </summary>
    public InertiaRotationBehavior? RotationBehavior { get; set; }

    /// <summary>
    /// Gets or sets the expansion behavior during inertia.
    /// </summary>
    public InertiaExpansionBehavior? ExpansionBehavior { get; set; }

    /// <summary>
    /// Initializes a new instance of the ManipulationInertiaStartingEventArgs class.
    /// </summary>
    public ManipulationInertiaStartingEventArgs(
        Point position,
        ManipulationDelta cumulative,
        ManipulationVelocities velocities)
    {
        Position = position;
        Cumulative = cumulative;
        Velocities = velocities;
    }
}

/// <summary>
/// Specifies translation inertia behavior.
/// </summary>
public sealed class InertiaTranslationBehavior
{
    /// <summary>
    /// Gets or sets the desired displacement.
    /// </summary>
    public double DesiredDisplacement { get; set; }

    /// <summary>
    /// Gets or sets the desired deceleration.
    /// </summary>
    public double DesiredDeceleration { get; set; }
}

/// <summary>
/// Specifies rotation inertia behavior.
/// </summary>
public sealed class InertiaRotationBehavior
{
    /// <summary>
    /// Gets or sets the desired rotation.
    /// </summary>
    public double DesiredRotation { get; set; }

    /// <summary>
    /// Gets or sets the desired deceleration.
    /// </summary>
    public double DesiredDeceleration { get; set; }
}

/// <summary>
/// Specifies expansion inertia behavior.
/// </summary>
public sealed class InertiaExpansionBehavior
{
    /// <summary>
    /// Gets or sets the desired expansion.
    /// </summary>
    public double DesiredExpansion { get; set; }

    /// <summary>
    /// Gets or sets the desired deceleration.
    /// </summary>
    public double DesiredDeceleration { get; set; }
}

#endregion

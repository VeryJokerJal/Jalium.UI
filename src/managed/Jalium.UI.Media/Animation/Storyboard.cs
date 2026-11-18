using Jalium.UI.Animation;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// A parallel timeline that supplies target information for its descendant animations.
/// </summary>
public partial class Storyboard : ParallelTimeline
{
    private static readonly HashSet<Storyboard> _activeStoryboards = [];
    private static readonly object _lock = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DependencyObject, Dictionary<DependencyProperty, RawAnimationState>> _rawAnimations = new();

    private sealed class RawAnimationState
    {
        internal readonly List<RawAnimationEntry> Entries = new();
        internal object? Snapshot;
        internal bool HasSnapshot;
        internal bool Updating;
    }

    private sealed record RawAnimationEntry(Storyboard Owner, IAnimationClock Clock, IAnimationTimeline Animation);

    private readonly List<IAnimationClock> _clocks = [];
    private readonly List<(IAnimationClock Clock, DependencyObject Target, DependencyProperty Property, bool ElementDriven)> _activeAnimations = [];
    private readonly HashSet<IAnimationClock> _settledClocks = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<IAnimationClock> _terminatedClocks = new(ReferenceEqualityComparer.Instance);
    private readonly RuntimeAdapter _runtimeAdapter;
    private int _terminatedCount;
    private bool _isRunning;
    private bool _isControllable;
    private DependencyObject? _containingObject;
    private FrameworkTemplate? _frameworkTemplate;
    private AnimationTickSubscription? _tickSubscription;
    private ClockGroup? _rootClock;
    private bool _rootCompletionPending;

    public Storyboard()
    {
        _runtimeAdapter = new RuntimeAdapter(this);
    }

    public static readonly DependencyProperty TargetNameProperty =
        DependencyProperty.RegisterAttached(
            "TargetName",
            typeof(string),
            typeof(Storyboard),
            new PropertyMetadata(null));

    public static readonly DependencyProperty TargetProperty =
        DependencyProperty.RegisterAttached(
            "Target",
            typeof(DependencyObject),
            typeof(Storyboard),
            new PropertyMetadata(null));

    public static readonly DependencyProperty TargetPropertyProperty =
        DependencyProperty.RegisterAttached(
            "TargetProperty",
            typeof(PropertyPath),
            typeof(Storyboard),
            new PropertyMetadata(null));

    public static string? GetTargetName(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (string?)element.GetValue(TargetNameProperty);
    }

    public static void SetTargetName(DependencyObject element, string? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(TargetNameProperty, value);
    }

    public static DependencyObject? GetTarget(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (DependencyObject?)element.GetValue(TargetProperty);
    }

    public static void SetTarget(DependencyObject element, DependencyObject? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(TargetProperty, value);
    }

    public static PropertyPath? GetTargetProperty(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (PropertyPath?)element.GetValue(TargetPropertyProperty);
    }

    public static void SetTargetProperty(DependencyObject element, PropertyPath? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(TargetPropertyProperty, value);
    }

    public new Storyboard Clone() => (Storyboard)base.Clone();

    protected override Freezable CreateInstanceCore() => new Storyboard();

    public static void StopAll()
    {
        Storyboard[] storyboards;
        lock (_lock)
        {
            storyboards = _activeStoryboards.ToArray();
        }

        foreach (Storyboard storyboard in storyboards)
        {
            storyboard.Stop();
        }
    }

    public void Begin() =>
        BeginCore(null, null, HandoffBehavior.SnapshotAndReplace, isControllable: true);

    public void Begin(FrameworkElement? containingObject)
    {
        BeginCore(containingObject, null, HandoffBehavior.SnapshotAndReplace, isControllable: false);
    }

    public void Begin(FrameworkElement containingObject, bool isControllable)
    {
        ArgumentNullException.ThrowIfNull(containingObject);
        BeginCore(containingObject, null, HandoffBehavior.SnapshotAndReplace, isControllable);
    }

    public void Begin(FrameworkElement containingObject, HandoffBehavior handoffBehavior)
    {
        ArgumentNullException.ThrowIfNull(containingObject);
        BeginCore(containingObject, null, handoffBehavior, isControllable: false);
    }

    public void Begin(FrameworkElement containingObject, HandoffBehavior handoffBehavior, bool isControllable)
    {
        ArgumentNullException.ThrowIfNull(containingObject);
        BeginCore(containingObject, null, handoffBehavior, isControllable);
    }

    public void Begin(FrameworkContentElement containingObject)
    {
        ArgumentNullException.ThrowIfNull(containingObject);
        BeginCore(containingObject, null, HandoffBehavior.SnapshotAndReplace, isControllable: false);
    }

    public void Begin(FrameworkContentElement containingObject, bool isControllable)
    {
        ArgumentNullException.ThrowIfNull(containingObject);
        BeginCore(containingObject, null, HandoffBehavior.SnapshotAndReplace, isControllable);
    }

    public void Begin(FrameworkContentElement containingObject, HandoffBehavior handoffBehavior)
    {
        ArgumentNullException.ThrowIfNull(containingObject);
        BeginCore(containingObject, null, handoffBehavior, isControllable: false);
    }

    public void Begin(
        FrameworkContentElement containingObject,
        HandoffBehavior handoffBehavior,
        bool isControllable)
    {
        ArgumentNullException.ThrowIfNull(containingObject);
        BeginCore(containingObject, null, handoffBehavior, isControllable);
    }

    public void Begin(FrameworkElement containingObject, FrameworkTemplate frameworkTemplate)
    {
        ArgumentNullException.ThrowIfNull(containingObject);
        ArgumentNullException.ThrowIfNull(frameworkTemplate);
        BeginCore(containingObject, frameworkTemplate, HandoffBehavior.SnapshotAndReplace, isControllable: false);
    }

    public void Begin(FrameworkElement containingObject, FrameworkTemplate frameworkTemplate, bool isControllable)
    {
        ArgumentNullException.ThrowIfNull(containingObject);
        ArgumentNullException.ThrowIfNull(frameworkTemplate);
        BeginCore(containingObject, frameworkTemplate, HandoffBehavior.SnapshotAndReplace, isControllable);
    }

    public void Begin(FrameworkElement containingObject, FrameworkTemplate frameworkTemplate, HandoffBehavior handoffBehavior)
    {
        ArgumentNullException.ThrowIfNull(containingObject);
        ArgumentNullException.ThrowIfNull(frameworkTemplate);
        BeginCore(containingObject, frameworkTemplate, handoffBehavior, isControllable: false);
    }

    public void Begin(
        FrameworkElement containingObject,
        FrameworkTemplate frameworkTemplate,
        HandoffBehavior handoffBehavior,
        bool isControllable)
    {
        ArgumentNullException.ThrowIfNull(containingObject);
        ArgumentNullException.ThrowIfNull(frameworkTemplate);
        BeginCore(containingObject, frameworkTemplate, handoffBehavior, isControllable);
    }

    private void BeginCore(
        DependencyObject? containingObject,
        FrameworkTemplate? frameworkTemplate,
        HandoffBehavior handoffBehavior,
        bool isControllable)
    {
        ValidateHandoffBehavior(handoffBehavior);
        StopCore(remove: false);

        _containingObject = containingObject;
        _frameworkTemplate = frameworkTemplate;
        _isControllable = isControllable;

        DependencyObject? storyboardTarget = GetTarget(this);
        string? storyboardTargetName = GetTargetName(this);
        PropertyPath? storyboardTargetProperty = GetTargetProperty(this);
        var resolved = new List<(IAnimationTimeline Animation, DependencyObject Target, DependencyProperty Property, IAnimationClock Clock)>();
        _rootClock = (ClockGroup)AllocateClock();
        _rootClock.SetHasControllableRoot(isControllable);
        _rootClock.Completed += OnRootClockCompleted;

        foreach (AnimationBinding binding in EnumerateAnimationBindings(
                     _rootClock,
                     storyboardTarget,
                     storyboardTargetName,
                     storyboardTargetProperty))
        {
            DependencyObject? target = ResolveTarget(binding.Target, binding.TargetName, containingObject, frameworkTemplate);
            if (target is null)
            {
                throw new InvalidOperationException(
                    string.IsNullOrEmpty(binding.TargetName)
                        ? "A storyboard animation requires a target object or containing object."
                        : $"The storyboard target name '{binding.TargetName}' could not be resolved.");
            }

            if (binding.TargetProperty is null)
            {
                throw new InvalidOperationException("A storyboard animation requires a TargetProperty.");
            }

            DependencyProperty? property = ResolveProperty(ref target, binding.TargetProperty);
            if (property is null)
            {
                throw new InvalidOperationException(
                    $"The storyboard property path '{binding.TargetProperty.Path}' could not be resolved on '{target.GetType().Name}'.");
            }

            target.VerifyAccess();
            if (target.IsSealed || property.ReadOnly ||
                property.GetMetadata(target.GetType()) is UIPropertyMetadata { IsAnimationProhibited: true })
            {
                throw new InvalidOperationException($"The storyboard target property '{property.Name}' cannot be animated.");
            }
            Type propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (!propertyType.IsAssignableFrom(binding.Animation.TargetPropertyType))
            {
                throw new ArgumentException($"The animation type does not match '{property.Name}'.");
            }
            resolved.Add((binding.Animation, target, property, binding.Clock));
        }

        try
        {
            _rootClock.ControllerBegin();
            foreach (var (animation, target, property, clock) in resolved)
            {
                _clocks.Add(clock);
                clock.Completed += OnClockCompleted;

                bool elementDriven = false;
                if (target is UIElement uiElement)
                {
                    elementDriven = uiElement.BeginStoryboardAnimation(
                        property,
                        animation,
                        clock,
                        _runtimeAdapter,
                        handoffBehavior);
                }
                else if (target is Animatable animatable && clock is AnimationClock animationClock)
                {
                    elementDriven = animatable.BeginStoryboardAnimation(property, animationClock, _runtimeAdapter, handoffBehavior);
                }

                if (!elementDriven)
                {
                    ClaimRawAnimation(target, property, clock, animation, handoffBehavior);
                    if (clock is not AnimationClock { HasStarted: true }) clock.Begin();
                }

                _activeAnimations.Add((clock, target, property, elementDriven));
            }
        }
        catch
        {
            StopCore(remove: false);
            throw;
        }

        if (!_rootClock.HasFutureTiming)
        {
            return;
        }

        _isRunning = true;
        RegisterTickSubscription();

        lock (_lock)
        {
            _activeStoryboards.Add(this);
        }

        OnCurrentStateInvalidated();
        OnCurrentTimeInvalidated();
        OnCurrentGlobalSpeedInvalidated();
    }

    public void Stop() => StopCore(remove: false);

    public void Stop(FrameworkElement containingObject)
    {
        ArgumentNullException.ThrowIfNull(containingObject);
        if (CanControl(containingObject))
        {
            StopCore(remove: false);
        }
    }

    public void Stop(FrameworkContentElement containingObject)
    {
        ArgumentNullException.ThrowIfNull(containingObject);
        if (CanControl(containingObject))
        {
            StopCore(remove: false);
        }
    }

    public void Remove() => StopCore(remove: true);

    public void Remove(FrameworkElement containingObject)
    {
        ArgumentNullException.ThrowIfNull(containingObject);
        if (CanControl(containingObject))
        {
            StopCore(remove: true);
        }
    }

    public void Remove(FrameworkContentElement containingObject)
    {
        ArgumentNullException.ThrowIfNull(containingObject);
        if (CanControl(containingObject))
        {
            StopCore(remove: true);
        }
    }

    private void StopCore(bool remove)
    {
        bool hadClocks = _clocks.Count > 0;
        _isRunning = false;
        UnregisterTickSubscription();
        if (_rootClock is { } rootClock)
        {
            rootClock.Completed -= OnRootClockCompleted;
            rootClock.ControllerStop();
            _rootClock = null;
        }
        _rootCompletionPending = false;

        lock (_lock)
        {
            _activeStoryboards.Remove(this);
        }

        var entries = _activeAnimations.ToArray();
        foreach (var (clock, target, property, elementDriven) in entries)
        {
            if (remove && clock is AnimationClock animationClock)
            {
                animationClock.ControllerRemove();
            }

            if (elementDriven && target is UIElement uiElement)
            {
                // The element owns a separate animated layer. Removing its clock
                // reveals the current base value, which may have changed since Begin.
                // Writing the captured value here would replace newer local/bound
                // values and turn CSS/style fallback values into native local values.
                uiElement.StopStoryboardAnimation(property, clock);
            }
            else if (elementDriven && target is Animatable animatable)
            {
                animatable.StopStoryboardAnimation(property, clock);
                clock.Stop();
            }
            else
            {
                if (!remove)
                {
                    clock.Stop();
                }
                RemoveRawAnimation(target, property, clock);
            }

            clock.Completed -= OnClockCompleted;
        }

        _clocks.Clear();
        _activeAnimations.Clear();
        _settledClocks.Clear();
        _terminatedClocks.Clear();
        _terminatedCount = 0;
        _containingObject = null;
        _frameworkTemplate = null;
        _isControllable = false;

        if (hadClocks)
        {
            OnCurrentTimeInvalidated();
            OnCurrentStateInvalidated();
            OnCurrentGlobalSpeedInvalidated();
        }
        if (remove && hadClocks)
        {
            OnRemoveRequested();
        }
    }

    public void Pause() => PauseCore();

    public void Pause(FrameworkElement containingObject)
    {
        ArgumentNullException.ThrowIfNull(containingObject);
        if (CanControl(containingObject))
        {
            PauseCore();
        }
    }

    public void Pause(FrameworkContentElement containingObject)
    {
        ArgumentNullException.ThrowIfNull(containingObject);
        if (CanControl(containingObject))
        {
            PauseCore();
        }
    }

    private void PauseCore()
    {
        if (_rootClock != null) _rootClock.ControllerPause();
        else foreach (IAnimationClock clock in _clocks)
        {
            clock.Pause();
        }
        if (_rootClock?.IsPaused == true) UnregisterTickSubscription();
        OnCurrentGlobalSpeedInvalidated();
    }

    public void Resume()
    {
        ResumeCore();
    }

    public void Resume(FrameworkElement containingObject)
    {
        ArgumentNullException.ThrowIfNull(containingObject);
        if (CanControl(containingObject))
        {
            ResumeCore();
        }
    }

    public void Resume(FrameworkContentElement containingObject)
    {
        ArgumentNullException.ThrowIfNull(containingObject);
        if (CanControl(containingObject))
        {
            ResumeCore();
        }
    }

    private void ResumeCore()
    {
        if (_rootClock != null) _rootClock.ControllerResume();
        else foreach (IAnimationClock clock in _clocks)
        {
            clock.Resume();
        }
        EnsureFrameSourcesAfterClockRestart();
        OnCurrentGlobalSpeedInvalidated();
    }

    public void Seek(TimeSpan offset) => SeekCore(offset, TimeSeekOrigin.BeginTime);

    public void Seek(TimeSpan offset, TimeSeekOrigin origin) => SeekCore(offset, origin);

    public void Seek(FrameworkElement containingObject, TimeSpan offset, TimeSeekOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(containingObject);
        if (CanControl(containingObject))
        {
            SeekCore(offset, origin);
        }
    }

    public void Seek(FrameworkContentElement containingObject, TimeSpan offset, TimeSeekOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(containingObject);
        if (CanControl(containingObject))
        {
            SeekCore(offset, origin);
        }
    }

    public void SeekAlignedToLastTick(TimeSpan offset) =>
        SeekAlignedToLastTick(offset, TimeSeekOrigin.BeginTime);

    public void SeekAlignedToLastTick(TimeSpan offset, TimeSeekOrigin origin) => SeekCore(offset, origin, alignedToLastTick: true);

    public void SeekAlignedToLastTick(FrameworkElement containingObject, TimeSpan offset, TimeSeekOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(containingObject);
        if (CanControl(containingObject))
        {
            SeekCore(offset, origin, alignedToLastTick: true);
        }
    }

    public void SeekAlignedToLastTick(
        FrameworkContentElement containingObject,
        TimeSpan offset,
        TimeSeekOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(containingObject);
        if (CanControl(containingObject))
        {
            SeekCore(offset, origin, alignedToLastTick: true);
        }
    }

    private void SeekCore(TimeSpan offset, TimeSeekOrigin origin, bool alignedToLastTick = false)
    {
        if (!Enum.IsDefined(origin))
        {
            throw new ArgumentOutOfRangeException(nameof(origin));
        }

        if (_rootClock != null)
        {
            bool sampleImmediately = alignedToLastTick || _rootClock.IsPaused;
            _settledClocks.Clear();
            _settledClocks.UnionWith(_terminatedClocks);
            _terminatedCount = _terminatedClocks.Count;
            _rootCompletionPending = false;
            _isRunning = true;
            lock (_lock) _activeStoryboards.Add(this);
            _rootClock.ControllerSeek(offset, origin, alignedToLastTick);
            EnsureFrameSourcesAfterClockRestart();
            if (sampleImmediately)
                OnAnimationFrame(AnimationManager.CurrentFrameTimestampOrNow);
            OnCurrentTimeInvalidated();
            OnCurrentStateInvalidated();
            OnCurrentGlobalSpeedInvalidated();
            return;
        }

        foreach (IAnimationClock clock in _clocks.ToArray())
        {
            if (clock is AnimationClock animationClock)
            {
                animationClock.ControllerSeek(offset, origin, alignedToLastTick);
                continue;
            }
            clock.Seek(
                offset,
                origin == TimeSeekOrigin.Duration
                    ? AnimationSeekOrigin.Duration
                    : AnimationSeekOrigin.BeginTime);
        }
        EnsureFrameSourcesAfterClockRestart();
        OnCurrentTimeInvalidated();
        OnCurrentStateInvalidated();
        OnCurrentGlobalSpeedInvalidated();
    }

    public void SetSpeedRatio(double speedRatio) => SetSpeedRatioCore(speedRatio);

    public void SetSpeedRatio(FrameworkElement containingObject, double speedRatio)
    {
        ArgumentNullException.ThrowIfNull(containingObject);
        if (CanControl(containingObject))
        {
            SetSpeedRatioCore(speedRatio);
        }
    }

    public void SetSpeedRatio(FrameworkContentElement containingObject, double speedRatio)
    {
        ArgumentNullException.ThrowIfNull(containingObject);
        if (CanControl(containingObject))
        {
            SetSpeedRatioCore(speedRatio);
        }
    }

    private void SetSpeedRatioCore(double speedRatio)
    {
        if (!double.IsFinite(speedRatio) || speedRatio < 0d)
        {
            throw new ArgumentException("Speed ratio must be finite and non-negative.", nameof(speedRatio));
        }

        if (_rootClock != null) _rootClock.SetInteractiveSpeedRatio(speedRatio);
        else foreach (AnimationClock clock in _clocks.OfType<AnimationClock>())
        {
            clock.SetInteractiveSpeedRatio(speedRatio);
        }
        EnsureFrameSourcesAfterClockRestart();
        OnCurrentGlobalSpeedInvalidated();
    }

    public void SkipToFill() => SkipToFillCore();

    public void SkipToFill(FrameworkElement containingObject)
    {
        ArgumentNullException.ThrowIfNull(containingObject);
        if (CanControl(containingObject))
        {
            SkipToFillCore();
        }
    }

    public void SkipToFill(FrameworkContentElement containingObject)
    {
        ArgumentNullException.ThrowIfNull(containingObject);
        if (CanControl(containingObject))
        {
            SkipToFillCore();
        }
    }

    private void SkipToFillCore()
    {
        if (_rootClock != null)
        {
            _rootClock.ControllerSkipToFill();
            OnAnimationFrame(AnimationManager.CurrentFrameTimestampOrNow);
        }
        else foreach (AnimationClock clock in _clocks.OfType<AnimationClock>().ToArray())
        {
            clock.ControllerSkipToFill();
        }
        OnCurrentTimeInvalidated();
        OnCurrentStateInvalidated();
        OnCurrentGlobalSpeedInvalidated();
    }

    public ClockState GetCurrentState() => FirstClock()?.CurrentState ?? ClockState.Stopped;
    public ClockState GetCurrentState(FrameworkElement containingObject)
    {
        ArgumentNullException.ThrowIfNull(containingObject);
        return IsCurrentOwner(containingObject) ? GetCurrentState() : ClockState.Stopped;
    }
    public ClockState GetCurrentState(FrameworkContentElement containingObject)
    {
        ArgumentNullException.ThrowIfNull(containingObject);
        return IsCurrentOwner(containingObject) ? GetCurrentState() : ClockState.Stopped;
    }

    public double GetCurrentProgress() => FirstClock()?.CurrentProgress ?? 0d;
    public double? GetCurrentProgress(FrameworkElement containingObject)
    {
        ArgumentNullException.ThrowIfNull(containingObject);
        return IsCurrentOwner(containingObject) ? FirstClock()?.CurrentProgress : null;
    }
    public double? GetCurrentProgress(FrameworkContentElement containingObject)
    {
        ArgumentNullException.ThrowIfNull(containingObject);
        return IsCurrentOwner(containingObject) ? FirstClock()?.CurrentProgress : null;
    }

    public TimeSpan GetCurrentTime() => FirstClock()?.CurrentTime ?? TimeSpan.Zero;
    public TimeSpan? GetCurrentTime(FrameworkElement containingObject)
    {
        ArgumentNullException.ThrowIfNull(containingObject);
        return IsCurrentOwner(containingObject) ? FirstClock()?.CurrentTime : null;
    }
    public TimeSpan? GetCurrentTime(FrameworkContentElement containingObject)
    {
        ArgumentNullException.ThrowIfNull(containingObject);
        return IsCurrentOwner(containingObject) ? FirstClock()?.CurrentTime : null;
    }

    public int GetCurrentIteration() => FirstClock()?.CurrentIteration ?? 0;
    public int? GetCurrentIteration(FrameworkElement containingObject)
    {
        ArgumentNullException.ThrowIfNull(containingObject);
        return IsCurrentOwner(containingObject) ? FirstClock()?.CurrentIteration : null;
    }
    public int? GetCurrentIteration(FrameworkContentElement containingObject)
    {
        ArgumentNullException.ThrowIfNull(containingObject);
        return IsCurrentOwner(containingObject) ? FirstClock()?.CurrentIteration : null;
    }

    public double GetCurrentGlobalSpeed() => FirstClock()?.CurrentGlobalSpeed ?? 0d;
    public double? GetCurrentGlobalSpeed(FrameworkElement containingObject)
    {
        ArgumentNullException.ThrowIfNull(containingObject);
        return IsCurrentOwner(containingObject) ? FirstClock()?.CurrentGlobalSpeed : null;
    }
    public double? GetCurrentGlobalSpeed(FrameworkContentElement containingObject)
    {
        ArgumentNullException.ThrowIfNull(containingObject);
        return IsCurrentOwner(containingObject) ? FirstClock()?.CurrentGlobalSpeed : null;
    }

    public bool GetIsPaused() => FirstClock()?.IsPaused ?? false;
    public bool GetIsPaused(FrameworkElement containingObject)
    {
        ArgumentNullException.ThrowIfNull(containingObject);
        return IsCurrentOwner(containingObject) && GetIsPaused();
    }

    public bool GetIsPaused(FrameworkContentElement containingObject)
    {
        ArgumentNullException.ThrowIfNull(containingObject);
        return IsCurrentOwner(containingObject) && GetIsPaused();
    }

    private Clock? FirstClock() => (Clock?)_rootClock ?? _clocks.OfType<AnimationClock>().FirstOrDefault();

    private bool CanControl(DependencyObject containingObject) =>
        _isControllable && IsCurrentOwner(containingObject);

    private bool IsCurrentOwner(DependencyObject containingObject) =>
        ReferenceEquals(_containingObject, containingObject) && _clocks.Count > 0;

    private void EnsureFrameSourcesAfterClockRestart()
    {
        if (_rootClock is { HasFutureTiming: true, IsPaused: false, AppliedSpeedRatio: > 0d }) RegisterTickSubscription();
        bool nonElementPending = false;
        foreach (var (clock, target, property, elementDriven) in _activeAnimations)
        {
            if (elementDriven)
            {
                if (target is UIElement uiElement)
                {
                    if (!uiElement.OwnsStoryboardAnimation(property, clock) && !_terminatedClocks.Contains(clock) &&
                        clock.Timeline is { } animation)
                        uiElement.BeginStoryboardAnimation(property, animation, clock, _runtimeAdapter);
                    uiElement.EnsureAnimationFrameSource();
                }
                else if (target is Animatable animatable)
                {
                    if (!animatable.OwnsStoryboardAnimation(property, clock) && !_terminatedClocks.Contains(clock) &&
                        clock is AnimationClock animationClock)
                        animatable.BeginStoryboardAnimation(property, animationClock, _runtimeAdapter, HandoffBehavior.SnapshotAndReplace);
                    animatable.EnsureAnimationFrameSource();
                }
            }
            else if (!clock.IsCompleted)
            {
                nonElementPending = true;
            }
        }

        if (nonElementPending)
        {
            RegisterTickSubscription();
        }
    }

    private bool HasPendingNonElementWork() =>
        _activeAnimations.Any(static entry => !entry.ElementDriven && !entry.Clock.IsCompleted);

    private void RegisterTickSubscription()
    {
        _tickSubscription ??= new AnimationTickSubscription(_runtimeAdapter, weak: false);
        AnimationManager.Register(_tickSubscription);
    }

    private void UnregisterTickSubscription()
    {
        if (_tickSubscription is not null)
        {
            AnimationManager.Unregister(_tickSubscription);
        }
    }

    private bool OnAnimationFrame(long frameTimestamp)
    {
        if (!_isRunning)
        {
            return false;
        }

        _rootClock?.TickChildren(frameTimestamp);
        bool anyActive = false;
        for (int i = 0; i < _activeAnimations.Count; i++)
        {
            var (clock, target, property, elementDriven) = _activeAnimations[i];
            if (elementDriven)
            {
                continue;
            }

            UpdateRawAnimation(target, property, frameTimestamp);
            anyActive |= !clock.IsCompleted && !clock.IsPaused;
        }

        if (_rootCompletionPending)
        {
            _rootCompletionPending = false;
            foreach (var clock in _clocks.ToArray()) SettleClock(clock, terminated: false);
            if (_clocks.Count == 0)
            {
                _isRunning = false;
                lock (_lock) _activeStoryboards.Remove(this);
                AnimationManager.QueuePostFrame(OnCompleted);
            }
        }
        return _isRunning && (_rootClock is { } root
            ? root.HasFutureTiming && !root.IsPaused && root.AppliedSpeedRatio > 0d : anyActive);
    }

    private void OnRootClockCompleted(object? sender, EventArgs e) => _rootCompletionPending = true;

    private void OnClockCompleted(object? sender, EventArgs e)
    {
        if (sender is not IAnimationClock clock)
        {
            return;
        }

        for (int i = 0; i < _activeAnimations.Count; i++)
        {
            var (entryClock, target, property, elementDriven) = _activeAnimations[i];
            if (!ReferenceEquals(entryClock, clock) || elementDriven)
            {
                continue;
            }

            UpdateRawAnimation(target, property);
            break;
        }

        if (_rootClock == null) SettleClock(clock, terminated: false);
    }

    private void NotifyClockTerminated(IAnimationClock clock) =>
        SettleClock(clock, terminated: true);

    private void NotifyClockSuperseded(IAnimationClock clock) =>
        SettleClock(clock, terminated: false);

    private void ClaimRawAnimation(DependencyObject target, DependencyProperty property, IAnimationClock clock,
        IAnimationTimeline animation, HandoffBehavior handoffBehavior)
    {
        var properties = _rawAnimations.GetOrCreateValue(target);
        properties.TryGetValue(property, out var previous);
        RawAnimationEntry[] replaced = Array.Empty<RawAnimationEntry>();
        var state = previous;
        if (previous != null && handoffBehavior == HandoffBehavior.SnapshotAndReplace)
        {
            state = new RawAnimationState { Snapshot = target.GetValue(property), HasSnapshot = true };
            replaced = previous.Entries.ToArray();
        }
        state ??= new RawAnimationState();
        state.Entries.Add(new RawAnimationEntry(this, clock, animation));
        properties[property] = state;
        foreach (var entry in replaced)
        {
            if (ReferenceEquals(entry.Owner, this)) entry.Owner.NotifyClockSuperseded(entry.Clock);
            else entry.Owner.NotifyClockTerminated(entry.Clock);
            entry.Clock.Stop();
        }
    }

    private static void RemoveRawAnimation(DependencyObject target, DependencyProperty property, IAnimationClock clock)
    {
        if (!_rawAnimations.TryGetValue(target, out var properties) || !properties.TryGetValue(property, out var state)) return;
        int index = state.Entries.FindIndex(entry => ReferenceEquals(entry.Clock, clock));
        if (index < 0) return;
        state.Entries.RemoveAt(index);
        if (state.Entries.Count == 0)
        {
            properties.Remove(property);
            target.DiscardAnimatedValue(property);
        }
        else UpdateRawAnimation(target, property);
    }

    private static void UpdateRawAnimation(DependencyObject target, DependencyProperty property, long? frameTimestamp = null)
    {
        if (!_rawAnimations.TryGetValue(target, out var properties) || !properties.TryGetValue(property, out var state) || state.Updating) return;
        var pool = System.Buffers.ArrayPool<RawAnimationEntry>.Shared;
        var entries = pool.Rent(state.Entries.Count);
        int count = state.Entries.Count;
        state.Entries.CopyTo(entries);
        state.Updating = true;
        try
        {
            if (frameTimestamp.HasValue)
            {
                for (int i = 0; i < count; i++)
                    if (state.Entries.Contains(entries[i])) entries[i].Clock.Tick(frameTimestamp.Value);
            }
            if (!properties.TryGetValue(property, out var current) || !ReferenceEquals(current, state)) return;
            object? baseValue = target.GetAnimationBaseValue(property);
            bool snapshotVisible = state.HasSnapshot && (state.Entries.Count == 0 ||
                (state.Entries[0].Clock is AnimationClock animationClock
                    ? !animationClock.IsStoppedAfterCompletion : !state.Entries[0].Clock.IsCompleted));
            object? value = snapshotVisible ? state.Snapshot : baseValue;
            bool applies = snapshotVisible;
            for (int i = 0; i < count; i++)
            {
                var entry = entries[i];
                if (!state.Entries.Contains(entry) || entry.Clock is Clock { CurrentState: ClockState.Stopped }) continue;
                value = entry.Animation.GetCurrentValue(value!, baseValue!, entry.Clock);
                applies = true;
            }
            if (applies) target.SetAnimatedValue(property, value, holdEndValue: false);
            else target.DiscardAnimatedValue(property);
        }
        finally
        {
            state.Updating = false;
            Array.Clear(entries, 0, count);
            pool.Return(entries);
        }
    }

    private sealed class RuntimeAdapter : IFrameAnimatable, IStoryboardClockOwner
    {
        private readonly Storyboard _owner;

        internal RuntimeAdapter(Storyboard owner)
        {
            _owner = owner;
        }

        bool IFrameAnimatable.OnAnimationFrame(long frameTimestamp) =>
            _owner.OnAnimationFrame(frameTimestamp);

        void IStoryboardClockOwner.NotifyClockTerminated(IAnimationClock clock) =>
            _owner.NotifyClockTerminated(clock);

        void IStoryboardClockOwner.NotifyClockSuperseded(IAnimationClock clock) =>
            _owner.NotifyClockSuperseded(clock);
    }

    private void SettleClock(IAnimationClock clock, bool terminated)
    {
        if (!_settledClocks.Add(clock))
        {
            return;
        }

        if (terminated)
        {
            _terminatedClocks.Add(clock);
            _terminatedCount++;
        }

        if (_settledClocks.Count < _clocks.Count)
        {
            return;
        }

        bool wasRunning = _isRunning;
        _isRunning = false;
        UnregisterTickSubscription();
        lock (_lock)
        {
            _activeStoryboards.Remove(this);
        }

        if (wasRunning && _terminatedCount == 0)
        {
            AnimationManager.QueuePostFrame(OnCompleted);
        }
    }

    private static IEnumerable<AnimationBinding> EnumerateAnimationBindings(
        ClockGroup clocks,
        DependencyObject? inheritedTarget,
        string? inheritedTargetName,
        PropertyPath? inheritedTargetProperty)
    {
        foreach (Clock childClock in clocks.ChildClocks)
        {
            Timeline timeline = childClock.Timeline;
            DependencyObject? target = GetTarget(timeline) ?? inheritedTarget;
            string? targetName = GetTargetName(timeline);
            if (string.IsNullOrEmpty(targetName))
            {
                targetName = inheritedTargetName;
            }
            PropertyPath? targetProperty = GetTargetProperty(timeline) ?? inheritedTargetProperty;

            if (timeline is IAnimationTimeline animation && childClock is IAnimationClock animationClock)
            {
                yield return new AnimationBinding(animation, animationClock, target, targetName, targetProperty);
            }

            if (childClock is ClockGroup group)
            {
                foreach (AnimationBinding descendant in EnumerateAnimationBindings(
                             group,
                             target,
                             targetName,
                             targetProperty))
                {
                    yield return descendant;
                }
            }
        }
    }

    private static DependencyObject? ResolveTarget(
        DependencyObject? directTarget,
        string? targetName,
        DependencyObject? containingObject,
        FrameworkTemplate? frameworkTemplate)
    {
        if (directTarget is not null)
        {
            return directTarget;
        }

        if (!string.IsNullOrEmpty(targetName) && containingObject is FrameworkElement element)
        {
            if (frameworkTemplate?.FindName(targetName, element) is DependencyObject templateTarget)
            {
                return templateTarget;
            }

            if (element.FindName(targetName) is DependencyObject scopedTarget)
            {
                return scopedTarget;
            }

            return FindElementByName(element, targetName);
        }

        if (!string.IsNullOrEmpty(targetName) && containingObject is FrameworkContentElement contentElement)
        {
            return contentElement.FindName(targetName) as DependencyObject;
        }

        return containingObject;
    }

    private static FrameworkElement? FindElementByName(FrameworkElement root, string name)
    {
        if (root.Name == name)
        {
            return root;
        }

        for (int i = 0; i < root.VisualChildrenCount; i++)
        {
            if (root.GetVisualChild(i) is FrameworkElement child)
            {
                FrameworkElement? found = FindElementByName(child, name);
                if (found is not null)
                {
                    return found;
                }
            }
        }
        return null;
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Dependency-property segments use the registry; CLR intermediate segments use source-generated property accessors, with reflection retained as the normal binding fallback.")]
    private static DependencyProperty? ResolveProperty(ref DependencyObject target, PropertyPath propertyPath)
    {
        var segments = propertyPath.CachedParsedSegments;
        object? current = target;
        for (int i = 0; i < segments.Length; i++)
        {
            string segment = segments[i];
            bool indexed = PropertyPath.TryGetIndexedSegment(segment, out string propertyName, out string index);
            string name = indexed ? propertyName : segment;
            DependencyProperty? property = null;
            if (current is DependencyObject dependencyObject)
            {
                if (name.StartsWith('(') && name.EndsWith(')'))
                {
                    name = name[1..^1];
                    if (int.TryParse(name, out int parameter) && parameter >= 0 && parameter < propertyPath.PathParameters.Count)
                        property = propertyPath.PathParameters[parameter] as DependencyProperty;
                    else
                    {
                        int dot = name.LastIndexOf('.');
                        Type? owner = dot >= 0 ? TypeResolver.ResolveTypeByName?.Invoke(name[..dot]) : null;
                        property = DependencyProperty.FromName(owner ?? dependencyObject.GetType(), dot >= 0 ? name[(dot + 1)..] : name);
                    }
                }
                else if (name.Length > 0)
                    property = DependencyProperty.FromName(dependencyObject.GetType(), name);

                if (i == segments.Length - 1)
                {
                    if (indexed) return null;
                    target = dependencyObject;
                    return property;
                }
            }

            if (property != null && current is DependencyObject ownerObject)
            {
                current = ownerObject.GetValue(property);
                if (current is Freezable { IsFrozen: true } frozen)
                {
                    ownerObject.SetCurrentValue(property, frozen.CloneCurrentValue());
                    current = ownerObject.GetValue(property);
                }
            }
            else if (name.Length > 0)
            {
                if (current == null || !PropertyAccessorRegistry.TryReadProperty(current, name, out current)) return null;
            }

            if (indexed)
            {
                if (current is System.Collections.IList list && int.TryParse(index, out int item) && item >= 0 && item < list.Count)
                {
                    current = list[item];
                    if (current is Freezable { IsFrozen: true } frozen)
                    {
                        current = frozen.CloneCurrentValue();
                        list[item] = current;
                    }
                }
                else if (current is System.Collections.IDictionary map && map.Contains(index))
                    current = map[index];
                else return null;
            }
        }
        return null;
    }

    private static void ValidateHandoffBehavior(HandoffBehavior handoffBehavior)
    {
        if (!Enum.IsDefined(handoffBehavior))
        {
            throw new ArgumentOutOfRangeException(nameof(handoffBehavior));
        }
    }

    private readonly record struct AnimationBinding(
        IAnimationTimeline Animation,
        IAnimationClock Clock,
        DependencyObject? Target,
        string? TargetName,
        PropertyPath? TargetProperty);
}

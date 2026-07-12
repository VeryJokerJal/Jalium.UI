using Jalium.UI.Animation;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// A parallel timeline that supplies target information for its descendant animations.
/// </summary>
public partial class Storyboard : ParallelTimeline
{
    private static readonly HashSet<Storyboard> _activeStoryboards = [];
    private static readonly object _lock = new();

    private readonly List<IAnimationClock> _clocks = [];
    private readonly List<(IAnimationClock Clock, DependencyObject Target, DependencyProperty Property, object? OriginalValue, bool ElementDriven)> _activeAnimations = [];
    private readonly HashSet<IAnimationClock> _settledClocks = new(ReferenceEqualityComparer.Instance);
    private readonly RuntimeAdapter _runtimeAdapter;
    private int _terminatedCount;
    private bool _isRunning;
    private bool _isControllable;
    private DependencyObject? _containingObject;
    private FrameworkTemplate? _frameworkTemplate;
    private AnimationTickSubscription? _tickSubscription;

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

        foreach (AnimationBinding binding in EnumerateAnimationBindings(
                     Children,
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

            DependencyProperty? property = ResolveProperty(target, binding.TargetProperty);
            if (property is null)
            {
                throw new InvalidOperationException(
                    $"The storyboard property path '{binding.TargetProperty.Path}' could not be resolved on '{target.GetType().Name}'.");
            }

            IAnimationClock clock = binding.Animation.CreateClock();
            object? originalValue = target.GetAnimationBaseValue(property);
            _clocks.Add(clock);
            clock.Completed += OnClockCompleted;

            bool elementDriven = false;
            if (target is UIElement uiElement)
            {
                elementDriven = uiElement.BeginStoryboardAnimation(
                    property,
                    binding.Animation,
                    clock,
                    _runtimeAdapter,
                    handoffBehavior);
            }

            if (!elementDriven)
            {
                clock.Begin();
            }

            _activeAnimations.Add((clock, target, property, originalValue, elementDriven));
        }

        if (_clocks.Count == 0)
        {
            return;
        }

        _isRunning = true;
        if (HasPendingNonElementWork())
        {
            RegisterTickSubscription();
        }

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

        lock (_lock)
        {
            _activeStoryboards.Remove(this);
        }

        var entries = _activeAnimations.ToArray();
        foreach (var (clock, target, property, originalValue, elementDriven) in entries)
        {
            if (remove && clock is AnimationClock animationClock)
            {
                animationClock.ControllerRemove();
            }

            if (elementDriven && target is UIElement uiElement)
            {
                uiElement.StopStoryboardAnimation(property, clock);
                if (remove)
                {
                    target.SetValue(property, originalValue ?? property.DefaultMetadata.DefaultValue);
                }
            }
            else
            {
                if (!remove)
                {
                    clock.Stop();
                }
                target.SetValue(property, originalValue ?? property.DefaultMetadata.DefaultValue);
            }

            clock.Completed -= OnClockCompleted;
        }

        _clocks.Clear();
        _activeAnimations.Clear();
        _settledClocks.Clear();
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
        foreach (IAnimationClock clock in _clocks)
        {
            clock.Pause();
        }
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
        foreach (IAnimationClock clock in _clocks)
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

    public void SeekAlignedToLastTick(TimeSpan offset, TimeSeekOrigin origin) => SeekCore(offset, origin);

    public void SeekAlignedToLastTick(FrameworkElement containingObject, TimeSpan offset, TimeSeekOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(containingObject);
        if (CanControl(containingObject))
        {
            SeekCore(offset, origin);
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
            SeekCore(offset, origin);
        }
    }

    private void SeekCore(TimeSpan offset, TimeSeekOrigin origin)
    {
        if (!Enum.IsDefined(origin))
        {
            throw new ArgumentOutOfRangeException(nameof(origin));
        }

        foreach (IAnimationClock clock in _clocks)
        {
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

        foreach (AnimationClock clock in _clocks.OfType<AnimationClock>())
        {
            clock.SetInteractiveSpeedRatio(speedRatio);
        }
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
        foreach (AnimationClock clock in _clocks.OfType<AnimationClock>().ToArray())
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

    private AnimationClock? FirstClock() => _clocks.OfType<AnimationClock>().FirstOrDefault();

    private bool CanControl(DependencyObject containingObject) =>
        _isControllable && IsCurrentOwner(containingObject);

    private bool IsCurrentOwner(DependencyObject containingObject) =>
        ReferenceEquals(_containingObject, containingObject) && _clocks.Count > 0;

    private void EnsureFrameSourcesAfterClockRestart()
    {
        bool nonElementPending = false;
        foreach (var (clock, target, _, _, elementDriven) in _activeAnimations)
        {
            if (elementDriven)
            {
                if (target is UIElement uiElement)
                {
                    uiElement.EnsureAnimationFrameSource();
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

        bool anyActive = false;
        for (int i = 0; i < _activeAnimations.Count; i++)
        {
            var (clock, target, property, originalValue, elementDriven) = _activeAnimations[i];
            if (elementDriven || clock.IsCompleted || clock.IsPaused)
            {
                continue;
            }

            clock.Tick(frameTimestamp);
            if (clock.IsCompleted)
            {
                continue;
            }

            if (clock.Timeline is IAnimationTimeline animation)
            {
                object origin = originalValue ?? GetDefaultValue(property);
                target.SetValue(property, animation.GetCurrentValue(origin, origin, clock));
            }
            anyActive = true;
        }

        return anyActive && _isRunning;
    }

    private void OnClockCompleted(object? sender, EventArgs e)
    {
        if (sender is not IAnimationClock clock)
        {
            return;
        }

        for (int i = 0; i < _activeAnimations.Count; i++)
        {
            var (entryClock, target, property, originalValue, elementDriven) = _activeAnimations[i];
            if (!ReferenceEquals(entryClock, clock) || elementDriven)
            {
                continue;
            }

            if (clock.Timeline is IAnimationTimeline animation)
            {
                object origin = originalValue ?? GetDefaultValue(property);
                target.SetValue(
                    property,
                    animation.AnimationFillBehavior == AnimationFillBehavior.Stop
                        ? originalValue ?? property.DefaultMetadata.DefaultValue
                        : animation.GetCurrentValue(origin, origin, clock));
            }
            break;
        }

        SettleClock(clock, terminated: false);
    }

    private void NotifyClockTerminated(IAnimationClock clock) =>
        SettleClock(clock, terminated: true);

    private void NotifyClockSuperseded(IAnimationClock clock) =>
        SettleClock(clock, terminated: false);

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
        IEnumerable<Timeline> timelines,
        DependencyObject? inheritedTarget,
        string? inheritedTargetName,
        PropertyPath? inheritedTargetProperty)
    {
        foreach (Timeline timeline in timelines)
        {
            DependencyObject? target = GetTarget(timeline) ?? inheritedTarget;
            string? targetName = GetTargetName(timeline);
            if (string.IsNullOrEmpty(targetName))
            {
                targetName = inheritedTargetName;
            }
            PropertyPath? targetProperty = GetTargetProperty(timeline) ?? inheritedTargetProperty;

            if (timeline is IAnimationTimeline animation)
            {
                yield return new AnimationBinding(animation, target, targetName, targetProperty);
            }

            if (timeline is TimelineGroup group)
            {
                foreach (AnimationBinding descendant in EnumerateAnimationBindings(
                             group.Children,
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

    private static DependencyProperty? ResolveProperty(DependencyObject target, PropertyPath propertyPath)
    {
        if (propertyPath.PathParameters.Count > 0 && propertyPath.PathParameters[0] is DependencyProperty directProperty)
        {
            return directProperty;
        }

        string propertyName = propertyPath.Path;
        if (propertyName.StartsWith('(') && propertyName.EndsWith(')'))
        {
            propertyName = propertyName[1..^1];
            int dot = propertyName.LastIndexOf('.');
            if (dot >= 0)
            {
                propertyName = propertyName[(dot + 1)..];
            }
        }
        return DependencyProperty.FromName(target.GetType(), propertyName);
    }

    private static object GetDefaultValue(DependencyProperty property) =>
        property.DefaultMetadata.DefaultValue ?? 0d;

    private static void ValidateHandoffBehavior(HandoffBehavior handoffBehavior)
    {
        if (!Enum.IsDefined(handoffBehavior))
        {
            throw new ArgumentOutOfRangeException(nameof(handoffBehavior));
        }
    }

    private readonly record struct AnimationBinding(
        IAnimationTimeline Animation,
        DependencyObject? Target,
        string? TargetName,
        PropertyPath? TargetProperty);
}

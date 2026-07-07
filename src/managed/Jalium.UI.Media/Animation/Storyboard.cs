using System.Collections.ObjectModel;
using Jalium.UI;
using Jalium.UI.Animation;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// A container timeline that provides object and property targeting information for its child animations.
/// </summary>
public sealed class Storyboard : Timeline, IStoryboard, IFrameAnimatable, IStoryboardClockOwner
{
    private static readonly HashSet<Storyboard> _activeStoryboards = new();
    private static readonly object _lock = new();

    private readonly List<AnimationClock> _clocks = new();
    private readonly List<(AnimationClock Clock, DependencyObject Target, DependencyProperty Property, object? OriginalValue, bool ElementDriven)> _activeAnimations = new();
    // Per-clock settlement bookkeeping: a clock settles either by completing
    // naturally (Completed event) or by being terminated from the element side
    // (NotifyClockTerminated). Only when every clock has settled may the
    // storyboard leave the static active set — and OnCompleted fires only when
    // none of them settled by termination (WPF: Stop does not fire Completed).
    private readonly HashSet<AnimationClock> _settledClocks = new();
    private int _terminatedCount;
    private bool _isRunning;
    // Strong-reference registration with the central AnimationManager, used only
    // while at least one non-UIElement target needs per-frame SetValue fallback.
    // Strong: a fire-and-forget Begin() must keep ticking after the local
    // variable goes out of scope.
    private AnimationTickSubscription? _tickSubscription;

    /// <summary>
    /// Stops all active storyboards. Called during application shutdown.
    /// </summary>
    public static void StopAll()
    {
        Storyboard[] storyboards;
        lock (_lock)
        {
            storyboards = _activeStoryboards.ToArray();
        }

        foreach (var storyboard in storyboards)
        {
            storyboard.Stop();
        }
    }

    #region Attached Properties

    /// <summary>
    /// Identifies the TargetName attached property.
    /// </summary>
    public static readonly DependencyProperty TargetNameProperty =
        DependencyProperty.RegisterAttached("TargetName", typeof(string), typeof(Storyboard),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the Target attached property.
    /// </summary>
    public static readonly DependencyProperty TargetProperty =
        DependencyProperty.RegisterAttached("Target", typeof(DependencyObject), typeof(Storyboard),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the TargetProperty attached property.
    /// </summary>
    public static readonly DependencyProperty TargetPropertyProperty =
        DependencyProperty.RegisterAttached("TargetProperty", typeof(PropertyPath), typeof(Storyboard),
            new PropertyMetadata(null));

    /// <summary>
    /// Gets the target name.
    /// </summary>
    public static string? GetTargetName(DependencyObject element)
    {
        return (string?)element.GetValue(TargetNameProperty);
    }

    /// <summary>
    /// Sets the target name.
    /// </summary>
    public static void SetTargetName(DependencyObject element, string? value)
    {
        element.SetValue(TargetNameProperty, value);
    }

    /// <summary>
    /// Gets the target object.
    /// </summary>
    public static DependencyObject? GetTarget(DependencyObject element)
    {
        return (DependencyObject?)element.GetValue(TargetProperty);
    }

    /// <summary>
    /// Sets the target object.
    /// </summary>
    public static void SetTarget(DependencyObject element, DependencyObject? value)
    {
        element.SetValue(TargetProperty, value);
    }

    /// <summary>
    /// Gets the target property.
    /// </summary>
    public static PropertyPath? GetTargetProperty(DependencyObject element)
    {
        return (PropertyPath?)element.GetValue(TargetPropertyProperty);
    }

    /// <summary>
    /// Sets the target property.
    /// </summary>
    public static void SetTargetProperty(DependencyObject element, PropertyPath? value)
    {
        element.SetValue(TargetPropertyProperty, value);
    }

    #endregion

    /// <summary>
    /// Gets the collection of children timelines.
    /// </summary>
    public Collection<Timeline> Children { get; } = new();

    /// <summary>
    /// Applies animations to their targets and begins the storyboard.
    /// </summary>
    public void Begin()
    {
        Begin(null, false);
    }

    /// <summary>
    /// Applies animations to their targets and begins the storyboard.
    /// </summary>
    /// <param name="containingObject">The object that contains the named elements to animate.</param>
    public void Begin(FrameworkElement? containingObject)
    {
        Begin(containingObject, false);
    }

    /// <summary>
    /// Applies animations to their targets and begins the storyboard.
    /// </summary>
    /// <param name="containingObject">The object that contains the named elements to animate.</param>
    /// <param name="isControllable">Whether the storyboard can be controlled.</param>
    public void Begin(FrameworkElement? containingObject, bool isControllable)
    {
        Stop();

        foreach (var child in Children)
        {
            if (child is AnimationTimeline animationTimeline)
            {
                var target = ResolveTarget(child, containingObject);
                var propertyPath = GetTargetProperty(child);

                if (target != null && propertyPath != null)
                {
                    var property = ResolveProperty(target, propertyPath);
                    if (property != null)
                    {
                        var clock = new AnimationClock(animationTimeline);
                        var originalValue = target.GetValue(property);
                        _clocks.Add(clock);
                        // Subscribed before the element's own Completed handler:
                        // natural completion settles here first, then the element
                        // applies FillBehavior (whose termination callback is a
                        // no-op for an already-settled clock).
                        clock.Completed += OnClockCompleted;

                        var elementDriven = false;
                        if (target is UIElement uiElement)
                        {
                            // Element-driven path: tick/value-write/FillBehavior
                            // cleanup all run through the element's unified
                            // animation entry; the clock is started by
                            // BeginAnimationCore.
                            elementDriven = uiElement.BeginStoryboardAnimation(
                                property, animationTimeline, clock, this);
                        }

                        if (!elementDriven)
                        {
                            clock.Begin();
                        }

                        _activeAnimations.Add((clock, target, property, originalValue, elementDriven));
                    }
                }
            }
        }

        if (_clocks.Count > 0)
        {
            _isRunning = true;

            // Only non-UIElement targets need the storyboard itself as a frame
            // driver (SetValue fallback); element-driven entries tick inside
            // their element.
            if (HasPendingNonElementWork())
            {
                RegisterTickSubscription();
            }

            lock (_lock)
            {
                _activeStoryboards.Add(this);
            }
        }
    }

    /// <summary>
    /// Stops the storyboard.
    /// </summary>
    public void Stop()
    {
        _isRunning = false;
        UnregisterTickSubscription();

        lock (_lock)
        {
            _activeStoryboards.Remove(this);
        }

        // Snapshot: StopStoryboardAnimation/SetValue raise property-changed
        // callbacks that run user code, which may re-enter this storyboard
        // (Begin/Stop) and mutate the live list mid-iteration.
        var entries = _activeAnimations.ToArray();
        foreach (var (clock, target, property, originalValue, elementDriven) in entries)
        {
            if (elementDriven && target is UIElement uiElement)
            {
                // Stops the element entry only while that exact clock still owns
                // the property — an animation that has since replaced it is left
                // alone. ClearAnimatedValue inside handles HoldEnd promotion.
                uiElement.StopStoryboardAnimation(property, clock);
            }
            else
            {
                clock.Stop();
                // Fallback for non-UIElement targets: restore original value on Stop()
                target.SetValue(property, originalValue ?? property.DefaultMetadata.DefaultValue);
            }
        }

        _clocks.Clear();
        _activeAnimations.Clear();
        _settledClocks.Clear();
        _terminatedCount = 0;
    }

    /// <summary>
    /// Pauses the storyboard.
    /// </summary>
    public void Pause()
    {
        foreach (var clock in _clocks)
        {
            clock.Pause();
        }
    }

    /// <summary>
    /// Resumes a paused storyboard.
    /// </summary>
    public void Resume()
    {
        foreach (var clock in _clocks)
        {
            clock.Resume();
        }

        EnsureFrameSourcesAfterClockRestart();
    }

    /// <summary>
    /// Seeks every clock to the specified position (measured from its begin time).
    /// </summary>
    public void Seek(TimeSpan offset)
    {
        foreach (var clock in _clocks)
        {
            clock.Seek(offset, TimeSeekOrigin.BeginTime);
        }

        EnsureFrameSourcesAfterClockRestart();
    }

    /// <summary>
    /// Re-arms frame sources after clocks were revived (Resume/Seek): an
    /// all-paused element or storyboard returns false from OnAnimationFrame and
    /// drops off the manager, so revival needs an explicit re-register.
    /// </summary>
    private void EnsureFrameSourcesAfterClockRestart()
    {
        var nonElementPending = false;

        foreach (var (clock, target, _, _, elementDriven) in _activeAnimations)
        {
            if (elementDriven)
            {
                if (target is UIElement uiElement)
                {
                    uiElement.EnsureAnimationFrameSource();
                }
            }
            else if (!((IAnimationClock)clock).IsCompleted)
            {
                nonElementPending = true;
            }
        }

        if (nonElementPending)
        {
            RegisterTickSubscription();
        }
    }

    private bool HasPendingNonElementWork()
    {
        foreach (var (clock, _, _, _, elementDriven) in _activeAnimations)
        {
            if (!elementDriven && !((IAnimationClock)clock).IsCompleted)
            {
                return true;
            }
        }

        return false;
    }

    private void RegisterTickSubscription()
    {
        _tickSubscription ??= new AnimationTickSubscription(this, weak: false);
        AnimationManager.Register(_tickSubscription);
    }

    private void UnregisterTickSubscription()
    {
        if (_tickSubscription != null)
        {
            AnimationManager.Unregister(_tickSubscription);
        }
    }

    /// <summary>
    /// Ticks the non-element-driven entries once per frame (element-driven
    /// entries are ticked by their own element). Returns false when no such
    /// entry can still make progress, unregistering from the manager.
    /// </summary>
    bool IFrameAnimatable.OnAnimationFrame(long frameTimestamp)
    {
        if (!_isRunning) return false;

        var anyActive = false;

        for (int i = 0; i < _activeAnimations.Count; i++)
        {
            var (clock, target, property, originalValue, elementDriven) = _activeAnimations[i];
            if (elementDriven)
            {
                continue;
            }

            IAnimationClock clockState = clock;
            if (clockState.IsCompleted || clockState.IsPaused)
            {
                continue;
            }

            // May complete naturally here → OnClockCompleted settles it and
            // applies FillBehavior, so a completed clock skips the value write.
            clock.Tick(frameTimestamp);

            if (clockState.IsCompleted)
            {
                continue;
            }

            if (clock.Timeline is AnimationTimeline animationTimeline)
            {
                var origin = originalValue ?? GetDefaultValue(property);
                target.SetValue(property, animationTimeline.GetCurrentValue(origin, origin, clock));
            }

            anyActive = true;
        }

        return anyActive && _isRunning;
    }

    private void OnClockCompleted(object? sender, EventArgs e)
    {
        if (sender is not AnimationClock clock)
        {
            return;
        }

        // FillBehavior epilogue for non-element entries (element-driven entries
        // are handled by the element's own Completed handler).
        for (int i = 0; i < _activeAnimations.Count; i++)
        {
            var (entryClock, target, property, originalValue, elementDriven) = _activeAnimations[i];
            if (!ReferenceEquals(entryClock, clock) || elementDriven)
            {
                continue;
            }

            if (clock.Timeline is AnimationTimeline animationTimeline)
            {
                var origin = originalValue ?? GetDefaultValue(property);
                if (animationTimeline.FillBehavior == FillBehavior.Stop)
                {
                    target.SetValue(property, originalValue ?? property.DefaultMetadata.DefaultValue);
                }
                else
                {
                    // HoldEnd: write the final frame's value (the tick loop skips
                    // completed clocks, so it would otherwise never land).
                    target.SetValue(property, animationTimeline.GetCurrentValue(origin, origin, clock));
                }
            }

            break;
        }

        SettleClock(clock, terminated: false);
    }

    /// <summary>
    /// Element-side termination bookkeeping (detach stop, container recycling,
    /// handoff replacement, StopStoryboardAnimation): the clock will never fire
    /// Completed, so it settles here instead.
    /// </summary>
    void IStoryboardClockOwner.NotifyClockTerminated(IAnimationClock clock)
    {
        if (clock is AnimationClock animationClock)
        {
            SettleClock(animationClock, terminated: true);
        }
    }

    /// <summary>
    /// A sibling child animation of this storyboard took over the same
    /// (target, property): the superseded clock settles like a natural
    /// completion — last-writer-wins is the storyboard playing as authored,
    /// so it must not suppress <see cref="Timeline.Completed"/>.
    /// </summary>
    void IStoryboardClockOwner.NotifyClockSuperseded(IAnimationClock clock)
    {
        if (clock is AnimationClock animationClock)
        {
            SettleClock(animationClock, terminated: false);
        }
    }

    private void SettleClock(AnimationClock clock, bool terminated)
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

        // Every clock has settled: release the static root and the frame source.
        // (During Stop() _isRunning is already false — the cleanup below is
        // idempotent and OnCompleted correctly stays silent.)
        var wasRunning = _isRunning;
        _isRunning = false;
        UnregisterTickSubscription();

        lock (_lock)
        {
            _activeStoryboards.Remove(this);
        }

        if (wasRunning && _terminatedCount == 0)
        {
            // WPF semantics: Completed fires only when every clock finished
            // naturally — never after a Stop/termination.
            //
            // Deferred to the end of the frame: an element-driven clock settles from
            // inside AnimationClock.Tick, which the element runs BEFORE it writes this
            // frame's final/FillBehavior value (UIElement.OnAnimationFrame ticks, then
            // writes). Raising Completed synchronously here would let a handler read the
            // pre-final value or start a follow-up animation off a stale reading. Queued
            // post-frame it runs after every value write has landed; outside a frame it
            // runs immediately (preserving the synchronous semantics the tests assert).
            AnimationManager.QueuePostFrame(OnCompleted);
        }
    }

    private DependencyObject? ResolveTarget(Timeline timeline, FrameworkElement? containingObject)
    {
        // First check for direct target
        var directTarget = GetTarget(timeline);
        if (directTarget != null)
        {
            return directTarget;
        }

        // Then check for target name
        var targetName = GetTargetName(timeline);
        if (!string.IsNullOrEmpty(targetName) && containingObject != null)
        {
            return FindElementByName(containingObject, targetName);
        }

        // Default to containing object
        return containingObject;
    }

    private static FrameworkElement? FindElementByName(FrameworkElement root, string name)
    {
        // Check the root element
        if (root.Name == name)
        {
            return root;
        }

        // Search children recursively
        for (int i = 0; i < root.VisualChildrenCount; i++)
        {
            var child = root.GetVisualChild(i);
            if (child is FrameworkElement fe)
            {
                var found = FindElementByName(fe, name);
                if (found != null)
                {
                    return found;
                }
            }
        }

        return null;
    }

    private DependencyProperty? ResolveProperty(DependencyObject target, PropertyPath propertyPath)
    {
        // Simple property resolution - just look for the property name
        var propertyName = propertyPath.Path;

        // Handle nested property paths like "(UIElement.Opacity)"
        if (propertyName.StartsWith("(", StringComparison.Ordinal) && propertyName.EndsWith(")", StringComparison.Ordinal))
        {
            propertyName = propertyName.Substring(1, propertyName.Length - 2);
            var dotIndex = propertyName.LastIndexOf('.');
            if (dotIndex >= 0)
            {
                propertyName = propertyName.Substring(dotIndex + 1);
            }
        }

        // AOT-safe DependencyProperty lookup via the registry (no reflection).
        return DependencyProperty.FromName(target.GetType(), propertyName);
    }

    private static object GetDefaultValue(DependencyProperty property)
    {
        return property.DefaultMetadata.DefaultValue ?? 0.0;
    }
}

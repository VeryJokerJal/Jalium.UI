using Jalium.UI;
using Jalium.UI.Animation;
using Jalium.UI.Threading;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Abstract class that provides animation support.
/// This is the base class for objects that can have animated dependency properties.
/// </summary>
public abstract class Animatable : Freezable, IAnimatable, IFrameAnimatable
{
    // Geometry, brushes, and path segments are Animatable but almost never have a clock attached.
    // Avoid paying for a dictionary on every short-lived render object.
    private AnimationStorage? _animationClocks;
    private AnimationTickSubscription? _animationTickSubscription;

    private sealed class AnimationStorage : Dictionary<DependencyProperty, PropertyAnimation>
    {
        internal uint ChangeVersion;
    }

    private sealed class PropertyAnimation
    {
        internal readonly List<AnimationEntry> Entries = new();
        internal object? Snapshot;
        internal bool HasSnapshot;
    }

    private sealed class AnimationEntry
    {
        private readonly Animatable _target;
        internal readonly DependencyProperty Property;
        internal readonly AnimationClock Clock;
        internal readonly IStoryboardClockOwner? StoryboardOwner;

        internal AnimationEntry(Animatable target, DependencyProperty property, AnimationClock clock, IStoryboardClockOwner? owner)
        {
            _target = target;
            Property = property;
            Clock = clock;
            StoryboardOwner = owner;
        }

        internal void Attach()
        {
            Clock.CurrentTimeInvalidated += OnInvalidated;
            Clock.CurrentStateInvalidated += OnInvalidated;
            Clock.CurrentGlobalSpeedInvalidated += OnInvalidated;
            Clock.RemoveRequested += OnRemoveRequested;
        }

        internal void Detach()
        {
            Clock.CurrentTimeInvalidated -= OnInvalidated;
            Clock.CurrentStateInvalidated -= OnInvalidated;
            Clock.CurrentGlobalSpeedInvalidated -= OnInvalidated;
            Clock.RemoveRequested -= OnRemoveRequested;
        }

        private void OnInvalidated(object? sender, EventArgs e)
        {
            if (!_target.Owns(this)) return;
            _target.UpdateAnimatedProperty(Property);
            _target.EnsureAnimationFrameSource();
        }

        private void OnRemoveRequested(object? sender, EventArgs e) => _target.RemoveEntry(this);
    }

    /// <summary>Initializes an Animatable on the current dispatcher.</summary>
    protected Animatable()
    {
    }

    /// <summary>Initializes an Animatable on an already selected dispatcher.</summary>
    internal Animatable(Dispatcher dispatcher)
        : base(dispatcher)
    {
    }

    /// <summary>
    /// Gets a value indicating whether one or more AnimationClock objects are associated
    /// with any of this object's dependency properties.
    /// </summary>
    public bool HasAnimatedProperties => _animationClocks?.Count > 0;

    /// <summary>Creates a modifiable clone with the concrete Animatable return type.</summary>
    public new Animatable Clone() => (Animatable)base.Clone();

    /// <summary>Prevents the implementation-only stored weak reference from being serialized.</summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static bool ShouldSerializeStoredWeakReference(DependencyObject target) => false;

    /// <summary>
    /// Applies an AnimationClock to the specified DependencyProperty, replacing existing animations.
    /// </summary>
    public void ApplyAnimationClock(DependencyProperty dp, AnimationClock? clock)
    {
        ApplyAnimationClock(dp, clock, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>
    /// Applies an AnimationClock to the specified DependencyProperty with the specified HandoffBehavior.
    /// </summary>
    public void ApplyAnimationClock(DependencyProperty dp, AnimationClock? clock, HandoffBehavior handoffBehavior)
    {
        ValidateAnimation(dp, clock?.Timeline, handoffBehavior);
        if (clock == null)
        {
            if (handoffBehavior != HandoffBehavior.Compose) RemoveAnimationClock(dp);
            return;
        }

        ApplyAnimationClockCore(dp, clock, handoffBehavior, null);
    }

    /// <summary>
    /// Starts an animation for the specified DependencyProperty.
    /// </summary>
    public void BeginAnimation(DependencyProperty dp, AnimationTimeline? animation)
    {
        BeginAnimation(dp, animation, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>
    /// Starts an animation for the specified DependencyProperty with the specified HandoffBehavior.
    /// </summary>
    public void BeginAnimation(DependencyProperty dp, AnimationTimeline? animation, HandoffBehavior handoffBehavior)
    {
        ValidateAnimation(dp, animation, handoffBehavior);
        if (animation == null)
        {
            if (handoffBehavior != HandoffBehavior.Compose) RemoveAnimationClock(dp);
            return;
        }

        if (!animation.BeginTime.HasValue)
        {
            if (handoffBehavior == HandoffBehavior.Compose) return;
            object? snapshot = GetValue(dp);
            RemoveAnimationClock(dp);
            (_animationClocks ??= new())[dp] = new PropertyAnimation { Snapshot = snapshot, HasSnapshot = true };
            WriteAnimatedProperty(dp, snapshot, hasValue: true);
            return;
        }

        ApplyAnimationClockCore(dp, animation.CreateClock(), handoffBehavior, null);
    }

    private void RemoveAnimationClock(DependencyProperty dp)
    {
        PropertyAnimation? removed = null;
        var clocks = _animationClocks;
        clocks?.Remove(dp, out removed);

        if (removed != null)
        {
            foreach (var entry in removed.Entries) entry.Detach();
        }
        WriteAnimatedProperty(dp, null, hasValue: false);
        if (clocks is { Count: 0 } && ReferenceEquals(_animationClocks, clocks))
            _animationClocks = null;
        if (removed != null)
        {
            foreach (var entry in removed.Entries) entry.StoryboardOwner?.NotifyClockTerminated(entry.Clock);
        }
        EnsureAnimationFrameSource();
    }

    private void ValidateAnimation(DependencyProperty dp, AnimationTimeline? animation, HandoffBehavior handoffBehavior)
    {
        ArgumentNullException.ThrowIfNull(dp);
        WritePreamble();
        if (!Enum.IsDefined(handoffBehavior)) throw new ArgumentOutOfRangeException(nameof(handoffBehavior));
        if (dp.ReadOnly) throw new ArgumentException("A read-only property cannot be animated.", nameof(dp));
        if (dp.GetMetadata(GetType()) is UIPropertyMetadata { IsAnimationProhibited: true })
            throw new ArgumentException("Animation is prohibited by this property's metadata.", nameof(dp));
        Type propertyType = Nullable.GetUnderlyingType(dp.PropertyType) ?? dp.PropertyType;
        if (animation != null && !propertyType.IsAssignableFrom(animation.TargetPropertyType))
            throw new ArgumentException($"{animation.GetType().Name} cannot animate {dp.Name}.", nameof(animation));
    }

    private void ApplyAnimationClockCore(DependencyProperty dp, AnimationClock clock, HandoffBehavior handoffBehavior, IStoryboardClockOwner? owner)
    {
        PropertyAnimation? state = null;
        _animationClocks?.TryGetValue(dp, out state);
        if (state != null && handoffBehavior == HandoffBehavior.SnapshotAndReplace)
        {
            object? snapshot = GetValue(dp);
            if (owner != null)
            {
                foreach (var entry in state.Entries)
                    if (ReferenceEquals(entry.StoryboardOwner, owner)) owner.NotifyClockSuperseded(entry.Clock);
            }
            RemoveAnimationClock(dp);
            state = new PropertyAnimation { Snapshot = snapshot, HasSnapshot = true };
        }
        state ??= new PropertyAnimation();
        (_animationClocks ??= new())[dp] = state;
        var added = new AnimationEntry(this, dp, clock, owner);
        state.Entries.Add(added);
        added.Attach();
        if (!clock.HasStarted) clock.Begin();
        UpdateAnimatedProperty(dp);
        EnsureAnimationFrameSource();
    }

    internal bool BeginStoryboardAnimation(DependencyProperty dp, AnimationClock clock, IStoryboardClockOwner owner, HandoffBehavior handoffBehavior)
    {
        ValidateAnimation(dp, clock.Timeline, handoffBehavior);
        ApplyAnimationClockCore(dp, clock, handoffBehavior, owner);
        return true;
    }

    internal void StopStoryboardAnimation(DependencyProperty dp, IAnimationClock clock)
    {
        if (_animationClocks is not { } clocks || !clocks.TryGetValue(dp, out var state)) return;
        foreach (var entry in state.Entries.ToArray())
            if (ReferenceEquals(entry.Clock, clock)) RemoveEntry(entry);
    }

    internal bool OwnsStoryboardAnimation(DependencyProperty dp, IAnimationClock clock)
    {
        if (_animationClocks is not { } clocks || !clocks.TryGetValue(dp, out var state)) return false;
        foreach (var entry in state.Entries) if (ReferenceEquals(entry.Clock, clock)) return true;
        return false;
    }

    private bool Owns(AnimationEntry entry) =>
        _animationClocks is { } clocks && clocks.TryGetValue(entry.Property, out var state) && state.Entries.Contains(entry);

    private void RemoveEntry(AnimationEntry entry)
    {
        if (_animationClocks is not { } clocks || !clocks.TryGetValue(entry.Property, out var state) || !state.Entries.Remove(entry)) return;
        entry.Detach();
        if (state.Entries.Count == 0) RemoveAnimationClock(entry.Property);
        else UpdateAnimatedProperty(entry.Property);
        entry.StoryboardOwner?.NotifyClockTerminated(entry.Clock);
        EnsureAnimationFrameSource();
    }

    private void UpdateAnimatedProperty(DependencyProperty dp)
    {
        if (_animationClocks is not { } clocks || !clocks.TryGetValue(dp, out var state)) return;
        object? baseValue = base.GetAnimationBaseValue(dp);
        bool snapshotVisible = state.HasSnapshot && (state.Entries.Count == 0 ||
            !state.Entries[0].Clock.IsStoppedAfterCompletion);
        object? value = snapshotVisible ? state.Snapshot : baseValue;
        bool hasValue = snapshotVisible;
        foreach (var entry in state.Entries)
        {
            if (entry.Clock.CurrentState == ClockState.Stopped) continue;
            value = entry.Clock.GetCurrentValue(value!, baseValue!);
            hasValue = true;
        }
        WriteAnimatedProperty(dp, value, hasValue);
    }

    private void WriteAnimatedProperty(DependencyProperty dp, object? value, bool hasValue)
    {
        var storage = _animationClocks;
        uint previousVersion = storage?.ChangeVersion ?? 0;
        bool changed;
        if (hasValue)
            changed = SetAnimatedValue(dp, value, holdEndValue: false);
        else
        {
            object? previous = GetValue(dp);
            DiscardAnimatedValue(dp);
            changed = !Equals(previous, GetValue(dp));
        }

        // Built-in brushes/transforms already report Changed from their property
        // callbacks. Supply the same notification for other Animatable subclasses,
        // without duplicating the built-in notifications or allocating per frame.
        if (changed && ReferenceEquals(storage, _animationClocks) && (storage?.ChangeVersion ?? 0) == previousVersion)
            WritePostscript();
    }

    protected override void OnChanged()
    {
        if (_animationClocks is { } storage) unchecked { storage.ChangeVersion++; }
        base.OnChanged();
    }

    internal void EnsureAnimationFrameSource()
    {
        if (HasRunningClocks())
        {
            _animationTickSubscription ??= new AnimationTickSubscription(this, weak: true);
            AnimationManager.Register(_animationTickSubscription);
        }
        else if (_animationTickSubscription != null)
        {
            AnimationManager.Unregister(_animationTickSubscription);
        }
    }

    private bool HasRunningClocks()
    {
        if (_animationClocks == null) return false;
        foreach (var state in _animationClocks.Values)
            foreach (var entry in state.Entries)
                if (!((IAnimationClock)entry.Clock).IsCompleted && !entry.Clock.IsPaused &&
                    !entry.Clock.IsTimeProgressBlocked) return true;
        return false;
    }

    bool IFrameAnimatable.OnAnimationFrame(long frameTimestamp)
    {
        if (_animationClocks == null) return false;
        int count = 0;
        foreach (var state in _animationClocks.Values) count += state.Entries.Count;
        var pool = System.Buffers.ArrayPool<AnimationEntry>.Shared;
        var entries = pool.Rent(count);
        int copied = 0;
        foreach (var state in _animationClocks.Values)
            foreach (var entry in state.Entries) entries[copied++] = entry;
        try
        {
            for (int i = 0; i < copied; i++)
                if (Owns(entries[i])) entries[i].Clock.Tick(frameTimestamp);
        }
        finally
        {
            Array.Clear(entries, 0, copied);
            pool.Return(entries);
        }
        return HasRunningClocks();
    }

    /// <summary>
    /// Retrieves the base value of the specified DependencyProperty, disregarding any animated value.
    /// </summary>
    public new object? GetAnimationBaseValue(DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(dp);
        return base.GetAnimationBaseValue(dp);
    }

    /// <inheritdoc/>
    protected override bool FreezeCore(bool isChecking)
    {
        if (HasAnimatedProperties)
            return false;

        return base.FreezeCore(isChecking);
    }
}

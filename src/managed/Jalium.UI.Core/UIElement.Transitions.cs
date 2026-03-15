using System.Collections.Specialized;
using Jalium.UI.Media.Animation;

namespace Jalium.UI;

public abstract partial class UIElement
{
    private const string TransitionAllValue = "All";
    private const string TransitionNoneValue = "None";
    private const string DefaultTransitionPropertyValue = TransitionNoneValue;
    private static readonly TimeSpan s_defaultTransitionDuration = TimeSpan.FromMilliseconds(180);

    private Dictionary<string, bool>? _transitionPropertyLookup;
    private string? _transitionPropertyLookupSource;
    private TransitionPropertyCollection? _transitionPropertyCollectionSubscription;
    private bool _transitionAllProperties;
    private bool _transitionNoProperties;
    private int _transitionArmVersion;
    private bool _automaticTransitionsArmed;

    internal static Func<DependencyProperty, object?, object?, TimeSpan, TransitionTimingFunction, IAnimationTimeline?>? AutomaticTransitionAnimationFactory { get; set; }

    /// <summary>
    /// Identifies the TransitionProperty dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty TransitionPropertyProperty =
        DependencyProperty.Register(nameof(TransitionProperty), typeof(TransitionPropertyCollection), typeof(UIElement),
            new PropertyMetadata(DefaultTransitionPropertyValue, OnTransitionConfigurationChanged));

    /// <summary>
    /// Identifies the TransitionDuration dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty TransitionDurationProperty =
        DependencyProperty.Register(nameof(TransitionDuration), typeof(Duration), typeof(UIElement),
            new PropertyMetadata(new Duration(s_defaultTransitionDuration), OnTransitionConfigurationChanged));

    /// <summary>
    /// Identifies the TransitionTimingFunction dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty TransitionTimingFunctionProperty =
        DependencyProperty.Register(nameof(TransitionTimingFunction), typeof(TransitionTimingFunction), typeof(UIElement),
            new PropertyMetadata(TransitionTimingFunction.Recommended, OnTransitionConfigurationChanged));

    /// <summary>
    /// Gets or sets the collection of properties that should participate in automatic transitions.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public TransitionPropertyCollection TransitionProperty
    {
        get => TransitionPropertyCollection.FromRawValue(GetValue(TransitionPropertyProperty));
        set => SetValue(TransitionPropertyProperty, value ?? TransitionPropertyCollection.None());
    }

    /// <summary>
    /// Gets or sets the duration used by automatic property transitions.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public Duration TransitionDuration
    {
        get => GetValue(TransitionDurationProperty) is Duration duration
            ? duration
            : new Duration(s_defaultTransitionDuration);
        set => SetValue(TransitionDurationProperty, value);
    }

    /// <summary>
    /// Gets or sets the timing function used by automatic property transitions.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public TransitionTimingFunction TransitionTimingFunction
    {
        get => GetValue(TransitionTimingFunctionProperty) is TransitionTimingFunction timingFunction
            ? timingFunction
            : TransitionTimingFunction.Recommended;
        set => SetValue(TransitionTimingFunctionProperty, value);
    }

    /// <summary>
    /// Allows derived controls to suppress automatic transitions for specific properties.
    /// </summary>
    /// <param name="dp">The property being mutated.</param>
    /// <returns><see langword="true"/> to bypass automatic transition handling for the property.</returns>
    protected virtual bool ShouldSuppressAutomaticTransition(DependencyProperty dp)
    {
        return false;
    }

    protected override void OnVisualParentChanged(Visual? oldParent)
    {
        base.OnVisualParentChanged(oldParent);

        if (VisualParent != null)
        {
            ScheduleAutomaticTransitionArmRecursive(this);
        }
        else if (oldParent != null)
        {
            DisarmAutomaticTransitionsRecursive(this);
        }
    }

    internal bool ShouldAutomaticallyTransition(DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(dp);

        if (!_automaticTransitionsArmed)
            return false;

        if (GetAutomaticTransitionAnimationFactory() == null)
            return false;

        if (ReferenceEquals(dp, TransitionPropertyProperty) ||
            ReferenceEquals(dp, TransitionDurationProperty) ||
            ReferenceEquals(dp, TransitionTimingFunctionProperty))
        {
            return false;
        }

        if (ShouldSuppressAutomaticTransition(dp))
            return false;

        var duration = GetTransitionDurationOrDefault();
        if (duration <= TimeSpan.Zero)
            return false;

        EnsureTransitionPropertyLookup();
        if (_transitionNoProperties)
            return false;

        return _transitionAllProperties ||
               (_transitionPropertyLookup?.ContainsKey(dp.Name) == true);
    }

    internal bool TryStartAutomaticTransition(DependencyProperty dp, object? fromValue, object? toValue)
    {
        ArgumentNullException.ThrowIfNull(dp);

        var duration = GetTransitionDurationOrDefault();
        if (duration <= TimeSpan.Zero)
            return false;

        var animationFactory = GetAutomaticTransitionAnimationFactory();
        if (animationFactory == null)
            return false;

        var animation = animationFactory(
            dp,
            fromValue,
            toValue,
            duration,
            TransitionTimingFunction);

        if (animation == null)
            return false;

        return BeginAnimationCore(
            dp,
            animation,
            HandoffBehavior.SnapshotAndReplace,
            ElementAnimationKind.AutomaticTransition,
            clearAnimatedValueOnReplace: false,
            allowAutomaticToReplaceExplicit: false,
            initialAnimatedValue: fromValue,
            useInitialAnimatedValue: true,
            deferClockBeginUntilRendering: true);
    }

    internal void StopAutomaticTransition(DependencyProperty dp, bool clearAnimatedValue)
    {
        StopAnimationCore(dp, ElementAnimationKind.AutomaticTransition, clearAnimatedValue);
    }

    internal bool HasExplicitAnimation(DependencyProperty dp)
    {
        return TryGetActiveAnimation(dp, out var animation) &&
               animation.Kind == ElementAnimationKind.Explicit;
    }

    internal bool HasAutomaticTransition(DependencyProperty dp)
    {
        return TryGetActiveAnimation(dp, out var animation) &&
               animation.Kind == ElementAnimationKind.AutomaticTransition;
    }

    private static void OnTransitionConfigurationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element)
        {
            if (ReferenceEquals(e.Property, TransitionPropertyProperty))
            {
                element.UpdateTransitionPropertyCollectionSubscription(e.OldValue, e.NewValue);
                element.InvalidateTransitionPropertyLookup();
            }
        }
    }

    private TimeSpan GetTransitionDurationOrDefault()
    {
        var duration = TransitionDuration;
        if (!duration.HasTimeSpan)
            return TimeSpan.Zero;

        return duration.TimeSpan;
    }

    private void EnsureTransitionPropertyLookup()
    {
        var raw = GetValue(TransitionPropertyProperty);
        var cacheKey = TransitionPropertyCollection.GetCacheKey(raw);
        if (cacheKey == _transitionPropertyLookupSource)
            return;

        _transitionPropertyLookupSource = cacheKey;
        _transitionPropertyLookup = null;
        _transitionAllProperties = false;
        _transitionNoProperties = false;

        if (raw is TransitionPropertyCollection collection)
        {
            ApplyTransitionPropertyCollectionLookup(collection);
            return;
        }

        var rawText = raw as string;
        if (string.IsNullOrWhiteSpace(rawText) ||
            string.Equals(rawText, TransitionNoneValue, StringComparison.OrdinalIgnoreCase))
        {
            _transitionNoProperties = true;
            return;
        }

        if (string.Equals(rawText, TransitionAllValue, StringComparison.OrdinalIgnoreCase))
        {
            _transitionAllProperties = true;
            return;
        }

        var lookup = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in rawText.Split(','))
        {
            var trimmed = TransitionPropertyCollection.NormalizeName(name);
            if (trimmed == null)
                continue;

            lookup[trimmed] = true;
        }

        _transitionPropertyLookup = lookup;
        _transitionNoProperties = lookup.Count == 0;
    }

    private void InvalidateTransitionPropertyLookup()
    {
        _transitionPropertyLookupSource = null;
        _transitionPropertyLookup = null;
        _transitionAllProperties = false;
        _transitionNoProperties = false;
    }

    private void ApplyTransitionPropertyCollectionLookup(TransitionPropertyCollection collection)
    {
        if (collection.IsNone)
        {
            _transitionNoProperties = true;
            return;
        }

        if (collection.IsAll)
        {
            _transitionAllProperties = true;
            return;
        }

        var lookup = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var propertyName in collection)
        {
            var normalized = TransitionPropertyCollection.NormalizeName(propertyName);
            if (normalized == null)
                continue;

            lookup[normalized] = true;
        }

        _transitionPropertyLookup = lookup;
        _transitionNoProperties = lookup.Count == 0;
    }

    private void UpdateTransitionPropertyCollectionSubscription(object? oldValue, object? newValue)
    {
        if (_transitionPropertyCollectionSubscription != null &&
            ReferenceEquals(oldValue, _transitionPropertyCollectionSubscription))
        {
            _transitionPropertyCollectionSubscription.CollectionChanged -= OnTransitionPropertyCollectionChanged;
            _transitionPropertyCollectionSubscription = null;
        }

        if (newValue is not TransitionPropertyCollection collection)
            return;

        collection.CollectionChanged -= OnTransitionPropertyCollectionChanged;
        collection.CollectionChanged += OnTransitionPropertyCollectionChanged;
        _transitionPropertyCollectionSubscription = collection;
    }

    private void OnTransitionPropertyCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        InvalidateTransitionPropertyLookup();
    }

    private void ScheduleAutomaticTransitionArmRecursive(UIElement root)
    {
        root.ScheduleAutomaticTransitionArm();

        for (int i = 0; i < root.VisualChildrenCount; i++)
        {
            if (root.GetVisualChild(i) is UIElement child)
            {
                ScheduleAutomaticTransitionArmRecursive(child);
            }
        }
    }

    private void DisarmAutomaticTransitionsRecursive(UIElement root)
    {
        root.DisarmAutomaticTransitions();

        for (int i = 0; i < root.VisualChildrenCount; i++)
        {
            if (root.GetVisualChild(i) is UIElement child)
            {
                DisarmAutomaticTransitionsRecursive(child);
            }
        }
    }

    private void ScheduleAutomaticTransitionArm()
    {
        var armVersion = unchecked(++_transitionArmVersion);
        _automaticTransitionsArmed = false;

        Dispatcher.BeginInvoke(() =>
        {
            if (_transitionArmVersion != armVersion)
                return;

            if (VisualParent == null)
                return;

            _automaticTransitionsArmed = true;
        });
    }

    private static Func<DependencyProperty, object?, object?, TimeSpan, TransitionTimingFunction, IAnimationTimeline?>? GetAutomaticTransitionAnimationFactory()
    {
        if (AutomaticTransitionAnimationFactory != null)
            return AutomaticTransitionAnimationFactory;

        var animationFactoryType = Type.GetType(
            "Jalium.UI.Media.Animation.AnimationFactory, Jalium.UI.Media",
            throwOnError: false);

        if (animationFactoryType == null)
            return null;

        var createTransitionMethod = animationFactoryType.GetMethod(
            "CreateTransitionAnimation",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
            binder: null,
            types:
            [
                typeof(DependencyProperty),
                typeof(object),
                typeof(object),
                typeof(TimeSpan),
                typeof(TransitionTimingFunction)
            ],
            modifiers: null);

        if (createTransitionMethod == null)
            return null;

        AutomaticTransitionAnimationFactory =
            (Func<DependencyProperty, object?, object?, TimeSpan, TransitionTimingFunction, IAnimationTimeline?>)
            Delegate.CreateDelegate(
                typeof(Func<DependencyProperty, object?, object?, TimeSpan, TransitionTimingFunction, IAnimationTimeline?>),
                createTransitionMethod);

        return AutomaticTransitionAnimationFactory;
    }

    private void DisarmAutomaticTransitions()
    {
        unchecked { _transitionArmVersion++; }
        _automaticTransitionsArmed = false;

        if (_activeAnimations == null || _activeAnimations.Count == 0)
            return;

        foreach (var dp in _activeAnimations
                     .Where(static pair => pair.Value.Kind == ElementAnimationKind.AutomaticTransition)
                     .Select(static pair => pair.Key)
                     .ToArray())
        {
            StopAutomaticTransition(dp, clearAnimatedValue: true);
        }
    }
}

using System.Runtime.CompilerServices;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Styling;

/// <summary>
/// Snapshot of a named view-transition participant before or after the DOM/state update callback.
/// </summary>
public sealed record CssViewTransitionSnapshot(
    string Name,
    string? ViewTransitionClass,
    Rect Bounds,
    double Opacity,
    FrameworkElement Element);

/// <summary>
/// Paired old/new state and interpolated metrics for a single named participant in a view transition.
/// </summary>
public sealed class CssViewTransitionParticipant
{
    internal Transform? SavedTransform { get; set; }
    internal Point SavedTransformOrigin { get; set; }
    internal double SavedOpacity { get; set; }
    internal bool HasSavedState { get; set; }

    public CssViewTransitionParticipant(
        string name,
        string? viewTransitionClass,
        CssViewTransitionSnapshot? oldSnapshot,
        CssViewTransitionSnapshot? newSnapshot)
    {
        Name = name;
        ViewTransitionClass = viewTransitionClass;
        OldSnapshot = oldSnapshot;
        NewSnapshot = newSnapshot;
        CurrentBounds = OldBounds;
        CurrentOpacity = OldOpacity;
    }

    public string Name { get; }
    public string? ViewTransitionClass { get; }
    public CssViewTransitionSnapshot? OldSnapshot { get; }
    public CssViewTransitionSnapshot? NewSnapshot { get; }

    public Rect OldBounds => OldSnapshot?.Bounds ?? NewSnapshot?.Bounds ?? Rect.Empty;
    public Rect NewBounds => NewSnapshot?.Bounds ?? OldSnapshot?.Bounds ?? Rect.Empty;
    public Rect CurrentBounds { get; internal set; }

    public double OldOpacity => OldSnapshot is not null ? OldSnapshot.Opacity : 0.0;
    public double NewOpacity => NewSnapshot is not null ? NewSnapshot.Opacity : 0.0;
    public double CurrentOpacity { get; internal set; }

    public FrameworkElement? TargetElement => NewSnapshot?.Element ?? OldSnapshot?.Element;

    public Rect InterpolateBounds(double progress)
    {
        var t = Math.Clamp(progress, 0.0, 1.0);
        var a = OldBounds;
        var b = NewBounds;
        if (a.IsEmpty) return b;
        if (b.IsEmpty) return a;
        return new Rect(
            a.X + (b.X - a.X) * t,
            a.Y + (b.Y - a.Y) * t,
            Math.Max(0.0, a.Width + (b.Width - a.Width) * t),
            Math.Max(0.0, a.Height + (b.Height - a.Height) * t));
    }

    public double InterpolateOpacity(double progress)
    {
        var t = Math.Clamp(progress, 0.0, 1.0);
        return OldOpacity + (NewOpacity - OldOpacity) * t;
    }
}

/// <summary>
/// Active controller for a CSS View Transition started via <see cref="CssViewTransitionHost.StartViewTransition"/>.
/// </summary>
public sealed class CssViewTransition
{
    private readonly Dictionary<string, CssViewTransitionParticipant> _participants;
    private readonly CssViewTransitionParticipant[] _participantList;
    private readonly Action<CssViewTransition>? _onFinished;

    internal CssViewTransition(
        FrameworkElement root,
        Dictionary<string, CssViewTransitionParticipant> participants,
        TimeSpan duration,
        Action<CssViewTransition>? onFinished)
    {
        Root = root;
        _participants = participants;
        _participantList = participants.Values.ToArray();
        Duration = duration;
        _onFinished = onFinished;
        IsActive = true;
        Seek(0.0);
    }

    public FrameworkElement Root { get; }
    public TimeSpan Duration { get; }
    public bool IsActive { get; private set; }
    public bool IsCompleted { get; private set; }
    public double Progress { get; private set; }

    public IReadOnlyDictionary<string, CssViewTransitionParticipant> Participants => _participants;
    public IReadOnlyList<CssViewTransitionParticipant> CapturedParticipants => _participantList;

    public CssViewTransitionParticipant? GetParticipant(string name)
        => _participants.TryGetValue(name, out var participant) ? participant : null;

    public Rect GetInterpolatedBounds(string name, double? progress = null)
    {
        if (!_participants.TryGetValue(name, out var participant))
            return Rect.Empty;
        return progress.HasValue
            ? participant.InterpolateBounds(progress.Value)
            : participant.CurrentBounds;
    }

    public double GetInterpolatedOpacity(string name, double? progress = null)
    {
        if (!_participants.TryGetValue(name, out var participant))
            return 0.0;
        return progress.HasValue
            ? participant.InterpolateOpacity(progress.Value)
            : participant.CurrentOpacity;
    }

    /// <summary>
    /// Seeks the view transition to the specified normalized <paramref name="progress"/> in <c>[0, 1]</c>,
    /// updating all participant snapshot bounds, opacities, and live element transforms.
    /// </summary>
    public void Seek(double progress)
    {
        if (!IsActive)
            return;

        var t = Math.Clamp(progress, 0.0, 1.0);
        Progress = t;

        foreach (var participant in _participantList)
        {
            var bounds = participant.InterpolateBounds(t);
            var opacity = participant.InterpolateOpacity(t);
            participant.CurrentBounds = bounds;
            participant.CurrentOpacity = opacity;

            if (participant.NewSnapshot?.Element is { } element)
            {
                if (!participant.HasSavedState)
                {
                    participant.SavedTransform = element.RenderTransform;
                    participant.SavedTransformOrigin = element.RenderTransformOrigin;
                    participant.SavedOpacity = element.Opacity;
                    participant.HasSavedState = true;
                }

                var targetBounds = participant.NewBounds;
                var sx = targetBounds.Width > 1e-6 ? bounds.Width / targetBounds.Width : 1.0;
                var sy = targetBounds.Height > 1e-6 ? bounds.Height / targetBounds.Height : 1.0;
                var dx = bounds.X - targetBounds.X;
                var dy = bounds.Y - targetBounds.Y;

                var matrix = new Matrix(sx, 0.0, 0.0, sy, dx, dy);
                var transform = new MatrixTransform(matrix);
                if (transform.CanFreeze)
                    transform.Freeze();

                element.RenderTransformOrigin = new Point(0, 0);
                element.SetCssAnimatedValue(UIElement.RenderTransformProperty, transform);
                element.SetCssAnimatedValue(UIElement.OpacityProperty, opacity);
                element.InvalidateVisual();
            }
        }

        Root.InvalidateVisual();
    }

    /// <summary>
    /// Finishes the view transition at progress <c>1.0</c> and cleans up temporary transition state.
    /// </summary>
    public void Complete()
    {
        if (!IsActive)
            return;

        Seek(1.0);
        RestoreParticipants();
        IsActive = false;
        IsCompleted = true;
        _onFinished?.Invoke(this);
    }

    /// <summary>
    /// Cancels the view transition immediately and restores underlying element state.
    /// </summary>
    public void Cancel()
    {
        if (!IsActive)
            return;

        RestoreParticipants();
        IsActive = false;
        IsCompleted = false;
        _onFinished?.Invoke(this);
    }

    private void RestoreParticipants()
    {
        foreach (var participant in _participantList)
        {
            if (participant.NewSnapshot?.Element is { } element && participant.HasSavedState)
            {
                element.ClearCssAnimatedValue(UIElement.RenderTransformProperty);
                element.ClearCssAnimatedValue(UIElement.OpacityProperty);
                element.RenderTransformOrigin = participant.SavedTransformOrigin;
                element.InvalidateVisual();
                participant.HasSavedState = false;
            }
        }

        Root.InvalidateVisual();
    }
}

/// <summary>
/// CSS View Transitions Level 1 &amp; 2 property registration (<c>view-transition-name</c>, <c>view-transition-class</c>).
/// </summary>
public static class CssViewTransitions
{
    public static readonly DependencyProperty NameProperty =
        DependencyProperty.RegisterAttached(
            "Name",
            typeof(string),
            typeof(CssViewTransitions),
            new PropertyMetadata(null));

    public static readonly DependencyProperty IsAutoNameProperty =
        DependencyProperty.RegisterAttached(
            "IsAutoName",
            typeof(bool),
            typeof(CssViewTransitions),
            new PropertyMetadata(false));

    public static readonly DependencyProperty ClassProperty =
        DependencyProperty.RegisterAttached(
            "Class",
            typeof(string),
            typeof(CssViewTransitions),
            new PropertyMetadata(null));

    static CssViewTransitions()
    {
        CssPropertyRegistry.RegisterExtension(Register);
    }

    /// <summary>
    /// Registers <c>view-transition-name</c> and <c>view-transition-class</c>.
    /// </summary>
    public static void Register()
    {
        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "view-transition-name",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = NameProperty,
            Parse = ParseViewTransitionName,
        });

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "view-transition-class",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = ClassProperty,
            Parse = ParseViewTransitionClass,
        });
    }

    public static string? GetViewTransitionName(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element.GetValue(NameProperty) is string explicitName && !string.IsNullOrEmpty(explicitName))
            return explicitName;

        if (element.GetValue(IsAutoNameProperty) is true)
        {
            if (element is FrameworkElement fe && !string.IsNullOrEmpty(fe.Name))
                return fe.Name;
            return $"auto-{RuntimeHelpers.GetHashCode(element):x8}";
        }

        return null;
    }

    public static void SetViewTransitionName(DependencyObject element, string? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(NameProperty, value);
    }

    public static string? GetViewTransitionClass(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetValue(ClassProperty) as string;
    }

    public static void SetViewTransitionClass(DependencyObject element, string? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(ClassProperty, value);
    }

    private static CssCompiledValue? ParseViewTransitionName(ref CssTokenReader reader, CssCompileContext context)
    {
        if (!reader.TryReadIdent(out var ident) || !reader.AtEnd)
            return null;

        if (ident.Equals("none", StringComparison.OrdinalIgnoreCase))
            return new CssViewTransitionNameValue(null, isAuto: false);

        if (ident.Equals("auto", StringComparison.OrdinalIgnoreCase))
            return new CssViewTransitionNameValue(null, isAuto: true);

        return new CssViewTransitionNameValue(ident.ToString(), isAuto: false);
    }

    private sealed class CssViewTransitionNameValue(string? name, bool isAuto) : CssCompiledValue
    {
        public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
        {
            sink.Set(NameProperty, name);
            sink.Set(IsAutoNameProperty, isAuto);
            return true;
        }
    }

    private static CssCompiledValue? ParseViewTransitionClass(ref CssTokenReader reader, CssCompileContext context)
    {
        var probe = reader;
        if (probe.TryReadIdent(out var single) && probe.AtEnd && single.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            reader = probe;
            return new CssImmediateValue(ClassProperty, null);
        }

        var classes = new List<string>();
        while (!reader.AtEnd)
        {
            if (!reader.TryReadIdent(out var ident) ||
                ident.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            classes.Add(ident.ToString());
        }

        if (classes.Count == 0)
            return null;

        return new CssImmediateValue(ClassProperty, string.Join(" ", classes));
    }
}

/// <summary>
/// Host coordinator for starting and managing CSS View Transitions on a <see cref="FrameworkElement"/> subtree.
/// </summary>
public static class CssViewTransitionHost
{
    private static readonly ConditionalWeakTable<FrameworkElement, CssViewTransition> s_activeTransitions = new();

    public static CssViewTransition? GetActiveTransition(FrameworkElement root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return s_activeTransitions.TryGetValue(root, out var transition) && transition.IsActive
            ? transition
            : null;
    }

    public static bool HasActiveTransition(FrameworkElement root)
        => GetActiveTransition(root) is not null;

    /// <summary>
    /// Captures old snapshots for all elements with <c>view-transition-name</c> under <paramref name="root"/>,
    /// invokes <paramref name="updateCallback"/>, re-evaluates CSS and layout, captures new snapshots,
    /// and returns a <see cref="CssViewTransition"/> controller.
    /// </summary>
    public static CssViewTransition StartViewTransition(
        FrameworkElement root,
        Action updateCallback,
        TimeSpan? duration = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(updateCallback);

        if (s_activeTransitions.TryGetValue(root, out var previous) && previous.IsActive)
        {
            previous.Cancel();
        }

        SettleSubtreeLayout(root);
        var oldSnapshots = CaptureNamedSnapshots(root);

        updateCallback();

        SettleSubtreeLayout(root);
        var newSnapshots = CaptureNamedSnapshots(root);

        var allNames = new HashSet<string>(oldSnapshots.Keys, StringComparer.Ordinal);
        foreach (var name in newSnapshots.Keys)
            allNames.Add(name);

        var participants = new Dictionary<string, CssViewTransitionParticipant>(allNames.Count, StringComparer.Ordinal);
        foreach (var name in allNames)
        {
            oldSnapshots.TryGetValue(name, out var oldSnap);
            newSnapshots.TryGetValue(name, out var newSnap);
            var vtClass = newSnap?.ViewTransitionClass ?? oldSnap?.ViewTransitionClass;
            participants[name] = new CssViewTransitionParticipant(name, vtClass, oldSnap, newSnap);
        }

        var transition = new CssViewTransition(
            root,
            participants,
            duration ?? TimeSpan.FromMilliseconds(250),
            t => s_activeTransitions.Remove(t.Root));

        s_activeTransitions.AddOrUpdate(root, transition);
        return transition;
    }

    private static void SettleSubtreeLayout(FrameworkElement root)
    {
        CssEvaluationScheduler.EvaluateSubtree(CssNode.Get(root));
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher ?? Dispatcher.CurrentDispatcher);

        var width = root.ActualWidth > 0
            ? root.ActualWidth
            : (double.IsFinite(root.Width) && root.Width > 0 ? root.Width : root.RenderSize.Width);
        var height = root.ActualHeight > 0
            ? root.ActualHeight
            : (double.IsFinite(root.Height) && root.Height > 0 ? root.Height : root.RenderSize.Height);

        if (width > 0 && height > 0)
        {
            root.Measure(new Size(width, height));
            root.Arrange(new Rect(0, 0, width, height));
            root.UpdateLayout();
        }
    }

    private static Dictionary<string, CssViewTransitionSnapshot> CaptureNamedSnapshots(FrameworkElement root)
    {
        var result = new Dictionary<string, CssViewTransitionSnapshot>(StringComparer.Ordinal);
        CaptureRecursive(root, root, result);
        return result;
    }

    private static void CaptureRecursive(
        FrameworkElement root,
        Visual current,
        Dictionary<string, CssViewTransitionSnapshot> snapshots)
    {
        if (current is FrameworkElement fe && fe.Visibility == Visibility.Visible)
        {
            var vtName = CssViewTransitions.GetViewTransitionName(fe);
            if (!string.IsNullOrEmpty(vtName))
            {
                var topLeft = ReferenceEquals(fe, root) ? new Point(0, 0) : fe.TransformToAncestor(root);
                var size = fe.RenderSize;
                if (size.Width <= 0 && size.Height <= 0)
                {
                    size = new Size(
                        fe.ActualWidth > 0 ? fe.ActualWidth : (double.IsFinite(fe.Width) ? fe.Width : 0),
                        fe.ActualHeight > 0 ? fe.ActualHeight : (double.IsFinite(fe.Height) ? fe.Height : 0));
                }

                var bounds = new Rect(topLeft.X, topLeft.Y, size.Width, size.Height);
                var vtClass = CssViewTransitions.GetViewTransitionClass(fe);
                snapshots[vtName!] = new CssViewTransitionSnapshot(vtName!, vtClass, bounds, fe.Opacity, fe);
            }
        }

        var childCount = current.InternalVisualChildrenCount;
        for (var i = 0; i < childCount; i++)
        {
            if (current.InternalGetVisualChild(i) is { } child)
            {
                CaptureRecursive(root, child, snapshots);
            }
        }
    }
}

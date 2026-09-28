using Jalium.UI;
using Jalium.UI.Markup;

namespace Jalium.UI.Media.Effects;

/// <summary>
/// A Freezable collection of element effects.
/// </summary>
public sealed class EffectCollection : FreezableCollection<Effect>
{
    private HashSet<EffectGroup>? _owners;

    /// <summary>Creates a modifiable deep clone of this collection.</summary>
    public new EffectCollection Clone() => (EffectCollection)base.Clone();

    /// <summary>Creates a modifiable deep clone using current values.</summary>
    public new EffectCollection CloneCurrentValue() => (EffectCollection)base.CloneCurrentValue();

    internal void ValidateOwner(EffectGroup owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        for (int i = 0; i < Count; i++)
        {
            ValidateAcyclicOwner(ChildrenGroupOrNull(this[i]), owner);
        }
    }

    internal void AttachOwner(EffectGroup owner)
    {
        ValidateOwner(owner);
        (_owners ??= new HashSet<EffectGroup>()).Add(owner);
    }

    internal void DetachOwner(EffectGroup owner)
    {
        if (_owners is null)
            return;

        _owners.Remove(owner);
        if (_owners.Count == 0)
            _owners = null;
    }

    protected override void ValidateItem(Effect item)
    {
        base.ValidateItem(item);

        if (_owners is null)
            return;

        var childGroup = ChildrenGroupOrNull(item);
        foreach (var owner in _owners)
        {
            ValidateAcyclicOwner(childGroup, owner);
        }
    }

    private static EffectGroup? ChildrenGroupOrNull(Effect effect) => effect as EffectGroup;

    private static void ValidateAcyclicOwner(EffectGroup? candidate, EffectGroup owner)
    {
        if (candidate is null)
            return;

        var pending = new Stack<EffectGroup>();
        var visited = new HashSet<EffectGroup>();
        pending.Push(candidate);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!visited.Add(current))
                continue;

            if (ReferenceEquals(current, owner))
            {
                throw new InvalidOperationException(
                    "An EffectGroup cannot contain itself, directly or indirectly.");
            }

            var children = current.Children;
            for (int i = 0; i < children.Count; i++)
            {
                if (children[i] is EffectGroup childGroup)
                    pending.Push(childGroup);
            }
        }
    }

    protected override Freezable CreateInstanceCore() => new EffectCollection();
}

/// <summary>
/// Composes multiple <see cref="Effect"/> instances into one element effect.
/// Children normally dispatch in declaration order. CSS box-shadow lists keep
/// authored order in this collection but paint from back to front.
/// </summary>
[ContentProperty(nameof(Children))]
public sealed class EffectGroup : Effect
{
    [ThreadStatic]
    private static HashSet<EffectGroup>? s_evaluationPath;

    /// <summary>Identifies the <see cref="Children"/> dependency property.</summary>
    public static readonly DependencyProperty ChildrenProperty =
        DependencyProperty.Register(
            nameof(Children),
            typeof(EffectCollection),
            typeof(EffectGroup),
            new PropertyMetadata(null));

    internal static readonly DependencyProperty CssShadowsFrontToBackProperty =
        DependencyProperty.Register(nameof(CssShadowsFrontToBack), typeof(bool),
            typeof(EffectGroup), new PropertyMetadata(false,
                static (d, _) => ((EffectGroup)d).OnEffectChanged()));

    internal bool CssShadowsFrontToBack
    {
        get => (bool)GetValue(CssShadowsFrontToBackProperty)!;
        set => SetValue(CssShadowsFrontToBackProperty, value);
    }

    internal static readonly DependencyProperty CssCombinedShadowAndFilterProperty =
        DependencyProperty.Register(nameof(CssCombinedShadowAndFilter), typeof(bool),
            typeof(EffectGroup), new PropertyMetadata(false,
                static (d, _) => ((EffectGroup)d).OnEffectChanged()));

    internal bool CssCombinedShadowAndFilter
    {
        get => (bool)GetValue(CssCombinedShadowAndFilterProperty)!;
        set => SetValue(CssCombinedShadowAndFilterProperty, value);
    }

    /// <summary>Initializes an empty effect group.</summary>
    public EffectGroup()
    {
        Children = new EffectCollection();
    }

    /// <summary>Gets or sets the child effects in declaration order.</summary>
    public EffectCollection Children
    {
        get => (EffectCollection?)GetValue(ChildrenProperty) ?? new EffectCollection();
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            value.ValidateOwner(this);
            SetValue(ChildrenProperty, value);
        }
    }

    /// <inheritdoc />
    public override bool HasEffect
    {
        get
        {
            var path = s_evaluationPath ??= new HashSet<EffectGroup>();
            if (!path.Add(this))
                return false;

            try
            {
                var children = Children;
                for (int i = 0; i < children.Count; i++)
                {
                    if (children[i].HasEffect)
                        return true;
                }
                return false;
            }
            finally
            {
                path.Remove(this);
                if (path.Count == 0)
                    s_evaluationPath = null;
            }
        }
    }

    /// <inheritdoc />
    public override EffectType EffectType => EffectType.EffectGroup;

    /// <inheritdoc />
    public override Thickness EffectPadding
    {
        get
        {
            var path = s_evaluationPath ??= new HashSet<EffectGroup>();
            if (!path.Add(this))
                return Thickness.Zero;

            // CSS shadow/filter pairs are serial; their extents add. Other
            // groups retain the largest child capture extent.
            try
            {
                if (CssCombinedShadowAndFilter && Children.Count == 2)
                {
                    var shadow = Children[0].EffectPadding;
                    var filter = Children[1].EffectPadding;
                    return new Thickness(shadow.Left + filter.Left,
                        shadow.Top + filter.Top,
                        shadow.Right + filter.Right,
                        shadow.Bottom + filter.Bottom);
                }
                double left = 0, top = 0, right = 0, bottom = 0;
                var children = Children;
                for (int i = 0; i < children.Count; i++)
                {
                    var p = children[i].EffectPadding;
                    left = Math.Max(left, p.Left);
                    top = Math.Max(top, p.Top);
                    right = Math.Max(right, p.Right);
                    bottom = Math.Max(bottom, p.Bottom);
                }
                return new Thickness(left, top, right, bottom);
            }
            finally
            {
                path.Remove(this);
                if (path.Count == 0)
                    s_evaluationPath = null;
            }
        }
    }

    /// <summary>Gets the active effects (those with <see cref="Effect.HasEffect"/>).</summary>
    internal IReadOnlyList<Effect> ActiveEffects
    {
        get
        {
            var result = new List<Effect>();
            var path = s_evaluationPath ??= new HashSet<EffectGroup>();
            if (!path.Add(this))
                return result;

            try
            {
                var children = Children;
                for (int i = 0; i < children.Count; i++)
                {
                    if (children[i].HasEffect)
                        result.Add(children[i]);
                }
                return result;
            }
            finally
            {
                path.Remove(this);
                if (path.Count == 0)
                    s_evaluationPath = null;
            }
        }
    }

    /// <summary>Creates a modifiable deep clone of this group.</summary>
    public new EffectGroup Clone() => (EffectGroup)base.Clone();

    /// <summary>Creates a modifiable deep clone using current values.</summary>
    public new EffectGroup CloneCurrentValue() => (EffectGroup)base.CloneCurrentValue();

    protected override Freezable CreateInstanceCore() => new EffectGroup();

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (ReferenceEquals(e.Property, ChildrenProperty))
        {
            if (e.OldValue is EffectCollection oldChildren &&
                !ReferenceEquals(oldChildren, e.NewValue))
            {
                oldChildren.DetachOwner(this);
            }

            if (e.NewValue is EffectCollection newChildren)
            {
                newChildren.AttachOwner(this);
            }

            OnFreezablePropertyChanged(
                e.OldValue as DependencyObject,
                e.NewValue as DependencyObject,
                ChildrenProperty);
            OnEffectChanged();
        }
    }
}

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Jalium.UI;

namespace Jalium.UI.Media.Effects;

/// <summary>
/// Composes multiple <see cref="Effect"/> instances into a single effect that applies them
/// sequentially. Each effect in the group is applied in order, with the output of one
/// feeding into the input of the next.
/// This is the modern replacement for the deprecated BitmapEffectGroup.
/// </summary>
public sealed class EffectGroup : Effect
{
    private readonly ObservableCollection<Effect> _children = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="EffectGroup"/> class.
    /// </summary>
    public EffectGroup()
    {
        _children.CollectionChanged += OnChildrenChanged;
    }

    /// <summary>
    /// Gets the collection of child effects. Effects are applied in order.
    /// </summary>
    public ObservableCollection<Effect> Children => _children;

    /// <inheritdoc />
    public override bool HasEffect
    {
        get
        {
            for (int i = 0; i < _children.Count; i++)
            {
                if (_children[i].HasEffect)
                    return true;
            }
            return false;
        }
    }

    /// <inheritdoc />
    public override EffectType EffectType => EffectType.EffectGroup;

    /// <inheritdoc />
    public override Thickness EffectPadding
    {
        get
        {
            // Accumulate padding from all child effects
            double left = 0, top = 0, right = 0, bottom = 0;
            for (int i = 0; i < _children.Count; i++)
            {
                var p = _children[i].EffectPadding;
                left = Math.Max(left, p.Left);
                top = Math.Max(top, p.Top);
                right = Math.Max(right, p.Right);
                bottom = Math.Max(bottom, p.Bottom);
            }
            return new Thickness(left, top, right, bottom);
        }
    }

    /// <summary>
    /// Gets the active effects (those with HasEffect == true).
    /// </summary>
    internal IReadOnlyList<Effect> ActiveEffects
    {
        get
        {
            var result = new List<Effect>();
            for (int i = 0; i < _children.Count; i++)
            {
                if (_children[i].HasEffect)
                    result.Add(_children[i]);
            }
            return result;
        }
    }

    private void OnChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Unsubscribe from old items
        if (e.OldItems != null)
        {
            foreach (Effect effect in e.OldItems)
            {
                effect.EffectChanged -= OnChildEffectChanged;
            }
        }

        // Subscribe to new items
        if (e.NewItems != null)
        {
            foreach (Effect effect in e.NewItems)
            {
                effect.EffectChanged += OnChildEffectChanged;
            }
        }

        OnEffectChanged();
    }

    private void OnChildEffectChanged(object? sender, EventArgs e)
    {
        OnEffectChanged();
    }
}

namespace Jalium.UI.Media;

using Jalium.UI.Media.Animation;

/// <summary>Defines caching behavior for a visual subtree.</summary>
[System.ComponentModel.TypeConverter("Jalium.UI.Media.CacheModeConverter, Jalium.UI.Media")]
public abstract class CacheMode : Animatable
{
    public new CacheMode Clone() => (CacheMode)base.Clone();
    public new CacheMode CloneCurrentValue() => (CacheMode)base.CloneCurrentValue();

    protected override void OnChanged() => base.OnChanged();
}

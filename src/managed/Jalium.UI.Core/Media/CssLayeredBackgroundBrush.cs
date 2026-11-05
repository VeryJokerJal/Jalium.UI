using Jalium.UI.Styling;

namespace Jalium.UI.Media;

/// <summary>
/// CSS paints its background color below its background image. Keeping the
/// two brushes together in the Background value preserves native local-value
/// precedence and lets every shape use its own existing contour.
/// </summary>
internal sealed class CssLayeredBackgroundBrush : Brush
{
    internal WeakReference<DependencyObject>? BlendOwner { get; init; }
    internal int ImageIndex { get; init; }
    private static readonly DependencyProperty SnapshotBlendModeProperty = DependencyProperty.Register(
        "SnapshotBlendMode", typeof(CssBlendMode), typeof(CssLayeredBackgroundBrush), new PropertyMetadata(CssBlendMode.Normal));
    internal CssBlendMode SnapshotBlendMode
    {
        get => (CssBlendMode)GetValue(SnapshotBlendModeProperty)!;
        init => SetValue(SnapshotBlendModeProperty, value);
    }
    internal CssBlendMode BlendMode => BlendOwner is { } reference && reference.TryGetTarget(out var owner)
        ? CssCompositingProperties.Background(owner, ImageIndex) : SnapshotBlendMode;
    internal static readonly DependencyProperty BottomProperty =
        DependencyProperty.Register(nameof(Bottom), typeof(Brush),
            typeof(CssLayeredBackgroundBrush), new PropertyMetadata(null));

    internal static readonly DependencyProperty TopProperty =
        DependencyProperty.Register(nameof(Top), typeof(Brush),
            typeof(CssLayeredBackgroundBrush), new PropertyMetadata(null));

    internal Brush? Bottom
    {
        get => (Brush?)GetValue(BottomProperty);
        set => SetValue(BottomProperty, value);
    }

    internal Brush? Top
    {
        get => (Brush?)GetValue(TopProperty);
        set => SetValue(TopProperty, value);
    }

    internal CssLayeredBackgroundBrush() { }

    internal CssLayeredBackgroundBrush(Brush bottom, Brush top)
    {
        Bottom = bottom;
        Top = top;
    }

    protected override Freezable CreateInstanceCore() => new CssLayeredBackgroundBrush();

    // Clones become independent brushes. Capture the owner's current list value
    // instead of dropping the mode or retaining a live CSS owner on the clone.
    protected override void CloneCore(Freezable source)
    { base.CloneCore(source); SetValue(SnapshotBlendModeProperty, ((CssLayeredBackgroundBrush)source).BlendMode); }
    protected override void CloneCurrentValueCore(Freezable source)
    { base.CloneCurrentValueCore(source); SetValue(SnapshotBlendModeProperty, ((CssLayeredBackgroundBrush)source).BlendMode); }
    protected override void GetAsFrozenCore(Freezable source)
    { base.GetAsFrozenCore(source); SetValue(SnapshotBlendModeProperty, ((CssLayeredBackgroundBrush)source).BlendMode); }
    protected override void GetCurrentValueAsFrozenCore(Freezable source)
    { base.GetCurrentValueAsFrozenCore(source); SetValue(SnapshotBlendModeProperty, ((CssLayeredBackgroundBrush)source).BlendMode); }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (ReferenceEquals(e.Property, BottomProperty) ||
            ReferenceEquals(e.Property, TopProperty) || ReferenceEquals(e.Property, SnapshotBlendModeProperty))
        {
            OnFreezablePropertyChanged(e.OldValue as DependencyObject,
                e.NewValue as DependencyObject, e.Property);
            WritePostscript();
        }
    }
}

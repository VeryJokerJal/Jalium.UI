namespace Jalium.UI.Media;

/// <summary>
/// CSS paints its background color below its background image. Keeping the
/// two brushes together in the Background value preserves native local-value
/// precedence and lets every shape use its own existing contour.
/// </summary>
internal sealed class CssLayeredBackgroundBrush : Brush
{
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

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (ReferenceEquals(e.Property, BottomProperty) ||
            ReferenceEquals(e.Property, TopProperty))
        {
            OnFreezablePropertyChanged(e.OldValue as DependencyObject,
                e.NewValue as DependencyObject, e.Property);
            WritePostscript();
        }
    }
}

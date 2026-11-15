namespace Jalium.UI.Controls.Primitives;

/// <summary>Arranges the line buttons and track of a scrollbar template.</summary>
public sealed class ScrollBarLayoutPanel : Panel
{
    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
        nameof(Orientation), typeof(Orientation), typeof(ScrollBarLayoutPanel),
        new FrameworkPropertyMetadata(Orientation.Vertical, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty UsesLineButtonsProperty = DependencyProperty.Register(
        nameof(UsesLineButtons), typeof(bool), typeof(ScrollBarLayoutPanel),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty PreferredThicknessProperty = DependencyProperty.Register(
        nameof(PreferredThickness), typeof(double), typeof(ScrollBarLayoutPanel),
        new FrameworkPropertyMetadata(16d, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty TrackEndInsetProperty = DependencyProperty.Register(
        nameof(TrackEndInset), typeof(double), typeof(ScrollBarLayoutPanel),
        new FrameworkPropertyMetadata(3d, FrameworkPropertyMetadataOptions.AffectsArrange));
    public static readonly DependencyProperty RoleProperty = DependencyProperty.RegisterAttached(
        "Role", typeof(ScrollBarLayoutRole), typeof(ScrollBarLayoutPanel),
        new FrameworkPropertyMetadata(ScrollBarLayoutRole.None, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public Orientation Orientation { get => (Orientation)GetValue(OrientationProperty)!; set => SetValue(OrientationProperty, value); }
    public bool UsesLineButtons { get => (bool)GetValue(UsesLineButtonsProperty)!; set => SetValue(UsesLineButtonsProperty, value); }
    public double PreferredThickness { get => (double)GetValue(PreferredThicknessProperty)!; set => SetValue(PreferredThicknessProperty, value); }
    public double TrackEndInset { get => (double)GetValue(TrackEndInsetProperty)!; set => SetValue(TrackEndInsetProperty, value); }
    public static ScrollBarLayoutRole GetRole(DependencyObject target) => (ScrollBarLayoutRole)target.GetValue(RoleProperty)!;
    public static void SetRole(DependencyObject target, ScrollBarLayoutRole role) => target.SetValue(RoleProperty, role);

    protected override Size MeasureOverride(Size availableSize)
    {
        bool vertical = Orientation == Orientation.Vertical;
        double cross = double.IsFinite(PreferredThickness) ? Math.Max(0, PreferredThickness) : 16;
        double axis = vertical ? availableSize.Height : availableSize.Width;
        double button = UsesLineButtons ? cross : 0;
        if (!double.IsFinite(axis)) axis = button * 2 + 40;
        double track = Math.Max(0, axis - button * 2);
        foreach (UIElement child in Children)
        {
            double length = GetRole(child) is ScrollBarLayoutRole.DecreaseLine or ScrollBarLayoutRole.IncreaseLine
                ? button : track;
            child.Measure(vertical ? new Size(cross, length) : new Size(length, cross));
        }
        return vertical ? new Size(cross, axis) : new Size(axis, cross);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        bool vertical = Orientation == Orientation.Vertical;
        double axis = vertical ? finalSize.Height : finalSize.Width;
        double cross = vertical ? finalSize.Width : finalSize.Height;
        double button = UsesLineButtons ? Math.Min(cross, axis / 2) : 0;
        double inset = UsesLineButtons ? 0 : Math.Min(Math.Max(0, TrackEndInset), axis / 2);
        foreach (UIElement child in Children)
        {
            var rect = GetRole(child) switch
            {
                ScrollBarLayoutRole.DecreaseLine => AxisRect(0, button),
                ScrollBarLayoutRole.IncreaseLine => AxisRect(axis - button, button),
                ScrollBarLayoutRole.Track => AxisRect(button + inset, Math.Max(0, axis - button * 2 - inset * 2)),
                _ => new Rect(finalSize)
            };
            child.Arrange(rect);
        }
        return finalSize;

        Rect AxisRect(double start, double length) => vertical
            ? new Rect(0, start, cross, length) : new Rect(start, 0, length, cross);
    }
}

/// <summary>Identifies a template child's role in scrollbar layout.</summary>
public enum ScrollBarLayoutRole { None, DecreaseLine, Track, IncreaseLine }

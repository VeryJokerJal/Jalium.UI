using Jalium.UI.Media;

namespace Jalium.UI.Controls;

[TemplatePart(Name = "PART_Targets", Type = typeof(ItemsControl))]
[TemplatePart(Name = "PART_Preview", Type = typeof(Border))]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Idle", GroupName = "HoverStates")]
[TemplateVisualState(Name = "TargetHovered", GroupName = "HoverStates")]
public sealed partial class DockIndicatorVisual
{
    private readonly DockIndicatorTargetInfo[] _targets =
    [
        new(DockPosition.Top), new(DockPosition.Left), new(DockPosition.Center),
        new(DockPosition.Right), new(DockPosition.Bottom), new(DockPosition.EdgeTop),
        new(DockPosition.EdgeBottom), new(DockPosition.EdgeLeft), new(DockPosition.EdgeRight)
    ];
    private ItemsControl? _targetPresenter;
    private bool _refreshing;

    static DockIndicatorVisual() => DefaultStyleKeyProperty.OverrideMetadata(typeof(DockIndicatorVisual), new PropertyMetadata(typeof(DockIndicatorVisual)));
    public static readonly DependencyProperty HoveredPositionProperty = DependencyProperty.Register(nameof(HoveredPosition), typeof(DockPosition), typeof(DockIndicatorVisual), new PropertyMetadata(DockPosition.None));
    public static readonly DependencyProperty ShowCenterCrossProperty = DependencyProperty.Register(nameof(ShowCenterCross), typeof(bool), typeof(DockIndicatorVisual), new PropertyMetadata(false));
    public static readonly DependencyProperty ShowEdgeButtonsProperty = DependencyProperty.Register(nameof(ShowEdgeButtons), typeof(bool), typeof(DockIndicatorVisual), new PropertyMetadata(false));
    public static readonly DependencyProperty TargetTemplateProperty = DependencyProperty.Register(nameof(TargetTemplate), typeof(DataTemplate), typeof(DockIndicatorVisual), new PropertyMetadata(null));
    public DataTemplate? TargetTemplate { get => (DataTemplate?)GetValue(TargetTemplateProperty); set => SetValue(TargetTemplateProperty, value); }
    public static readonly DependencyProperty TargetContainerStyleProperty = DependencyProperty.Register(nameof(TargetContainerStyle), typeof(Style), typeof(DockIndicatorVisual), new PropertyMetadata(null));
    public Style? TargetContainerStyle { get => (Style?)GetValue(TargetContainerStyleProperty); set => SetValue(TargetContainerStyleProperty, value); }
    public static readonly DependencyProperty HoverBackgroundProperty = DependencyProperty.Register(nameof(HoverBackground), typeof(Brush), typeof(DockIndicatorVisual), new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0x1e, 0x79, 0x3f)), FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush? HoverBackground { get => (Brush?)GetValue(HoverBackgroundProperty); set => SetValue(HoverBackgroundProperty, value); }
    public static readonly DependencyProperty HoverForegroundProperty = DependencyProperty.Register(nameof(HoverForeground), typeof(Brush), typeof(DockIndicatorVisual), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush? HoverForeground { get => (Brush?)GetValue(HoverForegroundProperty); set => SetValue(HoverForegroundProperty, value); }
    public static readonly DependencyProperty ChromeBackgroundProperty = DependencyProperty.Register(nameof(ChromeBackground), typeof(Brush), typeof(DockIndicatorVisual), new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromArgb(230, 32, 32, 32)), FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush? ChromeBackground { get => (Brush?)GetValue(ChromeBackgroundProperty); set => SetValue(ChromeBackgroundProperty, value); }
    public static readonly DependencyProperty ChromeBorderBrushProperty = DependencyProperty.Register(nameof(ChromeBorderBrush), typeof(Brush), typeof(DockIndicatorVisual), new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromArgb(180, 70, 70, 70)), FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush? ChromeBorderBrush { get => (Brush?)GetValue(ChromeBorderBrushProperty); set => SetValue(ChromeBorderBrushProperty, value); }

    private static readonly DependencyPropertyKey TargetsPropertyKey = DependencyProperty.RegisterReadOnly(nameof(Targets), typeof(IReadOnlyList<DockIndicatorTargetInfo>), typeof(DockIndicatorVisual), new PropertyMetadata(null));
    public static readonly DependencyProperty TargetsProperty = TargetsPropertyKey.DependencyProperty;
    public IReadOnlyList<DockIndicatorTargetInfo> Targets => (IReadOnlyList<DockIndicatorTargetInfo>)GetValue(TargetsProperty)!;
    private static readonly DependencyPropertyKey PreviewBoundsPropertyKey = DependencyProperty.RegisterReadOnly(nameof(PreviewBounds), typeof(Rect), typeof(DockIndicatorVisual), new PropertyMetadata(default(Rect)));
    public static readonly DependencyProperty PreviewBoundsProperty = PreviewBoundsPropertyKey.DependencyProperty;
    public Rect PreviewBounds => (Rect)GetValue(PreviewBoundsProperty)!;
    private static readonly DependencyPropertyKey PreviewVisibilityPropertyKey = DependencyProperty.RegisterReadOnly(nameof(PreviewVisibility), typeof(Visibility), typeof(DockIndicatorVisual), new PropertyMetadata(Visibility.Collapsed));
    public static readonly DependencyProperty PreviewVisibilityProperty = PreviewVisibilityPropertyKey.DependencyProperty;
    public Visibility PreviewVisibility => (Visibility)GetValue(PreviewVisibilityProperty)!;
    private static readonly DependencyPropertyKey CrossVisibilityPropertyKey = DependencyProperty.RegisterReadOnly(nameof(CrossVisibility), typeof(Visibility), typeof(DockIndicatorVisual), new PropertyMetadata(Visibility.Collapsed));
    public static readonly DependencyProperty CrossVisibilityProperty = CrossVisibilityPropertyKey.DependencyProperty;
    public Visibility CrossVisibility => (Visibility)GetValue(CrossVisibilityProperty)!;
    private static readonly DependencyPropertyKey HorizontalCrossBoundsPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HorizontalCrossBounds), typeof(Rect), typeof(DockIndicatorVisual), new PropertyMetadata(default(Rect)));
    public static readonly DependencyProperty HorizontalCrossBoundsProperty = HorizontalCrossBoundsPropertyKey.DependencyProperty;
    public Rect HorizontalCrossBounds => (Rect)GetValue(HorizontalCrossBoundsProperty)!;
    private static readonly DependencyPropertyKey VerticalCrossBoundsPropertyKey = DependencyProperty.RegisterReadOnly(nameof(VerticalCrossBounds), typeof(Rect), typeof(DockIndicatorVisual), new PropertyMetadata(default(Rect)));
    public static readonly DependencyProperty VerticalCrossBoundsProperty = VerticalCrossBoundsPropertyKey.DependencyProperty;
    public Rect VerticalCrossBounds => (Rect)GetValue(VerticalCrossBoundsProperty)!;

    public override void OnApplyTemplate()
    {
        ReleaseTargetPresenter();
        base.OnApplyTemplate();
        _targetPresenter = GetTemplateChild("PART_Targets") as ItemsControl;
        RefreshPresentation(RenderSize);
    }

    protected override void OnTemplateChanging()
    {
        ReleaseTargetPresenter();
        base.OnTemplateChanging();
    }

    private void ReleaseTargetPresenter()
    {
        if (_targetPresenter is { } presenter)
        {
            presenter.ClearBinding(ItemsControl.ItemsSourceProperty);
            presenter.SetCurrentValue(ItemsControl.ItemsSourceProperty, null);
        }
        _targetPresenter = null;
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_refreshing && (e.Property == HoveredPositionProperty || e.Property == ShowCenterCrossProperty ||
            e.Property == ShowEdgeButtonsProperty || e.Property == IsEnabledProperty)) RefreshPresentation(RenderSize);
    }

    private void RefreshPresentation(Size size)
    {
        if (_refreshing || _targets is null) return;
        _refreshing = true;
        try
        {
            var hovered = false;
            foreach (var target in _targets)
            {
                var visible = target.IsEdge ? ShowEdgeButtons : ShowCenterCross;
                var bounds = target.IsEdge ? DockIndicator.GetEdgeButtonRect(size.Width, size.Height, target.Position) : DockIndicator.GetCenterButtonRect(size.Width, size.Height, target.Position);
                var active = visible && target.Position == HoveredPosition;
                target.Update(bounds, visible, active);
                hovered |= active;
            }
            var position = HoveredPosition switch
            {
                DockPosition.EdgeLeft => DockPosition.Left, DockPosition.EdgeRight => DockPosition.Right,
                DockPosition.EdgeTop => DockPosition.Top, DockPosition.EdgeBottom => DockPosition.Bottom,
                _ => HoveredPosition
            };
            var preview = position switch
            {
                DockPosition.Left => new Rect(0, 0, size.Width / 2, size.Height),
                DockPosition.Right => new Rect(size.Width / 2, 0, size.Width / 2, size.Height),
                DockPosition.Top => new Rect(0, 0, size.Width, size.Height / 2),
                DockPosition.Bottom => new Rect(0, size.Height / 2, size.Width, size.Height / 2),
                DockPosition.Center => new Rect(size), _ => default
            };
            SetValue(PreviewBoundsPropertyKey, preview);
            SetValue(PreviewVisibilityPropertyKey, hovered ? Visibility.Visible : Visibility.Collapsed);
            SetValue(CrossVisibilityPropertyKey, ShowCenterCross ? Visibility.Visible : Visibility.Collapsed);
            SetValue(HorizontalCrossBoundsPropertyKey, new Rect(size.Width / 2 - 57, size.Height / 2 - 21, 114, 42));
            SetValue(VerticalCrossBoundsPropertyKey, new Rect(size.Width / 2 - 21, size.Height / 2 - 57, 42, 114));
            if (TemplateRootInternal is not null)
            {
                VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
                VisualStateManager.GoToState(this, hovered ? "TargetHovered" : "Idle", false);
            }
        }
        finally { _refreshing = false; }
    }
}

/// <summary>Stable data for a docking target; contains no visual elements.</summary>
public sealed class DockIndicatorTargetInfo : DependencyObject
{
    public DockPosition Position { get; }
    public bool IsEdge => Position is DockPosition.EdgeTop or DockPosition.EdgeBottom or DockPosition.EdgeLeft or DockPosition.EdgeRight;
    public Visibility EdgePlateVisibility => IsEdge ? Visibility.Visible : Visibility.Collapsed;
    public Rect IconRegion { get; }
    private static readonly DependencyPropertyKey BoundsPropertyKey = DependencyProperty.RegisterReadOnly(nameof(Bounds), typeof(Rect), typeof(DockIndicatorTargetInfo), new PropertyMetadata(default(Rect)));
    public static readonly DependencyProperty BoundsProperty = BoundsPropertyKey.DependencyProperty;
    public Rect Bounds => (Rect)GetValue(BoundsProperty)!;
    private static readonly DependencyPropertyKey VisibilityPropertyKey = DependencyProperty.RegisterReadOnly(nameof(Visibility), typeof(Visibility), typeof(DockIndicatorTargetInfo), new PropertyMetadata(Visibility.Collapsed));
    public static readonly DependencyProperty VisibilityProperty = VisibilityPropertyKey.DependencyProperty;
    public Visibility Visibility => (Visibility)GetValue(VisibilityProperty)!;
    private static readonly DependencyPropertyKey IsHoveredPropertyKey = DependencyProperty.RegisterReadOnly(nameof(IsHovered), typeof(bool), typeof(DockIndicatorTargetInfo), new PropertyMetadata(false));
    public static readonly DependencyProperty IsHoveredProperty = IsHoveredPropertyKey.DependencyProperty;
    public bool IsHovered => (bool)GetValue(IsHoveredProperty)!;

    internal DockIndicatorTargetInfo(DockPosition position)
    {
        Position = position;
        IconRegion = position switch
        {
            DockPosition.Left or DockPosition.EdgeLeft => new Rect(0, 0, 7.2, 18),
            DockPosition.Right or DockPosition.EdgeRight => new Rect(10.8, 0, 7.2, 18),
            DockPosition.Top or DockPosition.EdgeTop => new Rect(0, 0, 18, 7.2),
            DockPosition.Bottom or DockPosition.EdgeBottom => new Rect(0, 10.8, 18, 7.2),
            _ => new Rect(3, 3, 12, 12)
        };
    }

    internal void Update(Rect bounds, bool visible, bool hovered)
    {
        SetValue(BoundsPropertyKey, bounds);
        SetValue(VisibilityPropertyKey, visible ? Visibility.Visible : Visibility.Collapsed);
        SetValue(IsHoveredPropertyKey, hovered);
    }
}

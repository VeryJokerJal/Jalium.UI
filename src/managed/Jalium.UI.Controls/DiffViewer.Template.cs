using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

[TemplatePart(Name = "PART_ContentView", Type = typeof(DiffContentView))]
[TemplatePart(Name = "PART_Minimap", Type = typeof(Border))]
[TemplatePart(Name = "PART_MinimapViewport", Type = typeof(Border))]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Focused", GroupName = "FocusStates")]
[TemplateVisualState(Name = "Unfocused", GroupName = "FocusStates")]
[TemplateVisualState(Name = "SideBySide", GroupName = "ViewStates")]
[TemplateVisualState(Name = "Unified", GroupName = "ViewStates")]
public partial class DiffViewer
{
    private DiffContentView? _diffContentView;
    private bool _refreshingDiffPresentation;
    private (Size, Thickness, CornerRadius)? _diffClipStamp;

    static DiffViewer() => DefaultStyleKeyProperty.OverrideMetadata(typeof(DiffViewer), new PropertyMetadata(typeof(DiffViewer)));

    private static readonly DependencyPropertyKey DiffContentClipPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(DiffContentClip), typeof(Geometry), typeof(DiffViewer), new PropertyMetadata(null));
    public static readonly DependencyProperty DiffContentClipProperty = DiffContentClipPropertyKey.DependencyProperty;
    public Geometry? DiffContentClip => (Geometry?)GetValue(DiffContentClipProperty);

    private static readonly DependencyPropertyKey MinimapBoundsPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(MinimapBounds), typeof(Rect), typeof(DiffViewer), new PropertyMetadata(default(Rect)));
    public static readonly DependencyProperty MinimapBoundsProperty = MinimapBoundsPropertyKey.DependencyProperty;
    public Rect MinimapBounds => (Rect)GetValue(MinimapBoundsProperty)!;

    private static readonly DependencyPropertyKey MinimapViewportBoundsPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(MinimapViewportBounds), typeof(Rect), typeof(DiffViewer), new PropertyMetadata(default(Rect)));
    public static readonly DependencyProperty MinimapViewportBoundsProperty = MinimapViewportBoundsPropertyKey.DependencyProperty;
    public Rect MinimapViewportBounds => (Rect)GetValue(MinimapViewportBoundsProperty)!;

    private static readonly DependencyPropertyKey MinimapVisibilityPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(MinimapVisibility), typeof(Visibility), typeof(DiffViewer), new PropertyMetadata(Visibility.Collapsed));
    public static readonly DependencyProperty MinimapVisibilityProperty = MinimapVisibilityPropertyKey.DependencyProperty;
    public Visibility MinimapVisibility => (Visibility)GetValue(MinimapVisibilityProperty)!;

    public override void OnApplyTemplate()
    {
        ReleaseDiffView();
        base.OnApplyTemplate();
        _diffContentView = GetTemplateChild("PART_ContentView") as DiffContentView;
        _diffContentView?.SetCurrentValue(DiffContentView.OwnerProperty, this);
        RefreshDiffPresentation();
    }

    protected override void OnTemplateChanging()
    {
        ReleaseDiffView();
        base.OnTemplateChanging();
    }

    private void ReleaseDiffView()
    {
        if (_diffContentView is { } view)
        {
            view.ClearBinding(DiffContentView.OwnerProperty);
            view.SetCurrentValue(DiffContentView.OwnerProperty, null);
        }
        _diffContentView = null;
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_refreshingDiffPresentation && TemplateRootInternal is not null)
            InvalidateDiffView();
    }

    protected internal override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        RefreshDiffPresentation();
    }

    private void InvalidateDiffView()
    {
        RefreshDiffPresentation();
        _diffContentView?.InvalidateVisual();
        InvalidateVisual();
    }

    private void RefreshDiffPresentation()
    {
        if (_refreshingDiffPresentation || RenderSize.Width <= 0 || RenderSize.Height <= 0) return;
        _refreshingDiffPresentation = true;
        try
        {
            UpdateLayoutMetrics(RenderSize);
            var width = Math.Min(MinimapWidth, RenderSize.Width);
            var left = Math.Max(0, RenderSize.Width - width);
            SetValue(MinimapBoundsPropertyKey, new Rect(left, 0, width, RenderSize.Height));
            var height = Math.Clamp(RenderSize.Height / Math.Max(1, _totalContentHeight) * RenderSize.Height, Math.Min(10, RenderSize.Height), RenderSize.Height);
            var top = Math.Clamp(_scrollOffsetY / Math.Max(1, _totalContentHeight) * RenderSize.Height, 0, RenderSize.Height - height);
            SetValue(MinimapViewportBoundsPropertyKey, new Rect(left, top, width, height));
            SetValue(MinimapVisibilityPropertyKey, ShowMinimap && _diffLines.Count > 0 ? Visibility.Visible : Visibility.Collapsed);

            if (_diffClipStamp != (RenderSize, BorderThickness, CornerRadius))
            {
                _diffClipStamp = (RenderSize, BorderThickness, CornerRadius);
                var rect = ControlRenderGeometry.GetContentRect(new Rect(RenderSize), BorderThickness);
                var radii = CssBackgroundPainter.CircularRadii(CornerRadius).Normalize(RenderSize).Inset(BorderThickness).Normalize(rect.Size);
                var clip = new PathGeometry();
                clip.Figures.Add(CssRoundedRectangleGeometry.Figure(rect, radii));
                clip.Freeze();
                SetValue(DiffContentClipPropertyKey, clip);
            }
            if (TemplateRootInternal is not null)
            {
                VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
                VisualStateManager.GoToState(this, IsFocused ? "Focused" : "Unfocused", false);
                VisualStateManager.GoToState(this, ViewMode == DiffViewMode.SideBySide ? "SideBySide" : "Unified", false);
            }
        }
        finally { _refreshingDiffPresentation = false; }
    }
}

/// <summary>Renders diff text, line markers, selection and minimap data for its templated owner.</summary>
public sealed class DiffContentView : FrameworkElement
{
    public static readonly DependencyProperty OwnerProperty = DependencyProperty.Register(
        nameof(Owner), typeof(DiffViewer), typeof(DiffContentView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public DiffViewer? Owner { get => (DiffViewer?)GetValue(OwnerProperty); set => SetValue(OwnerProperty, value); }
    public DiffContentView() => Focusable = false;
    protected override bool ParticipatesInRenderCache => false;
    protected override Size MeasureOverride(Size size) => new(double.IsFinite(size.Width) ? size.Width : 0, double.IsFinite(size.Height) ? size.Height : 0);
    protected override void OnRender(DrawingContext dc) => Owner?.RenderDiffContent(dc);
}

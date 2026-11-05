using Jalium.UI.Media;
using Jalium.UI.Interop;

namespace Jalium.UI.Controls;
/// <summary>Renders one menu icon; visual icon content is hosted by its default template.</summary>
internal sealed class MenuIconPresenter : Control
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(nameof(Icon), typeof(object), typeof(MenuIconPresenter), new PropertyMetadata(null, (d, _) => ((MenuIconPresenter)d).RefreshIcon()));
    private static readonly DependencyPropertyKey VisualIconPropertyKey = DependencyProperty.RegisterReadOnly(nameof(VisualIcon), typeof(UIElement), typeof(MenuIconPresenter), new PropertyMetadata(null));
    public static readonly DependencyProperty VisualIconProperty = VisualIconPropertyKey.DependencyProperty;
    public object? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public UIElement? VisualIcon => (UIElement? )GetValue(VisualIconProperty);

    private IconSource? _source;
    private bool _suspended;
    static MenuIconPresenter() => DefaultStyleKeyProperty.OverrideMetadata(typeof(MenuIconPresenter), new PropertyMetadata(typeof(MenuIconPresenter)));
    public MenuIconPresenter()
    {
        Loaded += (_, _) =>
        {
            _suspended = false;
            RefreshIcon();
        };
        Unloaded += (_, _) =>
        {
            _suspended = true;
            RefreshIcon();
        };
    }

    private void RefreshIcon()
    {
        if (_source is not null)
            _source.Changed -= RefreshIcon;
        _source = null;
        UIElement? visual = null;
        if (!_suspended)
        {
            if (Icon is IconSource source)
            {
                _source = source;
                source.Changed += RefreshIcon;
                visual = source.CreateIconElement();
            }
            else
                visual = Icon as UIElement;
        }

        SetValue(VisualIconPropertyKey, visual);
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args.Property == ForegroundProperty && VisualIcon is { } visual)
            InvalidateIcon(visual);
    }

    private static void InvalidateIcon(Visual visual)
    {
        if (visual is UIElement element) element.InvalidateVisual();
        else visual.SetRenderDirty();
        for (int index = 0; index < visual.VisualChildrenCount; index++)
            if (visual.GetVisualChild(index)is { } child)
                InvalidateIcon(child);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = base.MeasureOverride(availableSize);
        return VisualIcon is not null ? size : new Size(Math.Min(16, availableSize.Width), Math.Min(16, availableSize.Height));
    }

    protected override void OnRender(DrawingContext context)
    {
        if (RenderSize.Width <= 0 || RenderSize.Height <= 0 || Foreground is not { } foreground || VisualIcon is not null)
            return;
        if (Icon is Geometry geometry)
            DrawGeometryIcon(context, geometry, foreground, 0, new Rect(RenderSize));
        else if (Icon is string glyph && glyph.Length > 0 && FontSize > 0)
        {
            var text = new FormattedText(glyph, "Segoe MDL2 Assets", FontSize)
            {
                Foreground = foreground
            };
            TextMeasurement.MeasureText(text);
            context.DrawText(text, new Point(0, (RenderSize.Height - text.Height) / 2));
        }
        else if (Icon is not UIElement and not IconSource and not string)
            IconRendering.DrawIcon(context, Icon, new Rect(RenderSize), foreground, FontSize);
    }

    private void DrawGeometryIcon(DrawingContext dc, Geometry geometry, Brush foreground, double x, Rect content)
    {
        var bounds = geometry.Bounds;
        var extent = Math.Max(bounds.Width, bounds.Height);
        var size = Math.Min(16, content.Height);
        if (bounds.IsEmpty || extent <= 0 || size <= 0 || !double.IsFinite(extent) || !double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y))
            return;
        var scale = size / extent;
        var left = x + (RenderSize.Width - bounds.Width * scale) / 2;
        var top = content.Y + (content.Height - bounds.Height * scale) / 2;
        dc.PushTransform(new MatrixTransform(new Matrix(scale, 0, 0, scale, left - bounds.X * scale, top - bounds.Y * scale)));
        try
        {
            if (geometry is not PathGeometry path)
            {
                dc.DrawGeometry(foreground, null, geometry);
                return;
            }

            bool open = path.Figures.Any(figure => !figure.IsClosed);
            bool closed = path.Figures.Any(figure => figure.IsClosed);
            var pen = open ? new Pen(foreground, 1.5 / scale) : null;
            if (!open || !closed)
            {
                dc.DrawGeometry(closed ? foreground : null, pen, geometry);
                return;
            }

            // Open figures must stay stroked even when the same icon also
            // contains filled shapes. Leave the caller's geometry unchanged.
            var fill = new PathGeometry
            {
                FillRule = path.FillRule,
                Transform = path.Transform
            };
            var stroke = new PathGeometry
            {
                FillRule = path.FillRule,
                Transform = path.Transform
            };
            foreach (var figure in path.Figures)
                (figure.IsClosed ? fill : stroke).Figures.Add(figure);
            dc.DrawGeometry(foreground, null, fill);
            dc.DrawGeometry(null, pen, stroke);
        }
        finally
        {
            dc.Pop();
        }
    }
}

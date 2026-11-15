using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal sealed partial class CssTableLayout
{
    private readonly Dictionary<FrameworkElement, Action<DependencyProperty, object?, object?>> _captionObservers = [];
    private Border? _captionBorderPainter;

    private void InvalidateOwner()
    {
        for (Visual? current = owner; current is not null; current = current.VisualParent)
            if (current is UIElement element) { element.InvalidateMeasure(); element.InvalidateVisual(); }
    }

    private void ObserveCaptions(Model? model)
    {
        var current = model?.Captions.Select(caption => caption.Element).ToHashSet() ?? [];
        foreach (var old in _captionObservers.Keys.Where(element => !current.Contains(element)).ToArray())
        { old.PropertyChangedInternal -= _captionObservers[old]; _captionObservers.Remove(old); }
        foreach (var element in current.Where(element => !_captionObservers.ContainsKey(element)))
        {
            var weak = new WeakReference<CssTableLayout>(this);
            Action<DependencyProperty, object?, object?> changed = (property, _, _) =>
            {
                if (property.ReadOnly || !weak.TryGetTarget(out var table)) return;
                if (table._computing > 0 && property.Name == "Padding" && !element.HasLocalOrAnimatedValue(property)) return;
                table.InvalidateOwner();
            };
            _captionObservers.Add(element, changed); element.PropertyChangedInternal += changed;
        }
    }

    internal static void CaptionSideChanged(FrameworkElement element)
    {
        if (Assigned(element) is { Caption: true } allocation && allocation.Layout.TryGetTarget(out var layout))
        { layout.InvalidateOwner(); }
        foreach (var child in element.SnapshotAttachedVisualChildren().OfType<FrameworkElement>())
        {
            if (child.HasValueAboveInherited(CssTableProperties.CaptionSideProperty)) continue;
            if (child.CssDisplayMode == CssDisplayMode.Table) { child.InvalidateMeasure(); child.InvalidateVisual(); }
            CaptionSideChanged(child);
        }
    }

    internal static double CaptionHeight(FrameworkElement element) =>
        element.CssDisplayMode == CssDisplayMode.Table && CssDisplayLayout.TryTableFor(element, out var table) ? table?._model?.CaptionHeight ?? 0 : 0;
    internal static Rect? DecorationBox(FrameworkElement element) =>
        element.CssDisplayMode == CssDisplayMode.Table && CssDisplayLayout.TryTableFor(element, out var table) && table?._model is { } model && (model.Captions.Count > 0 || model.Borders is not null)
            ? model.GridBounds : null;

    private static double CaptionMinimum(Model model, double containingWidth)
    {
        var minimum = 0d;
        foreach (var caption in model.Captions)
        {
            caption.Box.MeasureIntrinsicWidth();
            var element = caption.Element;
            var behavior = element.CssPresentedSize(CssLayoutSlotField.Width).SizingBasis;
            var definite = element.HasLocalOrAnimatedValue(FrameworkElement.WidthProperty) ||
                behavior is { IsSet: true, IsAuto: false, IsPercent: false };
            var preferred = definite ? element.CssPreferredWidth(containingWidth) : double.NaN;
            var margins = CssBoxMetrics.Margin(element, 0);
            minimum = Math.Max(minimum, (double.IsFinite(preferred) ? preferred : caption.Box.MinContent.Width) + margins.Left + margins.Right);
        }
        return minimum;
    }

    private static double MeasureCaptions(Model model, double width)
    {
        foreach (var caption in model.Captions)
        {
            var element = caption.Element;
            caption.Margin = CssBoxMetrics.Margin(element, width);
            var available = Math.Max(0, width - caption.Margin.Left - caption.Margin.Right);
            caption.Box.MeasureBorderBox(new(available, double.PositiveInfinity), new(width, double.PositiveInfinity));
            var preferred = element.CssPreferredWidth(width);
            var localAlignment = element.HasLocalOrAnimatedValue(FrameworkElement.HorizontalAlignmentProperty) && element.HorizontalAlignment != HorizontalAlignment.Stretch;
            var usedWidth = double.IsFinite(preferred) || localAlignment ? element.DesiredSize.Width : available;
            var limits = element.CssWidthBounds(width);
            usedWidth = Math.Clamp(usedWidth, limits.Min, limits.Max);
            caption.Box.MeasureBorderBox(new(usedWidth, double.PositiveInfinity), new(width, double.PositiveInfinity));
            var free = width - usedWidth - caption.Margin.Left - caption.Margin.Right;
            var x = caption.Margin.Left;
            if (!element.HasLocalOrAnimatedValue(FrameworkElement.MarginProperty) && element.CssLayout is { HasMargin: true } margin)
            {
                if (margin.MarginLeft.IsAuto) x += Math.Max(0, free) * (margin.MarginRight.IsAuto ? .5 : 1);
                else if (!margin.MarginRight.IsAuto && element.FlowDirection == FlowDirection.RightToLeft) x += free;
            }
            else if (element.FlowDirection == FlowDirection.RightToLeft) x += free;
            if (localAlignment) x = element.HorizontalAlignment switch
            { HorizontalAlignment.Center => caption.Margin.Left + free / 2, HorizontalAlignment.Right => caption.Margin.Left + free, _ => x };
            caption.Bounds = new(x, 0, Math.Max(0, usedWidth), element.DesiredSize.Height);
        }
        double Extent(bool bottom)
        {
            var at = 0d; var pending = default(CssMarginStrut);
            foreach (var caption in model.Captions.Where(caption => caption.Bottom == bottom))
            {
                at += pending.Merge(CssMarginStrut.From(caption.Margin.Top)).Value;
                caption.Bounds = new(caption.Bounds.X, at, caption.Bounds.Width, caption.Bounds.Height);
                at += caption.Bounds.Height; pending = CssMarginStrut.From(caption.Margin.Bottom);
            }
            return at + pending.Value;
        }
        var top = Extent(false); model.CaptionHeight = top + Extent(true); return top;
    }

    private void PlaceCaptions(Model model)
    {
        foreach (var caption in model.Captions)
        {
            var bounds = caption.Bounds;
            if (caption.Bottom) bounds = new(bounds.X, bounds.Y + model.GridBounds.Bottom, bounds.Width, bounds.Height);
            model.Slots[caption.Element] = bounds;
            var state = Assigned(caption.Element);
            if (state is null || !state.Caption || !state.Layout.TryGetTarget(out var previous) || !ReferenceEquals(previous, this))
            {
                s_allocations.Remove(caption.Element); state = new(this, caption.Element, cell: false) { Caption = true };
                s_allocations.Add(caption.Element, state);
            }
            state.Bounds = bounds; _managed.Add(caption.Element); caption.Element.InvalidateArrange();
        }
    }

    private bool PaintGridBackground(DrawingContext context)
    {
        if (_model is not { } model || model.Captions.Count == 0 && model.Borders is null) return false;
        var bounds = model.GridBounds;
        var (border, padding) = CssBoxMetrics.BackgroundInsets(owner, owner.CssLayout?.ContainingWidthCache ?? bounds.Width);
        var radii = CssReplacedContent.Radii(owner, bounds.Size);
        var geometry = new CssRoundedRectangleGeometry(bounds, radii);
        if (owner.BackdropEffect is { HasEffect: true } backdrop && bounds is { Width: > 0, Height: > 0 })
        {
            context.PushClip(geometry);
            try { context.DrawBackdropEffect(bounds, backdrop, new CornerRadius(0)); }
            finally { context.Pop(); }
        }
        var property = CssDependencyPropertyLookup.Find(owner.GetType(), "Background");
        if (property is not null && owner.GetValue(property) is Brush brush)
        {
            void Shape(Brush value) => context.DrawGeometry(value, null, geometry);
            if (!CssBackgroundPainter.TryDraw(owner, property, brush, context, bounds, radii, border, padding, Shape)) Shape(brush);
        }
        else CssBorderImagePainter.Decoration(owner, context, bounds, border);
        return true;
    }

    internal static bool PaintGridBorder(Visual visual, DrawingContext context)
    {
        if (visual is not FrameworkElement element || DecorationBox(element) is not { } bounds || !CssDisplayLayout.TryTableFor(element, out var table)) return false;
        if (table!.PaintCollapsedBorders(context, default)) return true;
        var border = CssBoxMetrics.BackgroundInsets(element, element.CssLayout?.ContainingWidthCache ?? bounds.Width).Border;
        // Background painting already emits the border-image center and edges.
        if (CssBorderImagePainter.Active(element, bounds, border)) return true;
        var colors = CssBorderPaintProperties.Get(element);
        if (colors is null && CssDependencyPropertyLookup.Find(element.GetType(), "BorderBrush") is { } property && element.GetValue(property) is Brush brush)
            colors = new(brush, brush, brush, brush);
        if (colors is null) return true;
        table!._captionBorderPainter ??= new Border();
        table._captionBorderPainter.SetRootDpi(element.DpiScale);
        table._captionBorderPainter.DrawCssSideColorsFor(context, colors, CssBorderStyleProperties.Get(element), bounds, border, CssReplacedContent.Radii(element, bounds.Size));
        return true;
    }

    internal static void PaintGridBeforeChild(Visual visual, Visual? child, DrawingContext context, ref bool background, ref bool border)
    {
        if (visual is not FrameworkElement element || DecorationBox(element) is null || !CssDisplayLayout.TryTableFor(element, out var table) || table?._model is not { } model) return;
        var caption = model.Captions.FirstOrDefault(caption => ReferenceEquals(caption.Element, child));
        if (caption is { Bottom: false }) return;
        if (!background)
        {
            table.PaintGridBackground(context); table.PaintColumns(context, default); background = true;
        }
        if (caption is { Bottom: true } && !border) { PaintGridBorder(visual, context); border = true; }
    }

    internal static Geometry? WrapperClip(Visual visual, Geometry? nativeClip) => CssLineClampLayout.Clip(visual, CssMultiColumnLayout.Clip(visual, TrackClip(visual,
        visual is FrameworkElement element && DecorationBox(element) is not null && element.Clip is null && !element.HasLocalOrAnimatedValue(UIElement.ClipToBoundsProperty) ? null : nativeClip)));

    internal static Geometry? GridChildClip(Visual visual, Visual child)
    {
        if (visual is not FrameworkElement element || element.Clip is not null || element.HasLocalOrAnimatedValue(UIElement.ClipToBoundsProperty) ||
            DecorationBox(element) is not { } box || !CssDisplayLayout.TryTableFor(element, out var table) || table?._model is not { } model ||
            model.Captions.Any(caption => ReferenceEquals(caption.Element, child)) || element.GetLayoutClip() is null) return null;
        var border = CssBoxMetrics.BackgroundInsets(element, element.CssLayout?.ContainingWidthCache ?? box.Width).Border;
        var inner = ControlRenderGeometry.GetContentRect(box, border);
        return new CssRoundedRectangleGeometry(inner, CssReplacedContent.Radii(element, box.Size).Inset(border));
    }

    internal static Visual[]? CaptionPaintChildren(Visual visual)
    {
        if (visual is not FrameworkElement element || DecorationBox(element) is null || !CssDisplayLayout.TryTableFor(element, out var table) || table?._model is not { } model) return null;
        var captions = model.Captions.ToDictionary(caption => (Visual)caption.Element, caption => caption.Bottom ? 2 : 0);
        return Enumerable.Range(0, visual.InternalVisualChildrenCount).Select(visual.InternalGetVisualChild).OfType<Visual>()
            .OrderBy(child => captions.GetValueOrDefault(child, 1)).ToArray();
    }
}

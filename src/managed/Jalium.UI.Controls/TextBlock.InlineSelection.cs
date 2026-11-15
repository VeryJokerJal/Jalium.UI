using System.Runtime.CompilerServices;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

internal static class CssInlineSelectionPainter
{
    private sealed class Group(object layout, IEnumerable<TextBlock> sources, IEnumerable<FrameworkElement> path)
    {
        internal readonly WeakReference<object> Layout = new(layout);
        internal readonly WeakReference<TextBlock>[] Sources = sources.Select(source => new WeakReference<TextBlock>(source)).ToArray();
        internal readonly WeakReference<FrameworkElement>[] Path = path.Distinct().Select(element => new WeakReference<FrameworkElement>(element)).ToArray();
        internal TextBlock[] Current()
            => Layout.TryGetTarget(out var layout) ? Sources.Select(reference => reference.TryGetTarget(out var text) && text.IsInlineSelectionLayout(layout) ? text : null)
                .Where(text => text is not null).Cast<TextBlock>().ToArray() : [];
        internal bool Neutral => Path.All(reference => reference.TryGetTarget(out var element) && IsNeutral(element));
    }
    private static readonly ConditionalWeakTable<FrameworkElement,List<Group>> s_groups = new();
    [ThreadStatic] private static HashSet<TextBlock>? s_suppressed;
    [ThreadStatic] private static Dictionary<TextBlock, Geometry>? s_overlays;
    [ThreadStatic] private static HashSet<TextBlock>? s_shadowSuppressed;
    internal static Geometry? Overlay(TextBlock source) => s_overlays?.GetValueOrDefault(source);
    internal static void SetOverlay(TextBlock source, Geometry area) => (s_overlays ??= [])[source] = area;
    internal static bool Active => s_suppressed is {Count:>0};
    internal static bool Suppressed(TextBlock text) => s_suppressed?.Contains(text) == true;
    internal static bool ShadowsSuppressed(TextBlock text) => s_shadowSuppressed?.Contains(text) == true;
    internal static bool NeedsParagraphPass(Visual owner) => CssFirstLinePainter.HasAllocation(owner) ||
        owner is FrameworkElement element && s_groups.TryGetValue(element, out var groups) && groups.Count > 0;

    internal static void Paragraph(Visual owner, DrawingContext context, Action render)
    {
        // Neutral first-line layers may share selection backgrounds and shadows
        // with the remaining lines. Isolated pseudo effects retain their passes.
        if (CssFirstLinePainter.CanCombineSelection(owner))
            Children(owner, context, () => CssFirstLinePainter.Children(owner, context, render));
        else CssFirstLinePainter.Children(owner, context, render);
    }

    internal static bool IsNeutral(FrameworkElement element)
    {
        if (element.Opacity != 1 || element.RenderTransform is not null || element.LayoutTransform is not null ||
            element.Clip is not null || element.ClipToBounds || element.Effect is IEffect {HasEffect:true} ||
            element.BackdropEffect is {HasEffect:true} || CssMaskProperties.Active(element) ||
            CssCompositingProperties.CreatesGroup(element) || CssBorderRadiusProperties.Get(element) is not null ||
            element.GetValue(CssClipPathProperties.ValueProperty) is CssClipPathValue {Shape:not CssClipShape.None}) return false;
        if (CssDependencyPropertyLookup.Find(element.GetType(),"Background") is { } background && element.GetValue(background) is Brush) return false;
        return CssBorderUsedThicknessProperties.Get(element) == default &&
            (element is not Border border || border.BorderThickness == default);
    }

    internal static void Set(FrameworkElement owner, object layout, IEnumerable<TextBlock> sources,
        IEnumerable<FrameworkElement> path, bool blocked)
    {
        var groups = s_groups.GetValue(owner,static _ => []);
        groups.RemoveAll(group => !group.Layout.TryGetTarget(out var current) || ReferenceEquals(current,layout) || group.Current().Length < 2);
        var text = sources.Distinct().ToArray();
        if (!blocked && text.Length >= 2) groups.Add(new(layout,text,path));
    }

    internal static void Children(Visual visual, DrawingContext context, Action render)
    {
        if (visual is not FrameworkElement owner || !s_groups.TryGetValue(owner,out var groups)) {render();return;}
        var added = new List<TextBlock>();
        var shadowsAdded = new List<TextBlock>();
        Dictionary<TextBlock,Geometry?>? previousOverlays = null;
        try
        {
            foreach (var group in groups)
            {
                if (!group.Neutral) continue;
                var sources = group.Current().Where(text => text.HasInlineSelection && text.Visibility == Visibility.Visible &&
                    CssDisplayProperties.EffectiveVisibility(text) == Visibility.Visible && !CssLineClampLayout.VisuallySuppressed(text) &&
                    !CssDisplayProperties.IsCollapsedFlexItem(text) && !CssContentsLayout.Suppressed(text) &&
                    !Suppressed(text) && !CssFirstLinePainter.Skip(text)).ToArray();
                if (sources.Length < 2) continue;
                var shadows = sources.Any(source => source.HasCssSelectionShadows);
                if (shadows && !sources.Any(source => source.HasCssSelectionStyle || source.NativeSelectionTextBrush is not null)) continue;
                previousOverlays ??= [];
                foreach (var source in sources) previousOverlays.TryAdd(source,Overlay(source));
                if (!TextBlock.PaintInlineSelection(owner,sources,context, shadows
                    ? () => TextBlock.PaintInlineSelectionShadows(owner,sources,context,original:true) : null)) continue;
                if (shadows)
                {
                    TextBlock.PaintInlineSelectionShadows(owner,sources,context,original:false);
                    s_shadowSuppressed ??= [];
                    foreach (var source in sources) if(s_shadowSuppressed.Add(source)) shadowsAdded.Add(source);
                }
                s_suppressed ??= [];
                foreach (var source in sources) if(s_suppressed.Add(source)) added.Add(source);
            }
            render();
        }
        finally
        {
            foreach(var source in added) s_suppressed!.Remove(source);
            foreach(var source in shadowsAdded) s_shadowSuppressed!.Remove(source);
            if (previousOverlays is not null)
                foreach (var (source,previous) in previousOverlays)
                    if (previous is null) s_overlays?.Remove(source); else SetOverlay(source,previous);
        }
    }
}

public partial class TextBlock
{
    internal bool IsInlineSelectionLayout(object layout) => ReferenceEquals(_cssInlineParagraph,layout);
    internal bool HasInlineSelection => this is InlineSourceText source && source.SourceOwner._selectionLength > 0;
    private readonly record struct SelectionRegion(Brush? Brush,Rect Rect,Rect? GlyphBounds = null)
    {
        internal Rect InkBounds => GlyphBounds ?? Rect;
    }

    internal static bool PaintInlineSelection(FrameworkElement owner, TextBlock[] sources, DrawingContext context, Action? beforeBackgrounds = null)
    {
        var offset = context is IOffsetDrawingContext drawing ? drawing.Offset : default;
        var regions = new List<SelectionRegion>();
        foreach (var source in sources.OfType<InlineSourceText>())
        {
            var start = Math.Clamp(source.SourceOwner._selectionStart-source.SourceOffset,0,source.Text.Length);
            var end = Math.Clamp(source.SourceOwner._selectionStart+source.SourceOwner._selectionLength-source.SourceOffset,0,source.Text.Length);
            source._selectionStart = start; source._selectionLength = Math.Max(0,end-start);
            if (source._selectionLength == 0) continue;
            var delta = source.TransformToAncestor(owner);
            var capture = new SelectionRegionContext(regions,offset,source.GetRenderClip().Rect)
                {Offset=new(offset.X+delta.X,offset.Y+delta.Y)};
            if (source.HasCssSelectionStyle)
            {
                foreach (var region in source.SelectionPaintRegions(capture))
                {
                    var brush = region.Style is { AuthorColors: true } style
                        ? CssSelection.Background(style, region.Underlying) : source.ResolveSelectionBrush();
                    var rect = Rect.Intersect(region.Bounds, source.GetRenderClip().Rect);
                    if (rect.Width > 0 && rect.Height > 0) regions.Add(new(brush, new(rect.X + delta.X, rect.Y + delta.Y, rect.Width, rect.Height),
                        new(region.InkBounds.X + delta.X, region.InkBounds.Y + delta.Y, region.InkBounds.Width, region.InkBounds.Height)));
                }
            }
            else source.DrawSelection(capture);
        }
        if (regions.Count == 0) return false;
        beforeBackgrounds?.Invoke();
        if (sources.Any(source => source.HasCssSelectionStyle || source.NativeSelectionTextBrush is not null))
            foreach (var source in sources)
            {
                var delta = source.TransformToAncestor(owner);
                CssInlineSelectionPainter.SetOverlay(source, CssSelectionPainter.Area(regions.Select(region =>
                    new Rect(region.InkBounds.X - delta.X, region.InkBounds.Y - delta.Y, region.InkBounds.Width, region.InkBounds.Height))));
            }
        foreach (var group in regions.GroupBy(region => region.Brush is SolidColorBrush solid
            ? (solid.Color,solid.Opacity,(Brush?)null) : (default(Color),0d,region.Brush)))
        {
            if (group.First().Brush is null) continue;
            var rectangles = new List<Rect>();
            foreach (var region in group.OrderBy(region => region.Rect.Top).ThenBy(region => region.Rect.Left))
            {
                var rect = region.Rect;
                if (rectangles.Count > 0 && rectangles[^1] is var previous &&
                    Math.Abs(previous.Top-rect.Top)<.001 && Math.Abs(previous.Bottom-rect.Bottom)<.001 && rect.Left<=previous.Right+.001)
                    rectangles[^1] = Rect.Union(previous,rect);
                else rectangles.Add(rect);
            }
            if (rectangles.Count == 1)
            {context.DrawRectangle(group.First().Brush,null,rectangles[0]);continue;}
            var geometry = new PathGeometry {FillRule=FillRule.Nonzero};
            foreach (var rect in rectangles) geometry.Figures.Add(CssRoundedRectangleGeometry.Figure(rect,default));
            geometry.Freeze(); context.DrawGeometry(group.First().Brush,null,geometry);
        }
        return true;
    }

    internal static void PaintInlineSelectionShadows(FrameworkElement owner, TextBlock[] sources, DrawingContext context, bool original)
    {
        if (context is not IOffsetDrawingContext offset) return;
        var previous = offset.Offset;
        try
        {
            foreach (var source in sources)
            {
                var delta = source.TransformToAncestor(owner); offset.Offset = new(previous.X + delta.X, previous.Y + delta.Y);
                if (original) CssSelectionPainter.OriginalShadows(context, source.DrawTextLines);
                else CssSelectionPainter.HighlightShadows(context, source.SelectionPaintRegions(context), source.DrawTextLines,
                    (paint, region) => CssSelectionPainter.Decorations(paint, region, source), lineClips: true,
                    wholeInk: source.WholeSelectionInk(), nativeForeground: source.NativeSelectionTextBrush, decorationClip: source.GetRenderClip());
            }
        }
        finally { offset.Offset = previous; }
    }

    private sealed class SelectionRegionContext(List<SelectionRegion> regions,Point ownerOffset,Rect clip) : DrawingContextAdapter,IOffsetDrawingContext
    {
        public Point Offset {get;set;}
        public override void DrawRectangle(Brush? brush,Pen? pen,Rect rect)
        {
            rect = Rect.Intersect(rect,clip);
            if (brush is not null && rect.Width > 0 && rect.Height > 0)
                regions.Add(new(brush,new(rect.X+Offset.X-ownerOffset.X,rect.Y+Offset.Y-ownerOffset.Y,rect.Width,rect.Height)));
        }
        public override void DrawText(FormattedText text,Point origin) { }
        public override void DrawLine(Pen pen,Point a,Point b) { }
        public override void DrawRoundedRectangle(Brush? brush,Pen? pen,Rect rect,double x,double y) { }
        public override void DrawEllipse(Brush? brush,Pen? pen,Point center,double x,double y) { }
        public override void DrawGeometry(Brush? brush,Pen? pen,Geometry geometry) { }
        public override void DrawImage(ImageSource source,Rect rect) { }
        public override void PushClip(Geometry geometry) { }
        public override void PushOpacity(double opacity) { }
        public override void PushTransform(Transform transform) { }
        public override void Pop() { }
        public override void Close() { }
        public override void DrawBackdropEffect(Rect rect,IBackdropEffect effect,CornerRadius radius) { }
    }

    internal sealed partial class CssTextFlowLayout
    {
        private sealed class SelectionGroup
        {
            internal readonly List<TextBlock> Sources = [];
            internal readonly List<FrameworkElement> Path = [];
            internal bool Blocked;
        }

        private void InstallInlineSelectionGroups()
        {
            if (_intrinsic) return;
            var root = new SelectionGroup();
            var groups = _frames.ToDictionary(frame => frame,_ => new SelectionGroup());
            foreach (var source in _sources.Where(source => !source.EmptyFrame))
            {
                var text = source.Element as InlineSourceText;
                var neutral = text is not null;
                var path = new List<FrameworkElement>();
                for (var frame = source.Frame; frame is not null; frame = frame.Parent)
                {
                    var group = groups[frame]; group.Blocked |= !neutral;
                    if (text is not null) group.Sources.Add(text);
                    group.Path.AddRange(path);
                    path.Add(frame.Element); neutral &= CssInlineSelectionPainter.IsNeutral(frame.Element);
                }
                root.Blocked |= !neutral;
                if (text is not null) root.Sources.Add(text);
                root.Path.AddRange(path);
            }
            var floats = _floatEvents.Count > 0 || _floatSpace?.HasFloats == true;
            CssInlineSelectionPainter.Set(owner,this,root.Sources,root.Path,root.Blocked || floats);
            foreach(var (frame,group) in groups)
                CssInlineSelectionPainter.Set(frame.Element,this,group.Sources,group.Path,group.Blocked || floats);
        }
    }
}

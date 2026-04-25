namespace Jalium.UI.Media.Rendering;

/// <summary>
/// Replays a <see cref="Drawing"/> onto a live <see cref="DrawingContext"/>.
/// The observable effect of <c>Replay(drawing, target)</c> is indistinguishable
/// from re-running the original <c>OnRender</c> body against <paramref name="target"/>,
/// except that no user code runs, no temporary brushes / pens / geometries
/// are allocated, and the command list is iterated as a dense array.
/// </summary>
/// <remarks>
/// Replay never invokes the base-class fallback paths on <see cref="DrawingContext"/>
/// that decompose a rounded rectangle or content border into geometry —
/// recording preserved the high-level intent precisely so the target's
/// concrete override (typically the optimized GPU path in
/// <c>RenderTargetDrawingContext</c>) can handle it.
/// </remarks>
internal static class DrawingReplayer
{
    public static void Replay(Drawing drawing, DrawingContext target)
    {
        if (drawing.Count == 0)
        {
            return;
        }

        // AABB short-circuit. When the drawing knows its own recorded
        // bounds AND the target exposes both its current clip bounds and
        // offset, we can skip the entire replay if the element's world
        // bounds no longer intersect the active clip.
        //
        // Coordinate-system reconciliation:
        //   - drawing.Bounds is in the recorder's local "drawing" space
        //     (the same space OnRender uses, i.e. ignoring
        //     IOffsetDrawingContext.Offset). The recorder's transform
        //     stack was applied, but its offsetProxy was not — the offset
        //     is a pure translation applied by the backend when draws are
        //     actually issued.
        //   - target.CurrentClipBounds is in managed world space
        //     ("design coord + accumulated translate"). It returns null
        //     when a non-translate native transform is active, which is
        //     exactly when drawing.Bounds could no longer be safely
        //     translated by Offset alone — so the null guard is also a
        //     correctness guard.
        //   - target.Offset is the pure-translation component the backend
        //     will apply to subsequent draws.
        //
        // Adding target.Offset to drawing.Bounds therefore yields the
        // world rect that replay is about to render into, directly
        // comparable to CurrentClipBounds.
        if (drawing.Bounds.HasValue &&
            target is IClipBoundsDrawingContext clipBoundsTarget &&
            clipBoundsTarget.CurrentClipBounds is Rect clip &&
            target is IOffsetDrawingContext offsetTarget)
        {
            var db = drawing.Bounds.Value;
            if (!db.IsEmpty)
            {
                var offs = offsetTarget.Offset;
                var world = new Rect(db.X + offs.X, db.Y + offs.Y, db.Width, db.Height);
                if (!world.IntersectsWith(clip))
                {
                    return;
                }
            }
        }

        var cmds = drawing.Commands;
        var count = drawing.Count;

        for (int i = 0; i < count; i++)
        {
            ref readonly var c = ref cmds[i];
            switch (c.Kind)
            {
                case DrawCommandKind.DrawLine:
                    target.DrawLine(
                        (Pen)c.A!,
                        new Point(c.V0, c.V1),
                        new Point(c.V2, c.V3));
                    break;

                case DrawCommandKind.DrawRectangle:
                    target.DrawRectangle(
                        (Brush?)c.A,
                        (Pen?)c.B,
                        new Rect(c.V0, c.V1, c.V2, c.V3));
                    break;

                case DrawCommandKind.DrawRoundedRectangle:
                    target.DrawRoundedRectangle(
                        (Brush?)c.A,
                        (Pen?)c.B,
                        new Rect(c.V0, c.V1, c.V2, c.V3),
                        c.V4, c.V5);
                    break;

                case DrawCommandKind.DrawRoundedRectangleCorner:
                    target.DrawRoundedRectangle(
                        (Brush?)c.A,
                        (Pen?)c.B,
                        new Rect(c.V0, c.V1, c.V2, c.V3),
                        new CornerRadius(c.V4, c.V5, c.V6, c.V7));
                    break;

                case DrawCommandKind.DrawContentBorder:
                    target.DrawContentBorder(
                        (Brush?)c.A,
                        (Pen?)c.B,
                        new Rect(c.V0, c.V1, c.V2, c.V3),
                        c.V4, c.V5);
                    break;

                case DrawCommandKind.DrawEllipse:
                    target.DrawEllipse(
                        (Brush?)c.A,
                        (Pen?)c.B,
                        new Point(c.V0, c.V1),
                        c.V2, c.V3);
                    break;

                case DrawCommandKind.DrawPoints:
                    target.DrawPoints(
                        (Brush)c.A!,
                        ((Point[])c.C!).AsSpan(),
                        c.V0);
                    break;

                case DrawCommandKind.DrawLines:
                    target.DrawLines(
                        (Pen)c.A!,
                        ((Point[])c.C!).AsSpan());
                    break;

                case DrawCommandKind.DrawText:
                    target.DrawText(
                        (FormattedText)c.A!,
                        new Point(c.V0, c.V1));
                    break;

                case DrawCommandKind.DrawGeometry:
                    target.DrawGeometry(
                        (Brush?)c.A,
                        (Pen?)c.B,
                        (Geometry)c.C!);
                    break;

                case DrawCommandKind.DrawImage:
                    target.DrawImage(
                        (ImageSource)c.A!,
                        new Rect(c.V0, c.V1, c.V2, c.V3),
                        (BitmapScalingMode)(int)c.V4);
                    break;

                case DrawCommandKind.DrawBackdropEffect:
                    target.DrawBackdropEffect(
                        new Rect(c.V0, c.V1, c.V2, c.V3),
                        (IBackdropEffect)c.A!,
                        new CornerRadius(c.V4, c.V5, c.V6, c.V7));
                    break;

                case DrawCommandKind.PushTransform:
                    target.PushTransform((Transform)c.A!);
                    break;

                case DrawCommandKind.PushClip:
                    target.PushClip((Geometry)c.A!);
                    break;

                case DrawCommandKind.PushOpacity:
                    target.PushOpacity(c.V0);
                    break;

                case DrawCommandKind.Pop:
                    target.Pop();
                    break;

                case DrawCommandKind.PushEffect:
                    target.PushEffect(
                        (IEffect)c.A!,
                        new Rect(c.V0, c.V1, c.V2, c.V3));
                    break;

                case DrawCommandKind.PopEffect:
                    target.PopEffect();
                    break;

                case DrawCommandKind.DrawLiquidGlass:
                    target.DrawLiquidGlass((LiquidGlassParameters)c.A!);
                    break;

            }
        }
    }
}

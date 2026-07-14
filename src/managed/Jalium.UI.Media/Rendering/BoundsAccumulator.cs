using System;
using System.Collections.Generic;

namespace Jalium.UI.Media.Rendering;

/// <summary>
/// Builds the axis-aligned world bounds of a <see cref="RecordedDrawing"/> while
/// <see cref="DrawingRecorder"/> captures draw / push / pop operations.
/// Maintains a transform stack and a clip stack so bounds for draws nested
/// inside <c>PushTransform</c> / <c>PushClip</c> scopes are computed in
/// the same coordinate space that <see cref="DrawingReplayer"/> will
/// replay against.
/// </summary>
/// <remarks>
/// <para>
/// Bounds are optional. Draw commands whose local extent cannot be
/// determined cheaply — most importantly <see cref="FormattedText"/> laid
/// out with <see cref="double.PositiveInfinity"/> wrap constraints — call
/// <see cref="MarkUnknown"/>, after which <see cref="GetBounds"/> returns
/// <c>null</c>. A null bounds disables the replay-time AABB short-circuit
/// but never produces a visual artifact, so this is the correct bail-out
/// for any operation whose true extent cannot be bounded conservatively.
/// </para>
/// <para>
/// Transform composition follows the row-vector convention used by
/// <see cref="Matrix.Transform(Point)"/> (<c>v' = v * M</c>): pushing a
/// transform <c>T</c> on top of current <c>C</c> yields <c>T * C</c>, i.e.
/// <c>T</c> applies first in the local coordinate space. Stacked pushes
/// therefore produce nested local-to-world transforms the same way
/// <c>RenderTargetDrawingContext</c> applies them on the native side.
/// </para>
/// </remarks>
internal sealed class BoundsAccumulator
{
    // Tracks what category each outstanding push belongs to so the single
    // DrawingContext.Pop() entrypoint can undo the right stack.
    private enum PushKind : byte { Transform, Clip, Opacity }

    private readonly Stack<Matrix> _matrixStack = new();
    private readonly Stack<Rect> _clipStack = new();
    private readonly Stack<PushKind> _pushKindStack = new();

    private Matrix _currentMatrix = Matrix.Identity;
    private Rect? _accumulated;
    private bool _unknown;

    /// <summary>
    /// Clears all accumulated state. Called between recordings when a
    /// recorder is returned to the pool.
    /// </summary>
    public void Reset()
    {
        _matrixStack.Clear();
        _clipStack.Clear();
        _pushKindStack.Clear();
        _currentMatrix = Matrix.Identity;
        _accumulated = null;
        _unknown = false;
    }

    /// <summary>
    /// Returns the accumulated axis-aligned world bounds, or <c>null</c>
    /// if any operation marked bounds as unknown (e.g. unbounded text).
    /// </summary>
    public Rect? GetBounds() => _unknown ? null : _accumulated;

    /// <summary>
    /// Declares that bounds for this recording cannot be computed.
    /// Idempotent; further <see cref="AccumulateRect(Rect)"/> calls become
    /// no-ops for bounds purposes.
    /// </summary>
    public void MarkUnknown() => _unknown = true;

    public void PushTransform(Transform transform)
    {
        _matrixStack.Push(_currentMatrix);
        // Row-vector convention: new = T * current, so T applies first in
        // the local coordinate space stacked on top of the existing world
        // transform — same as RenderTargetDrawingContext's native matrix.
        _currentMatrix = Matrix.Multiply(transform.Value, _currentMatrix);
        _pushKindStack.Push(PushKind.Transform);
    }

    public void PushClip(Geometry clipGeometry)
    {
        var worldClip = TransformBounds(_currentMatrix, clipGeometry.Bounds);
        var effective = _clipStack.Count > 0
            ? Rect.Intersect(worldClip, _clipStack.Peek())
            : worldClip;
        _clipStack.Push(effective);
        _pushKindStack.Push(PushKind.Clip);
    }

    public void PushOpacity()
    {
        _pushKindStack.Push(PushKind.Opacity);
    }

    public void Pop()
    {
        if (_pushKindStack.Count == 0)
        {
            return;
        }

        switch (_pushKindStack.Pop())
        {
            case PushKind.Transform:
                if (_matrixStack.Count > 0)
                {
                    _currentMatrix = _matrixStack.Pop();
                }
                break;
            case PushKind.Clip:
                if (_clipStack.Count > 0)
                {
                    _clipStack.Pop();
                }
                break;
            case PushKind.Opacity:
                // Opacity does not affect bounds.
                break;
        }
    }

    /// <summary>
    /// Unions the given local-space rect into the accumulated world bounds,
    /// after applying the current transform and clipping against the
    /// current clip stack. A zero-area or fully-clipped rect contributes
    /// nothing.
    /// </summary>
    public void AccumulateRect(Rect localRect)
    {
        if (_unknown)
        {
            return;
        }
        if (localRect.IsEmpty || localRect.Width <= 0 || localRect.Height <= 0)
        {
            return;
        }

        var world = TransformBounds(_currentMatrix, localRect);

        if (_clipStack.Count > 0)
        {
            world = Rect.Intersect(world, _clipStack.Peek());
            if (world.IsEmpty || world.Width <= 0 || world.Height <= 0)
            {
                return;
            }
        }

        _accumulated = _accumulated.HasValue
            ? Rect.Union(_accumulated.Value, world)
            : world;
    }

    /// <summary>
    /// As <see cref="AccumulateRect(Rect)"/>, with the local rect first
    /// inflated by <paramref name="strokeSlop"/> on each side to account
    /// for pen thickness that extends outside the geometric rect.
    /// </summary>
    public void AccumulateRect(Rect localRect, double strokeSlop)
    {
        if (_unknown)
        {
            return;
        }
        if (strokeSlop > 0)
        {
            localRect = new Rect(
                localRect.X - strokeSlop,
                localRect.Y - strokeSlop,
                localRect.Width + 2 * strokeSlop,
                localRect.Height + 2 * strokeSlop);
        }
        AccumulateRect(localRect);
    }

    /// <summary>
    /// Computes the axis-aligned bounding box of <paramref name="rect"/>
    /// after transformation by <paramref name="m"/>. For identity matrices
    /// the input is returned unchanged.
    /// </summary>
    public static Rect TransformBounds(Matrix m, Rect rect)
    {
        if (m.IsIdentity)
        {
            return rect;
        }

        var p0 = m.Transform(new Point(rect.X, rect.Y));
        var p1 = m.Transform(new Point(rect.Right, rect.Y));
        var p2 = m.Transform(new Point(rect.X, rect.Bottom));
        var p3 = m.Transform(new Point(rect.Right, rect.Bottom));

        var minX = Math.Min(Math.Min(p0.X, p1.X), Math.Min(p2.X, p3.X));
        var minY = Math.Min(Math.Min(p0.Y, p1.Y), Math.Min(p2.Y, p3.Y));
        var maxX = Math.Max(Math.Max(p0.X, p1.X), Math.Max(p2.X, p3.X));
        var maxY = Math.Max(Math.Max(p0.Y, p1.Y), Math.Max(p2.Y, p3.Y));

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }
}

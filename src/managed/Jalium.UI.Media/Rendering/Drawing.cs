namespace Jalium.UI.Media.Rendering;

/// <summary>
/// Immutable, retained-mode snapshot of the draw calls produced by a single
/// <c>OnRender</c> invocation. Produced by <see cref="DrawingRecorder.Commit"/>
/// and replayed by <see cref="DrawingReplayer"/>. Instances are cached on
/// <c>Visual._cachedDrawing</c> and shared across frames until the visual
/// marks itself render-dirty.
/// </summary>
/// <remarks>
/// <para>
/// The command array length equals <see cref="Count"/>; capacity may be
/// larger but the tail is unused. Consumers must respect <see cref="Count"/>
/// when iterating.
/// </para>
/// <para>
/// The Drawing does <em>not</em> deep-copy brushes, pens, geometries, or
/// text. Those are reference-captured. User code that mutates a captured
/// <see cref="Brush"/>'s color between frames will therefore observe the new
/// color on replay without <c>InvalidateVisual</c> — matching the old
/// immediate-mode behaviour of re-reading the brush on every <c>OnRender</c>
/// call. The object-pool pass (later phase) canonicalises color brushes to
/// immutable pooled instances and eliminates this aliasing.
/// </para>
/// </remarks>
public sealed class Drawing
{
    internal readonly DrawCommand[] Commands;
    internal readonly int Count;

    /// <summary>
    /// Cached axis-aligned world bounds of the recorded content. <c>null</c>
    /// means "unknown — treat as infinite and skip culling". Populated by
    /// the culling pass when enabled.
    /// </summary>
    internal Rect? Bounds;

    /// <summary>
    /// False when whole-frame capture encountered content it cannot represent
    /// as a <see cref="DrawCommand"/> (e.g. a windowless WebView transparent
    /// punch, video surface, ink-layer blit, or transition shader reached only
    /// via an <c>is RenderTargetDrawingContext</c> cast that the recorder fails).
    /// The render loop discards such a Drawing and direct-renders the frame so
    /// nothing is silently dropped. Always true for per-visual recordings.
    /// </summary>
    internal readonly bool FullyRecordable;

    internal Drawing(DrawCommand[] commands, int count, Rect? bounds, bool fullyRecordable = true)
    {
        Commands = commands;
        Count = count;
        Bounds = bounds;
        FullyRecordable = fullyRecordable;
    }

    /// <summary>
    /// Shared empty drawing returned for visuals whose <c>OnRender</c>
    /// records no commands. Replaying it is a zero-cost no-op.
    /// </summary>
    public static Drawing Empty { get; } = new(System.Array.Empty<DrawCommand>(), 0, null);

    /// <summary>
    /// Number of recorded commands. Used for diagnostics only; replay
    /// iterates the underlying array directly.
    /// </summary>
    public int CommandCount => Count;

    /// <summary>
    /// False when whole-frame capture hit content that cannot be represented as
    /// draw commands; the caller must discard this Drawing and direct-render the
    /// frame. Always true for per-visual recordings and for <see cref="Empty"/>.
    /// </summary>
    public bool IsFullyRecordable => FullyRecordable;
}

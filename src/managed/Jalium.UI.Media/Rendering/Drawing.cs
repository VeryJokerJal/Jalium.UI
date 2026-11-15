using System.Collections;
using System.Buffers;
using Jalium.UI.Rendering;

namespace Jalium.UI.Media.Rendering;

// 每段约 48 KiB，避免动画每帧的指令快照进入大对象堆并触发全堆回收。
internal readonly struct DrawCommandBuffer : IEnumerable<DrawCommand>
{
    private const int SegmentShift = 9;
    private const int SegmentSize = 1 << SegmentShift;
    private readonly DrawCommand[]? _single;
    private readonly DrawCommand[][]? _segments;
    private readonly bool _pooled;
    private const int MinimumPooledSize = 32;

    internal int Length { get; }

    internal DrawCommandBuffer(DrawCommand[] commands)
    {
        _single = commands;
        _segments = null;
        _pooled = false;
        Length = commands.Length;
    }

    private DrawCommandBuffer(DrawCommand[] commands, int length, bool pooled)
    {
        _single = commands;
        _segments = null;
        _pooled = pooled;
        Length = length;
    }

    private DrawCommandBuffer(DrawCommand[][] segments, int length, bool pooled)
    {
        _single = null;
        _segments = segments;
        _pooled = pooled;
        Length = length;
    }

    internal ref readonly DrawCommand this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Length) throw new IndexOutOfRangeException();
            if (_single is not null) return ref _single[index];
            return ref _segments![index >> SegmentShift][index & (SegmentSize - 1)];
        }
    }

    internal static DrawCommandBuffer CopyFrom(List<DrawCommand> commands)
    {
        if (commands.Count <= SegmentSize)
        {
            if (commands.Count < MinimumPooledSize)
                return new DrawCommandBuffer(commands.ToArray());
            var single = ArrayPool<DrawCommand>.Shared.Rent(commands.Count);
            commands.CopyTo(single);
            return new DrawCommandBuffer(single, commands.Count, pooled: true);
        }

        int count = commands.Count;
        var segments = new DrawCommand[(count + SegmentSize - 1) >> SegmentShift][];
        for (int i = 0, start = 0; i < segments.Length; i++, start += SegmentSize)
        {
            int length = Math.Min(SegmentSize, count - start);
            var segment = length >= MinimumPooledSize
                ? ArrayPool<DrawCommand>.Shared.Rent(length) : new DrawCommand[length];
            commands.CopyTo(start, segment, 0, length);
            segments[i] = segment;
        }
        return new DrawCommandBuffer(segments, count, pooled: true);
    }

    internal void ReturnBuffers()
    {
        if (!_pooled) return;
        if (_single is not null)
        {
            ArrayPool<DrawCommand>.Shared.Return(_single, clearArray: true);
            return;
        }
        for (int i = 0, remaining = Length; remaining > 0; i++, remaining -= SegmentSize)
        {
            if (Math.Min(remaining, SegmentSize) >= MinimumPooledSize)
                ArrayPool<DrawCommand>.Shared.Return(_segments![i], clearArray: true);
        }
    }

    public IEnumerator<DrawCommand> GetEnumerator()
    {
        for (int i = 0; i < Length; i++) yield return this[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Immutable, retained-mode snapshot of the draw calls produced by a single
/// <c>OnRender</c> invocation. Produced by <see cref="DrawingRecorder.Commit"/>
/// and replayed by <see cref="DrawingReplayer"/>. Instances are cached on
/// <c>Visual._cachedDrawing</c> and shared across frames until the visual
/// marks itself render-dirty.
/// </summary>
/// <remarks>
/// <para>
/// Large command snapshots use small immutable segments to avoid allocating
/// a large-object-heap array on every animation frame. Consumers must respect
/// <see cref="Count"/> when iterating.
/// </para>
/// <para>
/// Each visual cache, parent recording, and queued/working frame owns a
/// reference. Replay borrows its root until completion. The last release
/// returns large command buffers to the pool; released drawings must not be
/// read or replayed. Small snapshots keep exact-sized arrays to avoid padding
/// long-lived one-command visual caches to the pool's minimum bucket.
/// Payload snapshotting/canonicalization is performed by the recorder before
/// publication and is independent of command-buffer ownership.
/// </para>
/// </remarks>
internal sealed class RecordedDrawing : IRetainedDrawingLifetime
{
    private int _references = 1;
    private readonly bool _immortal;
    internal readonly DrawCommandBuffer Commands;
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

    internal RecordedDrawing(DrawCommand[] commands, int count, Rect? bounds, bool fullyRecordable = true)
        : this(new DrawCommandBuffer(commands), count, bounds, fullyRecordable)
    {
    }

    internal RecordedDrawing(DrawCommandBuffer commands, int count, Rect? bounds, bool fullyRecordable = true,
        bool ownsNestedReferences = false, bool immortal = false)
    {
        Commands = commands;
        Count = count;
        Bounds = bounds;
        FullyRecordable = fullyRecordable;
        _immortal = immortal;
        if (!ownsNestedReferences)
            for (int i = 0; i < Count; i++)
                if (Commands[i].Kind == DrawCommandKind.DrawRecordedDrawing)
                    DrawingLifetime.Retain(Commands[i].A);
    }

    internal bool IsReleased => !_immortal && Volatile.Read(ref _references) <= 0;

    public void Retain()
    {
        if (_immortal) return;
        int references;
        do
        {
            references = Volatile.Read(ref _references);
            if (references <= 0) throw new ObjectDisposedException(nameof(RecordedDrawing));
        } while (Interlocked.CompareExchange(ref _references, references + 1, references) != references);
    }

    public void Release()
    {
        if (_immortal) return;
        int references = Interlocked.Decrement(ref _references);
        if (references < 0) throw new ObjectDisposedException(nameof(RecordedDrawing));
        if (references != 0) return;
        for (int i = 0; i < Count; i++)
            if (Commands[i].Kind == DrawCommandKind.DrawRecordedDrawing)
                DrawingLifetime.Release(Commands[i].A);
        Commands.ReturnBuffers();
    }

    /// <summary>
    /// Shared empty drawing returned for visuals whose <c>OnRender</c>
    /// records no commands. Replaying it is a zero-cost no-op.
    /// </summary>
    internal static RecordedDrawing Empty { get; } = new(new DrawCommandBuffer(System.Array.Empty<DrawCommand>()),
        0, null, immortal: true);

    /// <summary>
    /// Number of recorded commands. Used for diagnostics only; replay
    /// iterates the command buffer directly.
    /// </summary>
    internal int CommandCount => Count;

    /// <summary>
    /// False when whole-frame capture hit content that cannot be represented as
    /// draw commands; the caller must discard this Drawing and direct-render the
    /// frame. Always true for per-visual recordings and for <see cref="Empty"/>.
    /// </summary>
    internal bool IsFullyRecordable => FullyRecordable;
}

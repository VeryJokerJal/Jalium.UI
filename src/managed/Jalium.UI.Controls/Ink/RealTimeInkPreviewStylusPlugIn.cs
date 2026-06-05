using Jalium.UI.Input;
using Jalium.UI.Input.StylusPlugIns;
using InkStylusPoint = Jalium.UI.Input.StylusPoint;

namespace Jalium.UI.Controls.Ink;

/// <summary>
/// Real-time stylus preview pipeline for <see cref="InkCanvas"/>. Runs on the
/// <see cref="RealTimeStylus"/> background thread (<see cref="StylusPlugIn.IsRealTimeCapable"/> = true)
/// so packet → preview latency does not depend on UI-thread liveness.
///
/// Threading contract:
///   <list type="bullet">
///     <item><b>OnStylusDown/Move/Up</b> — RTS background thread. Only touches
///       <see cref="RealTimePreviewSession"/> state (a lock-protected point
///       buffer + a bounds rect). Never reads DPs, never touches the visual
///       tree, never raises events.</item>
///     <item><b>OnStylusDownProcessed / OnStylusMoveProcessed / OnStylusUpProcessed</b>
///       — UI thread. Reads the canvas DPs to snapshot <see cref="DrawingAttributes"/>
///       (first packet only), invalidates the canvas, and on Up commits the
///       captured points into <see cref="InkCanvas.Strokes"/>.</item>
///   </list>
/// </summary>
internal sealed class RealTimeInkPreviewStylusPlugIn : StylusPlugIn
{
    private readonly InkCanvas _owner;
    private readonly Dictionary<uint, RealTimePreviewSession> _sessions = new();
    private readonly object _gate = new();

    public RealTimeInkPreviewStylusPlugIn(InkCanvas owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        IsRealTimeCapable = true;
    }

    /// <summary>
    /// Active sessions snapshot — only the UI thread should call this (the
    /// returned array is a one-shot copy, but the sessions inside take their
    /// own internal lock when their points are pulled).
    /// </summary>
    internal RealTimePreviewSession[] SnapshotSessions()
    {
        lock (_gate)
        {
            if (_sessions.Count == 0) return Array.Empty<RealTimePreviewSession>();
            var arr = new RealTimePreviewSession[_sessions.Count];
            int i = 0;
            foreach (var s in _sessions.Values) arr[i++] = s;
            return arr;
        }
    }

    /// <summary>True if there is at least one active preview session.</summary>
    internal bool HasActiveSessions
    {
        get { lock (_gate) return _sessions.Count > 0; }
    }

    /// <summary>Drop any in-flight sessions (called when EditingMode changes).</summary>
    internal void Reset()
    {
        lock (_gate) _sessions.Clear();
    }

    /// <summary>
    /// Only handle the editing modes that produce a continuous stroke; the
    /// whole-stroke-erase path is owned by <c>InkCollectionStylusPlugIn</c>.
    /// </summary>
    protected override bool IsActiveForInput(RawStylusInput rawStylusInput)
    {
        var mode = _owner.EditingMode;
        return mode is InkCanvasEditingMode.Ink or InkCanvasEditingMode.EraseByPoint;
    }

    // ── RTS background thread ──────────────────────────────────────────────

    protected override void OnStylusDown(RawStylusInput rawStylusInput)
    {
        var session = new RealTimePreviewSession(rawStylusInput.PointerId);
        session.AppendFromInput(rawStylusInput.GetStylusPoints());
        lock (_gate) _sessions[rawStylusInput.PointerId] = session;
        rawStylusInput.NotifyWhenProcessed(this);
    }

    protected override void OnStylusMove(RawStylusInput rawStylusInput)
    {
        RealTimePreviewSession? session;
        lock (_gate) _sessions.TryGetValue(rawStylusInput.PointerId, out session);
        if (session is null)
        {
            // Down packet may have been filtered out by IsActiveForInput on a
            // mid-stroke EditingMode change; ignore the orphaned Move.
            return;
        }
        session.AppendFromInput(rawStylusInput.GetStylusPoints());
        rawStylusInput.NotifyWhenProcessed(this);
    }

    protected override void OnStylusUp(RawStylusInput rawStylusInput)
    {
        RealTimePreviewSession? session;
        lock (_gate) _sessions.TryGetValue(rawStylusInput.PointerId, out session);
        if (session is null) return;
        session.AppendFromInput(rawStylusInput.GetStylusPoints());
        session.MarkComplete();
        rawStylusInput.NotifyWhenProcessed(this);
    }

    // ── UI thread (Processed) ──────────────────────────────────────────────

    protected override void OnStylusDownProcessed(RawStylusInput rawStylusInput)
    {
        RealTimePreviewSession? session;
        lock (_gate) _sessions.TryGetValue(rawStylusInput.PointerId, out session);
        if (session is null) return;

        // First packet only: capture the canvas attrs / editing mode on the
        // UI thread (DP reads aren't safe on the RTS thread).
        if (session.Attributes is null)
        {
            session.IsEraser = _owner.EditingMode == InkCanvasEditingMode.EraseByPoint;
            session.Attributes = session.IsEraser
                ? _owner.BuildEraserAttributesForRtsPreview()
                : _owner.BuildInkAttributesForRtsPreview();
        }
        _owner.NotifyRealTimePreviewInvalidate(session);
    }

    protected override void OnStylusMoveProcessed(RawStylusInput rawStylusInput)
    {
        RealTimePreviewSession? session;
        lock (_gate) _sessions.TryGetValue(rawStylusInput.PointerId, out session);
        if (session is null) return;
        _owner.NotifyRealTimePreviewInvalidate(session);
    }

    protected override void OnStylusUpProcessed(RawStylusInput rawStylusInput)
    {
        RealTimePreviewSession? session;
        lock (_gate)
        {
            _sessions.Remove(rawStylusInput.PointerId, out session);
        }
        if (session is null) return;
        _owner.CommitRealTimePreviewSession(session);
    }
}

/// <summary>
/// Per-pointer real-time preview state. Point buffer is mutated by the RTS
/// thread (Append*) and read by the UI thread (Snapshot*); both go through
/// the same lock. <see cref="Attributes"/> / <see cref="IsEraser"/> are
/// written exactly once on the UI thread (first Processed callback) before
/// being read by any other UI-thread consumer.
/// </summary>
internal sealed class RealTimePreviewSession
{
    private readonly object _pointsGate = new();
    private readonly List<InkStylusPoint> _points = new(256);
    private Rect _bounds = Rect.Empty;
    private volatile bool _complete;

    public RealTimePreviewSession(uint pointerId) => PointerId = pointerId;

    public uint PointerId { get; }

    /// <summary>UI-thread only. Captured DrawingAttributes for this stroke.</summary>
    public DrawingAttributes? Attributes { get; set; }

    /// <summary>UI-thread only. True when this session is the eraser-by-point variant.</summary>
    public bool IsEraser { get; set; }

    /// <summary>True after <see cref="MarkComplete"/> has been called (StylusUp).</summary>
    public bool IsComplete => _complete;

    /// <summary>
    /// RTS-thread or UI-thread. Append a packet's points and grow the bounds
    /// rect. Skips points that fall within <c>MinDeltaSquared</c> of the
    /// previous one to filter sub-pixel jitter at high sample rates.
    /// </summary>
    public void AppendFromInput(StylusPointCollection packet)
    {
        if (packet is null || packet.Count == 0) return;
        lock (_pointsGate)
        {
            for (int i = 0; i < packet.Count; i++)
            {
                var sp = packet[i];

                if (_points.Count > 0)
                {
                    var last = _points[_points.Count - 1];
                    double dx = sp.X - last.X;
                    double dy = sp.Y - last.Y;
                    if (dx * dx + dy * dy < 0.25) continue; // 0.5 px jitter floor
                }

                _points.Add(sp);

                if (_bounds.IsEmpty)
                    _bounds = new Rect(sp.X, sp.Y, 0, 0);
                else
                    _bounds = _bounds.Union(new Rect(sp.X, sp.Y, 0, 0));
            }
        }
    }

    /// <summary>Marks the session complete (StylusUp). Idempotent.</summary>
    public void MarkComplete() => _complete = true;

    /// <summary>UI-thread snapshot of the current point buffer.</summary>
    public InkStylusPoint[] SnapshotPoints()
    {
        lock (_pointsGate) return _points.ToArray();
    }

    /// <summary>Current point count (thread-safe).</summary>
    public int PointCount
    {
        get { lock (_pointsGate) return _points.Count; }
    }

    /// <summary>UI-thread snapshot of the current stroke bounds.</summary>
    public Rect SnapshotBounds()
    {
        lock (_pointsGate) return _bounds;
    }
}

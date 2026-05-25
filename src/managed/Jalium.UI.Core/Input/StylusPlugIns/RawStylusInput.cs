using System.Collections.Concurrent;

namespace Jalium.UI.Input.StylusPlugIns;

/// <summary>
/// Carries mutable stylus packet data through the stylus plug-in chain.
/// Stylus packets travel two stages: the input stage (potentially on the
/// dedicated <see cref="RealTimeStylus"/> background thread, where plug-ins
/// may mutate the packet) and the processed stage (always on the UI thread,
/// where plug-ins that called <see cref="NotifyWhenProcessed"/> get a chance
/// to react). <see cref="AddCustomData"/> / <see cref="GetCustomData{T}"/>
/// pair the two stages, letting an RTS-thread plug-in pass a result to its
/// own UI-thread processed callback.
/// </summary>
public sealed class RawStylusInput
{
    private readonly Queue<StylusPlugIn> _processedCallbacks = new();
    private readonly HashSet<StylusPlugIn> _processedCallbackSet = new();
    private readonly object _processedCallbacksGate = new();
    private ConcurrentDictionary<Guid, object>? _customData;
    private StylusPointCollection _stylusPoints;

    internal RawStylusInput(
        uint pointerId,
        UIElement target,
        StylusInputAction action,
        StylusPointCollection stylusPoints,
        int timestamp,
        bool inAir,
        bool inRange,
        bool inverted,
        bool barrelButtonPressed,
        bool eraserPressed)
    {
        PointerId = pointerId;
        Target = target;
        Action = action;
        Timestamp = timestamp;
        InAir = inAir;
        InRange = inRange;
        Inverted = inverted;
        IsBarrelButtonPressed = barrelButtonPressed;
        IsEraserPressed = eraserPressed;
        _stylusPoints = new StylusPointCollection(stylusPoints ?? throw new ArgumentNullException(nameof(stylusPoints)));
    }

    public uint PointerId { get; }
    public UIElement Target { get; }
    public StylusInputAction Action { get; }
    public int Timestamp { get; }
    public bool InAir { get; }
    public bool InRange { get; }
    public bool Inverted { get; }
    public bool IsBarrelButtonPressed { get; }
    public bool IsEraserPressed { get; }

    // Volatile because Cancel() may be called on the RTS background thread and
    // read on the UI thread immediately after the worker signals completion.
    // Without an explicit memory barrier the UI thread can observe a stale
    // value and run UI-stage plug-ins even though the RTS stage cancelled.
    private volatile bool _isCanceled;
    public bool IsCanceled => _isCanceled;

    public StylusPointCollection GetStylusPoints()
    {
        lock (_processedCallbacksGate)
        {
            return new(_stylusPoints);
        }
    }

    public void SetStylusPoints(StylusPointCollection stylusPoints)
    {
        ArgumentNullException.ThrowIfNull(stylusPoints);
        lock (_processedCallbacksGate)
        {
            _stylusPoints = new StylusPointCollection(stylusPoints);
        }
    }

    public void Cancel() => _isCanceled = true;

    /// <summary>
    /// Marks <paramref name="stylusPlugIn"/> so its <c>OnStylusXxxProcessed</c>
    /// hook fires on the UI thread once the input-stage chain completes. Safe
    /// to call from the RTS background thread; duplicates are coalesced.
    /// </summary>
    public void NotifyWhenProcessed(StylusPlugIn stylusPlugIn)
    {
        ArgumentNullException.ThrowIfNull(stylusPlugIn);

        lock (_processedCallbacksGate)
        {
            if (_processedCallbackSet.Add(stylusPlugIn))
            {
                _processedCallbacks.Enqueue(stylusPlugIn);
            }
        }
    }

    /// <summary>
    /// Attaches arbitrary state to this packet, keyed by <paramref name="id"/>.
    /// Intended for RTS-thread plug-ins to hand a computed result over to their
    /// UI-thread processed callback. Thread-safe; last writer wins for the same id.
    /// </summary>
    public void AddCustomData(Guid id, object data)
    {
        ArgumentNullException.ThrowIfNull(data);
        (_customData ??= new ConcurrentDictionary<Guid, object>())[id] = data;
    }

    /// <summary>True if a previously stored value exists for <paramref name="id"/>.</summary>
    public bool TryGetCustomData(Guid id, out object? data)
    {
        if (_customData != null && _customData.TryGetValue(id, out var v))
        {
            data = v;
            return true;
        }
        data = null;
        return false;
    }

    /// <summary>Strongly-typed convenience over <see cref="TryGetCustomData"/>.</summary>
    public T? GetCustomData<T>(Guid id) where T : class
        => _customData != null && _customData.TryGetValue(id, out var v) ? v as T : null;

    internal IReadOnlyList<StylusPlugIn> DrainProcessedCallbacks()
    {
        lock (_processedCallbacksGate)
        {
            if (_processedCallbacks.Count == 0)
            {
                return Array.Empty<StylusPlugIn>();
            }

            var result = new List<StylusPlugIn>(_processedCallbacks.Count);
            while (_processedCallbacks.Count > 0)
            {
                result.Add(_processedCallbacks.Dequeue());
            }

            _processedCallbackSet.Clear();
            return result;
        }
    }
}

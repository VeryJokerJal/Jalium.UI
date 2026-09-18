namespace Jalium.UI.Diagnostics;

/// <summary>
/// Per-window ring buffer of frame timings. RenderDebugHud pushes one sample
/// per completed frame; DevTools reads the buffer to render trend curves.
/// </summary>
public sealed class FrameHistory
{
    public readonly struct Sample
    {
        public Sample(double layoutMs, double renderMs, double presentMs, double totalMs, int dirtyElements)
            : this(layoutMs, renderMs, presentMs, totalMs, dirtyElements, 0)
        {
        }

        public Sample(double layoutMs, double renderMs, double presentMs, double totalMs, int dirtyElements, long timestampTicks)
        {
            LayoutMs = layoutMs;
            RenderMs = renderMs;
            PresentMs = presentMs;
            TotalMs = totalMs;
            DirtyElements = dirtyElements;
            TimestampTicks = timestampTicks;
        }

        public double LayoutMs { get; }
        public double RenderMs { get; }
        public double PresentMs { get; }
        public double TotalMs { get; }
        public int DirtyElements { get; }

        /// <summary>
        /// <see cref="System.Diagnostics.Stopwatch"/> timestamp stamped by <see cref="Push"/>
        /// when the frame completed. A consumer needs this to tell "the window is rendering
        /// at N FPS" apart from "the window stopped rendering and this is the last frame it
        /// ever produced" — the ring buffer alone cannot distinguish the two, which is why a
        /// stale average used to sit frozen on screen looking like a live measurement.
        /// Zero only for samples constructed directly by tests.
        /// </summary>
        public long TimestampTicks { get; }
    }

    public const int Capacity = 300;
    private const int InitialCapacity = 8;

    private Sample[] _samples = Array.Empty<Sample>();
    private int _head;
    private int _count;
    private long _totalFrames;
    private readonly object _lock = new();

    public int Count => Volatile.Read(ref _count);
    public long TotalFrames => Interlocked.Read(ref _totalFrames);

    public void Push(Sample sample)
    {
        var stamped = new Sample(
            sample.LayoutMs, sample.RenderMs, sample.PresentMs, sample.TotalMs, sample.DirtyElements,
            System.Diagnostics.Stopwatch.GetTimestamp());

        lock (_lock)
        {
            EnsureCapacityForOneMore();
            _samples[_head] = stamped;
            _head++;
            if (_head == _samples.Length) _head = 0;
            if (_count < Capacity) Volatile.Write(ref _count, _count + 1);
            Interlocked.Increment(ref _totalFrames);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _head = 0;
            Volatile.Write(ref _count, 0);

            // Clearing a long diagnostics session should make its large payload
            // collectible, while a history that only reached the startup buffer can
            // reuse that small allocation on the next Push.
            if (_samples.Length > InitialCapacity)
            {
                _samples = Array.Empty<Sample>();
            }

            Interlocked.Exchange(ref _totalFrames, 0);
        }
    }

    /// <summary>
    /// Copies the samples in chronological order (oldest first) into the provided buffer.
    /// Returns the number of samples copied.
    /// </summary>
    public int CopyTo(Span<Sample> destination)
    {
        lock (_lock)
        {
            int n = Math.Min(_count, destination.Length);
            if (n == 0) return 0;

            int start = _head - _count;
            if (start < 0) start += _samples.Length;

            int firstLength = Math.Min(n, _samples.Length - start);
            _samples.AsSpan(start, firstLength).CopyTo(destination);
            if (firstLength < n)
            {
                _samples.AsSpan(0, n - firstLength).CopyTo(destination[firstLength..]);
            }

            return n;
        }
    }

    private void EnsureCapacityForOneMore()
    {
        int currentCapacity = _samples.Length;
        if (_count < currentCapacity || currentCapacity == Capacity)
        {
            return;
        }

        int newCapacity = currentCapacity == 0
            ? InitialCapacity
            : Math.Min(currentCapacity * 2, Capacity);
        var expanded = new Sample[newCapacity];

        if (_count != 0)
        {
            int start = _head - _count;
            if (start < 0) start += currentCapacity;

            int firstLength = Math.Min(_count, currentCapacity - start);
            _samples.AsSpan(start, firstLength).CopyTo(expanded);
            if (firstLength < _count)
            {
                _samples.AsSpan(0, _count - firstLength).CopyTo(expanded.AsSpan(firstLength));
            }
        }

        _samples = expanded;
        _head = _count;
    }
}

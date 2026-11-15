using System.Collections.Generic;

namespace Jalium.UI;

/// <summary>
/// Arguments for <see cref="Window.FramePresented"/>.
/// </summary>
public sealed class FramePresentedEventArgs : EventArgs
{
    /// <summary>
    /// Creates the event payload.
    /// </summary>
    /// <param name="presentSerial">
    /// Monotonic serial assigned to the successful Present that was retired.
    /// </param>
    /// <param name="timestamp">Environment.TickCount64 observed when the retire was reported.</param>
    public FramePresentedEventArgs(long presentSerial, long timestamp)
    {
        PresentSerial = presentSerial;
        Timestamp = timestamp;
    }

    /// <summary>
    /// Gets the serial of the successful Present that the compositor has retired.
    /// Serials start at 1 and increase by one for every successful Present.
    /// </summary>
    public long PresentSerial { get; }

    /// <summary>
    /// Gets the <see cref="Environment.TickCount64"/> value observed when the
    /// swap-chain waitable reported the retire. This is a CPU-side observation
    /// time, not the display's vertical-blank timestamp.
    /// </summary>
    public long Timestamp { get; }
}

/// <summary>
/// FIFO bookkeeping between successful Present calls and swap-chain retire
/// signals. DXGI does not report which queued frame retired, so one retire
/// signal is paired with the oldest queued Present serial.
/// </summary>
internal sealed class PresentCompletionTracker
{
    private readonly Queue<long> _pending = new();
    private readonly object _gate = new();
    private long _lastQueuedSerial;
    private long _lastPresentedSerial;

    /// <summary>Serial of the most recent successful Present.</summary>
    public long LastQueuedSerial => Volatile.Read(ref _lastQueuedSerial);

    /// <summary>Serial of the most recent Present paired with a retire signal.</summary>
    public long LastPresentedSerial => Volatile.Read(ref _lastPresentedSerial);

    /// <summary>Number of successful Presents still waiting for a retire signal.</summary>
    public int PendingCount
    {
        get
        {
            lock (_gate)
            {
                return _pending.Count;
            }
        }
    }

    /// <summary>Records a successful Present. Non-positive serials are ignored.</summary>
    public void Queue(long serial)
    {
        if (serial <= 0)
        {
            return;
        }

        lock (_gate)
        {
            _pending.Enqueue(serial);
            Volatile.Write(ref _lastQueuedSerial, serial);
        }
    }

    /// <summary>
    /// Pairs one retire signal with the oldest queued Present. Returns the
    /// retired serial, or <see langword="null"/> when no Present was pending.
    /// </summary>
    public long? Retire()
    {
        lock (_gate)
        {
            if (_pending.Count == 0)
            {
                return null;
            }

            long serial = _pending.Dequeue();
            Volatile.Write(ref _lastPresentedSerial, serial);
            return serial;
        }
    }

    /// <summary>Drops pending serials without raising events.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _pending.Clear();
        }
    }
}

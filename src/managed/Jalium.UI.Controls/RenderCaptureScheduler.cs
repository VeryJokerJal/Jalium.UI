namespace Jalium.UI.Rendering;

// 合并录制请求，不合并输入事件。每个渲染线程生命周期拥有独立的调度器。
internal sealed class RenderCaptureScheduler
{
    private const long Active = 1;
    private const long InFlight = 2;
    private const long Pending = 4;
    private const long Flags = Active | InFlight | Pending;
    private const long TicketStep = Flags + 1;
    private long _state = Active;

    internal bool TryBeginCapture(out long ticket)
    {
        ticket = 0;
        while (true)
        {
            long state = Volatile.Read(ref _state);
            if ((state & Active) == 0) return false;
            if ((state & InFlight) != 0)
            {
                if ((state & Pending) != 0 ||
                    Interlocked.CompareExchange(ref _state, state | Pending, state) == state)
                    return false;
                continue;
            }
            long next = ((state & ~Flags) + TicketStep) | Active | InFlight;
            if (Interlocked.CompareExchange(ref _state, next, state) == state)
            {
                ticket = next & ~Flags;
                return true;
            }
        }
    }

    internal bool HasPendingCapture(long ticket)
    {
        long state = Volatile.Read(ref _state);
        return (state & ~Flags) == ticket && (state & (Active | InFlight | Pending)) == (Active | InFlight | Pending);
    }

    internal bool RequestCapture()
    {
        while (true)
        {
            long state = Volatile.Read(ref _state);
            if ((state & Active) == 0) return false;
            if ((state & InFlight) == 0) return true;
            if ((state & Pending) != 0 || Interlocked.CompareExchange(ref _state, state | Pending, state) == state)
                return false;
        }
    }

    internal bool IsAwaitingCapture(long ticket)
    {
        long state = Volatile.Read(ref _state);
        return (state & ~Flags) == ticket && (state & Active) != 0 && (state & InFlight) == 0;
    }

    internal RenderCaptureCompletion Complete(long ticket, bool retryPending)
    {
        while (true)
        {
            long state = Volatile.Read(ref _state);
            if ((state & ~Flags) != ticket || (state & (Active | InFlight)) != (Active | InFlight))
                return RenderCaptureCompletion.Idle;
            if (retryPending && (state & Pending) == 0)
                return RenderCaptureCompletion.Retry;
            long next = state & ~(InFlight | Pending);
            if (Interlocked.CompareExchange(ref _state, next, state) == state)
                return (state & Pending) != 0 ? RenderCaptureCompletion.CaptureRequested : RenderCaptureCompletion.Idle;
        }
    }

    internal void Stop() => Interlocked.Exchange(ref _state, 0);
}

internal enum RenderCaptureCompletion : byte
{
    Idle,
    Retry,
    CaptureRequested,
}

// Worker-owned estimates decide whether CPU recording needs to overlap replay.
// Light frames can request newer input after replay, avoiding an unnecessary
// queued snapshot; expensive frames retain the existing one-capture overlap.
internal struct RenderCaptureTiming
{
    private double _recordMs;
    private double _replayMs;
    private bool _initialized;
    private bool _overlap;
    private bool _phaseInitialized;
    private long _phaseAnchor;
    private long _phaseFrame;
    private long _phaseReplayStart;
    private long _phaseFrequency;
    private int _phaseRefreshRate;

    internal readonly bool OverlapRecording => !_initialized || _overlap;

    internal long GetCaptureDelayMicroseconds(long replayStart, long now, long frequency, int refreshRate)
    {
        if (OverlapRecording || frequency <= 0 || refreshRate <= 0) return 0;
        double period = (double)frequency / refreshRate;
        if (!_phaseInitialized || _phaseFrequency != frequency || _phaseRefreshRate != refreshRate)
        {
            _phaseInitialized = true;
            _phaseAnchor = replayStart;
            _phaseFrame = 0;
            _phaseReplayStart = replayStart;
            _phaseFrequency = frequency;
            _phaseRefreshRate = refreshRate;
        }
        else if (_phaseReplayStart != replayStart)
        {
            _phaseReplayStart = replayStart;
            ++_phaseFrame;
            double phase = _phaseAnchor + _phaseFrame * period;
            double late = replayStart - phase;
            if (late < -period * 0.5)
            {
                // A new presentation cadence must not inherit a future deadline.
                _phaseAnchor = replayStart;
                _phaseFrame = 0;
            }
            else if (late >= period)
            {
                // Skip elapsed periods after a stall without producing a burst
                // of catch-up captures or losing the remaining display phase.
                _phaseFrame += (long)Math.Floor(late / period);
            }
        }
        // Leave a small handoff margin before the predicted next native frame.
        // Waiting belongs to the worker; the UI continues handling input/layout.
        // Keep the refresh grid anchored: a late timer/UI handoff must not move
        // every later frame's deadline by that same lateness.
        double frameMs = 1000.0 / refreshRate;
        double target = _phaseAnchor + (_phaseFrame + 1) * period - (_recordMs + 0.05) * frequency / 1000.0;
        long delay = (long)((target - now) * 1_000_000.0 / frequency);
        return Math.Clamp(delay, 0, (long)(frameMs * 500));
    }

    internal void Observe(long recordTicks, long replayTicks, long frequency, int refreshRate)
    {
        if (recordTicks <= 0 || replayTicks <= 0 || frequency <= 0 || refreshRate <= 0) return;
        double recordMs = recordTicks * (1000.0 / frequency);
        double replayMs = replayTicks * (1000.0 / frequency);
        if (!_initialized)
        {
            _recordMs = recordMs;
            _replayMs = replayMs;
            _overlap = true;
            _initialized = true;
        }
        else
        {
            _recordMs += (recordMs - _recordMs) * 0.125;
            _replayMs += (replayMs - _replayMs) * 0.125;
        }
        double budget = 1000.0 / refreshRate;
        double cost = _recordMs + _replayMs;
        if (_overlap && cost < budget * 0.85) _overlap = false;
        else if (!_overlap && cost > budget * 1.05) _overlap = true;
        if (_overlap) _phaseInitialized = false;
    }
}

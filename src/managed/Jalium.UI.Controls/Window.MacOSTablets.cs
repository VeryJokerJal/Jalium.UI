using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Input;

namespace Jalium.UI;

public partial class Window
{
    private readonly Dictionary<uint, long> _macOSTabletPackets = [];
    private long _macOSTabletPacketSequence;

    private void HandleMacOSTablet(MacOSTabletRequest request)
    {
        // This Window owns promotion and cancellation; native code must never
        // duplicate the packet through its ordinary mouse fallback.
        request.Handled = true;
        var snapshot = request.Snapshot;
        PlatformEvent pointer = snapshot.ToPointerEvent();
        long time = ResolvePointerEventTimeMillis(pointer);
        var data = BuildPointerInputData(pointer, time);
        int timestamp = ToPointerInputTimestamp(time);
        if (pointer.Type == PlatformEventType.PointerCancel)
        {
            _macOSTabletPackets.Remove(pointer.PointerId);
            Action mouseCancel = _inputDispatcher.PrepareMacOSTabletMouseCancellation(pointer.PointerId);
            try { _inputDispatcher.HandleMacOSTabletCancel(data, timestamp); }
            finally { mouseCancel(); }
            return;
        }
        if (!MacOSGestureWindowAvailable) { request.Retire = true; return; }
        var position = data.Position;
        UIElement target = HitTestElement(position, "tablet-route") ?? this;
        var input = ((IInputDispatcherHost)this).CaptureNativeMouseCommandInput(target, null);
        if (input == null) { request.Retire = true; return; }
        long sequence = ++_macOSTabletPacketSequence;
        _macOSTabletPackets[pointer.PointerId] = sequence;
        var platform = _platformWindow;
        nint handle = Handle;
        long lifecycle = _renderLifecycleGeneration;
        var route = new GestureRoute(target, MacOSInputOwner);
        bool SamePacket() => ReferenceEquals(platform, _platformWindow) && Handle == handle &&
            _renderLifecycleGeneration == lifecycle &&
            _macOSTabletPackets.TryGetValue(pointer.PointerId, out long current) && current == sequence;
        bool Current()
        {
            if (!SamePacket()) return false;
            bool eligible = MacOSGestureWindowAvailable && input.IsCurrent() && route.IsCurrent &&
                MacOSInputOwner.IsMacOSInputTargetValid(target);
            return eligible && SamePacket();
        }
        try
        {
            _inputDispatcher.HandleMacOSTabletInput(data,
                pointer.Type == PlatformEventType.PointerDown, pointer.Type == PlatformEventType.PointerUp,
                timestamp, Current, snapshot.MouseType == 0 ? null : () =>
                {
                    long precedingEpoch = _inputDispatcher.MousePressEpoch;
                    OnPlatformEvent(snapshot.ToMouseEvent());
                    _inputDispatcher.TrackMacOSTabletMouse(pointer.PointerId,
                        snapshot.MouseType, snapshot.MouseButton, precedingEpoch);
                });
            if (data.Point.IsInContact && SamePacket() && !Current())
            {
                request.Retire = true;
                _macOSTabletPackets.Remove(pointer.PointerId);
                Action mouseCancel = _inputDispatcher.PrepareMacOSTabletMouseCancellation(pointer.PointerId);
                try { _inputDispatcher.HandleMacOSTabletCancel(data with { IsCanceled = true }, timestamp); }
                finally { mouseCancel(); }
            }
        }
        catch
        {
            if (SamePacket())
            {
                _macOSTabletPackets.Remove(pointer.PointerId);
                Action mouseCancel = _inputDispatcher.PrepareMacOSTabletMouseCancellation(pointer.PointerId);
                try { _inputDispatcher.HandleMacOSTabletCancel(data with { IsCanceled = true }, timestamp); }
                finally { mouseCancel(); }
            }
            throw;
        }
        finally
        {
            if (!data.IsInRange && SamePacket()) _macOSTabletPackets.Remove(pointer.PointerId);
        }
    }
}

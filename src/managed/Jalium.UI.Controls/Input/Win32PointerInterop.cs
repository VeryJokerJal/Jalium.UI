using System.Runtime.InteropServices;
using Jalium.UI.Input;

namespace Jalium.UI.Controls;

internal enum Win32PointerKind
{
    Unknown = 0,
    Touch = 2,
    Pen = 3,
    Mouse = 4
}

internal readonly record struct Win32PointerData(
    uint PointerId,
    Win32PointerKind Kind,
    PointerPoint Point,
    Point Position,
    ModifierKeys Modifiers,
    bool IsInRange,
    bool IsCanceled,
    StylusPointCollection StylusPoints);

internal static class Win32PointerInterop
{
    internal const uint WM_POINTERUPDATE = 0x0245;
    internal const uint WM_POINTERDOWN = 0x0246;
    internal const uint WM_POINTERUP = 0x0247;
    internal const uint WM_POINTERWHEEL = 0x024E;
    internal const uint WM_POINTERHWHEEL = 0x024F;
    internal const uint WM_POINTERCAPTURECHANGED = 0x024C;

    private const uint PT_TOUCH = 0x00000002;
    private const uint PT_PEN = 0x00000003;
    private const uint PT_MOUSE = 0x00000004;

    private const uint POINTER_FLAG_INRANGE = 0x00000002;
    private const uint POINTER_FLAG_INCONTACT = 0x00000004;
    private const uint POINTER_FLAG_FIRSTBUTTON = 0x00000010;
    private const uint POINTER_FLAG_SECONDBUTTON = 0x00000020;
    private const uint POINTER_FLAG_THIRDBUTTON = 0x00000040;
    private const uint POINTER_FLAG_FOURTHBUTTON = 0x00000080;
    private const uint POINTER_FLAG_FIFTHBUTTON = 0x00000100;
    private const uint POINTER_FLAG_PRIMARY = 0x00002000;
    private const uint POINTER_FLAG_CANCELED = 0x00004000;

    private const uint TOUCH_MASK_CONTACTAREA = 0x00000001;
    private const uint TOUCH_MASK_PRESSURE = 0x00000004;

    private const uint PEN_MASK_PRESSURE = 0x00000001;
    private const uint PEN_MASK_ROTATION = 0x00000004;
    private const uint PEN_MASK_TILT_X = 0x00000008;
    private const uint PEN_MASK_TILT_Y = 0x00000010;

    private const uint PEN_FLAG_BARREL = 0x00000001;
    private const uint PEN_FLAG_INVERTED = 0x00000002;
    private const uint PEN_FLAG_ERASER = 0x00000004;

    private const int MK_SHIFT = 0x0004;
    private const int MK_CONTROL = 0x0008;

    private const long MI_WP_SIGNATURE = 0xFF515700;
    private const long MI_WP_SIGNATURE_MASK = unchecked((int)0xFFFFFF00);

    internal static uint GetPointerId(nint wParam)
    {
        return (uint)(wParam.ToInt64() & 0xFFFF);
    }

    internal static bool IsPromotedMouseMessage()
    {
        long extra = GetMessageExtraInfo().ToInt64();
        return (extra & MI_WP_SIGNATURE_MASK) == MI_WP_SIGNATURE;
    }

    internal static bool TryGetPointerData(nint hwnd, nint wParam, double dpiScale, out Win32PointerData data)
    {
        data = default;
        uint pointerId = GetPointerId(wParam);

        if (!GetPointerType(pointerId, out uint pointerType))
            return false;

        if (!GetPointerInfo(pointerId, out POINTER_INFO info))
            return false;

        Win32PointerKind kind = pointerType switch
        {
            PT_TOUCH => Win32PointerKind.Touch,
            PT_PEN => Win32PointerKind.Pen,
            PT_MOUSE => Win32PointerKind.Mouse,
            _ => Win32PointerKind.Unknown
        };

        POINT clientPoint = info.ptPixelLocation;
        _ = ScreenToClient(hwnd, ref clientPoint);
        Point position = new(clientPoint.X / dpiScale, clientPoint.Y / dpiScale);

        PointerPointProperties properties = BuildProperties(kind, info, pointerId, hwnd, dpiScale);
        PointerPoint point = new(
            pointerId,
            position,
            kind switch
            {
                Win32PointerKind.Touch => PointerDeviceType.Touch,
                Win32PointerKind.Pen => PointerDeviceType.Pen,
                _ => PointerDeviceType.Mouse
            },
            (info.pointerFlags & POINTER_FLAG_INCONTACT) != 0,
            properties,
            (ulong)info.dwTime,
            info.frameId);

        data = new Win32PointerData(
            pointerId,
            kind,
            point,
            position,
            GetModifiers(info.dwKeyStates),
            (info.pointerFlags & POINTER_FLAG_INRANGE) != 0,
            (info.pointerFlags & POINTER_FLAG_CANCELED) != 0,
            BuildStylusPoints(kind, pointerId, info, hwnd, dpiScale, position, properties.Pressure));

        return true;
    }

    private static StylusPointCollection BuildStylusPoints(
        Win32PointerKind kind,
        uint pointerId,
        POINTER_INFO info,
        nint hwnd,
        double dpiScale,
        Point fallbackPosition,
        float fallbackPressure)
    {
        // Mouse inputs are represented as a single synthesized point.
        if (kind == Win32PointerKind.Mouse)
        {
            return new StylusPointCollection(new[] { new StylusPoint(fallbackPosition.X, fallbackPosition.Y, fallbackPressure) });
        }

        // Touch: use GetPointerTouchInfoHistory for high-fidelity multi-packet input.
        if (kind == Win32PointerKind.Touch)
        {
            return BuildTouchStylusPoints(pointerId, info, hwnd, dpiScale, fallbackPosition, fallbackPressure);
        }

        // Pen: use GetPointerPenInfoHistory.
        return BuildPenStylusPoints(pointerId, info, hwnd, dpiScale, fallbackPosition, fallbackPressure);
    }

    private static StylusPointCollection BuildTouchStylusPoints(
        uint pointerId,
        POINTER_INFO info,
        nint hwnd,
        double dpiScale,
        Point fallbackPosition,
        float fallbackPressure)
    {
        uint historyCount = Math.Max(1u, info.historyCount);
        var history = new POINTER_TOUCH_INFO[historyCount];
        uint entriesCount = historyCount;

        try
        {
            if (GetPointerTouchInfoHistory(pointerId, ref entriesCount, history) && entriesCount > 0)
            {
                var points = new List<StylusPoint>((int)entriesCount);

                // API returns newest packets first; convert to chronological order.
                for (int i = (int)entriesCount - 1; i >= 0; i--)
                {
                    var packet = history[i];
                    var point = packet.pointerInfo.ptPixelLocation;
                    _ = ScreenToClient(hwnd, ref point);

                    float pressure = (packet.touchMask & TOUCH_MASK_PRESSURE) != 0
                        ? Math.Clamp(packet.pressure / 1024f, 0.0f, 1.0f)
                        : fallbackPressure;

                    points.Add(new StylusPoint(point.X / dpiScale, point.Y / dpiScale, pressure));
                }

                if (points.Count > 0)
                {
                    return new StylusPointCollection(points);
                }
            }
        }
        catch (EntryPointNotFoundException)
        {
            // Older OS/runtime: history API unavailable.
        }

        return new StylusPointCollection(new[] { new StylusPoint(fallbackPosition.X, fallbackPosition.Y, fallbackPressure) });
    }

    private static StylusPointCollection BuildPenStylusPoints(
        uint pointerId,
        POINTER_INFO info,
        nint hwnd,
        double dpiScale,
        Point fallbackPosition,
        float fallbackPressure)
    {
        uint historyCount = Math.Max(1u, info.historyCount);
        var history = new POINTER_PEN_INFO[historyCount];
        uint entriesCount = historyCount;

        try
        {
            if (GetPointerPenInfoHistory(pointerId, ref entriesCount, history) && entriesCount > 0)
            {
                var points = new List<StylusPoint>((int)entriesCount);

                // API returns newest packets first; convert to chronological order.
                for (int i = (int)entriesCount - 1; i >= 0; i--)
                {
                    var packet = history[i];
                    var point = packet.pointerInfo.ptPixelLocation;
                    _ = ScreenToClient(hwnd, ref point);

                    float pressure = (packet.penMask & PEN_MASK_PRESSURE) != 0
                        ? Math.Clamp(packet.pressure / 1024f, 0.0f, 1.0f)
                        : fallbackPressure;

                    points.Add(new StylusPoint(point.X / dpiScale, point.Y / dpiScale, pressure));
                }

                if (points.Count > 0)
                {
                    return new StylusPointCollection(points);
                }
            }
        }
        catch (EntryPointNotFoundException)
        {
            // Older OS/runtime: history API unavailable. Fall back to the current packet.
        }

        return new StylusPointCollection(new[] { new StylusPoint(fallbackPosition.X, fallbackPosition.Y, fallbackPressure) });
    }

    private static ModifierKeys GetModifiers(uint keyStates)
    {
        ModifierKeys modifiers = ModifierKeys.None;
        if ((keyStates & MK_SHIFT) != 0) modifiers |= ModifierKeys.Shift;
        if ((keyStates & MK_CONTROL) != 0) modifiers |= ModifierKeys.Control;
        return modifiers;
    }

    private static PointerPointProperties BuildProperties(Win32PointerKind kind, POINTER_INFO info, uint pointerId, nint hwnd, double dpiScale)
    {
        bool isPrimary = (info.pointerFlags & POINTER_FLAG_PRIMARY) != 0;
        bool left = (info.pointerFlags & POINTER_FLAG_FIRSTBUTTON) != 0;
        bool right = (info.pointerFlags & POINTER_FLAG_SECONDBUTTON) != 0;
        bool middle = (info.pointerFlags & POINTER_FLAG_THIRDBUTTON) != 0;
        bool x1 = (info.pointerFlags & POINTER_FLAG_FOURTHBUTTON) != 0;
        bool x2 = (info.pointerFlags & POINTER_FLAG_FIFTHBUTTON) != 0;

        float pressure = 1.0f;
        float xTilt = 0;
        float yTilt = 0;
        float twist = 0;
        bool barrel = false;
        bool inverted = false;
        bool eraser = false;
        Rect contact = Rect.Empty;

        if (kind == Win32PointerKind.Touch && GetPointerTouchInfo(pointerId, out POINTER_TOUCH_INFO touchInfo))
        {
            if ((touchInfo.touchMask & TOUCH_MASK_PRESSURE) != 0)
            {
                pressure = Math.Clamp(touchInfo.pressure / 1024f, 0.0f, 1.0f);
            }

            if ((touchInfo.touchMask & TOUCH_MASK_CONTACTAREA) != 0)
            {
                POINT tl = new() { X = touchInfo.rcContact.left, Y = touchInfo.rcContact.top };
                POINT br = new() { X = touchInfo.rcContact.right, Y = touchInfo.rcContact.bottom };
                _ = ScreenToClient(hwnd, ref tl);
                _ = ScreenToClient(hwnd, ref br);
                contact = new Rect(
                    tl.X / dpiScale,
                    tl.Y / dpiScale,
                    Math.Max(0, (br.X - tl.X) / dpiScale),
                    Math.Max(0, (br.Y - tl.Y) / dpiScale));
            }
        }
        else if (kind == Win32PointerKind.Pen && GetPointerPenInfo(pointerId, out POINTER_PEN_INFO penInfo))
        {
            if ((penInfo.penMask & PEN_MASK_PRESSURE) != 0)
            {
                pressure = Math.Clamp(penInfo.pressure / 1024f, 0.0f, 1.0f);
            }

            if ((penInfo.penMask & PEN_MASK_ROTATION) != 0)
            {
                twist = penInfo.rotation;
            }

            if ((penInfo.penMask & PEN_MASK_TILT_X) != 0)
            {
                xTilt = penInfo.tiltX;
            }

            if ((penInfo.penMask & PEN_MASK_TILT_Y) != 0)
            {
                yTilt = penInfo.tiltY;
            }

            barrel = (penInfo.penFlags & PEN_FLAG_BARREL) != 0;
            inverted = (penInfo.penFlags & PEN_FLAG_INVERTED) != 0;
            eraser = (penInfo.penFlags & PEN_FLAG_ERASER) != 0;
        }

        return new PointerPointProperties
        {
            Pressure = pressure,
            IsLeftButtonPressed = left,
            IsRightButtonPressed = right,
            IsMiddleButtonPressed = middle,
            IsXButton1Pressed = x1,
            IsXButton2Pressed = x2,
            IsBarrelButtonPressed = barrel,
            IsInverted = inverted,
            IsEraser = eraser,
            IsPrimary = isPrimary,
            ContactRect = contact,
            ContactRectRaw = contact,
            XTilt = xTilt,
            YTilt = yTilt,
            Twist = twist,
            PointerUpdateKind = MapUpdateKind(info.ButtonChangeType)
        };
    }

    private static PointerUpdateKind MapUpdateKind(uint changeType)
    {
        return changeType switch
        {
            1 => PointerUpdateKind.LeftButtonPressed,
            2 => PointerUpdateKind.LeftButtonReleased,
            3 => PointerUpdateKind.RightButtonPressed,
            4 => PointerUpdateKind.RightButtonReleased,
            5 => PointerUpdateKind.MiddleButtonPressed,
            6 => PointerUpdateKind.MiddleButtonReleased,
            7 => PointerUpdateKind.XButton1Pressed,
            8 => PointerUpdateKind.XButton1Released,
            9 => PointerUpdateKind.XButton2Pressed,
            10 => PointerUpdateKind.XButton2Released,
            _ => PointerUpdateKind.Other
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINTER_INFO
    {
        public uint pointerType;
        public uint pointerId;
        public uint frameId;
        public uint pointerFlags;
        public nint sourceDevice;
        public nint hwndTarget;
        public POINT ptPixelLocation;
        public POINT ptHimetricLocation;
        public POINT ptPixelLocationRaw;
        public POINT ptHimetricLocationRaw;
        public uint dwTime;
        public uint historyCount;
        public int InputData;
        public uint dwKeyStates;
        public ulong PerformanceCount;
        public uint ButtonChangeType;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINTER_TOUCH_INFO
    {
        public POINTER_INFO pointerInfo;
        public uint touchFlags;
        public uint touchMask;
        public RECT rcContact;
        public RECT rcContactRaw;
        public uint orientation;
        public uint pressure;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINTER_PEN_INFO
    {
        public POINTER_INFO pointerInfo;
        public uint penFlags;
        public uint penMask;
        public uint pressure;
        public uint rotation;
        public int tiltX;
        public int tiltY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPointerType(uint pointerId, out uint pointerType);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPointerInfo(uint pointerId, out POINTER_INFO pointerInfo);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPointerTouchInfo(uint pointerId, out POINTER_TOUCH_INFO touchInfo);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPointerPenInfo(uint pointerId, out POINTER_PEN_INFO penInfo);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPointerTouchInfoHistory(uint pointerId, ref uint entriesCount, [Out] POINTER_TOUCH_INFO[] touchInfo);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPointerPenInfoHistory(uint pointerId, ref uint entriesCount, [Out] POINTER_PEN_INFO[] penInfo);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(nint hWnd, ref POINT point);

    [DllImport("user32.dll")]
    private static extern nint GetMessageExtraInfo();
}

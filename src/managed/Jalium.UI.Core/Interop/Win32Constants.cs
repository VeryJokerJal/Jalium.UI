// Consolidated Win32 constants — see GitHub issue #151.
//
// These window/message/input/cursor constants were duplicated (identical value and
// type) across the window-hosting controls. They are gathered here once. Consumers
// pull them in with `using static Jalium.UI.Interop.Win32.Win32Constants;` so existing
// unqualified call sites (WM_PAINT, SWP_NOACTIVATE, …) keep working unchanged.
//
// Only constants whose value AND type agreed across every copy live here. Constants
// that disagreed on type (e.g. WM_IME_* declared int in one place / uint in another,
// MK_LBUTTON int vs uint) or on value are intentionally left with their owning feature
// and handled in a later phase. See the issue for the phased plan.

namespace Jalium.UI.Interop.Win32;

internal static class Win32Constants
{
    // ── Window messages (WM_*) ────────────────────────────────────────────
    public const uint WM_DESTROY = 0x0002;
    public const uint WM_PAINT = 0x000F;
    public const uint WM_ERASEBKGND = 0x0014;
    public const uint WM_SETCURSOR = 0x0020;
    public const uint WM_MOUSEACTIVATE = 0x0021;
    public const uint WM_NCHITTEST = 0x0084;
    public const uint WM_MOUSEMOVE = 0x0200;
    public const uint WM_LBUTTONDOWN = 0x0201;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_RBUTTONDOWN = 0x0204;
    public const uint WM_RBUTTONUP = 0x0205;
    public const uint WM_MBUTTONDOWN = 0x0207;
    public const uint WM_MBUTTONUP = 0x0208;
    public const uint WM_MOUSEWHEEL = 0x020A;
    public const uint WM_CAPTURECHANGED = 0x0215;
    public const uint WM_MOUSELEAVE = 0x02A3;

    // IME messages — Window consumes these; InputMethod (Jalium.UI.Input) keeps its own
    // `public const int` copies as part of its IME API surface. Issue #151.
    public const uint WM_IME_STARTCOMPOSITION = 0x010D;
    public const uint WM_IME_ENDCOMPOSITION = 0x010E;
    public const uint WM_IME_COMPOSITION = 0x010F;
    public const uint WM_IME_SETCONTEXT = 0x0281;
    public const uint WM_IME_NOTIFY = 0x0282;
    public const uint WM_IME_CHAR = 0x0286;

    // ── Window styles / extended styles (WS_*) ────────────────────────────
    public const uint WS_POPUP = 0x80000000;
    public const uint WS_EX_TOPMOST = 0x00000008;
    public const uint WS_EX_TOOLWINDOW = 0x00000080;
    public const uint WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    public const uint WS_EX_NOACTIVATE = 0x08000000;

    // ── ShowWindow commands (SW_*) ────────────────────────────────────────
    public const int SW_HIDE = 0;
    // Correct Win32 value is 4 ("show without activating, most-recent size/pos"). Used by
    // PopupWindow and DockIndicatorWindow.
    public const int SW_SHOWNOACTIVATE = 4;
    // 8 = SW_SHOWNA ("show without activating, current size/pos"); used by WindowsFormsHost and
    // Window.cs. Window historically declared this value under the misnomer `SW_SHOWNOACTIVATE = 8`
    // (that name is really 4); its call sites now call SW_SHOWNA, so the runtime value is
    // unchanged. See issue #151.
    public const int SW_SHOWNA = 8;

    // ── SetWindowPos flags / insert-after handles (SWP_*, HWND_*) ─────────
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_NOOWNERZORDER = 0x0200;
    public static readonly nint HWND_TOPMOST = -1;

    // ── GetWindowLong indices (GWL_*) ─────────────────────────────────────
    public const int GWL_STYLE = -16;

    // ── Hit-test codes (HT*) ──────────────────────────────────────────────
    public const int HTCLIENT = 1;

    // ── Mouse-activate return codes (MA_*) ────────────────────────────────
    public const nint MA_NOACTIVATE = 3;

    // ── Mouse key state flags (MK_*) ──────────────────────────────────────
    // int (matching the window controls' `int flags = (int)wParam`); the drag/drop code that
    // used a uint copy now casts at its single bitwise test site. Issue #151.
    public const int MK_LBUTTON = 0x0001;
    public const int MK_RBUTTON = 0x0002;
    public const int MK_MBUTTON = 0x0010;
    public const int MK_SHIFT = 0x0004;
    public const int MK_CONTROL = 0x0008;
    public const int MK_XBUTTON1 = 0x0020;
    public const int MK_XBUTTON2 = 0x0040;

    // ── Virtual keys (VK_*) ───────────────────────────────────────────────
    public const int VK_SHIFT = 0x10;
    public const int VK_CONTROL = 0x11;
    public const int VK_MENU = 0x12;
    public const int VK_ESCAPE = 0x1B;

    // ── Standard cursor ids (IDC_*) ───────────────────────────────────────
    public const nint IDC_ARROW = 32512;
    public const nint IDC_IBEAM = 32513;
    public const nint IDC_WAIT = 32514;
    public const nint IDC_CROSS = 32515;
    public const nint IDC_UPARROW = 32516;
    public const nint IDC_SIZENWSE = 32642;
    public const nint IDC_SIZENESW = 32643;
    public const nint IDC_SIZEWE = 32644;
    public const nint IDC_SIZENS = 32645;
    public const nint IDC_SIZEALL = 32646;
    public const nint IDC_NO = 32648;
    public const nint IDC_HAND = 32649;
    public const nint IDC_APPSTARTING = 32650;
    public const nint IDC_HELP = 32651;

    // ── Monitor / system-color / mouse-tracking ───────────────────────────
    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const int COLOR_WINDOW = 5;
    public const uint TME_LEAVE = 0x00000002;

    // ── Window-chrome / DWM / hit-test / system-menu constants (moved from Window.cs, issue #151) ──
    public const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
    public const uint WS_CAPTION = 0x00C00000;
    public const uint WS_THICKFRAME = 0x00040000;
    public const uint WS_MINIMIZEBOX = 0x00020000;
    public const uint WS_MAXIMIZEBOX = 0x00010000;
    public const uint WS_SYSMENU = 0x00080000;
    public const uint WS_EX_APPWINDOW = 0x00040000;
    public const uint WS_EX_LAYERED = 0x00080000;
    public const uint LWA_ALPHA = 0x02;
    public const int GWL_EXSTYLE = -20;
    public const uint SWP_FRAMECHANGED = 0x0020;
    public const uint SWP_NOMOVE = 0x0002;
    public static readonly nint HWND_NOTOPMOST = new(-2);
    public static readonly nint HWND_TOP = nint.Zero;
    public const int CW_USEDEFAULT = unchecked((int)0x80000000);
    public const int SW_SHOW = 5;
    public const int SW_MINIMIZE = 6;
    public const int SW_MAXIMIZE = 3;
    public const int SW_RESTORE = 9;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_GETOBJECT = 0x003D;
    public const uint WM_GETMINMAXINFO = 0x0024;
    public const uint WM_QUERYENDSESSION = 0x0011;
    public const uint WM_ENDSESSION = 0x0016;
    public const uint WM_SETTINGCHANGE = 0x001A;
    public const uint WM_THEMECHANGED = 0x031A;
    public const uint WM_MOVE = 0x0003;
    public const uint WM_SIZE = 0x0005;
    public const uint WM_NCCALCSIZE = 0x0083;
    public const int HTNOWHERE = 0;
    public const int HTCAPTION = 2;
    public const int HTSYSMENU = 3;
    public const int HTMINBUTTON = 8;
    public const int HTMAXBUTTON = 9;
    public const int HTLEFT = 10;
    public const int HTRIGHT = 11;
    public const int HTTOP = 12;
    public const int HTTOPLEFT = 13;
    public const int HTTOPRIGHT = 14;
    public const int HTBOTTOM = 15;
    public const int HTBOTTOMLEFT = 16;
    public const int HTBOTTOMRIGHT = 17;
    public const int HTCLOSE = 20;
    public const int SC_SIZE = 0xF000;
    public const int SC_MOVE = 0xF010;
    public const int SC_MINIMIZE = 0xF020;
    public const int SC_MAXIMIZE = 0xF030;
    public const int SC_CLOSE = 0xF060;
    public const int SC_RESTORE = 0xF120;
    public const uint TPM_RETURNCMD = 0x0100;
    public const uint TPM_LEFTBUTTON = 0x0000;
    public const uint MF_BYCOMMAND = 0x00000000;
    public const uint MF_ENABLED = 0x00000000;
    public const uint MF_GRAYED = 0x00000001;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWCP_DEFAULT = 0;
    public const int DWMWCP_DONOTROUND = 1;
    public const int DWMWCP_ROUND = 2;
    public const int DWMWCP_ROUNDSMALL = 3;
    public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    public const int DWMWA_CAPTION_BUTTON_BOUNDS = 5;
    public const int DWMSBT_AUTO = 0;
    public const int DWMSBT_NONE = 1;
    public const int DWMSBT_MAINWINDOW = 2;
    public const int DWMSBT_TRANSIENTWINDOW = 3;
    public const int DWMSBT_TABBEDWINDOW = 4;
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int WCA_ACCENT_POLICY = 19;
    public const int ACCENT_DISABLED = 0;
    public const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;
    public const uint WM_MOVING = 0x0216;
    public const uint WM_SIZING = 0x0214;
    public const uint WM_DPICHANGED = 0x02E0;
    public const uint WM_ENTERSIZEMOVE = 0x0231;
    public const uint WM_EXITSIZEMOVE = 0x0232;
    public const uint CS_HREDRAW = 0x0002;
    public const uint CS_VREDRAW = 0x0001;
    public const nint IDC_SIZE = 32640;
    public const uint RDW_INVALIDATE = 0x0001;
    public const uint RDW_UPDATENOW = 0x0100;
    public const uint WM_USER = 0x0400;
    public const uint WM_APP_REPAINT = WM_USER + 1;
    public const int SIZE_RESTORED = 0;
    public const int SIZE_MINIMIZED = 1;
    public const int SIZE_MAXIMIZED = 2;
    public const uint WM_KEYDOWN = 0x0100;
    public const uint WM_KEYUP = 0x0101;
    public const uint WM_CHAR = 0x0102;
    public const uint WM_SYSKEYDOWN = 0x0104;
    public const uint WM_SYSKEYUP = 0x0105;
    public const uint WM_LBUTTONDBLCLK = 0x0203;
    public const uint WM_RBUTTONDBLCLK = 0x0206;
    public const uint WM_MBUTTONDBLCLK = 0x0209;
    public const uint WM_XBUTTONDOWN = 0x020B;
    public const uint WM_XBUTTONUP = 0x020C;
    public const uint WM_CANCELMODE = 0x001F;
    public const uint WM_ACTIVATE = 0x0006;
    public const uint WM_ACTIVATEAPP = 0x001C;
    public const uint WM_SETFOCUS = 0x0007;
    public const uint WM_KILLFOCUS = 0x0008;
    public const int WA_INACTIVE = 0;
    public const uint WM_NCMOUSEMOVE = 0x00A0;
    public const uint WM_NCMOUSEHOVER = 0x02A0;
    public const uint WM_NCLBUTTONDOWN = 0x00A1;
    public const uint WM_NCLBUTTONUP = 0x00A2;
    public const uint WM_NCLBUTTONDBLCLK = 0x00A3;
    public const uint WM_NCRBUTTONDOWN = 0x00A4;
    public const uint WM_NCRBUTTONUP = 0x00A5;
    public const uint WM_SYSCOMMAND = 0x0112;
    public const uint WM_NCMOUSELEAVE = 0x02A2;
    public const uint TME_HOVER = 0x00000001;
    public const uint TME_NONCLIENT = 0x00000010;
    public const uint HOVER_DEFAULT = 0xFFFFFFFF;
    public const int HTCLIENT_SETCURSOR = 1;
    public const int IACE_DEFAULT = 0x0010;
    public const int VK_LWIN = 0x5B;
    public const int VK_RWIN = 0x5C;
    public const uint WM_DISPLAYCHANGE = 0x007E;
    public const int ENUM_CURRENT_SETTINGS = -1;
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
}

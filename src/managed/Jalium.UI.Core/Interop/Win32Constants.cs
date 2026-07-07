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
    // 8 = SW_SHOWNA ("show without activating, current size/pos"); used by WindowsFormsHost.
    // NOTE: Window.cs still keeps a local `SW_SHOWNOACTIVATE = 8` — a historical misnomer (the
    // name says NOACTIVATE, which is 4, but the value is actually SW_SHOWNA, 8). It is left
    // untouched for now to avoid any behavior change; when Window.cs's constants are folded in
    // it should call SW_SHOWNA instead. See issue #151.
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

    // ── Mouse key state flags (MK_*) — only the type-consistent subset ────
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
}

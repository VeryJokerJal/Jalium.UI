#ifdef _WIN32

#ifndef UNICODE
#define UNICODE
#endif
#ifndef _UNICODE
#define _UNICODE
#endif

#include "jalium_platform.h"

#include <Windows.h>
#include <ShellScalingApi.h>
#include <windowsx.h>
#include <imm.h>

#include <algorithm>
#include <atomic>
#include <cstring>
#include <mutex>
#include <string>
#include <vector>

#pragma comment(lib, "user32.lib")
#pragma comment(lib, "gdi32.lib")
#pragma comment(lib, "shcore.lib")
#pragma comment(lib, "dwmapi.lib")
#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "imm32.lib")

static_assert(sizeof(wchar_t) == sizeof(JaliumUtf16Char),
              "Windows wchar_t must be a UTF-16 code unit");

static const wchar_t* AsWideString(const JaliumUtf16Char* text)
{
    return reinterpret_cast<const wchar_t*>(text);
}

// ============================================================================
// Win32 Window Implementation
// ============================================================================

static const wchar_t* kWindowClassName = L"JaliumPlatformWindow";
static std::atomic<bool> g_classRegistered{false};
static std::atomic<int32_t> g_exitCode{0};
static std::atomic<bool> g_quitRequested{false};

struct JaliumPlatformWindow {
    HWND                hwnd = nullptr;
    JaliumEventCallback callback = nullptr;
    void*               userData = nullptr;
    uint32_t            style = 0;
    float               dpiScale = 1.0f;
    bool                destroyed = false;

    void DispatchEvent(const JaliumPlatformEvent& evt)
    {
        if (callback && !destroyed)
        {
            callback(&evt, userData);
        }
    }
};

// ============================================================================
// Key Mapping
// ============================================================================

static int32_t GetModifiers()
{
    int32_t mods = JALIUM_MOD_NONE;
    if (GetKeyState(VK_SHIFT) & 0x8000)   mods |= JALIUM_MOD_SHIFT;
    if (GetKeyState(VK_CONTROL) & 0x8000)  mods |= JALIUM_MOD_CTRL;
    if (GetKeyState(VK_MENU) & 0x8000)     mods |= JALIUM_MOD_ALT;
    if (GetKeyState(VK_LWIN) & 0x8000 || GetKeyState(VK_RWIN) & 0x8000)
        mods |= JALIUM_MOD_META;
    if (GetKeyState(VK_CAPITAL) & 0x0001)  mods |= JALIUM_MOD_CAPS;
    if (GetKeyState(VK_NUMLOCK) & 0x0001)  mods |= JALIUM_MOD_NUM;
    return mods;
}

// ============================================================================
// DPI Helpers
// ============================================================================

typedef UINT (WINAPI *PFN_GetDpiForWindow)(HWND);
static PFN_GetDpiForWindow pfnGetDpiForWindow = nullptr;

static float GetWindowDpiScale(HWND hwnd)
{
    if (pfnGetDpiForWindow)
    {
        UINT dpi = pfnGetDpiForWindow(hwnd);
        return static_cast<float>(dpi) / 96.0f;
    }

    // Fallback: per-monitor via Shcore
    HMONITOR hMon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
    UINT dpiX = 96, dpiY = 96;
    if (SUCCEEDED(GetDpiForMonitor(hMon, MDT_EFFECTIVE_DPI, &dpiX, &dpiY)))
    {
        return static_cast<float>(dpiX) / 96.0f;
    }
    return 1.0f;
}

// ============================================================================
// WndProc
// ============================================================================

static LRESULT CALLBACK WndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
    JaliumPlatformWindow* win = reinterpret_cast<JaliumPlatformWindow*>(
        GetWindowLongPtrW(hwnd, GWLP_USERDATA));

    if (!win)
    {
        if (msg == WM_NCCREATE)
        {
            auto cs = reinterpret_cast<CREATESTRUCTW*>(lParam);
            win = reinterpret_cast<JaliumPlatformWindow*>(cs->lpCreateParams);
            SetWindowLongPtrW(hwnd, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(win));
            win->hwnd = hwnd;
            win->dpiScale = GetWindowDpiScale(hwnd);
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    JaliumPlatformEvent evt{};
    evt.window = win;

    switch (msg)
    {
    case WM_CLOSE:
        evt.type = JALIUM_EVENT_CLOSE_REQUESTED;
        win->DispatchEvent(evt);
        return 0; // Managed code decides whether to destroy

    case WM_DESTROY:
        evt.type = JALIUM_EVENT_DESTROYED;
        win->DispatchEvent(evt);
        win->destroyed = true;
        return 0;

    case WM_SIZE:
    {
        evt.type = JALIUM_EVENT_RESIZE;
        evt.resize.width = LOWORD(lParam);
        evt.resize.height = HIWORD(lParam);
        win->DispatchEvent(evt);

        // Window state changes
        JaliumPlatformEvent stateEvt{};
        stateEvt.type = JALIUM_EVENT_STATE_CHANGED;
        stateEvt.window = win;
        if (wParam == SIZE_MINIMIZED)
            stateEvt.stateChanged.newState = JALIUM_WINDOW_STATE_MINIMIZED;
        else if (wParam == SIZE_MAXIMIZED)
            stateEvt.stateChanged.newState = JALIUM_WINDOW_STATE_MAXIMIZED;
        else
            stateEvt.stateChanged.newState = JALIUM_WINDOW_STATE_NORMAL;
        win->DispatchEvent(stateEvt);
        return 0;
    }

    case WM_MOVE:
        evt.type = JALIUM_EVENT_MOVE;
        evt.move.x = static_cast<int16_t>(LOWORD(lParam));
        evt.move.y = static_cast<int16_t>(HIWORD(lParam));
        win->DispatchEvent(evt);
        return 0;

    case WM_DPICHANGED:
    {
        UINT dpiX = HIWORD(wParam);
        UINT dpiY = LOWORD(wParam);
        win->dpiScale = static_cast<float>(dpiX) / 96.0f;

        auto suggested = reinterpret_cast<RECT*>(lParam);
        evt.type = JALIUM_EVENT_DPI_CHANGED;
        evt.dpiChanged.dpiX = static_cast<float>(dpiX);
        evt.dpiChanged.dpiY = static_cast<float>(dpiY);
        evt.dpiChanged.suggestedX = suggested->left;
        evt.dpiChanged.suggestedY = suggested->top;
        evt.dpiChanged.suggestedWidth = suggested->right - suggested->left;
        evt.dpiChanged.suggestedHeight = suggested->bottom - suggested->top;
        win->DispatchEvent(evt);

        SetWindowPos(hwnd, nullptr,
                     suggested->left, suggested->top,
                     suggested->right - suggested->left,
                     suggested->bottom - suggested->top,
                     SWP_NOZORDER | SWP_NOACTIVATE);
        return 0;
    }

    case WM_PAINT:
    {
        evt.type = JALIUM_EVENT_PAINT;
        win->DispatchEvent(evt);
        ValidateRect(hwnd, nullptr);
        return 0;
    }

    case WM_ACTIVATE:
        evt.type = (LOWORD(wParam) != WA_INACTIVE)
                   ? JALIUM_EVENT_ACTIVATE : JALIUM_EVENT_DEACTIVATE;
        win->DispatchEvent(evt);
        return 0;

    case WM_SETFOCUS:
        evt.type = JALIUM_EVENT_FOCUS_GAINED;
        win->DispatchEvent(evt);
        return 0;

    case WM_KILLFOCUS:
        evt.type = JALIUM_EVENT_FOCUS_LOST;
        win->DispatchEvent(evt);
        return 0;

    // ---- Mouse ----
    case WM_MOUSEMOVE:
        evt.type = JALIUM_EVENT_MOUSE_MOVE;
        evt.mouse.x = static_cast<float>(GET_X_LPARAM(lParam));
        evt.mouse.y = static_cast<float>(GET_Y_LPARAM(lParam));
        evt.mouse.modifiers = GetModifiers();
        win->DispatchEvent(evt);
        return 0;

    case WM_LBUTTONDOWN:
    case WM_LBUTTONDBLCLK:
        SetCapture(hwnd);
        evt.type = JALIUM_EVENT_MOUSE_DOWN;
        evt.mouse.x = static_cast<float>(GET_X_LPARAM(lParam));
        evt.mouse.y = static_cast<float>(GET_Y_LPARAM(lParam));
        evt.mouse.button = JALIUM_MOUSE_BUTTON_LEFT;
        evt.mouse.modifiers = GetModifiers();
        evt.mouse.clickCount = (msg == WM_LBUTTONDBLCLK) ? 2 : 1;
        win->DispatchEvent(evt);
        return 0;

    case WM_LBUTTONUP:
        ReleaseCapture();
        evt.type = JALIUM_EVENT_MOUSE_UP;
        evt.mouse.x = static_cast<float>(GET_X_LPARAM(lParam));
        evt.mouse.y = static_cast<float>(GET_Y_LPARAM(lParam));
        evt.mouse.button = JALIUM_MOUSE_BUTTON_LEFT;
        evt.mouse.modifiers = GetModifiers();
        win->DispatchEvent(evt);
        return 0;

    case WM_RBUTTONDOWN:
    case WM_RBUTTONDBLCLK:
        SetCapture(hwnd);
        evt.type = JALIUM_EVENT_MOUSE_DOWN;
        evt.mouse.x = static_cast<float>(GET_X_LPARAM(lParam));
        evt.mouse.y = static_cast<float>(GET_Y_LPARAM(lParam));
        evt.mouse.button = JALIUM_MOUSE_BUTTON_RIGHT;
        evt.mouse.modifiers = GetModifiers();
        evt.mouse.clickCount = (msg == WM_RBUTTONDBLCLK) ? 2 : 1;
        win->DispatchEvent(evt);
        return 0;

    case WM_RBUTTONUP:
        ReleaseCapture();
        evt.type = JALIUM_EVENT_MOUSE_UP;
        evt.mouse.x = static_cast<float>(GET_X_LPARAM(lParam));
        evt.mouse.y = static_cast<float>(GET_Y_LPARAM(lParam));
        evt.mouse.button = JALIUM_MOUSE_BUTTON_RIGHT;
        evt.mouse.modifiers = GetModifiers();
        win->DispatchEvent(evt);
        return 0;

    case WM_MBUTTONDOWN:
    case WM_MBUTTONDBLCLK:
        SetCapture(hwnd);
        evt.type = JALIUM_EVENT_MOUSE_DOWN;
        evt.mouse.x = static_cast<float>(GET_X_LPARAM(lParam));
        evt.mouse.y = static_cast<float>(GET_Y_LPARAM(lParam));
        evt.mouse.button = JALIUM_MOUSE_BUTTON_MIDDLE;
        evt.mouse.modifiers = GetModifiers();
        evt.mouse.clickCount = (msg == WM_MBUTTONDBLCLK) ? 2 : 1;
        win->DispatchEvent(evt);
        return 0;

    case WM_MBUTTONUP:
        ReleaseCapture();
        evt.type = JALIUM_EVENT_MOUSE_UP;
        evt.mouse.x = static_cast<float>(GET_X_LPARAM(lParam));
        evt.mouse.y = static_cast<float>(GET_Y_LPARAM(lParam));
        evt.mouse.button = JALIUM_MOUSE_BUTTON_MIDDLE;
        evt.mouse.modifiers = GetModifiers();
        win->DispatchEvent(evt);
        return 0;

    case WM_XBUTTONDOWN:
    case WM_XBUTTONDBLCLK:
        SetCapture(hwnd);
        evt.type = JALIUM_EVENT_MOUSE_DOWN;
        evt.mouse.x = static_cast<float>(GET_X_LPARAM(lParam));
        evt.mouse.y = static_cast<float>(GET_Y_LPARAM(lParam));
        evt.mouse.button = (GET_XBUTTON_WPARAM(wParam) == XBUTTON1)
                           ? JALIUM_MOUSE_BUTTON_X1 : JALIUM_MOUSE_BUTTON_X2;
        evt.mouse.modifiers = GetModifiers();
        evt.mouse.clickCount = (msg == WM_XBUTTONDBLCLK) ? 2 : 1;
        win->DispatchEvent(evt);
        return TRUE;

    case WM_XBUTTONUP:
        ReleaseCapture();
        evt.type = JALIUM_EVENT_MOUSE_UP;
        evt.mouse.x = static_cast<float>(GET_X_LPARAM(lParam));
        evt.mouse.y = static_cast<float>(GET_Y_LPARAM(lParam));
        evt.mouse.button = (GET_XBUTTON_WPARAM(wParam) == XBUTTON1)
                           ? JALIUM_MOUSE_BUTTON_X1 : JALIUM_MOUSE_BUTTON_X2;
        evt.mouse.modifiers = GetModifiers();
        win->DispatchEvent(evt);
        return TRUE;

    case WM_MOUSEWHEEL:
        evt.type = JALIUM_EVENT_MOUSE_WHEEL;
        {
            POINT pt = {GET_X_LPARAM(lParam), GET_Y_LPARAM(lParam)};
            ScreenToClient(hwnd, &pt);
            evt.wheel.x = static_cast<float>(pt.x);
            evt.wheel.y = static_cast<float>(pt.y);
        }
        evt.wheel.deltaX = 0.0f;
        evt.wheel.deltaY = static_cast<float>(GET_WHEEL_DELTA_WPARAM(wParam)) / WHEEL_DELTA;
        evt.wheel.modifiers = GetModifiers();
        win->DispatchEvent(evt);
        return 0;

    case WM_MOUSEHWHEEL:
        evt.type = JALIUM_EVENT_MOUSE_WHEEL;
        {
            POINT pt = {GET_X_LPARAM(lParam), GET_Y_LPARAM(lParam)};
            ScreenToClient(hwnd, &pt);
            evt.wheel.x = static_cast<float>(pt.x);
            evt.wheel.y = static_cast<float>(pt.y);
        }
        evt.wheel.deltaX = static_cast<float>(GET_WHEEL_DELTA_WPARAM(wParam)) / WHEEL_DELTA;
        evt.wheel.deltaY = 0.0f;
        evt.wheel.modifiers = GetModifiers();
        win->DispatchEvent(evt);
        return 0;

    case WM_MOUSELEAVE:
        evt.type = JALIUM_EVENT_MOUSE_LEAVE;
        win->DispatchEvent(evt);
        return 0;

    // ---- Keyboard ----
    case WM_KEYDOWN:
    case WM_SYSKEYDOWN:
        evt.type = JALIUM_EVENT_KEY_DOWN;
        evt.key.keyCode = static_cast<int32_t>(wParam);
        evt.key.scanCode = static_cast<int32_t>((lParam >> 16) & 0xFF);
        evt.key.modifiers = GetModifiers();
        evt.key.isRepeat = (lParam & 0x40000000) ? 1 : 0;
        win->DispatchEvent(evt);
        if (msg == WM_SYSKEYDOWN) break; // Let DefWindowProc handle Alt+F4 etc.
        return 0;

    case WM_KEYUP:
    case WM_SYSKEYUP:
        evt.type = JALIUM_EVENT_KEY_UP;
        evt.key.keyCode = static_cast<int32_t>(wParam);
        evt.key.scanCode = static_cast<int32_t>((lParam >> 16) & 0xFF);
        evt.key.modifiers = GetModifiers();
        evt.key.isRepeat = 0;
        win->DispatchEvent(evt);
        if (msg == WM_SYSKEYUP) break;
        return 0;

    case WM_CHAR:
    case WM_SYSCHAR:
        evt.type = JALIUM_EVENT_CHAR_INPUT;
        evt.character.codepoint = static_cast<uint32_t>(wParam);
        win->DispatchEvent(evt);
        return 0;

    // ---- Pointer (WM_POINTER for touch/pen) ----
    case WM_POINTERDOWN:
    case WM_POINTERUP:
    case WM_POINTERUPDATE:
    {
        UINT32 pointerId = GET_POINTERID_WPARAM(wParam);
        POINTER_INFO pi{};
        if (GetPointerInfo(pointerId, &pi))
        {
            POINT pt = pi.ptPixelLocation;
            ScreenToClient(hwnd, &pt);

            if (msg == WM_POINTERDOWN) evt.type = JALIUM_EVENT_POINTER_DOWN;
            else if (msg == WM_POINTERUP) evt.type = JALIUM_EVENT_POINTER_UP;
            else evt.type = JALIUM_EVENT_POINTER_MOVE;

            evt.pointer.pointerId = pointerId;
            evt.pointer.x = static_cast<float>(pt.x);
            evt.pointer.y = static_cast<float>(pt.y);
            evt.pointer.pressure =
                (pi.pointerFlags & POINTER_FLAG_INCONTACT) != 0 ? 1.0f : 0.0f;
            evt.pointer.tiltX = 0.0f;
            evt.pointer.tiltY = 0.0f;
            evt.pointer.twist = 0.0f;
            evt.pointer.modifiers = GetModifiers();
            evt.pointer.flags = JALIUM_POINTER_FLAG_NONE;
            if ((pi.pointerFlags & POINTER_FLAG_INRANGE) != 0)
                evt.pointer.flags |= JALIUM_POINTER_FLAG_IN_RANGE;
            if ((pi.pointerFlags & POINTER_FLAG_INCONTACT) != 0)
                evt.pointer.flags |= JALIUM_POINTER_FLAG_IN_CONTACT;
            if ((pi.pointerFlags & POINTER_FLAG_PRIMARY) != 0)
                evt.pointer.flags |= JALIUM_POINTER_FLAG_PRIMARY;
            evt.pointer.buttons = JALIUM_POINTER_BUTTON_NONE;
            if ((pi.pointerFlags & POINTER_FLAG_FIRSTBUTTON) != 0)
                evt.pointer.buttons |= JALIUM_POINTER_BUTTON_PRIMARY;
            if ((pi.pointerFlags & POINTER_FLAG_SECONDBUTTON) != 0)
                evt.pointer.buttons |= JALIUM_POINTER_BUTTON_SECONDARY;
            if ((pi.pointerFlags & POINTER_FLAG_THIRDBUTTON) != 0)
                evt.pointer.buttons |= JALIUM_POINTER_BUTTON_TERTIARY;
            evt.pointer.toolType = JALIUM_POINTER_TOOL_UNKNOWN;

            if (pi.pointerType == PT_TOUCH)
                evt.pointer.pointerType = JALIUM_POINTER_TOUCH;
            else if (pi.pointerType == PT_PEN)
            {
                evt.pointer.pointerType = JALIUM_POINTER_PEN;
                evt.pointer.toolType = JALIUM_POINTER_TOOL_PEN;
                POINTER_PEN_INFO penInfo{};
                if (GetPointerPenInfo(pointerId, &penInfo))
                {
                    if ((penInfo.penMask & PEN_MASK_PRESSURE) != 0 &&
                        (evt.pointer.flags & JALIUM_POINTER_FLAG_IN_CONTACT) != 0)
                        evt.pointer.pressure = std::clamp(
                            static_cast<float>(penInfo.pressure) / 1024.0f,
                            0.0f, 1.0f);
                    else
                        evt.pointer.pressure = 0.0f;
                    if ((penInfo.penMask & PEN_MASK_TILT_X) != 0)
                        evt.pointer.tiltX = static_cast<float>(penInfo.tiltX);
                    if ((penInfo.penMask & PEN_MASK_TILT_Y) != 0)
                        evt.pointer.tiltY = static_cast<float>(penInfo.tiltY);
                    if ((penInfo.penMask & PEN_MASK_ROTATION) != 0)
                        evt.pointer.twist = static_cast<float>(penInfo.rotation);
                    if ((penInfo.penFlags & PEN_FLAG_ERASER) != 0)
                    {
                        evt.pointer.flags |= JALIUM_POINTER_FLAG_ERASER;
                        evt.pointer.toolType = JALIUM_POINTER_TOOL_ERASER;
                    }
                    if ((penInfo.penFlags & PEN_FLAG_INVERTED) != 0)
                        evt.pointer.flags |= JALIUM_POINTER_FLAG_INVERTED;
                    if ((penInfo.penFlags & PEN_FLAG_BARREL) != 0)
                    {
                        evt.pointer.flags |= JALIUM_POINTER_FLAG_BARREL;
                        evt.pointer.buttons |= JALIUM_POINTER_BUTTON_BARREL;
                    }
                }
            }
            else
            {
                evt.pointer.pointerType = JALIUM_POINTER_MOUSE;
                evt.pointer.toolType = JALIUM_POINTER_TOOL_MOUSE;
            }

            win->DispatchEvent(evt);
        }
        return 0;
    }

    default:
        break;
    }

    return DefWindowProcW(hwnd, msg, wParam, lParam);
}

// ============================================================================
// Platform Init / Shutdown
// ============================================================================

JaliumResult jalium_platform_init_impl()
{
    // Enable per-monitor DPI awareness
    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

    // Load GetDpiForWindow dynamically (Windows 10 1607+)
    HMODULE hUser32 = GetModuleHandleW(L"user32.dll");
    if (hUser32)
    {
        pfnGetDpiForWindow = reinterpret_cast<PFN_GetDpiForWindow>(
            GetProcAddress(hUser32, "GetDpiForWindow"));
    }

    // Register window class
    if (!g_classRegistered.exchange(true))
    {
        WNDCLASSEXW wc{};
        wc.cbSize = sizeof(wc);
        wc.style = CS_HREDRAW | CS_VREDRAW | CS_DBLCLKS;
        wc.lpfnWndProc = WndProc;
        wc.hInstance = GetModuleHandleW(nullptr);
        wc.hCursor = LoadCursorW(nullptr, IDC_ARROW);
        wc.lpszClassName = kWindowClassName;
        if (!RegisterClassExW(&wc))
        {
            g_classRegistered = false;
            return JALIUM_ERROR_INITIALIZATION_FAILED;
        }
    }

    return JALIUM_OK;
}

void jalium_platform_shutdown_impl()
{
    UnregisterClassW(kWindowClassName, GetModuleHandleW(nullptr));
    g_classRegistered = false;
}

JaliumPlatform jalium_platform_get_current_impl()
{
    return JALIUM_PLATFORM_WINDOWS;
}

// ============================================================================
// Window Management
// ============================================================================

JaliumPlatformWindow* jalium_window_create(const JaliumWindowParams* params)
{
    if (!params) return nullptr;

    auto win = new JaliumPlatformWindow();
    win->style = params->style;

    DWORD dwStyle = WS_OVERLAPPEDWINDOW;
    DWORD dwExStyle = 0;

    if (params->style & JALIUM_WINDOW_STYLE_BORDERLESS)
        dwStyle = WS_POPUP;

    if (!(params->style & JALIUM_WINDOW_STYLE_RESIZABLE))
        dwStyle &= ~(WS_THICKFRAME | WS_MAXIMIZEBOX);

    if (params->style & JALIUM_WINDOW_STYLE_TOPMOST)
        dwExStyle |= WS_EX_TOPMOST;

    if (params->style & JALIUM_WINDOW_STYLE_TRANSPARENT)
        dwExStyle |= WS_EX_NOREDIRECTIONBITMAP;

    if (params->style & JALIUM_WINDOW_STYLE_POPUP)
    {
        dwStyle = WS_POPUP;
        dwExStyle |= WS_EX_TOOLWINDOW;
    }

    int x = (params->x == JALIUM_DEFAULT_POS) ? CW_USEDEFAULT : params->x;
    int y = (params->y == JALIUM_DEFAULT_POS) ? CW_USEDEFAULT : params->y;

    // Adjust for client area size
    RECT rc = {0, 0, params->width, params->height};
    AdjustWindowRectEx(&rc, dwStyle, FALSE, dwExStyle);

    int w = (params->width > 0) ? (rc.right - rc.left) : CW_USEDEFAULT;
    int h = (params->height > 0) ? (rc.bottom - rc.top) : CW_USEDEFAULT;

    HWND parent = reinterpret_cast<HWND>(params->parentHandle);

    HWND hwnd = CreateWindowExW(
        dwExStyle,
        kWindowClassName,
        params->title ? AsWideString(params->title) : L"",
        dwStyle,
        x, y, w, h,
        parent,
        nullptr,
        GetModuleHandleW(nullptr),
        win
    );

    if (!hwnd)
    {
        delete win;
        return nullptr;
    }

    return win;
}

void jalium_window_destroy(JaliumPlatformWindow* window)
{
    if (!window) return;
    if (window->hwnd && !window->destroyed)
    {
        DestroyWindow(window->hwnd);
    }
    delete window;
}

void jalium_window_show(JaliumPlatformWindow* window)
{
    if (window && window->hwnd)
        ShowWindow(window->hwnd, SW_SHOW);
}

void jalium_window_hide(JaliumPlatformWindow* window)
{
    if (window && window->hwnd)
        ShowWindow(window->hwnd, SW_HIDE);
}

void jalium_window_set_title(JaliumPlatformWindow* window, const JaliumUtf16Char* title)
{
    if (window && window->hwnd)
        SetWindowTextW(window->hwnd, title ? AsWideString(title) : L"");
}

void jalium_window_resize(JaliumPlatformWindow* window, int32_t width, int32_t height)
{
    if (!window || !window->hwnd) return;

    DWORD dwStyle = static_cast<DWORD>(GetWindowLongPtrW(window->hwnd, GWL_STYLE));
    DWORD dwExStyle = static_cast<DWORD>(GetWindowLongPtrW(window->hwnd, GWL_EXSTYLE));

    RECT rc = {0, 0, width, height};
    AdjustWindowRectEx(&rc, dwStyle, FALSE, dwExStyle);
    SetWindowPos(window->hwnd, nullptr, 0, 0,
                 rc.right - rc.left, rc.bottom - rc.top,
                 SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE);
}

void jalium_window_move(JaliumPlatformWindow* window, int32_t x, int32_t y)
{
    if (window && window->hwnd)
        SetWindowPos(window->hwnd, nullptr, x, y, 0, 0,
                     SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
}

void jalium_window_set_state(JaliumPlatformWindow* window, JaliumWindowState state)
{
    if (!window || !window->hwnd) return;
    switch (state)
    {
    case JALIUM_WINDOW_STATE_NORMAL:
        ShowWindow(window->hwnd, SW_RESTORE);
        break;
    case JALIUM_WINDOW_STATE_MINIMIZED:
        ShowWindow(window->hwnd, SW_MINIMIZE);
        break;
    case JALIUM_WINDOW_STATE_MAXIMIZED:
        ShowWindow(window->hwnd, SW_MAXIMIZE);
        break;
    case JALIUM_WINDOW_STATE_FULLSCREEN:
        // TODO: True fullscreen with display mode change
        ShowWindow(window->hwnd, SW_MAXIMIZE);
        break;
    }
}

JaliumWindowState jalium_window_get_state(JaliumPlatformWindow* window)
{
    if (!window || !window->hwnd) return JALIUM_WINDOW_STATE_NORMAL;
    if (IsIconic(window->hwnd)) return JALIUM_WINDOW_STATE_MINIMIZED;
    if (IsZoomed(window->hwnd)) return JALIUM_WINDOW_STATE_MAXIMIZED;
    return JALIUM_WINDOW_STATE_NORMAL;
}

intptr_t jalium_window_get_native_handle(JaliumPlatformWindow* window)
{
    if (!window) return 0;
    return reinterpret_cast<intptr_t>(window->hwnd);
}

JaliumSurfaceDescriptor jalium_window_get_surface(JaliumPlatformWindow* window)
{
    JaliumSurfaceDescriptor desc{};
    if (window && window->hwnd)
    {
        desc.platform = JALIUM_PLATFORM_WINDOWS;
        desc.kind = (window->style & JALIUM_WINDOW_STYLE_TRANSPARENT)
                    ? JALIUM_SURFACE_KIND_COMPOSITION_TARGET
                    : JALIUM_SURFACE_KIND_NATIVE_WINDOW;
        desc.handle0 = reinterpret_cast<intptr_t>(window->hwnd);
    }
    return desc;
}

uint32_t jalium_window_get_portal_parent_handle(
    JaliumPlatformWindow*, char*, uint32_t)
{
    return 0;
}

uint32_t jalium_window_get_portal_parent_handle_for_native_handle(
    intptr_t, char*, uint32_t)
{
    return 0;
}

void jalium_window_set_event_callback(JaliumPlatformWindow* window,
                                       JaliumEventCallback callback, void* userData)
{
    if (!window) return;
    window->callback = callback;
    window->userData = userData;
}

void jalium_window_invalidate(JaliumPlatformWindow* window)
{
    if (window && window->hwnd)
        InvalidateRect(window->hwnd, nullptr, FALSE);
}

void jalium_window_set_cursor(JaliumPlatformWindow* window, JaliumCursorShape cursor)
{
    if (!window) return;
    LPCWSTR cursorId;
    switch (cursor)
    {
    case JALIUM_CURSOR_HAND:        cursorId = IDC_HAND; break;
    case JALIUM_CURSOR_IBEAM:       cursorId = IDC_IBEAM; break;
    case JALIUM_CURSOR_CROSSHAIR:   cursorId = IDC_CROSS; break;
    case JALIUM_CURSOR_RESIZE_NS:   cursorId = IDC_SIZENS; break;
    case JALIUM_CURSOR_RESIZE_EW:   cursorId = IDC_SIZEWE; break;
    case JALIUM_CURSOR_RESIZE_NESW: cursorId = IDC_SIZENESW; break;
    case JALIUM_CURSOR_RESIZE_NWSE: cursorId = IDC_SIZENWSE; break;
    case JALIUM_CURSOR_RESIZE_ALL:  cursorId = IDC_SIZEALL; break;
    case JALIUM_CURSOR_NOT_ALLOWED: cursorId = IDC_NO; break;
    case JALIUM_CURSOR_WAIT:        cursorId = IDC_WAIT; break;
    case JALIUM_CURSOR_HIDDEN:      SetCursor(nullptr); return;
    default:                        cursorId = IDC_ARROW; break;
    }
    SetCursor(LoadCursorW(nullptr, cursorId));
}

void jalium_window_get_client_size(JaliumPlatformWindow* window, int32_t* width, int32_t* height)
{
    if (!window || !window->hwnd) { if (width) *width = 0; if (height) *height = 0; return; }
    RECT rc;
    GetClientRect(window->hwnd, &rc);
    if (width) *width = rc.right - rc.left;
    if (height) *height = rc.bottom - rc.top;
}

void jalium_window_get_position(JaliumPlatformWindow* window, int32_t* x, int32_t* y)
{
    if (!window || !window->hwnd) { if (x) *x = 0; if (y) *y = 0; return; }
    RECT rc;
    GetWindowRect(window->hwnd, &rc);
    if (x) *x = rc.left;
    if (y) *y = rc.top;
}

// ============================================================================
// Event Loop
// ============================================================================

int32_t jalium_platform_run_message_loop(void)
{
    g_quitRequested = false;
    MSG msg;
    while (GetMessageW(&msg, nullptr, 0, 0) > 0)
    {
        TranslateMessage(&msg);
        DispatchMessageW(&msg);
        if (g_quitRequested.load(std::memory_order_acquire))
            break;
    }
    return g_exitCode.load(std::memory_order_acquire);
}

int32_t jalium_platform_poll_events(void)
{
    int32_t count = 0;
    MSG msg;
    while (PeekMessageW(&msg, nullptr, 0, 0, PM_REMOVE))
    {
        if (msg.message == WM_QUIT)
        {
            g_quitRequested = true;
            g_exitCode = static_cast<int32_t>(msg.wParam);
            break;
        }
        TranslateMessage(&msg);
        DispatchMessageW(&msg);
        count++;
    }
    return count;
}

void jalium_platform_quit(int32_t exitCode)
{
    g_exitCode = exitCode;
    g_quitRequested = true;
    PostQuitMessage(exitCode);
}

// ============================================================================
// Dispatcher (Win32 Message Window)
// ============================================================================

static const wchar_t* kDispatcherClassName = L"JaliumDispatcherMsgWnd";
static std::atomic<bool> g_dispatcherClassRegistered{false};

#define WM_JALIUM_DISPATCHER_WAKE (WM_USER + 0x0100)

struct JaliumDispatcher {
    HWND                    hwnd = nullptr;
    JaliumDispatcherCallback callback = nullptr;
    void*                   userData = nullptr;
    DWORD                   threadId = 0;
};

static LRESULT CALLBACK DispatcherWndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
    if (msg == WM_JALIUM_DISPATCHER_WAKE)
    {
        auto disp = reinterpret_cast<JaliumDispatcher*>(
            GetWindowLongPtrW(hwnd, GWLP_USERDATA));
        if (disp && disp->callback)
        {
            disp->callback(disp->userData);
        }
        return 0;
    }
    return DefWindowProcW(hwnd, msg, wParam, lParam);
}

JaliumResult jalium_dispatcher_create(JaliumDispatcher** outDispatcher)
{
    if (!outDispatcher) return JALIUM_ERROR_INVALID_ARGUMENT;

    // Register dispatcher window class once
    if (!g_dispatcherClassRegistered.exchange(true))
    {
        WNDCLASSEXW wc{};
        wc.cbSize = sizeof(wc);
        wc.lpfnWndProc = DispatcherWndProc;
        wc.hInstance = GetModuleHandleW(nullptr);
        wc.lpszClassName = kDispatcherClassName;
        if (!RegisterClassExW(&wc))
        {
            g_dispatcherClassRegistered = false;
            return JALIUM_ERROR_INITIALIZATION_FAILED;
        }
    }

    auto disp = new JaliumDispatcher();
    disp->threadId = GetCurrentThreadId();
    disp->hwnd = CreateWindowExW(
        0, kDispatcherClassName, L"", 0,
        0, 0, 0, 0,
        HWND_MESSAGE, nullptr,
        GetModuleHandleW(nullptr), nullptr);

    if (!disp->hwnd)
    {
        delete disp;
        return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
    }

    SetWindowLongPtrW(disp->hwnd, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(disp));
    *outDispatcher = disp;
    return JALIUM_OK;
}

void jalium_dispatcher_destroy(JaliumDispatcher* dispatcher)
{
    if (!dispatcher) return;
    if (dispatcher->hwnd)
        DestroyWindow(dispatcher->hwnd);
    delete dispatcher;
}

void jalium_dispatcher_wake(JaliumDispatcher* dispatcher)
{
    if (dispatcher && dispatcher->hwnd)
        PostMessageW(dispatcher->hwnd, WM_JALIUM_DISPATCHER_WAKE, 0, 0);
}

void jalium_dispatcher_set_callback(JaliumDispatcher* dispatcher,
                                     JaliumDispatcherCallback callback, void* userData)
{
    if (!dispatcher) return;
    dispatcher->callback = callback;
    dispatcher->userData = userData;
}

// ============================================================================
// High-Resolution Timer
// ============================================================================

typedef HANDLE (WINAPI *PFN_CreateWaitableTimerExW)(
    LPSECURITY_ATTRIBUTES, LPCWSTR, DWORD, DWORD);
static PFN_CreateWaitableTimerExW pfnCreateWaitableTimerExW = nullptr;

struct JaliumTimer {
    HANDLE              hTimer = nullptr;
    JaliumTimerCallback callback = nullptr;
    void*               userData = nullptr;
    bool                useFallback = false;
};

static void InitTimerFunctions()
{
    static bool initialized = false;
    if (!initialized)
    {
        HMODULE hKernel32 = GetModuleHandleW(L"kernel32.dll");
        if (hKernel32)
        {
            pfnCreateWaitableTimerExW = reinterpret_cast<PFN_CreateWaitableTimerExW>(
                GetProcAddress(hKernel32, "CreateWaitableTimerExW"));
        }
        initialized = true;
    }
}

JaliumResult jalium_timer_create(JaliumTimer** outTimer)
{
    if (!outTimer) return JALIUM_ERROR_INVALID_ARGUMENT;

    InitTimerFunctions();

    auto timer = new JaliumTimer();

    if (pfnCreateWaitableTimerExW)
    {
        // CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x00000002
        timer->hTimer = pfnCreateWaitableTimerExW(
            nullptr, nullptr, 0x00000002, TIMER_ALL_ACCESS);
    }

    if (!timer->hTimer)
    {
        // Fallback to standard waitable timer
        timer->hTimer = CreateWaitableTimerW(nullptr, TRUE, nullptr);
        timer->useFallback = true;
    }

    if (!timer->hTimer)
    {
        delete timer;
        return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
    }

    *outTimer = timer;
    return JALIUM_OK;
}

void jalium_timer_destroy(JaliumTimer* timer)
{
    if (!timer) return;
    if (timer->hTimer)
        CloseHandle(timer->hTimer);
    delete timer;
}

void jalium_timer_arm(JaliumTimer* timer, int64_t intervalMicroseconds)
{
    if (!timer || !timer->hTimer) return;

    LARGE_INTEGER dueTime;
    dueTime.QuadPart = -(intervalMicroseconds * 10); // 100ns units, negative = relative
    SetWaitableTimer(timer->hTimer, &dueTime, 0, nullptr, nullptr, FALSE);
}

void jalium_timer_arm_repeating(JaliumTimer* timer, int64_t intervalMicroseconds)
{
    if (!timer || !timer->hTimer) return;

    LARGE_INTEGER dueTime;
    dueTime.QuadPart = -(intervalMicroseconds * 10);
    LONG periodMs = static_cast<LONG>(intervalMicroseconds / 1000);
    if (periodMs < 1) periodMs = 1;
    SetWaitableTimer(timer->hTimer, &dueTime, periodMs, nullptr, nullptr, FALSE);
}

void jalium_timer_disarm(JaliumTimer* timer)
{
    if (timer && timer->hTimer)
        CancelWaitableTimer(timer->hTimer);
}

void jalium_timer_set_callback(JaliumTimer* timer, JaliumTimerCallback callback, void* userData)
{
    if (!timer) return;
    timer->callback = callback;
    timer->userData = userData;
}

int32_t jalium_timer_wait(JaliumTimer* timer, uint32_t timeoutMs)
{
    if (!timer || !timer->hTimer) return 0;

    DWORD timeout = (timeoutMs == 0) ? INFINITE : timeoutMs;
    DWORD result = WaitForSingleObject(timer->hTimer, timeout);
    return (result == WAIT_OBJECT_0) ? 1 : 0;
}

// ============================================================================
// DPI and Display
// ============================================================================

float jalium_platform_get_system_dpi_scale(void)
{
    HDC hdc = GetDC(nullptr);
    if (hdc)
    {
        float scale = static_cast<float>(GetDeviceCaps(hdc, LOGPIXELSX)) / 96.0f;
        ReleaseDC(nullptr, hdc);
        return scale;
    }
    return 1.0f;
}

float jalium_window_get_dpi_scale(JaliumPlatformWindow* window)
{
    if (!window) return 1.0f;
    return window->dpiScale;
}

int32_t jalium_window_get_monitor_refresh_rate(JaliumPlatformWindow* window)
{
    if (!window || !window->hwnd) return 60;

    HMONITOR hMon = MonitorFromWindow(window->hwnd, MONITOR_DEFAULTTONEAREST);
    MONITORINFOEXW mi{};
    mi.cbSize = sizeof(mi);
    if (GetMonitorInfoW(hMon, &mi))
    {
        DEVMODEW dm{};
        dm.dmSize = sizeof(dm);
        if (EnumDisplaySettingsW(mi.szDevice, ENUM_CURRENT_SETTINGS, &dm))
        {
            if (dm.dmDisplayFrequency > 0)
                return static_cast<int32_t>(dm.dmDisplayFrequency);
        }
    }
    return 60;
}

// ============================================================================
// Input State Polling
// ============================================================================

int16_t jalium_input_get_key_state(int32_t jaliumVirtualKey)
{
    // On Windows, Jalium virtual key codes map 1:1 to Win32 VK codes
    SHORT state = GetAsyncKeyState(jaliumVirtualKey);
    int16_t result = 0;
    if (state & 0x8000) result |= 1; // Currently pressed
    if (GetKeyState(jaliumVirtualKey) & 0x0001) result |= 2; // Toggled
    return result;
}

JaliumResult jalium_input_get_touch_capabilities(
    int32_t* touchPresent, int32_t* maxContacts)
{
    if (!touchPresent || !maxContacts)
        return JALIUM_ERROR_INVALID_ARGUMENT;
    const int digitizer = GetSystemMetrics(SM_DIGITIZER);
    const int touchMask =
        NID_READY | NID_INTEGRATED_TOUCH | NID_EXTERNAL_TOUCH;
    *touchPresent = (digitizer & touchMask) != 0 ? 1 : 0;
    *maxContacts = *touchPresent != 0
        ? std::max(GetSystemMetrics(SM_MAXIMUMTOUCHES), 0)
        : 0;
    return JALIUM_OK;
}

JaliumResult jalium_platform_set_double_click_settings(uint32_t, float)
{
    return JALIUM_ERROR_NOT_SUPPORTED;
}

JaliumResult jalium_input_get_cursor_pos(float* x, float* y)
{
    if (!x || !y) return JALIUM_ERROR_INVALID_ARGUMENT;
    POINT pt{};
    if (!GetCursorPos(&pt))
    {
        *x = 0.0f;
        *y = 0.0f;
        return JALIUM_ERROR_INVALID_STATE;
    }
    *x = static_cast<float>(pt.x);
    *y = static_cast<float>(pt.y);
    return JALIUM_OK;
}

// ============================================================================
// Clipboard
// ============================================================================

JaliumResult jalium_clipboard_get_text(JaliumUtf16Char** outText)
{
    if (!outText) return JALIUM_ERROR_INVALID_ARGUMENT;
    *outText = nullptr;

    if (!OpenClipboard(nullptr))
        return JALIUM_ERROR_INVALID_STATE;

    HANDLE hData = GetClipboardData(CF_UNICODETEXT);
    if (hData)
    {
        auto pText = static_cast<const wchar_t*>(GlobalLock(hData));
        if (pText)
        {
            size_t len = wcslen(pText);
            *outText = static_cast<JaliumUtf16Char*>(
                malloc((len + 1) * sizeof(JaliumUtf16Char)));
            if (*outText)
                memcpy(*outText, pText, (len + 1) * sizeof(JaliumUtf16Char));
            GlobalUnlock(hData);
        }
    }

    CloseClipboard();
    return JALIUM_OK;
}

JaliumResult jalium_clipboard_set_text(const JaliumUtf16Char* text)
{
    if (!text) return JALIUM_ERROR_INVALID_ARGUMENT;

    if (!OpenClipboard(nullptr))
        return JALIUM_ERROR_INVALID_STATE;

    EmptyClipboard();

    const wchar_t* wideText = AsWideString(text);
    size_t len = wcslen(wideText);
    size_t size = (len + 1) * sizeof(JaliumUtf16Char);
    HGLOBAL hMem = GlobalAlloc(GMEM_MOVEABLE, size);
    if (hMem)
    {
        auto pDest = static_cast<wchar_t*>(GlobalLock(hMem));
        if (pDest)
        {
            memcpy(pDest, wideText, size);
            GlobalUnlock(hMem);
            SetClipboardData(CF_UNICODETEXT, hMem);
        }
    }

    CloseClipboard();
    return JALIUM_OK;
}

JaliumResult jalium_clipboard_get_formats(char** outMimeTypes)
{
    if (!outMimeTypes) return JALIUM_ERROR_INVALID_ARGUMENT;
    *outMimeTypes = nullptr;
    const char* formats = IsClipboardFormatAvailable(CF_UNICODETEXT)
        ? "text/plain;charset=utf-8\ntext/plain\nUTF8_STRING" : "";
    const size_t length = strlen(formats);
    auto* result = static_cast<char*>(malloc(length + 1));
    if (!result) return JALIUM_ERROR_OUT_OF_MEMORY;
    memcpy(result, formats, length + 1);
    *outMimeTypes = result;
    return JALIUM_OK;
}

JaliumResult jalium_clipboard_get_data(
    const char* mimeType, uint8_t** outData, uint32_t* outDataSize)
{
    if (!mimeType || !outData || !outDataSize)
        return JALIUM_ERROR_INVALID_ARGUMENT;
    *outData = nullptr;
    *outDataSize = 0;
    const bool text = strcmp(mimeType, "text/plain;charset=utf-8") == 0 ||
                      strcmp(mimeType, "text/plain") == 0 ||
                      strcmp(mimeType, "UTF8_STRING") == 0;
    if (!text) return JALIUM_OK;
    JaliumUtf16Char* utf16 = nullptr;
    const JaliumResult result = jalium_clipboard_get_text(&utf16);
    if (result != JALIUM_OK || !utf16) return result;
    const wchar_t* wide = AsWideString(utf16);
    const int wideLength = static_cast<int>(wcslen(wide));
    const int size = WideCharToMultiByte(
        CP_UTF8, 0, wide, wideLength, nullptr, 0, nullptr, nullptr);
    if (size < 0) { free(utf16); return JALIUM_ERROR_UNKNOWN; }
    auto* bytes = static_cast<uint8_t*>(malloc(static_cast<size_t>(size > 0 ? size : 1)));
    if (!bytes) { free(utf16); return JALIUM_ERROR_OUT_OF_MEMORY; }
    if (size > 0)
        WideCharToMultiByte(
            CP_UTF8, 0, wide, wideLength, reinterpret_cast<char*>(bytes),
            size, nullptr, nullptr);
    free(utf16);
    *outData = bytes;
    *outDataSize = static_cast<uint32_t>(size);
    return JALIUM_OK;
}

JaliumResult jalium_clipboard_set_data(
    const JaliumClipboardDataItem* items, uint32_t itemCount)
{
    if (itemCount == 0) return jalium_clipboard_clear();
    if (!items) return JALIUM_ERROR_INVALID_ARGUMENT;
    for (uint32_t index = 0; index < itemCount; ++index)
    {
        if (!items[index].mimeType ||
            (!items[index].data && items[index].dataSize != 0))
            return JALIUM_ERROR_INVALID_ARGUMENT;
        if (strcmp(items[index].mimeType, "text/plain;charset=utf-8") != 0 &&
            strcmp(items[index].mimeType, "text/plain") != 0 &&
            strcmp(items[index].mimeType, "UTF8_STRING") != 0)
            continue;
        const int wideLength = MultiByteToWideChar(
            CP_UTF8, MB_ERR_INVALID_CHARS,
            reinterpret_cast<const char*>(items[index].data),
            static_cast<int>(items[index].dataSize), nullptr, 0);
        if (wideLength < 0) return JALIUM_ERROR_INVALID_ARGUMENT;
        std::vector<JaliumUtf16Char> wide(static_cast<size_t>(wideLength) + 1);
        if (wideLength != 0)
            MultiByteToWideChar(
                CP_UTF8, MB_ERR_INVALID_CHARS,
                reinterpret_cast<const char*>(items[index].data),
                static_cast<int>(items[index].dataSize),
                reinterpret_cast<wchar_t*>(wide.data()), wideLength);
        return jalium_clipboard_set_text(wide.data());
    }
    return JALIUM_ERROR_NOT_SUPPORTED;
}

JaliumResult jalium_clipboard_clear(void)
{
    if (!OpenClipboard(nullptr)) return JALIUM_ERROR_INVALID_STATE;
    const bool cleared = EmptyClipboard() != FALSE;
    CloseClipboard();
    return cleared ? JALIUM_OK : JALIUM_ERROR_UNKNOWN;
}

// Windows desktop drag/drop continues to use the managed AOT-compatible OLE
// IDataObject/IDropSource implementation. Export the cross-platform ABI so the
// native platform library remains symbol-compatible without disturbing OLE.
void jalium_drag_set_effect(JaliumPlatformWindow*, uint64_t, uint32_t) {}

JaliumResult jalium_drag_begin(
    JaliumPlatformWindow*, const JaliumDragDataItem*, uint32_t, uint32_t,
    uint32_t* performedEffect)
{
    if (performedEffect) *performedEffect = JALIUM_DRAG_EFFECT_NONE;
    return JALIUM_ERROR_NOT_SUPPORTED;
}

JaliumResult jalium_drag_begin_ex(
    JaliumPlatformWindow* window, const JaliumDragDataItem* items,
    uint32_t itemCount, uint32_t allowedEffects,
    JaliumDragFeedbackCallback, JaliumDragQueryContinueCallback, void*,
    uint32_t* performedEffect)
{
    return jalium_drag_begin(
        window, items, itemCount, allowedEffects, performedEffect);
}

JaliumResult jalium_drag_begin_with_image(
    JaliumPlatformWindow* window, const JaliumDragDataItem* items,
    uint32_t itemCount, uint32_t allowedEffects,
    JaliumDragFeedbackCallback feedbackCallback,
    JaliumDragQueryContinueCallback queryContinueCallback, void* userData,
    const JaliumDragImage*, uint32_t* performedEffect)
{
    return jalium_drag_begin_ex(
        window, items, itemCount, allowedEffects,
        feedbackCallback, queryContinueCallback, userData, performedEffect);
}

// Window-management extensions: the Windows managed layer talks to Win32
// directly (HWND path, PlatformFactory returns no native window there), so
// these exist for symbol compatibility only.
int32_t jalium_platform_get_monitor_count(void) { return 0; }

int32_t jalium_platform_get_monitor_info(int32_t, JaliumMonitorInfo* info)
{
    if (info) *info = JaliumMonitorInfo{};
    return JALIUM_ERROR_NOT_SUPPORTED;
}

int32_t jalium_window_set_min_max_size(JaliumPlatformWindow*, int32_t, int32_t, int32_t, int32_t)
{
    return JALIUM_ERROR_NOT_SUPPORTED;
}

int32_t jalium_window_begin_move_drag(JaliumPlatformWindow*)
{
    return JALIUM_ERROR_NOT_SUPPORTED;
}

int32_t jalium_window_begin_resize_drag(JaliumPlatformWindow*, int32_t)
{
    return JALIUM_ERROR_NOT_SUPPORTED;
}

int32_t jalium_window_set_icon(JaliumPlatformWindow*, const uint32_t*, int32_t, int32_t)
{
    return JALIUM_ERROR_NOT_SUPPORTED;
}

int32_t jalium_window_set_topmost(JaliumPlatformWindow*, int32_t)
{
    return JALIUM_ERROR_NOT_SUPPORTED;
}

int32_t jalium_window_set_enabled(JaliumPlatformWindow*, int32_t)
{
    return JALIUM_ERROR_NOT_SUPPORTED;
}

int32_t jalium_window_set_opacity(JaliumPlatformWindow*, double)
{
    return JALIUM_ERROR_NOT_SUPPORTED;
}

int32_t jalium_window_set_show_in_taskbar(JaliumPlatformWindow*, int32_t)
{
    return JALIUM_ERROR_NOT_SUPPORTED;
}

int32_t jalium_window_set_resizable(JaliumPlatformWindow*, int32_t)
{
    return JALIUM_ERROR_NOT_SUPPORTED;
}

int32_t jalium_window_set_decorated(JaliumPlatformWindow*, int32_t)
{
    return JALIUM_ERROR_NOT_SUPPORTED;
}

int32_t jalium_window_set_owner(JaliumPlatformWindow*, intptr_t)
{
    return JALIUM_ERROR_NOT_SUPPORTED;
}

int32_t jalium_window_activate(JaliumPlatformWindow*)
{
    return JALIUM_ERROR_NOT_SUPPORTED;
}

int32_t jalium_window_show_system_menu(
    JaliumPlatformWindow* window, int32_t, int32_t)
{
    return window ? JALIUM_ERROR_NOT_SUPPORTED : JALIUM_ERROR_INVALID_ARGUMENT;
}

int32_t jalium_window_update_ime_context(
    JaliumPlatformWindow* window, int32_t, const char*, int32_t, int32_t,
    int32_t, int32_t, int32_t, int32_t)
{
    return window ? JALIUM_ERROR_NOT_SUPPORTED : JALIUM_ERROR_INVALID_ARGUMENT;
}

#endif // _WIN32

#include "d3d12_window_composition.h"
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Numerics.h>
#include <winrt/Windows.System.h>
#include <winrt/Windows.UI.Composition.h>
#include <winrt/Windows.UI.Composition.Desktop.h>
#include <winrt/Windows.UI.Composition.Core.h>
#include <windows.ui.composition.interop.h>
#include <roapi.h>
#include <mutex>
#include <atomic>
#include <commctrl.h>
#include <dwmapi.h>
#pragma comment(lib, "windowsapp.lib")
#pragma comment(lib, "comctl32.lib")
#pragma comment(lib, "dwmapi.lib")
namespace jalium {
namespace composition = winrt::Windows::UI::Composition;
struct D3D12WindowComposition::State : std::enable_shared_from_this<State> {
    HWND parent = nullptr;
    HWND host = nullptr;
    UINT_PTR subscription = 0;
    bool sizing = false;
    bool resizePresentationEnabled = true;
    int appliedX = 0, appliedY = 0;
    UINT appliedWidth = 0, appliedHeight = 0;
    static UINT UpdateHostMessage() {
        static const UINT message = RegisterWindowMessageW(L"Jalium.UI.CompositionHost.UpdatePlacement");
        return message;
    }
    static LRESULT CALLBACK HostProc(HWND window, UINT message, WPARAM wp, LPARAM lp) {
        if (message == WM_NCHITTEST) return HTTRANSPARENT;
        if (message == WM_MOUSEACTIVATE) return MA_NOACTIVATE;
        if (message == WM_ERASEBKGND) return 1;
        if (message == WM_PAINT) {
            PAINTSTRUCT paint{};
            BeginPaint(window, &paint);
            EndPaint(window, &paint);
            return 0;
        }
        return DefWindowProcW(window, message, wp, lp);
    }
    static HWND CreateHost(HWND owner, UINT width, UINT height) {
        static ATOM hostClass = [] {
            WNDCLASSEXW descriptor{};
            descriptor.cbSize = sizeof(descriptor);
            descriptor.lpfnWndProc = HostProc;
            descriptor.hInstance = GetModuleHandleW(nullptr);
            descriptor.lpszClassName = L"Jalium.UI.CompositionResizeHost";
            return RegisterClassExW(&descriptor);
        }();
        if (!hostClass) return nullptr;
        return CreateWindowExW(WS_EX_NOREDIRECTIONBITMAP | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT,
            MAKEINTATOM(hostClass), L"", WS_POPUP, 0, 0, width, height, owner, nullptr, GetModuleHandleW(nullptr), nullptr);
    }
    void UpdateHost() {
        std::lock_guard<std::recursive_mutex> guard(*commitMutex);
        if (!resizePresentationEnabled || !sizing || (!right && !bottom) ||
            !IsWindowVisible(parent) || IsIconic(parent) || IsZoomed(parent)) {
            if (host && IsWindowVisible(host)) ShowWindow(host, SW_HIDE);
            return;
        }
        RECT client{};
        if (!host || !GetClientRect(parent, &client)) return;
        POINT origin{};
        if (!ClientToScreen(parent, &origin)) return;
        const int x = origin.x + (right ? client.right - static_cast<int>(width) : 0);
        const int y = origin.y + (bottom ? client.bottom - static_cast<int>(height) : 0);
        const int left = origin.x - x, top = origin.y - y;
        DWM_WINDOW_CORNER_PREFERENCE corners = DWMWCP_DONOTROUND;
        const bool rounded = SUCCEEDED(DwmGetWindowAttribute(parent, DWMWA_WINDOW_CORNER_PREFERENCE, &corners, sizeof(corners))) &&
            corners != DWMWCP_DONOTROUND && (GetWindowLongPtrW(parent, GWL_STYLE) & WS_THICKFRAME) != 0;
        const int radius = MulDiv(corners == DWMWCP_ROUNDSMALL ? 4 : 8, GetDpiForWindow(parent), 96);
        HRGN crop = rounded
            ? CreateRoundRectRgn(left, top, left + client.right + 1, top + client.bottom + 1, radius * 2, radius * 2)
            : CreateRectRgn(left, top, left + client.right, top + client.bottom);
        if (!crop || !SetWindowRgn(host, crop, FALSE)) {
            if (crop) DeleteObject(crop);
            ShowWindow(host, SW_HIDE);
            return;
        }
        if (x != appliedX || y != appliedY || width != appliedWidth || height != appliedHeight) {
            if (!SetWindowPos(host, nullptr, x, y, width, height, SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOCOPYBITS)) {
                ShowWindow(host, SW_HIDE);
                return;
            }
            appliedX = x; appliedY = y; appliedWidth = width; appliedHeight = height;
        }
        if (!IsWindowVisible(host)) ShowWindow(host, SW_SHOWNOACTIVATE);
    }
    static LRESULT CALLBACK ParentProc(HWND window, UINT message, WPARAM wp, LPARAM lp, UINT_PTR id, DWORD_PTR data) {
        auto state = reinterpret_cast<State*>(data)->shared_from_this();
        if (message == UpdateHostMessage()) { state->UpdateHost(); return 0; }
        if (message == WM_ENTERSIZEMOVE) state->sizing = true;
        const auto styles = message == WM_STYLECHANGING && wp == GWL_STYLE
            ? reinterpret_cast<const STYLESTRUCT*>(lp) : nullptr;
        const bool restoringCaption = styles && (styles->styleOld & WS_CAPTION) == 0 &&
            (styles->styleNew & WS_CAPTION) != 0;
        if (message == WM_EXITSIZEMOVE || message == WM_CANCELMODE || message == WM_DESTROY || restoringCaption ||
            (message == WM_SYSCOMMAND && ((wp & 0xFFF0) == SC_MAXIMIZE || (wp & 0xFFF0) == SC_MINIMIZE || (wp & 0xFFF0) == SC_RESTORE))) {
            // Retire the resize-only target before the normal surface placement
            // or a native caption animation changes. The parent's target keeps
            // displaying the same image throughout this handoff.
            state->sizing = false;
            state->UpdateHost();
        }
        const LRESULT result = DefSubclassProc(window, message, wp, lp);
        DWORD_PTR current = 0;
        if ((message == WM_ENTERSIZEMOVE || message == WM_WINDOWPOSCHANGED) &&
            GetWindowSubclass(window, ParentProc, id, &current) && current == data)
            state->UpdateHost();
        return result;
    }
    composition::Core::CompositorController commits{ nullptr };
    composition::Compositor compositor{ nullptr };
    composition::Desktop::DesktopWindowTarget target{ nullptr };
    composition::Desktop::DesktopWindowTarget resizeTarget{ nullptr };
    composition::ContainerVisual resizeRoot{ nullptr };
    composition::SpriteVisual resizeImage{ nullptr };
    composition::ContainerVisual root{ nullptr };
    composition::LayerVisual resizeLayer{ nullptr };
    composition::SpriteVisual image{ nullptr };
    std::shared_ptr<std::recursive_mutex> commitMutex = std::make_shared<std::recursive_mutex>();
    winrt::event_token commitNeeded{};
    bool right = false, bottom = false;
    UINT width = 0, height = 0;
    DWORD apartmentThread = 0;
    bool uninitializeApartment = false;
    ~State() {
        if (parent && subscription) RemoveWindowSubclass(parent, ParentProc, subscription);
        try {
            if (commits) commits.CommitNeeded(commitNeeded);
            if (target) target.Root(nullptr);
            if (resizeTarget) resizeTarget.Root(nullptr);
            if (resizeImage) resizeImage.Brush(nullptr);
            if (resizeRoot) resizeRoot.Children().RemoveAll();
            if (image) image.Brush(nullptr);
            if (resizeLayer) resizeLayer.Children().RemoveAll();
            if (root) root.Children().RemoveAll();
            if (target) target.Close();
            if (resizeTarget) resizeTarget.Close();
            if (compositor) compositor.Close();
        } catch (...) {}
        image = nullptr;
        resizeImage = nullptr;
        resizeRoot = nullptr;
        resizeTarget = nullptr;
        resizeLayer = nullptr;
        root = nullptr;
        target = nullptr;
        compositor = nullptr;
        commits = nullptr;
        if (host && IsWindow(host)) DestroyWindow(host);
        if (uninitializeApartment && apartmentThread == GetCurrentThreadId())
            RoUninitialize();
    }
};
D3D12WindowComposition::D3D12WindowComposition(std::shared_ptr<State> state) : state_(std::move(state)) {}
D3D12WindowComposition::~D3D12WindowComposition() {
}
void D3D12WindowComposition::SetResizePresentationEnabled(bool enabled) {
    if (!state_) return;
    std::lock_guard<std::recursive_mutex> guard(*state_->commitMutex);
    if (state_->resizePresentationEnabled == enabled) return;
    state_->resizePresentationEnabled = enabled;
    PostMessageW(state_->parent, State::UpdateHostMessage(), 0, 0);
}
std::unique_ptr<D3D12WindowComposition> D3D12WindowComposition::Create(HWND window, IDXGISwapChain1* swapChain, UINT width, UINT height) {
    try {
        auto state = std::make_shared<State>();
        std::lock_guard<std::recursive_mutex> guard(*state->commitMutex);
        HRESULT initialized = RoInitialize(RO_INIT_SINGLETHREADED);
        if (FAILED(initialized) && initialized != RPC_E_CHANGED_MODE) return nullptr;
        state->apartmentThread = GetCurrentThreadId();
        state->uninitializeApartment = SUCCEEDED(initialized);
        // The host owns the dispatcher and shuts its queue down with its message
        // loop. Standalone native callers without one retain the DComp fallback.
        if (!winrt::Windows::System::DispatcherQueue::GetForCurrentThread())
            return nullptr;
        // Commit anchor changes explicitly so the first size delta does not
        // depend on a later queue turn to place the already-presented image.
        state->commits = composition::Core::CompositorController();
        state->commitNeeded = state->commits.CommitNeeded(
            [gate = state->commitMutex](composition::Core::CompositorController const& controller,
                winrt::Windows::Foundation::IInspectable const&) {
                std::lock_guard<std::recursive_mutex> guard(*gate);
                try { controller.Commit(); } catch (...) {}
            });
        state->compositor = state->commits.Compositor();
        state->parent = window;
        state->host = State::CreateHost(window, width, height);
        if (!state->host) return nullptr;
        DWMNCRENDERINGPOLICY noNativeFrame = DWMNCRP_DISABLED;
        DwmSetWindowAttribute(state->host, DWMWA_NCRENDERING_POLICY, &noNativeFrame, sizeof(noNativeFrame));
        BOOL disableTransitions = TRUE;
        DwmSetWindowAttribute(state->host, DWMWA_TRANSITIONS_FORCEDISABLED, &disableTransitions, sizeof(disableTransitions));
        static std::atomic<UINT_PTR> nextSubscription{ 1 };
        state->subscription = nextSubscription.fetch_add(1);
        if (!SetWindowSubclass(window, State::ParentProc, state->subscription, reinterpret_cast<DWORD_PTR>(state.get()))) return nullptr;
        auto desktop = state->compositor.as<ABI::Windows::UI::Composition::Desktop::ICompositorDesktopInterop>();
        winrt::check_hresult(desktop->CreateDesktopWindowTarget(window, FALSE,
            reinterpret_cast<ABI::Windows::UI::Composition::Desktop::IDesktopWindowTarget**>(winrt::put_abi(state->target))));
        winrt::check_hresult(desktop->CreateDesktopWindowTarget(state->host, TRUE,
            reinterpret_cast<ABI::Windows::UI::Composition::Desktop::IDesktopWindowTarget**>(winrt::put_abi(state->resizeTarget))));
        state->root = state->compositor.CreateContainerVisual();
        state->root.RelativeSizeAdjustment({ 1.0f, 1.0f });
        state->target.Root(state->root);
        composition::ICompositionSurface surface{ nullptr };
        auto interop = state->compositor.as<ABI::Windows::UI::Composition::ICompositorInterop>();
        winrt::check_hresult(interop->CreateCompositionSurfaceForSwapChain(swapChain,
            reinterpret_cast<ABI::Windows::UI::Composition::ICompositionSurface**>(winrt::put_abi(surface))));
        auto brush = state->compositor.CreateSurfaceBrush(surface);
        brush.Stretch(composition::CompositionStretch::None);
        brush.HorizontalAlignmentRatio(0.0f);
        brush.VerticalAlignmentRatio(0.0f);
        state->image = state->compositor.CreateSpriteVisual();
        state->image.Size({ static_cast<float>(width), static_cast<float>(height) });
        state->image.Brush(brush);
        state->root.Children().InsertAtBottom(state->image);
        // This target never resizes within the reserved capacity. Its stationary
        // edge stays in screen coordinates while USER32 updates only the moving
        // edge of its window region. Reusing the surface adds no frame copy.
        state->resizeRoot = state->compositor.CreateContainerVisual();
        state->resizeImage = state->compositor.CreateSpriteVisual();
        state->resizeImage.Size({ static_cast<float>(width), static_cast<float>(height) });
        state->resizeImage.Brush(brush);
        state->resizeRoot.Children().InsertAtBottom(state->resizeImage);
        state->resizeTarget.Root(state->resizeRoot);
        state->width = width; state->height = height;
        state->appliedWidth = width; state->appliedHeight = height;
        state->UpdateHost();
        state->commits.Commit();
        return std::unique_ptr<D3D12WindowComposition>(new D3D12WindowComposition(std::move(state)));
    } catch (...) { return nullptr; }
}
bool D3D12WindowComposition::SetPlacement(bool right, bool bottom, UINT width, UINT height) {
    if (!state_) return false;
    try {
        std::lock_guard<std::recursive_mutex> guard(*state_->commitMutex);
        bool changed = false;
        const bool flattenResize = right || bottom;
        if (flattenResize && !state_->resizeLayer) {
            // Flatten the anchored image and its HWND-relative clipping into
            // one composition layer while the window is being resized.
            state_->resizeLayer = state_->compositor.CreateLayerVisual();
            state_->resizeLayer.RelativeSizeAdjustment({ 1.0f, 1.0f });
            state_->root.Children().Remove(state_->image);
            state_->resizeLayer.Children().InsertAtBottom(state_->image);
            state_->root.Children().InsertAtBottom(state_->resizeLayer);
            changed = true;
        } else if (!flattenResize && state_->resizeLayer) {
            state_->resizeLayer.Children().Remove(state_->image);
            state_->root.Children().Remove(state_->resizeLayer);
            state_->root.Children().InsertAtBottom(state_->image);
            state_->resizeLayer = nullptr;
            changed = true;
        }
        if (state_->width != width || state_->height != height) {
            state_->image.Size({ static_cast<float>(width), static_cast<float>(height) });
            state_->resizeImage.Size({ static_cast<float>(width), static_cast<float>(height) });
            state_->width = width; state_->height = height;
            changed = true;
        }
        if (state_->right != right || state_->bottom != bottom) {
            state_->image.AnchorPoint({ right ? 1.0f : 0.0f, bottom ? 1.0f : 0.0f });
            state_->image.RelativeOffsetAdjustment({ right ? 1.0f : 0.0f, bottom ? 1.0f : 0.0f, 0.0f });
            state_->right = right; state_->bottom = bottom;
            changed = true;
        }
        // Desktop target bounds can change even when the reserved surface and
        // the chosen anchor remain unchanged.
        state_->commits.Commit();
        if (changed) {
            // Keep the anchor transition out of a later compositor queue turn.
            // Polling the async status also works on an STA without pumping a
            // reentrant WM_PAINT into a frame that is still being submitted.
            auto completion = state_->commits.EnsurePreviousCommitCompletedAsync();
            const ULONGLONG deadline = GetTickCount64() + 40;
            while (completion.Status() == winrt::Windows::Foundation::AsyncStatus::Started &&
                   GetTickCount64() < deadline) {
                Sleep(1);
            }
            if (completion.Status() != winrt::Windows::Foundation::AsyncStatus::Started)
                completion.GetResults();
        }
        if (changed) PostMessageW(state_->parent, State::UpdateHostMessage(), 0, 0);
        return true;
    } catch (...) { return false; }
}
}

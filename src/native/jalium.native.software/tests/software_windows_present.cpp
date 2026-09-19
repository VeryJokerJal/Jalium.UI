#include "jalium_api.h"

#include <Windows.h>

#include <cmath>
#include <cstdint>
#include <cstdlib>
#include <iostream>
#include <memory>
#include <string>
#include <vector>

extern "C" void jalium_software_init();

namespace {

constexpr wchar_t kWindowClassName[] = L"JaliumSoftwarePresentRegressionWindow";

LRESULT CALLBACK PresentTestWindowProc(
    HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam)
{
    switch (message) {
    case WM_ERASEBKGND:
        return 1;
    case WM_PAINT:
    {
        PAINTSTRUCT paint{};
        BeginPaint(hwnd, &paint);
        EndPaint(hwnd, &paint);
        return 0;
    }
    default:
        return DefWindowProcW(hwnd, message, wParam, lParam);
    }
}

class ScopedEnvironmentVariable {
public:
    ScopedEnvironmentVariable(const char* name, const char* value)
        : name_(name)
    {
        const char* previous = std::getenv(name);
        if (previous) {
            hadPrevious_ = true;
            previous_ = previous;
        }
        valid_ = _putenv_s(name, value ? value : "") == 0;
    }

    ~ScopedEnvironmentVariable()
    {
        (void)_putenv_s(
            name_.c_str(), hadPrevious_ ? previous_.c_str() : "");
    }

    bool IsValid() const { return valid_; }

private:
    std::string name_;
    std::string previous_;
    bool hadPrevious_ = false;
    bool valid_ = false;
};

class ScopedWindow {
public:
    bool Create(int width, int height)
    {
        instance_ = GetModuleHandleW(nullptr);
        WNDCLASSW windowClass{};
        windowClass.lpfnWndProc = PresentTestWindowProc;
        windowClass.hInstance = instance_;
        windowClass.lpszClassName = kWindowClassName;
        atom_ = RegisterClassW(&windowClass);
        if (!atom_ && GetLastError() != ERROR_CLASS_ALREADY_EXISTS)
            return false;

        const int x = GetSystemMetrics(SM_XVIRTUALSCREEN);
        const int y = GetSystemMetrics(SM_YVIRTUALSCREEN);
        hwnd_ = CreateWindowExW(
            WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
            kWindowClassName,
            L"",
            WS_POPUP,
            x, y, width, height,
            nullptr, nullptr, instance_, nullptr);
        if (!hwnd_) return false;

        ShowWindow(hwnd_, SW_SHOWNOACTIVATE);
        UpdateWindow(hwnd_);
        return true;
    }

    ~ScopedWindow()
    {
        if (hwnd_) DestroyWindow(hwnd_);
        if (atom_) UnregisterClassW(kWindowClassName, instance_);
    }

    HWND Get() const { return hwnd_; }

private:
    HINSTANCE instance_ = nullptr;
    ATOM atom_ = 0;
    HWND hwnd_ = nullptr;
};

struct ContextDeleter {
    void operator()(JaliumContext* context) const
    {
        jalium_context_destroy(context);
    }
};

struct RenderTargetDeleter {
    void operator()(JaliumRenderTarget* target) const
    {
        jalium_render_target_destroy(target);
    }
};

using ContextPtr = std::unique_ptr<JaliumContext, ContextDeleter>;
using RenderTargetPtr = std::unique_ptr<JaliumRenderTarget, RenderTargetDeleter>;

bool DrawClear(
    JaliumRenderTarget* target,
    float red, float green, float blue, float alpha)
{
    if (jalium_render_target_begin_draw(target) != JALIUM_OK) return false;
    jalium_render_target_clear(target, red, green, blue, alpha);
    return jalium_render_target_end_draw(target) == JALIUM_OK;
}

bool ExpectSingleFramebuffer(
    JaliumRenderTarget* target, int width, int height, const char* stage)
{
    JaliumGpuStats stats{};
    if (jalium_render_target_query_gpu_stats(target, &stats) != JALIUM_OK) {
        std::cerr << "  " << stage << ": QueryGpuStats failed\n";
        return false;
    }

    const int64_t expectedBytes =
        static_cast<int64_t>(width) * static_cast<int64_t>(height) * 4;
    if (stats.textureCount == 1 &&
        stats.textureBytes == expectedBytes &&
        stats.softwareCacheBytes == expectedBytes &&
        stats.softwareWorkerCount == 0) {
        return true;
    }

    std::cerr << "  " << stage
              << ": textures=" << stats.textureCount
              << " texture-bytes=" << stats.textureBytes
              << " cache-bytes=" << stats.softwareCacheBytes
              << " workers=" << stats.softwareWorkerCount
              << " expected-bytes=" << expectedBytes << '\n';
    return false;
}

bool ExpectWindowPixel(
    HWND hwnd, int x, int y,
    int expectedRed, int expectedGreen, int expectedBlue,
    const char* stage)
{
    GdiFlush();
    HDC dc = GetDC(hwnd);
    if (!dc) {
        std::cerr << "  " << stage << ": GetDC failed\n";
        return false;
    }
    const COLORREF pixel = GetPixel(dc, x, y);
    ReleaseDC(hwnd, dc);
    if (pixel == CLR_INVALID) {
        std::cerr << "  " << stage << ": GetPixel failed\n";
        return false;
    }

    constexpr int tolerance = 2;
    const int red = GetRValue(pixel);
    const int green = GetGValue(pixel);
    const int blue = GetBValue(pixel);
    if (std::abs(red - expectedRed) <= tolerance &&
        std::abs(green - expectedGreen) <= tolerance &&
        std::abs(blue - expectedBlue) <= tolerance) {
        return true;
    }

    std::cerr << "  " << stage
              << ": pixel=(" << red << ',' << green << ',' << blue << ')'
              << " expected=(" << expectedRed << ',' << expectedGreen
              << ',' << expectedBlue << ")\n";
    return false;
}

bool ExpectCompositionReadback(JaliumRenderTarget* target)
{
    constexpr int width = 2;
    constexpr int height = 2;
    if (jalium_render_target_resize(target, width, height) != JALIUM_OK ||
        jalium_render_target_request_readback(target) != JALIUM_OK ||
        !DrawClear(target, 0.5f, 0.0f, 0.0f, 0.5f)) {
        std::cerr << "  failed to capture composition-alpha framebuffer\n";
        return false;
    }

    std::vector<uint8_t> pixels(static_cast<size_t>(width) * height * 4u);
    int32_t capturedWidth = 0;
    int32_t capturedHeight = 0;
    if (jalium_render_target_fetch_readback(
            target,
            pixels.data(), width * 4,
            &capturedWidth, &capturedHeight) != JALIUM_OK ||
        capturedWidth != width || capturedHeight != height) {
        std::cerr << "  failed to fetch composition-alpha framebuffer\n";
        return false;
    }

    // Readback is raw BGRA. A composition clear receives premultiplied colour,
    // so half-alpha red is B=0, G=0, R=128, A=128 in every pixel.
    for (size_t pixel = 0; pixel < pixels.size(); pixel += 4) {
        if (pixels[pixel + 0] != 0 ||
            pixels[pixel + 1] != 0 ||
            std::abs(static_cast<int>(pixels[pixel + 2]) - 128) > 1 ||
            std::abs(static_cast<int>(pixels[pixel + 3]) - 128) > 1) {
            std::cerr << "  composition-alpha BGRA=("
                      << static_cast<int>(pixels[pixel + 0]) << ','
                      << static_cast<int>(pixels[pixel + 1]) << ','
                      << static_cast<int>(pixels[pixel + 2]) << ','
                      << static_cast<int>(pixels[pixel + 3]) << ")\n";
            return false;
        }
    }
    return true;
}

bool TestDirectFramebufferPresent()
{
    ScopedEnvironmentVariable backendOverride(
        "JALIUM_RENDER_BACKEND", "software");
    ScopedEnvironmentVariable threadOverride("JALIUM_SOFTWARE_THREADS", "8");
    if (!backendOverride.IsValid() || !threadOverride.IsValid()) return false;

    // Keep the native test window small while exercising the real default
    // logical surface size. GDI clips presentation to the client rectangle, but
    // the backend still allocates and uploads the complete framebuffer.
    constexpr int windowWidth = 96;
    constexpr int windowHeight = 64;
    constexpr int initialWidth = 800;
    constexpr int initialHeight = 600;
    ScopedWindow window;
    if (!window.Create(windowWidth, windowHeight)) {
        std::cerr << "  failed to create Win32 test window\n";
        return false;
    }

    jalium_software_init();
    ContextPtr context(jalium_context_create(JALIUM_BACKEND_SOFTWARE));
    if (!context ||
        jalium_context_get_backend(context.get()) != JALIUM_BACKEND_SOFTWARE) {
        std::cerr << "  failed to create Software context\n";
        return false;
    }

    RenderTargetPtr target(jalium_render_target_create_for_composition(
        context.get(), window.Get(), initialWidth, initialHeight));
    if (!target) {
        std::cerr << "  failed to create Software composition target\n";
        return false;
    }

    jalium_render_target_set_full_invalidation(target.get());
    if (!DrawClear(target.get(), 0.8f, 0.1f, 0.05f, 1.0f) ||
        !ExpectWindowPixel(window.Get(), 48, 32, 204, 26, 13, "full-present") ||
        !ExpectSingleFramebuffer(
            target.get(), initialWidth, initialHeight, "full-present")) {
        return false;
    }

    jalium_render_target_add_dirty_rect(target.get(), 20, 15, 16, 12);
    if (!DrawClear(target.get(), 0.05f, 0.75f, 0.1f, 1.0f) ||
        !ExpectWindowPixel(window.Get(), 25, 20, 13, 191, 26, "dirty-inside") ||
        !ExpectWindowPixel(window.Get(), 5, 5, 204, 26, 13, "dirty-outside") ||
        !ExpectSingleFramebuffer(
            target.get(), initialWidth, initialHeight, "dirty-present")) {
        return false;
    }

    constexpr int highDpiWidth = 1600;
    constexpr int highDpiHeight = 1200;
    if (jalium_render_target_resize(
            target.get(), highDpiWidth, highDpiHeight) != JALIUM_OK) {
        std::cerr << "  failed to resize high-DPI target\n";
        return false;
    }
    jalium_render_target_set_dpi(target.get(), 192.0f, 192.0f);
    jalium_render_target_set_full_invalidation(target.get());
    if (!DrawClear(target.get(), 0.1f, 0.2f, 0.85f, 0.5f) ||
        !ExpectWindowPixel(window.Get(), 48, 32, 26, 51, 217, "high-dpi") ||
        !ExpectSingleFramebuffer(
            target.get(), highDpiWidth, highDpiHeight, "high-dpi")) {
        return false;
    }

    return ExpectCompositionReadback(target.get());
}

} // namespace

int main()
{
    if (!TestDirectFramebufferPresent()) {
        std::cerr << "FAIL: Software Win32 direct presentation regression\n";
        return 1;
    }

    std::cout << "PASS: Software Win32 present preserves damage, DPI, composition alpha, and one framebuffer\n";
    return 0;
}

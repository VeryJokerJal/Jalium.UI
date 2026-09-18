#include "jalium_api.h"

#ifdef _WIN32
#include <Windows.h>
#include <objbase.h>
#endif

#include <algorithm>
#include <array>
#include <cstdint>
#include <iostream>
#include <memory>
#include <vector>

extern "C" void jalium_software_init();

namespace {

#ifdef _WIN32
class ScopedComApartment {
public:
    ScopedComApartment()
        : result_(CoInitializeEx(nullptr, COINIT_MULTITHREADED))
    {
    }

    ~ScopedComApartment()
    {
        if (SUCCEEDED(result_)) CoUninitialize();
    }

    bool IsReady() const { return SUCCEEDED(result_); }
    HRESULT Result() const { return result_; }

private:
    HRESULT result_;
};
#endif

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

struct BitmapDeleter {
    void operator()(JaliumImage* bitmap) const
    {
        jalium_bitmap_destroy(bitmap);
    }
};

using ContextPtr = std::unique_ptr<JaliumContext, ContextDeleter>;
using RenderTargetPtr = std::unique_ptr<JaliumRenderTarget, RenderTargetDeleter>;
using BitmapPtr = std::unique_ptr<JaliumImage, BitmapDeleter>;

// 2x2 RGBA PNG, row-major: opaque red, green, blue, white.
constexpr std::array<uint8_t, 82> kOpaquePng = {
    137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82,
    0, 0, 0, 2, 0, 0, 0, 2, 8, 6, 0, 0, 0, 114, 182, 13, 36,
    0, 0, 0, 25, 73, 68, 65, 84, 120, 218, 5, 193, 1, 13, 0, 0,
    12, 195, 32, 150, 220, 191, 229, 30, 68, 210, 77, 194, 3, 62, 255,
    6, 0, 19, 49, 59, 121, 0, 0, 0, 0, 73, 69, 78, 68, 174, 66,
    96, 130
};

constexpr std::array<uint8_t, 16> kExpectedBgra = {
    0, 0, 255, 255,      // opaque red
    0, 255, 0, 255,      // opaque green
    255, 0, 0, 255,      // opaque blue
    255, 255, 255, 255   // opaque white
};

bool ExpectResult(JaliumResult actual, JaliumResult expected, const char* stage)
{
    if (actual == expected) return true;
    std::cerr << "FAIL: " << stage << " returned " << static_cast<int>(actual)
              << ", expected " << static_cast<int>(expected) << '\n';
    return false;
}

RenderTargetPtr CreateOffscreenTarget(JaliumContext* context)
{
    JaliumSurfaceDescriptor surface{};
    surface.platform = JALIUM_PLATFORM_UNKNOWN;
    surface.kind = JALIUM_SURFACE_KIND_NATIVE_WINDOW;
    surface.handle0 = 1;
    return RenderTargetPtr(
        jalium_render_target_create_for_surface(context, &surface, 2, 2));
}

} // namespace

int main()
{
#ifndef _WIN32
    std::cout << "SKIP: WIC memory decode is Windows-only\n";
    return 0;
#else
    ScopedComApartment apartment;
    if (!apartment.IsReady()) {
        std::cerr << "FAIL: CoInitializeEx returned 0x" << std::hex
                  << static_cast<unsigned long>(apartment.Result()) << '\n';
        return 1;
    }

    jalium_software_init();
    ContextPtr context(jalium_context_create(JALIUM_BACKEND_SOFTWARE));
    if (!context ||
        jalium_context_get_backend(context.get()) != JALIUM_BACKEND_SOFTWARE) {
        std::cerr << "FAIL: could not create software context\n";
        return 1;
    }

    auto encoded = kOpaquePng;
    const auto original = encoded;
    BitmapPtr bitmap(jalium_bitmap_create_from_memory(
        context.get(), encoded.data(), static_cast<uint32_t>(encoded.size())));
    if (!bitmap) {
        std::cerr << "FAIL: WIC did not decode the embedded PNG\n";
        return 1;
    }
    if (encoded != original) {
        std::cerr << "FAIL: encoded const input was modified\n";
        return 1;
    }
    if (jalium_bitmap_get_width(bitmap.get()) != 2 ||
        jalium_bitmap_get_height(bitmap.get()) != 2) {
        std::cerr << "FAIL: decoded bitmap dimensions differ\n";
        return 1;
    }

    // The decode contract is synchronous. Overwrite the caller-owned encoded
    // buffer before drawing; the returned bitmap must remain byte-exact.
    std::fill(encoded.begin(), encoded.end(), 0xA5);

    RenderTargetPtr target(CreateOffscreenTarget(context.get()));
    if (!target) {
        std::cerr << "FAIL: could not create offscreen software target\n";
        return 1;
    }
    if (!ExpectResult(
            jalium_render_target_request_readback(target.get()), JALIUM_OK,
            "request readback") ||
        !ExpectResult(
            jalium_render_target_begin_draw(target.get()), JALIUM_OK,
            "BeginDraw")) {
        return 1;
    }

    jalium_render_target_clear(target.get(), 0.0f, 0.0f, 0.0f, 0.0f);
    jalium_draw_bitmap_ex(
        target.get(), bitmap.get(), 0.0f, 0.0f, 2.0f, 2.0f, 1.0f,
        JALIUM_BITMAP_SCALING_NEAREST_NEIGHBOR);
    if (!ExpectResult(
            jalium_render_target_end_draw(target.get()), JALIUM_OK,
            "EndDraw")) {
        return 1;
    }

    int32_t width = 0;
    int32_t height = 0;
    if (!ExpectResult(
            jalium_render_target_fetch_readback(
                target.get(), nullptr, 0, &width, &height),
            JALIUM_OK, "readback size query") ||
        width != 2 || height != 2) {
        std::cerr << "FAIL: readback dimensions differ\n";
        return 1;
    }

    std::vector<uint8_t> pixels(kExpectedBgra.size());
    if (!ExpectResult(
            jalium_render_target_fetch_readback(
                target.get(), pixels.data(), 2u * 4u, &width, &height),
            JALIUM_OK, "readback fetch") ||
        !std::equal(pixels.begin(), pixels.end(), kExpectedBgra.begin())) {
        std::cerr << "FAIL: decoded BGRA pixels differ";
        for (uint8_t value : pixels) {
            std::cerr << ' ' << static_cast<unsigned>(value);
        }
        std::cerr << '\n';
        return 1;
    }

    BitmapPtr nullInput(
        jalium_bitmap_create_from_memory(context.get(), nullptr, 1));
    BitmapPtr emptyInput(
        jalium_bitmap_create_from_memory(context.get(), original.data(), 0));
    if (nullInput || emptyInput) {
        std::cerr << "FAIL: invalid input guard regressed\n";
        return 1;
    }

    std::cout << "PASS: WIC memory decode preserved const input, synchronous "
                 "lifetime, dimensions, and exact BGRA pixels\n";
    return 0;
#endif
}

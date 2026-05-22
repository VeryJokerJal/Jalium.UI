#include "jalium_text_options.h"

#include <atomic>
#include <cstdio>

#if defined(_WIN32)
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN  // cmake already defines this; guard so MSVC C4005 stays quiet
#endif
#include <windows.h>
#endif

namespace jalium {
namespace text_options {
namespace {

// Default: AUTO. Each backend resolves Auto to its platform-native choice
// when reading the value so we don't have to know the host OS at init time.
std::atomic<int32_t>  g_mode{JALIUM_TEXT_AA_AUTO};
std::atomic<uint64_t> g_generation{0};

}  // namespace

int32_t ResolveMode(int32_t mode) noexcept {
    // Auto resolves to the platform's expected default: ClearType on Windows
    // (the WPF / Win32 convention every desktop app honours), Grayscale on
    // every other platform (macOS, Android, iOS, Linux — none of them ship
    // sub-pixel text rendering by default). Grayscale stays available on
    // Windows for callers that need it (high-DPI authoring tools, off-screen
    // render targets that get resampled) via
    //     TextOptions.ProcessTextRenderingMode = TextRenderingMode.Grayscale;
    // but we don't impose it as the framework default because Windows users
    // expect their text to look exactly like every other desktop app on the
    // same OS.
    if (mode == JALIUM_TEXT_AA_AUTO) {
#if defined(_WIN32)
        return JALIUM_TEXT_AA_CLEARTYPE;
#else
        return JALIUM_TEXT_AA_GRAYSCALE;
#endif
    }
    if (mode < JALIUM_TEXT_AA_AUTO || mode > JALIUM_TEXT_AA_CLEARTYPE) {
#if defined(_WIN32)
        return JALIUM_TEXT_AA_CLEARTYPE;
#else
        return JALIUM_TEXT_AA_GRAYSCALE;
#endif
    }
    return mode;
}

}  // namespace text_options
}  // namespace jalium

extern "C" {

JALIUM_API void jalium_text_set_global_antialias_mode(int32_t mode) {
    using namespace jalium::text_options;
    if (mode < JALIUM_TEXT_AA_AUTO || mode > JALIUM_TEXT_AA_CLEARTYPE) {
        mode = JALIUM_TEXT_AA_AUTO;
    }
    int32_t previous = g_mode.exchange(mode, std::memory_order_release);
    if (previous != mode) {
        // Bump the generation so live atlases notice the change on their next
        // frame and reset their cached glyph entries. This is the only signal
        // that crosses the C ABI — backends compare lastSeenGen against this.
        g_generation.fetch_add(1, std::memory_order_acq_rel);

#if defined(_WIN32)
        // Always-on trace: there's no harm in one line per mode flip and the
        // information is critical for diagnosing "I set Grayscale but still
        // see ClearType fringe" reports — confirms the P/Invoke reached the
        // native dll, with the resolved-to value the backend will actually use.
        char buf[160];
        const int resolved = ResolveMode(mode);
        const char* names[] = { "Auto", "Aliased", "Grayscale", "ClearType" };
        const char* nm  = (mode     >= 0 && mode     <= 3) ? names[mode]     : "?";
        const char* res = (resolved >= 0 && resolved <= 3) ? names[resolved] : "?";
        std::snprintf(buf, sizeof(buf),
            "[jalium text] antialias mode -> %s (resolved=%s) gen=%llu\n",
            nm, res, (unsigned long long)g_generation.load(std::memory_order_acquire));
        OutputDebugStringA(buf);
#endif
    }
}

JALIUM_API int32_t jalium_text_get_global_antialias_mode(void) {
    return jalium::text_options::g_mode.load(std::memory_order_acquire);
}

JALIUM_API uint64_t jalium_text_get_antialias_generation(void) {
    return jalium::text_options::g_generation.load(std::memory_order_acquire);
}

}  // extern "C"

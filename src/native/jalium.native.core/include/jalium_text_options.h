#pragma once

#include <cstdint>
#include "jalium_api.h"

// Global text-rendering options. Lives in jalium.native.core so that every
// backend dll (d3d12, vulkan, software) reads the same atomic state — otherwise
// each dll would carry its own value and the managed RenderOptions surface
// would have to be plumbed N times. Backends read the current mode each time
// a glyph would be rasterized and reset their atlas if the mode changed since
// the last frame, so ClearType and Grayscale can coexist across runs without
// per-element plumbing.

#ifdef __cplusplus
extern "C" {
#endif

// Mirrors managed Jalium.UI.Media.TextRenderingMode.
//   0 = Auto (platform default — ClearType on Windows, Grayscale on mobile)
//   1 = Aliased (bilevel)
//   2 = Grayscale
//   3 = ClearType
typedef enum JaliumTextAntialiasMode {
    JALIUM_TEXT_AA_AUTO      = 0,
    JALIUM_TEXT_AA_ALIASED   = 1,
    JALIUM_TEXT_AA_GRAYSCALE = 2,
    JALIUM_TEXT_AA_CLEARTYPE = 3,
} JaliumTextAntialiasMode;

/// Sets the global text antialias mode. Subsequent glyph rasterizations use
/// this mode; existing atlas entries from a previous mode are invalidated
/// on the next frame so a swap from ClearType to Grayscale (or vice versa)
/// fully takes effect within ~one frame.
JALIUM_API void jalium_text_set_global_antialias_mode(int32_t mode);

/// Reads the current global text antialias mode.
JALIUM_API int32_t jalium_text_get_global_antialias_mode(void);

/// Monotonically-increasing generation token that is bumped each time the
/// global antialias mode is set. Backends compare against their cached value
/// to detect a mode change and invalidate their glyph atlases.
JALIUM_API uint64_t jalium_text_get_antialias_generation(void);

#ifdef __cplusplus
}  // extern "C"

namespace jalium {
namespace text_options {

/// Resolves Auto to a platform-appropriate concrete mode. Windows → ClearType,
/// other platforms → Grayscale. Aliased / Grayscale / ClearType pass through.
JALIUM_API int32_t ResolveMode(int32_t mode) noexcept;

}  // namespace text_options
}  // namespace jalium

#endif  // __cplusplus

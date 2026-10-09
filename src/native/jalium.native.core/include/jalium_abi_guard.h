#pragma once

#include <cstddef>
#include <cstdint>
#include <utility>

namespace jalium {

// Public C ABI array/string limits.  They are intentionally generous for UI
// workloads, while preventing a corrupt or hostile caller from turning a
// 32-bit count into an unbounded read, multi-gigabyte allocation, or wrapped
// per-element offset in a backend.
inline constexpr uint32_t kMaxEncodedBitmapBytes = 512u * 1024u * 1024u;
inline constexpr uint32_t kMaxGradientStopCount = 64u * 1024u;
inline constexpr uint32_t kMaxFontFallbackCount = 4096u;
inline constexpr uint32_t kMaxFontUnicodeRangeCount = 65536u;
inline constexpr uint32_t kMaxFontCharacterCount = 65536u;
inline constexpr uint32_t kMaxEllipseBatchCount = 1024u * 1024u;
inline constexpr uint32_t kMaxPathFloatCount = 16u * 1024u * 1024u;
inline constexpr uint32_t kMaxDashFloatCount = 1024u * 1024u;
inline constexpr uint32_t kMaxShaderBytecodeBytes = 64u * 1024u * 1024u;
inline constexpr uint32_t kMaxShaderSourceBytes = 4u * 1024u * 1024u;
inline constexpr uint32_t kMaxShaderKeyBytes = 4096u;
inline constexpr uint32_t kMaxShaderConstantFloatCount = 64u * 1024u;
inline constexpr int32_t kMaxInkPointCount = 4 * 1024 * 1024;
inline constexpr int32_t kMaxInkExtraParameterBytes = 16 * 1024 * 1024;
inline constexpr int32_t kMaxLiquidGlassNeighborCount = 4096;

inline bool CStringLengthBounded(
    const char* value,
    uint32_t maxLength,
    uint32_t* length) noexcept
{
    if (!value || !length) return false;
    for (uint32_t i = 0; i < maxLength; ++i) {
        if (value[i] == '\0') {
            *length = i;
            return true;
        }
    }
    *length = 0;
    return false;
}

// Runs one backend operation with an optional transient transform.  Container
// pushes in the built-in backends provide the strong exception guarantee, so
// a successful PushTransform is the exact point at which cleanup becomes
// necessary.  The public draw ABI is void/best-effort, hence there is no error
// channel to preserve; the important contract is that no exception escapes
// and renderer state is restored before returning.
template <typename Target, typename Callback>
inline void InvokeWithOptionalTransformNoexcept(
    Target* target,
    const float* transform,
    Callback&& callback) noexcept
{
    if (!target) return;

    bool pushed = false;
    try {
        if (transform) {
            target->PushTransform(transform);
            pushed = true;
        }
        std::forward<Callback>(callback)();
    } catch (...) {
        // The void C ABI intentionally drops the failed draw call.
    }

    if (pushed) {
        try {
            target->PopTransform();
        } catch (...) {
            // Preserve the ABI boundary if a third-party backend violates the
            // built-in backends' non-throwing PopTransform contract.
        }
    }
}

} // namespace jalium

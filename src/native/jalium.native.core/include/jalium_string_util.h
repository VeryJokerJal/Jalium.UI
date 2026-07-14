#pragma once

#include <cstdint>
#include <cstddef>
#include <string>

// ============================================================================
// Cross-platform string conversion utilities
//
// .NET always passes text as UTF-16 (2 bytes per code unit). On Windows,
// wchar_t is also 2 bytes so the data can be used directly. On Linux/Android,
// wchar_t is 4 bytes (UTF-32), so we must convert from the UTF-16 payload
// that the managed P/Invoke layer sends.
//
// All public C API functions that accept `const wchar_t*` from managed code
// should use these helpers to obtain correctly-typed wchar_t strings.
// ============================================================================

namespace jalium {

#if defined(_WIN32)

// On Windows, wchar_t == uint16_t, so the managed UTF-16 data is already wchar_t.
inline const wchar_t* ManagedStringPtr(const wchar_t* s) { return s; }
inline std::wstring ManagedToWString(const wchar_t* s, uint32_t len) {
    return std::wstring(s, len);
}

#else

// On Linux/Android, wchar_t is 4 bytes. The parameter actually points to
// packed UTF-16 code units (2 bytes each). Reinterpret and convert.
inline std::wstring ManagedToWString(const void* utf16_ptr, uint32_t len) {
    const uint16_t* s = reinterpret_cast<const uint16_t*>(utf16_ptr);
    std::wstring result;
    result.reserve(len);
    for (uint32_t i = 0; i < len; i++) {
        uint16_t c = s[i];
        // Handle UTF-16 surrogate pairs
        if (c >= 0xD800 && c <= 0xDBFF && i + 1 < len) {
            uint16_t lo = s[i + 1];
            if (lo >= 0xDC00 && lo <= 0xDFFF) {
                uint32_t cp = ((uint32_t)(c - 0xD800) << 10) + (lo - 0xDC00) + 0x10000;
                result += static_cast<wchar_t>(cp);
                i++;
                continue;
            }
        }
        result += static_cast<wchar_t>(c);
    }
    return result;
}

// Hit-testing APIs expose indices back to managed code. The public ABI uses
// UTF-16 offsets while std::wstring uses UTF-32 offsets on Linux/Android, so
// those indices must be translated in both directions around the backend call.
// An offset in the middle of a surrogate pair maps to that scalar's leading
// edge; valid caret offsets before and after the pair round-trip exactly.
inline uint32_t ManagedUtf16IndexToWStringIndex(
    const void* utf16_ptr,
    uint32_t len,
    uint32_t utf16_index) {
    if (!utf16_ptr) return 0;
    const uint16_t* s = reinterpret_cast<const uint16_t*>(utf16_ptr);
    const uint32_t target = utf16_index < len ? utf16_index : len;
    uint32_t utf16 = 0;
    uint32_t wide = 0;
    while (utf16 < target) {
        const uint16_t ch = s[utf16];
        if (ch >= 0xD800 && ch <= 0xDBFF && utf16 + 1 < len) {
            const uint16_t lo = s[utf16 + 1];
            if (lo >= 0xDC00 && lo <= 0xDFFF) {
                if (utf16 + 1 >= target) break;
                utf16 += 2;
                ++wide;
                continue;
            }
        }
        ++utf16;
        ++wide;
    }
    return wide;
}

inline uint32_t ManagedWStringIndexToUtf16Index(
    const void* utf16_ptr,
    uint32_t len,
    uint32_t wide_index) {
    if (!utf16_ptr) return 0;
    const uint16_t* s = reinterpret_cast<const uint16_t*>(utf16_ptr);
    uint32_t utf16 = 0;
    uint32_t wide = 0;
    while (utf16 < len && wide < wide_index) {
        const uint16_t ch = s[utf16];
        if (ch >= 0xD800 && ch <= 0xDBFF && utf16 + 1 < len) {
            const uint16_t lo = s[utf16 + 1];
            if (lo >= 0xDC00 && lo <= 0xDFFF) {
                utf16 += 2;
                ++wide;
                continue;
            }
        }
        ++utf16;
        ++wide;
    }
    return utf16;
}

// Convenience for null-terminated strings
inline std::wstring ManagedToWString(const void* utf16_ptr) {
    if (!utf16_ptr) return {};
    const uint16_t* s = reinterpret_cast<const uint16_t*>(utf16_ptr);
    uint32_t len = 0;
    while (s[len]) len++;
    return ManagedToWString(utf16_ptr, len);
}

#endif

} // namespace jalium

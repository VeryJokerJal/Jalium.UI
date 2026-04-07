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

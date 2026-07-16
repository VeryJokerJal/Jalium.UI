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

// Copies a null-terminated wide string into a fixed-width UTF-16 ABI buffer.
// This is a code-unit copy on Windows and a UTF-32 -> UTF-16 conversion on
// Unix-like platforms, where wchar_t is four bytes.
template <size_t N>
inline void WideToFixedUtf16(const wchar_t* source, uint16_t (&destination)[N]) noexcept {
    static_assert(N > 0);
    for (size_t i = 0; i < N; ++i) destination[i] = 0;
    if (!source) return;

    size_t output = 0;
    while (*source && output + 1 < N) {
        uint32_t codePoint = static_cast<uint32_t>(*source++);
        if constexpr (sizeof(wchar_t) == sizeof(uint16_t)) {
            const uint16_t codeUnit = static_cast<uint16_t>(codePoint);
            const uint16_t nextCodeUnit = static_cast<uint16_t>(*source);
            if (codeUnit >= 0xD800u && codeUnit <= 0xDBFFu &&
                nextCodeUnit >= 0xDC00u && nextCodeUnit <= 0xDFFFu) {
                if (output + 2 >= N) break;
                destination[output++] = codeUnit;
                destination[output++] = static_cast<uint16_t>(*source++);
            } else if (codeUnit >= 0xD800u && codeUnit <= 0xDFFFu) {
                destination[output++] = 0xFFFDu;
            } else {
                destination[output++] = codeUnit;
            }
        } else {
            if (codePoint > 0x10FFFFu || (codePoint >= 0xD800u && codePoint <= 0xDFFFu)) {
                codePoint = 0xFFFDu;
            }
            if (codePoint <= 0xFFFFu) {
                destination[output++] = static_cast<uint16_t>(codePoint);
            } else {
                if (output + 2 >= N) break;
                codePoint -= 0x10000u;
                destination[output++] = static_cast<uint16_t>(0xD800u + (codePoint >> 10));
                destination[output++] = static_cast<uint16_t>(0xDC00u + (codePoint & 0x3FFu));
            }
        }
    }
    destination[output] = 0;
}

// Converts a null-terminated UTF-8 string into a fixed-width UTF-16 ABI
// buffer without depending on the process locale.
template <size_t N>
inline void Utf8ToFixedUtf16(const char* source, uint16_t (&destination)[N]) noexcept {
    static_assert(N > 0);
    for (size_t i = 0; i < N; ++i) destination[i] = 0;
    if (!source) return;

    size_t input = 0;
    size_t output = 0;
    while (source[input] != '\0' && output + 1 < N) {
        const uint8_t lead = static_cast<uint8_t>(source[input]);
        uint32_t codePoint = 0;
        uint32_t minimum = 0;
        size_t sequenceLength = 1;
        bool valid = true;

        if (lead < 0x80u) {
            codePoint = lead;
        } else if ((lead & 0xE0u) == 0xC0u) {
            codePoint = lead & 0x1Fu;
            minimum = 0x80u;
            sequenceLength = 2;
        } else if ((lead & 0xF0u) == 0xE0u) {
            codePoint = lead & 0x0Fu;
            minimum = 0x800u;
            sequenceLength = 3;
        } else if ((lead & 0xF8u) == 0xF0u) {
            codePoint = lead & 0x07u;
            minimum = 0x10000u;
            sequenceLength = 4;
        } else {
            valid = false;
        }

        if (sequenceLength > 1) {
            for (size_t i = 1; i < sequenceLength; ++i) {
                const uint8_t next = static_cast<uint8_t>(source[input + i]);
                if (next == 0 || (next & 0xC0u) != 0x80u) {
                    valid = false;
                    break;
                }
                codePoint = (codePoint << 6) | (next & 0x3Fu);
            }
            if (codePoint < minimum || codePoint > 0x10FFFFu ||
                (codePoint >= 0xD800u && codePoint <= 0xDFFFu)) {
                valid = false;
            }
        }

        if (!valid) {
            codePoint = 0xFFFDu;
            ++input;
        } else {
            input += sequenceLength;
        }

        if (codePoint <= 0xFFFFu) {
            destination[output++] = static_cast<uint16_t>(codePoint);
        } else {
            if (output + 2 >= N) break;
            codePoint -= 0x10000u;
            destination[output++] = static_cast<uint16_t>(0xD800u + (codePoint >> 10));
            destination[output++] = static_cast<uint16_t>(0xDC00u + (codePoint & 0x3FFu));
        }
    }
    destination[output] = 0;
}

template <size_t N>
inline std::wstring FixedUtf16ToWide(const uint16_t (&source)[N]) {
    std::wstring result;
    result.reserve(N);
    for (size_t i = 0; i < N && source[i] != 0; ++i) {
        uint32_t codePoint = source[i];
        if constexpr (sizeof(wchar_t) == sizeof(uint16_t)) {
            result.push_back(static_cast<wchar_t>(codePoint));
        } else {
            if (codePoint >= 0xD800u && codePoint <= 0xDBFFu &&
                i + 1 < N && source[i + 1] >= 0xDC00u && source[i + 1] <= 0xDFFFu) {
                const uint32_t low = source[++i];
                codePoint = 0x10000u + ((codePoint - 0xD800u) << 10) + (low - 0xDC00u);
            } else if (codePoint >= 0xD800u && codePoint <= 0xDFFFu) {
                codePoint = 0xFFFDu;
            }
            result.push_back(static_cast<wchar_t>(codePoint));
        }
    }
    return result;
}

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

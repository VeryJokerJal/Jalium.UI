#pragma once

#include <stdint.h>

#if defined(_WIN32)
    #if defined(JALIUM_STATIC)
        #define JALIUM_TEXT_API
    #elif defined(JALIUM_TEXT_EXPORTS)
        #define JALIUM_TEXT_API __declspec(dllexport)
    #else
        #define JALIUM_TEXT_API __declspec(dllimport)
    #endif
#else
    #define JALIUM_TEXT_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

/// Returns the number of system font-family names exposed by the platform
/// font discovery service. Linux uses Fontconfig and returns zero if its
/// current configuration cannot be initialized.
JALIUM_TEXT_API int32_t jalium_text_get_system_font_family_count(void);

/// Copies one UTF-8 system font-family name into `buffer`.
///
/// The return value is the required byte count including the trailing NUL.
/// Passing a null/zero-length buffer is the supported size-query operation.
/// Zero is returned for an invalid index or when font discovery is unavailable.
JALIUM_TEXT_API int32_t jalium_text_copy_system_font_family(
    int32_t index,
    char* buffer,
    int32_t buffer_size);

#ifdef __cplusplus
}
#endif

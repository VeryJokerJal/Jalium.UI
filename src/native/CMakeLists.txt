cmake_minimum_required(VERSION 3.25)
project(JaliumNative VERSION 0.1.0 LANGUAGES CXX)

if(APPLE)
    enable_language(OBJCXX)
endif()

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# ---------------------------------------------------------------------------
# Build flavor: shared (default, NuGet) or static (NativeAOT single-exe)
# ---------------------------------------------------------------------------
option(JALIUM_BUILD_STATIC "Build native libraries as static archives for NativeAOT linking" OFF)

# 排查/诊断用的 native demo 可执行文件（默认关，不影响 NuGet / AOT 产物）。
# 打开方式: cmake -DJALIUM_BUILD_DEMOS=ON ...
option(JALIUM_BUILD_DEMOS "Build native diagnostic demo executables (memprobe, ...)" OFF)

if(JALIUM_BUILD_STATIC)
    set(JALIUM_LIB_TYPE STATIC)
    add_compile_definitions(JALIUM_STATIC)
    # PIC is required if these archives are later combined into a shared image.
    set(CMAKE_POSITION_INDEPENDENT_CODE ON)
else()
    set(JALIUM_LIB_TYPE SHARED)
endif()

# Windows-specific settings
if(WIN32)
    add_compile_definitions(
        UNICODE
        _UNICODE
        WIN32_LEAN_AND_MEAN
        NOMINMAX
    )
endif()

# Output directories
#
# Shared-DLL builds (the default) drop right into src/native/bin/native/<Config>/
# so the Jalium.UI.Interop project picks them up via its <Content Include>. This
# is the canonical location Visual Studio / `dotnet build` / Gallery all read,
# so any cmake build (CLI or VS-integrated) immediately ships its output to
# every downstream consumer without a separate copy step.
#
# Static (NativeAOT) builds stay under the cmake binary dir because they're
# bundled by ConfigureJaliumNativeAot from build-native-static.ps1 instead.
if(JALIUM_BUILD_STATIC)
    set(CMAKE_RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin-static)
    set(CMAKE_LIBRARY_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/lib-static)
    set(CMAKE_ARCHIVE_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/lib-static)
else()
    set(_jalium_native_out "${CMAKE_CURRENT_SOURCE_DIR}/bin/native")
    if(CMAKE_CONFIGURATION_TYPES)
        # Multi-config (Visual Studio, Ninja Multi-Config): each config lands in
        # its own bin/native/<Config>/ subdir so Debug and Release coexist.
        set(CMAKE_RUNTIME_OUTPUT_DIRECTORY "${_jalium_native_out}")
        set(CMAKE_LIBRARY_OUTPUT_DIRECTORY "${_jalium_native_out}")
        set(CMAKE_ARCHIVE_OUTPUT_DIRECTORY "${_jalium_native_out}")
        foreach(_cfg ${CMAKE_CONFIGURATION_TYPES})
            string(TOUPPER "${_cfg}" _cfgU)
            set(CMAKE_RUNTIME_OUTPUT_DIRECTORY_${_cfgU} "${_jalium_native_out}/${_cfg}")
            set(CMAKE_LIBRARY_OUTPUT_DIRECTORY_${_cfgU} "${_jalium_native_out}/${_cfg}")
            set(CMAKE_ARCHIVE_OUTPUT_DIRECTORY_${_cfgU} "${_jalium_native_out}/${_cfg}")
        endforeach()
    else()
        # Single-config generators (e.g. Unix Makefiles): default CMAKE_BUILD_TYPE
        # to Debug so the output still lands somewhere predictable.
        if(NOT CMAKE_BUILD_TYPE)
            set(CMAKE_BUILD_TYPE Debug CACHE STRING "" FORCE)
        endif()
        set(CMAKE_RUNTIME_OUTPUT_DIRECTORY "${_jalium_native_out}/${CMAKE_BUILD_TYPE}")
        set(CMAKE_LIBRARY_OUTPUT_DIRECTORY "${_jalium_native_out}/${CMAKE_BUILD_TYPE}")
        set(CMAKE_ARCHIVE_OUTPUT_DIRECTORY "${_jalium_native_out}/${CMAKE_BUILD_TYPE}")
    endif()
endif()

# Subdirectories — order matters: text must precede vulkan/software for linkage
add_subdirectory(jalium.native.core)
add_subdirectory(jalium.native.platform)

# Cross-platform media (image/video/camera) public ABI + status helpers.
# Per-platform implementation libraries link to this for the C ABI header.
add_subdirectory(jalium.native.media.core)

# ---------------------------------------------------------------------------
# Shared audio TUs: physically live under jalium.native.media.core/src/audio/
# but are compiled INTO each per-platform library (jalium.native.media.{windows,
# apple, linux, android}) so the audio symbols ship inside the same DLL that
# managed P/Invoke loads (`jalium.native.media`). This avoids having to
# re-export symbols across DLL boundaries on Windows or pkg-config across
# .so boundaries on Linux.
# ---------------------------------------------------------------------------
set(JALIUM_AUDIO_SOURCES
    ${CMAKE_CURRENT_SOURCE_DIR}/jalium.native.media.core/src/audio/audio_init.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/jalium.native.media.core/src/audio/audio_stats.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/jalium.native.media.core/src/audio/miniaudio_backend.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/jalium.native.media.core/src/audio/audio_device.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/jalium.native.media.core/src/audio/audio_decoder.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/jalium.native.media.core/src/audio/decoder_wav.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/jalium.native.media.core/src/audio/decoder_flac.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/jalium.native.media.core/src/audio/decoder_mp3.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/jalium.native.media.core/src/audio/decoder_vorbis.cpp
    CACHE INTERNAL "Audio TUs compiled into each per-platform jalium.native.media library"
)
set(JALIUM_AUDIO_PRIVATE_INCLUDES
    ${CMAKE_CURRENT_SOURCE_DIR}/jalium.native.media.core/src/audio
    ${CMAKE_CURRENT_SOURCE_DIR}/thirdparty/miniaudio
    ${CMAKE_CURRENT_SOURCE_DIR}/thirdparty/dr_libs
    ${CMAKE_CURRENT_SOURCE_DIR}/thirdparty/minimp3
    ${CMAKE_CURRENT_SOURCE_DIR}/thirdparty/stb
    CACHE INTERNAL "Private include dirs needed by JALIUM_AUDIO_SOURCES"
)

# Cross-platform text engine (FreeType + HarfBuzz) — must be before backends that link it
if(NOT WIN32)
    add_subdirectory(jalium.native.text)
endif()

if(WIN32)
    add_subdirectory(jalium.native.d3d12)
    add_subdirectory(jalium.native.browser)
    add_subdirectory(jalium.native.media.windows)
elseif(APPLE)
    add_subdirectory(jalium.native.metal)
else()
    # Linux / Android — Vulkan is the primary backend
    add_subdirectory(jalium.native.vulkan)
    if(ANDROID)
        add_subdirectory(jalium.native.media.android)
    endif()
endif()

add_subdirectory(jalium.native.software)

# Static-only aggregation TU that exports jalium_aot_register_all_backends().
# Only meaningful when JALIUM_BUILD_STATIC=ON; pulls every backend into the AOT image.
if(JALIUM_BUILD_STATIC)
    add_subdirectory(jalium.native.aot)
endif()

# ---------------------------------------------------------------------------
# 诊断 demo（opt-in）。只在 Windows 共享构建下有意义：memprobe 链接
# jalium.native.core + jalium.native.d3d12 这两个 SHARED 目标，跑空白窗口
# 排查内存暴涨。静态/AOT 构建不掺和。
# ---------------------------------------------------------------------------
if(JALIUM_BUILD_DEMOS AND WIN32 AND NOT JALIUM_BUILD_STATIC)
    add_subdirectory(jalium.native.demo.memprobe)
endif()

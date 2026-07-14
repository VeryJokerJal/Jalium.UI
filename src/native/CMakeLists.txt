cmake_minimum_required(VERSION 3.25)
project(JaliumNative VERSION 0.1.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# Every Linux shared target starts hidden. Public C functions and the small
# cross-backend C++ surface opt in through JALIUM_*_API, then a per-library GNU
# version script applies the second half of the whitelist at link time.
if(CMAKE_SYSTEM_NAME STREQUAL "Linux")
    set(CMAKE_C_VISIBILITY_PRESET hidden)
    set(CMAKE_CXX_VISIBILITY_PRESET hidden)
    set(CMAKE_VISIBILITY_INLINES_HIDDEN ON)
    include(cmake/JaliumLinuxExports.cmake)
endif()

# NuGet consumes the shared libraries directly from the build output.  Never
# embed the source checkout's absolute path in their RUNPATH: every Jalium
# library dependency is deployed beside its consumer under runtimes/<rid>/native.
if(UNIX AND NOT APPLE)
    set(CMAKE_BUILD_RPATH_USE_ORIGIN ON)
    set(CMAKE_BUILD_RPATH "$ORIGIN")
    set(CMAKE_INSTALL_RPATH "$ORIGIN")
    set(CMAKE_INSTALL_RPATH_USE_LINK_PATH OFF)
    # The NuGet payload is taken straight from the build output rather than a
    # separate `cmake --install` tree. Use the portable install RPATH at link
    # time so CMake cannot append an absolute build-tree search directory.
    set(CMAKE_BUILD_WITH_INSTALL_RPATH ON)
endif()

# MSVC reads source files using the host ANSI code page (e.g. 936/GBK on a
# Chinese Windows install) unless told otherwise. Every source/header here is
# stored as UTF-8 (Chinese comments included), so without this flag any file
# with non-ASCII bytes trips C4819 ("cannot be represented in the current code
# page"). /utf-8 declares the real source AND execution charset as UTF-8 — the
# canonical Microsoft-recommended fix. Applied globally so it also covers the
# vendored single-header thirdparty TUs. Must precede every add_subdirectory().
if(MSVC)
    add_compile_options(/utf-8)
endif()

# ---------------------------------------------------------------------------
# Build flavor: shared (default, NuGet) or static (NativeAOT single-exe)
# ---------------------------------------------------------------------------
option(JALIUM_BUILD_STATIC "Build native libraries as static archives for NativeAOT linking" OFF)

# 排查/诊断用的 native demo 可执行文件（默认关，不影响 NuGet / AOT 产物）。
# 打开方式: cmake -DJALIUM_BUILD_DEMOS=ON ...
option(JALIUM_BUILD_DEMOS "Build native diagnostic demo executables (memprobe, ...)" OFF)

# Also build the Vulkan backend on Windows (it is the primary backend only on
# Linux/Android). Off by default so the normal Windows/NuGet build is unchanged;
# turn on with -DJALIUM_BUILD_VULKAN=ON to compile/link/test the Vulkan backend
# DLL alongside D3D12 (e.g. to validate the InkCanvas GPU pipeline / effects).
option(JALIUM_BUILD_VULKAN "Also build the Vulkan backend on Windows (dev/test)" OFF)

# Native smoke options are supplied on the initial CMake command line by
# eng/linux/build-native.sh. enable_testing() must run at the source root so
# `ctest --test-dir <root>` can discover tests registered by subdirectories;
# calling it only inside those subdirectories silently reports "No tests were
# found" and gives CI a false pass.
if(JALIUM_PLATFORM_BUILD_TESTS OR
   JALIUM_MEDIA_LINUX_BUILD_TESTS OR
   JALIUM_SOFTWARE_BUILD_TESTS OR
   JALIUM_TEXT_BUILD_TESTS)
    enable_testing()
endif()

# ---------------------------------------------------------------------------
# Runtime identifier and output isolation
# ---------------------------------------------------------------------------
# Every shared native build is written below bin/native/<rid>/<Config>.  The
# RID is also validated against CMake's target platform/architecture so an
# Android build can never be labelled (and later packed) as a Linux payload.
# Cross builds should set CMAKE_SYSTEM_PROCESSOR in their toolchain file and
# may set JALIUM_LINUX_LIBC when the compiler triple does not identify musl.
set(JALIUM_NATIVE_RID "" CACHE STRING
    "Portable .NET RID for these native artifacts (for example linux-x64)")
set(JALIUM_LINUX_LIBC "auto" CACHE STRING
    "Linux C library used by the target toolchain: auto, glibc, or musl")
set_property(CACHE JALIUM_LINUX_LIBC PROPERTY STRINGS auto glibc musl)
set(JALIUM_NATIVE_OUTPUT_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/bin/native" CACHE PATH
    "Root directory for RID-isolated shared native artifacts")

function(_jalium_normalize_arch raw_arch output_variable)
    string(TOLOWER "${raw_arch}" _arch)
    if(_arch MATCHES "^(amd64|x86_64|x64)$")
        set(_normalized x64)
    elseif(_arch MATCHES "^(aarch64|arm64|arm64-v8a)$")
        set(_normalized arm64)
    elseif(_arch MATCHES "^(arm|armv7|armv7l|armeabi-v7a)$")
        set(_normalized arm)
    elseif(_arch MATCHES "^(i[3-6]86|x86)$")
        set(_normalized x86)
    else()
        set(_normalized "")
    endif()
    set(${output_variable} "${_normalized}" PARENT_SCOPE)
endfunction()

function(_jalium_detect_linux_libc output_variable)
    string(TOLOWER "${JALIUM_LINUX_LIBC}" _requested_libc)
    if(NOT _requested_libc MATCHES "^(auto|glibc|musl)$")
        message(FATAL_ERROR
            "JALIUM_LINUX_LIBC must be one of: auto, glibc, musl (got '${JALIUM_LINUX_LIBC}')")
    endif()

    set(_compiler_target "${CMAKE_CXX_COMPILER_TARGET}")
    if(_compiler_target STREQUAL "" AND CMAKE_CXX_COMPILER)
        execute_process(
            COMMAND "${CMAKE_CXX_COMPILER}" -dumpmachine
            OUTPUT_VARIABLE _compiler_target
            OUTPUT_STRIP_TRAILING_WHITESPACE
            ERROR_QUIET)
    endif()
    string(TOLOWER "${_compiler_target}" _compiler_target)

    if(_compiler_target MATCHES "musl" OR
       (NOT CMAKE_CROSSCOMPILING AND EXISTS "/etc/alpine-release"))
        set(_toolchain_libc musl)
    elseif(_compiler_target MATCHES "(^|-)gnu($|-)" OR
           _compiler_target MATCHES "linux-gnu")
        set(_toolchain_libc glibc)
    else()
        set(_toolchain_libc "")
    endif()

    if(NOT _requested_libc STREQUAL "auto")
        if(NOT _toolchain_libc STREQUAL "" AND
           NOT _toolchain_libc STREQUAL _requested_libc)
            message(FATAL_ERROR
                "JALIUM_LINUX_LIBC='${_requested_libc}' conflicts with compiler "
                "target '${_compiler_target}' (${_toolchain_libc}). Refusing to "
                "write a mislabelled Linux native payload.")
        endif()
        set(${output_variable} "${_requested_libc}" PARENT_SCOPE)
        return()
    endif()

    if(_toolchain_libc STREQUAL "")
        # glibc is the portable Linux default. Cross toolchains whose triple is
        # ambiguous must opt into musl explicitly rather than being mislabelled.
        set(_detected_libc glibc)
    else()
        set(_detected_libc "${_toolchain_libc}")
    endif()
    set(${output_variable} "${_detected_libc}" PARENT_SCOPE)
endfunction()

if(ANDROID AND CMAKE_ANDROID_ARCH_ABI)
    set(_jalium_target_processor "${CMAKE_ANDROID_ARCH_ABI}")
elseif(CMAKE_GENERATOR_PLATFORM)
    set(_jalium_target_processor "${CMAKE_GENERATOR_PLATFORM}")
elseif(CMAKE_SYSTEM_PROCESSOR)
    set(_jalium_target_processor "${CMAKE_SYSTEM_PROCESSOR}")
else()
    set(_jalium_target_processor "${CMAKE_HOST_SYSTEM_PROCESSOR}")
endif()
_jalium_normalize_arch("${_jalium_target_processor}" _jalium_target_arch)
if(_jalium_target_arch STREQUAL "")
    message(FATAL_ERROR
        "Unsupported native target architecture '${_jalium_target_processor}'. "
        "Use a toolchain that sets CMAKE_SYSTEM_PROCESSOR to x86_64/aarch64, "
        "or another architecture with a defined .NET RID mapping.")
endif()

if(ANDROID)
    set(_jalium_expected_rid_prefix "android-")
elseif(WIN32)
    set(_jalium_expected_rid_prefix "win-")
elseif(APPLE)
    set(_jalium_expected_rid_prefix "osx-")
elseif(CMAKE_SYSTEM_NAME STREQUAL "Linux")
    _jalium_detect_linux_libc(_jalium_linux_libc)
    if(_jalium_linux_libc STREQUAL "musl")
        set(_jalium_expected_rid_prefix "linux-musl-")
    else()
        set(_jalium_expected_rid_prefix "linux-")
    endif()
else()
    message(FATAL_ERROR
        "No Jalium native RID mapping exists for CMake system '${CMAKE_SYSTEM_NAME}'.")
endif()

set(_jalium_inferred_rid "${_jalium_expected_rid_prefix}${_jalium_target_arch}")
if(JALIUM_NATIVE_RID STREQUAL "")
    set(JALIUM_NATIVE_RID "${_jalium_inferred_rid}" CACHE STRING
        "Portable .NET RID for these native artifacts" FORCE)
elseif(NOT "${JALIUM_NATIVE_RID}" STREQUAL "${_jalium_inferred_rid}")
    message(FATAL_ERROR
        "JALIUM_NATIVE_RID='${JALIUM_NATIVE_RID}' does not match the target "
        "toolchain '${_jalium_inferred_rid}'. This guard prevents native assets "
        "from being written into another platform's package directory.")
endif()

message(STATUS "Jalium native RID: ${JALIUM_NATIVE_RID}")

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
# Shared-library builds (the default) drop into
# src/native/bin/native/<rid>/<Config>/ so Jalium.UI.Interop can pack the exact
# target payload without mixing Windows, Linux, Android, glibc, or musl files.
#
# Static (NativeAOT) builds stay under the cmake binary dir because they're
# bundled by ConfigureJaliumNativeAot from build-native-static.ps1 instead.
if(JALIUM_BUILD_STATIC)
    set(CMAKE_RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin-static)
    set(CMAKE_LIBRARY_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/lib-static)
    set(CMAKE_ARCHIVE_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/lib-static)
else()
    set(_jalium_native_out "${JALIUM_NATIVE_OUTPUT_ROOT}/${JALIUM_NATIVE_RID}")
    if(CMAKE_CONFIGURATION_TYPES)
        # Multi-config (Visual Studio, Ninja Multi-Config): each config lands in
        # its own bin/native/<rid>/<Config>/ subdir so configurations coexist.
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

# Cross-platform self-hosted text engine — must be before backends that link it
if(NOT WIN32)
    add_subdirectory(jalium.native.text)
endif()

if(WIN32)
    add_subdirectory(jalium.native.d3d12)
    add_subdirectory(jalium.native.browser)
    add_subdirectory(jalium.native.media.windows)
    if(JALIUM_BUILD_VULKAN)
        # Dev/test: build the Vulkan backend DLL on Windows too. The Vulkan
        # backend is normally the Linux/Android primary; this lets us validate
        # it (InkCanvas GPU pipeline, effects) without leaving Windows.
        add_subdirectory(jalium.native.vulkan)
    endif()
elseif(APPLE)
    add_subdirectory(jalium.native.metal)
else()
    # Linux / Android — Vulkan is the primary backend
    add_subdirectory(jalium.native.vulkan)
    if(ANDROID)
        add_subdirectory(jalium.native.media.android)
    else()
        add_subdirectory(jalium.native.media.linux)
    endif()
endif()

add_subdirectory(jalium.native.software)

if(CMAKE_SYSTEM_NAME STREQUAL "Linux" AND NOT JALIUM_BUILD_STATIC)
    jalium_apply_linux_export_policy(jalium.native.core core.map)
    jalium_apply_linux_export_policy(jalium.native.platform platform.map)
    jalium_apply_linux_export_policy(jalium.native.media.core media-core.map)
    jalium_apply_linux_export_policy(jalium.native.media.linux media.map)
    jalium_apply_linux_export_policy(jalium.native.text text.map)
    jalium_apply_linux_export_policy(jalium.native.vulkan vulkan.map)
    jalium_apply_linux_export_policy(jalium.native.software software.map)
endif()

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

# A package directory is considered usable only after every shared library for
# the configured platform has linked successfully. Interop checks this stamp so
# a failed or still-running native build cannot produce a partial NuGet RID.
if(NOT JALIUM_BUILD_STATIC)
    set(_jalium_package_targets
        jalium.native.core
        jalium.native.platform
        jalium.native.media.core
        jalium.native.software)
    foreach(_optional_target
            jalium.native.text
            jalium.native.d3d12
            jalium.native.browser
            jalium.native.media.windows
            jalium.native.vulkan
            jalium.native.metal
            jalium.native.media.android
            jalium.native.media.linux)
        if(TARGET ${_optional_target})
            list(APPEND _jalium_package_targets ${_optional_target})
        endif()
    endforeach()

    add_custom_target(jalium.native.package.invalidate
        COMMAND ${CMAKE_COMMAND} -E rm -f
            "$<TARGET_FILE_DIR:jalium.native.core>/.jalium-native-complete"
        COMMENT "Invalidating ${JALIUM_NATIVE_RID} native payload")
    foreach(_package_target ${_jalium_package_targets})
        add_dependencies(${_package_target} jalium.native.package.invalidate)
    endforeach()

    # The stamp is not a bare `touch` marker: it records which commit produced
    # the payload (head=/dirty= lines) so the NuGet pack guard can refuse to
    # ship binaries that do not match the commit being packed. The write runs
    # at build time (cmake -P), never at configure time, so the recorded HEAD
    # is always the one current when the libraries actually linked.
    get_filename_component(_jalium_repo_root "${CMAKE_CURRENT_SOURCE_DIR}/../.." ABSOLUTE)
    add_custom_target(jalium.native.package.complete ALL
        COMMAND ${CMAKE_COMMAND}
            "-DJALIUM_STAMP_FILE=$<TARGET_FILE_DIR:jalium.native.core>/.jalium-native-complete"
            "-DJALIUM_REPO_ROOT=${_jalium_repo_root}"
            "-DJALIUM_STAMP_RID=${JALIUM_NATIVE_RID}"
            "-DJALIUM_STAMP_CONFIG=$<CONFIG>"
            -P "${CMAKE_CURRENT_SOURCE_DIR}/cmake/JaliumNativeStamp.cmake"
        DEPENDS ${_jalium_package_targets}
        COMMENT "Marking ${JALIUM_NATIVE_RID} native payload complete (provenance stamp)")
endif()

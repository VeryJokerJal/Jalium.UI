cmake_minimum_required(VERSION 3.25)
project(JaliumNative VERSION 0.1.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# ---------------------------------------------------------------------------
# Build flavor: shared (default, NuGet) or static (NativeAOT single-exe)
# ---------------------------------------------------------------------------
option(JALIUM_BUILD_STATIC "Build native libraries as static archives for NativeAOT linking" OFF)

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
if(JALIUM_BUILD_STATIC)
    set(CMAKE_RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin-static)
    set(CMAKE_LIBRARY_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/lib-static)
    set(CMAKE_ARCHIVE_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/lib-static)
else()
    set(CMAKE_RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)
    set(CMAKE_LIBRARY_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/lib)
    set(CMAKE_ARCHIVE_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/lib)
endif()

# Subdirectories — order matters: text must precede vulkan/software for linkage
add_subdirectory(jalium.native.core)
add_subdirectory(jalium.native.platform)

# Cross-platform media (image/video/camera) public ABI + status helpers.
# Per-platform implementation libraries link to this for the C ABI header.
add_subdirectory(jalium.native.media.core)

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

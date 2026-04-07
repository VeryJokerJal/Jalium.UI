cmake_minimum_required(VERSION 3.25)
project(JaliumNative VERSION 0.1.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

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
set(CMAKE_RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)
set(CMAKE_LIBRARY_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/lib)
set(CMAKE_ARCHIVE_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/lib)

# Subdirectories — order matters: text must precede vulkan/software for linkage
add_subdirectory(jalium.native.core)
add_subdirectory(jalium.native.platform)

# Cross-platform text engine (FreeType + HarfBuzz) — must be before backends that link it
if(NOT WIN32)
    add_subdirectory(jalium.native.text)
endif()

if(WIN32)
    add_subdirectory(jalium.native.d3d12)
    add_subdirectory(jalium.native.browser)
elseif(APPLE)
    add_subdirectory(jalium.native.metal)
else()
    # Linux / Android — Vulkan is the primary backend
    add_subdirectory(jalium.native.vulkan)
endif()

add_subdirectory(jalium.native.software)

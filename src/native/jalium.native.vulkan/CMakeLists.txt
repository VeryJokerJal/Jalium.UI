project(jalium.native.vulkan)

# Keep Linux/Android builds hermetic: stb_image is vendored beside the other
# native single-header dependencies. Windows still uses the repository-local
# Vulkan SDK by default because its import library is linked explicitly below.
set(JALIUM_STB_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/../thirdparty/stb")
if(NOT EXISTS "${JALIUM_STB_ROOT}/stb_image.h")
    message(FATAL_ERROR "Vendored stb_image.h is missing: ${JALIUM_STB_ROOT}")
endif()

set(JALIUM_VULKAN_INCLUDE_DIRS "")
if(WIN32)
    if(DEFINED ENV{JALIUM_SDK_ROOT} AND NOT "$ENV{JALIUM_SDK_ROOT}" STREQUAL "")
        file(TO_CMAKE_PATH "$ENV{JALIUM_SDK_ROOT}" JALIUM_SDK_ROOT)
    else()
        get_filename_component(JALIUM_SDK_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/../../../../Jalium.SDK" ABSOLUTE)
    endif()

    if(DEFINED ENV{VULKAN_SDK} AND NOT "$ENV{VULKAN_SDK}" STREQUAL "")
        file(TO_CMAKE_PATH "$ENV{VULKAN_SDK}" JALIUM_VULKAN_SDK_ROOT)
    else()
        set(JALIUM_VULKAN_SDK_ROOT "${JALIUM_SDK_ROOT}/VulkanSDK/1.4.341.1")
    endif()
    list(APPEND JALIUM_VULKAN_INCLUDE_DIRS "${JALIUM_VULKAN_SDK_ROOT}/Include")
endif()

set(HEADERS
    include/vulkan_backend.h
    include/vulkan_glyph_atlas.h
    include/vulkan_impeller_engine.h
    include/vulkan_ink_layer.h
    include/vulkan_render_target.h
    include/vulkan_resources.h
    include/vulkan_runtime.h
    include/vulkan_shader_compiler.h
    include/vulkan_vello_engine.h
    include/vulkan_vello_compute.h
)

set(SOURCES
    src/vulkan_backend.cpp
    src/vulkan_glyph_atlas.cpp
    src/vulkan_impeller_engine.cpp
    src/vulkan_init.cpp
    src/vulkan_ink_layer.cpp
    src/vulkan_render_target.cpp
    src/vulkan_resources.cpp
    src/vulkan_runtime.cpp
    src/vulkan_shader_compiler.cpp
    src/vulkan_video_surface.cpp
    src/vulkan_vello_engine.cpp
    src/vulkan_vello_compute.cpp
)

add_library(${PROJECT_NAME} ${JALIUM_LIB_TYPE} ${HEADERS} ${SOURCES})

target_include_directories(${PROJECT_NAME}
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.core/include
        ${JALIUM_VULKAN_INCLUDE_DIRS}
        ${JALIUM_STB_ROOT}
)

target_link_libraries(${PROJECT_NAME}
    PRIVATE
        jalium.native.core
)

# Link text engine on non-Windows platforms
if(NOT WIN32)
    target_link_libraries(${PROJECT_NAME}
        PRIVATE
            jalium.native.text
    )
    target_include_directories(${PROJECT_NAME}
        PRIVATE
            ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.text/include
    )
endif()

if(WIN32)
    target_link_libraries(${PROJECT_NAME}
        PRIVATE
            ${JALIUM_VULKAN_SDK_ROOT}/Lib/vulkan-1.lib
            # DirectWrite: Windows-Vulkan text rasterization + layout mirrors the
            # D3D12 backend's DWrite path (IDWriteFactory5, IDWriteTextLayout,
            # IDWriteGlyphRunAnalysis). Required since the GDI text path was
            # replaced with DirectWrite for D3D12 parity.
            dwrite
    )
elseif(ANDROID)
    target_link_libraries(${PROJECT_NAME}
        PRIVATE
            vulkan
            android
            log
    )
elseif(UNIX AND NOT APPLE)
    find_package(X11 REQUIRED)
    target_include_directories(${PROJECT_NAME}
        PRIVATE
            ${X11_INCLUDE_DIR}
            ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.platform/include
    )
    target_link_libraries(${PROJECT_NAME}
        PRIVATE
            vulkan
            ${X11_LIBRARIES}
            jalium.native.platform
            dl
    )
endif()

set_target_properties(${PROJECT_NAME} PROPERTIES
    OUTPUT_NAME "jalium.native.vulkan"
    DEBUG_POSTFIX ""
)

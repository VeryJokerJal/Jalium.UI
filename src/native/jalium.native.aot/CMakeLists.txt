project(jalium.native.aot)

# This module only makes sense in static-flavor builds. The shared-DLL build
# never reaches this CMakeLists at all (the parent guards the add_subdirectory).

set(HEADERS
    include/jalium_aot.h
)

set(SOURCES
    aot_register.cpp
)

add_library(${PROJECT_NAME} STATIC ${HEADERS} ${SOURCES})

target_include_directories(${PROJECT_NAME}
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.core/include
)

# Force-pull every backend's .lib into the AOT image. The init symbols referenced
# by aot_register.cpp give the linker a strong reason to keep each backend TU.
target_link_libraries(${PROJECT_NAME}
    PUBLIC
        jalium.native.core
        jalium.native.software
)

if(WIN32 AND TARGET jalium.native.d3d12)
    target_link_libraries(${PROJECT_NAME} PUBLIC jalium.native.d3d12)
    target_compile_definitions(${PROJECT_NAME} PRIVATE)
elseif(NOT WIN32 AND NOT APPLE AND TARGET jalium.native.vulkan)
    target_link_libraries(${PROJECT_NAME} PUBLIC jalium.native.vulkan)
    target_compile_definitions(${PROJECT_NAME} PRIVATE JALIUM_AOT_INCLUDE_VULKAN)
elseif(APPLE AND TARGET jalium.native.metal)
    target_link_libraries(${PROJECT_NAME} PUBLIC jalium.native.metal)
endif()

# Pull jalium.native.media (image/video/camera) into the static AOT image.
if(TARGET jalium.native.media.core)
    target_link_libraries(${PROJECT_NAME} PUBLIC jalium.native.media.core)
endif()
if(WIN32 AND TARGET jalium.native.media.windows)
    target_link_libraries(${PROJECT_NAME} PUBLIC jalium.native.media.windows)
elseif(ANDROID AND TARGET jalium.native.media.android)
    target_link_libraries(${PROJECT_NAME} PUBLIC jalium.native.media.android)
endif()

set_target_properties(${PROJECT_NAME} PROPERTIES
    OUTPUT_NAME "jalium.native.aot"
    DEBUG_POSTFIX ""
)

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
elseif(CMAKE_SYSTEM_NAME STREQUAL "Linux" AND TARGET jalium.native.media.linux)
    # The Linux implementation owns jalium_media_initialize and every
    # jalium_audio_* export. media.core only contains shared helpers, so linking
    # it alone leaves the NativeAOT aggregator's force-link references unresolved.
    target_link_libraries(${PROJECT_NAME} PUBLIC jalium.native.media.linux)
endif()

set_target_properties(${PROJECT_NAME} PROPERTIES
    OUTPUT_NAME "jalium.native.aot"
    DEBUG_POSTFIX ""
)

if(CMAKE_SYSTEM_NAME STREQUAL "Linux" AND JALIUM_MEDIA_LINUX_BUILD_TESTS)
    find_package(Threads REQUIRED)
    add_executable(jalium.native.aot.linux.media-link.tests
        tests/linux_aot_link_smoke.cpp)
    target_include_directories(jalium.native.aot.linux.media-link.tests PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/include
        ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.core/include)
    # Isolate the AOT -> Linux media edge in this regression fixture. The test
    # supplies inert renderer init symbols and links the two archives by path,
    # so unrelated renderer archive collisions cannot hide a missing
    # jalium_media_initialize/jalium_audio_initialize implementation.
    add_dependencies(jalium.native.aot.linux.media-link.tests
        jalium.native.aot
        jalium.native.media.linux)
    target_link_libraries(jalium.native.aot.linux.media-link.tests PRIVATE
        $<TARGET_FILE:jalium.native.aot>
        $<TARGET_FILE:jalium.native.media.linux>
        jalium.native.media.core
        Threads::Threads
        ${CMAKE_DL_LIBS}
        m)
    add_test(NAME jalium.native.aot.linux.media-link
        COMMAND jalium.native.aot.linux.media-link.tests)
endif()

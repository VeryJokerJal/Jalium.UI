project(jalium.native.software)

set(HEADERS
    include/software_backend.h
)

set(SOURCES
    src/software_backend.cpp
    src/software_init.cpp
)

add_library(${PROJECT_NAME} ${JALIUM_LIB_TYPE} ${HEADERS} ${SOURCES})

target_include_directories(${PROJECT_NAME}
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.core/include
)

set(JALIUM_STB_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/../thirdparty/stb")
if(NOT EXISTS "${JALIUM_STB_ROOT}/stb_image.h")
    message(FATAL_ERROR "Vendored stb_image.h is missing: ${JALIUM_STB_ROOT}")
endif()

target_link_libraries(${PROJECT_NAME}
    PRIVATE
        jalium.native.core
)

if(WIN32)
    target_link_libraries(${PROJECT_NAME} PRIVATE gdi32)
endif()

# Link cross-platform self-hosted text engine
if(TARGET jalium.native.text)
    target_link_libraries(${PROJECT_NAME} PRIVATE jalium.native.text)
    target_include_directories(${PROJECT_NAME} PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.text/include)
    target_compile_definitions(${PROJECT_NAME} PRIVATE JALIUM_HAS_TEXT_ENGINE)
endif()

# stb_image for non-Windows image decoding
if(NOT WIN32)
    if(EXISTS "${JALIUM_STB_ROOT}")
        target_include_directories(${PROJECT_NAME} PRIVATE ${JALIUM_STB_ROOT})
    endif()

    # Link X11 for present on Linux
    if(UNIX AND NOT APPLE AND NOT ANDROID)
        find_package(X11 QUIET)
        if(X11_FOUND)
            target_include_directories(${PROJECT_NAME} PRIVATE ${X11_INCLUDE_DIR})
            target_link_libraries(${PROJECT_NAME} PRIVATE ${X11_LIBRARIES})
            target_compile_definitions(${PROJECT_NAME} PRIVATE JALIUM_SOFTWARE_X11_PRESENT)
        endif()

        # Wayland software presentation is independent of Vulkan and only
        # needs the core wayland-client protocol (wl_shm is a core global).
        # Keep it optional so X11-only build environments remain supported.
        find_package(PkgConfig QUIET)
        if(PkgConfig_FOUND)
            pkg_check_modules(WAYLAND_CLIENT QUIET IMPORTED_TARGET wayland-client)
        endif()
        if(WAYLAND_CLIENT_FOUND)
            target_sources(${PROJECT_NAME} PRIVATE
                src/wayland_shm_present.cpp)
            target_include_directories(${PROJECT_NAME} PRIVATE
                ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.platform/include)
            target_link_libraries(${PROJECT_NAME} PRIVATE
                PkgConfig::WAYLAND_CLIENT
                jalium.native.platform)
            target_compile_definitions(${PROJECT_NAME} PRIVATE
                JALIUM_SOFTWARE_WAYLAND_PRESENT=1)
        else()
            message(STATUS
                "wayland-client not found; software backend will use X11 presentation only")
        endif()
    endif()

    # Link android for ANativeWindow present on Android
    if(ANDROID)
        target_link_libraries(${PROJECT_NAME} PRIVATE android log)
    endif()
endif()

set_target_properties(${PROJECT_NAME} PROPERTIES
    OUTPUT_NAME "jalium.native.software"
    DEBUG_POSTFIX ""
)

option(JALIUM_SOFTWARE_BUILD_TESTS
    "Build jalium.native.software Linux presentation smoke tests" OFF)
if(JALIUM_SOFTWARE_BUILD_TESTS AND UNIX AND NOT APPLE AND NOT ANDROID)
    if(WAYLAND_CLIENT_FOUND AND TARGET jalium.native.platform)
        add_executable(jalium.native.software.wayland.tests
            tests/software_wayland_smoke.cpp)
        target_include_directories(jalium.native.software.wayland.tests PRIVATE
            ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.core/include
            ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.platform/include)
        target_link_libraries(jalium.native.software.wayland.tests PRIVATE
            jalium.native.core
            jalium.native.platform
            jalium.native.software)
    endif()
endif()

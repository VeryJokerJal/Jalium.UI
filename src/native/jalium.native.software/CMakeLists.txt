project(jalium.native.software)

option(JALIUM_SOFTWARE_ENABLE_XSHM
    "Enable the optional MIT-SHM fast path for X11 software presentation" ON)

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
    # software_backend.h conditionally owns a TextEngine unique_ptr, so this is
    # part of the public C++ class layout and must propagate to native consumers.
    target_compile_definitions(${PROJECT_NAME} PUBLIC JALIUM_HAS_TEXT_ENGINE)
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
            # software_backend.h conditionally owns the X11 presenter, so the
            # definition is part of the public C++ class layout.
            target_compile_definitions(${PROJECT_NAME} PUBLIC
                JALIUM_SOFTWARE_X11_PRESENT)

            # MIT-SHM is an optional acceleration layer. Keep the baseline
            # XPutImage presenter buildable when libXext headers/libraries are
            # absent (notably minimal musl toolchains), and enable it only when
            # both halves of the extension SDK were discovered.
            set(JALIUM_SOFTWARE_HAS_XSHM OFF)
            if(JALIUM_SOFTWARE_ENABLE_XSHM)
                find_path(JALIUM_XSHM_INCLUDE_DIR
                    NAMES X11/extensions/XShm.h
                    HINTS ${X11_INCLUDE_DIR} ${X11_Xext_INCLUDE_PATH})
                set(JALIUM_XEXT_LIBRARY "${X11_Xext_LIB}")
                if(NOT JALIUM_XEXT_LIBRARY)
                    find_library(JALIUM_XEXT_LIBRARY NAMES Xext)
                endif()
            endif()
            if(JALIUM_SOFTWARE_ENABLE_XSHM AND
               JALIUM_XSHM_INCLUDE_DIR AND JALIUM_XEXT_LIBRARY)
                target_include_directories(${PROJECT_NAME} PRIVATE
                    ${JALIUM_XSHM_INCLUDE_DIR})
                target_link_libraries(${PROJECT_NAME} PRIVATE
                    ${JALIUM_XEXT_LIBRARY})
                target_compile_definitions(${PROJECT_NAME} PRIVATE
                    JALIUM_SOFTWARE_XSHM_PRESENT=1)
                set(JALIUM_SOFTWARE_HAS_XSHM ON)
            else()
                if(JALIUM_SOFTWARE_ENABLE_XSHM)
                    message(STATUS
                        "libXext/XShm unavailable; software X11 present will use XPutImage")
                else()
                    message(STATUS
                        "MIT-SHM disabled; software X11 present will use XPutImage")
                endif()
            endif()
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
    if(TARGET jalium.native.platform)
        if(WAYLAND_CLIENT_FOUND)
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

        if(X11_FOUND)
            add_executable(jalium.native.software.x11.tests
                tests/software_x11_present_smoke.cpp)
            target_include_directories(jalium.native.software.x11.tests PRIVATE
                ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.core/include
                ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.platform/include
                ${X11_INCLUDE_DIR})
            target_link_libraries(jalium.native.software.x11.tests PRIVATE
                jalium.native.core
                jalium.native.platform
                jalium.native.software
                ${X11_LIBRARIES})

            add_test(NAME jalium.native.software.x11-fallback
                COMMAND jalium.native.software.x11.tests)
            set_tests_properties(jalium.native.software.x11-fallback PROPERTIES
                ENVIRONMENT
                    "JALIUM_WINDOW_SYSTEM=x11;JALIUM_SOFTWARE_X11_DISABLE_SHM=1")

            if(JALIUM_SOFTWARE_HAS_XSHM)
                add_test(NAME jalium.native.software.x11-xshm
                    COMMAND jalium.native.software.x11.tests)
                set_tests_properties(jalium.native.software.x11-xshm PROPERTIES
                    ENVIRONMENT
                        "JALIUM_WINDOW_SYSTEM=x11;JALIUM_SOFTWARE_X11_REQUIRE_SHM=1")
            endif()
        endif()
    endif()
endif()

if(JALIUM_TEXT_BUILD_TESTS AND UNIX AND NOT APPLE AND NOT ANDROID AND TARGET jalium.native.text)
    # This pixel test inspects SoftwareRenderTarget's framebuffer directly.
    # Keep that implementation ABI out of the production .so by compiling a
    # test-only static copy, just as the text-engine implementation tests do.
    add_library(jalium.native.software.text.test-support STATIC
        src/software_backend.cpp)
    target_compile_definitions(jalium.native.software.text.test-support
        PUBLIC JALIUM_STATIC JALIUM_HAS_TEXT_ENGINE)
    target_include_directories(jalium.native.software.text.test-support
        PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/include
        PRIVATE
            ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.core/include
            ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.text/include
            ${JALIUM_STB_ROOT})
    target_link_libraries(jalium.native.software.text.test-support PRIVATE
        jalium.native.core
        jalium.native.text)
    if(X11_FOUND)
        target_compile_definitions(jalium.native.software.text.test-support
            PUBLIC JALIUM_SOFTWARE_X11_PRESENT)
        target_include_directories(jalium.native.software.text.test-support PRIVATE
            ${X11_INCLUDE_DIR})
        target_link_libraries(jalium.native.software.text.test-support PRIVATE
            ${X11_LIBRARIES})
        if(JALIUM_SOFTWARE_HAS_XSHM)
            target_compile_definitions(jalium.native.software.text.test-support PRIVATE
                JALIUM_SOFTWARE_XSHM_PRESENT=1)
            target_include_directories(jalium.native.software.text.test-support PRIVATE
                ${JALIUM_XSHM_INCLUDE_DIR})
            target_link_libraries(jalium.native.software.text.test-support PRIVATE
                ${JALIUM_XEXT_LIBRARY})
        endif()
    endif()
    if(WAYLAND_CLIENT_FOUND)
        target_sources(jalium.native.software.text.test-support PRIVATE
            src/wayland_shm_present.cpp)
        target_compile_definitions(jalium.native.software.text.test-support PRIVATE
            JALIUM_SOFTWARE_WAYLAND_PRESENT=1)
        target_include_directories(jalium.native.software.text.test-support PRIVATE
            ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.platform/include)
        target_link_libraries(jalium.native.software.text.test-support PRIVATE
            PkgConfig::WAYLAND_CLIENT
            jalium.native.platform)
    endif()

    add_executable(jalium.native.software.text.tests
        tests/software_text_pixels.cpp)
    target_include_directories(jalium.native.software.text.tests PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/include
        ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.core/include
        ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.text/include)
    target_link_libraries(jalium.native.software.text.tests PRIVATE
        jalium.native.software.text.test-support
        jalium.native.text
        jalium.native.core)
    add_test(NAME jalium.native.software.text-pixels
             COMMAND jalium.native.software.text.tests)
endif()

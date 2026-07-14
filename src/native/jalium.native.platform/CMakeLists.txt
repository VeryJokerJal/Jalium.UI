project(jalium.native.platform)

set(HEADERS
    include/jalium_platform.h
)

# Platform-specific sources
if(WIN32)
    set(PLATFORM_SOURCES
        src/platform_win32.cpp
        src/platform_notification_stubs_win32.cpp
    )
elseif(ANDROID)
    set(PLATFORM_SOURCES
        src/platform_android.cpp
    )
elseif(UNIX AND NOT APPLE)
    set(PLATFORM_SOURCES
        src/platform_linux_x11.cpp
    )

    # Wayland is optional: distributions that only ship X11 development files
    # must continue to build a fully functional X11 backend.  When the complete
    # protocol toolchain is present, generate the stable xdg-shell client glue
    # at build time instead of carrying a stale generated copy in the repo.
    find_package(PkgConfig QUIET)
    if(PkgConfig_FOUND)
        pkg_check_modules(WAYLAND_CLIENT QUIET IMPORTED_TARGET wayland-client)
        pkg_check_modules(WAYLAND_CURSOR QUIET IMPORTED_TARGET wayland-cursor)
        pkg_check_modules(XKBCOMMON QUIET IMPORTED_TARGET xkbcommon)
        pkg_get_variable(WAYLAND_PROTOCOLS_DIR wayland-protocols pkgdatadir)
    endif()
    find_program(WAYLAND_SCANNER_EXECUTABLE NAMES wayland-scanner)
    set(XDG_SHELL_PROTOCOL "${WAYLAND_PROTOCOLS_DIR}/stable/xdg-shell/xdg-shell.xml")
    if(WAYLAND_CLIENT_FOUND AND WAYLAND_CURSOR_FOUND AND XKBCOMMON_FOUND AND
       WAYLAND_SCANNER_EXECUTABLE AND EXISTS "${XDG_SHELL_PROTOCOL}")
        enable_language(C)
        set(WAYLAND_GENERATED_DIR "${CMAKE_CURRENT_BINARY_DIR}/generated")
        set(XDG_SHELL_HEADER "${WAYLAND_GENERATED_DIR}/xdg-shell-client-protocol.h")
        set(XDG_SHELL_CODE "${WAYLAND_GENERATED_DIR}/xdg-shell-protocol.c")
        add_custom_command(
            OUTPUT "${XDG_SHELL_HEADER}" "${XDG_SHELL_CODE}"
            COMMAND ${CMAKE_COMMAND} -E make_directory "${WAYLAND_GENERATED_DIR}"
            COMMAND "${WAYLAND_SCANNER_EXECUTABLE}" client-header
                    "${XDG_SHELL_PROTOCOL}" "${XDG_SHELL_HEADER}"
            COMMAND "${WAYLAND_SCANNER_EXECUTABLE}" private-code
                    "${XDG_SHELL_PROTOCOL}" "${XDG_SHELL_CODE}"
            DEPENDS "${XDG_SHELL_PROTOCOL}"
            VERBATIM)
        list(APPEND PLATFORM_SOURCES "${XDG_SHELL_CODE}")
        set(JALIUM_PLATFORM_HAS_WAYLAND ON)

        # xdg-activation-v1 first appeared in wayland-protocols 1.21, while
        # the supported Ubuntu 20.04 build baseline ships 1.20. Prefer the
        # distribution copy when available and otherwise generate from the
        # upstream MIT-licensed protocol XML carried with this backend.
        set(XDG_ACTIVATION_V1_PROTOCOL
            "${WAYLAND_PROTOCOLS_DIR}/staging/xdg-activation/xdg-activation-v1.xml")
        if(NOT EXISTS "${XDG_ACTIVATION_V1_PROTOCOL}")
            set(XDG_ACTIVATION_V1_PROTOCOL
                "${CMAKE_CURRENT_SOURCE_DIR}/protocols/xdg-activation-v1.xml")
        endif()
        if(EXISTS "${XDG_ACTIVATION_V1_PROTOCOL}")
            set(XDG_ACTIVATION_V1_HEADER
                "${WAYLAND_GENERATED_DIR}/xdg-activation-v1-client-protocol.h")
            set(XDG_ACTIVATION_V1_CODE
                "${WAYLAND_GENERATED_DIR}/xdg-activation-v1-protocol.c")
            add_custom_command(
                OUTPUT "${XDG_ACTIVATION_V1_HEADER}" "${XDG_ACTIVATION_V1_CODE}"
                COMMAND ${CMAKE_COMMAND} -E make_directory "${WAYLAND_GENERATED_DIR}"
                COMMAND "${WAYLAND_SCANNER_EXECUTABLE}" client-header
                        "${XDG_ACTIVATION_V1_PROTOCOL}" "${XDG_ACTIVATION_V1_HEADER}"
                COMMAND "${WAYLAND_SCANNER_EXECUTABLE}" private-code
                        "${XDG_ACTIVATION_V1_PROTOCOL}" "${XDG_ACTIVATION_V1_CODE}"
                DEPENDS "${XDG_ACTIVATION_V1_PROTOCOL}"
                VERBATIM)
            list(APPEND PLATFORM_SOURCES "${XDG_ACTIVATION_V1_CODE}")
            set(JALIUM_PLATFORM_HAS_XDG_ACTIVATION_V1 ON)
        endif()

        # text-input-v3 is optional at runtime and at build time.  It carries
        # compositor/IME preedit and committed UTF-8 text; plain xkb input
        # remains the explicit fallback on compositors that do not advertise it.
        set(TEXT_INPUT_V3_PROTOCOL
            "${WAYLAND_PROTOCOLS_DIR}/unstable/text-input/text-input-unstable-v3.xml")
        if(EXISTS "${TEXT_INPUT_V3_PROTOCOL}")
            set(TEXT_INPUT_V3_HEADER
                "${WAYLAND_GENERATED_DIR}/text-input-unstable-v3-client-protocol.h")
            set(TEXT_INPUT_V3_CODE
                "${WAYLAND_GENERATED_DIR}/text-input-unstable-v3-protocol.c")
            add_custom_command(
                OUTPUT "${TEXT_INPUT_V3_HEADER}" "${TEXT_INPUT_V3_CODE}"
                COMMAND ${CMAKE_COMMAND} -E make_directory "${WAYLAND_GENERATED_DIR}"
                COMMAND "${WAYLAND_SCANNER_EXECUTABLE}" client-header
                        "${TEXT_INPUT_V3_PROTOCOL}" "${TEXT_INPUT_V3_HEADER}"
                COMMAND "${WAYLAND_SCANNER_EXECUTABLE}" private-code
                        "${TEXT_INPUT_V3_PROTOCOL}" "${TEXT_INPUT_V3_CODE}"
                DEPENDS "${TEXT_INPUT_V3_PROTOCOL}"
                VERBATIM)
            list(APPEND PLATFORM_SOURCES "${TEXT_INPUT_V3_CODE}")
            set(JALIUM_PLATFORM_HAS_TEXT_INPUT_V3 ON)
        endif()

        set(TEXT_INPUT_V1_PROTOCOL
            "${WAYLAND_PROTOCOLS_DIR}/unstable/text-input/text-input-unstable-v1.xml")
        if(EXISTS "${TEXT_INPUT_V1_PROTOCOL}")
            set(TEXT_INPUT_V1_HEADER
                "${WAYLAND_GENERATED_DIR}/text-input-unstable-v1-client-protocol.h")
            set(TEXT_INPUT_V1_CODE
                "${WAYLAND_GENERATED_DIR}/text-input-unstable-v1-protocol.c")
            add_custom_command(
                OUTPUT "${TEXT_INPUT_V1_HEADER}" "${TEXT_INPUT_V1_CODE}"
                COMMAND ${CMAKE_COMMAND} -E make_directory "${WAYLAND_GENERATED_DIR}"
                COMMAND "${WAYLAND_SCANNER_EXECUTABLE}" client-header
                        "${TEXT_INPUT_V1_PROTOCOL}" "${TEXT_INPUT_V1_HEADER}"
                COMMAND "${WAYLAND_SCANNER_EXECUTABLE}" private-code
                        "${TEXT_INPUT_V1_PROTOCOL}" "${TEXT_INPUT_V1_CODE}"
                DEPENDS "${TEXT_INPUT_V1_PROTOCOL}"
                VERBATIM)
            list(APPEND PLATFORM_SOURCES "${TEXT_INPUT_V1_CODE}")
            set(JALIUM_PLATFORM_HAS_TEXT_INPUT_V1 ON)
        endif()

        # Linux touch is available from the core wl_touch protocol. Pen/tablet
        # input and portal parent handles are optional protocol extensions: keep
        # the Wayland backend buildable on older wayland-protocols packages and
        # enable each capability only when its XML is installed.
        set(TABLET_V2_PROTOCOL
            "${WAYLAND_PROTOCOLS_DIR}/unstable/tablet/tablet-unstable-v2.xml")
        if(EXISTS "${TABLET_V2_PROTOCOL}")
            set(TABLET_V2_HEADER
                "${WAYLAND_GENERATED_DIR}/tablet-unstable-v2-client-protocol.h")
            set(TABLET_V2_CODE
                "${WAYLAND_GENERATED_DIR}/tablet-unstable-v2-protocol.c")
            add_custom_command(
                OUTPUT "${TABLET_V2_HEADER}" "${TABLET_V2_CODE}"
                COMMAND ${CMAKE_COMMAND} -E make_directory "${WAYLAND_GENERATED_DIR}"
                COMMAND "${WAYLAND_SCANNER_EXECUTABLE}" client-header
                        "${TABLET_V2_PROTOCOL}" "${TABLET_V2_HEADER}"
                COMMAND "${WAYLAND_SCANNER_EXECUTABLE}" private-code
                        "${TABLET_V2_PROTOCOL}" "${TABLET_V2_CODE}"
                DEPENDS "${TABLET_V2_PROTOCOL}"
                VERBATIM)
            list(APPEND PLATFORM_SOURCES "${TABLET_V2_CODE}")
            set(JALIUM_PLATFORM_HAS_TABLET_V2 ON)
        endif()

        set(XDG_FOREIGN_V2_PROTOCOL
            "${WAYLAND_PROTOCOLS_DIR}/unstable/xdg-foreign/xdg-foreign-unstable-v2.xml")
        if(EXISTS "${XDG_FOREIGN_V2_PROTOCOL}")
            set(XDG_FOREIGN_V2_HEADER
                "${WAYLAND_GENERATED_DIR}/xdg-foreign-unstable-v2-client-protocol.h")
            set(XDG_FOREIGN_V2_CODE
                "${WAYLAND_GENERATED_DIR}/xdg-foreign-unstable-v2-protocol.c")
            add_custom_command(
                OUTPUT "${XDG_FOREIGN_V2_HEADER}" "${XDG_FOREIGN_V2_CODE}"
                COMMAND ${CMAKE_COMMAND} -E make_directory "${WAYLAND_GENERATED_DIR}"
                COMMAND "${WAYLAND_SCANNER_EXECUTABLE}" client-header
                        "${XDG_FOREIGN_V2_PROTOCOL}" "${XDG_FOREIGN_V2_HEADER}"
                COMMAND "${WAYLAND_SCANNER_EXECUTABLE}" private-code
                        "${XDG_FOREIGN_V2_PROTOCOL}" "${XDG_FOREIGN_V2_CODE}"
                DEPENDS "${XDG_FOREIGN_V2_PROTOCOL}"
                VERBATIM)
            list(APPEND PLATFORM_SOURCES "${XDG_FOREIGN_V2_CODE}")
            set(JALIUM_PLATFORM_HAS_XDG_FOREIGN_V2 ON)
        endif()

        set(XDG_DECORATION_V1_PROTOCOL
            "${WAYLAND_PROTOCOLS_DIR}/unstable/xdg-decoration/xdg-decoration-unstable-v1.xml")
        if(EXISTS "${XDG_DECORATION_V1_PROTOCOL}")
            set(XDG_DECORATION_V1_HEADER
                "${WAYLAND_GENERATED_DIR}/xdg-decoration-unstable-v1-client-protocol.h")
            set(XDG_DECORATION_V1_CODE
                "${WAYLAND_GENERATED_DIR}/xdg-decoration-unstable-v1-protocol.c")
            add_custom_command(
                OUTPUT "${XDG_DECORATION_V1_HEADER}" "${XDG_DECORATION_V1_CODE}"
                COMMAND ${CMAKE_COMMAND} -E make_directory "${WAYLAND_GENERATED_DIR}"
                COMMAND "${WAYLAND_SCANNER_EXECUTABLE}" client-header
                        "${XDG_DECORATION_V1_PROTOCOL}" "${XDG_DECORATION_V1_HEADER}"
                COMMAND "${WAYLAND_SCANNER_EXECUTABLE}" private-code
                        "${XDG_DECORATION_V1_PROTOCOL}" "${XDG_DECORATION_V1_CODE}"
                DEPENDS "${XDG_DECORATION_V1_PROTOCOL}"
                VERBATIM)
            list(APPEND PLATFORM_SOURCES "${XDG_DECORATION_V1_CODE}")
            set(JALIUM_PLATFORM_HAS_XDG_DECORATION_V1 ON)
        endif()

        # xdg-toplevel-icon-v1 is a staging protocol and is therefore absent
        # on older distributions/compositors. Build the client glue only when
        # the installed wayland-protocols package provides the XML; runtime
        # global discovery still decides whether an individual compositor can
        # accept per-toplevel pixel icons.
        set(XDG_TOPLEVEL_ICON_V1_PROTOCOL
            "${WAYLAND_PROTOCOLS_DIR}/staging/xdg-toplevel-icon/xdg-toplevel-icon-v1.xml")
        if(EXISTS "${XDG_TOPLEVEL_ICON_V1_PROTOCOL}")
            set(XDG_TOPLEVEL_ICON_V1_HEADER
                "${WAYLAND_GENERATED_DIR}/xdg-toplevel-icon-v1-client-protocol.h")
            set(XDG_TOPLEVEL_ICON_V1_CODE
                "${WAYLAND_GENERATED_DIR}/xdg-toplevel-icon-v1-protocol.c")
            add_custom_command(
                OUTPUT "${XDG_TOPLEVEL_ICON_V1_HEADER}" "${XDG_TOPLEVEL_ICON_V1_CODE}"
                COMMAND ${CMAKE_COMMAND} -E make_directory "${WAYLAND_GENERATED_DIR}"
                COMMAND "${WAYLAND_SCANNER_EXECUTABLE}" client-header
                        "${XDG_TOPLEVEL_ICON_V1_PROTOCOL}" "${XDG_TOPLEVEL_ICON_V1_HEADER}"
                COMMAND "${WAYLAND_SCANNER_EXECUTABLE}" private-code
                        "${XDG_TOPLEVEL_ICON_V1_PROTOCOL}" "${XDG_TOPLEVEL_ICON_V1_CODE}"
                DEPENDS "${XDG_TOPLEVEL_ICON_V1_PROTOCOL}"
                VERBATIM)
            list(APPEND PLATFORM_SOURCES "${XDG_TOPLEVEL_ICON_V1_CODE}")
            set(JALIUM_PLATFORM_HAS_XDG_TOPLEVEL_ICON_V1 ON)
        endif()
    else()
        message(STATUS "Wayland development stack incomplete; building X11-only platform backend")
    endif()
elseif(APPLE)
    # No macOS backend exists yet. Referencing a nonexistent
    # src/platform_macos.mm made every Apple configure fail with a
    # misleading missing-file error; fail honestly instead.
    message(FATAL_ERROR
        "jalium.native.platform has no macOS backend yet. "
        "Supported window systems: Win32, X11/Wayland, Android.")
endif()

set(SOURCES
    src/platform_common.cpp
    ${PLATFORM_SOURCES}
)

add_library(${PROJECT_NAME} ${JALIUM_LIB_TYPE} ${HEADERS} ${SOURCES})

if(NOT JALIUM_BUILD_STATIC)
    target_compile_definitions(${PROJECT_NAME} PRIVATE JALIUM_PLATFORM_EXPORTS)
endif()

target_include_directories(${PROJECT_NAME}
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.core/include
)

target_link_libraries(${PROJECT_NAME}
    PRIVATE
        jalium.native.core
)

# Platform-specific link libraries
if(WIN32)
    target_link_libraries(${PROJECT_NAME}
        PRIVATE
            user32
            gdi32
            shcore
            dwmapi
            ole32
            imm32
    )
elseif(ANDROID)
    target_link_libraries(${PROJECT_NAME}
        PRIVATE
            android
            log
    )
elseif(UNIX AND NOT APPLE)
    find_package(X11 REQUIRED)
    if(PkgConfig_FOUND)
        pkg_check_modules(XRANDR xrandr)
        pkg_check_modules(XINPUT2 QUIET IMPORTED_TARGET xi)
        pkg_check_modules(XCURSOR QUIET IMPORTED_TARGET xcursor)
    endif()

    target_include_directories(${PROJECT_NAME}
        PRIVATE
            ${X11_INCLUDE_DIR}
    )
    target_link_libraries(${PROJECT_NAME}
        PRIVATE
            ${X11_LIBRARIES}
            pthread
    )
    if(XRANDR_FOUND)
        target_link_libraries(${PROJECT_NAME} PRIVATE ${XRANDR_LIBRARIES})
        target_compile_definitions(${PROJECT_NAME} PRIVATE JALIUM_HAS_XRANDR)
    endif()
    if(XINPUT2_FOUND)
        target_link_libraries(${PROJECT_NAME} PRIVATE PkgConfig::XINPUT2)
        target_compile_definitions(${PROJECT_NAME} PRIVATE JALIUM_HAS_XINPUT2=1)
    else()
        message(STATUS "libXi development files unavailable; X11 touch/pen input disabled")
    endif()
    if(XCURSOR_FOUND)
        target_link_libraries(${PROJECT_NAME} PRIVATE PkgConfig::XCURSOR)
        target_compile_definitions(${PROJECT_NAME} PRIVATE JALIUM_HAS_XCURSOR=1)
    else()
        message(STATUS "libXcursor development files unavailable; X11 drag images disabled")
    endif()
    if(JALIUM_PLATFORM_HAS_WAYLAND)
        target_include_directories(${PROJECT_NAME} PRIVATE "${WAYLAND_GENERATED_DIR}")
        target_link_libraries(${PROJECT_NAME} PRIVATE
            PkgConfig::WAYLAND_CLIENT
            PkgConfig::WAYLAND_CURSOR
            PkgConfig::XKBCOMMON)
        target_compile_definitions(${PROJECT_NAME} PRIVATE JALIUM_HAS_WAYLAND=1)
        if(JALIUM_PLATFORM_HAS_TEXT_INPUT_V3)
            target_compile_definitions(${PROJECT_NAME} PRIVATE JALIUM_HAS_WAYLAND_TEXT_INPUT_V3=1)
        endif()
        if(JALIUM_PLATFORM_HAS_TEXT_INPUT_V1)
            target_compile_definitions(${PROJECT_NAME} PRIVATE JALIUM_HAS_WAYLAND_TEXT_INPUT_V1=1)
        endif()
        if(JALIUM_PLATFORM_HAS_TABLET_V2)
            target_compile_definitions(${PROJECT_NAME} PRIVATE JALIUM_HAS_WAYLAND_TABLET_V2=1)
        endif()
        if(JALIUM_PLATFORM_HAS_XDG_FOREIGN_V2)
            target_compile_definitions(${PROJECT_NAME} PRIVATE JALIUM_HAS_XDG_FOREIGN_V2=1)
        endif()
        if(JALIUM_PLATFORM_HAS_XDG_DECORATION_V1)
            target_compile_definitions(${PROJECT_NAME} PRIVATE JALIUM_HAS_XDG_DECORATION_V1=1)
        endif()
        if(JALIUM_PLATFORM_HAS_XDG_TOPLEVEL_ICON_V1)
            target_compile_definitions(${PROJECT_NAME} PRIVATE JALIUM_HAS_XDG_TOPLEVEL_ICON_V1=1)
        endif()
        if(JALIUM_PLATFORM_HAS_XDG_ACTIVATION_V1)
            target_compile_definitions(${PROJECT_NAME} PRIVATE JALIUM_HAS_XDG_ACTIVATION_V1=1)
        endif()
    endif()
elseif(APPLE)
    find_library(COCOA_FRAMEWORK Cocoa REQUIRED)
    find_library(QUARTZCORE_FRAMEWORK QuartzCore REQUIRED)
    target_link_libraries(${PROJECT_NAME}
        PRIVATE
            ${COCOA_FRAMEWORK}
            ${QUARTZCORE_FRAMEWORK}
    )
endif()

set_target_properties(${PROJECT_NAME} PROPERTIES
    OUTPUT_NAME "jalium.native.platform"
    DEBUG_POSTFIX ""
)

option(JALIUM_PLATFORM_BUILD_TESTS "Build jalium.native.platform Linux tests" OFF)
if(JALIUM_PLATFORM_BUILD_TESTS AND UNIX AND NOT APPLE AND NOT ANDROID)
    enable_testing()
    target_compile_definitions(${PROJECT_NAME} PRIVATE JALIUM_PLATFORM_TEST_HOOKS=1)
    add_executable(jalium.native.platform.tests
        tests/platform_linux_tests.cpp
    )
    target_include_directories(jalium.native.platform.tests
        PRIVATE
            ${CMAKE_CURRENT_SOURCE_DIR}/include
            ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.core/include
            ${X11_INCLUDE_DIR}
    )
    target_compile_definitions(jalium.native.platform.tests
        PRIVATE JALIUM_PLATFORM_TEST_HOOKS=1)
    target_link_libraries(jalium.native.platform.tests
        PRIVATE
            jalium.native.platform
            jalium.native.core
            ${X11_LIBRARIES}
            dl
            pthread
    )
    add_test(NAME jalium.native.platform.linux
             COMMAND jalium.native.platform.tests)
    set_tests_properties(jalium.native.platform.linux PROPERTIES
        ENVIRONMENT "JALIUM_WINDOW_SYSTEM=x11")
endif()

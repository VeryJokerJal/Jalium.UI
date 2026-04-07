project(jalium.native.platform)

set(HEADERS
    include/jalium_platform.h
)

# Platform-specific sources
if(WIN32)
    set(PLATFORM_SOURCES
        src/platform_win32.cpp
    )
elseif(ANDROID)
    set(PLATFORM_SOURCES
        src/platform_android.cpp
    )
elseif(UNIX AND NOT APPLE)
    set(PLATFORM_SOURCES
        src/platform_linux_x11.cpp
    )
elseif(APPLE)
    set(PLATFORM_SOURCES
        src/platform_macos.mm
    )
endif()

set(SOURCES
    src/platform_common.cpp
    ${PLATFORM_SOURCES}
)

add_library(${PROJECT_NAME} SHARED ${HEADERS} ${SOURCES})

target_compile_definitions(${PROJECT_NAME} PRIVATE JALIUM_PLATFORM_EXPORTS)

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
    find_package(PkgConfig)
    if(PkgConfig_FOUND)
        pkg_check_modules(XRANDR xrandr)
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

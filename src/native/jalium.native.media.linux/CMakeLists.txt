project(jalium.native.media.linux)

find_package(Threads REQUIRED)
find_package(PkgConfig QUIET)

if(PkgConfig_FOUND)
    pkg_check_modules(JALIUM_GSTREAMER QUIET IMPORTED_TARGET
        gstreamer-1.0>=1.16
        gstreamer-app-1.0>=1.16
        gstreamer-video-1.0>=1.16
        gstreamer-audio-1.0>=1.16
        gstreamer-pbutils-1.0>=1.16)
endif()

set(SOURCES
    src/linux_media.cpp
)

# The shared audio translation units deliberately live in the platform media
# library so managed code has one stable P/Invoke target on every OS.
add_library(${PROJECT_NAME} ${JALIUM_LIB_TYPE}
    ${SOURCES}
    ${JALIUM_AUDIO_SOURCES}
)

target_include_directories(${PROJECT_NAME}
    PRIVATE
        ${JALIUM_AUDIO_PRIVATE_INCLUDES}
)

target_link_libraries(${PROJECT_NAME}
    PRIVATE
        jalium.native.media.core
        Threads::Threads
        ${CMAKE_DL_LIBS}
        m
)

if(JALIUM_GSTREAMER_FOUND)
    target_link_libraries(${PROJECT_NAME} PRIVATE PkgConfig::JALIUM_GSTREAMER)
    target_compile_definitions(${PROJECT_NAME} PRIVATE JALIUM_HAS_GSTREAMER=1)
    message(STATUS "Linux media: GStreamer backend enabled (${JALIUM_GSTREAMER_VERSION})")
else()
    target_compile_definitions(${PROJECT_NAME} PRIVATE JALIUM_HAS_GSTREAMER=0)
    message(WARNING
        "Linux media: GStreamer development packages were not found. "
        "Building the ABI-complete fallback backend; image/video/camera/AAC "
        "entry points will report UNSUPPORTED or NOT_IMPLEMENTED.")
endif()

target_compile_definitions(${PROJECT_NAME} PRIVATE JALIUM_MEDIA_EXPORTS)

set_target_properties(${PROJECT_NAME} PROPERTIES
    OUTPUT_NAME "jalium.native.media"
    DEBUG_POSTFIX ""
)

option(JALIUM_MEDIA_LINUX_BUILD_TESTS "Build Linux native media smoke tests" OFF)
if(JALIUM_MEDIA_LINUX_BUILD_TESTS AND NOT JALIUM_BUILD_STATIC)
    enable_testing()
    add_executable(jalium.native.media.linux.tests
        tests/linux_media_smoke.cpp)
    target_include_directories(jalium.native.media.linux.tests PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.media.core/include)
    target_link_libraries(jalium.native.media.linux.tests
        PRIVATE
            jalium.native.media.linux
            jalium.native.media.core)
    target_compile_definitions(jalium.native.media.linux.tests PRIVATE
        JALIUM_TEST_HAS_GSTREAMER=$<BOOL:${JALIUM_GSTREAMER_FOUND}>)
    add_test(NAME jalium.native.media.linux
             COMMAND jalium.native.media.linux.tests)
endif()

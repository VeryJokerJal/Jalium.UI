project(jalium.native.media.linux)

find_package(Threads REQUIRED)
find_package(PkgConfig QUIET)

if(PkgConfig_FOUND)
    pkg_check_modules(JALIUM_GSTREAMER QUIET IMPORTED_TARGET
        gstreamer-1.0>=1.16
        gstreamer-app-1.0>=1.16
        gstreamer-video-1.0>=1.16
        gstreamer-audio-1.0>=1.16
        gstreamer-allocators-1.0>=1.16
        gstreamer-pbutils-1.0>=1.16)
endif()

set(SOURCES
    src/gstreamer_loader.cpp
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
        ${CMAKE_CURRENT_SOURCE_DIR}/../thirdparty
)

target_link_libraries(${PROJECT_NAME}
    PRIVATE
        jalium.native.media.core
        Threads::Threads
        ${CMAKE_DL_LIBS}
        m
)

if(JALIUM_GSTREAMER_FOUND)
    # Headers and compile flags are used to keep the runtime symbol table ABI
    # correct. Do not link PkgConfig::JALIUM_GSTREAMER: GStreamer is optional
    # and is loaded by gstreamer_loader.cpp only when the capability is used.
    target_include_directories(${PROJECT_NAME} PRIVATE
        ${JALIUM_GSTREAMER_INCLUDE_DIRS})
    target_compile_options(${PROJECT_NAME} PRIVATE
        ${JALIUM_GSTREAMER_CFLAGS_OTHER})
    target_compile_definitions(${PROJECT_NAME} PRIVATE JALIUM_HAS_GSTREAMER=1)
    message(STATUS
        "Linux media: runtime-loaded GStreamer backend enabled")
else()
    target_compile_definitions(${PROJECT_NAME} PRIVATE JALIUM_HAS_GSTREAMER=0)
    message(WARNING
        "Linux media: GStreamer development packages were not found. "
        "Building the ABI-complete fallback backend; image/video/camera/AAC "
        "entry points will report UNSUPPORTED or NOT_IMPLEMENTED.")
endif()

if(NOT JALIUM_BUILD_STATIC)
    # Catch a newly introduced direct GStreamer/GLib reference at link time.
    # Without -z defs, ELF permits unresolved symbols in a shared library and
    # the mistake would only surface when managed code first loads the .so.
    target_link_options(${PROJECT_NAME} PRIVATE "LINKER:-z,defs")
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
        ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.media.core/include
        ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.core/include)
    target_link_libraries(jalium.native.media.linux.tests
        PRIVATE
            jalium.native.media.linux
            jalium.native.media.core)
    target_compile_definitions(jalium.native.media.linux.tests PRIVATE
        JALIUM_TEST_HAS_GSTREAMER=$<BOOL:${JALIUM_GSTREAMER_FOUND}>)
    add_test(NAME jalium.native.media.linux.loader-present
             COMMAND jalium.native.media.linux.tests)
    add_test(NAME jalium.native.media.linux.loader-absent
             COMMAND jalium.native.media.linux.tests --expect-no-gstreamer)
    set_tests_properties(jalium.native.media.linux.loader-absent PROPERTIES
        ENVIRONMENT "JALIUM_MEDIA_DISABLE_GSTREAMER=1")

    if(CMAKE_READELF AND CMAKE_NM)
        add_test(NAME jalium.native.media.linux.no-direct-gstreamer-dependency
            COMMAND ${CMAKE_COMMAND}
                -DREADELF=${CMAKE_READELF}
                -DNM=${CMAKE_NM}
                -DLIBRARY=$<TARGET_FILE:jalium.native.media.linux>
                -P ${CMAKE_CURRENT_SOURCE_DIR}/tests/check_no_gstreamer_needed.cmake)
    endif()

    # A generated fixture avoids checking codec binaries into source while
    # still proving the actual H.264 decoder, AAC decoder, track/subtitle
    # selectors, accurate seek and HTTP uridecodebin lifecycle. The test is
    # registered only when both generation/runtime helpers are available;
    # Linux CI installs them explicitly.
    find_package(Python3 COMPONENTS Interpreter QUIET)
    find_program(JALIUM_MEDIA_FFMPEG_EXECUTABLE NAMES ffmpeg)
    if(JALIUM_GSTREAMER_FOUND AND Python3_Interpreter_FOUND AND
       JALIUM_MEDIA_FFMPEG_EXECUTABLE)
        add_test(NAME jalium.native.media.linux.real-h264-http-multitrack
            COMMAND ${Python3_EXECUTABLE}
                ${CMAKE_CURRENT_SOURCE_DIR}/tests/run_real_media_smoke.py
                --smoke $<TARGET_FILE:jalium.native.media.linux.tests>
                --ffmpeg ${JALIUM_MEDIA_FFMPEG_EXECUTABLE}
                --subtitle ${CMAKE_CURRENT_SOURCE_DIR}/tests/media_smoke.srt
                --workdir ${CMAKE_CURRENT_BINARY_DIR}/real-media-smoke)
        set_tests_properties(
            jalium.native.media.linux.real-h264-http-multitrack
            PROPERTIES TIMEOUT 90)
    endif()
endif()

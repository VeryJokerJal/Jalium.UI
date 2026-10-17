project(jalium.native.media.apple)

# miniaudio's mobile CoreAudio backend imports AVFoundation and uses Objective-C.
# Keep the shared .cpp source portable, but compile this Apple target as ObjC++.
set_source_files_properties(
    ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.media.core/src/audio/miniaudio_backend.cpp
    PROPERTIES LANGUAGE OBJCXX
)

add_library(${PROJECT_NAME} ${JALIUM_LIB_TYPE}
    src/apple_media.mm
    src/apple_media_availability.h
    ${JALIUM_AUDIO_SOURCES}
)

target_include_directories(${PROJECT_NAME} PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.media.core/include
    ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.core/include
    ${JALIUM_AUDIO_PRIVATE_INCLUDES}
)

target_link_libraries(${PROJECT_NAME} PRIVATE
    jalium.native.media.core
    "-framework Foundation"
    "-framework CoreFoundation"
    "-framework CoreGraphics"
    "-framework ImageIO"
    "-framework AVFoundation"
    "-framework CoreMedia"
    "-framework CoreVideo"
    "-framework VideoToolbox"
    "-framework AudioToolbox"
)

target_compile_definitions(${PROJECT_NAME} PRIVATE JALIUM_MEDIA_EXPORTS)
target_compile_options(${PROJECT_NAME} PRIVATE -fobjc-arc)
set_target_properties(${PROJECT_NAME} PROPERTIES
    OUTPUT_NAME "jalium.native.media"
    DEBUG_POSTFIX ""
)

option(JALIUM_MEDIA_APPLE_BUILD_TESTS "Build Apple media availability and video tests" OFF)
if((JALIUM_MEDIA_APPLE_BUILD_TESTS OR JALIUM_PLATFORM_BUILD_TESTS) AND
   CMAKE_SYSTEM_NAME STREQUAL "Darwin")
    add_executable(jalium.native.media.apple.tests tests/apple_media_tests.mm)
    target_include_directories(jalium.native.media.apple.tests PRIVATE
        src ../jalium.native.media.core/include ../jalium.native.core/include)
    target_compile_options(jalium.native.media.apple.tests PRIVATE -fobjc-arc)
    target_link_libraries(jalium.native.media.apple.tests PRIVATE
        jalium.native.media.apple "-framework Foundation" "-framework AVFoundation"
        "-framework CoreMedia" "-framework CoreVideo")
    add_test(NAME jalium.native.media.apple.availability COMMAND jalium.native.media.apple.tests)
    set_tests_properties(jalium.native.media.apple.availability PROPERTIES TIMEOUT 60)
endif()

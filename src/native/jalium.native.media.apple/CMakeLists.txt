project(jalium.native.media.apple)

set(HEADERS
)

set(SOURCES
    src/apple_media_exports.cpp
)

# Compile shared audio TUs into the Apple media library so jalium_audio_* and
# telemetry exports ship from libjalium.native.media.dylib.
add_library(${PROJECT_NAME} ${JALIUM_LIB_TYPE}
    ${HEADERS}
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
        "-framework CoreAudio"
        "-framework AudioToolbox"
        "-framework CoreFoundation"
)

target_compile_definitions(${PROJECT_NAME}
    PRIVATE
        JALIUM_MEDIA_EXPORTS
)

# Single library name across platforms — Apple produces libjalium.native.media.dylib
set_target_properties(${PROJECT_NAME} PROPERTIES
    OUTPUT_NAME "jalium.native.media"
    DEBUG_POSTFIX ""
)

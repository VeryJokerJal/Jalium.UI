project(jalium.native.media.apple)

add_library(${PROJECT_NAME} ${JALIUM_LIB_TYPE}
    src/apple_media.mm
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

project(jalium.native.media.windows)

set(HEADERS
    include/win_media_init.h
    include/win_wic_decoder.h
    include/win_mf_video_decoder.h
    include/win_mf_camera_source.h
)

set(SOURCES
    src/win_media_init.cpp
    src/win_wic_decoder.cpp
    src/win_mf_video_decoder.cpp
    src/win_mf_codec_caps.cpp
    src/win_mf_camera_source.cpp
    src/win_media_exports.cpp
)

add_library(${PROJECT_NAME} ${JALIUM_LIB_TYPE} ${HEADERS} ${SOURCES})

target_include_directories(${PROJECT_NAME}
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/include
)

target_link_libraries(${PROJECT_NAME}
    PRIVATE
        jalium.native.media.core
        windowscodecs
        mfplat
        mfreadwrite
        mf
        mfuuid
        propsys
        ole32
        oleaut32
        shlwapi
)

target_compile_definitions(${PROJECT_NAME}
    PRIVATE
        JALIUM_MEDIA_EXPORTS
)

# Single library name across platforms — Windows produces jalium.native.media.dll
set_target_properties(${PROJECT_NAME} PROPERTIES
    OUTPUT_NAME "jalium.native.media"
    DEBUG_POSTFIX ""
)

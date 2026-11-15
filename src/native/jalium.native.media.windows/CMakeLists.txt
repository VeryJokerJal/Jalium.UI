project(jalium.native.media.windows)

set(HEADERS
    include/win_media_init.h
    include/win_wic_decoder.h
    include/win_mf_video_decoder.h
    include/win_mf_camera_source.h
    include/win_mf_aac_decoder.h
)

set(SOURCES
    src/win_media_init.cpp
    src/win_wic_decoder.cpp
    src/win_mf_video_decoder.cpp
    src/win_mf_codec_caps.cpp
    src/win_mf_camera_source.cpp
    src/win_mf_aac_decoder.cpp
    src/win_media_exports.cpp
)

# Pull in the shared audio TUs so their symbols ship from this DLL too
# (see root CMakeLists.txt for the rationale).
add_library(${PROJECT_NAME} ${JALIUM_LIB_TYPE}
    ${HEADERS}
    ${SOURCES}
    ${JALIUM_AUDIO_SOURCES}
)

target_include_directories(${PROJECT_NAME}
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/include
        ${JALIUM_AUDIO_PRIVATE_INCLUDES}
)

target_link_libraries(${PROJECT_NAME}
    PRIVATE
        jalium.native.media.core
        windowscodecs
        d3d11
        mfplat
        mfreadwrite
        mf
        mfuuid
        propsys
        ole32
        oleaut32
        shlwapi
        winmm           # miniaudio WASAPI/WinMM backend
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

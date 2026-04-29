project(jalium.native.media.android)

set(HEADERS
    include/and_media_init.h
    include/and_image_decoder.h
    include/and_video_decoder.h
    include/and_camera_source.h
    include/and_yuv_simd.h
)

set(SOURCES
    src/and_media_init.cpp
    src/and_image_decoder.cpp
    src/and_jni_image_fallback.cpp
    src/and_video_decoder.cpp
    src/and_camera_source.cpp
    src/and_codec_caps.cpp
    src/and_yuv_scalar.cpp
    src/and_yuv_neon.cpp
    src/and_yuv_sse2.cpp
    src/and_media_exports.cpp
)

add_library(${PROJECT_NAME} ${JALIUM_LIB_TYPE} ${HEADERS} ${SOURCES})

target_include_directories(${PROJECT_NAME}
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/include
)

target_link_libraries(${PROJECT_NAME}
    PRIVATE
        jalium.native.media.core
        jalium.native.platform
        mediandk
        jnigraphics
        camera2ndk
        android
        log
)

target_compile_definitions(${PROJECT_NAME}
    PRIVATE
        JALIUM_MEDIA_EXPORTS
)

# Per-ABI SIMD enablement.
if(${ANDROID_ABI} STREQUAL "arm64-v8a")
    target_compile_options(${PROJECT_NAME} PRIVATE -march=armv8-a+simd)
elseif(${ANDROID_ABI} STREQUAL "x86_64")
    target_compile_options(${PROJECT_NAME} PRIVATE -msse2)
endif()

# Single library name across platforms — Android produces libjalium.native.media.so
set_target_properties(${PROJECT_NAME} PROPERTIES
    OUTPUT_NAME "jalium.native.media"
    DEBUG_POSTFIX ""
)

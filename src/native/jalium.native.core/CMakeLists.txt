project(jalium.native.core)

set(HEADERS
    include/jalium_api.h
    include/jalium_types.h
    include/jalium_backend.h
    include/jalium_internal.h
    include/jalium_abi_guard.h
    include/jalium_rendering_engine.h
    include/jalium_triangulate.h
    include/jalium_path_stats.h
    include/jalium_path_cache.h
    include/jalium_stencil_path.h
    include/jalium_flatten.h
    include/jalium_bitmap_stats.h
    include/jalium_text_stats.h
    include/jalium_text_options.h
    include/jalium_dwrite_font_units.h
    include/jalium_font_resource.h
    include/jalium_elliptical_clip.h
    include/jalium_elliptical_clip_shader.h
    include/jalium_video_surface.h
)

set(SOURCES
    src/context.cpp
    src/render_target.cpp
    src/brush.cpp
    src/text_paragraph.cpp
    src/font_resource.cpp
    src/bitmap.cpp
    src/backend_registry.cpp
    src/jalium_path_stats.cpp
    src/jalium_path_cache.cpp
    src/jalium_stencil_path.cpp
    src/jalium_bitmap_stats.cpp
    src/jalium_text_stats.cpp
    src/jalium_text_options.cpp
    src/video_surface.cpp
)

set(SHADER_HEADERS
    shaders/continuous_corner.hlsli
    shaders/elliptical_clip.hlsli
)
set_source_files_properties(${SHADER_HEADERS} PROPERTIES HEADER_FILE_ONLY TRUE)
source_group("Shaders" FILES ${SHADER_HEADERS})

add_library(${PROJECT_NAME} ${JALIUM_LIB_TYPE}
    ${HEADERS}
    ${SOURCES}
    ${SHADER_HEADERS}
)

target_include_directories(${PROJECT_NAME}
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
)

if(NOT JALIUM_BUILD_STATIC)
    target_compile_definitions(${PROJECT_NAME}
        PRIVATE
            JALIUM_EXPORTS
    )
endif()

# Link Windows libraries
if(WIN32)
    target_sources(${PROJECT_NAME} PRIVATE src/text_outline_win.cpp)
    target_link_libraries(${PROJECT_NAME}
        PRIVATE
            kernel32
            user32
            dwrite
            gdi32
    )
elseif(APPLE)
    target_link_libraries(${PROJECT_NAME} PRIVATE "-framework CoreText" "-framework CoreGraphics" "-framework CoreFoundation")
    target_sources(${PROJECT_NAME} PRIVATE src/apple_font.mm include/jalium_apple_font.h)
    target_compile_options(${PROJECT_NAME} PRIVATE $<$<COMPILE_LANGUAGE:OBJCXX>:-fobjc-arc>)
    if(CMAKE_SYSTEM_NAME STREQUAL "Darwin")
        target_link_libraries(${PROJECT_NAME} PRIVATE "-framework AppKit")
    endif()
elseif(ANDROID)
    target_link_libraries(${PROJECT_NAME}
        PRIVATE
            log
    )
elseif(UNIX)
    # context.cpp discovers backend DSOs with dlopen/dlsym. Newer glibc folds
    # libdl into libc, but the Ubuntu 20.04 / glibc 2.31 release baseline still
    # requires an explicit -ldl dependency.
    target_link_libraries(${PROJECT_NAME}
        PRIVATE
            ${CMAKE_DL_LIBS}
    )
endif()

# Set output name
set_target_properties(${PROJECT_NAME} PROPERTIES
    OUTPUT_NAME "jalium.native.core"
    DEBUG_POSTFIX ""
)

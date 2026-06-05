project(jalium.native.core)

set(HEADERS
    include/jalium_api.h
    include/jalium_types.h
    include/jalium_backend.h
    include/jalium_internal.h
    include/jalium_rendering_engine.h
    include/jalium_triangulate.h
    include/jalium_path_stats.h
    include/jalium_path_cache.h
    include/jalium_stencil_path.h
    include/jalium_flatten.h
    include/jalium_bitmap_stats.h
    include/jalium_text_stats.h
    include/jalium_text_options.h
    include/jalium_video_surface.h
)

set(SOURCES
    src/context.cpp
    src/render_target.cpp
    src/brush.cpp
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

add_library(${PROJECT_NAME} ${JALIUM_LIB_TYPE} ${HEADERS} ${SOURCES})

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
    target_link_libraries(${PROJECT_NAME}
        PRIVATE
            kernel32
            user32
    )
elseif(ANDROID)
    target_link_libraries(${PROJECT_NAME}
        PRIVATE
            log
    )
endif()

# Set output name
set_target_properties(${PROJECT_NAME} PROPERTIES
    OUTPUT_NAME "jalium.native.core"
    DEBUG_POSTFIX ""
)

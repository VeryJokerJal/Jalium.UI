project(jalium.native.d3d12)

set(HEADERS
    include/d3d12_backend.h
    include/d3d12_direct_renderer.h
    include/d3d12_glyph_atlas.h
    include/d3d12_impeller_engine.h
    include/d3d12_pipeline.h
    include/d3d12_render_target.h
    include/d3d12_resources.h
    include/d3d12_shader_bytecode.h
    include/d3d12_shader_source.h
    include/d3d12_triangulate.h
    include/d3d12_vello.h
    include/d3d12_vello_engine.h
    include/d3dx12.h
    include/liquid_glass_effects.h
    include/transition_shader_effect.h
)

set(SOURCES
    src/d3d12_backend.cpp
    src/d3d12_bitmap.cpp
    src/d3d12_brush.cpp
    src/d3d12_direct_renderer.cpp
    src/d3d12_glyph_atlas.cpp
    src/d3d12_impeller_engine.cpp
    src/d3d12_init.cpp
    src/d3d12_render_target.cpp
    src/d3d12_text.cpp
    src/d3d12_vello.cpp
    src/d3d12_vello_engine.cpp
    src/liquid_glass_effects.cpp
    src/transition_shader_effect.cpp
)

add_library(${PROJECT_NAME} SHARED ${HEADERS} ${SOURCES})

target_include_directories(${PROJECT_NAME}
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.core/include
)

target_compile_definitions(${PROJECT_NAME}
    PRIVATE
        JALIUM_D3D12_EXPORTS
)

# Link against jalium.native.core and Windows libraries
target_link_libraries(${PROJECT_NAME}
    PRIVATE
        jalium.native.core
        d3d12
        dxgi
        d3dcompiler
        dwrite
        d2d1
        dcomp
        dxguid
)

# Set output name
set_target_properties(${PROJECT_NAME} PROPERTIES
    OUTPUT_NAME "jalium.native.d3d12"
    DEBUG_POSTFIX ""
)

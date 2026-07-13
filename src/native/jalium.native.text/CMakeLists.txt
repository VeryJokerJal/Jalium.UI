project(jalium.native.text)

set(HEADERS
    include/text_engine.h
    include/text_shaper.h
    include/text_layout.h
    include/font_provider.h
    include/glyph_rasterizer.h
    include/glyph_atlas.h
    # Self-hosted font engine (replaces FreeType + HarfBuzz)
    include/sfnt_reader.h
    include/font_data.h
    include/font_face.h
    include/opentype_cmap.h
    include/glyf_outline.h
    include/cff_charstring.h
    include/ot_layout.h
    include/ot_shaper.h
)

set(SOURCES
    src/text_engine.cpp
    src/text_shaper.cpp
    src/text_layout.cpp
    src/font_provider.cpp
    src/glyph_rasterizer.cpp
    src/glyph_atlas.cpp
    # Self-hosted font engine
    src/font_face.cpp
    src/opentype_cmap.cpp
    src/glyf_outline.cpp
    src/cff_charstring.cpp
    src/ot_layout.cpp
    src/ot_shaper.cpp
)

# Platform-specific font discovery (path resolution only — no FreeType).
if(ANDROID)
    list(APPEND SOURCES src/font_provider_android.cpp)
elseif(UNIX AND NOT APPLE)
    list(APPEND SOURCES src/font_provider_fontconfig.cpp)
endif()

add_library(${PROJECT_NAME} ${JALIUM_LIB_TYPE} ${HEADERS} ${SOURCES})

if(NOT JALIUM_BUILD_STATIC)
    target_compile_definitions(${PROJECT_NAME} PRIVATE JALIUM_TEXT_EXPORTS)
endif()

target_include_directories(${PROJECT_NAME}
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.core/include
)

target_link_libraries(${PROJECT_NAME}
    PRIVATE
        jalium.native.core
)

# Fontconfig (Linux desktop only) — font DISCOVERY, not rendering. Android reads
# /system/fonts + fonts.xml directly; the self-hosted engine parses the resolved
# file bytes itself, so no FreeType/HarfBuzz dependency remains.
#
# REQUIRED, not optional: font_provider_fontconfig.cpp is compiled
# unconditionally on Linux desktop and #includes <fontconfig/fontconfig.h>, so
# a missing dev package failed later with a confusing compile error (or a
# quiet link break in cross builds). Fail at configure time with the fix.
if(UNIX AND NOT APPLE AND NOT ANDROID)
    find_package(PkgConfig REQUIRED)
    pkg_check_modules(FONTCONFIG REQUIRED fontconfig)
    target_include_directories(${PROJECT_NAME} PRIVATE ${FONTCONFIG_INCLUDE_DIRS})
    target_link_libraries(${PROJECT_NAME} PRIVATE ${FONTCONFIG_LIBRARIES})
endif()

set_target_properties(${PROJECT_NAME} PROPERTIES
    OUTPUT_NAME "jalium.native.text"
    DEBUG_POSTFIX ""
)

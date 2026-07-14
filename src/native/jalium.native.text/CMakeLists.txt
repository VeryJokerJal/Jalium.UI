project(jalium.native.text)

set(HEADERS
    include/jalium_text_api.h
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
        ${CMAKE_CURRENT_SOURCE_DIR}/../thirdparty/stb
)

if(NOT EXISTS "${CMAKE_CURRENT_SOURCE_DIR}/../thirdparty/stb/stb_image.h")
    message(FATAL_ERROR "Vendored stb_image.h is required for CBDT/CBLC PNG color glyphs")
endif()

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

    # The renderer remains self-hosted.  FreeType is used only as an optional,
    # runtime-loaded decoder for color formats whose paint graph is outside our
    # COLR-v0/CBDT implementation (notably COLR v1).  Link against libdl, never
    # libfreetype, so packaging and NativeAOT do not gain a hard dependency.
    pkg_check_modules(FREETYPE_HEADERS QUIET freetype2)
    if(FREETYPE_HEADERS_FOUND)
        target_include_directories(${PROJECT_NAME} PRIVATE ${FREETYPE_HEADERS_INCLUDE_DIRS})
        target_compile_definitions(${PROJECT_NAME} PRIVATE JALIUM_HAS_FREETYPE_HEADERS=1)
        target_link_libraries(${PROJECT_NAME} PRIVATE ${CMAKE_DL_LIBS})
    endif()
endif()

set_target_properties(${PROJECT_NAME} PROPERTIES
    OUTPUT_NAME "jalium.native.text"
    DEBUG_POSTFIX ""
)

option(JALIUM_TEXT_BUILD_TESTS "Build Linux self-hosted text pixel/fallback tests" OFF)
if(JALIUM_TEXT_BUILD_TESTS AND UNIX AND NOT APPLE AND NOT ANDROID)
    # The pixel tests intentionally reach text-engine implementation classes
    # (FontFace, GlyphRasterizer, FontProviderFontconfig). Those are not part of
    # the production cross-library ABI and must stay hidden from the shared
    # object's dynamic symbol table. Compile the same sources into a test-only
    # static archive instead of weakening libjalium.native.text.so's whitelist.
    add_library(jalium.native.text.test-support STATIC ${HEADERS} ${SOURCES})
    target_compile_definitions(jalium.native.text.test-support PRIVATE JALIUM_STATIC)
    target_include_directories(jalium.native.text.test-support
        PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/include
        PRIVATE
            ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.core/include
            ${CMAKE_CURRENT_SOURCE_DIR}/../thirdparty/stb
            ${FONTCONFIG_INCLUDE_DIRS})
    target_link_libraries(jalium.native.text.test-support PRIVATE
        jalium.native.core
        ${FONTCONFIG_LIBRARIES})
    if(FREETYPE_HEADERS_FOUND)
        target_include_directories(jalium.native.text.test-support PRIVATE
            ${FREETYPE_HEADERS_INCLUDE_DIRS})
        target_compile_definitions(jalium.native.text.test-support PRIVATE
            JALIUM_HAS_FREETYPE_HEADERS=1)
        target_link_libraries(jalium.native.text.test-support PRIVATE ${CMAKE_DL_LIBS})
    endif()

    add_executable(jalium.native.text.tests
        tests/text_linux_tests.cpp)
    target_include_directories(jalium.native.text.tests PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/include
        ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.core/include)
    target_link_libraries(jalium.native.text.tests PRIVATE
        jalium.native.text.test-support
        jalium.native.core)
    add_test(NAME jalium.native.text.pixel-and-fallback
             COMMAND jalium.native.text.tests)
    set_tests_properties(jalium.native.text.pixel-and-fallback PROPERTIES
        SKIP_RETURN_CODE 77)
endif()

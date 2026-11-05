# SheenBidi is compiled into the text module itself, including static/AOT
# archives. Consumers never need to discover or link another native library.
enable_language(C)
set(JALIUM_SHEENBIDI_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/../thirdparty/sheenbidi")
set(JALIUM_SHEENBIDI_SOURCE "${JALIUM_SHEENBIDI_ROOT}/Source/SheenBidi.c")
set_source_files_properties("${JALIUM_SHEENBIDI_SOURCE}" PROPERTIES COMPILE_DEFINITIONS SB_CONFIG_UNITY)
option(JALIUM_BIDI_BUILD_TESTS "Build portable Unicode bidi integration checks" OFF)
if(JALIUM_BIDI_BUILD_TESTS)
    add_library(jalium.native.bidi STATIC "${JALIUM_SHEENBIDI_SOURCE}")
    target_include_directories(jalium.native.bidi PUBLIC "${JALIUM_SHEENBIDI_ROOT}/Headers" PRIVATE "${JALIUM_SHEENBIDI_ROOT}/Source")
    set_target_properties(jalium.native.bidi PROPERTIES POSITION_INDEPENDENT_CODE ON C_STANDARD 11 C_STANDARD_REQUIRED ON C_VISIBILITY_PRESET hidden)
endif()

project(jalium.native.text)

set(HEADERS
    include/jalium_text_api.h
    include/text_engine.h
    include/text_shaper.h
    include/text_bidi.h
    include/text_layout.h
    include/shared_text_paragraph.h
    include/text_grapheme.h
    include/unicode_grapheme_data.h
    include/shared_text_paragraph.h
    include/font_provider.h
    include/glyph_rasterizer.h
    include/glyph_atlas.h
    include/jalium_unicode_casing.h
    include/jalium_unicode_casing_data.h
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
    "${JALIUM_SHEENBIDI_SOURCE}"
    src/text_engine.cpp
    src/text_shaper.cpp
    src/text_bidi.cpp
    src/text_layout.cpp
    src/shared_text_paragraph.cpp
    src/shared_text_paragraph.cpp
    src/text_outline.cpp
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
        ${JALIUM_SHEENBIDI_ROOT}/Headers
        ${JALIUM_SHEENBIDI_ROOT}/Source
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
    C_STANDARD 11
    C_STANDARD_REQUIRED ON
)

option(JALIUM_TEXT_BUILD_TESTS "Build Linux self-hosted text pixel/fallback tests" OFF)
option(JALIUM_FONT_FEATURE_BUILD_TESTS "Build portable OpenType FeatureList checks" OFF)
option(JALIUM_UNICODE_CASING_BUILD_TESTS "Build portable Unicode CSS casing checks" OFF)
if(JALIUM_UNICODE_CASING_BUILD_TESTS)
    enable_testing()
    add_executable(jalium.native.unicode-casing.tests tests/unicode_casing_tests.cpp)
    target_include_directories(jalium.native.unicode-casing.tests PRIVATE include)
    add_test(NAME jalium.native.unicode-casing.tests COMMAND jalium.native.unicode-casing.tests)
    set_tests_properties(jalium.native.unicode-casing.tests PROPERTIES TIMEOUT 30)
endif()
if(JALIUM_BIDI_BUILD_TESTS)
    enable_testing()
    add_executable(jalium.native.text.grapheme.tests tests/text_grapheme_tests.cpp)
    target_include_directories(jalium.native.text.grapheme.tests PRIVATE include)
    add_test(NAME jalium.native.text.grapheme COMMAND jalium.native.text.grapheme.tests
        "${CMAKE_CURRENT_SOURCE_DIR}/../thirdparty/unicode-grapheme-17/GraphemeBreakTest.txt")
    set_tests_properties(jalium.native.text.grapheme PROPERTIES TIMEOUT 30)
endif()
if(JALIUM_FONT_FEATURE_BUILD_TESTS)
    enable_testing()
    add_executable(jalium.native.text.gvar-metrics tests/gvar_metrics_tests.cpp)
    target_include_directories(jalium.native.text.gvar-metrics PRIVATE include)
    add_test(NAME jalium.native.text.gvar-metrics COMMAND jalium.native.text.gvar-metrics
        "${CMAKE_CURRENT_SOURCE_DIR}/../../../tests/Jalium.UI.MacOS.Tests/Assets/FontGvarMetrics")
    set_tests_properties(jalium.native.text.gvar-metrics PROPERTIES TIMEOUT 30)
    add_executable(jalium.native.text.vertical-alias tests/vertical_alias_tests.cpp)
    target_include_directories(jalium.native.text.vertical-alias PRIVATE include)
    add_test(NAME jalium.native.text.vertical-alias COMMAND jalium.native.text.vertical-alias
        "${CMAKE_CURRENT_SOURCE_DIR}/../../../tests/Jalium.UI.MacOS.Tests/Assets/FontVerticalForms")
    set_tests_properties(jalium.native.text.vertical-alias PROPERTIES TIMEOUT 30)
    add_executable(jalium.native.font-feature-parser.tests tests/font_feature_availability_tests.cpp)
    target_include_directories(jalium.native.font-feature-parser.tests PRIVATE ../jalium.native.core/include)
    add_test(NAME jalium.native.font-feature-parser.tests COMMAND jalium.native.font-feature-parser.tests)
endif()
if(JALIUM_TEXT_BUILD_TESTS AND UNIX AND NOT APPLE AND NOT ANDROID)
    # The pixel tests intentionally reach text-engine implementation classes
    # (FontFace, GlyphRasterizer, FontProviderFontconfig). Those are not part of
    # the production cross-library ABI and must stay hidden from the shared
    # object's dynamic symbol table. Compile the same sources into a test-only
    # static archive instead of weakening libjalium.native.text.so's whitelist.
    add_library(jalium.native.text.test-support STATIC ${HEADERS} ${SOURCES})
    set_target_properties(jalium.native.text.test-support PROPERTIES C_STANDARD 11 C_STANDARD_REQUIRED ON)
    target_compile_definitions(jalium.native.text.test-support PRIVATE JALIUM_STATIC)
    target_include_directories(jalium.native.text.test-support
        PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/include
        PRIVATE
            ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.core/include
            ${CMAKE_CURRENT_SOURCE_DIR}/../thirdparty/stb
            ${FONTCONFIG_INCLUDE_DIRS}
            ${JALIUM_SHEENBIDI_ROOT}/Headers
            ${JALIUM_SHEENBIDI_ROOT}/Source)
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

    add_executable(jalium.native.text.paragraph.tests tests/shared_text_paragraph_tests.cpp)
    target_include_directories(jalium.native.text.paragraph.tests PRIVATE include ../jalium.native.core/include)
    target_link_libraries(jalium.native.text.paragraph.tests PRIVATE jalium.native.text.test-support jalium.native.core)
    add_test(NAME jalium.native.text.paragraph COMMAND jalium.native.text.paragraph.tests)
    set_tests_properties(jalium.native.text.paragraph PROPERTIES TIMEOUT 60)

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
    add_test(NAME jalium.native.text.outline-path
             COMMAND jalium.native.text.tests --outline-only)
    set_tests_properties(jalium.native.text.pixel-and-fallback PROPERTIES
        SKIP_RETURN_CODE 77)

    add_executable(jalium.native.text.bidi-layout.tests tests/text_bidi_layout_tests.cpp)
    target_include_directories(jalium.native.text.bidi-layout.tests PRIVATE include ../jalium.native.core/include)
    target_link_libraries(jalium.native.text.bidi-layout.tests PRIVATE jalium.native.text jalium.native.core)
    add_test(NAME jalium.native.text.bidi-layout COMMAND jalium.native.text.bidi-layout.tests)

    add_executable(jalium.native.string-util.tests
        tests/string_util_tests.cpp)
    target_include_directories(jalium.native.string-util.tests PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.core/include)
    add_test(NAME jalium.native.core.string-util-bounds
             COMMAND jalium.native.string-util.tests)

    add_executable(jalium.native.abi-guard.tests
        tests/abi_guard_tests.cpp)
    target_include_directories(jalium.native.abi-guard.tests PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.core/include)
    add_test(NAME jalium.native.core.abi-guard
             COMMAND jalium.native.abi-guard.tests)

    add_executable(jalium.native.geometry-safety.tests
        tests/geometry_safety_tests.cpp)
    target_include_directories(jalium.native.geometry-safety.tests PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.core/include)
    add_test(NAME jalium.native.core.geometry-safety
             COMMAND jalium.native.geometry-safety.tests)

    add_executable(jalium.native.font-parser-fuzz.tests
        tests/font_parser_fuzz_tests.cpp)
    target_include_directories(jalium.native.font-parser-fuzz.tests PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/include
        ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.core/include)
    target_link_libraries(jalium.native.font-parser-fuzz.tests PRIVATE
        jalium.native.text.test-support
        jalium.native.core)
    add_test(NAME jalium.native.text.font-parser-boundaries
             COMMAND jalium.native.font-parser-fuzz.tests)
endif()

# CoreText layout and CPU glyph rasterization are shared by Metal and Software.
# No Metal device or GPU resource is created by this target's Apple text path.
if(APPLE)
    target_sources(${PROJECT_NAME} PRIVATE
        src/apple_vertical_features.cpp
        src/apple_vertical_metrics.cpp
        ../jalium.native.metal/src/metal_backend.cpp
        ../jalium.native.metal/src/metal_text_paragraph.cpp
        ../jalium.native.metal/src/metal_word_navigation.mm)
    target_compile_definitions(${PROJECT_NAME} PRIVATE JALIUM_APPLE_TEXT_ONLY)
    target_include_directories(${PROJECT_NAME} PRIVATE ../jalium.native.metal/include)
    target_include_directories(${PROJECT_NAME} PUBLIC $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.metal/src>)
    install(FILES ../jalium.native.metal/src/metal_font_visibility.h DESTINATION include)
    set_source_files_properties(../jalium.native.metal/src/metal_backend.cpp ../jalium.native.metal/src/metal_text_paragraph.cpp PROPERTIES LANGUAGE OBJCXX)
    target_compile_options(${PROJECT_NAME} PRIVATE $<$<COMPILE_LANGUAGE:OBJCXX>:-fobjc-arc>)
    target_link_libraries(${PROJECT_NAME} PRIVATE "-framework CoreText" "-framework CoreFoundation" "-framework CoreGraphics")
    if(CMAKE_SYSTEM_NAME STREQUAL "Darwin")
        target_link_libraries(${PROJECT_NAME} PRIVATE "-framework AppKit")
    else()
        target_link_libraries(${PROJECT_NAME} PRIVATE "-framework UIKit")
    endif()
endif()

if(JALIUM_BIDI_BUILD_TESTS)
    enable_testing()
    add_executable(jalium.native.bidi.tests tests/text_bidi_tests.cpp src/text_bidi.cpp)
    target_include_directories(jalium.native.bidi.tests PRIVATE include ../jalium.native.core/include)
    target_link_libraries(jalium.native.bidi.tests PRIVATE jalium.native.bidi)
    add_test(NAME jalium.native.text.bidi COMMAND jalium.native.bidi.tests)
endif()

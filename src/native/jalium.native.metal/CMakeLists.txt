project(jalium.native.metal)

set(HEADERS
    include/metal_backend.h
    src/metal_text_paragraph.h
    src/metal_word_navigation.h
)

set(SOURCES
    src/metal_backend.cpp
    src/metal_render_target.cpp
    src/metal_shader_compiler.cpp
    src/metal_vello.cpp
    src/metal_init.cpp
)

# On Apple platforms, compile as Objective-C++
if(APPLE)
    set_source_files_properties(
        src/metal_backend.cpp
            src/metal_render_target.cpp
        src/metal_shader_compiler.cpp
        src/metal_vello.cpp
        src/metal_init.cpp
        PROPERTIES LANGUAGE OBJCXX
    )
    # Keep symbols in development builds, but optimize bitmap conversion, the
    # frame encoder, and inline analytic rasterizer. Per-pixel C++ loops can consume
    # an entire frame budget when scrolling introduces a fresh subpixel phase.
    if(NOT CMAKE_GENERATOR STREQUAL "Xcode")
        set_property(SOURCE src/metal_render_target.cpp src/metal_backend.cpp APPEND PROPERTY
            COMPILE_OPTIONS "$<$<CONFIG:Debug>:-O1>")
    endif()
endif()

add_library(${PROJECT_NAME} ${JALIUM_LIB_TYPE} ${HEADERS} ${SOURCES})

# Xcode supports configuration-dependent options at target scope only.
# Keep Release optimization intact while retaining the Debug render profile.
if(APPLE AND CMAKE_GENERATOR STREQUAL "Xcode")
    target_compile_options(${PROJECT_NAME} PRIVATE "$<$<CONFIG:Debug>:-O1>")
endif()

target_include_directories(${PROJECT_NAME}
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.core/include
)

target_link_libraries(${PROJECT_NAME}
    PUBLIC jalium.native.text
    PRIVATE
        jalium.native.core
)

if(APPLE)
    target_link_libraries(${PROJECT_NAME}
        PRIVATE
            "-framework Metal"
            "-framework QuartzCore"
            "-framework CoreFoundation"
            "-framework CoreGraphics"
            "-framework CoreText"
            "-framework CoreVideo"
            "-framework ImageIO"
            "-framework IOSurface"
    )
    if(CMAKE_SYSTEM_NAME STREQUAL "Darwin")
        target_link_libraries(${PROJECT_NAME} PRIVATE
            "-framework AppKit" "-framework ScreenCaptureKit")
    else()
        target_link_libraries(${PROJECT_NAME} PRIVATE "-framework UIKit")
    endif()
    target_compile_options(${PROJECT_NAME} PRIVATE -fobjc-arc)
endif()

set(JALIUM_DXC_INCLUDE_DIR "" CACHE PATH "DXC include directory for Apple runtime HLSL")
set(JALIUM_DXC_LIBRARY "" CACHE FILEPATH "DXC static library for the current Apple slice")
set(JALIUM_SPIRV_CROSS_INCLUDE_DIR "" CACHE PATH "SPIRV-Cross C API include directory")
set(JALIUM_SPIRV_CROSS_LIBRARIES "" CACHE STRING "Semicolon-separated SPIRV-Cross static libraries")
option(JALIUM_METAL_REQUIRE_RUNTIME_SHADER_COMPILER
    "Fail Apple packaging builds unless SourceHlsl's static compiler is linked" OFF)
if(JALIUM_DXC_INCLUDE_DIR AND JALIUM_DXC_LIBRARY AND
   JALIUM_SPIRV_CROSS_INCLUDE_DIR AND JALIUM_SPIRV_CROSS_LIBRARIES)
    target_include_directories(${PROJECT_NAME} PRIVATE
        "${JALIUM_DXC_INCLUDE_DIR}" "${JALIUM_SPIRV_CROSS_INCLUDE_DIR}")
    target_link_libraries(${PROJECT_NAME} PRIVATE
        "${JALIUM_DXC_LIBRARY}" ${JALIUM_SPIRV_CROSS_LIBRARIES})
    # DXCompiler.cpp owns the Unix constructor which initializes DXC. A plain
    # archive link only pulls dxcapi.cpp; retain the initializer object as well.
    target_link_options(${PROJECT_NAME} PRIVATE "LINKER:-u,__Z7DllMainv")
    target_compile_definitions(${PROJECT_NAME} PRIVATE
        JALIUM_HAS_DXC_SPIRV_CROSS=1
        JALIUM_METAL_SHADER_CACHE_ABI="dxc-4c7c497b-spvc-cd3fcb26-msl30000-v2")
elseif(APPLE AND JALIUM_METAL_REQUIRE_RUNTIME_SHADER_COMPILER)
    message(FATAL_ERROR
        "The Apple package requires the static DXC/SPIRV-Cross runtime compiler. "
        "Run eng/apple/build-native.sh (which prepares the pinned slice), or "
        "provide JALIUM_DXC_INCLUDE_DIR, JALIUM_DXC_LIBRARY, "
        "JALIUM_SPIRV_CROSS_INCLUDE_DIR and JALIUM_SPIRV_CROSS_LIBRARIES.")
endif()

set_target_properties(${PROJECT_NAME} PROPERTIES
    OUTPUT_NAME "jalium.native.metal"
    DEBUG_POSTFIX ""
)

option(JALIUM_METAL_BUILD_TESTS "Build macOS Metal/CoreText/AppKit smoke tests" OFF)
if(JALIUM_METAL_BUILD_TESTS AND APPLE AND CMAKE_SYSTEM_NAME STREQUAL "Darwin")
    add_executable(jalium.native.metal.resize tests/metal_resize.mm)
    target_compile_options(jalium.native.metal.resize PRIVATE -fobjc-arc)
    target_link_libraries(jalium.native.metal.resize PRIVATE jalium.native.metal jalium.native.core
        "-framework AppKit" "-framework QuartzCore")
    add_test(NAME jalium.native.metal.resize COMMAND jalium.native.metal.resize)
    set_tests_properties(jalium.native.metal.resize PROPERTIES TIMEOUT 30)
    add_executable(jalium.native.metal.gradient-text tests/metal_gradient_text.mm)
    target_compile_options(jalium.native.metal.gradient-text PRIVATE -fobjc-arc)
    target_link_libraries(jalium.native.metal.gradient-text PRIVATE jalium.native.metal jalium.native.core
        "-framework AppKit" "-framework QuartzCore")
    add_test(NAME jalium.native.metal.gradient-text COMMAND jalium.native.metal.gradient-text)
    set_tests_properties(jalium.native.metal.gradient-text PROPERTIES TIMEOUT 90)
    add_executable(jalium.native.metal.text-scroll tests/metal_text_scroll.mm)
    target_compile_options(jalium.native.metal.text-scroll PRIVATE -fobjc-arc)
    target_link_libraries(jalium.native.metal.text-scroll PRIVATE jalium.native.metal jalium.native.core
        "-framework AppKit" "-framework QuartzCore")
    add_test(NAME jalium.native.metal.text-scroll COMMAND jalium.native.metal.text-scroll)
    set_tests_properties(jalium.native.metal.text-scroll PROPERTIES TIMEOUT 90)
    add_executable(jalium.native.metal.readback tests/metal_readback.mm)
    target_compile_options(jalium.native.metal.readback PRIVATE -fobjc-arc)
    target_link_libraries(jalium.native.metal.readback PRIVATE jalium.native.metal jalium.native.core
        "-framework AppKit" "-framework Metal" "-framework QuartzCore")
    add_test(NAME jalium.native.metal.readback COMMAND jalium.native.metal.readback)
    add_test(NAME jalium.native.metal.readback-no-resize COMMAND jalium.native.metal.readback --framework-no-resize)
    add_test(NAME jalium.native.metal.gpu-baseline COMMAND jalium.native.metal.readback --gpu-baseline)
    add_test(NAME jalium.native.metal.gpu-msaa-baseline COMMAND jalium.native.metal.readback --gpu-msaa-baseline)
    foreach(profile scene-usage clip sampled-clip appkit layer resize clip-only scissor-only depth-only)
        add_test(NAME jalium.native.metal.gpu-${profile}-baseline
            COMMAND jalium.native.metal.readback --gpu-${profile}-baseline)
        set_tests_properties(jalium.native.metal.gpu-${profile}-baseline PROPERTIES TIMEOUT 30)
    endforeach()
    set_tests_properties(jalium.native.metal.readback jalium.native.metal.readback-no-resize jalium.native.metal.gpu-baseline
        jalium.native.metal.gpu-msaa-baseline PROPERTIES TIMEOUT 30)
    add_executable(jalium.native.metal.text-metrics tests/metal_text_metrics.mm)
    target_compile_options(jalium.native.metal.text-metrics PRIVATE -fobjc-arc)
    target_link_libraries(jalium.native.metal.text-metrics PRIVATE jalium.native.metal jalium.native.core
        "-framework CoreText" "-framework CoreFoundation" "-framework Foundation")
    add_test(NAME jalium.native.metal.text-metrics COMMAND jalium.native.metal.text-metrics)
    set_tests_properties(jalium.native.metal.text-metrics PROPERTIES TIMEOUT 30)
    add_executable(jalium.native.metal.text-glyph-ranges tests/metal_text_glyph_ranges.mm)
    target_compile_options(jalium.native.metal.text-glyph-ranges PRIVATE -fobjc-arc)
    target_link_libraries(jalium.native.metal.text-glyph-ranges PRIVATE jalium.native.metal jalium.native.core
        "-framework AppKit" "-framework QuartzCore")
    add_test(NAME jalium.native.metal.text-glyph-ranges COMMAND jalium.native.metal.text-glyph-ranges)
    add_dependencies(jalium.native.metal.text-glyph-ranges jalium.native.software)
    add_test(NAME jalium.native.software.apple.text-glyph-ranges COMMAND jalium.native.metal.text-glyph-ranges --software)
    set_tests_properties(jalium.native.metal.text-glyph-ranges jalium.native.software.apple.text-glyph-ranges PROPERTIES TIMEOUT 60)
    add_executable(jalium.native.metal.fonts tests/metal_font_resources.mm tests/metal_font_cascade.mm tests/metal_font_width.mm tests/metal_font_matching.mm)
    target_compile_options(jalium.native.metal.fonts PRIVATE -fobjc-arc)
    target_link_libraries(jalium.native.metal.fonts PRIVATE jalium.native.metal jalium.native.core
        "-framework AppKit" "-framework CoreText")
    add_test(NAME jalium.native.metal.fonts COMMAND jalium.native.metal.fonts)
    set_tests_properties(jalium.native.metal.fonts PROPERTIES TIMEOUT 30)
    add_executable(jalium.native.metal.blending tests/metal_blending.mm)
    target_compile_options(jalium.native.metal.blending PRIVATE -fobjc-arc)
    target_link_libraries(jalium.native.metal.blending PRIVATE jalium.native.metal jalium.native.core "-framework AppKit")
    add_test(NAME jalium.native.metal.blending COMMAND jalium.native.metal.blending)
    set_tests_properties(jalium.native.metal.blending PROPERTIES TIMEOUT 90)
    add_executable(jalium.native.metal.alpha-mask tests/metal_alpha_mask.mm)
    target_compile_options(jalium.native.metal.alpha-mask PRIVATE -fobjc-arc)
    target_link_libraries(jalium.native.metal.alpha-mask PRIVATE jalium.native.metal jalium.native.core "-framework AppKit")
    add_test(NAME jalium.native.metal.alpha-mask COMMAND jalium.native.metal.alpha-mask)
    set_tests_properties(jalium.native.metal.alpha-mask PROPERTIES TIMEOUT 90)
    add_executable(jalium.native.metal.font-face-metrics tests/metal_font_face_metrics.mm)
    target_compile_options(jalium.native.metal.font-face-metrics PRIVATE -fobjc-arc)
    target_link_libraries(jalium.native.metal.font-face-metrics PRIVATE jalium.native.metal jalium.native.core "-framework AppKit" "-framework CoreText")
    add_test(NAME jalium.native.metal.font-face-metrics COMMAND jalium.native.metal.font-face-metrics
        "${CMAKE_CURRENT_SOURCE_DIR}/../../../tests/Jalium.UI.MacOS.Tests/Assets/FontSizeAdjust")
    set_tests_properties(jalium.native.metal.font-face-metrics PROPERTIES TIMEOUT 90)
    add_executable(jalium.native.metal.font-size-adjust tests/metal_font_size_adjust.mm)
    target_compile_options(jalium.native.metal.font-size-adjust PRIVATE -fobjc-arc)
    target_link_libraries(jalium.native.metal.font-size-adjust PRIVATE jalium.native.metal jalium.native.core "-framework AppKit" "-framework CoreText")
    add_test(NAME jalium.native.metal.font-size-adjust COMMAND jalium.native.metal.font-size-adjust
        "${CMAKE_CURRENT_SOURCE_DIR}/../../../tests/Jalium.UI.MacOS.Tests/Assets/FontSizeAdjust")
    set_tests_properties(jalium.native.metal.font-size-adjust PROPERTIES TIMEOUT 90)
    add_executable(jalium.native.metal.font-optical tests/metal_font_optical_sizing.mm)
    target_compile_options(jalium.native.metal.font-optical PRIVATE -fobjc-arc)
    target_link_libraries(jalium.native.metal.font-optical PRIVATE jalium.native.metal jalium.native.core "-framework AppKit" "-framework CoreText")
    add_test(NAME jalium.native.metal.font-optical COMMAND jalium.native.metal.font-optical
        "${CMAKE_CURRENT_SOURCE_DIR}/../../../tests/Jalium.UI.MacOS.Tests/Assets/FontVariations/variabletest_box.ttf")
    set_tests_properties(jalium.native.metal.font-optical PROPERTIES TIMEOUT 90)
    add_executable(jalium.native.metal.font-variations tests/metal_font_variations.mm)
    target_compile_options(jalium.native.metal.font-variations PRIVATE -fobjc-arc)
    target_link_libraries(jalium.native.metal.font-variations PRIVATE jalium.native.metal jalium.native.core "-framework AppKit" "-framework CoreText")
    add_test(NAME jalium.native.metal.font-variations COMMAND jalium.native.metal.font-variations
        "${CMAKE_CURRENT_SOURCE_DIR}/../../../tests/Jalium.UI.MacOS.Tests/Assets/FontVariations/variabletest_box.ttf")
    set_tests_properties(jalium.native.metal.font-variations PROPERTIES TIMEOUT 60)
    add_executable(jalium.native.metal.font-features tests/metal_font_features.mm)
    target_compile_options(jalium.native.metal.font-features PRIVATE -fobjc-arc)
    target_link_libraries(jalium.native.metal.font-features PRIVATE jalium.native.metal jalium.native.core
        "-framework AppKit" "-framework CoreText")
    add_test(NAME jalium.native.metal.font-features COMMAND jalium.native.metal.font-features
        "${CMAKE_CURRENT_SOURCE_DIR}/../../../tests/Jalium.UI.MacOS.Tests/Assets/FontNumeric/FontWithFancyFeatures.otf")
    set_tests_properties(jalium.native.metal.font-features PROPERTIES TIMEOUT 60)
    add_executable(jalium.native.metal.font-caps tests/metal_font_caps.mm)
    target_include_directories(jalium.native.metal.font-caps PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}/src")
    target_compile_options(jalium.native.metal.font-caps PRIVATE -fobjc-arc)
    target_link_libraries(jalium.native.metal.font-caps PRIVATE jalium.native.metal jalium.native.core
        "-framework AppKit" "-framework CoreText")
    add_test(NAME jalium.native.metal.font-caps COMMAND jalium.native.metal.font-caps)
    set_tests_properties(jalium.native.metal.font-caps PROPERTIES TIMEOUT 60)
    add_executable(jalium.native.metal.font-emoji tests/metal_font_emoji.mm)
    target_include_directories(jalium.native.metal.font-emoji PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}/src")
    target_compile_options(jalium.native.metal.font-emoji PRIVATE -fobjc-arc)
    target_link_libraries(jalium.native.metal.font-emoji PRIVATE jalium.native.metal jalium.native.core
        "-framework AppKit" "-framework CoreText")
    add_test(NAME jalium.native.metal.font-emoji COMMAND jalium.native.metal.font-emoji)
    set_tests_properties(jalium.native.metal.font-emoji PROPERTIES TIMEOUT 60)
    add_executable(jalium.native.metal.font-feature-priority tests/metal_font_feature_priority.mm)
    target_include_directories(jalium.native.metal.font-feature-priority PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}/src")
    target_compile_options(jalium.native.metal.font-feature-priority PRIVATE -fobjc-arc)
    target_link_libraries(jalium.native.metal.font-feature-priority PRIVATE jalium.native.metal jalium.native.core "-framework AppKit" "-framework CoreText")
    add_test(NAME jalium.native.metal.font-feature-priority COMMAND jalium.native.metal.font-feature-priority
        "${CMAKE_CURRENT_SOURCE_DIR}/../../../tests/Jalium.UI.MacOS.Tests/Assets/FontFeaturePriority/selectable-variants.otf")
    set_tests_properties(jalium.native.metal.font-feature-priority PROPERTIES TIMEOUT 60)
    add_executable(jalium.native.metal.vertical-orientation tests/metal_vertical_orientation.mm)
    target_include_directories(jalium.native.metal.vertical-orientation PRIVATE
        "${CMAKE_CURRENT_SOURCE_DIR}/src" "${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.text/include")
    target_compile_options(jalium.native.metal.vertical-orientation PRIVATE -fobjc-arc)
    target_link_libraries(jalium.native.metal.vertical-orientation PRIVATE jalium.native.metal jalium.native.text jalium.native.core
        "-framework AppKit" "-framework CoreText" "-framework ImageIO" "-framework QuartzCore")
    add_test(NAME jalium.native.metal.vertical-orientation COMMAND jalium.native.metal.vertical-orientation --metal)
    add_test(NAME jalium.native.software.apple.vertical-orientation COMMAND jalium.native.metal.vertical-orientation --software)
    set_tests_properties(jalium.native.metal.vertical-orientation jalium.native.software.apple.vertical-orientation PROPERTIES TIMEOUT 60)
    add_executable(jalium.native.metal.vertical-features tests/metal_vertical_features.mm)
    target_include_directories(jalium.native.metal.vertical-features PRIVATE
        "${CMAKE_CURRENT_SOURCE_DIR}/src" "${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.text/include")
    target_compile_options(jalium.native.metal.vertical-features PRIVATE -fobjc-arc)
    target_link_libraries(jalium.native.metal.vertical-features PRIVATE jalium.native.metal jalium.native.text jalium.native.core
        "-framework AppKit" "-framework CoreText" "-framework ImageIO" "-framework QuartzCore")
    add_test(NAME jalium.native.metal.vertical-features COMMAND jalium.native.metal.vertical-features --metal
        "${CMAKE_CURRENT_SOURCE_DIR}/../../../tests/Jalium.UI.MacOS.Tests/Assets/FontVerticalForms")
    add_test(NAME jalium.native.software.apple.vertical-features COMMAND jalium.native.metal.vertical-features --software
        "${CMAKE_CURRENT_SOURCE_DIR}/../../../tests/Jalium.UI.MacOS.Tests/Assets/FontVerticalForms")
    set_tests_properties(jalium.native.metal.vertical-features jalium.native.software.apple.vertical-features PROPERTIES TIMEOUT 60)
    add_executable(jalium.native.metal.vertical-metrics tests/metal_vertical_metrics.mm)
    target_include_directories(jalium.native.metal.vertical-metrics PRIVATE
        "${CMAKE_CURRENT_SOURCE_DIR}/src" "${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.text/include")
    target_compile_options(jalium.native.metal.vertical-metrics PRIVATE -fobjc-arc)
    target_link_libraries(jalium.native.metal.vertical-metrics PRIVATE jalium.native.metal jalium.native.text jalium.native.core
        "-framework AppKit" "-framework CoreText" "-framework ImageIO" "-framework QuartzCore")
    add_test(NAME jalium.native.metal.vertical-metrics COMMAND jalium.native.metal.vertical-metrics --metal
        "${CMAKE_CURRENT_SOURCE_DIR}/../../../tests/Jalium.UI.MacOS.Tests/Assets/FontVerticalMetrics")
    add_test(NAME jalium.native.software.apple.vertical-metrics COMMAND jalium.native.metal.vertical-metrics --software
        "${CMAKE_CURRENT_SOURCE_DIR}/../../../tests/Jalium.UI.MacOS.Tests/Assets/FontVerticalMetrics")
    set_tests_properties(jalium.native.metal.vertical-metrics jalium.native.software.apple.vertical-metrics PROPERTIES TIMEOUT 60)
    add_executable(jalium.native.metal.vertical-origins tests/metal_vertical_origins.mm)
    target_include_directories(jalium.native.metal.vertical-origins PRIVATE
        "${CMAKE_CURRENT_SOURCE_DIR}/src" "${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.text/include")
    target_compile_options(jalium.native.metal.vertical-origins PRIVATE -fobjc-arc)
    target_link_libraries(jalium.native.metal.vertical-origins PRIVATE jalium.native.metal jalium.native.text jalium.native.core
        "-framework AppKit" "-framework CoreText" "-framework ImageIO" "-framework QuartzCore")
    add_test(NAME jalium.native.metal.vertical-origins COMMAND jalium.native.metal.vertical-origins --metal
        "${CMAKE_CURRENT_SOURCE_DIR}/../../../tests/Jalium.UI.MacOS.Tests/Assets/FontVerticalOrigins")
    add_test(NAME jalium.native.software.apple.vertical-origins COMMAND jalium.native.metal.vertical-origins --software
        "${CMAKE_CURRENT_SOURCE_DIR}/../../../tests/Jalium.UI.MacOS.Tests/Assets/FontVerticalOrigins")
    set_tests_properties(jalium.native.metal.vertical-origins jalium.native.software.apple.vertical-origins PROPERTIES TIMEOUT 60)
    add_executable(jalium.native.metal.text-combine tests/metal_text_combine.mm)
    target_include_directories(jalium.native.metal.text-combine PRIVATE
        "${CMAKE_CURRENT_SOURCE_DIR}/src" "${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.text/include")
    target_compile_options(jalium.native.metal.text-combine PRIVATE -fobjc-arc)
    target_link_libraries(jalium.native.metal.text-combine PRIVATE jalium.native.metal jalium.native.text jalium.native.core
        "-framework AppKit" "-framework CoreText" "-framework ImageIO" "-framework QuartzCore")
    add_test(NAME jalium.native.metal.text-combine COMMAND jalium.native.metal.text-combine --metal
        "${CMAKE_CURRENT_SOURCE_DIR}/../../../tests/Jalium.UI.MacOS.Tests/Assets/FontTextCombine")
    add_test(NAME jalium.native.software.apple.text-combine COMMAND jalium.native.metal.text-combine --software
        "${CMAKE_CURRENT_SOURCE_DIR}/../../../tests/Jalium.UI.MacOS.Tests/Assets/FontTextCombine")
    set_tests_properties(jalium.native.metal.text-combine jalium.native.software.apple.text-combine PROPERTIES TIMEOUT 60)
    add_executable(jalium.native.metal.upright-text tests/metal_upright_text.mm)
    target_include_directories(jalium.native.metal.upright-text PRIVATE
        "${CMAKE_CURRENT_SOURCE_DIR}/src" "${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.text/include")
    target_compile_options(jalium.native.metal.upright-text PRIVATE -fobjc-arc)
    target_link_libraries(jalium.native.metal.upright-text PRIVATE jalium.native.metal jalium.native.text jalium.native.core
        "-framework AppKit" "-framework CoreText" "-framework ImageIO" "-framework QuartzCore")
    add_test(NAME jalium.native.metal.upright-text COMMAND jalium.native.metal.upright-text --metal
        "${CMAKE_CURRENT_SOURCE_DIR}/../../../tests/Jalium.UI.MacOS.Tests/Assets/FontSizeAdjust")
    add_test(NAME jalium.native.software.apple.upright-text COMMAND jalium.native.metal.upright-text --software
        "${CMAKE_CURRENT_SOURCE_DIR}/../../../tests/Jalium.UI.MacOS.Tests/Assets/FontSizeAdjust")
    set_tests_properties(jalium.native.metal.upright-text jalium.native.software.apple.upright-text PROPERTIES TIMEOUT 60)
    add_executable(jalium.native.metal.font-position tests/metal_font_position.mm)
    target_include_directories(jalium.native.metal.font-position PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}/src")
    target_compile_options(jalium.native.metal.font-position PRIVATE -fobjc-arc)
    target_link_libraries(jalium.native.metal.font-position PRIVATE jalium.native.metal jalium.native.core
        "-framework AppKit" "-framework CoreText")
    add_test(NAME jalium.native.metal.font-position COMMAND jalium.native.metal.font-position)
    set_tests_properties(jalium.native.metal.font-position PROPERTIES TIMEOUT 60)
    add_executable(jalium.native.metal.tests tests/metal_smoke.mm tests/metal_word_navigation.mm)
    target_compile_options(jalium.native.metal.tests PRIVATE -fobjc-arc)
    target_link_libraries(jalium.native.metal.tests PRIVATE
        jalium.native.metal jalium.native.core jalium.native.platform
        "-framework AppKit" "-framework CoreFoundation")
    add_test(NAME jalium.native.metal.smoke COMMAND jalium.native.metal.tests)
    set_tests_properties(jalium.native.metal.smoke PROPERTIES TIMEOUT 30)
    add_executable(jalium.native.metal.regression tests/metal_regression.mm)
    target_include_directories(jalium.native.metal.regression PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.media.core/include)
    target_compile_options(jalium.native.metal.regression PRIVATE -fobjc-arc)
    target_link_libraries(jalium.native.metal.regression PRIVATE
        jalium.native.metal jalium.native.core jalium.native.platform jalium.native.media.apple
        "-framework AppKit" "-framework CoreFoundation" "-framework CoreGraphics" "-framework ImageIO")
    add_test(NAME jalium.native.metal.regression COMMAND jalium.native.metal.regression)
    set_tests_properties(jalium.native.metal.regression PROPERTIES TIMEOUT 30)
    add_test(NAME jalium.native.metal.retained-viewport COMMAND jalium.native.metal.regression --retained-viewport)
    set_tests_properties(jalium.native.metal.retained-viewport PROPERTIES TIMEOUT 60)
    add_executable(jalium.native.metal.bitmap-upload tests/metal_bitmap_upload.mm)
    target_compile_options(jalium.native.metal.bitmap-upload PRIVATE -fobjc-arc)
    target_link_libraries(jalium.native.metal.bitmap-upload PRIVATE
        jalium.native.metal jalium.native.core "-framework AppKit")
    add_test(NAME jalium.native.metal.bitmap-upload COMMAND jalium.native.metal.bitmap-upload)
    set_tests_properties(jalium.native.metal.bitmap-upload PROPERTIES TIMEOUT 60)
    add_executable(jalium.native.metal.paths tests/metal_paths.mm)
    target_compile_options(jalium.native.metal.paths PRIVATE -fobjc-arc)
    target_link_libraries(jalium.native.metal.paths PRIVATE jalium.native.metal jalium.native.core
        "-framework AppKit" "-framework CoreGraphics" "-framework ImageIO" "-framework Metal")
    add_test(NAME jalium.native.metal.paths COMMAND jalium.native.metal.paths)
    set_tests_properties(jalium.native.metal.paths PROPERTIES TIMEOUT 120)
    add_executable(jalium.native.metal.effects tests/metal_effects.mm)
    target_compile_options(jalium.native.metal.effects PRIVATE -fobjc-arc)
    target_link_libraries(jalium.native.metal.effects PRIVATE jalium.native.metal jalium.native.core
        "-framework AppKit" "-framework CoreGraphics" "-framework ImageIO" "-framework CoreText")
    if(JALIUM_DXC_INCLUDE_DIR AND JALIUM_DXC_LIBRARY AND
        JALIUM_SPIRV_CROSS_INCLUDE_DIR AND JALIUM_SPIRV_CROSS_LIBRARIES)
        add_test(NAME jalium.native.metal.effects COMMAND jalium.native.metal.effects)
        set_tests_properties(jalium.native.metal.effects PROPERTIES TIMEOUT 60)
    endif()
endif()

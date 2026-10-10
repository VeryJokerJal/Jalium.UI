project(jalium.native.metal)

set(HEADERS
    include/metal_backend.h
    src/metal_text_paragraph.h
    src/metal_word_navigation.h
)

set(SOURCES
    src/metal_backend.cpp
    src/metal_text_paragraph.cpp
    src/metal_render_target.cpp
    src/metal_shader_compiler.cpp
    src/metal_vello.cpp
    src/metal_init.cpp
)

# On Apple platforms, compile as Objective-C++
if(APPLE)
    list(APPEND SOURCES src/metal_word_navigation.mm)
    set_source_files_properties(
        src/metal_backend.cpp
        src/metal_text_paragraph.cpp
        src/metal_render_target.cpp
        src/metal_shader_compiler.cpp
        src/metal_vello.cpp
        src/metal_init.cpp
        PROPERTIES LANGUAGE OBJCXX
    )
endif()

add_library(${PROJECT_NAME} ${JALIUM_LIB_TYPE} ${HEADERS} ${SOURCES})

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
    add_executable(jalium.native.metal.image-orientation tests/metal_image_orientation.mm)
    target_include_directories(jalium.native.metal.image-orientation PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.text/include
        ${CMAKE_CURRENT_SOURCE_DIR}/../thirdparty/stb
        ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.media.core/include)
    target_compile_options(jalium.native.metal.image-orientation PRIVATE -fobjc-arc)
    target_link_libraries(jalium.native.metal.image-orientation PRIVATE
        jalium.native.metal jalium.native.core jalium.native.software jalium.native.media.apple
        "-framework AppKit" "-framework CoreGraphics" "-framework ImageIO")
    add_test(NAME jalium.native.metal.image-pixels COMMAND jalium.native.metal.image-orientation --pixels)
    add_test(NAME jalium.native.metal.image-render COMMAND jalium.native.metal.image-orientation)
    set_tests_properties(jalium.native.metal.image-pixels jalium.native.metal.image-render
        PROPERTIES TIMEOUT 60)
    add_executable(jalium.native.metal.text-metrics tests/metal_text_metrics.mm)
    target_compile_options(jalium.native.metal.text-metrics PRIVATE -fobjc-arc)
    target_link_libraries(jalium.native.metal.text-metrics PRIVATE jalium.native.metal jalium.native.core
        "-framework CoreText" "-framework CoreFoundation" "-framework Foundation")
    add_test(NAME jalium.native.metal.text-metrics COMMAND jalium.native.metal.text-metrics)
    set_tests_properties(jalium.native.metal.text-metrics PROPERTIES TIMEOUT 30)
    add_executable(jalium.native.metal.fonts tests/metal_font_resources.mm tests/metal_font_cascade.mm tests/metal_font_width.mm tests/metal_font_matching.mm)
    target_compile_options(jalium.native.metal.fonts PRIVATE -fobjc-arc)
    target_link_libraries(jalium.native.metal.fonts PRIVATE jalium.native.metal jalium.native.core
        "-framework AppKit" "-framework CoreText")
    add_test(NAME jalium.native.metal.fonts COMMAND jalium.native.metal.fonts)
    set_tests_properties(jalium.native.metal.fonts PROPERTIES TIMEOUT 30)
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

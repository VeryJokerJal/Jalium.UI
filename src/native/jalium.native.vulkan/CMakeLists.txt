project(jalium.native.vulkan)

# Keep Linux/Android builds hermetic: stb_image is vendored beside the other
# native single-header dependencies. Windows still uses the repository-local
# Vulkan SDK by default because its import library is linked explicitly below.
set(JALIUM_STB_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/../thirdparty/stb")
if(NOT EXISTS "${JALIUM_STB_ROOT}/stb_image.h")
    message(FATAL_ERROR "Vendored stb_image.h is missing: ${JALIUM_STB_ROOT}")
endif()

set(JALIUM_VULKAN_INCLUDE_DIRS "")
if(WIN32)
    if(DEFINED ENV{JALIUM_SDK_ROOT} AND NOT "$ENV{JALIUM_SDK_ROOT}" STREQUAL "")
        file(TO_CMAKE_PATH "$ENV{JALIUM_SDK_ROOT}" JALIUM_SDK_ROOT)
    else()
        get_filename_component(JALIUM_SDK_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/../../../../Jalium.SDK" ABSOLUTE)
    endif()

    if(DEFINED ENV{VULKAN_SDK} AND NOT "$ENV{VULKAN_SDK}" STREQUAL "")
        file(TO_CMAKE_PATH "$ENV{VULKAN_SDK}" _jalium_vulkan_sdk_default)
    else()
        set(_jalium_vulkan_sdk_default "${JALIUM_SDK_ROOT}/VulkanSDK/1.4.341.1")
    endif()
    set(JALIUM_VULKAN_SDK_ROOT "${_jalium_vulkan_sdk_default}" CACHE PATH
        "Vulkan SDK containing headers for the Windows target")
    list(APPEND JALIUM_VULKAN_INCLUDE_DIRS "${JALIUM_VULKAN_SDK_ROOT}/Include")
    set(JALIUM_VULKAN_LIBRARY "${JALIUM_VULKAN_SDK_ROOT}/Lib/vulkan-1.lib"
        CACHE FILEPATH "Vulkan loader import library for the target Windows architecture")
endif()

set(HEADERS
    include/vulkan_backend.h
    include/vulkan_capabilities.h
    include/vulkan_graphics_constants.h
    include/vulkan_video_format.h
    include/vulkan_video_shaders.h
    include/vulkan_glyph_atlas.h
    include/vulkan_impeller_engine.h
    include/vulkan_ink_layer.h
    include/vulkan_render_lifecycle.h
    include/vulkan_render_target.h
    include/vulkan_resources.h
    include/vulkan_runtime.h
    include/vulkan_environment.h
    include/vulkan_shader_compiler.h
    include/vulkan_vello_engine.h
    include/vulkan_vello_compute.h
)

set(SOURCES
    src/vulkan_backend.cpp
    src/vulkan_glyph_atlas.cpp
    src/vulkan_impeller_engine.cpp
    src/vulkan_init.cpp
    src/vulkan_ink_layer.cpp
    src/vulkan_render_target.cpp
    src/vulkan_resources.cpp
    src/vulkan_runtime.cpp
    src/vulkan_shader_compiler.cpp
    src/vulkan_video_surface.cpp
    src/vulkan_video_import.cpp
    src/vulkan_vello_engine.cpp
    src/vulkan_vello_compute.cpp
)

set(SHADER_SOURCES
    shaders/liquid_glass_quad.frag.hlsl
    shaders/liquid_glass_quad.vert.hlsl
    shaders/solid_rect.frag.hlsl
    shaders/solid_rect.vert.hlsl
    shaders/video_readback.vert
    shaders/video_readback.frag
    ../jalium.native.core/shaders/continuous_corner.hlsli
)
set_source_files_properties(${SHADER_SOURCES} PROPERTIES HEADER_FILE_ONLY TRUE)
source_group("Shaders" FILES ${SHADER_SOURCES})

add_library(${PROJECT_NAME} ${JALIUM_LIB_TYPE}
    ${HEADERS}
    ${SOURCES}
    ${SHADER_SOURCES}
)

target_include_directories(${PROJECT_NAME}
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.core/include
        ${JALIUM_VULKAN_INCLUDE_DIRS}
        ${JALIUM_STB_ROOT}
)

target_link_libraries(${PROJECT_NAME}
    PRIVATE
        jalium.native.core
)

# Link text engine on non-Windows platforms
if(NOT WIN32)
    target_link_libraries(${PROJECT_NAME}
        PRIVATE
            jalium.native.text
    )
    target_include_directories(${PROJECT_NAME}
        PRIVATE
            ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.text/include
    )
endif()

if(WIN32)
    if(MINGW AND CMAKE_CXX_COMPILER_ID MATCHES "Clang")
        set_source_files_properties(src/vulkan_shader_compiler.cpp PROPERTIES
            COMPILE_OPTIONS "-fms-extensions")
    endif()
    target_link_libraries(${PROJECT_NAME}
        PRIVATE
            ${JALIUM_VULKAN_LIBRARY}
            # DirectWrite: Windows-Vulkan text rasterization + layout mirrors the
            # D3D12 backend's DWrite path (IDWriteFactory5, IDWriteTextLayout,
            # IDWriteGlyphRunAnalysis). Required since the GDI text path was
            # replaced with DirectWrite for D3D12 parity.
            dwrite
            ${CMAKE_DL_LIBS}
    )
elseif(ANDROID)
    target_link_libraries(${PROJECT_NAME}
        PRIVATE
            vulkan
            android
            log
    )
elseif(UNIX AND NOT APPLE)
    find_package(X11 REQUIRED)
    target_include_directories(${PROJECT_NAME}
        PRIVATE
            ${X11_INCLUDE_DIR}
            ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.platform/include
    )
    target_link_libraries(${PROJECT_NAME}
        PRIVATE
            vulkan
            ${X11_LIBRARIES}
            jalium.native.platform
            dl
    )
endif()

set_target_properties(${PROJECT_NAME} PROPERTIES
    OUTPUT_NAME "jalium.native.vulkan"
    DEBUG_POSTFIX ""
)

if(JALIUM_VULKAN_BUILD_TESTS OR
   ((JALIUM_TEXT_BUILD_TESTS OR JALIUM_PLATFORM_BUILD_TESTS) AND
    UNIX AND NOT APPLE AND NOT ANDROID))
    add_executable(jalium.native.vulkan.lcd.tests
        tests/vulkan_lcd_staging_tests.cpp
    )
    target_include_directories(jalium.native.vulkan.lcd.tests
        PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/include)
    add_test(NAME jalium.native.vulkan.lcd
             COMMAND jalium.native.vulkan.lcd.tests)

    add_executable(jalium.native.vulkan.capabilities.tests
        tests/vulkan_capabilities_tests.cpp)
    target_include_directories(jalium.native.vulkan.capabilities.tests PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/include
        ${JALIUM_VULKAN_INCLUDE_DIRS}
        ${X11_INCLUDE_DIR})
    add_test(NAME jalium.native.vulkan.capabilities
             COMMAND jalium.native.vulkan.capabilities.tests)

    add_executable(jalium.native.vulkan.shader-constants.tests
        tests/vulkan_shader_constants_tests.cpp)
    target_include_directories(jalium.native.vulkan.shader-constants.tests PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/include)
    add_test(NAME jalium.native.vulkan.shader-constants
        COMMAND jalium.native.vulkan.shader-constants.tests)
    find_program(JALIUM_SPIRV_VAL NAMES spirv-val
        HINTS "$ENV{VULKAN_SDK}/Bin" "$ENV{VULKAN_SDK}/bin")
    if(JALIUM_SPIRV_VAL)
        add_test(NAME jalium.native.vulkan.shader-constants-validation
            COMMAND ${CMAKE_COMMAND}
                "-DSHADER_TEST=$<TARGET_FILE:jalium.native.vulkan.shader-constants.tests>"
                "-DSPIRV_VAL=${JALIUM_SPIRV_VAL}"
                "-DOUTPUT_DIR=${CMAKE_CURRENT_BINARY_DIR}/uniform-shader-validation"
                -P "${CMAKE_CURRENT_SOURCE_DIR}/tests/validate_graphics_shaders.cmake")
    endif()
endif()

if(JALIUM_VULKAN_BUILD_TESTS)
    if(WIN32 OR ANDROID)
        set(_jalium_external_video_test jalium.native.vulkan.external-video.tests)
    elseif(UNIX AND NOT APPLE)
        # Exercises the Android import dispatch on the host with an explicit
        # NDK ABI shim. Target Android compilation is validated separately.
        set(_jalium_external_video_test jalium.native.vulkan.android-import.tests)
    endif()
    if(DEFINED _jalium_external_video_test)
        add_executable(${_jalium_external_video_test}
            tests/vulkan_external_video_tests.cpp src/vulkan_video_import.cpp)
        target_include_directories(${_jalium_external_video_test} PRIVATE
            ${CMAKE_CURRENT_SOURCE_DIR}/include
            ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.core/include
            ${JALIUM_VULKAN_INCLUDE_DIRS})
        target_link_libraries(${_jalium_external_video_test} PRIVATE ${CMAKE_DL_LIBS})
        set_target_properties(${_jalium_external_video_test} PROPERTIES ENABLE_EXPORTS ON)
        if(UNIX AND NOT APPLE AND NOT ANDROID)
            target_include_directories(${_jalium_external_video_test} BEFORE PRIVATE
                ${CMAKE_CURRENT_SOURCE_DIR}/tests/android-shim)
            target_compile_definitions(${_jalium_external_video_test} PRIVATE __ANDROID__)
        endif()
        add_test(NAME ${_jalium_external_video_test} COMMAND ${_jalium_external_video_test})
    endif()
endif()

if(JALIUM_VULKAN_BUILD_TESTS AND UNIX AND NOT APPLE AND NOT ANDROID)
    add_library(jalium.native.vulkan.video-interop-shim SHARED
        tests/vulkan_video_interop_shim.cpp)
    target_include_directories(jalium.native.vulkan.video-interop-shim PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.core/include
        ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.media.core/include)
    set_target_properties(jalium.native.vulkan.video-interop-shim PROPERTIES
        LIBRARY_OUTPUT_DIRECTORY "${CMAKE_CURRENT_BINARY_DIR}")

    add_executable(jalium.native.vulkan.win32-import.tests
        tests/vulkan_external_video_tests.cpp src/vulkan_video_import.cpp)
    target_include_directories(jalium.native.vulkan.win32-import.tests PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/include ${CMAKE_CURRENT_SOURCE_DIR}/tests
        ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.core/include)
    target_include_directories(jalium.native.vulkan.win32-import.tests BEFORE PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/tests/win32-shim)
    target_compile_definitions(jalium.native.vulkan.win32-import.tests PRIVATE
        JALIUM_VULKAN_TEST_WIN32_IMPORT)
    target_link_libraries(jalium.native.vulkan.win32-import.tests PRIVATE ${X11_LIBRARIES})
    add_test(NAME jalium.native.vulkan.win32-import
        COMMAND jalium.native.vulkan.win32-import.tests)

    add_executable(jalium.native.vulkan.video-import.tests
        tests/vulkan_video_import_tests.cpp src/vulkan_video_import.cpp)
    target_include_directories(jalium.native.vulkan.video-import.tests PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/include
        ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.core/include)
    add_test(NAME jalium.native.vulkan.video-import
        COMMAND jalium.native.vulkan.video-import.tests)

    add_executable(jalium.native.vulkan.text-cache.tests tests/vulkan_text_run_cache_tests.cpp)
    target_include_directories(jalium.native.vulkan.text-cache.tests PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/src)
    add_test(NAME jalium.native.vulkan.text-cache COMMAND jalium.native.vulkan.text-cache.tests)

    add_executable(jalium.native.vulkan.render.tests tests/vulkan_render_tests.cpp)
    target_include_directories(jalium.native.vulkan.render.tests PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.core/include
        ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.platform/include)
    target_link_libraries(jalium.native.vulkan.render.tests PRIVATE
        jalium.native.core jalium.native.platform jalium.native.vulkan vulkan)

    add_library(jalium.native.vulkan.test-loader SHARED tests/vulkan_test_loader.cpp)
    target_link_libraries(jalium.native.vulkan.test-loader PRIVATE ${CMAKE_DL_LIBS})
    # Keep fault-injection helpers out of the native payload directory.
    set_target_properties(jalium.native.vulkan.test-loader PROPERTIES
        CXX_VISIBILITY_PRESET default
        LIBRARY_OUTPUT_DIRECTORY "${CMAKE_CURRENT_BINARY_DIR}"
        LIBRARY_OUTPUT_DIRECTORY_DEBUG "${CMAKE_CURRENT_BINARY_DIR}"
        LIBRARY_OUTPUT_DIRECTORY_RELEASE "${CMAKE_CURRENT_BINARY_DIR}"
        LIBRARY_OUTPUT_DIRECTORY_RELWITHDEBINFO "${CMAKE_CURRENT_BINARY_DIR}"
        LIBRARY_OUTPUT_DIRECTORY_MINSIZEREL "${CMAKE_CURRENT_BINARY_DIR}")
    add_dependencies(jalium.native.vulkan.render.tests jalium.native.vulkan.test-loader)
    add_dependencies(jalium.native.vulkan.render.tests jalium.native.vulkan.shader-constants.tests)

    add_test(NAME jalium.native.vulkan.render COMMAND jalium.native.vulkan.render.tests)
    add_test(NAME jalium.native.vulkan.render-long-text COMMAND jalium.native.vulkan.render.tests)
    add_test(NAME jalium.native.vulkan.render-source-text COMMAND jalium.native.vulkan.render.tests)
    set_tests_properties(jalium.native.vulkan.render-source-text PROPERTIES
        ENVIRONMENT "JALIUM_VK_TEST_SOURCE_TEXT=1;JALIUM_VK_EFFECT_GPU_RT=1"
        TIMEOUT 120 SKIP_RETURN_CODE 77
        FAIL_REGULAR_EXPRESSION "\\[Vulkan\\]\\[validation\\]\\[ERROR\\]")
    set_tests_properties(jalium.native.vulkan.render-long-text PROPERTIES
        ENVIRONMENT "JALIUM_VK_TEST_LONG_TEXT=1;JALIUM_VK_EFFECT_GPU_RT=1"
        TIMEOUT 120 SKIP_RETURN_CODE 77
    )
    add_test(NAME jalium.native.vulkan.render-alpha-mask COMMAND jalium.native.vulkan.render.tests)
    set_tests_properties(jalium.native.vulkan.render-alpha-mask PROPERTIES
        ENVIRONMENT "JALIUM_VK_TEST_ALPHA_MASKS=1;JALIUM_VK_EFFECT_GPU_RT=1"
        TIMEOUT 120 SKIP_RETURN_CODE 77
        FAIL_REGULAR_EXPRESSION "\\[Vulkan\\]\\[validation\\]\\[ERROR\\]")
    add_test(NAME jalium.native.vulkan.render-video-fd-fixture
        COMMAND jalium.native.vulkan.render.tests)
    set_tests_properties(jalium.native.vulkan.render-video-fd-fixture PROPERTIES
        ENVIRONMENT "JALIUM_VK_TEST_VIDEO=1;JALIUM_VK_EFFECT_GPU_RT=1"
        TIMEOUT 120 SKIP_RETURN_CODE 77
        FAIL_REGULAR_EXPRESSION "\\[Vulkan\\]\\[validation\\]\\[ERROR\\]")
    add_test(NAME jalium.native.vulkan.render-video-128
        COMMAND ${CMAKE_COMMAND} -E env
            "LD_PRELOAD=$<TARGET_FILE:jalium.native.vulkan.test-loader>"
            JALIUM_VK_TEST_CONSTANTS_128=1 JALIUM_VK_TEST_VIDEO=1
            $<TARGET_FILE:jalium.native.vulkan.render.tests>)
    set_tests_properties(jalium.native.vulkan.render-video-128 PROPERTIES
        ENVIRONMENT "JALIUM_VK_EFFECT_GPU_RT=1" TIMEOUT 120 SKIP_RETURN_CODE 77
        FAIL_REGULAR_EXPRESSION "\\[Vulkan\\]\\[validation\\]\\[ERROR\\];cannot be preloaded")
    add_test(NAME jalium.native.vulkan.render-video-cpu-effects
        COMMAND jalium.native.vulkan.render.tests)
    set_tests_properties(jalium.native.vulkan.render-video-cpu-effects PROPERTIES
        ENVIRONMENT "JALIUM_VK_TEST_VIDEO=1;JALIUM_VK_EFFECT_CPU_CAPTURE=1"
        TIMEOUT 120 SKIP_RETURN_CODE 77
        FAIL_REGULAR_EXPRESSION "\\[Vulkan\\]\\[validation\\]\\[ERROR\\]")
    add_test(NAME jalium.native.vulkan.render-incomplete
        COMMAND ${CMAKE_COMMAND} -E env
            "LD_PRELOAD=$<TARGET_FILE:jalium.native.vulkan.test-loader>"
            $<TARGET_FILE:jalium.native.vulkan.render.tests>)
    add_test(NAME jalium.native.vulkan.render-graphics-only
        COMMAND ${CMAKE_COMMAND} -E env
            "LD_PRELOAD=$<TARGET_FILE:jalium.native.vulkan.test-loader>"
            JALIUM_VK_TEST_GRAPHICS_ONLY=1
            $<TARGET_FILE:jalium.native.vulkan.render.tests>)
    add_test(NAME jalium.native.vulkan.render-split-queues
        COMMAND ${CMAKE_COMMAND} -E env
            "LD_PRELOAD=$<TARGET_FILE:jalium.native.vulkan.test-loader>"
            JALIUM_VK_TEST_SPLIT_QUEUES=1
            $<TARGET_FILE:jalium.native.vulkan.render.tests>)
    add_test(NAME jalium.native.vulkan.render-vulkan10-loader
        COMMAND ${CMAKE_COMMAND} -E env
            "LD_PRELOAD=$<TARGET_FILE:jalium.native.vulkan.test-loader>"
            JALIUM_VK_TEST_VULKAN10_LOADER=1
            $<TARGET_FILE:jalium.native.vulkan.render.tests>)
    set_tests_properties(
        jalium.native.vulkan.render
        jalium.native.vulkan.render-incomplete
        jalium.native.vulkan.render-graphics-only
        jalium.native.vulkan.render-split-queues
        jalium.native.vulkan.render-vulkan10-loader
        PROPERTIES SKIP_RETURN_CODE 77 TIMEOUT 120
        ENVIRONMENT "JALIUM_VK_EFFECT_GPU_RT=1"
        FAIL_REGULAR_EXPRESSION "\\[Vulkan\\]\\[validation\\]\\[ERROR\\];cannot be preloaded")
endif()

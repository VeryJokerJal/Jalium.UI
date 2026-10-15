cmake_minimum_required(VERSION 3.28)
project(JaliumD3D12InkTests LANGUAGES CXX)

if(NOT WIN32)
    message(FATAL_ERROR "The ink integration tests require Windows D3D12 WARP")
endif()

enable_testing()
add_executable(jalium.native.d3d12.ink.tests
    ink_batch_tests.cpp
    ../src/d3d12_ink_layer.cpp
    ../src/d3d12_brush_shader.cpp)
target_compile_features(jalium.native.d3d12.ink.tests PRIVATE cxx_std_20)
target_compile_definitions(jalium.native.d3d12.ink.tests PRIVATE
    WIN32_LEAN_AND_MEAN NOMINMAX JALIUM_STATIC)
target_include_directories(jalium.native.d3d12.ink.tests PRIVATE
    ../include ../../jalium.native.core/include)
target_link_libraries(jalium.native.d3d12.ink.tests PRIVATE d3d12 dxgi d3dcompiler dxguid)
if(MSVC)
    target_compile_options(jalium.native.d3d12.ink.tests PRIVATE /utf-8 /W4 /EHsc)
endif()

foreach(case IN ITEMS pso-before-submit pso-in-flight pso-dedupe unload-open-batch
        close-retry clear-retry allocator-retry signal-retry signal-teardown)
    add_test(NAME jalium.native.d3d12.ink.${case}
        COMMAND jalium.native.d3d12.ink.tests ${case})
    set_tests_properties(jalium.native.d3d12.ink.${case} PROPERTIES TIMEOUT 45)
endforeach()

project(jalium.native.metal)

set(HEADERS
    include/metal_backend.h
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
        target_link_libraries(${PROJECT_NAME} PRIVATE "-framework AppKit")
    else()
        target_link_libraries(${PROJECT_NAME} PRIVATE "-framework UIKit")
    endif()
    target_compile_options(${PROJECT_NAME} PRIVATE -fobjc-arc)
endif()

set(JALIUM_DXC_INCLUDE_DIR "" CACHE PATH "DXC include directory for Apple runtime HLSL")
set(JALIUM_DXC_LIBRARY "" CACHE FILEPATH "DXC static library for the current Apple slice")
set(JALIUM_SPIRV_CROSS_INCLUDE_DIR "" CACHE PATH "SPIRV-Cross C API include directory")
set(JALIUM_SPIRV_CROSS_LIBRARIES "" CACHE STRING "Semicolon-separated SPIRV-Cross static libraries")
if(JALIUM_DXC_INCLUDE_DIR AND JALIUM_DXC_LIBRARY AND
   JALIUM_SPIRV_CROSS_INCLUDE_DIR AND JALIUM_SPIRV_CROSS_LIBRARIES)
    target_include_directories(${PROJECT_NAME} PRIVATE
        "${JALIUM_DXC_INCLUDE_DIR}" "${JALIUM_SPIRV_CROSS_INCLUDE_DIR}")
    target_link_libraries(${PROJECT_NAME} PRIVATE
        "${JALIUM_DXC_LIBRARY}" ${JALIUM_SPIRV_CROSS_LIBRARIES})
    target_compile_definitions(${PROJECT_NAME} PRIVATE JALIUM_HAS_DXC_SPIRV_CROSS=1)
endif()

set_target_properties(${PROJECT_NAME} PROPERTIES
    OUTPUT_NAME "jalium.native.metal"
    DEBUG_POSTFIX ""
)

option(JALIUM_METAL_BUILD_TESTS "Build real-device macOS Metal smoke tests" OFF)
if(JALIUM_METAL_BUILD_TESTS AND CMAKE_SYSTEM_NAME STREQUAL "Darwin")
    add_executable(jalium.native.metal.smoke tests/metal_smoke.mm)
    target_include_directories(jalium.native.metal.smoke PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/include
        ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.core/include)
    target_link_libraries(jalium.native.metal.smoke PRIVATE
        jalium.native.metal jalium.native.core
        "-framework AppKit" "-framework Metal" "-framework QuartzCore")
    target_compile_options(jalium.native.metal.smoke PRIVATE -fobjc-arc)
    add_test(NAME jalium.native.metal.gpu-readback
             COMMAND jalium.native.metal.smoke)
endif()

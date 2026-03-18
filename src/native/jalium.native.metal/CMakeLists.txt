project(jalium.native.metal)

set(HEADERS
    include/metal_backend.h
)

set(SOURCES
    src/metal_backend.cpp
    src/metal_init.cpp
)

# On Apple platforms, compile as Objective-C++
if(APPLE)
    set_source_files_properties(
        src/metal_backend.cpp
        src/metal_init.cpp
        PROPERTIES LANGUAGE OBJCXX
    )
endif()

add_library(${PROJECT_NAME} SHARED ${HEADERS} ${SOURCES})

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
            "-framework AppKit"
            "-framework CoreFoundation"
            "-framework CoreGraphics"
            "-framework CoreText"
            "-framework ImageIO"
    )
    target_compile_options(${PROJECT_NAME} PRIVATE -fobjc-arc)
endif()

set_target_properties(${PROJECT_NAME} PROPERTIES
    OUTPUT_NAME "jalium.native.metal"
    DEBUG_POSTFIX ""
)

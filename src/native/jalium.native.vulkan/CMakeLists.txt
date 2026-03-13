project(jalium.native.vulkan)

if(DEFINED ENV{VULKAN_SDK} AND NOT "$ENV{VULKAN_SDK}" STREQUAL "")
    file(TO_CMAKE_PATH "$ENV{VULKAN_SDK}" JALIUM_VULKAN_SDK_ROOT)
else()
    set(JALIUM_VULKAN_SDK_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/../../../third_party/VulkanSDK/1.4.341.1")
endif()

set(HEADERS
    include/vulkan_backend.h
    include/vulkan_render_target.h
    include/vulkan_resources.h
    include/vulkan_runtime.h
)

set(SOURCES
    src/vulkan_backend.cpp
    src/vulkan_init.cpp
    src/vulkan_render_target.cpp
    src/vulkan_resources.cpp
    src/vulkan_runtime.cpp
)

add_library(${PROJECT_NAME} SHARED ${HEADERS} ${SOURCES})

target_include_directories(${PROJECT_NAME}
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.core/include
        ${JALIUM_VULKAN_SDK_ROOT}/Include
)

target_link_libraries(${PROJECT_NAME}
    PRIVATE
        jalium.native.core
)

if(WIN32)
    target_link_libraries(${PROJECT_NAME}
        PRIVATE
            ${JALIUM_VULKAN_SDK_ROOT}/Lib/vulkan-1.lib
            dwrite
            windowscodecs
            ole32
            dxgi
            d3d11
    )
elseif(UNIX AND NOT APPLE)
    find_package(X11 REQUIRED)
    target_include_directories(${PROJECT_NAME}
        PRIVATE
            ${X11_INCLUDE_DIR}
    )
    target_link_libraries(${PROJECT_NAME}
        PRIVATE
            ${X11_LIBRARIES}
            dl
    )
endif()

set_target_properties(${PROJECT_NAME} PROPERTIES
    OUTPUT_NAME "jalium.native.vulkan"
    DEBUG_POSTFIX ""
)

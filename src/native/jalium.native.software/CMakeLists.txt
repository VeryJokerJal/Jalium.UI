project(jalium.native.software)

set(HEADERS
    include/software_backend.h
)

set(SOURCES
    src/software_backend.cpp
    src/software_init.cpp
)

add_library(${PROJECT_NAME} SHARED ${HEADERS} ${SOURCES})

target_include_directories(${PROJECT_NAME}
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.core/include
)

if(DEFINED ENV{JALIUM_SDK_ROOT} AND NOT "$ENV{JALIUM_SDK_ROOT}" STREQUAL "")
    file(TO_CMAKE_PATH "$ENV{JALIUM_SDK_ROOT}" JALIUM_SDK_ROOT)
else()
    get_filename_component(JALIUM_SDK_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/../../../../Jalium.SDK" ABSOLUTE)
endif()

set(JALIUM_STB_ROOT "${JALIUM_SDK_ROOT}/stb")

target_link_libraries(${PROJECT_NAME}
    PRIVATE
        jalium.native.core
)

if(WIN32)
    target_link_libraries(${PROJECT_NAME} PRIVATE gdi32)
endif()

# Link cross-platform text engine (FreeType + HarfBuzz)
if(TARGET jalium.native.text)
    target_link_libraries(${PROJECT_NAME} PRIVATE jalium.native.text)
    target_include_directories(${PROJECT_NAME} PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.text/include)
    target_compile_definitions(${PROJECT_NAME} PRIVATE JALIUM_HAS_TEXT_ENGINE)
endif()

# stb_image for non-Windows image decoding
if(NOT WIN32)
    if(EXISTS "${JALIUM_STB_ROOT}")
        target_include_directories(${PROJECT_NAME} PRIVATE ${JALIUM_STB_ROOT})
    endif()

    # Link X11 for present on Linux
    if(UNIX AND NOT APPLE AND NOT ANDROID)
        find_package(X11 QUIET)
        if(X11_FOUND)
            target_include_directories(${PROJECT_NAME} PRIVATE ${X11_INCLUDE_DIR})
            target_link_libraries(${PROJECT_NAME} PRIVATE ${X11_LIBRARIES})
            target_compile_definitions(${PROJECT_NAME} PRIVATE JALIUM_SOFTWARE_X11_PRESENT)
        endif()
    endif()

    # Link android for ANativeWindow present on Android
    if(ANDROID)
        target_link_libraries(${PROJECT_NAME} PRIVATE android log)
    endif()
endif()

set_target_properties(${PROJECT_NAME} PROPERTIES
    OUTPUT_NAME "jalium.native.software"
    DEBUG_POSTFIX ""
)

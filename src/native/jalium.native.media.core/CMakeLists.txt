project(jalium.native.media.core)

set(HEADERS
    include/jalium_media.h
    include/jalium_media_internal.h
)

set(SOURCES
    src/jalium_media_common.cpp
)

add_library(${PROJECT_NAME} ${JALIUM_LIB_TYPE} ${HEADERS} ${SOURCES})

target_include_directories(${PROJECT_NAME}
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
)

if(NOT JALIUM_BUILD_STATIC)
    target_compile_definitions(${PROJECT_NAME}
        PRIVATE
            JALIUM_MEDIA_EXPORTS
    )
endif()

set_target_properties(${PROJECT_NAME} PROPERTIES
    OUTPUT_NAME "jalium.native.media.core"
    DEBUG_POSTFIX ""
)

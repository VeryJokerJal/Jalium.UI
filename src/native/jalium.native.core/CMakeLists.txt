project(jalium.native.core)

set(HEADERS
    include/jalium_api.h
    include/jalium_types.h
    include/jalium_backend.h
)

set(SOURCES
    src/context.cpp
    src/render_target.cpp
    src/brush.cpp
    src/bitmap.cpp
    src/backend_registry.cpp
)

add_library(${PROJECT_NAME} SHARED ${HEADERS} ${SOURCES})

target_include_directories(${PROJECT_NAME}
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
)

target_compile_definitions(${PROJECT_NAME}
    PRIVATE
        JALIUM_EXPORTS
)

# Link Windows libraries
if(WIN32)
    target_link_libraries(${PROJECT_NAME}
        PRIVATE
            kernel32
            user32
    )
elseif(ANDROID)
    target_link_libraries(${PROJECT_NAME}
        PRIVATE
            log
    )
endif()

# Set output name
set_target_properties(${PROJECT_NAME} PROPERTIES
    OUTPUT_NAME "jalium.native.core"
    DEBUG_POSTFIX ""
)

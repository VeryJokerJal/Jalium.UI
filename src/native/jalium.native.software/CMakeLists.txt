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

target_link_libraries(${PROJECT_NAME}
    PRIVATE
        jalium.native.core
)

if(WIN32)
    target_link_libraries(${PROJECT_NAME} PRIVATE gdi32)
endif()

set_target_properties(${PROJECT_NAME} PROPERTIES
    OUTPUT_NAME "jalium.native.software"
    DEBUG_POSTFIX ""
)

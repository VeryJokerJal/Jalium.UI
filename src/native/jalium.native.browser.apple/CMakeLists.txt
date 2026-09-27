project(jalium.native.browser.apple)

add_library(${PROJECT_NAME} ${JALIUM_LIB_TYPE} src/apple_browser.mm)
target_include_directories(${PROJECT_NAME} PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.browser/include)
target_link_libraries(${PROJECT_NAME} PRIVATE
    "-framework Foundation"
    "-framework QuartzCore")
if(NOT CMAKE_SYSTEM_NAME STREQUAL "tvOS")
    target_link_libraries(${PROJECT_NAME} PRIVATE "-framework WebKit")
endif()
if(CMAKE_SYSTEM_NAME STREQUAL "Darwin")
    target_link_libraries(${PROJECT_NAME} PRIVATE "-framework AppKit")
else()
    target_link_libraries(${PROJECT_NAME} PRIVATE "-framework UIKit")
endif()
target_compile_options(${PROJECT_NAME} PRIVATE -fobjc-arc)
set_target_properties(${PROJECT_NAME} PROPERTIES
    OUTPUT_NAME "jalium.native.browser"
    DEBUG_POSTFIX "")

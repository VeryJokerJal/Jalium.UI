project(jalium.native.text)

# Jalium SDK root for bundled dependencies
if(DEFINED ENV{JALIUM_SDK_ROOT} AND NOT "$ENV{JALIUM_SDK_ROOT}" STREQUAL "")
    file(TO_CMAKE_PATH "$ENV{JALIUM_SDK_ROOT}" JALIUM_SDK_ROOT)
else()
    get_filename_component(JALIUM_SDK_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/../../../../Jalium.SDK" ABSOLUTE)
endif()

set(HEADERS
    include/text_engine.h
    include/text_shaper.h
    include/text_layout.h
    include/font_provider.h
    include/glyph_rasterizer.h
    include/glyph_atlas.h
)

set(SOURCES
    src/text_engine.cpp
    src/text_shaper.cpp
    src/text_layout.cpp
    src/font_provider.cpp
    src/glyph_rasterizer.cpp
    src/glyph_atlas.cpp
)

# Platform-specific font provider
if(ANDROID)
    list(APPEND SOURCES src/font_provider_android.cpp)
elseif(UNIX AND NOT APPLE)
    list(APPEND SOURCES src/font_provider_fontconfig.cpp)
endif()

add_library(${PROJECT_NAME} SHARED ${HEADERS} ${SOURCES})

target_compile_definitions(${PROJECT_NAME} PRIVATE JALIUM_TEXT_EXPORTS)

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

# Find FreeType
find_package(Freetype QUIET)
if(NOT Freetype_FOUND)
    # Try pkg-config fallback
    find_package(PkgConfig)
    if(PkgConfig_FOUND)
        pkg_check_modules(FREETYPE2 freetype2)
        if(FREETYPE2_FOUND)
            target_include_directories(${PROJECT_NAME} PRIVATE ${FREETYPE2_INCLUDE_DIRS})
            target_link_libraries(${PROJECT_NAME} PRIVATE ${FREETYPE2_LIBRARIES})
            set(Freetype_FOUND TRUE)
        endif()
    endif()
endif()

if(Freetype_FOUND AND NOT FREETYPE2_FOUND)
    target_link_libraries(${PROJECT_NAME} PRIVATE Freetype::Freetype)
endif()

# Find HarfBuzz
find_package(PkgConfig)
if(PkgConfig_FOUND)
    pkg_check_modules(HARFBUZZ harfbuzz)
    if(HARFBUZZ_FOUND)
        target_include_directories(${PROJECT_NAME} PRIVATE ${HARFBUZZ_INCLUDE_DIRS})
        target_link_libraries(${PROJECT_NAME} PRIVATE ${HARFBUZZ_LIBRARIES})
    endif()
endif()

# Find Fontconfig (Linux only)
if(UNIX AND NOT APPLE AND NOT ANDROID)
    if(PkgConfig_FOUND)
        pkg_check_modules(FONTCONFIG fontconfig)
        if(FONTCONFIG_FOUND)
            target_include_directories(${PROJECT_NAME} PRIVATE ${FONTCONFIG_INCLUDE_DIRS})
            target_link_libraries(${PROJECT_NAME} PRIVATE ${FONTCONFIG_LIBRARIES})
        endif()
    endif()
endif()

# Map Android ABI to SDK directory name
if(ANDROID)
    if(ANDROID_ABI STREQUAL "arm64-v8a")
        set(JALIUM_SDK_ANDROID_DIR "android-arm64")
    elseif(ANDROID_ABI STREQUAL "x86_64")
        set(JALIUM_SDK_ANDROID_DIR "android-x86_64")
    elseif(ANDROID_ABI STREQUAL "armeabi-v7a")
        set(JALIUM_SDK_ANDROID_DIR "android-arm32")
    elseif(ANDROID_ABI STREQUAL "x86")
        set(JALIUM_SDK_ANDROID_DIR "android-x86")
    else()
        set(JALIUM_SDK_ANDROID_DIR "android-${ANDROID_ABI}")
    endif()
endif()

# Bundled SDK fallback paths (if system packages not found)
if(NOT Freetype_FOUND AND NOT FREETYPE2_FOUND)
    if(EXISTS "${JALIUM_SDK_ROOT}/freetype")
        target_include_directories(${PROJECT_NAME} PRIVATE "${JALIUM_SDK_ROOT}/freetype/include")
        if(ANDROID)
            target_link_libraries(${PROJECT_NAME} PRIVATE "${JALIUM_SDK_ROOT}/freetype/lib/${JALIUM_SDK_ANDROID_DIR}/libfreetype.a")
        else()
            target_link_libraries(${PROJECT_NAME} PRIVATE "${JALIUM_SDK_ROOT}/freetype/lib/linux-x64/libfreetype.a")
        endif()
    else()
        message(FATAL_ERROR "FreeType not found. Install libfreetype-dev or set JALIUM_SDK_ROOT.")
    endif()
endif()

if(NOT HARFBUZZ_FOUND)
    if(EXISTS "${JALIUM_SDK_ROOT}/harfbuzz")
        target_include_directories(${PROJECT_NAME} PRIVATE "${JALIUM_SDK_ROOT}/harfbuzz/include/harfbuzz")
        if(ANDROID)
            target_link_libraries(${PROJECT_NAME} PRIVATE "${JALIUM_SDK_ROOT}/harfbuzz/lib/${JALIUM_SDK_ANDROID_DIR}/libharfbuzz.a")
        else()
            target_link_libraries(${PROJECT_NAME} PRIVATE "${JALIUM_SDK_ROOT}/harfbuzz/lib/linux-x64/libharfbuzz.a")
        endif()
    else()
        message(FATAL_ERROR "HarfBuzz not found. Install libharfbuzz-dev or set JALIUM_SDK_ROOT.")
    endif()
endif()

set_target_properties(${PROJECT_NAME} PROPERTIES
    OUTPUT_NAME "jalium.native.text"
    DEBUG_POSTFIX ""
)

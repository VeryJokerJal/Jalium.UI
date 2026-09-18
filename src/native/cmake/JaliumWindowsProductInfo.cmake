# Windows VERSIONINFO resources for Jalium native DLLs.
# Keep native metadata in sync with the root MSBuild product/version settings.

if(NOT WIN32)
    return()
endif()

set(_jalium_product_props "${CMAKE_CURRENT_LIST_DIR}/../../../Directory.Build.props")
set_property(DIRECTORY APPEND PROPERTY CMAKE_CONFIGURE_DEPENDS "${_jalium_product_props}")
file(READ "${_jalium_product_props}" _jalium_product_props_text)

function(_jalium_read_msbuild_property property_name output_var)
    string(REGEX MATCH "<${property_name}>[ \t\r\n]*([^<]+)[ \t\r\n]*</${property_name}>"
           _jalium_property_match "${_jalium_product_props_text}")
    if(NOT CMAKE_MATCH_1)
        message(FATAL_ERROR "Directory.Build.props is missing <${property_name}> required for Windows VERSIONINFO")
    endif()
    string(STRIP "${CMAKE_MATCH_1}" _jalium_property_value)
    set(${output_var} "${_jalium_property_value}" PARENT_SCOPE)
endfunction()

_jalium_read_msbuild_property(Version JALIUM_PRODUCT_VERSION_STRING)
_jalium_read_msbuild_property(FileVersion JALIUM_FILE_VERSION_STRING)
_jalium_read_msbuild_property(Product JALIUM_PRODUCT_NAME)
_jalium_read_msbuild_property(Company JALIUM_COMPANY_NAME)
_jalium_read_msbuild_property(Copyright JALIUM_COPYRIGHT)
string(REPLACE "." "," JALIUM_FILE_VERSION_COMMA "${JALIUM_FILE_VERSION_STRING}")

function(jalium_add_windows_product_info target_name)
    if(NOT TARGET ${target_name})
        message(FATAL_ERROR "Cannot attach Windows product info to missing target '${target_name}'")
    endif()

    get_target_property(_jalium_target_type ${target_name} TYPE)
    if(NOT _jalium_target_type STREQUAL "SHARED_LIBRARY" AND
       NOT _jalium_target_type STREQUAL "MODULE_LIBRARY")
        return()
    endif()

    get_target_property(_jalium_output_name ${target_name} OUTPUT_NAME)
    if(NOT _jalium_output_name OR _jalium_output_name MATCHES "-NOTFOUND$")
        set(_jalium_output_name "${target_name}")
    endif()

    set(JALIUM_FILE_DESCRIPTION "${_jalium_output_name}")
    set(JALIUM_INTERNAL_NAME "${_jalium_output_name}")
    set(JALIUM_ORIGINAL_FILENAME "${_jalium_output_name}.dll")

    set(_jalium_version_dir "${CMAKE_CURRENT_BINARY_DIR}/jalium-version/${target_name}")
    file(MAKE_DIRECTORY "${_jalium_version_dir}")
    configure_file(
        "${CMAKE_CURRENT_FUNCTION_LIST_DIR}/JaliumNativeVersion.generated.h.in"
        "${_jalium_version_dir}/jalium.native.version.generated.h"
        @ONLY)
    configure_file(
        "${CMAKE_CURRENT_FUNCTION_LIST_DIR}/../../../eng/native/jalium.native.version.rc"
        "${_jalium_version_dir}/${target_name}.version.rc"
        COPYONLY)

    target_sources(${target_name} PRIVATE "${_jalium_version_dir}/${target_name}.version.rc")

    # CMake's Visual Studio projects also import the repository-wide
    # Directory.Build.targets. Mark these targets so that MSBuild does not add
    # a second VERSIONINFO resource on top of the CMake-generated one.
    set_property(TARGET ${target_name} PROPERTY VS_GLOBAL_JaliumProductInfoProvided true)
endfunction()

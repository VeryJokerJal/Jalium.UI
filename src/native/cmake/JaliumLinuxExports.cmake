# Linux shared objects are consumed directly from the NuGet RID directory.
# Keep their dynamic symbol tables deliberately small: an API declaration must
# opt in with a visibility attribute and its owning library's version script
# must also allow it.

function(jalium_apply_linux_export_policy target export_map)
    if(NOT CMAKE_SYSTEM_NAME STREQUAL "Linux" OR JALIUM_BUILD_STATIC)
        return()
    endif()
    if(NOT TARGET "${target}")
        message(FATAL_ERROR "Cannot apply Linux export policy to missing target '${target}'")
    endif()

    set(_map "${CMAKE_CURRENT_FUNCTION_LIST_DIR}/exports/${export_map}")
    if(NOT EXISTS "${_map}")
        message(FATAL_ERROR "Linux export map does not exist: ${_map}")
    endif()

    set_target_properties("${target}" PROPERTIES
        C_VISIBILITY_PRESET hidden
        CXX_VISIBILITY_PRESET hidden
        VISIBILITY_INLINES_HIDDEN YES
        LINK_DEPENDS "${_map}")
    target_link_options("${target}" PRIVATE
        "LINKER:--version-script=${_map}"
        "LINKER:--no-undefined")
endfunction()

if(NOT DEFINED READELF OR NOT DEFINED NM OR NOT DEFINED LIBRARY)
    message(FATAL_ERROR "READELF, NM, and LIBRARY are required")
endif()

execute_process(
    COMMAND "${READELF}" -d "${LIBRARY}"
    RESULT_VARIABLE readelf_result
    OUTPUT_VARIABLE dynamic_section
    ERROR_VARIABLE readelf_error)
if(NOT readelf_result EQUAL 0)
    message(FATAL_ERROR "readelf failed: ${readelf_error}")
endif()

if(dynamic_section MATCHES
   [=[Shared library: \[lib(gst|glib|gobject|gio)[^]]*\.so]=])
    message(FATAL_ERROR
        "Linux media has a direct optional-runtime dependency:\n${dynamic_section}")
endif()

execute_process(
    COMMAND "${NM}" -D --undefined-only "${LIBRARY}"
    RESULT_VARIABLE nm_result
    OUTPUT_VARIABLE undefined_symbols
    ERROR_VARIABLE nm_error)
if(NOT nm_result EQUAL 0)
    message(FATAL_ERROR "nm failed: ${nm_error}")
endif()

if(undefined_symbols MATCHES "[ \t]U[ \t]+(gst_|g_(error|free|list|object|signal|str))")
    message(FATAL_ERROR
        "Linux media has an unresolved optional-runtime symbol:\n${undefined_symbols}")
endif()

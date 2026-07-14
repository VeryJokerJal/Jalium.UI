if(NOT DEFINED JALIUM_CROSS_EXPECTED_RID OR
   NOT DEFINED JALIUM_CROSS_EXPECTED_LIBC OR
   NOT DEFINED JALIUM_CROSS_EXPECTED_ARCH OR
   NOT DEFINED JALIUM_CROSS_TARGET_TRIPLE)
    message(FATAL_ERROR "Jalium Linux cross toolchain variant is incomplete")
endif()

set(CMAKE_SYSTEM_NAME Linux)
set(CMAKE_SYSTEM_PROCESSOR "${JALIUM_CROSS_EXPECTED_ARCH}")
set(CMAKE_TRY_COMPILE_TARGET_TYPE STATIC_LIBRARY)

set(JALIUM_NATIVE_RID "${JALIUM_CROSS_EXPECTED_RID}" CACHE STRING
    "Portable .NET RID for these native artifacts")
set(JALIUM_LINUX_LIBC "${JALIUM_CROSS_EXPECTED_LIBC}" CACHE STRING
    "Linux C library used by the target toolchain")
if(NOT JALIUM_NATIVE_RID STREQUAL JALIUM_CROSS_EXPECTED_RID)
    message(FATAL_ERROR
        "Toolchain ${JALIUM_CROSS_EXPECTED_RID} cannot produce JALIUM_NATIVE_RID='${JALIUM_NATIVE_RID}'")
endif()
if(NOT JALIUM_LINUX_LIBC STREQUAL JALIUM_CROSS_EXPECTED_LIBC)
    message(FATAL_ERROR
        "Toolchain ${JALIUM_CROSS_EXPECTED_RID} requires ${JALIUM_CROSS_EXPECTED_LIBC}, "
        "not JALIUM_LINUX_LIBC='${JALIUM_LINUX_LIBC}'")
endif()

set(JALIUM_CROSS_C_COMPILER "$ENV{JALIUM_CROSS_C_COMPILER}" CACHE FILEPATH
    "Target C compiler (absolute path or executable name)")
set(JALIUM_CROSS_CXX_COMPILER "$ENV{JALIUM_CROSS_CXX_COMPILER}" CACHE FILEPATH
    "Target C++ compiler (absolute path or executable name)")
set(JALIUM_CROSS_SYSROOT "$ENV{JALIUM_CROSS_SYSROOT}" CACHE PATH
    "Target sysroot containing usr/include and target libraries")
set(JALIUM_CROSS_PKG_CONFIG_EXECUTABLE "$ENV{JALIUM_CROSS_PKG_CONFIG_EXECUTABLE}" CACHE FILEPATH
    "Target-aware pkg-config executable")
set(JALIUM_CROSS_PKG_CONFIG_LIBDIR "$ENV{JALIUM_CROSS_PKG_CONFIG_LIBDIR}" CACHE STRING
    "Colon-separated target pkg-config directories")

if(JALIUM_CROSS_C_COMPILER STREQUAL "")
    unset(JALIUM_CROSS_C_COMPILER CACHE)
    find_program(JALIUM_CROSS_C_COMPILER
        NAMES ${JALIUM_CROSS_C_COMPILER_CANDIDATES}
        NO_CMAKE_FIND_ROOT_PATH)
endif()
if(JALIUM_CROSS_CXX_COMPILER STREQUAL "")
    unset(JALIUM_CROSS_CXX_COMPILER CACHE)
    find_program(JALIUM_CROSS_CXX_COMPILER
        NAMES ${JALIUM_CROSS_CXX_COMPILER_CANDIDATES}
        NO_CMAKE_FIND_ROOT_PATH)
endif()
if(NOT JALIUM_CROSS_C_COMPILER OR NOT JALIUM_CROSS_CXX_COMPILER)
    message(FATAL_ERROR
        "No ${JALIUM_CROSS_TARGET_TRIPLE} compiler pair was found. Set "
        "JALIUM_CROSS_C_COMPILER and JALIUM_CROSS_CXX_COMPILER explicitly.")
endif()

find_program(_jalium_real_c_compiler NAMES "${JALIUM_CROSS_C_COMPILER}"
    NO_CMAKE_FIND_ROOT_PATH)
find_program(_jalium_real_cxx_compiler NAMES "${JALIUM_CROSS_CXX_COMPILER}"
    NO_CMAKE_FIND_ROOT_PATH)
if(NOT _jalium_real_c_compiler)
    set(_jalium_real_c_compiler "${JALIUM_CROSS_C_COMPILER}")
endif()
if(NOT _jalium_real_cxx_compiler)
    set(_jalium_real_cxx_compiler "${JALIUM_CROSS_CXX_COMPILER}")
endif()
set(CMAKE_C_COMPILER "${_jalium_real_c_compiler}" CACHE FILEPATH "" FORCE)
set(CMAKE_CXX_COMPILER "${_jalium_real_cxx_compiler}" CACHE FILEPATH "" FORCE)

execute_process(
    COMMAND "${CMAKE_CXX_COMPILER}" -dumpmachine
    RESULT_VARIABLE _jalium_dumpmachine_result
    OUTPUT_VARIABLE _jalium_compiler_target
    OUTPUT_STRIP_TRAILING_WHITESPACE
    ERROR_QUIET)
string(TOLOWER "${_jalium_compiler_target}" _jalium_compiler_target)
if(NOT _jalium_dumpmachine_result EQUAL 0 OR _jalium_compiler_target STREQUAL "")
    message(FATAL_ERROR "${CMAKE_CXX_COMPILER} did not report a target with -dumpmachine")
endif()
if(JALIUM_CROSS_EXPECTED_ARCH STREQUAL "x86_64")
    if(NOT _jalium_compiler_target MATCHES "^(x86_64|amd64)")
        message(FATAL_ERROR
            "x64 toolchain selected compiler target '${_jalium_compiler_target}'")
    endif()
elseif(JALIUM_CROSS_EXPECTED_ARCH STREQUAL "aarch64")
    if(NOT _jalium_compiler_target MATCHES "^(aarch64|arm64)")
        message(FATAL_ERROR
            "arm64 toolchain selected compiler target '${_jalium_compiler_target}'")
    endif()
endif()
if(NOT _jalium_compiler_target MATCHES "(^|-)linux($|-)")
    message(FATAL_ERROR
        "Linux cross toolchain selected non-Linux compiler target '${_jalium_compiler_target}'")
endif()
if(JALIUM_CROSS_EXPECTED_LIBC STREQUAL "musl")
    if(NOT _jalium_compiler_target MATCHES "(^|-)musl($|-)")
        message(FATAL_ERROR
            "musl toolchain selected non-musl compiler target '${_jalium_compiler_target}'")
    endif()
elseif(NOT _jalium_compiler_target MATCHES "(^|-)gnu($|-)|linux-gnu" OR
       _jalium_compiler_target MATCHES "android|bionic|musl|uclibc")
    message(FATAL_ERROR
        "glibc toolchain selected non-glibc compiler target '${_jalium_compiler_target}'")
endif()

if(NOT JALIUM_CROSS_SYSROOT STREQUAL "")
    if(NOT IS_DIRECTORY "${JALIUM_CROSS_SYSROOT}")
        message(FATAL_ERROR "JALIUM_CROSS_SYSROOT does not exist: ${JALIUM_CROSS_SYSROOT}")
    endif()
    set(CMAKE_SYSROOT "${JALIUM_CROSS_SYSROOT}" CACHE PATH "" FORCE)
    set(CMAKE_FIND_ROOT_PATH "${JALIUM_CROSS_SYSROOT}" CACHE STRING "" FORCE)
    set(CMAKE_FIND_ROOT_PATH_MODE_PROGRAM NEVER)
    set(CMAKE_FIND_ROOT_PATH_MODE_LIBRARY ONLY)
    set(CMAKE_FIND_ROOT_PATH_MODE_INCLUDE ONLY)
    set(CMAKE_FIND_ROOT_PATH_MODE_PACKAGE ONLY)
else()
    # A same-architecture cross driver (for example x86_64-linux-gnu-g++)
    # does not make CMake infer Debian's multiarch library directory while
    # try-compiles are static. Name that target directory explicitly; this is
    # still target-selected and never guesses another architecture's host libs.
    set(CMAKE_LIBRARY_ARCHITECTURE "${JALIUM_CROSS_TARGET_TRIPLE}")
    set(_jalium_target_library_paths
        "/usr/lib/${JALIUM_CROSS_TARGET_TRIPLE}"
        "/lib/${JALIUM_CROSS_TARGET_TRIPLE}")
    string(TOLOWER "${CMAKE_HOST_SYSTEM_PROCESSOR}" _jalium_host_processor)
    if((JALIUM_CROSS_EXPECTED_ARCH STREQUAL "x86_64" AND
        _jalium_host_processor MATCHES "^(x86_64|amd64)") OR
       (JALIUM_CROSS_EXPECTED_ARCH STREQUAL "aarch64" AND
        _jalium_host_processor MATCHES "^(aarch64|arm64)"))
        # Alpine and some non-multiarch distributions keep target libraries in
        # /usr/lib. It is safe only when target and host architecture agree.
        list(APPEND _jalium_target_library_paths /usr/lib /lib)
    endif()
    set(CMAKE_LIBRARY_PATH "${_jalium_target_library_paths}"
        CACHE STRING "Target library search path")
endif()

if(JALIUM_CROSS_PKG_CONFIG_EXECUTABLE STREQUAL "")
    unset(JALIUM_CROSS_PKG_CONFIG_EXECUTABLE CACHE)
    find_program(JALIUM_CROSS_PKG_CONFIG_EXECUTABLE NAMES pkg-config pkgconf
        NO_CMAKE_FIND_ROOT_PATH)
endif()
if(NOT JALIUM_CROSS_PKG_CONFIG_EXECUTABLE)
    message(FATAL_ERROR
        "pkg-config was not found; set JALIUM_CROSS_PKG_CONFIG_EXECUTABLE")
endif()
set(PKG_CONFIG_EXECUTABLE "${JALIUM_CROSS_PKG_CONFIG_EXECUTABLE}" CACHE FILEPATH "" FORCE)

if(NOT JALIUM_CROSS_SYSROOT STREQUAL "")
    set(ENV{PKG_CONFIG_SYSROOT_DIR} "${JALIUM_CROSS_SYSROOT}")
endif()
if(NOT JALIUM_CROSS_PKG_CONFIG_LIBDIR STREQUAL "")
    set(ENV{PKG_CONFIG_LIBDIR} "${JALIUM_CROSS_PKG_CONFIG_LIBDIR}")
elseif(NOT JALIUM_CROSS_SYSROOT STREQUAL "")
    set(ENV{PKG_CONFIG_LIBDIR}
        "${JALIUM_CROSS_SYSROOT}/usr/lib/${JALIUM_CROSS_TARGET_TRIPLE}/pkgconfig:${JALIUM_CROSS_SYSROOT}/usr/lib/pkgconfig:${JALIUM_CROSS_SYSROOT}/usr/share/pkgconfig")
endif()

message(STATUS
    "Jalium cross target: ${JALIUM_CROSS_EXPECTED_RID}; compiler=${_jalium_compiler_target}; "
    "sysroot=${JALIUM_CROSS_SYSROOT}; pkg-config=${PKG_CONFIG_EXECUTABLE}")

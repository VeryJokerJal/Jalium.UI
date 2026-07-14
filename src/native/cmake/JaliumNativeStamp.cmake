# Writes the .jalium-native-complete payload stamp with build provenance:
#
#   head=<commit sha | unknown>
#   dirty=<0 | 1 | unknown>     (uncommitted changes under src/native)
#   rid=<JALIUM_NATIVE_RID>     (checked against the payload directory)
#   configuration=<config>      (checked against the payload directory)
#   source=cmake                (informational)
#
# The NuGet pack guard (eng/msbuild/JaliumStaleNativeGuard.targets) refuses
# to pack a payload whose stamp does not match the commit being packed, so
# this must stay a single file(WRITE) with no temp-file side effects: CI
# counts the payload directory entries exactly (7 .so + 1 stamp).
#
# Usage:
#   cmake -DJALIUM_STAMP_FILE=<file> -DJALIUM_REPO_ROOT=<repo>
#         [-DJALIUM_STAMP_RID=<rid>] [-DJALIUM_STAMP_CONFIG=<config>]
#         -P cmake/JaliumNativeStamp.cmake
#
# The JALIUM_GIT_HEAD / JALIUM_GIT_DIRTY environment variables override git
# discovery independently, for environments where git cannot read the
# checkout (for example a container that mounts a git worktree, whose .git
# file points at an unreachable host path).

if(NOT JALIUM_STAMP_FILE OR NOT JALIUM_REPO_ROOT)
    message(FATAL_ERROR "JaliumNativeStamp.cmake requires JALIUM_STAMP_FILE and JALIUM_REPO_ROOT")
endif()

set(_jalium_head "$ENV{JALIUM_GIT_HEAD}")
set(_jalium_dirty "$ENV{JALIUM_GIT_DIRTY}")

find_program(_jalium_git git)

if(_jalium_head STREQUAL "" AND _jalium_git)
    execute_process(
        COMMAND "${_jalium_git}" -C "${JALIUM_REPO_ROOT}" rev-parse HEAD
        OUTPUT_VARIABLE _jalium_head_out
        OUTPUT_STRIP_TRAILING_WHITESPACE
        ERROR_QUIET
        RESULT_VARIABLE _jalium_head_rc)
    if(NOT _jalium_head_rc EQUAL 0)
        # Root-in-container over a host-owned mount trips git's
        # dubious-ownership check. git >= 2.38 honors -c safe.directory;
        # older gits fail again and we fall back to head=unknown (the Linux
        # build scripts additionally bootstrap a global safe.directory entry
        # when running as root in a container).
        execute_process(
            COMMAND "${_jalium_git}" -c "safe.directory=${JALIUM_REPO_ROOT}" -C "${JALIUM_REPO_ROOT}" rev-parse HEAD
            OUTPUT_VARIABLE _jalium_head_out
            OUTPUT_STRIP_TRAILING_WHITESPACE
            ERROR_QUIET
            RESULT_VARIABLE _jalium_head_rc)
    endif()
    if(_jalium_head_rc EQUAL 0 AND _jalium_head_out MATCHES "^[0-9a-fA-F]+$")
        set(_jalium_head "${_jalium_head_out}")
    endif()
endif()

# Dirty detection is independent of how head was obtained, so an env-provided
# JALIUM_GIT_HEAD does not silently degrade dirty to unknown. The untracked
# mode is forced so a user-level status.showUntrackedFiles=no cannot hide
# new not-yet-added sources.
if(_jalium_dirty STREQUAL "" AND _jalium_git)
    execute_process(
        COMMAND "${_jalium_git}" -C "${JALIUM_REPO_ROOT}" status --porcelain --untracked-files=normal -- src/native
        OUTPUT_VARIABLE _jalium_status_out
        ERROR_QUIET
        RESULT_VARIABLE _jalium_status_rc)
    if(NOT _jalium_status_rc EQUAL 0)
        execute_process(
            COMMAND "${_jalium_git}" -c "safe.directory=${JALIUM_REPO_ROOT}" -C "${JALIUM_REPO_ROOT}" status --porcelain --untracked-files=normal -- src/native
            OUTPUT_VARIABLE _jalium_status_out
            ERROR_QUIET
            RESULT_VARIABLE _jalium_status_rc)
    endif()
    if(_jalium_status_rc EQUAL 0)
        if(_jalium_status_out STREQUAL "")
            set(_jalium_dirty "0")
        else()
            set(_jalium_dirty "1")
        endif()
    endif()
endif()

if(_jalium_head STREQUAL "")
    set(_jalium_head "unknown")
endif()
if(_jalium_dirty STREQUAL "")
    set(_jalium_dirty "unknown")
endif()

file(WRITE "${JALIUM_STAMP_FILE}" "head=${_jalium_head}\ndirty=${_jalium_dirty}\nrid=${JALIUM_STAMP_RID}\nconfiguration=${JALIUM_STAMP_CONFIG}\nsource=cmake\n")

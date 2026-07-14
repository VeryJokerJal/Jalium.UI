#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "Usage: bash eng/linux/test-print-portal.sh <portal-smoke.dll>" >&2
  exit 2
fi

smoke_dll="$(realpath "$1")"
if [[ ! -f "$smoke_dll" ]]; then
  echo "Portal smoke assembly not found: $smoke_dll" >&2
  exit 2
fi

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/../.." && pwd)"
source_file="$repo_root/tests/Jalium.UI.LinuxPortalSmoke/fake_print_portal.c"
work_dir="$(mktemp -d -t jalium-print-portal.XXXXXX)"
portal_pid=""

cleanup() {
  if [[ -n "$portal_pid" ]]; then
    kill "$portal_pid" 2>/dev/null || true
  fi
  case "$work_dir" in
    /tmp/jalium-print-portal.*|"${RUNNER_TEMP:-/nonexistent}"/jalium-print-portal.*)
      rm -rf -- "$work_dir"
      ;;
  esac
}
trap cleanup EXIT

# pkg-config emits one compiler/linker argument per whitespace-delimited token.
# shellcheck disable=SC2046
cc -Wall -Wextra -Werror "$source_file" \
  -o "$work_dir/fake-print-portal" \
  $(pkg-config --cflags --libs gio-unix-2.0)

export JALIUM_PRINT_PORTAL_BINARY="$work_dir/fake-print-portal"
export JALIUM_PRINT_PORTAL_SMOKE_DLL="$smoke_dll"
export JALIUM_PRINT_PORTAL_LOG="$work_dir/portal.log"

# The quoted body is intentionally evaluated by the nested session shell.
# shellcheck disable=SC2016
dbus-run-session -- bash -euo pipefail -c '
  "$JALIUM_PRINT_PORTAL_BINARY" >"$JALIUM_PRINT_PORTAL_LOG" 2>&1 &
  portal_pid=$!
  trap '\''kill "$portal_pid" 2>/dev/null || true'\'' EXIT

  for _ in $(seq 1 100); do
    grep -q FAKE_PORTAL_READY "$JALIUM_PRINT_PORTAL_LOG" 2>/dev/null && break
    sleep 0.02
  done
  grep -q FAKE_PORTAL_READY "$JALIUM_PRINT_PORTAL_LOG"

  timeout 15s dotnet "$JALIUM_PRINT_PORTAL_SMOKE_DLL" --print-fd-smoke
  grep -q FAKE_PORTAL_PDF_FD_OK "$JALIUM_PRINT_PORTAL_LOG"
'

echo "Print portal PreparePrint/Print + Unix FD transfer smoke passed."

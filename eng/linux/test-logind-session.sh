#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "Usage: bash eng/linux/test-logind-session.sh <portal-smoke.dll>" >&2
  exit 2
fi

smoke_dll="$(realpath "$1")"
if [[ ! -f "$smoke_dll" ]]; then
  echo "Portal smoke assembly not found: $smoke_dll" >&2
  exit 2
fi

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/../.." && pwd)"
source_file="$repo_root/tests/Jalium.UI.LinuxPortalSmoke/fake_logind.c"
work_dir="$(mktemp -d -t jalium-logind.XXXXXX)"

cleanup() {
  case "$work_dir" in
    /tmp/jalium-logind.*|"${RUNNER_TEMP:-/nonexistent}"/jalium-logind.*)
      rm -rf -- "$work_dir"
      ;;
  esac
}
trap cleanup EXIT

# pkg-config emits one compiler/linker argument per whitespace-delimited token.
# shellcheck disable=SC2046
cc -Wall -Wextra -Werror "$source_file" \
  -o "$work_dir/fake-logind" \
  $(pkg-config --cflags --libs gio-unix-2.0)

export JALIUM_LOGIND_BINARY="$work_dir/fake-logind"
export JALIUM_LOGIND_SMOKE_DLL="$smoke_dll"
export JALIUM_LOGIND_LOG="$work_dir/logind.log"

# The quoted body is intentionally evaluated by the nested session shell.
# shellcheck disable=SC2016
dbus-run-session -- bash -euo pipefail -c '
  export DBUS_SYSTEM_BUS_ADDRESS="$DBUS_SESSION_BUS_ADDRESS"
  "$JALIUM_LOGIND_BINARY" >"$JALIUM_LOGIND_LOG" 2>&1 &
  logind_pid=$!
  trap '\''kill "$logind_pid" 2>/dev/null || true'\'' EXIT

  for _ in $(seq 1 100); do
    grep -q FAKE_LOGIND_READY "$JALIUM_LOGIND_LOG" 2>/dev/null && break
    sleep 0.02
  done
  grep -q FAKE_LOGIND_READY "$JALIUM_LOGIND_LOG"

  timeout 15s dotnet "$JALIUM_LOGIND_SMOKE_DLL" --logind-smoke
  for _ in $(seq 1 100); do
    grep -q FAKE_LOGIND_INHIBITOR_RELEASED "$JALIUM_LOGIND_LOG" 2>/dev/null && break
    sleep 0.02
  done
  grep -q FAKE_LOGIND_INHIBITOR_OK "$JALIUM_LOGIND_LOG"
  grep -q FAKE_LOGIND_PREPARE_EMITTED "$JALIUM_LOGIND_LOG"
  grep -q FAKE_LOGIND_INHIBITOR_RELEASED "$JALIUM_LOGIND_LOG"
'

echo "logind delay inhibitor + PrepareForShutdown smoke passed."

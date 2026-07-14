#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "Usage: bash eng/linux/test-xsmp-session.sh <portal-smoke.dll>" >&2
  exit 2
fi

smoke_dll="$1"
if [[ ! -f "$smoke_dll" ]]; then
  echo "Portal smoke assembly does not exist: $smoke_dll" >&2
  exit 1
fi
if ! command -v cc >/dev/null 2>&1; then
  echo "A C compiler is required for the fake XSMP manager." >&2
  exit 1
fi

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/../.." && pwd)"
manager_source="$repo_root/tests/Jalium.UI.LinuxPortalSmoke/fake_xsmp_manager.c"
runtime_dir="$(mktemp -d)"
cleanup() {
  rm -rf -- "$runtime_dir"
}
trap cleanup EXIT

# Xtrans intentionally refuses to create the global ICE socket directory as a
# non-root process. Desktop machines already have it; minimal CI images may not.
if [[ ! -d /tmp/.ICE-unix ]]; then
  if [[ "$(id -u)" == "0" ]]; then
    install -d -m 1777 -o root -g root /tmp/.ICE-unix
  elif command -v sudo >/dev/null 2>&1 && sudo -n true 2>/dev/null; then
    sudo install -d -m 1777 -o root -g root /tmp/.ICE-unix
  else
    echo "/tmp/.ICE-unix is missing and cannot be created with root ownership." >&2
    exit 1
  fi
fi

find_library() {
  local soname="$1"
  local path
  path="$(ldconfig -p 2>/dev/null | awk -v name="$soname" '$1 == name { print $NF; exit }')"
  if [[ -z "$path" || ! -f "$path" ]]; then
    echo "Required runtime library was not found: $soname" >&2
    exit 1
  fi
  printf '%s\n' "$path"
}

libsm="$(find_library libSM.so.6)"
libice="$(find_library libICE.so.6)"
cc -std=c11 -Wall -Wextra -Werror -O2 \
  "$manager_source" "$libsm" "$libice" \
  -o "$runtime_dir/fake-xsmp-manager"

JALIUM_XSMP_DEBUG=1 timeout 20s "$runtime_dir/fake-xsmp-manager" \
  dotnet "$smoke_dll" --xsmp-smoke

echo "XSMP logout + cancellation protocol smoke passed."

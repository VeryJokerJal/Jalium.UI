#!/usr/bin/env bash
set -euo pipefail

usage() {
  echo "Usage: bash eng/linux/test-native-aot-static.sh <rid> [Debug|Release]" >&2
  echo "Supported RIDs: linux-x64, linux-arm64, linux-musl-x64, linux-musl-arm64" >&2
}

if [[ $# -lt 1 || $# -gt 2 ]]; then
  usage
  exit 2
fi

rid="$1"
configuration="${2:-Release}"
case "$rid" in
  linux-x64|linux-arm64|linux-musl-x64|linux-musl-arm64) ;;
  *) usage; exit 2 ;;
esac
case "$configuration" in
  Debug|Release) ;;
  *) echo "Configuration must be Debug or Release (got '$configuration')." >&2; exit 2 ;;
esac

case "$rid" in
  linux-x64|linux-musl-x64) expected_machine=x86_64 ;;
  linux-arm64|linux-musl-arm64) expected_machine=aarch64 ;;
esac
machine="$(uname -m)"
if [[ "$machine" != "$expected_machine" ]]; then
  echo "$rid static AOT validation requires $expected_machine (current host: $machine)." >&2
  exit 1
fi

if [[ "$rid" == linux-musl-* ]]; then
  ldd_version="$(ldd --version 2>&1 || true)"
  if ! grep -qi musl <<<"$ldd_version"; then
    echo "$rid static AOT validation must run in a musl environment." >&2
    exit 1
  fi
else
  libc_version="$(getconf GNU_LIBC_VERSION 2>/dev/null || true)"
  if [[ "$libc_version" != glibc\ * ]]; then
    echo "$rid static AOT validation must run in a glibc environment." >&2
    exit 1
  fi
fi

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/../.." && pwd)"
native_root="$repo_root/src/native"
build_dir="${JALIUM_NATIVE_STATIC_BUILD_DIR:-$native_root/out/build-static/$rid}"

cmake \
  --preset "$rid" \
  -S "$native_root" \
  -B "$build_dir" \
  -D "CMAKE_BUILD_TYPE=$configuration" \
  -D JALIUM_BUILD_STATIC=ON \
  -D JALIUM_PLATFORM_BUILD_TESTS=OFF \
  -D JALIUM_MEDIA_LINUX_BUILD_TESTS=ON \
  -D JALIUM_SOFTWARE_BUILD_TESTS=OFF \
  -D JALIUM_TEXT_BUILD_TESTS=OFF

cmake --build "$build_dir" \
  --target jalium.native.aot.linux.media-link.tests \
  --parallel

ctest \
  --test-dir "$build_dir" \
  --output-on-failure \
  --tests-regex '^jalium[.]native[.]aot[.]linux[.]media-link$'

aot_archive="$build_dir/lib-static/libjalium.native.aot.a"
link_smoke="$build_dir/bin-static/jalium.native.aot.linux.media-link.tests"
if [[ ! -s "$aot_archive" || ! -x "$link_smoke" ]]; then
  echo "Static NativeAOT outputs are incomplete under $build_dir." >&2
  exit 1
fi

if command -v nm >/dev/null 2>&1; then
  nm -g --defined-only "$aot_archive" | grep -Eq '[[:space:]]jalium_aot_register_all_backends$'
fi

echo "Static NativeAOT Linux media/audio aggregate linked and ran for $rid."

#!/usr/bin/env bash
set -euo pipefail

usage() {
  echo "Usage: bash eng/linux/build-native-cross.sh <rid> [Debug|Release]" >&2
  echo "Set JALIUM_CROSS_{C_COMPILER,CXX_COMPILER,SYSROOT,PKG_CONFIG_LIBDIR} as needed." >&2
}

if [[ $# -lt 1 || $# -gt 2 ]]; then
  usage
  exit 2
fi

rid="$1"
configuration="${2:-Release}"
case "$rid" in
  linux-x64) preset=linux-cross-x64 ;;
  linux-arm64) preset=linux-cross-arm64 ;;
  linux-musl-x64) preset=linux-cross-musl-x64 ;;
  linux-musl-arm64) preset=linux-cross-musl-arm64 ;;
  *) usage; exit 2 ;;
esac
case "$configuration" in
  Debug|Release) ;;
  *) usage; exit 2 ;;
esac

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/../.." && pwd)"
native_root="$repo_root/src/native"
build_dir="${JALIUM_NATIVE_BUILD_DIR:-$native_root/out/build/$preset}"

# See build-native.sh: keep git usable for the provenance stamp when running
# as root in a container over a host-owned mount.
if ! git -C "$repo_root" rev-parse HEAD >/dev/null 2>&1 && [[ "$(id -u)" == "0" ]]; then
  git config --global --add safe.directory "$repo_root" 2>/dev/null || true
fi
output_root="${JALIUM_NATIVE_OUTPUT_ROOT:-$native_root/bin/native}"
output_dir="$output_root/$rid/$configuration"

cmake_args=(
  --preset "$preset"
  -S "$native_root"
  -B "$build_dir"
  -D "CMAKE_BUILD_TYPE=$configuration"
  -D "JALIUM_NATIVE_OUTPUT_ROOT=$output_root"
)
for variable in \
    JALIUM_CROSS_C_COMPILER \
    JALIUM_CROSS_CXX_COMPILER \
    JALIUM_CROSS_SYSROOT \
    JALIUM_CROSS_PKG_CONFIG_EXECUTABLE \
    JALIUM_CROSS_PKG_CONFIG_LIBDIR; do
  if [[ -n "${!variable:-}" ]]; then
    cmake_args+=("-D" "$variable=${!variable}")
  fi
done

cmake "${cmake_args[@]}"
cmake --build "$build_dir" --target jalium.native.package.complete --parallel

test -f "$output_dir/.jalium-native-complete"
bash "$script_dir/check-native-exports.sh" "$output_dir" "$repo_root"
echo "Cross-built and validated $rid in $output_dir"

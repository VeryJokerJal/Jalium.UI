#!/usr/bin/env bash
set -euo pipefail

usage() {
  echo "Usage: bash eng/linux/build-native.sh <rid> [Debug|Release]" >&2
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
  *)
    usage
    exit 2
    ;;
esac

case "$configuration" in
  Debug|Release) ;;
  *)
    echo "Configuration must be Debug or Release (got '$configuration')." >&2
    exit 2
    ;;
esac

machine="$(uname -m)"
case "$rid" in
  linux-x64|linux-musl-x64) expected_machine=x86_64 ;;
  linux-arm64|linux-musl-arm64) expected_machine=aarch64 ;;
esac
if [[ "$machine" != "$expected_machine" ]]; then
  echo "$rid must be built on $expected_machine (current host: $machine)." >&2
  echo "Use the matching native runner/container; the canonical script does not cross-compile." >&2
  exit 1
fi

if [[ "$rid" == linux-musl-* ]]; then
  ldd_version="$(ldd --version 2>&1 || true)"
  if ! grep -qi musl <<<"$ldd_version"; then
    echo "$rid must be built in a musl environment (for example Alpine)." >&2
    exit 1
  fi
else
  libc_version="$(getconf GNU_LIBC_VERSION 2>/dev/null || true)"
  if [[ "$libc_version" != glibc\ * ]]; then
    echo "$rid must be built in a glibc environment (detected: ${libc_version:-unknown})." >&2
    exit 1
  fi
fi

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/../.." && pwd)"
native_root="$repo_root/src/native"
build_dir="${JALIUM_NATIVE_BUILD_DIR:-$native_root/out/build/$rid}"

# Container builds run as root over a host-owned mount; git then refuses the
# repository (dubious ownership) and the provenance stamp written by
# cmake/JaliumNativeStamp.cmake would degrade to head=unknown, which the NuGet
# pack guard rejects. Only the container's own global config is touched, never
# a host user's (the bootstrap is gated on uid 0 + a failing rev-parse).
if ! git -C "$repo_root" rev-parse HEAD >/dev/null 2>&1 && [[ "$(id -u)" == "0" ]]; then
  git config --global --add safe.directory "$repo_root" 2>/dev/null || true
fi
output_root="${JALIUM_NATIVE_OUTPUT_ROOT:-$native_root/bin/native}"
output_dir="$output_root/$rid/$configuration"

cmake_args=(
  --preset "$rid"
  -S "$native_root"
  -B "$build_dir"
  -D "CMAKE_BUILD_TYPE=$configuration"
  -D "JALIUM_NATIVE_OUTPUT_ROOT=$output_root"
)

# CI and release validation opt in to every Linux-native smoke target while
# ordinary developer builds stay fast.  Keeping this switch in the canonical
# build script prevents the four RID jobs from drifting into subtly different
# CMake invocations.
if [[ "${JALIUM_NATIVE_BUILD_TESTS:-0}" == "1" ]]; then
  cmake_args+=(
    -D JALIUM_PLATFORM_BUILD_TESTS=ON
    -D JALIUM_MEDIA_LINUX_BUILD_TESTS=ON
    -D JALIUM_SOFTWARE_BUILD_TESTS=ON
    -D JALIUM_TEXT_BUILD_TESTS=ON
  )
fi

cmake "${cmake_args[@]}"

# A release-validation build must not silently compile out the XI2 touch/pen/
# smooth-scroll path or Xcursor drag images just because a runner omitted two
# development packages. Ordinary developer builds keep both dependencies
# optional, while CI/full-smoke builds fail before producing a misleading
# completion stamp.
if [[ "${JALIUM_NATIVE_BUILD_TESTS:-0}" == "1" ]]; then
  for capability in XINPUT2 XCURSOR; do
    if ! grep -qx "${capability}_FOUND:INTERNAL=1" "$build_dir/CMakeCache.txt"; then
      echo "Full Linux validation requires ${capability}; install its development package." >&2
      exit 1
    fi
  done
fi

cmake --build "$build_dir" --parallel

if [[ ! -f "$output_dir/.jalium-native-complete" ]]; then
  echo "Native build did not produce its completion stamp: $output_dir/.jalium-native-complete" >&2
  exit 1
fi

required_libraries=(
  libjalium.native.core.so
  libjalium.native.media.core.so
  libjalium.native.media.so
  libjalium.native.platform.so
  libjalium.native.software.so
  libjalium.native.text.so
  libjalium.native.vulkan.so
)
for library in "${required_libraries[@]}"; do
  if [[ ! -f "$output_dir/$library" ]]; then
    echo "Missing required native output: $output_dir/$library" >&2
    exit 1
  fi
done

mapfile -d '' native_libraries < <(find "$output_dir" -maxdepth 1 -type f -name '*.so' -print0)
if [[ ${#native_libraries[@]} -eq 0 ]]; then
  echo "No shared libraries were produced in $output_dir" >&2
  exit 1
fi

if command -v readelf >/dev/null 2>&1; then
  for library in "${native_libraries[@]}"; do
    if readelf -d "$library" | grep -Eq 'NEEDED.*\[(libandroid|liblog|libc\+\+_shared)\.so\]'; then
      echo "Android dependency detected in Linux payload: $library" >&2
      exit 1
    fi
  done
fi

echo "Built and verified ${#native_libraries[@]} native libraries for $rid in $output_dir"

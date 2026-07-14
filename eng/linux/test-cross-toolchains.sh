#!/usr/bin/env bash
set -euo pipefail

mode="${1:-all}"
if [[ "$mode" != all && "$mode" != --negative-only ]]; then
  echo "Usage: bash eng/linux/test-cross-toolchains.sh [--negative-only]" >&2
  exit 2
fi

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/../.." && pwd)"
native_root="$repo_root/src/native"
work="$(mktemp -d "${TMPDIR:-/tmp}/jalium-cross-contract.XXXXXX")"
trap 'rm -rf -- "$work"' EXIT

expect_configure_failure() {
  local name="$1"
  local expected="$2"
  shift 2
  if cmake -S "$native_root" -B "$work/$name" -G Ninja "$@" >"$work/$name.log" 2>&1; then
    echo "Expected $name configure to fail" >&2
    exit 1
  fi
  if ! grep -F "$expected" "$work/$name.log"; then
    cat "$work/$name.log" >&2
    echo "Expected $name failure to contain: $expected" >&2
    exit 1
  fi
}

glibc_x64="$native_root/cmake/toolchains/linux-glibc-x64.cmake"
musl_x64="$native_root/cmake/toolchains/linux-musl-x64.cmake"
expect_configure_failure wrong-rid "cannot produce JALIUM_NATIVE_RID='linux-arm64'" \
  -D "CMAKE_TOOLCHAIN_FILE=$glibc_x64" \
  -D JALIUM_NATIVE_RID=linux-arm64
expect_configure_failure wrong-libc "requires glibc, not JALIUM_LINUX_LIBC='musl'" \
  -D "CMAKE_TOOLCHAIN_FILE=$glibc_x64" \
  -D JALIUM_LINUX_LIBC=musl

make_fake_compiler() {
  local path="$1"
  local triple="$2"
  # ${1:-} belongs to the generated script, not this test harness.
  # shellcheck disable=SC2016
  printf '#!/usr/bin/env bash\nif [[ "${1:-}" == -dumpmachine ]]; then echo "%s"; exit 0; fi\nexit 1\n' \
    "$triple" >"$path"
  chmod +x "$path"
}

android_compiler="$work/x86_64-linux-android-g++"
freebsd_compiler="$work/x86_64-unknown-freebsd-g++"
gnu_compiler="$work/x86_64-linux-gnu-g++"
make_fake_compiler "$android_compiler" x86_64-linux-android
make_fake_compiler "$freebsd_compiler" x86_64-unknown-freebsd
make_fake_compiler "$gnu_compiler" x86_64-linux-gnu

expect_configure_failure android-triple \
  "glibc toolchain selected non-glibc compiler target 'x86_64-linux-android'" \
  -D "CMAKE_TOOLCHAIN_FILE=$glibc_x64" \
  -D "JALIUM_CROSS_C_COMPILER=$android_compiler" \
  -D "JALIUM_CROSS_CXX_COMPILER=$android_compiler"
expect_configure_failure freebsd-triple \
  "Linux cross toolchain selected non-Linux compiler target" \
  -D "CMAKE_TOOLCHAIN_FILE=$glibc_x64" \
  -D "JALIUM_CROSS_C_COMPILER=$freebsd_compiler" \
  -D "JALIUM_CROSS_CXX_COMPILER=$freebsd_compiler"
expect_configure_failure musl-with-gnu-triple \
  "musl toolchain selected non-musl compiler target 'x86_64-linux-gnu'" \
  -D "CMAKE_TOOLCHAIN_FILE=$musl_x64" \
  -D "JALIUM_CROSS_C_COMPILER=$gnu_compiler" \
  -D "JALIUM_CROSS_CXX_COMPILER=$gnu_compiler"

echo "Wrong RID, libc and compiler-triple toolchain configurations were rejected."

if [[ "$mode" == all ]]; then
  cmake --preset linux-cross-x64 \
    -S "$native_root" \
    -B "$work/positive" \
    -D "JALIUM_NATIVE_OUTPUT_ROOT=$work/output"
  cmake --build "$work/positive" --target jalium.native.media.core --parallel
  test -f "$work/output/linux-x64/Release/libjalium.native.media.core.so"
  echo "linux-cross-x64 configured and built jalium.native.media.core."
fi

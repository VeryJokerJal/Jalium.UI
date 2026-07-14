#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "Usage: bash eng/linux/test-cross-preset.sh <rid>" >&2
  exit 2
fi

rid="$1"
case "$rid" in
  linux-x64) preset=linux-cross-x64; expected_machine='Advanced Micro Devices X86-64' ;;
  linux-arm64) preset=linux-cross-arm64; expected_machine='AArch64' ;;
  linux-musl-x64) preset=linux-cross-musl-x64; expected_machine='Advanced Micro Devices X86-64' ;;
  linux-musl-arm64) preset=linux-cross-musl-arm64; expected_machine='AArch64' ;;
  *) echo "Unsupported cross preset RID: $rid" >&2; exit 2 ;;
esac

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/../.." && pwd)"
native_root="$repo_root/src/native"
build_dir="${JALIUM_CROSS_TEST_BUILD_DIR:-$(mktemp -d "${TMPDIR:-/tmp}/jalium-$preset.XXXXXX")/build}"
output_root="${JALIUM_CROSS_TEST_OUTPUT_ROOT:-$(dirname "$build_dir")/output}"

cmake --preset "$preset" \
  -S "$native_root" \
  -B "$build_dir" \
  -D "JALIUM_NATIVE_OUTPUT_ROOT=$output_root"
cmake --build "$build_dir" --target jalium.native.media.core --parallel

library="$output_root/$rid/Release/libjalium.native.media.core.so"
test -f "$library"
actual_machine="$(readelf -h "$library" | sed -n 's/^[[:space:]]*Machine:[[:space:]]*//p')"
[[ "$actual_machine" == "$expected_machine" ]] || {
  echo "$preset produced '$actual_machine', expected '$expected_machine'." >&2
  exit 1
}
echo "$preset configured and built $library"

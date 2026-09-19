#!/usr/bin/env bash
set -euo pipefail

if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "Apple native builds require macOS with Xcode." >&2
  exit 2
fi

target="${1:?usage: build-native.sh <macos|ios|iossimulator|tvos|tvossimulator|visionos|visionossimulator> [Debug|Release]}"
configuration="${2:-Release}"
case "$configuration" in Debug|Release) ;; *) echo "configuration must be Debug or Release" >&2; exit 2;; esac

case "$target" in
  macos) preset=apple-macos-arm64; rid=osx-arm64 ;;
  ios) preset=apple-ios-arm64; rid=ios-arm64 ;;
  iossimulator) preset=apple-iossimulator-arm64; rid=iossimulator-arm64 ;;
  tvos) preset=apple-tvos-arm64; rid=tvos-arm64 ;;
  tvossimulator) preset=apple-tvossimulator-arm64; rid=tvossimulator-arm64 ;;
  visionos) preset=apple-visionos-arm64; rid=visionos-arm64 ;;
  visionsimulator) preset=apple-visionossimulator-arm64; rid=visionossimulator-arm64 ;;
  *) echo "unsupported Apple target: $target" >&2; exit 2 ;;
esac

repo_root="$(cd "$(dirname "$0")/../.." && pwd)"
native_root="$repo_root/src/native"
build_dir="$native_root/out/build/$preset"
slice_dir="$native_root/artifacts/apple/slices/$rid/$configuration"
deps_root="${JALIUM_APPLE_DEPS_ROOT:-$native_root/out/apple-deps}"
compiler_env="$deps_root/slices/$rid/$configuration/jalium-shader-toolchain.env"

"$repo_root/eng/apple/prepare-shader-toolchain.sh" "$target" "$configuration"
# The file is generated exclusively by prepare-shader-toolchain.sh and contains
# shell-escaped absolute paths, not user input.
# shellcheck disable=SC1090
source "$compiler_env"

cmake_compiler_args=(
  -D "JALIUM_DXC_INCLUDE_DIR=$JALIUM_DXC_INCLUDE_DIR"
  -D "JALIUM_DXC_LIBRARY=$JALIUM_DXC_LIBRARY"
  -D "JALIUM_SPIRV_CROSS_INCLUDE_DIR=$JALIUM_SPIRV_CROSS_INCLUDE_DIR"
  -D "JALIUM_SPIRV_CROSS_LIBRARIES=$JALIUM_SPIRV_CROSS_LIBRARIES"
)

cmake --preset "$preset" -S "$native_root" "${cmake_compiler_args[@]}"
"$repo_root/eng/apple/generate-metal-shaders.sh" "$target" "$configuration"
if [[ "$target" == macos ]]; then
  cmake --build "$build_dir" --config "$configuration" --target jalium.native.package.complete
  JALIUM_METALLIB_DIR="$native_root/artifacts/apple/slices/$rid/$configuration" \
    ctest --test-dir "$build_dir" -C "$configuration" --output-on-failure
else
  cmake --build "$build_dir" --config "$configuration" --target jalium.native.aot
fi

if [[ "$target" == macos ]]; then
  echo "Apple macOS dynamic payload ready: $native_root/bin/native/$rid/$configuration"
  exit 0
fi

mkdir -p "$slice_dir"
find "$build_dir" -type f -name '*.a' -path "*/$configuration/*" -exec cp -f {} "$slice_dir/" \;
if ! find "$slice_dir" -maxdepth 1 -name '*.a' -print -quit | grep -q .; then
  find "$build_dir" -type f -name '*.a' -exec cp -f {} "$slice_dir/" \;
fi
# Static libraries do not recursively contain their private link dependencies.
# Include the compiler archive in the slice so package-xcframework.sh folds it
# into libJaliumNative.a and SourceHlsl remains available after ForceLoad/AOT.
cp -f "$JALIUM_DXC_LIBRARY" "$slice_dir/"
IFS=';' read -r -a spvc_archives <<< "$JALIUM_SPIRV_CROSS_LIBRARIES"
for archive in "${spvc_archives[@]}"; do cp -f "$archive" "$slice_dir/"; done
if ! find "$slice_dir" -maxdepth 1 -name '*.a' -print -quit | grep -q .; then
  echo "no static archives were produced for $rid" >&2
  exit 3
fi

head="$(git -C "$repo_root" rev-parse HEAD)"
dirty=0
git -C "$repo_root" diff --quiet || dirty=1
{
  echo "head=$head"
  echo "dirty=$dirty"
  echo "rid=$rid"
  echo "configuration=$configuration"
  echo "xcode=$(xcodebuild -version | tr '\n' ' ')"
} > "$slice_dir/.jalium-native-complete"

echo "Apple native slice ready: $slice_dir"

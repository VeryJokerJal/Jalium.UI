#!/usr/bin/env bash
set -euo pipefail

if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "Metal shader generation requires macOS/Xcode." >&2
  exit 2
fi

target="${1:?usage: generate-metal-shaders.sh <macos|ios|iossimulator|tvos|tvossimulator|visionos|visionossimulator> [Debug|Release]}"
configuration="${2:-Release}"
case "$target" in
  macos) sdk=macosx; rid=osx-arm64 ;;
  ios) sdk=iphoneos; rid=ios-arm64 ;;
  iossimulator) sdk=iphonesimulator; rid=iossimulator-arm64 ;;
  tvos) sdk=appletvos; rid=tvos-arm64 ;;
  tvossimulator) sdk=appletvsimulator; rid=tvossimulator-arm64 ;;
  visionos) sdk=xros; rid=visionos-arm64 ;;
  visionsimulator) sdk=xrsimulator; rid=visionossimulator-arm64 ;;
  *) echo "unsupported target: $target" >&2; exit 2 ;;
esac

repo_root="$(cd "$(dirname "$0")/../.." && pwd)"
deps_root="${JALIUM_APPLE_DEPS_ROOT:-$repo_root/src/native/out/apple-deps}"
work="$repo_root/src/native/out/metal-shaders/$rid"
out="$repo_root/src/native/artifacts/apple/slices/$rid/$configuration"
spirv_cross_rev=cd3fcb2603ede297edb90ab5a679e4ac814055e2
spirv_cross_src="$deps_root/SPIRV-Cross"
spirv_cross_build="$deps_root/SPIRV-Cross-build"

if [[ ! -d "$spirv_cross_src/.git" ]]; then
  git clone --filter=blob:none https://github.com/KhronosGroup/SPIRV-Cross.git "$spirv_cross_src"
fi
git -C "$spirv_cross_src" fetch --depth 1 origin "$spirv_cross_rev"
git -C "$spirv_cross_src" checkout --detach "$spirv_cross_rev"
cmake -S "$spirv_cross_src" -B "$spirv_cross_build" -G Ninja \
  -DCMAKE_BUILD_TYPE=Release -DSPIRV_CROSS_CLI=ON -DSPIRV_CROSS_ENABLE_TESTS=OFF
cmake --build "$spirv_cross_build" --target spirv-cross
spirv_cross="$spirv_cross_build/spirv-cross"

rm -rf "$work"
mkdir -p "$work" "$out"
spv_root="$repo_root/src/native/jalium.native.vulkan/shaders/vello_spv"
stages=(pathtag_reduce pathtag_reduce2 pathtag_scan1 pathtag_scan bbox_clear \
  flatten draw_reduce draw_leaf clip_reduce clip_leaf binning tile_alloc \
  path_count_setup path_count backdrop coarse path_tiling_setup path_tiling fine)
air_files=()
for stage in "${stages[@]}"; do
  input="$spv_root/vello_${stage}.spv"
  metal="$work/vello_${stage}.metal"
  air="$work/vello_${stage}.air"
  [[ -f "$input" ]] || { echo "missing $input" >&2; exit 3; }
  "$spirv_cross" "$input" --msl --msl-version 30000 --msl-argument-buffers \
    --msl-force-active-argument-buffer-resources \
    --rename-entry-point main "vello_${stage}" comp --output "$metal"
  xcrun -sdk "$sdk" metal -std=metal3.0 -c "$metal" -o "$air"
  air_files+=("$air")
done
xcrun -sdk "$sdk" metallib "${air_files[@]}" -o "$out/jalium_vello.metallib"
echo "Metal Vello library ready: $out/jalium_vello.metallib"

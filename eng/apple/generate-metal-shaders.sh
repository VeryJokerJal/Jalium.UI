#!/usr/bin/env bash
set -euo pipefail

if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "Metal shader generation requires macOS/Xcode." >&2
  exit 2
fi

target="${1:?usage: generate-metal-shaders.sh <macos|ios|iossimulator|tvos|tvossimulator|visionos|visionsimulator> [Debug|Release]}"
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
spirv_cross_rev="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["dependencies"]["SPIRV-Cross"]["revision"])' "$repo_root/eng/apple/dependencies.lock.json")"
spirv_cross_src="$deps_root/src/SPIRV-Cross"
spirv_cross_build="$deps_root/build/host/spirv-cross-cli"

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
reflection_root="$work/reflection"
mkdir -p "$reflection_root"
stages=(pathtag_reduce pathtag_reduce2 pathtag_scan1 pathtag_scan bbox_clear \
  flatten draw_reduce draw_leaf clip_reduce clip_leaf binning tile_alloc \
  path_count_setup path_count backdrop coarse path_tiling_setup path_tiling fine)
air_files=()
for stage in "${stages[@]}"; do
  input="$spv_root/vello_${stage}.spv"
  metal="$work/vello_${stage}.metal"
  air="$work/vello_${stage}.air"
  [[ -f "$input" ]] || { echo "missing $input" >&2; exit 3; }
  "$spirv_cross" "$input" --reflect --output "$reflection_root/vello_${stage}.json"
  "$spirv_cross" "$input" --msl --msl-version 30000 --msl-argument-buffers \
    --msl-force-active-argument-buffer-resources \
    --rename-entry-point main "vello_${stage}" comp --output "$metal"
  xcrun -sdk "$sdk" metal -std=metal3.0 -c "$metal" -o "$air"
  air_files+=("$air")
done
python3 "$repo_root/tools/validate_vello_reflection.py" "$reflection_root"
xcrun -sdk "$sdk" metallib "${air_files[@]}" -o "$out/jalium_vello.metallib"

# The core SDF/bitmap/effect/blur library is embedded once in C++ for source
# checkouts and native unit tests. Production packages compile that exact byte
# sequence offline, avoiding a first-frame newLibraryWithSource compilation and
# preventing the embedded fallback from drifting from the shipped metallib.
core_metal="$work/jalium_core.metal"
core_air="$work/jalium_core.air"
python3 "$repo_root/tools/extract_embedded_metal.py" \
  "$repo_root/src/native/jalium.native.metal/src/metal_shaders.h" "$core_metal"
xcrun -sdk "$sdk" metal -std=metal3.0 -c "$core_metal" -o "$core_air"
xcrun -sdk "$sdk" metallib "$core_air" -o "$out/jalium_core.metallib"

python3 - "$out" "$rid" <<'PY'
import hashlib
import json
import pathlib
import sys

root = pathlib.Path(sys.argv[1])
manifest = {
    "formatVersion": 1,
    "rid": sys.argv[2],
    "libraries": {},
    "velloStages": [
        "pathtag_reduce", "pathtag_reduce2", "pathtag_scan1", "pathtag_scan",
        "bbox_clear", "flatten", "draw_reduce", "draw_leaf", "clip_reduce",
        "clip_leaf", "binning", "tile_alloc", "path_count_setup", "path_count",
        "backdrop", "coarse", "path_tiling_setup", "path_tiling", "fine",
    ],
}
for name in ("jalium_core.metallib", "jalium_vello.metallib"):
    payload = (root / name).read_bytes()
    manifest["libraries"][name] = {
        "sha256": hashlib.sha256(payload).hexdigest(),
        "size": len(payload),
    }
(root / "jalium_metal_manifest.json").write_text(
    json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
PY

echo "Metal libraries ready: $out/jalium_core.metallib and $out/jalium_vello.metallib"

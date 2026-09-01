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

cmake --preset "$preset" -S "$native_root"
if [[ "$target" == macos ]]; then
  cmake --build "$build_dir" --config "$configuration" --target jalium.native.package.complete
  ctest --test-dir "$build_dir" -C "$configuration" --output-on-failure
else
  cmake --build "$build_dir" --config "$configuration" --target jalium.native.aot
fi
"$repo_root/eng/apple/generate-metal-shaders.sh" "$target" "$configuration"

if [[ "$target" == macos ]]; then
  echo "Apple macOS dynamic payload ready: $native_root/bin/native/$rid/$configuration"
  exit 0
fi

mkdir -p "$slice_dir"
find "$build_dir" -type f -name '*.a' -path "*/$configuration/*" -exec cp -f {} "$slice_dir/" \;
if ! find "$slice_dir" -maxdepth 1 -name '*.a' -print -quit | grep -q .; then
  find "$build_dir" -type f -name '*.a' -exec cp -f {} "$slice_dir/" \;
fi
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

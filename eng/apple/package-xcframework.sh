#!/usr/bin/env bash
set -euo pipefail

if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "XCFramework assembly requires macOS/Xcode." >&2
  exit 2
fi

configuration="${1:-Release}"
repo_root="$(cd "$(dirname "$0")/../.." && pwd)"
native_root="$repo_root/src/native"
apple_root="$native_root/artifacts/apple"
headers="$apple_root/headers"
output="$apple_root/JaliumNative.xcframework"

required=(ios-arm64 iossimulator-arm64 tvos-arm64 tvossimulator-arm64)
optional=(visionos-arm64 visionossimulator-arm64)
rm -rf "$headers" "$output"
mkdir -p "$headers"
cp "$native_root/jalium.native.core/include/"*.h "$headers/"
cp "$native_root/jalium.native.platform/include/"*.h "$headers/"
cp "$native_root/jalium.native.media.core/include/"*.h "$headers/"

args=()
merge_slice() {
  local rid="$1"
  local dir="$apple_root/slices/$rid/$configuration"
  [[ -f "$dir/.jalium-native-complete" ]] || return 1
  mapfile -t archives < <(find "$dir" -maxdepth 1 -name '*.a' -print | sort)
  [[ ${#archives[@]} -gt 0 ]] || return 1
  local merged="$dir/libJaliumNative.a"
  /usr/bin/libtool -static -o "$merged" "${archives[@]}"
  args+=( -library "$merged" -headers "$headers" )
}

for rid in "${required[@]}"; do
  merge_slice "$rid" || { echo "missing required Apple slice: $rid/$configuration" >&2; exit 3; }
done
for rid in "${optional[@]}"; do merge_slice "$rid" || true; done

xcodebuild -create-xcframework "${args[@]}" -output "$output"
echo "XCFramework ready: $output"

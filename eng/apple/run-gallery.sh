#!/usr/bin/env bash
set -euo pipefail

configuration=Debug
allow_newer_xcode=false
build_only=false
for argument in "$@"; do
  case "$argument" in
    Debug|Release) configuration="$argument" ;;
    --allow-newer-xcode) allow_newer_xcode=true ;;
    --build-only) build_only=true ;;
    *) echo "usage: run-gallery.sh [Debug|Release] [--allow-newer-xcode] [--build-only]" >&2; exit 2 ;;
  esac
done

if [[ "$(uname -s)" != Darwin ]]; then
  echo "The macOS Gallery requires macOS with Xcode." >&2
  exit 2
fi

repo_root="$(cd "$(dirname "$0")/../.." && pwd)"
source "$repo_root/eng/apple/resolve-apple-target.sh"
jalium_resolve_apple_target macos
gallery_root="${JALIUM_GALLERY_ROOT:-$(dirname "$repo_root")/Jalium.UI.Gallery}"
project="$gallery_root/Jalium.UI.Gallery.MacOS/Jalium.UI.Gallery.MacOS.csproj"
build_root="${JALIUM_GALLERY_BUILD_ROOT:-$repo_root/artifacts/macos-gallery}"
mkdir -p "$build_root"
build_root="$(cd "$build_root" && pwd)"
[[ -f "$project" ]] || { echo "Gallery project not found: $project (set JALIUM_GALLERY_ROOT)" >&2; exit 2; }

dotnet_command="$(command -v dotnet || true)"
if [[ -z "$dotnet_command" && -x "$repo_root/.tools/dotnet/dotnet" ]]; then
  dotnet_command="$repo_root/.tools/dotnet/dotnet"
fi
[[ -n "$dotnet_command" ]] || { echo "Install .NET 10 and its macos workload first." >&2; exit 2; }

# Allow the ignored, repository-local tools used for development on a fresh Mac.
if ! command -v cmake >/dev/null; then
  for candidate in "$repo_root"/.tools/cmake-*-macos-universal/CMake.app/Contents/bin; do
    if [[ -x "$candidate/cmake" ]]; then export PATH="$candidate:$PATH"; break; fi
  done
fi
if ! command -v ninja >/dev/null && [[ -x "$repo_root/.tools/bin/ninja" ]]; then
  export PATH="$repo_root/.tools/bin:$PATH"
fi
for tool in cmake ctest ninja python3 xcrun; do
  command -v "$tool" >/dev/null || { echo "Required tool not found: $tool" >&2; exit 2; }
done

native_arguments=(macos "$configuration" --development)
if [[ "$build_only" == true ]]; then native_arguments+=(--build-only); fi
JALIUM_APPLE_ARCHITECTURE="$arch" bash "$repo_root/eng/apple/build-native.sh" "${native_arguments[@]}"
build_arguments=(
  build "$project" -c "$configuration" -r "$rid"
  "-p:JaliumBuildRoot=$build_root"
)
if [[ "$allow_newer_xcode" == true ]]; then
  echo "Local development override: skipping the .NET workload's Xcode version check."
  build_arguments+=(-p:ValidateXcodeVersion=false)
fi
"$dotnet_command" "${build_arguments[@]}"

app="$build_root/bin/Jalium.UI.Gallery.MacOS/$configuration/net10.0-macos/$rid/Jalium.UI.Gallery.MacOS.app"
[[ -d "$app" ]] || { echo "Expected application bundle not found: $app" >&2; exit 3; }
echo "Gallery application: $app"
if [[ "$build_only" == false ]]; then open "$app"; fi

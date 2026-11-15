#!/usr/bin/env bash
set -euo pipefail
platform="${1:?Usage: build-gallery.sh <linux|macos>}"
ui_root="$(cd "$(dirname "$0")/../.." && pwd)"
gallery_root="${JALIUM_GALLERY_ROOT:-$(dirname "$ui_root")/Jalium.UI.Gallery}"
release_root="${JALIUM_RELEASE_ROOT:-$(dirname "$ui_root")/artifacts/gallery-26.11.0}"
export JALIUM_RELEASE_BUILD_ROOT="${JALIUM_RELEASE_BUILD_ROOT:-$release_root/build/$platform}"
mkdir -p "$release_root/assets" "$release_root/test-results"
bash "$ui_root/eng/release/bootstrap-tools.sh"
properties=("-p:RestoreConfigFile=$ui_root/nuget.config" -p:GeneratePackageOnBuild=false \
  -p:BuildJaliumNativeProjects=false -p:BuildJaliumNativeStaticProjects=false)
case "$platform" in
  linux)
    rid=linux-x64
    if [[ "${JALIUM_RELEASE_NATIVE_READY:-0}" != 1 ]]; then
      JALIUM_NATIVE_BUILD_TESTS=0 bash "$ui_root/eng/linux/build-native.sh" "$rid" Release
    fi
    native="$ui_root/src/native/bin/native/$rid/Release"
    bash "$ui_root/eng/linux/check-native-exports.sh" "$native"
    bash "$ui_root/eng/linux/check-symbol-versions.sh" "$native" 2.31 3.4.28
    export LD_LIBRARY_PATH="$native${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
    export JALIUM_WINDOW_SYSTEM=x11
    test_command=(xvfb-run -a dotnet test)
    published="$release_root/publish/$rid"
    dotnet publish "$gallery_root/Jalium.UI.Gallery.Linux/Jalium.UI.Gallery.Linux.csproj" \
      -c Release -r "$rid" --self-contained true -m:1 \
      "-p:JaliumBuildRoot=$JALIUM_RELEASE_BUILD_ROOT/app" "${properties[@]}" -o "$published"
    cp "$native"/*.so "$published/"
    "$published/Jalium.UI.Gallery" --diagnostics-only
    ;;
  macos)
    rid=osx-arm64
    if [[ "${JALIUM_RELEASE_NATIVE_READY:-0}" != 1 ]]; then
      bash "$ui_root/eng/apple/build-native.sh" macos Release
    fi
    native="$ui_root/src/native/bin/native/$rid/Release"
    python3 "$ui_root/eng/apple/check-native-exports.py" "$native" --configuration Release
    export DYLD_LIBRARY_PATH="$native${DYLD_LIBRARY_PATH:+:$DYLD_LIBRARY_PATH}"
    test_command=(dotnet test)
    if [[ "${JALIUM_RELEASE_ALLOW_NEWER_XCODE:-0}" == 1 ]]; then
      properties+=(-p:ValidateXcodeVersion=false)
    fi
    dotnet build "$gallery_root/Jalium.UI.Gallery.MacOS/Jalium.UI.Gallery.MacOS.csproj" \
      -c Release -m:1 "-p:JaliumBuildRoot=$JALIUM_RELEASE_BUILD_ROOT/app" "${properties[@]}"
    published="$JALIUM_RELEASE_BUILD_ROOT/app/bin/Jalium.UI.Gallery.MacOS/Release/net10.0-macos/$rid/Jalium.UI.Gallery.MacOS.app"
    codesign --verify --deep --strict "$published"
    ;;
  *) echo "Unsupported release platform: $platform" >&2; exit 2 ;;
esac
"${test_command[@]}" "$gallery_root/Tests/Jalium.UI.Gallery.Headless.Tests/Jalium.UI.Gallery.Headless.Tests.csproj" \
  -c Release -m:1 "-p:JaliumBuildRoot=$JALIUM_RELEASE_BUILD_ROOT/headless-tests" "${properties[@]}" \
  --results-directory "$release_root/test-results" --logger "trx;LogFileName=$rid-headless.trx"
"${test_command[@]}" "$gallery_root/Tests/Jalium.UI.Gallery.Modules.Main.Tests/Jalium.UI.Gallery.Modules.Main.Tests.csproj" \
  -c Release -m:1 "-p:JaliumBuildRoot=$JALIUM_RELEASE_BUILD_ROOT/gallery-tests" "${properties[@]}" \
  --results-directory "$release_root/test-results" --logger "trx;LogFileName=$rid-gallery.trx"
python3 "$ui_root/eng/release/package-gallery.py" "$platform" \
  --publish "$published" --output "$release_root/assets" --rid "$rid" \
  --framework-root "$ui_root" --gallery-root "$gallery_root"

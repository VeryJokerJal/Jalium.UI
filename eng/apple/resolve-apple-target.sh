#!/usr/bin/env bash
# Resolve one Apple slice consistently across native, shader and SDK inputs.
# Call with jalium_resolve_apple_target "$target" after sourcing this file.
jalium_resolve_apple_target() {
  local requested="$1"
  arch=arm64
  case "$requested" in
    macos)
      arch="${JALIUM_APPLE_ARCHITECTURE:-$(uname -m)}"
      case "$arch" in
        arm64) rid=osx-arm64; preset_arch=arm64 ;;
        x86_64|x64) arch=x86_64; rid=osx-x64; preset_arch=x64 ;;
        *) echo "unsupported macOS architecture: $arch" >&2; return 2 ;;
      esac
      sdk=macosx; system_name=Darwin; deployment=15.0
      preset="apple-macos-$preset_arch"
      ;;
    ios) sdk=iphoneos; system_name=iOS; deployment=18.0; rid=ios-arm64 ;;
    iossimulator) sdk=iphonesimulator; system_name=iOS; deployment=18.0; rid=iossimulator-arm64 ;;
    tvos) sdk=appletvos; system_name=tvOS; deployment=18.0; rid=tvos-arm64 ;;
    tvossimulator) sdk=appletvsimulator; system_name=tvOS; deployment=18.0; rid=tvossimulator-arm64 ;;
    visionos) sdk=xros; system_name=visionOS; deployment=2.0; rid=visionos-arm64 ;;
    visionsimulator) sdk=xrsimulator; system_name=visionOS; deployment=2.0; rid=visionossimulator-arm64 ;;
    *) echo "unsupported Apple target: $requested" >&2; return 2 ;;
  esac
  if [[ "$requested" != macos ]]; then
    if [[ "${JALIUM_APPLE_ARCHITECTURE:-arm64}" != arm64 ]]; then
      echo "this Apple target supports only arm64: $requested" >&2; return 2
    fi
    preset="apple-$rid"
  fi
}

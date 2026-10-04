#!/usr/bin/env bash
set -euo pipefail

if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "The Apple shader compiler slices require macOS with Xcode." >&2
  exit 2
fi

target="${1:?usage: prepare-shader-toolchain.sh <macos|ios|iossimulator|tvos|tvossimulator|visionos|visionsimulator> [Debug|Release]}"
configuration="${2:-Release}"
case "$configuration" in Debug|Release) ;; *) echo "configuration must be Debug or Release" >&2; exit 2;; esac

case "$target" in
  macos) sdk=macosx; system_name=Darwin; deployment=15.0; rid=osx-arm64 ;;
  ios) sdk=iphoneos; system_name=iOS; deployment=18.0; rid=ios-arm64 ;;
  iossimulator) sdk=iphonesimulator; system_name=iOS; deployment=18.0; rid=iossimulator-arm64 ;;
  tvos) sdk=appletvos; system_name=tvOS; deployment=18.0; rid=tvos-arm64 ;;
  tvossimulator) sdk=appletvsimulator; system_name=tvOS; deployment=18.0; rid=tvossimulator-arm64 ;;
  visionos) sdk=xros; system_name=visionOS; deployment=2.0; rid=visionos-arm64 ;;
  visionsimulator) sdk=xrsimulator; system_name=visionOS; deployment=2.0; rid=visionossimulator-arm64 ;;
  *) echo "unsupported Apple target: $target" >&2; exit 2 ;;
esac

repo_root="$(cd "$(dirname "$0")/../.." && pwd)"
deps_root="${JALIUM_APPLE_DEPS_ROOT:-$repo_root/src/native/out/apple-deps}"
source_root="$deps_root/src"
build_root="$deps_root/build/$rid"
slice_root="$deps_root/slices/$rid/$configuration"
host_root="$deps_root/build/host"
env_file="$slice_root/jalium-shader-toolchain.env"
lock_file="$repo_root/eng/apple/dependencies.lock.json"

dxc_revision="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["dependencies"]["DirectXShaderCompiler"]["revision"])' "$lock_file")"
spirv_cross_revision="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["dependencies"]["SPIRV-Cross"]["revision"])' "$lock_file")"
fingerprint="$dxc_revision:$spirv_cross_revision:$sdk:$deployment:$configuration:$(xcodebuild -version | tr '\n' ' ')"

if [[ -f "$slice_root/.complete" ]] && [[ "$(cat "$slice_root/.complete")" == "$fingerprint" ]] && [[ -f "$env_file" ]]; then
  echo "$env_file"
  exit 0
fi

mkdir -p "$source_root" "$build_root" "$slice_root" "$host_root"

checkout_pinned() {
  local repository="$1"
  local revision="$2"
  local destination="$3"
  if [[ ! -d "$destination/.git" ]]; then
    git clone --filter=blob:none "$repository" "$destination"
  fi
  git -C "$destination" fetch --depth 1 origin "$revision"
  git -C "$destination" checkout --detach "$revision"
}

dxc_source="$source_root/DirectXShaderCompiler"
spirv_cross_source="$source_root/SPIRV-Cross"
checkout_pinned https://github.com/microsoft/DirectXShaderCompiler.git "$dxc_revision" "$dxc_source"
checkout_pinned https://github.com/KhronosGroup/SPIRV-Cross.git "$spirv_cross_revision" "$spirv_cross_source"

# Build the host compiler once. It is used by the offline shader pipeline; the
# target slice below is linked into Jalium for dynamic SourceHlsl compilation.
host_dxc_build="$host_root/dxc"
if [[ ! -x "$host_dxc_build/bin/dxc" ]]; then
  cmake -S "$dxc_source" -B "$host_dxc_build" -G Ninja \
    -C "$dxc_source/cmake/caches/PredefinedParams.cmake" \
    -DCMAKE_BUILD_TYPE=Release -DBUILD_SHARED_LIBS=OFF \
    -DENABLE_SPIRV_CODEGEN=ON -DSPIRV_BUILD_TESTS=OFF \
    -DLLVM_INCLUDE_TESTS=OFF -DCLANG_INCLUDE_TESTS=OFF \
    -DHLSL_INCLUDE_TESTS=OFF -DLLVM_INCLUDE_EXAMPLES=OFF \
    -DLLVM_BUILD_EXAMPLES=OFF -DLLVM_INCLUDE_BENCHMARKS=OFF
  cmake --build "$host_dxc_build" --target dxc llvm-tblgen --parallel
fi

common_apple_args=(
  -G Xcode
  -DCMAKE_OSX_ARCHITECTURES=arm64
  -DCMAKE_OSX_SYSROOT="$sdk"
  -DCMAKE_OSX_DEPLOYMENT_TARGET="$deployment"
  -DCMAKE_TRY_COMPILE_TARGET_TYPE=STATIC_LIBRARY
)
if [[ "$system_name" != Darwin ]]; then
  common_apple_args+=( -DCMAKE_SYSTEM_NAME="$system_name" )
fi

spirv_cross_build="$build_root/spirv-cross"
cmake -S "$spirv_cross_source" -B "$spirv_cross_build" "${common_apple_args[@]}" \
  -DBUILD_SHARED_LIBS=OFF -DSPIRV_CROSS_STATIC=ON \
  -DSPIRV_CROSS_CLI=OFF -DSPIRV_CROSS_ENABLE_TESTS=OFF \
  -DSPIRV_CROSS_ENABLE_C_API=ON -DSPIRV_CROSS_ENABLE_MSL=ON \
  -DSPIRV_CROSS_ENABLE_GLSL=OFF -DSPIRV_CROSS_ENABLE_HLSL=OFF \
  -DSPIRV_CROSS_ENABLE_CPP=OFF -DSPIRV_CROSS_ENABLE_REFLECT=OFF
cmake --build "$spirv_cross_build" --config "$configuration" \
  --target spirv-cross-c spirv-cross-msl spirv-cross-core --parallel

dxc_build="$build_root/dxc"
cmake -S "$dxc_source" -B "$dxc_build" "${common_apple_args[@]}" \
  -C "$dxc_source/cmake/caches/PredefinedParams.cmake" \
  -DBUILD_SHARED_LIBS=OFF -DENABLE_SPIRV_CODEGEN=ON \
  -DSPIRV_BUILD_TESTS=OFF -DLLVM_INCLUDE_TESTS=OFF \
  -DCLANG_INCLUDE_TESTS=OFF -DHLSL_INCLUDE_TESTS=OFF \
  -DLLVM_INCLUDE_EXAMPLES=OFF -DLLVM_BUILD_EXAMPLES=OFF \
  -DLLVM_INCLUDE_BENCHMARKS=OFF \
  -DLLVM_TABLEGEN="$host_dxc_build/bin/llvm-tblgen"
cmake --build "$dxc_build" --config "$configuration" --target dxcompiler --parallel

dxc_archives=()
while IFS= read -r archive; do dxc_archives+=("$archive"); done < <(
  find "$dxc_build" -type f -name '*.a' \
    ! -path '*/CMakeFiles/CMakeScratch/*' ! -name '*gtest*' ! -name '*unittest*' | sort)
if [[ ${#dxc_archives[@]} -eq 0 ]]; then
  echo "DXC did not produce target archives for $rid" >&2
  exit 3
fi
dxc_merged="$slice_root/libJaliumDxcRuntime.a"
/usr/bin/libtool -static -o "$dxc_merged" "${dxc_archives[@]}"

find_archive() {
  local stem="$1"
  find "$spirv_cross_build" -type f -name "lib${stem}.a" -print -quit
}
spvc_c="$(find_archive spirv-cross-c)"
spvc_msl="$(find_archive spirv-cross-msl)"
spvc_core="$(find_archive spirv-cross-core)"
if [[ -z "$spvc_c" || -z "$spvc_msl" || -z "$spvc_core" ]]; then
  echo "SPIRV-Cross did not produce the C/MSL/core archives for $rid" >&2
  exit 3
fi

{
  printf 'JALIUM_DXC_INCLUDE_DIR=%q\n' "$dxc_source/include"
  printf 'JALIUM_DXC_LIBRARY=%q\n' "$dxc_merged"
  printf 'JALIUM_SPIRV_CROSS_INCLUDE_DIR=%q\n' "$spirv_cross_source"
  printf 'JALIUM_SPIRV_CROSS_LIBRARIES=%q\n' "$spvc_c;$spvc_msl;$spvc_core"
  printf 'JALIUM_DXC_HOST_EXECUTABLE=%q\n' "$host_dxc_build/bin/dxc"
} > "$env_file"
printf '%s' "$fingerprint" > "$slice_root/.complete"
echo "$env_file"

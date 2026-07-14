#!/usr/bin/env bash
# Cross-compile Jalium native libraries for Android
# Usage: bash build-android.sh [arm64-v8a|x86_64|all]
#   Default/package mode: all (builds both arm64-v8a and x86_64)
#   A single ABI is development-only and invalidates the package-set stamp;
#   run "all" before building or packing Jalium.UI.Android.
#
# Prerequisites:
#   - Android NDK 27.2.12479018 installed at $ANDROID_SDK_ROOT/ndk/
#   - CMake 3.25+
#
# Override the pin deliberately with JALIUM_ANDROID_NDK_VERSION=<version>.
# For a non-standard installation, also set ANDROID_NDK_ROOT=<path>.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ANDROID_SDK="${ANDROID_SDK_ROOT:-${ANDROID_HOME:-${ANDROID_SDK:-$HOME/AppData/Local/Android/Sdk}}}"
TARGET_ABI="${1:-all}"
ANDROID_API="${JALIUM_ANDROID_API:-24}"
NDK_VERSION="${JALIUM_ANDROID_NDK_VERSION:-27.2.12479018}"
NDK_DIR="${ANDROID_NDK_ROOT:-${ANDROID_NDK_HOME:-$ANDROID_SDK/ndk/$NDK_VERSION}}"
HOST_TAG="${JALIUM_ANDROID_NDK_HOST_TAG:-windows-x86_64}"
JOBS="${JALIUM_ANDROID_BUILD_JOBS:-$(nproc 2>/dev/null || echo 4)}"
ANDROID_PACKAGE_ROOT="$SCRIPT_DIR/../../samples/Jalium.UI.AndroidDemo/libs"
PACKAGE_STAMP="$ANDROID_PACKAGE_ROOT/.jalium-android-payload-complete"

# Build provenance recorded into every payload stamp; the NuGet pack guard
# (eng/msbuild/JaliumStaleNativeGuard.targets) compares it with the commit
# being packed and refuses stale payloads. JALIUM_GIT_HEAD / JALIUM_GIT_DIRTY
# override discovery for environments where git cannot read this checkout.
jalium_git_head() {
    if [ -n "${JALIUM_GIT_HEAD:-}" ]; then
        echo "$JALIUM_GIT_HEAD"
    else
        git -C "$SCRIPT_DIR/../.." rev-parse HEAD 2>/dev/null || echo unknown
    fi
}
jalium_git_dirty() {
    if [ -n "${JALIUM_GIT_DIRTY:-}" ]; then
        echo "$JALIUM_GIT_DIRTY"
    elif ! git -C "$SCRIPT_DIR/../.." rev-parse HEAD >/dev/null 2>&1; then
        echo unknown
    elif [ -n "$(git -C "$SCRIPT_DIR/../.." status --porcelain --untracked-files=normal -- src/native 2>/dev/null)" ]; then
        echo 1
    else
        echo 0
    fi
}

# Reject bad ABI arguments before any stamp is invalidated below; a typo like
# "arm64" must not destroy a previously valid dual-ABI package stamp.
case "$TARGET_ABI" in
    arm64-v8a|x86_64|all) ;;
    *)
        echo "ERROR: Unknown ABI '$TARGET_ABI'. Use arm64-v8a, x86_64, or all."
        exit 1
        ;;
esac

readonly EXPECTED_LIBRARIES=(
    libjalium.native.core.so
    libjalium.native.platform.so
    libjalium.native.media.core.so
    libjalium.native.text.so
    libjalium.native.vulkan.so
    libjalium.native.software.so
    libjalium.native.media.so
)

if [ ! -d "$NDK_DIR" ]; then
    echo "ERROR: Pinned Android NDK $NDK_VERSION was not found at $NDK_DIR"
    echo "Install it with sdkmanager 'ndk;$NDK_VERSION', or set"
    echo "JALIUM_ANDROID_NDK_VERSION and (for custom paths) ANDROID_NDK_ROOT."
    exit 1
fi

ACTUAL_NDK_VERSION="$(sed -n 's/^Pkg\.Revision[[:space:]]*=[[:space:]]*//p' "$NDK_DIR/source.properties" | tr -d '\r')"
if [ "$ACTUAL_NDK_VERSION" != "$NDK_VERSION" ]; then
    echo "ERROR: NDK path/version mismatch: requested $NDK_VERSION, found ${ACTUAL_NDK_VERSION:-unknown} at $NDK_DIR"
    exit 1
fi

echo "Using pinned NDK: $NDK_DIR ($ACTUAL_NDK_VERSION)"

TOOLCHAIN="$NDK_DIR/build/cmake/android.toolchain.cmake"
MAKE_PROGRAM="$NDK_DIR/prebuilt/$HOST_TAG/bin/make.exe"
READELF="$NDK_DIR/toolchains/llvm/prebuilt/$HOST_TAG/bin/llvm-readelf.exe"

for required_tool in "$TOOLCHAIN" "$MAKE_PROGRAM" "$READELF"; do
    if [ ! -f "$required_tool" ]; then
        echo "ERROR: Required NDK tool not found: $required_tool"
        exit 1
    fi
done

# Any native rebuild makes the dual-ABI delivery set incomplete until this
# invocation successfully finishes "all". This prevents a fresh ABI from being
# packed beside a stale ABI left by an earlier build.
mkdir -p "$ANDROID_PACKAGE_ROOT"
rm -f "$PACKAGE_STAMP" "$PACKAGE_STAMP.tmp"

check_16k_alignment() {
    local library="$1"
    local alignment
    local load_count=0

    while IFS= read -r alignment; do
        load_count=$((load_count + 1))
        if (( alignment < 16384 )); then
            echo "ERROR: $library has LOAD alignment $alignment; Android native libraries require at least 0x4000 (16 KB)."
            return 1
        fi
    done < <("$READELF" -lW "$library" | awk '$1 == "LOAD" { print $NF }')

    if (( load_count == 0 )); then
        echo "ERROR: Could not find ELF LOAD segments in $library"
        return 1
    fi
}

build_abi() {
    local ABI="$1"
    local RID
    local STL_TRIPLE
    case "$ABI" in
        arm64-v8a)
            RID="android-arm64"
            STL_TRIPLE="aarch64-linux-android"
            ;;
        x86_64)
            RID="android-x64"
            STL_TRIPLE="x86_64-linux-android"
            ;;
        *)
            echo "ERROR: Unsupported packaged ABI '$ABI'."
            return 1
            ;;
    esac

    local BUILD_DIR="$SCRIPT_DIR/out/android/$NDK_VERSION/$ABI"
    local NATIVE_OUTPUT_DIR="$SCRIPT_DIR/bin/native/$RID/Release"
    local OUTPUT_DIR="$ANDROID_PACKAGE_ROOT/$ABI"
    local STL_SO="$NDK_DIR/toolchains/llvm/prebuilt/$HOST_TAG/sysroot/usr/lib/$STL_TRIPLE/libc++_shared.so"
    local library
    local stamp_tmp

    mkdir -p "$BUILD_DIR" "$NATIVE_OUTPUT_DIR" "$OUTPUT_DIR"

    # A build or copy failure must leave packaging disabled. CMake writes its
    # own stamp only after every native target links; the destination stamp is
    # written atomically only after every file and ELF alignment is validated.
    rm -f "$NATIVE_OUTPUT_DIR/.jalium-native-complete" \
          "$OUTPUT_DIR/.jalium-native-complete" \
          "$OUTPUT_DIR/.jalium-native-complete.tmp"

    echo ""
    echo "=== Configuring CMake for Android $ABI ==="
    cmake -G "Unix Makefiles" -B "$BUILD_DIR" -S "$SCRIPT_DIR" \
        -DCMAKE_TOOLCHAIN_FILE="$TOOLCHAIN" \
        -DANDROID_ABI="$ABI" \
        -DANDROID_PLATFORM="android-$ANDROID_API" \
        -DCMAKE_BUILD_TYPE=Release \
        -DANDROID_STL=c++_shared \
        -DANDROID_SUPPORT_FLEXIBLE_PAGE_SIZES=ON \
        -DJALIUM_NATIVE_RID="$RID" \
        -DCMAKE_MAKE_PROGRAM="$MAKE_PROGRAM"

    echo "=== Building $ABI ==="
    cmake --build "$BUILD_DIR" --config Release \
        --target jalium.native.package.complete --parallel "$JOBS"

    if [ ! -f "$NATIVE_OUTPUT_DIR/.jalium-native-complete" ]; then
        echo "ERROR: CMake did not produce the complete payload stamp at $NATIVE_OUTPUT_DIR/.jalium-native-complete"
        exit 1
    fi

    if [ ! -f "$STL_SO" ]; then
        echo "ERROR: libc++_shared.so was not found at $STL_SO"
        exit 1
    fi

    echo "=== Validating native payload and 16 KB ELF alignment ==="
    for library in "${EXPECTED_LIBRARIES[@]}"; do
        if [ ! -f "$NATIVE_OUTPUT_DIR/$library" ]; then
            echo "ERROR: Complete-stamped payload is missing $NATIVE_OUTPUT_DIR/$library"
            exit 1
        fi
        check_16k_alignment "$NATIVE_OUTPUT_DIR/$library"
    done
    check_16k_alignment "$STL_SO"

    echo "=== Copying complete RID payload from $NATIVE_OUTPUT_DIR to $OUTPUT_DIR ==="
    rm -f "$OUTPUT_DIR"/libjalium.native.*.so "$OUTPUT_DIR/libc++_shared.so"
    for library in "${EXPECTED_LIBRARIES[@]}"; do
        cp -v "$NATIVE_OUTPUT_DIR/$library" "$OUTPUT_DIR/$library"
    done
    cp -v "$STL_SO" "$OUTPUT_DIR/libc++_shared.so"

    for library in "${EXPECTED_LIBRARIES[@]}" libc++_shared.so; do
        check_16k_alignment "$OUTPUT_DIR/$library"
    done

    stamp_tmp="$OUTPUT_DIR/.jalium-native-complete.tmp"
    {
        echo "head=$(jalium_git_head)"
        echo "dirty=$(jalium_git_dirty)"
        echo "abi=$ABI"
        echo "rid=$RID"
        echo "configuration=Release"
        echo "android_api=$ANDROID_API"
        echo "ndk=$NDK_VERSION"
        echo "page_alignment=16384"
    } > "$stamp_tmp"
    mv "$stamp_tmp" "$OUTPUT_DIR/.jalium-native-complete"

    echo "=== Done $ABI! .so files: ==="
    ls -la "$OUTPUT_DIR/"*.so "$OUTPUT_DIR/.jalium-native-complete"
}

case "$TARGET_ABI" in
    arm64-v8a)
        build_abi arm64-v8a
        ;;
    x86_64)
        build_abi x86_64
        ;;
    all)
        build_abi arm64-v8a
        build_abi x86_64
        {
            echo "head=$(jalium_git_head)"
            echo "dirty=$(jalium_git_dirty)"
            echo "abis=arm64-v8a;x86_64"
            echo "configuration=Release"
            echo "android_api=$ANDROID_API"
            echo "ndk=$NDK_VERSION"
            echo "page_alignment=16384"
        } > "$PACKAGE_STAMP.tmp"
        mv "$PACKAGE_STAMP.tmp" "$PACKAGE_STAMP"
        echo "=== Dual-ABI Android package payload complete: $PACKAGE_STAMP ==="
        ;;
    *)
        echo "ERROR: Unknown ABI '$TARGET_ABI'. Use arm64-v8a, x86_64, or all."
        exit 1
        ;;
esac

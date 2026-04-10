#!/bin/bash
# Cross-compile Jalium native libraries for Android
# Usage: bash build-android.sh [arm64-v8a|x86_64|all]
#   Default: all (builds both arm64-v8a and x86_64)
#
# Prerequisites:
#   - Android NDK installed at $ANDROID_SDK/ndk/<version>
#   - CMake 3.25+

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ANDROID_SDK="${ANDROID_SDK:-$HOME/AppData/Local/Android/Sdk}"
TARGET_ABI="${1:-all}"

# Find latest NDK
NDK_DIR=$(ls -d "$ANDROID_SDK/ndk/"* 2>/dev/null | sort -V | tail -1)
if [ -z "$NDK_DIR" ]; then
    echo "ERROR: Android NDK not found in $ANDROID_SDK/ndk/"
    exit 1
fi
echo "Using NDK: $NDK_DIR"

TOOLCHAIN="$NDK_DIR/build/cmake/android.toolchain.cmake"
MAKE_PROGRAM="$NDK_DIR/prebuilt/windows-x86_64/bin/make.exe"

build_abi() {
    local ABI="$1"
    local BUILD_DIR="$SCRIPT_DIR/out/android/$ABI"
    local OUTPUT_DIR="$SCRIPT_DIR/../../samples/Jalium.UI.AndroidDemo/libs/$ABI"

    mkdir -p "$BUILD_DIR" "$OUTPUT_DIR"

    echo ""
    echo "=== Configuring CMake for Android $ABI ==="
    cmake -G "Unix Makefiles" -B "$BUILD_DIR" -S "$SCRIPT_DIR" \
        -DCMAKE_TOOLCHAIN_FILE="$TOOLCHAIN" \
        -DANDROID_ABI="$ABI" \
        -DANDROID_PLATFORM=android-24 \
        -DCMAKE_BUILD_TYPE=Release \
        -DANDROID_STL=c++_shared \
        -DCMAKE_MAKE_PROGRAM="$MAKE_PROGRAM"

    echo "=== Building $ABI ==="
    cmake --build "$BUILD_DIR" --config Release -j$(nproc 2>/dev/null || echo 4)

    echo "=== Copying .so files to $OUTPUT_DIR ==="
    find "$BUILD_DIR" -name "libjalium.native.*.so" -exec cp -v {} "$OUTPUT_DIR/" \;

    # Copy the STL shared library needed at runtime
    if [ "$ABI" = "arm64-v8a" ]; then
        STL_SO="$NDK_DIR/toolchains/llvm/prebuilt/windows-x86_64/sysroot/usr/lib/aarch64-linux-android/libc++_shared.so"
    elif [ "$ABI" = "x86_64" ]; then
        STL_SO="$NDK_DIR/toolchains/llvm/prebuilt/windows-x86_64/sysroot/usr/lib/x86_64-linux-android/libc++_shared.so"
    fi
    if [ -n "$STL_SO" ] && [ -f "$STL_SO" ]; then
        cp -v "$STL_SO" "$OUTPUT_DIR/"
    fi

    echo "=== Done $ABI! .so files: ==="
    ls -la "$OUTPUT_DIR/"*.so 2>/dev/null
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
        ;;
    *)
        echo "ERROR: Unknown ABI '$TARGET_ABI'. Use arm64-v8a, x86_64, or all."
        exit 1
        ;;
esac

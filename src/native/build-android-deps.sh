#!/bin/bash
# Build FreeType and HarfBuzz for Android
# Usage: bash build-android-deps.sh [arm64-v8a|x86_64|all]
#   Default: all
# These are required by jalium.native.text
set -e

ANDROID_SDK="${ANDROID_SDK:-$HOME/AppData/Local/Android/Sdk}"
NDK_DIR=$(ls -d "$ANDROID_SDK/ndk/"* 2>/dev/null | sort -V | tail -1)
TOOLCHAIN="$NDK_DIR/build/cmake/android.toolchain.cmake"
MAKE="$NDK_DIR/prebuilt/windows-x86_64/bin/make.exe"
TARGET_ABI="${1:-all}"

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
SDK_ROOT="$(cd "$SCRIPT_DIR/../../../Jalium.SDK" && pwd)"
DEPS_BUILD="$SCRIPT_DIR/out/android/deps"

echo "NDK: $NDK_DIR"
echo "SDK_ROOT: $SDK_ROOT"

# Map ABI to SDK dir name
abi_to_sdk_dir() {
    case "$1" in
        arm64-v8a) echo "android-arm64" ;;
        x86_64)    echo "android-x86_64" ;;
        *) echo "$1" ;;
    esac
}

FREETYPE_VER="2.13.3"
HARFBUZZ_VER="10.4.0"
FREETYPE_SRC="$DEPS_BUILD/freetype-$FREETYPE_VER"
HARFBUZZ_SRC="$DEPS_BUILD/harfbuzz-$HARFBUZZ_VER"

mkdir -p "$DEPS_BUILD"

# Download sources once (shared across ABIs)
if [ ! -d "$FREETYPE_SRC" ]; then
    echo "=== Downloading FreeType $FREETYPE_VER ==="
    cd "$DEPS_BUILD"
    curl -L "https://download.savannah.gnu.org/releases/freetype/freetype-$FREETYPE_VER.tar.xz" -o freetype.tar.xz
    tar xf freetype.tar.xz
fi

if [ ! -d "$HARFBUZZ_SRC" ]; then
    echo "=== Downloading HarfBuzz $HARFBUZZ_VER ==="
    cd "$DEPS_BUILD"
    curl -L "https://github.com/harfbuzz/harfbuzz/releases/download/$HARFBUZZ_VER/harfbuzz-$HARFBUZZ_VER.tar.xz" -o harfbuzz.tar.xz
    tar xf harfbuzz.tar.xz
fi

build_deps_for_abi() {
    local ABI="$1"
    local SDK_DIR="$(abi_to_sdk_dir "$ABI")"

    mkdir -p "$SDK_ROOT/freetype/include" "$SDK_ROOT/freetype/lib/$SDK_DIR"
    mkdir -p "$SDK_ROOT/harfbuzz/include/harfbuzz" "$SDK_ROOT/harfbuzz/lib/$SDK_DIR"

    # === FreeType ===
    if [ ! -f "$SDK_ROOT/freetype/lib/$SDK_DIR/libfreetype.a" ]; then
        echo ""
        echo "=== Building FreeType $FREETYPE_VER for $ABI ==="
        local FT_BUILD="$DEPS_BUILD/freetype-build-$ABI"

        cmake -G "Unix Makefiles" -B "$FT_BUILD" -S "$FREETYPE_SRC" \
            -DCMAKE_TOOLCHAIN_FILE="$TOOLCHAIN" \
            -DANDROID_ABI="$ABI" \
            -DANDROID_PLATFORM=android-24 \
            -DCMAKE_BUILD_TYPE=Release \
            -DCMAKE_MAKE_PROGRAM="$MAKE" \
            -DBUILD_SHARED_LIBS=OFF \
            -DFT_DISABLE_ZLIB=ON \
            -DFT_DISABLE_BZIP2=ON \
            -DFT_DISABLE_PNG=ON \
            -DFT_DISABLE_HARFBUZZ=ON \
            -DFT_DISABLE_BROTLI=ON

        cmake --build "$FT_BUILD" --config Release -j4

        cp "$FT_BUILD/libfreetype.a" "$SDK_ROOT/freetype/lib/$SDK_DIR/"
        cp -r "$FREETYPE_SRC/include/"* "$SDK_ROOT/freetype/include/"
        cp "$FT_BUILD/include/freetype/config/"* "$SDK_ROOT/freetype/include/freetype/config/" 2>/dev/null || true
        echo "FreeType installed to $SDK_ROOT/freetype/lib/$SDK_DIR/"
    else
        echo "FreeType for $ABI already built, skipping."
    fi

    # === HarfBuzz ===
    if [ ! -f "$SDK_ROOT/harfbuzz/lib/$SDK_DIR/libharfbuzz.a" ]; then
        echo ""
        echo "=== Building HarfBuzz $HARFBUZZ_VER for $ABI ==="
        local HB_BUILD="$DEPS_BUILD/harfbuzz-build-$ABI"

        cmake -G "Unix Makefiles" -B "$HB_BUILD" -S "$HARFBUZZ_SRC" \
            -DCMAKE_TOOLCHAIN_FILE="$TOOLCHAIN" \
            -DANDROID_ABI="$ABI" \
            -DANDROID_PLATFORM=android-24 \
            -DCMAKE_BUILD_TYPE=Release \
            -DCMAKE_MAKE_PROGRAM="$MAKE" \
            -DBUILD_SHARED_LIBS=OFF \
            -DHB_HAVE_FREETYPE=ON \
            -DFREETYPE_INCLUDE_DIR_freetype2="$SDK_ROOT/freetype/include" \
            -DFREETYPE_INCLUDE_DIR_ft2build="$SDK_ROOT/freetype/include" \
            -DFREETYPE_LIBRARY="$SDK_ROOT/freetype/lib/$SDK_DIR/libfreetype.a" \
            -DHB_HAVE_GLIB=OFF \
            -DHB_HAVE_ICU=OFF \
            -DHB_BUILD_SUBSET=OFF

        cmake --build "$HB_BUILD" --config Release -j4

        cp "$HB_BUILD/libharfbuzz.a" "$SDK_ROOT/harfbuzz/lib/$SDK_DIR/"
        cp "$HARFBUZZ_SRC/src/"*.h "$SDK_ROOT/harfbuzz/include/harfbuzz/"
        echo "HarfBuzz installed to $SDK_ROOT/harfbuzz/lib/$SDK_DIR/"
    else
        echo "HarfBuzz for $ABI already built, skipping."
    fi
}

case "$TARGET_ABI" in
    arm64-v8a) build_deps_for_abi arm64-v8a ;;
    x86_64)    build_deps_for_abi x86_64 ;;
    all)
        build_deps_for_abi arm64-v8a
        build_deps_for_abi x86_64
        ;;
    *)
        echo "ERROR: Unknown ABI '$TARGET_ABI'. Use arm64-v8a, x86_64, or all."
        exit 1
        ;;
esac

echo ""
echo "=== Dependencies ready! Now run: bash build-android.sh ==="

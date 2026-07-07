#!/bin/bash
# FreeType and HarfBuzz have been removed — jalium.native.text now uses a
# self-hosted font engine (sfnt/TrueType/CFF parsing, rasterization and OpenType
# shaping) with no third-party font dependencies. This script is kept as a no-op
# so existing build-android.sh invocations that call it still succeed.
echo "build-android-deps.sh: nothing to build (self-hosted text engine, no FreeType/HarfBuzz)."
exit 0

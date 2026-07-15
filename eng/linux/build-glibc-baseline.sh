#!/usr/bin/env bash
set -euo pipefail

usage() {
  echo "Usage: bash eng/linux/build-glibc-baseline.sh <linux-x64|linux-arm64> [Debug|Release]" >&2
}

if [[ $# -lt 1 || $# -gt 2 ]]; then
  usage
  exit 2
fi

rid="$1"
configuration="${2:-Release}"
case "$rid" in
  linux-x64) expected_machine=x86_64 ;;
  linux-arm64) expected_machine=aarch64 ;;
  *) usage; exit 2 ;;
esac
case "$configuration" in
  Debug|Release) ;;
  *) usage; exit 2 ;;
esac

if [[ ! -r /etc/os-release ]]; then
  echo "The glibc baseline build must run in Ubuntu 20.04." >&2
  exit 1
fi
# shellcheck disable=SC1091
source /etc/os-release
if [[ "${ID:-}" != ubuntu || "${VERSION_ID:-}" != 20.04 ]]; then
  echo "Expected Ubuntu 20.04, got ${PRETTY_NAME:-unknown}." >&2
  exit 1
fi
if [[ "$(uname -m)" != "$expected_machine" ]]; then
  echo "$rid requires $expected_machine, but the container is $(uname -m)." >&2
  exit 1
fi
if [[ "$(getconf GNU_LIBC_VERSION)" != "glibc 2.31" ]]; then
  echo "Expected the Ubuntu 20.04 glibc 2.31 baseline; got $(getconf GNU_LIBC_VERSION)." >&2
  exit 1
fi
if ! command -v cmake >/dev/null 2>&1 || ! command -v ninja >/dev/null 2>&1; then
  echo "Use the image built from eng/linux/ubuntu20.04-glibc.Dockerfile." >&2
  exit 1
fi

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/../.." && pwd)"
build_dir="${JALIUM_NATIVE_BUILD_DIR:-/tmp/jalium-native-build-$rid}"
output_root="${JALIUM_NATIVE_OUTPUT_ROOT:-$repo_root/src/native/bin/native}"
payload="$output_root/$rid/$configuration"

export JALIUM_NATIVE_BUILD_DIR="$build_dir"
export JALIUM_NATIVE_OUTPUT_ROOT="$output_root"
export JALIUM_NATIVE_BUILD_TESTS=1

cmake --version
"${CXX:-c++}" --version | head -n 1
bash "$script_dir/build-native.sh" "$rid" "$configuration"

# Use a build-local GStreamer registry so release validation cannot inherit a
# stale or partially initialized cache from the container. Prewarming the
# elements also turns decoder/demuxer discovery failures into an explicit
# dependency error instead of a generic unsupported-format result in the media
# smoke test.
export GST_REGISTRY="$build_dir/gstreamer-registry.bin"
rm -f "$GST_REGISTRY" "$GST_REGISTRY.lock"
for element in avdec_h264 h264parse matroskademux souphttpsrc avdec_aac aacparse; do
  if ! gst-inspect-1.0 "$element" >/dev/null; then
    echo "Required GStreamer element is unavailable: $element" >&2
    exit 1
  fi
done

xvfb-run -a ctest --test-dir "$build_dir" --output-on-failure

runtime="/tmp/jalium-weston-$rid"
mkdir -p "$runtime"
chmod 700 "$runtime"
XDG_RUNTIME_DIR="$runtime" weston \
  --backend=headless-backend.so \
  --socket=wayland-baseline \
  --idle-time=0 \
  --log=/tmp/jalium-weston.log &
weston_pid=$!
cleanup() {
  kill "$weston_pid" 2>/dev/null || true
  wait "$weston_pid" 2>/dev/null || true
}
trap cleanup EXIT
for _ in $(seq 1 100); do
  [[ -S "$runtime/wayland-baseline" ]] && break
  sleep 0.1
done
if [[ ! -S "$runtime/wayland-baseline" ]]; then
  cat /tmp/jalium-weston.log >&2
  echo "Weston did not create the baseline Wayland socket." >&2
  exit 1
fi

wayland_smoke="$payload/jalium.native.software.wayland.tests"
if [[ ! -x "$wayland_smoke" ]]; then
  echo "Wayland software smoke executable is missing: $wayland_smoke" >&2
  exit 1
fi
XDG_RUNTIME_DIR="$runtime" \
  WAYLAND_DISPLAY=wayland-baseline \
  JALIUM_WINDOW_SYSTEM=wayland \
  "$wayland_smoke"
cleanup
trap - EXIT

bash "$script_dir/check-symbol-versions.sh" "$payload" 2.31 3.4.28
echo "Ubuntu 20.04 glibc baseline validated for $rid."

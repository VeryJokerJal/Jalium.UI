#!/usr/bin/env bash
set -euo pipefail

rid=${1:-linux-x64}
configuration=${2:-Release}
repo_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
payload=${JALIUM_NATIVE_OUTPUT_ROOT:-$repo_root/src/native/bin/native}/$rid/$configuration
test_binary=$payload/jalium.native.platform.window.tests
output=${JALIUM_WINDOW_TEST_OUTPUT:-$(mktemp -d -t jalium-window-menu-input.XXXXXX)}
mkdir -p "$output"
output=$(cd -- "$output" && pwd)
export JALIUM_WINDOW_TEST_OUTPUT="$output"
test -x "$test_binary"
command -v weston >/dev/null
command -v timeout >/dev/null
if [[ ${3:-} != --inside-xvfb ]]; then
    command -v xvfb-run >/dev/null
    exec xvfb-run -a -s "-screen 0 1400x900x24" bash "$0" "$rid" "$configuration" --inside-xvfb
fi

runtime=$(mktemp -d -t jalium-window-menu-input.XXXXXX)
chmod 700 "$runtime"
compositor_pid=""
cleanup() {
    if [[ -n "$compositor_pid" ]]; then
        kill "$compositor_pid" 2>/dev/null || true
        wait "$compositor_pid" 2>/dev/null || true
    fi
    rm -rf -- "$runtime"
}
trap cleanup EXIT
export XDG_RUNTIME_DIR="$runtime" WAYLAND_DISPLAY=wayland-menu-input
export LD_LIBRARY_PATH="$payload${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
# Unlike the headless backend, the nested X11 backend supplies a real wl_seat.
# These tests verify request ownership; no compositor menu appearance is assumed.
weston --backend=x11-backend.so --use-pixman --socket="$WAYLAND_DISPLAY" \
    --width=1280 --height=800 --idle-time=0 --log="$output/weston.log" &
compositor_pid=$!
for attempt in $(seq 1 100); do
    test -S "$runtime/$WAYLAND_DISPLAY" && break
    kill -0 "$compositor_pid"
    sleep .05
done
test -S "$runtime/$WAYLAND_DISPLAY"
timeout 60s env JALIUM_WINDOW_SYSTEM=wayland "$test_binary" --wayland-system-menu \
    | tee "$output/native.log"
timeout 60s env JALIUM_WINDOW_SYSTEM=wayland "$test_binary" --wayland-input-devices \
    | tee "$output/input-devices.log"
timeout 60s env JALIUM_WINDOW_SYSTEM=wayland "$payload/jalium.native.platform.tests" --wayland-smoke \
    | tee "$output/platform.log"
kill -0 "$compositor_pid"
printf 'Wayland window-menu input ownership passed. Logs: %s\n' "$output"

#!/usr/bin/env bash
set -euo pipefail
rid=${1:-linux-x64}
configuration=${2:-Release}
repo_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
payload=${JALIUM_NATIVE_OUTPUT_ROOT:-$repo_root/src/native/bin/native}/$rid/$configuration
output=${JALIUM_WINDOW_TEST_OUTPUT:-$(mktemp -d -t jalium-window-seat-globals.XXXXXX)}
mkdir -p "$output"
output=$(cd -- "$output" && pwd)
module=$payload/test-modules/jalium-seat-test.so
test -f "$module"
runtime=$(mktemp -d -t jalium-seat.XXXXXX)
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
export XDG_RUNTIME_DIR="$runtime" WAYLAND_DISPLAY=wayland-seat-globals
export JALIUM_TEST_SEAT_CONTROL="$runtime/control"
export LD_LIBRARY_PATH="$payload${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
weston --backend=headless-backend.so --use-pixman --socket="$WAYLAND_DISPLAY" \
    --width=1024 --height=768 --idle-time=0 --modules="$module" \
    --log="$output/weston.log" 2>"$output/seat-commands.log" &
compositor_pid=$!
for attempt in $(seq 1 100); do
    test -S "$runtime/$WAYLAND_DISPLAY" && test -S "$JALIUM_TEST_SEAT_CONTROL" && break
    kill -0 "$compositor_pid"
    sleep .05
done
test -S "$JALIUM_TEST_SEAT_CONTROL"
timeout 90s env WAYLAND_DEBUG=1 JALIUM_WINDOW_SYSTEM=wayland \
    "$payload/jalium.native.platform.window.tests" --wayland-seat-globals \
    >"$output/native.log" 2>"$output/wire.log"
cat "$output/native.log"
kill -0 "$compositor_pid"

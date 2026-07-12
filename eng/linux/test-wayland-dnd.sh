#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
rid="${1:-linux-x64}"
configuration="${2:-Release}"
native_dir="$root/src/native/bin/native/$rid/$configuration"
test_binary="$native_dir/jalium.native.platform.tests"

if [[ ! -x "$test_binary" ]]; then
    echo "Missing platform test binary: $test_binary" >&2
    exit 2
fi
command -v weston >/dev/null
command -v xdotool >/dev/null
: "${DISPLAY:?Run this script inside an X server (for CI: xvfb-run -a).}"

runtime_dir="$(mktemp -d /tmp/jalium-wayland-dnd.XXXXXX)"
chmod 700 "$runtime_dir"
weston_log="$runtime_dir/weston.log"
socket_name="jalium-wayland-dnd-$$"
weston_pid=""

cleanup() {
    if [[ -n "$weston_pid" ]] && kill -0 "$weston_pid" 2>/dev/null; then
        kill "$weston_pid" 2>/dev/null || true
        wait "$weston_pid" 2>/dev/null || true
    fi
    rm -rf "$runtime_dir"
}
trap cleanup EXIT

XDG_RUNTIME_DIR="$runtime_dir" weston \
    --backend=x11 \
    --renderer=pixman \
    --socket="$socket_name" \
    --width=800 \
    --height=600 \
    --idle-time=0 \
    --no-config \
    --log="$weston_log" &
weston_pid=$!

for _ in $(seq 1 200); do
    [[ -S "$runtime_dir/$socket_name" ]] && break
    if ! kill -0 "$weston_pid" 2>/dev/null; then
        cat "$weston_log" >&2
        exit 3
    fi
    sleep 0.02
done
if [[ ! -S "$runtime_dir/$socket_name" ]]; then
    cat "$weston_log" >&2
    echo "Weston did not create $socket_name" >&2
    exit 3
fi

(
    # Give the Jalium xdg-toplevel time to configure, then press inside its
    # centered 480x320 content, move while held, and release to self-drop.
    sleep 1
    weston_window="$(sed -n 's/.*x11 output .* window id \([0-9][0-9]*\).*/\1/p' "$weston_log" | tail -n 1)"
    if [[ -z "$weston_window" ]]; then
        weston_window="$(xdotool search --onlyvisible --name 'Weston' 2>/dev/null | tail -n 1)"
    fi
    if [[ -z "$weston_window" ]]; then
        weston_window="$(xdotool search --onlyvisible --class 'weston' 2>/dev/null | tail -n 1)"
    fi
    [[ -n "$weston_window" ]]
    echo "Injecting X11 input into Weston window $weston_window: $(xdotool getwindowname "$weston_window" 2>/dev/null || true)" >&2
    xdotool getwindowgeometry "$weston_window" >&2 || true
    # Desktop-shell placement varies with Weston versions and decoration
    # metrics. Probe a small center grid; as soon as one press lands in the
    # Jalium content the test starts its drag while that button is still held.
    for y in 160 240 320 400 480; do
        for x in 200 300 400 500 600; do
            xdotool mousemove --window "$weston_window" "$x" "$y"
            xdotool mousedown 1
            sleep 0.08
            xdotool mousemove --window "$weston_window" "$((x + 12))" "$((y + 8))"
            sleep 0.08
            xdotool mouseup 1
            sleep 0.08
        done
    done
) &
input_pid=$!

set +e
XDG_RUNTIME_DIR="$runtime_dir" \
WAYLAND_DISPLAY="$socket_name" \
JALIUM_WINDOW_SYSTEM=wayland \
LD_LIBRARY_PATH="$native_dir${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}" \
timeout 15s "$test_binary" --wayland-dnd-smoke
status=$?
set -e
wait "$input_pid" || true
if [[ $status -ne 0 ]]; then
    cat "$weston_log" >&2
    exit "$status"
fi

#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
rid="${1:-linux-x64}"
configuration="${2:-Release}"

case "$rid" in
    linux-x64|linux-arm64)
        ;;
    *)
        echo "Unsupported clipboard smoke RID '$rid'. Expected linux-x64 or linux-arm64." >&2
        exit 2
        ;;
esac

native_dir="${JALIUM_NATIVE_DIR:-$repo_root/src/native/bin/native/$rid/$configuration}"
test_binary="$native_dir/jalium.native.platform.tests"

if [[ ! -x "$test_binary" ]]; then
    echo "Missing $test_binary; build the $rid native target first." >&2
    exit 2
fi
for command in xclip wl-copy wl-paste weston xdotool xwininfo; do
    command -v "$command" >/dev/null || {
        echo "Missing smoke-test dependency: $command" >&2
        exit 2
    }
done

export DISPLAY="${DISPLAY:-:0}"
export LD_LIBRARY_PATH="$native_dir${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"

# A real X11 clipboard manager/client owns and requests the selection.
(printf 'external-中文-😀' | xclip -selection clipboard >/dev/null 2>&1) &
sleep 0.1
JALIUM_WINDOW_SYSTEM=x11 "$test_binary" --x11-clipboard-tool-smoke

# WSLg only sends wl_data_device selection events to the keyboard-focused
# client, and focus is controlled by the Windows RAIL host. Run a nested Weston
# X11 compositor so xdotool can deterministically focus its real input seat;
# the Jalium child still uses native Wayland and real wl-copy/wl-paste peers.
runtime_dir="$(mktemp -d)"
socket_name="jalium-wayland-$PPID-$$"
weston_log="$runtime_dir/weston.log"
weston_pid=""
cleanup() {
    if [[ -n "$weston_pid" ]]; then
        kill "$weston_pid" 2>/dev/null || true
        wait "$weston_pid" 2>/dev/null || true
    fi
    case "$runtime_dir" in
        /tmp/tmp.*|"${RUNNER_TEMP:-/nonexistent}"/tmp.*)
            rm -rf -- "$runtime_dir"
            ;;
    esac
}
trap cleanup EXIT

chmod 700 "$runtime_dir"
XDG_RUNTIME_DIR="$runtime_dir" weston \
    --backend=x11-backend.so \
    --socket="$socket_name" \
    --width=800 --height=600 --idle-time=0 \
    >"$weston_log" 2>&1 &
weston_pid=$!

for _ in $(seq 1 100); do
    [[ -S "$runtime_dir/$socket_name" ]] && break
    kill -0 "$weston_pid" 2>/dev/null || {
        cat "$weston_log" >&2
        exit 1
    }
    sleep 0.05
done
[[ -S "$runtime_dir/$socket_name" ]] || {
    cat "$weston_log" >&2
    echo "Nested Weston socket was not created." >&2
    exit 1
}

XDG_RUNTIME_DIR="$runtime_dir" WAYLAND_DISPLAY="$socket_name" \
    wl-copy 'external-中文-😀'

XDG_RUNTIME_DIR="$runtime_dir" WAYLAND_DISPLAY="$socket_name" \
    JALIUM_WINDOW_SYSTEM=wayland \
    "$test_binary" --wayland-clipboard-tool-smoke &
smoke_pid=$!

# The X11 backend names its output window "Weston Compositor - screen0".
# Direct focus/click avoids depending on a window manager in WSLg's X server.
for _ in $(seq 1 60); do
    weston_window="$(xwininfo -root -tree 2>/dev/null | \
        awk '/"Weston Compositor - screen0"/ { print $1; exit }')"
    if [[ -n "$weston_window" ]]; then
        xdotool windowfocus "$weston_window" \
            mousemove --window "$weston_window" 400 300 click 1 key a || true
    fi
    kill -0 "$smoke_pid" 2>/dev/null || break
    sleep 0.1
done
if [[ -z "${weston_window:-}" ]]; then
    DISPLAY="$DISPLAY" xwininfo -root -tree >&2 || true
    echo "Could not find nested Weston's X11 output window." >&2
    kill "$smoke_pid" 2>/dev/null || true
    wait "$smoke_pid" 2>/dev/null || true
    exit 1
fi

wait "$smoke_pid"

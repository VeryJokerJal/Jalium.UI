#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
host="${JALIUM_WINDOW_TEST_HOST:-$repo_root/tests/Jalium.UI.Linux.HostSmoke/bin/Release/net10.0/Jalium.UI.Linux.HostSmoke.dll}"
dotnet_bin="${JALIUM_DOTNET_BIN:-dotnet}"
output="${JALIUM_WINDOW_TEST_OUTPUT:-$(mktemp -d -t jalium-keyboard-tests.XXXXXX)}"
payload="${JALIUM_NATIVE_OUTPUT_ROOT:-$repo_root/src/native/bin/native}/${JALIUM_NATIVE_RID:-linux-x64}/${JALIUM_NATIVE_CONFIGURATION:-Release}"
mkdir -p "$output"
export JALIUM_WINDOW_TEST_HOST="$host" JALIUM_DOTNET_BIN="$dotnet_bin" JALIUM_WINDOW_TEST_OUTPUT="$output"
export LD_LIBRARY_PATH="$payload${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"

if [[ "${1:-}" != --inside-xvfb ]]; then
    test -f "$host"
    command -v "$dotnet_bin" >/dev/null
    for tool in xvfb-run openbox weston xdotool; do command -v "$tool" >/dev/null; done
    for mode in x11 wayland; do
        JALIUM_WINDOW_TEST_MODE="$mode" xvfb-run -a -s "-screen 0 1280x900x24" \
            bash "$0" --inside-xvfb
    done
    printf 'X11 and Wayland keyboard input passed. Logs: %s\n' "$output"
    exit
fi

mode="$JALIUM_WINDOW_TEST_MODE"
runtime=""
server_pid=""
app_pid=""
cleanup() {
    for pid in "$app_pid" "$server_pid"; do
        if [[ -n "$pid" ]]; then kill "$pid" 2>/dev/null || true; wait "$pid" 2>/dev/null || true; fi
    done
    if [[ -n "$runtime" ]]; then rm -rf -- "$runtime"; fi
}
trap cleanup EXIT
if [[ "$mode" == x11 ]]; then
    openbox > "$output/keyboard-openbox.log" 2>&1 &
    server_pid=$!
else
    runtime=$(mktemp -d -t jalium-keyboard-wayland.XXXXXX)
    chmod 700 "$runtime"
    export XDG_RUNTIME_DIR="$runtime" WAYLAND_DISPLAY=wayland-keyboard
    weston --backend=x11-backend.so --use-pixman --width=1200 --height=820 \
        --idle-time=0 --socket="$WAYLAND_DISPLAY" --log="$output/keyboard-weston.log" &
    server_pid=$!
    for attempt in $(seq 1 100); do
        test -S "$runtime/$WAYLAND_DISPLAY" && break
        kill -0 "$server_pid" 2>/dev/null || exit 1
        sleep 0.05
    done
    test -S "$runtime/$WAYLAND_DISPLAY"
fi

JALIUM_WINDOW_SYSTEM="$mode" JALIUM_RENDER_BACKEND=software \
    "$dotnet_bin" "$host" --keyboard-smoke > "$output/keyboard-$mode.log" 2>&1 &
app_pid=$!
for attempt in $(seq 1 200); do
    grep -q 'keyboard ready' "$output/keyboard-$mode.log" && break
    kill -0 "$app_pid" 2>/dev/null || { cat "$output/keyboard-$mode.log"; exit 1; }
    sleep 0.05
done
grep -q 'keyboard ready' "$output/keyboard-$mode.log"
if [[ "$mode" == x11 ]]; then
    window_id=$(xdotool search --name 'Jalium.*Linux Window' | head -n 1)
    xdotool windowactivate --sync "$window_id"
fi
sleep 0.2
xdotool key --clearmodifiers Tab
sleep 0.15
xdotool key --clearmodifiers Shift+Tab
sleep 0.15
xdotool type --clearmodifiers --delay 30 _qa
for attempt in $(seq 1 200); do
    kill -0 "$app_pid" 2>/dev/null || break
    sleep 0.05
done
if kill -0 "$app_pid" 2>/dev/null; then cat "$output/keyboard-$mode.log"; exit 1; fi
wait "$app_pid"
app_pid=""
grep -q 'finished keyboard failures=0' "$output/keyboard-$mode.log"
cat "$output/keyboard-$mode.log"

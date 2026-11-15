#!/usr/bin/env bash
set -euo pipefail

rid="${1:-linux-x64}"
configuration="${2:-Release}"
repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
payload="${JALIUM_NATIVE_OUTPUT_ROOT:-$repo_root/src/native/bin/native}/$rid/$configuration"
test_binary="$payload/jalium.native.platform.window.tests"
test_output="${JALIUM_WINDOW_TEST_OUTPUT:-$(mktemp -d -t jalium-window-tests.XXXXXX)}"
mkdir -p "$test_output"
command -v xvfb-run >/dev/null
command -v openbox >/dev/null
command -v weston >/dev/null
command -v xprop >/dev/null
test -x "$test_binary"
if [[ -n "${JALIUM_WINDOW_TEST_HOST:-}" ]]; then
    command -v dotnet >/dev/null
    test -f "$JALIUM_WINDOW_TEST_HOST"
fi
export LD_LIBRARY_PATH="$payload${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
export JALIUM_WINDOW_TEST_BINARY="$test_binary"
export JALIUM_WINDOW_TEST_OUTPUT="$test_output"
export JALIUM_WINDOW_TEST_HOST="${JALIUM_WINDOW_TEST_HOST:-}"

# An X server without a WM cannot acknowledge EWMH state or moveresize requests.
# Keep this integration distinct from the platform's no-WM contract CTest.
xvfb-run -a -s "-screen 0 1280x800x24" bash -euo pipefail -c '
  openbox > "$JALIUM_WINDOW_TEST_OUTPUT/openbox.log" 2>&1 &
  wm_pid=$!
  trap '\''kill "$wm_pid" 2>/dev/null || true'\'' EXIT
  for attempt in $(seq 1 100); do
    xprop -root _NET_SUPPORTING_WM_CHECK 2>/dev/null | grep -q "window id" && break
    sleep 0.05
  done
  xprop -root _NET_SUPPORTING_WM_CHECK | grep -q "window id"
  timeout 45s env JALIUM_WINDOW_SYSTEM=x11 "$JALIUM_WINDOW_TEST_BINARY" --x11-wm
  if [[ -n "$JALIUM_WINDOW_TEST_HOST" ]]; then
    timeout 45s env JALIUM_WINDOW_SYSTEM=x11 JALIUM_RENDER_BACKEND=software \
      dotnet "$JALIUM_WINDOW_TEST_HOST" --automated
    timeout 45s env JALIUM_WINDOW_SYSTEM=x11 JALIUM_RENDER_BACKEND=software \
      dotnet "$JALIUM_WINDOW_TEST_HOST" --automated --native-titlebar
  fi
'

runtime="$(mktemp -d -t jalium-wayland-window.XXXXXX)"
chmod 700 "$runtime"
weston_pid=""
cleanup() {
    if [[ -n "$weston_pid" ]]; then kill "$weston_pid" 2>/dev/null || true; wait "$weston_pid" 2>/dev/null || true; fi
    rm -rf -- "$runtime"
}
trap cleanup EXIT
XDG_RUNTIME_DIR="$runtime" weston --backend=headless-backend.so --socket=wayland-window \
    --width=1280 --height=800 --idle-time=0 --log="$test_output/weston.log" &
weston_pid=$!
for attempt in $(seq 1 100); do
    test -S "$runtime/wayland-window" && break
    kill -0 "$weston_pid" 2>/dev/null || { cat "$test_output/weston.log"; exit 1; }
    sleep 0.05
done
test -S "$runtime/wayland-window"
timeout 45s env XDG_RUNTIME_DIR="$runtime" WAYLAND_DISPLAY=wayland-window \
    JALIUM_WINDOW_SYSTEM=wayland "$test_binary" --wayland
if [[ -n "$JALIUM_WINDOW_TEST_HOST" ]]; then
    timeout 45s env XDG_RUNTIME_DIR="$runtime" WAYLAND_DISPLAY=wayland-window \
        JALIUM_WINDOW_SYSTEM=wayland JALIUM_RENDER_BACKEND=software \
        dotnet "$JALIUM_WINDOW_TEST_HOST" --automated
    timeout 45s env XDG_RUNTIME_DIR="$runtime" WAYLAND_DISPLAY=wayland-window \
        JALIUM_WINDOW_SYSTEM=wayland JALIUM_RENDER_BACKEND=software \
        dotnet "$JALIUM_WINDOW_TEST_HOST" --automated --native-titlebar
fi

fractional_module="$payload/test-modules/jalium-fractional-scale-test.so"
if [[ -f "$fractional_module" ]]; then
    kill "$weston_pid" 2>/dev/null || true
    wait "$weston_pid" 2>/dev/null || true
    for scale120 in 150 180 210; do
        socket="wayland-fractional-$scale120"
        XDG_RUNTIME_DIR="$runtime" JALIUM_TEST_FRACTIONAL_SCALE="$scale120" weston \
            --backend=headless-backend.so --socket="$socket" --width=1280 --height=800 \
            --idle-time=0 --modules="$fractional_module" --log="$test_output/weston-$scale120.log" &
        weston_pid=$!
        for attempt in $(seq 1 100); do
            test -S "$runtime/$socket" && break
            kill -0 "$weston_pid" 2>/dev/null || { cat "$test_output/weston-$scale120.log"; exit 1; }
            sleep 0.05
        done
        test -S "$runtime/$socket"
        timeout 45s env XDG_RUNTIME_DIR="$runtime" WAYLAND_DISPLAY="$socket" \
            JALIUM_WINDOW_SYSTEM=wayland JALIUM_TEST_FRACTIONAL_SCALE="$scale120" \
            "$test_binary" --wayland
        if [[ -n "$JALIUM_WINDOW_TEST_HOST" ]]; then
            timeout 45s env XDG_RUNTIME_DIR="$runtime" WAYLAND_DISPLAY="$socket" \
                JALIUM_WINDOW_SYSTEM=wayland JALIUM_RENDER_BACKEND=software \
                JALIUM_TEST_FRACTIONAL_SCALE="$scale120" \
                dotnet "$JALIUM_WINDOW_TEST_HOST" --automated --native-titlebar
        fi
        kill "$weston_pid" 2>/dev/null || true
        wait "$weston_pid" 2>/dev/null || true
        weston_pid=""
    done
else
    echo "Fractional compositor integration unavailable: build tests with libweston development files."
fi

JALIUM_WINDOW_TEST_OUTPUT="$test_output/menu-input" \
    bash "$repo_root/eng/linux/test-window-menu-input.sh" "$rid" "$configuration"

if [[ -f "$payload/test-modules/jalium-seat-test.so" ]]; then
    JALIUM_WINDOW_TEST_OUTPUT="$test_output/seat-globals" \
        bash "$repo_root/eng/linux/test-window-seat-globals.sh" "$rid" "$configuration"
else
    echo "Seat-global compositor integration unavailable: this fixture requires libweston 8."
fi

JALIUM_WINDOW_TEST_OUTPUT="$test_output/callback-lifetime" \
    bash "$repo_root/eng/linux/test-window-input-lifetime.sh" "$rid" "$configuration"

echo "X11/Openbox and Wayland/Weston Window behavior passed. Logs: $test_output"

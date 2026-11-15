#!/usr/bin/env bash
set -euo pipefail
rid=${1:-linux-x64}
configuration=${2:-Release}
repo_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
payload=${JALIUM_NATIVE_OUTPUT_ROOT:-$repo_root/src/native/bin/native}/$rid/$configuration
output=${JALIUM_WINDOW_TEST_OUTPUT:-$(mktemp -d -t jalium-input-lifetime.XXXXXX)}
mkdir -p "$output"
output=$(cd -- "$output" && pwd)
test_binary=$payload/jalium.native.platform.input-lifetime.tests
test -x "$test_binary"
if [[ ${3:-} != --inside-xvfb ]]; then
    exec xvfb-run -a -s "-screen 0 1280x800x24" bash "$0" "$rid" "$configuration" --inside-xvfb
fi
runtime=$(mktemp -d -t jalium-lifetime.XXXXXX)
chmod 700 "$runtime"
compositor_pid=""
cleanup() {
    if [[ -n "$compositor_pid" ]]; then kill "$compositor_pid" 2>/dev/null || true; wait "$compositor_pid" 2>/dev/null || true; fi
    rm -rf -- "$runtime"
}
trap cleanup EXIT
export XDG_RUNTIME_DIR="$runtime" WAYLAND_DISPLAY=wayland-input-lifetime
export LD_LIBRARY_PATH="$payload${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
weston --backend=x11-backend.so --use-pixman --socket="$WAYLAND_DISPLAY" \
    --width=1200 --height=760 --idle-time=0 --log="$output/weston.log" &
compositor_pid=$!
for attempt in $(seq 1 100); do
    test -S "$runtime/$WAYLAND_DISPLAY" && break
    kill -0 "$compositor_pid"
    sleep .05
done
test -S "$runtime/$WAYLAND_DISPLAY"
status=0
for mode in preedit-switch preedit-hide preedit-close preedit-reenter-same commit-reenter leave-reenter \
    touch-destroy-reenter touch-hide-close touch-cancel-replace; do
    result=0
    timeout 15s env JALIUM_WINDOW_SYSTEM=wayland WAYLAND_DEBUG=client "$test_binary" "$mode" \
        >"$output/$mode.log" 2>"$output/$mode-wire.log" || result=$?
    printf '%s exit=%s\n' "$mode" "$result" | tee -a "$output/results.log"
    if [[ "$result" != 0 ]]; then status=1; fi
done
kill -0 "$compositor_pid"
exit "$status"

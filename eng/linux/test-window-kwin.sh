#!/usr/bin/env bash
set -euo pipefail

rid="${1:-linux-x64}"
configuration="${2:-Release}"
repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
payload="${JALIUM_NATIVE_OUTPUT_ROOT:-$repo_root/src/native/bin/native}/$rid/$configuration"
test_binary="$payload/jalium.native.platform.window.tests"
output="${JALIUM_WINDOW_TEST_OUTPUT:-$(mktemp -d -t jalium-kwin-tests.XXXXXX)}"
host="${JALIUM_WINDOW_TEST_HOST:-}"
dotnet_bin="${JALIUM_DOTNET_BIN:-dotnet}"
kwin_bin="$(command -v "${JALIUM_KWIN_BIN:-kwin_wayland}")"
mkdir -p "$output"
output="$(cd -- "$output" && pwd)"
export JALIUM_WINDOW_TEST_OUTPUT="$output"
test -x "$test_binary"
command -v timeout >/dev/null
if [[ -n "$host" ]]; then
    test -f "$host"
    command -v "$dotnet_bin" >/dev/null
fi

# KWin and its activated services must not join a user's desktop D-Bus session.
if [[ "${3:-}" != --inside-session ]]; then
    command -v dbus-run-session >/dev/null
    exec dbus-run-session -- bash "$0" "$rid" "$configuration" --inside-session
fi

runtime="$(mktemp -d -t jalium-kwin-window.XXXXXX)"
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

# The packaged binary can have CAP_SYS_NICE, unavailable in a test container.
# Omit xattrs, keep the basename required by KWin's QPA plugin, and give every
# run an immutable copy: replacing a shared executable crashes live emulated
# instances when another test starts.
mkdir -p "$runtime/bin"
cp --preserve=mode,timestamps -- "$kwin_bin" "$runtime/bin/kwin_wayland"
export XDG_RUNTIME_DIR="$runtime" WAYLAND_DISPLAY=kwin-window
export LIBGL_ALWAYS_SOFTWARE=1 QT_FORCE_STDERR_LOGGING=1
export LD_LIBRARY_PATH="$payload${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
printf 'KWin executable: %s\n' "$runtime/bin/kwin_wayland"
"$runtime/bin/kwin_wayland" --virtual --width=1280 --height=800 \
    --socket="$WAYLAND_DISPLAY" --no-lockscreen --no-kactivities \
    > "$output/kwin.log" 2>&1 &
compositor_pid=$!

check_compositor() {
    if kill -0 "$compositor_pid" 2>/dev/null; then return; fi
    local result=1
    if wait "$compositor_pid"; then :; else result=$?; fi
    printf 'KWin exited before verification completed (status %s).\n' "$result" >&2
    tail -n 35 "$output/kwin.log" >&2
    compositor_pid=""
    return "$result"
}
for attempt in $(seq 1 200); do
    check_compositor
    test -S "$runtime/$WAYLAND_DISPLAY" && break
    sleep 0.05
done
test -S "$runtime/$WAYLAND_DISPLAY"

run_case() {
    local name="$1"
    shift
    if timeout 60s env JALIUM_WINDOW_SYSTEM=wayland WAYLAND_DEBUG=client \
        "$@" > "$output/$name.log" 2> "$output/$name-wire.log"; then
        check_compositor
    else
        local result=$?
        tail -n 35 "$output/$name.log" "$output/$name-wire.log" >&2
        return "$result"
    fi
    tail -n 2 "$output/$name.log"
}
run_case native "$test_binary" --wayland
if [[ -n "$host" ]]; then
    run_case host-custom env JALIUM_RENDER_BACKEND=software \
        "$dotnet_bin" "$host" --automated
    grep -q 'finished failures=0' "$output/host-custom.log"
    run_case host-native env JALIUM_RENDER_BACKEND=software \
        "$dotnet_bin" "$host" --automated --native-titlebar
    grep -q 'finished failures=0' "$output/host-native.log"
fi
check_compositor
printf 'KWin Window behavior passed; compositor remains alive. Logs: %s\n' "$output"

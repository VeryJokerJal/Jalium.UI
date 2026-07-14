#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 1 || $# -gt 2 ]]; then
  echo "Usage: bash eng/linux/test-status-notifier.sh <portal-smoke.dll> [native-payload]" >&2
  exit 2
fi

smoke_dll="$(realpath "$1")"
if [[ ! -f "$smoke_dll" ]]; then
  echo "Portal smoke assembly not found: $smoke_dll" >&2
  exit 2
fi

if [[ $# -eq 2 ]]; then
  native_payload="$(realpath "$2")"
  if [[ ! -d "$native_payload" ]]; then
    echo "Native payload directory not found: $native_payload" >&2
    exit 2
  fi
  export LD_LIBRARY_PATH="$native_payload${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
fi

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/../.." && pwd)"
source_file="$repo_root/tests/Jalium.UI.LinuxPortalSmoke/fake_status_notifier_watcher.c"
work_dir="$(mktemp -d -t jalium-status-notifier.XXXXXX)"

cleanup() {
  case "$work_dir" in
    /tmp/jalium-status-notifier.*|"${RUNNER_TEMP:-/nonexistent}"/jalium-status-notifier.*)
      rm -rf -- "$work_dir"
      ;;
  esac
}
trap cleanup EXIT

# pkg-config emits one compiler/linker argument per whitespace-delimited token.
# shellcheck disable=SC2046
cc -Wall -Wextra -Werror "$source_file" \
  -o "$work_dir/fake-status-notifier" \
  $(pkg-config --cflags --libs gio-2.0)

export JALIUM_SNI_BINARY="$work_dir/fake-status-notifier"
export JALIUM_SNI_SMOKE_DLL="$smoke_dll"
export JALIUM_SNI_FAKE_LOG="$work_dir/fake.log"
export JALIUM_SNI_CLIENT_LOG="$work_dir/client.log"

# The quoted body is intentionally evaluated by the nested D-Bus session shell.
# shellcheck disable=SC2016
xvfb-run -a dbus-run-session -- bash -euo pipefail -c '
  "$JALIUM_SNI_BINARY" >"$JALIUM_SNI_FAKE_LOG" 2>&1 &
  fake_pid=$!
  client_pid=""
  trap '\''
    status=$?
    if [[ -n "$client_pid" ]]; then kill "$client_pid" 2>/dev/null || true; fi
    kill "$fake_pid" 2>/dev/null || true
    if [[ $status -ne 0 ]]; then
      echo "StatusNotifier smoke client log:" >&2
      cat "$JALIUM_SNI_CLIENT_LOG" >&2 2>/dev/null || true
      echo "StatusNotifier fake desktop log:" >&2
      cat "$JALIUM_SNI_FAKE_LOG" >&2 2>/dev/null || true
    fi
    exit "$status"
  '\'' EXIT

  for _ in $(seq 1 100); do
    grep -q FAKE_DESKTOP_PROTOCOLS_READY "$JALIUM_SNI_FAKE_LOG" 2>/dev/null && break
    sleep 0.02
  done
  grep -q FAKE_DESKTOP_PROTOCOLS_READY "$JALIUM_SNI_FAKE_LOG"

  timeout 20s env JALIUM_WINDOW_SYSTEM=x11 \
    dotnet "$JALIUM_SNI_SMOKE_DLL" --status-notifier-smoke \
    >"$JALIUM_SNI_CLIENT_LOG" 2>&1 &
  client_pid=$!

  for _ in $(seq 1 200); do
    if grep -q "FAKE_SNI_REGISTER freedesktop " "$JALIUM_SNI_FAKE_LOG" 2>/dev/null &&
       grep -q "FAKE_SNI_REGISTER kde " "$JALIUM_SNI_FAKE_LOG" 2>/dev/null; then
      break
    fi
    sleep 0.02
  done
  grep -q "FAKE_SNI_REGISTER freedesktop " "$JALIUM_SNI_FAKE_LOG"
  grep -q "FAKE_SNI_REGISTER kde " "$JALIUM_SNI_FAKE_LOG"

  service="$(awk '\''$1 == "FAKE_SNI_REGISTER" && $2 == "freedesktop" { print $3; exit }'\'' "$JALIUM_SNI_FAKE_LOG")"
  test -n "$service"
  interface=org.freedesktop.StatusNotifierItem

  title="$(timeout 5s gdbus call --session --dest "$service" \
    --object-path /StatusNotifierItem \
    --method org.freedesktop.DBus.Properties.Get "$interface" Title)"
  status="$(timeout 5s gdbus call --session --dest "$service" \
    --object-path /StatusNotifierItem \
    --method org.freedesktop.DBus.Properties.Get "$interface" Status)"
  icon="$(timeout 5s gdbus call --session --dest "$service" \
    --object-path /StatusNotifierItem \
    --method org.freedesktop.DBus.Properties.Get "$interface" IconPixmap)"
  tooltip="$(timeout 5s gdbus call --session --dest "$service" \
    --object-path /StatusNotifierItem \
    --method org.freedesktop.DBus.Properties.Get "$interface" ToolTip)"
  item_is_menu="$(timeout 5s gdbus call --session --dest "$service" \
    --object-path /StatusNotifierItem \
    --method org.freedesktop.DBus.Properties.Get "$interface" ItemIsMenu)"
  menu="$(timeout 5s gdbus call --session --dest "$service" \
    --object-path /StatusNotifierItem \
    --method org.freedesktop.DBus.Properties.Get "$interface" Menu)"

  [[ "$title" == *"Jalium SNI smoke"* ]]
  [[ "$status" == *"Active"* ]]
  [[ "$icon" == *"(2, 1,"* ]]
  [[ "$icon" == *"0xff"*"0x30"*"0x20"*"0x10"* ]]
  [[ "$tooltip" == *"Jalium SNI smoke"* ]]
  [[ "$item_is_menu" == *"false"* ]]
  [[ "$menu" == *"/NO_DBUSMENU"* ]]

  timeout 5s gdbus call --session --dest "$service" \
    --object-path /StatusNotifierItem --method "$interface.Activate" 11 22
  timeout 5s gdbus call --session --dest "$service" \
    --object-path /StatusNotifierItem --method "$interface.ContextMenu" 33 44
  timeout 5s gdbus call --session --dest "$service" \
    --object-path /StatusNotifierItem --method "$interface.SecondaryActivate" 55 66
  timeout 5s gdbus call --session --dest "$service" \
    --object-path /StatusNotifierItem --method "$interface.Scroll" 120 vertical

  wait "$client_pid"
  client_pid=""
  cat "$JALIUM_SNI_CLIENT_LOG"
  grep -q STATUS_NOTIFIER_CLIENT_OK "$JALIUM_SNI_CLIENT_LOG"
  grep -q "FAKE_NOTIFICATION_NOTIFY .*action=default" "$JALIUM_SNI_FAKE_LOG"
  grep -q "FAKE_NOTIFICATION_ACTION .*action=default" "$JALIUM_SNI_FAKE_LOG"
  grep -q FAKE_NOTIFICATION_CLOSED "$JALIUM_SNI_FAKE_LOG"
'

echo "StatusNotifierItem properties/method callbacks and notification action smoke passed."

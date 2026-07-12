#!/usr/bin/env bash
set -euo pipefail

mode=jit
runner="${1:-/tmp/codex-dotnet10/dotnet}"
app_dll="${2:-}"
native_dir="${3:-}"
if [[ "$runner" == "--native" ]]; then
  mode=native
  runner="${2:-}"
  app_dll=""
  native_dir="${3:-}"
  if [[ -z "$runner" || ! -x "$runner" ]]; then
    echo "usage: $0 --native <Jalium.UI.LinuxAccessibilitySmoke> [native-library-dir]" >&2
    exit 2
  fi
elif [[ -z "$app_dll" || ! -f "$app_dll" ]]; then
  echo "usage: $0 [dotnet] <Jalium.UI.LinuxAccessibilitySmoke.dll> [native-library-dir]" >&2
  exit 2
fi

for command in dbus-run-session gdbus grep sed; do
  if ! command -v "$command" >/dev/null 2>&1; then
    echo "Missing AT-SPI smoke dependency: $command" >&2
    exit 3
  fi
done

dbus-run-session -- bash -s -- "$mode" "$runner" "$app_dll" "$native_dir" <<'INNER'
set -euo pipefail

mode="$1"
runner="$2"
app_dll="$3"
native_dir="$4"
work_dir="$(mktemp -d)"
app_log="$work_dir/app.log"
monitor_log="$work_dir/monitor.log"
tree_log="$work_dir/tree.log"
action_marker="$work_dir/action.marker"
app_pid=""
monitor_pid=""

cleanup() {
  if [[ -n "$monitor_pid" ]]; then kill "$monitor_pid" 2>/dev/null || true; fi
  if [[ -n "$app_pid" ]]; then kill "$app_pid" 2>/dev/null || true; fi
  wait "$monitor_pid" 2>/dev/null || true
  wait "$app_pid" 2>/dev/null || true
  rm -rf "$work_dir"
}
trap cleanup EXIT

address_reply="$(gdbus call --session \
  --dest org.a11y.Bus \
  --object-path /org/a11y/bus \
  --method org.a11y.Bus.GetAddress)"
a11y_address="$(printf '%s\n' "$address_reply" | sed -n "s/^('\\(.*\\)',)$/\\1/p")"
if [[ -z "$a11y_address" ]]; then
  echo "org.a11y.Bus returned no accessibility bus address: $address_reply" >&2
  exit 4
fi

export JALIUM_ATSPI_TRACE=1
export JALIUM_ATSPI_SMOKE_SECONDS=45
export JALIUM_ATSPI_ACTION_MARKER="$action_marker"
export JALIUM_RENDER_BACKEND="${JALIUM_RENDER_BACKEND:-Software}"
export JALIUM_WINDOW_SYSTEM="${JALIUM_WINDOW_SYSTEM:-x11}"
if [[ -n "$native_dir" ]]; then
  export LD_LIBRARY_PATH="$native_dir${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
fi

if [[ "$mode" == native ]]; then
  "$runner" >"$app_log" 2>&1 &
else
  "$runner" "$app_dll" >"$app_log" 2>&1 &
fi
app_pid=$!

app_bus=""
for _ in $(seq 1 150); do
  if ! kill -0 "$app_pid" 2>/dev/null; then
    echo "Accessibility smoke app exited before registration:" >&2
    cat "$app_log" >&2
    exit 5
  fi

  children="$(gdbus call --address "$a11y_address" \
    --dest org.a11y.atspi.Registry \
    --object-path /org/a11y/atspi/accessible/root \
    --method org.a11y.atspi.Accessible.GetChildren 2>/dev/null || true)"
  while IFS= read -r reference; do
    candidate_bus="$(printf '%s' "$reference" | cut -d"'" -f2)"
    candidate_path="$(printf '%s' "$reference" | cut -d"'" -f4)"
    name="$(gdbus call --address "$a11y_address" \
      --dest "$candidate_bus" \
      --object-path "$candidate_path" \
      --method org.freedesktop.DBus.Properties.Get \
      org.a11y.atspi.Accessible Name 2>/dev/null || true)"
    if [[ "$name" == *"Jalium.UI.LinuxAccessibilitySmoke"* ]]; then
      app_bus="$candidate_bus"
      break
    fi
  done < <(printf '%s\n' "$children" | grep -o "('[^']*', objectpath '[^']*')" || true)

  [[ -n "$app_bus" ]] && break
  sleep 0.1
done

if [[ -z "$app_bus" ]]; then
  echo "Jalium application root did not appear in the AT-SPI registry:" >&2
  cat "$app_log" >&2
  exit 6
fi

root_path=/org/a11y/atspi/accessible/root
root_role="$(gdbus call --address "$a11y_address" --dest "$app_bus" \
  --object-path "$root_path" --method org.a11y.atspi.Accessible.GetRole)"
[[ "$root_role" == *"75"* ]] || { echo "Unexpected application role: $root_role" >&2; exit 7; }

declare -a queue=("$root_path")
declare -A seen=()
button_path=""
editor_path=""
close_path=""
for _ in $(seq 1 120); do
  [[ ${#queue[@]} -gt 0 ]] || break
  path="${queue[0]}"
  queue=("${queue[@]:1}")
  [[ -z "${seen[$path]:-}" ]] || continue
  seen[$path]=1

  name="$(gdbus call --address "$a11y_address" --dest "$app_bus" \
    --object-path "$path" --method org.freedesktop.DBus.Properties.Get \
    org.a11y.atspi.Accessible Name 2>/dev/null || true)"
  [[ "$name" == *"Smoke Action"* ]] && button_path="$path"
  [[ "$name" == *"Smoke Editor"* ]] && editor_path="$path"
  [[ "$name" == *"Close Smoke Window"* ]] && close_path="$path"

  child_reply="$(gdbus call --address "$a11y_address" --dest "$app_bus" \
    --object-path "$path" --method org.a11y.atspi.Accessible.GetChildren 2>/dev/null || true)"
  printf 'path=%s name=%s children=%s\n' "$path" "$name" "$child_reply" >>"$tree_log"
  while IFS= read -r child_path; do
    [[ -n "$child_path" ]] && queue+=("$child_path")
  done < <(printf '%s\n' "$child_reply" | \
    grep -o "'/org/a11y/atspi/accessible/[0-9][0-9]*'" | tr -d "'" || true)

  [[ -n "$button_path" && -n "$editor_path" && -n "$close_path" ]] && break
done

if [[ -z "$button_path" || -z "$editor_path" || -z "$close_path" ]]; then
  echo "Could not enumerate the expected Button/TextBox/close action through Accessible.GetChildren." >&2
  cat "$tree_log" >&2
  cat "$app_log" >&2
  exit 8
fi

text="$(gdbus call --address "$a11y_address" --dest "$app_bus" \
  --object-path "$editor_path" --method org.a11y.atspi.Text.GetText -- 0 -1)"
[[ "$text" == *"Editable smoke text"* ]] || { echo "Unexpected Text.GetText response: $text" >&2; exit 9; }

extents="$(gdbus call --address "$a11y_address" --dest "$app_bus" \
  --object-path "$button_path" --method org.a11y.atspi.Component.GetExtents 0)"
[[ "$extents" == *"("* ]] || { echo "Component.GetExtents failed: $extents" >&2; exit 10; }

gdbus monitor --address "$a11y_address" --dest "$app_bus" >"$monitor_log" 2>&1 &
monitor_pid=$!
sleep 0.2

focus="$(gdbus call --address "$a11y_address" --dest "$app_bus" \
  --object-path "$button_path" --method org.a11y.atspi.Component.GrabFocus)"
[[ "$focus" == *"true"* ]] || { echo "Component.GrabFocus failed: $focus" >&2; cat "$app_log" >&2; exit 11; }

action="$(gdbus call --address "$a11y_address" --dest "$app_bus" \
  --object-path "$button_path" --method org.a11y.atspi.Action.DoAction 0)"
[[ "$action" == *"true"* ]] || { echo "Action.DoAction failed: $action" >&2; exit 12; }

for _ in $(seq 1 50); do
  [[ -f "$action_marker" ]] && \
    grep -q "PropertyChange" "$monitor_log" && \
    grep -q "ChildrenChanged" "$monitor_log" && \
    grep -Eq "StateChanged|Focus" "$monitor_log" && break
  sleep 0.1
done

[[ -f "$action_marker" ]] || { echo "The AT-SPI action did not reach the Jalium Button." >&2; exit 13; }
grep -q "PropertyChange" "$monitor_log" || { echo "Missing PropertyChange signal." >&2; cat "$monitor_log" >&2; exit 14; }
grep -q "ChildrenChanged" "$monitor_log" || { echo "Missing ChildrenChanged signal." >&2; cat "$monitor_log" >&2; exit 15; }
grep -Eq "StateChanged|Focus" "$monitor_log" || { echo "Missing focus signal." >&2; cat "$monitor_log" >&2; exit 16; }
grep -q "AT-SPI status=active" "$app_log" || { echo "Bridge diagnostics did not report active." >&2; cat "$app_log" >&2; exit 17; }

close_result="$(gdbus call --address "$a11y_address" --dest "$app_bus" \
  --object-path "$close_path" --method org.a11y.atspi.Action.DoAction 0)"
[[ "$close_result" == *"true"* ]] || { echo "Close Action.DoAction failed: $close_result" >&2; exit 18; }
for _ in $(seq 1 50); do
  ! kill -0 "$app_pid" 2>/dev/null && break
  sleep 0.1
done
if kill -0 "$app_pid" 2>/dev/null; then
  echo "The AT-SPI close action did not close the application." >&2
  exit 19
fi
wait "$app_pid"
app_pid=""
grep -q "Destroy" "$monitor_log" || { echo "Missing Window.Destroy signal." >&2; cat "$monitor_log" >&2; exit 20; }
grep -q "'remove'" "$monitor_log" || { echo "Missing root ChildrenChanged remove signal." >&2; cat "$monitor_log" >&2; exit 21; }

echo "AT-SPI smoke passed: application/window tree, role, Text, Component, Action, focus, property, children and window lifecycle events."
INNER

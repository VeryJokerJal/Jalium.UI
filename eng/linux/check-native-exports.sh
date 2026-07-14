#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 1 || $# -gt 2 ]]; then
  echo "Usage: bash eng/linux/check-native-exports.sh <payload-dir> [repo-root]" >&2
  exit 2
fi

for tool in python3 readelf c++filt; do
  if ! command -v "$tool" >/dev/null 2>&1; then
    echo "$tool is required for native export validation." >&2
    exit 1
  fi
done

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="${2:-$(cd -- "$script_dir/../.." && pwd)}"
python3 "$script_dir/check-native-exports.py" "$1" "$repo_root"

# On glibc, -r resolves both relocations and dependencies. musl's ldd does not
# implement that option, so its CI path retains the normal ldd + scanelf gate.
if ldd --version 2>&1 | grep -Eqi 'glibc|GNU libc|GNU C Library'; then
  case "$(uname -m)" in
    x86_64|amd64) host_elf_machine='Advanced Micro Devices X86-64' ;;
    aarch64|arm64) host_elf_machine='AArch64' ;;
    *) host_elf_machine='' ;;
  esac
  resolved_count=0
  foreign_count=0
  while IFS= read -r -d '' library; do
    library_elf_machine="$(readelf -h "$library" | sed -n 's/^[[:space:]]*Machine:[[:space:]]*//p')"
    if readelf -d "$library" | grep -Eq 'NEEDED.*libc[.]musl-'; then
      library_libc=musl
    else
      library_libc=glibc
    fi
    if [[ -z "$host_elf_machine" || "$library_elf_machine" != "$host_elf_machine" ||
          "$library_libc" != glibc ]]; then
      echo "$(basename "$library"): foreign ELF ($library_elf_machine, $library_libc); host glibc ldd -r skipped"
      foreign_count=$((foreign_count + 1))
      continue
    fi
    linkage="$(LD_LIBRARY_PATH="$1${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}" ldd -r "$library" 2>&1)"
    if grep -Eq 'undefined symbol|not found' <<<"$linkage"; then
      printf '%s\n' "$linkage" >&2
      echo "Unresolved relocation/dependency in $library" >&2
      exit 1
    fi
    echo "$(basename "$library"): ldd -r resolved"
    resolved_count=$((resolved_count + 1))
  done < <(find "$1" -maxdepth 1 -type f -name '*.so' -print0 | sort -z)
  if [[ $foreign_count -eq 0 ]]; then
    echo "ldd -r resolved all $resolved_count host-architecture shared libraries in $1"
  else
    echo "ldd -r resolved $resolved_count host-architecture libraries; skipped $foreign_count foreign-architecture libraries after readelf/export validation."
  fi
fi

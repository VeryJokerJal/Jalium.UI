#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat >&2 <<'USAGE'
Usage: bash eng/linux/check-symbol-versions.sh <path> <max-glibc> <max-glibcxx>

Recursively checks every ELF file below <path> (or the single ELF at <path>).
Version arguments omit the symbol prefix, for example: 2.31 3.4.28.
USAGE
}

if [[ $# -ne 3 ]]; then
  usage
  exit 2
fi

target="$1"
max_glibc="$2"
max_glibcxx="$3"

if [[ ! -e "$target" ]]; then
  echo "Symbol-version check target does not exist: $target" >&2
  exit 1
fi
if ! command -v readelf >/dev/null 2>&1; then
  echo "readelf is required for the symbol-version check." >&2
  exit 1
fi

version_is_at_most() {
  local actual="$1"
  local maximum="$2"
  [[ "$(printf '%s\n%s\n' "$actual" "$maximum" | sort -V | tail -n 1)" == "$maximum" ]]
}

check_elf() {
  local file="$1"
  local version_info glibc glibcxx

  if ! readelf -h "$file" >/dev/null 2>&1; then
    return 0
  fi

  elf_count=$((elf_count + 1))
  version_info="$(readelf --version-info "$file" 2>/dev/null || true)"
  glibc="$(grep -oE 'GLIBC_[0-9]+(\.[0-9]+)+' <<<"$version_info" | sed 's/^GLIBC_//' | sort -Vu | tail -n 1 || true)"
  glibcxx="$(grep -oE 'GLIBCXX_[0-9]+(\.[0-9]+)+' <<<"$version_info" | sed 's/^GLIBCXX_//' | sort -Vu | tail -n 1 || true)"

  printf '%s: GLIBC_%s GLIBCXX_%s\n' \
    "$file" "${glibc:-none}" "${glibcxx:-none}"

  if [[ -n "$glibc" ]] && ! version_is_at_most "$glibc" "$max_glibc"; then
    echo "$file requires GLIBC_$glibc, above the allowed GLIBC_$max_glibc baseline." >&2
    failure=1
  fi
  if [[ -n "$glibcxx" ]] && ! version_is_at_most "$glibcxx" "$max_glibcxx"; then
    echo "$file requires GLIBCXX_$glibcxx, above the allowed GLIBCXX_$max_glibcxx baseline." >&2
    failure=1
  fi
}

export LC_ALL=C
elf_count=0
failure=0

if [[ -f "$target" ]]; then
  check_elf "$target"
else
  while IFS= read -r -d '' file; do
    check_elf "$file"
  done < <(find "$target" -type f -print0)
fi

if [[ $elf_count -eq 0 ]]; then
  echo "No ELF files found under $target" >&2
  exit 1
fi
if [[ $failure -ne 0 ]]; then
  exit 1
fi

echo "Checked $elf_count ELF file(s): maximum GLIBC_$max_glibc, GLIBCXX_$max_glibcxx."

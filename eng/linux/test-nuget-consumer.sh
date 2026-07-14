#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
rid="${1:?usage: $0 <rid> <package-directory> <output-root> [package-version] [self-contained|single-file|aot]}"
package_directory="$(cd "${2:?missing package directory}" && pwd)"
output_root="${3:?missing output root}"
package_version="${4:-$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' "$repo_root/Directory.Build.props" | head -1)}"
mode="${5:-self-contained}"
require_glibc_baseline="${JALIUM_REQUIRE_GLIBC_BASELINE:-0}"
glibc_symbol_ceiling="${JALIUM_GLIBC_SYMBOL_CEILING:-}"
glibcxx_symbol_ceiling="${JALIUM_GLIBCXX_SYMBOL_CEILING:-}"

[[ -n "$package_version" ]] || {
    echo "Could not determine the Jalium package version." >&2
    exit 2
}

case "$rid" in
    linux-x64|linux-arm64|linux-musl-x64|linux-musl-arm64)
        ;;
    *)
        echo "Unsupported Linux consumer RID '$rid'." >&2
        exit 2
        ;;
esac
case "$mode" in
    self-contained|single-file|aot)
        ;;
    *)
        echo "Unsupported consumer mode '$mode'. Expected self-contained, single-file, or aot." >&2
        exit 2
        ;;
esac

if [[ -n "$glibc_symbol_ceiling" || -n "$glibcxx_symbol_ceiling" ]]; then
    if [[ -z "$glibc_symbol_ceiling" || -z "$glibcxx_symbol_ceiling" ]]; then
        echo "Set both JALIUM_GLIBC_SYMBOL_CEILING and JALIUM_GLIBCXX_SYMBOL_CEILING." >&2
        exit 2
    fi
    if [[ "$rid" == linux-musl-* ]]; then
        echo "A glibc symbol ceiling cannot be applied to the musl RID '$rid'." >&2
        exit 2
    fi
fi

if [[ "$require_glibc_baseline" == 1 ]]; then
    if [[ "$rid" == linux-musl-* ]]; then
        echo "The Ubuntu glibc baseline cannot validate the musl RID '$rid'." >&2
        exit 2
    fi
    # shellcheck disable=SC1091
    source /etc/os-release
    if [[ "${ID:-}" != ubuntu || "${VERSION_ID:-}" != 20.04 ||
          "$(getconf GNU_LIBC_VERSION)" != "glibc 2.31" ]]; then
        echo "JALIUM_REQUIRE_GLIBC_BASELINE=1 requires Ubuntu 20.04 with glibc 2.31." >&2
        exit 3
    fi
fi

for command in dotnet find grep file ldd mktemp readelf timeout xvfb-run; do
    command -v "$command" >/dev/null || {
        echo "Missing NuGet consumer dependency: $command" >&2
        exit 3
    }
done

project="$repo_root/tests/Jalium.UI.NuGetTest.Linux/Jalium.UI.NuGetTest.Linux.csproj"
config_file="$repo_root/tests/Jalium.UI.NuGetTest.Linux/NuGet.config"
build_root="$output_root/build-$mode"
publish_dir="$output_root/publish-$mode"
nuget_packages="$output_root/nuget-$mode"
log_file="$output_root/run-$mode.log"
mkdir -p "$output_root" "$build_root" "$publish_dir" "$nuget_packages"

export JALIUM_NUGET_FEED="$package_directory"

common_properties=(
    -p:JaliumBuildRoot="$build_root"
    -p:JaliumPackageVersion="$package_version"
    -p:RuntimeIdentifier="$rid"
    -p:SelfContained=true
    -p:GeneratePackageOnBuild=false
)
if [[ "$mode" == aot ]]; then
    common_properties+=(
        -p:PublishAot=true
        -p:PublishTrimmed=true
    )
elif [[ "$mode" == single-file ]]; then
    common_properties+=(
        -p:PublishSingleFile=true
        -p:PublishTrimmed=true
        -p:IncludeNativeLibrariesForSelfExtract=true
        -p:DebugSymbols=false
        -p:DebugType=None
    )
fi

dotnet restore "$project" \
    --configfile "$config_file" \
    --packages "$nuget_packages" \
    "${common_properties[@]}"

dotnet build "$project" \
    -c Release \
    --no-restore \
    "${common_properties[@]}"

dotnet publish "$project" \
    -c Release \
    --no-restore \
    --output "$publish_dir" \
    "${common_properties[@]}"

required_libraries=(
    libjalium.native.core.so
    libjalium.native.platform.so
    libjalium.native.media.core.so
    libjalium.native.media.so
    libjalium.native.text.so
    libjalium.native.vulkan.so
    libjalium.native.software.so
)
for library in "${required_libraries[@]}"; do
    if [[ "$mode" == single-file ]]; then
        [[ ! -e "$publish_dir/$library" ]] || {
            echo "Single-file consumer unexpectedly retained sidecar $library for $rid." >&2
            exit 4
        }
    else
        [[ -f "$publish_dir/$library" ]] || {
            echo "Published consumer is missing $library for $rid." >&2
            exit 4
        }
    fi
done

executable="$publish_dir/Jalium.UI.NuGetTest.Linux"
[[ -x "$executable" ]] || {
    echo "Published consumer executable is missing: $executable" >&2
    exit 4
}
file_output="$(file "$executable")"
printf '%s\n' "$file_output"
[[ "$file_output" == *"ELF 64-bit"* ]] || {
    echo "Consumer output is not a 64-bit ELF executable." >&2
    exit 4
}
if [[ "$rid" == *-x64 ]]; then
    expected_file_architecture="x86-64"
    [[ "$file_output" == *"$expected_file_architecture"* ]] || {
        echo "Consumer output has the wrong architecture for $rid." >&2
        exit 4
    }
else
    expected_file_architecture="ARM aarch64"
    [[ "$file_output" == *"$expected_file_architecture"* ]] || {
        echo "Consumer output has the wrong architecture for $rid." >&2
        exit 4
    }
fi

program_headers="$(readelf -l "$executable")"
if [[ "$rid" == linux-musl-* ]]; then
    [[ "$program_headers" == *"ld-musl"* ]] || {
        echo "Consumer output does not use the musl ELF interpreter for $rid." >&2
        exit 4
    }
else
    [[ "$program_headers" != *"ld-musl"* ]] || {
        echo "glibc consumer unexpectedly uses the musl ELF interpreter." >&2
        exit 4
    }
fi

dependencies="$(ldd "$executable")"
printf '%s\n' "$dependencies"
[[ "$dependencies" != *"not found"* &&
   "$dependencies" != *"Error loading shared library"* &&
   "$dependencies" != *"Error relocating"* ]] || {
    echo "Consumer executable has unresolved ELF dependencies." >&2
    exit 4
}

case "$mode" in
    aot)
        [[ ! -f "$publish_dir/Jalium.UI.NuGetTest.Linux.dll" ]] || {
            echo "NativeAOT publish unexpectedly retained the managed entry assembly." >&2
            exit 4
        }
        ;;
    single-file)
        [[ ! -f "$publish_dir/Jalium.UI.NuGetTest.Linux.dll" ]] || {
            echo "Single-file publish unexpectedly retained the managed entry assembly." >&2
            exit 4
        }
        mapfile -t published_entries < <(
            find "$publish_dir" -mindepth 1 -maxdepth 1 -print
        )
        if [[ ${#published_entries[@]} -ne 1 ]]; then
            printf 'Single-file publish contains unexpected sidecars for %s:\n' "$rid" >&2
            printf '  %s\n' "${published_entries[@]}" >&2
            exit 4
        fi
        [[ "${published_entries[0]}" == "$executable" ]] || {
            echo "Single-file publish did not contain the expected executable: $executable" >&2
            exit 4
        }
        ;;
    self-contained)
        [[ -f "$publish_dir/Jalium.UI.NuGetTest.Linux.dll" ]] || {
            echo "Self-contained publish is missing its managed entry assembly." >&2
            exit 4
        }
        ;;
esac

if [[ -n "$glibc_symbol_ceiling" ]]; then
    bash "$repo_root/eng/linux/check-symbol-versions.sh" \
        "$publish_dir" "$glibc_symbol_ceiling" "$glibcxx_symbol_ceiling"
fi

runtime_environment=(
    DOTNET_MULTILEVEL_LOOKUP=0
    JALIUM_WINDOW_SYSTEM=x11
    JALIUM_RENDER_BACKEND=software
    JALIUM_NUGET_SMOKE_MS=750
)
bundle_extract_root=""
if [[ "$mode" == single-file ]]; then
    bundle_extract_root="$(mktemp -d "$output_root/bundle-extract-$mode.XXXXXX")"
    [[ -z "$(find "$bundle_extract_root" -mindepth 1 -print -quit)" ]] || {
        echo "Single-file bundle extraction root was not clean: $bundle_extract_root" >&2
        exit 4
    }
    runtime_environment+=(
        "DOTNET_BUNDLE_EXTRACT_BASE_DIR=$bundle_extract_root"
    )
fi

env "${runtime_environment[@]}" \
    timeout 30s xvfb-run -a "$executable" | tee "$log_file"

grep -Fq "[nuget-consumer] ready" "$log_file"
grep -Fq "[nuget-consumer] completed" "$log_file"
for library in "${required_libraries[@]}"; do
    grep -Fq "[nuget-consumer] loaded $library" "$log_file"
done

if [[ "$mode" == single-file ]]; then
    mapfile -t extracted_jalium_libraries < <(
        find "$bundle_extract_root" -type f -name 'libjalium.native*.so' -print
    )
    if [[ ${#extracted_jalium_libraries[@]} -ne ${#required_libraries[@]} ]]; then
        printf 'Expected exactly %d extracted Jalium native payloads, found %d under %s:\n' \
            "${#required_libraries[@]}" "${#extracted_jalium_libraries[@]}" "$bundle_extract_root" >&2
        printf '  %s\n' "${extracted_jalium_libraries[@]}" >&2
        exit 4
    fi

    extracted_payload_dir=""
    for library in "${required_libraries[@]}"; do
        mapfile -t matches < <(
            find "$bundle_extract_root" -type f -name "$library" -print
        )
        if [[ ${#matches[@]} -ne 1 ]]; then
            printf 'Expected one extracted %s, found %d under %s.\n' \
                "$library" "${#matches[@]}" "$bundle_extract_root" >&2
            exit 4
        fi

        extracted_library="${matches[0]}"
        [[ -s "$extracted_library" ]] || {
            echo "Extracted native payload is empty: $extracted_library" >&2
            exit 4
        }
        if [[ -z "$extracted_payload_dir" ]]; then
            extracted_payload_dir="$(dirname "$extracted_library")"
        elif [[ "$(dirname "$extracted_library")" != "$extracted_payload_dir" ]]; then
            echo "Extracted Jalium payloads do not share one bundle directory." >&2
            exit 4
        fi

        extracted_file_output="$(file "$extracted_library")"
        [[ "$extracted_file_output" == *"ELF 64-bit"* &&
           "$extracted_file_output" == *"$expected_file_architecture"* ]] || {
            echo "Extracted native payload has the wrong format for $rid: $extracted_file_output" >&2
            exit 4
        }
        extracted_dependencies="$(ldd "$extracted_library")"
        [[ "$extracted_dependencies" != *"not found"* &&
           "$extracted_dependencies" != *"Error loading shared library"* &&
           "$extracted_dependencies" != *"Error relocating"* ]] || {
            echo "Extracted native payload has unresolved dependencies: $extracted_library" >&2
            exit 4
        }
        grep -Fq "[nuget-consumer] loaded $library from $extracted_library" "$log_file" || {
            echo "Consumer did not load $library from its clean bundle extraction directory." >&2
            exit 4
        }
    done

    if [[ -n "$glibc_symbol_ceiling" ]]; then
        bash "$repo_root/eng/linux/check-symbol-versions.sh" \
            "$bundle_extract_root" "$glibc_symbol_ceiling" "$glibcxx_symbol_ceiling"
    fi
    echo "Single-file bundle extracted exactly seven Jalium native payloads to $extracted_payload_dir."
fi

echo "NuGet consumer $mode restore/build/publish/load/start smoke passed for $rid."

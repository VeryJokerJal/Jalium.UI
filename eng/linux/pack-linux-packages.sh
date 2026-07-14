#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
rid="${1:?usage: $0 <rid> [configuration] [package-output] [build-root]}"

# The stale-native pack guard (eng/msbuild/JaliumStaleNativeGuard.targets)
# shells out to git during `dotnet pack`. When this script runs as root in a
# container over a host-owned mount, git refuses the repository (dubious
# ownership); mark it safe in the container's own global config only.
if ! git -C "$repo_root" rev-parse HEAD >/dev/null 2>&1 && [[ "$(id -u)" == "0" ]]; then
    git config --global --add safe.directory "$repo_root" 2>/dev/null || true
fi
configuration="${2:-Release}"
package_output="${3:-$repo_root/artifacts/linux-packages/$rid}"
build_root="${4:-${TMPDIR:-/tmp}/jalium-linux-pack-$rid}"
native_output_root="${JALIUM_NATIVE_OUTPUT_ROOT:-$repo_root/src/native/bin/native}"
if [[ "$native_output_root" != /* ]]; then
    native_output_root="$repo_root/$native_output_root"
fi

case "$rid" in
    linux-x64|linux-arm64|linux-musl-x64|linux-musl-arm64|all)
        ;;
    *)
        echo "Unsupported Linux package RID '$rid'." >&2
        exit 2
        ;;
esac

required_libraries=(
    libjalium.native.core.so
    libjalium.native.platform.so
    libjalium.native.media.core.so
    libjalium.native.media.so
    libjalium.native.text.so
    libjalium.native.vulkan.so
    libjalium.native.software.so
)

if [[ "$rid" == all ]]; then
    package_rid=""
    payload_rids=(linux-x64 linux-arm64 linux-musl-x64 linux-musl-arm64)
else
    package_rid="$rid"
    payload_rids=("$rid")
fi

for payload_rid in "${payload_rids[@]}"; do
    native_dir="$native_output_root/$payload_rid/$configuration"
    [[ -f "$native_dir/.jalium-native-complete" ]] || {
        echo "Incomplete native payload: $native_dir/.jalium-native-complete is missing." >&2
        exit 3
    }
    for library in "${required_libraries[@]}"; do
        [[ -f "$native_dir/$library" ]] || {
            echo "Incomplete native payload: $native_dir/$library is missing." >&2
            exit 3
        }
    done
done

mkdir -p "$package_output" "$build_root"
package_output="$(cd "$package_output" && pwd)"
nuget_packages="$build_root/nuget-packages"
nuget_config="$build_root/NuGet.pack.config"
mkdir -p "$nuget_packages"

# NuGet does not define source ordering when the same ID/version exists in
# multiple feeds. Keep Jalium.* pinned to this invocation's isolated local
# feed so a previously published or globally cached package cannot make CI
# validate stale bits. All external dependencies used by these projects are
# Microsoft/System/runtime/NETStandard packages from nuget.org.
cat >"$nuget_config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="jalium-local" value="$package_output" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="jalium-local">
      <package pattern="Jalium.*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="Microsoft.*" />
      <package pattern="System.*" />
      <package pattern="runtime.*" />
      <package pattern="NETStandard.*" />
      <package pattern="QRCoder" />
    </packageSource>
  </packageSourceMapping>
</configuration>
EOF

projects=(
    src/managed/Jalium.UI.Build/Jalium.UI.Build.csproj
    src/managed/Jalium.UI.Xaml.SourceGenerator/Jalium.UI.Xaml.SourceGenerator.csproj
    src/managed/Jalium.UI.Managed/Jalium.UI.Managed.csproj
    src/managed/Jalium.UI.Core/Jalium.UI.Core.csproj
    src/managed/Jalium.UI.Media/Jalium.UI.Media.csproj
    src/managed/Jalium.UI.Input/Jalium.UI.Input.csproj
    src/managed/Jalium.UI.Interop/Jalium.UI.Interop.csproj
    src/managed/Jalium.UI.Controls/Jalium.UI.Controls.csproj
    src/managed/Jalium.UI.Gpu/Jalium.UI.Gpu.csproj
    src/managed/Jalium.UI.Xaml/Jalium.UI.Xaml.csproj
    src/packaging/Jalium.UI.Linux/Jalium.UI.Linux.csproj
)

for project in "${projects[@]}"; do
    # Restore each package in dependency order. In particular, the Linux
    # metapackage has an explicit PackageReference to Jalium.UI.Build, so it
    # must resolve the package produced at the start of this loop instead of a
    # stale developer feed or a previously published version.
    dotnet restore "$repo_root/$project" \
        --configfile "$nuget_config" \
        --packages "$nuget_packages" \
        -p:JaliumBuildRoot="$build_root" \
        -p:JaliumPlatform=linux \
        -p:JaliumNativeConfiguration="$configuration" \
        -p:JaliumNativeOutputRoot="$native_output_root" \
        -p:JaliumNativePackRid="$package_rid" \
        -p:GeneratePackageOnBuild=false

    dotnet pack "$repo_root/$project" \
        -c "$configuration" \
        --no-restore \
        -p:JaliumBuildRoot="$build_root" \
        -p:JaliumPlatform=linux \
        -p:JaliumNativeConfiguration="$configuration" \
        -p:JaliumNativeOutputRoot="$native_output_root" \
        -p:JaliumNativePackRid="$package_rid" \
        -p:GeneratePackageOnBuild=false \
        -p:PackageOutputPath="$package_output"
done

package_ids=(
    Jalium.UI.Build
    Jalium.UI.Xaml.SourceGenerator
    Jalium.UI.Managed
    Jalium.UI.Core
    Jalium.UI.Media
    Jalium.UI.Input
    Jalium.UI.Interop
    Jalium.UI.Controls
    Jalium.UI.Gpu
    Jalium.UI.Xaml
    Jalium.UI.Linux
)
package_version="$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' "$repo_root/Directory.Build.props" | head -1)"
[[ -n "$package_version" ]] || {
    echo "Could not determine the package version from Directory.Build.props." >&2
    exit 4
}
for package_id in "${package_ids[@]}"; do
    [[ -f "$package_output/$package_id.$package_version.nupkg" ]] || {
        echo "Expected package was not produced: $package_id.$package_version.nupkg" >&2
        exit 4
    }
done

echo "Packed ${#package_ids[@]} Linux consumer packages for $rid into $package_output."

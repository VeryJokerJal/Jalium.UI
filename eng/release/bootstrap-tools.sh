#!/usr/bin/env bash
set -euo pipefail
ui_root="$(cd "$(dirname "$0")/../.." && pwd)"
build_root="${JALIUM_RELEASE_BUILD_ROOT:?Set JALIUM_RELEASE_BUILD_ROOT to an isolated build directory}"
mkdir -p "$ui_root/packages"
for project in Jalium.UI.Xaml.SourceGenerator Jalium.UI.Build; do
  dotnet pack "$ui_root/src/managed/$project/$project.csproj" -c Release -m:1 \
    "-p:JaliumBuildRoot=$build_root/bootstrap/$project" \
    -p:BuildJaliumNativeProjects=false -p:BuildJaliumNativeStaticProjects=false \
    -p:GeneratePackageOnBuild=false "-p:PackageOutputPath=$ui_root/packages"
done

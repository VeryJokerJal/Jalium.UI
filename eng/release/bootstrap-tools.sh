#!/usr/bin/env bash
set -euo pipefail
ui_root="${JALIUM_FRAMEWORK_ROOT:-$(cd "$(dirname "$0")/../.." && pwd)}"
build_root="${JALIUM_RELEASE_BUILD_ROOT:?Set JALIUM_RELEASE_BUILD_ROOT to an isolated build directory}"
mkdir -p "$ui_root/packages"
# The 26.11.1 source project spells legacy Content paths as Tools while Git
# records tools. Supply the alias on case-sensitive filesystems without
# modifying the tagged project or any compiled sources.
css_tooling="$ui_root/src/managed/Jalium.UI.Css.Tooling"
if [[ ! -e "$css_tooling/Tools" && -d "$css_tooling/tools" ]]; then
  ln -s tools "$css_tooling/Tools"
fi
for project in Jalium.UI.Xaml.SourceGenerator Jalium.UI.Build; do
  dotnet pack "$ui_root/src/managed/$project/$project.csproj" -c Release -m:1 \
    "-p:JaliumBuildRoot=$build_root/bootstrap/$project" \
    -p:BuildJaliumNativeProjects=false -p:BuildJaliumNativeStaticProjects=false \
    -p:GeneratePackageOnBuild=false "-p:PackageOutputPath=$ui_root/packages"
done

#!/usr/bin/env python3
"""Pack the common 26.11.0 dependency closure from an explicit release checkout."""
import argparse
import json
import pathlib
import subprocess
import xml.etree.ElementTree as ET

VERSION = "26.11.0"
SOURCE = "a8467a8e723ba4dec090e6e141691ae91c16b639"
COMMON = ["Jalium.UI.Xaml.SourceGenerator", "Jalium.UI.Build", "Jalium.UI.Managed",
          "Jalium.UI.Core", "Jalium.UI.Media", "Jalium.UI.Input", "Jalium.UI.Interop",
          "Jalium.UI.Controls", "Jalium.UI.Gpu", "Jalium.UI.Xaml"]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=pathlib.Path)
    parser.add_argument("output", type=pathlib.Path)
    parser.add_argument("build", type=pathlib.Path)
    parser.add_argument("--entry", choices=["common", "Android", "MacOS"], default="common")
    parser.add_argument("--allow-newer-xcode", action="store_true")
    parser.add_argument("--source-commit", default=SOURCE,
                        help="Explicit revision for a supplemental packaging correction")
    args = parser.parse_args()
    source = args.source.resolve(strict=True)
    output = args.output.resolve()
    build = args.build.resolve()
    output.mkdir(parents=True, exist_ok=True)
    build.mkdir(parents=True, exist_ok=True)
    head = subprocess.check_output(["git", "-C", str(source), "rev-parse", "HEAD"], text=True).strip()
    assert head == args.source_commit, "Release packages must use the explicitly selected source revision"
    assert not subprocess.check_output(["git", "-C", str(source), "status", "--porcelain", "--untracked-files=no"], text=True).strip()
    # Separate source mappings prevent an already cached or published Jalium
    # package from replacing any dependency built during this release.
    config = ET.Element("configuration")
    sources = ET.SubElement(config, "packageSources")
    ET.SubElement(sources, "clear")
    ET.SubElement(sources, "add", key="release", value=str(output))
    ET.SubElement(sources, "add", key="nuget.org", value="https://api.nuget.org/v3/index.json")
    mapping = ET.SubElement(config, "packageSourceMapping")
    local = ET.SubElement(mapping, "packageSource", key="release")
    ET.SubElement(local, "package", pattern="Jalium.*")
    public = ET.SubElement(mapping, "packageSource", key="nuget.org")
    ET.SubElement(public, "package", pattern="*")
    config_path = build / "NuGet.release.config"
    ET.ElementTree(config).write(config_path, encoding="utf-8", xml_declaration=True)
    properties = [f"-p:JaliumBuildRoot={build}", f"-p:RestoreConfigFile={config_path}",
                  f"-p:RestorePackagesPath={build / 'nuget'}", f"-p:PackageOutputPath={output}",
                  "-p:GeneratePackageOnBuild=false", "-p:BuildJaliumNativeProjects=false",
                  "-p:BuildJaliumNativeStaticProjects=false", f"-p:RepositoryCommit={head}"]
    if args.allow_newer_xcode:
        properties.append("-p:ValidateXcodeVersion=false")
    projects = [source / "src/managed" / name / (name + ".csproj") for name in COMMON]
    if args.entry == "common":
        projects += [source / "src/packaging" / name / (name + ".csproj")
                     for name in ["Jalium.UI", "Jalium.UI.Desktop", "Jalium.UI.Linux"]]
    else:
        # Bootstrap packages are local compiler inputs only. The common release
        # job supplies the complete Build package with Windows static archives.
        projects = projects[:2] + [source / "src/packaging" / ("Jalium.UI." + args.entry) /
                                   ("Jalium.UI." + args.entry + ".csproj")]
    packed = []
    for project in projects:
        subprocess.run(["dotnet", "pack", str(project), "-c", "Release", "-m:1", *properties], check=True)
        package = output / (project.stem + "." + VERSION + ".nupkg")
        assert package.is_file(), package
        packed.append(package.name)
    (build / "pack-result.json").write_text(json.dumps({"sourceCommit": head, "packages": packed}, indent=2) + "\n")


if __name__ == "__main__":
    main()

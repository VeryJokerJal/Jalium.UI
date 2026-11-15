#!/usr/bin/env python3
"""Pack a version-consistent dependency closure from an explicit release checkout."""
import argparse
import json
import pathlib
import subprocess
import xml.etree.ElementTree as ET

COMMON = ["Jalium.UI.Xaml.SourceGenerator", "Jalium.UI.Build", "Jalium.UI.Managed",
          "Jalium.UI.Core", "Jalium.UI.Media", "Jalium.UI.Input", "Jalium.UI.Interop",
          "Jalium.UI.Controls", "Jalium.UI.Gpu", "Jalium.UI.Xaml"]


def verify_build_payload_mappings(nuspec):
    files = ET.parse(nuspec).getroot().findall(".//{*}file")
    prefix = "tasks/net10.0/tools/css-tooling/node/"
    expected = set()
    for rid in ("win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64"):
        binary = "node.exe" if rid.startswith("win-") else "node"
        expected.update(prefix + rid + "/" + binary + suffix for suffix in (".br", ".sha256"))
    nodes = [item for item in files if "tools/css-tooling/node/" in item.get("target", "").replace("\\", "/")]
    assert len(nodes) == len(expected), "CSS host runtimes were duplicated or omitted during package item batching"
    assert {item.get("target", "").replace("\\", "/") for item in nodes} == expected, "Incorrect CSS runtime package paths"
    assert len({item.get("src") for item in nodes}) == len(expected), "Duplicate CSS runtime sources"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=pathlib.Path)
    parser.add_argument("output", type=pathlib.Path)
    parser.add_argument("build", type=pathlib.Path)
    parser.add_argument("--entry", choices=["common", "Android", "MacOS"], default="common")
    parser.add_argument("--allow-newer-xcode", action="store_true")
    parser.add_argument("--source-commit", required=True,
                        help="Full source revision selected for this release")
    args = parser.parse_args()
    source = args.source.resolve(strict=True)
    version = ET.parse(source / "Directory.Build.props").getroot().findtext(".//Version")
    if not version or "-" in version or not all(part.isdigit() for part in version.split(".")):
        raise ValueError("A stable numeric release version is required")
    output = args.output.resolve()
    build = args.build.resolve()
    output.mkdir(parents=True, exist_ok=True)
    build.mkdir(parents=True, exist_ok=True)
    head = subprocess.check_output(["git", "-C", str(source), "rev-parse", "HEAD"], text=True).strip()
    assert head == args.source_commit, "Release packages must use the explicitly selected source revision"
    changed = set(filter(None, subprocess.check_output(
        ["git", "-C", str(source), "diff", "HEAD", "--name-only", "-z"], text=True).split("\0")))
    generated = set()
    if args.entry == "Android":
        # The source tree tracks historical sample payloads. The canonical
        # dual-ABI builder replaces precisely these files with fresh outputs.
        prefix = "samples/Jalium.UI.AndroidDemo/libs/"
        generated.add(prefix + ".jalium-android-payload-complete")
        for abi in ("arm64-v8a", "x86_64"):
            generated.add(prefix + abi + "/.jalium-native-complete")
            for library in ("core", "platform", "media.core", "media", "text", "vulkan", "software"):
                generated.add(prefix + abi + "/libjalium.native." + library + ".so")
            generated.add(prefix + abi + "/libc++_shared.so")
    assert not changed.difference(generated), "Tracked sources changed: " + repr(sorted(changed.difference(generated)))
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
        bootstrap = [project for project in projects[:2]
                     if not (output / f"{project.stem}.{version}.nupkg").is_file()]
        projects = bootstrap + [source / "src/packaging" / ("Jalium.UI." + args.entry) /
                                ("Jalium.UI." + args.entry + ".csproj")]
    packed = []
    for project in projects:
        if project.stem == "Jalium.UI.Build":
            # Inspect the generated map before ZIP creation. An unqualified
            # MSBuild metadata expression can multiply large host runtimes by
            # every existing Content item and otherwise create multi-GB files.
            subprocess.run(["dotnet", "pack", str(project), "-c", "Release", "-m:1", *properties,
                            "-p:ContinuePackingAfterGeneratingNuspec=false"], check=True)
            specs = list((build / "obj" / project.stem).rglob(f"{project.stem}.{version}.nuspec"))
            assert len(specs) == 1, specs
            verify_build_payload_mappings(specs[0])
        subprocess.run(["dotnet", "pack", str(project), "-c", "Release", "-m:1", *properties], check=True)
        package = output / (project.stem + "." + version + ".nupkg")
        assert package.is_file(), package
        assert package.stat().st_size < 250_000_000, (package, "NuGet.org package size limit")
        packed.append(package.name)
    (build / "pack-result.json").write_text(json.dumps({"sourceCommit": head, "version": version,
                                                       "packages": packed}, indent=2) + "\n")


if __name__ == "__main__":
    main()

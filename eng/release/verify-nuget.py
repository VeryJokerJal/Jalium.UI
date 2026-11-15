#!/usr/bin/env python3
"""Verify the stable NuGet release closure before publishing any package."""
import argparse
import hashlib
import io
import json
from pathlib import Path
import re
import subprocess
import struct
import xml.etree.ElementTree as ET
import zipfile

EXPECTED = {
    "Jalium.UI", "Jalium.UI.Desktop", "Jalium.UI.Linux", "Jalium.UI.Android", "Jalium.UI.MacOS",
    "Jalium.UI.Xaml.SourceGenerator", "Jalium.UI.Build", "Jalium.UI.Managed", "Jalium.UI.Core",
    "Jalium.UI.Media", "Jalium.UI.Input", "Jalium.UI.Interop", "Jalium.UI.Controls", "Jalium.UI.Gpu", "Jalium.UI.Xaml",
}


def verify(folder, version, commit):
    records = {}
    assert re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+", version), "Stable three-part version required"
    assert re.fullmatch(r"[0-9a-f]{40}", commit), "Full source commit required"
    for package in sorted(folder.glob("*.nupkg")):
        assert package.stat().st_size < 250_000_000, (package, "NuGet.org package size limit")
        with zipfile.ZipFile(package) as archive:
            assert archive.testzip() is None, package
            specs = [name for name in archive.namelist() if name.endswith(".nuspec")]
            assert len(specs) == 1, package
            metadata = ET.fromstring(archive.read(specs[0])).find("{*}metadata")
            identity = metadata.findtext("{*}id")
            assert identity in EXPECTED and identity not in records, (package, identity)
            assert metadata.findtext("{*}version") == version, package
            repository = metadata.find("{*}repository")
            assert repository is not None and repository.get("commit") == commit, (package, "source commit")
            for dependency in metadata.findall(".//{*}dependency"):
                if dependency.get("id", "").startswith("Jalium."):
                    assert dependency.get("id") in EXPECTED, dependency.attrib
                    assert set(re.findall(r"[0-9]+\.[0-9]+\.[0-9]+", dependency.get("version", ""))) == {version}, dependency.attrib
            names = set(archive.namelist())
            if identity == "Jalium.UI.Interop":
                for rid in ["win-x64", "linux-x64", "linux-arm64", "linux-musl-x64", "linux-musl-arm64"]:
                    core = "jalium.native.core.dll" if rid.startswith("win-") else "libjalium.native.core.so"
                    assert f"runtimes/{rid}/native/{core}" in names, (package, rid)
            elif identity == "Jalium.UI.Build":
                assert "tools/net10.0/jalxamlc.dll" in names, package
                for rid in ["win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64"]:
                    binary = "node.exe" if rid.startswith("win-") else "node"
                    path = f"tasks/net10.0/tools/css-tooling/node/{rid}/{binary}"
                    assert path not in names and path + ".br" in names and path + ".sha256" in names, (package, rid)
                    checksum = archive.read(path + ".sha256").decode().strip().lower()
                    assert re.fullmatch(r"[0-9a-f]{64}", checksum), (package, rid)
                    subprocess.run(["node", "-e", "const fs=require('node:fs'),z=require('node:zlib'),c=require('node:crypto');"
                        "const b=z.brotliDecompressSync(fs.readFileSync(0),{maxOutputLength:256*1024*1024});"
                        "if(c.createHash('sha256').update(b).digest('hex')!==process.argv[1])process.exit(1);", checksum],
                        input=archive.read(path + ".br"), check=True)
                for library in ["core", "d3d12", "vulkan", "software", "platform", "media.core", "media.windows", "aot", "browser"]:
                    assert f"build/nativeaot/win-x64/jalium.native.{library}.static.lib" in names, (package, library)
            elif identity == "Jalium.UI.MacOS":
                assemblies = [name for name in names if name.startswith("lib/") and name.endswith("/Jalium.UI.MacOS.dll")]
                assert len(assemblies) == 1, (package, "portable macOS managed assembly")
                assembly = archive.read(assemblies[0])
                pe_offset = struct.unpack_from("<I", assembly, 0x3c)[0]
                assert struct.unpack_from("<H", assembly, pe_offset + 4)[0] == 0x14c, (package, "macOS library must target AnyCPU")
                for rid in ["osx-arm64", "osx-x64"]:
                    for file in ["libjalium.native.core.dylib", "libjalium.native.platform.dylib", "jalium_core.metallib", "jalium_vello.metallib", "jalium_metal_manifest.json"]:
                        assert f"runtimes/{rid}/native/{file}" in names, (package, rid, file)
            elif identity == "Jalium.UI.Android":
                aars = [name for name in names if name.endswith(".aar")]
                assert aars, (package, "Android AAR")
                native = set()
                for aar in aars:
                    with zipfile.ZipFile(io.BytesIO(archive.read(aar))) as payload:
                        native.update(payload.namelist())
                for abi in ["arm64-v8a", "x86_64"]:
                    for library in ["core", "platform", "media.core", "media", "text", "vulkan", "software"]:
                        assert f"jni/{abi}/libjalium.native.{library}.so" in native, (package, abi, library)
            records[identity] = {"id": identity, "file": package.name, "size": package.stat().st_size,
                                 "sha256": hashlib.sha256(package.read_bytes()).hexdigest()}
    assert set(records) == EXPECTED, {"missing": sorted(EXPECTED - records.keys())}
    manifest = {"version": version, "sourceCommit": commit, "packageCount": len(records),
                "packages": list(records.values())}
    (folder / "nuget-release-manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
    return manifest


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("folder", type=Path)
    parser.add_argument("version")
    parser.add_argument("commit")
    args = parser.parse_args()
    print(json.dumps(verify(args.folder, args.version, args.commit), indent=2))

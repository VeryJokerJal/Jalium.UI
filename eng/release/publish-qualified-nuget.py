#!/usr/bin/env python3
"""Publish an immutable qualified artifact using a short-lived OIDC credential."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import xml.etree.ElementTree as ET
import zipfile

ORDER = ["Jalium.UI.Xaml.SourceGenerator", "Jalium.UI.Build", "Jalium.UI.Managed", "Jalium.UI.Core",
         "Jalium.UI.Media", "Jalium.UI.Input", "Jalium.UI.Interop", "Jalium.UI.Controls", "Jalium.UI.Gpu",
         "Jalium.UI.Xaml", "Jalium.UI", "Jalium.UI.Desktop", "Jalium.UI.Linux", "Jalium.UI.Android", "Jalium.UI.MacOS"]


def qualified_artifact(run, artifacts, version, commit):
    assert re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+", version), "Stable version required"
    assert re.fullmatch(r"[0-9a-f]{40}", commit), "Full source revision required"
    assert run["status"] == "completed" and run["conclusion"] == "success", "Qualification did not succeed"
    assert run["head_sha"] == commit, "Qualification source mismatch"
    assert run["path"] == ".github/workflows/release-nuget.yml", "Unexpected qualification workflow"
    matches = [item for item in artifacts if item["name"] == f"nuget-release-{version}"
               and not item["expired"] and item["workflow_run"]["head_sha"] == commit]
    assert len(matches) == 1, "Qualified artifact missing or ambiguous"
    return matches[0]


def check(repository, run_id, version, commit):
    assert re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", repository), "Invalid repository"
    assert run_id.isdigit(), "Invalid qualification run"
    def api(suffix):
        return json.loads(subprocess.check_output(["gh", "api", f"repos/{repository}/actions/runs/{run_id}{suffix}"], text=True))
    artifact = qualified_artifact(api(""), api("/artifacts?per_page=100")["artifacts"], version, commit)
    print(json.dumps({"artifact": artifact["name"], "artifactId": artifact["id"], "digest": artifact["digest"], "sourceCommit": commit}))
    with open(os.environ["GITHUB_OUTPUT"], "a") as output:
        output.write(f'artifact_name={artifact["name"]}\n')


def verify(folder, version, commit, original):
    manifest = json.loads((folder / "nuget-release-manifest.json").read_text())
    assert manifest == json.loads(original.read_text()), "Qualified package hashes changed"
    assert manifest["version"] == version and manifest["sourceCommit"] == commit
    assert manifest["packageCount"] == len(ORDER)
    assert {item["id"] for item in manifest["packages"]} == set(ORDER)
    for item in manifest["packages"]:
        assert item["file"] == f'{item["id"]}.{version}.nupkg'
        package = folder / item["file"]
        assert package.stat().st_size == item["size"]
        assert hashlib.sha256(package.read_bytes()).hexdigest() == item["sha256"], package
    symbols = []
    for package in sorted(folder.glob("*.snupkg")):
        with zipfile.ZipFile(package) as archive:
            assert archive.testzip() is None, package
            specs = [name for name in archive.namelist() if name.endswith(".nuspec")]
            assert len(specs) == 1, package
            metadata = ET.fromstring(archive.read(specs[0])).find("{*}metadata")
            identity = metadata.findtext("{*}id")
            assert identity in ORDER and metadata.findtext("{*}version") == version, package
            assert metadata.find("{*}repository").get("commit") == commit, package
            assert package.name == f"{identity}.{version}.snupkg", package
            assert any(name.endswith(".pdb") for name in archive.namelist()), package
        symbols.append({"id": identity, "file": package.name, "sha256": hashlib.sha256(package.read_bytes()).hexdigest()})
    (folder / "nuget-symbol-manifest.json").write_text(json.dumps(symbols, indent=2) + "\n")
    print(f"Verified {manifest['packageCount']} packages and {len(symbols)} symbol packages.")


def push(folder):
    key = os.environ["NUGET_TEMP_API_KEY"]
    assert key, "Temporary NuGet credential missing"
    manifest = json.loads((folder / "nuget-release-manifest.json").read_text())
    symbols = json.loads((folder / "nuget-symbol-manifest.json").read_text())
    records = {item["id"]: item for item in manifest["packages"]}
    assert set(records) == set(ORDER), "Unexpected package closure"
    receipt = {"version": manifest["version"], "sourceCommit": manifest["sourceCommit"], "uploads": []}
    uploads = [(records[identity], False) for identity in ORDER] + [(item, True) for item in symbols]
    for item, is_symbols in uploads:
        package = folder / item["file"]
        assert hashlib.sha256(package.read_bytes()).hexdigest() == item["sha256"], "Package changed before upload"
        source = "https://www.nuget.org/api/v2/symbolpackage" if is_symbols else "https://api.nuget.org/v3/index.json"
        command = ["dotnet", "nuget", "push", str(package), "--source", source, "--api-key", key,
                   "--skip-duplicate", "--timeout", "600"]
        if not is_symbols:
            command.append("--no-symbols")
        else:
            command.append("--no-service-endpoint")
        print(f"Publishing {item['file']}", flush=True)
        result = subprocess.run(command, check=False)
        receipt["uploads"].append({"id": item["id"], "file": item["file"], "symbols": is_symbols,
                                   "exitCode": result.returncode, "at": datetime.now(timezone.utc).isoformat()})
        (folder / "nuget-publish-results.json").write_text(json.dumps(receipt, indent=2) + "\n")
        if result.returncode:
            raise RuntimeError(f"NuGet rejected {item['file']} (exit {result.returncode})")
    print(f"Submitted {len(records)} packages and {len(symbols)} symbol packages.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    check_parser = commands.add_parser("check")
    for name in ("repository", "run_id", "version", "commit"):
        check_parser.add_argument(name)
    verify_parser = commands.add_parser("verify")
    verify_parser.add_argument("folder", type=Path)
    verify_parser.add_argument("version")
    verify_parser.add_argument("commit")
    verify_parser.add_argument("original", type=Path)
    push_parser = commands.add_parser("push")
    push_parser.add_argument("folder", type=Path)
    args = vars(parser.parse_args())
    command = args.pop("command")
    globals()[command](**args)


if __name__ == "__main__":
    main()

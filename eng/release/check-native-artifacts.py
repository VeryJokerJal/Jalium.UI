#!/usr/bin/env python3
"""Reuse a successful native build only when all of its source inputs are identical."""
import argparse
import json
import subprocess

NAMES = {"native-windows", "native-linux-x64", "native-linux-arm64", "native-linux-musl-x64",
         "native-linux-musl-arm64", "native-osx-arm64", "native-osx-x64"}
INPUTS = ["src/native", "eng/apple", "eng/linux", "eng/native", "eng/windows",
          "samples/build-native-static.ps1", "Directory.Build.props", "Directory.Build.targets", "global.json"]


def api(repository, suffix):
    return json.loads(subprocess.check_output(["gh", "api", f"repos/{repository}/actions/{suffix}"], text=True))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("repository")
    parser.add_argument("run", type=int)
    parser.add_argument("source_commit")
    args = parser.parse_args()
    head = subprocess.check_output(["git", "rev-parse", "HEAD"], text=True).strip()
    assert head == args.source_commit, "Unexpected release checkout"
    run = api(args.repository, f"runs/{args.run}")
    assert run["status"] == "completed", "Native source run is still running"
    native_commit = run["head_sha"]
    contracts = set()
    for revision in (native_commit, head):
        matches = subprocess.check_output(["git", "grep", "-Il", "-e", "DllImport", "-e", "LibraryImport",
                                            revision, "--", "src/managed", "src/packaging"], text=True)
        contracts.update(line.split(":", 1)[1] for line in matches.splitlines())
    subprocess.run(["git", "diff", "--exit-code", native_commit, head, "--", *INPUTS, *sorted(contracts)], check=True)
    jobs = api(args.repository, f"runs/{args.run}/jobs?filter=latest&per_page=100")["jobs"]
    native_jobs = [job for job in jobs if job["name"] == "windows-native"
                   or job["name"].startswith(("linux-native (", "macos-native ("))]
    assert len(native_jobs) == 7 and all(job["conclusion"] == "success" for job in native_jobs), "Native jobs did not all pass"
    artifacts = api(args.repository, f"runs/{args.run}/artifacts?per_page=100")["artifacts"]
    selected = [item for item in artifacts if item["name"] in NAMES and not item["expired"]
                and item["workflow_run"]["head_sha"] == native_commit]
    assert {item["name"] for item in selected} == NAMES, "Missing or mismatched native artifacts"
    print(json.dumps({"sourceCommit": head, "nativeSourceCommit": native_commit,
                      "nativeSourceRun": args.run, "identicalInputs": INPUTS + sorted(contracts),
                      "artifacts": [{"name": item["name"], "digest": item["digest"]} for item in selected]}, indent=2))


if __name__ == "__main__":
    main()

#!/usr/bin/env python3
"""Run Window theme/null-resource contracts against the head and an unchanged parent."""
from pathlib import Path
import argparse
import hashlib
import json
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET


FIXTURE = Path("tests/Jalium.UI.Tests/WindowThemeRefreshTests.cs")
MAC_PROJECT = Path("tests/Jalium.UI.MacOS.Tests/Jalium.UI.MacOS.Tests.csproj")


def source_hashes(repo):
    return {str(path.relative_to(repo)): hashlib.sha256(path.read_bytes()).hexdigest()
            for path in (repo / "src").rglob("*") if path.is_file()
            and not any(part in ("bin", "obj", "out", "artifacts") for part in path.relative_to(repo / "src").parts)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--work-parent", type=Path, required=True)
    parser.add_argument("--baseline-ref", default="a730ad4f2609aee637594719e542a70aa348e727")
    parser.add_argument("--baseline-path", type=Path, help="use an already frozen parent instead of creating a CI worktree")
    args = parser.parse_args()
    repo = args.repo.resolve()
    args.work_parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="jalium-window-theme-", dir=args.work_parent) as directory:
        work = Path(directory)
        baseline = args.baseline_path.resolve() if args.baseline_path else work / "parent"
        registered = False
        try:
            if not args.baseline_path:
                subprocess.run(["git", "worktree", "add", "--detach", str(baseline), args.baseline_ref], cwd=repo, check=True)
                registered = True
            before = source_hashes(baseline)
            # Only tests enter the parent. No current product/helper source is
            # backfilled to make a negative control compile or pass.
            shutil.copyfile(repo / FIXTURE, baseline / FIXTURE)
            project = MAC_PROJECT if sys.platform == "darwin" else Path("tests/Jalium.UI.Tests/Jalium.UI.Tests.csproj")
            if sys.platform == "darwin":
                path = baseline / MAC_PROJECT
                source = path.read_text(encoding="utf-8")
                if FIXTURE.name not in source:
                    source = source.replace("</Project>", '  <ItemGroup><Compile Include="../Jalium.UI.Tests/WindowThemeRefreshTests.cs" Link="WindowSystem/WindowThemeRefreshTests.cs" /></ItemGroup>\n</Project>')
                    path.write_text(source, encoding="utf-8")
            receipts = []
            for label, target, expected_code, passed, failed in (("parent", baseline, 1, 9, 21), ("head", repo, 0, 30, 0)):
                results = work / (label + "-results")
                results.mkdir()
                command = ["dotnet", "test", str(target / project), "-c", "Debug",
                           "-p:JaliumBuildRoot=" + str(work / (label + "-build")), "-p:JaliumMacNativeGeometryTests=false",
                           "--filter", "FullyQualifiedName~WindowThemeRefreshTests", "--logger", "trx;LogFileName=result.trx",
                           "--results-directory", str(results), "-p:UseSharedCompilation=false", "-m:1",
                           "--disable-build-servers"]
                run = subprocess.run(command, cwd=target)
                if run.returncode != expected_code:
                    raise RuntimeError(label + " returned " + str(run.returncode) + "; compilation/host errors are not a negative control")
                path = results / "result.trx"
                if not path.is_file():
                    raise RuntimeError(label + " did not produce completed test results")
                tree = ET.parse(path)
                counts = tree.find(".//{*}Counters").attrib
                failures = [item for item in tree.findall(".//{*}UnitTestResult") if item.get("outcome") == "Failed"]
                if label == "parent" and any(not item.findtext("{*}Output/{*}ErrorInfo/{*}Message", default="").startswith("Assert.")
                                             for item in failures):
                    raise RuntimeError("parent failures must be assertions, not environment or product-loading errors")
                expected = {"total": 30, "executed": 30, "passed": passed, "failed": failed,
                            "error": 0, "timeout": 0, "aborted": 0, "notExecuted": 0}
                if any(int(counts[key]) != value for key, value in expected.items()):
                    raise RuntimeError(label + " counts differ: " + json.dumps(counts))
                receipts.append({"variant": label, "exitCode": run.returncode, "counts": counts})
            if before != source_hashes(baseline):
                raise RuntimeError("parent production sources changed")
            print(json.dumps({"contracts": receipts, "parentSourceFilesUnchanged": len(before), "physicalInput": False}), flush=True)
        finally:
            if registered:
                subprocess.run(["git", "worktree", "remove", "--force", str(baseline)], cwd=repo, check=True)


if __name__ == "__main__":
    main()
